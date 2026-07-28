namespace Net7ClientManager.Observations.Observers;

using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Net7ClientManager.Observations.Models;

internal sealed class ClientNavigationObserver
{
    /*
     * NavigationDataClass is attached to ClientGameObject instances through
     * the process-global object-metadata binder. The observer follows the
     * exact native roots and container layouts. It never scans process memory.
     *
     * Durable addon identity is ActiveSectorNumber + ObjectId. Native map,
     * node, metadata, and ClientGameObject addresses are diagnostics only.
     */
    private const uint ImageBase = 0x00400000;

    private const uint NavigationDataClassVTableStatic =
        0x00b065c8;

    private const uint NavigationDataClassVTableRva =
        NavigationDataClassVTableStatic - ImageBase;

    private const uint ClientGameObjectVTableStatic =
        0x00b0070c;

    private const uint ClientGameObjectVTableRva =
        ClientGameObjectVTableStatic - ImageBase;

    private const uint NavigationDataClassTypeDescriptorStatic =
        0x00b9e7f0;

    private const uint NavigationDataClassTypeDescriptorRva =
        NavigationDataClassTypeDescriptorStatic - ImageBase;

    private const uint ObjectMetadataBinderRegistryInitFlagsStatic =
        0x00be86d8;

    private const uint ObjectMetadataBinderRegistryInitFlagsRva =
        ObjectMetadataBinderRegistryInitFlagsStatic - ImageBase;

    private const uint ObjectMetadataBinderRegistryStatic =
        0x00be86e0;

    private const uint ObjectMetadataBinderRegistryRva =
        ObjectMetadataBinderRegistryStatic - ImageBase;

    private const uint RuntimeTypeDescriptorCachedName = 0x04;

    private const uint HashMapBucketBegin = 0x08;
    private const uint HashMapBucketEnd = 0x0c;
    private const uint HashMapBucketCapacityEnd = 0x10;
    private const uint HashMapEntryCount = 0x14;
    private const int HashMapHeaderSnapshotLength = 0x18;

    private const uint StringMapNodeNext = 0x00;
    private const uint StringMapNodeKeyPointer = 0x08;
    private const uint StringMapNodeKeyLength = 0x0c;
    private const uint StringMapNodeValue = 0x14;
    private const int StringMapNodeSnapshotLength = 0x18;

    private const uint UInt32PointerMapNodeNext = 0x00;
    private const uint UInt32PointerMapNodeKey = 0x04;
    private const uint UInt32PointerMapNodeValue = 0x08;
    private const int UInt32PointerMapNodeSnapshotLength = 0x0c;

    private const uint NavigationDataVTable = 0x00;
    private const uint NavigationDataClientObject = 0x04;
    private const uint NavigationDataSignature = 0x08;
    private const uint NavigationDataPlayerHasVisited = 0x0c;
    private const uint NavigationDataNavType = 0x10;
    private const uint NavigationDataIsHuge = 0x14;
    private const int NavigationDataSnapshotLength = 0x18;

    private const uint ClientObjectAuxData = 0x88;
    private const uint ClientObjectObjectId = 0x90;
    private const uint ClientObjectType = 0x94;
    private const int ClientObjectSnapshotLength = 0x95;

    private const int MaximumRuntimeTypeNameLength = 256;
    private const int MaximumMapBucketCount = 65536;
    private const int MaximumMapEntryCount = 16384;
    private const int MaximumMapChainLength = 4096;
    private const int MaximumStableReadAttempts = 3;
    private const int MaximumRefreshAttempts = 2;

    private readonly System.Threading.Lock cacheLock = new();

    private readonly Dictionary<int, ProcessCache> processCaches = [];

    private readonly ClientMapIdentityReader mapIdentityReader =
        new();

    private readonly ClientSpatialObserver spatialObserver =
        new();

    public void Refresh(
        ProcessMemoryReader memory,
        ObservedClientState state)
    {
        if (!state.HasDirectClientState ||
            state.ModuleBaseAddress == 0 ||
            state.ClientContextAddress == 0 ||
            !state.World.IsAvailable)
        {
            this.Forget(state.ProcessId);

            SetObservation(
                state,
                ClientNavigationObservation.Unavailable(
                    "Direct SClient world state is unavailable",
                    state.World.ActiveSectorNumber));

            return;
        }

        if (state.World.Environment ==
            ClientWorldEnvironment.Transitioning)
        {
            this.Forget(state.ProcessId);

            SetObservation(
                state,
                ClientNavigationObservation.Unavailable(
                    "Navigation data is intentionally suspended during world transition",
                    state.World.ActiveSectorNumber));

            return;
        }

        var cache = this.GetOrCreateCache(state);
        var lastError = "Navigation data could not be read";

        for (var attempt = 0;
             attempt < MaximumRefreshAttempts;
             attempt++)
        {
            var binding = cache.Binding;

            if (binding == null ||
                !this.TryValidateBinding(
                    memory,
                    state.ModuleBaseAddress,
                    binding,
                    out _))
            {
                cache.Binding = null;
                cache.Identities.Clear();

                if (!this.TryDiscoverBinding(
                        memory,
                        state.ModuleBaseAddress,
                        out var discoveredBinding,
                        out lastError))
                {
                    break;
                }

                binding = discoveredBinding;
                cache.Binding = discoveredBinding;
            }

            if (!this.TryReadStableNavigationEntries(
                    memory,
                    binding.NavigationDataMapAddress,
                    out var mapHeader,
                    out var entries,
                    out lastError))
            {
                cache.Binding = null;
                continue;
            }

            var separator =
                this.mapIdentityReader.ReadMapDisplayNameSeparator(
                    memory,
                    state.ModuleBaseAddress);

            if (!this.TryObserveTargets(
                    memory,
                    state,
                    cache,
                    separator,
                    entries,
                    out var targets,
                    out lastError))
            {
                continue;
            }

            var liveClientObjects = entries
                .Select(
                    entry =>
                        entry.ClientObjectAddress)
                .ToHashSet();

            foreach (var staleAddress
                     in cache.Identities.Keys
                         .Where(
                             address =>
                                 !liveClientObjects.Contains(
                                     address))
                         .ToArray())
            {
                cache.Identities.Remove(
                    staleAddress);
            }

            var visitedCount = targets.Count(
                target =>
                    target.PlayerHasVisited);

            var undiscoveredCount =
                targets.Count - visitedCount;

            var routeCandidateCount = targets.Count(
                target =>
                    target.NavType == 1);

            var hugeCount = targets.Count(
                target =>
                    target.IsHuge);

            SetObservation(
                state,
                new ClientNavigationObservation
                {
                    IsAvailable = true,
                    Status = string.Create(
                        CultureInfo.InvariantCulture,
                        $"Available; {targets.Count} navigation point(s), {visitedCount} visited, {undiscoveredCount} undiscovered, {routeCandidateCount} route candidate(s), {hugeCount} huge"),
                    ActiveSectorNumber =
                        state.World.ActiveSectorNumber,
                    BinderRegistryAddress =
                        binding.BinderRegistryAddress,
                    BinderEntryAddress =
                        binding.BinderEntryAddress,
                    NavigationDataMapAddress =
                        binding.NavigationDataMapAddress,
                    RuntimeTypeNameAddress =
                        binding.RuntimeTypeNameAddress,
                    RuntimeTypeName =
                        binding.RuntimeTypeName,
                    BucketCount =
                        mapHeader.BucketCount,
                    EntryCount =
                        targets.Count,
                    Targets = targets,
                });

            return;
        }

        SetObservation(
            state,
            ClientNavigationObservation.Unavailable(
                lastError,
                state.World.ActiveSectorNumber));
    }

    public void Forget(
        int processId)
    {
        lock (this.cacheLock)
        {
            this.processCaches.Remove(
                processId);
        }
    }

    private ProcessCache GetOrCreateCache(
        ObservedClientState state)
    {
        lock (this.cacheLock)
        {
            if (this.processCaches.TryGetValue(
                    state.ProcessId,
                    out var existing) &&
                existing.ModuleBaseAddress ==
                    state.ModuleBaseAddress &&
                existing.ClientContextAddress ==
                    state.ClientContextAddress &&
                existing.ActiveSectorNumber ==
                    state.World.ActiveSectorNumber)
            {
                return existing;
            }

            var created = new ProcessCache(
                state.ModuleBaseAddress,
                state.ClientContextAddress,
                state.World.ActiveSectorNumber);

            this.processCaches[state.ProcessId] =
                created;

            return created;
        }
    }

    private bool TryDiscoverBinding(
        ProcessMemoryReader memory,
        uint moduleBaseAddress,
        out NavigationBinding binding,
        out string error)
    {
        binding = null!;
        error = "";

        try
        {
            var initFlagsAddress = checked(
                moduleBaseAddress +
                ObjectMetadataBinderRegistryInitFlagsRva);

            if (!TryReadByte(
                    memory,
                    initFlagsAddress,
                    out var initFlags))
            {
                error = $"Could not read object-metadata binder initialization flags at 0x{initFlagsAddress:X8}";

                return false;
            }

            if ((initFlags & 1) == 0)
            {
                error =
                    "Object-metadata binder registry is not initialized";

                return false;
            }

            var typeDescriptorAddress = checked(
                moduleBaseAddress +
                NavigationDataClassTypeDescriptorRva);

            var cachedNamePointerAddress = checked(
                typeDescriptorAddress +
                RuntimeTypeDescriptorCachedName);

            if (!memory.TryReadUInt32(
                    cachedNamePointerAddress,
                    out var runtimeTypeNameAddress) ||
                runtimeTypeNameAddress == 0)
            {
                error = $"NavigationDataClass runtime type name is not cached at 0x{cachedNamePointerAddress:X8}";

                return false;
            }

            if (!memory.TryReadNullTerminatedLatin1String(
                    runtimeTypeNameAddress,
                    MaximumRuntimeTypeNameLength,
                    out var runtimeTypeName) ||
                string.IsNullOrWhiteSpace(
                    runtimeTypeName))
            {
                error = $"Could not read NavigationDataClass runtime type name at 0x{runtimeTypeNameAddress:X8}";

                return false;
            }

            var binderRegistryAddress = checked(
                moduleBaseAddress +
                ObjectMetadataBinderRegistryRva);

            if (!this.TryFindStringMapEntry(
                    memory,
                    binderRegistryAddress,
                    runtimeTypeName,
                    out var binderEntryAddress,
                    out var navigationDataMapAddress,
                    out error))
            {
                return false;
            }

            if (navigationDataMapAddress == 0)
            {
                error = $"NavigationDataClass binder entry at 0x{binderEntryAddress:X8} contains a null value map";

                return false;
            }

            binding = new NavigationBinding(
                binderRegistryAddress,
                binderEntryAddress,
                navigationDataMapAddress,
                runtimeTypeNameAddress,
                runtimeTypeName);

            return true;
        }
        catch (OverflowException)
        {
            error =
                "Navigation binder address calculation overflow";

            return false;
        }
    }

    private bool TryValidateBinding(
        ProcessMemoryReader memory,
        uint moduleBaseAddress,
        NavigationBinding binding,
        out string error)
    {
        error = "";

        try
        {
            var typeDescriptorAddress = checked(
                moduleBaseAddress +
                NavigationDataClassTypeDescriptorRva);

            if (!memory.TryReadUInt32(
                    checked(
                        typeDescriptorAddress +
                        RuntimeTypeDescriptorCachedName),
                    out var runtimeTypeNameAddress) ||
                runtimeTypeNameAddress !=
                    binding.RuntimeTypeNameAddress)
            {
                error =
                    "NavigationDataClass runtime type-name cache changed";

                return false;
            }

            if (!memory.TryReadBytes(
                    binding.BinderEntryAddress,
                    StringMapNodeSnapshotLength,
                    out var nodeBytes))
            {
                error = $"Could not read cached NavigationDataClass binder entry at 0x{binding.BinderEntryAddress:X8}";

                return false;
            }

            var keyPointer = BitConverter.ToUInt32(
                nodeBytes,
                checked((int)StringMapNodeKeyPointer));

            var keyLength = BitConverter.ToUInt32(
                nodeBytes,
                checked((int)StringMapNodeKeyLength));

            var valueMapAddress = BitConverter.ToUInt32(
                nodeBytes,
                checked((int)StringMapNodeValue));

            if (valueMapAddress !=
                binding.NavigationDataMapAddress)
            {
                error =
                    "NavigationDataClass value-map pointer changed";

                return false;
            }

            if (!TryReadExactLatin1String(
                    memory,
                    keyPointer,
                    keyLength,
                    MaximumRuntimeTypeNameLength,
                    out var observedKey,
                    out error) ||
                !string.Equals(
                    observedKey,
                    binding.RuntimeTypeName,
                    StringComparison.Ordinal))
            {
                if (string.IsNullOrEmpty(error))
                {
                    error =
                        "Cached binder entry no longer has the NavigationDataClass key";
                }

                return false;
            }

            return true;
        }
        catch (OverflowException)
        {
            error =
                "Navigation binding validation address overflow";

            return false;
        }
    }

    private bool TryFindStringMapEntry(
        ProcessMemoryReader memory,
        uint mapAddress,
        string expectedKey,
        out uint entryAddress,
        out uint valueAddress,
        out string error)
    {
        entryAddress = 0;
        valueAddress = 0;
        error = "";

        for (var attempt = 0;
             attempt < MaximumStableReadAttempts;
             attempt++)
        {
            if (!TryReadMapHeader(
                    memory,
                    mapAddress,
                    out var before,
                    out error))
            {
                return false;
            }

            if (!TryReadBucketHeads(
                    memory,
                    before,
                    out var bucketHeads,
                    out error))
            {
                return false;
            }

            var foundEntryAddress = 0u;
            var foundValueAddress = 0u;
            HashSet<uint> visited = [];

            foreach (var bucketHead
                     in bucketHeads)
            {
                var nodeAddress = bucketHead;

                for (var chainIndex = 0;
                     nodeAddress != 0 &&
                     chainIndex < MaximumMapChainLength;
                     chainIndex++)
                {
                    if (!visited.Add(nodeAddress))
                    {
                        error = $"String-map chains contain a duplicate or cycle at 0x{nodeAddress:X8}";

                        return false;
                    }

                    if (!memory.TryReadBytes(
                            nodeAddress,
                            StringMapNodeSnapshotLength,
                            out var nodeBytes))
                    {
                        error = $"Could not read string-map node at 0x{nodeAddress:X8}";

                        return false;
                    }

                    var nextAddress = BitConverter.ToUInt32(
                        nodeBytes,
                        checked((int)StringMapNodeNext));

                    var keyPointer = BitConverter.ToUInt32(
                        nodeBytes,
                        checked((int)StringMapNodeKeyPointer));

                    var keyLength = BitConverter.ToUInt32(
                        nodeBytes,
                        checked((int)StringMapNodeKeyLength));

                    if (keyLength ==
                        (uint)Encoding.Latin1.GetByteCount(
                            expectedKey) &&
                        TryReadExactLatin1String(
                            memory,
                            keyPointer,
                            keyLength,
                            MaximumRuntimeTypeNameLength,
                            out var candidateKey,
                            out _) &&
                        string.Equals(
                            candidateKey,
                            expectedKey,
                            StringComparison.Ordinal))
                    {
                        if (foundEntryAddress != 0)
                        {
                            error = $"Object-metadata binder contains duplicate {expectedKey} entries";

                            return false;
                        }

                        foundEntryAddress = nodeAddress;
                        foundValueAddress = BitConverter.ToUInt32(
                            nodeBytes,
                            checked((int)StringMapNodeValue));
                    }

                    nodeAddress = nextAddress;
                }

                if (nodeAddress != 0)
                {
                    error = string.Create(
                        CultureInfo.InvariantCulture,
                        $"String-map chain exceeded {MaximumMapChainLength} nodes");

                    return false;
                }
            }

            if (!TryReadMapHeader(
                    memory,
                    mapAddress,
                    out var after,
                    out error))
            {
                return false;
            }

            if (before != after)
            {
                continue;
            }

            if (visited.Count != before.EntryCount)
            {
                continue;
            }

            if (foundEntryAddress == 0)
            {
                error = $"Object-metadata binder has no {expectedKey} entry";

                return false;
            }

            entryAddress = foundEntryAddress;
            valueAddress = foundValueAddress;
            return true;
        }

        error =
            "Object-metadata binder changed while it was being read";

        return false;
    }

    private bool TryReadStableNavigationEntries(
        ProcessMemoryReader memory,
        uint mapAddress,
        out HashMapHeader stableHeader,
        out IReadOnlyList<NavigationMapEntry> entries,
        out string error)
    {
        stableHeader = default;
        entries = [];
        error = "";

        for (var attempt = 0;
             attempt < MaximumStableReadAttempts;
             attempt++)
        {
            if (!TryReadMapHeader(
                    memory,
                    mapAddress,
                    out var before,
                    out error))
            {
                return false;
            }

            if (!TryReadBucketHeads(
                    memory,
                    before,
                    out var bucketHeads,
                    out error))
            {
                return false;
            }

            List<NavigationMapEntry> observedEntries = [];
            HashSet<uint> visited = [];

            foreach (var bucketHead
                     in bucketHeads)
            {
                var nodeAddress = bucketHead;

                for (var chainIndex = 0;
                     nodeAddress != 0 &&
                     chainIndex < MaximumMapChainLength;
                     chainIndex++)
                {
                    if (!visited.Add(nodeAddress))
                    {
                        error = $"NavigationDataClass map contains a duplicate or cycle at 0x{nodeAddress:X8}";

                        return false;
                    }

                    if (!memory.TryReadBytes(
                            nodeAddress,
                            UInt32PointerMapNodeSnapshotLength,
                            out var nodeBytes))
                    {
                        error = $"Could not read NavigationDataClass map node at 0x{nodeAddress:X8}";

                        return false;
                    }

                    var nextAddress = BitConverter.ToUInt32(
                        nodeBytes,
                        checked((int)UInt32PointerMapNodeNext));

                    var clientObjectAddress = BitConverter.ToUInt32(
                        nodeBytes,
                        checked((int)UInt32PointerMapNodeKey));

                    var navigationDataAddress = BitConverter.ToUInt32(
                        nodeBytes,
                        checked((int)UInt32PointerMapNodeValue));

                    if (clientObjectAddress == 0 ||
                        navigationDataAddress == 0)
                    {
                        error = $"NavigationDataClass map node at 0x{nodeAddress:X8} contains a null key or value";

                        return false;
                    }

                    observedEntries.Add(
                        new NavigationMapEntry(
                            nodeAddress,
                            clientObjectAddress,
                            navigationDataAddress));

                    if (observedEntries.Count >
                        MaximumMapEntryCount)
                    {
                        error = string.Create(
                            CultureInfo.InvariantCulture,
                            $"NavigationDataClass map exceeded {MaximumMapEntryCount} entries");

                        return false;
                    }

                    nodeAddress = nextAddress;
                }

                if (nodeAddress != 0)
                {
                    error = string.Create(
                        CultureInfo.InvariantCulture,
                        $"NavigationDataClass map chain exceeded {MaximumMapChainLength} nodes");

                    return false;
                }
            }

            if (!TryReadMapHeader(
                    memory,
                    mapAddress,
                    out var after,
                    out error))
            {
                return false;
            }

            if (before != after ||
                observedEntries.Count != before.EntryCount)
            {
                continue;
            }

            stableHeader = before;
            entries = [.. observedEntries
                .OrderBy(
                    entry =>
                        entry.ClientObjectAddress)];

            return true;
        }

        error =
            "NavigationDataClass map changed while it was being read";

        return false;
    }

    private bool TryObserveTargets(
        ProcessMemoryReader memory,
        ObservedClientState state,
        ProcessCache cache,
        string separator,
        IReadOnlyList<NavigationMapEntry> entries,
        out IReadOnlyList<ClientNavigationTargetObservation> targets,
        out string error)
    {
        targets = [];
        error = "";

        uint expectedNavigationDataVTable;
        uint expectedClientObjectVTable;

        try
        {
            expectedNavigationDataVTable = checked(
                state.ModuleBaseAddress +
                NavigationDataClassVTableRva);

            expectedClientObjectVTable = checked(
                state.ModuleBaseAddress +
                ClientGameObjectVTableRva);
        }
        catch (OverflowException)
        {
            error =
                "Navigation target vtable address overflow";

            return false;
        }

        List<ClientNavigationTargetObservation> observed = [];

        foreach (var entry in entries)
        {
            if (!memory.TryReadBytes(
                    entry.NavigationDataAddress,
                    NavigationDataSnapshotLength,
                    out var navigationBytes))
            {
                error = $"Could not read NavigationDataClass at 0x{entry.NavigationDataAddress:X8}";

                return false;
            }

            var navigationVTable = BitConverter.ToUInt32(
                navigationBytes,
                checked((int)NavigationDataVTable));

            var boundClientObject = BitConverter.ToUInt32(
                navigationBytes,
                checked((int)NavigationDataClientObject));

            if (navigationVTable !=
                    expectedNavigationDataVTable ||
                boundClientObject !=
                    entry.ClientObjectAddress)
            {
                error = $"NavigationDataClass at 0x{entry.NavigationDataAddress:X8} failed structural validation";

                return false;
            }

            var signature = BitConverter.ToSingle(
                navigationBytes,
                checked((int)NavigationDataSignature));

            if (!float.IsFinite(signature))
            {
                error = string.Create(
                    CultureInfo.InvariantCulture,
                    $"NavigationDataClass at 0x{entry.NavigationDataAddress:X8} contains non-finite Signature {signature}");

                return false;
            }

            var playerHasVisited =
                navigationBytes[
                    checked((int)NavigationDataPlayerHasVisited)] != 0;

            var navType = BitConverter.ToInt32(
                navigationBytes,
                checked((int)NavigationDataNavType));

            var isHuge =
                navigationBytes[
                    checked((int)NavigationDataIsHuge)] != 0;

            // NavigationDataClass is inserted into the binder immediately
            // before NavigationDataClassApplyPacket fills it. NavType -1 is
            // the constructor default, so do not publish that tiny transient.
            if (navType == -1)
            {
                error = $"NavigationDataClass at 0x{entry.NavigationDataAddress:X8} is not packet-initialized yet";

                return false;
            }

            if (!memory.TryReadBytes(
                    entry.ClientObjectAddress,
                    ClientObjectSnapshotLength,
                    out var clientObjectBytes))
            {
                error = $"Could not read bound ClientGameObject at 0x{entry.ClientObjectAddress:X8}";

                return false;
            }

            var clientObjectVTable = BitConverter.ToUInt32(
                clientObjectBytes,
                0);

            var auxDataAddress = BitConverter.ToUInt32(
                clientObjectBytes,
                checked((int)ClientObjectAuxData));

            var objectId = BitConverter.ToUInt32(
                clientObjectBytes,
                checked((int)ClientObjectObjectId));

            var rawObjectType =
                clientObjectBytes[
                    checked((int)ClientObjectType)];

            if (clientObjectVTable !=
                    expectedClientObjectVTable ||
                objectId is 0 or uint.MaxValue)
            {
                error = $"Bound ClientGameObject at 0x{entry.ClientObjectAddress:X8} failed structural validation";

                return false;
            }

            ClientMapIdentityReadResult identity;

            if (cache.Identities.TryGetValue(
                    entry.ClientObjectAddress,
                    out var cachedIdentity) &&
                cachedIdentity.ObjectId == objectId &&
                cachedIdentity.AuxDataAddress ==
                    auxDataAddress &&
                cachedIdentity.RawObjectType ==
                    rawObjectType &&
                string.Equals(
                    cachedIdentity.Separator,
                    separator,
                    StringComparison.Ordinal))
            {
                identity = cachedIdentity.Identity;
            }
            else
            {
                identity = this.mapIdentityReader.Read(
                    memory,
                    state.ModuleBaseAddress,
                    auxDataAddress,
                    rawObjectType,
                    separator);

                cache.Identities[
                    entry.ClientObjectAddress] =
                    new CachedIdentity(
                        objectId,
                        auxDataAddress,
                        rawObjectType,
                        separator,
                        identity);
            }

            var spatial = this.spatialObserver.Observe(
                memory,
                state.ModuleBaseAddress,
                state.CurrentClientTime,
                entry.ClientObjectAddress);

            List<string> statusParts = [];

            if (!string.Equals(
                    identity.Status,
                    "Available",
                    StringComparison.Ordinal))
            {
                statusParts.Add(
                    $"name: {identity.Status}");
            }

            if (!spatial.IsAvailable)
            {
                statusParts.Add(
                    $"spatial: {spatial.Status}");
            }

            observed.Add(
                new ClientNavigationTargetObservation
                {
                    IsAvailable = true,
                    Status = statusParts.Count == 0
                        ? "Available"
                        : string.Join("; ", statusParts),
                    ActiveSectorNumber =
                        state.World.ActiveSectorNumber,
                    ObjectId = objectId,
                    ClientObjectAddress =
                        entry.ClientObjectAddress,
                    NavigationMapNodeAddress =
                        entry.NodeAddress,
                    NavigationDataAddress =
                        entry.NavigationDataAddress,
                    AuxDataAddress = auxDataAddress,
                    RawObjectType = rawObjectType,
                    Name = identity.Name,
                    Owner = identity.Owner,
                    Title = identity.Title,
                    Rank = identity.Rank,
                    MapDisplayName =
                        identity.MapDisplayName,
                    MapDisplayNameSource =
                        identity.MapDisplayNameSource,
                    NameStatus = identity.Status,
                    Signature = signature,
                    PlayerHasVisited =
                        playerHasVisited,
                    NavType = navType,
                    IsHuge = isHuge,
                    Spatial = spatial,
                });
        }

        targets = [.. observed
            .OrderBy(
                target =>
                    target.ObjectId)];

        return true;
    }

    private static bool TryReadMapHeader(
        ProcessMemoryReader memory,
        uint mapAddress,
        out HashMapHeader header,
        out string error)
    {
        header = default;
        error = "";

        if (!memory.TryReadBytes(
                mapAddress,
                HashMapHeaderSnapshotLength,
                out var bytes))
        {
            error = $"Could not read hash-map header at 0x{mapAddress:X8}";

            return false;
        }

        var bucketBegin = BitConverter.ToUInt32(
            bytes,
            checked((int)HashMapBucketBegin));

        var bucketEnd = BitConverter.ToUInt32(
            bytes,
            checked((int)HashMapBucketEnd));

        var bucketCapacityEnd = BitConverter.ToUInt32(
            bytes,
            checked((int)HashMapBucketCapacityEnd));

        var entryCountRaw = BitConverter.ToUInt32(
            bytes,
            checked((int)HashMapEntryCount));

        if (bucketBegin == 0 ||
            bucketEnd < bucketBegin ||
            bucketCapacityEnd < bucketEnd)
        {
            error = $"Hash-map header at 0x{mapAddress:X8} is inconsistent: begin=0x{bucketBegin:X8}, end=0x{bucketEnd:X8}, capacity=0x{bucketCapacityEnd:X8}";

            return false;
        }

        var activeLength =
            bucketEnd - bucketBegin;

        if (activeLength % sizeof(uint) != 0)
        {
            error = $"Hash-map bucket storage at 0x{mapAddress:X8} is not DWORD-aligned";

            return false;
        }

        var bucketCount = checked(
            (int)(activeLength / sizeof(uint)));

        if (bucketCount is <= 0 or > MaximumMapBucketCount)
        {
            error = string.Create(
                CultureInfo.InvariantCulture,
                $"Hash-map at 0x{mapAddress:X8} has unexpected bucket count {bucketCount}");

            return false;
        }

        if (entryCountRaw >
            MaximumMapEntryCount)
        {
            error = $"Hash-map at 0x{mapAddress:X8} has unexpected entry count {entryCountRaw}";

            return false;
        }

        header = new HashMapHeader(
            bucketBegin,
            bucketEnd,
            bucketCapacityEnd,
            checked((int)entryCountRaw),
            bucketCount);

        return true;
    }

    private static bool TryReadBucketHeads(
        ProcessMemoryReader memory,
        HashMapHeader header,
        out IReadOnlyList<uint> bucketHeads,
        out string error)
    {
        bucketHeads = [];
        error = "";

        if (!memory.TryReadBytes(
                header.BucketBegin,
                checked(
                    header.BucketCount *
                    sizeof(uint)),
                out var bytes))
        {
            error = string.Create(
                CultureInfo.InvariantCulture,
                $"Could not read {header.BucketCount} hash-map bucket head(s) at 0x{header.BucketBegin:X8}");

            return false;
        }

        var observed = new uint[
            header.BucketCount];

        for (var index = 0;
             index < observed.Length;
             index++)
        {
            observed[index] = BitConverter.ToUInt32(
                bytes,
                checked(
                    index *
                    sizeof(uint)));
        }

        bucketHeads = observed;
        return true;
    }

    private static bool TryReadExactLatin1String(
        ProcessMemoryReader memory,
        uint address,
        uint length,
        int maximumLength,
        out string value,
        out string error)
    {
        value = "";
        error = "";

        if (address == 0 ||
            maximumLength < 0 ||
            length > checked((uint)maximumLength))
        {
            error = $"String reference is inconsistent: address=0x{address:X8}, length={length}";

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
            error = $"Could not read {length}-byte string at 0x{address:X8}";

            return false;
        }

        value = Encoding.Latin1.GetString(bytes);
        return true;
    }

    private static bool TryReadByte(
        ProcessMemoryReader memory,
        uint address,
        out byte value)
    {
        value = 0;

        if (!memory.TryReadBytes(
                address,
                sizeof(byte),
                out var bytes))
        {
            return false;
        }

        value = bytes[0];
        return true;
    }

    private static void SetObservation(
        ObservedClientState state,
        ClientNavigationObservation observation)
    {
        state.Navigation = observation;
    }

    private sealed class ProcessCache(
        uint moduleBaseAddress,
        uint clientContextAddress,
        uint activeSectorNumber)
    {
        public uint ModuleBaseAddress { get; } =
            moduleBaseAddress;

        public uint ClientContextAddress { get; } =
            clientContextAddress;

        public uint ActiveSectorNumber { get; } =
            activeSectorNumber;

        public NavigationBinding? Binding { get; set; }

        public Dictionary<uint, CachedIdentity> Identities
        {
            get;
        } = [];
    }

    private sealed record NavigationBinding(
        uint BinderRegistryAddress,
        uint BinderEntryAddress,
        uint NavigationDataMapAddress,
        uint RuntimeTypeNameAddress,
        string RuntimeTypeName);

    private sealed record CachedIdentity(
        uint ObjectId,
        uint AuxDataAddress,
        byte RawObjectType,
        string Separator,
        ClientMapIdentityReadResult Identity);

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct NavigationMapEntry(
        uint NodeAddress,
        uint ClientObjectAddress,
        uint NavigationDataAddress);

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct HashMapHeader(
        uint BucketBegin,
        uint BucketEnd,
        uint BucketCapacityEnd,
        int EntryCount,
        int BucketCount);
}
