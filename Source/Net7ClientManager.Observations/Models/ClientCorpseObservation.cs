namespace Net7ClientManager.Observations.Models;

public sealed record ClientCorpseObservation
{
    public bool IsAvailable { get; init; }

    public string Status { get; init; } = "";

    public uint AuxDataLookupAddress { get; init; }

    public int MaximumObservedSlotCount { get; init; }

    public int PresentSlotCount { get; init; }

    public int ValidSlotCount { get; init; }

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

    public IReadOnlyList<ClientCorpseCargoSlotObservation> Slots
    { get; init; } = [];

    public IReadOnlyList<ClientCorpseCargoSlotObservation> LootItems =>
    [
        .. this.Slots.Where(
            slot =>
                slot.IsOccupied),
    ];

    public int OccupiedSlotCount =>
        this.Slots.Count(
            slot =>
                slot.IsOccupied);

    public int EmptyValidSlotCount =>
        this.Slots.Count(
            slot =>
                slot.IsPresent &&
                slot.IsValid &&
                !slot.IsOccupied);

    public bool HasLoot =>
        this.OccupiedSlotCount > 0;

    // A hydrated corpse consistently exposes 39 valid cargo slots and one
    // permanently invalid slot. Zero valid slots means "not requested yet",
    // never an empty corpse.
    public bool IsHydrated =>
        this.IsAvailable &&
        this.PresentSlotCount > 0 &&
        this.ValidSlotCount > 0 &&
        this.InvalidSlotCount <= 1;

    public bool IsKnownEmpty =>
        this.IsHydrated &&
        this.OccupiedSlotCount == 0;

    public static ClientCorpseObservation Unavailable(
        string status,
        uint auxDataLookupAddress = 0)
    {
        return new ClientCorpseObservation
        {
            Status = status,
            AuxDataLookupAddress =
                auxDataLookupAddress,
        };
    }

    public static ClientCorpseObservation NotApplicable()
    {
        return new ClientCorpseObservation
        {
            IsAvailable = true,
            Status = "Target is not a corpse",
        };
    }
}
