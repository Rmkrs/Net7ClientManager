namespace Net7ClientManager.Observations.Models;

public sealed record ClientStarbaseRoomControllerObservation
{
    public uint Address { get; init; }

    public int RoomClass { get; init; } = -1;

    public uint RoomDefinitionAddress { get; init; }

    public bool IsCurrent { get; init; }

    public string Status { get; init; } = "";
}
