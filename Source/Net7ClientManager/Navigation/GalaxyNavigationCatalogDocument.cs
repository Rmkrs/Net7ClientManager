namespace Net7ClientManager.Navigation;

internal sealed record GalaxyNavigationCatalogDocument
{
    public int SchemaVersion { get; init; }

    public required DateTimeOffset GeneratedAt { get; init; }

    public IReadOnlyList<GalaxyNavigationCatalogSector> Sectors { get; init; } = [];
}
