namespace Net7ClientManager.Observations.Models;

public sealed record ClientReputationObservation
{
    public bool IsAvailable { get; init; }

    public string Status { get; init; } = "";

    public uint Address { get; init; }

    public uint ValidState { get; init; }

    public uint FactionCollectionAddress { get; init; }

    public uint FactionCollectionValidState { get; init; }

    public int Capacity { get; init; }

    public string Affiliation { get; init; } = "";

    public string FactionCatalogStatus { get; init; } = "";

    public string? FactionCatalogSourcePath { get; init; }

    public int FactionCatalogDefinitionCount { get; init; }

    public IReadOnlyList<ClientFactionReputationObservation>
        Factions
    { get; init; } = [];

    public int OccupiedSlotCount =>
        this.Factions.Count;

    public int ResolvedFactionCount =>
        this.Factions.Count(
            faction =>
                faction.IsCatalogResolved);

    public ClientFactionReputationObservation? GetByKey(
        string factionKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            factionKey);

        return this.Factions.FirstOrDefault(
            faction =>
                string.Equals(
                    faction.FactionKey,
                    factionKey,
                    StringComparison.Ordinal));
    }

    public static ClientReputationObservation Unavailable(
        string status,
        uint address = 0,
        uint factionCollectionAddress = 0)
    {
        return new ClientReputationObservation
        {
            Status = status,
            Address = address,
            FactionCollectionAddress =
                factionCollectionAddress,
        };
    }
}
