// ReSharper disable GrammarMistakeInComment
namespace Net7ClientManager.Observations.Observers;

using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Net7ClientManager.Observations.Models;

internal sealed class ClientGutterRadarObserver
{
    /*
     * Stable roots:
     *   SClient +0x1354 -> MainView
     *   MainView +0x80  -> RadarSystem
     *   RadarSystem +0x08 -> hovered ClientGameObject
     *   RadarSystem +0x48 -> active-presentation count reported by the client
     *
     * GutterRadarObjectPresentation instances are attached to ClientGameObject
     * instances through the process-global object-metadata binder. The exact
     * binder key is read from the client-owned runtime type-name cache at the
     * presentation RTTI descriptor. The observer follows that registry and
     * value map directly. It never scans process memory.
     *
     * Durable identity is ActiveSectorNumber + ObjectId. Native map, node,
     * presentation, render-object, and ClientGameObject addresses are exposed
     * only as laboratory diagnostics.
     */
    private const uint ImageBase = 0x00400000;

    private const uint GutterRadarObjectPresentationVTableStatic =
        0x00b06068;

    private const uint GutterRadarObjectPresentationVTableRva =
        GutterRadarObjectPresentationVTableStatic - ImageBase;

    private const uint GutterRadarObjectPresentationTypeDescriptorStatic =
        0x00b9e7a0;

    private const uint GutterRadarObjectPresentationTypeDescriptorRva =
        GutterRadarObjectPresentationTypeDescriptorStatic - ImageBase;

    private const uint ClientGameObjectVTableStatic =
        0x00b0070c;

    private const uint ClientGameObjectVTableRva =
        ClientGameObjectVTableStatic - ImageBase;

    private const uint ObjectMetadataBinderRegistryInitFlagsStatic =
        0x00be86d8;

    private const uint ObjectMetadataBinderRegistryInitFlagsRva =
        ObjectMetadataBinderRegistryInitFlagsStatic - ImageBase;

    private const uint ObjectMetadataBinderRegistryStatic =
        0x00be86e0;

    private const uint ObjectMetadataBinderRegistryRva =
        ObjectMetadataBinderRegistryStatic - ImageBase;

    private const uint RuntimeTypeDescriptorCachedName = 0x04;

    private const uint ClientContextMainView = 0x1354;
    private const uint MainViewRadarSystem = 0x80;

    private const uint RadarSystemClientContext = 0x00;
    private const uint RadarSystemHoveredClientObject = 0x08;
    private const uint RadarSystemActivePresentationCount = 0x48;

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

    private const uint PresentationVTable = 0x00;
    private const uint PresentationClientObject = 0x04;
    private const uint PresentationNormalizedX = 0x08;
    private const uint PresentationNormalizedY = 0x0c;
    private const uint PresentationIconRenderObject = 0x10;
    private const uint PresentationIconSubObject = 0x14;
    private const uint PresentationIconPartId = 0x18;
    private const uint PresentationUnknown1C = 0x1c;
    private const uint PresentationUnknown20 = 0x20;
    private const uint PresentationIsInsideViewport = 0x24;
    private const uint PresentationGutterIndicator = 0x28;
    private const int PresentationSnapshotLength = 0x2c;

    private const uint ClientObjectAuxData = 0x88;
    private const uint ClientObjectObjectId = 0x90;
    private const uint ClientObjectType = 0x94;
    private const uint ClientObjectRelationshipRaw = 0x12c;
    private const uint ClientObjectAggressionRaw = 0x130;
    private const int ClientObjectSnapshotLength = 0x134;

    private const int MaximumRuntimeTypeNameLength = 256;
    private const int MaximumMapBucketCount = 65536;
    private const int MaximumMapEntryCount = 16384;
    private const int MaximumMapChainLength = 4096;
    private const int MaximumStableReadAttempts = 3;
    private const int MaximumRefreshAttempts = 2;

    private static readonly TimeSpan MobObservationRefreshInterval =
        TimeSpan.FromSeconds(5);

    private const float MinimumNormalizedCoordinate = -0.01f;
    private const float MaximumNormalizedCoordinate = 1.01f;

    private readonly System.Threading.Lock cacheLock = new();

    private readonly Dictionary<int, ProcessCache> processCaches = [];

    private readonly ClientMapIdentityReader mapIdentityReader =
        new();

    private readonly ClientNearbyTargetVitalsReader vitalsReader =
        new();

    private readonly ClientNearbyMobObserver mobObserver =
        new();

    private readonly ClientSpatialObserver spatialObserver =
        new();

    private readonly ClientCorpseObserver corpseObserver =
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
                ClientGutterRadarObservation.Unavailable(
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
                ClientGutterRadarObservation.Unavailable(
                    "Gutter radar observation is intentionally suspended during world transition",
                    state.World.ActiveSectorNumber));

            return;
        }

        var cache = this.GetOrCreateCache(state);
        var lastError = "Gutter radar data could not be read";

        for (var attempt = 0;
             attempt < MaximumRefreshAttempts;
             attempt++)
        {
            if (!this.TryReadRadarRoot(
                    memory,
                    state,
                    out var radarRoot,
                    out lastError))
            {
                continue;
            }

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
                cache.Vitals.Clear();
                cache.Mobs.Clear();

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

            if (!this.TryReadStablePresentationEntries(
                    memory,
                    binding.PresentationMapAddress,
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
                    radarRoot,
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

            foreach (var staleAddress
                     in cache.Vitals.Keys
                         .Where(
                             address =>
                                 !liveClientObjects.Contains(
                                     address))
                         .ToArray())
            {
                cache.Vitals.Remove(
                    staleAddress);
            }

            foreach (var staleAddress
                     in cache.Mobs.Keys
                         .Where(
                             address =>
                                 !liveClientObjects.Contains(
                                     address))
                         .ToArray())
            {
                cache.Mobs.Remove(
                    staleAddress);
            }

            var hoveredObjectId = targets
                .FirstOrDefault(
                    target =>
                        target.ClientObjectAddress ==
                            radarRoot.HoveredClientObjectAddress)
                ?.ObjectId;

            if (!hoveredObjectId.HasValue &&
                radarRoot.HoveredClientObjectAddress != 0)
            {
                _ = this.TryReadClientObjectIdentity(
                    memory,
                    state.ModuleBaseAddress,
                    radarRoot.HoveredClientObjectAddress,
                    out hoveredObjectId,
                    out _);
            }

            var activeCountMatchesBinderEntryCount =
                radarRoot.ActivePresentationCount ==
                    targets.Count;

            var hoverText = hoveredObjectId.HasValue
                ? $"hovered ObjectId {hoveredObjectId.Value}" : radarRoot.HoveredClientObjectAddress == 0
                    ? "no hovered object"
                    : $"unresolved hovered object at 0x{radarRoot.HoveredClientObjectAddress:X8}";

            SetObservation(
                state,
                new ClientGutterRadarObservation
                {
                    IsAvailable = true,
                    Status = string.Create(
                        CultureInfo.InvariantCulture,
                        $"Available; {targets.Count} gutter presentation(s), radar reports {radarRoot.ActivePresentationCount} active, {hoverText}"),
                    ActiveSectorNumber =
                        state.World.ActiveSectorNumber,
                    MainViewAddress =
                        radarRoot.MainViewAddress,
                    RadarSystemAddress =
                        radarRoot.RadarSystemAddress,
                    HoveredClientObjectAddress =
                        radarRoot.HoveredClientObjectAddress,
                    HoveredObjectId =
                        hoveredObjectId,
                    RadarReportedActiveCount =
                        radarRoot.ActivePresentationCount,
                    ActiveCountMatchesBinderEntryCount =
                        activeCountMatchesBinderEntryCount,
                    BinderRegistryAddress =
                        binding.BinderRegistryAddress,
                    BinderEntryAddress =
                        binding.BinderEntryAddress,
                    RuntimeTypeNameAddress =
                        binding.RuntimeTypeNameAddress,
                    RuntimeTypeName =
                        binding.RuntimeTypeName,
                    BinderKeyAddress =
                        binding.BinderKeyAddress,
                    PresentationMapAddress =
                        binding.PresentationMapAddress,
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
            ClientGutterRadarObservation.Unavailable(
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

    private bool TryReadRadarRoot(
        ProcessMemoryReader memory,
        ObservedClientState state,
        out RadarRoot root,
        out string error)
    {
        root = default;
        error = "";

        if (!TryReadPointer(
                memory,
                state.ClientContextAddress,
                ClientContextMainView,
                "SClient.MainView",
                out var mainViewAddress,
                out error))
        {
            return false;
        }

        if (mainViewAddress == 0)
        {
            error = "SClient MainView is null";
            return false;
        }

        if (!TryReadPointer(
                memory,
                mainViewAddress,
                MainViewRadarSystem,
                "MainView.RadarSystem",
                out var radarSystemAddress,
                out error))
        {
            return false;
        }

        if (radarSystemAddress == 0)
        {
            error = "MainView RadarSystem is null";
            return false;
        }

        if (!TryReadPointer(
                memory,
                radarSystemAddress,
                RadarSystemClientContext,
                "RadarSystem.Client",
                out var radarClientContextAddress,
                out error))
        {
            return false;
        }

        if (radarClientContextAddress !=
            state.ClientContextAddress)
        {
            error = $"RadarSystem at 0x{radarSystemAddress:X8} has unexpected SClient backlink 0x{radarClientContextAddress:X8}; expected 0x{state.ClientContextAddress:X8}";

            return false;
        }

        if (!TryReadPointer(
                memory,
                radarSystemAddress,
                RadarSystemHoveredClientObject,
                "RadarSystem.HoveredClientObject",
                out var hoveredClientObjectAddress,
                out error) ||
            !TryReadUInt32(
                memory,
                radarSystemAddress,
                RadarSystemActivePresentationCount,
                "RadarSystem.ActivePresentationCount",
                out var activePresentationCountRaw,
                out error))
        {
            return false;
        }

        if (activePresentationCountRaw >
            MaximumMapEntryCount)
        {
            error = $"RadarSystem reports unexpected active-presentation count {activePresentationCountRaw}";

            return false;
        }

        root = new RadarRoot(
            mainViewAddress,
            radarSystemAddress,
            hoveredClientObjectAddress,
            checked((int)activePresentationCountRaw));

        return true;
    }

    private bool TryDiscoverBinding(
        ProcessMemoryReader memory,
        uint moduleBaseAddress,
        out GutterRadarBinding binding,
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
                GutterRadarObjectPresentationTypeDescriptorRva);

            var cachedNamePointerAddress = checked(
                typeDescriptorAddress +
                RuntimeTypeDescriptorCachedName);

            if (!memory.TryReadUInt32(
                    cachedNamePointerAddress,
                    out var runtimeTypeNameAddress) ||
                runtimeTypeNameAddress == 0)
            {
                error = $"Gutter radar presentation runtime type name is not cached at 0x{cachedNamePointerAddress:X8}";

                return false;
            }

            if (!memory.TryReadNullTerminatedLatin1String(
                    runtimeTypeNameAddress,
                    MaximumRuntimeTypeNameLength,
                    out var runtimeTypeName) ||
                string.IsNullOrWhiteSpace(runtimeTypeName))
            {
                error = $"Could not read gutter radar presentation runtime type name at 0x{runtimeTypeNameAddress:X8}";

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
                    out var binderKeyAddress,
                    out var presentationMapAddress,
                    out error))
            {
                return false;
            }

            if (presentationMapAddress == 0)
            {
                error = $"Gutter radar presentation binder entry at 0x{binderEntryAddress:X8} contains a null value map";

                return false;
            }

            binding = new GutterRadarBinding(
                binderRegistryAddress,
                binderEntryAddress,
                binderKeyAddress,
                presentationMapAddress,
                runtimeTypeNameAddress,
                runtimeTypeName);

            return true;
        }
        catch (OverflowException)
        {
            error =
                "Gutter radar binder address calculation overflow";

            return false;
        }
    }

    private bool TryValidateBinding(
        ProcessMemoryReader memory,
        uint moduleBaseAddress,
        GutterRadarBinding binding,
        out string error)
    {
        error = "";

        try
        {
            var typeDescriptorAddress = checked(
                moduleBaseAddress +
                GutterRadarObjectPresentationTypeDescriptorRva);

            if (!memory.TryReadUInt32(
                    checked(
                        typeDescriptorAddress +
                        RuntimeTypeDescriptorCachedName),
                    out var runtimeTypeNameAddress) ||
                runtimeTypeNameAddress !=
                    binding.RuntimeTypeNameAddress)
            {
                error =
                    "Gutter radar presentation runtime type-name cache changed";

                return false;
            }

            if (!memory.TryReadBytes(
                    binding.BinderEntryAddress,
                    StringMapNodeSnapshotLength,
                    out var nodeBytes))
            {
                error = $"Could not read cached gutter radar presentation binder entry at 0x{binding.BinderEntryAddress:X8}";

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

            if (keyPointer !=
                    binding.BinderKeyAddress ||
                valueMapAddress !=
                    binding.PresentationMapAddress)
            {
                error =
                    "Cached gutter radar presentation binder entry changed";

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
                        "Cached binder entry no longer has the gutter radar presentation runtime type key";
                }

                return false;
            }

            return true;
        }
        catch (OverflowException)
        {
            error =
                "Gutter radar binding validation address overflow";

            return false;
        }
    }

    private bool TryFindStringMapEntry(
        ProcessMemoryReader memory,
        uint mapAddress,
        string expectedKey,
        out uint entryAddress,
        out uint keyAddress,
        out uint valueAddress,
        out string error)
    {
        entryAddress = 0;
        keyAddress = 0;
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
                    out error) ||
                !TryReadBucketHeads(
                    memory,
                    before,
                    out var bucketHeads,
                    out error))
            {
                return false;
            }

            var foundEntryAddress = 0u;
            var foundKeyAddress = 0u;
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

                    var candidateKeyAddress = BitConverter.ToUInt32(
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
                            candidateKeyAddress,
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
                        foundKeyAddress = candidateKeyAddress;
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

            if (before != after ||
                visited.Count != before.EntryCount)
            {
                continue;
            }

            if (foundEntryAddress == 0)
            {
                error = $"Object-metadata binder has no {expectedKey} entry";

                return false;
            }

            entryAddress = foundEntryAddress;
            keyAddress = foundKeyAddress;
            valueAddress = foundValueAddress;
            return true;
        }

        error =
            "Object-metadata binder changed while it was being read";

        return false;
    }

    private bool TryReadStablePresentationEntries(
        ProcessMemoryReader memory,
        uint mapAddress,
        out HashMapHeader stableHeader,
        out IReadOnlyList<PresentationMapEntry> entries,
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
                    out error) ||
                !TryReadBucketHeads(
                    memory,
                    before,
                    out var bucketHeads,
                    out error))
            {
                return false;
            }

            List<PresentationMapEntry> observedEntries = [];
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
                        error = $"Gutter presentation map contains a duplicate or cycle at 0x{nodeAddress:X8}";

                        return false;
                    }

                    if (!memory.TryReadBytes(
                            nodeAddress,
                            UInt32PointerMapNodeSnapshotLength,
                            out var nodeBytes))
                    {
                        error = $"Could not read gutter presentation map node at 0x{nodeAddress:X8}";

                        return false;
                    }

                    var nextAddress = BitConverter.ToUInt32(
                        nodeBytes,
                        checked((int)UInt32PointerMapNodeNext));

                    var clientObjectAddress = BitConverter.ToUInt32(
                        nodeBytes,
                        checked((int)UInt32PointerMapNodeKey));

                    var presentationAddress = BitConverter.ToUInt32(
                        nodeBytes,
                        checked((int)UInt32PointerMapNodeValue));

                    if (clientObjectAddress == 0 ||
                        presentationAddress == 0)
                    {
                        error = $"Gutter presentation map node at 0x{nodeAddress:X8} contains a null key or value";

                        return false;
                    }

                    observedEntries.Add(
                        new PresentationMapEntry(
                            nodeAddress,
                            clientObjectAddress,
                            presentationAddress));

                    if (observedEntries.Count >
                        MaximumMapEntryCount)
                    {
                        error = string.Create(
                            CultureInfo.InvariantCulture,
                            $"Gutter presentation map exceeded {MaximumMapEntryCount} entries");

                        return false;
                    }

                    nodeAddress = nextAddress;
                }

                if (nodeAddress != 0)
                {
                    error = string.Create(
                        CultureInfo.InvariantCulture,
                        $"Gutter presentation map chain exceeded {MaximumMapChainLength} nodes");

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
            "Gutter presentation map changed while it was being read";

        return false;
    }

    private bool TryObserveTargets(
        ProcessMemoryReader memory,
        ObservedClientState state,
        ProcessCache cache,
        string separator,
        RadarRoot radarRoot,
        IReadOnlyList<PresentationMapEntry> entries,
        out IReadOnlyList<ClientGutterRadarTargetObservation> targets,
        out string error)
    {
        targets = [];
        error = "";

        uint expectedPresentationVTable;
        uint expectedClientObjectVTable;

        try
        {
            expectedPresentationVTable = checked(
                state.ModuleBaseAddress +
                GutterRadarObjectPresentationVTableRva);

            expectedClientObjectVTable = checked(
                state.ModuleBaseAddress +
                ClientGameObjectVTableRva);
        }
        catch (OverflowException)
        {
            error =
                "Gutter target vtable address overflow";

            return false;
        }

        List<ClientGutterRadarTargetObservation> observed = [];
        HashSet<uint> observedObjectIds = [];
        var navigationObjectIds = state.Navigation.IsAvailable &&
            (state.Navigation.ActiveSectorNumber == 0 ||
             state.Navigation.ActiveSectorNumber ==
                state.World.ActiveSectorNumber)
            ? state.Navigation.Targets
                .Where(target =>
                    target.IsAvailable &&
                    target.ObjectId != 0)
                .Select(target => target.ObjectId)
                .ToHashSet()
            : [];

        foreach (var entry in entries)
        {
            if (!memory.TryReadBytes(
                    entry.PresentationAddress,
                    PresentationSnapshotLength,
                    out var presentationBytes))
            {
                error = $"Could not read GutterRadarObjectPresentation at 0x{entry.PresentationAddress:X8}";

                return false;
            }

            var presentationVTable = BitConverter.ToUInt32(
                presentationBytes,
                checked((int)PresentationVTable));

            var boundClientObjectAddress = BitConverter.ToUInt32(
                presentationBytes,
                checked((int)PresentationClientObject));

            if (presentationVTable !=
                    expectedPresentationVTable ||
                boundClientObjectAddress !=
                    entry.ClientObjectAddress)
            {
                error = $"GutterRadarObjectPresentation at 0x{entry.PresentationAddress:X8} failed structural validation";

                return false;
            }

            var normalizedX = BitConverter.ToSingle(
                presentationBytes,
                checked((int)PresentationNormalizedX));

            var normalizedY = BitConverter.ToSingle(
                presentationBytes,
                checked((int)PresentationNormalizedY));

            if (!float.IsFinite(normalizedX) ||
                !float.IsFinite(normalizedY) ||
                normalizedX < MinimumNormalizedCoordinate ||
                normalizedX > MaximumNormalizedCoordinate ||
                normalizedY < MinimumNormalizedCoordinate ||
                normalizedY > MaximumNormalizedCoordinate)
            {
                error = string.Create(
                    CultureInfo.InvariantCulture,
                    $"GutterRadarObjectPresentation at 0x{entry.PresentationAddress:X8} contains unexpected normalized position ({normalizedX}, {normalizedY})");

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

            var relationshipRaw = BitConverter.ToInt32(
                clientObjectBytes,
                checked((int)ClientObjectRelationshipRaw));

            var aggressionRaw = BitConverter.ToInt32(
                clientObjectBytes,
                checked((int)ClientObjectAggressionRaw));

            var relationship =
                ClientObjectRelationshipObservation.FromRaw(
                    relationshipRaw,
                    aggressionRaw);

            if (clientObjectVTable !=
                    expectedClientObjectVTable ||
                objectId is 0 or uint.MaxValue)
            {
                error = $"Bound ClientGameObject at 0x{entry.ClientObjectAddress:X8} failed structural validation";

                return false;
            }

            if (!observedObjectIds.Add(objectId))
            {
                error = $"Gutter presentation map contains duplicate ObjectId {objectId}";

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

            var kind =
                ClientNearbyTargetKindCatalog.FromRawObjectType(
                    rawObjectType);
            var isGutterOnlyStaticWorldTarget =
                !navigationObjectIds.Contains(objectId) &&
                ShouldObserveStaticWorldTarget(kind);
            ClientSpatialObservation staticWorldSpatial;

            if (!isGutterOnlyStaticWorldTarget)
            {
                staticWorldSpatial = ClientSpatialObservation.Unavailable(
                    navigationObjectIds.Contains(objectId)
                        ? "Target is already present in NavigationData"
                        : "Nearby target kind is not a supported static world target",
                    entry.ClientObjectAddress);
            }
            else if (cache.StaticWorldSpatials.TryGetValue(
                         objectId,
                         out var cachedStaticSpatial))
            {
                staticWorldSpatial = cachedStaticSpatial;
            }
            else
            {
                staticWorldSpatial = this.spatialObserver.Observe(
                    memory,
                    state.ModuleBaseAddress,
                    state.CurrentClientTime,
                    entry.ClientObjectAddress);

                if (staticWorldSpatial.IsAvailable &&
                    staticWorldSpatial.ProviderKind ==
                        ClientSpatialProviderKind.FixedPosition &&
                    staticWorldSpatial.StateKind ==
                        ClientSpatialStateKind.Static)
                {
                    cache.StaticWorldSpatials[objectId] =
                        staticWorldSpatial;
                }
            }

            ClientNearbyTargetVitalsObservation vitals;

            if (ShouldObserveVitals(kind))
            {
                cache.Vitals.TryGetValue(
                    entry.ClientObjectAddress,
                    out var cachedVitalsBinding);

                vitals = this.vitalsReader.Observe(
                    memory,
                    state.ModuleBaseAddress,
                    state.CurrentClientTime,
                    auxDataAddress,
                    cachedVitalsBinding);

                if (vitals.Binding != null)
                {
                    cache.Vitals[entry.ClientObjectAddress] =
                        vitals.Binding;
                }
                else
                {
                    cache.Vitals.Remove(
                        entry.ClientObjectAddress);
                }
            }
            else
            {
                cache.Vitals.Remove(
                    entry.ClientObjectAddress);

                vitals =
                    ClientNearbyTargetVitalsObservation.Unavailable(
                        "Nearby target kind does not expose combat vitals");
            }

            ClientNearbyMobObservation mob;

            if (ShouldObserveMob(kind))
            {
                var now = DateTimeOffset.UtcNow;

                if (cache.Mobs.TryGetValue(
                        entry.ClientObjectAddress,
                        out var cachedMob) &&
                    cachedMob.ObjectId == objectId &&
                    cachedMob.AuxDataAddress == auxDataAddress &&
                    now - cachedMob.ObservedAtUtc <
                        MobObservationRefreshInterval)
                {
                    var spatial = this.spatialObserver.Observe(
                        memory,
                        state.ModuleBaseAddress,
                        state.CurrentClientTime,
                        entry.ClientObjectAddress);
                    var hasSemanticIdentity =
                        !string.IsNullOrWhiteSpace(
                            cachedMob.Observation.Name) &&
                        cachedMob.Observation.CombatLevel.HasValue &&
                        cachedMob.Observation.IsOrganic.HasValue;

                    mob = cachedMob.Observation with
                    {
                        IsAvailable =
                            hasSemanticIdentity && spatial.IsAvailable,
                        Status = hasSemanticIdentity
                            ? spatial.IsAvailable
                                ? "Available"
                                : $"Waiting for complete mob world position: {spatial.Status}"
                            : cachedMob.Observation.Status,
                        Spatial = spatial,
                    };
                }
                else
                {
                    mob = this.mobObserver.Observe(
                        memory,
                        state.ModuleBaseAddress,
                        state.CurrentClientTime,
                        entry.ClientObjectAddress,
                        auxDataAddress,
                        identity.Name);
                    cache.Mobs[entry.ClientObjectAddress] =
                        new CachedMobObservation(
                            objectId,
                            auxDataAddress,
                            now,
                            mob);
                }
            }
            else
            {
                cache.Mobs.Remove(entry.ClientObjectAddress);
                mob = ClientNearbyMobObservation.Unavailable(
                    "Nearby target kind is not a mob");
            }

            ClientSpatialObservation corpseSpatial;
            ClientCorpseObservation corpse;

            if (kind == ClientNearbyTargetKind.Corpse)
            {
                corpseSpatial = this.spatialObserver.Observe(
                    memory,
                    state.ModuleBaseAddress,
                    state.CurrentClientTime,
                    entry.ClientObjectAddress);
                var shouldReadHydratedCargo =
                    objectId == state.TargetObjectId ||
                    (state.Looting.IsAvailable &&
                     state.Looting.LootTargetObjectId == objectId);

                corpse = this.corpseObserver.Observe(
                    memory,
                    state.ModuleBaseAddress,
                    auxDataAddress,
                    readOccupiedDetails: shouldReadHydratedCargo);
            }
            else
            {
                corpseSpatial = ClientSpatialObservation.Unavailable(
                    "Nearby target kind is not a corpse",
                    entry.ClientObjectAddress);
                corpse = ClientCorpseObservation.NotApplicable();
            }

            var iconRenderObjectAddress = BitConverter.ToUInt32(
                presentationBytes,
                checked((int)PresentationIconRenderObject));

            var iconSubObjectAddress = BitConverter.ToUInt32(
                presentationBytes,
                checked((int)PresentationIconSubObject));

            var iconPartId = BitConverter.ToUInt32(
                presentationBytes,
                checked((int)PresentationIconPartId));

            var unknown1C = BitConverter.ToUInt32(
                presentationBytes,
                checked((int)PresentationUnknown1C));

            var unknown20 = BitConverter.ToUInt32(
                presentationBytes,
                checked((int)PresentationUnknown20));

            var isInsideViewport =
                presentationBytes[
                    checked((int)PresentationIsInsideViewport)] != 0;

            var gutterIndicatorAddress = BitConverter.ToUInt32(
                presentationBytes,
                checked((int)PresentationGutterIndicator));

            List<string> statusParts = [];

            if (!string.Equals(
                    identity.Status,
                    "Available",
                    StringComparison.Ordinal))
            {
                statusParts.Add(
                    $"name: {identity.Status}");
            }

            if (ShouldObserveVitals(kind) &&
                (!vitals.Hull.IsAvailable ||
                 !vitals.Shield.IsAvailable))
            {
                statusParts.Add(
                    $"vitals: {vitals.Status}");
            }

            if (ShouldObserveMob(kind) && !mob.IsAvailable)
            {
                statusParts.Add(
                    $"mob: {mob.Status}");
            }

            if (isGutterOnlyStaticWorldTarget &&
                !staticWorldSpatial.IsAvailable)
            {
                statusParts.Add(
                    $"world position: {staticWorldSpatial.Status}");
            }

            if (kind == ClientNearbyTargetKind.Corpse &&
                !corpseSpatial.IsAvailable)
            {
                statusParts.Add(
                    $"corpse position: {corpseSpatial.Status}");
            }

            if (kind == ClientNearbyTargetKind.Corpse &&
                !corpse.IsAvailable)
            {
                statusParts.Add(
                    $"corpse cargo: {corpse.Status}");
            }

            if (iconRenderObjectAddress == 0)
            {
                statusParts.Add(
                    "icon render object is null");
            }

            observed.Add(
                new ClientGutterRadarTargetObservation
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
                    PresentationMapNodeAddress =
                        entry.NodeAddress,
                    PresentationAddress =
                        entry.PresentationAddress,
                    AuxDataAddress =
                        auxDataAddress,
                    RawObjectType =
                        rawObjectType,
                    Kind = kind,
                    Name = identity.Name,
                    Owner = identity.Owner,
                    Title = identity.Title,
                    Rank = identity.Rank,
                    DisplayName =
                        identity.MapDisplayName,
                    NameStatus = identity.Status,
                    NormalizedX =
                        normalizedX,
                    NormalizedY =
                        normalizedY,
                    IsInsideViewport =
                        isInsideViewport,
                    IsHovered =
                        entry.ClientObjectAddress ==
                            radarRoot.HoveredClientObjectAddress,
                    Hull = vitals.Hull,
                    Shield = vitals.Shield,
                    Mob = mob,
                    Relationship = relationship,
                    Spatial = staticWorldSpatial,
                    CorpseSpatial = corpseSpatial,
                    Corpse = corpse,
                    IconRenderObjectAddress =
                        iconRenderObjectAddress,
                    IconSubObjectAddress =
                        iconSubObjectAddress,
                    IconPartId =
                        iconPartId,
                    Unknown1C =
                        unknown1C,
                    Unknown20 =
                        unknown20,
                    GutterIndicatorAddress =
                        gutterIndicatorAddress,
                });
        }

        targets = [.. observed
            .OrderBy(
                target =>
                    target.ObjectId)];

        return true;
    }

    private static bool ShouldObserveStaticWorldTarget(
        ClientNearbyTargetKind kind)
    {
        return kind is
            ClientNearbyTargetKind.Planet or
            ClientNearbyTargetKind.SectorGate or
            ClientNearbyTargetKind.Station or
            ClientNearbyTargetKind.NavigationPoint;
    }

    private static bool ShouldObserveVitals(
        ClientNearbyTargetKind kind)
    {
        return kind is
            ClientNearbyTargetKind.NonPlayerShip or
            ClientNearbyTargetKind.Player or
            ClientNearbyTargetKind.CapitalShip or
            ClientNearbyTargetKind.Building or
            ClientNearbyTargetKind.Drone;
    }

    private static bool ShouldObserveMob(
        ClientNearbyTargetKind kind)
    {
        return kind is
            ClientNearbyTargetKind.NonPlayerShip or
            ClientNearbyTargetKind.CapitalShip or
            ClientNearbyTargetKind.Drone;
    }

    private bool TryReadClientObjectIdentity(
        ProcessMemoryReader memory,
        uint moduleBaseAddress,
        uint clientObjectAddress,
        out uint? objectId,
        out string error)
    {
        objectId = null;
        error = "";

        uint expectedClientObjectVTable;

        try
        {
            expectedClientObjectVTable = checked(
                moduleBaseAddress +
                ClientGameObjectVTableRva);
        }
        catch (OverflowException)
        {
            error =
                "Hovered ClientGameObject vtable address overflow";

            return false;
        }

        if (!memory.TryReadBytes(
                clientObjectAddress,
                ClientObjectSnapshotLength,
                out var bytes))
        {
            error = $"Could not read hovered ClientGameObject at 0x{clientObjectAddress:X8}";

            return false;
        }

        var vtable = BitConverter.ToUInt32(
            bytes,
            0);

        var observedObjectId = BitConverter.ToUInt32(
            bytes,
            checked((int)ClientObjectObjectId));

        if (vtable != expectedClientObjectVTable ||
            observedObjectId is 0 or uint.MaxValue)
        {
            error = $"Hovered ClientGameObject at 0x{clientObjectAddress:X8} failed structural validation";

            return false;
        }

        objectId = observedObjectId;
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

        if (bucketBegin == 0 &&
            bucketEnd == 0 &&
            bucketCapacityEnd == 0 &&
            entryCountRaw == 0)
        {
            header = new HashMapHeader(
                0,
                0,
                0,
                0,
                0);

            return true;
        }

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

        if (header.BucketCount == 0)
        {
            return true;
        }

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

    private static bool TryReadPointer(
        ProcessMemoryReader memory,
        uint baseAddress,
        uint offset,
        string fieldName,
        out uint value,
        out string error)
    {
        return TryReadUInt32(
            memory,
            baseAddress,
            offset,
            fieldName,
            out value,
            out error);
    }

    private static bool TryReadUInt32(
        ProcessMemoryReader memory,
        uint baseAddress,
        uint offset,
        string fieldName,
        out uint value,
        out string error)
    {
        value = 0;
        error = "";

        uint address;

        try
        {
            address = checked(
                baseAddress + offset);
        }
        catch (OverflowException)
        {
            error =
                $"Address overflow while reading {fieldName}";

            return false;
        }

        if (!memory.TryReadUInt32(
                address,
                out value))
        {
            error = $"Could not read {fieldName} at 0x{address:X8}";

            return false;
        }

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
        ClientGutterRadarObservation observation)
    {
        state.NearbyTargets = observation;
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

        public GutterRadarBinding? Binding { get; set; }

        public Dictionary<uint, CachedIdentity> Identities
        {
            get;
        } = [];

        public Dictionary<uint, ClientNearbyTargetVitalsBinding> Vitals
        {
            get;
        } = [];

        public Dictionary<uint, CachedMobObservation> Mobs
        {
            get;
        } = [];

        public Dictionary<uint, ClientSpatialObservation> StaticWorldSpatials
        {
            get;
        } = [];
    }

    private sealed record GutterRadarBinding(
        uint BinderRegistryAddress,
        uint BinderEntryAddress,
        uint BinderKeyAddress,
        uint PresentationMapAddress,
        uint RuntimeTypeNameAddress,
        string RuntimeTypeName);

    private sealed record CachedIdentity(
        uint ObjectId,
        uint AuxDataAddress,
        byte RawObjectType,
        string Separator,
        ClientMapIdentityReadResult Identity);

    private sealed record CachedMobObservation(
        uint ObjectId,
        uint AuxDataAddress,
        DateTimeOffset ObservedAtUtc,
        ClientNearbyMobObservation Observation);

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct RadarRoot(
        uint MainViewAddress,
        uint RadarSystemAddress,
        uint HoveredClientObjectAddress,
        int ActivePresentationCount);

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct PresentationMapEntry(
        uint NodeAddress,
        uint ClientObjectAddress,
        uint PresentationAddress);

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct HashMapHeader(
        uint BucketBegin,
        uint BucketEnd,
        uint BucketCapacityEnd,
        int EntryCount,
        int BucketCount);
}
