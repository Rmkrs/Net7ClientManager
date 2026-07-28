namespace Net7ClientManager.Observations.Models;

public sealed record ClientStarbaseRoomObservation
{
    public int RoomClass { get; init; } = -1;

    /*
     * StarbaseDefinition.RoomMap is keyed by a native definition key, not by
     * StarbaseView's operational room class. DAT_00B90F3C translates the
     * room class to this key. Keeping both identities prevents the native
     * room 2/3 mapping from being silently reversed.
     */
    public int DefinitionKey { get; init; } = -1;

    public uint DefinitionAddress { get; init; }

    public uint ControllerAddress { get; init; }

    public bool IsCurrent { get; init; }

    public string Status { get; init; } = "";

    public IReadOnlyList<ClientStarbaseFacilityObservation> Facilities
    { get; init; } = [];

    public IReadOnlyList<ClientStarbaseNpcObservation> Npcs
    { get; init; } = [];
}
