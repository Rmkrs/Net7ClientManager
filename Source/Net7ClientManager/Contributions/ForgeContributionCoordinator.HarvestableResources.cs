namespace Net7ClientManager.Contributions;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Net7ClientManager.Models;
using Net7ClientManager.Navigation;
using Net7ClientManager.Observations;
using Net7ClientManager.Observations.Models;

internal sealed partial class ForgeContributionCoordinator
{
    private static readonly TimeSpan HarvestableBatchSettleTime =
        TimeSpan.FromSeconds(30);

    private readonly Dictionary<HarvestableProcessKey, HarvestableProcessState>
        harvestableProcessStates = [];
    private readonly HashSet<string> inFlightHarvestableBatchKeys =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> sessionObservedHarvestableResourceKeys =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> lifetimeObservedHarvestableResourceKeys;
    private readonly HashSet<string> acceptedHarvestableResourceIds =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<int>> submittedHarvestableItems =
        new(StringComparer.Ordinal);

    private void ObserveHarvestableResources(ClientObservationSnapshot snapshot)
    {
        List<PreparedHarvestableSubmission> submissions = [];
        var observationsChanged = false;

        lock (this.stateLock)
        {
            if (!this.TryCreateHarvestableContext(
                    snapshot,
                    out var context,
                    out var unavailableReason))
            {
                foreach (var pair in this.harvestableProcessStates
                             .Where(pair => pair.Key.ProcessId == snapshot.ProcessId)
                             .ToArray())
                {
                    pair.Value.IsRetired = true;

                    if (this.TryPrepareHarvestableSubmission(
                            pair.Key,
                            pair.Value,
                            snapshot.ObservedAt,
                            force: true,
                            out var submission))
                    {
                        submissions.Add(submission);
                    }
                    else if (pair.Value.Pending.Count == 0 &&
                             pair.Value.InFlightKey == null)
                    {
                        this.harvestableProcessStates.Remove(pair.Key);
                    }
                }

                if (!string.IsNullOrWhiteSpace(unavailableReason))
                {
                    this.status = unavailableReason;
                }
            }
            else
            {
                var currentKey = new HarvestableProcessKey(
                    snapshot.ProcessId,
                    context.SectorId);

                foreach (var pair in this.harvestableProcessStates
                             .Where(pair =>
                                 pair.Key.ProcessId == snapshot.ProcessId &&
                                 !pair.Key.Equals(currentKey))
                             .ToArray())
                {
                    pair.Value.IsRetired = true;

                    if (this.TryPrepareHarvestableSubmission(
                            pair.Key,
                            pair.Value,
                            snapshot.ObservedAt,
                            force: true,
                            out var submission))
                    {
                        submissions.Add(submission);
                    }
                    else if (pair.Value.Pending.Count == 0 &&
                             pair.Value.InFlightKey == null)
                    {
                        this.harvestableProcessStates.Remove(pair.Key);
                    }
                }

                if (!this.harvestableProcessStates.TryGetValue(
                        currentKey,
                        out var state))
                {
                    state = new HarvestableProcessState(context);
                    this.harvestableProcessStates[currentKey] = state;
                }
                else
                {
                    state.Refresh(context);
                    state.IsRetired = false;
                }

                if (TryCreateHarvestableSample(snapshot, out var sample))
                {
                    observationsChanged |= this.ObserveHarvestableSample(
                        state,
                        sample);
                }

                if (this.TryPrepareHarvestableSubmission(
                        currentKey,
                        state,
                        snapshot.ObservedAt,
                        force: false,
                        out var currentSubmission))
                {
                    submissions.Add(currentSubmission);
                }
            }
        }

        if (observationsChanged)
        {
            this.saveSettings();
        }

        this.RaiseStatisticsChanged();

        foreach (var submission in submissions)
        {
            var submissionCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    this.cancellation.Token,
                    submission.ParticipationToken);
            var task = this.SubmitHarvestableResourcesAsync(
                submission.Batch,
                submission.SubmissionKey,
                submissionCancellation);
            this.Track(task);
        }
    }

    private bool ObserveHarvestableSample(
        HarvestableProcessState state,
        ObservedHarvestableSample sample)
    {
        var stableKey = CreateHarvestableStableKey(state.SectorId, sample);
        var changed = false;

        foreach (var itemTemplateId in sample.ItemTemplateIds)
        {
            changed |= this.RecordObservedHarvestableResource(
                state.SectorId,
                sample,
                itemTemplateId);
        }

        this.submittedHarvestableItems.TryGetValue(
            stableKey,
            out var submittedItems);
        var hasUnsubmittedItem = submittedItems == null ||
            sample.ItemTemplateIds.Any(item => !submittedItems.Contains(item));

        if (!state.Pending.TryGetValue(stableKey, out var track))
        {
            if (!hasUnsubmittedItem)
            {
                return changed;
            }

            track = new HarvestableTrack(stableKey, sample);
            state.Pending.Add(stableKey, track);
            state.FirstPendingAtUtc ??= sample.ObservedAtUtc;
        }
        else
        {
            track.Observe(sample);
        }

        this.status = string.Create(
            CultureInfo.InvariantCulture,
            $"Harvestable contents observed in {sample.Name} level {sample.TechLevel}.");
        return changed;
    }

    private bool TryPrepareHarvestableSubmission(
        HarvestableProcessKey stateKey,
        HarvestableProcessState state,
        DateTimeOffset observedAtUtc,
        bool force,
        out PreparedHarvestableSubmission submission)
    {
        submission = null!;
        var canAttempt = state.Pending.Count != 0 &&
            state.InFlightKey == null &&
            observedAtUtc >= state.NextAttemptAllowedAt;
        var isSettled = force ||
            state.Pending.Count >= 64 ||
            state.FirstPendingAtUtc is not { } firstPending ||
            observedAtUtc - firstPending >= HarvestableBatchSettleTime;

        if (!canAttempt || !isSettled)
        {
            return false;
        }

        var tracks = state.Pending.Values
            .OrderBy(track => track.FirstObservedAtUtc)
            .ThenBy(track => track.ObjectId)
            .Take(512)
            .Select(track => track.Snapshot())
            .ToArray();
        var batch = new ObservedHarvestableBatch(
            stateKey,
            state.LivePilotName,
            state.SectorId,
            state.SectorKey,
            state.SectorName,
            state.SystemName,
            state.ActiveSectorNumber,
            tracks.Min(track => track.FirstObservedAtUtc),
            tracks.Max(track => track.LastObservedAtUtc),
            tracks);
        var submissionKey = CreateHarvestableSubmissionKey(batch);

        if (!this.inFlightHarvestableBatchKeys.Add(submissionKey))
        {
            return false;
        }

        state.InFlightKey = submissionKey;
        this.status = "Sharing harvestable-resource observations with Forge.";
        submission = new PreparedHarvestableSubmission(
            batch,
            submissionKey,
            this.participationCancellation.Token);
        return true;
    }

    private async Task SubmitHarvestableResourcesAsync(
        ObservedHarvestableBatch batch,
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
            var unsignedRequest = new ForgeHarvestableResourcesContributionRequest
            {
                ContributorId = identity.ContributorId,
                RequestId = Guid.NewGuid().ToString("D", CultureInfo.InvariantCulture),
                SubmittedAtUtc = DateTimeOffset.UtcNow,
                FirstObservedAtUtc = batch.FirstObservedAtUtc,
                LastObservedAtUtc = batch.LastObservedAtUtc,
                ObservationCount = checked(
                    batch.Tracks.Sum(track => track.ObservationCount)),
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
                Harvestables =
                [
                    .. batch.Tracks.Select(track =>
                        new ForgeHarvestableResourceContributionItem
                        {
                            ObjectId = track.ObjectId,
                            Name = track.Name,
                            RawObjectType = track.RawObjectType,
                            TechLevel = track.TechLevel,
                            X = track.X,
                            Y = track.Y,
                            Z = track.Z,
                            FirstObservedAtUtc = track.FirstObservedAtUtc,
                            LastObservedAtUtc = track.LastObservedAtUtc,
                            ObservationCount = track.ObservationCount,
                            ItemTemplateIds = track.ItemTemplateIds,
                        }),
                ],
            };
            var request = unsignedRequest with
            {
                Signature = identity.Sign(unsignedRequest),
            };
            var response = await this.client.SubmitHarvestableResourcesAsync(
                    request,
                    cancellationToken)
                .ConfigureAwait(false);

            lock (this.stateLock)
            {
                foreach (var relationshipId in response.AcceptedRelationshipIds)
                {
                    this.acceptedHarvestableResourceIds.Add(relationshipId);
                }

                this.SynchronizePendingHarvestableResourceIds();

                foreach (var track in batch.Tracks)
                {
                    if (!this.submittedHarvestableItems.TryGetValue(
                            track.StableKey,
                            out var submittedItems))
                    {
                        submittedItems = [];
                        this.submittedHarvestableItems.Add(
                            track.StableKey,
                            submittedItems);
                    }

                    submittedItems.UnionWith(track.ItemTemplateIds);
                }

                if (this.harvestableProcessStates.TryGetValue(
                        batch.StateKey,
                        out var state))
                {
                    foreach (var track in batch.Tracks)
                    {
                        if (!state.Pending.TryGetValue(
                                track.StableKey,
                                out var current))
                        {
                            continue;
                        }

                        if (!this.submittedHarvestableItems.TryGetValue(
                                track.StableKey,
                                out var submittedItems))
                        {
                            continue;
                        }

                        var currentItems = current.Snapshot().ItemTemplateIds;

                        if (currentItems.All(submittedItems.Contains))
                        {
                            state.Pending.Remove(track.StableKey);
                        }
                    }

                    state.FirstPendingAtUtc = state.Pending.Count == 0
                        ? null
                        : state.Pending.Values.Min(track => track.FirstObservedAtUtc);
                    state.NextAttemptAllowedAt = DateTimeOffset.MinValue;

                    if (state.IsRetired && state.Pending.Count == 0)
                    {
                        this.harvestableProcessStates.Remove(batch.StateKey);
                    }
                }

                this.session.HarvestableFactsSubmitted += response.Received;
                this.session.HarvestableAlreadyCanonical += response.AlreadyCanonical;
                this.session.HarvestableEvidenceAccepted += response.EvidenceAccepted;
                this.session.HarvestableVariantsCreated += response.VariantsCreated;
                this.session.HarvestableFieldsCreated += response.FieldsCreated;
                this.session.HarvestableFieldsUpdated += response.FieldsUpdated;
                this.session.HarvestableRelationshipsCreated += response.RelationshipsCreated;
                this.session.HarvestableRelationshipsStrengthened +=
                    response.RelationshipsStrengthened;
                this.session.SuccessfulBatches++;
                this.session.LastSuccessfulContributionUtc = DateTimeOffset.UtcNow;

                var lifetime = this.settings.Lifetime;
                lifetime.HarvestableFactsSubmitted += response.Received;
                lifetime.HarvestableAlreadyCanonical += response.AlreadyCanonical;
                lifetime.HarvestableEvidenceAccepted += response.EvidenceAccepted;
                lifetime.HarvestableVariantsCreated += response.VariantsCreated;
                lifetime.HarvestableFieldsCreated += response.FieldsCreated;
                lifetime.HarvestableFieldsUpdated += response.FieldsUpdated;
                lifetime.HarvestableRelationshipsCreated += response.RelationshipsCreated;
                lifetime.HarvestableRelationshipsStrengthened +=
                    response.RelationshipsStrengthened;
                lifetime.SuccessfulBatches++;
                lifetime.LastSuccessfulContributionUtc = DateTimeOffset.UtcNow;

                if (response.PublishedRevision is { } revision)
                {
                    this.session.PublishedRevisions++;
                    lifetime.PublishedRevisions++;
                    this.status = string.Create(
                        CultureInfo.InvariantCulture,
                        $"Forge accepted the harvestable batch and published revision {revision}.");
                }
                else
                {
                    this.status = string.Create(
                        CultureInfo.InvariantCulture,
                        $"Forge accepted {response.Received} harvestable observations and {response.EvidenceAccepted} item evidence facts.");
                }
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
            // Participation changes and shutdown abandon best-effort work.
        }
        catch (Exception)
        {
            lock (this.stateLock)
            {
                this.session.FailedBatches++;
                this.session.LastFailedContributionUtc = DateTimeOffset.UtcNow;
                this.settings.Lifetime.FailedBatches++;
                this.settings.Lifetime.LastFailedContributionUtc =
                    DateTimeOffset.UtcNow;
                this.status =
                    "Harvestable observations could not be shared. They will retry automatically.";

                if (this.harvestableProcessStates.TryGetValue(
                        batch.StateKey,
                        out var state))
                {
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
                this.inFlightHarvestableBatchKeys.Remove(submissionKey);

                if (this.harvestableProcessStates.TryGetValue(
                        batch.StateKey,
                        out var state) &&
                    string.Equals(
                        state.InFlightKey,
                        submissionKey,
                        StringComparison.Ordinal))
                {
                    state.InFlightKey = null;
                }
            }

            submissionCancellation.Dispose();
        }
    }

    private bool TryCreateHarvestableContext(
        ClientObservationSnapshot snapshot,
        out ObservedHarvestableContext context,
        out string unavailableReason)
    {
        context = null!;
        unavailableReason = "";

        if (snapshot.LifecycleState != ClientLifecycleState.InGame ||
            snapshot.LoadingOrTransitionFlag != 0 ||
            !snapshot.World.IsAvailable ||
            snapshot.World.Environment != ClientWorldEnvironment.Space ||
            snapshot.World.ActiveSectorNumber == 0)
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
            unavailableReason =
                "Harvestable contribution is waiting for the latest Forge data.";
            return false;
        }

        context = new ObservedHarvestableContext(
            identity.Name.Trim(),
            sector.Id,
            sector.Key,
            sector.Name,
            sector.SystemName,
            snapshot.World.ActiveSectorNumber);
        return true;
    }

    private static bool TryCreateHarvestableSample(
        ClientObservationSnapshot snapshot,
        out ObservedHarvestableSample sample)
    {
        sample = null!;
        var target = snapshot.Target;
        var manifest = target.Asteroid;

        if (!target.IsAvailable ||
            !target.HasTarget ||
            target.ObjectId == 0 ||
            target.ObjectType != 0x26 ||
            string.IsNullOrWhiteSpace(target.Name) ||
            !target.Distance.Target.IsAvailable ||
            !manifest.IsAvailable ||
            manifest.TechLevel is not { } techLevel ||
            techLevel is < 1 or > 255 ||
            manifest.MaximumObservedSlotCount != 40 ||
            manifest.PresentSlotCount != 40 ||
            manifest.ValidSlotCount != 40 ||
            !float.IsFinite(target.Distance.Target.Position.X) ||
            !float.IsFinite(target.Distance.Target.Position.Y) ||
            !float.IsFinite(target.Distance.Target.Position.Z))
        {
            return false;
        }

        var itemTemplateIds = manifest.Resources
            .Select(resource => resource.ItemTemplateId)
            .Where(itemTemplateId => itemTemplateId is > 0)
            .Select(itemTemplateId => itemTemplateId!.Value)
            .Distinct()
            .Order()
            .ToArray();

        if (itemTemplateIds.Length == 0)
        {
            return false;
        }

        sample = new ObservedHarvestableSample(
            target.ObjectId,
            target.Name.Trim(),
            target.ObjectType,
            techLevel,
            target.Distance.Target.Position.X,
            target.Distance.Target.Position.Y,
            target.Distance.Target.Position.Z,
            snapshot.ObservedAt,
            itemTemplateIds);
        return true;
    }

    private bool RecordObservedHarvestableResource(
        string sectorId,
        ObservedHarvestableSample sample,
        int itemTemplateId)
    {
        var key = string.Create(
            CultureInfo.InvariantCulture,
            $"{sectorId}|{NormalizeHarvestableIdentityValue(sample.Name)}|{sample.TechLevel}|{itemTemplateId}|{MathF.Round(sample.X / 1000.0f):R}|{MathF.Round(sample.Y / 1000.0f):R}|{MathF.Round(sample.Z / 1000.0f):R}");
        var changed = false;

        if (this.sessionObservedHarvestableResourceKeys.Add(key))
        {
            this.session.HarvestableResourcesObserved =
                this.sessionObservedHarvestableResourceKeys.Count;
            changed = true;
        }

        if (this.lifetimeObservedHarvestableResourceKeys.Add(key))
        {
            this.settings.Lifetime.ObservedHarvestableResourceKeys =
            [
                .. this.lifetimeObservedHarvestableResourceKeys
                    .Order(StringComparer.Ordinal),
            ];
            this.settings.Lifetime.HarvestableResourcesObserved =
                this.lifetimeObservedHarvestableResourceKeys.Count;
            changed = true;
        }

        return changed;
    }

    private bool PruneActivatedHarvestableResourceIds()
    {
        var activeIds = this.dataSet.Document.HarvestableResources
            .Select(resource => resource.Id)
            .ToHashSet(StringComparer.Ordinal);
        this.acceptedHarvestableResourceIds.RemoveWhere(activeIds.Contains);
        return this.SynchronizePendingHarvestableResourceIds();
    }

    private bool SynchronizePendingHarvestableResourceIds()
    {
        var pending = this.acceptedHarvestableResourceIds
            .Where(relationshipId =>
                !this.dataSet.Document.HarvestableResources.Any(resource =>
                    string.Equals(resource.Id, relationshipId, StringComparison.Ordinal)))
            .Order(StringComparer.Ordinal)
            .ToArray();

        if (this.settings.PendingHarvestableResourceIds.SequenceEqual(
                pending,
                StringComparer.Ordinal))
        {
            return false;
        }

        this.settings.PendingHarvestableResourceIds = [.. pending];
        return true;
    }

    private static string CreateHarvestableStableKey(
        string sectorId,
        ObservedHarvestableSample sample)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{sectorId}|{sample.ObjectId}|{sample.RawObjectType}|{NormalizeHarvestableIdentityValue(sample.Name)}|{sample.TechLevel}|{sample.X:R}|{sample.Y:R}|{sample.Z:R}");
    }

    private static string CreateHarvestableSubmissionKey(
        ObservedHarvestableBatch batch)
    {
        var builder = new StringBuilder();
        builder.Append(batch.StateKey.ProcessId.ToString(CultureInfo.InvariantCulture));
        builder.Append('|');
        builder.Append(batch.SectorId);

        foreach (var track in batch.Tracks)
        {
            builder.Append('|');
            builder.Append(track.Fingerprint);
        }

        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static string NormalizeHarvestableIdentityValue(string value)
    {
        return string.Concat(value.Where(char.IsLetterOrDigit)).ToUpperInvariant();
    }

    private void RemoveHarvestableProcessStates(int processId)
    {
        foreach (var key in this.harvestableProcessStates.Keys
                     .Where(key => key.ProcessId == processId)
                     .ToArray())
        {
            this.harvestableProcessStates.Remove(key);
        }
    }

    private readonly record struct HarvestableProcessKey(
        int ProcessId,
        string SectorId);

    private sealed class HarvestableProcessState
    {
        public HarvestableProcessState(ObservedHarvestableContext context)
        {
            this.Refresh(context);
        }

        public string LivePilotName { get; private set; } = "";

        public string SectorId { get; private set; } = "";

        public string SectorKey { get; private set; } = "";

        public string SectorName { get; private set; } = "";

        public string SystemName { get; private set; } = "";

        public uint ActiveSectorNumber { get; private set; }

        public Dictionary<string, HarvestableTrack> Pending { get; } =
            new(StringComparer.Ordinal);

        public DateTimeOffset? FirstPendingAtUtc { get; set; }

        public DateTimeOffset NextAttemptAllowedAt { get; set; } =
            DateTimeOffset.MinValue;

        public string? InFlightKey { get; set; }

        public bool IsRetired { get; set; }

        public void Refresh(ObservedHarvestableContext context)
        {
            this.LivePilotName = context.LivePilotName;
            this.SectorId = context.SectorId;
            this.SectorKey = context.SectorKey;
            this.SectorName = context.SectorName;
            this.SystemName = context.SystemName;
            this.ActiveSectorNumber = context.ActiveSectorNumber;
        }
    }

    private sealed class HarvestableTrack
    {
        private readonly HashSet<int> itemTemplateIds = [];

        public HarvestableTrack(
            string stableKey,
            ObservedHarvestableSample first)
        {
            this.StableKey = stableKey;
            this.ObjectId = first.ObjectId;
            this.Name = first.Name;
            this.RawObjectType = first.RawObjectType;
            this.TechLevel = first.TechLevel;
            this.X = first.X;
            this.Y = first.Y;
            this.Z = first.Z;
            this.FirstObservedAtUtc = first.ObservedAtUtc;
            this.LastObservedAtUtc = first.ObservedAtUtc;
            this.ObservationCount = 1;
            this.itemTemplateIds.UnionWith(first.ItemTemplateIds);
        }

        public string StableKey { get; }

        public uint ObjectId { get; }

        public string Name { get; }

        public byte RawObjectType { get; }

        public int TechLevel { get; }

        public float X { get; }

        public float Y { get; }

        public float Z { get; }

        public DateTimeOffset FirstObservedAtUtc { get; }

        public DateTimeOffset LastObservedAtUtc { get; private set; }

        public int ObservationCount { get; private set; }

        public void Observe(ObservedHarvestableSample sample)
        {
            this.LastObservedAtUtc = sample.ObservedAtUtc;
            this.ObservationCount = checked(this.ObservationCount + 1);
            this.itemTemplateIds.UnionWith(sample.ItemTemplateIds);
        }

        public ObservedHarvestableTrack Snapshot()
        {
            var items = this.itemTemplateIds.Order().ToArray();
            var fingerprint = string.Create(
                CultureInfo.InvariantCulture,
                $"{this.StableKey}|{this.FirstObservedAtUtc:O}|{this.LastObservedAtUtc:O}|{this.ObservationCount}|{string.Join(',', items)}");
            return new ObservedHarvestableTrack(
                this.StableKey,
                this.ObjectId,
                this.Name,
                this.RawObjectType,
                this.TechLevel,
                this.X,
                this.Y,
                this.Z,
                this.FirstObservedAtUtc,
                this.LastObservedAtUtc,
                this.ObservationCount,
                items,
                fingerprint);
        }
    }

    private sealed record ObservedHarvestableContext(
        string LivePilotName,
        string SectorId,
        string SectorKey,
        string SectorName,
        string SystemName,
        uint ActiveSectorNumber);

    private sealed record ObservedHarvestableSample(
        uint ObjectId,
        string Name,
        byte RawObjectType,
        int TechLevel,
        float X,
        float Y,
        float Z,
        DateTimeOffset ObservedAtUtc,
        IReadOnlyList<int> ItemTemplateIds);

    private sealed record ObservedHarvestableTrack(
        string StableKey,
        uint ObjectId,
        string Name,
        byte RawObjectType,
        int TechLevel,
        float X,
        float Y,
        float Z,
        DateTimeOffset FirstObservedAtUtc,
        DateTimeOffset LastObservedAtUtc,
        int ObservationCount,
        IReadOnlyList<int> ItemTemplateIds,
        string Fingerprint);

    private sealed record ObservedHarvestableBatch(
        HarvestableProcessKey StateKey,
        string LivePilotName,
        string SectorId,
        string SectorKey,
        string SectorName,
        string SystemName,
        uint ActiveSectorNumber,
        DateTimeOffset FirstObservedAtUtc,
        DateTimeOffset LastObservedAtUtc,
        IReadOnlyList<ObservedHarvestableTrack> Tracks);

    private sealed record PreparedHarvestableSubmission(
        ObservedHarvestableBatch Batch,
        string SubmissionKey,
        CancellationToken ParticipationToken);
}
