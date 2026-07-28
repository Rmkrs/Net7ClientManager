// ReSharper disable IdentifierTypo
// ReSharper disable StringLiteralTypo
// ReSharper disable CommentTypo
namespace Net7ClientManager.Observations.Observers;

using System.Globalization;
using System.Runtime.InteropServices;
using Net7ClientManager.Observations.Models;

internal sealed class ClientSpatialObserver
{
    private const uint ImageBase = 0x00400000;

    private const uint LocalShipSpatialProviderVTableStatic =
        0x00b028a4;

    private const uint LocalShipSpatialProviderVTableRva =
        LocalShipSpatialProviderVTableStatic - ImageBase;

    private const uint RemoteShipSpatialProviderVTableStatic =
        0x00b02854;

    private const uint RemoteShipSpatialProviderVTableRva =
        RemoteShipSpatialProviderVTableStatic - ImageBase;

    private const uint FixedPositionSpatialProviderVTableStatic =
        0x00b02728;

    private const uint FixedPositionSpatialProviderVTableRva =
        FixedPositionSpatialProviderVTableStatic - ImageBase;

    private const uint PlanetSpatialProviderVTableStatic =
        0x00b02818;

    private const uint PlanetSpatialProviderVTableRva =
        PlanetSpatialProviderVTableStatic - ImageBase;

    private const uint KeyframedSpatialStateVTableStatic =
        0x00b0242c;

    private const uint KeyframedSpatialStateVTableRva =
        KeyframedSpatialStateVTableStatic - ImageBase;

    private const uint InterpolatedSpatialStateVTableStatic =
        0x00b0252c;

    private const uint InterpolatedSpatialStateVTableRva =
        InterpolatedSpatialStateVTableStatic - ImageBase;

    private const uint SimplePositionalUpdateTransitionVTableStatic =
        0x00b024f8;

    private const uint SimplePositionalUpdateTransitionVTableRva =
        SimplePositionalUpdateTransitionVTableStatic - ImageBase;

    private const uint ParentRelativeSpatialStateVTableStatic =
        0x00b02628;

    private const uint ParentRelativeSpatialStateVTableRva =
        ParentRelativeSpatialStateVTableStatic - ImageBase;

    private const uint PlanetOrbitalSpatialStateVTableStatic =
        0x00b02560;

    private const uint PlanetOrbitalSpatialStateVTableRva =
        PlanetOrbitalSpatialStateVTableStatic - ImageBase;

    private const uint SpatialKeyframeVTableStatic =
        0x00b02480;

    private const uint SpatialKeyframeVTableRva =
        SpatialKeyframeVTableStatic - ImageBase;

    private const uint RemoteSpatialTimeOffsetEnabledStatic =
        0x00b9489f;

    private const uint RemoteSpatialTimeOffsetEnabledRva =
        RemoteSpatialTimeOffsetEnabledStatic - ImageBase;

    private const uint ClientObjectSpatialProvider = 0x98;
    private const uint ClientObjectTargetingDistanceRadius = 0x9c;

    private const uint SpatialProviderCurrentState = 0x04;
    private const uint RemoteProviderTransitionStartTime = 0x08;
    private const uint RemoteProviderTransitionStartOffset = 0x0c;
    private const uint RemoteProviderTransitionEndTime = 0x10;
    private const uint RemoteProviderTransitionEndOffset = 0x14;

    private const uint FixedPositionProviderPositionX = 0x34;
    private const uint FixedPositionProviderPositionY = 0x38;
    private const uint FixedPositionProviderPositionZ = 0x3c;

    private const uint PlanetStateCachedPositionX = 0x0c;
    private const uint PlanetStateCachedPositionY = 0x10;
    private const uint PlanetStateCachedPositionZ = 0x14;

    private const uint KeyframedStatePrimaryKeyframe = 0x04;
    private const uint KeyframedStatePrimaryNextCache = 0x08;
    private const uint KeyframedStateSampleKeyframeCache = 0x0c;
    private const uint KeyframedStateAlternateNextCache = 0x10;
    private const int KeyframedSpatialStateSnapshotLength = 0x14;

    private const uint SpatialKeyframeTimestamp = 0x04;
    private const uint SpatialKeyframeTransform00 = 0x08;
    private const uint SpatialKeyframeTransform01 = 0x0c;
    private const uint SpatialKeyframeTransform02 = 0x10;
    private const uint SpatialKeyframePositionX = 0x14;
    private const uint SpatialKeyframeTransform10 = 0x18;
    private const uint SpatialKeyframeTransform11 = 0x1c;
    private const uint SpatialKeyframeTransform12 = 0x20;
    private const uint SpatialKeyframePositionY = 0x24;
    private const uint SpatialKeyframeTransform20 = 0x28;
    private const uint SpatialKeyframeTransform21 = 0x2c;
    private const uint SpatialKeyframeTransform22 = 0x30;
    private const uint SpatialKeyframePositionZ = 0x34;
    private const uint SpatialKeyframeCurrentSpeed = 0x38;
    private const uint SpatialKeyframeTargetSpeed = 0x3c;
    private const uint SpatialKeyframeAccelerationActive = 0x40;
    private const uint SpatialKeyframeAcceleration = 0x44;
    private const uint SpatialKeyframeTurnActive = 0x48;
    private const uint SpatialKeyframeTurnCurrent = 0x4c;
    private const uint SpatialKeyframeTurnStep = 0x50;
    private const uint SpatialKeyframeTurnTarget = 0x54;
    private const uint SpatialKeyframeTiltActive = 0x58;
    private const uint SpatialKeyframeTiltCurrent = 0x5c;
    private const uint SpatialKeyframeTiltStep = 0x60;
    private const uint SpatialKeyframeTiltTarget = 0x64;
    private const uint SpatialKeyframeDriftActive = 0x68;
    private const uint SpatialKeyframeDriftX = 0x6c;
    private const uint SpatialKeyframeDriftY = 0x70;
    private const uint SpatialKeyframeDriftZ = 0x74;
    private const uint SpatialKeyframeDriftDamping = 0x78;
    private const uint SpatialKeyframeTurnDampingActive = 0x7c;
    private const uint SpatialKeyframeTurnDampingValue = 0x80;
    private const uint SpatialKeyframeTurnDampingFactor = 0x84;
    private const uint SpatialKeyframeBankActive = 0x88;
    private const uint SpatialKeyframeBankValue = 0x8c;
    private const uint SpatialKeyframeBankDamping = 0x90;
    private const uint SpatialKeyframeYawActive = 0x94;
    private const uint SpatialKeyframeYawValue = 0x98;
    private const uint SpatialKeyframeYawDamping = 0x9c;
    private const int SpatialKeyframeSize = 0xa0;

    private const float SpatialMotionEpsilon = 1e-6f;
    private const float SpatialAngularCoupling = 0.00048828125f;
    private const float MaximumSpatialTilt = 1.2217305f;

    private const uint SimpleTransitionStartState = 0x04;
    private const uint SimpleTransitionEndState = 0x08;
    private const uint SimpleTransitionStartTime = 0x0c;
    private const uint SimpleTransitionEndTime = 0x10;
    private const int SimpleTransitionSnapshotLength = 0x14;

    private const uint InterpolatedStateStartState = 0x04;
    private const uint InterpolatedStateEndState = 0x08;
    private const uint InterpolatedStateStartTime = 0x0c;
    private const uint InterpolatedStateEndTime = 0x10;
    private const uint InterpolatedStateStartPosition = 0x14;
    private const uint InterpolatedStateStartOrientation = 0x2c;
    private const uint InterpolatedStateEndPosition = 0x4c;
    private const uint InterpolatedStateEndOrientation = 0x64;
    private const int InterpolatedSpatialStateSnapshotLength = 0x74;

    private const uint ParentRelativeStateClientContext = 0x04;
    private const uint ParentRelativeStateAnchorObjectId = 0x08;
    private const uint ParentRelativeStateOffsetX = 0x0c;
    private const uint ParentRelativeStateOffsetY = 0x10;
    private const uint ParentRelativeStateOffsetZ = 0x14;
    private const uint ParentRelativeStateFallbackTransform = 0x18;
    private const int ParentRelativeSpatialStateSnapshotLength = 0x48;

    private const int MaximumSpatialReadAttempts = 3;
    private const int MaximumSpatialStateDepth = 8;
    private const int MaximumSpatialKeyframeStepCount = 4096;

    private readonly ClientObjectResolver objectResolver =
        new();

    public ClientSpatialObservation Observe(
        ProcessMemoryReader memory,
        uint moduleBaseAddress,
        uint clientTime,
        uint clientObjectAddress)
    {
        if (clientObjectAddress == 0)
        {
            return ClientSpatialObservation.Unavailable(
                "ClientGameObject address is zero");
        }

        try
        {
            var lastRetryReason =
                "Spatial state changed while it was being read";

            for (var attempt = 0;
                 attempt < MaximumSpatialReadAttempts;
                 attempt++)
            {
                var outcome = this.TryObserveOnce(
                    memory,
                    moduleBaseAddress,
                    clientTime,
                    clientObjectAddress,
                    out var observation,
                    out var error);

                if (outcome == ReadOutcome.Success)
                {
                    return observation;
                }

                if (outcome == ReadOutcome.Failure)
                {
                    return ClientSpatialObservation.Unavailable(
                        error,
                        clientObjectAddress);
                }

                lastRetryReason = error;
            }

            return ClientSpatialObservation.Unavailable(
                lastRetryReason,
                clientObjectAddress);
        }
        catch (OverflowException)
        {
            return ClientSpatialObservation.Unavailable(
                "Spatial address calculation overflow",
                clientObjectAddress);
        }
    }

    private ReadOutcome TryObserveOnce(
        ProcessMemoryReader memory,
        uint moduleBaseAddress,
        uint clientTime,
        uint clientObjectAddress,
        out ClientSpatialObservation observation,
        out string error)
    {
        observation =
            ClientSpatialObservation.Unavailable(
                "Spatial observation did not complete",
                clientObjectAddress);

        error = "";

        if (!TryReadSingle(
                memory,
                checked(
                    clientObjectAddress +
                    ClientObjectTargetingDistanceRadius),
                out var targetingDistanceRadius))
        {
            error = $"Could not read targeting-distance radius at 0x{clientObjectAddress + ClientObjectTargetingDistanceRadius:X8}";

            return ReadOutcome.Failure;
        }

        if (!float.IsFinite(targetingDistanceRadius) ||
            targetingDistanceRadius < 0)
        {
            error = string.Create(
                CultureInfo.InvariantCulture, $"Invalid targeting-distance radius {targetingDistanceRadius} at 0x{clientObjectAddress + ClientObjectTargetingDistanceRadius:X8}");

            return ReadOutcome.Failure;
        }

        var spatialProviderPointerAddress = checked(
            clientObjectAddress +
            ClientObjectSpatialProvider);

        if (!memory.TryReadUInt32(
                spatialProviderPointerAddress,
                out var spatialProviderAddress) ||
            spatialProviderAddress == 0)
        {
            error = $"Could not resolve spatial provider at 0x{spatialProviderPointerAddress:X8}";

            return ReadOutcome.Failure;
        }

        if (!memory.TryReadUInt32(
                spatialProviderAddress,
                out var providerVTable))
        {
            error = $"Could not read spatial-provider vtable at 0x{spatialProviderAddress:X8}";

            return ReadOutcome.Failure;
        }

        var fixedPositionProviderVTable = checked(
            moduleBaseAddress +
            FixedPositionSpatialProviderVTableRva);

        if (providerVTable == fixedPositionProviderVTable)
        {
            return this.TryReadFixedPositionProvider(
                memory,
                clientTime,
                clientObjectAddress,
                spatialProviderPointerAddress,
                spatialProviderAddress,
                targetingDistanceRadius,
                out observation,
                out error);
        }

        var planetProviderVTable = checked(
            moduleBaseAddress +
            PlanetSpatialProviderVTableRva);

        if (providerVTable == planetProviderVTable)
        {
            return this.TryReadPlanetProvider(
                memory,
                moduleBaseAddress,
                clientTime,
                clientObjectAddress,
                spatialProviderPointerAddress,
                spatialProviderAddress,
                targetingDistanceRadius,
                out observation,
                out error);
        }

        var localProviderVTable = checked(
            moduleBaseAddress +
            LocalShipSpatialProviderVTableRva);

        var remoteProviderVTable = checked(
            moduleBaseAddress +
            RemoteShipSpatialProviderVTableRva);

        if (providerVTable != localProviderVTable &&
            providerVTable != remoteProviderVTable)
        {
            error = $"Unsupported spatial-provider vtable 0x{providerVTable:X8} at 0x{spatialProviderAddress:X8}";

            return ReadOutcome.Failure;
        }

        if (!memory.TryReadBytes(
                spatialProviderAddress,
                checked((int)RemoteProviderTransitionEndOffset + sizeof(int)),
                out var providerBytes))
        {
            error = $"Could not read ship spatial provider at 0x{spatialProviderAddress:X8}";

            return ReadOutcome.Failure;
        }

        var spatialStateAddress =
            BitConverter.ToUInt32(
                providerBytes,
                checked((int)SpatialProviderCurrentState));

        if (spatialStateAddress == 0)
        {
            error = $"Spatial provider 0x{spatialProviderAddress:X8} has no current state";

            return ReadOutcome.Failure;
        }

        ClientSpatialProviderKind providerKind;
        int timeOffset;

        if (providerVTable == localProviderVTable)
        {
            providerKind =
                ClientSpatialProviderKind.LocalShip;

            timeOffset = 0;
        }
        else
        {
            providerKind =
                ClientSpatialProviderKind.RemoteShip;

            if (!this.TryComputeRemoteTimeOffset(
                    memory,
                    moduleBaseAddress,
                    clientTime,
                    providerBytes,
                    out timeOffset,
                    out error))
            {
                return ReadOutcome.Failure;
            }
        }

        var effectiveTime = unchecked(
            (uint)(
                unchecked((int)clientTime) -
                timeOffset));

        var stateOutcome = this.TryReadSpatialState(
            memory,
            moduleBaseAddress,
            spatialStateAddress,
            effectiveTime,
            0,
            [],
            out var stateSample,
            out error);

        if (stateOutcome != ReadOutcome.Success)
        {
            return stateOutcome;
        }

        if (!memory.TryReadUInt32(
                spatialProviderPointerAddress,
                out var verifiedProviderAddress) ||
            verifiedProviderAddress != spatialProviderAddress)
        {
            error =
                "Spatial provider changed while it was being read";

            return ReadOutcome.Retry;
        }

        if (!memory.TryReadUInt32(
                checked(
                    spatialProviderAddress +
                    SpatialProviderCurrentState),
                out var verifiedStateAddress) ||
            verifiedStateAddress != spatialStateAddress)
        {
            error =
                "Current spatial state changed while it was being read";

            return ReadOutcome.Retry;
        }

        observation = new ClientSpatialObservation
        {
            IsAvailable = true,
            Status = "Available",
            ClientObjectAddress = clientObjectAddress,
            SpatialProviderAddress = spatialProviderAddress,
            SpatialStateAddress = stateSample.StateAddress,
            ProviderKind = providerKind,
            StateKind = stateSample.StateKind,
            ClientTime = clientTime,
            TimeOffset = timeOffset,
            EffectiveTime = effectiveTime,
            SampleStartTime = stateSample.SampleStartTime,
            SampleEndTime = stateSample.SampleEndTime,
            Position = stateSample.Position,
            TargetingDistanceRadius = targetingDistanceRadius,
        };

        return ReadOutcome.Success;
    }

    private ReadOutcome TryReadFixedPositionProvider(
        ProcessMemoryReader memory,
        uint clientTime,
        uint clientObjectAddress,
        uint spatialProviderPointerAddress,
        uint spatialProviderAddress,
        float targetingDistanceRadius,
        out ClientSpatialObservation observation,
        out string error)
    {
        observation =
            ClientSpatialObservation.Unavailable(
                "Fixed-position spatial observation did not complete",
                clientObjectAddress);

        error = "";

        if (!memory.TryReadBytes(
                spatialProviderAddress,
                checked((int)FixedPositionProviderPositionZ + sizeof(float)),
                out var providerBytes))
        {
            error = $"Could not read fixed-position spatial provider at 0x{spatialProviderAddress:X8}";

            return ReadOutcome.Failure;
        }

        var position = new ClientSpatialPosition(
            BitConverter.ToSingle(
                providerBytes,
                checked((int)FixedPositionProviderPositionX)),
            BitConverter.ToSingle(
                providerBytes,
                checked((int)FixedPositionProviderPositionY)),
            BitConverter.ToSingle(
                providerBytes,
                checked((int)FixedPositionProviderPositionZ)));

        if (!IsFinite(position))
        {
            error = $"Fixed-position spatial provider at 0x{spatialProviderAddress:X8} contained an invalid position";

            return ReadOutcome.Failure;
        }

        if (!memory.TryReadUInt32(
                spatialProviderPointerAddress,
                out var verifiedProviderAddress) ||
            verifiedProviderAddress != spatialProviderAddress)
        {
            error =
                "Fixed-position spatial provider changed while it was being read";

            return ReadOutcome.Retry;
        }

        observation = new ClientSpatialObservation
        {
            IsAvailable = true,
            Status = "Available from fixed-position spatial provider",
            ClientObjectAddress = clientObjectAddress,
            SpatialProviderAddress = spatialProviderAddress,
            ProviderKind = ClientSpatialProviderKind.FixedPosition,
            StateKind = ClientSpatialStateKind.Static,
            ClientTime = clientTime,
            EffectiveTime = clientTime,
            Position = position,
            TargetingDistanceRadius = targetingDistanceRadius,
        };

        return ReadOutcome.Success;
    }

    private ReadOutcome TryReadPlanetProvider(
        ProcessMemoryReader memory,
        uint moduleBaseAddress,
        uint clientTime,
        uint clientObjectAddress,
        uint spatialProviderPointerAddress,
        uint spatialProviderAddress,
        float targetingDistanceRadius,
        out ClientSpatialObservation observation,
        out string error)
    {
        observation =
            ClientSpatialObservation.Unavailable(
                "Planet spatial observation did not complete",
                clientObjectAddress);

        error = "";

        var statePointerAddress = checked(
            spatialProviderAddress +
            SpatialProviderCurrentState);

        if (!memory.TryReadUInt32(
                statePointerAddress,
                out var spatialStateAddress) ||
            spatialStateAddress == 0)
        {
            error = $"Could not resolve planet spatial state at 0x{statePointerAddress:X8}";

            return ReadOutcome.Failure;
        }

        if (!memory.TryReadBytes(
                spatialStateAddress,
                checked((int)PlanetStateCachedPositionZ + sizeof(float)),
                out var stateBytes))
        {
            error = $"Could not read planet orbital spatial state at 0x{spatialStateAddress:X8}";

            return ReadOutcome.Failure;
        }

        var expectedStateVTable = checked(
            moduleBaseAddress +
            PlanetOrbitalSpatialStateVTableRva);

        var actualStateVTable =
            BitConverter.ToUInt32(
                stateBytes,
                0);

        if (actualStateVTable != expectedStateVTable)
        {
            error = $"Unexpected planet spatial-state vtable 0x{actualStateVTable:X8} at 0x{spatialStateAddress:X8}; expected 0x{expectedStateVTable:X8}";

            return ReadOutcome.Failure;
        }

        var position = new ClientSpatialPosition(
            BitConverter.ToSingle(
                stateBytes,
                checked((int)PlanetStateCachedPositionX)),
            BitConverter.ToSingle(
                stateBytes,
                checked((int)PlanetStateCachedPositionY)),
            BitConverter.ToSingle(
                stateBytes,
                checked((int)PlanetStateCachedPositionZ)));

        if (!IsFinite(position))
        {
            error = $"Planet orbital state at 0x{spatialStateAddress:X8} contained an invalid cached position";

            return ReadOutcome.Failure;
        }

        if (!memory.TryReadUInt32(
                spatialProviderPointerAddress,
                out var verifiedProviderAddress) ||
            verifiedProviderAddress != spatialProviderAddress)
        {
            error =
                "Planet spatial provider changed while it was being read";

            return ReadOutcome.Retry;
        }

        if (!memory.TryReadUInt32(
                statePointerAddress,
                out var verifiedStateAddress) ||
            verifiedStateAddress != spatialStateAddress)
        {
            error =
                "Planet spatial state changed while it was being read";

            return ReadOutcome.Retry;
        }

        observation = new ClientSpatialObservation
        {
            IsAvailable = true,
            Status = "Available from client-sampled planet orbit position",
            ClientObjectAddress = clientObjectAddress,
            SpatialProviderAddress = spatialProviderAddress,
            SpatialStateAddress = spatialStateAddress,
            ProviderKind = ClientSpatialProviderKind.Planet,
            StateKind = ClientSpatialStateKind.PlanetOrbital,
            ClientTime = clientTime,
            EffectiveTime = clientTime,
            Position = position,
            TargetingDistanceRadius = targetingDistanceRadius,
        };

        return ReadOutcome.Success;
    }

    private bool TryComputeRemoteTimeOffset(
        ProcessMemoryReader memory,
        uint moduleBaseAddress,
        uint clientTime,
        byte[] providerBytes,
        out int timeOffset,
        out string error)
    {
        timeOffset = 0;
        error = "";

        var enabledAddress = checked(
            moduleBaseAddress +
            RemoteSpatialTimeOffsetEnabledRva);

        if (!TryReadByte(
                memory,
                enabledAddress,
                out var enabled))
        {
            error = $"Could not read remote spatial-time-offset flag at 0x{enabledAddress:X8}";

            return false;
        }

        if (enabled == 0)
        {
            return true;
        }

        var transitionStartTime =
            BitConverter.ToUInt32(
                providerBytes,
                checked((int)RemoteProviderTransitionStartTime));

        var transitionStartOffset =
            BitConverter.ToInt32(
                providerBytes,
                checked((int)RemoteProviderTransitionStartOffset));

        var transitionEndTime =
            BitConverter.ToUInt32(
                providerBytes,
                checked((int)RemoteProviderTransitionEndTime));

        var transitionEndOffset =
            BitConverter.ToInt32(
                providerBytes,
                checked((int)RemoteProviderTransitionEndOffset));

        if (clientTime <= transitionStartTime)
        {
            timeOffset = transitionStartOffset;
            return true;
        }

        if (clientTime >= transitionEndTime)
        {
            timeOffset = transitionEndOffset;
            return true;
        }

        var duration =
            transitionEndTime -
            transitionStartTime;

        if (duration == 0)
        {
            error =
                "Remote spatial-time-offset transition has zero duration";

            return false;
        }

        var elapsed =
            clientTime -
            transitionStartTime;

        var progress =
            elapsed /
            (double)duration;

        var offsetDelta = unchecked(
            transitionEndOffset -
            transitionStartOffset);

        var interpolatedDelta =
            (int)(progress * offsetDelta);

        timeOffset = unchecked(
            transitionStartOffset +
            interpolatedDelta);

        return true;
    }

    private ReadOutcome TryReadSpatialState(
        ProcessMemoryReader memory,
        uint moduleBaseAddress,
        uint spatialStateAddress,
        uint effectiveTime,
        int depth,
        HashSet<uint> visited,
        out SpatialStateSample sample,
        out string error)
    {
        sample = default;
        error = "";

        if (spatialStateAddress == 0)
        {
            error = "Spatial state address is zero";
            return ReadOutcome.Failure;
        }

        if (depth >= MaximumSpatialStateDepth)
        {
            error = string.Create(
                CultureInfo.InvariantCulture,
$"Spatial state chain exceeded {MaximumSpatialStateDepth} entries");

            return ReadOutcome.Failure;
        }

        if (!visited.Add(spatialStateAddress))
        {
            error = $"Cycle detected in spatial state chain at 0x{spatialStateAddress:X8}";

            return ReadOutcome.Failure;
        }

        if (!memory.TryReadUInt32(
                spatialStateAddress,
                out var stateVTable))
        {
            error = $"Could not read spatial-state vtable at 0x{spatialStateAddress:X8}";

            return ReadOutcome.Failure;
        }

        var keyframedVTable = checked(
            moduleBaseAddress +
            KeyframedSpatialStateVTableRva);

        if (stateVTable == keyframedVTable)
        {
            return this.TryReadKeyframedState(
                memory,
                moduleBaseAddress,
                spatialStateAddress,
                effectiveTime,
                out sample,
                out error);
        }

        var interpolatedVTable = checked(
            moduleBaseAddress +
            InterpolatedSpatialStateVTableRva);

        if (stateVTable == interpolatedVTable)
        {
            return this.TryReadInterpolatedState(
                memory,
                moduleBaseAddress,
                spatialStateAddress,
                effectiveTime,
                depth,
                visited,
                out sample,
                out error);
        }

        var simpleTransitionVTable = checked(
            moduleBaseAddress +
            SimplePositionalUpdateTransitionVTableRva);

        if (stateVTable == simpleTransitionVTable)
        {
            return this.TryReadSimplePositionalUpdateTransition(
                memory,
                moduleBaseAddress,
                spatialStateAddress,
                effectiveTime,
                depth,
                visited,
                out sample,
                out error);
        }

        var parentRelativeVTable = checked(
            moduleBaseAddress +
            ParentRelativeSpatialStateVTableRva);

        if (stateVTable == parentRelativeVTable)
        {
            return this.TryReadParentRelativeState(
                memory,
                moduleBaseAddress,
                spatialStateAddress,
                effectiveTime,
                depth,
                visited,
                out sample,
                out error);
        }

        error = $"Unsupported spatial-state vtable 0x{stateVTable:X8} at 0x{spatialStateAddress:X8}";

        return ReadOutcome.Failure;
    }

    private ReadOutcome TryReadKeyframedState(
        ProcessMemoryReader memory,
        uint moduleBaseAddress,
        uint spatialStateAddress,
        uint effectiveTime,
        out SpatialStateSample sample,
        out string error)
    {
        sample = default;
        error = "";

        if (!memory.TryReadBytes(
                spatialStateAddress,
                KeyframedSpatialStateSnapshotLength,
                out var stateBytes))
        {
            error = $"Could not read keyframed spatial state at 0x{spatialStateAddress:X8}";

            return ReadOutcome.Failure;
        }

        var primaryKeyframeAddress =
            BitConverter.ToUInt32(
                stateBytes,
                checked((int)KeyframedStatePrimaryKeyframe));

        var primaryNextCacheAddress =
            BitConverter.ToUInt32(
                stateBytes,
                checked((int)KeyframedStatePrimaryNextCache));

        var sampleKeyframeCacheAddress =
            BitConverter.ToUInt32(
                stateBytes,
                checked((int)KeyframedStateSampleKeyframeCache));

        var alternateNextCacheAddress =
            BitConverter.ToUInt32(
                stateBytes,
                checked((int)KeyframedStateAlternateNextCache));

        if (primaryKeyframeAddress == 0)
        {
            error =
                "Keyframed spatial state has no primary keyframe";

            return ReadOutcome.Failure;
        }

        SpatialKeyframeState sourceKeyframe;

        if (sampleKeyframeCacheAddress != 0)
        {
            if (!this.TryReadSpatialKeyframeState(
                    memory,
                    moduleBaseAddress,
                    sampleKeyframeCacheAddress,
                    out var cachedKeyframe,
                    out error))
            {
                return ReadOutcome.Retry;
            }

            if (cachedKeyframe.Timestamp <= effectiveTime)
            {
                sourceKeyframe = cachedKeyframe;
            }
            else if (!this.TryReadSpatialKeyframeState(
                         memory,
                         moduleBaseAddress,
                         primaryKeyframeAddress,
                         out sourceKeyframe,
                         out error))
            {
                return ReadOutcome.Retry;
            }
        }
        else if (!this.TryReadSpatialKeyframeState(
                     memory,
                     moduleBaseAddress,
                     primaryKeyframeAddress,
                     out sourceKeyframe,
                     out error))
        {
            return ReadOutcome.Retry;
        }

        // SampleKeyframedSpatialStateAtTime mutates either its cached clone
        // or the primary keyframe. The external observer mirrors that work on
        // a private byte-for-byte clone and never writes into the game.
        var sourceTimestampAtRead =
            sourceKeyframe.Timestamp;

        var currentKeyframe =
            sourceKeyframe.Clone();

        var sampledNextCacheAddress = 0u;
        var sampledNextCacheTimestamp = 0u;

        var elapsedFromSource = unchecked(
            effectiveTime -
            currentKeyframe.Timestamp);

        var remainder =
            elapsedFromSource % 100u;

        SpatialTransform transform;
        uint sampleStartTime;
        uint sampleEndTime;

        if (remainder == 0)
        {
            if (!TryAdvanceSpatialKeyframeToTime(
                    currentKeyframe,
                    effectiveTime,
                    out error))
            {
                return ReadOutcome.Failure;
            }

            transform = GetOrRepairSpatialKeyframeTransform(
                currentKeyframe);

            sampleStartTime = currentKeyframe.Timestamp;
            sampleEndTime = currentKeyframe.Timestamp;
        }
        else
        {
            var alignedTime = unchecked(
                effectiveTime - remainder);

            if (!TryAdvanceSpatialKeyframeToTime(
                    currentKeyframe,
                    alignedTime,
                    out error))
            {
                return ReadOutcome.Failure;
            }

            var nextTime = unchecked(
                alignedTime + 100u);

            if (!this.TryReadMatchingKeyframeCache(
                    memory,
                    moduleBaseAddress,
                    primaryNextCacheAddress,
                    alternateNextCacheAddress,
                    nextTime,
                    out var cachedNextKeyframe,
                    out error))
            {
                return ReadOutcome.Retry;
            }

            if (cachedNextKeyframe != null)
            {
                sampledNextCacheAddress =
                    cachedNextKeyframe.SourceAddress;

                sampledNextCacheTimestamp =
                    cachedNextKeyframe.Timestamp;
            }

            var nextKeyframe =
                cachedNextKeyframe?.Clone() ??
                currentKeyframe.Clone();

            if (cachedNextKeyframe == null &&
                !TryAdvanceSpatialKeyframeToTime(
                    nextKeyframe,
                    nextTime,
                    out error))
            {
                return ReadOutcome.Failure;
            }

            var fraction =
                remainder * 0.01f;

            transform = InterpolateSpatialTransform(
                GetOrRepairSpatialKeyframeTransform(
                    currentKeyframe),
                GetOrRepairSpatialKeyframeTransform(
                    nextKeyframe),
                fraction);

            sampleStartTime = currentKeyframe.Timestamp;
            sampleEndTime = nextKeyframe.Timestamp;
        }

        if (!IsFinite(transform))
        {
            error = $"Native keyframe sampling produced an invalid transform for state 0x{spatialStateAddress:X8}";

            return ReadOutcome.Failure;
        }

        if (!memory.TryReadBytes(
                spatialStateAddress,
                KeyframedSpatialStateSnapshotLength,
                out var verifiedStateBytes) ||
            !RelevantKeyframedStatePointersMatch(
                stateBytes,
                verifiedStateBytes,
                sampledNextCacheAddress) ||
            !memory.TryReadUInt32(
                checked(
                    sourceKeyframe.SourceAddress +
                    SpatialKeyframeTimestamp),
                out var verifiedSourceTimestamp) ||
            verifiedSourceTimestamp !=
                sourceTimestampAtRead ||
            sampledNextCacheAddress != 0 &&
            (!memory.TryReadUInt32(
                 checked(
                     sampledNextCacheAddress +
                     SpatialKeyframeTimestamp),
                 out var verifiedNextTimestamp) ||
             verifiedNextTimestamp !=
                 sampledNextCacheTimestamp))
        {
            error =
                "Keyframed spatial-state caches changed while they were being read";

            return ReadOutcome.Retry;
        }

        sample = new SpatialStateSample(
            spatialStateAddress,
            ClientSpatialStateKind.Keyframed,
            sampleStartTime,
            sampleEndTime,
            transform);

        return ReadOutcome.Success;
    }

    private ReadOutcome TryReadSimplePositionalUpdateTransition(
        ProcessMemoryReader memory,
        uint moduleBaseAddress,
        uint spatialStateAddress,
        uint effectiveTime,
        int depth,
        HashSet<uint> visited,
        out SpatialStateSample sample,
        out string error)
    {
        sample = default;
        error = "";

        if (!memory.TryReadBytes(
                spatialStateAddress,
                SimpleTransitionSnapshotLength,
                out var transitionBytes))
        {
            error = string.Create(
                CultureInfo.InvariantCulture,
                $"Could not read simple positional-update transition at 0x{spatialStateAddress:X8}");

            return ReadOutcome.Failure;
        }

        var startStateAddress =
            BitConverter.ToUInt32(
                transitionBytes,
                checked((int)SimpleTransitionStartState));

        var endStateAddress =
            BitConverter.ToUInt32(
                transitionBytes,
                checked((int)SimpleTransitionEndState));

        var startTime =
            BitConverter.ToUInt32(
                transitionBytes,
                checked((int)SimpleTransitionStartTime));

        var endTime =
            BitConverter.ToUInt32(
                transitionBytes,
                checked((int)SimpleTransitionEndTime));

        if (startStateAddress == 0)
        {
            error =
                "Simple positional-update transition has no start state";

            return ReadOutcome.Failure;
        }

        if (endStateAddress == 0)
        {
            error =
                "Simple positional-update transition has no end state";

            return ReadOutcome.Failure;
        }

        if (endTime < startTime)
        {
            error = string.Create(
                CultureInfo.InvariantCulture,
                $"Invalid simple positional-update transition interval {startTime}..{endTime}");

            return ReadOutcome.Failure;
        }

        if (effectiveTime <= startTime)
        {
            return this.TryReadSpatialState(
                memory,
                moduleBaseAddress,
                startStateAddress,
                effectiveTime,
                depth + 1,
                new HashSet<uint>(
                    visited),
                out sample,
                out error);
        }

        if (effectiveTime >= endTime ||
            endTime == startTime)
        {
            return this.TryReadSpatialState(
                memory,
                moduleBaseAddress,
                endStateAddress,
                effectiveTime,
                depth + 1,
                new HashSet<uint>(
                    visited),
                out sample,
                out error);
        }

        var startOutcome = this.TryReadSpatialState(
            memory,
            moduleBaseAddress,
            startStateAddress,
            effectiveTime,
            depth + 1,
            new HashSet<uint>(
                visited),
            out var startSample,
            out error);

        if (startOutcome != ReadOutcome.Success)
        {
            return startOutcome;
        }

        var endOutcome = this.TryReadSpatialState(
            memory,
            moduleBaseAddress,
            endStateAddress,
            effectiveTime,
            depth + 1,
            new HashSet<uint>(
                visited),
            out var endSample,
            out error);

        if (endOutcome != ReadOutcome.Success)
        {
            return endOutcome;
        }

        var fraction =
            (effectiveTime - startTime) /
            (float)(endTime - startTime);

        var transform = InterpolateSpatialTransform(
            startSample.Transform,
            endSample.Transform,
            fraction);

        if (!IsFinite(transform))
        {
            error = string.Create(
                CultureInfo.InvariantCulture,
                $"Simple positional-update transition at 0x{spatialStateAddress:X8} produced an invalid transform");

            return ReadOutcome.Failure;
        }

        if (!memory.TryReadBytes(
                spatialStateAddress,
                SimpleTransitionSnapshotLength,
                out var verifiedTransitionBytes) ||
            !transitionBytes.AsSpan().SequenceEqual(
                verifiedTransitionBytes))
        {
            error =
                "Simple positional-update transition changed while it was being read";

            return ReadOutcome.Retry;
        }

        sample = new SpatialStateSample(
            spatialStateAddress,
            ClientSpatialStateKind.SimplePositionalUpdateTransition,
            startTime,
            endTime,
            transform);

        return ReadOutcome.Success;
    }

    private ReadOutcome TryReadInterpolatedState(
        ProcessMemoryReader memory,
        uint moduleBaseAddress,
        uint spatialStateAddress,
        uint effectiveTime,
        int depth,
        HashSet<uint> visited,
        out SpatialStateSample sample,
        out string error)
    {
        sample = default;
        error = "";

        if (!memory.TryReadBytes(
                spatialStateAddress,
                InterpolatedSpatialStateSnapshotLength,
                out var stateBytes))
        {
            error = $"Could not read interpolated spatial state at 0x{spatialStateAddress:X8}";

            return ReadOutcome.Failure;
        }

        var startStateAddress =
            BitConverter.ToUInt32(
                stateBytes,
                checked((int)InterpolatedStateStartState));

        var endStateAddress =
            BitConverter.ToUInt32(
                stateBytes,
                checked((int)InterpolatedStateEndState));

        var startTime =
            BitConverter.ToUInt32(
                stateBytes,
                checked((int)InterpolatedStateStartTime));

        var endTime =
            BitConverter.ToUInt32(
                stateBytes,
                checked((int)InterpolatedStateEndTime));

        if (effectiveTime <= startTime)
        {
            if (startStateAddress == 0)
            {
                error =
                    "Interpolated spatial state has no start state";

                return ReadOutcome.Failure;
            }

            return this.TryReadSpatialState(
                memory,
                moduleBaseAddress,
                startStateAddress,
                effectiveTime,
                depth + 1,
                visited,
                out sample,
                out error);
        }

        if (effectiveTime >= endTime)
        {
            if (endStateAddress == 0)
            {
                error =
                    "Interpolated spatial state has no end state";

                return ReadOutcome.Failure;
            }

            return this.TryReadSpatialState(
                memory,
                moduleBaseAddress,
                endStateAddress,
                effectiveTime,
                depth + 1,
                visited,
                out sample,
                out error);
        }

        if (endTime <= startTime)
        {
            error = $"Invalid interpolated-state interval {startTime}..{endTime}";

            return ReadOutcome.Failure;
        }

        var startPosition = ReadPosition(
            stateBytes,
            checked((int)InterpolatedStateStartPosition));

        var endPosition = ReadPosition(
            stateBytes,
            checked((int)InterpolatedStateEndPosition));

        var startOrientation = ReadQuaternion(
            stateBytes,
            checked((int)InterpolatedStateStartOrientation));

        var endOrientation = ReadQuaternion(
            stateBytes,
            checked((int)InterpolatedStateEndOrientation));

        if (!IsFinite(startPosition) ||
            !IsFinite(endPosition) ||
            !IsFinite(startOrientation) ||
            !IsFinite(endOrientation))
        {
            error = $"Interpolated spatial state at 0x{spatialStateAddress:X8} contained an invalid cached transform";

            return ReadOutcome.Failure;
        }

        var fraction =
            (effectiveTime - startTime) /
            (float)(endTime - startTime);

        var transform = CreateSpatialTransform(
            InterpolateQuaternion(
                startOrientation,
                endOrientation,
                fraction),
            Lerp(
                startPosition,
                endPosition,
                fraction));

        if (!IsFinite(transform))
        {
            error = $"Interpolated spatial state at 0x{spatialStateAddress:X8} produced an invalid transform";

            return ReadOutcome.Failure;
        }

        if (!memory.TryReadBytes(
                spatialStateAddress,
                InterpolatedSpatialStateSnapshotLength,
                out var verifiedStateBytes) ||
            !stateBytes.AsSpan().SequenceEqual(
                verifiedStateBytes))
        {
            error =
                "Interpolated spatial state changed while it was being read";

            return ReadOutcome.Retry;
        }

        sample = new SpatialStateSample(
            spatialStateAddress,
            ClientSpatialStateKind.Interpolated,
            startTime,
            endTime,
            transform);

        return ReadOutcome.Success;
    }

    private ReadOutcome TryReadParentRelativeState(
        ProcessMemoryReader memory,
        uint moduleBaseAddress,
        uint spatialStateAddress,
        uint effectiveTime,
        int depth,
        HashSet<uint> visited,
        out SpatialStateSample sample,
        out string error)
    {
        sample = default;
        error = "";

        if (!memory.TryReadBytes(
                spatialStateAddress,
                ParentRelativeSpatialStateSnapshotLength,
                out var stateBytes))
        {
            error = $"Could not read parent-relative spatial state at 0x{spatialStateAddress:X8}";

            return ReadOutcome.Failure;
        }

        var clientContextAddress =
            BitConverter.ToUInt32(
                stateBytes,
                checked((int)ParentRelativeStateClientContext));

        var anchorObjectId =
            BitConverter.ToUInt32(
                stateBytes,
                checked((int)ParentRelativeStateAnchorObjectId));

        var localOffset = new ClientSpatialPosition(
            BitConverter.ToSingle(
                stateBytes,
                checked((int)ParentRelativeStateOffsetX)),
            BitConverter.ToSingle(
                stateBytes,
                checked((int)ParentRelativeStateOffsetY)),
            BitConverter.ToSingle(
                stateBytes,
                checked((int)ParentRelativeStateOffsetZ)));

        var fallbackTransform = ReadTransform(
            stateBytes,
            checked((int)ParentRelativeStateFallbackTransform));

        if (clientContextAddress == 0 ||
            ClientObjectResolver.IsAbsentObjectId(
                anchorObjectId))
        {
            return this.CompleteParentRelativeFallback(
                memory,
                spatialStateAddress,
                effectiveTime,
                stateBytes,
                fallbackTransform,
                out sample,
                out error);
        }

        if (!IsFinite(localOffset) ||
            !IsFinite(fallbackTransform))
        {
            error = $"Parent-relative spatial state at 0x{spatialStateAddress:X8} contained invalid offset or fallback transform data";

            return ReadOutcome.Failure;
        }

        if (!this.objectResolver.TryLookupClientObject(
                memory,
                clientContextAddress,
                anchorObjectId,
                out var anchorClientObjectAddress,
                out var lookupError,
                out _))
        {
            if (IsObjectLookupAbsence(
                    lookupError))
            {
                return this.CompleteParentRelativeFallback(
                    memory,
                    spatialStateAddress,
                    effectiveTime,
                    stateBytes,
                    fallbackTransform,
                    out sample,
                    out error);
            }

            error = $"Could not resolve parent-relative anchor object {anchorObjectId}: {lookupError}";

            return ReadOutcome.Retry;
        }

        var anchorOutcome = this.TryReadClientObjectTransformAtTime(
            memory,
            moduleBaseAddress,
            effectiveTime,
            anchorClientObjectAddress,
            depth + 1,
            visited,
            out var anchorSample,
            out error);

        if (anchorOutcome != ReadOutcome.Success)
        {
            return anchorOutcome;
        }

        var worldPosition =
            anchorSample.Transform.TransformPoint(
                localOffset);

        var transform =
            anchorSample.Transform.WithPosition(
                worldPosition);

        if (!IsFinite(transform))
        {
            error = $"Parent-relative spatial state at 0x{spatialStateAddress:X8} produced an invalid world transform";

            return ReadOutcome.Failure;
        }

        if (!memory.TryReadBytes(
                spatialStateAddress,
                ParentRelativeSpatialStateSnapshotLength,
                out var verifiedStateBytes) ||
            !stateBytes.AsSpan().SequenceEqual(
                verifiedStateBytes))
        {
            error =
                "Parent-relative spatial state changed while it was being read";

            return ReadOutcome.Retry;
        }

        if (!this.objectResolver.TryLookupClientObject(
                memory,
                clientContextAddress,
                anchorObjectId,
                out var verifiedAnchorClientObjectAddress,
                out _,
                out _) ||
            verifiedAnchorClientObjectAddress !=
                anchorClientObjectAddress)
        {
            error =
                "Parent-relative anchor object changed while it was being read";

            return ReadOutcome.Retry;
        }

        sample = new SpatialStateSample(
            spatialStateAddress,
            ClientSpatialStateKind.ParentRelative,
            anchorSample.SampleStartTime,
            anchorSample.SampleEndTime,
            transform);

        return ReadOutcome.Success;
    }

    private ReadOutcome CompleteParentRelativeFallback(
        ProcessMemoryReader memory,
        uint spatialStateAddress,
        uint effectiveTime,
        byte[] stateBytes,
        SpatialTransform fallbackTransform,
        out SpatialStateSample sample,
        out string error)
    {
        sample = default;
        error = "";

        if (!IsFinite(fallbackTransform))
        {
            error = $"Parent-relative spatial state at 0x{spatialStateAddress:X8} contained an invalid fallback transform";

            return ReadOutcome.Failure;
        }

        if (!memory.TryReadBytes(
                spatialStateAddress,
                ParentRelativeSpatialStateSnapshotLength,
                out var verifiedStateBytes) ||
            !stateBytes.AsSpan().SequenceEqual(
                verifiedStateBytes))
        {
            error =
                "Parent-relative fallback transform changed while it was being read";

            return ReadOutcome.Retry;
        }

        sample = new SpatialStateSample(
            spatialStateAddress,
            ClientSpatialStateKind.ParentRelative,
            effectiveTime,
            effectiveTime,
            fallbackTransform);

        return ReadOutcome.Success;
    }

    private ReadOutcome TryReadClientObjectTransformAtTime(
        ProcessMemoryReader memory,
        uint moduleBaseAddress,
        uint clientTime,
        uint clientObjectAddress,
        int depth,
        HashSet<uint> visited,
        out SpatialStateSample sample,
        out string error)
    {
        sample = default;
        error = "";

        if (depth >= MaximumSpatialStateDepth)
        {
            error = string.Create(
                CultureInfo.InvariantCulture,
$"Spatial object chain exceeded {MaximumSpatialStateDepth} entries");

            return ReadOutcome.Failure;
        }

        var providerPointerAddress = checked(
            clientObjectAddress +
            ClientObjectSpatialProvider);

        if (!memory.TryReadUInt32(
                providerPointerAddress,
                out var providerAddress) ||
            providerAddress == 0)
        {
            error = $"Could not resolve anchor spatial provider at 0x{providerPointerAddress:X8}";

            return ReadOutcome.Retry;
        }

        if (!memory.TryReadUInt32(
                providerAddress,
                out var providerVTable))
        {
            error = $"Could not read anchor spatial-provider vtable at 0x{providerAddress:X8}";

            return ReadOutcome.Retry;
        }

        var localProviderVTable = checked(
            moduleBaseAddress +
            LocalShipSpatialProviderVTableRva);

        var remoteProviderVTable = checked(
            moduleBaseAddress +
            RemoteShipSpatialProviderVTableRva);

        if (providerVTable != localProviderVTable &&
            providerVTable != remoteProviderVTable)
        {
            error = $"Unsupported anchor spatial-provider vtable 0x{providerVTable:X8} at 0x{providerAddress:X8}";

            return ReadOutcome.Failure;
        }

        if (!memory.TryReadBytes(
                providerAddress,
                checked((int)RemoteProviderTransitionEndOffset + sizeof(int)),
                out var providerBytes))
        {
            error = $"Could not read anchor ship spatial provider at 0x{providerAddress:X8}";

            return ReadOutcome.Retry;
        }

        var stateAddress =
            BitConverter.ToUInt32(
                providerBytes,
                checked((int)SpatialProviderCurrentState));

        if (stateAddress == 0)
        {
            error =
                "Anchor ship spatial provider has no current state";

            return ReadOutcome.Retry;
        }

        var timeOffset = 0;

        if (providerVTable == remoteProviderVTable &&
            !this.TryComputeRemoteTimeOffset(
                memory,
                moduleBaseAddress,
                clientTime,
                providerBytes,
                out timeOffset,
                out error))
        {
            return ReadOutcome.Failure;
        }

        var effectiveTime = unchecked(
            (uint)(
                unchecked((int)clientTime) -
                timeOffset));

        var outcome = this.TryReadSpatialState(
            memory,
            moduleBaseAddress,
            stateAddress,
            effectiveTime,
            depth,
            visited,
            out sample,
            out error);

        if (outcome != ReadOutcome.Success)
        {
            return outcome;
        }

        if (!memory.TryReadUInt32(
                providerPointerAddress,
                out var verifiedShipProviderAddress) ||
            verifiedShipProviderAddress != providerAddress ||
            !memory.TryReadUInt32(
                checked(
                    providerAddress +
                    SpatialProviderCurrentState),
                out var verifiedStateAddress) ||
            verifiedStateAddress != stateAddress)
        {
            error =
                "Anchor ship spatial state changed while it was being read";

            return ReadOutcome.Retry;
        }

        return ReadOutcome.Success;
    }

    private bool TryReadSpatialKeyframeState(
        ProcessMemoryReader memory,
        uint moduleBaseAddress,
        uint keyframeAddress,
        out SpatialKeyframeState keyframe,
        out string error)
    {
        keyframe = null!;
        error = "";

        if (!memory.TryReadBytes(
                keyframeAddress,
                SpatialKeyframeSize,
                out var keyframeBytes))
        {
            error = $"Could not read spatial keyframe at 0x{keyframeAddress:X8}";

            return false;
        }

        var expectedVTable = checked(
            moduleBaseAddress +
            SpatialKeyframeVTableRva);

        var actualVTable =
            BitConverter.ToUInt32(
                keyframeBytes,
                0);

        if (actualVTable != expectedVTable)
        {
            error = $"Unexpected spatial-keyframe vtable 0x{actualVTable:X8} at 0x{keyframeAddress:X8}; expected 0x{expectedVTable:X8}";

            return false;
        }

        keyframe = new SpatialKeyframeState(
            keyframeAddress,
            keyframeBytes);

        if (!IsFinite(keyframe.Position))
        {
            error = $"Spatial keyframe at 0x{keyframeAddress:X8} contained an invalid position";

            return false;
        }

        return true;
    }

    private bool TryReadMatchingKeyframeCache(
        ProcessMemoryReader memory,
        uint moduleBaseAddress,
        uint primaryCacheAddress,
        uint alternateCacheAddress,
        uint expectedTimestamp,
        out SpatialKeyframeState? keyframe,
        out string error)
    {
        keyframe = null;
        error = "";

        for (var cacheIndex = 0;
             cacheIndex < 2;
             cacheIndex++)
        {
            var cacheAddress = cacheIndex == 0
                ? primaryCacheAddress
                : alternateCacheAddress;

            if (cacheAddress == 0 ||
                cacheIndex == 1 &&
                cacheAddress == primaryCacheAddress)
            {
                continue;
            }

            if (!this.TryReadSpatialKeyframeState(
                    memory,
                    moduleBaseAddress,
                    cacheAddress,
                    out var candidate,
                    out error))
            {
                return false;
            }

            if (candidate.Timestamp ==
                expectedTimestamp)
            {
                keyframe = candidate;
                return true;
            }
        }

        return true;
    }

    private static bool TryAdvanceSpatialKeyframeToTime(
        SpatialKeyframeState keyframe,
        uint targetTime,
        out string error)
    {
        error = "";

        if (targetTime < keyframe.Timestamp)
        {
            if (!CanRewindSpatialKeyframe(keyframe))
            {
                // This is the native behavior. Complex keyframes are left at
                // their current timestamp when asked to move backwards.
                return true;
            }

            var sourceTimestamp =
                keyframe.Timestamp;

            var requiredTicks = checked(
                (int)(
                    (sourceTimestamp - targetTime + 99u) /
                    100u));

            if (requiredTicks >
                MaximumSpatialKeyframeStepCount)
            {
                error = string.Create(
                    CultureInfo.InvariantCulture,
    $"Spatial keyframe rewind requires {requiredTicks} ticks from {sourceTimestamp} toward {targetTime}, exceeding safety limit {MaximumSpatialKeyframeStepCount}");

                return false;
            }

            for (var tick = 0;
                 tick < requiredTicks &&
                 targetTime < keyframe.Timestamp;
                 tick++)
            {
                var previousTimestamp =
                    keyframe.Timestamp;

                RewindSpatialKeyframeOneTick(
                    keyframe);

                var expectedTimestamp = unchecked(
                    previousTimestamp - 100u);

                if (keyframe.Timestamp !=
                    expectedTimestamp)
                {
                    error = $"Spatial keyframe rewind did not update timestamp from {previousTimestamp} to {expectedTimestamp}; observed {keyframe.Timestamp}";

                    return false;
                }
            }

            return ValidateSampledKeyframe(
                keyframe,
                out error);
        }

        if (keyframe.Timestamp < targetTime)
        {
            var sourceTimestamp =
                keyframe.Timestamp;

            if (CanFastForwardSpatialKeyframeTimestampOnly(
                    keyframe))
            {
                keyframe.Timestamp = targetTime;

                if (keyframe.Timestamp != targetTime)
                {
                    error = $"Spatial keyframe fast-forward did not update timestamp from {sourceTimestamp} to {targetTime}; observed {keyframe.Timestamp}";

                    return false;
                }

                return ValidateSampledKeyframe(
                    keyframe,
                    out error);
            }

            var requiredTicks = checked(
                (int)(
                    (targetTime - sourceTimestamp + 99u) /
                    100u));

            if (requiredTicks >
                MaximumSpatialKeyframeStepCount)
            {
                error = string.Create(
                    CultureInfo.InvariantCulture,
    $"Spatial keyframe advance requires {requiredTicks} ticks from {sourceTimestamp} toward {targetTime}, exceeding safety limit {MaximumSpatialKeyframeStepCount}");

                return false;
            }

            for (var tick = 0;
                 tick < requiredTicks &&
                 keyframe.Timestamp < targetTime;
                 tick++)
            {
                var previousTimestamp =
                    keyframe.Timestamp;

                AdvanceSpatialKeyframeOneTick(
                    keyframe);

                var expectedTimestamp = unchecked(
                    previousTimestamp + 100u);

                if (keyframe.Timestamp !=
                    expectedTimestamp)
                {
                    error = $"Spatial keyframe advance did not update timestamp from {previousTimestamp} to {expectedTimestamp}; observed {keyframe.Timestamp}";

                    return false;
                }
            }

            if (keyframe.Timestamp < targetTime)
            {
                error = $"Spatial keyframe advance stopped at {keyframe.Timestamp} before target {targetTime}";

                return false;
            }
        }

        return ValidateSampledKeyframe(
            keyframe,
            out error);
    }

    private static bool CanFastForwardSpatialKeyframeTimestampOnly(
        SpatialKeyframeState keyframe)
    {
        return MathF.Abs(
                   keyframe.ReadSingle(
                       SpatialKeyframeCurrentSpeed)) <=
                   SpatialMotionEpsilon &&
               keyframe.ReadByte(
                   SpatialKeyframeAccelerationActive) == 0 &&
               keyframe.ReadByte(
                   SpatialKeyframeTurnActive) == 0 &&
               keyframe.ReadByte(
                   SpatialKeyframeTiltActive) == 0 &&
               keyframe.ReadByte(
                   SpatialKeyframeDriftActive) == 0 &&
               keyframe.ReadByte(
                   SpatialKeyframeTurnDampingActive) == 0 &&
               keyframe.ReadByte(
                   SpatialKeyframeBankActive) == 0 &&
               keyframe.ReadByte(
                   SpatialKeyframeYawActive) == 0;
    }

    private static bool CanRewindSpatialKeyframe(
        SpatialKeyframeState keyframe)
    {
        var driftX = keyframe.ReadSingle(
            SpatialKeyframeDriftX);

        var driftY = keyframe.ReadSingle(
            SpatialKeyframeDriftY);

        var driftZ = keyframe.ReadSingle(
            SpatialKeyframeDriftZ);

        var driftMagnitudeSquared =
            driftX * driftX +
            driftY * driftY +
            driftZ * driftZ;

        return driftMagnitudeSquared <=
                   SpatialMotionEpsilon &&
               MathF.Abs(
                   keyframe.ReadSingle(
                       SpatialKeyframeAcceleration)) <=
                   SpatialMotionEpsilon &&
               MathF.Abs(
                   keyframe.ReadSingle(
                       SpatialKeyframeCurrentSpeed)) >=
                   SpatialMotionEpsilon &&
               MathF.Abs(
                   keyframe.ReadSingle(
                       SpatialKeyframeTargetSpeed)) >=
                   SpatialMotionEpsilon;
    }

    private static void AdvanceSpatialKeyframeOneTick(
        SpatialKeyframeState keyframe)
    {
        TranslateSpatialKeyframeAlongForward(
            keyframe,
            keyframe.ReadSingle(
                SpatialKeyframeCurrentSpeed));

        if (keyframe.ReadByte(
                SpatialKeyframeAccelerationActive) != 0)
        {
            var acceleration =
                keyframe.ReadSingle(
                    SpatialKeyframeAcceleration);

            var speed =
                keyframe.ReadSingle(
                    SpatialKeyframeCurrentSpeed) +
                acceleration;

            var targetSpeed =
                keyframe.ReadSingle(
                    SpatialKeyframeTargetSpeed);

            if (acceleration <= 0.0f)
            {
                if (speed <= targetSpeed)
                {
                    speed = targetSpeed;
                }
            }
            else if (targetSpeed <= speed)
            {
                speed = targetSpeed;
            }

            keyframe.WriteSingle(
                SpatialKeyframeCurrentSpeed,
                speed);
        }

        if (keyframe.ReadByte(
                SpatialKeyframeDriftActive) != 0)
        {
            var damping =
                keyframe.ReadSingle(
                    SpatialKeyframeDriftDamping);

            var driftX =
                keyframe.ReadSingle(
                    SpatialKeyframeDriftX) *
                damping;

            var driftY =
                keyframe.ReadSingle(
                    SpatialKeyframeDriftY) *
                damping;

            var driftZ =
                keyframe.ReadSingle(
                    SpatialKeyframeDriftZ) *
                damping;

            keyframe.WriteSingle(
                SpatialKeyframeDriftX,
                driftX);

            keyframe.WriteSingle(
                SpatialKeyframeDriftY,
                driftY);

            keyframe.WriteSingle(
                SpatialKeyframeDriftZ,
                driftZ);

            if (MathF.Abs(driftX) >= SpatialMotionEpsilon ||
                MathF.Abs(driftY) >= SpatialMotionEpsilon ||
                MathF.Abs(driftZ) >= SpatialMotionEpsilon)
            {
                keyframe.WriteSingle(
                    SpatialKeyframePositionX,
                    keyframe.ReadSingle(
                        SpatialKeyframePositionX) +
                    driftX);

                keyframe.WriteSingle(
                    SpatialKeyframePositionY,
                    keyframe.ReadSingle(
                        SpatialKeyframePositionY) +
                    driftY);

                keyframe.WriteSingle(
                    SpatialKeyframePositionZ,
                    keyframe.ReadSingle(
                        SpatialKeyframePositionZ) +
                    driftZ);
            }
            else
            {
                keyframe.WriteByte(
                    SpatialKeyframeDriftActive,
                    0);
            }
        }

        if (keyframe.ReadByte(
                SpatialKeyframeTurnActive) != 0)
        {
            var difference =
                keyframe.ReadSingle(
                    SpatialKeyframeTurnTarget) -
                keyframe.ReadSingle(
                    SpatialKeyframeTurnCurrent);

            var step =
                keyframe.ReadSingle(
                    SpatialKeyframeTurnStep);

            if (MathF.Abs(difference) <
                MathF.Abs(step))
            {
                step = difference;

                keyframe.WriteSingle(
                    SpatialKeyframeTurnStep,
                    step);
            }

            if (MathF.Abs(step) <
                SpatialMotionEpsilon)
            {
                keyframe.WriteByte(
                    SpatialKeyframeTurnActive,
                    0);
            }
        }

        if (keyframe.ReadByte(
                SpatialKeyframeTurnDampingActive) != 0)
        {
            var dampingValue =
                keyframe.ReadSingle(
                    SpatialKeyframeTurnDampingFactor) *
                keyframe.ReadSingle(
                    SpatialKeyframeTurnDampingValue);

            keyframe.WriteSingle(
                SpatialKeyframeTurnDampingValue,
                dampingValue);

            if (MathF.Abs(dampingValue) <
                SpatialMotionEpsilon)
            {
                keyframe.WriteByte(
                    SpatialKeyframeTurnDampingActive,
                    0);
            }
        }

        if (keyframe.ReadByte(
                SpatialKeyframeTurnActive) != 0 ||
            keyframe.ReadByte(
                SpatialKeyframeTurnDampingActive) != 0)
        {
            var turn =
                keyframe.ReadSingle(
                    SpatialKeyframeTurnDampingValue) +
                keyframe.ReadSingle(
                    SpatialKeyframeTurnStep);

            RotateSpatialTransformFirstSecond(
                keyframe,
                turn);

            keyframe.WriteSingle(
                SpatialKeyframeTurnCurrent,
                keyframe.ReadSingle(
                    SpatialKeyframeTurnCurrent) +
                turn);
        }

        if (keyframe.ReadByte(
                SpatialKeyframeBankActive) != 0)
        {
            var bank =
                keyframe.ReadSingle(
                    SpatialKeyframeBankDamping) *
                keyframe.ReadSingle(
                    SpatialKeyframeBankValue) -
                keyframe.ReadSingle(
                    SpatialKeyframeTransform21) *
                SpatialAngularCoupling;

            keyframe.WriteSingle(
                SpatialKeyframeBankValue,
                bank);

            RotateSpatialTransformSecondThird(
                keyframe,
                bank);

            if (MathF.Abs(bank) <
                SpatialMotionEpsilon)
            {
                keyframe.WriteByte(
                    SpatialKeyframeBankActive,
                    0);
            }
        }

        if (keyframe.ReadByte(
                SpatialKeyframeYawActive) != 0)
        {
            var yaw =
                keyframe.ReadSingle(
                    SpatialKeyframeYawDamping) *
                keyframe.ReadSingle(
                    SpatialKeyframeYawValue);

            keyframe.WriteSingle(
                SpatialKeyframeYawValue,
                yaw);

            RotateSpatialTransformFirstThird(
                keyframe,
                yaw);

            if (MathF.Abs(yaw) <
                SpatialMotionEpsilon)
            {
                keyframe.WriteByte(
                    SpatialKeyframeYawActive,
                    0);
            }
        }

        if (keyframe.ReadByte(
                SpatialKeyframeTiltActive) != 0)
        {
            var currentTilt =
                keyframe.ReadSingle(
                    SpatialKeyframeTiltCurrent);

            var step =
                keyframe.ReadSingle(
                    SpatialKeyframeTiltStep);

            var difference =
                keyframe.ReadSingle(
                    SpatialKeyframeTiltTarget) -
                currentTilt;

            if (difference * step < 0.0f ||
                MathF.Abs(difference) <
                    SpatialMotionEpsilon)
            {
                step = 0.0f;
            }
            else if (MathF.Abs(difference) <
                     MathF.Abs(step))
            {
                step = difference;
            }

            if (MathF.Abs(currentTilt) <=
                MaximumSpatialTilt)
            {
                step = Math.Clamp(
                    step,
                    -MaximumSpatialTilt -
                        currentTilt,
                    MaximumSpatialTilt -
                        currentTilt);
            }
            else if (currentTilt * step > 0.0f)
            {
                step = 0.0f;
            }

            keyframe.WriteSingle(
                SpatialKeyframeTiltStep,
                step);

            if (MathF.Abs(step) >
                SpatialMotionEpsilon)
            {
                RotateSpatialTransformFirstThird(
                    keyframe,
                    step);

                keyframe.Timestamp = unchecked(
                    keyframe.Timestamp + 100u);

                keyframe.WriteSingle(
                    SpatialKeyframeTiltCurrent,
                    currentTilt + step);

                return;
            }

            keyframe.WriteByte(
                SpatialKeyframeTiltActive,
                0);
        }

        keyframe.Timestamp = unchecked(
            keyframe.Timestamp + 100u);
    }

    private static void RewindSpatialKeyframeOneTick(
        SpatialKeyframeState keyframe)
    {
        keyframe.Timestamp = unchecked(
            keyframe.Timestamp - 100u);

        if (keyframe.ReadByte(
                SpatialKeyframeTiltActive) != 0)
        {
            var tiltStep =
                keyframe.ReadSingle(
                    SpatialKeyframeTiltStep);

            RotateSpatialTransformFirstThird(
                keyframe,
                -tiltStep);

            keyframe.WriteSingle(
                SpatialKeyframeTiltCurrent,
                keyframe.ReadSingle(
                    SpatialKeyframeTiltCurrent) -
                tiltStep);
        }

        var bank = keyframe.ReadSingle(
            SpatialKeyframeBankValue);

        if (bank != 0.0f)
        {
            RotateSpatialTransformSecondThird(
                keyframe,
                -bank);

            keyframe.WriteSingle(
                SpatialKeyframeBankValue,
                (keyframe.ReadSingle(
                     SpatialKeyframeTransform21) *
                     SpatialAngularCoupling +
                 bank) /
                keyframe.ReadSingle(
                    SpatialKeyframeBankDamping));
        }

        var yaw = keyframe.ReadSingle(
            SpatialKeyframeYawValue);

        if (yaw != 0.0f)
        {
            RotateSpatialTransformFirstThird(
                keyframe,
                -yaw);

            keyframe.WriteSingle(
                SpatialKeyframeYawValue,
                (yaw -
                 keyframe.ReadSingle(
                     SpatialKeyframeTransform20) *
                 SpatialAngularCoupling) /
                keyframe.ReadSingle(
                    SpatialKeyframeYawDamping));
        }

        if (keyframe.ReadByte(
                SpatialKeyframeTurnActive) != 0 ||
            keyframe.ReadByte(
                SpatialKeyframeTurnDampingActive) != 0)
        {
            var turn =
                keyframe.ReadSingle(
                    SpatialKeyframeTurnDampingValue) +
                keyframe.ReadSingle(
                    SpatialKeyframeTurnStep);

            RotateSpatialTransformFirstSecond(
                keyframe,
                -turn);

            keyframe.WriteSingle(
                SpatialKeyframeTurnCurrent,
                keyframe.ReadSingle(
                    SpatialKeyframeTurnCurrent) -
                turn);

            if (keyframe.ReadByte(
                    SpatialKeyframeTurnDampingActive) != 0)
            {
                keyframe.WriteSingle(
                    SpatialKeyframeTurnDampingValue,
                    keyframe.ReadSingle(
                        SpatialKeyframeTurnDampingValue) /
                    keyframe.ReadSingle(
                        SpatialKeyframeTurnDampingFactor));
            }
        }

        if (keyframe.ReadByte(
                SpatialKeyframeDriftActive) != 0)
        {
            var driftX = keyframe.ReadSingle(
                SpatialKeyframeDriftX);

            var driftY = keyframe.ReadSingle(
                SpatialKeyframeDriftY);

            var driftZ = keyframe.ReadSingle(
                SpatialKeyframeDriftZ);

            keyframe.WriteSingle(
                SpatialKeyframePositionX,
                keyframe.ReadSingle(
                    SpatialKeyframePositionX) -
                driftX);

            keyframe.WriteSingle(
                SpatialKeyframePositionY,
                keyframe.ReadSingle(
                    SpatialKeyframePositionY) -
                driftY);

            keyframe.WriteSingle(
                SpatialKeyframePositionZ,
                keyframe.ReadSingle(
                    SpatialKeyframePositionZ) -
                driftZ);

            var damping = keyframe.ReadSingle(
                SpatialKeyframeDriftDamping);

            keyframe.WriteSingle(
                SpatialKeyframeDriftX,
                driftX / damping);

            keyframe.WriteSingle(
                SpatialKeyframeDriftY,
                driftY / damping);

            keyframe.WriteSingle(
                SpatialKeyframeDriftZ,
                driftZ / damping);
        }

        if (keyframe.ReadByte(
                SpatialKeyframeAccelerationActive) != 0)
        {
            keyframe.WriteSingle(
                SpatialKeyframeCurrentSpeed,
                keyframe.ReadSingle(
                    SpatialKeyframeCurrentSpeed) -
                keyframe.ReadSingle(
                    SpatialKeyframeAcceleration));
        }

        TranslateSpatialKeyframeAlongForward(
            keyframe,
            -keyframe.ReadSingle(
                SpatialKeyframeCurrentSpeed));
    }

    private static void TranslateSpatialKeyframeAlongForward(
        SpatialKeyframeState keyframe,
        float distance)
    {
        keyframe.WriteSingle(
            SpatialKeyframePositionX,
            keyframe.ReadSingle(
                SpatialKeyframePositionX) +
            distance *
            keyframe.ReadSingle(
                SpatialKeyframeTransform00));

        keyframe.WriteSingle(
            SpatialKeyframePositionY,
            keyframe.ReadSingle(
                SpatialKeyframePositionY) +
            distance *
            keyframe.ReadSingle(
                SpatialKeyframeTransform10));

        keyframe.WriteSingle(
            SpatialKeyframePositionZ,
            keyframe.ReadSingle(
                SpatialKeyframePositionZ) +
            distance *
            keyframe.ReadSingle(
                SpatialKeyframeTransform20));
    }

    private static void RotateSpatialTransformFirstSecond(
        SpatialKeyframeState keyframe,
        float angle)
    {
        var sine = MathF.Sin(angle);
        var cosine = MathF.Cos(angle);

        RotatePairStandard(
            keyframe,
            SpatialKeyframeTransform00,
            SpatialKeyframeTransform10,
            sine,
            cosine);

        RotatePairStandard(
            keyframe,
            SpatialKeyframeTransform01,
            SpatialKeyframeTransform11,
            sine,
            cosine);

        RotatePairStandard(
            keyframe,
            SpatialKeyframeTransform02,
            SpatialKeyframeTransform12,
            sine,
            cosine);
    }

    private static void RotateSpatialTransformSecondThird(
        SpatialKeyframeState keyframe,
        float angle)
    {
        var sine = MathF.Sin(angle);
        var cosine = MathF.Cos(angle);

        RotatePairReverse(
            keyframe,
            SpatialKeyframeTransform01,
            SpatialKeyframeTransform02,
            sine,
            cosine);

        RotatePairReverse(
            keyframe,
            SpatialKeyframeTransform11,
            SpatialKeyframeTransform12,
            sine,
            cosine);

        RotatePairReverse(
            keyframe,
            SpatialKeyframeTransform21,
            SpatialKeyframeTransform22,
            sine,
            cosine);
    }

    private static void RotateSpatialTransformFirstThird(
        SpatialKeyframeState keyframe,
        float angle)
    {
        var sine = MathF.Sin(angle);
        var cosine = MathF.Cos(angle);

        RotatePairStandard(
            keyframe,
            SpatialKeyframeTransform00,
            SpatialKeyframeTransform02,
            sine,
            cosine);

        RotatePairStandard(
            keyframe,
            SpatialKeyframeTransform10,
            SpatialKeyframeTransform12,
            sine,
            cosine);

        RotatePairStandard(
            keyframe,
            SpatialKeyframeTransform20,
            SpatialKeyframeTransform22,
            sine,
            cosine);
    }

    private static void RotatePairStandard(
        SpatialKeyframeState keyframe,
        uint firstOffset,
        uint secondOffset,
        float sine,
        float cosine)
    {
        var first = keyframe.ReadSingle(
            firstOffset);

        var second = keyframe.ReadSingle(
            secondOffset);

        keyframe.WriteSingle(
            firstOffset,
            first * cosine -
            second * sine);

        keyframe.WriteSingle(
            secondOffset,
            first * sine +
            second * cosine);
    }

    private static void RotatePairReverse(
        SpatialKeyframeState keyframe,
        uint firstOffset,
        uint secondOffset,
        float sine,
        float cosine)
    {
        var first = keyframe.ReadSingle(
            firstOffset);

        var second = keyframe.ReadSingle(
            secondOffset);

        keyframe.WriteSingle(
            firstOffset,
            second * sine +
            first * cosine);

        keyframe.WriteSingle(
            secondOffset,
            second * cosine -
            first * sine);
    }

    private static bool ValidateSampledKeyframe(
        SpatialKeyframeState keyframe,
        out string error)
    {
        if (IsFinite(keyframe.Position))
        {
            error = "";
            return true;
        }

        error = $"Spatial keyframe sampled from 0x{keyframe.SourceAddress:X8} produced a non-finite position";

        return false;
    }

    private static bool RelevantKeyframedStatePointersMatch(
        byte[] before,
        byte[] after,
        uint sampledNextCacheAddress)
    {
        var primaryUnchanged =
            BitConverter.ToUInt32(
                before,
                checked((int)KeyframedStatePrimaryKeyframe)) ==
            BitConverter.ToUInt32(
                after,
                checked((int)KeyframedStatePrimaryKeyframe));

        var sampleCacheUnchanged =
            BitConverter.ToUInt32(
                before,
                checked((int)KeyframedStateSampleKeyframeCache)) ==
            BitConverter.ToUInt32(
                after,
                checked((int)KeyframedStateSampleKeyframeCache));

        if (!primaryUnchanged ||
            !sampleCacheUnchanged)
        {
            return false;
        }

        if (sampledNextCacheAddress == 0)
        {
            return true;
        }

        var primaryNextAddress =
            BitConverter.ToUInt32(
                after,
                checked((int)KeyframedStatePrimaryNextCache));

        var alternateNextAddress =
            BitConverter.ToUInt32(
                after,
                checked((int)KeyframedStateAlternateNextCache));

        return primaryNextAddress ==
                   sampledNextCacheAddress ||
               alternateNextAddress ==
                   sampledNextCacheAddress;
    }

    private static ClientSpatialPosition ReadPosition(
        byte[] bytes,
        int offset)
    {
        return new ClientSpatialPosition(
            BitConverter.ToSingle(
                bytes,
                offset),
            BitConverter.ToSingle(
                bytes,
                offset + sizeof(float)),
            BitConverter.ToSingle(
                bytes,
                offset + sizeof(float) * 2));
    }

    private static SpatialQuaternion ReadQuaternion(
        byte[] bytes,
        int offset)
    {
        return new SpatialQuaternion(
            BitConverter.ToSingle(
                bytes,
                offset),
            BitConverter.ToSingle(
                bytes,
                offset + sizeof(float)),
            BitConverter.ToSingle(
                bytes,
                offset + sizeof(float) * 2),
            BitConverter.ToSingle(
                bytes,
                offset + sizeof(float) * 3));
    }

    private static SpatialTransform ReadTransform(
        byte[] bytes,
        int offset)
    {
        return new SpatialTransform(
            BitConverter.ToSingle(bytes, offset),
            BitConverter.ToSingle(bytes, offset + sizeof(float)),
            BitConverter.ToSingle(bytes, offset + sizeof(float) * 2),
            BitConverter.ToSingle(bytes, offset + sizeof(float) * 3),
            BitConverter.ToSingle(bytes, offset + sizeof(float) * 4),
            BitConverter.ToSingle(bytes, offset + sizeof(float) * 5),
            BitConverter.ToSingle(bytes, offset + sizeof(float) * 6),
            BitConverter.ToSingle(bytes, offset + sizeof(float) * 7),
            BitConverter.ToSingle(bytes, offset + sizeof(float) * 8),
            BitConverter.ToSingle(bytes, offset + sizeof(float) * 9),
            BitConverter.ToSingle(bytes, offset + sizeof(float) * 10),
            BitConverter.ToSingle(bytes, offset + sizeof(float) * 11));
    }

    private static ClientSpatialPosition Lerp(
        ClientSpatialPosition start,
        ClientSpatialPosition end,
        float fraction)
    {
        return new ClientSpatialPosition(
            (end.X - start.X) * fraction + start.X,
            (end.Y - start.Y) * fraction + start.Y,
            (end.Z - start.Z) * fraction + start.Z);
    }

    private static SpatialTransform GetOrRepairSpatialKeyframeTransform(
        SpatialKeyframeState keyframe)
    {
        var transform = keyframe.Transform;

        if (IsTransformBasisOrthonormal(
                transform))
        {
            return transform;
        }

        return RepairTransformBasisOrResetIdentity(
            transform);
    }

    private static bool IsTransformBasisOrthonormal(
        SpatialTransform transform)
    {
        var row0LengthSquared =
            transform.M00 * transform.M00 +
            transform.M01 * transform.M01 +
            transform.M02 * transform.M02;

        var row1LengthSquared =
            transform.M10 * transform.M10 +
            transform.M11 * transform.M11 +
            transform.M12 * transform.M12;

        var row2LengthSquared =
            transform.M20 * transform.M20 +
            transform.M21 * transform.M21 +
            transform.M22 * transform.M22;

        var row0Row1 =
            transform.M00 * transform.M10 +
            transform.M01 * transform.M11 +
            transform.M02 * transform.M12;

        var row1Row2 =
            transform.M10 * transform.M20 +
            transform.M11 * transform.M21 +
            transform.M12 * transform.M22;

        var row0Row2 =
            transform.M00 * transform.M20 +
            transform.M01 * transform.M21 +
            transform.M02 * transform.M22;

        return MathF.Abs(row0Row1) <= 0.0001f &&
               MathF.Abs(row1Row2) <= 0.0001f &&
               MathF.Abs(row0Row2) <= 0.0001f &&
               MathF.Abs(row0LengthSquared - 1.0f) <= 0.0001f &&
               MathF.Abs(row1LengthSquared - 1.0f) <= 0.0001f &&
               MathF.Abs(row2LengthSquared - 1.0f) <= 0.0001f;
    }

    private static SpatialTransform RepairTransformBasisOrResetIdentity(
        SpatialTransform transform)
    {
        var row0X = transform.M00;
        var row0Y = transform.M01;
        var row0Z = transform.M02;

        var row2X =
            transform.M12 * row0Y -
            transform.M11 * row0Z;

        var row2Y =
            transform.M10 * row0Z -
            transform.M12 * row0X;

        var row2Z =
            transform.M11 * row0X -
            transform.M10 * row0Y;

        var row1X =
            row2Y * row0Z -
            row2Z * row0Y;

        var row1Y =
            row2Z * row0X -
            row2X * row0Z;

        var row1Z =
            row2X * row0Y -
            row2Y * row0X;

        var row0Length = MathF.Sqrt(
            row0X * row0X +
            row0Y * row0Y +
            row0Z * row0Z);

        var row1Length = MathF.Sqrt(
            row1X * row1X +
            row1Y * row1Y +
            row1Z * row1Z);

        var row2Length = MathF.Sqrt(
            row2X * row2X +
            row2Y * row2Y +
            row2Z * row2Z);

        if (row0Length < 0.0001f ||
            row1Length < 0.0001f ||
            row2Length < 0.0001f)
        {
            return SpatialTransform.Identity.WithPosition(
                transform.Position);
        }

        var inverseRow0Length = 1.0f / row0Length;
        var inverseRow1Length = 1.0f / row1Length;
        var inverseRow2Length = 1.0f / row2Length;

        return new SpatialTransform(
            row0X * inverseRow0Length,
            row0Y * inverseRow0Length,
            row0Z * inverseRow0Length,
            transform.M03,
            row1X * inverseRow1Length,
            row1Y * inverseRow1Length,
            row1Z * inverseRow1Length,
            transform.M13,
            row2X * inverseRow2Length,
            row2Y * inverseRow2Length,
            row2Z * inverseRow2Length,
            transform.M23);
    }

    private static SpatialTransform InterpolateSpatialTransform(
        SpatialTransform start,
        SpatialTransform end,
        float fraction)
    {
        return CreateSpatialTransform(
            InterpolateQuaternion(
                ExtractOrientationQuaternion(
                    start),
                ExtractOrientationQuaternion(
                    end),
                fraction),
            Lerp(
                start.Position,
                end.Position,
                fraction));
    }

    private static SpatialQuaternion ExtractOrientationQuaternion(
        SpatialTransform transform)
    {
        var trace =
            transform.M00 +
            transform.M11 +
            transform.M22;

        SpatialQuaternion result;

        if (trace > 0.0f)
        {
            var root = MathF.Sqrt(
                trace + 1.0f);

            var w = root * 0.5f;
            var scale = root != 0.0f
                ? 0.5f / root
                : 0.0f;

            result = new SpatialQuaternion(
                (transform.M21 - transform.M12) * scale,
                (transform.M02 - transform.M20) * scale,
                (transform.M10 - transform.M01) * scale,
                w);
        }
        else if (transform.M00 >= transform.M11 &&
                 transform.M00 >= transform.M22)
        {
            var root = MathF.Sqrt(
                transform.M00 -
                transform.M11 -
                transform.M22 +
                1.0f);

            var x = root * 0.5f;
            var scale = root != 0.0f
                ? 0.5f / root
                : 0.0f;

            result = new SpatialQuaternion(
                x,
                (transform.M01 + transform.M10) * scale,
                (transform.M02 + transform.M20) * scale,
                (transform.M21 - transform.M12) * scale);
        }
        else if (transform.M11 >= transform.M22)
        {
            var root = MathF.Sqrt(
                transform.M11 -
                transform.M22 -
                transform.M00 +
                1.0f);

            var y = root * 0.5f;
            var scale = root != 0.0f
                ? 0.5f / root
                : 0.0f;

            result = new SpatialQuaternion(
                (transform.M01 + transform.M10) * scale,
                y,
                (transform.M12 + transform.M21) * scale,
                (transform.M02 - transform.M20) * scale);
        }
        else
        {
            var root = MathF.Sqrt(
                transform.M22 -
                transform.M00 -
                transform.M11 +
                1.0f);

            var z = root * 0.5f;
            var scale = root != 0.0f
                ? 0.5f / root
                : 0.0f;

            result = new SpatialQuaternion(
                (transform.M02 + transform.M20) * scale,
                (transform.M12 + transform.M21) * scale,
                z,
                (transform.M10 - transform.M01) * scale);
        }

        return NormalizeQuaternion(
            result);
    }

    private static SpatialQuaternion InterpolateQuaternion(
        SpatialQuaternion start,
        SpatialQuaternion end,
        float fraction)
    {
        start = NormalizeQuaternion(start);
        end = NormalizeQuaternion(end);

        var dot =
            start.X * end.X +
            start.Y * end.Y +
            start.Z * end.Z +
            start.W * end.W;

        if (dot < 0.0f)
        {
            end = new SpatialQuaternion(
                -end.X,
                -end.Y,
                -end.Z,
                -end.W);

            dot = -dot;
        }

        if (dot > 0.9995f)
        {
            return NormalizeQuaternion(
                new SpatialQuaternion(
                    start.X +
                        (end.X - start.X) * fraction,
                    start.Y +
                        (end.Y - start.Y) * fraction,
                    start.Z +
                        (end.Z - start.Z) * fraction,
                    start.W +
                        (end.W - start.W) * fraction));
        }

        dot = Math.Clamp(
            dot,
            -1.0f,
            1.0f);

        var angle = MathF.Acos(dot);
        var sine = MathF.Sin(angle);

        if (MathF.Abs(sine) <
            SpatialMotionEpsilon)
        {
            return start;
        }

        var startWeight =
            MathF.Sin((1.0f - fraction) * angle) /
            sine;

        var endWeight =
            MathF.Sin(fraction * angle) /
            sine;

        return NormalizeQuaternion(
            new SpatialQuaternion(
                start.X * startWeight +
                    end.X * endWeight,
                start.Y * startWeight +
                    end.Y * endWeight,
                start.Z * startWeight +
                    end.Z * endWeight,
                start.W * startWeight +
                    end.W * endWeight));
    }

    private static SpatialQuaternion NormalizeQuaternion(
        SpatialQuaternion value)
    {
        var magnitudeSquared =
            value.X * value.X +
            value.Y * value.Y +
            value.Z * value.Z +
            value.W * value.W;

        if (magnitudeSquared <=
            SpatialMotionEpsilon)
        {
            return new SpatialQuaternion(
                0.0f,
                0.0f,
                0.0f,
                1.0f);
        }

        var inverseMagnitude =
            1.0f /
            MathF.Sqrt(magnitudeSquared);

        return new SpatialQuaternion(
            value.X * inverseMagnitude,
            value.Y * inverseMagnitude,
            value.Z * inverseMagnitude,
            value.W * inverseMagnitude);
    }

    private static SpatialTransform CreateSpatialTransform(
        SpatialQuaternion orientation,
        ClientSpatialPosition position)
    {
        orientation = NormalizeQuaternion(
            orientation);

        var x2 = orientation.X + orientation.X;
        var y2 = orientation.Y + orientation.Y;
        var z2 = orientation.Z + orientation.Z;
        var xx = orientation.X * x2;
        var xy = orientation.X * y2;
        var xz = orientation.X * z2;
        var yy = orientation.Y * y2;
        var yz = orientation.Y * z2;
        var zz = orientation.Z * z2;
        var wx = orientation.W * x2;
        var wy = orientation.W * y2;
        var wz = orientation.W * z2;

        return new SpatialTransform(
            1.0f - yy - zz,
            xy - wz,
            xz + wy,
            position.X,
            xy + wz,
            1.0f - xx - zz,
            yz - wx,
            position.Y,
            xz - wy,
            yz + wx,
            1.0f - xx - yy,
            position.Z);
    }

    private static bool IsFinite(
        ClientSpatialPosition position)
    {
        return float.IsFinite(position.X) &&
               float.IsFinite(position.Y) &&
               float.IsFinite(position.Z);
    }

    private static bool IsFinite(
        SpatialQuaternion value)
    {
        return float.IsFinite(value.X) &&
               float.IsFinite(value.Y) &&
               float.IsFinite(value.Z) &&
               float.IsFinite(value.W);
    }

    private static bool IsFinite(
        SpatialTransform transform)
    {
        return float.IsFinite(transform.M00) &&
               float.IsFinite(transform.M01) &&
               float.IsFinite(transform.M02) &&
               float.IsFinite(transform.M03) &&
               float.IsFinite(transform.M10) &&
               float.IsFinite(transform.M11) &&
               float.IsFinite(transform.M12) &&
               float.IsFinite(transform.M13) &&
               float.IsFinite(transform.M20) &&
               float.IsFinite(transform.M21) &&
               float.IsFinite(transform.M22) &&
               float.IsFinite(transform.M23);
    }

    private static bool IsObjectLookupAbsence(
        string error)
    {
        return error.Contains(
                   "not present",
                   StringComparison.OrdinalIgnoreCase) ||
               error.Contains(
                   "not found",
                   StringComparison.OrdinalIgnoreCase) ||
               error.Contains(
                   "absent",
                   StringComparison.OrdinalIgnoreCase) ||
               error.Contains(
                   "null ClientObject",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryReadSingle(
        ProcessMemoryReader memory,
        uint address,
        out float value)
    {
        value = 0;

        if (!memory.TryReadBytes(
                address,
                sizeof(float),
                out var bytes))
        {
            return false;
        }

        value = BitConverter.ToSingle(
            bytes,
            0);

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
                1,
                out var bytes))
        {
            return false;
        }

        value = bytes[0];
        return true;
    }

    private enum ReadOutcome
    {
        Success,
        Retry,
        Failure,
    }

    private sealed class SpatialKeyframeState(
        uint sourceAddress,
        byte[] bytes)
    {
        public uint SourceAddress { get; } = sourceAddress;

        public uint Timestamp
        {
            get => BitConverter.ToUInt32(
                bytes,
                checked((int)SpatialKeyframeTimestamp));

            set
            {
                var offset = checked(
                    (int)SpatialKeyframeTimestamp);

                bytes[offset] =
                    (byte)value;

                bytes[offset + 1] =
                    (byte)(value >> 8);

                bytes[offset + 2] =
                    (byte)(value >> 16);

                bytes[offset + 3] =
                    (byte)(value >> 24);
            }
        }

        public ClientSpatialPosition Position =>
            new(
                this.ReadSingle(
                    SpatialKeyframePositionX),
                this.ReadSingle(
                    SpatialKeyframePositionY),
                this.ReadSingle(
                    SpatialKeyframePositionZ));

        public SpatialTransform Transform =>
            new(
                this.ReadSingle(
                    SpatialKeyframeTransform00),
                this.ReadSingle(
                    SpatialKeyframeTransform01),
                this.ReadSingle(
                    SpatialKeyframeTransform02),
                this.ReadSingle(
                    SpatialKeyframePositionX),
                this.ReadSingle(
                    SpatialKeyframeTransform10),
                this.ReadSingle(
                    SpatialKeyframeTransform11),
                this.ReadSingle(
                    SpatialKeyframeTransform12),
                this.ReadSingle(
                    SpatialKeyframePositionY),
                this.ReadSingle(
                    SpatialKeyframeTransform20),
                this.ReadSingle(
                    SpatialKeyframeTransform21),
                this.ReadSingle(
                    SpatialKeyframeTransform22),
                this.ReadSingle(
                    SpatialKeyframePositionZ));

        public byte ReadByte(
            uint offset)
        {
            return bytes[
                checked((int)offset)];
        }

        public void WriteByte(
            uint offset,
            byte value)
        {
            bytes[
                checked((int)offset)] = value;
        }

        public float ReadSingle(
            uint offset)
        {
            return BitConverter.ToSingle(
                bytes,
                checked((int)offset));
        }

        public void WriteSingle(
            uint offset,
            float value)
        {
            BitConverter.TryWriteBytes(
                bytes.AsSpan(
                    checked((int)offset),
                    sizeof(float)),
                value);
        }

        public SpatialKeyframeState Clone()
        {
            return new SpatialKeyframeState(
                this.SourceAddress,
                (byte[])bytes.Clone());
        }
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct SpatialStateSample(
        uint StateAddress,
        ClientSpatialStateKind StateKind,
        uint SampleStartTime,
        uint SampleEndTime,
        SpatialTransform Transform)
    {
        public ClientSpatialPosition Position =>
            this.Transform.Position;
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct SpatialQuaternion(
        float X,
        float Y,
        float Z,
        float W);

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct SpatialTransform(
        float M00,
        float M01,
        float M02,
        float M03,
        float M10,
        float M11,
        float M12,
        float M13,
        float M20,
        float M21,
        float M22,
        float M23)
    {
        public static SpatialTransform Identity =>
            new(
                1.0f,
                0.0f,
                0.0f,
                0.0f,
                0.0f,
                1.0f,
                0.0f,
                0.0f,
                0.0f,
                0.0f,
                1.0f,
                0.0f);

        public ClientSpatialPosition Position =>
            new(
                this.M03,
                this.M13,
                this.M23);

        public ClientSpatialPosition TransformPoint(
            ClientSpatialPosition local)
        {
            return new ClientSpatialPosition(
                this.M00 * local.X +
                    this.M01 * local.Y +
                    this.M02 * local.Z +
                    this.M03,
                this.M10 * local.X +
                    this.M11 * local.Y +
                    this.M12 * local.Z +
                    this.M13,
                this.M20 * local.X +
                    this.M21 * local.Y +
                    this.M22 * local.Z +
                    this.M23);
        }

        public SpatialTransform WithPosition(
            ClientSpatialPosition position)
        {
            return this with
            {
                M03 = position.X,
                M13 = position.Y,
                M23 = position.Z,
            };
        }
    }
}
