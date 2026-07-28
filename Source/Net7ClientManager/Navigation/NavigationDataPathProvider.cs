namespace Net7ClientManager.Navigation;

internal sealed class NavigationDataPathProvider
{
    public NavigationDataPathProvider()
    {
        var applicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData);
        this.RootDirectory = Path.Combine(
            applicationData,
            "Net7ClientManager",
            "NavigationData");
        this.PackageDirectory = Path.Combine(
            this.RootDirectory,
            "Packages");
        this.SnapshotDirectory = Path.Combine(
            this.RootDirectory,
            "Snapshots");
        this.DownloadDirectory = Path.Combine(
            this.RootDirectory,
            "Downloads");
        this.StatePath = Path.Combine(
            this.RootDirectory,
            "state.json");
        this.ProductionRecipeCatalogPath = Path.Combine(
            this.RootDirectory,
            "production-recipes.json");
        this.MissionCatalogPath = Path.Combine(
            this.RootDirectory,
            "missions.json");
        this.BundledPackageDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "BundledData");
    }

    public string RootDirectory { get; }

    public string PackageDirectory { get; }

    public string SnapshotDirectory { get; }

    public string DownloadDirectory { get; }

    public string StatePath { get; }

    public string ProductionRecipeCatalogPath { get; }

    public string MissionCatalogPath { get; }

    public string BundledPackageDirectory { get; }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(this.RootDirectory);
        Directory.CreateDirectory(this.PackageDirectory);
        Directory.CreateDirectory(this.SnapshotDirectory);
        Directory.CreateDirectory(this.DownloadDirectory);
    }
}
