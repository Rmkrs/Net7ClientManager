namespace Net7ClientManager.Addons.Loading;

public sealed class AddonPathProvider
{
    public AddonPathProvider()
    {
        var applicationDirectory = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData),
            "Net7ClientManager");

        this.LegacyRootDirectory = Path.Combine(
            applicationDirectory,
            "Addons");

        this.DevelopmentRootDirectory = Path.Combine(
            applicationDirectory,
            "AddonDevelopment");

        this.PackageCacheDirectory = Path.Combine(
            applicationDirectory,
            "AddonPackages");

        this.InstallationStatePath = Path.Combine(
            applicationDirectory,
            "installed-addons.json");

        this.RegistryCatalogCachePath = Path.Combine(
            applicationDirectory,
            "addon-registry-cache.json");

        this.BundledPackageDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "BundledAddons");

        this.StorageRootDirectory = Path.Combine(
            applicationDirectory,
            "AddonStorage");

        this.MissionGuideCacheDirectory = Path.Combine(
            applicationDirectory,
            "MissionGuideCache");
    }

    public string LegacyRootDirectory { get; }

    public string DevelopmentRootDirectory { get; }

    public string PackageCacheDirectory { get; }

    public string InstallationStatePath { get; }

    public string RegistryCatalogCachePath { get; }

    public string BundledPackageDirectory { get; }

    public string StorageRootDirectory { get; }

    public string MissionGuideCacheDirectory { get; }

    public string GetCachedPackagePath(string packageSha256)
    {
        return Path.Combine(
            this.PackageCacheDirectory,
            string.Concat(
                packageSha256.ToLowerInvariant(),
                ".n7addon"));
    }
}
