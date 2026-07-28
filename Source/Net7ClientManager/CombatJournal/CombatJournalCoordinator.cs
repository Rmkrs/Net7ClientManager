namespace Net7ClientManager.CombatJournal;

using System.Diagnostics;
using Net7ClientManager.ActivityJournal;
using Net7ClientManager.Observations;
using Net7ClientManager.Observations.Models;

internal sealed class CombatJournalCoordinator
{
    private static readonly TimeSpan encounterInactivityTimeout =
        TimeSpan.FromSeconds(30);
    private static readonly TimeSpan playerDeathAttributionWindow =
        TimeSpan.FromSeconds(10);
    // The existing mob-loot lineage probe established that a genuine mob to
    // corpse transition is observed within 488 ms and 116 world units. The
    // normal production cadence is one second, so use the same conservative
    // production envelope as contribution capture.
    private const double maximumCorpseLineageDistance = 500;

    private readonly CombatJournalStore store;
    private readonly Lock stateLock = new();
    private readonly Dictionary<int, CombatProcessState> processStates = [];
    private bool recordHistory;
    private bool recordActivityOutcomes;

    public CombatJournalCoordinator(
        CombatJournalStore store,
        bool recordHistory,
        bool recordActivityOutcomes)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.recordHistory = recordHistory;
        this.recordActivityOutcomes = recordActivityOutcomes;
    }

    public event EventHandler<CombatJournalChangedEventArgs>? JournalChanged;

    public event EventHandler<CombatEncounterEndedEventArgs>? EncounterEnded;

    public void SetRecordingOptions(
        bool recordHistory,
        bool recordActivityOutcomes)
    {
        IReadOnlyList<CombatJournalEncounter> ended = [];
        var persistEnded = false;

        lock (this.stateLock)
        {
            if (this.recordHistory == recordHistory &&
                this.recordActivityOutcomes == recordActivityOutcomes)
            {
                return;
            }

            var wasObserving =
                this.recordHistory || this.recordActivityOutcomes;
            var willObserve =
                recordHistory || recordActivityOutcomes;
            var historyModeChanged = this.recordHistory != recordHistory;

            if (wasObserving &&
                (historyModeChanged || !willObserve))
            {
                ended = this.processStates.Values
                    .SelectMany(state => state.EndAll(
                        CombatJournalOutcome.Interrupted,
                        state.LastObservedAt))
                    .ToArray();
                this.processStates.Clear();
                persistEnded = this.recordHistory;
            }

            this.recordHistory = recordHistory;
            this.recordActivityOutcomes = recordActivityOutcomes;
        }

        if (persistEnded)
        {
            this.PersistEndedEncounters(ended);
        }
    }

    public IReadOnlyList<CombatJournalEncounter> GetHistory(
        uint characterId,
        int maximumResults = 1000) =>
        this.store.GetHistory(characterId, maximumResults);

    public CombatJournalEncounter? GetEncounter(string encounterId) =>
        this.store.GetEncounter(encounterId);

    public DateTimeOffset? GetLastRecordedAt(uint characterId) =>
        this.store.GetLastRecordedAt(characterId);

    public void Observe(ClientObservationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        IReadOnlyList<CombatJournalWrite> writes = [];
        IReadOnlyList<CombatJournalEncounter> ended = [];

        lock (this.stateLock)
        {
            if (!this.recordHistory && !this.recordActivityOutcomes)
            {
                return;
            }

            if (snapshot.LifecycleState != ClientLifecycleState.InGame)
            {
                if (this.processStates.Remove(snapshot.ProcessId, out var oldState))
                {
                    ended = oldState.EndAll(
                        CombatJournalOutcome.Interrupted,
                        snapshot.ObservedAt);
                }
            }
            else if (snapshot.LoadingOrTransitionFlag != 0 ||
                     !snapshot.World.IsAvailable)
            {
                if (this.processStates.TryGetValue(
                        snapshot.ProcessId,
                        out var transitionState))
                {
                    ended = transitionState.EndAll(
                        CombatJournalOutcome.Interrupted,
                        snapshot.ObservedAt);
                    this.processStates.Remove(snapshot.ProcessId);
                }
            }
            else
            {
                var identity = ClientLiveCharacterIdentityResolver.Resolve(snapshot);

                if (!identity.IsAvailable ||
                    identity.CharacterObjectId is not { } characterId ||
                    string.IsNullOrWhiteSpace(identity.Name))
                {
                    return;
                }

                var location = JournalLocationResolver.Capture(snapshot);

                if (!location.IsUsable)
                {
                    return;
                }

                if (!this.processStates.TryGetValue(
                        snapshot.ProcessId,
                        out var state) ||
                    state.CharacterId != characterId)
                {
                    this.processStates[snapshot.ProcessId] =
                        new CombatProcessState(
                            characterId,
                            identity.Name.Trim(),
                            location,
                            snapshot);
                    return;
                }

                state.PilotName = identity.Name.Trim();
                (writes, ended) = state.Observe(snapshot, location);
            }
        }

        this.SaveWrites(writes);
        this.SaveEndedEncounters(ended);
    }

    public void ForgetProcess(int processId)
    {
        IReadOnlyList<CombatJournalEncounter> ended = [];

        lock (this.stateLock)
        {
            if (this.processStates.Remove(processId, out var state))
            {
                ended = state.EndAll(
                    CombatJournalOutcome.Interrupted,
                    state.LastObservedAt);
            }
        }

        this.SaveEndedEncounters(ended);
    }

    private void SaveWrites(IReadOnlyList<CombatJournalWrite> writes)
    {
        if (writes.Count == 0)
        {
            return;
        }

        List<uint> changed = [];

        if (!this.recordHistory)
        {
            return;
        }

        try
        {
            var encounters = writes
                .GroupBy(write => write.Encounter.EncounterId, StringComparer.Ordinal)
                .Select(group => group.Last().Encounter)
                .ToArray();
            var events = writes
                .Select(write => write.Event)
                .ToArray();
            this.store.SaveBatch(encounters, events);
            changed.AddRange(encounters.Select(encounter => encounter.CharacterId));
        }
        catch (Exception exception)
        {
            Debug.WriteLine(
                string.Concat("Combat Journal write failed: ", exception),
                "Net7.CombatJournal");
        }

        this.PublishChanged(changed);
    }

    private void PersistEndedEncounters(
        IReadOnlyList<CombatJournalEncounter> encounters)
    {
        if (encounters.Count == 0)
        {
            return;
        }

        List<uint> changed = [];

        foreach (var encounter in encounters)
        {
            try
            {
                this.store.SaveEncounter(encounter);
                changed.Add(encounter.CharacterId);
            }
            catch (Exception exception)
            {
                Debug.WriteLine(
                    string.Concat(
                        "Combat Journal encounter finalization failed: ",
                        exception),
                    "Net7.CombatJournal");
            }
        }

        this.PublishChanged(changed);
    }

    private void SaveEndedEncounters(
        IReadOnlyList<CombatJournalEncounter> encounters)
    {
        if (encounters.Count == 0)
        {
            return;
        }

        List<uint> changed = [];

        foreach (var encounter in encounters)
        {
            try
            {
                if (this.recordHistory)
                {
                    this.store.SaveEncounter(encounter);
                    changed.Add(encounter.CharacterId);
                }

                if (this.recordActivityOutcomes)
                {
                    this.EncounterEnded?.Invoke(
                        this,
                        new CombatEncounterEndedEventArgs(encounter));
                }
            }
            catch (Exception exception)
            {
                Debug.WriteLine(
                    string.Concat(
                        "Combat Journal encounter finalization failed: ",
                        exception),
                    "Net7.CombatJournal");
            }
        }

        this.PublishChanged(changed);
    }

    private void PublishChanged(IEnumerable<uint> characterIds)
    {
        foreach (var characterId in characterIds.Distinct())
        {
            this.JournalChanged?.Invoke(
                this,
                new CombatJournalChangedEventArgs(characterId));
        }
    }

    private sealed record CombatJournalWrite(
        CombatJournalEncounter Encounter,
        CombatJournalEvent Event);

    private sealed class CombatProcessState
    {
        private readonly Dictionary<uint, EncounterAccumulator> active = [];
        private JournalLocationContext latestLocation;
        private string latestWorldKey;
        private long lastProcessedSequence;
        private float? previousPlayerHull;
        private HashSet<CorpseInstanceKey> liveCorpseKeys;

        public CombatProcessState(
            uint characterId,
            string pilotName,
            JournalLocationContext location,
            ClientObservationSnapshot initialSnapshot)
        {
            this.CharacterId = characterId;
            this.PilotName = pilotName;
            this.latestLocation = location;
            this.latestWorldKey = BuildWorldKey(location);
            this.LastObservedAt = initialSnapshot.ObservedAt;
            this.lastProcessedSequence = initialSnapshot.Combat.RecentEvents
                .Select(item => item.Sequence)
                .DefaultIfEmpty(0)
                .Max();
            this.previousPlayerHull = GetPlayerHull(initialSnapshot);
            this.liveCorpseKeys = CaptureCorpseKeys(initialSnapshot);
        }

        public uint CharacterId { get; }

        public string PilotName { get; set; }

        public DateTimeOffset LastObservedAt { get; private set; }

        public (
            IReadOnlyList<CombatJournalWrite> Writes,
            IReadOnlyList<CombatJournalEncounter> Ended) Observe(
            ClientObservationSnapshot snapshot,
            JournalLocationContext location)
        {
            this.LastObservedAt = snapshot.ObservedAt;
            this.latestLocation = location;
            List<CombatJournalWrite> writes = [];
            List<CombatJournalEncounter> ended = [];
            var worldKey = BuildWorldKey(location);

            if (!string.Equals(
                    this.latestWorldKey,
                    worldKey,
                    StringComparison.OrdinalIgnoreCase))
            {
                ended.AddRange(this.EndAll(
                    CombatJournalOutcome.Interrupted,
                    snapshot.ObservedAt));
                this.latestWorldKey = worldKey;
            }

            foreach (var observedEvent in snapshot.Combat.RecentEvents
                         .Where(item => item.Sequence > this.lastProcessedSequence)
                         .OrderBy(item => item.Sequence))
            {
                this.lastProcessedSequence = Math.Max(
                    this.lastProcessedSequence,
                    observedEvent.Sequence);

                if (!TryResolveCounterpart(
                        observedEvent,
                        snapshot,
                        out var direction,
                        out var targetObjectId,
                        out var targetName,
                        out var targetLevel,
                        out var targetClientObjectAddress,
                        out var targetPosition))
                {
                    continue;
                }

                if (!this.active.TryGetValue(targetObjectId, out var encounter))
                {
                    encounter = new EncounterAccumulator(
                        Guid.NewGuid().ToString("N"),
                        this.CharacterId,
                        this.PilotName,
                        targetObjectId,
                        targetName,
                        targetLevel,
                        targetClientObjectAddress,
                        targetPosition,
                        observedEvent.ObservedAt,
                        this.latestLocation);
                    this.active[targetObjectId] = encounter;
                }
                else
                {
                    encounter.UpdateIdentity(
                        targetName,
                        targetLevel,
                        targetClientObjectAddress,
                        targetPosition);
                }

                var combatEvent = encounter.Add(
                    observedEvent,
                    direction,
                    ResolveActorName(observedEvent.SourceIdentity),
                    ResolveActorName(observedEvent.VictimIdentity),
                    this.latestLocation,
                    this.PilotName);
                writes.Add(new CombatJournalWrite(
                    encounter.ToRecord(),
                    combatEvent));
            }

            this.UpdateIdentities(snapshot);
            ended.AddRange(this.ResolveDefeatedTargets(snapshot));
            ended.AddRange(this.ResolvePlayerDeath(snapshot));
            ended.AddRange(this.ResolveInactive(snapshot.ObservedAt));
            this.previousPlayerHull = GetPlayerHull(snapshot);
            return (writes, ended);
        }

        public IReadOnlyList<CombatJournalEncounter> EndAll(
            CombatJournalOutcome outcome,
            DateTimeOffset occurredAt)
        {
            var ended = this.active.Values
                .Select(encounter => encounter.End(outcome, occurredAt))
                .ToArray();
            this.active.Clear();
            return ended;
        }

        private void UpdateIdentities(ClientObservationSnapshot snapshot)
        {
            foreach (var target in snapshot.NearbyTargets.Targets.Where(
                         target => target.IsAvailable && target.ObjectId != 0))
            {
                if (!this.active.TryGetValue(target.ObjectId, out var encounter))
                {
                    continue;
                }

                encounter.UpdateIdentity(
                    FirstNonEmpty(
                        target.DisplayName,
                        target.Name,
                        target.Mob.Name),
                    target.Mob.CombatLevel,
                    target.ClientObjectAddress,
                    target.Mob.Spatial.IsAvailable
                        ? target.Mob.Spatial.Position
                        : null);
            }

            if (snapshot.Target is { IsAvailable: true, HasTarget: true } &&
                this.active.TryGetValue(
                    snapshot.Target.ObjectId,
                    out var selected))
            {
                selected.UpdateIdentity(
                    snapshot.Target.Name,
                    snapshot.Target.Operational.Identity.CombatLevel,
                    snapshot.Target.ClientObjectAddress,
                    null);
            }
        }

        private IReadOnlyList<CombatJournalEncounter> ResolveDefeatedTargets(
            ClientObservationSnapshot snapshot)
        {
            HashSet<uint> defeatedObjectIds = [];

            foreach (var target in snapshot.NearbyTargets.Targets.Where(
                         target => target.IsAvailable && target.ObjectId != 0))
            {
                if (target.Kind == ClientNearbyTargetKind.Corpse ||
                    target.Hull is
                    {
                        HasCompleteHullData: true,
                        HullPoints: <= 0,
                    })
                {
                    defeatedObjectIds.Add(target.ObjectId);
                }
            }

            if (snapshot.Target is
                {
                    IsAvailable: true,
                    HasTarget: true,
                } selected &&
                (selected.Kind == ClientTargetKind.Corpse ||
                 selected.Hull is
                 {
                     HasCompleteHullData: true,
                     HullPoints: <= 0,
                 }))
            {
                defeatedObjectIds.Add(selected.ObjectId);
            }

            List<CombatJournalEncounter> ended = [];

            foreach (var objectId in defeatedObjectIds)
            {
                if (!this.active.Remove(objectId, out var encounter))
                {
                    continue;
                }

                ended.Add(encounter.End(
                    encounter.OutgoingHitCount > 0
                        ? CombatJournalOutcome.Killed
                        : CombatJournalOutcome.Disengaged,
                    snapshot.ObservedAt));
            }

            if (!snapshot.NearbyTargets.IsAvailable)
            {
                return ended;
            }

            var corpses = snapshot.NearbyTargets.Targets
                .Where(target => target is
                {
                    IsAvailable: true,
                    ObjectId: not 0,
                    Kind: ClientNearbyTargetKind.Corpse,
                })
                .ToArray();
            var currentCorpseKeys = corpses
                .Select(CreateCorpseInstanceKey)
                .ToHashSet();
            var newCorpses = corpses
                .Where(corpse => !this.liveCorpseKeys.Contains(
                    CreateCorpseInstanceKey(corpse)))
                .ToArray();
            this.liveCorpseKeys = currentCorpseKeys;

            foreach (var corpse in newCorpses)
            {
                var matched = this.TryMatchNewCorpse(corpse, snapshot);

                if (matched == null ||
                    !this.active.Remove(matched.TargetObjectId))
                {
                    continue;
                }

                ended.Add(matched.End(
                    matched.OutgoingHitCount > 0
                        ? CombatJournalOutcome.Killed
                        : CombatJournalOutcome.Disengaged,
                    snapshot.ObservedAt));
            }

            return ended;
        }

        private EncounterAccumulator? TryMatchNewCorpse(
            ClientGutterRadarTargetObservation corpse,
            ClientObservationSnapshot snapshot)
        {
            var liveObjectIds = snapshot.NearbyTargets.Targets
                .Where(target => target is
                {
                    IsAvailable: true,
                    ObjectId: not 0,
                    Kind: not ClientNearbyTargetKind.Corpse,
                })
                .Select(target => target.ObjectId)
                .ToHashSet();
            var candidates = this.active.Values
                .Where(encounter => !liveObjectIds.Contains(
                    encounter.TargetObjectId))
                .ToArray();
            var exact = candidates
                .Where(encounter =>
                    encounter.TargetClientObjectAddress != 0 &&
                    corpse.ClientObjectAddress != 0 &&
                    encounter.TargetClientObjectAddress ==
                        corpse.ClientObjectAddress)
                .ToArray();

            if (exact.Length == 1)
            {
                return exact[0];
            }

            if (!corpse.CorpseSpatial.IsAvailable)
            {
                return null;
            }

            var spatial = candidates
                .Where(encounter =>
                    encounter.TargetPosition.HasValue &&
                    IsCorpseNameCompatible(
                        corpse.Name,
                        encounter.TargetName) &&
                    CalculateDistance(
                        encounter.TargetPosition.Value,
                        corpse.CorpseSpatial.Position) <=
                            maximumCorpseLineageDistance)
                .ToArray();
            return spatial.Length == 1 ? spatial[0] : null;
        }

        private static HashSet<CorpseInstanceKey> CaptureCorpseKeys(
            ClientObservationSnapshot snapshot)
        {
            if (!snapshot.NearbyTargets.IsAvailable)
            {
                return [];
            }

            return snapshot.NearbyTargets.Targets
                .Where(target => target is
                {
                    IsAvailable: true,
                    ObjectId: not 0,
                    Kind: ClientNearbyTargetKind.Corpse,
                })
                .Select(CreateCorpseInstanceKey)
                .ToHashSet();
        }

        private static CorpseInstanceKey CreateCorpseInstanceKey(
            ClientGutterRadarTargetObservation corpse) =>
            new(corpse.ObjectId, corpse.ClientObjectAddress);

        private static bool IsCorpseNameCompatible(
            string corpseName,
            string targetName)
        {
            var normalizedCorpse = NormalizeMobName(corpseName);
            var normalizedTarget = NormalizeMobName(targetName);

            if (normalizedCorpse.Length == 0 ||
                normalizedTarget.Length == 0)
            {
                return true;
            }

            foreach (var prefix in new[] { "CORPSEOF", "WRECKOF" })
            {
                if (normalizedCorpse.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return string.Equals(
                        normalizedCorpse[prefix.Length..],
                        normalizedTarget,
                        StringComparison.Ordinal);
                }
            }

            return string.Equals(
                normalizedCorpse,
                normalizedTarget,
                StringComparison.Ordinal);
        }

        private static string NormalizeMobName(string value) =>
            new(value
                .Where(char.IsLetterOrDigit)
                .Select(char.ToUpperInvariant)
                .ToArray());

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

        private IReadOnlyList<CombatJournalEncounter> ResolvePlayerDeath(
            ClientObservationSnapshot snapshot)
        {
            var currentHull = GetPlayerHull(snapshot);

            if (!this.previousPlayerHull.HasValue ||
                !currentHull.HasValue ||
                this.previousPlayerHull.Value <= 0 ||
                currentHull.Value > 0)
            {
                return [];
            }

            var killer = this.active.Values
                .Where(encounter =>
                    encounter.IncomingHitCount > 0 &&
                    snapshot.ObservedAt - encounter.LastIncomingAt <=
                        playerDeathAttributionWindow)
                .OrderByDescending(encounter => encounter.LastIncomingAt)
                .FirstOrDefault();
            List<CombatJournalEncounter> ended = [];

            foreach (var item in this.active.Values.ToArray())
            {
                this.active.Remove(item.TargetObjectId);
                ended.Add(item.End(
                    ReferenceEquals(item, killer)
                        ? CombatJournalOutcome.Died
                        : CombatJournalOutcome.Disengaged,
                    snapshot.ObservedAt));
            }

            return ended;
        }

        private IReadOnlyList<CombatJournalEncounter> ResolveInactive(
            DateTimeOffset observedAt)
        {
            var stale = this.active.Values
                .Where(encounter =>
                    observedAt - encounter.LastEventAt >=
                        encounterInactivityTimeout)
                .ToArray();
            List<CombatJournalEncounter> ended = [];

            foreach (var encounter in stale)
            {
                this.active.Remove(encounter.TargetObjectId);
                ended.Add(encounter.End(
                    CombatJournalOutcome.Disengaged,
                    encounter.LastEventAt));
            }

            return ended;
        }

        private static float? GetPlayerHull(ClientObservationSnapshot snapshot)
        {
            return snapshot.LocalPlayer.Hull is
            {
                HasCompleteHullData: true,
            } hull
                ? hull.HullPointsRaw
                : null;
        }

        private static bool TryResolveCounterpart(
            ClientCombatEventObservation observedEvent,
            ClientObservationSnapshot snapshot,
            out CombatJournalDirection direction,
            out uint targetObjectId,
            out string targetName,
            out int? targetLevel,
            out uint targetClientObjectAddress,
            out ClientSpatialPosition? targetPosition)
        {
            direction = default;
            targetObjectId = 0;
            targetName = "";
            targetLevel = null;
            targetClientObjectAddress = 0;
            targetPosition = null;

            switch (observedEvent.Direction)
            {
                case ClientCombatPacketDirection.Outgoing:
                    direction = CombatJournalDirection.Outgoing;
                    targetObjectId = observedEvent.VictimObjectId;
                    targetName = ResolveActorName(observedEvent.VictimIdentity);
                    break;

                case ClientCombatPacketDirection.Incoming:
                    direction = CombatJournalDirection.Incoming;
                    targetObjectId = observedEvent.SourceObjectId;
                    targetName = ResolveActorName(observedEvent.SourceIdentity);
                    break;

                default:
                    return false;
            }

            if (targetObjectId == 0 ||
                targetObjectId == observedEvent.LocalPlayerObjectId)
            {
                return false;
            }

            var u = targetObjectId;
            var nearby = snapshot.NearbyTargets.Targets.FirstOrDefault(item => item.IsAvailable && item.ObjectId == u);

            if (nearby != null)
            {
                targetName = FirstNonEmpty(
                    targetName,
                    nearby.DisplayName,
                    nearby.Name,
                    nearby.Mob.Name);
                targetLevel = nearby.Mob.CombatLevel;
                targetClientObjectAddress = nearby.ClientObjectAddress;
                targetPosition = nearby.Mob.Spatial.IsAvailable
                    ? nearby.Mob.Spatial.Position
                    : null;
            }

            if (snapshot.Target is
                {
                    IsAvailable: true,
                    HasTarget: true,
                } selected &&
                selected.ObjectId == targetObjectId)
            {
                targetName = FirstNonEmpty(targetName, selected.Name);
                targetLevel ??= selected.Operational.Identity.CombatLevel;
                targetClientObjectAddress = targetClientObjectAddress != 0
                    ? targetClientObjectAddress
                    : selected.ClientObjectAddress;
            }

            targetName = string.IsNullOrWhiteSpace(targetName)
                ? string.Concat("Target ", targetObjectId)
                : targetName.Trim();
            return true;
        }

        private static string BuildWorldKey(JournalLocationContext location) =>
            string.Concat(
                location.SystemName,
                "\u001f",
                location.SectorName,
                "\u001f",
                location.StarbaseName);
    }

    private readonly record struct CorpseInstanceKey(
        uint ObjectId,
        uint ClientObjectAddress);

    private sealed class EncounterAccumulator
    {
        private double outgoingDamage;
        private double incomingDamage;
        private int outgoingHitCount;
        private int incomingHitCount;
        private int outgoingCriticalCount;
        private int incomingCriticalCount;
        private float largestOutgoingHit;
        private float largestIncomingHit;
        private JournalLocationContext location;
        private uint targetClientObjectAddress;
        private ClientSpatialPosition? targetPosition;

        public EncounterAccumulator(
            string encounterId,
            uint characterId,
            string pilotName,
            uint targetObjectId,
            string targetName,
            int? targetCombatLevel,
            uint targetClientObjectAddress,
            ClientSpatialPosition? targetPosition,
            DateTimeOffset startedAt,
            JournalLocationContext location)
        {
            this.EncounterId = encounterId;
            this.CharacterId = characterId;
            this.PilotName = pilotName;
            this.TargetObjectId = targetObjectId;
            this.TargetName = targetName;
            this.TargetCombatLevel = targetCombatLevel;
            this.targetClientObjectAddress = targetClientObjectAddress;
            this.targetPosition = targetPosition;
            this.StartedAt = startedAt;
            this.LastEventAt = startedAt;
            this.LastIncomingAt = DateTimeOffset.MinValue;
            this.location = location;
        }

        public string EncounterId { get; }
        public uint CharacterId { get; }
        public string PilotName { get; }
        public uint TargetObjectId { get; }
        public string TargetName { get; private set; }
        public int? TargetCombatLevel { get; private set; }
        public DateTimeOffset StartedAt { get; }
        public DateTimeOffset LastEventAt { get; private set; }
        public DateTimeOffset LastIncomingAt { get; private set; }
        public int OutgoingHitCount => this.outgoingHitCount;
        public int IncomingHitCount => this.incomingHitCount;
        public uint TargetClientObjectAddress =>
            this.targetClientObjectAddress;
        public ClientSpatialPosition? TargetPosition => this.targetPosition;

        public void UpdateIdentity(
            string? targetName,
            int? targetLevel,
            uint targetClientObjectAddress,
            ClientSpatialPosition? targetPosition)
        {
            if (!string.IsNullOrWhiteSpace(targetName) &&
                (string.IsNullOrWhiteSpace(this.TargetName) ||
                 this.TargetName.StartsWith(
                     "Target ",
                     StringComparison.Ordinal)))
            {
                this.TargetName = targetName.Trim();
            }

            this.TargetCombatLevel ??= targetLevel;

            if (targetClientObjectAddress != 0)
            {
                this.targetClientObjectAddress = targetClientObjectAddress;
            }

            this.targetPosition = targetPosition ?? this.targetPosition;
        }

        public CombatJournalEvent Add(
            ClientCombatEventObservation observedEvent,
            CombatJournalDirection direction,
            string sourceName,
            string victimName,
            JournalLocationContext currentLocation,
            string pilotName)
        {
            this.LastEventAt = observedEvent.ObservedAt;
            this.location = currentLocation;
            var damage = Math.Max(0, observedEvent.Damage);

            if (direction == CombatJournalDirection.Outgoing)
            {
                this.outgoingDamage += damage;
                this.outgoingHitCount++;
                this.largestOutgoingHit = Math.Max(
                    this.largestOutgoingHit,
                    damage);

                if (observedEvent.IsCritical)
                {
                    this.outgoingCriticalCount++;
                }
            }
            else
            {
                this.incomingDamage += damage;
                this.incomingHitCount++;
                this.LastIncomingAt = observedEvent.ObservedAt;
                this.largestIncomingHit = Math.Max(
                    this.largestIncomingHit,
                    damage);

                if (observedEvent.IsCritical)
                {
                    this.incomingCriticalCount++;
                }
            }

            return new CombatJournalEvent
            {
                EncounterId = this.EncounterId,
                Sequence = observedEvent.Sequence,
                OccurredAt = observedEvent.ObservedAt,
                Direction = direction,
                Damage = damage,
                UnmodifiedDamage = observedEvent.UnmodifiedDamage,
                Modifier = observedEvent.Modifier,
                DamageType = observedEvent.KnownDamageType?.ToString() ??
                    "Unknown",
                IsCritical = observedEvent.IsCritical,
                SourceObjectId = observedEvent.SourceObjectId,
                SourceName = FirstNonEmpty(
                    sourceName,
                    direction == CombatJournalDirection.Outgoing
                        ? pilotName
                        : this.TargetName),
                VictimObjectId = observedEvent.VictimObjectId,
                VictimName = FirstNonEmpty(
                    victimName,
                    direction == CombatJournalDirection.Outgoing
                        ? this.TargetName
                        : pilotName),
            };
        }

        public CombatJournalEncounter End(
            CombatJournalOutcome outcome,
            DateTimeOffset occurredAt)
        {
            var endedAt = occurredAt < this.StartedAt
                ? this.LastEventAt
                : occurredAt;
            return this.ToRecord() with
            {
                Outcome = outcome,
                EndedAt = endedAt,
            };
        }

        public CombatJournalEncounter ToRecord()
        {
            return new CombatJournalEncounter
            {
                EncounterId = this.EncounterId,
                CharacterId = this.CharacterId,
                PilotName = this.PilotName,
                TargetObjectId = this.TargetObjectId,
                TargetName = this.TargetName,
                TargetCombatLevel = this.TargetCombatLevel,
                StartedAt = this.StartedAt,
                LastEventAt = this.LastEventAt,
                Outcome = CombatJournalOutcome.Active,
                SystemName = this.location.SystemName,
                SectorName = this.location.SectorName,
                StarbaseName = this.location.StarbaseName,
                NearestNavName = this.location.NearestNavName,
                OutgoingDamage = this.outgoingDamage,
                IncomingDamage = this.incomingDamage,
                OutgoingHitCount = this.outgoingHitCount,
                IncomingHitCount = this.incomingHitCount,
                OutgoingCriticalCount = this.outgoingCriticalCount,
                IncomingCriticalCount = this.incomingCriticalCount,
                LargestOutgoingHit = this.largestOutgoingHit,
                LargestIncomingHit = this.largestIncomingHit,
            };
        }
    }

    private static string ResolveActorName(
        ClientCombatActorIdentityObservation identity) =>
        FirstNonEmpty(
            identity.DisplayName,
            identity.Name,
            identity.Owner,
            identity.Title);

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim()
        ?? "";
}
