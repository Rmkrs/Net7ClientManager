namespace Net7ClientManager.Navigation;

internal sealed record ForgeNavigationGravityWellDocument
{
    public required string Id { get; init; }

    public required string SectorId { get; init; }

    public float CenterX { get; init; }

    public float CenterY { get; init; }

    public float CenterZ { get; init; }

    public float Radius { get; init; }

    public int BoundaryObservationCount { get; init; }

    public int EnteringCount { get; init; }

    public int LeavingCount { get; init; }

    public float AngularCoverage { get; init; }

    public float Confidence { get; init; }

    public DateTimeOffset FirstObservedAtUtc { get; init; }

    public DateTimeOffset LastObservedAtUtc { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; init; }
}
