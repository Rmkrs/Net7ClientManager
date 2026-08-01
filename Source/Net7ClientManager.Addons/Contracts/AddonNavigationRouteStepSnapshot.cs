namespace Net7ClientManager.Addons.Contracts;

public sealed record AddonNavigationRouteStepSnapshot
{
    public required int Number { get; init; }

    public required string Kind { get; init; }

    public required AddonNavigationLocationSnapshot From { get; init; }

    public required AddonNavigationLocationSnapshot To { get; init; }

    public AddonNavigationTargetSnapshot? DepartureTarget { get; init; }

    public AddonNavigationWormholeSnapshot? Wormhole { get; init; }

    public AddonNavigationTargetSnapshot? FinalTarget { get; init; }

    public string? AccessRequirement { get; init; }
}
