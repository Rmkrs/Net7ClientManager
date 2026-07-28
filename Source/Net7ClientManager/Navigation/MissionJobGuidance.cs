namespace Net7ClientManager.Navigation;

using System.Globalization;
using Net7ClientManager.MissionJournal;

public sealed record MissionJobGuidance
{
    public required string MissionName { get; init; }

    public string Summary { get; init; } = "";

    public string Objective { get; init; } = "";

    public int? Stage { get; init; }

    public int? StageCount { get; init; }

    public string Reward { get; init; } = "";

    public MissionJournalJobCategory JobCategory { get; init; }

    public DateTimeOffset? AcceptedAt { get; init; }

    public string AcceptedSystem { get; init; } = "";

    public string AcceptedSector { get; init; } = "";

    public string AcceptedStarbase { get; init; } = "";

    public MissionJobDestinationResolution? Destination { get; init; }

    public string Fingerprint => string.Join(
        "|",
        this.MissionName,
        this.Summary,
        this.Objective,
        this.Stage?.ToString(CultureInfo.InvariantCulture) ?? "",
        this.StageCount?.ToString(CultureInfo.InvariantCulture) ?? "",
        this.Reward,
        this.JobCategory.ToString(),
        this.AcceptedAt?.ToString("O", CultureInfo.InvariantCulture) ?? "",
        this.AcceptedSystem,
        this.AcceptedSector,
        this.AcceptedStarbase,
        this.Destination?.Fingerprint ?? "");
}
