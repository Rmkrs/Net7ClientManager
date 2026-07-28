namespace Net7ClientManager.Contributions;

using System.Globalization;
using System.Text;
using Net7ClientManager.Models;
using Net7ClientManager.Navigation;
using Net7ClientManager.Observations;
using Net7ClientManager.Observations.Models;

internal sealed partial class ForgeContributionCoordinator
{
    private readonly Dictionary<int, VendorInventoryProcessState>
        vendorInventoryProcessStates = [];
    private readonly HashSet<string> inFlightVendorCatalogKeys =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> completedVendorCatalogKeys =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> sessionObservedVendorKeys =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> sessionObservedVendorItemKeys =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> lifetimeObservedVendorKeys;
    private readonly HashSet<string> lifetimeObservedVendorItemKeys;

    private void ObserveVendorInventories(ClientObservationSnapshot snapshot)
    {
        if (!TryCreateVendorCatalogObservation(
                snapshot,
                out var observation,
                out var unavailableReason))
        {
            lock (this.stateLock)
            {
                if (this.vendorInventoryProcessStates.TryGetValue(
                        snapshot.ProcessId,
                        out var existing))
                {
                    existing.EndGeneration();
                }

                if (!string.IsNullOrWhiteSpace(unavailableReason))
                {
                    this.status = unavailableReason;
                }
            }

            return;
        }

        string? submissionKey = null;
        CancellationToken participationToken = default;
        var completedWithoutSubmission = false;

        lock (this.stateLock)
        {
            if (!this.vendorInventoryProcessStates.TryGetValue(
                    snapshot.ProcessId,
                    out var state))
            {
                state = new VendorInventoryProcessState();
                this.vendorInventoryProcessStates.Add(snapshot.ProcessId, state);
            }

            if (!string.Equals(
                    state.ActiveVendorNpcId,
                    observation.VendorNpcId,
                    StringComparison.Ordinal))
            {
                state.BeginGeneration(
                    observation.VendorNpcId,
                    observation.CatalogFingerprint,
                    snapshot.ObservedAt);
                this.status = string.Create(
                    CultureInfo.InvariantCulture,
                    $"Watching {observation.VendorName}'s catalogue for a fresh ownership confirmation.");
                return;
            }

            state.ObservationCount++;

            if (!string.Equals(
                    state.CurrentFingerprint,
                    observation.CatalogFingerprint,
                    StringComparison.Ordinal))
            {
                state.ConfirmRewrite(
                    observation.VendorNpcId,
                    observation.CatalogFingerprint,
                    snapshot.ObservedAt);
                this.status = string.Create(
                    CultureInfo.InvariantCulture,
                    $"Waiting for {observation.VendorName}'s {observation.Items.Count}-item catalogue to settle.");
                return;
            }

            if (!state.OwnershipConfirmed &&
                state.TryConfirmKnownOwner(
                    observation.VendorNpcId,
                    observation.CatalogFingerprint))
            {
                state.StableSince = snapshot.ObservedAt;
                state.ObservationCount = 1;
            }

            if (!state.OwnershipConfirmed || observation.Items.Count == 0)
            {
                return;
            }

            observation = observation with
            {
                FirstObservedAtUtc = state.StableSince,
                LastObservedAtUtc = snapshot.ObservedAt,
                ObservationCount = state.ObservationCount,
            };

            if (snapshot.ObservedAt - state.StableSince < RosterSettleTime ||
                snapshot.ObservedAt < state.NextAttemptAllowedAt ||
                string.Equals(
                    state.AttemptedFingerprint,
                    observation.CatalogFingerprint,
                    StringComparison.Ordinal))
            {
                return;
            }

            if (!this.TryValidateCanonicalVendor(
                    observation,
                    out var canonicalUnavailableReason))
            {
                this.status = canonicalUnavailableReason;
                return;
            }

            state.AttemptedFingerprint = observation.CatalogFingerprint;
            state.NextAttemptAllowedAt = DateTimeOffset.MaxValue;
            var canonicalTemplateIds = this.dataSet.Document.VendorItems
                .Where(item => string.Equals(
                    item.VendorNpcId,
                    observation.VendorNpcId,
                    StringComparison.Ordinal))
                .Select(item => item.ItemTemplateId)
                .ToHashSet();
            var observedTemplateIds = observation.Items
                .Select(item => item.ItemTemplateId)
                .ToHashSet();
            var alreadyCurrent = canonicalTemplateIds.SetEquals(
                observedTemplateIds);
            this.RecordObservedVendorCatalog(
                observation,
                alreadyCurrent
                    ? observedTemplateIds
                    : new HashSet<int>());

            if (alreadyCurrent)
            {
                this.status = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{observation.VendorName}'s {observation.Items.Count}-item catalogue is already known by Forge.");
                completedWithoutSubmission = true;
            }
            else
            {
                submissionKey = this.CreateVendorCatalogSubmissionKey(
                    observation);

                if (this.completedVendorCatalogKeys.Contains(submissionKey) ||
                    !this.inFlightVendorCatalogKeys.Add(submissionKey))
                {
                    return;
                }

                this.status = string.Create(
                    CultureInfo.InvariantCulture,
                    $"Preparing {observation.VendorName}'s complete {observation.Items.Count}-item catalogue for Forge.");
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
        var task = this.SubmitVendorInventoryAsync(
            observation,
            submissionKey!,
            submissionCancellation);
        this.Track(task);
    }

    private async Task SubmitVendorInventoryAsync(
        ObservedVendorCatalog observation,
        string submissionKey,
        CancellationTokenSource submissionCancellation)
    {
        var cancellationToken = submissionCancellation.Token;

        try
        {
            var identity = await this.identityService.EnsureAsync(
                    observation.LivePilotName,
                    cancellationToken)
                .ConfigureAwait(false);
            var unsignedRequest = new ForgeVendorInventoryContributionRequest
            {
                ContributorId = identity.ContributorId,
                RequestId = Guid.NewGuid().ToString("D", CultureInfo.InvariantCulture),
                SubmittedAtUtc = DateTimeOffset.UtcNow,
                FirstObservedAtUtc = observation.FirstObservedAtUtc,
                LastObservedAtUtc = observation.LastObservedAtUtc,
                ObservationCount = observation.ObservationCount,
                ClientVersion = clientVersion,
                DatasetRevision = this.dataSet.AuthorityRevision,
                Attribution = this.settings.Attribution ==
                    ForgeContributionAttribution.LivePilotName
                        ? "live-pilot-name"
                        : "publicly-anonymous",
                LivePilotName = observation.LivePilotName,
                StarbaseId = observation.StarbaseId,
                StationName = observation.StationName,
                SectorName = observation.SectorName,
                ActiveSectorNumber = observation.ActiveSectorNumber,
                RoomClass = observation.RoomClass,
                RoomDefinitionKey = observation.RoomDefinitionKey,
                RoomNpcSlot = observation.RoomNpcSlot,
                DefinitionKey = observation.DefinitionKey,
                DefinitionSecondaryId = observation.DefinitionSecondaryId,
                VendorName = observation.VendorName,
                VendorType = observation.VendorType,
                AmbientType = observation.AmbientType,
                CatalogFingerprint = observation.CatalogFingerprint,
                Items =
                [
                    .. observation.Items.Select(item =>
                        new ForgeVendorInventoryContributionItem
                        {
                            Slot = item.Slot,
                            ItemTemplateId = item.ItemTemplateId,
                        }),
                ],
            };
            var request = unsignedRequest with
            {
                Signature = identity.Sign(unsignedRequest),
            };
            var response = await this.client.SubmitVendorInventoryAsync(
                    request,
                    cancellationToken)
                .ConfigureAwait(false);

            lock (this.stateLock)
            {
                this.completedVendorCatalogKeys.Add(submissionKey);
                this.session.VendorItemFactsSubmitted += response.Received;
                this.session.VendorItemsAlreadyCanonical +=
                    response.AlreadyCanonical;
                this.session.VendorItemEvidenceAccepted +=
                    response.EvidenceAccepted;
                this.session.VendorItemConflicts += response.Conflicts;
                this.session.VendorItemsRemoved += response.Removed;
                this.session.SuccessfulBatches++;
                this.session.LastSuccessfulContributionUtc =
                    DateTimeOffset.UtcNow;

                var lifetime = this.settings.Lifetime;
                lifetime.VendorItemFactsSubmitted += response.Received;
                lifetime.VendorItemsAlreadyCanonical +=
                    response.AlreadyCanonical;
                lifetime.VendorItemEvidenceAccepted +=
                    response.EvidenceAccepted;
                lifetime.VendorItemConflicts += response.Conflicts;
                lifetime.VendorItemsRemoved += response.Removed;
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
                        $"Forge accepted {observation.VendorName}'s catalogue and published revision {revision}. Activate the staged Forge dataset update when ready.")
                    : string.Create(
                        CultureInfo.InvariantCulture,
                        $"Forge accepted {observation.VendorName}'s catalogue: {response.EvidenceAccepted} evidence facts, {response.AlreadyCanonical} already canonical, {response.Removed} removed.");
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
                    "Forge vendor-inventory contribution failed: ",
                    exception.Message,
                    ". The confirmed catalogue can be retried later this session.");

                if (this.vendorInventoryProcessStates.TryGetValue(
                        observation.ProcessId,
                        out var state) &&
                    string.Equals(
                        state.ActiveVendorNpcId,
                        observation.VendorNpcId,
                        StringComparison.Ordinal) &&
                    string.Equals(
                        state.CurrentFingerprint,
                        observation.CatalogFingerprint,
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
                this.inFlightVendorCatalogKeys.Remove(submissionKey);
            }

            submissionCancellation.Dispose();
        }
    }

    private bool TryValidateCanonicalVendor(
        ObservedVendorCatalog observation,
        out string reason)
    {
        var vendor = this.dataSet.Document.Npcs.FirstOrDefault(npc =>
            string.Equals(
                npc.Id,
                observation.VendorNpcId,
                StringComparison.Ordinal));

        if (vendor == null)
        {
            reason = string.Create(
                CultureInfo.InvariantCulture,
                $"Waiting for {observation.VendorName} to become canonical in the active Forge dataset before contributing inventory.");
            return false;
        }

        if (vendor.Role != observation.VendorType ||
            vendor.Classification != observation.AmbientType ||
            vendor.StarbaseId != observation.StarbaseId ||
            vendor.ActiveSectorNumber != observation.ActiveSectorNumber ||
            vendor.RoomClass != observation.RoomClass ||
            vendor.RoomDefinitionKey != observation.RoomDefinitionKey ||
            vendor.RoomNpcSlot != observation.RoomNpcSlot ||
            vendor.DefinitionKey != observation.DefinitionKey ||
            vendor.DefinitionSecondaryId != observation.DefinitionSecondaryId ||
            string.IsNullOrWhiteSpace(vendor.Name) ||
            !NormalizedEquals(vendor.Name, observation.VendorName) ||
            !NormalizedEquals(vendor.StationName, observation.StationName) ||
            !NormalizedEquals(vendor.SectorName, observation.SectorName))
        {
            reason = string.Create(
                CultureInfo.InvariantCulture,
                $"Waiting for the canonical Forge identity of {observation.VendorName} to match the live vendor before contributing inventory.");
            return false;
        }

        reason = "";
        return true;
    }

    private void RecordObservedVendorCatalog(
        ObservedVendorCatalog observation,
        IReadOnlySet<int> canonicalTemplateIds)
    {
        if (this.sessionObservedVendorKeys.Add(observation.VendorNpcId))
        {
            this.session.VendorsObserved++;
        }

        if (this.lifetimeObservedVendorKeys.Add(observation.VendorNpcId))
        {
            this.settings.Lifetime.ObservedVendorKeys.Add(
                observation.VendorNpcId);
            this.settings.Lifetime.VendorsObserved++;
        }

        foreach (var item in observation.Items)
        {
            var itemId = CreateVendorItemId(
                observation.VendorNpcId,
                item.ItemTemplateId);

            if (this.sessionObservedVendorItemKeys.Add(itemId))
            {
                this.session.VendorItemsObserved++;

                if (canonicalTemplateIds.Contains(item.ItemTemplateId))
                {
                    this.session.VendorItemsAlreadyCanonical++;
                }
            }

            if (this.lifetimeObservedVendorItemKeys.Add(itemId))
            {
                this.settings.Lifetime.ObservedVendorItemKeys.Add(itemId);
                this.settings.Lifetime.VendorItemsObserved++;

                if (canonicalTemplateIds.Contains(item.ItemTemplateId))
                {
                    this.settings.Lifetime.VendorItemsAlreadyCanonical++;
                }
            }
        }
    }

    private string CreateVendorCatalogSubmissionKey(
        ObservedVendorCatalog observation)
    {
        var attributionIdentity = this.settings.Attribution ==
            ForgeContributionAttribution.LivePilotName
                ? NormalizeKey(observation.LivePilotName)
                : "ANONYMOUS";
        return string.Concat(
            this.dataSet.AuthorityRevision.ToString(CultureInfo.InvariantCulture),
            "|",
            attributionIdentity,
            "|",
            observation.VendorNpcId,
            "|",
            observation.CatalogFingerprint);
    }

    private static bool TryCreateVendorCatalogObservation(
        ClientObservationSnapshot snapshot,
        out ObservedVendorCatalog observation,
        out string unavailableReason)
    {
        observation = null!;
        unavailableReason = "";
        var interaction = snapshot.StarbaseContext.Interaction;

        if (snapshot.LifecycleState != ClientLifecycleState.InGame ||
            snapshot.LoadingOrTransitionFlag != 0 ||
            !snapshot.World.IsAvailable ||
            snapshot.World.Environment != ClientWorldEnvironment.Starbase ||
            !snapshot.StarbaseContext.IsAvailable ||
            !snapshot.LocalPlayer.IsAvailable ||
            interaction.Kind is not (
                ClientStarbaseInteractionKind.TalkTree or
                ClientStarbaseInteractionKind.VendorTrade) ||
            interaction.RoomClass < 0 ||
            interaction.NpcSlot < 0 ||
            string.IsNullOrWhiteSpace(snapshot.World.CurrentStarbaseName) ||
            string.IsNullOrWhiteSpace(snapshot.World.CurrentSectorName))
        {
            return false;
        }

        var room = snapshot.StarbaseContext.Rooms.FirstOrDefault(candidate =>
            candidate.RoomClass == interaction.RoomClass);
        var npc = room?.Npcs.FirstOrDefault(candidate =>
            candidate.Slot == interaction.NpcSlot);

        if (room == null || npc == null || !npc.IsVendor ||
            string.IsNullOrWhiteSpace(npc.Name))
        {
            return false;
        }

        var inventory = snapshot.LocalPlayer.VendorInventory;

        if (!inventory.IsAvailable ||
            inventory.ReadErrorCount != 0 ||
            inventory.Slots.Count != 128)
        {
            unavailableReason = string.Create(
                CultureInfo.InvariantCulture,
                $"Waiting for a complete readable vendor catalogue from {npc.Name.Trim()}.");
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
        var items = inventory.Items
            .Select(item => new ObservedVendorItem(
                item.Slot,
                item.ItemTemplateId ?? 0,
                item.ItemName?.Trim() ?? ""))
            .OrderBy(item => item.ItemTemplateId)
            .ThenBy(item => item.Slot)
            .ToArray();

        if (items.Any(item =>
                item.ItemTemplateId <= 0 ||
                string.IsNullOrWhiteSpace(item.Name)) ||
            items.Select(item => item.Slot).Distinct().Count() != items.Length ||
            items.Select(item => item.ItemTemplateId).Distinct().Count() != items.Length)
        {
            unavailableReason = string.Create(
                CultureInfo.InvariantCulture,
                $"Waiting for all item identities and names in {npc.Name.Trim()}'s catalogue to resolve.");
            return false;
        }

        var stationName = snapshot.World.CurrentStarbaseName.Trim();
        var sectorName = snapshot.World.CurrentSectorName.Trim();
        var observedNpc = new ObservedNpc(
            starbaseId,
            stationName,
            sectorName,
            snapshot.World.ActiveSectorNumber,
            room.RoomClass,
            room.DefinitionKey,
            npc.Slot,
            npc.DefinitionKey,
            npc.DefinitionSecondaryId,
            npc.Name.Trim(),
            npc.VendorType,
            npc.AmbientType);
        var vendorNpcId = CreateNpcId(observedNpc);
        var fingerprintSource = string.Join(
            "|",
            items.Select(item => item.ItemTemplateId.ToString(
                CultureInfo.InvariantCulture)));
        var fingerprint = ForgeNavigationHash.ComputeSha256(
            Encoding.UTF8.GetBytes(fingerprintSource));
        observation = new ObservedVendorCatalog(
            snapshot.ProcessId,
            starbaseId,
            stationName,
            sectorName,
            snapshot.World.ActiveSectorNumber,
            identity.Name.Trim(),
            room.RoomClass,
            room.DefinitionKey,
            npc.Slot,
            npc.DefinitionKey,
            npc.DefinitionSecondaryId,
            npc.Name.Trim(),
            (int)npc.VendorType,
            (int)npc.AmbientType,
            vendorNpcId,
            items,
            fingerprint);
        return true;
    }

    private static string CreateVendorItemId(
        string vendorNpcId,
        int itemTemplateId)
    {
        var hash = ForgeNavigationHash.ComputeSha256(
            Encoding.UTF8.GetBytes(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{vendorNpcId}|item-template:{itemTemplateId}")));
        return string.Concat("vendor-item-", hash[..32]);
    }

    private sealed class VendorInventoryProcessState
    {
        private readonly HashSet<string> confirmedOwners =
            new(StringComparer.Ordinal);

        public string? ActiveVendorNpcId { get; private set; }

        public string CurrentFingerprint { get; private set; } = "";

        public DateTimeOffset StableSince { get; set; }

        public int ObservationCount { get; set; }

        public bool OwnershipConfirmed { get; private set; }

        public string? AttemptedFingerprint { get; set; }

        public DateTimeOffset NextAttemptAllowedAt { get; set; }

        public void BeginGeneration(
            string vendorNpcId,
            string fingerprint,
            DateTimeOffset observedAt)
        {
            this.ActiveVendorNpcId = vendorNpcId;
            this.CurrentFingerprint = fingerprint;
            this.StableSince = observedAt;
            this.ObservationCount = 1;
            this.AttemptedFingerprint = null;
            this.NextAttemptAllowedAt = DateTimeOffset.MinValue;
            this.OwnershipConfirmed = this.TryConfirmKnownOwner(
                vendorNpcId,
                fingerprint);
        }

        public void ConfirmRewrite(
            string vendorNpcId,
            string fingerprint,
            DateTimeOffset observedAt)
        {
            this.CurrentFingerprint = fingerprint;
            this.StableSince = observedAt;
            this.ObservationCount = 1;
            this.AttemptedFingerprint = null;
            this.NextAttemptAllowedAt = DateTimeOffset.MinValue;
            this.confirmedOwners.Add(
                CreateConfirmedOwnerKey(vendorNpcId, fingerprint));
            this.OwnershipConfirmed = true;
        }

        public bool TryConfirmKnownOwner(
            string vendorNpcId,
            string fingerprint)
        {
            this.OwnershipConfirmed = this.confirmedOwners.Contains(
                CreateConfirmedOwnerKey(vendorNpcId, fingerprint));
            return this.OwnershipConfirmed;
        }

        private static string CreateConfirmedOwnerKey(
            string vendorNpcId,
            string fingerprint)
        {
            return string.Concat(vendorNpcId, "|", fingerprint);
        }

        public void EndGeneration()
        {
            this.ActiveVendorNpcId = null;
            this.CurrentFingerprint = "";
            this.OwnershipConfirmed = false;
            this.AttemptedFingerprint = null;
            this.NextAttemptAllowedAt = DateTimeOffset.MinValue;
            this.ObservationCount = 0;
        }
    }

    private sealed record ObservedVendorCatalog(
        int ProcessId,
        uint StarbaseId,
        string StationName,
        string SectorName,
        uint ActiveSectorNumber,
        string LivePilotName,
        int RoomClass,
        int RoomDefinitionKey,
        int RoomNpcSlot,
        int DefinitionKey,
        int DefinitionSecondaryId,
        string VendorName,
        int VendorType,
        int AmbientType,
        string VendorNpcId,
        IReadOnlyList<ObservedVendorItem> Items,
        string CatalogFingerprint,
        DateTimeOffset FirstObservedAtUtc = default,
        DateTimeOffset LastObservedAtUtc = default,
        int ObservationCount = 1);

    private sealed record ObservedVendorItem(
        int Slot,
        int ItemTemplateId,
        string Name);
}
