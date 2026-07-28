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
    private const float MobTrackMaximumRadius = 5000.0f;
    private static readonly TimeSpan MobTrackMaximumAge =
        TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MobBatchSettleTime =
        TimeSpan.FromSeconds(30);

    private readonly Dictionary<MobProcessKey, MobProcessState> mobProcessStates = [];
    private readonly HashSet<string> inFlightMobBatchKeys =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> sessionObservedMobSightingKeys =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> lifetimeObservedMobSightingKeys;

    private void ObserveMobSightings(ClientObservationSnapshot snapshot)
    {
        List<PreparedMobSubmission> submissions = [];
        var observationsChanged = false;

        lock (this.stateLock)
        {
            if (!this.TryCreateMobFrame(
                    snapshot,
                    out var frame,
                    out var unavailableReason))
            {
                foreach (var pair in this.mobProcessStates
                             .Where(pair => pair.Key.ProcessId == snapshot.ProcessId)
                             .ToArray())
                {
                    pair.Value.IsRetired = true;
                    observationsChanged |= this.FinalizeActiveMobTracks(pair.Value);

                    if (this.TryPrepareMobSubmission(
                            pair.Key,
                            pair.Value,
                            snapshot.ObservedAt,
                            force: true,
                            out var submission))
                    {
                        submissions.Add(submission);
                    }
                    else if (pair.Value.ActiveTracks.Count == 0 &&
                             pair.Value.Pending.Count == 0 &&
                             pair.Value.InFlightKey == null)
                    {
                        this.mobProcessStates.Remove(pair.Key);
                    }
                }

                if (!string.IsNullOrWhiteSpace(unavailableReason))
                {
                    this.status = unavailableReason;
                }
            }
            else
            {
                var currentKey = new MobProcessKey(
                    snapshot.ProcessId,
                    frame.SectorId);

                foreach (var pair in this.mobProcessStates
                             .Where(pair =>
                                 pair.Key.ProcessId == snapshot.ProcessId &&
                                 !pair.Key.Equals(currentKey))
                             .ToArray())
                {
                    pair.Value.IsRetired = true;
                    observationsChanged |= this.FinalizeActiveMobTracks(pair.Value);

                    if (this.TryPrepareMobSubmission(
                            pair.Key,
                            pair.Value,
                            snapshot.ObservedAt,
                            force: true,
                            out var submission))
                    {
                        submissions.Add(submission);
                    }
                    else if (pair.Value.ActiveTracks.Count == 0 &&
                             pair.Value.Pending.Count == 0 &&
                             pair.Value.InFlightKey == null)
                    {
                        this.mobProcessStates.Remove(pair.Key);
                    }
                }

                if (!this.mobProcessStates.TryGetValue(currentKey, out var state))
                {
                    state = new MobProcessState(frame);
                    this.mobProcessStates[currentKey] = state;
                }
                else
                {
                    state.Refresh(frame);
                    state.IsRetired = false;
                }

                var currentObjectIds = frame.Samples
                    .Select(sample => sample.ObjectId)
                    .ToHashSet();

                foreach (var active in state.ActiveTracks.Values.ToArray())
                {
                    if (currentObjectIds.Contains(active.ObjectId))
                    {
                        continue;
                    }

                    state.ActiveTracks.Remove(active.ObjectId);
                    observationsChanged |= this.QueueCompletedMobTrack(
                        state,
                        active.Complete());
                }

                foreach (var sample in frame.Samples)
                {
                    if (!state.ActiveTracks.TryGetValue(
                            sample.ObjectId,
                            out var track) ||
                        !track.HasSameIdentity(sample))
                    {
                        if (track != null)
                        {
                            observationsChanged |= this.QueueCompletedMobTrack(
                                state,
                                track.Complete());
                        }

                        state.ActiveTracks[sample.ObjectId] = new MobTrack(sample);
                        continue;
                    }

                    track.Observe(sample);

                    if (track.Age < MobTrackMaximumAge &&
                        track.Radius < MobTrackMaximumRadius)
                    {
                        continue;
                    }

                    observationsChanged |= this.QueueCompletedMobTrack(
                        state,
                        track.Complete());
                    state.ActiveTracks[sample.ObjectId] = new MobTrack(sample);
                }

                if (this.TryPrepareMobSubmission(
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
            var task = this.SubmitMobSightingsAsync(
                submission.Batch,
                submission.SubmissionKey,
                submissionCancellation);
            this.Track(task);
        }
    }

    private bool FinalizeActiveMobTracks(MobProcessState state)
    {
        var changed = false;

        foreach (var track in state.ActiveTracks.Values)
        {
            changed |= this.QueueCompletedMobTrack(state, track.Complete());
        }

        state.ActiveTracks.Clear();
        return changed;
    }

    private bool TryPrepareMobSubmission(
        MobProcessKey stateKey,
        MobProcessState state,
        DateTimeOffset observedAtUtc,
        bool force,
        out PreparedMobSubmission submission)
    {
        submission = null!;
        var canAttempt = state.Pending.Count != 0 &&
            state.InFlightKey == null &&
            observedAtUtc >= state.NextAttemptAllowedAt;
        var isSettled = force ||
            state.Pending.Count >= 64 ||
            state.FirstPendingAtUtc is not { } firstPending ||
            observedAtUtc - firstPending >= MobBatchSettleTime;

        if (!canAttempt || !isSettled)
        {
            return false;
        }

        var tracks = state.Pending.Values
            .OrderBy(track => track.FirstObservedAtUtc)
            .ThenBy(track => track.ObjectId)
            .Take(512)
            .ToArray();
        var batch = new ObservedMobBatch(
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
        var submissionKey = CreateMobSubmissionKey(batch);

        if (!this.inFlightMobBatchKeys.Add(submissionKey))
        {
            return false;
        }

        state.InFlightKey = submissionKey;
        this.status = "Sharing mob observations with Forge.";
        submission = new PreparedMobSubmission(
            batch,
            submissionKey,
            this.participationCancellation.Token);
        return true;
    }

    private bool QueueCompletedMobTrack(
        MobProcessState state,
        ObservedMobTrack track)
    {
        state.Pending.TryAdd(track.Fingerprint, track);
        state.FirstPendingAtUtc ??= track.FirstObservedAtUtc;
        return this.RecordObservedMobSighting(state.SectorId, track);
    }

    private async Task SubmitMobSightingsAsync(
        ObservedMobBatch batch,
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
            var unsignedRequest = new ForgeMobSightingsContributionRequest
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
                Sightings =
                [
                    .. batch.Tracks.Select(track =>
                        new ForgeMobSightingContributionItem
                        {
                            ObjectId = track.ObjectId,
                            Name = track.Name,
                            RawObjectType = track.RawObjectType,
                            CombatLevel = track.CombatLevel,
                            FactionIdentifier = track.FactionIdentifier,
                            FactionBindingKind = track.FactionBindingKind,
                            IntrinsicRelationshipRaw =
                                track.IntrinsicRelationshipRaw,
                            IntrinsicDisposition =
                                track.IntrinsicDisposition,
                            ObservedRelationshipRaw =
                                track.ObservedRelationshipRaw,
                            ObservedAggressionRaw =
                                track.ObservedAggressionRaw,
                            ObservedActivelyAggressive =
                                track.ObservedActivelyAggressive,
                            IsOrganic = track.IsOrganic,
                            AutoLevel = track.AutoLevel,
                            X = track.X,
                            Y = track.Y,
                            Z = track.Z,
                            FirstObservedAtUtc = track.FirstObservedAtUtc,
                            LastObservedAtUtc = track.LastObservedAtUtc,
                            ObservationCount = track.ObservationCount,
                            Radius = track.Radius,
                        }),
                ],
            };
            var request = unsignedRequest with
            {
                Signature = identity.Sign(unsignedRequest),
            };
            var response = await this.client.SubmitMobSightingsAsync(
                    request,
                    cancellationToken)
                .ConfigureAwait(false);

            lock (this.stateLock)
            {
                if (this.mobProcessStates.TryGetValue(
                        batch.StateKey,
                        out var state))
                {
                    foreach (var track in batch.Tracks)
                    {
                        state.Pending.Remove(track.Fingerprint);
                    }

                    state.FirstPendingAtUtc = state.Pending.Count == 0
                        ? null
                        : state.Pending.Values.Min(track => track.FirstObservedAtUtc);
                    state.NextAttemptAllowedAt = DateTimeOffset.MinValue;

                    if (state.IsRetired &&
                        state.ActiveTracks.Count == 0 &&
                        state.Pending.Count == 0)
                    {
                        this.mobProcessStates.Remove(batch.StateKey);
                    }
                }

                this.session.MobSightingFactsSubmitted += response.Received;
                this.session.MobSightingEvidenceAccepted += response.EvidenceAccepted;
                this.session.MobVariantsCreated += response.VariantsCreated;
                this.session.MobClustersCreated += response.ClustersCreated;
                this.session.MobClustersUpdated += response.ClustersUpdated;
                this.session.SuccessfulBatches++;
                this.session.LastSuccessfulContributionUtc = DateTimeOffset.UtcNow;

                var lifetime = this.settings.Lifetime;
                lifetime.MobSightingFactsSubmitted += response.Received;
                lifetime.MobSightingEvidenceAccepted += response.EvidenceAccepted;
                lifetime.MobVariantsCreated += response.VariantsCreated;
                lifetime.MobClustersCreated += response.ClustersCreated;
                lifetime.MobClustersUpdated += response.ClustersUpdated;
                lifetime.SuccessfulBatches++;
                lifetime.LastSuccessfulContributionUtc = DateTimeOffset.UtcNow;
                this.status = "Mob observations shared with Forge.";
            }

            this.saveSettings();
            this.RaiseStatisticsChanged();
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
                    "Mob observations could not be shared. They will retry automatically.";

                if (this.mobProcessStates.TryGetValue(
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
                this.inFlightMobBatchKeys.Remove(submissionKey);

                if (this.mobProcessStates.TryGetValue(
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

    private bool TryCreateMobFrame(
        ClientObservationSnapshot snapshot,
        out ObservedMobFrame frame,
        out string unavailableReason)
    {
        frame = null!;
        unavailableReason = "";

        if (snapshot.LifecycleState != ClientLifecycleState.InGame ||
            snapshot.LoadingOrTransitionFlag != 0 ||
            !snapshot.World.IsAvailable ||
            snapshot.World.Environment != ClientWorldEnvironment.Space ||
            !snapshot.NearbyTargets.IsAvailable ||
            snapshot.World.ActiveSectorNumber == 0 ||
            snapshot.NearbyTargets.ActiveSectorNumber !=
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
            unavailableReason =
                "Mob contribution is waiting for the latest Forge data.";
            return false;
        }

        var samples = snapshot.NearbyTargets.Targets
            .Where(target =>
                target.IsAvailable &&
                target.ObjectId != 0 &&
                target.RawObjectType is 0 or 2 or 35 &&
                target.Mob is
                {
                    IsAvailable: true,
                    CombatLevel: not null,
                    IsOrganic: not null,
                    Spatial.IsAvailable: true,
                })
            .Select(target => CreateObservedMobSample(
                snapshot.ObservedAt,
                target,
                ResolveTargetFactionIdentifierOverride(snapshot, target.ObjectId)))
            .Where(sample => sample != null)
            .Cast<ObservedMobSample>()
            .ToArray();

        frame = new ObservedMobFrame(
            identity.Name,
            sector.Id,
            sector.Key,
            sector.Name,
            sector.SystemName,
            snapshot.World.ActiveSectorNumber,
            samples);
        return true;
    }

    private static ObservedMobSample? CreateObservedMobSample(
        DateTimeOffset observedAtUtc,
        ClientGutterRadarTargetObservation target,
        string? factionIdentifierOverride)
    {
        var mob = target.Mob;
        var name = ForgeMobNameCanonicalizer.Canonicalize(mob.Name);

        if (string.IsNullOrWhiteSpace(name) ||
            IsGuardianTurret(name) ||
            mob.CombatLevel is not { } combatLevel ||
            mob.IsOrganic is not { } isOrganic ||
            !float.IsFinite(mob.Spatial.Position.X) ||
            !float.IsFinite(mob.Spatial.Position.Y) ||
            !float.IsFinite(mob.Spatial.Position.Z))
        {
            return null;
        }

        var observedFactionIdentifier = string.IsNullOrWhiteSpace(
                factionIdentifierOverride)
            ? mob.FactionIdentifier
            : factionIdentifierOverride;
        var factionBindingKind =
            ResolveMobFactionBindingKind(observedFactionIdentifier);

        if (factionBindingKind == EncounterFactionBindingKind.Unknown)
        {
            return null;
        }

        var intrinsicRelationshipRaw =
            ResolveIntrinsicRelationshipRaw(
                factionBindingKind,
                target.Relationship);
        var intrinsicDisposition =
            ResolveIntrinsicDisposition(
                factionBindingKind,
                target.Relationship);

        return new ObservedMobSample(
            target.ObjectId,
            name,
            target.RawObjectType,
            combatLevel,
            NormalizeMobFaction(observedFactionIdentifier),
            factionBindingKind,
            intrinsicRelationshipRaw,
            intrinsicDisposition,
            target.Relationship.RelationshipRaw >= 0
                ? target.Relationship.RelationshipRaw
                : null,
            target.Relationship.RelationshipRaw >= 0
                ? target.Relationship.AggressionRaw
                : null,
            target.Relationship.RelationshipRaw >= 0
                ? target.Relationship.IsActivelyAggressive
                : null,
            isOrganic,
            mob.AutoLevel,
            mob.Spatial.Position.X,
            mob.Spatial.Position.Y,
            mob.Spatial.Position.Z,
            observedAtUtc);
    }

    private static string? ResolveTargetFactionIdentifierOverride(
        ClientObservationSnapshot snapshot,
        uint objectId)
    {
        if (!snapshot.Target.IsAvailable ||
            !snapshot.Target.HasTarget ||
            snapshot.Target.ObjectId != objectId)
        {
            return null;
        }

        var factionIdentifier =
            snapshot.Target.Operational.Identity.FactionIdentifier;
        return string.IsNullOrWhiteSpace(factionIdentifier)
            ? null
            : factionIdentifier.Trim();
    }

    private bool RecordObservedMobSighting(
        string sectorId,
        ObservedMobTrack track)
    {
        var key = string.Create(
            CultureInfo.InvariantCulture,
            $"{sectorId}|{track.RawObjectType}|{NormalizeMobIdentityValue(track.Name)}|{track.CombatLevel}|{(track.IsOrganic ? 1 : 0)}|{NormalizeMobIdentityValue(track.FactionIdentifier)}|{(int)track.FactionBindingKind}|{track.IntrinsicRelationshipRaw?.ToString(CultureInfo.InvariantCulture) ?? ""}|{MathF.Round(track.X / 1000.0f):R}|{MathF.Round(track.Y / 1000.0f):R}|{MathF.Round(track.Z / 1000.0f):R}");
        var changed = false;

        if (this.sessionObservedMobSightingKeys.Add(key))
        {
            this.session.MobSightingsObserved =
                this.sessionObservedMobSightingKeys.Count;
            changed = true;
        }

        if (this.lifetimeObservedMobSightingKeys.Add(key))
        {
            this.settings.Lifetime.ObservedMobSightingKeys =
            [
                .. this.lifetimeObservedMobSightingKeys
                    .Order(StringComparer.Ordinal),
            ];
            this.settings.Lifetime.MobSightingsObserved =
                this.lifetimeObservedMobSightingKeys.Count;
            changed = true;
        }

        return changed;
    }

    private static string CreateMobSubmissionKey(ObservedMobBatch batch)
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

    private void RemoveMobProcessStates(int processId)
    {
        foreach (var key in this.mobProcessStates.Keys
                     .Where(key => key.ProcessId == processId)
                     .ToArray())
        {
            this.mobProcessStates.Remove(key);
        }
    }

    private static EncounterFactionBindingKind ResolveMobFactionBindingKind(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return EncounterFactionBindingKind.Unknown;
        }

        return string.Equals(
            value.Trim(),
            "None",
            StringComparison.OrdinalIgnoreCase)
            ? EncounterFactionBindingKind.Unaffiliated
            : EncounterFactionBindingKind.FactionLinked;
    }

    private static int? ResolveIntrinsicRelationshipRaw(
        EncounterFactionBindingKind factionBindingKind,
        ClientObjectRelationshipObservation relationship)
    {
        if (factionBindingKind != EncounterFactionBindingKind.Unaffiliated)
        {
            return null;
        }

        return relationship.RelationshipRaw is 0 or 1
            ? relationship.RelationshipRaw
            : null;
    }

    private static EncounterResolvedDisposition? ResolveIntrinsicDisposition(
        EncounterFactionBindingKind factionBindingKind,
        ClientObjectRelationshipObservation relationship)
    {
        if (ResolveIntrinsicRelationshipRaw(
                factionBindingKind,
                relationship) == null)
        {
            return null;
        }

        return ToEncounterResolvedDisposition(relationship.Disposition);
    }

    private static EncounterResolvedDisposition ToEncounterResolvedDisposition(
        ClientResolvedDisposition disposition)
    {
        return disposition switch
        {
            ClientResolvedDisposition.Hostile =>
                EncounterResolvedDisposition.Hostile,
            ClientResolvedDisposition.Neutral =>
                EncounterResolvedDisposition.Neutral,
            ClientResolvedDisposition.Friendly =>
                EncounterResolvedDisposition.Friendly,
            _ => EncounterResolvedDisposition.Unknown,
        };
    }

    private static string NormalizeMobFaction(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            string.Equals(value.Trim(), "None", StringComparison.OrdinalIgnoreCase))
        {
            return "";
        }

        return value.Trim();
    }

    private static string NormalizeMobIdentityValue(string value)
    {
        return string.Concat(value.Where(char.IsLetterOrDigit)).ToUpperInvariant();
    }

    private static bool IsGuardianTurret(string name)
    {
        var normalized = NormalizeMobIdentityValue(name);
        return string.Equals(normalized, "GATEGUARDIANTURRET", StringComparison.Ordinal) ||
               string.Equals(normalized, "STARBASEGUARDIANTURRET", StringComparison.Ordinal);
    }

    private readonly record struct MobProcessKey(
        int ProcessId,
        string SectorId);

    private sealed class MobProcessState
    {
        public MobProcessState(ObservedMobFrame frame)
        {
            this.Refresh(frame);
        }

        public string LivePilotName { get; private set; } = "";

        public string SectorId { get; private set; } = "";

        public string SectorKey { get; private set; } = "";

        public string SectorName { get; private set; } = "";

        public string SystemName { get; private set; } = "";

        public uint ActiveSectorNumber { get; private set; }

        public Dictionary<uint, MobTrack> ActiveTracks { get; } = [];

        public Dictionary<string, ObservedMobTrack> Pending { get; } =
            new(StringComparer.Ordinal);

        public DateTimeOffset? FirstPendingAtUtc { get; set; }

        public DateTimeOffset NextAttemptAllowedAt { get; set; } =
            DateTimeOffset.MinValue;

        public string? InFlightKey { get; set; }

        public bool IsRetired { get; set; }

        public void Refresh(ObservedMobFrame frame)
        {
            this.LivePilotName = frame.LivePilotName;
            this.SectorId = frame.SectorId;
            this.SectorKey = frame.SectorKey;
            this.SectorName = frame.SectorName;
            this.SystemName = frame.SystemName;
            this.ActiveSectorNumber = frame.ActiveSectorNumber;
        }
    }

    private sealed class MobTrack
    {
        private float minX;
        private float minY;
        private float minZ;
        private float maxX;
        private float maxY;
        private float maxZ;

        public MobTrack(ObservedMobSample first)
        {
            this.ObjectId = first.ObjectId;
            this.Name = first.Name;
            this.RawObjectType = first.RawObjectType;
            this.CombatLevel = first.CombatLevel;
            this.FactionIdentifier = first.FactionIdentifier;
            this.FactionBindingKind = first.FactionBindingKind;
            this.IntrinsicRelationshipRaw = first.IntrinsicRelationshipRaw;
            this.IntrinsicDisposition = first.IntrinsicDisposition;
            this.ObservedRelationshipRaw = first.ObservedRelationshipRaw;
            this.ObservedAggressionRaw = first.ObservedAggressionRaw;
            this.ObservedActivelyAggressive = first.ObservedActivelyAggressive;
            this.IsOrganic = first.IsOrganic;
            this.AutoLevel = first.AutoLevel;
            this.FirstObservedAtUtc = first.ObservedAtUtc;
            this.LastObservedAtUtc = first.ObservedAtUtc;
            this.minX = this.maxX = first.X;
            this.minY = this.maxY = first.Y;
            this.minZ = this.maxZ = first.Z;
            this.ObservationCount = 1;
        }

        public uint ObjectId { get; }

        public string Name { get; }

        public byte RawObjectType { get; }

        public int CombatLevel { get; }

        public string FactionIdentifier { get; }

        public EncounterFactionBindingKind FactionBindingKind { get; }

        public int? IntrinsicRelationshipRaw { get; }

        public EncounterResolvedDisposition? IntrinsicDisposition { get; }

        public int? ObservedRelationshipRaw { get; }

        public int? ObservedAggressionRaw { get; }

        public bool? ObservedActivelyAggressive { get; }

        public bool IsOrganic { get; }

        public bool? AutoLevel { get; }

        public DateTimeOffset FirstObservedAtUtc { get; }

        public DateTimeOffset LastObservedAtUtc { get; private set; }

        public int ObservationCount { get; private set; }

        public TimeSpan Age => this.LastObservedAtUtc - this.FirstObservedAtUtc;

        public float Radius => CalculateRadius(
            this.minX,
            this.minY,
            this.minZ,
            this.maxX,
            this.maxY,
            this.maxZ);

        public bool HasSameIdentity(ObservedMobSample sample)
        {
            return this.RawObjectType == sample.RawObjectType &&
                this.CombatLevel == sample.CombatLevel &&
                this.IsOrganic == sample.IsOrganic &&
                string.Equals(
                    NormalizeMobIdentityValue(this.Name),
                    NormalizeMobIdentityValue(sample.Name),
                    StringComparison.Ordinal) &&
                string.Equals(
                    NormalizeMobIdentityValue(this.FactionIdentifier),
                    NormalizeMobIdentityValue(sample.FactionIdentifier),
                    StringComparison.Ordinal) &&
                this.FactionBindingKind == sample.FactionBindingKind &&
                this.IntrinsicRelationshipRaw ==
                    sample.IntrinsicRelationshipRaw;
        }

        public void Observe(ObservedMobSample sample)
        {
            this.LastObservedAtUtc = sample.ObservedAtUtc;
            this.ObservationCount = checked(this.ObservationCount + 1);
            this.minX = Math.Min(this.minX, sample.X);
            this.minY = Math.Min(this.minY, sample.Y);
            this.minZ = Math.Min(this.minZ, sample.Z);
            this.maxX = Math.Max(this.maxX, sample.X);
            this.maxY = Math.Max(this.maxY, sample.Y);
            this.maxZ = Math.Max(this.maxZ, sample.Z);
        }

        public ObservedMobTrack Complete()
        {
            var x = (this.minX + this.maxX) / 2.0f;
            var y = (this.minY + this.maxY) / 2.0f;
            var z = (this.minZ + this.maxZ) / 2.0f;
            var radius = this.Radius;
            var fingerprint = string.Create(
                CultureInfo.InvariantCulture,
                $"{this.ObjectId}|{this.FirstObservedAtUtc:O}|{this.LastObservedAtUtc:O}|{this.RawObjectType}|{NormalizeMobIdentityValue(this.Name)}|{this.CombatLevel}|{(this.IsOrganic ? 1 : 0)}|{NormalizeMobIdentityValue(this.FactionIdentifier)}|{(int)this.FactionBindingKind}|{this.IntrinsicRelationshipRaw?.ToString(CultureInfo.InvariantCulture) ?? ""}|{x:R}|{y:R}|{z:R}|{radius:R}|{this.ObservationCount}");
            return new ObservedMobTrack(
                this.ObjectId,
                this.Name,
                this.RawObjectType,
                this.CombatLevel,
                this.FactionIdentifier,
                this.FactionBindingKind,
                this.IntrinsicRelationshipRaw,
                this.IntrinsicDisposition,
                this.ObservedRelationshipRaw,
                this.ObservedAggressionRaw,
                this.ObservedActivelyAggressive,
                this.IsOrganic,
                this.AutoLevel,
                x,
                y,
                z,
                radius,
                this.ObservationCount,
                this.FirstObservedAtUtc,
                this.LastObservedAtUtc,
                fingerprint);
        }

        private static float CalculateRadius(
            float minX,
            float minY,
            float minZ,
            float maxX,
            float maxY,
            float maxZ)
        {
            var x = (maxX - minX) / 2.0f;
            var y = (maxY - minY) / 2.0f;
            var z = (maxZ - minZ) / 2.0f;
            return MathF.Sqrt((x * x) + (y * y) + (z * z));
        }
    }

    private sealed record ObservedMobFrame(
        string LivePilotName,
        string SectorId,
        string SectorKey,
        string SectorName,
        string SystemName,
        uint ActiveSectorNumber,
        IReadOnlyList<ObservedMobSample> Samples);

    private sealed record PreparedMobSubmission(
        ObservedMobBatch Batch,
        string SubmissionKey,
        CancellationToken ParticipationToken);

    private sealed record ObservedMobBatch(
        MobProcessKey StateKey,
        string LivePilotName,
        string SectorId,
        string SectorKey,
        string SectorName,
        string SystemName,
        uint ActiveSectorNumber,
        DateTimeOffset FirstObservedAtUtc,
        DateTimeOffset LastObservedAtUtc,
        IReadOnlyList<ObservedMobTrack> Tracks);

    private sealed record ObservedMobSample(
        uint ObjectId,
        string Name,
        byte RawObjectType,
        int CombatLevel,
        string FactionIdentifier,
        EncounterFactionBindingKind FactionBindingKind,
        int? IntrinsicRelationshipRaw,
        EncounterResolvedDisposition? IntrinsicDisposition,
        int? ObservedRelationshipRaw,
        int? ObservedAggressionRaw,
        bool? ObservedActivelyAggressive,
        bool IsOrganic,
        bool? AutoLevel,
        float X,
        float Y,
        float Z,
        DateTimeOffset ObservedAtUtc);

    private sealed record ObservedMobTrack(
        uint ObjectId,
        string Name,
        byte RawObjectType,
        int CombatLevel,
        string FactionIdentifier,
        EncounterFactionBindingKind FactionBindingKind,
        int? IntrinsicRelationshipRaw,
        EncounterResolvedDisposition? IntrinsicDisposition,
        int? ObservedRelationshipRaw,
        int? ObservedAggressionRaw,
        bool? ObservedActivelyAggressive,
        bool IsOrganic,
        bool? AutoLevel,
        float X,
        float Y,
        float Z,
        float Radius,
        int ObservationCount,
        DateTimeOffset FirstObservedAtUtc,
        DateTimeOffset LastObservedAtUtc,
        string Fingerprint);
}
