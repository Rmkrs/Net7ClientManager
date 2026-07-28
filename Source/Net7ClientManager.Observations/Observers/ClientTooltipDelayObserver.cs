namespace Net7ClientManager.Observations.Observers;

using System.Globalization;
using System.Text;
using Net7ClientManager.Observations.Models;

internal sealed class ClientTooltipDelayObserver
{
    /*
     * Live option path proven in Ghidra and validated in Memory Probe:
     *
     *   client.exe +0x7E8B38 -> OptionsManager*
     *   OptionsManager +0x30 -> embedded float-option hash map
     *   matching node +0x14  -> FloatOption*
     *   FloatOption +0x10    -> current live value
     *   FloatOption +0x14    -> initial/default value
     *
     * The exact FloatOption pointer is resolved once per gameplay session.
     * Normal feature refreshes read only the two adjacent floats. Returning
     * to character selection invalidates the cache through ClientSessionTask
     * and SClient identity, even when the old allocation remains readable.
     */
    private const uint ImageBase = 0x00400000;
    private const uint OptionsManagerGlobalRva =
        0x00be8b38 - ImageBase;
    private const uint OptionsManagerFloatMapOffset = 0x30;

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
    private const int ValuePairLength = 0x08;

    private const int MaximumBucketCount = 65_536;
    private const int MaximumEntryCount = 4_096;
    private const int MaximumChainLength = 4_096;
    private const int MaximumOptionNameLength = 256;

    private const float MinimumPercent = 0.0f;
    private const float MaximumPercent = 100.0f;
    private const float NativeBaseDelayMilliseconds = 1200.0f;
    private const float NativePercentScale = 0.01f;

    private const string TooltipDelayOptionName =
        "Interface_Tooltip_Delay";

    private readonly System.Threading.Lock cacheLock = new();
    private readonly Dictionary<int, ProcessCache> processCaches = [];

    public void Refresh(
        ProcessMemoryReader memory,
        ObservedClientState state)
    {
        if (state.ModuleBaseAddress == 0 ||
            state.ClientSessionTaskAddress == 0 ||
            state.ClientContextAddress == 0)
        {
            this.Forget(state.ProcessId);
            state.TooltipDelay =
                ClientTooltipDelayObservation.Unavailable(
                    "No active gameplay session");
            return;
        }

        uint optionsManagerGlobalAddress;

        try
        {
            optionsManagerGlobalAddress = checked(
                state.ModuleBaseAddress +
                OptionsManagerGlobalRva);
        }
        catch (OverflowException)
        {
            this.Forget(state.ProcessId);
            state.TooltipDelay =
                ClientTooltipDelayObservation.Unavailable(
                    "OptionsManager global address overflow");
            return;
        }

        if (!memory.TryReadUInt32(
                optionsManagerGlobalAddress,
                out var optionsManagerAddress) ||
            optionsManagerAddress == 0)
        {
            this.Forget(state.ProcessId);
            state.TooltipDelay =
                ClientTooltipDelayObservation.Unavailable(
                    "OptionsManager is unavailable");
            return;
        }

        var cached = this.GetCache(state.ProcessId);

        if (cached != null &&
            cached.ModuleBaseAddress == state.ModuleBaseAddress &&
            cached.ClientSessionTaskAddress ==
                state.ClientSessionTaskAddress &&
            cached.ClientContextAddress ==
                state.ClientContextAddress &&
            cached.OptionsManagerAddress == optionsManagerAddress &&
            TryReadValues(
                memory,
                cached.FloatOptionAddress,
                out var cachedCurrentPercent,
                out var cachedDefaultPercent))
        {
            state.TooltipDelay = BuildObservation(
                cached,
                cachedCurrentPercent,
                cachedDefaultPercent,
                usedCachedResolution: true);
            return;
        }

        this.Forget(state.ProcessId);

        if (!TryResolveOption(
                memory,
                state.ModuleBaseAddress,
                state.ClientSessionTaskAddress,
                state.ClientContextAddress,
                optionsManagerAddress,
                out var resolved,
                out var currentPercent,
                out var defaultPercent,
                out var error))
        {
            state.TooltipDelay =
                ClientTooltipDelayObservation.Unavailable(error);
            return;
        }

        this.SetCache(
            state.ProcessId,
            resolved);

        state.TooltipDelay = BuildObservation(
            resolved,
            currentPercent,
            defaultPercent,
            usedCachedResolution: false);
    }

    public void Forget(int processId)
    {
        lock (this.cacheLock)
        {
            _ = this.processCaches.Remove(processId);
        }
    }

    private static bool TryResolveOption(
        ProcessMemoryReader memory,
        uint moduleBaseAddress,
        uint clientSessionTaskAddress,
        uint clientContextAddress,
        uint optionsManagerAddress,
        out ProcessCache resolved,
        out float currentPercent,
        out float defaultPercent,
        out string error)
    {
        resolved = default!;
        currentPercent = 0;
        defaultPercent = 0;
        error = "Tooltip-delay option could not be resolved";

        uint floatMapAddress;

        try
        {
            floatMapAddress = checked(
                optionsManagerAddress +
                OptionsManagerFloatMapOffset);
        }
        catch (OverflowException)
        {
            error = "Float-option map address overflow";
            return false;
        }

        if (!TryReadMapHeader(
                memory,
                floatMapAddress,
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
                $"Could not read {bucketCount} float-option bucket head(s)");
            return false;
        }

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
                        $"Float-option bucket {bucketIndex} exceeded its chain limit");
                    return false;
                }

                if (!visitedNodes.Add(nodeAddress))
                {
                    error = "Float-option map contains a repeated node";
                    return false;
                }

                if (++scannedNodeCount > MaximumEntryCount)
                {
                    error = "Float-option map exceeded its node limit";
                    return false;
                }

                if (!memory.TryReadBytes(
                        nodeAddress,
                        NodeLength,
                        out var nodeBytes))
                {
                    error = "Could not read a float-option map node";
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
                    error = "Could not read a float-option name";
                    return false;
                }

                if (string.Equals(
                        key,
                        TooltipDelayOptionName,
                        StringComparison.Ordinal))
                {
                    var floatOptionAddress = BitConverter.ToUInt32(
                        nodeBytes,
                        checked((int)NodeValueOffset));

                    if (floatOptionAddress == 0 ||
                        !TryReadValues(
                            memory,
                            floatOptionAddress,
                            out currentPercent,
                            out defaultPercent))
                    {
                        error = "Tooltip-delay FloatOption is unavailable";
                        return false;
                    }

                    resolved = new ProcessCache(
                        moduleBaseAddress,
                        clientSessionTaskAddress,
                        clientContextAddress,
                        optionsManagerAddress,
                        floatOptionAddress);
                    return true;
                }

                nodeAddress = nextNodeAddress;
            }
        }

        error = string.Create(
            CultureInfo.InvariantCulture,
            $"'{TooltipDelayOptionName}' was not found in {scannedNodeCount} float-option node(s)");
        return false;
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
            error = "Could not read the float-option map header";
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
            error = "Float-option map header is inconsistent";
            return false;
        }

        bucketCount = checked(
            (int)((bucketEnd - bucketBegin) /
                  sizeof(uint)));

        if (bucketCount <= 0 ||
            bucketCount > MaximumBucketCount ||
            entryCount > MaximumEntryCount)
        {
            error = "Float-option map dimensions are outside expected bounds";
            return false;
        }

        return true;
    }

    private static bool TryReadValues(
        ProcessMemoryReader memory,
        uint floatOptionAddress,
        out float currentPercent,
        out float defaultPercent)
    {
        currentPercent = 0;
        defaultPercent = 0;

        uint valueAddress;

        try
        {
            valueAddress = checked(
                floatOptionAddress +
                CurrentValueOffset);
        }
        catch (OverflowException)
        {
            return false;
        }

        if (!memory.TryReadBytes(
                valueAddress,
                ValuePairLength,
                out var bytes))
        {
            return false;
        }

        currentPercent = BitConverter.ToSingle(bytes, 0);
        defaultPercent = BitConverter.ToSingle(bytes, sizeof(float));

        return IsValidPercent(currentPercent) &&
               IsValidPercent(defaultPercent);
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

    private static bool IsValidPercent(float value)
    {
        return float.IsFinite(value) &&
               value >= MinimumPercent &&
               value <= MaximumPercent;
    }

    private static ClientTooltipDelayObservation BuildObservation(
        ProcessCache cache,
        float currentPercent,
        float defaultPercent,
        bool usedCachedResolution)
    {
        var delayMilliseconds = checked(
            (int)Math.Truncate(
                currentPercent *
                NativeBaseDelayMilliseconds *
                NativePercentScale));

        return new ClientTooltipDelayObservation
        {
            IsAvailable = true,
            Status = usedCachedResolution
                ? "Available; cached gameplay-session option refreshed"
                : "Available; option resolved for active gameplay session",
            CurrentPercent = currentPercent,
            DefaultPercent = defaultPercent,
            DelayMilliseconds = delayMilliseconds,
            FloatOptionAddress = cache.FloatOptionAddress,
            UsedCachedResolution = usedCachedResolution,
        };
    }

    private ProcessCache? GetCache(int processId)
    {
        lock (this.cacheLock)
        {
            return this.processCaches.TryGetValue(
                processId,
                out var cache)
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
        uint FloatOptionAddress);
}
