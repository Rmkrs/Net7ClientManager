namespace Net7ClientManager.Observations.Models;

public enum ClientNavigationStatePhase
{
    Unavailable,
    Loading,
    AwaitingWorldReplacement,
    AwaitingAuxData,
    AwaitingPathState,
    Idle,
    SelectingTarget,
    WarpStarting,
    Warping,
    WarpRecovery,
    GateTransitionLocked,
}
