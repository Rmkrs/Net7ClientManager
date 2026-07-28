namespace Net7ClientManager.Contributions;

using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Net7ClientManager.Addons.Development;
using Net7ClientManager.Models;
using Net7ClientManager.Navigation;
using Net7ClientManager.Observations;
using Net7ClientManager.Observations.Models;
using Net7ClientManager.SkillPlanning;

internal sealed partial class ForgeContributionCoordinator : IDisposable
{
    private static readonly TimeSpan RosterSettleTime =
        TimeSpan.FromSeconds(2);

    private static readonly TimeSpan FailedSubmissionRetryDelay =
        TimeSpan.FromMinutes(2);

    private static readonly string clientVersion =
        typeof(ForgeContributionCoordinator).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ??
        typeof(ForgeContributionCoordinator).Assembly.GetName().Version?
            .ToString() ??
        "unknown";

    private readonly System.Threading.Lock stateLock = new();
    private readonly Dictionary<int, ProcessRosterState> processStates = [];
    private readonly HashSet<string> inFlightRosterKeys =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> completedRosterKeys =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> sessionObservedStationKeys =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> sessionObservedNpcKeys =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> lifetimeObservedStationKeys;
    private readonly HashSet<string> lifetimeObservedNpcKeys;
    private readonly HashSet<Task> activeTasks = [];
    private readonly HashSet<string> inFlightPilotClaims =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> completedPilotClaims =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> pilotClaimRetryAt =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource cancellation = new();
    private CancellationTokenSource participationCancellation = new();
    private readonly ForgeContributionSettings settings;
    private GalaxyDataSet dataSet;
    private readonly ForgeContributionClient client = new();
    private readonly ForgeContributionIdentityService identityService;
    private readonly Dictionary<int, ClientObservationSnapshot> latestSnapshotsByProcessId = [];
    private readonly Action saveSettings;
    private readonly Func<string?> resolveLivePilotName;
    private readonly Func<string, bool> isPilotLive;
    private MutableStatistics session = new();
    private string status = "Contribution is disabled.";

    internal bool HasIdentity => this.identityService.HasIdentity;

    internal ForgeIdentityStatusSnapshot GetIdentityStatus() =>
        this.identityService.GetStatus();

    internal Task<ForgeIdentityStatusSnapshot> BeginIdentityRecoveryAsync(
        string livePilotName,
        CancellationToken cancellationToken) =>
        this.identityService.BeginRecoveryAsync(
            livePilotName,
            deviceLabel: null,
            cancellationToken);

    internal Task<ForgeIdentityStatusSnapshot> RefreshIdentityRecoveryAsync(
        CancellationToken cancellationToken) =>
        this.identityService.RefreshRecoveryAsync(cancellationToken);

    internal async Task<ForgeAddonPublicationResponse> PublishAddonAsync(
        string livePilotName,
        AddonPublicationSourcePackage sourcePackage,
        string? summary,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(livePilotName);
        ArgumentNullException.ThrowIfNull(sourcePackage);

        var identity = await this.EnsureIdentityAsync(
                livePilotName,
                cancellationToken)
            .ConfigureAwait(false);
        var unsignedRequest = new ForgeAddonPublicationRequest
        {
            ContributorId = identity.ContributorId,
            RequestId = Guid.NewGuid().ToString("D", CultureInfo.InvariantCulture),
            SubmittedAtUtc = DateTimeOffset.UtcNow,
            ClientVersion = clientVersion,
            LivePilotName = livePilotName.Trim(),
            AddonId = sourcePackage.Manifest.Id,
            Version = sourcePackage.Manifest.Version,
            Summary = string.IsNullOrWhiteSpace(summary)
                ? null
                : summary.Trim(),
            SourcePackageSha256 = sourcePackage.Sha256,
            SourcePackageBase64 = Convert.ToBase64String(
                sourcePackage.Bytes),
        };
        var request = unsignedRequest with
        {
            Signature = identity.Sign(unsignedRequest),
        };

        return await this.client.PublishAddonAsync(
                request,
                cancellationToken)
            .ConfigureAwait(false);
    }

    internal Task<ForgeContributionIdentity> EnsureIdentityAsync(
        string livePilotName,
        CancellationToken cancellationToken)
    {
        return this.identityService.EnsureAsync(
            livePilotName,
            cancellationToken);
    }

    internal Task<ForgeContributionIdentity> EnsureMutationIdentityAsync(
        string? livePilotName,
        CancellationToken cancellationToken)
    {
        return this.identityService.EnsureMutationIdentityAsync(
            livePilotName,
            cancellationToken);
    }

    internal async Task<ForgeBuildPublicationResponse> PublishBuildAsync(
        SkillBuildDocument build,
        string? forgeBuildId,
        string pilotName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(build);
        ArgumentException.ThrowIfNullOrWhiteSpace(pilotName);
        var prepared = SkillBuildForgeDocumentCodec.Prepare(build);
        var publisherIsLive = this.isPilotLive(pilotName.Trim());
        var identity = publisherIsLive
            ? await this.EnsureIdentityAsync(pilotName, cancellationToken)
                .ConfigureAwait(false)
            : await this.identityService.EnsureMutationIdentityAsync(
                    livePilotName: null,
                    cancellationToken)
                .ConfigureAwait(false);
        var unsigned = new ForgeBuildPublicationRequest
        {
            ContributorId = identity.ContributorId,
            RequestId = Guid.NewGuid().ToString("D", CultureInfo.InvariantCulture),
            SubmittedAtUtc = DateTimeOffset.UtcNow,
            ClientVersion = clientVersion,
            LivePilotName = pilotName.Trim(),
            BuildId = string.IsNullOrWhiteSpace(forgeBuildId)
                ? null
                : forgeBuildId.Trim(),
            DocumentSha256 = ComputeSha256(prepared.CanonicalJson),
            DocumentJson = prepared.CanonicalJson,
        };
        var request = unsigned with
        {
            Signature = identity.Sign(unsigned),
        };
        return await this.client.PublishBuildAsync(request, cancellationToken)
            .ConfigureAwait(false);
    }

    internal async Task<ForgeBuildSearchResponse> SearchBuildsAsync(
        string query,
        int? professionIndex,
        string publisherPilotName,
        bool starredOnly,
        bool ownedOnly,
        string sort,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        var identity = this.TryGetOptionalReadIdentity();
        if (identity == null && (starredOnly || ownedOnly))
        {
            throw new ForgeBuildApiException(
                "build_identity_required",
                "Connect this installation to Forge before filtering by your builds or stars.",
                400);
        }

        var unsigned = new ForgeBuildSearchRequest
        {
            ContributorId = identity?.ContributorId ?? "",
            RequestId = Guid.NewGuid().ToString("D", CultureInfo.InvariantCulture),
            SubmittedAtUtc = DateTimeOffset.UtcNow,
            ClientVersion = clientVersion,
            LivePilotName = "",
            Query = query?.Trim() ?? "",
            ProfessionIndex = professionIndex,
            PublisherPilotName = publisherPilotName?.Trim() ?? "",
            StarredOnly = starredOnly,
            OwnedOnly = ownedOnly,
            Sort = sort?.Trim() ?? "relevance",
            Offset = offset,
            Limit = limit,
        };
        var request = identity == null
            ? unsigned
            : unsigned with
            {
                Signature = identity.Sign(unsigned),
            };
        try
        {
            return await this.client.SearchBuildsAsync(request, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ForgeBuildApiException exception) when (
            identity != null &&
            !starredOnly &&
            !ownedOnly &&
            IsOptionalIdentityReadFailure(exception))
        {
            // Build discovery is public. A disabled, revoked, or stale local
            // credential must remove mutation authority, not prevent the user
            // from browsing the anonymous catalogue.
            return await this.client.SearchBuildsAsync(
                    unsigned with
                    {
                        ContributorId = "",
                        Signature = "",
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    internal async Task<ForgeBuildDetailsResponse> GetBuildDetailsAsync(
        string buildId,
        CancellationToken cancellationToken)
    {
        var request = this.CreateBuildAccessRequest(buildId, null);
        try
        {
            return await this.client.GetBuildDetailsAsync(request, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ForgeBuildApiException exception) when (
            !string.IsNullOrWhiteSpace(request.ContributorId) &&
            IsOptionalIdentityReadFailure(exception))
        {
            return await this.client.GetBuildDetailsAsync(
                    CreateAnonymousBuildAccessRequest(buildId, null),
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    internal async Task<ForgeBuildVersionResponse> GetBuildVersionAsync(
        string buildId,
        int version,
        CancellationToken cancellationToken)
    {
        var request = this.CreateBuildAccessRequest(buildId, version);
        try
        {
            return await this.client.GetBuildVersionAsync(request, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ForgeBuildApiException exception) when (
            !string.IsNullOrWhiteSpace(request.ContributorId) &&
            IsOptionalIdentityReadFailure(exception))
        {
            return await this.client.GetBuildVersionAsync(
                    CreateAnonymousBuildAccessRequest(buildId, version),
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    internal async Task<ForgeBuildStarResponse> SetBuildStarAsync(
        string buildId,
        bool starred,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(buildId);
        var livePilotName = this.identityService.HasIdentity
            ? null
            : this.resolveLivePilotName();
        var identity = await this.identityService.EnsureMutationIdentityAsync(
                livePilotName,
                cancellationToken)
            .ConfigureAwait(false);
        var unsigned = new ForgeBuildStarRequest
        {
            ContributorId = identity.ContributorId,
            RequestId = Guid.NewGuid().ToString("D", CultureInfo.InvariantCulture),
            SubmittedAtUtc = DateTimeOffset.UtcNow,
            ClientVersion = clientVersion,
            LivePilotName = livePilotName?.Trim() ?? "",
            BuildId = buildId.Trim(),
            Starred = starred,
        };
        var request = unsigned with
        {
            Signature = identity.Sign(unsigned),
        };
        return await this.client.SetBuildStarAsync(request, cancellationToken)
            .ConfigureAwait(false);
    }

    private ForgeBuildAccessRequest CreateBuildAccessRequest(
        string buildId,
        int? version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(buildId);
        var identity = this.TryGetOptionalReadIdentity();
        var unsigned = new ForgeBuildAccessRequest
        {
            ContributorId = identity?.ContributorId ?? "",
            RequestId = Guid.NewGuid().ToString("D", CultureInfo.InvariantCulture),
            SubmittedAtUtc = DateTimeOffset.UtcNow,
            ClientVersion = clientVersion,
            LivePilotName = "",
            BuildId = buildId.Trim(),
            Version = version,
        };
        return identity == null
            ? unsigned
            : unsigned with
            {
                Signature = identity.Sign(unsigned),
            };
    }

    private static ForgeBuildAccessRequest CreateAnonymousBuildAccessRequest(
        string buildId,
        int? version)
    {
        return new ForgeBuildAccessRequest
        {
            ContributorId = "",
            RequestId = Guid.NewGuid().ToString("D", CultureInfo.InvariantCulture),
            SubmittedAtUtc = DateTimeOffset.UtcNow,
            ClientVersion = clientVersion,
            LivePilotName = "",
            BuildId = buildId.Trim(),
            Version = version,
        };
    }

    private ForgeContributionIdentity? TryGetOptionalReadIdentity()
    {
        try
        {
            return this.identityService.TryGetExistingIdentity();
        }
        catch (Exception exception) when (
            exception is CryptographicException or FormatException)
        {
            // Public build discovery must stay available even when local
            // credential material is damaged. The next mutation will run the
            // normal identity repair/reset path instead.
            return null;
        }
    }

    private static bool IsOptionalIdentityReadFailure(
        ForgeBuildApiException exception)
    {
        return exception.Code is
            "unknown_contributor" or
            "contributor_disabled" or
            "no_credential" or
            "credential_disabled" or
            "invalid_signature";
    }

    private static string ComputeSha256(string value) =>
        Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    public ForgeContributionCoordinator(
        ForgeContributionSettings settings,
        GalaxyDataSet dataSet,
        Func<string?> resolveLivePilotName,
        Func<string, bool> isPilotLive,
        Action saveSettings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(dataSet);
        ArgumentNullException.ThrowIfNull(resolveLivePilotName);
        ArgumentNullException.ThrowIfNull(isPilotLive);
        ArgumentNullException.ThrowIfNull(saveSettings);

        settings.EnsureDefaults();
        this.settings = settings;
        this.dataSet = dataSet;
        this.resolveLivePilotName = resolveLivePilotName;
        this.isPilotLive = isPilotLive;
        this.saveSettings = saveSettings;
        this.lifetimeObservedStationKeys = new HashSet<string>(
            settings.Lifetime.ObservedStationKeys,
            StringComparer.Ordinal);
        this.lifetimeObservedNpcKeys = new HashSet<string>(
            settings.Lifetime.ObservedNpcKeys,
            StringComparer.Ordinal);
        this.lifetimeObservedFacilityKeys = new HashSet<string>(
            settings.Lifetime.ObservedStationFacilityKeys,
            StringComparer.Ordinal);
        this.lifetimeObservedNavigationObjectKeys = new HashSet<string>(
            settings.Lifetime.ObservedNavigationObjectKeys,
            StringComparer.Ordinal);
        this.lifetimeObservedVendorKeys = new HashSet<string>(
            settings.Lifetime.ObservedVendorKeys,
            StringComparer.Ordinal);
        this.lifetimeObservedVendorItemKeys = new HashSet<string>(
            settings.Lifetime.ObservedVendorItemKeys,
            StringComparer.Ordinal);
        this.lifetimeObservedMobSightingKeys = new HashSet<string>(
            settings.Lifetime.ObservedMobSightingKeys,
            StringComparer.Ordinal);
        this.lifetimeObservedMobLootRelationshipKeys = new HashSet<string>(
            settings.Lifetime.ObservedMobLootRelationshipKeys,
            StringComparer.Ordinal);
        this.lifetimeObservedHarvestableResourceKeys = new HashSet<string>(
            settings.Lifetime.ObservedHarvestableResourceKeys,
            StringComparer.Ordinal);
        this.lifetimeObservedProductionRecipeKeys = new HashSet<string>(
            settings.Lifetime.ObservedProductionRecipeKeys,
            StringComparer.Ordinal);
        this.lifetimeObservedMissionKeys = new HashSet<string>(
            settings.Lifetime.ObservedMissionKeys,
            StringComparer.Ordinal);
        this.lifetimeObservedJobOfferKeys = new HashSet<string>(
            settings.Lifetime.ObservedJobOfferKeys,
            StringComparer.Ordinal);
        this.acceptedMobLootRelationshipIds.UnionWith(
            settings.PendingMobLootRelationshipIds);
        this.acceptedHarvestableResourceIds.UnionWith(
            settings.PendingHarvestableResourceIds);
        this.identityService = new ForgeContributionIdentityService(
            this.client,
            settings,
            saveSettings);
#if DEBUG
        ValidateNavigationSignatureRegression();
#endif
    }

    public event EventHandler<ForgeContributionStatisticsChangedEventArgs>?
        StatisticsChanged;

    public event EventHandler<ForgeContributionRevisionPublishedEventArgs>?
        RevisionPublished;

    public bool CanActivateDataSet(out string reason)
    {
        lock (this.stateLock)
        {
            if (this.activeTasks.Count == 0 &&
                this.inFlightRosterKeys.Count == 0 &&
                this.inFlightNavigationBatchKeys.Count == 0 &&
                this.inFlightFacilityRosterKeys.Count == 0 &&
                this.inFlightVendorCatalogKeys.Count == 0 &&
                this.inFlightMobBatchKeys.Count == 0 &&
                this.inFlightMobLootBatchKeys.Count == 0 &&
                this.inFlightHarvestableBatchKeys.Count == 0 &&
                this.inFlightProductionRecipeKeys.Count == 0 &&
                this.inFlightMissionKeys.Count == 0 &&
                this.inFlightJobOfferKeys.Count == 0 &&
                this.inFlightGravityWellKeys.Count == 0)
            {
                reason = "";
                return true;
            }

            reason =
                "Wait for the current Forge contribution batch to finish before activating new data.";
            return false;
        }
    }

    public void UpdateDataSet(GalaxyDataSet dataSet)
    {
        ArgumentNullException.ThrowIfNull(dataSet);
        var settingsChanged = false;

        lock (this.stateLock)
        {
            this.dataSet = dataSet;
            this.processStates.Clear();
            this.latestSnapshotsByProcessId.Clear();
            this.navigationProcessStates.Clear();
            this.facilityProcessStates.Clear();
            this.mobLootProcessStates.Clear();
            this.harvestableProcessStates.Clear();
            this.productionRecipeProcessStates.Clear();
            this.missionProcessStates.Clear();
            foreach (var vendorState in this.vendorInventoryProcessStates.Values)
            {
                vendorState.EndGeneration();
            }

            settingsChanged = this.PruneActivatedMobLootRelationshipIds();
            settingsChanged |= this.PruneActivatedHarvestableResourceIds();
            this.status = string.Create(
                CultureInfo.InvariantCulture,
                $"Forge dataset revision {dataSet.AuthorityRevision} is active.");
        }

        if (settingsChanged)
        {
            this.saveSettings();
        }

        this.RaiseStatisticsChanged();
    }

    private void ObserveNpcPresence(ClientObservationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!this.settings.Enabled ||
            !this.settings.Categories.NpcPresence)
        {
            lock (this.stateLock)
            {
                this.processStates.Remove(snapshot.ProcessId);
                this.status = this.settings.Enabled
                    ? "NPC contribution is disabled."
                    : "Contribution is disabled.";
            }

            return;
        }

        if (!TryCreateRoster(
                snapshot,
                out var roster,
                out var unavailableReason))
        {
            lock (this.stateLock)
            {
                this.processStates.Remove(snapshot.ProcessId);

                if (!string.IsNullOrWhiteSpace(unavailableReason))
                {
                    this.status = unavailableReason;
                }
            }

            return;
        }

        string? submissionKey = null;
        IReadOnlyList<ObservedNpc> unknownNpcs = [];
        HashSet<string> knownNpcIds = new(StringComparer.Ordinal);
        var completedWithoutSubmission = false;
        CancellationToken participationToken = default;

        lock (this.stateLock)
        {
            if (!this.processStates.TryGetValue(
                    snapshot.ProcessId,
                    out var state) ||
                !string.Equals(
                    state.Fingerprint,
                    roster.Fingerprint,
                    StringComparison.Ordinal))
            {
                this.processStates[snapshot.ProcessId] =
                    new ProcessRosterState(
                        roster.Fingerprint,
                        snapshot.ObservedAt);
                this.status = string.Create(
                    CultureInfo.InvariantCulture,
                    $"Waiting for the NPC roster at {roster.StationName} to settle.");
                return;
            }

            state.ObservationCount++;
            roster = roster with
            {
                FirstObservedAtUtc = state.StableSince,
                LastObservedAtUtc = snapshot.ObservedAt,
                ObservationCount = state.ObservationCount,
            };

            if (snapshot.ObservedAt - state.StableSince < RosterSettleTime ||
                snapshot.ObservedAt < state.NextAttemptAllowedAt ||
                string.Equals(
                    state.AttemptedFingerprint,
                    roster.Fingerprint,
                    StringComparison.Ordinal))
            {
                return;
            }

            state.AttemptedFingerprint = roster.Fingerprint;
            state.NextAttemptAllowedAt = DateTimeOffset.MaxValue;
            (unknownNpcs, knownNpcIds) = this.FindUncoveredNpcs(roster.Npcs);
            this.RecordObservedRoster(roster, knownNpcIds);
            this.status = unknownNpcs.Count == 0
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"{roster.Npcs.Count} NPCs at {roster.StationName} are already known by Forge.")
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"Preparing {unknownNpcs.Count} new or conflicting NPC facts from {roster.StationName}.");

            if (unknownNpcs.Count == 0)
            {
                completedWithoutSubmission = true;
            }

            if (completedWithoutSubmission)
            {
                submissionKey = null;
            }
            else
            {

                submissionKey = this.CreateSubmissionKey(roster);

                if (this.completedRosterKeys.Contains(submissionKey) ||
                    !this.inFlightRosterKeys.Add(submissionKey))
                {
                    return;
                }

                participationToken = this.participationCancellation.Token;
            }
        }

        this.saveSettings();
        this.RaiseStatisticsChanged();

        if (completedWithoutSubmission)
        {
            return;
        }

        var submissionCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                this.cancellation.Token,
                participationToken);
        var task = this.SubmitAsync(
            roster,
            unknownNpcs,
            submissionKey!,
            submissionCancellation);
        this.Track(task);
    }

    public void ResetSessionStatistics()
    {
        lock (this.stateLock)
        {
            this.session = new MutableStatistics();
            this.sessionObservedStationKeys.Clear();
            this.sessionObservedNpcKeys.Clear();
            this.sessionObservedFacilityKeys.Clear();
            this.sessionObservedNavigationObjectKeys.Clear();
            this.sessionObservedVendorKeys.Clear();
            this.sessionObservedVendorItemKeys.Clear();
            this.sessionObservedMobSightingKeys.Clear();
            this.sessionObservedMobLootRelationshipKeys.Clear();
            this.sessionObservedHarvestableResourceKeys.Clear();
            this.sessionObservedProductionRecipeKeys.Clear();
            this.sessionObservedMissionKeys.Clear();
            this.sessionObservedJobOfferKeys.Clear();
            this.status = this.settings.Enabled
                ? "Waiting for a settled supported observation."
                : "Contribution is disabled.";
        }

        this.RaiseStatisticsChanged();
    }

    public (ForgeContributionStatisticsSnapshot Session,
        ForgeContributionStatisticsSnapshot Lifetime) GetStatistics()
    {
        lock (this.stateLock)
        {
            return (
                this.CreateSessionSnapshot(),
                this.CreateLifetimeSnapshot());
        }
    }

    public void SettingsChanged()
    {
        CancellationTokenSource previousParticipationCancellation;

        lock (this.stateLock)
        {
            previousParticipationCancellation = this.participationCancellation;
            this.participationCancellation = new CancellationTokenSource();
            this.processStates.Clear();
            this.latestSnapshotsByProcessId.Clear();
            this.navigationProcessStates.Clear();
            this.facilityProcessStates.Clear();
            this.vendorInventoryProcessStates.Clear();
            this.mobProcessStates.Clear();
            this.mobLootProcessStates.Clear();
            this.harvestableProcessStates.Clear();
            this.productionRecipeProcessStates.Clear();
            this.missionProcessStates.Clear();
            this.completedJobOfferKeys.Clear();
            this.jobOfferRetryAllowedAt.Clear();
            this.inFlightRosterKeys.Clear();
            this.inFlightNavigationBatchKeys.Clear();
            this.inFlightFacilityRosterKeys.Clear();
            this.inFlightVendorCatalogKeys.Clear();
            this.inFlightMobBatchKeys.Clear();
            this.inFlightMobLootBatchKeys.Clear();
            this.inFlightHarvestableBatchKeys.Clear();
            this.inFlightProductionRecipeKeys.Clear();
            this.inFlightMissionKeys.Clear();
            this.inFlightJobOfferKeys.Clear();
            this.status = this.settings.Enabled
                ? "Waiting for a settled supported observation."
                : "Contribution is disabled.";
        }

        previousParticipationCancellation.Cancel();
        previousParticipationCancellation.Dispose();
        this.RaiseStatisticsChanged();
    }

    public void Dispose()
    {
        this.cancellation.Cancel();
        this.participationCancellation.Cancel();
        Task[] tasks;

        lock (this.stateLock)
        {
            tasks = [.. this.activeTasks];
        }

        try
        {
            Task.WaitAll(tasks, TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // Best-effort network work is abandoned during application exit.
        }

        this.participationCancellation.Dispose();
        this.cancellation.Dispose();
        this.client.Dispose();
    }

    private async Task SubmitAsync(
        ObservedRoster roster,
        IReadOnlyList<ObservedNpc> unknownNpcs,
        string submissionKey,
        CancellationTokenSource submissionCancellation)
    {
        var cancellationToken = submissionCancellation.Token;

        try
        {
            var identity = await this.identityService.EnsureAsync(
                    roster.LivePilotName,
                    cancellationToken)
                .ConfigureAwait(false);
            var unsignedRequest = new ForgeNpcPresenceContributionRequest
            {
                ContributorId = identity.ContributorId,
                RequestId = Guid.NewGuid().ToString("D", CultureInfo.InvariantCulture),
                SubmittedAtUtc = DateTimeOffset.UtcNow,
                FirstObservedAtUtc = roster.FirstObservedAtUtc,
                LastObservedAtUtc = roster.LastObservedAtUtc,
                ObservationCount = roster.ObservationCount,
                ClientVersion = clientVersion,
                DatasetRevision = this.dataSet.AuthorityRevision,
                Attribution = this.settings.Attribution ==
                    ForgeContributionAttribution.LivePilotName
                        ? "live-pilot-name"
                        : "publicly-anonymous",
                LivePilotName = roster.LivePilotName,
                StarbaseId = roster.StarbaseId,
                StationName = roster.StationName,
                SectorName = roster.SectorName,
                ActiveSectorNumber = roster.ActiveSectorNumber,
                Npcs =
                [
                    .. unknownNpcs.Select(npc =>
                        new ForgeNpcPresenceContributionItem
                        {
                            RoomClass = npc.RoomClass,
                            RoomDefinitionKey = npc.RoomDefinitionKey,
                            RoomNpcSlot = npc.RoomNpcSlot,
                            DefinitionKey = npc.DefinitionKey,
                            DefinitionSecondaryId = npc.DefinitionSecondaryId,
                            Name = npc.Name,
                            Role = (int)npc.VendorType,
                            Classification = (int)npc.AmbientType,
                        }),
                ],
            };
            var request = unsignedRequest with
            {
                Signature = identity.Sign(unsignedRequest),
            };
            var response = await this.client.SubmitNpcPresenceAsync(
                    request,
                    cancellationToken)
                .ConfigureAwait(false);

            lock (this.stateLock)
            {
                this.completedRosterKeys.Add(submissionKey);
                this.session.NpcFactsSubmitted += response.Received;
                this.session.AlreadyCanonical += response.AlreadyCanonical;
                this.session.EvidenceAccepted += response.EvidenceAccepted;
                this.session.Conflicts += response.Conflicts;
                this.session.SuccessfulBatches++;
                this.session.LastSuccessfulContributionUtc = DateTimeOffset.UtcNow;

                var lifetime = this.settings.Lifetime;
                lifetime.NpcFactsSubmitted += response.Received;
                lifetime.AlreadyCanonical += response.AlreadyCanonical;
                lifetime.EvidenceAccepted += response.EvidenceAccepted;
                lifetime.Conflicts += response.Conflicts;
                lifetime.SuccessfulBatches++;
                lifetime.LastSuccessfulContributionUtc = DateTimeOffset.UtcNow;

                if (response.PublishedRevision != null)
                {
                    this.session.PublishedRevisions++;
                    lifetime.PublishedRevisions++;
                }

                this.status = response.PublishedRevision is { } revision
                    ? string.Create(
                        CultureInfo.InvariantCulture,
                        $"Forge accepted the NPC batch and published revision {revision}. Activate the staged Forge dataset update when ready.")
                    : string.Create(
                        CultureInfo.InvariantCulture,
                        $"Forge accepted the NPC batch: {response.EvidenceAccepted} evidence facts, {response.AlreadyCanonical} already canonical, {response.Conflicts} conflicts.");
            }

            this.saveSettings();
            this.RaiseStatisticsChanged();

            if (response.PublishedRevision is { } publishedRevision)
            {
                this.RevisionPublished?.Invoke(
                    this,
                    new ForgeContributionRevisionPublishedEventArgs(
                        publishedRevision));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Application shutdown abandons best-effort contribution work.
        }
        catch (Exception exception)
        {
            lock (this.stateLock)
            {
                this.session.FailedBatches++;
                this.session.LastFailedContributionUtc = DateTimeOffset.UtcNow;
                this.settings.Lifetime.FailedBatches++;
                this.settings.Lifetime.LastFailedContributionUtc = DateTimeOffset.UtcNow;
                this.status = string.Concat(
                    "Forge contribution failed: ",
                    exception.Message,
                    ". The settled roster can be retried later this session.");

                if (this.processStates.TryGetValue(
                        roster.ProcessId,
                        out var state) &&
                    string.Equals(
                        state.Fingerprint,
                        roster.Fingerprint,
                        StringComparison.Ordinal))
                {
                    state.AttemptedFingerprint = null;
                    state.NextAttemptAllowedAt =
                        DateTimeOffset.UtcNow + FailedSubmissionRetryDelay;
                }
            }

            this.saveSettings();
            this.RaiseStatisticsChanged();
        }
        finally
        {
            lock (this.stateLock)
            {
                this.inFlightRosterKeys.Remove(submissionKey);
            }

            submissionCancellation.Dispose();
        }
    }

    private void Track(Task task)
    {
        lock (this.stateLock)
        {
            this.activeTasks.Add(task);
        }

        _ = task.ContinueWith(
            completed =>
            {
                lock (this.stateLock)
                {
                    this.activeTasks.Remove(completed);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private (IReadOnlyList<ObservedNpc> Unknown, HashSet<string> KnownIds)
        FindUncoveredNpcs(IReadOnlyList<ObservedNpc> observed)
    {
        var knownById = this.dataSet.Document.Npcs.ToDictionary(
            npc => npc.Id,
            StringComparer.Ordinal);
        List<ObservedNpc> unknown = [];
        HashSet<string> knownIds = new(StringComparer.Ordinal);

        foreach (var npc in observed)
        {
            var id = CreateNpcId(npc);

            if (knownById.TryGetValue(id, out var existing) &&
                IsCompatible(existing, npc))
            {
                knownIds.Add(id);
            }
            else
            {
                unknown.Add(npc);
            }
        }

        return (unknown, knownIds);
    }

    private void RecordObservedRoster(
        ObservedRoster roster,
        IReadOnlySet<string> knownNpcIds)
    {
        var stationKey = CreateStationId(
            roster.StarbaseId,
            roster.StationName,
            roster.SectorName);

        if (this.sessionObservedStationKeys.Add(stationKey))
        {
            this.session.StationsObserved =
                this.sessionObservedStationKeys.Count;
        }

        if (this.lifetimeObservedStationKeys.Add(stationKey))
        {
            this.settings.Lifetime.ObservedStationKeys.Add(stationKey);
            this.settings.Lifetime.StationsObserved =
                this.lifetimeObservedStationKeys.Count;
        }

        foreach (var npc in roster.Npcs)
        {
            var npcKey = CreateNpcId(npc);

            if (this.sessionObservedNpcKeys.Add(npcKey))
            {
                this.session.NpcsObserved = this.sessionObservedNpcKeys.Count;

                if (knownNpcIds.Contains(npcKey))
                {
                    this.session.AlreadyCanonical++;
                }
            }

            if (this.lifetimeObservedNpcKeys.Add(npcKey))
            {
                this.settings.Lifetime.ObservedNpcKeys.Add(npcKey);
                this.settings.Lifetime.NpcsObserved =
                    this.lifetimeObservedNpcKeys.Count;

                if (knownNpcIds.Contains(npcKey))
                {
                    this.settings.Lifetime.AlreadyCanonical++;
                }
            }
        }
    }

    private string CreateSubmissionKey(ObservedRoster roster)
    {
        var attributionIdentity = this.settings.Attribution ==
            ForgeContributionAttribution.LivePilotName
                ? NormalizeKey(roster.LivePilotName)
                : "anonymous";
        return string.Concat(
            this.dataSet.AuthorityRevision.ToString(CultureInfo.InvariantCulture),
            "|",
            attributionIdentity,
            "|",
            roster.Fingerprint);
    }

    private ForgeContributionStatisticsSnapshot CreateSessionSnapshot()
    {
        return this.session.ToSnapshot(
            this.settings.Enabled,
            !string.IsNullOrWhiteSpace(this.settings.ContributorId),
            this.status);
    }

    private ForgeContributionStatisticsSnapshot CreateLifetimeSnapshot()
    {
        var lifetime = this.settings.Lifetime;
        return new ForgeContributionStatisticsSnapshot
        {
            Enabled = this.settings.Enabled,
            IdentityRegistered = !string.IsNullOrWhiteSpace(
                this.settings.ContributorId),
            Status = this.status,
            StationsObserved = lifetime.StationsObserved,
            NpcsObserved = lifetime.NpcsObserved,
            NpcFactsSubmitted = lifetime.NpcFactsSubmitted,
            AlreadyCanonical = lifetime.AlreadyCanonical,
            EvidenceAccepted = lifetime.EvidenceAccepted,
            Conflicts = lifetime.Conflicts,
            StationFacilitiesObserved = lifetime.StationFacilitiesObserved,
            StationFacilityFactsSubmitted = lifetime.StationFacilityFactsSubmitted,
            StationFacilitiesAlreadyCanonical =
                lifetime.StationFacilitiesAlreadyCanonical,
            StationFacilityEvidenceAccepted =
                lifetime.StationFacilityEvidenceAccepted,
            StationFacilityConflicts = lifetime.StationFacilityConflicts,
            NavigationObjectsObserved = lifetime.NavigationObjectsObserved,
            NavigationObjectFactsSubmitted =
                lifetime.NavigationObjectFactsSubmitted,
            NavigationObjectsAlreadyCanonical =
                lifetime.NavigationObjectsAlreadyCanonical,
            NavigationObjectEvidenceAccepted =
                lifetime.NavigationObjectEvidenceAccepted,
            NavigationObjectConflicts = lifetime.NavigationObjectConflicts,
            VendorsObserved = lifetime.VendorsObserved,
            VendorItemsObserved = lifetime.VendorItemsObserved,
            VendorItemFactsSubmitted = lifetime.VendorItemFactsSubmitted,
            VendorItemsAlreadyCanonical = lifetime.VendorItemsAlreadyCanonical,
            VendorItemEvidenceAccepted = lifetime.VendorItemEvidenceAccepted,
            VendorItemConflicts = lifetime.VendorItemConflicts,
            VendorItemsRemoved = lifetime.VendorItemsRemoved,
            MobSightingsObserved = lifetime.MobSightingsObserved,
            MobSightingFactsSubmitted = lifetime.MobSightingFactsSubmitted,
            MobSightingEvidenceAccepted = lifetime.MobSightingEvidenceAccepted,
            MobVariantsCreated = lifetime.MobVariantsCreated,
            MobClustersCreated = lifetime.MobClustersCreated,
            MobClustersUpdated = lifetime.MobClustersUpdated,
            LootCorpsesObserved = lifetime.LootCorpsesObserved,
            MobLootRelationshipsObserved =
                lifetime.MobLootRelationshipsObserved,
            MobLootFactsSubmitted = lifetime.MobLootFactsSubmitted,
            MobLootAlreadyCanonical = lifetime.MobLootAlreadyCanonical,
            MobLootEvidenceAccepted = lifetime.MobLootEvidenceAccepted,
            MobLootVariantsCreated = lifetime.MobLootVariantsCreated,
            MobLootRelationshipsCreated =
                lifetime.MobLootRelationshipsCreated,
            MobLootRelationshipsStrengthened =
                lifetime.MobLootRelationshipsStrengthened,
            HarvestableResourcesObserved =
                lifetime.HarvestableResourcesObserved,
            HarvestableFactsSubmitted = lifetime.HarvestableFactsSubmitted,
            HarvestableAlreadyCanonical = lifetime.HarvestableAlreadyCanonical,
            HarvestableEvidenceAccepted = lifetime.HarvestableEvidenceAccepted,
            HarvestableVariantsCreated = lifetime.HarvestableVariantsCreated,
            HarvestableFieldsCreated = lifetime.HarvestableFieldsCreated,
            HarvestableFieldsUpdated = lifetime.HarvestableFieldsUpdated,
            HarvestableRelationshipsCreated =
                lifetime.HarvestableRelationshipsCreated,
            HarvestableRelationshipsStrengthened =
                lifetime.HarvestableRelationshipsStrengthened,
            ProductionRecipesObserved = lifetime.ProductionRecipesObserved,
            ProductionRecipeFactsSubmitted =
                lifetime.ProductionRecipeFactsSubmitted,
            ProductionRecipesAlreadyCanonical =
                lifetime.ProductionRecipesAlreadyCanonical,
            ProductionRecipeEvidenceAccepted =
                lifetime.ProductionRecipeEvidenceAccepted,
            ProductionRecipeConflicts = lifetime.ProductionRecipeConflicts,
            ProductionRecipesCreated = lifetime.ProductionRecipesCreated,
            MissionsObserved = lifetime.MissionsObserved,
            MissionFactsSubmitted = lifetime.MissionFactsSubmitted,
            MissionsAlreadyCanonical = lifetime.MissionsAlreadyCanonical,
            MissionEvidenceAccepted = lifetime.MissionEvidenceAccepted,
            MissionConflicts = lifetime.MissionConflicts,
            MissionsCreated = lifetime.MissionsCreated,
            MissionsStrengthened = lifetime.MissionsStrengthened,
            JobOffersObserved = lifetime.JobOffersObserved,
            JobOfferFactsSubmitted = lifetime.JobOfferFactsSubmitted,
            JobOffersAlreadyCanonical = lifetime.JobOffersAlreadyCanonical,
            JobOfferEvidenceAccepted = lifetime.JobOfferEvidenceAccepted,
            JobOfferConflicts = lifetime.JobOfferConflicts,
            JobOffersCreated = lifetime.JobOffersCreated,
            JobOffersStrengthened = lifetime.JobOffersStrengthened,
            SuccessfulBatches = lifetime.SuccessfulBatches,
            FailedBatches = lifetime.FailedBatches,
            PublishedRevisions = lifetime.PublishedRevisions,
            LastSuccessfulContributionUtc =
                lifetime.LastSuccessfulContributionUtc,
            LastFailedContributionUtc =
                lifetime.LastFailedContributionUtc,
        };
    }

    private void RaiseStatisticsChanged()
    {
        ForgeContributionStatisticsSnapshot sessionSnapshot;
        ForgeContributionStatisticsSnapshot lifetimeSnapshot;

        lock (this.stateLock)
        {
            sessionSnapshot = this.CreateSessionSnapshot();
            lifetimeSnapshot = this.CreateLifetimeSnapshot();
        }

        this.StatisticsChanged?.Invoke(
            this,
            new ForgeContributionStatisticsChangedEventArgs(
                sessionSnapshot,
                lifetimeSnapshot));
    }

    private static bool TryCreateRoster(
        ClientObservationSnapshot snapshot,
        out ObservedRoster roster,
        out string unavailableReason)
    {
        roster = null!;
        unavailableReason = "";

        if (snapshot.LifecycleState != ClientLifecycleState.InGame ||
            snapshot.LoadingOrTransitionFlag != 0 ||
            !snapshot.World.IsAvailable ||
            snapshot.World.Environment != ClientWorldEnvironment.Starbase ||
            !snapshot.StarbaseContext.IsAvailable ||
            string.IsNullOrWhiteSpace(snapshot.World.CurrentStarbaseName) ||
            string.IsNullOrWhiteSpace(snapshot.World.CurrentSectorName))
        {
            return false;
        }

        var identity = ClientLiveCharacterIdentityResolver.Resolve(snapshot);

        if (string.IsNullOrWhiteSpace(identity.Name))
        {
            return false;
        }

        var starbaseId = snapshot.World.CurrentStarbaseId != 0
            ? snapshot.World.CurrentStarbaseId
            : snapshot.StarbaseContext.StarbaseId;

        if (snapshot.World.CurrentStarbaseId != 0 &&
            snapshot.StarbaseContext.StarbaseId != 0 &&
            snapshot.World.CurrentStarbaseId != snapshot.StarbaseContext.StarbaseId)
        {
            return false;
        }

        var observedDefinitions = snapshot.StarbaseContext.Rooms
            .SelectMany(
                room => room.Npcs.Select(
                    npc => (Room: room, Npc: npc)))
            .Where(item =>
                item.Npc.DefinitionKey != 0 ||
                item.Npc.DefinitionSecondaryId != 0 ||
                !string.IsNullOrWhiteSpace(item.Npc.Name))
            .ToArray();

        if (observedDefinitions.Any(item =>
                string.IsNullOrWhiteSpace(item.Npc.Name)))
        {
            unavailableReason = string.Create(
                CultureInfo.InvariantCulture,
                $"Waiting for the complete named NPC roster at {snapshot.World.CurrentStarbaseName.Trim()}; unnamed NPC definitions are never contributed.");
            return false;
        }

        var npcs = observedDefinitions
            .Select(item => new ObservedNpc(
                starbaseId,
                snapshot.World.CurrentStarbaseName.Trim(),
                snapshot.World.CurrentSectorName.Trim(),
                snapshot.World.ActiveSectorNumber,
                item.Room.RoomClass,
                item.Room.DefinitionKey,
                item.Npc.Slot,
                item.Npc.DefinitionKey,
                item.Npc.DefinitionSecondaryId,
                item.Npc.Name.Trim(),
                item.Npc.VendorType,
                item.Npc.AmbientType))
            .GroupBy(CreateNpcId, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(npc => npc.DefinitionKey)
            .ThenBy(npc => npc.DefinitionSecondaryId)
            .ThenBy(npc => npc.Name, StringComparer.Ordinal)
            .ToArray();

        if (npcs.Length == 0)
        {
            return false;
        }

        var fingerprintSource = string.Join(
            "|",
            npcs.Select(CreateNpcFactFingerprint));
        var fingerprint = ForgeNavigationHash.ComputeSha256(
            Encoding.UTF8.GetBytes(
                string.Concat(
                    starbaseId.ToString(CultureInfo.InvariantCulture),
                    "|",
                    NormalizeKey(snapshot.World.CurrentStarbaseName),
                    "|",
                    fingerprintSource)));
        roster = new ObservedRoster(
            snapshot.ProcessId,
            starbaseId,
            snapshot.World.CurrentStarbaseName.Trim(),
            snapshot.World.CurrentSectorName.Trim(),
            snapshot.World.ActiveSectorNumber,
            identity.Name.Trim(),
            npcs,
            fingerprint);
        return true;
    }

    private static bool IsCompatible(
        ForgeNavigationNpcDocument existing,
        ObservedNpc observed)
    {
        return existing.StarbaseId == observed.StarbaseId &&
            existing.ActiveSectorNumber == observed.ActiveSectorNumber &&
            existing.RoomClass == observed.RoomClass &&
            existing.RoomDefinitionKey == observed.RoomDefinitionKey &&
            existing.RoomNpcSlot == observed.RoomNpcSlot &&
            existing.DefinitionKey == observed.DefinitionKey &&
            existing.DefinitionSecondaryId == observed.DefinitionSecondaryId &&
            NormalizedEquals(existing.StationName, observed.StationName) &&
            NormalizedEquals(existing.SectorName, observed.SectorName) &&
            !string.IsNullOrWhiteSpace(existing.Name) &&
            NormalizedEquals(existing.Name, observed.Name) &&
            existing.Role == (int)observed.VendorType &&
            existing.Classification == (int)observed.AmbientType;
    }

    private static string CreateStationId(
        uint starbaseId,
        string stationName,
        string sectorName)
    {
        return starbaseId != 0
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"starbase:{starbaseId}")
            : string.Concat(
                "station:",
                NormalizeKey(sectorName),
                ":",
                NormalizeKey(stationName));
    }

    private static string CreateNpcId(ObservedNpc npc)
    {
        return CreateNpcId(
            npc.StarbaseId,
            npc.StationName,
            npc.SectorName,
            npc.DefinitionKey,
            npc.DefinitionSecondaryId,
            npc.Name);
    }

    private static string CreateNpcId(
        uint starbaseId,
        string stationName,
        string sectorName,
        int definitionKey,
        int definitionSecondaryId,
        string name)
    {
        var stationIdentity = CreateStationId(
            starbaseId,
            stationName,
            sectorName);
        var npcIdentity = definitionKey != 0 ||
                          definitionSecondaryId != 0
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"definition:{definitionKey}:{definitionSecondaryId}")
            : string.Concat("name:", NormalizeKey(name));
        var hash = ForgeNavigationHash.ComputeSha256(
            Encoding.UTF8.GetBytes(
                string.Concat(stationIdentity, "|", npcIdentity)));
        return string.Concat("npc-", hash[..32]);
    }

    private static string CreateNpcFactFingerprint(ObservedNpc npc)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{CreateNpcId(npc)}:{npc.RoomClass}:{npc.RoomDefinitionKey}:{npc.RoomNpcSlot}:{npc.DefinitionKey}:{npc.DefinitionSecondaryId}:{NormalizeKey(npc.Name)}:{((int)npc.VendorType).ToString(CultureInfo.InvariantCulture)}:{((int)npc.AmbientType).ToString(CultureInfo.InvariantCulture)}");
    }

    private static bool NormalizedEquals(string left, string right)
    {
        return string.Equals(
            NormalizeKey(left),
            NormalizeKey(right),
            StringComparison.Ordinal);
    }

    private static string NormalizeKey(string value)
    {
        return string.Concat(value.Where(char.IsLetterOrDigit))
            .ToUpperInvariant();
    }

    private sealed class ProcessRosterState
    {
        public ProcessRosterState(
            string fingerprint,
            DateTimeOffset stableSince)
        {
            this.Fingerprint = fingerprint;
            this.StableSince = stableSince;
        }

        public string Fingerprint { get; }

        public DateTimeOffset StableSince { get; }

        public string? AttemptedFingerprint { get; set; }

        public DateTimeOffset NextAttemptAllowedAt { get; set; }

        public int ObservationCount { get; set; } = 1;
    }

    private sealed record ObservedRoster(
        int ProcessId,
        uint StarbaseId,
        string StationName,
        string SectorName,
        uint ActiveSectorNumber,
        string LivePilotName,
        IReadOnlyList<ObservedNpc> Npcs,
        string Fingerprint,
        DateTimeOffset FirstObservedAtUtc = default,
        DateTimeOffset LastObservedAtUtc = default,
        int ObservationCount = 1);

    private sealed record ObservedNpc(
        uint StarbaseId,
        string StationName,
        string SectorName,
        uint ActiveSectorNumber,
        int RoomClass,
        int RoomDefinitionKey,
        int RoomNpcSlot,
        int DefinitionKey,
        int DefinitionSecondaryId,
        string Name,
        ClientStarbaseNpcVendorType VendorType,
        ClientStarbaseNpcAmbientType AmbientType);

    private sealed class MutableStatistics
    {
        public long StationsObserved { get; set; }

        public long NpcsObserved { get; set; }

        public long NpcFactsSubmitted { get; set; }

        public long AlreadyCanonical { get; set; }

        public long EvidenceAccepted { get; set; }

        public long Conflicts { get; set; }

        public long StationFacilitiesObserved { get; set; }

        public long StationFacilityFactsSubmitted { get; set; }

        public long StationFacilitiesAlreadyCanonical { get; set; }

        public long StationFacilityEvidenceAccepted { get; set; }

        public long StationFacilityConflicts { get; set; }

        public long NavigationObjectsObserved { get; set; }

        public long NavigationObjectFactsSubmitted { get; set; }

        public long NavigationObjectsAlreadyCanonical { get; set; }

        public long NavigationObjectEvidenceAccepted { get; set; }

        public long NavigationObjectConflicts { get; set; }

        public long VendorsObserved { get; set; }

        public long VendorItemsObserved { get; set; }

        public long VendorItemFactsSubmitted { get; set; }

        public long VendorItemsAlreadyCanonical { get; set; }

        public long VendorItemEvidenceAccepted { get; set; }

        public long VendorItemConflicts { get; set; }

        public long VendorItemsRemoved { get; set; }

        public long MobSightingsObserved { get; set; }

        public long MobSightingFactsSubmitted { get; set; }

        public long MobSightingEvidenceAccepted { get; set; }

        public long MobVariantsCreated { get; set; }

        public long MobClustersCreated { get; set; }

        public long MobClustersUpdated { get; set; }

        public long LootCorpsesObserved { get; set; }

        public long MobLootRelationshipsObserved { get; set; }

        public long MobLootFactsSubmitted { get; set; }

        public long MobLootAlreadyCanonical { get; set; }

        public long MobLootEvidenceAccepted { get; set; }

        public long MobLootVariantsCreated { get; set; }

        public long MobLootRelationshipsCreated { get; set; }

        public long MobLootRelationshipsStrengthened { get; set; }

        public long HarvestableResourcesObserved { get; set; }

        public long HarvestableFactsSubmitted { get; set; }

        public long HarvestableAlreadyCanonical { get; set; }

        public long HarvestableEvidenceAccepted { get; set; }

        public long HarvestableVariantsCreated { get; set; }

        public long HarvestableFieldsCreated { get; set; }

        public long HarvestableFieldsUpdated { get; set; }

        public long HarvestableRelationshipsCreated { get; set; }

        public long HarvestableRelationshipsStrengthened { get; set; }

        public long ProductionRecipesObserved { get; set; }

        public long ProductionRecipeFactsSubmitted { get; set; }

        public long ProductionRecipesAlreadyCanonical { get; set; }

        public long ProductionRecipeEvidenceAccepted { get; set; }

        public long ProductionRecipeConflicts { get; set; }

        public long ProductionRecipesCreated { get; set; }

        public long MissionsObserved { get; set; }

        public long MissionFactsSubmitted { get; set; }

        public long MissionsAlreadyCanonical { get; set; }

        public long MissionEvidenceAccepted { get; set; }

        public long MissionConflicts { get; set; }

        public long MissionsCreated { get; set; }

        public long MissionsStrengthened { get; set; }

        public long JobOffersObserved { get; set; }

        public long JobOfferFactsSubmitted { get; set; }

        public long JobOffersAlreadyCanonical { get; set; }

        public long JobOfferEvidenceAccepted { get; set; }

        public long JobOfferConflicts { get; set; }

        public long JobOffersCreated { get; set; }

        public long JobOffersStrengthened { get; set; }

        public long SuccessfulBatches { get; set; }

        public long FailedBatches { get; set; }

        public long PublishedRevisions { get; set; }

        public DateTimeOffset? LastSuccessfulContributionUtc { get; set; }

        public DateTimeOffset? LastFailedContributionUtc { get; set; }

        public ForgeContributionStatisticsSnapshot ToSnapshot(
            bool enabled,
            bool identityRegistered,
            string status)
        {
            return new ForgeContributionStatisticsSnapshot
            {
                Enabled = enabled,
                IdentityRegistered = identityRegistered,
                Status = status,
                StationsObserved = this.StationsObserved,
                NpcsObserved = this.NpcsObserved,
                NpcFactsSubmitted = this.NpcFactsSubmitted,
                AlreadyCanonical = this.AlreadyCanonical,
                EvidenceAccepted = this.EvidenceAccepted,
                Conflicts = this.Conflicts,
                StationFacilitiesObserved = this.StationFacilitiesObserved,
                StationFacilityFactsSubmitted =
                    this.StationFacilityFactsSubmitted,
                StationFacilitiesAlreadyCanonical =
                    this.StationFacilitiesAlreadyCanonical,
                StationFacilityEvidenceAccepted =
                    this.StationFacilityEvidenceAccepted,
                StationFacilityConflicts = this.StationFacilityConflicts,
                NavigationObjectsObserved = this.NavigationObjectsObserved,
                NavigationObjectFactsSubmitted =
                    this.NavigationObjectFactsSubmitted,
                NavigationObjectsAlreadyCanonical =
                    this.NavigationObjectsAlreadyCanonical,
                NavigationObjectEvidenceAccepted =
                    this.NavigationObjectEvidenceAccepted,
                NavigationObjectConflicts = this.NavigationObjectConflicts,
                VendorsObserved = this.VendorsObserved,
                VendorItemsObserved = this.VendorItemsObserved,
                VendorItemFactsSubmitted = this.VendorItemFactsSubmitted,
                VendorItemsAlreadyCanonical = this.VendorItemsAlreadyCanonical,
                VendorItemEvidenceAccepted = this.VendorItemEvidenceAccepted,
                VendorItemConflicts = this.VendorItemConflicts,
                VendorItemsRemoved = this.VendorItemsRemoved,
                MobSightingsObserved = this.MobSightingsObserved,
                MobSightingFactsSubmitted = this.MobSightingFactsSubmitted,
                MobSightingEvidenceAccepted = this.MobSightingEvidenceAccepted,
                MobVariantsCreated = this.MobVariantsCreated,
                MobClustersCreated = this.MobClustersCreated,
                MobClustersUpdated = this.MobClustersUpdated,
                LootCorpsesObserved = this.LootCorpsesObserved,
                MobLootRelationshipsObserved =
                    this.MobLootRelationshipsObserved,
                MobLootFactsSubmitted = this.MobLootFactsSubmitted,
                MobLootAlreadyCanonical = this.MobLootAlreadyCanonical,
                MobLootEvidenceAccepted = this.MobLootEvidenceAccepted,
                MobLootVariantsCreated = this.MobLootVariantsCreated,
                MobLootRelationshipsCreated =
                    this.MobLootRelationshipsCreated,
                MobLootRelationshipsStrengthened =
                    this.MobLootRelationshipsStrengthened,
                HarvestableResourcesObserved =
                    this.HarvestableResourcesObserved,
                HarvestableFactsSubmitted = this.HarvestableFactsSubmitted,
                HarvestableAlreadyCanonical = this.HarvestableAlreadyCanonical,
                HarvestableEvidenceAccepted = this.HarvestableEvidenceAccepted,
                HarvestableVariantsCreated = this.HarvestableVariantsCreated,
                HarvestableFieldsCreated = this.HarvestableFieldsCreated,
                HarvestableFieldsUpdated = this.HarvestableFieldsUpdated,
                HarvestableRelationshipsCreated =
                    this.HarvestableRelationshipsCreated,
                HarvestableRelationshipsStrengthened =
                    this.HarvestableRelationshipsStrengthened,
                ProductionRecipesObserved = this.ProductionRecipesObserved,
                ProductionRecipeFactsSubmitted =
                    this.ProductionRecipeFactsSubmitted,
                ProductionRecipesAlreadyCanonical =
                    this.ProductionRecipesAlreadyCanonical,
                ProductionRecipeEvidenceAccepted =
                    this.ProductionRecipeEvidenceAccepted,
                ProductionRecipeConflicts = this.ProductionRecipeConflicts,
                ProductionRecipesCreated = this.ProductionRecipesCreated,
                MissionsObserved = this.MissionsObserved,
                MissionFactsSubmitted = this.MissionFactsSubmitted,
                MissionsAlreadyCanonical = this.MissionsAlreadyCanonical,
                MissionEvidenceAccepted = this.MissionEvidenceAccepted,
                MissionConflicts = this.MissionConflicts,
                MissionsCreated = this.MissionsCreated,
                MissionsStrengthened = this.MissionsStrengthened,
                JobOffersObserved = this.JobOffersObserved,
                JobOfferFactsSubmitted = this.JobOfferFactsSubmitted,
                JobOffersAlreadyCanonical = this.JobOffersAlreadyCanonical,
                JobOfferEvidenceAccepted = this.JobOfferEvidenceAccepted,
                JobOfferConflicts = this.JobOfferConflicts,
                JobOffersCreated = this.JobOffersCreated,
                JobOffersStrengthened = this.JobOffersStrengthened,
                SuccessfulBatches = this.SuccessfulBatches,
                FailedBatches = this.FailedBatches,
                PublishedRevisions = this.PublishedRevisions,
                LastSuccessfulContributionUtc =
                    this.LastSuccessfulContributionUtc,
                LastFailedContributionUtc =
                    this.LastFailedContributionUtc,
            };
        }
    }
}
