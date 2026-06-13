namespace Net7ClientManager.Models;

public enum ClientState
{
    WaitingForGameWindow,
    Docked,

    WaitingForTos,
    AcceptingTos,

    WaitingForSizzle,
    WaitingForLogin,
    LoginNameFilled,

    Closing,
    Stopped,
}
