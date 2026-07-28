namespace Net7ClientManager.Navigation;

public sealed class NavigationPlannerRequestedEventArgs(
    int? processId)
    : EventArgs
{
    public int? ProcessId { get; } = processId;
}
