namespace Net7ClientManager.Observations.Models;

public sealed record ClientRewardOverflowInventoryObservation
{
    public const int RewardSlotCount = 1;

    public const int OverflowSlotCount = 8;

    public bool IsAvailable { get; init; }

    public string Status { get; init; } = "";

    public uint HullAuxDataAddress { get; init; }

    public uint RewardInventoryAddress { get; init; }

    public uint OverflowInventoryAddress { get; init; }

    public uint AuxDataLookupAddress { get; init; }

    public int LookupTraversalNodeCount { get; init; }

    public int ReadErrorCount { get; init; }

    public IReadOnlyList<ClientInventoryItemObservation> RewardSlots
    { get; init; } = [];

    public IReadOnlyList<ClientInventoryItemObservation> OverflowSlots
    { get; init; } = [];

    public IReadOnlyList<ClientInventoryItemObservation> RewardItems =>
    [
        .. this.RewardSlots.Where(
            slot => slot.IsOccupied),
    ];

    public IReadOnlyList<ClientInventoryItemObservation> OverflowItems =>
    [
        .. this.OverflowSlots.Where(
            slot => slot.IsOccupied),
    ];

    public int RewardOccupiedSlotCount =>
        this.RewardItems.Count;

    public int OverflowOccupiedSlotCount =>
        this.OverflowItems.Count;

    public int RewardUnavailableSlotCount =>
        this.RewardSlots.Count(
            slot => slot.IsUnavailable);

    public int OverflowUnavailableSlotCount =>
        this.OverflowSlots.Count(
            slot => slot.IsUnavailable);

    public static ClientRewardOverflowInventoryObservation Unavailable(
        string status,
        uint hullAuxDataAddress = 0,
        uint rewardInventoryAddress = 0,
        uint overflowInventoryAddress = 0,
        uint auxDataLookupAddress = 0,
        int lookupTraversalNodeCount = 0)
    {
        return new ClientRewardOverflowInventoryObservation
        {
            Status = status,
            HullAuxDataAddress = hullAuxDataAddress,
            RewardInventoryAddress = rewardInventoryAddress,
            OverflowInventoryAddress = overflowInventoryAddress,
            AuxDataLookupAddress = auxDataLookupAddress,
            LookupTraversalNodeCount = lookupTraversalNodeCount,
        };
    }
}
