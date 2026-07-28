namespace Net7ClientManager.Observations;

using Net7ClientManager.Observations.Models;

public sealed record ClientObservationSnapshot
{
    public required int ProcessId { get; init; }

    public required DateTimeOffset ProcessStartedAt { get; init; }

    public required long Sequence { get; init; }

    public required DateTimeOffset ObservedAt { get; init; }

    public bool IsAttached { get; init; }

    public bool IsAvailable { get; init; }

    public ClientLifecycleState LifecycleState { get; init; }

    public string StatusText { get; init; } = "";

    public DateTimeOffset? LastMessageAt { get; init; }

    public bool HasGameState { get; init; }

    public string GameStateStatus { get; init; } = "";

    public uint CurrentClientTime { get; init; }

    public uint LocalPlayerObjectId { get; init; }

    public uint TargetObjectId { get; init; }

    public uint LoadingOrTransitionFlag { get; init; }

    public uint LocalPlayerAuxDataAddress { get; init; }

    public uint PresentationMode { get; init; }

    public uint DockingTargetObjectId { get; init; }

    public uint PendingLandOrDockTargetObjectId { get; init; }

    public DateTimeOffset? LastStateObservedAt { get; init; }

    public DateTimeOffset? LastFeatureObservedAt { get; init; }

    public DateTimeOffset? LastSlowFeatureObservedAt { get; init; }

    public ClientNetworkTrafficObservation NetworkTraffic { get; init; } =
        ClientNetworkTrafficObservation.Unavailable("Not observed yet");

    public ClientFrameRateObservation FrameRate { get; init; } =
        ClientFrameRateObservation.Unavailable("Not observed yet");

    public ClientStarMapPresentationObservation StarMapPresentation { get; init; } =
        ClientStarMapPresentationObservation.Unavailable("Not observed yet");

    public ClientPanelPresentationObservation PanelPresentation { get; init; } =
        ClientPanelPresentationObservation.Unavailable("Not observed yet");

    public ClientLootingObservation Looting { get; init; } =
        ClientLootingObservation.Unavailable("Not observed yet");

    public ClientLootTractorObservation LootTractor { get; init; } =
        ClientLootTractorObservation.Unavailable("Not observed yet");

    public ClientWorldObservation World { get; init; } =
        ClientWorldObservation.Unavailable("Not observed yet");

    public ClientNavigationObservation Navigation { get; init; } =
        ClientNavigationObservation.Unavailable("Not observed yet");

    public ClientNavigationStateObservation NavigationState { get; init; } =
        ClientNavigationStateObservation.Unavailable(
            processId: 0,
            processStartedAt: DateTimeOffset.MinValue,
            sequence: 0,
            generationSequence: 0,
            status: "Navigation control observation is inactive");

    public ClientGutterRadarObservation NearbyTargets { get; init; } =
        ClientGutterRadarObservation.Unavailable("Not observed yet");

    public ClientStarbaseContextObservation StarbaseContext { get; init; } =
        ClientStarbaseContextObservation.Unavailable("Not observed yet");

    public ClientAudioCueObservation AudioCue { get; init; } =
        ClientAudioCueObservation.Unavailable("Not observed yet");

    public ClientJobTerminalObservation JobTerminal { get; init; } =
        ClientJobTerminalObservation.Unavailable("Not observed yet");

    public ClientLocalPlayerObservation LocalPlayer { get; init; } =
        ClientLocalPlayerObservation.Unavailable("Not observed yet");

    public ClientGroupObservation Group { get; init; } =
        ClientGroupObservation.Unavailable("Not observed yet");

    public ClientTargetObservation Target { get; init; } =
        ClientTargetObservation.Unavailable("Not observed yet");

    public ClientTargetInteractionObservation TargetInteraction { get; init; } =
        ClientTargetInteractionObservation.Unavailable("Not observed yet");

    public ClientShortcutStateObservation Shortcuts { get; init; } =
        ClientShortcutStateObservation.Unavailable("Not observed yet");

    public ClientTooltipDelayObservation TooltipDelay { get; init; } =
        ClientTooltipDelayObservation.Unavailable("Not observed yet");

    public ClientTooltipHoverObservation TooltipHover { get; init; } =
        ClientTooltipHoverObservation.Unavailable("Not observed yet");

    public ClientProductionRecipeObservation ProductionRecipe { get; init; } =
        ClientProductionRecipeObservation.Unavailable("Not observed yet");

    public ClientCombatObservation Combat { get; init; } =
        ClientCombatObservation.Unavailable("Not observed yet");
}
