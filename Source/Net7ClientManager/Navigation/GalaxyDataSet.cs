namespace Net7ClientManager.Navigation;

public sealed record GalaxyDataSet
{
    internal GalaxyDataSet(
        ForgeNavigationEntitySnapshotDocument snapshot,
        ForgeNavigationDataDocument document,
        long authorityRevision,
        string datasetEpoch,
        string source,
        string snapshotSha256,
        string packageSha256,
        GalaxyTopology topology,
        GalaxyNavigationCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(datasetEpoch);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotSha256);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageSha256);
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(catalog);

        this.Snapshot = snapshot;
        this.Document = document;
        this.AuthorityRevision = authorityRevision;
        this.DatasetEpoch = datasetEpoch;
        this.Source = source;
        this.SnapshotSha256 = snapshotSha256;
        this.PackageSha256 = packageSha256;
        this.Topology = topology;
        this.Catalog = catalog;
    }

    internal ForgeNavigationEntitySnapshotDocument Snapshot { get; }

    internal ForgeNavigationDataDocument Document { get; }

    public long Revision => this.Snapshot.Revision;

    public long AuthorityRevision { get; }

    public int ContractVersion => this.Snapshot.ContractVersion;

    public int NpcCount => this.Document.Npcs.Count;

    public int StationFacilityCount => this.Document.StationFacilities.Count;

    public int VendorItemCount => this.Document.VendorItems.Count;

    public int MobVariantCount => this.Document.MobVariants.Count;

    public int MobClusterCount => this.Document.MobClusters.Count;

    public int MobLootCount => this.Document.MobLoot.Count;

    public int HarvestableVariantCount => this.Document.HarvestableVariants.Count;

    public int HarvestableFieldCount => this.Document.HarvestableFields.Count;

    public int HarvestableResourceCount => this.Document.HarvestableResources.Count;

    public int GravityWellCount => this.Document.GravityWells.Count;

    public string DatasetEpoch { get; }

    public string Source { get; }

    public string SnapshotSha256 { get; }

    public string PackageSha256 { get; }

    public GalaxyTopology Topology { get; }

    public GalaxyNavigationCatalog Catalog { get; }
}
