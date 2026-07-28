namespace Net7ClientManager.Navigation;

internal sealed record ForgeNavigationSectorRecordDocument
{
    public required string Id { get; init; }

    public required string Key { get; init; }

    public required string Name { get; init; }

    public required string SystemName { get; init; }

    public string? RequiredProfession { get; init; }

    public string? RequiredFaction { get; init; }

    public int? MinimumFactionStanding { get; init; }

    public IReadOnlyList<string> Aliases { get; init; } = [];

    public IReadOnlyList<string> Connections { get; init; } = [];

    public uint ActiveSectorNumber { get; init; }

    public static ForgeNavigationSectorRecordDocument FromSector(
        ForgeNavigationSectorDocument sector)
    {
        ArgumentNullException.ThrowIfNull(sector);

        return new ForgeNavigationSectorRecordDocument
        {
            Id = sector.Id,
            Key = sector.Key,
            Name = sector.Name,
            SystemName = sector.SystemName,
            RequiredProfession = sector.RequiredProfession,
            RequiredFaction = sector.RequiredFaction,
            MinimumFactionStanding = sector.MinimumFactionStanding,
            Aliases = sector.Aliases,
            Connections = sector.Connections,
            ActiveSectorNumber = sector.ActiveSectorNumber,
        };
    }
}
