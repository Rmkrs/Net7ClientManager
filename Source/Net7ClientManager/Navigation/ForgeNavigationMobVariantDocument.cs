namespace Net7ClientManager.Navigation;

internal sealed record ForgeNavigationMobVariantDocument
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public byte RawObjectType { get; init; }

    public int CombatLevel { get; init; }

    public string FactionIdentifier { get; init; } = "";

    public ForgeNavigationEncounterFactionBindingKind FactionBindingKind
    { get; init; } = ForgeNavigationEncounterFactionBindingKind.Unknown;

    public int? IntrinsicRelationshipRaw { get; init; }

    public ForgeNavigationEncounterResolvedDisposition? IntrinsicDisposition
    { get; init; }

    public bool IsOrganic { get; init; }
}
