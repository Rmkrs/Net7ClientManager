namespace Net7ClientManager.Navigation;

public sealed class NavigationRouteChangedEventArgs(
    NavigationRouteSnapshot snapshot)
    : EventArgs
{
    public NavigationRouteSnapshot Snapshot { get; } = snapshot;
}
