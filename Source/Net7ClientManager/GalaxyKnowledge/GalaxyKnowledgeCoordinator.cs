namespace Net7ClientManager.GalaxyKnowledge;

using Net7ClientManager.Navigation;
using Net7ClientManager.Observations.Models;

internal sealed class GalaxyKnowledgeCoordinator : IDisposable
{
    private static readonly TimeSpan fileChangeDebounce =
        TimeSpan.FromMilliseconds(1500);

    private static readonly TimeSpan fallbackRefreshInterval =
        TimeSpan.FromSeconds(30);

    private readonly object stateLock = new();
    private readonly object rebuildLock = new();
    private readonly System.Threading.Timer refreshTimer;
    private FileSystemWatcher? catalogWatcher;
    private GalaxyDataSet dataSet;
    private ForgeProductionRecipeCatalogSnapshot recipeCatalog;
    private ForgeMissionCatalogSnapshot missionCatalog;
    private GalaxyKnowledgeSnapshot current;
    private string lastStatus = "";
    private bool disposed;

    public GalaxyKnowledgeCoordinator(
        GalaxyDataSet dataSet,
        ForgeProductionRecipeCatalogSnapshot recipeCatalog,
        ForgeMissionCatalogSnapshot missionCatalog)
    {
        ArgumentNullException.ThrowIfNull(dataSet);
        ArgumentNullException.ThrowIfNull(recipeCatalog);
        ArgumentNullException.ThrowIfNull(missionCatalog);
        this.dataSet = dataSet;
        this.recipeCatalog = recipeCatalog;
        this.missionCatalog = missionCatalog;

        var catalog =
            ClientItemTemplateNameResolver.GetCatalogSnapshot();

        try
        {
            this.current = GalaxyKnowledgeBuilder.Build(
                catalog,
                dataSet,
                recipeCatalog,
                missionCatalog);
            this.lastStatus = DescribeSnapshot(this.current);
        }
        catch (Exception exception)
        {
            this.current = GalaxyKnowledgeSnapshot.Empty;
            this.lastStatus =
                $"Galaxy knowledge could not be built: {exception.Message}";
        }

        this.refreshTimer = new System.Threading.Timer(
            this.RefreshTimer_OnElapsed,
            null,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);
        this.ConfigureWatcher(catalog.SourcePath);
        this.refreshTimer.Change(
            fallbackRefreshInterval,
            fallbackRefreshInterval);
    }

    public GalaxyKnowledgeSnapshot Current =>
        Volatile.Read(ref this.current);

    public string Status
    {
        get
        {
            lock (this.stateLock)
            {
                return this.lastStatus;
            }
        }
    }

    public event EventHandler<GalaxyKnowledgeSnapshotChangedEventArgs>?
        SnapshotChanged;

    public void UpdateNavigationData(
        GalaxyDataSet nextDataSet)
    {
        ArgumentNullException.ThrowIfNull(nextDataSet);

        lock (this.stateLock)
        {
            if (this.disposed)
            {
                return;
            }

            this.dataSet = nextDataSet;
        }

        ThreadPool.QueueUserWorkItem(
            static state =>
            {
                var coordinator = (GalaxyKnowledgeCoordinator)state!;
                coordinator.TryRebuildFromCurrentInputs(
                    refreshCatalog: false);
            },
            this);
    }

    public void UpdateRecipeCatalog(
        ForgeProductionRecipeCatalogSnapshot nextRecipeCatalog)
    {
        ArgumentNullException.ThrowIfNull(nextRecipeCatalog);

        lock (this.stateLock)
        {
            if (this.disposed)
            {
                return;
            }

            this.recipeCatalog = nextRecipeCatalog;
        }

        ThreadPool.QueueUserWorkItem(
            static state =>
            {
                var coordinator = (GalaxyKnowledgeCoordinator)state!;
                coordinator.TryRebuildFromCurrentInputs(
                    refreshCatalog: false);
            },
            this);
    }

    public void UpdateMissionCatalog(
        ForgeMissionCatalogSnapshot nextMissionCatalog)
    {
        ArgumentNullException.ThrowIfNull(nextMissionCatalog);

        lock (this.stateLock)
        {
            if (this.disposed)
            {
                return;
            }

            this.missionCatalog = nextMissionCatalog;
        }

        ThreadPool.QueueUserWorkItem(
            static state =>
            {
                var coordinator = (GalaxyKnowledgeCoordinator)state!;
                coordinator.TryRebuildFromCurrentInputs(
                    refreshCatalog: false);
            },
            this);
    }

    public void Dispose()
    {
        lock (this.stateLock)
        {
            if (this.disposed)
            {
                return;
            }

            this.disposed = true;
            this.catalogWatcher?.Dispose();
            this.catalogWatcher = null;
        }

        this.refreshTimer.Dispose();
    }

    private void ConfigureWatcher(
        string? sourcePath)
    {
        lock (this.stateLock)
        {
            if (this.disposed)
            {
                return;
            }

            this.catalogWatcher?.Dispose();
            this.catalogWatcher = null;

            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                return;
            }

            var directory = Path.GetDirectoryName(sourcePath);
            var fileName = Path.GetFileName(sourcePath);

            if (string.IsNullOrWhiteSpace(directory) ||
                string.IsNullOrWhiteSpace(fileName) ||
                !Directory.Exists(directory))
            {
                return;
            }

            try
            {
                var watcher = new FileSystemWatcher(directory, fileName)
                {
                    IncludeSubdirectories = false,
                    NotifyFilter =
                        NotifyFilters.FileName |
                        NotifyFilters.LastWrite |
                        NotifyFilters.Size |
                        NotifyFilters.CreationTime,
                };
                watcher.Changed += this.CatalogWatcher_OnChanged;
                watcher.Created += this.CatalogWatcher_OnChanged;
                watcher.Deleted += this.CatalogWatcher_OnChanged;
                watcher.Renamed += this.CatalogWatcher_OnRenamed;
                watcher.EnableRaisingEvents = true;
                this.catalogWatcher = watcher;
            }
            catch (Exception exception)
            {
                this.lastStatus =
                    $"{DescribeSnapshot(this.Current)} " +
                    "File watching is unavailable; periodic refresh remains " +
                    $"active: {exception.Message}";
            }
        }
    }

    private void CatalogWatcher_OnChanged(
        object sender,
        FileSystemEventArgs e)
    {
        this.QueueDebouncedRefresh();
    }

    private void CatalogWatcher_OnRenamed(
        object sender,
        RenamedEventArgs e)
    {
        this.QueueDebouncedRefresh();
    }

    private void QueueDebouncedRefresh()
    {
        lock (this.stateLock)
        {
            if (this.disposed)
            {
                return;
            }

            this.refreshTimer.Change(
                fileChangeDebounce,
                fallbackRefreshInterval);
        }
    }

    private void RefreshTimer_OnElapsed(
        object? state)
    {
        this.TryRebuildFromCurrentInputs(
            refreshCatalog: true);
    }

    private void TryRebuildFromCurrentInputs(
        bool refreshCatalog)
    {
        try
        {
            this.RebuildFromCurrentInputs(refreshCatalog);
        }
        catch (Exception exception)
        {
            lock (this.stateLock)
            {
                if (!this.disposed)
                {
                    this.lastStatus =
                        $"Galaxy knowledge refresh failed: {exception.Message}";
                }
            }
        }
    }

    private void RebuildFromCurrentInputs(
        bool refreshCatalog)
    {
        lock (this.rebuildLock)
        {
            GalaxyDataSet activeDataSet;
            ForgeProductionRecipeCatalogSnapshot activeRecipeCatalog;
            ForgeMissionCatalogSnapshot activeMissionCatalog;

            lock (this.stateLock)
            {
                if (this.disposed)
                {
                    return;
                }

                activeDataSet = this.dataSet;
                activeRecipeCatalog = this.recipeCatalog;
                activeMissionCatalog = this.missionCatalog;
            }

            ClientItemTemplateCatalogSnapshot catalog;
            var catalogChanged = false;

            if (refreshCatalog)
            {
                var refresh =
                    ClientItemTemplateNameResolver
                        .RefreshCatalogIfChanged();
                catalog = refresh.Snapshot;
                catalogChanged = refresh.Applied;

                if (refresh.InputChanged && !refresh.Applied)
                {
                    lock (this.stateLock)
                    {
                        if (!this.disposed)
                        {
                            this.lastStatus = refresh.Status;
                        }
                    }

                    return;
                }
            }
            else
            {
                catalog =
                    ClientItemTemplateNameResolver.GetCatalogSnapshot();
            }

            var previous = this.Current;
            var forgeChanged =
                !string.Equals(
                    previous.Provenance.ForgeDatasetEpoch,
                    activeDataSet.DatasetEpoch,
                    StringComparison.Ordinal) ||
                previous.Provenance.ForgeRevision !=
                    activeDataSet.Revision ||
                previous.Provenance.ForgeContractVersion !=
                    activeDataSet.ContractVersion ||
                !string.Equals(
                    previous.Provenance.ForgeSnapshotSha256,
                    activeDataSet.SnapshotSha256,
                    StringComparison.OrdinalIgnoreCase);
            var recipeCatalogChanged =
                previous.Provenance.RecipeReadModelAvailable !=
                    activeRecipeCatalog.IsAvailable ||
                previous.Provenance.RecipeCatalogRevision !=
                    activeRecipeCatalog.Revision ||
                !string.Equals(
                    previous.Provenance.RecipeCatalogSha256,
                    activeRecipeCatalog.Sha256,
                    StringComparison.OrdinalIgnoreCase);

            var missionCatalogChanged =
                previous.Provenance.MissionReadModelAvailable !=
                    activeMissionCatalog.IsAvailable ||
                previous.Provenance.MissionCatalogRevision !=
                    activeMissionCatalog.Revision ||
                !string.Equals(
                    previous.Provenance.MissionCatalogSha256,
                    activeMissionCatalog.Sha256,
                    StringComparison.OrdinalIgnoreCase);

            if (!catalogChanged &&
                !forgeChanged &&
                !recipeCatalogChanged &&
                !missionCatalogChanged)
            {
                return;
            }

            var next = GalaxyKnowledgeBuilder.Build(
                catalog,
                activeDataSet,
                activeRecipeCatalog,
                activeMissionCatalog);
            Volatile.Write(ref this.current, next);

            lock (this.stateLock)
            {
                if (!this.disposed)
                {
                    this.lastStatus = DescribeSnapshot(next);
                }
            }

            this.ConfigureWatcher(catalog.SourcePath);

            lock (this.stateLock)
            {
                if (this.disposed)
                {
                    return;
                }
            }

            this.SnapshotChanged?.Invoke(
                this,
                new GalaxyKnowledgeSnapshotChangedEventArgs(next));
        }
    }

    private static string DescribeSnapshot(
        GalaxyKnowledgeSnapshot snapshot)
    {
        var recipeStatus = snapshot.Provenance.RecipeReadModelAvailable
            ? $", {snapshot.Recipes.Count} recipes " +
              $"(recipe revision {snapshot.Provenance.RecipeCatalogRevision})"
            : ", recipe data unavailable";
        var missionStatus = snapshot.Provenance.MissionReadModelAvailable
            ? $", {snapshot.Missions.Count} missions " +
              $"(mission revision {snapshot.Provenance.MissionCatalogRevision})"
            : ", mission data unavailable";

        return
            $"Galaxy knowledge ready: {snapshot.ItemsByTemplateId.Count} items, " +
            $"{snapshot.EffectsByIdentity.Count} effects{recipeStatus}" +
            $"{missionStatus}, Forge navigation revision " +
            $"{snapshot.Provenance.ForgeRevision}.";
    }


}
