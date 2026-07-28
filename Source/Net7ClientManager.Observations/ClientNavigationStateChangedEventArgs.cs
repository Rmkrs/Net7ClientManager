namespace Net7ClientManager.Observations;

using Net7ClientManager.Observations.Models;

public sealed class ClientNavigationStateChangedEventArgs(
    ClientNavigationStateObservation previous,
    ClientNavigationStateObservation current) : EventArgs
{
    public ClientNavigationStateObservation Previous { get; } = previous;

    public ClientNavigationStateObservation Current { get; } = current;
}
