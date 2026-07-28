namespace Net7ClientManager.Forms;

using Net7ClientManager.Navigation;

internal sealed record GalaxyAtlasNode
{
    public required GalaxyNavigationCatalogTarget Target { get; init; }

    public required NavigationDestination TargetDestination { get; init; }

    public required PointF Point { get; init; }

    public required RectangleF HitBounds { get; init; }

    public GalaxyNavigationCatalogDeparture? Departure { get; init; }

    public GalaxySectorDefinition? Destination { get; init; }

    public required GalaxyAtlasDepartureAccess Access { get; init; }

    public bool IsLandablePlanet { get; init; }

    public bool IsClickable =>
        this.Departure != null &&
        !string.IsNullOrWhiteSpace(this.Departure.ToSectorKey) &&
        this.Destination != null;
}
