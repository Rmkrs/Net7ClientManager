namespace Net7ClientManager.Addons.Loading;

using System.Text.Json;
using Net7ClientManager.Addons.Contracts;
using Net7ClientManager.Addons.Registry;

internal sealed class AddonCatalog : IDisposable
{
    private static readonly JsonSerializerOptions registryCacheJsonOptions =
        new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        };

    private readonly AddonPathProvider pathProvider;
    private readonly AddonPackageReader packageReader = new();
    private readonly InstalledAddonStore installedAddonStore;
    private readonly AddonRegistryClient registryClient = new();

    private IReadOnlyDictionary<string, AddonPackage> packages =
        new Dictionary<string, AddonPackage>(StringComparer.Ordinal);

    private IReadOnlyDictionary<string, AddonRegistrySummary> registryAddons =
        new Dictionary<string, AddonRegistrySummary>(StringComparer.Ordinal);

    public AddonCatalog(AddonPathProvider pathProvider)
    {
        this.pathProvider = pathProvider;
        this.installedAddonStore = new InstalledAddonStore(
            pathProvider.InstallationStatePath);

        var seeder = new BundledAddonSeeder(
            pathProvider,
            this.packageReader,
            this.installedAddonStore);
        seeder.EnsureInitialized();

        this.LoadRegistryCache();
    }

    public IReadOnlyList<AddonDescriptor> Descriptors { get; private set; } = [];

    public IReadOnlyList<AddonRegistrySummary> RegistryAddons =>
    [
        .. this.registryAddons.Values.OrderBy(
            addon => addon.Name,
            StringComparer.OrdinalIgnoreCase),
    ];

    public DateTimeOffset? RegistryFetchedAt { get; private set; }

    public string RegistryError { get; private set; } = "";

    public void Refresh()
    {
        this.RefreshLocalCatalog();
    }

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            var addons = await this.registryClient
                .GetAddonsAsync(cancellationToken)
                .ConfigureAwait(false);

            this.registryAddons =
                AddonRegistryCatalogValidator.CreateIndex(addons);
            this.RegistryFetchedAt = DateTimeOffset.UtcNow;
            this.RegistryError = "";
            this.SaveRegistryCache();
        }
        catch (Exception ex)
            when (ex is HttpRequestException or
                  TaskCanceledException or
                  InvalidDataException or
                  JsonException or
                  IOException or
                  UnauthorizedAccessException)
        {
            this.RegistryError = ex.Message;
        }

        this.RefreshLocalCatalog();
    }

    public async Task<AddonCommandResult> InstallLatestAsync(
        string addonId,
        CancellationToken cancellationToken)
    {
        var registryAddon = await this.GetRegistryAddonAsync(
                addonId,
                cancellationToken)
            .ConfigureAwait(false);

        if (registryAddon == null)
        {
            return AddonCommandResult.Failure(
                string.Concat(
                    "Addon '",
                    addonId,
                    "' is not available from Net7 Forge."));
        }

        var release = AddonRegistryCompatibility.GetLatestCompatibleRelease(registryAddon);

        return release == null
            ? AddonCommandResult.Failure(
                string.Concat(
                    "No release of ",
                    registryAddon.Name,
                    " supports addon API version ",
                    AddonApiVersion.Current,
                    "."))
            : await this.InstallReleaseAsync(
                    addonId,
                    release.Version,
                    cancellationToken)
                .ConfigureAwait(false);
    }

    public async Task<AddonCommandResult> InstallReleaseAsync(
        string addonId,
        string version,
        CancellationToken cancellationToken)
    {
        var acquisition = await this.AcquireReleasePackageAsync(
                addonId,
                version,
                cancellationToken)
            .ConfigureAwait(false);

        if (!acquisition.Result.Succeeded)
        {
            return acquisition.Result;
        }

        var registryAddon = this.registryAddons[addonId];
        var release = registryAddon.Releases.First(candidate => string.Equals(
            candidate.Version,
            version,
            StringComparison.Ordinal));
        var state = this.installedAddonStore.Load();
        var isPinned = state.Addons.TryGetValue(
            addonId,
            out var existing) && existing.IsPinned;

        state.Addons[addonId] = new InstalledAddonReference
        {
            Version = release.Version,
            PackageSha256 = release.PackageSha256,
            IsPinned = isPinned,
        };

        this.installedAddonStore.Save(state);
        this.RefreshLocalCatalog();

        return AddonCommandResult.Success();
    }

    public async Task<AddonPackageAcquisitionResult>
        AcquireReleasePackageAsync(
            string addonId,
            string version,
            CancellationToken cancellationToken)
    {
        var registryAddon = await this.GetRegistryAddonAsync(
                addonId,
                cancellationToken)
            .ConfigureAwait(false);

        if (registryAddon == null)
        {
            return AddonPackageAcquisitionResult.Failure(
                string.Concat(
                    "Addon '",
                    addonId,
                    "' is not available from Net7 Forge."));
        }

        var release = registryAddon.Releases.FirstOrDefault(candidate =>
            string.Equals(
                candidate.Version,
                version,
                StringComparison.Ordinal));

        if (release == null)
        {
            return AddonPackageAcquisitionResult.Failure(
                string.Concat(
                    "Version ",
                    version,
                    " of ",
                    registryAddon.Name,
                    " is not available."));
        }

        if (release.ApiVersion != AddonApiVersion.Current)
        {
            return AddonPackageAcquisitionResult.Failure(
                string.Concat(
                    "Addon version ",
                    release.Version,
                    " requires API version ",
                    release.ApiVersion,
                    "."));
        }

        Directory.CreateDirectory(
            this.pathProvider.PackageCacheDirectory);

        var cachedPath = this.pathProvider.GetCachedPackagePath(
            release.PackageSha256);
        var cachedPackage = File.Exists(cachedPath)
            ? this.packageReader.ReadArchive(cachedPath).Package
            : null;
        var cachedPackageIsExpected =
            cachedPackage != null &&
            string.Equals(
                cachedPackage.Manifest.Id,
                addonId,
                StringComparison.Ordinal) &&
            string.Equals(
                cachedPackage.Manifest.Version,
                release.Version,
                StringComparison.Ordinal) &&
            string.Equals(
                cachedPackage.PackageSha256,
                release.PackageSha256,
                StringComparison.OrdinalIgnoreCase);

        if (cachedPackageIsExpected)
        {
            return AddonPackageAcquisitionResult.Success(cachedPath);
        }

        var temporaryPath = string.Concat(
            cachedPath,
            ".download-",
            Guid.NewGuid().ToString("N"));

        try
        {
            await this.registryClient.DownloadPackageAsync(
                    release,
                    temporaryPath,
                    cancellationToken)
                .ConfigureAwait(false);

            var downloaded = this.packageReader.ReadArchive(
                temporaryPath);

            if (downloaded.Package == null ||
                !string.Equals(
                    downloaded.Package.Manifest.Id,
                    addonId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    downloaded.Package.Manifest.Version,
                    release.Version,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    downloaded.Package.PackageSha256,
                    release.PackageSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                return AddonPackageAcquisitionResult.Failure(
                    "The downloaded addon package failed validation.");
            }

            File.Move(
                temporaryPath,
                cachedPath,
                overwrite: true);
            return AddonPackageAcquisitionResult.Success(cachedPath);
        }
        catch (Exception ex)
            when (ex is IOException or
                  UnauthorizedAccessException or
                  HttpRequestException or
                  TaskCanceledException or
                  InvalidDataException or
                  JsonException)
        {
            return AddonPackageAcquisitionResult.Failure(
                string.Concat(
                    "Could not download addon package: ",
                    ex.Message));
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public AddonCommandResult Uninstall(
        string addonId,
        bool removeStoredData)
    {
        var state = this.installedAddonStore.Load();

        if (!state.Addons.ContainsKey(addonId))
        {
            return AddonCommandResult.Failure(
                string.Concat(
                    "Addon '",
                    addonId,
                    "' is not installed."));
        }

        try
        {
            if (removeStoredData)
            {
                var storageDirectory = Path.Combine(
                    this.pathProvider.StorageRootDirectory,
                    addonId);

                if (Directory.Exists(storageDirectory))
                {
                    Directory.Delete(
                        storageDirectory,
                        recursive: true);
                }
            }

            state.Addons.Remove(addonId);
            this.installedAddonStore.Save(state);
            this.RefreshLocalCatalog();
            return AddonCommandResult.Success();
        }
        catch (Exception ex)
            when (ex is IOException or UnauthorizedAccessException)
        {
            return AddonCommandResult.Failure(
                string.Concat(
                    "Could not uninstall addon: ",
                    ex.Message));
        }
    }

    public AddonCommandResult SetPinned(
        string addonId,
        bool pinned)
    {
        var state = this.installedAddonStore.Load();

        if (!state.Addons.TryGetValue(
                addonId,
                out var installed))
        {
            return AddonCommandResult.Failure(
                string.Concat(
                    "Addon '",
                    addonId,
                    "' is not installed."));
        }

        state.Addons[addonId] = installed with
        {
            IsPinned = pinned,
        };

        this.installedAddonStore.Save(state);
        this.RefreshLocalCatalog();
        return AddonCommandResult.Success();
    }

    public IReadOnlyList<AddonInstallationInfo> GetInstallations()
    {
        var state = this.installedAddonStore.Load();

        return
        [
            .. state.Addons
                .OrderBy(item => item.Key, StringComparer.Ordinal)
                .Select(item => new AddonInstallationInfo
                {
                    AddonId = item.Key,
                    Version = item.Value.Version,
                    PackageSha256 = item.Value.PackageSha256,
                    IsPinned = item.Value.IsPinned,
                }),
        ];
    }

    public bool TryGetPackage(
        string addonId,
        out AddonPackage package)
    {
        return this.packages.TryGetValue(addonId, out package!);
    }

    public void Dispose()
    {
        this.registryClient.Dispose();
    }

    private async Task<AddonRegistrySummary?> GetRegistryAddonAsync(
        string addonId,
        CancellationToken cancellationToken)
    {
        if (this.registryAddons.TryGetValue(
                addonId,
                out var registryAddon))
        {
            return registryAddon;
        }

        try
        {
            var addons = await this.registryClient
                .GetAddonsAsync(cancellationToken)
                .ConfigureAwait(false);

            this.registryAddons =
                AddonRegistryCatalogValidator.CreateIndex(addons);
            this.RegistryFetchedAt = DateTimeOffset.UtcNow;
            this.RegistryError = "";
            this.SaveRegistryCache();
        }
        catch (Exception ex)
            when (ex is HttpRequestException or
                  TaskCanceledException or
                  InvalidDataException or
                  JsonException or
                  IOException or
                  UnauthorizedAccessException)
        {
            this.RegistryError = ex.Message;
            return null;
        }

        return this.registryAddons.GetValueOrDefault(addonId);
    }

    private void LoadRegistryCache()
    {
        if (!File.Exists(this.pathProvider.RegistryCatalogCachePath))
        {
            return;
        }

        try
        {
            var snapshot = JsonSerializer.Deserialize<AddonRegistrySnapshot>(
                File.ReadAllText(
                    this.pathProvider.RegistryCatalogCachePath),
                registryCacheJsonOptions);

            if (snapshot == null ||
                snapshot.FormatVersion !=
                AddonRegistrySnapshot.CurrentFormatVersion)
            {
                return;
            }

            this.registryAddons =
                AddonRegistryCatalogValidator.CreateIndex(snapshot.Addons);
            this.RegistryFetchedAt = snapshot.FetchedAt;
        }
        catch (Exception ex)
            when (ex is IOException or
                  UnauthorizedAccessException or
                  InvalidDataException or
                  JsonException)
        {
            this.RegistryError = string.Concat(
                "The cached Net7 Forge catalog could not be loaded: ",
                ex.Message);
        }
    }

    private void SaveRegistryCache()
    {
        var directory = Path.GetDirectoryName(
                            this.pathProvider.RegistryCatalogCachePath) ??
                        throw new InvalidOperationException(
                            "The registry cache path has no parent directory.");
        Directory.CreateDirectory(directory);

        var snapshot = new AddonRegistrySnapshot
        {
            FetchedAt = this.RegistryFetchedAt ?? DateTimeOffset.UtcNow,
            Addons = this.RegistryAddons,
        };

        var temporaryPath = string.Concat(
            this.pathProvider.RegistryCatalogCachePath,
            ".tmp");

        File.WriteAllText(
            temporaryPath,
            JsonSerializer.Serialize(
                snapshot,
                registryCacheJsonOptions));
        File.Move(
            temporaryPath,
            this.pathProvider.RegistryCatalogCachePath,
            overwrite: true);
    }

    private void RefreshLocalCatalog()
    {
        var state = this.installedAddonStore.Load();
        List<AddonDescriptor> descriptors = [];
        Dictionary<string, AddonPackage> installedPackages =
            new(StringComparer.Ordinal);

        foreach (var installed in state.Addons.OrderBy(
                     item => item.Key,
                     StringComparer.Ordinal))
        {
            var packagePath = this.pathProvider.GetCachedPackagePath(
                installed.Value.PackageSha256);

            if (!File.Exists(packagePath))
            {
                descriptors.Add(
                    CreateInvalidInstalledDescriptor(
                        installed.Key,
                        packagePath,
                        "The installed package file is missing."));
                continue;
            }

            var result = this.packageReader.ReadArchive(packagePath);

            if (result.Package == null)
            {
                descriptors.Add(result.Descriptor);
                continue;
            }

            if (!string.Equals(
                    result.Package.Manifest.Id,
                    installed.Key,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    result.Package.Manifest.Version,
                    installed.Value.Version,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    result.Package.PackageSha256,
                    installed.Value.PackageSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                descriptors.Add(
                    result.Descriptor with
                    {
                        IsValid = false,
                        Error = "The installed package does not match installed-addons.json.",
                    });
                continue;
            }

            var descriptor = result.Package.Descriptor with
            {
                IsPinned = installed.Value.IsPinned,
            };

            installedPackages[installed.Key] = result.Package with
            {
                Descriptor = descriptor,
            };
        }

        AddonPackageReadResult[] developmentResults =
        [
            .. Directory
                .EnumerateDirectories(
                    this.pathProvider.DevelopmentRootDirectory)
                .Where(directory =>
                    !Path.GetFileName(directory).StartsWith(
                        ".",
                        StringComparison.Ordinal))
                .Order(StringComparer.OrdinalIgnoreCase)
                .Select(this.packageReader.ReadDirectory),
        ];

        descriptors.AddRange(
            developmentResults
                .Where(result => result.Package == null)
                .Select(result => result.Descriptor));

        Dictionary<string, AddonPackage> effectivePackages = new(
            installedPackages,
            StringComparer.Ordinal);

        foreach (var group in developmentResults
                     .Where(result => result.Package != null)
                     .GroupBy(
                         result => result.Package!.Manifest.Id,
                         StringComparer.Ordinal))
        {
            AddonPackageReadResult[] candidates = [.. group];

            if (candidates.Length > 1)
            {
                effectivePackages.Remove(group.Key);

                descriptors.AddRange(
                    candidates.Select(candidate =>
                        candidate.Descriptor with
                        {
                            IsValid = false,
                            Error = string.Concat(
                                "Duplicate development addon id '",
                                group.Key,
                                "'."),
                        }));
                continue;
            }

            var developmentPackage = candidates[0].Package!;

            if (developmentPackage.Manifest.ApiVersion !=
                    AddonApiVersion.Current &&
                effectivePackages.TryGetValue(
                    group.Key,
                    out var installedPackage))
            {
                var descriptor = installedPackage.Descriptor with
                {
                    Notice = string.Concat(
                        "Ignored incompatible development workspace '",
                        developmentPackage.Descriptor.DirectoryPath,
                        "' because it targets API version ",
                        developmentPackage.Manifest.ApiVersion,
                        "; this build supports API version ",
                        AddonApiVersion.Current,
                        "."),
                };

                effectivePackages[group.Key] = installedPackage with
                {
                    Descriptor = descriptor,
                };
                continue;
            }

            effectivePackages[group.Key] = developmentPackage;
        }

        AddonPackage[] packagesToDescribe =
        [
            .. effectivePackages.Values,
        ];

        foreach (var originalPackage in packagesToDescribe)
        {
            var package = originalPackage;
            var descriptor = package.Descriptor;

            var availableRelease = !package.IsDevelopment &&
                this.registryAddons.TryGetValue(
                    package.Manifest.Id,
                    out var registryAddon)
                    ? AddonRegistryCompatibility.GetLatestCompatibleRelease(registryAddon)
                    : null;

            if (availableRelease != null &&
                AddonSemanticVersion.TryParse(
                    package.Manifest.Version,
                    out var installedVersion) &&
                AddonSemanticVersion.TryParse(
                    availableRelease.Version,
                    out var availableVersion) &&
                availableVersion.CompareTo(installedVersion) > 0)
            {
                descriptor = descriptor with
                {
                    AvailableVersion = availableRelease.Version,
                };

                package = package with
                {
                    Descriptor = descriptor,
                };
                effectivePackages[package.Manifest.Id] = package;
            }

            descriptors.Add(descriptor);
        }

        this.packages = effectivePackages;
        this.Descriptors =
        [
            .. descriptors.OrderBy(
                descriptor => descriptor.Name,
                StringComparer.OrdinalIgnoreCase),
        ];
    }

    private static AddonDescriptor CreateInvalidInstalledDescriptor(
        string addonId,
        string packagePath,
        string error)
    {
        return new AddonDescriptor
        {
            DirectoryName = addonId,
            DirectoryPath = packagePath,
            IsValid = false,
            Error = error,
        };
    }
}
