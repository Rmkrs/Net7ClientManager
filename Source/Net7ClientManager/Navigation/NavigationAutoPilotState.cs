namespace Net7ClientManager.Navigation;

public enum NavigationAutoPilotState
{
    Inactive,
    Starting,
    SelectingTarget,
    WaitingForWarp,
    WaitingForEnergy,
    EngagingWarp,
    Warping,
    VerifyingArrival,
    ActivatingGate,
    WaitingForSector,
    ActivatingDestination,
    WaitingForDestination,
    ManualFinalLeg,
    Arrived,
    Stopped,
    ActivatingWormhole,
}
