namespace Net7ClientManager.Addons.Runtime;

using Net7ClientManager.Addons.Contracts;
using Net7ClientManager.Addons.Development;
using Net7ClientManager.Addons.Loading;
using Net7ClientManager.Addons.Projection;
using Net7ClientManager.Addons.Registry;
using Net7ClientManager.Addons.Runtime.LuaCSharp;
using Net7ClientManager.Observations;

/// <summary>
/// Process-wide authority for all owner-scoped addon runtimes.
///
/// One coordinator knows every observed client, while every Lua state remains
/// bound to one addon and one hosted-client owner. Lua never receives a client
/// registry, a process id, memory data or another owner's snapshot.
/// </summary>
public sealed partial class AddonRuntimeCoordinator : IAsyncDisposable
{
    private const int MaximumRecentLogCount = 500;

    private static readonly TimeSpan addonLoadTimeout =
        TimeSpan.FromSeconds(1);

    private static readonly TimeSpan addonUnloadTimeout =
        TimeSpan.FromMilliseconds(500);

    private static readonly TimeSpan addonEventTimeout =
        TimeSpan.FromMilliseconds(500);

    // Direct UI gestures may await a bounded host-side action such as
    // normalizing the game map and verifying a target selection.
    private static readonly TimeSpan addonUiInteractionTimeout =
        TimeSpan.FromSeconds(12);

    private readonly System.Threading.Lock lockObject = new();
    private readonly Dictionary<int, OwnerState> owners = [];
    private readonly Queue<AddonLogEntry> recentLogs = [];
    private readonly GameSnapshotProjector snapshotProjector;
    private readonly ILuaRuntimeFactory runtimeFactory =
        new LuaCSharpRuntimeFactory();
    private readonly AddonPathProvider pathProvider;
    private readonly AddonDevelopmentWorkspaceService developmentService;
    private readonly AddonWorkScheduler scheduler = new();
    private readonly AddonStorageStore storageStore;
    private readonly AddonCatalog catalog;
    private readonly AddonActionDispatcher? actionDispatcher;

    private IReadOnlyList<AddonDescriptor> catalogDescriptors = [];
    private bool started;
    private bool disposed;

    public AddonRuntimeCoordinator(
        AddonActionDispatcher? actionDispatcher = null)
    {
        this.actionDispatcher = actionDispatcher;
        this.pathProvider = new AddonPathProvider();
        this.developmentService = new AddonDevelopmentWorkspaceService(
            this.pathProvider.DevelopmentRootDirectory);
        this.storageStore = new AddonStorageStore(
            this.pathProvider.StorageRootDirectory);
        this.catalog = new AddonCatalog(this.pathProvider);
        this.snapshotProjector = new GameSnapshotProjector();
    }

    public event EventHandler<AddonUiCommandEventArgs>? UiCommandEmitted;

    public string AddonDirectory =>
        this.pathProvider.DevelopmentRootDirectory;

    public IReadOnlyList<AddonRegistrySummary> RegistryAddons =>
        this.catalog.RegistryAddons;

    public string RegistryError => this.catalog.RegistryError;

    public DateTimeOffset? RegistryFetchedAt =>
        this.catalog.RegistryFetchedAt;

    public IReadOnlyList<AddonInstallationInfo> GetInstallations()
    {
        return this.catalog.GetInstallations();
    }

    public IReadOnlyList<string> RetireInstalledAddonsByName(
        string addonName,
        bool removeStoredData)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(addonName);

        lock (this.lockObject)
        {
            if (!this.started)
            {
                throw new InvalidOperationException(
                    "Start the addon coordinator before retiring installed addons.");
            }

            if (this.owners.Count != 0)
            {
                return [];
            }

            var installedIds = this.catalog.GetInstallations()
                .Select(installation => installation.AddonId)
                .ToHashSet(StringComparer.Ordinal);
            var matchingIds = this.catalogDescriptors
                .Where(descriptor =>
                    installedIds.Contains(descriptor.Id) &&
                    string.Equals(
                        descriptor.Name,
                        addonName,
                        StringComparison.OrdinalIgnoreCase))
                .Select(descriptor => descriptor.Id)
                .Concat(this.catalog.RegistryAddons
                    .Where(addon =>
                        installedIds.Contains(addon.Id) &&
                        string.Equals(
                            addon.Name,
                            addonName,
                            StringComparison.OrdinalIgnoreCase))
                    .Select(addon => addon.Id))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            List<string> retired = [];

            foreach (var addonId in matchingIds)
            {
                var result = this.catalog.Uninstall(
                    addonId,
                    removeStoredData);

                if (result.Succeeded)
                {
                    retired.Add(addonId);
                }
            }

            this.catalogDescriptors = [.. this.catalog.Descriptors];
            return retired;
        }
    }

    public IReadOnlyList<AddonDevelopmentWorkspace> GetDevelopmentWorkspaces()
    {
        return this.developmentService.GetWorkspaces();
    }

    public AddonDevelopmentWorkspace CreateDevelopmentWorkspace(
        string addonId,
        string name,
        string? author = null)
    {
        return this.developmentService.CreateWorkspace(
            addonId,
            name,
            author);
    }

    public Task<AddonCommandResult>
        CreateDevelopmentWorkspaceFromInstalledAddonAsync(
            string addonId,
            CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(addonId);

        return this.scheduler.EnqueueAsync(
            async token =>
            {
                var existing = this.developmentService.GetWorkspaces()
                    .FirstOrDefault(workspace => string.Equals(
                        workspace.Id,
                        addonId,
                        StringComparison.Ordinal));

                if (existing != null)
                {
                    return AddonCommandResult.Success();
                }

                if (!this.catalog.TryGetPackage(addonId, out var package) ||
                    package.IsDevelopment ||
                    !File.Exists(package.Descriptor.DirectoryPath))
                {
                    return AddonCommandResult.Failure(
                        "The installed package source is not available.");
                }

                this.developmentService.CreateWorkspaceFromPackage(
                    package.Descriptor.DirectoryPath,
                    "Installed package");
                this.catalog.Refresh();
                await this.ReconcileAllOwnersAsync(token)
                    .ConfigureAwait(false);
                return AddonCommandResult.Success();
            },
            cancellationToken);
    }

    public Task<AddonCommandResult>
        CreateDevelopmentWorkspaceFromRegistryReleaseAsync(
            string addonId,
            string version,
            CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(addonId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        return this.scheduler.EnqueueAsync(
            async token =>
            {
                var existing = this.developmentService.GetWorkspaces()
                    .FirstOrDefault(workspace => string.Equals(
                        workspace.Id,
                        addonId,
                        StringComparison.Ordinal));

                if (existing != null)
                {
                    return AddonCommandResult.Success();
                }

                var acquisition = await this.catalog
                    .AcquireReleasePackageAsync(
                        addonId,
                        version,
                        token)
                    .ConfigureAwait(false);

                if (!acquisition.Result.Succeeded)
                {
                    return acquisition.Result;
                }

                this.developmentService.CreateWorkspaceFromPackage(
                    acquisition.PackagePath,
                    string.Concat("Net7 Forge release ", version));
                this.catalog.Refresh();
                await this.ReconcileAllOwnersAsync(token)
                    .ConfigureAwait(false);
                return AddonCommandResult.Success();
            },
            cancellationToken);
    }

    public Task<AddonCommandResult> DiscardDevelopmentWorkspaceAsync(
        string addonId,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(addonId);

        return this.scheduler.EnqueueAsync(
            async token =>
            {
                this.developmentService.DeleteWorkspace(addonId);
                this.catalog.Refresh();
                await this.ReconcileAllOwnersAsync(token)
                    .ConfigureAwait(false);
                return AddonCommandResult.Success();
            },
            cancellationToken);
    }

    public IReadOnlyList<AddonDevelopmentDocument> GetDevelopmentDocuments(
        string workspaceId)
    {
        return this.developmentService.GetDocuments(workspaceId);
    }

    public AddonDevelopmentDocumentContent ReadDevelopmentDocument(
        string workspaceId,
        string relativePath)
    {
        return this.developmentService.ReadDocument(
            workspaceId,
            relativePath);
    }

    public AddonDevelopmentSaveResult SaveDevelopmentDocument(
        string workspaceId,
        string relativePath,
        string text)
    {
        return this.developmentService.SaveDocument(
            workspaceId,
            relativePath,
            text);
    }

    public AddonDevelopmentValidationResult ValidateDevelopmentDocument(
        string workspaceId,
        string relativePath,
        string text)
    {
        return this.developmentService.ValidateDocument(
            workspaceId,
            relativePath,
            text);
    }

    public AddonDevelopmentValidationResult ValidateDevelopmentWorkspace(
        string workspaceId)
    {
        return this.developmentService.ValidateWorkspace(workspaceId);
    }

    public AddonPublicationSourcePackage
        BuildDevelopmentPublicationSourcePackage(string workspaceId)
    {
        return this.developmentService.BuildPublicationSourcePackage(
            workspaceId);
    }

    public string WriteDevelopmentApiStub(string workspaceId)
    {
        return this.developmentService.WriteGeneratedApiStub(workspaceId);
    }

    public string WriteDevelopmentApiReference(string workspaceId)
    {
        return this.developmentService.WriteGeneratedApiReference(workspaceId);
    }

    public void Start(bool checkForUpdatesAutomatically = true)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);

        lock (this.lockObject)
        {
            if (this.started)
            {
                return;
            }

            this.catalog.Refresh();
            this.catalogDescriptors = [.. this.catalog.Descriptors];
            this.started = true;
        }

        if (checkForUpdatesAutomatically)
        {
            this.RefreshCatalogInBackground();
        }
    }

    public void RefreshCatalogInBackground()
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);

        this.scheduler.EnqueueFireAndForget(
            async token =>
            {
                await this.catalog.RefreshAsync(token)
                    .ConfigureAwait(false);

                lock (this.lockObject)
                {
                    this.catalogDescriptors =
                        [.. this.catalog.Descriptors];
                }
            },
            this.LogSchedulerFailure);
    }

    public void UpdateOwnerSnapshot(
        ClientObservationSnapshot snapshot,
        AddonOwnerRegistration registration,
        AddonNavigationRouteSnapshot? navigationRoute = null)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(registration);

        if (!this.started)
        {
            this.Start();
        }

        var projection = this.snapshotProjector.Project(
            snapshot,
            navigationRoute);
        AddonGameSnapshot? previousSnapshot;
        ILuaRuntime[] runtimes;
        bool requiresReconciliation;

        lock (this.lockObject)
        {
            if (!this.owners.TryGetValue(
                    snapshot.ProcessId,
                    out var owner))
            {
                owner = new OwnerState
                {
                    Registration = registration,
                };

                this.owners[snapshot.ProcessId] = owner;
                requiresReconciliation = true;
            }
            else
            {
                requiresReconciliation =
                    !OwnerRegistrationsEqual(
                        owner.Registration,
                        registration);

                owner.Registration = registration;
            }

            previousSnapshot = owner.Snapshot;

            if (previousSnapshot != null &&
                !string.Equals(
                    previousSnapshot.Lifecycle.State,
                    projection.Lifecycle.State,
                    StringComparison.Ordinal))
            {
                requiresReconciliation = true;
            }

            owner.Snapshot = projection;

            runtimes =
            [
                .. owner.Runtimes.Values
                    .Where(instance =>
                        AddonActivationContexts.IsActive(
                            instance.Package.Manifest,
                            projection.Lifecycle.State))
                    .Select(instance => instance.Runtime)
                    .OfType<ILuaRuntime>(),
            ];
        }

        foreach (var runtime in runtimes)
        {
            runtime.UpdateGameSnapshot(projection);
        }

        if (requiresReconciliation)
        {
            this.scheduler.EnqueueFireAndForget(
                token => this.ReconcileOwnerCoreAsync(
                    snapshot.ProcessId,
                    token),
                this.LogSchedulerFailure);
        }

        if (previousSnapshot != null)
        {
            foreach (var gameEvent in CreateEvents(
                         previousSnapshot,
                         projection))
            {
                this.scheduler.EnqueueFireAndForget(
                    token => this.RaiseEventCoreAsync(
                        snapshot.ProcessId,
                        gameEvent,
                        token,
                        previousSnapshot.Lifecycle.State),
                    this.LogSchedulerFailure);
            }
        }
    }

    public void DetachOwner(int processId)
    {
        if (this.disposed)
        {
            return;
        }

        this.snapshotProjector.Forget(processId);

        this.scheduler.EnqueueFireAndForget(
            token => this.DetachOwnerCoreAsync(
                processId,
                token),
            this.LogSchedulerFailure);
    }

    public void PublishChatMessage(ClientChatMessage message)
    {
        if (this.disposed)
        {
            return;
        }

        ArgumentNullException.ThrowIfNull(message);

        AddonGameSnapshot? snapshot;

        lock (this.lockObject)
        {
            snapshot = this.owners
                .GetValueOrDefault(message.ProcessId)?
                .Snapshot;
        }

        if (snapshot == null)
        {
            return;
        }

        var gameEvent = new AddonGameEvent
        {
            Name = "chat.message",
            Snapshot = snapshot,
            OccurredAt = message.ObservedAt,
            Data = new Dictionary<string, object?>(
                StringComparer.Ordinal)
            {
                ["text"] = message.Text,
                ["observed_at"] = message.ObservedAt,
                ["from_history"] = message.IsSnapshot,
            },
        };

        this.scheduler.EnqueueFireAndForget(
            token => this.RaiseEventCoreAsync(
                message.ProcessId,
                gameEvent,
                token),
            this.LogSchedulerFailure);
    }

    public IReadOnlyList<AddonRuntimeStatus> GetOwnerStatuses(
        int ownerProcessId)
    {
        lock (this.lockObject)
        {
            this.owners.TryGetValue(
                ownerProcessId,
                out var owner);

            var descriptorById = this.catalogDescriptors
                .GroupBy(
                    descriptor => descriptor.Id,
                    StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.First(),
                    StringComparer.Ordinal);

            var addonIds = new HashSet<string>(
                descriptorById.Keys,
                StringComparer.Ordinal);

            if (owner != null)
            {
                addonIds.UnionWith(
                    owner.Registration.EnabledAddonIds);
                addonIds.UnionWith(owner.Runtimes.Keys);
            }

            return
            [
                .. addonIds
                    .OrderBy(
                        addonId => descriptorById
                            .GetValueOrDefault(addonId)?.Name ?? addonId,
                        StringComparer.OrdinalIgnoreCase)
                    .Select(addonId => CreateStatus(
                        addonId,
                        descriptorById.GetValueOrDefault(addonId),
                        owner)),
            ];
        }
    }

    public IReadOnlyList<AddonLogEntry> GetRecentLogs(
        int? ownerProcessId = null)
    {
        lock (this.lockObject)
        {
            return
            [
                .. this.recentLogs.Where(
                    entry => ownerProcessId == null ||
                             entry.OwnerProcessId == ownerProcessId.Value),
            ];
        }
    }

    public Task RefreshCatalogAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);

        return this.scheduler.EnqueueAsync(
            async token =>
            {
                await this.catalog.RefreshAsync(token)
                    .ConfigureAwait(false);

                int[] ownerProcessIds;

                lock (this.lockObject)
                {
                    this.catalogDescriptors =
                        [.. this.catalog.Descriptors];
                    ownerProcessIds = [.. this.owners.Keys];
                }

                foreach (var ownerProcessId in ownerProcessIds)
                {
                    await this.ReconcileOwnerCoreAsync(
                            ownerProcessId,
                            token)
                        .ConfigureAwait(false);
                }
            },
            cancellationToken);
    }

    public Task<AddonCommandResult> UpdateAddonAsync(
        string addonId,
        CancellationToken cancellationToken = default)
    {
        return this.InstallAddonVersionCoreAsync(
            addonId,
            version: null,
            cancellationToken);
    }

    public Task<AddonCommandResult> InstallAddonVersionAsync(
        string addonId,
        string version,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        return this.InstallAddonVersionCoreAsync(
            addonId,
            version,
            cancellationToken);
    }

    public Task<AddonCommandResult> UninstallAddonAsync(
        string addonId,
        bool removeStoredData,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);

        return this.scheduler.EnqueueAsync(
            async token =>
            {
                var result = this.catalog.Uninstall(
                    addonId,
                    removeStoredData);

                if (!result.Succeeded)
                {
                    return result;
                }

                await this.ReconcileAllOwnersAsync(token)
                    .ConfigureAwait(false);
                return AddonCommandResult.Success();
            },
            cancellationToken);
    }

    public Task<AddonCommandResult> SetAddonPinnedAsync(
        string addonId,
        bool pinned,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);

        return this.scheduler.EnqueueAsync(
            token =>
            {
                token.ThrowIfCancellationRequested();
                var result = this.catalog.SetPinned(
                    addonId,
                    pinned);

                if (result.Succeeded)
                {
                    lock (this.lockObject)
                    {
                        this.catalogDescriptors =
                            [.. this.catalog.Descriptors];
                    }
                }

                return ValueTask.FromResult(result);
            },
            cancellationToken);
    }

    private Task<AddonCommandResult> InstallAddonVersionCoreAsync(
        string addonId,
        string? version,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);

        return this.scheduler.EnqueueAsync(
            async token =>
            {
                var result = version == null
                    ? await this.catalog.InstallLatestAsync(
                            addonId,
                            token)
                        .ConfigureAwait(false)
                    : await this.catalog.InstallReleaseAsync(
                            addonId,
                            version,
                            token)
                        .ConfigureAwait(false);

                if (!result.Succeeded)
                {
                    return result;
                }

                await this.ReconcileAllOwnersAsync(token)
                    .ConfigureAwait(false);
                return AddonCommandResult.Success();
            },
            cancellationToken);
    }

    private async Task ReconcileAllOwnersAsync(
        CancellationToken cancellationToken)
    {
        int[] ownerProcessIds;

        lock (this.lockObject)
        {
            this.catalogDescriptors =
                [.. this.catalog.Descriptors];
            ownerProcessIds = [.. this.owners.Keys];
        }

        foreach (var ownerProcessId in ownerProcessIds)
        {
            await this.ReconcileOwnerCoreAsync(
                    ownerProcessId,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public void PublishUiInteraction(
        AddonUiInteraction interaction)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);
        ArgumentNullException.ThrowIfNull(interaction);

        this.scheduler.EnqueueFireAndForget(
            token => this.RaiseUiInteractionCoreAsync(
                interaction,
                token));
    }

    public Task<AddonCommandResult> EnableAddonAsync(
        int ownerProcessId,
        string addonId,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);

        return this.scheduler.EnqueueAsync(
            token => this.EnableAddonCoreAsync(
                ownerProcessId,
                addonId,
                token),
            cancellationToken);
    }

    public Task<AddonCommandResult> DisableAddonAsync(
        int ownerProcessId,
        string addonId,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);

        return this.scheduler.EnqueueAsync(
            token => this.DisableAddonCoreAsync(
                ownerProcessId,
                addonId,
                updateDesiredState: true,
                token),
            cancellationToken);
    }

    public Task<AddonCommandResult> ReloadAddonAsync(
        int ownerProcessId,
        string addonId,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);

        return this.scheduler.EnqueueAsync(
            async token =>
            {
                this.catalog.Refresh();

                lock (this.lockObject)
                {
                    this.catalogDescriptors =
                        [.. this.catalog.Descriptors];
                }

                await this.DisableAddonCoreAsync(
                        ownerProcessId,
                        addonId,
                        updateDesiredState: false,
                        token)
                    .ConfigureAwait(false);

                return await this.LoadAddonCoreAsync(
                        ownerProcessId,
                        addonId,
                        token)
                    .ConfigureAwait(false);
            },
            cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;

        try
        {
            await this.scheduler.EnqueueAsync(
                    this.StopAllCoreAsync)
                .ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
        }

        await this.scheduler.DisposeAsync().ConfigureAwait(false);
        this.catalog.Dispose();

        lock (this.lockObject)
        {
            this.owners.Clear();
            this.recentLogs.Clear();
            this.catalogDescriptors = [];
        }
    }

    private async ValueTask ReconcileOwnerCoreAsync(
        int ownerProcessId,
        CancellationToken cancellationToken)
    {
        AddonOwnerRegistration registration;
        AddonGameSnapshot snapshot;
        string[] currentAddonIds;

        lock (this.lockObject)
        {
            if (!this.owners.TryGetValue(
                    ownerProcessId,
                    out var owner) ||
                owner.Snapshot == null)
            {
                return;
            }

            registration = owner.Registration;
            snapshot = owner.Snapshot;
            currentAddonIds = [.. owner.Runtimes.Keys];
        }

        foreach (var addonId in currentAddonIds)
        {
            AddonRuntimeInstance? instance = null;

            lock (this.lockObject)
            {
                if (this.owners.TryGetValue(
                        ownerProcessId,
                        out var owner))
                {
                    owner.Runtimes.TryGetValue(
                        addonId,
                        out instance);
                }
            }

            if (!registration.EnabledAddonIds.Contains(addonId) ||
                instance == null ||
                !string.Equals(
                    instance.OwnerKey,
                    registration.OwnerKey,
                    StringComparison.Ordinal) ||
                !this.catalog.TryGetPackage(
                    addonId,
                    out var package) ||
                !string.Equals(
                    instance.Package.PackageSha256,
                    package.PackageSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                await this.DisableAddonCoreAsync(
                        ownerProcessId,
                        addonId,
                        updateDesiredState: false,
                        cancellationToken)
                    .ConfigureAwait(false);

                continue;
            }

            var shouldRun = AddonActivationContexts.IsActive(
                package.Manifest,
                snapshot.Lifecycle.State);

            if (shouldRun &&
                instance.State == AddonRuntimeState.WaitingForContext)
            {
                await this.DisableAddonCoreAsync(
                        ownerProcessId,
                        addonId,
                        updateDesiredState: false,
                        cancellationToken)
                    .ConfigureAwait(false);

                _ = await this.LoadAddonCoreAsync(
                        ownerProcessId,
                        addonId,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            else if (!shouldRun &&
                     instance.State !=
                         AddonRuntimeState.WaitingForContext)
            {
                await this.DisableAddonCoreAsync(
                        ownerProcessId,
                        addonId,
                        updateDesiredState: false,
                        cancellationToken)
                    .ConfigureAwait(false);

                _ = await this.LoadAddonCoreAsync(
                        ownerProcessId,
                        addonId,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        foreach (var addonId in registration.EnabledAddonIds)
        {
            bool alreadyKnown;

            lock (this.lockObject)
            {
                alreadyKnown = this.owners
                    .GetValueOrDefault(ownerProcessId)?
                    .Runtimes.ContainsKey(addonId) == true;
            }

            if (!alreadyKnown)
            {
                _ = await this.LoadAddonCoreAsync(
                        ownerProcessId,
                        addonId,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private async ValueTask<AddonCommandResult> EnableAddonCoreAsync(
        int ownerProcessId,
        string addonId,
        CancellationToken cancellationToken)
    {
        lock (this.lockObject)
        {
            if (!this.owners.TryGetValue(ownerProcessId, out var value))
            {
                return AddonCommandResult.Failure(
                    "The addon owner is not attached.");
            }

            if (value.Runtimes.TryGetValue(addonId, out var existing) &&
                existing.State == AddonRuntimeState.Running)
            {
                return AddonCommandResult.Success();
            }
        }

        var result = await this.LoadAddonCoreAsync(
                ownerProcessId,
                addonId,
                cancellationToken)
            .ConfigureAwait(false);

        if (!result.Succeeded)
        {
            return result;
        }

        lock (this.lockObject)
        {
            if (this.owners.TryGetValue(
                    ownerProcessId,
                    out var owner))
            {
                owner.Registration = owner.Registration with
                {
                    EnabledAddonIds = AddToSet(
                        owner.Registration.EnabledAddonIds,
                        addonId),
                };
            }
        }

        return result;
    }

    private async ValueTask<AddonCommandResult> LoadAddonCoreAsync(
        int ownerProcessId,
        string addonId,
        CancellationToken cancellationToken)
    {
        AddonOwnerRegistration registration;
        AddonGameSnapshot snapshot;

        lock (this.lockObject)
        {
            if (!this.owners.TryGetValue(
                    ownerProcessId,
                    out var owner))
            {
                return AddonCommandResult.Failure(
                    "The addon owner is not attached.");
            }

            if (owner.Snapshot == null)
            {
                return AddonCommandResult.Failure(
                    "The addon owner has no observation snapshot yet.");
            }

            registration = owner.Registration;
            snapshot = owner.Snapshot;
        }

        if (!this.catalog.TryGetPackage(
                addonId,
                out var package))
        {
            return AddonCommandResult.Failure(
                string.Concat(
                    "Addon '",
                    addonId,
                    "' is missing or invalid."));
        }

        if (!AddonActivationContexts.IsActive(
                package.Manifest,
                snapshot.Lifecycle.State))
        {
            var waitingInstance = new AddonRuntimeInstance
            {
                OwnerProcessId = ownerProcessId,
                OwnerKey = registration.OwnerKey,
                Package = package,
                State = AddonRuntimeState.WaitingForContext,
                LastActivityAt = DateTimeOffset.UtcNow,
            };

            lock (this.lockObject)
            {
                if (!this.owners.TryGetValue(
                        ownerProcessId,
                        out var owner))
                {
                    return AddonCommandResult.Failure(
                        "The addon owner detached before activation could be recorded.");
                }

                owner.Runtimes[addonId] = waitingInstance;
            }

            this.ClearAddonUi(ownerProcessId, addonId);
            return AddonCommandResult.Success();
        }

        var instance = new AddonRuntimeInstance
        {
            OwnerProcessId = ownerProcessId,
            OwnerKey = registration.OwnerKey,
            Package = package,
            State = AddonRuntimeState.Loading,
            LastActivityAt = DateTimeOffset.UtcNow,
        };

        lock (this.lockObject)
        {
            if (!this.owners.TryGetValue(
                    ownerProcessId,
                    out var owner))
            {
                return AddonCommandResult.Failure(
                    "The addon owner detached before loading completed.");
            }

            owner.Runtimes[addonId] = instance;
        }

        var runtime = this.runtimeFactory.Create(
            new AddonRuntimeOptions
            {
                OwnerProcessId = ownerProcessId,
                Manifest = package.Manifest,
                Modules = package.Modules,
                ActionDispatcher = this.actionDispatcher,
                OwnerKey = registration.OwnerKey,
                Storage = this.storageStore,
            });

        lock (this.lockObject)
        {
            instance.Runtime = runtime;
        }

        runtime.LogEntryWritten += this.Runtime_OnLogEntryWritten;
        runtime.UiCommandEmitted += this.Runtime_OnUiCommandEmitted;
        runtime.UpdateGameSnapshot(snapshot);

        var result = await runtime.StartAsync(
                package.EntryPointSource,
                package.EntryPointSourceName,
                addonLoadTimeout,
                cancellationToken)
            .ConfigureAwait(false);

        if (!result.Succeeded)
        {
            lock (this.lockObject)
            {
                instance.State = AddonRuntimeState.Failed;
                instance.Error = result.Error;
                instance.LastActivityAt = DateTimeOffset.UtcNow;
                instance.Runtime = null;
            }

            runtime.LogEntryWritten -= this.Runtime_OnLogEntryWritten;
            runtime.UiCommandEmitted -= this.Runtime_OnUiCommandEmitted;
            this.ClearAddonUi(ownerProcessId, addonId);
            await runtime.DisposeAsync().ConfigureAwait(false);

            return AddonCommandResult.Failure(
                string.IsNullOrWhiteSpace(result.Error)
                    ? "The addon failed to load."
                    : result.Error);
        }

        lock (this.lockObject)
        {
            instance.State = AddonRuntimeState.Running;
            instance.Error = "";
            instance.LoadedAt = DateTimeOffset.UtcNow;
            instance.LastActivityAt = instance.LoadedAt;
        }

        return AddonCommandResult.Success();
    }

    private async ValueTask<AddonCommandResult> DisableAddonCoreAsync(
        int ownerProcessId,
        string addonId,
        bool updateDesiredState,
        CancellationToken cancellationToken)
    {
        AddonRuntimeInstance? instance;

        lock (this.lockObject)
        {
            if (!this.owners.TryGetValue(
                    ownerProcessId,
                    out var owner))
            {
                return AddonCommandResult.Failure(
                    "The addon owner is not attached.");
            }

            if (updateDesiredState)
            {
                owner.Registration = owner.Registration with
                {
                    EnabledAddonIds = RemoveFromSet(
                        owner.Registration.EnabledAddonIds,
                        addonId),
                };
            }

            if (owner.Runtimes.Remove(addonId, out instance))
            {
                instance.State = AddonRuntimeState.Stopping;
            }
        }

        if (instance?.Runtime == null)
        {
            this.ClearAddonUi(ownerProcessId, addonId);
            return AddonCommandResult.Success();
        }

        var stopResult = await instance.Runtime.StopAsync(
                addonUnloadTimeout,
                cancellationToken)
            .ConfigureAwait(false);

        instance.Runtime.LogEntryWritten -=
            this.Runtime_OnLogEntryWritten;

        instance.Runtime.UiCommandEmitted -=
            this.Runtime_OnUiCommandEmitted;

        this.ClearAddonUi(ownerProcessId, addonId);

        await instance.Runtime.DisposeAsync().ConfigureAwait(false);
        instance.Runtime = null;

        return stopResult.Succeeded
            ? AddonCommandResult.Success()
            : AddonCommandResult.Failure(stopResult.Error);
    }

    private async ValueTask RaiseEventCoreAsync(
        int ownerProcessId,
        AddonGameEvent gameEvent,
        CancellationToken cancellationToken,
        string? previousLifecycleContext = null)
    {
        AddonRuntimeInstance[] instances;

        lock (this.lockObject)
        {
            if (!this.owners.TryGetValue(
                    ownerProcessId,
                    out var owner))
            {
                return;
            }

            instances =
            [
                .. owner.Runtimes.Values.Where(
                    instance =>
                        instance is { State: AddonRuntimeState.Running, Runtime: not null } &&
                        ShouldDispatchEvent(
                            instance.Package.Manifest,
                            gameEvent,
                            previousLifecycleContext)),
            ];
        }

        foreach (var instance in instances)
        {
            var result = await instance.Runtime!
                .RaiseEventAsync(
                    gameEvent,
                    addonEventTimeout,
                    cancellationToken)
                .ConfigureAwait(false);

            lock (this.lockObject)
            {
                instance.LastActivityAt = DateTimeOffset.UtcNow;
            }

            if (result.Succeeded || result.WasCancelled)
            {
                // Snapshot/event callbacks are bounded work. Cancellation or
                // a time-budget expiry is recoverable and must not unload the
                // complete addon package. The runtime already recorded a
                // warning and can process the next event normally.
                continue;
            }

            var failedRuntime = instance.Runtime;

            lock (this.lockObject)
            {
                instance.State = AddonRuntimeState.Failed;
                instance.Error = result.Error;
                instance.Runtime = null;
            }

            if (failedRuntime != null)
            {
                failedRuntime.LogEntryWritten -=
                    this.Runtime_OnLogEntryWritten;

                failedRuntime.UiCommandEmitted -=
                    this.Runtime_OnUiCommandEmitted;

                this.ClearAddonUi(
                    ownerProcessId,
                    instance.Package.Manifest.Id);

                await failedRuntime.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private async ValueTask RaiseUiInteractionCoreAsync(
        AddonUiInteraction interaction,
        CancellationToken cancellationToken)
    {
        AddonRuntimeInstance? instance;

        lock (this.lockObject)
        {
            if (!this.owners.TryGetValue(
                    interaction.OwnerProcessId,
                    out var owner) ||
                !owner.Runtimes.TryGetValue(
                    interaction.AddonId,
                    out instance) ||
                instance.State != AddonRuntimeState.Running ||
                instance.Runtime == null ||
                owner.Snapshot == null ||
                !AddonActivationContexts.IsActive(
                    instance.Package.Manifest,
                    owner.Snapshot.Lifecycle.State))
            {
                return;
            }
        }

        var result = await instance.Runtime
            .RaiseUiInteractionAsync(
                interaction,
                addonUiInteractionTimeout,
                cancellationToken)
            .ConfigureAwait(false);

        lock (this.lockObject)
        {
            instance.LastActivityAt = DateTimeOffset.UtcNow;
        }

        if (result.Succeeded || result.WasCancelled)
        {
            // A direct gesture may legitimately lose its foreground-input
            // race or exceed the host action budget. Keep the addon running
            // so its controls and the next user gesture remain available.
            return;
        }

        var failedRuntime = instance.Runtime;

        lock (this.lockObject)
        {
            instance.State = AddonRuntimeState.Failed;
            instance.Error = result.Error;
            instance.Runtime = null;
        }

        if (failedRuntime == null)
        {
            return;
        }

        failedRuntime.LogEntryWritten -=
            this.Runtime_OnLogEntryWritten;

        failedRuntime.UiCommandEmitted -=
            this.Runtime_OnUiCommandEmitted;

        this.ClearAddonUi(
            interaction.OwnerProcessId,
            interaction.AddonId);

        await failedRuntime.DisposeAsync().ConfigureAwait(false);
    }

    private async ValueTask DetachOwnerCoreAsync(
        int processId,
        CancellationToken cancellationToken)
    {
        string[] addonIds;

        lock (this.lockObject)
        {
            addonIds = this.owners.TryGetValue(
                    processId,
                    out var owner)
                ? [.. owner.Runtimes.Keys]
                : [];
        }

        foreach (var addonId in addonIds)
        {
            _ = await this.DisableAddonCoreAsync(
                    processId,
                    addonId,
                    updateDesiredState: false,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        lock (this.lockObject)
        {
            this.owners.Remove(processId);
        }
    }

    private async ValueTask StopAllCoreAsync(
        CancellationToken cancellationToken)
    {
        int[] ownerProcessIds;

        lock (this.lockObject)
        {
            ownerProcessIds = [.. this.owners.Keys];
        }

        foreach (var ownerProcessId in ownerProcessIds)
        {
            await this.DetachOwnerCoreAsync(
                    ownerProcessId,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private void Runtime_OnUiCommandEmitted(
        object? sender,
        AddonUiCommandEventArgs e)
    {
        this.UiCommandEmitted?.Invoke(this, e);
    }

    private void ClearAddonUi(
        int ownerProcessId,
        string addonId)
    {
        this.UiCommandEmitted?.Invoke(
            this,
            new AddonUiCommandEventArgs(
                new AddonUiCommand
                {
                    Kind = AddonUiCommandKind.ClearAddon,
                    OwnerProcessId = ownerProcessId,
                    AddonId = addonId,
                }));
    }

    private void Runtime_OnLogEntryWritten(
        object? sender,
        AddonLogEntryEventArgs eventArgs)
    {
        lock (this.lockObject)
        {
            this.recentLogs.Enqueue(eventArgs.Entry);

            while (this.recentLogs.Count > MaximumRecentLogCount)
            {
                this.recentLogs.Dequeue();
            }
        }
    }

    private void LogSchedulerFailure(Exception exception)
    {
        this.Runtime_OnLogEntryWritten(
            this,
            new AddonLogEntryEventArgs(
                new AddonLogEntry(
                    DateTimeOffset.UtcNow,
                    0,
                    "net7.addons.host",
                    AddonLogLevel.Error,
                    string.Concat(
                        "Addon scheduler failure: ",
                        exception.Message))));
    }

    private static IReadOnlyList<AddonGameEvent> CreateEvents(
        AddonGameSnapshot previous,
        AddonGameSnapshot current)
    {
        List<AddonGameEvent> events = [];

        if (!string.Equals(
                previous.Lifecycle.State,
                current.Lifecycle.State,
                StringComparison.Ordinal))
        {
            events.Add(
                new AddonGameEvent
                {
                    Name = "lifecycle.changed",
                    Snapshot = current,
                    Data = new Dictionary<string, object?>(
                        StringComparer.Ordinal)
                    {
                        ["previous"] = previous.Lifecycle.State,
                        ["current"] = current.Lifecycle.State,
                    },
                });
        }

        if (!string.Equals(
                previous.World.Environment,
                current.World.Environment,
                StringComparison.Ordinal))
        {
            events.Add(
                new AddonGameEvent
                {
                    Name = "world.environment_changed",
                    Snapshot = current,
                    Data = new Dictionary<string, object?>(
                        StringComparer.Ordinal)
                    {
                        ["previous"] = previous.World.Environment,
                        ["current"] = current.World.Environment,
                    },
                });
        }

        if (!LocationsEqual(previous.World, current.World))
        {
            events.Add(
                new AddonGameEvent
                {
                    Name = "world.location_changed",
                    Snapshot = current,
                    Data = new Dictionary<string, object?>(
                        StringComparer.Ordinal)
                    {
                        ["previous"] = CreateLocationData(previous.World),
                        ["current"] = CreateLocationData(current.World),
                    },
                });
        }

        AddLootTractorEvents(
            events,
            previous,
            current);

        AddDerivedSnapshotEvents(
            events,
            previous,
            current);

        List<string> changedDomains = [];

        foreach (var fingerprint in current.EventDomainFingerprints
                     .OrderBy(
                         item => item.Key,
                         StringComparer.Ordinal))
        {
            if (!previous.EventDomainFingerprints.TryGetValue(
                    fingerprint.Key,
                    out var previousFingerprint) ||
                string.Equals(
                    previousFingerprint,
                    fingerprint.Value,
                    StringComparison.Ordinal))
            {
                continue;
            }

            changedDomains.Add(fingerprint.Key);

            events.Add(
                new AddonGameEvent
                {
                    Name = string.Concat(
                        fingerprint.Key,
                        ".changed"),
                    Snapshot = current,
                    Data = new Dictionary<string, object?>(
                        StringComparer.Ordinal)
                    {
                        ["domain"] = fingerprint.Key,
                    },
                });
        }

        if (changedDomains.Count > 0)
        {
            events.Add(
                new AddonGameEvent
                {
                    Name = "game.updated",
                    Snapshot = current,
                    Data = new Dictionary<string, object?>(
                        StringComparer.Ordinal)
                    {
                        ["domains"] = changedDomains,
                    },
                });
        }

        var previousCombatSequences = previous.RecentCombatEvents
            .Select(GetCombatEventSequence)
            .Where(sequence => sequence.HasValue)
            .Select(sequence => sequence!.Value)
            .ToHashSet();

        foreach (var combatEvent in current.RecentCombatEvents)
        {
            var sequence = GetCombatEventSequence(combatEvent);

            if (!sequence.HasValue ||
                previousCombatSequences.Contains(sequence.Value))
            {
                continue;
            }

            events.Add(
                new AddonGameEvent
                {
                    Name = "combat.event",
                    Snapshot = current,
                    OccurredAt = combatEvent.TryGetValue(
                            "observed_at",
                            out var observedAt) &&
                        observedAt is DateTimeOffset timestamp
                            ? timestamp
                            : null,
                    // sequence remains for bundled DPS addon compatibility.
                    Data = combatEvent,
                });
        }

        return events;
    }

    private static void AddLootTractorEvents(
        List<AddonGameEvent> events,
        AddonGameSnapshot previous,
        AddonGameSnapshot current)
    {
        var previousTractor = GetLootTractorDomain(previous);
        var currentTractor = GetLootTractorDomain(current);

        if (currentTractor == null)
        {
            return;
        }

        var previousActive = GetBoolean(
            previousTractor,
            "is_tractoring");
        var currentActive = GetBoolean(
            currentTractor,
            "is_tractoring");
        var previousStartedAt = GetInt64(
            previousTractor,
            "started_at");
        var currentStartedAt = GetInt64(
            currentTractor,
            "started_at");

        if (previousActive &&
            currentActive &&
            previousStartedAt.HasValue &&
            currentStartedAt.HasValue &&
            previousStartedAt.Value != currentStartedAt.Value)
        {
            if (previousTractor != null)
            {
                events.Add(
                    CreateLootTractorEvent(
                        "loot.tractor_completed",
                        current,
                        previousTractor,
                        current.ObservedAt.ToUnixTimeMilliseconds()));
            }

            events.Add(
                CreateLootTractorEvent(
                    "loot.tractor_started",
                    current,
                    currentTractor));

            return;
        }

        if (!previousActive && currentActive)
        {
            events.Add(
                CreateLootTractorEvent(
                    "loot.tractor_started",
                    current,
                    currentTractor));

            return;
        }

        if (previousActive && !currentActive)
        {
            var isInterrupted = GetBoolean(
                currentTractor,
                "recently_interrupted");

            events.Add(
                CreateLootTractorEvent(
                    isInterrupted
                        ? "loot.tractor_interrupted"
                        : "loot.tractor_completed",
                    current,
                    previousTractor ?? currentTractor,
                    GetInt64(currentTractor, "completed_at") ??
                    current.ObservedAt.ToUnixTimeMilliseconds()));
        }
    }

    private static AddonGameEvent CreateLootTractorEvent(
        string name,
        AddonGameSnapshot snapshot,
        IReadOnlyDictionary<string, object?> tractor,
        long? completedAtOverride = null)
    {
        return new AddonGameEvent
        {
            Name = name,
            Snapshot = snapshot,
            Data = new Dictionary<string, object?>(
                StringComparer.Ordinal)
            {
                ["item_name"] = GetString(
                    tractor,
                    "item_name"),
                ["started_at"] = tractor.TryGetValue(
                    "started_at",
                    out var startedAt)
                    ? startedAt
                    : null,
                ["completed_at"] = completedAtOverride ??
                    (tractor.TryGetValue(
                        "completed_at",
                        out var completedAt)
                        ? completedAt
                        : null),
            },
        };
    }

    private static IReadOnlyDictionary<string, object?>?
        GetLootTractorDomain(
            AddonGameSnapshot snapshot)
    {
        if (!snapshot.PublicData.TryGetValue(
                "loot",
                out var lootValue) ||
            lootValue is not IReadOnlyDictionary<string, object?> loot ||
            !loot.TryGetValue(
                "tractor",
                out var tractorValue) ||
            tractorValue is not IReadOnlyDictionary<string, object?> tractor)
        {
            return null;
        }

        return tractor;
    }

    private static bool GetBoolean(
        IReadOnlyDictionary<string, object?>? values,
        string key)
    {
        return values != null &&
               values.TryGetValue(
                   key,
                   out var value) &&
               value is bool boolean &&
               boolean;
    }

    private static string? GetString(
        IReadOnlyDictionary<string, object?>? values,
        string key)
    {
        return values != null &&
               values.TryGetValue(
                   key,
                   out var value) &&
               value is string text &&
               !string.IsNullOrWhiteSpace(text)
            ? text
            : null;
    }

    private static long? GetInt64(
        IReadOnlyDictionary<string, object?>? values,
        string key)
    {
        if (values == null ||
            !values.TryGetValue(
                key,
                out var value))
        {
            return null;
        }

        return value switch
        {
            byte number => number,
            sbyte number => number,
            short number => number,
            ushort number => number,
            int number => number,
            uint number => number,
            long number => number,
            ulong number when number <= long.MaxValue =>
                checked((long)number),
            DateTimeOffset timestamp => timestamp.ToUnixTimeMilliseconds(),
            _ => null,
        };
    }

    private static bool ShouldDispatchEvent(
        AddonManifest manifest,
        AddonGameEvent gameEvent,
        string? previousLifecycleContext)
    {
        if (!AddonActivationContexts.IsActive(
                manifest,
                gameEvent.Snapshot.Lifecycle.State))
        {
            return false;
        }

        return previousLifecycleContext == null ||
               AddonActivationContexts.IsActive(
                   manifest,
                   previousLifecycleContext);
    }

    private static long? GetCombatEventSequence(
        IReadOnlyDictionary<string, object?> combatEvent)
    {
        if (!combatEvent.TryGetValue(
                "sequence",
                out var value))
        {
            return null;
        }

        return value switch
        {
            byte number => number,
            sbyte number => number,
            short number => number,
            ushort number => number,
            int number => number,
            uint number => number,
            long number => number,
            ulong number when number <= long.MaxValue =>
                checked((long)number),
            _ => null,
        };
    }

    private static bool LocationsEqual(
        AddonWorldSnapshot first,
        AddonWorldSnapshot second)
    {
        return string.Equals(
                   first.SystemName,
                   second.SystemName,
                   StringComparison.Ordinal) &&
               string.Equals(
                   first.SectorName,
                   second.SectorName,
                   StringComparison.Ordinal) &&
               string.Equals(
                   first.StarbaseName,
                   second.StarbaseName,
                   StringComparison.Ordinal);
    }

    private static IReadOnlyDictionary<string, object?> CreateLocationData(
        AddonWorldSnapshot world)
    {
        return new Dictionary<string, object?>(
            StringComparer.Ordinal)
        {
            ["system_name"] = world.SystemName,
            ["sector_name"] = world.SectorName,
            ["starbase_name"] = world.StarbaseName,
        };
    }

    private static AddonRuntimeStatus CreateStatus(
        string addonId,
        AddonDescriptor? descriptor,
        OwnerState? owner)
    {
        AddonRuntimeInstance? instance = null;
        owner?.Runtimes.TryGetValue(
            addonId,
            out instance);

        var enabled = owner?.Registration.EnabledAddonIds
            .Contains(addonId) == true;

        var currentContext = owner?.Snapshot?.Lifecycle.State ??
            "unknown";

        IReadOnlyList<string> activationContexts =
            descriptor?.Manifest == null
                ? []
                : AddonActivationContexts.GetEffectiveContexts(
                    descriptor.Manifest);

        var isContextActive = descriptor?.Manifest != null &&
            AddonActivationContexts.IsActive(
                descriptor.Manifest,
                currentContext);

        var state = instance?.State ??
            (descriptor?.IsValid == false || descriptor == null
                ? AddonRuntimeState.Unavailable
                : enabled
                    ? isContextActive
                        ? AddonRuntimeState.Loading
                        : AddonRuntimeState.WaitingForContext
                    : AddonRuntimeState.Disabled);

        var detail = state == AddonRuntimeState.WaitingForContext
            ? string.Concat(
                "Waiting for activation context: ",
                string.Join(", ", activationContexts),
                ". Current context: ",
                currentContext,
                ".")
            : "";

        if (!string.IsNullOrWhiteSpace(descriptor?.Notice))
        {
            detail = string.IsNullOrWhiteSpace(detail)
                ? descriptor.Notice
                : string.Concat(detail, " ", descriptor.Notice);
        }

        return new AddonRuntimeStatus
        {
            AddonId = addonId,
            Name = descriptor?.Name ?? addonId,
            Version = descriptor?.Version ?? "",
            Description = descriptor?.Manifest?.Description,
            Author = GetAddonAuthor(descriptor),
            PublisherName = descriptor?.PublisherName ?? "",
            IsOfficial = string.Equals(
                descriptor?.PublisherId,
                "net7forge.official",
                StringComparison.Ordinal),
            IsPinned = descriptor?.IsPinned == true,
            DirectoryPath = descriptor?.DirectoryPath ?? "",
            IsDevelopment = descriptor?.IsDevelopment == true,
            Source = GetAddonSource(descriptor),
            AvailableVersion = descriptor?.AvailableVersion,
            IsValid = descriptor?.IsValid == true,
            IsEnabled = enabled,
            State = state,
            Error = instance?.Error ?? descriptor?.Error ??
                (descriptor == null
                    ? "The enabled addon is not installed."
                    : ""),
            Detail = detail,
            ActivationContexts = activationContexts,
            CurrentContext = currentContext,
            LoadedAt = instance?.LoadedAt,
            LastActivityAt = instance?.LastActivityAt,
        };
    }

    private static string GetAddonAuthor(AddonDescriptor? descriptor)
    {
        if (!string.IsNullOrWhiteSpace(descriptor?.Manifest?.Author))
        {
            return descriptor.Manifest.Author;
        }

        return descriptor?.PublisherName ?? "";
    }

    private static string GetAddonSource(AddonDescriptor? descriptor)
    {
        if (descriptor == null)
        {
            return "Unavailable";
        }

        if (descriptor.IsDevelopment)
        {
            return "Development";
        }

        return string.Equals(
            descriptor.PublisherId,
            "net7forge.official",
            StringComparison.Ordinal)
                ? "Official package"
                : "Community package";
    }

    private static bool OwnerRegistrationsEqual(
        AddonOwnerRegistration first,
        AddonOwnerRegistration second)
    {
        return string.Equals(
                   first.OwnerKey,
                   second.OwnerKey,
                   StringComparison.Ordinal) &&
               string.Equals(
                   first.DisplayName,
                   second.DisplayName,
                   StringComparison.Ordinal) &&
               first.EnabledAddonIds.SetEquals(
                   second.EnabledAddonIds);
    }

    private static IReadOnlySet<string> AddToSet(
        IReadOnlySet<string> source,
        string value)
    {
        var result = new HashSet<string>(
            source,
            StringComparer.Ordinal)
        {
            value,
        };

        return result;
    }

    private static IReadOnlySet<string> RemoveFromSet(
        IReadOnlySet<string> source,
        string value)
    {
        var result = new HashSet<string>(
            source,
            StringComparer.Ordinal);

        result.Remove(value);
        return result;
    }

    private sealed class OwnerState
    {
        public required AddonOwnerRegistration Registration { get; set; }

        public AddonGameSnapshot? Snapshot { get; set; }

        public Dictionary<string, AddonRuntimeInstance> Runtimes { get; } =
            new(StringComparer.Ordinal);
    }
}
