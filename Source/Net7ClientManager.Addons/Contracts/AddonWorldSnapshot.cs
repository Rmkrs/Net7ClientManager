namespace Net7ClientManager.Addons.Contracts;

public sealed record AddonWorldSnapshot
{
    public bool IsAvailable { get; init; }

    public required string Environment { get; init; }

    public string? SystemName { get; init; }

    public string? SectorName { get; init; }

    public string? StarbaseName { get; init; }

    public uint? SectorId { get; init; }

    public uint? StarbaseId { get; init; }
}
