namespace Net7ClientManager.Navigation;

internal sealed record NavigationAutoPilotMachineState
{
    public required Guid RouteId { get; init; }

    public required string DestinationName { get; init; }

    public required string ExpectedCurrentSectorKey { get; init; }

    public string ExpectedCurrentSectorName { get; init; } = "";

    public NavigationAutoPilotMachinePhase Phase { get; init; }

    public required DateTimeOffset PhaseStartedAt { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public required DateTimeOffset StepStartedAt { get; init; }

    public NavigationAutoPilotStepPlan? Step { get; init; }

    public uint? TargetObjectId { get; init; }

    public DateTimeOffset? VerbMissingSince { get; init; }

    public long StepGenerationSequence { get; init; }

    public long WarpRequestNavigationSequence { get; init; }

    public long WarpRequestGenerationSequence { get; init; }

    public DateTimeOffset? WarpRequestedAt { get; init; }

    public bool WarpRequestAccepted { get; init; }

    public bool WarpReachedActiveState { get; init; }

    public bool TransitionAccepted { get; init; }

    public NavigationAutoPilotState PublicState { get; init; }

    public NavigationAutoPilotStopReason StopReason { get; init; }

    public string StatusText { get; init; } = "";

    public int? CurrentEnergy { get; init; }

    public int? RequiredEnergy { get; init; }

    public bool IsTerminal =>
        this.Phase is
            NavigationAutoPilotMachinePhase.ManualFinalLeg or
            NavigationAutoPilotMachinePhase.Arrived or
            NavigationAutoPilotMachinePhase.Stopped;

    public static NavigationAutoPilotMachineState Create(
        Guid routeId,
        string destinationName,
        string expectedCurrentSectorKey,
        string expectedCurrentSectorName,
        DateTimeOffset now)
    {
        return new NavigationAutoPilotMachineState
        {
            RouteId = routeId,
            DestinationName = destinationName,
            ExpectedCurrentSectorKey = expectedCurrentSectorKey,
            ExpectedCurrentSectorName = expectedCurrentSectorName,
            Phase = NavigationAutoPilotMachinePhase.ReconcilingStep,
            PhaseStartedAt = now,
            StartedAt = now,
            StepStartedAt = now,
            PublicState = NavigationAutoPilotState.Starting,
            StatusText = $"Starting Auto Pilot toward {destinationName}.",
        };
    }
}
