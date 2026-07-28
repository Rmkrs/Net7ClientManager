namespace Net7ClientManager.Navigation;

using System.Globalization;
using System.Text.Json;

internal sealed class NavigationDataStore
{
    public const string BaselineDatasetEpoch = "net7-navigation-v1-001";

    private readonly System.Threading.Lock stateLock = new();
    private readonly NavigationDataPathProvider paths;

    public NavigationDataStore()
        : this(new NavigationDataPathProvider())
    {
    }

    internal NavigationDataStore(NavigationDataPathProvider paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        this.paths = paths;
#if DEBUG
        NavigationDistributionRegression.Validate();
#endif
    }

    public NavigationDataLoadResult InitializeAndLoad()
    {
        lock (this.stateLock)
        {
            this.paths.EnsureDirectories();
            var bundled = this.LoadBundledBaseline();
            var bundledReference = this.EnsureFullPackage(
                bundled,
                "bundled");
            var state = this.LoadState() with
            {
                Bundled = bundledReference,
            };

            state = this.PromotePending(state);

            if (state.Active == null)
            {
                state = state with
                {
                    Active = bundledReference,
                };
            }
            else if (SameDatasetEpoch(bundledReference, state.Active) &&
                bundledReference.Revision > state.Active.Revision)
            {
                state = state with
                {
                    Previous = state.Active,
                    Active = bundledReference,
                };
            }

            var candidates = new[]
            {
                state.Active,
                state.Previous,
                bundledReference,
            };
            GalaxyDataSet? dataSet = null;
            NavigationDataSnapshotReference? selected = null;

            foreach (var candidate in candidates.Where(candidate => candidate != null))
            {
                if (this.TryLoadReference(candidate!, out dataSet))
                {
                    selected = candidate;
                    break;
                }
            }

            if (dataSet == null || selected == null)
            {
                throw new InvalidOperationException(
                    "No valid navigation dataset is available, including the bundled baseline.");
            }

            var usedFallback = state.Active == null ||
                !Equals(selected, state.Active);

            if (!Equals(state.Active, selected))
            {
                state = state with
                {
                    Active = selected,
                    Previous = null,
                };
            }

            this.SaveState(state);
            return new NavigationDataLoadResult(dataSet, usedFallback);
        }
    }


    public NavigationDataUpdateStatus GetUpdateStatus(
        long activeRevision,
        bool isChecking)
    {
        lock (this.stateLock)
        {
            var state = this.LoadState();
            return new NavigationDataUpdateStatus
            {
                ActiveRevision = activeRevision,
                PendingRevision = state.Pending?.Revision,
                IsChecking = isChecking,
                LastSuccessfulCheck = state.LastSuccessfulUpdateCheck,
                LastFailedCheck = state.LastFailedUpdateCheck,
                LastError = state.LastUpdateError,
            };
        }
    }

    public bool TryActivatePending(
        out NavigationDataLoadResult loadResult,
        out string message)
    {
        lock (this.stateLock)
        {
            var state = this.LoadState();

            if (state.Pending == null)
            {
                loadResult = null!;
                message = "No downloaded Forge dataset is waiting to be activated.";
                return false;
            }

            if (!this.TryLoadReference(state.Pending, out var dataSet))
            {
                this.SaveState(state with
                {
                    Pending = null,
                    LastFailedUpdateCheck = DateTimeOffset.UtcNow,
                    LastUpdateError =
                        "The downloaded Forge dataset could not be loaded.",
                });

                loadResult = null!;
                message =
                    "The downloaded Forge dataset is invalid and was discarded.";
                return false;
            }

            if (state.Active != null &&
                SameDatasetEpoch(state.Pending, state.Active) &&
                state.Pending.Revision <= state.Active.Revision)
            {
                this.SaveState(state with
                {
                    Pending = null,
                });

                loadResult = null!;
                message = "The active Forge dataset is already current.";
                return false;
            }

            var activated = state.Pending with
            {
                ActivatedAt = DateTimeOffset.UtcNow,
            };

            this.SaveState(state with
            {
                Previous = state.Active,
                Active = activated,
                Pending = null,
                LastUpdateError = null,
            });

            loadResult = new NavigationDataLoadResult(
                dataSet,
                UsedFallback: false);
            message = string.Create(
                CultureInfo.InvariantCulture,
                $"Forge dataset epoch {dataSet.DatasetEpoch} revision {dataSet.Revision} is now active.");
            return true;
        }
    }

    public void StageUpdate(
        ForgeNavigationUpdatePackage package,
        GalaxyDataSet current,
        string datasetEpoch)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentException.ThrowIfNullOrWhiteSpace(datasetEpoch);

        lock (this.stateLock)
        {
            var epochMatches = string.Equals(
                datasetEpoch,
                current.DatasetEpoch,
                StringComparison.Ordinal);

            if (epochMatches && package.Manifest.DataRevision <= current.Revision)
            {
                return;
            }

            ForgeNavigationEntitySnapshotDocument resultSnapshot;
            byte[] resultBytes;

            if (package.FullSnapshot != null)
            {
                resultSnapshot = package.FullSnapshot;
                resultBytes = package.PayloadBytes;
            }
            else if (package.DeltaDocument != null)
            {
                if (!epochMatches ||
                    package.Manifest.ContractVersion != current.ContractVersion)
                {
                    throw new InvalidOperationException(
                        "A navigation delta cannot cross dataset epochs or distribution contracts.");
                }

                resultSnapshot = ForgeNavigationDeltaApplier.Apply(
                    current.Snapshot,
                    package.DeltaDocument);
                resultBytes = ForgeNavigationDataJson.SerializeCanonical(
                    resultSnapshot);
            }
            else
            {
                throw new InvalidOperationException(
                    "Navigation update package contains no payload.");
            }

            var resultSha256 = ForgeNavigationHash.ComputeSha256(resultBytes);

            if (!string.Equals(
                    resultSha256,
                    package.Manifest.ResultSnapshotSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Applied navigation update does not produce the advertised snapshot.");
            }

            ForgeNavigationDataPackageLoader.ValidateSnapshot(resultSnapshot);
            var resultDocument =
                ForgeNavigationEntityCatalogCodec.ToDataDocument(resultSnapshot);
            ForgeNavigationDataPackageLoader.ValidateDocument(resultDocument);
            ForgeNavigationDataPackageLoader.ValidateManifestCounts(
                package.Manifest,
                resultSnapshot);
            this.EnsurePackageFile(
                package.PackagePath,
                package.PackageSha256,
                package.PackageSize);
            this.EnsureSnapshot(resultSha256, resultBytes);

            var pending = new NavigationDataSnapshotReference
            {
                DatasetEpoch = datasetEpoch,
                Revision = resultSnapshot.Revision,
                AuthorityRevision = package.Manifest.SourceAuthorityRevision,
                ContractVersion = resultSnapshot.ContractVersion,
                SnapshotSha256 = resultSha256,
                PackageSha256 = package.PackageSha256,
                Source = "forge",
                ActivatedAt = DateTimeOffset.UtcNow,
            };
            var state = this.LoadState();

            if (state.Active != null &&
                SameDatasetEpoch(pending, state.Active) &&
                pending.Revision <= state.Active.Revision)
            {
                return;
            }

            this.SaveState(state with
            {
                Pending = pending,
                LastSuccessfulUpdateCheck = DateTimeOffset.UtcNow,
                LastFailedUpdateCheck = null,
                LastUpdateError = null,
            });
        }
    }

    public void RecordSuccessfulUpdateCheck()
    {
        lock (this.stateLock)
        {
            var state = this.LoadState();
            this.SaveState(state with
            {
                LastSuccessfulUpdateCheck = DateTimeOffset.UtcNow,
                LastFailedUpdateCheck = null,
                LastUpdateError = null,
            });
        }
    }

    public void RecordFailedUpdateCheck(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        lock (this.stateLock)
        {
            var state = this.LoadState();
            this.SaveState(state with
            {
                LastFailedUpdateCheck = DateTimeOffset.UtcNow,
                LastUpdateError = exception.Message,
            });
        }
    }

    private ForgeNavigationFullPackage LoadBundledBaseline()
    {
        if (!Directory.Exists(this.paths.BundledPackageDirectory))
        {
            throw new InvalidOperationException(
                "The bundled navigation package directory is missing.");
        }

        var validPackages = new List<ForgeNavigationFullPackage>();

        foreach (var path in Directory.EnumerateFiles(
                     this.paths.BundledPackageDirectory,
                     "*.n7data",
                     SearchOption.TopDirectoryOnly))
        {
            try
            {
                validPackages.Add(
                    ForgeNavigationDataPackageLoader.LoadFull(path));
            }
            catch (InvalidOperationException)
            {
                // A stale or damaged bundled package must not hide a valid
                // newer package copied by the application release.
            }
            catch (InvalidDataException)
            {
                // Invalid ZIP data is a damaged candidate, not a fatal stop.
            }
            catch (IOException)
            {
                // Transient file failures are treated as a damaged candidate
                // so another bundled package can win.
            }
            catch (JsonException)
            {
                // Malformed JSON is a damaged candidate, not a fatal stop.
            }
            catch (UnauthorizedAccessException)
            {
                // Keep searching if one candidate cannot be read.
            }
        }

        return validPackages
            .OrderByDescending(package => package.Manifest.DataRevision)
            .FirstOrDefault() ??
            throw new InvalidOperationException(
                "No valid bundled navigation package is available.");
    }

    private NavigationDataSnapshotReference EnsureFullPackage(
        ForgeNavigationFullPackage package,
        string source)
    {
        this.EnsurePackageFile(
            package.PackagePath,
            package.PackageSha256,
            package.PackageSize);
        this.EnsureSnapshot(
            package.Manifest.ResultSnapshotSha256,
            package.SnapshotBytes);

        return new NavigationDataSnapshotReference
        {
            DatasetEpoch = BaselineDatasetEpoch,
            Revision = package.Snapshot.Revision,
            AuthorityRevision = package.Manifest.SourceAuthorityRevision,
            ContractVersion = package.Snapshot.ContractVersion,
            SnapshotSha256 = package.Manifest.ResultSnapshotSha256.ToLowerInvariant(),
            PackageSha256 = package.PackageSha256.ToLowerInvariant(),
            Source = source,
            ActivatedAt = DateTimeOffset.UtcNow,
        };
    }

    private NavigationDataStateDocument PromotePending(
        NavigationDataStateDocument state)
    {
        if (state.Pending == null)
        {
            return state;
        }

        if (!this.TryLoadReference(state.Pending, out _) ||
            (state.Active != null &&
             SameDatasetEpoch(state.Pending, state.Active) &&
             state.Pending.Revision <= state.Active.Revision))
        {
            return state with
            {
                Pending = null,
            };
        }

        return state with
        {
            Previous = state.Active,
            Active = state.Pending,
            Pending = null,
        };
    }

    private bool TryLoadReference(
        NavigationDataSnapshotReference reference,
        out GalaxyDataSet dataSet)
    {
        dataSet = null!;

        if (!IsValidReference(reference))
        {
            return false;
        }

        var snapshotPath = Path.Combine(
            this.paths.SnapshotDirectory,
            string.Concat(reference.SnapshotSha256, ".json"));

        try
        {
            if (!File.Exists(snapshotPath))
            {
                return false;
            }

            var bytes = File.ReadAllBytes(snapshotPath);
            var actualSha256 = ForgeNavigationHash.ComputeSha256(bytes);

            if (!string.Equals(
                    actualSha256,
                    reference.SnapshotSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var snapshot = ForgeNavigationDataPackageLoader.DeserializeSnapshot(bytes);

            if (snapshot.Revision != reference.Revision ||
                snapshot.ContractVersion != reference.ContractVersion)
            {
                return false;
            }

            dataSet = ForgeNavigationDataPackageLoader.CreateDataSet(
                snapshot,
                reference.AuthorityRevision,
                reference.DatasetEpoch,
                reference.Source,
                reference.SnapshotSha256,
                reference.PackageSha256);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void EnsurePackageFile(
        string sourcePath,
        string packageSha256,
        long packageSize)
    {
        if (!ForgeNavigationHash.IsSha256(packageSha256) ||
            packageSize <= 0)
        {
            throw new InvalidOperationException(
                "Navigation package cache metadata is invalid.");
        }

        var destinationPath = Path.Combine(
            this.paths.PackageDirectory,
            string.Concat(packageSha256.ToLowerInvariant(), ".n7data"));

        if (File.Exists(destinationPath))
        {
            var existing = new FileInfo(destinationPath);

            if (existing.Length == packageSize &&
                string.Equals(
                    ForgeNavigationHash.ComputeFileSha256(destinationPath),
                    packageSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        var temporaryPath = CreateTemporaryPath(destinationPath);

        try
        {
            File.Copy(sourcePath, temporaryPath, overwrite: false);

            var temporaryInfo = new FileInfo(temporaryPath);

            if (temporaryInfo.Length != packageSize ||
                !string.Equals(
                    ForgeNavigationHash.ComputeFileSha256(temporaryPath),
                    packageSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Navigation package changed while it was being cached.");
            }

            File.Move(temporaryPath, destinationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private void EnsureSnapshot(
        string snapshotSha256,
        byte[] bytes)
    {
        if (!ForgeNavigationHash.IsSha256(snapshotSha256) ||
            !string.Equals(
                ForgeNavigationHash.ComputeSha256(bytes),
                snapshotSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Navigation snapshot hash is invalid.");
        }

        var destinationPath = Path.Combine(
            this.paths.SnapshotDirectory,
            string.Concat(snapshotSha256.ToLowerInvariant(), ".json"));

        if (File.Exists(destinationPath))
        {
            var existingBytes = File.ReadAllBytes(destinationPath);

            if (string.Equals(
                    ForgeNavigationHash.ComputeSha256(existingBytes),
                    snapshotSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        var temporaryPath = CreateTemporaryPath(destinationPath);

        try
        {
            File.WriteAllBytes(temporaryPath, bytes);
            File.Move(temporaryPath, destinationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private NavigationDataStateDocument LoadState()
    {
        if (!File.Exists(this.paths.StatePath))
        {
            return new NavigationDataStateDocument();
        }

        try
        {
            var state = JsonSerializer.Deserialize<NavigationDataStateDocument>(
                File.ReadAllBytes(this.paths.StatePath),
                ForgeNavigationDataJson.ReadOptions);

            return state is { SchemaVersion: NavigationDataStateDocument.CurrentSchemaVersion }
                ? state
                : new NavigationDataStateDocument();
        }
        catch (JsonException)
        {
            return new NavigationDataStateDocument();
        }
        catch (IOException)
        {
            return new NavigationDataStateDocument();
        }
        catch (UnauthorizedAccessException)
        {
            return new NavigationDataStateDocument();
        }
    }

    private void SaveState(NavigationDataStateDocument state)
    {
        var bytes = ForgeNavigationDataJson.SerializeCanonical(state);
        var temporaryPath = CreateTemporaryPath(this.paths.StatePath);

        try
        {
            File.WriteAllBytes(temporaryPath, bytes);
            File.Move(temporaryPath, this.paths.StatePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static bool IsValidReference(
        NavigationDataSnapshotReference reference)
    {
        return !string.IsNullOrWhiteSpace(reference.DatasetEpoch) &&
               reference.Revision > 0 &&
               reference.AuthorityRevision > 0 &&
               reference.ContractVersion ==
                   ForgeNavigationEntitySnapshotDocument.CurrentContractVersion &&
               ForgeNavigationHash.IsSha256(reference.SnapshotSha256) &&
               ForgeNavigationHash.IsSha256(reference.PackageSha256) &&
               !string.IsNullOrWhiteSpace(reference.Source);
    }

    private static bool SameDatasetEpoch(
        NavigationDataSnapshotReference left,
        NavigationDataSnapshotReference right)
    {
        return string.Equals(
            left.DatasetEpoch,
            right.DatasetEpoch,
            StringComparison.Ordinal);
    }

    private static string CreateTemporaryPath(string destinationPath)
    {
        return string.Concat(
            destinationPath,
            ".",
            Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture),
            ".tmp");
    }
}
