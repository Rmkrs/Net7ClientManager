namespace Net7ClientManager.Observations.Models;

public sealed record ClientBuffsObservation
{
    public bool IsAvailable { get; init; }

    public string Status { get; init; } = "";

    public uint AuxDataLookupAddress { get; init; }

    public int MaximumSlotCount { get; init; }

    public IReadOnlyList<ClientBuffObservation> Slots
    { get; init; } = [];

    public IReadOnlyList<ClientBuffObservation> ActiveBuffs =>
    [
        .. this.Slots.Where(
            slot =>
                slot.IsOccupied),
    ];

    public int ActiveBuffCount =>
        this.Slots.Count(
            slot =>
                slot.IsOccupied);

    public int PermanentBuffCount =>
        this.Slots.Count(
            slot =>
                slot.IsOccupied &&
                slot.IsPermanent == true);

    public int TimedBuffCount =>
        this.Slots.Count(
            slot =>
                slot.IsOccupied &&
                slot.IsPermanent == false);

    public int NominallyExpiredBuffCount =>
        this.Slots.Count(
            slot =>
                slot.IsNominallyExpired);

    public static ClientBuffsObservation Unavailable(
        string status,
        uint auxDataLookupAddress = 0)
    {
        return new ClientBuffsObservation
        {
            Status = status,
            AuxDataLookupAddress =
                auxDataLookupAddress,
        };
    }
}
