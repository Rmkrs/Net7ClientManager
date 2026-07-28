namespace Net7ClientManager.Navigation;

internal sealed record ForgeNavigationMobLootDocument
{
    public required string Id { get; init; }

    public required string MobVariantId { get; init; }

    public int ItemTemplateId { get; init; }

    public required string Confidence { get; init; }

    public bool HasAnonymousReports { get; init; }

    public IReadOnlyList<string> NamedReporters { get; init; } = [];
}
