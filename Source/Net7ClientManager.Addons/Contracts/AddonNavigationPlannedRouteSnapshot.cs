namespace Net7ClientManager.Addons.Contracts;

public sealed record AddonNavigationPlannedRouteSnapshot
{
    public required string Id { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    public required AddonNavigationLocationSnapshot Origin { get; init; }

    public required AddonNavigationDestinationSnapshot OriginDestination
    { get; init; }

    public required AddonNavigationLocationSnapshot Current { get; init; }

    public required AddonNavigationDestinationSnapshot Destination
    { get; init; }

    public int CompletedHopCount { get; init; }

    public int RemainingHopCount { get; init; }

    public int TotalHopCount { get; init; }

    public AddonNavigationRouteStepSnapshot? NextStep { get; init; }

    public IReadOnlyList<AddonNavigationRouteStepSnapshot> Steps
    { get; init; } = [];

    public IReadOnlyList<string> Warnings { get; init; } = [];
}
