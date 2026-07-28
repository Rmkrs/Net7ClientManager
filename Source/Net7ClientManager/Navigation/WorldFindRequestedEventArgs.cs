namespace Net7ClientManager.Navigation;

public sealed class WorldFindRequestedEventArgs(
    int? processId,
    string? query = null)
    : EventArgs
{
    public int? ProcessId { get; } = processId;

    public string? Query { get; } = query;
}
