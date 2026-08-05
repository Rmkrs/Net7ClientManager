namespace Net7ClientManager.Observations;

public enum ClientSelectedChatChannel
{
    Unknown = -1,
    Broadcast = 0,
    Local = 1,
    Guild = 2,
    Group = 3,
    PrivateChannel = 4,
    PublicChannel = 5,
    DirectMessage = 6,
}

public sealed record ClientChatState(
    ClientChatInputState InputState,
    int RawSelectedChannel,
    ClientSelectedChatChannel SelectedChannel,
    string? SelectedChannelName,
    string? ReplyTarget,
    string DiagnosticStatus);
