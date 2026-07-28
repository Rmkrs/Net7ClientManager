namespace Net7ClientManager.Forms;

public sealed class InGameOptionsRequestedEventArgs(
    int processId,
    IWin32Window owner)
    : EventArgs
{
    public int ProcessId { get; } = processId;

    public IWin32Window Owner { get; } = owner;
}
