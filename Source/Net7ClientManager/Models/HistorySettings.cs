namespace Net7ClientManager.Models;

public sealed class HistorySettings
{
    public bool RecordMissionHistory { get; set; } = true;

    public bool RecordActivityHistory { get; set; }

    public bool RecordCombatHistory { get; set; }
}
