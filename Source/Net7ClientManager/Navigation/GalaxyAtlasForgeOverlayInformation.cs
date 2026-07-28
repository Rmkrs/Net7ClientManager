namespace Net7ClientManager.Navigation;

internal enum GalaxyAtlasForgeOverlayKind
{
    MobEncounter,
    HarvestableField,
    GravityWell,
}

internal sealed record GalaxyAtlasForgeOverlaySection
{
    public required string Title { get; init; }

    public IReadOnlyList<string> Lines { get; init; } = [];
}

internal sealed record GalaxyAtlasForgeOverlayInformation
{
    public required string Id { get; init; }

    public required GalaxyAtlasForgeOverlayKind Kind { get; init; }

    public required string SectorKey { get; init; }

    public required string Title { get; init; }

    public required string MapLabel { get; init; }

    public required string Subtitle { get; init; }

    public float CenterX { get; init; }

    public float CenterY { get; init; }

    public float CenterZ { get; init; }

    public float Radius { get; init; }

    public string MobFactionIdentifier { get; init; } = "";

    public ForgeNavigationEncounterFactionBindingKind MobFactionBindingKind
    { get; init; } = ForgeNavigationEncounterFactionBindingKind.Unknown;

    public int? IntrinsicRelationshipRaw { get; init; }

    public IReadOnlyList<string> DetailLines { get; init; } = [];

    public IReadOnlyList<GalaxyAtlasForgeOverlaySection> Sections
    { get; init; } = [];

    public required NavigationDestination Destination { get; init; }

    public required string RouteDescription { get; init; }
}
