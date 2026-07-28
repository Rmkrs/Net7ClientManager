namespace Net7ClientManager.Navigation;

public sealed record NavigationRoutePlan
{
    public required Guid RouteId { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    public required string CharacterKey { get; init; }

    public required string CharacterName { get; init; }

    public required GalaxySectorDefinition Origin { get; init; }

    public required NavigationDestination OriginDestination { get; init; }

    public required GalaxySectorDefinition Current { get; init; }

    public required NavigationDestination Destination { get; init; }

    public required NavigationRouteStatus Status { get; init; }

    public string StatusText { get; init; } = "";

    public int CompletedHopCount { get; init; }

    public int RemainingHopCount { get; init; }

    public int TotalHopCount =>
        this.CompletedHopCount + this.RemainingHopCount;

    public IReadOnlyList<NavigationRouteStep> Steps { get; init; } = [];

    public NavigationRouteStep? NextStep =>
        this.Steps.Count > 0
            ? this.Steps[0]
            : null;

    public IReadOnlyList<string> Warnings { get; init; } = [];
}
