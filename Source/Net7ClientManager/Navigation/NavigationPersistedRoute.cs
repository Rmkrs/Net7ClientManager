namespace Net7ClientManager.Navigation;

internal sealed record NavigationPersistedRoute
{
    public required string CharacterKey { get; init; }

    public required string CharacterName { get; init; }

    public required Guid RouteId { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    public required string OriginSectorKey { get; init; }

    public NavigationDestination? OriginDestination { get; init; }

    public required NavigationDestination Destination { get; init; }

    public IReadOnlyList<string> VisitedSectorKeys { get; init; } = [];

    public string LastKnownSectorKey { get; init; } = "";
}
