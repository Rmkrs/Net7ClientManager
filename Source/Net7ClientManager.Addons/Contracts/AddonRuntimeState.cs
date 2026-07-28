namespace Net7ClientManager.Addons.Contracts;

public enum AddonRuntimeState
{
    Disabled,
    WaitingForContext,
    Suspended,
    Loading,
    Running,
    Failed,
    Stopping,
    Unavailable,
}
