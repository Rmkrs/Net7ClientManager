namespace Net7ClientManager.Observations.Models;

public sealed record ClientStarbaseInteractionObservation
{
    public static ClientStarbaseInteractionObservation None { get; } =
        new()
        {
            Status = "No starbase interaction is active",
        };

    public ClientStarbaseInteractionKind Kind { get; init; }

    public string Status { get; init; } = "";

    public int RoomClass { get; init; } = -1;

    public int FacilitySlot { get; init; } = -1;

    public int? FacilityType { get; init; }

    public string FacilityName { get; init; } = "";

    public int NpcSlot { get; init; } = -1;

    public string NpcName { get; init; } = "";

    public uint NpcNameAddress { get; init; }

    public bool IsActive =>
        this.Kind != ClientStarbaseInteractionKind.None;
}
