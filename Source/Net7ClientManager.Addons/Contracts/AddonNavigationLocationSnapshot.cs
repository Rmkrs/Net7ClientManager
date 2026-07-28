namespace Net7ClientManager.Addons.Contracts;

public sealed record AddonNavigationLocationSnapshot
{
    public required string SectorKey { get; init; }

    public required string SectorName { get; init; }

    public required string SystemName { get; init; }
}
