namespace Net7ClientManager.Forms;

using Net7ClientManager.Navigation;

public sealed class GalaxyAtlasDestinationRequestedEventArgs(
    NavigationDestination destination)
    : EventArgs
{
    public NavigationDestination Destination { get; } = destination;
}
