namespace Net7ClientManager.Navigation;

internal sealed record ForgeMissionCatalogResponse
{
    public long Revision { get; init; }

    public DateTimeOffset GeneratedAtUtc { get; init; }

    public IReadOnlyList<ForgeMissionCatalogItem> Missions
    { get; init; } = [];
}

internal sealed record ForgeMissionCatalogItem
{
    public required string Id { get; init; }

    public required string SemanticFingerprint { get; init; }

    public required string Name { get; init; }

    public string Summary { get; init; } = "";

    public string RewardText { get; init; } = "";

    public string FailureConsequence { get; init; } = "";

    public string IssuingFaction { get; init; } = "";

    public int StageCount { get; init; }

    public bool? IsTimed { get; init; }

    public bool? IsForfeitable { get; init; }

    public IReadOnlyList<ForgeMissionStage> Stages { get; init; } = [];

    public IReadOnlyList<string> IssuerNpcIds { get; init; } = [];

    public IReadOnlyList<string> CompletionNpcIds { get; init; } = [];

    public IReadOnlyList<ForgeMissionCompletionLocation>
        CompletionLocations { get; init; } = [];

    public ForgeMissionReward? Reward { get; init; }

    public required string Confidence { get; init; }

    public bool HasAnonymousReports { get; init; }

    public IReadOnlyList<string> NamedReporters { get; init; } = [];
}

internal sealed record ForgeMissionStage
{
    public int Index { get; init; }

    public string Text { get; init; } = "";

    public bool? IsTimed { get; init; }
}

internal sealed record ForgeMissionCompletionLocation
{
    public string SectorName { get; init; } = "";

    public string StationName { get; init; } = "";
}

internal sealed record ForgeMissionReward
{
    public long Credits { get; init; }

    public int CombatExperience { get; init; }

    public int ExploreExperience { get; init; }

    public int TradeExperience { get; init; }

    public IReadOnlyList<ForgeMissionReputationReward> Reputation
    { get; init; } = [];

    public IReadOnlyList<ForgeMissionItemReward> Items
    { get; init; } = [];
}

internal sealed record ForgeMissionReputationReward
{
    public required string FactionKey { get; init; }

    public string DisplayName { get; init; } = "";

    public float ReactionDelta { get; init; }
}

internal sealed record ForgeMissionItemReward
{
    public int ItemTemplateId { get; init; }

    public int Quantity { get; init; }
}

internal sealed record ForgeMissionCatalogSnapshot
{
    public static ForgeMissionCatalogSnapshot Unavailable(
        string status = "Forge mission data is not available.")
    {
        return new ForgeMissionCatalogSnapshot
        {
            Status = status,
        };
    }

    public bool IsAvailable { get; init; }

    public string Status { get; init; } = "";

    public long Revision { get; init; }

    public DateTimeOffset GeneratedAtUtc { get; init; }

    public string Sha256 { get; init; } = "";

    public IReadOnlyList<ForgeMissionCatalogItem> Missions
    { get; init; } = [];
}
