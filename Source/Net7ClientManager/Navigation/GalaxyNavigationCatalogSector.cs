namespace Net7ClientManager.Navigation;

public sealed record GalaxyNavigationCatalogSector
{
    public required string SectorKey { get; init; }

    public uint ActiveSectorNumber { get; init; }

    public IReadOnlyList<GalaxyNavigationCatalogTarget> Targets { get; init; } = [];

    public IReadOnlyList<GalaxyNavigationCatalogDeparture> Departures { get; init; } = [];
}
