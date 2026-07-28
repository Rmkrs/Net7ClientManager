namespace Net7ClientManager.Navigation;

internal sealed record ForgeNavigationMobEncounterClusterDocument
{
    public required string Id { get; init; }

    public required string MobVariantId { get; init; }

    public required string SectorId { get; init; }

    public float CenterX { get; init; }

    public float CenterY { get; init; }

    public float CenterZ { get; init; }

    public float Radius { get; init; }

    public long SightingCount { get; init; }

    public DateTimeOffset FirstSeenAtUtc { get; init; }

    public DateTimeOffset LastSeenAtUtc { get; init; }
}
