
namespace Net7ClientManager.Observations;

internal static class ClientLifecycleClassifier
{
    public static ClientLifecycleState Evaluate(
        ObservedClientState state)
    {
        if (state.KernelAddress == 0)
        {
            return ClientLifecycleState.Unknown;
        }

        if (state.ClientSessionTaskAddress != 0 &&
            state.ClientContextAddress != 0 &&
            state.HasDirectClientState)
        {
            return ClientLifecycleState.InGame;
        }

        if (state.LoginTaskAddress != 0 &&
            state.CharacterViewMode == 2)
        {
            return ClientLifecycleState.CharacterSelection;
        }

        if (state.LoginTaskAddress != 0 &&
            state.CharacterViewMode == 1)
        {
            return ClientLifecycleState.LoginScreen;
        }

        if (state.InitialLoadTaskAddress != 0)
        {
            return ClientLifecycleState.IntroScene;
        }

        return ClientLifecycleState.ApplicationStarted;
    }
}
