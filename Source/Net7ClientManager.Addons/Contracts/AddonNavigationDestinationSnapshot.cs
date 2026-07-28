namespace Net7ClientManager.Addons.Contracts;

public sealed record AddonNavigationDestinationSnapshot
{
    public required string Kind { get; init; }

    public required AddonNavigationLocationSnapshot Sector { get; init; }

    public AddonNavigationTargetSnapshot? Target { get; init; }
}
