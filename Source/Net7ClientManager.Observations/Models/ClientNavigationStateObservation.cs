namespace Net7ClientManager.Observations.Models;

using Net7ClientManager.Observations;

public sealed record ClientNavigationStateObservation
{
    public required int ProcessId { get; init; }

    public required DateTimeOffset ProcessStartedAt { get; init; }

    public required long Sequence { get; init; }

    public required DateTimeOffset ObservedAt { get; init; }

    public bool IsAvailable { get; init; }

    public string Status { get; init; } = "";

    public ClientLifecycleState LifecycleState { get; init; }

    public ClientWorldEnvironment Environment { get; init; }

    public uint ActiveSectorNumber { get; init; }

    public string SectorName { get; init; } = "";

    public bool SectorNameDirectlyObserved { get; init; }

    public uint PresentationMode { get; init; }

    public bool IsWorldPresent { get; init; }

    public bool IsLoading { get; init; }

    public ClientNavigationStateGeneration Generation { get; init; }

    public required long GenerationSequence { get; init; }

    public bool GenerationChanged { get; init; }

    public uint CurrentClientTime { get; init; }

    /// <summary>
    /// Raw SClient +0x1198 gate around part of frame processing. Its broader
    /// semantic meaning is intentionally not named as a loading flag.
    /// </summary>
    public uint FrameProcessingGateFlag { get; init; }

    public uint LocalPlayerObjectId { get; init; }

    public bool SelectedTargetKnown { get; init; }

    public uint SelectedTargetObjectId { get; init; }

    public bool HasSelectedTarget =>
        this.SelectedTargetKnown &&
        this.SelectedTargetObjectId is not 0 and not uint.MaxValue;

    public bool PathBuildStateKnown { get; init; }

    public bool PathBuildBusy { get; init; }

    public float? TargetDistance { get; init; }

    public DateTimeOffset? TargetDistanceObservedAt { get; init; }

    public ClientObservedAuxDataValue<int> PrivateWarpState { get; init; }

    public ClientObservedAuxDataValue<int> GlobalWarpState { get; init; }

    public ClientObservedAuxDataValue<int> WarpAvailable { get; init; }

    public ClientObservedAuxDataValue<ulong> WarpPhaseTriggerClientTime { get; init; }

    public ClientObservedAuxDataValue<float> MaximumSpeed { get; init; }

    public ClientObservedAuxDataValue<bool> LockSpeed { get; init; }

    public ClientObservedAuxDataValue<bool> LockOrient { get; init; }

    public ClientObservedAuxDataValue<int> EngineThrustState { get; init; }

    public ClientObservedAuxDataValue<bool> IsCloaked { get; init; }

    public ClientObservedAuxDataValue<float> EnergyFraction { get; init; }

    public ClientObservedAuxDataValue<float> MaximumEnergyPower { get; init; }

    public float? CurrentEnergyPower { get; init; }

    public int? LastTerminalWarpReason { get; init; }

    public DateTimeOffset? LastTerminalWarpReasonAt { get; init; }

    public bool RequiredPropertiesAvailable { get; init; }

    public ClientNavigationStatePhase Phase { get; init; }

    /// <summary>
    /// The private state owns local Warp control. The global state can remain
    /// at recovery value 3 after a gate/world transition even after the ship's
    /// controls and Warp command are available again.
    /// </summary>
    public bool IsWarpIdle =>
        this.PrivateWarpState is { IsAvailable: true, Value: 0 } &&
        this.GlobalWarpState is
        {
            IsAvailable: true,
            Value: 0 or 3,
        };

    public bool HasGlobalWarpRecoveryResidue =>
        this.PrivateWarpState is { IsAvailable: true, Value: 0 } &&
        this.GlobalWarpState is { IsAvailable: true, Value: 3 };

    public bool IsWarpStarting =>
        this.PrivateWarpState is { IsAvailable: true, Value: 1 } ||
        this.GlobalWarpState is { IsAvailable: true, Value: 1 };

    public bool IsWarpActive =>
        this.PrivateWarpState is { IsAvailable: true, Value: 2 } ||
        this.GlobalWarpState is { IsAvailable: true, Value: 2 };

    public bool IsWarpRecovering =>
        this.PrivateWarpState is { IsAvailable: true, Value: 3 } ||
        (this.GlobalWarpState is { IsAvailable: true, Value: 3 } &&
         !this.HasGlobalWarpRecoveryResidue);

    public bool IsGateTransitionLocked =>
        this.PrivateWarpState is { IsAvailable: true, Value: 4 };

    /// <summary>
    /// Interaction verbs can be used as soon as physical Warp travel has
    /// ended and the game releases movement controls. Warp recovery state 3
    /// is a cooldown for starting another Warp, not a blocker for Gate, Dock,
    /// or Land when the client already exposes that verb as executable.
    /// </summary>
    public bool IsInteractionControlReady =>
        this.IsAvailable &&
        this.RequiredPropertiesAvailable &&
        this.IsWorldPresent &&
        !this.IsLoading &&
        this.Environment == ClientWorldEnvironment.Space &&
        this.PrivateWarpState is
        {
            IsAvailable: true,
            Value: 0 or 3,
        } &&
        this.GlobalWarpState is
        {
            IsAvailable: true,
            Value: 0 or 3,
        } &&
        this.LockSpeed is { IsAvailable: true, Value: false } &&
        this.LockOrient is { IsAvailable: true, Value: false };

    public bool IsWarpControlBusy =>
        this.IsWarpStarting ||
        this.IsWarpActive ||
        this.IsWarpRecovering ||
        this.IsGateTransitionLocked;

    public bool IsClientWarpReady =>
        this.IsAvailable &&
        this.RequiredPropertiesAvailable &&
        this.IsWorldPresent &&
        !this.IsLoading &&
        this.Environment == ClientWorldEnvironment.Space &&
        this.IsWarpIdle &&
        this.WarpAvailable is { IsAvailable: true, Value: 2 } &&
        this.MaximumSpeed is { IsAvailable: true, Value: > 0.0f } &&
        this.LockSpeed is { IsAvailable: true, Value: false } &&
        this.LockOrient is { IsAvailable: true, Value: false } &&
        this.SelectedTargetKnown &&
        this.HasSelectedTarget &&
        this.PathBuildStateKnown &&
        !this.PathBuildBusy;

    public string GetClientWarpReadinessReason()
    {
        if (!this.IsAvailable)
        {
            return string.IsNullOrWhiteSpace(this.Status)
                ? "Navigation state is unavailable"
                : this.Status;
        }

        if (!this.RequiredPropertiesAvailable)
        {
            return "Required navigation properties are unavailable";
        }

        if (!this.IsWorldPresent || this.IsLoading)
        {
            return "The game world is loading or transitioning";
        }

        if (this.Environment != ClientWorldEnvironment.Space)
        {
            return $"The current environment is {this.Environment}";
        }

        if (!this.IsWarpIdle)
        {
            return "Warp control is not idle";
        }

        if (this.WarpAvailable.Value != 2)
        {
            return $"Warp capability state is {this.WarpAvailable.Value}";
        }

        if (this.MaximumSpeed.Value <= 0.0f)
        {
            return "The ship cannot currently move";
        }

        if (this.LockSpeed.Value || this.LockOrient.Value)
        {
            return "Ship movement controls are locked";
        }

        if (!this.SelectedTargetKnown)
        {
            return "The selected target is not observable";
        }

        if (!this.HasSelectedTarget)
        {
            return "No target is selected";
        }

        if (!this.PathBuildStateKnown)
        {
            return "The client path-build state is not observable";
        }

        if (this.PathBuildBusy)
        {
            return "The client is still building the selected path";
        }

        return "Ready";
    }

    public static ClientNavigationStateObservation Unavailable(
        int processId,
        DateTimeOffset processStartedAt,
        long sequence,
        long generationSequence,
        string status,
        ClientLifecycleState lifecycleState = default,
        ClientWorldEnvironment environment = default,
        uint activeSectorNumber = 0,
        string sectorName = "")
    {
        return new ClientNavigationStateObservation
        {
            ProcessId = processId,
            ProcessStartedAt = processStartedAt,
            Sequence = sequence,
            ObservedAt = DateTimeOffset.UtcNow,
            GenerationSequence = generationSequence,
            Status = status,
            LifecycleState = lifecycleState,
            Environment = environment,
            ActiveSectorNumber = activeSectorNumber,
            SectorName = sectorName,
            Phase = ClientNavigationStatePhase.Unavailable,
        };
    }
}
