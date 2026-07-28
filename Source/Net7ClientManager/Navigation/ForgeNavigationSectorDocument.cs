namespace Net7ClientManager.Navigation;

internal sealed record ForgeNavigationSectorDocument
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

    public IReadOnlyList<ForgeNavigationTargetDocument> Targets { get; init; } = [];

    public IReadOnlyList<ForgeNavigationDepartureDocument> Departures { get; init; } = [];
}
