namespace Net7ClientManager.Contributions;

using System.Globalization;
using System.Text;
using Net7ClientManager.Models;
using Net7ClientManager.Navigation;
using Net7ClientManager.Observations;
using Net7ClientManager.Observations.Models;

internal sealed partial class ForgeContributionCoordinator
{
    private readonly Dictionary<int, ProcessRosterState> facilityProcessStates = [];
    private readonly HashSet<string> inFlightFacilityRosterKeys =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> completedFacilityRosterKeys =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> sessionObservedFacilityKeys =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> lifetimeObservedFacilityKeys;

    public void Observe(ClientObservationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        this.ObserveForgeIdentity(snapshot);

        if (!this.settings.Enabled)
        {
            lock (this.stateLock)
            {
                this.processStates.Remove(snapshot.ProcessId);
                this.latestSnapshotsByProcessId.Remove(snapshot.ProcessId);
                this.gravityWellInsideStateByProcessId.Remove(snapshot.ProcessId);
                this.navigationProcessStates.Remove(snapshot.ProcessId);
                this.facilityProcessStates.Remove(snapshot.ProcessId);
                this.vendorInventoryProcessStates.Remove(snapshot.ProcessId);
                this.RemoveMobProcessStates(snapshot.ProcessId);
                this.mobLootProcessStates.Remove(snapshot.ProcessId);
                this.RemoveHarvestableProcessStates(snapshot.ProcessId);
                this.productionRecipeProcessStates.Remove(snapshot.ProcessId);
                this.missionProcessStates.Remove(snapshot.ProcessId);
                this.status = "Contribution is disabled.";
            }

            return;
        }

        lock (this.stateLock)
        {
            this.latestSnapshotsByProcessId[snapshot.ProcessId] = snapshot;
        }

        if (this.settings.Categories.NpcPresence)
        {
            this.ObserveNpcPresence(snapshot);
        }
        else
        {
            lock (this.stateLock)
            {
                this.processStates.Remove(snapshot.ProcessId);
            }
        }

        if (this.settings.Categories.NavigationObjects)
        {
            this.ObserveNavigationObjects(snapshot);
        }
        else
        {
            lock (this.stateLock)
            {
                this.navigationProcessStates.Remove(snapshot.ProcessId);
            }
        }

        if (this.settings.Categories.StationServices)
        {
            this.ObserveStationServices(snapshot);
        }
        else
        {
            lock (this.stateLock)
            {
                this.facilityProcessStates.Remove(snapshot.ProcessId);
            }
        }

        if (this.settings.Categories.VendorInventories)
        {
            this.ObserveVendorInventories(snapshot);
        }
        else
        {
            lock (this.stateLock)
            {
                this.vendorInventoryProcessStates.Remove(snapshot.ProcessId);
            }
        }

        if (this.settings.Categories.MobObservations)
        {
            this.ObserveMobSightings(snapshot);
        }
        else
        {
            lock (this.stateLock)
            {
                this.RemoveMobProcessStates(snapshot.ProcessId);
            }
        }

        if (this.settings.Categories.LootObservations)
        {
            this.ObserveMobLoot(snapshot);
        }
        else
        {
            lock (this.stateLock)
            {
                this.mobLootProcessStates.Remove(snapshot.ProcessId);
            }
        }

        if (this.settings.Categories.ResourceObservations)
        {
            this.ObserveHarvestableResources(snapshot);
        }
        else
        {
            lock (this.stateLock)
            {
                this.RemoveHarvestableProcessStates(snapshot.ProcessId);
            }
        }

        if (this.settings.Categories.ProductionRecipes)
        {
            this.ObserveProductionRecipe(snapshot);
        }
        else
        {
            lock (this.stateLock)
            {
                this.productionRecipeProcessStates.Remove(snapshot.ProcessId);
            }
        }


        if (this.settings.Categories.JobOffers)
        {
            this.ObserveJobOffers(snapshot);
        }

        if (this.settings.Categories.Missions)
        {
            this.ObserveMissions(snapshot);
        }
        else
        {
            lock (this.stateLock)
            {
                this.missionProcessStates.Remove(snapshot.ProcessId);
            }
        }
    }

    private void ObserveForgeIdentity(ClientObservationSnapshot snapshot)
    {
        if (!this.identityService.HasIdentity ||
            snapshot.LifecycleState != ClientLifecycleState.InGame)
        {
            return;
        }

        var pilotName = ClientLiveCharacterIdentityResolver.Resolve(snapshot).Name;
        if (string.IsNullOrWhiteSpace(pilotName))
        {
            return;
        }

        pilotName = pilotName.Trim();
        lock (this.stateLock)
        {
            if (this.completedPilotClaims.Contains(pilotName) ||
                (this.pilotClaimRetryAt.TryGetValue(
                     pilotName,
                     out var retryAt) &&
                 retryAt > DateTimeOffset.UtcNow) ||
                !this.inFlightPilotClaims.Add(pilotName))
            {
                return;
            }
        }

        var task = this.ClaimObservedPilotAsync(
            pilotName,
            this.cancellation.Token);
        this.Track(task);
    }

    private async Task ClaimObservedPilotAsync(
        string pilotName,
        CancellationToken cancellationToken)
    {
        try
        {
            await this.identityService.ClaimPilotIfRegisteredAsync(
                    pilotName,
                    cancellationToken)
                .ConfigureAwait(false);

            lock (this.stateLock)
            {
                this.completedPilotClaims.Add(pilotName);
                this.pilotClaimRetryAt.Remove(pilotName);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected during shutdown.
        }
        catch (Exception exception)
        {
            lock (this.stateLock)
            {
                this.pilotClaimRetryAt[pilotName] = DateTimeOffset.UtcNow.Add(
                    exception is ForgeIdentityApiException apiException &&
                    string.Equals(
                        apiException.Code,
                        "pilot_claim_conflict",
                        StringComparison.OrdinalIgnoreCase)
                        ? TimeSpan.FromHours(1)
                        : TimeSpan.FromMinutes(5));
                this.status = string.Concat(
                    "Could not connect ",
                    pilotName,
                    " to the Forge identity: ",
                    exception.Message);
            }
        }
        finally
        {
            lock (this.stateLock)
            {
                this.inFlightPilotClaims.Remove(pilotName);
            }
        }
    }

    private void ObserveStationServices(ClientObservationSnapshot snapshot)
    {
        if (!TryCreateFacilityRoster(
                snapshot,
                out var roster,
                out var unavailableReason))
        {
            lock (this.stateLock)
            {
                this.facilityProcessStates.Remove(snapshot.ProcessId);

                if (!string.IsNullOrWhiteSpace(unavailableReason))
                {
                    this.status = unavailableReason;
                }
            }

            return;
        }

        string? submissionKey = null;
        HashSet<string> knownFacilityIds = new(StringComparer.Ordinal);
        var completedWithoutSubmission = false;
        CancellationToken participationToken = default;

        lock (this.stateLock)
        {
            if (!this.facilityProcessStates.TryGetValue(
                    snapshot.ProcessId,
                    out var state) ||
                !string.Equals(
                    state.Fingerprint,
                    roster.Fingerprint,
                    StringComparison.Ordinal))
            {
                this.facilityProcessStates[snapshot.ProcessId] =
                    new ProcessRosterState(
                        roster.Fingerprint,
                        snapshot.ObservedAt);
                this.status = string.Create(
                    CultureInfo.InvariantCulture,
                    $"Waiting for the station service roster at {roster.StationName} to settle.");
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
            var unknownFacilities = this.FindUncoveredStationFacilities(
                roster.Facilities,
                out knownFacilityIds);
            this.RecordObservedFacilityRoster(roster, knownFacilityIds);
            this.status = unknownFacilities.Count == 0
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"{roster.Facilities.Count} station facilities at {roster.StationName} are already known by Forge.")
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"Preparing the complete {roster.Facilities.Count}-facility roster from {roster.StationName}; {unknownFacilities.Count} instances are new or conflicting.");

            if (unknownFacilities.Count == 0)
            {
                completedWithoutSubmission = true;
            }
            else
            {
                submissionKey = this.CreateFacilitySubmissionKey(roster);

                if (this.completedFacilityRosterKeys.Contains(submissionKey) ||
                    !this.inFlightFacilityRosterKeys.Add(submissionKey))
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
        var task = this.SubmitStationServicesAsync(
            roster,
            submissionKey!,
            submissionCancellation);
        this.Track(task);
    }

    private async Task SubmitStationServicesAsync(
        ObservedFacilityRoster roster,
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
            var unsignedRequest = new ForgeStationServicesContributionRequest
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
                StarbaseDefinitionId = roster.StarbaseDefinitionId,
                StationName = roster.StationName,
                SectorName = roster.SectorName,
                ActiveSectorNumber = roster.ActiveSectorNumber,
                RoomCount = roster.RoomCount,
                FacilityDefinitionCount = roster.Facilities.Count,
                Facilities =
                [
                    .. roster.Facilities.Select(facility =>
                        new ForgeStationServiceContributionItem
                        {
                            RoomClass = facility.RoomClass,
                            RoomDefinitionKey = facility.RoomDefinitionKey,
                            RoomFacilitySlot = facility.RoomFacilitySlot,
                            DefinitionSlot = facility.DefinitionSlot,
                            FacilityType = facility.FacilityType,
                            Name = facility.Name,
                            ReservedValue = facility.ReservedValue,
                        }),
                ],
            };
            var request = unsignedRequest with
            {
                Signature = identity.Sign(unsignedRequest),
            };
            var response = await this.client.SubmitStationServicesAsync(
                    request,
                    cancellationToken)
                .ConfigureAwait(false);

            lock (this.stateLock)
            {
                this.completedFacilityRosterKeys.Add(submissionKey);
                this.session.StationFacilityFactsSubmitted += response.Received;
                this.session.StationFacilityEvidenceAccepted +=
                    response.EvidenceAccepted;
                this.session.StationFacilityConflicts += response.Conflicts;
                this.session.SuccessfulBatches++;
                this.session.LastSuccessfulContributionUtc = DateTimeOffset.UtcNow;

                var lifetime = this.settings.Lifetime;
                lifetime.StationFacilityFactsSubmitted += response.Received;
                lifetime.StationFacilityEvidenceAccepted +=
                    response.EvidenceAccepted;
                lifetime.StationFacilityConflicts += response.Conflicts;
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
                        $"Forge accepted the station service roster and published revision {revision}. Activate the staged Forge dataset update when ready.")
                    : string.Create(
                        CultureInfo.InvariantCulture,
                        $"Forge accepted the station service roster: {response.EvidenceAccepted} evidence facts, {response.AlreadyCanonical} already canonical, {response.Conflicts} conflicts.");
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
                    "Forge station service contribution failed: ",
                    exception.Message,
                    ". The settled roster can be retried later this session.");

                if (this.facilityProcessStates.TryGetValue(
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
                this.inFlightFacilityRosterKeys.Remove(submissionKey);
            }

            submissionCancellation.Dispose();
        }
    }

    private IReadOnlyList<ObservedStationFacility> FindUncoveredStationFacilities(
        IReadOnlyList<ObservedStationFacility> observed,
        out HashSet<string> knownIds)
    {
        var knownById = this.dataSet.Document.StationFacilities.ToDictionary(
            facility => facility.Id,
            StringComparer.Ordinal);
        List<ObservedStationFacility> unknown = [];
        knownIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var facility in observed)
        {
            var id = CreateStationFacilityId(facility);

            if (knownById.TryGetValue(id, out var existing) &&
                IsStationFacilityCompatible(existing, facility))
            {
                knownIds.Add(id);
            }
            else
            {
                unknown.Add(facility);
            }
        }

        return unknown;
    }

    private void RecordObservedFacilityRoster(
        ObservedFacilityRoster roster,
        IReadOnlySet<string> knownFacilityIds)
    {
        var stationKey = CreateStationId(
            roster.StarbaseId,
            roster.StationName,
            roster.SectorName);

        if (this.sessionObservedStationKeys.Add(stationKey))
        {
            this.session.StationsObserved = this.sessionObservedStationKeys.Count;
        }

        if (this.lifetimeObservedStationKeys.Add(stationKey))
        {
            this.settings.Lifetime.ObservedStationKeys.Add(stationKey);
            this.settings.Lifetime.StationsObserved =
                this.lifetimeObservedStationKeys.Count;
        }

        foreach (var facility in roster.Facilities)
        {
            var facilityKey = CreateStationFacilityId(facility);

            if (this.sessionObservedFacilityKeys.Add(facilityKey))
            {
                this.session.StationFacilitiesObserved =
                    this.sessionObservedFacilityKeys.Count;

                if (knownFacilityIds.Contains(facilityKey))
                {
                    this.session.StationFacilitiesAlreadyCanonical++;
                }
            }

            if (this.lifetimeObservedFacilityKeys.Add(facilityKey))
            {
                this.settings.Lifetime.ObservedStationFacilityKeys.Add(facilityKey);
                this.settings.Lifetime.StationFacilitiesObserved =
                    this.lifetimeObservedFacilityKeys.Count;

                if (knownFacilityIds.Contains(facilityKey))
                {
                    this.settings.Lifetime.StationFacilitiesAlreadyCanonical++;
                }
            }
        }
    }

    private string CreateFacilitySubmissionKey(ObservedFacilityRoster roster)
    {
        var attributionIdentity = this.settings.Attribution ==
            ForgeContributionAttribution.LivePilotName
                ? NormalizeKey(roster.LivePilotName)
                : "anonymous";
        return string.Concat(
            this.dataSet.AuthorityRevision.ToString(CultureInfo.InvariantCulture),
            "|station-services|",
            attributionIdentity,
            "|",
            roster.Fingerprint);
    }

    private static bool TryCreateFacilityRoster(
        ClientObservationSnapshot snapshot,
        out ObservedFacilityRoster roster,
        out string unavailableReason)
    {
        roster = null!;
        unavailableReason = "";

        if (snapshot.LifecycleState != ClientLifecycleState.InGame ||
            snapshot.LoadingOrTransitionFlag != 0 ||
            !snapshot.World.IsAvailable ||
            snapshot.World.Environment != ClientWorldEnvironment.Starbase ||
            !snapshot.StarbaseContext.IsAvailable ||
            snapshot.StarbaseContext.ReadErrorCount != 0 ||
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

        var rooms = snapshot.StarbaseContext.Rooms.ToArray();

        if (rooms.Length is < 1 or > 64 ||
            rooms.Any(room =>
                room.RoomClass < 0 ||
                room.DefinitionKey < 0) ||
            rooms.Select(room => room.RoomClass).Distinct().Count() != rooms.Length ||
            rooms.Select(room => room.DefinitionKey).Distinct().Count() != rooms.Length)
        {
            unavailableReason = string.Create(
                CultureInfo.InvariantCulture,
                $"Waiting for the complete station topology at {snapshot.World.CurrentStarbaseName.Trim()}.");
            return false;
        }

        var facilities = rooms
            .SelectMany(room => room.Facilities.Select(facility =>
                new ObservedStationFacility(
                    starbaseId,
                    snapshot.StarbaseContext.StarbaseDefinitionId,
                    snapshot.World.CurrentStarbaseName.Trim(),
                    snapshot.World.CurrentSectorName.Trim(),
                    snapshot.World.ActiveSectorNumber,
                    room.RoomClass,
                    room.DefinitionKey,
                    facility.Slot,
                    facility.DefinitionSlot,
                    facility.FacilityType,
                    string.IsNullOrWhiteSpace(facility.FacilityTypeName)
                        ? string.Create(
                            CultureInfo.InvariantCulture,
                            $"Facility type {facility.FacilityType}")
                        : facility.FacilityTypeName.Trim(),
                    facility.ReservedValue)))
            .OrderBy(facility => facility.RoomDefinitionKey)
            .ThenBy(facility => facility.RoomFacilitySlot)
            .ThenBy(facility => facility.FacilityType)
            .ToArray();

        if (facilities.Length is < 1 or > 256 ||
            facilities.Any(facility =>
                facility.RoomFacilitySlot is < 0 or > 4095 ||
                facility.DefinitionSlot is < 0 or > 4095 ||
                facility.FacilityType is < 0 or > 65535 ||
                string.IsNullOrWhiteSpace(facility.Name)) ||
            facilities
                .Select(facility =>
                    (facility.RoomDefinitionKey, facility.RoomFacilitySlot))
                .Distinct()
                .Count() != facilities.Length)
        {
            unavailableReason = string.Create(
                CultureInfo.InvariantCulture,
                $"Waiting for the complete station service roster at {snapshot.World.CurrentStarbaseName.Trim()}.");
            return false;
        }

        var fingerprintSource = string.Join(
            "|",
            facilities.Select(CreateStationFacilityFactFingerprint));
        var fingerprint = ForgeNavigationHash.ComputeSha256(
            Encoding.UTF8.GetBytes(
                string.Concat(
                    starbaseId.ToString(CultureInfo.InvariantCulture),
                    "|",
                    snapshot.StarbaseContext.StarbaseDefinitionId.ToString(
                        CultureInfo.InvariantCulture),
                    "|",
                    rooms.Length.ToString(CultureInfo.InvariantCulture),
                    "|",
                    NormalizeKey(snapshot.World.CurrentStarbaseName),
                    "|",
                    fingerprintSource)));
        roster = new ObservedFacilityRoster(
            snapshot.ProcessId,
            starbaseId,
            snapshot.StarbaseContext.StarbaseDefinitionId,
            snapshot.World.CurrentStarbaseName.Trim(),
            snapshot.World.CurrentSectorName.Trim(),
            snapshot.World.ActiveSectorNumber,
            identity.Name.Trim(),
            rooms.Length,
            facilities,
            fingerprint);
        return true;
    }

    private static bool IsStationFacilityCompatible(
        ForgeNavigationStationFacilityDocument existing,
        ObservedStationFacility observed)
    {
        return existing.StarbaseId == observed.StarbaseId &&
            existing.StarbaseDefinitionId == observed.StarbaseDefinitionId &&
            existing.ActiveSectorNumber == observed.ActiveSectorNumber &&
            existing.RoomClass == observed.RoomClass &&
            existing.RoomDefinitionKey == observed.RoomDefinitionKey &&
            existing.RoomFacilitySlot == observed.RoomFacilitySlot &&
            existing.DefinitionSlot == observed.DefinitionSlot &&
            existing.FacilityType == observed.FacilityType &&
            NormalizedEquals(existing.StationName, observed.StationName) &&
            NormalizedEquals(existing.SectorName, observed.SectorName) &&
            NormalizedEquals(existing.Name, observed.Name);
    }

    private static string CreateStationFacilityId(
        ObservedStationFacility facility)
    {
        var stationIdentity = CreateStationId(
            facility.StarbaseId,
            facility.StationName,
            facility.SectorName);
        var instanceIdentity = string.Create(
            CultureInfo.InvariantCulture,
            $"room:{facility.RoomDefinitionKey}:slot:{facility.RoomFacilitySlot}");
        var hash = ForgeNavigationHash.ComputeSha256(
            Encoding.UTF8.GetBytes(
                string.Concat(stationIdentity, "|", instanceIdentity)));
        return string.Concat("facility-", hash[..32]);
    }

    private static string CreateStationFacilityFactFingerprint(
        ObservedStationFacility facility)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{CreateStationFacilityId(facility)}:{facility.RoomClass}:{facility.RoomDefinitionKey}:{facility.RoomFacilitySlot}:{facility.DefinitionSlot}:{facility.FacilityType}:{NormalizeKey(facility.Name)}:{facility.ReservedValue}");
    }

    private sealed record ObservedFacilityRoster(
        int ProcessId,
        uint StarbaseId,
        uint StarbaseDefinitionId,
        string StationName,
        string SectorName,
        uint ActiveSectorNumber,
        string LivePilotName,
        int RoomCount,
        IReadOnlyList<ObservedStationFacility> Facilities,
        string Fingerprint,
        DateTimeOffset FirstObservedAtUtc = default,
        DateTimeOffset LastObservedAtUtc = default,
        int ObservationCount = 1);

    private sealed record ObservedStationFacility(
        uint StarbaseId,
        uint StarbaseDefinitionId,
        string StationName,
        string SectorName,
        uint ActiveSectorNumber,
        int RoomClass,
        int RoomDefinitionKey,
        int RoomFacilitySlot,
        int DefinitionSlot,
        int FacilityType,
        string Name,
        uint ReservedValue);
}
