namespace Net7ClientManager.Observations;

public sealed class ClientChatMessageObservedEventArgs(
    ClientChatMessage message)
    : EventArgs
{
    public ClientChatMessage Message { get; } = message;
}
