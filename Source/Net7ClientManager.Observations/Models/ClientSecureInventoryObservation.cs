namespace Net7ClientManager.Observations.Models;

public sealed record ClientSecureInventoryObservation
{
    public const int ExpectedSlotCount = 96;

    public bool IsAvailable { get; init; }

    public string Status { get; init; } = "";

    public uint BindingAddress { get; init; }

    public uint PropertyAddress { get; init; }

    public uint SlotVectorAddress { get; init; }

    public int ReadErrorCount { get; init; }

    public IReadOnlyList<ClientInventoryItemObservation> Slots
    { get; init; } = [];

    public IReadOnlyList<ClientInventoryItemObservation> Items =>
    [
        .. this.Slots.Where(
            slot => slot.IsOccupied),
    ];

    public int OccupiedSlotCount =>
        this.Slots.Count(
            slot => slot.IsOccupied);

    public int FreeSlotCount =>
        this.Slots.Count(
            slot => slot.IsUsableEmpty);

    public int UnavailableSlotCount =>
        this.Slots.Count(
            slot => slot.IsUnavailable);

    public static ClientSecureInventoryObservation Unavailable(
        string status,
        uint bindingAddress = 0,
        uint propertyAddress = 0,
        uint slotVectorAddress = 0)
    {
        return new ClientSecureInventoryObservation
        {
            Status = status,
            BindingAddress = bindingAddress,
            PropertyAddress = propertyAddress,
            SlotVectorAddress = slotVectorAddress,
        };
    }
}
