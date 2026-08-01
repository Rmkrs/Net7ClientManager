namespace Net7ClientManager.Navigation;

public sealed record GalaxyRouteTransition
{
    public required GalaxyRouteTransitionKind Kind { get; init; }

    public required GalaxySectorDefinition From { get; init; }

    public required GalaxySectorDefinition To { get; init; }

    public NavigationWormholeDestinationAvailability? Wormhole { get; init; }
}
