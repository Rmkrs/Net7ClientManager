using System.Globalization;
using Net7ClientManager.Observations.Models;

namespace Net7ClientManager.Observations.Observers;

internal sealed class ClientLootTractorObserver
{
    private const uint ImageBase = 0x00400000;

    private const uint ClientContextCurrentTime = 0x10bc;
    private const uint ClientContextLootTractorCameraTargetObjectId = 0x13b4;

    private const uint ClientGameObjectVTableStatic = 0x00b0070c;
    private const uint ClientGameObjectVTableRva =
        ClientGameObjectVTableStatic - ImageBase;

    private const uint StringAuxDataPropertyVTableStatic = 0x00aeb0e4;
    private const uint StringAuxDataPropertyVTableRva =
        StringAuxDataPropertyVTableStatic - ImageBase;

    private const uint ComponentPositionalHandlerVTableStatic = 0x00b028f0;
    private const uint ComponentPositionalHandlerVTableRva =
        ComponentPositionalHandlerVTableStatic - ImageBase;

    private const uint DirectSpatialProviderVTableStatic = 0x00b025c8;
    private const uint DirectSpatialProviderVTableRva =
        DirectSpatialProviderVTableStatic - ImageBase;

    private const uint DirectSpatialStateVTableStatic = 0x00b025fc;
    private const uint DirectSpatialStateVTableRva =
        DirectSpatialStateVTableStatic - ImageBase;

    private const uint ClientObjectVTable = 0x00;
    private const uint ClientObjectAuxData = 0x88;
    private const uint ClientObjectObjectId = 0x90;
    private const uint ClientObjectType = 0x94;
    private const uint ClientObjectComponentPositionalHandler = 0x98;

    private const uint ComponentPositionalHandlerProvider = 0x04;
    private const uint ComponentPositionalHandlerTractorObjectId = 0x08;
    private const uint ComponentPositionalHandlerTractorEffectId = 0x0c;

    private const uint DirectSpatialProviderState = 0x04;

    private const uint DirectSpatialStateTime = 0x04;
    private const uint DirectSpatialStatePositionX = 0x14;
    private const uint DirectSpatialStatePositionY = 0x24;
    private const uint DirectSpatialStatePositionZ = 0x34;
    private const uint DirectSpatialStateMotionX = 0x38;
    private const uint DirectSpatialStateMotionY = 0x3c;
    private const uint DirectSpatialStateMotionZ = 0x40;
    private const uint DirectSpatialStateSpeed = 0x44;
    private const uint DirectSpatialStateTractorSpeed = 0x48;
    private const uint DirectSpatialStateTractorOwnerObjectId = 0x4c;
    private const uint DirectSpatialStateClientContext = 0x50;
    private const uint DirectSpatialStateReadLength = 0x54;

    private const uint ObjectAuxDataNameProperty = 0xa0;
    private const uint AuxDataPropertyValid = 0x70;
    private const uint AuxDataPropertyValue = 0x84;

    private const int MaximumNameLength = 256;
    private static readonly TimeSpan RecentlyCompletedWindow =
        TimeSpan.FromSeconds(2);

    private readonly ClientObjectResolver objectResolver = new();

    private readonly Dictionary<int, ActiveLootTractorTracker> trackers =
        new();

    public void Forget(
        int processId)
    {
        this.trackers.Remove(
            processId);
    }

    public void Refresh(
        ProcessMemoryReader memory,
        ObservedClientState state)
    {
        var observedAt = DateTimeOffset.UtcNow;
        var observation = this.Observe(
            memory,
            state,
            observedAt);

        state.LootTractor = this.ApplyTracking(
            state.ProcessId,
            observation,
            observedAt);
    }

    public ClientLootTractorObservation Observe(
        ProcessMemoryReader memory,
        ObservedClientState state,
        DateTimeOffset observedAt)
    {
        if (!state.HasDirectClientState ||
            state.ModuleBaseAddress == 0 ||
            state.ClientContextAddress == 0)
        {
            return ClientLootTractorObservation.Unavailable(
                "Direct SClient state is unavailable") with
            {
                ObservedAt = observedAt,
            };
        }

        try
        {
            var clientTime = state.CurrentClientTime;

            if (clientTime == 0)
            {
                var clientTimeAddress = checked(
                    state.ClientContextAddress +
                    ClientContextCurrentTime);

                if (!memory.TryReadUInt32(
                        clientTimeAddress,
                        out clientTime))
                {
                    return ClientLootTractorObservation.Unavailable(
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"Could not read client time at 0x{clientTimeAddress:X8}")) with
                    {
                        ObservedAt = observedAt,
                    };
                }
            }

            var cameraTargetAddress = checked(
                state.ClientContextAddress +
                ClientContextLootTractorCameraTargetObjectId);

            if (!memory.TryReadUInt32(
                    cameraTargetAddress,
                    out var cameraTargetObjectId))
            {
                return ClientLootTractorObservation.Unavailable(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Could not read SClient+0x13B4 at 0x{cameraTargetAddress:X8}")) with
                {
                    ObservedAt = observedAt,
                    ClientTime = clientTime,
                };
            }

            if (ClientObjectResolver.IsAbsentObjectId(
                    cameraTargetObjectId))
            {
                return new ClientLootTractorObservation
                {
                    IsAvailable = true,
                    Status = "No camera tractor target is latched",
                    ObservedAt = observedAt,
                    ClientTime = clientTime,
                };
            }

            if (!this.objectResolver.TryLookupClientObject(
                    memory,
                    state.ClientContextAddress,
                    cameraTargetObjectId,
                    out var targetClientObjectAddress,
                    out var lookupError,
                    out _))
            {
                return new ClientLootTractorObservation
                {
                    IsAvailable = true,
                    Status = string.Create(
                        CultureInfo.InvariantCulture,
                        $"No active tractor object; SClient+0x13B4 is stale or unresolved: {lookupError}"),
                    ObservedAt = observedAt,
                    ClientTime = clientTime,
                    CameraTargetObjectId = cameraTargetObjectId,
                };
            }

            return this.ObserveResolvedCameraTarget(
                memory,
                state,
                observedAt,
                clientTime,
                cameraTargetObjectId,
                targetClientObjectAddress);
        }
        catch (OverflowException)
        {
            return ClientLootTractorObservation.Unavailable(
                "Loot tractor address calculation overflow") with
            {
                ObservedAt = observedAt,
            };
        }
    }

    private ClientLootTractorObservation ObserveResolvedCameraTarget(
        ProcessMemoryReader memory,
        ObservedClientState state,
        DateTimeOffset observedAt,
        uint clientTime,
        uint cameraTargetObjectId,
        uint targetClientObjectAddress)
    {
        if (!memory.TryReadUInt32(
                targetClientObjectAddress + ClientObjectVTable,
                out var clientObjectVTableAddress) ||
            !memory.TryReadUInt32(
                targetClientObjectAddress + ClientObjectAuxData,
                out var auxDataAddress) ||
            !memory.TryReadUInt32(
                targetClientObjectAddress + ClientObjectObjectId,
                out var objectIdReadFromClientObject) ||
            !TryReadByte(
                memory,
                targetClientObjectAddress + ClientObjectType,
                out var objectType) ||
            !memory.TryReadUInt32(
                targetClientObjectAddress + ClientObjectComponentPositionalHandler,
                out var handlerAddress))
        {
            return new ClientLootTractorObservation
            {
                IsAvailable = true,
                Status = string.Create(
                    CultureInfo.InvariantCulture,
                    $"Camera target {cameraTargetObjectId} resolved to 0x{targetClientObjectAddress:X8}, but ClientGameObject fields could not be read"),
                ObservedAt = observedAt,
                ClientTime = clientTime,
                CameraTargetObjectId = cameraTargetObjectId,
                TargetClientObjectAddress = targetClientObjectAddress,
                IsCameraTargetResolved = true,
            };
        }

        var expectedClientObjectVTable = checked(
            state.ModuleBaseAddress +
            ClientGameObjectVTableRva);

        var targetKind = GetTargetKind(
            objectType);

        var identity = this.ObserveIdentity(
            memory,
            state.ModuleBaseAddress,
            auxDataAddress);

        if (clientObjectVTableAddress != expectedClientObjectVTable)
        {
            return this.CreateResolvedInactiveObservation(
                observedAt,
                clientTime,
                cameraTargetObjectId,
                targetClientObjectAddress,
                objectIdReadFromClientObject,
                clientObjectVTableAddress,
                auxDataAddress,
                identity,
                objectType,
                targetKind,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Camera target resolved, but ClientObject vtable 0x{clientObjectVTableAddress:X8} was not the expected ClientGameObject vtable 0x{expectedClientObjectVTable:X8}"));
        }

        if (objectIdReadFromClientObject != cameraTargetObjectId)
        {
            return this.CreateResolvedInactiveObservation(
                observedAt,
                clientTime,
                cameraTargetObjectId,
                targetClientObjectAddress,
                objectIdReadFromClientObject,
                clientObjectVTableAddress,
                auxDataAddress,
                identity,
                objectType,
                targetKind,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Camera target resolved, but ClientObject ObjectId read back as {objectIdReadFromClientObject} / 0x{objectIdReadFromClientObject:X8}"));
        }

        if (handlerAddress == 0)
        {
            return this.CreateResolvedInactiveObservation(
                observedAt,
                clientTime,
                cameraTargetObjectId,
                targetClientObjectAddress,
                objectIdReadFromClientObject,
                clientObjectVTableAddress,
                auxDataAddress,
                identity,
                objectType,
                targetKind,
                "Camera target resolved, but ComponentPositionalHandler was null");
        }

        if (!memory.TryReadUInt32(
                handlerAddress,
                out var handlerVTableAddress) ||
            !memory.TryReadUInt32(
                handlerAddress + ComponentPositionalHandlerProvider,
                out var providerAddress) ||
            !memory.TryReadUInt32(
                handlerAddress + ComponentPositionalHandlerTractorObjectId,
                out var handlerTractorObjectId) ||
            !memory.TryReadUInt32(
                handlerAddress + ComponentPositionalHandlerTractorEffectId,
                out var handlerTractorEffectId))
        {
            return this.CreateResolvedInactiveObservation(
                observedAt,
                clientTime,
                cameraTargetObjectId,
                targetClientObjectAddress,
                objectIdReadFromClientObject,
                clientObjectVTableAddress,
                auxDataAddress,
                identity,
                objectType,
                targetKind,
                "Camera target resolved, but ComponentPositionalHandler fields could not be read") with
            {
                HandlerAddress = handlerAddress,
            };
        }

        var expectedHandlerVTable = checked(
            state.ModuleBaseAddress +
            ComponentPositionalHandlerVTableRva);

        if (handlerVTableAddress != expectedHandlerVTable)
        {
            return this.CreateResolvedInactiveObservation(
                observedAt,
                clientTime,
                cameraTargetObjectId,
                targetClientObjectAddress,
                objectIdReadFromClientObject,
                clientObjectVTableAddress,
                auxDataAddress,
                identity,
                objectType,
                targetKind,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Camera target resolved, but positional handler vtable 0x{handlerVTableAddress:X8} was not expected 0x{expectedHandlerVTable:X8}")) with
            {
                HandlerAddress = handlerAddress,
                HandlerVTableAddress = handlerVTableAddress,
                HandlerTractorObjectId = handlerTractorObjectId,
                HandlerTractorEffectId = handlerTractorEffectId,
            };
        }

        if (providerAddress == 0 ||
            !memory.TryReadUInt32(
                providerAddress,
                out var providerVTableAddress))
        {
            return this.CreateResolvedInactiveObservation(
                observedAt,
                clientTime,
                cameraTargetObjectId,
                targetClientObjectAddress,
                objectIdReadFromClientObject,
                clientObjectVTableAddress,
                auxDataAddress,
                identity,
                objectType,
                targetKind,
                "Camera target resolved, but spatial provider was null or unreadable") with
            {
                HandlerAddress = handlerAddress,
                HandlerVTableAddress = handlerVTableAddress,
                HandlerTractorObjectId = handlerTractorObjectId,
                HandlerTractorEffectId = handlerTractorEffectId,
                ProviderAddress = providerAddress,
            };
        }

        var expectedDirectProviderVTable = checked(
            state.ModuleBaseAddress +
            DirectSpatialProviderVTableRva);

        if (providerVTableAddress != expectedDirectProviderVTable)
        {
            return this.CreateResolvedInactiveObservation(
                observedAt,
                clientTime,
                cameraTargetObjectId,
                targetClientObjectAddress,
                objectIdReadFromClientObject,
                clientObjectVTableAddress,
                auxDataAddress,
                identity,
                objectType,
                targetKind,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Camera target resolved, but provider vtable 0x{providerVTableAddress:X8} was not DirectSpatialStateProvider 0x{expectedDirectProviderVTable:X8}")) with
            {
                HandlerAddress = handlerAddress,
                HandlerVTableAddress = handlerVTableAddress,
                HandlerTractorObjectId = handlerTractorObjectId,
                HandlerTractorEffectId = handlerTractorEffectId,
                ProviderAddress = providerAddress,
                ProviderVTableAddress = providerVTableAddress,
            };
        }

        if (!memory.TryReadUInt32(
                providerAddress + DirectSpatialProviderState,
                out var stateAddress) ||
            stateAddress == 0)
        {
            return this.CreateResolvedInactiveObservation(
                observedAt,
                clientTime,
                cameraTargetObjectId,
                targetClientObjectAddress,
                objectIdReadFromClientObject,
                clientObjectVTableAddress,
                auxDataAddress,
                identity,
                objectType,
                targetKind,
                "Camera target resolved, but DirectSpatialStateProvider had no state") with
            {
                HandlerAddress = handlerAddress,
                HandlerVTableAddress = handlerVTableAddress,
                HandlerTractorObjectId = handlerTractorObjectId,
                HandlerTractorEffectId = handlerTractorEffectId,
                ProviderAddress = providerAddress,
                ProviderVTableAddress = providerVTableAddress,
            };
        }

        if (!memory.TryReadBytes(
                stateAddress,
                checked((int)DirectSpatialStateReadLength),
                out var stateBytes))
        {
            return this.CreateResolvedInactiveObservation(
                observedAt,
                clientTime,
                cameraTargetObjectId,
                targetClientObjectAddress,
                objectIdReadFromClientObject,
                clientObjectVTableAddress,
                auxDataAddress,
                identity,
                objectType,
                targetKind,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Camera target resolved, but DirectSpatialState at 0x{stateAddress:X8} could not be read")) with
            {
                HandlerAddress = handlerAddress,
                HandlerVTableAddress = handlerVTableAddress,
                HandlerTractorObjectId = handlerTractorObjectId,
                HandlerTractorEffectId = handlerTractorEffectId,
                ProviderAddress = providerAddress,
                ProviderVTableAddress = providerVTableAddress,
                StateAddress = stateAddress,
            };
        }

        var stateVTableAddress = ReadUInt32(
            stateBytes,
            0);

        var expectedDirectStateVTable = checked(
            state.ModuleBaseAddress +
            DirectSpatialStateVTableRva);

        var stateTime = ReadUInt32(
            stateBytes,
            DirectSpatialStateTime);

        var position = new ClientSpatialPosition(
            ReadSingle(stateBytes, DirectSpatialStatePositionX),
            ReadSingle(stateBytes, DirectSpatialStatePositionY),
            ReadSingle(stateBytes, DirectSpatialStatePositionZ));

        var motion = new ClientSpatialPosition(
            ReadSingle(stateBytes, DirectSpatialStateMotionX),
            ReadSingle(stateBytes, DirectSpatialStateMotionY),
            ReadSingle(stateBytes, DirectSpatialStateMotionZ));

        var transform44 = ReadSingle(
            stateBytes,
            0x2c);

        var stateSpeed = ReadSingle(
            stateBytes,
            DirectSpatialStateSpeed);

        var tractorSpeed = ReadSingle(
            stateBytes,
            DirectSpatialStateTractorSpeed);

        var stateTractorOwnerObjectId = ReadUInt32(
            stateBytes,
            DirectSpatialStateTractorOwnerObjectId);

        var stateClientContextAddress = ReadUInt32(
            stateBytes,
            DirectSpatialStateClientContext);

        var isDirectSpatialState =
            stateVTableAddress == expectedDirectStateVTable;

        var isStateBoundToClientContext =
            stateClientContextAddress == state.ClientContextAddress;

        var isByLocalPlayer =
            state.LocalPlayerObjectId != 0 &&
            handlerTractorObjectId == state.LocalPlayerObjectId &&
            stateTractorOwnerObjectId == state.LocalPlayerObjectId;

        var isTractoring =
            isDirectSpatialState &&
            isStateBoundToClientContext &&
            isByLocalPlayer;

        var matchesCurrentTarget =
            state.Target.HasTarget &&
            state.Target.ObjectId == cameraTargetObjectId;

        var currentTargetName = matchesCurrentTarget
            ? state.Target.Name
            : "";

        var status = isTractoring
            ? "Active; camera target DirectSpatialState is owned by local player"
            : "Camera target resolved, but it is not an active local-player tractor state";

        return new ClientLootTractorObservation
        {
            IsAvailable = true,
            Status = status,
            ObservedAt = observedAt,
            ClientTime = clientTime,
            IsTractoring = isTractoring,
            IsByLocalPlayer = isByLocalPlayer,
            IsCameraTargetResolved = true,
            IsDirectSpatialState = isDirectSpatialState,
            IsStateBoundToClientContext = isStateBoundToClientContext,
            MatchesCurrentTarget = matchesCurrentTarget,
            CameraTargetObjectId = cameraTargetObjectId,
            TargetClientObjectAddress = targetClientObjectAddress,
            ObjectIdReadFromClientObject = objectIdReadFromClientObject,
            ClientObjectVTableAddress = clientObjectVTableAddress,
            AuxDataAddress = auxDataAddress,
            NamePropertyAddress = identity.NamePropertyAddress,
            NameAddress = identity.NameAddress,
            NamePropertyValid = identity.NamePropertyValid,
            Name = identity.Name,
            ObjectType = objectType,
            TargetKind = targetKind,
            HandlerAddress = handlerAddress,
            HandlerVTableAddress = handlerVTableAddress,
            ProviderAddress = providerAddress,
            ProviderVTableAddress = providerVTableAddress,
            StateAddress = stateAddress,
            StateVTableAddress = stateVTableAddress,
            StateTime = stateTime,
            Position = position,
            Motion = motion,
            Transform44 = transform44,
            StateSpeed = stateSpeed,
            TractorSpeed = tractorSpeed,
            HandlerTractorObjectId = handlerTractorObjectId,
            HandlerTractorEffectId = handlerTractorEffectId,
            StateTractorOwnerObjectId = stateTractorOwnerObjectId,
            StateClientContextAddress = stateClientContextAddress,
            IdentityStatus = identity.Status,
            CurrentTargetName = currentTargetName,
        };
    }

    private ClientLootTractorObservation CreateResolvedInactiveObservation(
        DateTimeOffset observedAt,
        uint clientTime,
        uint cameraTargetObjectId,
        uint targetClientObjectAddress,
        uint objectIdReadFromClientObject,
        uint clientObjectVTableAddress,
        uint auxDataAddress,
        LootTractorIdentity identity,
        byte objectType,
        ClientTargetKind targetKind,
        string status)
    {
        return new ClientLootTractorObservation
        {
            IsAvailable = true,
            Status = status,
            ObservedAt = observedAt,
            ClientTime = clientTime,
            CameraTargetObjectId = cameraTargetObjectId,
            TargetClientObjectAddress = targetClientObjectAddress,
            ObjectIdReadFromClientObject = objectIdReadFromClientObject,
            ClientObjectVTableAddress = clientObjectVTableAddress,
            AuxDataAddress = auxDataAddress,
            NamePropertyAddress = identity.NamePropertyAddress,
            NameAddress = identity.NameAddress,
            NamePropertyValid = identity.NamePropertyValid,
            Name = identity.Name,
            ObjectType = objectType,
            TargetKind = targetKind,
            IsCameraTargetResolved = true,
            IdentityStatus = identity.Status,
        };
    }

    private ClientLootTractorObservation ApplyTracking(
        int processId,
        ClientLootTractorObservation observation,
        DateTimeOffset observedAt)
    {
        if (!this.trackers.TryGetValue(
                processId,
                out var tracker))
        {
            tracker = new ActiveLootTractorTracker();
            this.trackers[processId] = tracker;
        }

        if (observation.IsTractoring)
        {
            var started =
                !tracker.IsActive ||
                tracker.ObjectId != observation.CameraTargetObjectId;

            if (started)
            {
                tracker.StartedAt = observedAt;
                tracker.ObjectId = observation.CameraTargetObjectId;
            }

            tracker.IsActive = true;
            tracker.LastActiveAt = observedAt;
            tracker.CompletedAt = null;
            tracker.LastItemName = observation.ItemName;
            tracker.LastEffectObjectId = observation.HandlerTractorEffectId;
            tracker.LastAuxDataAddress = observation.AuxDataAddress;
            tracker.LastTargetClientObjectAddress =
                observation.TargetClientObjectAddress;
            tracker.LastInterruptedAt = null;

            return observation with
            {
                TransitionKind = started
                    ? ClientLootTractorTransitionKind.Started
                    : ClientLootTractorTransitionKind.None,
                StartedAt = tracker.StartedAt,
                LastActiveAt = tracker.LastActiveAt,
                ActiveDuration = observedAt - tracker.StartedAt,
            };
        }

        if (tracker.IsActive)
        {
            tracker.IsActive = false;

            var transitionKind =
                !observation.IsCameraTargetResolved
                    ? ClientLootTractorTransitionKind.Completed
                    : ClientLootTractorTransitionKind.Interrupted;

            if (transitionKind == ClientLootTractorTransitionKind.Completed)
            {
                tracker.CompletedAt = observedAt;
            }
            else
            {
                tracker.LastInterruptedAt = observedAt;
            }

            return observation with
            {
                WasRecentlyCompleted =
                    transitionKind == ClientLootTractorTransitionKind.Completed,
                WasRecentlyInterrupted =
                    transitionKind == ClientLootTractorTransitionKind.Interrupted,
                TransitionKind = transitionKind,
                StartedAt = tracker.StartedAt,
                LastActiveAt = tracker.LastActiveAt,
                CompletedAt = transitionKind == ClientLootTractorTransitionKind.Completed
                    ? tracker.CompletedAt
                    : tracker.LastInterruptedAt,
                ActiveDuration = tracker.LastActiveAt - tracker.StartedAt,
                CameraTargetObjectId = tracker.ObjectId,
                HandlerTractorEffectId = tracker.LastEffectObjectId,
                AuxDataAddress = tracker.LastAuxDataAddress,
                TargetClientObjectAddress =
                    tracker.LastTargetClientObjectAddress,
                Name = tracker.LastItemName,
            };
        }

        if (tracker.CompletedAt.HasValue &&
            observedAt - tracker.CompletedAt.Value <=
            RecentlyCompletedWindow)
        {
            return observation with
            {
                WasRecentlyCompleted = true,
                TransitionKind = ClientLootTractorTransitionKind.Completed,
                StartedAt = tracker.StartedAt,
                LastActiveAt = tracker.LastActiveAt,
                CompletedAt = tracker.CompletedAt,
                ActiveDuration = tracker.LastActiveAt - tracker.StartedAt,
                CameraTargetObjectId = tracker.ObjectId,
                HandlerTractorEffectId = tracker.LastEffectObjectId,
                AuxDataAddress = tracker.LastAuxDataAddress,
                TargetClientObjectAddress =
                    tracker.LastTargetClientObjectAddress,
                Name = tracker.LastItemName,
            };
        }

        if (tracker.LastInterruptedAt.HasValue &&
            observedAt - tracker.LastInterruptedAt.Value <=
            RecentlyCompletedWindow)
        {
            return observation with
            {
                WasRecentlyInterrupted = true,
                TransitionKind = ClientLootTractorTransitionKind.Interrupted,
                StartedAt = tracker.StartedAt,
                LastActiveAt = tracker.LastActiveAt,
                CompletedAt = tracker.LastInterruptedAt,
                ActiveDuration = tracker.LastActiveAt - tracker.StartedAt,
                CameraTargetObjectId = tracker.ObjectId,
                HandlerTractorEffectId = tracker.LastEffectObjectId,
                AuxDataAddress = tracker.LastAuxDataAddress,
                TargetClientObjectAddress =
                    tracker.LastTargetClientObjectAddress,
                Name = tracker.LastItemName,
            };
        }

        return observation;
    }

    private LootTractorIdentity ObserveIdentity(
        ProcessMemoryReader memory,
        uint moduleBaseAddress,
        uint auxDataAddress)
    {
        if (auxDataAddress == 0)
        {
            return new LootTractorIdentity(
                "ObjectAuxData pointer is null");
        }

        uint namePropertyAddress;

        try
        {
            namePropertyAddress = checked(
                auxDataAddress +
                ObjectAuxDataNameProperty);
        }
        catch (OverflowException)
        {
            return new LootTractorIdentity(
                "ObjectAuxData name-property address overflow");
        }

        var expectedNamePropertyVTable = checked(
            moduleBaseAddress +
            StringAuxDataPropertyVTableRva);

        if (!memory.TryReadUInt32(
                namePropertyAddress,
                out var namePropertyVTable))
        {
            return new LootTractorIdentity(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Could not read ObjectAuxData name-property vtable at 0x{namePropertyAddress:X8}"))
            {
                NamePropertyAddress = namePropertyAddress,
            };
        }

        if (namePropertyVTable != expectedNamePropertyVTable)
        {
            return new LootTractorIdentity(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"ObjectAuxData +0xA0 vtable 0x{namePropertyVTable:X8} was not expected string-property vtable 0x{expectedNamePropertyVTable:X8}"))
            {
                NamePropertyAddress = namePropertyAddress,
            };
        }

        if (!memory.TryReadUInt32(
                namePropertyAddress + AuxDataPropertyValid,
                out var validValue) ||
            !memory.TryReadUInt32(
                namePropertyAddress + AuxDataPropertyValue,
                out var nameAddress))
        {
            return new LootTractorIdentity(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Could not read ObjectAuxData name-property state at 0x{namePropertyAddress:X8}"))
            {
                NamePropertyAddress = namePropertyAddress,
            };
        }

        var isValid =
            validValue != 0;

        if (!isValid ||
            nameAddress == 0)
        {
            return new LootTractorIdentity(
                "ObjectAuxData name property is not populated")
            {
                NamePropertyAddress = namePropertyAddress,
                NameAddress = nameAddress,
                NamePropertyValid = isValid,
            };
        }

        if (!memory.TryReadNullTerminatedLatin1String(
                nameAddress,
                MaximumNameLength,
                out var name))
        {
            return new LootTractorIdentity(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Could not read ObjectAuxData name string at 0x{nameAddress:X8}"))
            {
                NamePropertyAddress = namePropertyAddress,
                NameAddress = nameAddress,
                NamePropertyValid = true,
            };
        }

        return new LootTractorIdentity(
            string.IsNullOrWhiteSpace(name)
                ? "ObjectAuxData name string is empty"
                : "ObjectAuxData name property available")
        {
            NamePropertyAddress = namePropertyAddress,
            NameAddress = nameAddress,
            NamePropertyValid = true,
            Name = name,
        };
    }

    private static ClientTargetKind GetTargetKind(
        byte objectType)
    {
        return objectType switch
        {
            0 => ClientTargetKind.NonPlayerShip,
            1 => ClientTargetKind.Player,
            3 => ClientTargetKind.Planet,
            11 => ClientTargetKind.SectorGate,
            12 => ClientTargetKind.Station,
            25 => ClientTargetKind.Corpse,
            37 => ClientTargetKind.NavigationPoint,
            38 => ClientTargetKind.Asteroid,
            _ => ClientTargetKind.Unknown,
        };
    }

    private static bool TryReadByte(
        ProcessMemoryReader memory,
        uint address,
        out byte value)
    {
        value = 0;

        if (!memory.TryReadBytes(
                address,
                1,
                out var bytes))
        {
            return false;
        }

        value = bytes[0];
        return true;
    }

    private static uint ReadUInt32(
        byte[] bytes,
        uint offset)
    {
        return BitConverter.ToUInt32(
            bytes,
            checked((int)offset));
    }

    private static float ReadSingle(
        byte[] bytes,
        uint offset)
    {
        return BitConverter.ToSingle(
            bytes,
            checked((int)offset));
    }

    private sealed class ActiveLootTractorTracker
    {
        public bool IsActive { get; set; }

        public uint ObjectId { get; set; }

        public DateTimeOffset StartedAt { get; set; }

        public DateTimeOffset LastActiveAt { get; set; }

        public DateTimeOffset? CompletedAt { get; set; }

        public DateTimeOffset? LastInterruptedAt { get; set; }

        public string LastItemName { get; set; } = "";

        public uint LastEffectObjectId { get; set; }

        public uint LastAuxDataAddress { get; set; }

        public uint LastTargetClientObjectAddress { get; set; }
    }

    private sealed record LootTractorIdentity(
        string Status)
    {
        public uint NamePropertyAddress { get; init; }

        public uint NameAddress { get; init; }

        public bool NamePropertyValid { get; init; }

        public string Name { get; init; } = "";
    }
}
