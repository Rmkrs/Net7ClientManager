namespace Net7ClientManager.Navigation;

internal sealed record ForgeNavigationHarvestableVariantDocument
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public byte RawObjectType { get; init; }

    public int TechLevel { get; init; }

    public required string Category { get; init; }
}
