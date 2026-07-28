namespace Net7ClientManager.Contributions;

internal sealed record ForgeProductionRecipeContributionRequest
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

    public IReadOnlyList<ForgeProductionRecipeContributionItem> Recipes
    { get; init; } = [];

    public string Signature { get; init; } = "";
}

internal sealed record ForgeProductionRecipeContributionItem
{
    public int Kind { get; init; }

    public int OutputItemTemplateId { get; init; }

    public required string RecipeFingerprint { get; init; }

    public IReadOnlyList<ForgeProductionRecipeIngredient> Ingredients
    { get; init; } = [];
}

internal sealed record ForgeProductionRecipeIngredient
{
    public int ItemTemplateId { get; init; }

    public int Quantity { get; init; }
}

internal sealed record ForgeProductionRecipeContributionResponse(
    string RequestId,
    int Received,
    int AlreadyCanonical,
    int EvidenceAccepted,
    int Conflicts,
    int Created,
    long CatalogRevision,
    string Status);
