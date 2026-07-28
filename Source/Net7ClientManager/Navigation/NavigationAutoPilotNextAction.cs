namespace Net7ClientManager.Navigation;

public enum NavigationAutoPilotNextAction
{
    None,
    Wait,
    SelectTarget,
    RequestWarp,
    ActivateVerb,
    ReconcileRoute,
    Complete,
    Stop,
}
