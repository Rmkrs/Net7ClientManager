namespace Net7ClientManager.Navigation;

using Net7ClientManager.Observations;
using Net7ClientManager.Observations.Models;

internal sealed record NavigationAutoPilotMachineFrame
{
    public required DateTimeOffset Now { get; init; }

    public bool ClientAvailable { get; init; }

    public bool ObservationAvailable { get; init; }

    public ClientLifecycleState LifecycleState { get; init; }

    public uint LoadingOrTransitionFlag { get; init; }

    public bool WorldAvailable { get; init; }

    public ClientWorldEnvironment Environment { get; init; }

    public uint ActiveSectorNumber { get; init; }

    public string WorldSectorName { get; init; } = "";

    public string CurrentStarbaseName { get; init; } = "";

    public bool RouteAvailable { get; init; }

    public Guid? RouteId { get; init; }

    public NavigationRouteStatus RouteStatus { get; init; }

    public string RouteStatusText { get; init; } = "";

    public string? RouteCurrentSectorKey { get; init; }

    public string? RouteCurrentSectorName { get; init; }

    public string RouteDestinationName { get; init; } = "";

    public NavigationRouteStep? NextStep { get; init; }

    public bool NavigationStateAvailable { get; init; }

    public bool NavigationPropertiesAvailable { get; init; }

    public long NavigationStateSequence { get; init; }

    public long NavigationGenerationSequence { get; init; }

    public bool NavigationGenerationChanged { get; init; }

    public ClientNavigationStatePhase NavigationPhase { get; init; }

    public string NavigationStateStatus { get; init; } = "";

    public int? PrivateWarpState { get; init; }

    public int? GlobalWarpState { get; init; }

    public bool IsWarpIdle { get; init; }

    public bool IsWarpStarting { get; init; }

    public bool IsWarpActive { get; init; }

    public bool IsWarpRecovering { get; init; }

    public bool IsGateTransitionLocked { get; init; }

    public bool IsInteractionControlReady { get; init; }

    public bool IsClientWarpReady { get; init; }

    public string ClientWarpReadinessReason { get; init; } = "";

    public bool PathBuildStateKnown { get; init; }

    public bool PathBuildBusy { get; init; }

    public bool? LockSpeed { get; init; }

    public bool? LockOrient { get; init; }

    public int? LastTerminalWarpReason { get; init; }

    public DateTimeOffset? LastTerminalWarpReasonAt { get; init; }

    public float? TargetDistance { get; init; }

    public int? CurrentEnergy { get; init; }

    public bool PublishedNavigationAvailable { get; init; }

    public uint PublishedNavigationSectorNumber { get; init; }

    public int PublishedNavigationTargetCount { get; init; }

    public string PublishedNavigationStatus { get; init; } = "";

    public NavigationTargetResolutionResult TargetResolution { get; init; } =
        NavigationTargetResolutionResult.Waiting(
            "Target resolution was not requested");

    public bool SelectedTargetKnown { get; init; }

    public bool HasSelectedTarget { get; init; }

    public uint SelectedTargetObjectId { get; init; }

    public ClientTargetVerb DetectedVerb { get; init; } =
        ClientTargetVerb.NotApplicable;

    public NavigationAutoPilotVerbState VerbState { get; init; }

    public bool DockingRequestObserved { get; init; }

    public bool IsStableSpace =>
        this.ObservationAvailable &&
        this.LifecycleState == ClientLifecycleState.InGame &&
        this.LoadingOrTransitionFlag == 0 &&
        this.WorldAvailable &&
        this.Environment == ClientWorldEnvironment.Space &&
        this.ActiveSectorNumber != 0;

    public bool IsStablePlanet =>
        this.ObservationAvailable &&
        this.LifecycleState == ClientLifecycleState.InGame &&
        this.LoadingOrTransitionFlag == 0 &&
        this.WorldAvailable &&
        this.Environment == ClientWorldEnvironment.Planet &&
        this.ActiveSectorNumber != 0;

    public bool IsStableStarbase =>
        this.ObservationAvailable &&
        this.LifecycleState == ClientLifecycleState.InGame &&
        this.LoadingOrTransitionFlag == 0 &&
        this.WorldAvailable &&
        this.Environment == ClientWorldEnvironment.Starbase;
}
