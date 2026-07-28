namespace Net7ClientManager.Forms;

internal enum GalaxyAtlasSearchResultKind
{
    Sector,
    Station,
    Gate,
    Planet,
    NavigationPoint,
    Pilot,
}

internal sealed record GalaxyAtlasSearchResult
{
    public required GalaxyAtlasSearchResultKind Kind { get; init; }

    public required string Identity { get; init; }

    public required string Name { get; init; }

    public required string SectorKey { get; init; }

    public required string SectorName { get; init; }

    public required string SystemName { get; init; }

    public string? Detail { get; init; }

    public int Rank { get; init; }

    public string KindLabel => this.Kind switch
    {
        GalaxyAtlasSearchResultKind.Sector => "SECTOR",
        GalaxyAtlasSearchResultKind.Station => "STATION",
        GalaxyAtlasSearchResultKind.Gate => "GATE",
        GalaxyAtlasSearchResultKind.Planet => "PLANET",
        GalaxyAtlasSearchResultKind.NavigationPoint => "NAV",
        GalaxyAtlasSearchResultKind.Pilot => "PILOT",
        _ => "",
    };

    public string ContextText => this.Kind == GalaxyAtlasSearchResultKind.Sector
        ? this.SystemName
        : string.IsNullOrWhiteSpace(this.Detail)
            ? string.Concat(this.SystemName, " / ", this.SectorName)
            : string.Concat(
                this.SystemName,
                " / ",
                this.SectorName,
                " · ",
                this.Detail);

    public override string ToString()
    {
        return this.Name;
    }
}
