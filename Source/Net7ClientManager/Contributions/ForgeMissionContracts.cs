namespace Net7ClientManager.Contributions;

internal sealed record ForgeMissionContributionRequest
{
    public int ProtocolVersion { get; init; } = 1;

    public required string ContributorId { get; init; }

    public required string RequestId { get; init; }

    public DateTimeOffset SubmittedAtUtc { get; init; }

    public DateTimeOffset FirstObservedAtUtc { get; init; }

    public DateTimeOffset LastObservedAtUtc { get; init; }

    public int ObservationCount { get; init; }

    public required string ClientVersion { get; init; }

    public long DatasetRevision { get; init; }

    public required string Attribution { get; init; }

    public required string LivePilotName { get; init; }

    public IReadOnlyList<ForgeMissionContributionItem> Missions { get; init; } = [];

    public string Signature { get; init; } = "";
}

internal sealed record ForgeMissionContributionItem
{
    public required string SemanticFingerprint { get; init; }

    public required string EvidenceKind { get; init; }

    public required string Name { get; init; }

    public string Summary { get; init; } = "";

    public string RewardText { get; init; } = "";

    public string FailureConsequence { get; init; } = "";

    public string IssuingFaction { get; init; } = "";

    public int StageCount { get; init; }

    public bool? IsTimed { get; init; }

    public bool? IsForfeitable { get; init; }

    public IReadOnlyList<ForgeMissionStageContributionItem> Stages { get; init; } = [];

    public string IssuerNpcId { get; init; } = "";

    public string CompletionNpcId { get; init; } = "";

    public string CompletionSectorName { get; init; } = "";

    public string CompletionStationName { get; init; } = "";

    public ForgeMissionRewardContribution? Reward { get; init; }
}

internal sealed record ForgeMissionStageContributionItem
{
    public int Index { get; init; }

    public string Text { get; init; } = "";

    public bool? IsTimed { get; init; }
}

internal sealed record ForgeMissionRewardContribution
{
    public long Credits { get; init; }

    public int CombatExperience { get; init; }

    public int ExploreExperience { get; init; }

    public int TradeExperience { get; init; }

    public IReadOnlyList<ForgeMissionReputationRewardContribution> Reputation { get; init; } = [];

    public IReadOnlyList<ForgeMissionItemRewardContribution> Items { get; init; } = [];
}

internal sealed record ForgeMissionReputationRewardContribution
{
    public required string FactionKey { get; init; }

    public string DisplayName { get; init; } = "";

    public float ReactionDelta { get; init; }
}

internal sealed record ForgeMissionItemRewardContribution
{
    public int ItemTemplateId { get; init; }

    public int Quantity { get; init; }
}

internal sealed record ForgeMissionContributionResponse(
    string RequestId,
    int Received,
    int AlreadyCanonical,
    int EvidenceAccepted,
    int Conflicts,
    int Created,
    int Strengthened,
    long CatalogRevision,
    string Status);
