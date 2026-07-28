namespace Net7ClientManager.Social;

using System.Globalization;
using System.Reflection;
using Net7ClientManager.Contributions;
using Net7ClientManager.Models;
using Net7ClientManager.Navigation;
using Net7ClientManager.Observations;
using Net7ClientManager.Observations.Models;

internal sealed class SocialCoordinator : IDisposable
{
    private static readonly TimeSpan recentlySeenWindow =
        TimeSpan.FromHours(1);

    private static readonly string clientVersion =
        typeof(SocialCoordinator).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ??
        typeof(SocialCoordinator).Assembly.GetName().Version?.ToString() ??
        "unknown";

    private readonly System.Threading.Lock stateLock = new();
    private readonly SemaphoreSlim operationLock = new(1, 1);
    private readonly CancellationTokenSource cancellation = new();
    private readonly Dictionary<string, ClientObservationSnapshot> latestByPilot =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ForgeSocialClient client = new();
    private readonly SocialSettings settings;
    private readonly Func<string?, CancellationToken, Task<ForgeContributionIdentity>> ensureMutationIdentity;
    private readonly Func<bool> hasIdentity;
    private readonly Action saveSettings;
    private GalaxyDataSet dataSet;
    private SocialDataSnapshot snapshot = SocialDataSnapshot.Empty;
    private Task? loopTask;
    private DateTimeOffset nextPublishAt = DateTimeOffset.MinValue;
    private DateTimeOffset nextRefreshAt = DateTimeOffset.MinValue;
    private bool disposed;

    public SocialCoordinator(
        SocialSettings settings,
        GalaxyDataSet dataSet,
        Func<string?, CancellationToken, Task<ForgeContributionIdentity>> ensureMutationIdentity,
        Func<bool> hasIdentity,
        Action saveSettings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(dataSet);
        ArgumentNullException.ThrowIfNull(ensureMutationIdentity);
        ArgumentNullException.ThrowIfNull(hasIdentity);
        ArgumentNullException.ThrowIfNull(saveSettings);

        settings.EnsureDefaults();
        this.settings = settings;
        this.dataSet = dataSet;
        this.ensureMutationIdentity = ensureMutationIdentity;
        this.hasIdentity = hasIdentity;
        this.saveSettings = saveSettings;
    }

    public event EventHandler<SocialSnapshotRefreshedEventArgs>?
        SnapshotRefreshed;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);

        if (this.loopTask != null)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        this.nextPublishAt = now.AddSeconds(5);
        this.nextRefreshAt = now.AddSeconds(2);
        this.loopTask = Task.Run(() => this.RunAsync(this.cancellation.Token));
    }

    public void Observe(ClientObservationSnapshot observation)
    {
        ArgumentNullException.ThrowIfNull(observation);

        if (!TryGetPilotName(observation, out var pilotName))
        {
            return;
        }

        lock (this.stateLock)
        {
            // Runtime observation is only the live source used for presence and
            // for explicitly saved profiles. Do not continuously rewrite the
            // LFG profession/level snapshot when a pilot dings; those fields
            // intentionally refresh only when the player saves the profile.
            this.latestByPilot[pilotName] = observation;
        }
    }

    public void UpdateDataSet(GalaxyDataSet dataSet)
    {
        ArgumentNullException.ThrowIfNull(dataSet);
        lock (this.stateLock)
        {
            this.dataSet = dataSet;
        }
    }

    public SocialDataSnapshot GetSnapshot()
    {
        lock (this.stateLock)
        {
            return this.snapshot;
        }
    }

    public SocialPilotSettings GetOrCreatePilotSettings(string pilotName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pilotName);

        lock (this.stateLock)
        {
            return this.settings.GetOrCreatePilot(pilotName);
        }
    }

    public SocialGuildRecruitmentSettings GetOrCreateGuildSettings(
        string guildName,
        string publishingPilotName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(guildName);
        ArgumentException.ThrowIfNullOrWhiteSpace(publishingPilotName);

        lock (this.stateLock)
        {
            return this.settings.GetOrCreateGuild(
                guildName,
                publishingPilotName);
        }
    }

    public IReadOnlyList<SocialLocalPilot> GetLocalPilots()
    {
        lock (this.stateLock)
        {
            Dictionary<string, SocialLocalPilot> pilots =
                new(StringComparer.OrdinalIgnoreCase);

            foreach (var (pilotName, observation) in this.latestByPilot)
            {
                var identity = observation.LocalPlayer.Operational.Identity;
                pilots[pilotName] = new SocialLocalPilot(
                    pilotName,
                    Normalize(identity.ProfessionName),
                    observation.LocalPlayer.CharacterProgression.OverallLevel,
                    Normalize(identity.GuildName),
                    observation.LifecycleState == ClientLifecycleState.InGame,
                    observation.ProcessId);
            }

            foreach (var (pilotName, saved) in this.settings.Pilots)
            {
                if (!pilots.ContainsKey(pilotName))
                {
                    pilots[pilotName] = new SocialLocalPilot(
                        pilotName,
                        saved.ProfessionNameSnapshot,
                        saved.OverallLevelSnapshot,
                        saved.GuildNameSnapshot,
                        false,
                        null);
                }
            }

            return [.. pilots.Values
                .OrderByDescending(pilot => pilot.IsRunning)
                .ThenBy(pilot => pilot.PilotName, StringComparer.OrdinalIgnoreCase)];
        }
    }

    public SocialPresenceFreshness GetFreshness(
        DateTimeOffset updatedAtUtc,
        DateTimeOffset? now = null)
    {
        var age = (now ?? DateTimeOffset.UtcNow) - updatedAtUtc;

        if (age <= TimeSpan.FromMinutes(this.settings.OnlineThresholdMinutes))
        {
            return SocialPresenceFreshness.Online;
        }

        if (age < recentlySeenWindow)
        {
            return SocialPresenceFreshness.RecentlySeen;
        }

        return SocialPresenceFreshness.Offline;
    }

    public async Task SavePilotAsync(
        string pilotName,
        bool publishLookingForGuild,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pilotName);
        this.settings.EnsureDefaults();
        this.saveSettings();

        await this.operationLock.WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            await this.PublishPilotPresenceCoreAsync(
                    pilotName.Trim(),
                    cancellationToken)
                .ConfigureAwait(false);

            if (publishLookingForGuild)
            {
                await this.PublishLookingForGuildCoreAsync(
                        pilotName.Trim(),
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            await this.RefreshCoreAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            this.operationLock.Release();
        }
    }

    public async Task SaveGuildAsync(
        string guildName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(guildName);
        this.settings.EnsureDefaults();
        this.saveSettings();

        await this.operationLock.WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            await this.PublishGuildCoreAsync(
                    guildName.Trim(),
                    cancellationToken)
                .ConfigureAwait(false);
            await this.RefreshCoreAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            this.operationLock.Release();
        }
    }

    public async Task RefreshAsync(
        CancellationToken cancellationToken = default)
    {
        await this.operationLock.WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            await this.RefreshCoreAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            this.operationLock.Release();
        }
    }

    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;
        this.cancellation.Cancel();

        try
        {
            this.loopTask?.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown.
        }

        this.client.Dispose();
        this.operationLock.Dispose();
        this.cancellation.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

        while (await timer.WaitForNextTickAsync(cancellationToken)
                   .ConfigureAwait(false))
        {
            var now = DateTimeOffset.UtcNow;

            if (now >= this.nextPublishAt)
            {
                this.nextPublishAt = now.AddSeconds(
                    this.settings.PresencePublishIntervalSeconds);
                await this.TryPeriodicOperationAsync(
                        this.PublishEnabledPresenceCoreAsync,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            if (now >= this.nextRefreshAt)
            {
                this.nextRefreshAt = now.AddSeconds(
                    this.settings.RefreshIntervalSeconds);
                await this.TryPeriodicOperationAsync(
                        this.RefreshCoreAsync,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private async Task TryPeriodicOperationAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        if (!await this.operationLock.WaitAsync(0, cancellationToken)
                .ConfigureAwait(false))
        {
            return;
        }

        try
        {
            await operation(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            this.SetStatus($"Social sync failed: {exception.Message}");
        }
        finally
        {
            this.operationLock.Release();
        }
    }

    private async Task PublishEnabledPresenceCoreAsync(
        CancellationToken cancellationToken)
    {
        string[] pilots;

        lock (this.stateLock)
        {
            pilots = [.. this.latestByPilot
                .Where(pair =>
                    pair.Value.LifecycleState == ClientLifecycleState.InGame &&
                    this.settings.Pilots.TryGetValue(pair.Key, out var pilot) &&
                    pilot.PublishPresence)
                .Select(pair => pair.Key)];
        }

        foreach (var pilotName in pilots)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await this.PublishPilotPresenceCoreAsync(
                        pilotName,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (
                exception is not OperationCanceledException)
            {
                this.SetStatus(
                    $"Could not publish {pilotName}'s presence: {exception.Message}");
            }
        }
    }

    private async Task PublishPilotPresenceCoreAsync(
        string pilotName,
        CancellationToken cancellationToken)
    {
        SocialPilotSettings pilotSettings;
        ClientObservationSnapshot? observation;
        GalaxyDataSet currentDataSet;

        lock (this.stateLock)
        {
            pilotSettings = this.settings.GetOrCreatePilot(pilotName);
            this.latestByPilot.TryGetValue(pilotName, out observation);
            currentDataSet = this.dataSet;
        }

        if (pilotSettings.PublishPresence &&
            observation?.LifecycleState != ClientLifecycleState.InGame)
        {
            this.SetStatus(
                $"{pilotName}'s presence will publish when the pilot is in game.");
            return;
        }

        var identity = await this.ResolveMutationIdentityAsync(
                pilotName,
                observation?.LifecycleState == ClientLifecycleState.InGame,
                cancellationToken)
            .ConfigureAwait(false);
        var location = BuildLocation(observation, currentDataSet);
        var unsigned = new SocialPresenceUpsertRequest(
            1,
            identity.ContributorId,
            Guid.NewGuid().ToString("D", CultureInfo.InvariantCulture),
            DateTimeOffset.UtcNow,
            clientVersion,
            pilotName,
            pilotSettings.PublishPresence,
            pilotSettings.AtlasVisibility,
            location.SectorId,
            location.SectorKey,
            location.SectorName,
            location.SystemName,
            location.ActiveSectorNumber,
            location.X,
            location.Y,
            location.Z,
            location.NearestNavObjectId,
            location.NearestNavName,
            "",
            location.StationName);
        var request = unsigned with
        {
            Signature = identity.Sign(unsigned),
        };

        _ = await this.client.UpsertPresenceAsync(request, cancellationToken)
            .ConfigureAwait(false);
        this.SetStatus($"Published {pilotName}'s social presence.");
    }

    private async Task PublishLookingForGuildCoreAsync(
        string pilotName,
        CancellationToken cancellationToken)
    {
        SocialPilotSettings settings;
        ClientObservationSnapshot? observation;

        lock (this.stateLock)
        {
            settings = this.settings.GetOrCreatePilot(pilotName);
            this.latestByPilot.TryGetValue(pilotName, out observation);

            if (observation != null)
            {
                settings.ProfessionNameSnapshot =
                    Normalize(observation.LocalPlayer.Operational.Identity.ProfessionName) ??
                    settings.ProfessionNameSnapshot;
                settings.OverallLevelSnapshot =
                    observation.LocalPlayer.CharacterProgression.OverallLevel ??
                    settings.OverallLevelSnapshot;
                settings.GuildNameSnapshot =
                    Normalize(observation.LocalPlayer.Operational.Identity.GuildName) ??
                    settings.GuildNameSnapshot;
            }
        }

        var identity = await this.ResolveMutationIdentityAsync(
                pilotName,
                observation?.LifecycleState == ClientLifecycleState.InGame,
                cancellationToken)
            .ConfigureAwait(false);
        var unsigned = new LookingForGuildUpsertRequest(
            1,
            identity.ContributorId,
            Guid.NewGuid().ToString("D", CultureInfo.InvariantCulture),
            DateTimeOffset.UtcNow,
            clientVersion,
            pilotName,
            settings.IsLookingForGuild,
            settings.ProfessionNameSnapshot,
            settings.OverallLevelSnapshot,
            settings.InterestTags,
            settings.Languages,
            Normalize(settings.OtherLanguage),
            Normalize(settings.Region),
            Normalize(settings.OtherRegion),
            Normalize(settings.Availability),
            Normalize(settings.Message),
            "");
        var request = unsigned with
        {
            Signature = identity.Sign(unsigned),
        };

        _ = await this.client.UpsertLookingForGuildAsync(request, cancellationToken)
            .ConfigureAwait(false);
        this.saveSettings();
        this.SetStatus($"Published {pilotName}'s Looking for Guild profile.");
    }

    private async Task PublishGuildCoreAsync(
        string guildName,
        CancellationToken cancellationToken)
    {
        SocialGuildRecruitmentSettings settings;

        lock (this.stateLock)
        {
            settings = this.settings.Guilds.TryGetValue(guildName, out var found)
                ? found
                : throw new InvalidOperationException(
                    $"No recruitment settings exist for {guildName}.");
        }

        var publishingPilotIsLive = false;

        lock (this.stateLock)
        {
            publishingPilotIsLive =
                this.latestByPilot.TryGetValue(
                    settings.PublishingPilotName,
                    out var observation) &&
                observation.LifecycleState == ClientLifecycleState.InGame;
        }

        var identity = await this.ResolveMutationIdentityAsync(
                settings.PublishingPilotName,
                publishingPilotIsLive,
                cancellationToken)
            .ConfigureAwait(false);
        var unsigned = new GuildRecruitmentUpsertRequest(
            1,
            identity.ContributorId,
            Guid.NewGuid().ToString("D", CultureInfo.InvariantCulture),
            DateTimeOffset.UtcNow,
            clientVersion,
            settings.GuildName,
            settings.PublishingPilotName,
            settings.IsRecruiting,
            Normalize(settings.OtherContacts),
            settings.FocusTags,
            settings.WantedProfessions,
            settings.Languages,
            Normalize(settings.OtherLanguage),
            Normalize(settings.Region),
            Normalize(settings.OtherRegion),
            Normalize(settings.ActiveTimes),
            Normalize(settings.Requirements),
            Normalize(settings.Message),
            "");
        var request = unsigned with
        {
            Signature = identity.Sign(unsigned),
        };

        _ = await this.client.UpsertGuildRecruitmentAsync(request, cancellationToken)
            .ConfigureAwait(false);
        this.SetStatus($"Published {guildName}'s recruitment listing.");
    }

    private async Task RefreshCoreAsync(CancellationToken cancellationToken)
    {
        var updatedAfterUtc = DateTimeOffset.UtcNow.AddDays(
            -this.settings.DiscoveryLookbackDays);
        var presenceTask = this.client.GetPresenceAsync(
            updatedAfterUtc,
            cancellationToken);
        var lookingTask = this.client.GetLookingForGuildAsync(
            updatedAfterUtc,
            cancellationToken);
        var guildTask = this.client.GetGuildRecruitmentAsync(
            updatedAfterUtc,
            cancellationToken);

        await Task.WhenAll(presenceTask, lookingTask, guildTask)
            .ConfigureAwait(false);

        SocialDataSnapshot previous;
        SocialDataSnapshot current;

        lock (this.stateLock)
        {
            previous = this.snapshot;
            current = new SocialDataSnapshot(
                presenceTask.Result,
                lookingTask.Result,
                guildTask.Result,
                DateTimeOffset.UtcNow,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Social data refreshed: {presenceTask.Result.Count} pilots, " +
                    $"{lookingTask.Result.Count} LFG profiles, " +
                    $"{guildTask.Result.Count} guilds."),
                this.snapshot.Version + 1);
            this.snapshot = current;
        }

        this.SnapshotRefreshed?.Invoke(
            this,
            new SocialSnapshotRefreshedEventArgs(previous, current));
    }

    private Task<ForgeContributionIdentity> ResolveMutationIdentityAsync(
        string pilotName,
        bool pilotIsLive,
        CancellationToken cancellationToken)
    {
        if (!this.hasIdentity() && !pilotIsLive)
        {
            throw new ForgeIdentityRequiredException();
        }

        // Only a live game observation may create a pilot claim. Once an
        // identity exists, offline Social edits authenticate with the existing
        // credential and let Forge verify that the selected pilot was already
        // claimed during a genuine login.
        return this.ensureMutationIdentity(
            pilotIsLive ? pilotName : null,
            cancellationToken);
    }

    private void SetStatus(string status)
    {
        lock (this.stateLock)
        {
            this.snapshot = this.snapshot with
            {
                Status = status,
                Version = this.snapshot.Version + 1,
            };
        }
    }

    private static SocialLocation BuildLocation(
        ClientObservationSnapshot? observation,
        GalaxyDataSet dataSet)
    {
        if (observation == null ||
            observation.LifecycleState != ClientLifecycleState.InGame)
        {
            return SocialLocation.Empty;
        }

        GalaxySectorDefinition? sector = null;
        if (!dataSet.Topology.TryResolve(
                observation.World.CurrentSectorName,
                out sector!) &&
            observation.World.ActiveSectorNumber != 0)
        {
            var catalogSector = dataSet.Catalog.FindSectorByActiveSectorNumber(
                observation.World.ActiveSectorNumber);

            if (catalogSector != null)
            {
                dataSet.Topology.TryGetByKey(
                    catalogSector.SectorKey,
                    out sector!);
            }
        }

        double? x = null;
        double? y = null;
        double? z = null;
        long? nearestNavObjectId = null;
        string? nearestNavName = null;

        var hasWorldSpatialPosition =
            (observation.World.Environment is
                ClientWorldEnvironment.Space or
                ClientWorldEnvironment.Planet or
                ClientWorldEnvironment.GasGiant) &&
            observation.LocalPlayer.Spatial.IsAvailable;

        if (hasWorldSpatialPosition)
        {
            var position = observation.LocalPlayer.Spatial.Position;
            x = position.X;
            y = position.Y;
            z = position.Z;

            var nearest = observation.Navigation.Targets
                .Where(target =>
                    target.IsAvailable &&
                    target.Spatial.IsAvailable &&
                    !string.IsNullOrWhiteSpace(
                        string.IsNullOrWhiteSpace(target.MapDisplayName)
                            ? target.Name
                            : target.MapDisplayName))
                .Select(target => new
                {
                    Target = target,
                    DistanceSquared = DistanceSquared(
                        position,
                        target.Spatial.Position),
                })
                .OrderBy(item => item.DistanceSquared)
                .FirstOrDefault();

            if (nearest != null)
            {
                nearestNavObjectId = nearest.Target.ObjectId;
                nearestNavName = string.IsNullOrWhiteSpace(
                    nearest.Target.MapDisplayName)
                    ? nearest.Target.Name.Trim()
                    : nearest.Target.MapDisplayName.Trim();
            }
        }

        return new SocialLocation(
            sector?.Key,
            sector?.Key,
            Normalize(observation.World.CurrentSectorName) ?? sector?.Name,
            Normalize(observation.World.CurrentSystemName) ?? sector?.SystemName,
            checked((int)observation.World.ActiveSectorNumber),
            x,
            y,
            z,
            nearestNavObjectId,
            nearestNavName,
            observation.World.Environment == ClientWorldEnvironment.Starbase
                ? Normalize(observation.World.CurrentStarbaseName)
                : null);
    }

    private static double DistanceSquared(
        ClientSpatialPosition left,
        ClientSpatialPosition right)
    {
        var dx = (double)left.X - right.X;
        var dy = (double)left.Y - right.Y;
        var dz = (double)left.Z - right.Z;
        return (dx * dx) + (dy * dy) + (dz * dz);
    }

    private static bool TryGetPilotName(
        ClientObservationSnapshot observation,
        out string pilotName)
    {
        pilotName = Normalize(
            ClientLiveCharacterIdentityResolver.Resolve(observation).Name) ??
            Normalize(observation.LocalPlayer.Operational.Identity.Name) ??
            "";
        return pilotName.Length > 0;
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private sealed record SocialLocation(
        string? SectorId,
        string? SectorKey,
        string? SectorName,
        string? SystemName,
        int ActiveSectorNumber,
        double? X,
        double? Y,
        double? Z,
        long? NearestNavObjectId,
        string? NearestNavName,
        string? StationName)
    {
        public static SocialLocation Empty { get; } = new(
            null,
            null,
            null,
            null,
            0,
            null,
            null,
            null,
            null,
            null,
            null);
    }
}
