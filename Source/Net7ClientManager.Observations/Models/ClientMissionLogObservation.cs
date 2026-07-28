namespace Net7ClientManager.Observations.Models;

public sealed record ClientMissionLogObservation
{
    public bool IsAvailable { get; init; }

    public string Status { get; init; } = "";

    public uint Address { get; init; }

    public uint ValidState { get; init; }

    public int Capacity { get; init; }

    public IReadOnlyList<ClientMissionObservation>
        Missions
    { get; init; } = [];

    public int OccupiedSlotCount =>
        this.Missions.Count;

    public ClientMissionObservation? GetBySlot(
        int slot)
    {
        return this.Missions.FirstOrDefault(
            mission =>
                mission.Slot == slot);
    }

    public ClientMissionObservation? GetByAddress(
        uint address)
    {
        return address != 0
            ? this.Missions.FirstOrDefault(
                mission =>
                    mission.Address == address)
            : null;
    }

    public static ClientMissionLogObservation Unavailable(
        string status,
        uint address = 0)
    {
        return new ClientMissionLogObservation
        {
            Status = status,
            Address = address,
        };
    }
}
