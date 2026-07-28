namespace Net7ClientManager.Addons.Loading;

using System.Security.Cryptography;

internal sealed class BundledAddonSeeder(
    AddonPathProvider pathProvider,
    AddonPackageReader packageReader,
    InstalledAddonStore installedAddonStore)
{
    public void EnsureInitialized()
    {
        Directory.CreateDirectory(
            pathProvider.PackageCacheDirectory);
        Directory.CreateDirectory(
            pathProvider.DevelopmentRootDirectory);

        var bundledPackages = this.ReadBundledPackages();
        this.MigrateLegacyDirectories(bundledPackages);

        var state = installedAddonStore.Load();

        foreach (var bundledPackage in bundledPackages)
        {
            var cachedPath = pathProvider.GetCachedPackagePath(
                bundledPackage.Package.PackageSha256);

            var cacheIsCurrent = File.Exists(cachedPath) &&
                string.Equals(
                    Convert.ToHexStringLower(
                        SHA256.HashData(
                            File.ReadAllBytes(cachedPath))),
                    bundledPackage.Package.PackageSha256,
                    StringComparison.OrdinalIgnoreCase);

            if (!cacheIsCurrent)
            {
                var temporaryPath = string.Concat(
                    cachedPath,
                    ".seed-",
                    Guid.NewGuid().ToString("N"));

                try
                {
                    File.Copy(
                        bundledPackage.Package.Descriptor.DirectoryPath,
                        temporaryPath,
                        overwrite: false);
                    File.Move(
                        temporaryPath,
                        cachedPath,
                        overwrite: true);
                }
                finally
                {
                    if (File.Exists(temporaryPath))
                    {
                        File.Delete(temporaryPath);
                    }
                }
            }
        }

        if (state.BaselineInitialized)
        {
            return;
        }

        foreach (var bundledPackage in bundledPackages)
        {
            state.Addons.TryAdd(
                bundledPackage.Package.Manifest.Id,
                new InstalledAddonReference
                {
                    Version = bundledPackage.Package.Manifest.Version,
                    PackageSha256 = bundledPackage.Package.PackageSha256,
                });
        }

        state.BaselineInitialized = true;
        installedAddonStore.Save(state);
    }

    private IReadOnlyList<BundledPackage> ReadBundledPackages()
    {
        if (!Directory.Exists(pathProvider.BundledPackageDirectory))
        {
            throw new DirectoryNotFoundException(
                string.Concat(
                    "Bundled addon directory does not exist: ",
                    pathProvider.BundledPackageDirectory));
        }

        List<BundledPackage> packages = [];

        foreach (var packagePath in Directory
                     .EnumerateFiles(
                         pathProvider.BundledPackageDirectory,
                         "*.n7addon",
                         SearchOption.TopDirectoryOnly)
                     .Order(StringComparer.OrdinalIgnoreCase))
        {
            var result = packageReader.ReadArchive(packagePath);

            if (result.Package == null)
            {
                throw new InvalidDataException(
                    string.Concat(
                        "Bundled addon package is invalid: ",
                        result.Descriptor.Error));
            }

            packages.Add(
                new BundledPackage(
                    result.Package,
                    packageReader.CalculateArchiveContentFingerprint(
                        packagePath)));
        }

        if (packages.Count == 0)
        {
            throw new InvalidDataException(
                "No bundled addon packages were found.");
        }

        var duplicateAddon = packages
            .GroupBy(
                package => package.Package.Manifest.Id,
                StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicateAddon != null)
        {
            throw new InvalidDataException(
                string.Concat(
                    "Duplicate bundled addon id '",
                    duplicateAddon.Key,
                    "'."));
        }

        return packages;
    }

    private void MigrateLegacyDirectories(
        IReadOnlyList<BundledPackage> bundledPackages)
    {
        if (!Directory.Exists(pathProvider.LegacyRootDirectory))
        {
            return;
        }

        string[] legacyDirectories =
        [
            .. Directory.EnumerateDirectories(
                pathProvider.LegacyRootDirectory),
        ];

        foreach (var legacyDirectory in legacyDirectories)
        {
            var legacyResult = packageReader.ReadDirectory(
                legacyDirectory);
            var matchingBundle = legacyResult.Package == null
                ? null
                : bundledPackages.FirstOrDefault(bundle =>
                    string.Equals(
                        bundle.Package.Manifest.Id,
                        legacyResult.Package.Manifest.Id,
                        StringComparison.Ordinal) &&
                    string.Equals(
                        bundle.Package.Manifest.Version,
                        legacyResult.Package.Manifest.Version,
                        StringComparison.Ordinal));

            var matchesBundledPackage = false;

            if (matchingBundle != null)
            {
                try
                {
                    matchesBundledPackage = string.Equals(
                        matchingBundle.ContentFingerprint,
                        packageReader.CalculateDirectoryContentFingerprint(
                            legacyDirectory),
                        StringComparison.OrdinalIgnoreCase);
                }
                catch (Exception ex)
                    when (ex is IOException or
                          UnauthorizedAccessException or
                          InvalidOperationException)
                {
                    // Preserve unreadable or unusual legacy directories as
                    // development workspaces rather than deleting them.
                }
            }

            if (matchesBundledPackage)
            {
                Directory.Delete(
                    legacyDirectory,
                    recursive: true);
                continue;
            }

            var destination = this.GetAvailableDevelopmentDirectory(
                Path.GetFileName(legacyDirectory));
            Directory.Move(legacyDirectory, destination);
        }

        if (!Directory.EnumerateFileSystemEntries(
                pathProvider.LegacyRootDirectory).Any())
        {
            Directory.Delete(pathProvider.LegacyRootDirectory);
        }
    }

    private string GetAvailableDevelopmentDirectory(string directoryName)
    {
        var candidate = Path.Combine(
            pathProvider.DevelopmentRootDirectory,
            directoryName);

        if (!Path.Exists(candidate))
        {
            return candidate;
        }

        for (var suffix = 1; ; suffix++)
        {
            candidate = Path.Combine(
                pathProvider.DevelopmentRootDirectory,
                string.Concat(
                    directoryName,
                    "-legacy-",
                    suffix));

            if (!Path.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    private sealed record BundledPackage(
        AddonPackage Package,
        string ContentFingerprint);
}
