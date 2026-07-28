namespace Net7ClientManager.Navigation;

internal sealed record NavigationRouteDocument
{
    public int SchemaVersion { get; init; }

    public DateTimeOffset SavedAt { get; init; }

    public IReadOnlyList<NavigationPersistedRoute> Routes { get; init; } = [];
}
