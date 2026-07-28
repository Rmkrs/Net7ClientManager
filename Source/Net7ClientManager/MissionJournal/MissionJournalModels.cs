namespace Net7ClientManager.MissionJournal;

using System.Globalization;

public enum MissionJournalSource
{
    Unknown = 0,
    Npc = 1,
    JobTerminal = 2,
}

public enum MissionJournalJobCategory
{
    Unknown = 0,
    Combat = 1,
    Trade = 2,
    Explore = 3,
}

public enum MissionJournalStatus
{
    Active = 0,
    Completed = 1,
    Forfeited = 2,
    Failed = 3,
    Expired = 4,
    NoLongerActive = 5,
}

public enum MissionJournalEventKind
{
    Observed = 0,
    Accepted = 1,
    SourceIdentified = 2,
    Progressed = 3,
    Completed = 4,
    Forfeited = 5,
    Failed = 6,
    Expired = 7,
    NoLongerActive = 8,
}

public sealed record MissionJournalEvent
{
    public required long EventId { get; init; }

    public required DateTimeOffset OccurredAt { get; init; }

    public required MissionJournalEventKind Kind { get; init; }

    public int? Stage { get; init; }

    public string Objective { get; init; } = "";

    public string SystemName { get; init; } = "";

    public string SectorName { get; init; } = "";

    public string StarbaseName { get; init; } = "";

    public string Details { get; init; } = "";
}

public sealed record MissionJournalEntry
{
    public required string EpisodeId { get; init; }

    public required uint CharacterId { get; init; }

    public required string PilotName { get; init; }

    public required MissionJournalSource Source { get; init; }

    public required MissionJournalStatus Status { get; init; }

    public uint? JobId { get; init; }

    public MissionJournalJobCategory JobCategory { get; init; }

    public int? MissionRawId { get; init; }

    public int? MissionStartTime { get; init; }

    public required string SemanticFingerprint { get; init; }

    public required string Name { get; init; }

    public string Summary { get; init; } = "";

    public string RewardText { get; init; } = "";

    public string FailureConsequence { get; init; } = "";

    public string IssuingFaction { get; init; } = "";

    public DateTimeOffset? AcceptedAt { get; init; }

    public required DateTimeOffset FirstObservedAt { get; init; }

    public required DateTimeOffset LastObservedAt { get; init; }

    public DateTimeOffset? EndedAt { get; init; }

    public int? Stage { get; init; }

    public int? StageCount { get; init; }

    public string CurrentObjective { get; init; } = "";

    public string AcceptedSystem { get; init; } = "";

    public string AcceptedSector { get; init; } = "";

    public string AcceptedStarbase { get; init; } = "";

    public string IssuerNpcName { get; init; } = "";

    public string CompletionSystem { get; init; } = "";

    public string CompletionSector { get; init; } = "";

    public string CompletionStarbase { get; init; } = "";

    public IReadOnlyList<MissionJournalEvent> Events { get; init; } = [];

    public DateTimeOffset DisplayStartedAt =>
        this.AcceptedAt ?? this.FirstObservedAt;

    public TimeSpan? Duration => this.EndedAt.HasValue
        ? this.EndedAt.Value - this.DisplayStartedAt
        : null;

    public string TypeDisplay => this.Source switch
    {
        MissionJournalSource.JobTerminal => this.JobCategory switch
        {
            MissionJournalJobCategory.Combat => "Combat job",
            MissionJournalJobCategory.Trade => "Trade job",
            MissionJournalJobCategory.Explore => "Explore job",
            _ => "Job",
        },
        MissionJournalSource.Npc => "NPC mission",
        _ => "Mission",
    };

    public string StatusDisplay => this.Status switch
    {
        MissionJournalStatus.Active => "Active",
        MissionJournalStatus.Completed => "Completed",
        MissionJournalStatus.Forfeited => "Forfeited",
        MissionJournalStatus.Failed => "Failed",
        MissionJournalStatus.Expired => "Expired",
        MissionJournalStatus.NoLongerActive => "No longer active",
        _ => this.Status.ToString(),
    };

    public string DisplayDuration =>
        this.Status == MissionJournalStatus.NoLongerActive
            ? ""
            : this.Duration.HasValue
                ? FormatDuration(this.Duration.Value)
                : this.Status == MissionJournalStatus.Active
                    ? "Active"
                    : "";

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            duration = TimeSpan.Zero;
        }

        if (duration.TotalDays >= 1)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{(int)duration.TotalDays}d {duration.Hours}h");
        }

        if (duration.TotalHours >= 1)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{(int)duration.TotalHours}h {duration.Minutes}m");
        }

        if (duration.TotalMinutes >= 1)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{(int)duration.TotalMinutes}m");
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{Math.Max(0, (int)duration.TotalSeconds)}s");
    }
}

public sealed class MissionJournalChangedEventArgs(uint characterId) : EventArgs
{
    public uint CharacterId { get; } = characterId;
}

internal sealed class MissionJournalLifecycleEventArgs : EventArgs
{
    public required MissionJournalEntry Entry { get; init; }

    public required MissionJournalEventKind Kind { get; init; }

    public required DateTimeOffset OccurredAt { get; init; }

    public string SystemName { get; init; } = "";

    public string SectorName { get; init; } = "";

    public string StarbaseName { get; init; } = "";
}

internal sealed record MissionJournalActiveContext
{
    public required string EpisodeId { get; init; }

    public required MissionJournalSource Source { get; init; }

    public uint? JobId { get; init; }

    public MissionJournalJobCategory JobCategory { get; init; }

    public string RewardText { get; init; } = "";

    public DateTimeOffset? AcceptedAt { get; init; }

    public string AcceptedSystem { get; init; } = "";

    public string AcceptedSector { get; init; } = "";

    public string AcceptedStarbase { get; init; } = "";
}
