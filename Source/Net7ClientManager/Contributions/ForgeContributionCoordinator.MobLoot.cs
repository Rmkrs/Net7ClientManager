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
    private static readonly TimeSpan MobLootBatchSettleTime =
        TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MobLootCorpseSettleTime =
        TimeSpan.FromSeconds(1);
    // Probe captures placed genuine mob-to-corpse transitions within 488 ms
    // and 116 world units. The production census runs once per second, so
    // these limits include polling margin while remaining deliberately tight.
    private static readonly TimeSpan MobLootLineageRetentionTime =
        TimeSpan.FromSeconds(3);
    private static readonly TimeSpan MobLootMissingCorpseRetentionTime =
        TimeSpan.FromSeconds(10);
    private const double MobLootMaximumLineageDistance = 500;

    private readonly Dictionary<int, MobLootProcessState> mobLootProcessStates = [];
    private readonly HashSet<string> inFlightMobLootBatchKeys =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> acceptedMobLootRelationshipIds =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> sessionObservedMobLootRelationshipKeys =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> lifetimeObservedMobLootRelationshipKeys;

    private void ObserveMobLoot(ClientObservationSnapshot snapshot)
    {
        if (!this.TryResolveMobLootContext(
                snapshot,
                out var context,
                out var unavailableReason))
        {
            lock (this.stateLock)
            {
                this.mobLootProcessStates.Remove(snapshot.ProcessId);

                if (!string.IsNullOrWhiteSpace(unavailableReason))
                {
                    this.status = unavailableReason;
                }
            }

            return;
        }

        ObservedMobLootBatch? batch = null;
        string? submissionKey = null;
        CancellationToken participationToken = default;
        var observationsChanged = false;

        lock (this.stateLock)
        {
            if (!this.mobLootProcessStates.TryGetValue(
                    snapshot.ProcessId,
                    out var state) ||
                !string.Equals(
                    state.SectorId,
                    context.SectorId,
                    StringComparison.Ordinal))
            {
                state = new MobLootProcessState(context.SectorId);
                this.mobLootProcessStates[snapshot.ProcessId] = state;
            }

            this.ObserveMobLootLineageAndCargo(
                state,
                context,
                snapshot,
                ref observationsChanged);

            var canAttempt = state.Pending.Count != 0 &&
                state.InFlightKey == null &&
                snapshot.ObservedAt >= state.NextAttemptAllowedAt;
            var isSettled = state.Pending.Count >= 32 ||
                state.FirstPendingAtUtc is not { } firstPending ||
                snapshot.ObservedAt - firstPending >= MobLootBatchSettleTime;

            if (canAttempt && isSettled)
            {
                var facts = state.Pending.Values
                    .OrderBy(fact => fact.RelationshipId, StringComparer.Ordinal)
                    .Take(256)
                    .ToArray();
                var candidate = new ObservedMobLootBatch(
                    snapshot.ProcessId,
                    context.LivePilotName,
                    facts.Min(fact => fact.ObservedAtUtc),
                    facts.Max(fact => fact.ObservedAtUtc),
                    facts);
                var candidateKey = CreateMobLootSubmissionKey(candidate);

                if (this.inFlightMobLootBatchKeys.Add(candidateKey))
                {
                    batch = candidate;
                    submissionKey = candidateKey;
                    state.InFlightKey = candidateKey;
                    participationToken = this.participationCancellation.Token;
                    this.status = "Sharing mob loot with Forge.";
                }
            }
        }

        if (observationsChanged)
        {
            this.saveSettings();
        }

        this.RaiseStatisticsChanged();

        if (batch == null || submissionKey == null)
        {
            return;
        }

        var submissionCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                this.cancellation.Token,
                participationToken);
        var task = this.SubmitMobLootAsync(
            batch,
            submissionKey,
            submissionCancellation);
        this.Track(task);
    }

    private void ObserveMobLootLineageAndCargo(
        MobLootProcessState state,
        MobLootContext context,
        ClientObservationSnapshot snapshot,
        ref bool observationsChanged)
    {
        var targets = snapshot.NearbyTargets.Targets
            .Where(target => target is
            {
                IsAvailable: true,
                ObjectId: not 0,
            })
            .ToArray();
        var currentRawMobIds = targets
            .Where(target => target.RawObjectType is 0 or 2 or 35)
            .Select(target => target.ObjectId)
            .ToHashSet();

        foreach (var existing in state.LiveMobs.Values.ToArray())
        {
            if (currentRawMobIds.Contains(existing.ObjectId))
            {
                continue;
            }

            state.LiveMobs.Remove(existing.ObjectId);
            state.RecentlyDisappearedMobs.Add(
                new RecentlyDisappearedLootMob(
                    existing,
                    snapshot.ObservedAt));
        }

        foreach (var target in targets.Where(candidate =>
                     candidate.RawObjectType is 0 or 2 or 35))
        {
            var mob = TryCreateNearbyLootMob(target);

            if (mob != null)
            {
                state.RecentlyDisappearedMobs.RemoveAll(candidate =>
                    candidate.Mob.ObjectId == mob.ObjectId);
                state.LiveMobs[target.ObjectId] = mob;
            }
        }

        state.RecentlyDisappearedMobs.RemoveAll(candidate =>
            currentRawMobIds.Contains(candidate.Mob.ObjectId) ||
            snapshot.ObservedAt - candidate.DisappearedAtUtc >
                MobLootLineageRetentionTime);

        var corpses = targets
            .Where(target => target.Kind == ClientNearbyTargetKind.Corpse)
            .ToArray();
        var currentCorpseKeys = corpses
            .Select(CreateCorpseInstanceKey)
            .ToHashSet();

        if (state.HasObservedCorpseBaseline)
        {
            foreach (var newCorpseKey in currentCorpseKeys.Where(key =>
                         !state.LiveCorpseKeys.Contains(key)))
            {
                state.UnmatchedCorpses[newCorpseKey] = snapshot.ObservedAt;
            }
        }
        else
        {
            state.HasObservedCorpseBaseline = true;
        }

        state.LiveCorpseKeys.Clear();
        state.LiveCorpseKeys.UnionWith(currentCorpseKeys);

        foreach (var staleKey in state.UnmatchedCorpses
                     .Where(entry =>
                         !currentCorpseKeys.Contains(entry.Key) ||
                         snapshot.ObservedAt - entry.Value >
                             MobLootLineageRetentionTime)
                     .Select(entry => entry.Key)
                     .ToArray())
        {
            state.UnmatchedCorpses.Remove(staleKey);
        }

        foreach (var staleKey in state.PendingCorpses
                     .Where(entry =>
                         !currentCorpseKeys.Contains(entry.Key) &&
                         snapshot.ObservedAt - entry.Value.LastSeenAtUtc >
                             MobLootMissingCorpseRetentionTime)
                     .Select(entry => entry.Key)
                     .ToArray())
        {
            state.PendingCorpses.Remove(staleKey);
        }

        foreach (var corpseTarget in corpses)
        {
            var corpseKey = CreateCorpseInstanceKey(corpseTarget);

            if (state.ProcessedCorpses.Contains(corpseKey))
            {
                continue;
            }

            if (!state.PendingCorpses.TryGetValue(
                    corpseKey,
                    out var pending))
            {
                if (!state.UnmatchedCorpses.TryGetValue(
                        corpseKey,
                        out var firstSeenAtUtc))
                {
                    continue;
                }

                var matchedPending = TryMatchMobLootCorpse(
                    state,
                    corpses,
                    corpseTarget,
                    firstSeenAtUtc);

                if (matchedPending == null)
                {
                    continue;
                }

                pending = matchedPending;
                state.UnmatchedCorpses.Remove(corpseKey);
                state.PendingCorpses[corpseKey] = pending;
            }

            state.UnmatchedCorpses.Remove(corpseKey);
            pending.LastSeenAtUtc = snapshot.ObservedAt;
            this.ObservePendingMobLootCorpse(
                state,
                context,
                corpseKey,
                corpseTarget,
                pending,
                snapshot,
                snapshot.ObservedAt,
                ref observationsChanged);
        }
    }

    private static PendingMobLootCorpse? TryMatchMobLootCorpse(
        MobLootProcessState state,
        IReadOnlyList<ClientGutterRadarTargetObservation> currentCorpses,
        ClientGutterRadarTargetObservation corpse,
        DateTimeOffset corpseFirstSeenAtUtc)
    {
        var exactCandidates = state.RecentlyDisappearedMobs
            .Where(candidate =>
                IsWithinMobLootLineageWindow(
                    candidate.DisappearedAtUtc,
                    corpseFirstSeenAtUtc) &&
                candidate.Mob.ClientObjectAddress != 0 &&
                candidate.Mob.ClientObjectAddress ==
                    corpse.ClientObjectAddress)
            .ToArray();

        RecentlyDisappearedLootMob? matched = exactCandidates.Length == 1
            ? exactCandidates[0]
            : null;
        var evidence = "exact native ClientGameObject reuse";

        if (matched == null &&
            corpse.CorpseSpatial.IsAvailable)
        {
            var spatialCandidates = state.RecentlyDisappearedMobs
                .Where(candidate =>
                    IsSpatialMobLootLineageCandidate(
                        candidate,
                        corpse,
                        corpseFirstSeenAtUtc))
                .ToArray();

            if (spatialCandidates.Length == 1)
            {
                var candidate = spatialCandidates[0];
                var candidateCorpseCount = currentCorpses.Count(otherCorpse =>
                    state.UnmatchedCorpses.TryGetValue(
                        CreateCorpseInstanceKey(otherCorpse),
                        out var otherFirstSeenAtUtc) &&
                    IsSpatialMobLootLineageCandidate(
                        candidate,
                        otherCorpse,
                        otherFirstSeenAtUtc));

                if (candidateCorpseCount == 1)
                {
                    matched = candidate;
                    evidence =
                        "a unique recent 500-unit spatial transition";
                }
            }
        }

        if (matched == null)
        {
            return null;
        }

        state.RecentlyDisappearedMobs.Remove(matched);
        return new PendingMobLootCorpse(
            matched.Mob,
            corpse.ObjectId,
            evidence,
            corpseFirstSeenAtUtc);
    }

    private static ClientCorpseObservation ResolveMobLootCorpseObservation(
        ClientObservationSnapshot snapshot,
        ClientGutterRadarTargetObservation nearbyCorpse)
    {
        var selectedTarget = snapshot.Target;

        if (!selectedTarget.IsAvailable ||
            !selectedTarget.HasTarget ||
            selectedTarget.Kind != ClientTargetKind.Corpse ||
            selectedTarget.ObjectId != nearbyCorpse.ObjectId ||
            !selectedTarget.Corpse.IsAvailable)
        {
            return nearbyCorpse.Corpse;
        }

        var selectedCorpse = selectedTarget.Corpse;
        var nearbyObservation = nearbyCorpse.Corpse;

        return !nearbyObservation.IsAvailable ||
               selectedCorpse.ValidSlotCount >
                   nearbyObservation.ValidSlotCount ||
               selectedCorpse.OccupiedSlotCount >
                   nearbyObservation.OccupiedSlotCount
            ? selectedCorpse
            : nearbyObservation;
    }

    private void ObservePendingMobLootCorpse(
        MobLootProcessState state,
        MobLootContext context,
        CorpseInstanceKey corpseKey,
        ClientGutterRadarTargetObservation corpseTarget,
        PendingMobLootCorpse pending,
        ClientObservationSnapshot snapshot,
        DateTimeOffset observedAtUtc,
        ref bool observationsChanged)
    {
        var corpse = ResolveMobLootCorpseObservation(
            snapshot,
            corpseTarget);

        if (corpse.IsHydrated)
        {
            var currentItemTemplateIds = corpse.LootItems
                .Select(slot => slot.ItemTemplateId)
                .OfType<int>()
                .Where(itemTemplateId => itemTemplateId > 0)
                .Distinct()
                .Order()
                .ToArray();

            pending.ObserveCargo(
                corpse,
                currentItemTemplateIds,
                observedAtUtc);
        }

        if (!pending.HasHydratedCargo)
        {
            return;
        }

        if (observedAtUtc - pending.StableSinceUtc <
            MobLootCorpseSettleTime)
        {
            return;
        }

        var itemTemplateIds = pending.ObservedItemTemplateIds
            .Order()
            .ToArray();

        state.ProcessedCorpses.Add(corpseKey);
        state.PendingCorpses.Remove(corpseKey);
        this.session.LootCorpsesObserved++;
        this.settings.Lifetime.LootCorpsesObserved++;

        if (itemTemplateIds.Length == 0)
        {
            this.status = "No loot was observed on that corpse.";
            return;
        }

        foreach (var itemTemplateId in itemTemplateIds)
        {
            var variantId = CreateClientMobVariantId(pending.Mob);
            var relationshipId = CreateClientMobLootId(
                variantId,
                itemTemplateId);
            var canonicalMobName =
                ForgeMobNameCanonicalizer.Canonicalize(pending.Mob.Name);
            var fact = new ObservedMobLootFact(
                context.SectorId,
                context.SectorKey,
                context.SectorName,
                context.SystemName,
                context.ActiveSectorNumber,
                pending.Mob.ObjectId,
                pending.CorpseObjectId,
                canonicalMobName,
                pending.Mob.RawObjectType,
                pending.Mob.CombatLevel,
                pending.Mob.FactionIdentifier,
                pending.Mob.FactionBindingKind,
                pending.Mob.IntrinsicRelationshipRaw,
                pending.Mob.IntrinsicDisposition,
                pending.Mob.ObservedRelationshipRaw,
                pending.Mob.ObservedAggressionRaw,
                pending.Mob.ObservedActivelyAggressive,
                pending.Mob.IsOrganic,
                pending.Mob.AutoLevel,
                itemTemplateId,
                relationshipId,
                observedAtUtc);

            observationsChanged |=
                this.RecordObservedMobLootRelationship(fact);

            if (this.IsMobLootRelationshipKnown(relationshipId) ||
                this.acceptedMobLootRelationshipIds.Contains(relationshipId))
            {
                continue;
            }

            state.Pending[relationshipId] = fact;
            state.FirstPendingAtUtc ??= observedAtUtc;
        }

        this.status = "Mob loot observed.";
    }

    private async Task SubmitMobLootAsync(
        ObservedMobLootBatch batch,
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
            var unsignedRequest = new ForgeMobLootContributionRequest
            {
                ContributorId = identity.ContributorId,
                RequestId = Guid.NewGuid().ToString("D", CultureInfo.InvariantCulture),
                SubmittedAtUtc = DateTimeOffset.UtcNow,
                FirstObservedAtUtc = batch.FirstObservedAtUtc,
                LastObservedAtUtc = batch.LastObservedAtUtc,
                ObservationCount = batch.Facts.Count,
                ClientVersion = clientVersion,
                DatasetRevision = this.dataSet.AuthorityRevision,
                Attribution = this.settings.Attribution ==
                    ForgeContributionAttribution.LivePilotName
                        ? "live-pilot-name"
                        : "publicly-anonymous",
                LivePilotName = batch.LivePilotName,
                Drops =
                [
                    .. batch.Facts.Select(fact =>
                        new ForgeMobLootContributionItem
                        {
                            SectorId = fact.SectorId,
                            SectorKey = fact.SectorKey,
                            SectorName = fact.SectorName,
                            SystemName = fact.SystemName,
                            ActiveSectorNumber = fact.ActiveSectorNumber,
                            MobObjectId = fact.MobObjectId,
                            CorpseObjectId = fact.CorpseObjectId,
                            MobName = fact.MobName,
                            RawObjectType = fact.RawObjectType,
                            CombatLevel = fact.CombatLevel,
                            FactionIdentifier = fact.FactionIdentifier,
                            FactionBindingKind = fact.FactionBindingKind,
                            IntrinsicRelationshipRaw =
                                fact.IntrinsicRelationshipRaw,
                            IntrinsicDisposition =
                                fact.IntrinsicDisposition,
                            ObservedRelationshipRaw =
                                fact.ObservedRelationshipRaw,
                            ObservedAggressionRaw =
                                fact.ObservedAggressionRaw,
                            ObservedActivelyAggressive =
                                fact.ObservedActivelyAggressive,
                            IsOrganic = fact.IsOrganic,
                            AutoLevel = fact.AutoLevel,
                            ItemTemplateId = fact.ItemTemplateId,
                        }),
                ],
            };
            var request = unsignedRequest with
            {
                Signature = identity.Sign(unsignedRequest),
            };
            var response = await this.client.SubmitMobLootAsync(
                    request,
                    cancellationToken)
                .ConfigureAwait(false);

            lock (this.stateLock)
            {
                foreach (var fact in batch.Facts)
                {
                    this.acceptedMobLootRelationshipIds.Add(
                        fact.RelationshipId);
                }

                this.SynchronizePendingMobLootRelationshipIds();

                if (this.mobLootProcessStates.TryGetValue(
                        batch.ProcessId,
                        out var state))
                {
                    foreach (var fact in batch.Facts)
                    {
                        state.Pending.Remove(fact.RelationshipId);
                    }

                    state.FirstPendingAtUtc = state.Pending.Count == 0
                        ? null
                        : state.Pending.Values.Min(fact => fact.ObservedAtUtc);
                    state.NextAttemptAllowedAt = DateTimeOffset.MinValue;
                }

                this.session.MobLootFactsSubmitted += response.Received;
                this.session.MobLootAlreadyCanonical += response.AlreadyCanonical;
                this.session.MobLootEvidenceAccepted += response.EvidenceAccepted;
                this.session.MobLootVariantsCreated += response.VariantsCreated;
                this.session.MobLootRelationshipsCreated +=
                    response.RelationshipsCreated;
                this.session.MobLootRelationshipsStrengthened +=
                    response.RelationshipsStrengthened;
                this.session.SuccessfulBatches++;
                this.session.LastSuccessfulContributionUtc = DateTimeOffset.UtcNow;

                var lifetime = this.settings.Lifetime;
                lifetime.MobLootFactsSubmitted += response.Received;
                lifetime.MobLootAlreadyCanonical += response.AlreadyCanonical;
                lifetime.MobLootEvidenceAccepted += response.EvidenceAccepted;
                lifetime.MobLootVariantsCreated += response.VariantsCreated;
                lifetime.MobLootRelationshipsCreated +=
                    response.RelationshipsCreated;
                lifetime.MobLootRelationshipsStrengthened +=
                    response.RelationshipsStrengthened;
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
                        $"Mob loot shared with Forge. Revision {revision} is ready.")
                    : "Mob loot shared with Forge.";
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
                    "Mob loot could not be shared. It will retry automatically.";

                if (this.mobLootProcessStates.TryGetValue(
                        batch.ProcessId,
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
                this.inFlightMobLootBatchKeys.Remove(submissionKey);

                if (this.mobLootProcessStates.TryGetValue(
                        batch.ProcessId,
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

    private bool TryResolveMobLootContext(
        ClientObservationSnapshot snapshot,
        out MobLootContext context,
        out string unavailableReason)
    {
        context = null!;
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
                "Mob loot contribution is waiting for the latest Forge data.";
            return false;
        }

        context = new MobLootContext(
            identity.Name,
            sector.Id,
            sector.Key,
            sector.Name,
            sector.SystemName,
            snapshot.World.ActiveSectorNumber);
        return true;
    }

    private static ObservedLootMob? TryCreateNearbyLootMob(
        ClientGutterRadarTargetObservation target)
    {
        if (target.RawObjectType is not 0 and not 2 and not 35)
        {
            return null;
        }

        var mob = target.Mob;
        var name = mob.Name.Trim();

        if (string.IsNullOrWhiteSpace(name) ||
            IsGuardianTurret(name) ||
            mob.CombatLevel is not { } combatLevel ||
            mob.IsOrganic is not { } isOrganic)
        {
            return null;
        }

        ClientSpatialPosition? position = mob.Spatial.IsAvailable
            ? mob.Spatial.Position
            : null;

        var factionBindingKind =
            ResolveMobFactionBindingKind(mob.FactionIdentifier);

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

        return new ObservedLootMob(
            target.ObjectId,
            target.ClientObjectAddress,
            position,
            name,
            target.RawObjectType,
            combatLevel,
            NormalizeMobFaction(mob.FactionIdentifier),
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
            mob.AutoLevel);
    }

    private static CorpseInstanceKey CreateCorpseInstanceKey(
        ClientGutterRadarTargetObservation corpse)
    {
        return new CorpseInstanceKey(
            corpse.ObjectId,
            corpse.ClientObjectAddress);
    }

    private static bool IsCorpseNameCompatible(
        string corpseName,
        string mobName)
    {
        var normalizedCorpse = NormalizeMobIdentityValue(corpseName);
        var normalizedMob = NormalizeMobIdentityValue(mobName);

        if (normalizedCorpse.Length == 0 ||
            normalizedMob.Length == 0)
        {
            return true;
        }

        if (normalizedCorpse.StartsWith(
                "CORPSEOF",
                StringComparison.Ordinal))
        {
            return string.Equals(
                normalizedCorpse["CORPSEOF".Length..],
                normalizedMob,
                StringComparison.Ordinal);
        }

        if (normalizedCorpse.StartsWith(
                "WRECKOF",
                StringComparison.Ordinal))
        {
            return string.Equals(
                normalizedCorpse["WRECKOF".Length..],
                normalizedMob,
                StringComparison.Ordinal);
        }

        return true;
    }

    private static bool IsSpatialMobLootLineageCandidate(
        RecentlyDisappearedLootMob candidate,
        ClientGutterRadarTargetObservation corpse,
        DateTimeOffset corpseFirstSeenAtUtc)
    {
        return IsWithinMobLootLineageWindow(
                   candidate.DisappearedAtUtc,
                   corpseFirstSeenAtUtc) &&
               candidate.Mob.Position is { } position &&
               corpse.CorpseSpatial.IsAvailable &&
               IsCorpseNameCompatible(
                   corpse.Name,
                   candidate.Mob.Name) &&
               CalculateDistance(
                   position,
                   corpse.CorpseSpatial.Position) <=
                       MobLootMaximumLineageDistance;
    }

    private static bool IsWithinMobLootLineageWindow(
        DateTimeOffset mobDisappearedAtUtc,
        DateTimeOffset corpseFirstSeenAtUtc)
    {
        var gap = corpseFirstSeenAtUtc - mobDisappearedAtUtc;
        return gap >= TimeSpan.Zero &&
               gap <= MobLootLineageRetentionTime;
    }

    private static double CalculateDistance(
        ClientSpatialPosition left,
        ClientSpatialPosition right)
    {
        var deltaX = (double)left.X - right.X;
        var deltaY = (double)left.Y - right.Y;
        var deltaZ = (double)left.Z - right.Z;
        return Math.Sqrt(
            (deltaX * deltaX) +
            (deltaY * deltaY) +
            (deltaZ * deltaZ));
    }

    private bool IsMobLootRelationshipKnown(string relationshipId)
    {
        return this.dataSet.Document.MobLoot.Any(relationship =>
            string.Equals(
                relationship.Id,
                relationshipId,
                StringComparison.Ordinal));
    }

    private bool PruneActivatedMobLootRelationshipIds()
    {
        this.acceptedMobLootRelationshipIds.RemoveWhere(
            this.IsMobLootRelationshipKnown);
        return this.SynchronizePendingMobLootRelationshipIds();
    }

    private bool SynchronizePendingMobLootRelationshipIds()
    {
        var pending = this.acceptedMobLootRelationshipIds
            .Where(relationshipId =>
                !this.IsMobLootRelationshipKnown(relationshipId))
            .Order(StringComparer.Ordinal)
            .ToArray();

        if (this.settings.PendingMobLootRelationshipIds.SequenceEqual(
                pending,
                StringComparer.Ordinal))
        {
            return false;
        }

        this.settings.PendingMobLootRelationshipIds = [.. pending];
        return true;
    }

    private bool RecordObservedMobLootRelationship(ObservedMobLootFact fact)
    {
        var key = fact.RelationshipId;
        var changed = false;

        if (this.sessionObservedMobLootRelationshipKeys.Add(key))
        {
            this.session.MobLootRelationshipsObserved =
                this.sessionObservedMobLootRelationshipKeys.Count;
            changed = true;
        }

        if (this.lifetimeObservedMobLootRelationshipKeys.Add(key))
        {
            this.settings.Lifetime.ObservedMobLootRelationshipKeys =
            [
                .. this.lifetimeObservedMobLootRelationshipKeys
                    .Order(StringComparer.Ordinal),
            ];
            this.settings.Lifetime.MobLootRelationshipsObserved =
                this.lifetimeObservedMobLootRelationshipKeys.Count;
            changed = true;
        }

        return changed;
    }

    private static string CreateClientMobVariantId(ObservedLootMob mob)
    {
        var canonicalName = ForgeMobNameCanonicalizer.Canonicalize(mob.Name);
        var semanticKey = string.Create(
            CultureInfo.InvariantCulture,
            $"mob-variant:{mob.RawObjectType}:{NormalizeMobIdentityValue(canonicalName)}:{mob.CombatLevel}:{(mob.IsOrganic ? 1 : 0)}:{NormalizeMobIdentityValue(mob.FactionIdentifier)}:{(int)mob.FactionBindingKind}:{mob.IntrinsicRelationshipRaw?.ToString(CultureInfo.InvariantCulture) ?? ""}");
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(semanticKey));
        Span<byte> guidBytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(guidBytes);
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x80);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
        return new Guid(guidBytes, bigEndian: true).ToString("D");
    }

    private static string CreateClientMobLootId(
        string variantId,
        int itemTemplateId)
    {
        var hash = SHA256.HashData(
            Encoding.UTF8.GetBytes(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{variantId}|item-template:{itemTemplateId}")));
        return string.Concat(
            "mob-loot-",
            Convert.ToHexString(hash).ToLowerInvariant()[..32]);
    }

    private static string CreateMobLootSubmissionKey(
        ObservedMobLootBatch batch)
    {
        var builder = new StringBuilder();
        builder.Append(batch.ProcessId.ToString(CultureInfo.InvariantCulture));

        foreach (var fact in batch.Facts)
        {
            builder.Append('|');
            builder.Append(fact.RelationshipId);
            builder.Append('|');
            builder.Append(fact.CorpseObjectId.ToString(CultureInfo.InvariantCulture));
        }

        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private sealed class MobLootProcessState(string sectorId)
    {
        public string SectorId { get; } = sectorId;

        public Dictionary<uint, ObservedLootMob> LiveMobs { get; } = [];

        public List<RecentlyDisappearedLootMob> RecentlyDisappearedMobs
        {
            get;
        } = [];

        public bool HasObservedCorpseBaseline { get; set; }

        public HashSet<CorpseInstanceKey> LiveCorpseKeys { get; } = [];

        public Dictionary<CorpseInstanceKey, DateTimeOffset>
            UnmatchedCorpses { get; } = [];

        public Dictionary<CorpseInstanceKey, PendingMobLootCorpse>
            PendingCorpses { get; } = [];

        public HashSet<CorpseInstanceKey> ProcessedCorpses { get; } = [];

        public Dictionary<string, ObservedMobLootFact> Pending { get; } =
            new(StringComparer.Ordinal);

        public DateTimeOffset? FirstPendingAtUtc { get; set; }

        public DateTimeOffset NextAttemptAllowedAt { get; set; } =
            DateTimeOffset.MinValue;

        public string? InFlightKey { get; set; }
    }

    private sealed record MobLootContext(
        string LivePilotName,
        string SectorId,
        string SectorKey,
        string SectorName,
        string SystemName,
        uint ActiveSectorNumber);

    private readonly record struct CorpseInstanceKey(
        uint ObjectId,
        uint ClientObjectAddress);

    private sealed record ObservedLootMob(
        uint ObjectId,
        uint ClientObjectAddress,
        ClientSpatialPosition? Position,
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
        bool? AutoLevel);

    private sealed record RecentlyDisappearedLootMob(
        ObservedLootMob Mob,
        DateTimeOffset DisappearedAtUtc);

    private sealed class PendingMobLootCorpse(
        ObservedLootMob mob,
        uint corpseObjectId,
        string lineageEvidence,
        DateTimeOffset observedAtUtc)
    {
        private string cargoFingerprint = "";

        public ObservedLootMob Mob { get; } = mob;

        public uint CorpseObjectId { get; } = corpseObjectId;

        public string LineageEvidence { get; } = lineageEvidence;

        public DateTimeOffset StableSinceUtc { get; private set; } =
            observedAtUtc;

        public DateTimeOffset LastSeenAtUtc { get; set; } =
            observedAtUtc;

        public HashSet<int> ObservedItemTemplateIds { get; } = [];

        public bool HasHydratedCargo { get; private set; }

        public void ObserveCargo(
            ClientCorpseObservation corpse,
            IReadOnlyList<int> itemTemplateIds,
            DateTimeOffset observedAtUtc)
        {
            this.HasHydratedCargo = true;

            foreach (var itemTemplateId in itemTemplateIds)
            {
                this.ObservedItemTemplateIds.Add(itemTemplateId);
            }

            var fingerprint = string.Create(
                CultureInfo.InvariantCulture,
                $"{corpse.PresentSlotCount}:{corpse.ValidSlotCount}:{string.Join(",", itemTemplateIds)}");

            if (string.Equals(
                    this.cargoFingerprint,
                    fingerprint,
                    StringComparison.Ordinal))
            {
                return;
            }

            this.cargoFingerprint = fingerprint;
            this.StableSinceUtc = observedAtUtc;
        }
    }

    private sealed record ObservedMobLootFact(
        string SectorId,
        string SectorKey,
        string SectorName,
        string SystemName,
        uint ActiveSectorNumber,
        uint MobObjectId,
        uint CorpseObjectId,
        string MobName,
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
        int ItemTemplateId,
        string RelationshipId,
        DateTimeOffset ObservedAtUtc);

    private sealed record ObservedMobLootBatch(
        int ProcessId,
        string LivePilotName,
        DateTimeOffset FirstObservedAtUtc,
        DateTimeOffset LastObservedAtUtc,
        IReadOnlyList<ObservedMobLootFact> Facts);
}
