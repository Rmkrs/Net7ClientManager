namespace Net7ClientManager.Observations.Models;

public sealed record ClientAsteroidObservation
{
    public bool IsAvailable { get; init; }

    public string Status { get; init; } = "";

    public uint AuxDataLookupAddress { get; init; }

    public uint TechLevelPropertyAddress { get; init; }

    public uint PercentFullPropertyAddress { get; init; }

    public int? TechLevel { get; init; }

    public float? PercentFull { get; init; }

    public int MaximumObservedSlotCount { get; init; }

    public int PresentSlotCount { get; init; }

    public int ValidSlotCount { get; init; }

    public IReadOnlyList<ClientAsteroidResourceSlotObservation> Slots
    { get; init; } = [];

    public IReadOnlyList<ClientAsteroidResourceSlotObservation> Resources =>
    [
        .. this.Slots.Where(
            slot =>
                slot.IsOccupied),
    ];

    public int ResourceCount =>
        this.Slots.Count(
            slot =>
                slot.IsOccupied);

    public int MissingSlotCount =>
        Math.Max(
            0,
            this.MaximumObservedSlotCount -
            this.PresentSlotCount);

    public int InvalidSlotCount =>
        Math.Max(
            0,
            this.PresentSlotCount -
            this.ValidSlotCount);

    public int EmptyValidSlotCount =>
        this.Slots.Count(
            slot =>
                slot.IsPresent &&
                slot.IsValid &&
                !slot.IsOccupied);

    public float? PercentFullPercent =>
        this.PercentFull.HasValue
            ? Math.Clamp(
                  this.PercentFull.Value,
                  0.0f,
                  1.0f) *
              100.0f
            : null;

    public bool IsDepleted =>
        this.PercentFull.HasValue &&
        this.PercentFull.Value <= 0.0f;

    public bool HasStaleResourceManifest =>
        this.IsDepleted &&
        this.ResourceCount > 0;

    public static ClientAsteroidObservation Unavailable(
        string status,
        uint auxDataLookupAddress = 0)
    {
        return new ClientAsteroidObservation
        {
            Status = status,
            AuxDataLookupAddress =
                auxDataLookupAddress,
        };
    }

    public static ClientAsteroidObservation NotApplicable()
    {
        return new ClientAsteroidObservation
        {
            IsAvailable = true,
            Status = "Target is not a harvestable",
        };
    }
}
