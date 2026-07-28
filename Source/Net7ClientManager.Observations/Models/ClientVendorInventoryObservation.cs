namespace Net7ClientManager.Observations.Models;

public sealed record ClientVendorInventoryObservation
{
    public bool IsAvailable { get; init; }

    public string Status { get; init; } = "";

    public uint HullAuxDataAddress { get; init; }

    public uint VendorInventoryAddress { get; init; }

    public uint AuxDataLookupAddress { get; init; }

    public int LookupTraversalNodeCount { get; init; }

    public ulong? CurrentCredits { get; init; }

    public int ReadErrorCount { get; init; }

    public IReadOnlyList<ClientVendorInventoryItemObservation> Slots
    { get; init; } = [];

    public IReadOnlyList<ClientVendorInventoryItemObservation> Items =>
    [
        .. this.Slots.Where(
            slot => slot.IsOccupied),
    ];

    public bool HasLoadedSnapshot =>
        this.Items.Count > 0;

    public int OccupiedSlotCount =>
        this.Items.Count;

    public int UnavailableSlotCount =>
        this.Slots.Count(
            slot =>
                slot.IsPresent &&
                slot.IsValid &&
                slot.ItemTemplateId == -2);

    public int UnknownPriceCount =>
        this.Items.Count(
            item => !item.Price.HasValue);

    public int? AffordableItemCount =>
        this.CurrentCredits.HasValue
            ? this.Items.Count(
                item => item.IsAffordable == true)
            : null;

    public int? UnaffordableItemCount =>
        this.CurrentCredits.HasValue
            ? this.Items.Count(
                item => item.IsAffordable == false)
            : null;

    public static ClientVendorInventoryObservation Unavailable(
        string status,
        uint hullAuxDataAddress = 0,
        uint vendorInventoryAddress = 0,
        uint auxDataLookupAddress = 0,
        int lookupTraversalNodeCount = 0)
    {
        return new ClientVendorInventoryObservation
        {
            Status = status,
            HullAuxDataAddress = hullAuxDataAddress,
            VendorInventoryAddress = vendorInventoryAddress,
            AuxDataLookupAddress = auxDataLookupAddress,
            LookupTraversalNodeCount = lookupTraversalNodeCount,
        };
    }
}
