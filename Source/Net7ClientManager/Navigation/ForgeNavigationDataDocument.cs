namespace Net7ClientManager.Navigation;

internal sealed record ForgeNavigationDataDocument
{
    public int ContractVersion { get; init; } =
        ForgeNavigationEntitySnapshotDocument.CurrentContractVersion;

    public long Revision { get; init; }

    public DateTimeOffset GeneratedAt { get; init; }

    public IReadOnlyList<ForgeNavigationSectorDocument> Sectors { get; init; } = [];

    public IReadOnlyList<ForgeNavigationNpcDocument> Npcs { get; init; } = [];

    public IReadOnlyList<ForgeNavigationStationFacilityDocument> StationFacilities
    { get; init; } = [];

    public IReadOnlyList<ForgeNavigationVendorItemDocument> VendorItems
    { get; init; } = [];

    public IReadOnlyList<ForgeNavigationMobVariantDocument> MobVariants
    { get; init; } = [];

    public IReadOnlyList<ForgeNavigationMobEncounterClusterDocument> MobClusters
    { get; init; } = [];

    public IReadOnlyList<ForgeNavigationMobLootDocument> MobLoot
    { get; init; } = [];

    public IReadOnlyList<ForgeNavigationHarvestableVariantDocument> HarvestableVariants
    { get; init; } = [];

    public IReadOnlyList<ForgeNavigationHarvestableFieldDocument> HarvestableFields
    { get; init; } = [];

    public IReadOnlyList<ForgeNavigationHarvestableResourceDocument> HarvestableResources
    { get; init; } = [];

    public IReadOnlyList<ForgeNavigationGravityWellDocument> GravityWells
    { get; init; } = [];
}
