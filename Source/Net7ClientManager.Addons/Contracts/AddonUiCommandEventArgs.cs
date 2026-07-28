namespace Net7ClientManager.Addons.Contracts;

public sealed class AddonUiCommandEventArgs(
    AddonUiCommand command)
    : EventArgs
{
    public AddonUiCommand Command { get; } = command;
}
