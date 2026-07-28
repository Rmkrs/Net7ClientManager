namespace Net7ClientManager.Navigation;

internal sealed record ForgeNavigationNpcDocument
{
    public required string Id { get; init; }

    public uint StarbaseId { get; init; }

    public required string StationName { get; init; }

    public required string SectorName { get; init; }

    public uint ActiveSectorNumber { get; init; }

    public int RoomClass { get; init; }

    public int RoomDefinitionKey { get; init; }

    public int RoomNpcSlot { get; init; }

    public int DefinitionKey { get; init; }

    public int DefinitionSecondaryId { get; init; }

    public string? Name { get; init; }

    public int? Role { get; init; }

    public int? Classification { get; init; }

    public required string Confidence { get; init; }

    public bool HasAnonymousReports { get; init; }

    public IReadOnlyList<string> NamedReporters { get; init; } = [];
}
