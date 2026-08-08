namespace Net7ClientManager.Models;

public sealed class PilotArchiveSettings
{
    public uint? SelectedCharacterId { get; set; }

    public string SelectedSection { get; set; } = "overview";

    public int? RosterSplitterDistance { get; set; }

    public int? MissionHistorySplitterDistance { get; set; }

    public int? ActivityHistorySplitterDistance { get; set; }

    public int? CombatHistorySplitterDistance { get; set; }

    public int? ReputationHistorySplitterDistance { get; set; }

    public bool ShowActivityNavigation { get; set; } = true;

    public bool ShowActivityMissions { get; set; } = true;

    public bool ShowActivityReputation { get; set; } = true;

    public bool ShowActivityCredits { get; set; } = true;

    public bool ShowActivityLoot { get; set; } = true;

    public bool ShowActivityCombat { get; set; } = true;

    public bool ShowActivityCrafting { get; set; } = true;

    public Dictionary<string, PilotArchiveGridSortSettings> GridSorts
    { get; set; } = new(StringComparer.Ordinal);

    public void EnsureDefaults()
    {
        this.SelectedSection = string.IsNullOrWhiteSpace(this.SelectedSection)
            ? "overview"
            : this.SelectedSection.Trim();
        this.GridSorts = this.GridSorts == null
            ? new Dictionary<string, PilotArchiveGridSortSettings>(
                StringComparer.Ordinal)
            : new Dictionary<string, PilotArchiveGridSortSettings>(
                this.GridSorts,
                StringComparer.Ordinal);
    }
}

public sealed class PilotArchiveGridSortSettings
{
    public int ColumnIndex { get; set; } = -1;

    public bool Descending { get; set; }
}
