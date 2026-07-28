namespace Net7ClientManager.Observations.Observers;

using Net7ClientManager.Observations.Models;

internal sealed class ClientTargetObserver
{
    private const uint ImageBase = 0x00400000;

    private const uint ClientGameObjectVTableStatic =
        0x00b0070c;

    private const uint ClientGameObjectVTableRva =
        ClientGameObjectVTableStatic - ImageBase;

    private const uint StringAuxDataPropertyVTableStatic =
        0x00aeb0e4;

    private const uint StringAuxDataPropertyVTableRva =
        StringAuxDataPropertyVTableStatic - ImageBase;

    private const uint ClientContextCurrentTime = 0x10bc;

    private const uint ClientObjectAuxData = 0x88;
    private const uint ClientObjectObjectId = 0x90;
    private const uint ClientObjectType = 0x94;
    private const uint ClientObjectEmbeddedState = 0x12c;
    private const uint ClientObjectHostileAttackingState = 0x130;

    private const uint ObjectAuxDataNameProperty = 0xa0;
    private const uint AuxDataPropertyValid = 0x70;
    private const uint AuxDataPropertyValue = 0x84;

    private const int MaximumTargetNameLength = 256;

    private readonly ClientObjectResolver objectResolver =
        new();

    private readonly ClientSpatialObserver spatialObserver =
        new();

    private readonly ClientShipAuxDataObserver shipAuxDataObserver =
        new();

    private readonly ClientCorpseObserver corpseObserver =
        new();

    private readonly ClientAsteroidObserver asteroidObserver =
        new();

    public void Refresh(
        ProcessMemoryReader memory,
        ObservedClientState state)
    {
        if (!state.HasDirectClientState ||
            state.ClientContextAddress == 0)
        {
            state.Target =
                ClientTargetObservation.Unavailable(
                    "Direct SClient state is unavailable");

            return;
        }

        if (!this.objectResolver.TryReadCurrentTargetSource(
                memory,
                state.ClientContextAddress,
                out var source,
                out var error,
                out _))
        {
            state.Target =
                ClientTargetObservation.Unavailable(
                    error);

            return;
        }

        if (ClientObjectResolver.IsAbsentObjectId(
                source.TargetObjectId))
        {
            state.Target =
                ClientTargetObservation.NoTarget(
                    source.TargetObjectIdAddress);

            return;
        }

        if (!this.objectResolver.TryLookupClientObject(
                memory,
                state.ClientContextAddress,
                source.TargetObjectId,
                out var targetClientObjectAddress,
                out error,
                out _))
        {
            state.Target =
                ClientTargetObservation.Unavailable(
                    error);

            return;
        }

        var expectedClientObjectVTable = checked(
            state.ModuleBaseAddress +
            ClientGameObjectVTableRva);

        if (!memory.TryReadUInt32(
                targetClientObjectAddress,
                out var actualClientObjectVTable))
        {
            state.Target =
                ClientTargetObservation.Unavailable(
                        $"Could not read target ClientGameObject vtable at 0x{targetClientObjectAddress:X8}");

            return;
        }

        if (actualClientObjectVTable !=
            expectedClientObjectVTable)
        {
            state.Target =
                ClientTargetObservation.Unavailable(
                        $"Unexpected target ClientGameObject vtable 0x{actualClientObjectVTable:X8} at 0x{targetClientObjectAddress:X8}; expected 0x{expectedClientObjectVTable:X8}");

            return;
        }

        if (!memory.TryReadUInt32(
                targetClientObjectAddress +
                ClientObjectObjectId,
                out var observedObjectId) ||
            observedObjectId != source.TargetObjectId)
        {
            state.Target =
                ClientTargetObservation.Unavailable(
                        $"Target ClientGameObject ObjectId validation failed at 0x{targetClientObjectAddress + ClientObjectObjectId:X8}");

            return;
        }

        if (!memory.TryReadUInt32(
                targetClientObjectAddress +
                ClientObjectAuxData,
                out var targetAuxDataAddress) ||
            targetAuxDataAddress == 0)
        {
            state.Target =
                ClientTargetObservation.Unavailable(
                        $"Could not resolve target ObjectAuxData at 0x{targetClientObjectAddress + ClientObjectAuxData:X8}");

            return;
        }

        if (!TryReadByte(
                memory,
                targetClientObjectAddress +
                ClientObjectType,
                out var objectType))
        {
            state.Target =
                ClientTargetObservation.Unavailable(
                        $"Could not read target object type at 0x{targetClientObjectAddress + ClientObjectType:X8}");

            return;
        }

        if (!memory.TryReadUInt32(
                targetClientObjectAddress +
                ClientObjectEmbeddedState,
                out var embeddedStateRaw))
        {
            state.Target =
                ClientTargetObservation.Unavailable(
                        $"Could not read target embedded state at 0x{targetClientObjectAddress + ClientObjectEmbeddedState:X8}");

            return;
        }

        if (!TryReadByte(
                memory,
                targetClientObjectAddress +
                ClientObjectHostileAttackingState,
                out var hostileAttackingState))
        {
            state.Target =
                ClientTargetObservation.Unavailable(
                        $"Could not read target hostile-attacking state at 0x{targetClientObjectAddress + ClientObjectHostileAttackingState:X8}");

            return;
        }

        uint namePropertyAddress;

        try
        {
            namePropertyAddress = checked(
                targetAuxDataAddress +
                ObjectAuxDataNameProperty);
        }
        catch (OverflowException)
        {
            state.Target =
                ClientTargetObservation.Unavailable(
                    "Target name-property address overflow");

            return;
        }

        var expectedNamePropertyVTable = checked(
            state.ModuleBaseAddress +
            StringAuxDataPropertyVTableRva);

        if (!memory.TryReadUInt32(
                namePropertyAddress,
                out var actualNamePropertyVTable))
        {
            state.Target =
                ClientTargetObservation.Unavailable(
                        $"Could not read target name-property vtable at 0x{namePropertyAddress:X8}");

            return;
        }

        if (actualNamePropertyVTable !=
            expectedNamePropertyVTable)
        {
            state.Target =
                ClientTargetObservation.Unavailable(
                        $"Unexpected target name-property vtable 0x{actualNamePropertyVTable:X8} at 0x{namePropertyAddress:X8}; expected 0x{expectedNamePropertyVTable:X8}");

            return;
        }

        if (!memory.TryReadUInt32(
                namePropertyAddress +
                AuxDataPropertyValid,
                out var namePropertyValidValue))
        {
            state.Target =
                ClientTargetObservation.Unavailable(
                        $"Could not read target name-property valid state at 0x{namePropertyAddress + AuxDataPropertyValid:X8}");

            return;
        }

        if (!memory.TryReadUInt32(
                namePropertyAddress +
                AuxDataPropertyValue,
                out var nameAddress))
        {
            state.Target =
                ClientTargetObservation.Unavailable(
                        $"Could not read target name pointer at 0x{namePropertyAddress + AuxDataPropertyValue:X8}");

            return;
        }

        var namePropertyValid =
            namePropertyValidValue != 0;

        var name = "";

        if (namePropertyValid &&
            nameAddress != 0 &&
            !memory.TryReadNullTerminatedLatin1String(
                nameAddress,
                MaximumTargetNameLength,
                out name))
        {
            state.Target =
                ClientTargetObservation.Unavailable(
                        $"Could not read target name at 0x{nameAddress:X8}");

            return;
        }

        var kind =
            GetTargetKind(objectType);

        var isSelf =
            source.TargetObjectId ==
            state.LocalPlayerObjectId;

        var isGroupMember =
            state.Group.ContainsObjectId(
                source.TargetObjectId);

        var embeddedStateValue =
            unchecked((int)embeddedStateRaw);

        var relation =
            GetTargetRelation(
                kind,
                isSelf,
                isGroupMember,
                embeddedStateValue);

        var distance = this.ObserveTargetDistance(
            memory,
            state,
            source.LocalClientObjectAddress,
            targetClientObjectAddress,
            kind,
            isSelf);

        var shipAuxData = this.shipAuxDataObserver.Observe(
            memory,
            state.ModuleBaseAddress,
            state.CurrentClientTime,
            targetAuxDataAddress);

        var corpse = kind == ClientTargetKind.Corpse
            ? this.corpseObserver.Observe(
                memory,
                state.ModuleBaseAddress,
                targetAuxDataAddress)
            : ClientCorpseObservation.NotApplicable();

        var asteroid = kind == ClientTargetKind.Asteroid
            ? this.asteroidObserver.Observe(
                memory,
                state.ModuleBaseAddress,
                targetAuxDataAddress)
            : ClientAsteroidObservation.NotApplicable();

        var status =
            namePropertyValid &&
            nameAddress != 0
                ? "Available"
                : "Available; target name is not populated";

        state.Target =
            new ClientTargetObservation
            {
                IsAvailable = true,
                Status = status,
                HasTarget = true,
                TargetGameIdAddress =
                    source.TargetObjectIdAddress,
                ObjectId = source.TargetObjectId,
                ClientObjectAddress =
                    targetClientObjectAddress,
                AuxDataAddress =
                    targetAuxDataAddress,
                NamePropertyAddress =
                    namePropertyAddress,
                NameAddress = nameAddress,
                NamePropertyValid =
                    namePropertyValid,
                Name = name,
                ObjectType = objectType,
                Kind = kind,
                Relation = relation,
                EmbeddedStateValue =
                    embeddedStateValue,
                HostileAttackingState =
                    hostileAttackingState,
                IsSelf = isSelf,
                IsGroupMember =
                    isGroupMember,
                Distance = distance,
                Shield = shipAuxData.Shield,
                Hull = shipAuxData.Hull,
                Energy = shipAuxData.Energy,
                Operational = shipAuxData.Operational,
                Corpse = corpse,
                Asteroid = asteroid,
            };
    }

    private ClientTargetDistanceObservation ObserveTargetDistance(
        ProcessMemoryReader memory,
        ObservedClientState state,
        uint localClientObjectAddress,
        uint targetClientObjectAddress,
        ClientTargetKind targetKind,
        bool isSelf)
    {
        if (targetKind is not ClientTargetKind.Player and
            not ClientTargetKind.NonPlayerShip and
            not ClientTargetKind.Station and
            not ClientTargetKind.Planet and
            not ClientTargetKind.Asteroid and
            not ClientTargetKind.SectorGate and
            not ClientTargetKind.Corpse and
            not ClientTargetKind.NavigationPoint)
        {
            return ClientTargetDistanceObservation.Unavailable(
                $"Distance is not promoted yet for {targetKind} targets");
        }

        var clientTimeAddress = checked(
            state.ClientContextAddress +
            ClientContextCurrentTime);

        if (!memory.TryReadUInt32(
                clientTimeAddress,
                out var clientTime))
        {
            return ClientTargetDistanceObservation.Unavailable(
                    $"Could not read current client time at 0x{clientTimeAddress:X8}");
        }

        var local = this.spatialObserver.Observe(
            memory,
            state.ModuleBaseAddress,
            clientTime,
            localClientObjectAddress);

        if (!local.IsAvailable)
        {
            return new ClientTargetDistanceObservation
            {
                Status =
                    $"Local spatial state unavailable: {local.Status}",
                Local = local,
            };
        }

        var target = isSelf
            ? local
            : this.spatialObserver.Observe(
                memory,
                state.ModuleBaseAddress,
                clientTime,
                targetClientObjectAddress);

        if (!target.IsAvailable)
        {
            return new ClientTargetDistanceObservation
            {
                Status =
                    $"Target spatial state unavailable: {target.Status}",
                Local = local,
                Target = target,
            };
        }

        var deltaX =
            target.Position.X -
            local.Position.X;

        var deltaY =
            target.Position.Y -
            local.Position.Y;

        var deltaZ =
            target.Position.Z -
            local.Position.Z;

        var centerDistance = MathF.Sqrt(
            deltaX * deltaX +
            deltaY * deltaY +
            deltaZ * deltaZ);

        var surfaceDistance =
            centerDistance -
            (local.TargetingDistanceRadius +
             target.TargetingDistanceRadius);

        if (surfaceDistance < 0)
        {
            surfaceDistance = 0;
        }

        return new ClientTargetDistanceObservation
        {
            IsAvailable = true,
            Status = $"Available for {targetKind} targets",
            SurfaceDistance = surfaceDistance,
            Local = local,
            Target = target,
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

    private static ClientTargetRelation GetTargetRelation(
        ClientTargetKind kind,
        bool isSelf,
        bool isGroupMember,
        int embeddedStateValue)
    {
        if (isSelf)
        {
            return ClientTargetRelation.Self;
        }

        if (isGroupMember)
        {
            return ClientTargetRelation.GroupMember;
        }

        if (kind == ClientTargetKind.NonPlayerShip)
        {
            return embeddedStateValue is >= 0 and < 2
                ? ClientTargetRelation.Enemy
                : ClientTargetRelation.Friendly;
        }

        return ClientTargetRelation.Unknown;
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
}
