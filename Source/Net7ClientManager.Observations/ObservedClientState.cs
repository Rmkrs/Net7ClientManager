namespace Net7ClientManager.Observations;

using Net7ClientManager.Observations.Models;

internal sealed class ObservedClientState(
    int processId,
    DateTimeOffset processStartedAt)
{
    public int ProcessId { get; } = processId;

    public DateTimeOffset ProcessStartedAt { get; } = processStartedAt;

    public uint ModuleBaseAddress { get; set; }

    public uint KernelAddress { get; set; }

    public uint InitialLoadTaskAddress { get; set; }

    public uint LoginTaskAddress { get; set; }

    public uint? LoginTaskState { get; set; }

    public uint CharacterViewAddress { get; set; }

    public uint? CharacterViewMode { get; set; }

    public uint ClientSessionTaskAddress { get; set; }

    public uint ClientContextAddress { get; set; }

    public uint ChatPanelAddress { get; set; }

    public uint RingAddress { get; set; }

    public uint RingCapacity { get; set; }

    public uint RingEntries { get; set; }

    public uint? LastWriteCounter { get; set; }

    public DateTimeOffset? LastMessageAt { get; set; }

    public string StatusText { get; set; } =
        "Attached, waiting for Kernel";

    public bool HasDirectClientState { get; set; }

    public string DirectClientStateStatus { get; set; } =
        "Not read yet";

    public uint CurrentClientTime { get; set; }

    public uint LocalPlayerObjectId { get; set; }

    public uint TargetObjectId { get; set; }

    public uint LoadingOrTransitionFlag { get; set; }

    public uint LocalPlayerAuxDataAddress { get; set; }

    public uint PresentationMode { get; set; }

    public uint DockingTargetObjectId { get; set; }

    public uint PendingLandOrDockTargetObjectId { get; set; }

    public DateTimeOffset? LastStateObservedAt { get; set; }

    public ClientNetworkTrafficObservation NetworkTraffic { get; set; } =
        ClientNetworkTrafficObservation.Unavailable(
            "Not observed yet");

    public ClientFrameRateObservation FrameRate { get; set; } =
        ClientFrameRateObservation.Unavailable(
            "Not observed yet");

    public ClientStarMapPresentationObservation StarMapPresentation { get; set; } =
        ClientStarMapPresentationObservation.Unavailable(
            "Not observed yet");

    public ClientPanelPresentationObservation PanelPresentation { get; set; } =
        ClientPanelPresentationObservation.Unavailable(
            "Not observed yet");

    public System.Threading.Lock PanelPresentationLock { get; } = new();

    public ClientLootingObservation Looting { get; set; } =
        ClientLootingObservation.Unavailable(
            "Not observed yet");

    public ClientLootTractorObservation LootTractor { get; set; } =
        ClientLootTractorObservation.Unavailable(
            "Not observed yet");

    public ClientWorldObservation World { get; set; } =
        ClientWorldObservation.Unavailable(
            "Not observed yet");

    public ClientNavigationObservation Navigation { get; set; } =
        ClientNavigationObservation.Unavailable(
            "Not observed yet");

    public System.Threading.Lock NavigationStateLock { get; } = new();

    public ClientNavigationStateObservation NavigationState { get; set; } =
        ClientNavigationStateObservation.Unavailable(
            processId,
            processStartedAt,
            sequence: 0,
            generationSequence: 0,
            status: "Navigation control observation is inactive");

    public int NavigationStateObserverReferenceCount { get; set; }

    public DateTimeOffset NextNavigationStatePollAt { get; set; }

    public long NavigationStateSequence { get; set; }

    public ClientGutterRadarObservation NearbyTargets { get; set; } =
        ClientGutterRadarObservation.Unavailable(
            "Not observed yet");

    public ClientStarbaseContextObservation StarbaseContext { get; set; } =
        ClientStarbaseContextObservation.Unavailable(
            "Not observed yet");

    public ClientAudioCueObservation AudioCue { get; set; } =
        ClientAudioCueObservation.Unavailable(
            "Not observed yet");

    public ClientJobTerminalObservation JobTerminal { get; set; } =
        ClientJobTerminalObservation.Unavailable(
            "Not observed yet");

    public ClientLocalPlayerObservation LocalPlayer { get; set; } =
        ClientLocalPlayerObservation.Unavailable(
            "Not observed yet");

    public ClientGroupObservation Group { get; set; } =
        ClientGroupObservation.Unavailable(
            "Not observed yet");

    public ClientTargetObservation Target { get; set; } =
        ClientTargetObservation.Unavailable(
            "Not observed yet");

    public ClientTargetInteractionObservation TargetInteraction { get; set; } =
        ClientTargetInteractionObservation.Unavailable(
            "Not observed yet");

    public ClientShortcutStateObservation Shortcuts { get; set; } =
        ClientShortcutStateObservation.Unavailable(
            "Not observed yet");

    public ClientTooltipDelayObservation TooltipDelay { get; set; } =
        ClientTooltipDelayObservation.Unavailable(
            "Not observed yet");

    public ClientTooltipHoverObservation TooltipHover { get; set; } =
        ClientTooltipHoverObservation.Unavailable(
            "Not observed yet");

    public ClientProductionRecipeObservation ProductionRecipe { get; set; } =
        ClientProductionRecipeObservation.Unavailable(
            "Not observed yet");

    public ClientManufacturingActivityObservation ManufacturingActivity
    { get; set; } =
        ClientManufacturingActivityObservation.Unavailable(
            "Not observed yet");

    // Latest sample from the dedicated native Analyze pacing loop. It is
    // intentionally separate from ManufacturingActivity so the visible
    // 0.1-second countdown never depends on, or fans out through, the general
    // snapshot scheduler.
    public ClientManufacturingActivityObservation RealtimeManufacturingActivity
    { get; set; } =
        ClientManufacturingActivityObservation.Unavailable(
            "Not observed yet");

    public ClientManufacturingCatalogObservation ManufacturingCatalog
    { get; set; } =
        ClientManufacturingCatalogObservation.Unavailable(
            "Not observed yet");

    public bool RecipeMappingCatalogObservationEnabled { get; set; }

    public ClientLifecycleState LifecycleState { get; set; }

    public DateTimeOffset NextFallbackScanAt { get; set; }

    public DateTimeOffset NextPollAt { get; set; }

    public DateTimeOffset NextPanelPresentationPollAt { get; set; }

    public DateTimeOffset NextFeaturePollAt { get; set; }

    public DateTimeOffset NextLootTractorPollAt { get; set; }

    public DateTimeOffset NextCraftingActivityPollAt { get; set; } =
        DateTimeOffset.MaxValue;

    public DateTimeOffset NextTooltipHoverPollAt { get; set; }

    public DateTimeOffset NextTooltipItemPollAt { get; set; } =
        DateTimeOffset.MaxValue;

    public ClientTooltipItemRefreshScope PendingTooltipItemRefreshScope
    {
        get;
        set;
    }

    public DateTimeOffset NextGroupSkillsTargetPollAt { get; set; } =
        DateTimeOffset.MaxValue;

    public DateTimeOffset NextSlowFeaturePollAt { get; set; }

    public DateTimeOffset NextVendorShoppingPollAt { get; set; }

    public DateTimeOffset NextVendorTransactionPollAt { get; set; }

    public DateTimeOffset? LastFeatureObservedAt { get; set; }

    public DateTimeOffset? LastSlowFeatureObservedAt { get; set; }

    public long SnapshotSequence { get; set; }

    public ClientObservationSnapshot? Snapshot { get; set; }
}
