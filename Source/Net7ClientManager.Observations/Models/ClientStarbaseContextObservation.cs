namespace Net7ClientManager.Observations.Models;

public sealed record ClientStarbaseContextObservation
{
    public bool IsAvailable { get; init; }

    public string Status { get; init; } = "";

    public uint OwnerClientContextAddress { get; init; }

    public uint StarbaseViewAddress { get; init; }

    public uint StarbaseId { get; init; }

    public uint StarbaseDefinitionAddress { get; init; }

    public uint StarbaseDefinitionId { get; init; }

    public int CurrentRoomClass { get; init; } = -1;

    public int PendingRoomClass { get; init; } = -1;

    public int PreviousRoomClass { get; init; } = -1;

    public uint CurrentRoomDisplayControllerAddress { get; init; }

    /*
     * Diagnostics-only current-room controller. The operational current room
     * comes directly from StarbaseView +0x50. This reverse-resolved controller
     * remains available for the bounded flight recorder and transient research.
     */
    public uint ControllerAddress { get; init; }

    public string ControllerResolutionStatus { get; init; } = "";

    public int ReadErrorCount { get; init; }

    public IReadOnlyList<ClientStarbaseRoomObservation> Rooms
    { get; init; } = [];

    public ClientStarbaseInteractionObservation Interaction
    { get; init; } = ClientStarbaseInteractionObservation.None;

    public ClientStarbaseDiagnosticsObservation Diagnostics
    { get; init; } = new();

    public ClientStarbaseRoomObservation? CurrentRoom =>
        this.Rooms.FirstOrDefault(
            room => room.RoomClass == this.CurrentRoomClass);

    public static ClientStarbaseContextObservation Unavailable(
        string status)
    {
        return new ClientStarbaseContextObservation
        {
            Status = status,
        };
    }
}
