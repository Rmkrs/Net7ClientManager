namespace Net7ClientManager.Navigation;

using Net7ClientManager.ActivityJournal;

public sealed record FactionReactionPresentation
{
    public required string DefeatedFactionKey { get; init; }

    public required string DefeatedFactionName { get; init; }

    public required float Multiplier { get; init; }

    public bool IsEstimated { get; init; }
}

public sealed record FactionDetailsPresentation
{
    public static FactionDetailsPresentation Hidden { get; } = new();

    public bool IsVisible { get; init; }

    public string FactionKey { get; init; } = "";

    public string DisplayName { get; init; } = "";

    public string Description { get; init; } = "";

    public float? Reputation { get; init; }

    public bool IsAffiliated { get; init; }

    public IReadOnlyList<FactionReactionPresentation> HelpfulKills
    { get; init; } = [];

    public IReadOnlyList<FactionReactionPresentation> HarmfulKills
    { get; init; } = [];

    public IReadOnlyList<ReputationJournalEntry> RecentChanges
    { get; init; } = [];

    public string Fingerprint => string.Join(
        "|",
        this.IsVisible,
        this.FactionKey,
        this.DisplayName,
        this.Description,
        this.Reputation?.ToString("R", System.Globalization.CultureInfo.InvariantCulture) ?? "",
        this.IsAffiliated,
        string.Join(
            ",",
            this.HelpfulKills.Select(rule =>
                $"{rule.DefeatedFactionKey}:{rule.Multiplier:R}:{rule.IsEstimated}")),
        string.Join(
            ",",
            this.HarmfulKills.Select(rule =>
                $"{rule.DefeatedFactionKey}:{rule.Multiplier:R}:{rule.IsEstimated}")),
        string.Join(
            ",",
            this.RecentChanges.Select(entry =>
                string.Join(
                    ":",
                    entry.ReputationEventId,
                    entry.OccurredAt.ToUnixTimeMilliseconds(),
                    entry.PreviousReaction.ToString(
                        "R",
                        System.Globalization.CultureInfo.InvariantCulture),
                    entry.CurrentReaction.ToString(
                        "R",
                        System.Globalization.CultureInfo.InvariantCulture),
                    entry.Reason,
                    entry.SystemName,
                    entry.SectorName,
                    entry.StarbaseName,
                    entry.NearestNavName))));
}
