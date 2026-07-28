namespace Net7ClientManager.Addons.Contracts;

public sealed record AddonNavigationRouteSnapshot
{
    public bool IsAvailable { get; init; }

    public string Status { get; init; } = "unavailable";

    public string StatusText { get; init; } = "";

    public bool HasRoute { get; init; }

    public AddonNavigationPlannedRouteSnapshot? Route { get; init; }

    public AddonNavigationJourneySnapshot Journey { get; init; } =
        new();
}
