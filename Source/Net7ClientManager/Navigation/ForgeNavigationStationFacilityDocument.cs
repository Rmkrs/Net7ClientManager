namespace Net7ClientManager.Navigation;

internal sealed record ForgeNavigationStationFacilityDocument
{
    public required string Id { get; init; }

    public uint StarbaseId { get; init; }

    public uint StarbaseDefinitionId { get; init; }

    public required string StationName { get; init; }

    public required string SectorName { get; init; }

    public uint ActiveSectorNumber { get; init; }

    public int RoomClass { get; init; }

    public int RoomDefinitionKey { get; init; }

    public int RoomFacilitySlot { get; init; }

    public int DefinitionSlot { get; init; }

    public int FacilityType { get; init; }

    public required string Name { get; init; }

    public required string Confidence { get; init; }

    public bool HasAnonymousReports { get; init; }

    public IReadOnlyList<string> NamedReporters { get; init; } = [];
}
