namespace Net7ClientManager.Navigation;

public sealed class GalaxyAtlasRequestedEventArgs(
    int? processId)
    : EventArgs
{
    public int? ProcessId { get; } = processId;
}
