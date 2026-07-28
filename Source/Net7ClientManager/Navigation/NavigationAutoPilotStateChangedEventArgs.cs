namespace Net7ClientManager.Navigation;

public sealed class NavigationAutoPilotStateChangedEventArgs(
    NavigationAutoPilotSnapshot snapshot) : EventArgs
{
    public NavigationAutoPilotSnapshot Snapshot { get; } = snapshot;
}
