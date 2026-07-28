namespace Net7ClientManager.Contributions;

using System.Globalization;
using System.Text;
using Net7ClientManager.Models;
using Net7ClientManager.Navigation;
using Net7ClientManager.Observations;
using Net7ClientManager.Observations.Models;

internal sealed partial class ForgeContributionCoordinator
{
    private const float NavigationPositionTolerance = 5.0f;

    private readonly Dictionary<int, ProcessRosterState> navigationProcessStates = [];
    private readonly HashSet<string> inFlightNavigationBatchKeys =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> completedNavigationBatchKeys =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> sessionObservedNavigationObjectKeys =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> lifetimeObservedNavigationObjectKeys;

    private void ObserveNavigationObjects(ClientObservationSnapshot snapshot)
    {
        if (!this.TryCreateNavigationBatch(
                snapshot,
                out var batch,
                out var unavailableReason))
        {
            lock (this.stateLock)
            {
                this.navigationProcessStates.Remove(snapshot.ProcessId);

                if (!string.IsNullOrWhiteSpace(unavailableReason))
                {
                    this.status = unavailableReason;
                }
            }

            return;
        }

        string? submissionKey = null;
        IReadOnlyList<ObservedNavigationObject> uncovered = [];
        HashSet<string> knownObjectKeys = new(StringComparer.Ordinal);
        var completedWithoutSubmission = false;
        CancellationToken participationToken = default;

        lock (this.stateLock)
        {
            if (!this.navigationProcessStates.TryGetValue(
                    snapshot.ProcessId,
                    out var state) ||
                !string.Equals(
                    state.Fingerprint,
                    batch.Fingerprint,
                    StringComparison.Ordinal))
            {
                this.navigationProcessStates[snapshot.ProcessId] =
                    new ProcessRosterState(
                        batch.Fingerprint,
                        snapshot.ObservedAt);
                this.status = string.Create(
                    CultureInfo.InvariantCulture,
                    $"Waiting for the navigation-object inventory in {batch.SectorName} to settle.");
                return;
            }

            state.ObservationCount++;
            batch = batch with
            {
                FirstObservedAtUtc = state.StableSince,
                LastObservedAtUtc = snapshot.ObservedAt,
                ObservationCount = state.ObservationCount,
            };

            if (snapshot.ObservedAt - state.StableSince < RosterSettleTime ||
                snapshot.ObservedAt < state.NextAttemptAllowedAt ||
                string.Equals(
                    state.AttemptedFingerprint,
                    batch.Fingerprint,
                    StringComparison.Ordinal))
            {
                return;
            }

            state.AttemptedFingerprint = batch.Fingerprint;
            state.NextAttemptAllowedAt = DateTimeOffset.MaxValue;
            (uncovered, knownObjectKeys) =
                this.FindUncoveredNavigationObjects(batch);
            this.RecordObservedNavigationObjects(batch, knownObjectKeys);
            this.status = uncovered.Count == 0
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"{batch.Targets.Count} map locations in {batch.SectorName} are already known by Forge.")
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"Preparing {uncovered.Count} new or conflicting navigation-object facts from {batch.SectorName}.");

            if (uncovered.Count == 0)
            {
                completedWithoutSubmission = true;
            }
            else
            {
                submissionKey = this.CreateNavigationSubmissionKey(batch);

                if (this.completedNavigationBatchKeys.Contains(submissionKey) ||
                    !this.inFlightNavigationBatchKeys.Add(submissionKey))
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
        var task = this.SubmitNavigationObjectsAsync(
            batch,
            uncovered,
            submissionKey!,
            submissionCancellation);
        this.Track(task);
    }

    private async Task SubmitNavigationObjectsAsync(
        ObservedNavigationBatch batch,
        IReadOnlyList<ObservedNavigationObject> uncovered,
        string submissionKey,
        CancellationTokenSource submissionCancellation)
    {
        var cancellationToken = submissionCancellation.Token;

        try
        {
            var identity = await this.identityService.EnsureAsync(
                    batch.LivePilotName,
                    cancellationToken)
                .ConfigureAwait(false);
            var unsignedRequest = new ForgeNavigationObjectsContributionRequest
            {
                ContributorId = identity.ContributorId,
                RequestId = Guid.NewGuid().ToString("D", CultureInfo.InvariantCulture),
                SubmittedAtUtc = DateTimeOffset.UtcNow,
                FirstObservedAtUtc = batch.FirstObservedAtUtc,
                LastObservedAtUtc = batch.LastObservedAtUtc,
                ObservationCount = batch.ObservationCount,
                ClientVersion = clientVersion,
                DatasetRevision = this.dataSet.AuthorityRevision,
                Attribution = this.settings.Attribution ==
                    ForgeContributionAttribution.LivePilotName
                        ? "live-pilot-name"
                        : "publicly-anonymous",
                LivePilotName = batch.LivePilotName,
                SectorId = batch.SectorId,
                SectorKey = batch.SectorKey,
                SectorName = batch.SectorName,
                SystemName = batch.SystemName,
                ActiveSectorNumber = batch.ActiveSectorNumber,
                Targets =
                [
                    .. uncovered.Select(target =>
                        new ForgeNavigationObjectContributionItem
                        {
                            ObjectId = target.ObjectId,
                            Name = target.Name,
                            MapDisplayName = target.MapDisplayName,
                            Signature = target.Signature,
                            RawObjectType = target.RawObjectType,
                            NavType = target.NavType,
                            IsHuge = target.IsHuge,
                            SelectionContext = target.SelectionContext ==
                                GalaxyNavigationTargetSelectionContext.Navigation
                                    ? "Navigation"
                                    : "Object",
                            HasPosition = target.HasPosition,
                            X = target.X,
                            Y = target.Y,
                            Z = target.Z,
                        }),
                ],
            };
            var request = unsignedRequest with
            {
                Signature = identity.Sign(unsignedRequest),
            };
            var response = await this.client.SubmitNavigationObjectsAsync(
                    request,
                    cancellationToken)
                .ConfigureAwait(false);

            lock (this.stateLock)
            {
                this.completedNavigationBatchKeys.Add(submissionKey);
                this.session.NavigationObjectFactsSubmitted += response.Received;
                this.session.NavigationObjectsAlreadyCanonical +=
                    response.AlreadyCanonical;
                this.session.NavigationObjectEvidenceAccepted +=
                    response.EvidenceAccepted;
                this.session.NavigationObjectConflicts += response.Conflicts;
                this.session.SuccessfulBatches++;
                this.session.LastSuccessfulContributionUtc =
                    DateTimeOffset.UtcNow;

                var lifetime = this.settings.Lifetime;
                lifetime.NavigationObjectFactsSubmitted += response.Received;
                lifetime.NavigationObjectsAlreadyCanonical +=
                    response.AlreadyCanonical;
                lifetime.NavigationObjectEvidenceAccepted +=
                    response.EvidenceAccepted;
                lifetime.NavigationObjectConflicts += response.Conflicts;
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
                        $"Forge accepted the navigation-object batch and published revision {revision}. Activate the staged Forge dataset update when ready.")
                    : string.Create(
                        CultureInfo.InvariantCulture,
                        $"Forge accepted the navigation-object batch: {response.EvidenceAccepted} evidence facts, {response.AlreadyCanonical} already canonical, {response.Conflicts} conflicts.");
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
                this.settings.Lifetime.LastFailedContributionUtc =
                    DateTimeOffset.UtcNow;
                this.status = string.Concat(
                    "Forge navigation-object contribution failed: ",
                    exception.Message,
                    ". The settled sector inventory can be retried later this session.");

                if (this.navigationProcessStates.TryGetValue(
                        batch.ProcessId,
                        out var state) &&
                    string.Equals(
                        state.Fingerprint,
                        batch.Fingerprint,
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
                this.inFlightNavigationBatchKeys.Remove(submissionKey);
            }

            submissionCancellation.Dispose();
        }
    }

    private bool TryCreateNavigationBatch(
        ClientObservationSnapshot snapshot,
        out ObservedNavigationBatch batch,
        out string unavailableReason)
    {
        batch = null!;
        unavailableReason = "";

        if (snapshot.LifecycleState != ClientLifecycleState.InGame ||
            snapshot.LoadingOrTransitionFlag != 0 ||
            !snapshot.World.IsAvailable ||
            snapshot.World.Environment != ClientWorldEnvironment.Space ||
            !snapshot.Navigation.IsAvailable ||
            snapshot.World.ActiveSectorNumber == 0 ||
            snapshot.Navigation.ActiveSectorNumber !=
                snapshot.World.ActiveSectorNumber)
        {
            return false;
        }

        var identity = ClientLiveCharacterIdentityResolver.Resolve(snapshot);

        if (string.IsNullOrWhiteSpace(identity.Name))
        {
            return false;
        }

        var sector = this.dataSet.Document.Sectors.SingleOrDefault(candidate =>
            candidate.ActiveSectorNumber == snapshot.World.ActiveSectorNumber);

        if (sector == null)
        {
            unavailableReason = string.Create(
                CultureInfo.InvariantCulture,
                $"Waiting for active sector {snapshot.World.ActiveSectorNumber} to exist in the Forge dataset before contributing navigation objects.");
            return false;
        }

        var supportedNavigationTargets = snapshot.Navigation.Targets
            .Where(target =>
                target.IsAvailable &&
                IsSupportedNavigationObjectType(target.RawObjectType))
            .ToArray();

        var incompleteNavigationTarget =
            supportedNavigationTargets.FirstOrDefault(target =>
                target.ObjectId == 0 ||
                string.IsNullOrWhiteSpace(target.Name) ||
                !float.IsFinite(target.Signature) ||
                target.NavType < 0 ||
                !target.Spatial.IsAvailable ||
                !float.IsFinite(target.Spatial.Position.X) ||
                !float.IsFinite(target.Spatial.Position.Y) ||
                !float.IsFinite(target.Spatial.Position.Z));

        if (incompleteNavigationTarget != null)
        {
            unavailableReason = string.Create(
                CultureInfo.InvariantCulture,
                $"Waiting for the complete named and positioned navigation-object inventory in {sector.Name}; incomplete targets are never contributed.");
            return false;
        }

        var navigationObjectIds = supportedNavigationTargets
            .Select(target => target.ObjectId)
            .ToHashSet();
        var navigationTargets = supportedNavigationTargets
            .Select(target => new ObservedNavigationObject(
                target.ObjectId,
                target.Name.Trim(),
                target.MapDisplayName.Trim(),
                target.Signature,
                target.RawObjectType,
                target.NavType,
                target.IsHuge,
                GalaxyNavigationTargetSelectionContext.Navigation,
                true,
                target.Spatial.Position.X,
                target.Spatial.Position.Y,
                target.Spatial.Position.Z));
        var staticObjectTargets = snapshot.NearbyTargets.IsAvailable &&
            snapshot.NearbyTargets.ActiveSectorNumber ==
                snapshot.World.ActiveSectorNumber
            ? snapshot.NearbyTargets.Targets
                .Where(target =>
                    target.IsAvailable &&
                    target.ObjectId != 0 &&
                    !navigationObjectIds.Contains(target.ObjectId) &&
                    IsSupportedNavigationObjectType(target.RawObjectType) &&
                    !string.IsNullOrWhiteSpace(target.Name) &&
                    target.Spatial.IsAvailable &&
                    target.Spatial.ProviderKind ==
                        ClientSpatialProviderKind.FixedPosition &&
                    target.Spatial.StateKind ==
                        ClientSpatialStateKind.Static &&
                    float.IsFinite(target.Spatial.Position.X) &&
                    float.IsFinite(target.Spatial.Position.Y) &&
                    float.IsFinite(target.Spatial.Position.Z))
                .Select(target => new ObservedNavigationObject(
                    target.ObjectId,
                    target.Name.Trim(),
                    string.IsNullOrWhiteSpace(target.DisplayName)
                        ? target.Name.Trim()
                        : target.DisplayName.Trim(),
                    null,
                    target.RawObjectType,
                    null,
                    null,
                    GalaxyNavigationTargetSelectionContext.Object,
                    true,
                    target.Spatial.Position.X,
                    target.Spatial.Position.Y,
                    target.Spatial.Position.Z))
            : [];
        var targets = navigationTargets
            .Concat(staticObjectTargets)
            .GroupBy(target => target.ObjectId)
            .Select(group => group
                .OrderBy(target => target.SelectionContext)
                .First())
            .OrderBy(target => target.ObjectId)
            .ToArray();

        if (targets.Length == 0)
        {
            return false;
        }

        if (targets.Select(target => target.ObjectId).Distinct().Count() !=
            targets.Length)
        {
            unavailableReason = string.Create(
                CultureInfo.InvariantCulture,
                $"Waiting for a navigation-object inventory without duplicate native identities in {sector.Name}.");
            return false;
        }

        var fingerprintSource = string.Join(
            "|",
            targets.Select(CreateNavigationObjectFingerprint));
        var fingerprint = ForgeNavigationHash.ComputeSha256(
            Encoding.UTF8.GetBytes(
                string.Concat(
                    sector.Id,
                    "|",
                    fingerprintSource)));
        batch = new ObservedNavigationBatch(
            snapshot.ProcessId,
            sector.Id,
            sector.Key,
            sector.Name,
            sector.SystemName,
            sector.ActiveSectorNumber,
            identity.Name.Trim(),
            targets,
            fingerprint);
        return true;
    }

    private (IReadOnlyList<ObservedNavigationObject> Unknown,
        HashSet<string> KnownObjectKeys) FindUncoveredNavigationObjects(
        ObservedNavigationBatch batch)
    {
        var sector = this.dataSet.Document.Sectors.Single(candidate =>
            string.Equals(candidate.Id, batch.SectorId, StringComparison.Ordinal));
        List<ObservedNavigationObject> unknown = [];
        HashSet<string> knownObjectKeys = new(StringComparer.Ordinal);

        foreach (var observed in batch.Targets)
        {
            if (sector.Targets.Any(existing =>
                    IsNavigationObjectCompatible(existing, observed)))
            {
                knownObjectKeys.Add(
                    CreateNavigationObjectObservationKey(
                        batch.SectorId,
                        observed.ObjectId));
            }
            else
            {
                unknown.Add(observed);
            }
        }

        return (unknown, knownObjectKeys);
    }

    private void RecordObservedNavigationObjects(
        ObservedNavigationBatch batch,
        IReadOnlySet<string> knownObjectKeys)
    {
        foreach (var target in batch.Targets)
        {
            var key = CreateNavigationObjectObservationKey(
                batch.SectorId,
                target.ObjectId);

            if (this.sessionObservedNavigationObjectKeys.Add(key))
            {
                this.session.NavigationObjectsObserved =
                    this.sessionObservedNavigationObjectKeys.Count;

                if (knownObjectKeys.Contains(key))
                {
                    this.session.NavigationObjectsAlreadyCanonical++;
                }
            }

            if (this.lifetimeObservedNavigationObjectKeys.Add(key))
            {
                this.settings.Lifetime.ObservedNavigationObjectKeys.Add(key);
                this.settings.Lifetime.NavigationObjectsObserved =
                    this.lifetimeObservedNavigationObjectKeys.Count;

                if (knownObjectKeys.Contains(key))
                {
                    this.settings.Lifetime.NavigationObjectsAlreadyCanonical++;
                }
            }
        }
    }

    private string CreateNavigationSubmissionKey(
        ObservedNavigationBatch batch)
    {
        var attributionIdentity = this.settings.Attribution ==
            ForgeContributionAttribution.LivePilotName
                ? NormalizeKey(batch.LivePilotName)
                : "anonymous";
        return string.Concat(
            this.dataSet.AuthorityRevision.ToString(CultureInfo.InvariantCulture),
            "|",
            attributionIdentity,
            "|navigation|",
            batch.Fingerprint);
    }

    private static bool IsSupportedNavigationObjectType(byte rawObjectType)
    {
        return rawObjectType is 3 or 11 or 12 or 37;
    }

    private static bool IsNavigationObjectCompatible(
        ForgeNavigationTargetDocument existing,
        ObservedNavigationObject observed)
    {
        var existingContext = ParseSelectionContext(
            existing.SelectionContext);

        return existing.RawObjectType == observed.RawObjectType &&
            (!existing.NavType.HasValue ||
             !observed.NavType.HasValue ||
             existing.NavType.Value == observed.NavType.Value) &&
            (!existing.IsHuge.HasValue ||
             !observed.IsHuge.HasValue ||
             existing.IsHuge.Value == observed.IsHuge.Value) &&
            !(existingContext == GalaxyNavigationTargetSelectionContext.Object &&
              observed.SelectionContext ==
                GalaxyNavigationTargetSelectionContext.Navigation) &&
            existing.HasPosition == observed.HasPosition &&
            NavigationNamesOverlap(existing, observed) &&
            NearlyEqual(existing.X, observed.X, NavigationPositionTolerance) &&
            NearlyEqual(existing.Y, observed.Y, NavigationPositionTolerance) &&
            NearlyEqual(existing.Z, observed.Z, NavigationPositionTolerance);
    }

    private static GalaxyNavigationTargetSelectionContext
        ParseSelectionContext(string? value)
    {
        return string.Equals(
            value,
            "Object",
            StringComparison.Ordinal)
            ? GalaxyNavigationTargetSelectionContext.Object
            : GalaxyNavigationTargetSelectionContext.Navigation;
    }

    private static bool NavigationNamesOverlap(
        ForgeNavigationTargetDocument existing,
        ObservedNavigationObject observed)
    {
        var observedNames = new HashSet<string>(StringComparer.Ordinal)
        {
            NormalizeKey(observed.Name),
        };

        if (!string.IsNullOrWhiteSpace(observed.MapDisplayName))
        {
            observedNames.Add(NormalizeKey(observed.MapDisplayName));
        }

        return observedNames.Contains(NormalizeKey(existing.Name)) ||
            (!string.IsNullOrWhiteSpace(existing.MapDisplayName) &&
             observedNames.Contains(NormalizeKey(existing.MapDisplayName)));
    }

    private static bool NearlyEqual(float left, float right, float tolerance)
    {
        return Math.Abs(left - right) <= tolerance;
    }

    private static string CreateNavigationObjectObservationKey(
        string sectorId,
        uint objectId)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{sectorId}|{objectId}");
    }

    private static string CreateNavigationObjectFingerprint(
        ObservedNavigationObject target)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{target.ObjectId}:{NormalizeKey(target.Name)}:{NormalizeKey(target.MapDisplayName)}:{target.RawObjectType}:{target.SelectionContext}:{target.NavType?.ToString(CultureInfo.InvariantCulture) ?? "-"}:{(target.IsHuge.HasValue ? target.IsHuge.Value ? "1" : "0" : "-")}:{target.X:R}:{target.Y:R}:{target.Z:R}");
    }

    private sealed record ObservedNavigationBatch(
        int ProcessId,
        string SectorId,
        string SectorKey,
        string SectorName,
        string SystemName,
        uint ActiveSectorNumber,
        string LivePilotName,
        IReadOnlyList<ObservedNavigationObject> Targets,
        string Fingerprint,
        DateTimeOffset FirstObservedAtUtc = default,
        DateTimeOffset LastObservedAtUtc = default,
        int ObservationCount = 1);

    private sealed record ObservedNavigationObject(
        uint ObjectId,
        string Name,
        string MapDisplayName,
        float? Signature,
        byte RawObjectType,
        int? NavType,
        bool? IsHuge,
        GalaxyNavigationTargetSelectionContext SelectionContext,
        bool HasPosition,
        float X,
        float Y,
        float Z);

#if DEBUG
    private static void ValidateNavigationSignatureRegression()
    {
        var canonical = new ForgeNavigationTargetDocument
        {
            Id = "signature-regression",
            Ordinal = 0,
            Name = "Prasad Beacon",
            MapDisplayName = "Prasad Beacon",
            Signature = 30000.0f,
            RawObjectType = 37,
            NavType = 1,
            HasPosition = true,
            X = 1.0f,
            Y = 2.0f,
            Z = 3.0f,
        };
        var observed = new ObservedNavigationObject(
            123,
            "Prasad Beacon",
            "Prasad Beacon",
            35000.0f,
            37,
            1,
            false,
            GalaxyNavigationTargetSelectionContext.Navigation,
            true,
            1.0f,
            2.0f,
            3.0f);

        var secondObservation = observed with
        {
            Signature = 30000.0f,
        };

        if (!IsNavigationObjectCompatible(canonical, observed) ||
            !string.Equals(
                CreateNavigationObjectFingerprint(observed),
                CreateNavigationObjectFingerprint(secondObservation),
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Perceived navigation Signature must not affect matching or canonical equivalence.");
        }

        var staticCanonical = canonical with
        {
            Signature = null,
            NavType = null,
            IsHuge = null,
            SelectionContext = "Object",
        };
        var staticObserved = observed with
        {
            Signature = null,
            NavType = null,
            IsHuge = null,
            SelectionContext =
                GalaxyNavigationTargetSelectionContext.Object,
        };

        if (!IsNavigationObjectCompatible(canonical, staticObserved) ||
            IsNavigationObjectCompatible(staticCanonical, observed))
        {
            throw new InvalidOperationException(
                "Static world targets must remain compatible with stronger navigation records while still allowing later promotion to NavigationData.");
        }
    }
#endif
}
