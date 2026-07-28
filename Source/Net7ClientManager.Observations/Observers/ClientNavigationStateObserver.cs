namespace Net7ClientManager.Observations.Observers;

using Net7ClientManager.Observations.Models;

internal sealed class ClientNavigationStateObserver
{
    private const uint ClientContextCurrentTime = 0x10bc;
    private const uint ClientContextLocalPlayerObjectId = 0x112c;
    private const uint ClientContextFrameProcessingGate = 0x1198;
    private const uint ClientContextGalaxyMap = 0x12a0;
    private const uint ClientContextPathController = 0x12a4;
    private const uint ClientContextActiveSectorNumber = 0x12f0;
    private const uint ClientContextPresentationMode = 0x136c;

    private const uint PathControllerBuildBusy = 0x10;
    private const uint GalaxyMapCurrentSectorName = 0xb0;

    private const uint ClientObjectAuxData = 0x88;
    private const uint ObjectAuxDataLookup = 0x94;
    private const uint ShipAuxTargetGameId = 0x130c;

    private const uint AuxDataStateMarker = 0x70;
    private const uint AuxDataScalarValue = 0x84;
    private const uint AuxDataSecondaryValue = 0x88;

    private const int MaximumLocationNameLength = 256;

    private const string PrivateWarpStateName = "PrivateWarpState";
    private const string GlobalWarpStateName = "GlobalWarpState";
    private const string WarpAvailableName = "WarpAvailable";
    private const string WarpTriggerTimeName = "WarpTriggerTime";
    private const string MaximumSpeedName = "MaxSpeed";
    private const string LockSpeedName = "LockSpeed";
    private const string LockOrientName = "LockOrient";
    private const string EngineThrustStateName = "EngineThrustState";
    private const string IsCloakedName = "IsCloaked";
    private const string EnergyPercentName = "EnergyPercent";
    private const string MaximumEnergyPowerName = "MaxEnergyPower";

    private static readonly string[] propertyNames =
    [
        PrivateWarpStateName,
        GlobalWarpStateName,
        WarpAvailableName,
        WarpTriggerTimeName,
        MaximumSpeedName,
        LockSpeedName,
        LockOrientName,
        EngineThrustStateName,
        IsCloakedName,
        EnergyPercentName,
        MaximumEnergyPowerName,
    ];

    private readonly System.Threading.Lock lockObject = new();
    private readonly Dictionary<int, ProcessCache> caches = [];
    private readonly ClientObjectResolver objectResolver = new();
    private readonly ClientAuxDataLookupReader auxDataReader = new();

    public ClientNavigationStateObservation Observe(
        ProcessMemoryReader memory,
        int processId,
        DateTimeOffset processStartedAt,
        long sequence,
        uint moduleBaseAddress,
        uint clientContextAddress,
        ClientLifecycleState lifecycleState,
        ClientWorldObservation world,
        ClientTargetObservation target,
        DateTimeOffset? targetObservedAt,
        ClientNavigationStateObservation? previous)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(target);

        var now = DateTimeOffset.UtcNow;

        ProcessCache cache;

        lock (this.lockObject)
        {
            if (!this.caches.TryGetValue(processId, out var existing))
            {
                existing = new ProcessCache();
                this.caches.Add(processId, existing);
            }

            cache = existing;
        }

        if (clientContextAddress == 0)
        {
            return CreateUnavailable(
                processId,
                processStartedAt,
                sequence,
                cache.GenerationSequence,
                lifecycleState,
                world,
                "SClient is unavailable");
        }

        _ = TryReadUInt32(
            memory,
            clientContextAddress,
            ClientContextCurrentTime,
            out var currentClientTime);

        _ = TryReadUInt32(
            memory,
            clientContextAddress,
            ClientContextLocalPlayerObjectId,
            out var localPlayerObjectId);

        _ = TryReadUInt32(
            memory,
            clientContextAddress,
            ClientContextFrameProcessingGate,
            out var frameProcessingGateFlag);

        var directWorld = ReadDirectWorldContext(
            memory,
            clientContextAddress,
            world);

        var pathBuildStateKnown = false;
        var pathBuildBusy = false;

        if (TryReadUInt32(
                memory,
                clientContextAddress,
                ClientContextPathController,
                out var pathControllerAddress) &&
            pathControllerAddress != 0 &&
            TryReadUInt32(
                memory,
                pathControllerAddress,
                PathControllerBuildBusy,
                out var pathBuildBusyRaw))
        {
            pathBuildStateKnown = true;
            pathBuildBusy = pathBuildBusyRaw != 0;
        }

        if (!this.objectResolver.TryResolveLocalPlayerClientObject(
                memory,
                clientContextAddress,
                out var clientObjectAddress,
                out var objectError,
                out _))
        {
            var absentGeneration = new ClientNavigationStateGeneration(
                moduleBaseAddress,
                clientContextAddress,
                0,
                0,
                0);

            var generationChanged = UpdateGeneration(
                cache,
                absentGeneration);

            var awaitingReplacement =
                cache.AwaitingWorldReplacement ||
                previous?.Generation.HasClientObject == true;

            return new ClientNavigationStateObservation
            {
                ProcessId = processId,
                ProcessStartedAt = processStartedAt,
                Sequence = sequence,
                ObservedAt = now,
                IsAvailable = false,
                Status = objectError,
                LifecycleState = lifecycleState,
                Environment = ClientWorldEnvironment.Transitioning,
                ActiveSectorNumber = directWorld.ActiveSectorNumber,
                SectorName = directWorld.SectorName,
                SectorNameDirectlyObserved =
                    directWorld.SectorNameDirectlyObserved,
                PresentationMode = directWorld.PresentationMode,
                IsWorldPresent = false,
                IsLoading = true,
                Generation = absentGeneration,
                GenerationSequence = cache.GenerationSequence,
                GenerationChanged = generationChanged,
                CurrentClientTime = currentClientTime,
                FrameProcessingGateFlag = frameProcessingGateFlag,
                LocalPlayerObjectId = localPlayerObjectId,
                PathBuildStateKnown = pathBuildStateKnown,
                PathBuildBusy = pathBuildBusy,
                TargetDistance = ReadTargetDistance(target),
                TargetDistanceObservedAt = targetObservedAt,
                Phase = awaitingReplacement
                    ? ClientNavigationStatePhase.AwaitingWorldReplacement
                    : ClientNavigationStatePhase.Loading,
            };
        }

        var environment = directWorld.PresentationModeKnown
            ? ClassifyEnvironment(directWorld.PresentationMode)
            : ClientWorldEnvironment.Unknown;

        var isLoading =
            lifecycleState != ClientLifecycleState.InGame ||
            !directWorld.PresentationModeKnown ||
            directWorld.PresentationMode == 2;

        var isWorldPresent =
            lifecycleState == ClientLifecycleState.InGame &&
            directWorld.ActiveSectorNumberKnown &&
            directWorld.PresentationModeKnown &&
            directWorld.ActiveSectorNumber != 0 &&
            directWorld.PresentationMode != 2;

        if (!TryReadUInt32(
                memory,
                clientObjectAddress,
                ClientObjectAuxData,
                out var auxDataAddress) ||
            auxDataAddress == 0)
        {
            var partialGeneration =
                new ClientNavigationStateGeneration(
                    moduleBaseAddress,
                    clientContextAddress,
                    clientObjectAddress,
                    0,
                    0);

            var generationChanged = UpdateGeneration(
                cache,
                partialGeneration);

            return CreatePartialObservation(
                processId,
                processStartedAt,
                sequence,
                cache.GenerationSequence,
                lifecycleState,
                environment,
                directWorld.ActiveSectorNumber,
                directWorld.SectorName,
                directWorld.SectorNameDirectlyObserved,
                directWorld.PresentationMode,
                isWorldPresent,
                isLoading,
                partialGeneration,
                generationChanged,
                currentClientTime,
                frameProcessingGateFlag,
                localPlayerObjectId,
                pathBuildStateKnown,
                pathBuildBusy,
                target,
                targetObservedAt,
                "Local-player AuxData is unavailable",
                ClientNavigationStatePhase.AwaitingAuxData);
        }

        if (!TryReadUInt32(
                memory,
                auxDataAddress,
                ObjectAuxDataLookup,
                out var auxDataLookupAddress) ||
            auxDataLookupAddress == 0)
        {
            var partialGeneration =
                new ClientNavigationStateGeneration(
                    moduleBaseAddress,
                    clientContextAddress,
                    clientObjectAddress,
                    auxDataAddress,
                    0);

            var generationChanged = UpdateGeneration(
                cache,
                partialGeneration);

            return CreatePartialObservation(
                processId,
                processStartedAt,
                sequence,
                cache.GenerationSequence,
                lifecycleState,
                environment,
                directWorld.ActiveSectorNumber,
                directWorld.SectorName,
                directWorld.SectorNameDirectlyObserved,
                directWorld.PresentationMode,
                isWorldPresent,
                isLoading,
                partialGeneration,
                generationChanged,
                currentClientTime,
                frameProcessingGateFlag,
                localPlayerObjectId,
                pathBuildStateKnown,
                pathBuildBusy,
                target,
                targetObservedAt,
                "Local-player AuxData lookup is unavailable",
                ClientNavigationStatePhase.AwaitingAuxData);
        }

        var generation = new ClientNavigationStateGeneration(
            moduleBaseAddress,
            clientContextAddress,
            clientObjectAddress,
            auxDataAddress,
            auxDataLookupAddress);

        var didChangeGeneration = UpdateGeneration(
            cache,
            generation);

        if (!cache.HasLookup)
        {
            if (!this.auxDataReader.TryOpenTargeted(
                    memory,
                    moduleBaseAddress,
                    auxDataAddress,
                    propertyNames,
                    out var lookup,
                    out _,
                    out var lookupError))
            {
                return new ClientNavigationStateObservation
                {
                    ProcessId = processId,
                    ProcessStartedAt = processStartedAt,
                    Sequence = sequence,
                    ObservedAt = now,
                    IsAvailable = false,
                    Status = lookupError,
                    LifecycleState = lifecycleState,
                    Environment = environment,
                    ActiveSectorNumber = directWorld.ActiveSectorNumber,
                    SectorName = directWorld.SectorName,
                    SectorNameDirectlyObserved =
                        directWorld.SectorNameDirectlyObserved,
                    PresentationMode = directWorld.PresentationMode,
                    IsWorldPresent = isWorldPresent,
                    IsLoading = isLoading,
                    Generation = generation,
                    GenerationSequence = cache.GenerationSequence,
                    GenerationChanged = didChangeGeneration,
                    CurrentClientTime = currentClientTime,
                    FrameProcessingGateFlag = frameProcessingGateFlag,
                    LocalPlayerObjectId = localPlayerObjectId,
                    PathBuildStateKnown = pathBuildStateKnown,
                    PathBuildBusy = pathBuildBusy,
                    TargetDistance = ReadTargetDistance(target),
                    TargetDistanceObservedAt = targetObservedAt,
                    Phase = ClientNavigationStatePhase.AwaitingAuxData,
                };
            }

            if (lookup.LookupAddress != auxDataLookupAddress)
            {
                return new ClientNavigationStateObservation
                {
                    ProcessId = processId,
                    ProcessStartedAt = processStartedAt,
                    Sequence = sequence,
                    ObservedAt = now,
                    IsAvailable = false,
                    Status = "AuxData lookup changed while navigation properties were being resolved",
                    LifecycleState = lifecycleState,
                    Environment = environment,
                    ActiveSectorNumber = directWorld.ActiveSectorNumber,
                    SectorName = directWorld.SectorName,
                    SectorNameDirectlyObserved =
                        directWorld.SectorNameDirectlyObserved,
                    PresentationMode = directWorld.PresentationMode,
                    IsWorldPresent = isWorldPresent,
                    IsLoading = isLoading,
                    Generation = generation,
                    GenerationSequence = cache.GenerationSequence,
                    GenerationChanged = didChangeGeneration,
                    CurrentClientTime = currentClientTime,
                    FrameProcessingGateFlag = frameProcessingGateFlag,
                    LocalPlayerObjectId = localPlayerObjectId,
                    PathBuildStateKnown = pathBuildStateKnown,
                    PathBuildBusy = pathBuildBusy,
                    TargetDistance = ReadTargetDistance(target),
                    TargetDistanceObservedAt = targetObservedAt,
                    Phase = ClientNavigationStatePhase.AwaitingAuxData,
                };
            }

            cache.Lookup = lookup;
            cache.HasLookup = true;
        }

        if (!TryReadInt32(
                memory,
                cache.Lookup,
                PrivateWarpStateName,
                out var privateWarpState) ||
            !TryReadInt32(
                memory,
                cache.Lookup,
                GlobalWarpStateName,
                out var globalWarpState) ||
            !TryReadInt32(
                memory,
                cache.Lookup,
                WarpAvailableName,
                out var warpAvailable) ||
            !TryReadFloat(
                memory,
                cache.Lookup,
                MaximumSpeedName,
                out var maximumSpeed) ||
            !TryReadBoolean(
                memory,
                cache.Lookup,
                LockSpeedName,
                out var lockSpeed) ||
            !TryReadBoolean(
                memory,
                cache.Lookup,
                LockOrientName,
                out var lockOrient))
        {
            return new ClientNavigationStateObservation
            {
                ProcessId = processId,
                ProcessStartedAt = processStartedAt,
                Sequence = sequence,
                ObservedAt = now,
                IsAvailable = false,
                Status = "Could not read one or more navigation properties",
                LifecycleState = lifecycleState,
                Environment = environment,
                ActiveSectorNumber = directWorld.ActiveSectorNumber,
                SectorName = directWorld.SectorName,
                SectorNameDirectlyObserved =
                    directWorld.SectorNameDirectlyObserved,
                PresentationMode = directWorld.PresentationMode,
                IsWorldPresent = isWorldPresent,
                IsLoading = isLoading,
                Generation = generation,
                GenerationSequence = cache.GenerationSequence,
                GenerationChanged = didChangeGeneration,
                CurrentClientTime = currentClientTime,
                FrameProcessingGateFlag = frameProcessingGateFlag,
                LocalPlayerObjectId = localPlayerObjectId,
                PathBuildStateKnown = pathBuildStateKnown,
                PathBuildBusy = pathBuildBusy,
                TargetDistance = ReadTargetDistance(target),
                TargetDistanceObservedAt = targetObservedAt,
                Phase = ClientNavigationStatePhase.AwaitingAuxData,
            };
        }

        _ = TryReadUInt64Secondary(
            memory,
            cache.Lookup,
            WarpTriggerTimeName,
            out var warpTriggerTime);

        _ = TryReadInt32(
            memory,
            cache.Lookup,
            EngineThrustStateName,
            out var engineThrustState);

        _ = TryReadBoolean(
            memory,
            cache.Lookup,
            IsCloakedName,
            out var isCloaked);

        _ = this.TryReadEnergyFraction(
            memory,
            cache.Lookup,
            currentClientTime,
            out var energyFraction);

        _ = TryReadFloat(
            memory,
            cache.Lookup,
            MaximumEnergyPowerName,
            out var maximumEnergyPower);

        var selectedTargetKnown = TryReadUInt32(
            memory,
            auxDataAddress,
            ShipAuxTargetGameId,
            out var selectedTargetObjectId);

        var requiredPropertiesAvailable =
            privateWarpState.IsAvailable &&
            globalWarpState.IsAvailable &&
            warpAvailable.IsAvailable &&
            maximumSpeed.IsAvailable &&
            lockSpeed.IsAvailable &&
            lockOrient.IsAvailable;

        UpdateTerminalWarpReason(
            cache,
            privateWarpState,
            globalWarpState,
            now);

        float? currentEnergyPower = null;

        if (energyFraction.IsAvailable &&
            maximumEnergyPower.IsAvailable)
        {
            currentEnergyPower =
                Math.Clamp(
                    energyFraction.Value,
                    0.0f,
                    1.0f) *
                maximumEnergyPower.Value;
        }

        var phase = DerivePhase(
            lifecycleState,
            isLoading,
            isWorldPresent,
            requiredPropertiesAvailable,
            pathBuildStateKnown,
            pathBuildBusy,
            privateWarpState,
            globalWarpState);

        return new ClientNavigationStateObservation
        {
            ProcessId = processId,
            ProcessStartedAt = processStartedAt,
            Sequence = sequence,
            ObservedAt = now,
            IsAvailable = requiredPropertiesAvailable,
            Status = requiredPropertiesAvailable
                ? "Available"
                : "Required navigation properties are not populated yet",
            LifecycleState = lifecycleState,
            Environment = environment,
            ActiveSectorNumber = directWorld.ActiveSectorNumber,
            SectorName = directWorld.SectorName,
            SectorNameDirectlyObserved =
                directWorld.SectorNameDirectlyObserved,
            PresentationMode = directWorld.PresentationMode,
            IsWorldPresent = isWorldPresent,
            IsLoading = isLoading,
            Generation = generation,
            GenerationSequence = cache.GenerationSequence,
            GenerationChanged = didChangeGeneration,
            CurrentClientTime = currentClientTime,
            FrameProcessingGateFlag = frameProcessingGateFlag,
            LocalPlayerObjectId = localPlayerObjectId,
            SelectedTargetKnown = selectedTargetKnown,
            SelectedTargetObjectId = selectedTargetObjectId,
            PathBuildStateKnown = pathBuildStateKnown,
            PathBuildBusy = pathBuildBusy,
            TargetDistance = ReadTargetDistance(target),
            TargetDistanceObservedAt = targetObservedAt,
            PrivateWarpState = privateWarpState,
            GlobalWarpState = globalWarpState,
            WarpAvailable = warpAvailable,
            WarpPhaseTriggerClientTime = warpTriggerTime,
            MaximumSpeed = maximumSpeed,
            LockSpeed = lockSpeed,
            LockOrient = lockOrient,
            EngineThrustState = engineThrustState,
            IsCloaked = isCloaked,
            EnergyFraction = energyFraction,
            MaximumEnergyPower = maximumEnergyPower,
            CurrentEnergyPower = currentEnergyPower,
            LastTerminalWarpReason = cache.LastTerminalWarpReason,
            LastTerminalWarpReasonAt = cache.LastTerminalWarpReasonAt,
            RequiredPropertiesAvailable = requiredPropertiesAvailable,
            Phase = phase,
        };
    }

    public void Forget(int processId)
    {
        lock (this.lockObject)
        {
            this.caches.Remove(processId);
        }
    }

    private static ClientNavigationStateObservation CreatePartialObservation(
        int processId,
        DateTimeOffset processStartedAt,
        long sequence,
        long generationSequence,
        ClientLifecycleState lifecycleState,
        ClientWorldEnvironment environment,
        uint activeSectorNumber,
        string sectorName,
        bool sectorNameDirectlyObserved,
        uint presentationMode,
        bool isWorldPresent,
        bool isLoading,
        ClientNavigationStateGeneration generation,
        bool generationChanged,
        uint currentClientTime,
        uint frameProcessingGateFlag,
        uint localPlayerObjectId,
        bool pathBuildStateKnown,
        bool pathBuildBusy,
        ClientTargetObservation target,
        DateTimeOffset? targetObservedAt,
        string status,
        ClientNavigationStatePhase phase)
    {
        return new ClientNavigationStateObservation
        {
            ProcessId = processId,
            ProcessStartedAt = processStartedAt,
            Sequence = sequence,
            ObservedAt = DateTimeOffset.UtcNow,
            Status = status,
            LifecycleState = lifecycleState,
            Environment = environment,
            ActiveSectorNumber = activeSectorNumber,
            SectorName = sectorName,
            SectorNameDirectlyObserved =
                sectorNameDirectlyObserved,
            PresentationMode = presentationMode,
            IsWorldPresent = isWorldPresent,
            IsLoading = isLoading,
            Generation = generation,
            GenerationSequence = generationSequence,
            GenerationChanged = generationChanged,
            CurrentClientTime = currentClientTime,
            FrameProcessingGateFlag = frameProcessingGateFlag,
            LocalPlayerObjectId = localPlayerObjectId,
            PathBuildStateKnown = pathBuildStateKnown,
            PathBuildBusy = pathBuildBusy,
            TargetDistance = ReadTargetDistance(target),
            TargetDistanceObservedAt = targetObservedAt,
            Phase = phase,
        };
    }

    private static ClientNavigationStateObservation CreateUnavailable(
        int processId,
        DateTimeOffset processStartedAt,
        long sequence,
        long generationSequence,
        ClientLifecycleState lifecycleState,
        ClientWorldObservation world,
        string status)
    {
        return ClientNavigationStateObservation.Unavailable(
            processId,
            processStartedAt,
            sequence,
            generationSequence,
            status,
            lifecycleState,
            world.Environment,
            world.ActiveSectorNumber,
            world.CurrentSectorName);
    }

    private static bool UpdateGeneration(
        ProcessCache cache,
        ClientNavigationStateGeneration generation)
    {
        if (cache.HasGeneration &&
            cache.Generation == generation)
        {
            return false;
        }

        var previousHadClientObject =
            cache.HasGeneration &&
            cache.Generation.HasClientObject;

        if (previousHadClientObject &&
            !generation.HasClientObject)
        {
            cache.AwaitingWorldReplacement = true;
        }
        else if (generation.HasClientObject)
        {
            cache.AwaitingWorldReplacement = false;
        }

        cache.HasGeneration = true;
        cache.Generation = generation;
        cache.GenerationSequence++;
        cache.Lookup = default;
        cache.HasLookup = false;
        cache.LastTerminalWarpReason = null;
        cache.LastTerminalWarpReasonAt = null;
        cache.PreviousPrivateWarpState = null;
        cache.PreviousGlobalWarpState = null;

        return true;
    }

    private static void UpdateTerminalWarpReason(
        ProcessCache cache,
        ClientObservedAuxDataValue<int> privateWarpState,
        ClientObservedAuxDataValue<int> globalWarpState,
        DateTimeOffset now)
    {
        var beginsNewWarp =
            cache.PreviousPrivateWarpState == 0 &&
            cache.PreviousGlobalWarpState == 0 &&
            ((privateWarpState.IsAvailable &&
              privateWarpState.Value is 1 or 2) ||
             (globalWarpState.IsAvailable &&
              globalWarpState.Value is 1 or 2));

        if (beginsNewWarp)
        {
            cache.LastTerminalWarpReason = null;
            cache.LastTerminalWarpReasonAt = null;
        }

        var terminalReason =
            privateWarpState.IsAvailable &&
            privateWarpState.Value is >= 5 and <= 11
                ? privateWarpState.Value
                : globalWarpState.IsAvailable &&
                  globalWarpState.Value is >= 5 and <= 11
                    ? globalWarpState.Value
                    : (int?)null;

        if (terminalReason.HasValue)
        {
            cache.LastTerminalWarpReason = terminalReason.Value;
            cache.LastTerminalWarpReasonAt = now;
        }

        cache.PreviousPrivateWarpState = privateWarpState.IsAvailable
            ? privateWarpState.Value
            : null;

        cache.PreviousGlobalWarpState = globalWarpState.IsAvailable
            ? globalWarpState.Value
            : null;
    }

    private static ClientNavigationStatePhase DerivePhase(
        ClientLifecycleState lifecycleState,
        bool isLoading,
        bool isWorldPresent,
        bool requiredPropertiesAvailable,
        bool pathBuildStateKnown,
        bool pathBuildBusy,
        ClientObservedAuxDataValue<int> privateWarpState,
        ClientObservedAuxDataValue<int> globalWarpState)
    {
        if (lifecycleState != ClientLifecycleState.InGame)
        {
            return ClientNavigationStatePhase.Unavailable;
        }

        if (isLoading || !isWorldPresent)
        {
            return ClientNavigationStatePhase.Loading;
        }

        if (!requiredPropertiesAvailable)
        {
            return ClientNavigationStatePhase.AwaitingAuxData;
        }

        if (privateWarpState.Value == 4)
        {
            return ClientNavigationStatePhase.GateTransitionLocked;
        }

        var hasGlobalRecoveryResidue =
            privateWarpState.Value == 0 &&
            globalWarpState.Value == 3;

        if (privateWarpState.Value == 3 ||
            (globalWarpState.Value == 3 &&
             !hasGlobalRecoveryResidue))
        {
            return ClientNavigationStatePhase.WarpRecovery;
        }

        if (privateWarpState.Value == 2 ||
            globalWarpState.Value == 2)
        {
            return ClientNavigationStatePhase.Warping;
        }

        if (privateWarpState.Value == 1 ||
            globalWarpState.Value == 1)
        {
            return ClientNavigationStatePhase.WarpStarting;
        }

        if (!pathBuildStateKnown)
        {
            return ClientNavigationStatePhase.AwaitingPathState;
        }

        if (pathBuildBusy)
        {
            return ClientNavigationStatePhase.SelectingTarget;
        }

        return ClientNavigationStatePhase.Idle;
    }

    private bool TryReadEnergyFraction(
        ProcessMemoryReader memory,
        ClientAuxDataLookupSnapshot lookup,
        uint currentClientTime,
        out ClientObservedAuxDataValue<float> value)
    {
        value = default;

        if (!TryReadMarker(
                memory,
                lookup,
                EnergyPercentName,
                out var propertyAddress,
                out var stateMarker))
        {
            return false;
        }

        if (propertyAddress == 0 || stateMarker == 0)
        {
            value = new ClientObservedAuxDataValue<float>(
                propertyAddress,
                stateMarker,
                0.0f);

            return true;
        }

        if (!this.auxDataReader.TryReadDeltaInterpolatedFloatProperty(
                memory,
                lookup,
                EnergyPercentName,
                currentClientTime,
                out var sample,
                out _))
        {
            return false;
        }

        value = new ClientObservedAuxDataValue<float>(
            propertyAddress,
            stateMarker,
            sample.Value);

        return true;
    }

    private static bool TryReadInt32(
        ProcessMemoryReader memory,
        ClientAuxDataLookupSnapshot lookup,
        string propertyName,
        out ClientObservedAuxDataValue<int> value)
    {
        value = default;

        if (!TryReadMarker(
                memory,
                lookup,
                propertyName,
                out var propertyAddress,
                out var stateMarker))
        {
            return false;
        }

        if (propertyAddress == 0)
        {
            return true;
        }

        if (!TryReadUInt32(
                memory,
                propertyAddress,
                AuxDataScalarValue,
                out var rawValue))
        {
            return false;
        }

        value = new ClientObservedAuxDataValue<int>(
            propertyAddress,
            stateMarker,
            unchecked((int)rawValue));

        return true;
    }

    private static bool TryReadBoolean(
        ProcessMemoryReader memory,
        ClientAuxDataLookupSnapshot lookup,
        string propertyName,
        out ClientObservedAuxDataValue<bool> value)
    {
        value = default;

        if (!TryReadMarker(
                memory,
                lookup,
                propertyName,
                out var propertyAddress,
                out var stateMarker))
        {
            return false;
        }

        if (propertyAddress == 0)
        {
            return true;
        }

        if (!TryReadUInt32(
                memory,
                propertyAddress,
                AuxDataScalarValue,
                out var rawValue))
        {
            return false;
        }

        value = new ClientObservedAuxDataValue<bool>(
            propertyAddress,
            stateMarker,
            (rawValue & 0xff) != 0);

        return true;
    }

    private static bool TryReadFloat(
        ProcessMemoryReader memory,
        ClientAuxDataLookupSnapshot lookup,
        string propertyName,
        out ClientObservedAuxDataValue<float> value)
    {
        value = default;

        if (!TryReadMarker(
                memory,
                lookup,
                propertyName,
                out var propertyAddress,
                out var stateMarker))
        {
            return false;
        }

        if (propertyAddress == 0)
        {
            return true;
        }

        if (!TryReadUInt32(
                memory,
                propertyAddress,
                AuxDataScalarValue,
                out var rawValue))
        {
            return false;
        }

        var floatValue = BitConverter.Int32BitsToSingle(
            unchecked((int)rawValue));

        if (!float.IsFinite(floatValue))
        {
            return false;
        }

        value = new ClientObservedAuxDataValue<float>(
            propertyAddress,
            stateMarker,
            floatValue);

        return true;
    }

    private static bool TryReadUInt64Secondary(
        ProcessMemoryReader memory,
        ClientAuxDataLookupSnapshot lookup,
        string propertyName,
        out ClientObservedAuxDataValue<ulong> value)
    {
        value = default;

        if (!TryReadMarker(
                memory,
                lookup,
                propertyName,
                out var propertyAddress,
                out var stateMarker))
        {
            return false;
        }

        if (propertyAddress == 0)
        {
            return true;
        }

        if (!TryReadUInt32(
                memory,
                propertyAddress,
                AuxDataSecondaryValue,
                out var low) ||
            !TryReadUInt32(
                memory,
                propertyAddress,
                AuxDataSecondaryValue + sizeof(uint),
                out var high))
        {
            return false;
        }

        value = new ClientObservedAuxDataValue<ulong>(
            propertyAddress,
            stateMarker,
            ((ulong)high << 32) | low);

        return true;
    }

    private static bool TryReadMarker(
        ProcessMemoryReader memory,
        ClientAuxDataLookupSnapshot lookup,
        string propertyName,
        out uint propertyAddress,
        out uint stateMarker)
    {
        propertyAddress = 0;
        stateMarker = 0;

        if (!lookup.Properties.TryGetValue(
                propertyName,
                out propertyAddress) ||
            propertyAddress == 0)
        {
            return true;
        }

        return TryReadUInt32(
            memory,
            propertyAddress,
            AuxDataStateMarker,
            out stateMarker);
    }

    private static DirectWorldContext ReadDirectWorldContext(
        ProcessMemoryReader memory,
        uint clientContextAddress,
        ClientWorldObservation fallback)
    {
        var activeSectorNumber = fallback.ActiveSectorNumber;
        var presentationMode = fallback.PresentationMode;
        var sectorName = fallback.CurrentSectorName;
        var sectorNameDirectlyObserved = false;
        var activeSectorNumberKnown = TryReadUInt32(
            memory,
            clientContextAddress,
            ClientContextActiveSectorNumber,
            out var observedSectorNumber);
        var presentationModeKnown = TryReadUInt32(
            memory,
            clientContextAddress,
            ClientContextPresentationMode,
            out var observedPresentationMode);

        if (activeSectorNumberKnown)
        {
            activeSectorNumber = observedSectorNumber;
        }

        if (presentationModeKnown)
        {
            presentationMode = observedPresentationMode;
        }

        if (TryReadUInt32(
                memory,
                clientContextAddress,
                ClientContextGalaxyMap,
                out var galaxyMapAddress) &&
            galaxyMapAddress != 0 &&
            TryReadUInt32(
                memory,
                galaxyMapAddress,
                GalaxyMapCurrentSectorName,
                out var sectorNameAddress) &&
            sectorNameAddress != 0 &&
            memory.TryReadNullTerminatedLatin1String(
                sectorNameAddress,
                MaximumLocationNameLength,
                out var observedSectorName) &&
            !string.IsNullOrWhiteSpace(observedSectorName))
        {
            sectorName = observedSectorName;
            sectorNameDirectlyObserved = true;
        }

        return new DirectWorldContext(
            activeSectorNumber,
            activeSectorNumberKnown,
            sectorName,
            sectorNameDirectlyObserved,
            presentationMode,
            presentationModeKnown);
    }

    private static ClientWorldEnvironment ClassifyEnvironment(
        uint presentationMode)
    {
        return presentationMode switch
        {
            0 => ClientWorldEnvironment.Space,
            1 => ClientWorldEnvironment.Planet,
            2 => ClientWorldEnvironment.Initializing,
            3 => ClientWorldEnvironment.Movie3D,
            4 => ClientWorldEnvironment.Starbase,
            5 => ClientWorldEnvironment.GasGiant,
            6 => ClientWorldEnvironment.ScriptedMovie,
            _ => ClientWorldEnvironment.Unknown,
        };
    }

    private static bool TryReadUInt32(
        ProcessMemoryReader memory,
        uint baseAddress,
        uint offset,
        out uint value)
    {
        value = 0;

        try
        {
            return memory.TryReadUInt32(
                checked(baseAddress + offset),
                out value);
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static float? ReadTargetDistance(
        ClientTargetObservation target)
    {
        return target.IsAvailable &&
               target.HasTarget &&
               target.Distance.IsAvailable
            ? target.Distance.SurfaceDistance
            : null;
    }

    private readonly record struct DirectWorldContext(
        uint ActiveSectorNumber,
        bool ActiveSectorNumberKnown,
        string SectorName,
        bool SectorNameDirectlyObserved,
        uint PresentationMode,
        bool PresentationModeKnown);

    private sealed class ProcessCache
    {
        public bool HasGeneration { get; set; }

        public ClientNavigationStateGeneration Generation { get; set; }

        public long GenerationSequence { get; set; }

        public bool HasLookup { get; set; }

        public bool AwaitingWorldReplacement { get; set; }

        public ClientAuxDataLookupSnapshot Lookup { get; set; }

        public int? PreviousPrivateWarpState { get; set; }

        public int? PreviousGlobalWarpState { get; set; }

        public int? LastTerminalWarpReason { get; set; }

        public DateTimeOffset? LastTerminalWarpReasonAt { get; set; }
    }
}
