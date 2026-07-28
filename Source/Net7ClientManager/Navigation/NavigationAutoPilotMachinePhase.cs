namespace Net7ClientManager.Navigation;

internal enum NavigationAutoPilotMachinePhase
{
    ReconcilingStep,
    ResolvingTarget,
    SelectingTarget,
    EvaluatingRange,
    WaitingForWarp,
    EngagingWarp,
    WaitingForWarpStart,
    Warping,
    WaitingForArrival,
    WaitingForVerb,
    ActivatingVerb,
    WaitingForTransition,
    WaitingForDestination,
    ManualFinalLeg,
    Arrived,
    Stopped,
}
