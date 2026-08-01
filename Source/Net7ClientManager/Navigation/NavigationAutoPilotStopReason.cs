namespace Net7ClientManager.Navigation;

public enum NavigationAutoPilotStopReason
{
    None,
    UserStopped,
    DestinationReached,
    TargetSelectionFailed,
    WarpUnavailable,
    WarpDidNotEngage,
    WarpInterrupted,
    GateUnavailable,
    GateActivationFailed,
    SectorTransitionTimedOut,
    DestinationUnavailable,
    DestinationActivationFailed,
    DestinationTransitionTimedOut,
    UnexpectedSector,
    RouteChanged,
    ObservationUnavailable,
    ClientUnavailable,
    EnergyUnavailable,
    InternalError,
    WormholeUnavailable,
    WormholeActivationFailed,
    WormholeTransitionTimedOut,
}
