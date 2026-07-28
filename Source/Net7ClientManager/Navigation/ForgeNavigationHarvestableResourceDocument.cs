namespace Net7ClientManager.Navigation;

internal sealed record ForgeNavigationHarvestableResourceDocument
{
    public required string Id { get; init; }

    public required string HarvestableFieldId { get; init; }

    public required string HarvestableVariantId { get; init; }

    public int ItemTemplateId { get; init; }

    public required string Confidence { get; init; }

    public bool HasAnonymousReports { get; init; }

    public IReadOnlyList<string> NamedReporters { get; init; } = [];
}
