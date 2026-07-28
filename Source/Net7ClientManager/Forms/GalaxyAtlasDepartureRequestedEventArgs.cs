namespace Net7ClientManager.Forms;

using Net7ClientManager.Navigation;

public sealed class GalaxyAtlasDepartureRequestedEventArgs(
    GalaxyNavigationCatalogDeparture departure)
    : EventArgs
{
    public GalaxyNavigationCatalogDeparture Departure { get; } = departure;
}
