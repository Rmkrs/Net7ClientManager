namespace Net7ClientManager.Models;

/// <summary>
/// Window-hosting and startup-automation state.
///
/// This is deliberately separate from the observed game lifecycle exposed by
/// <see cref="Net7ClientManager.Observations.ClientLifecycleState"/>.
/// </summary>
public enum ClientState
{
    WaitingForGameWindow,
    Docked,

    WaitingForTos,
    AcceptingTos,

    WaitingForIntro,
    WaitingForLogin,
    LoginSubmitted,

    WaitingForCharacterSelect,
    EnteringGame,
    Ready,

    Closing,
    Stopped,
}
