namespace Net7ClientManager.Addons.Contracts;

public sealed class AddonUiInteractionEventArgs(
    AddonUiInteraction interaction)
    : EventArgs
{
    public AddonUiInteraction Interaction { get; } = interaction;
}
