namespace Net7ClientManager.Observations.Observers;

using System.Globalization;
using System.Text;
using Net7ClientManager.Observations.Models;

internal sealed class ClientChatChannelOptionsReader
{
    /*
     * Live option path proven from ConstructOptionsManager:
     *
     *   client.exe +0x7E8B38 -> OptionsManager*
     *   OptionsManager +0x00 -> embedded boolean-option hash map
     *   matching node +0x14  -> BooleanOption*
     *   BooleanOption +0x10  -> current live value
     *   BooleanOption +0x11  -> initial/default value
     *
     * Only Channel_* options are retained. Resolved BooleanOption addresses
     * are cached for the active gameplay session; ordinary reads then touch
     * one byte per channel and reflect changes made in Set Channels To Monitor.
     */
    private const uint ImageBase = 0x00400000;
    private const uint OptionsManagerGlobalRva =
        0x00be8b38 - ImageBase;
    private const uint OptionsManagerBooleanMapOffset = 0x00;

    private const uint HashMapBucketBeginOffset = 0x08;
    private const uint HashMapBucketEndOffset = 0x0c;
    private const uint HashMapEntryCountOffset = 0x14;
    private const int HashMapHeaderLength = 0x18;

    private const uint NodeNextOffset = 0x00;
    private const uint NodeKeyPointerOffset = 0x08;
    private const uint NodeKeyLengthOffset = 0x0c;
    private const uint NodeValueOffset = 0x14;
    private const int NodeLength = 0x18;

    private const uint CurrentValueOffset = 0x10;

    private const int MaximumBucketCount = 65_536;
    private const int MaximumEntryCount = 4_096;
    private const int MaximumChainLength = 4_096;
    private const int MaximumOptionNameLength = 256;

    private readonly System.Threading.Lock cacheLock = new();
    private readonly Dictionary<int, ProcessCache> processCaches = [];

    public ClientChatChannelOptionsState Read(
        ProcessMemoryReader memory,
        ObservedClientState state)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(state);

        if (state.ModuleBaseAddress == 0 ||
            state.ClientSessionTaskAddress == 0 ||
            state.ClientContextAddress == 0)
        {
            this.Forget(state.ProcessId);
            return ClientChatChannelOptionsState.Unavailable(
                "No active gameplay session");
        }

        uint optionsManagerGlobalAddress;

        try
        {
            optionsManagerGlobalAddress = checked(
                state.ModuleBaseAddress + OptionsManagerGlobalRva);
        }
        catch (OverflowException)
        {
            this.Forget(state.ProcessId);
            return ClientChatChannelOptionsState.Unavailable(
                "OptionsManager global address overflow");
        }

        if (!memory.TryReadUInt32(
                optionsManagerGlobalAddress,
                out var optionsManagerAddress) ||
            optionsManagerAddress == 0)
        {
            this.Forget(state.ProcessId);
            return ClientChatChannelOptionsState.Unavailable(
                "OptionsManager is unavailable");
        }

        var cached = this.GetCache(state.ProcessId);

        if (cached != null &&
            cached.ModuleBaseAddress == state.ModuleBaseAddress &&
            cached.ClientSessionTaskAddress == state.ClientSessionTaskAddress &&
            cached.ClientContextAddress == state.ClientContextAddress &&
            cached.OptionsManagerAddress == optionsManagerAddress &&
            TryReadCurrentValues(
                memory,
                cached.OptionAddresses,
                out var cachedValues))
        {
            return new ClientChatChannelOptionsState(
                true,
                cachedValues,
                "Available; cached live channel options refreshed");
        }

        this.Forget(state.ProcessId);

        if (!TryResolveOptions(
                memory,
                state.ModuleBaseAddress,
                state.ClientSessionTaskAddress,
                state.ClientContextAddress,
                optionsManagerAddress,
                out var resolved,
                out var values,
                out var error))
        {
            return ClientChatChannelOptionsState.Unavailable(error);
        }

        this.SetCache(state.ProcessId, resolved);

        return new ClientChatChannelOptionsState(
            true,
            values,
            string.Create(
                CultureInfo.InvariantCulture,
                $"Available; resolved {values.Count} live channel option(s)"));
    }

    public void Forget(int processId)
    {
        lock (this.cacheLock)
        {
            _ = this.processCaches.Remove(processId);
        }
    }

    private static bool TryResolveOptions(
        ProcessMemoryReader memory,
        uint moduleBaseAddress,
        uint clientSessionTaskAddress,
        uint clientContextAddress,
        uint optionsManagerAddress,
        out ProcessCache resolved,
        out IReadOnlyDictionary<string, bool> values,
        out string error)
    {
        resolved = default!;
        values = new Dictionary<string, bool>(
            StringComparer.OrdinalIgnoreCase);
        error = "Live chat-channel options could not be resolved";

        uint booleanMapAddress;

        try
        {
            booleanMapAddress = checked(
                optionsManagerAddress + OptionsManagerBooleanMapOffset);
        }
        catch (OverflowException)
        {
            error = "Boolean-option map address overflow";
            return false;
        }

        if (!TryReadMapHeader(
                memory,
                booleanMapAddress,
                out var bucketBegin,
                out var bucketCount,
                out error))
        {
            return false;
        }

        if (!memory.TryReadBytes(
                bucketBegin,
                checked(bucketCount * sizeof(uint)),
                out var bucketBytes))
        {
            error = string.Create(
                CultureInfo.InvariantCulture,
                $"Could not read {bucketCount} boolean-option bucket head(s)");
            return false;
        }

        Dictionary<string, uint> optionAddresses =
            new(StringComparer.OrdinalIgnoreCase);
        HashSet<uint> visitedNodes = [];
        var scannedNodeCount = 0;

        for (var bucketIndex = 0;
             bucketIndex < bucketCount;
             bucketIndex++)
        {
            var nodeAddress = BitConverter.ToUInt32(
                bucketBytes,
                checked(bucketIndex * sizeof(uint)));
            var chainLength = 0;

            while (nodeAddress != 0)
            {
                if (++chainLength > MaximumChainLength)
                {
                    error = string.Create(
                        CultureInfo.InvariantCulture,
                        $"Boolean-option bucket {bucketIndex} exceeded its chain limit");
                    return false;
                }

                if (!visitedNodes.Add(nodeAddress))
                {
                    error = "Boolean-option map contains a repeated node";
                    return false;
                }

                if (++scannedNodeCount > MaximumEntryCount)
                {
                    error = "Boolean-option map exceeded its node limit";
                    return false;
                }

                if (!memory.TryReadBytes(
                        nodeAddress,
                        NodeLength,
                        out var nodeBytes))
                {
                    error = "Could not read a boolean-option map node";
                    return false;
                }

                var nextNodeAddress = BitConverter.ToUInt32(
                    nodeBytes,
                    checked((int)NodeNextOffset));
                var keyAddress = BitConverter.ToUInt32(
                    nodeBytes,
                    checked((int)NodeKeyPointerOffset));
                var keyLength = BitConverter.ToUInt32(
                    nodeBytes,
                    checked((int)NodeKeyLengthOffset));

                if (!TryReadExactLatin1String(
                        memory,
                        keyAddress,
                        keyLength,
                        out var key))
                {
                    error = "Could not read a boolean-option name";
                    return false;
                }

                if (key.StartsWith(
                        "Channel_",
                        StringComparison.OrdinalIgnoreCase))
                {
                    var booleanOptionAddress = BitConverter.ToUInt32(
                        nodeBytes,
                        checked((int)NodeValueOffset));

                    if (booleanOptionAddress == 0)
                    {
                        error = string.Concat(
                            "Boolean option '",
                            key,
                            "' is unavailable");
                        return false;
                    }

                    optionAddresses[key] = booleanOptionAddress;
                }

                nodeAddress = nextNodeAddress;
            }
        }

        if (optionAddresses.Count == 0)
        {
            error = string.Create(
                CultureInfo.InvariantCulture,
                $"No Channel_* options were found in {scannedNodeCount} boolean-option node(s)");
            return false;
        }

        if (!TryReadCurrentValues(
                memory,
                optionAddresses,
                out values))
        {
            error = "One or more live channel-option values could not be read";
            return false;
        }

        resolved = new ProcessCache(
            moduleBaseAddress,
            clientSessionTaskAddress,
            clientContextAddress,
            optionsManagerAddress,
            optionAddresses);
        return true;
    }

    private static bool TryReadCurrentValues(
        ProcessMemoryReader memory,
        IReadOnlyDictionary<string, uint> optionAddresses,
        out IReadOnlyDictionary<string, bool> values)
    {
        Dictionary<string, bool> result =
            new(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in optionAddresses)
        {
            uint valueAddress;

            try
            {
                valueAddress = checked(pair.Value + CurrentValueOffset);
            }
            catch (OverflowException)
            {
                values = result;
                return false;
            }

            if (!memory.TryReadBytes(
                    valueAddress,
                    1,
                    out var bytes) ||
                bytes[0] > 1)
            {
                values = result;
                return false;
            }

            result[pair.Key] = bytes[0] != 0;
        }

        values = result;
        return true;
    }

    private static bool TryReadMapHeader(
        ProcessMemoryReader memory,
        uint mapAddress,
        out uint bucketBegin,
        out int bucketCount,
        out string error)
    {
        bucketBegin = 0;
        bucketCount = 0;
        error = "";

        if (!memory.TryReadBytes(
                mapAddress,
                HashMapHeaderLength,
                out var bytes))
        {
            error = "Could not read the boolean-option map header";
            return false;
        }

        bucketBegin = BitConverter.ToUInt32(
            bytes,
            checked((int)HashMapBucketBeginOffset));
        var bucketEnd = BitConverter.ToUInt32(
            bytes,
            checked((int)HashMapBucketEndOffset));
        var entryCount = BitConverter.ToUInt32(
            bytes,
            checked((int)HashMapEntryCountOffset));

        if (bucketBegin == 0 ||
            bucketEnd < bucketBegin ||
            (bucketEnd - bucketBegin) % sizeof(uint) != 0)
        {
            error = "Boolean-option map header is inconsistent";
            return false;
        }

        bucketCount = checked(
            (int)((bucketEnd - bucketBegin) / sizeof(uint)));

        if (bucketCount <= 0 ||
            bucketCount > MaximumBucketCount ||
            entryCount > MaximumEntryCount)
        {
            error = "Boolean-option map dimensions are outside expected bounds";
            return false;
        }

        return true;
    }

    private static bool TryReadExactLatin1String(
        ProcessMemoryReader memory,
        uint address,
        uint length,
        out string value)
    {
        value = "";

        if (length > MaximumOptionNameLength ||
            (length > 0 && address == 0))
        {
            return false;
        }

        if (length == 0)
        {
            return true;
        }

        if (!memory.TryReadBytes(
                address,
                checked((int)length),
                out var bytes))
        {
            return false;
        }

        value = Encoding.Latin1.GetString(bytes);
        return true;
    }

    private ProcessCache? GetCache(int processId)
    {
        lock (this.cacheLock)
        {
            return this.processCaches.TryGetValue(processId, out var cache)
                ? cache
                : null;
        }
    }

    private void SetCache(
        int processId,
        ProcessCache cache)
    {
        lock (this.cacheLock)
        {
            this.processCaches[processId] = cache;
        }
    }

    private sealed record ProcessCache(
        uint ModuleBaseAddress,
        uint ClientSessionTaskAddress,
        uint ClientContextAddress,
        uint OptionsManagerAddress,
        IReadOnlyDictionary<string, uint> OptionAddresses);
}
