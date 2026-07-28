namespace Net7ClientManager.Models;

public enum FleetCommandStepKind
{
    Action,
    Delay,
    SetTitle,
    RestorePilotFocus,
    TapKey,
    TypeText,
    TargetInvokingPilot,
    TargetInvokingPilotTarget,
    ChatCommand,
}
