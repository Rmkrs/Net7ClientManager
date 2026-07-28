namespace Net7ClientManager.MissionJournal;

using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Net7ClientManager.Observations;
using Net7ClientManager.Observations.Models;

/// <summary>
/// Builds a durable mission lifecycle journal from the existing observation
/// stream. It does not add memory reads or change observer cadence. Mission
/// provenance is captured when a mission appears and restored conservatively
/// on later game or N7CM sessions; ambiguous matches are never guessed.
/// </summary>
internal sealed class MissionJournalCoordinator
{
    private static readonly TimeSpan recentNpcRetention =
        TimeSpan.FromSeconds(5);
    private static readonly TimeSpan recentJobTerminalRetention =
        TimeSpan.FromSeconds(10);
    private static readonly TimeSpan recentForfeitRetention =
        TimeSpan.FromSeconds(3);
    private static readonly TimeSpan missionArrayCollapseConfirmation =
        TimeSpan.FromSeconds(3);
    private static readonly TimeSpan batchRemovalConfirmation =
        TimeSpan.FromSeconds(3);
    private static readonly TimeSpan completionOutcomeWindow =
        TimeSpan.FromSeconds(5);
    private static readonly TimeSpan restoredMissionMissingConfirmation =
        TimeSpan.FromSeconds(3);
    private static readonly TimeSpan periodicPersistenceInterval =
        TimeSpan.FromMinutes(1);
    private const int MaximumKnownJobObjectives = 512;

    private readonly MissionJournalStore store;
    private readonly Lock stateLock = new();
    private readonly Dictionary<int, ProcessState> processStates = [];
    private bool retainFinishedHistory;

    public MissionJournalCoordinator(
        MissionJournalStore store,
        bool retainFinishedHistory)
    {
        this.store = store ??
            throw new ArgumentNullException(nameof(store));
        this.retainFinishedHistory = retainFinishedHistory;
    }

    public void SetRetainFinishedHistory(bool enabled)
    {
        Volatile.Write(ref this.retainFinishedHistory, enabled);
    }

    public event EventHandler<MissionJournalChangedEventArgs>?
        JournalChanged;

    internal event EventHandler<MissionJournalLifecycleEventArgs>?
        LifecycleEvent;

    public IReadOnlyList<MissionJournalEntry> GetHistory(
        uint characterId,
        int maximumResults = 1000) =>
        this.store.GetHistory(characterId, maximumResults);

    public DateTimeOffset? GetLastObservedAt(uint characterId) =>
        this.store.GetLastObservedAt(characterId);

    public void Observe(ClientObservationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        uint? changedCharacterId = null;

        try
        {
            lock (this.stateLock)
            {
                if (snapshot.LifecycleState != ClientLifecycleState.InGame)
                {
                    this.processStates.Remove(snapshot.ProcessId);
                    return;
                }

                var identity = ClientLiveCharacterIdentityResolver.Resolve(snapshot);

                if (!identity.IsAvailable ||
                    identity.CharacterObjectId is not { } characterId ||
                    string.IsNullOrWhiteSpace(identity.Name))
                {
                    return;
                }

                if (!this.processStates.TryGetValue(
                        snapshot.ProcessId,
                        out var state) ||
                    state.CharacterId != characterId)
                {
                    state = new ProcessState(
                        characterId,
                        identity.Name.Trim(),
                        this.store.GetActiveEntries(characterId));
                    this.processStates[snapshot.ProcessId] = state;
                }
                else
                {
                    state.PilotName = identity.Name.Trim();
                }

                this.ObserveSourceContext(snapshot, state);

                if (snapshot.LoadingOrTransitionFlag != 0 ||
                    !snapshot.LocalPlayer.Missions.IsAvailable ||
                    snapshot.LastSlowFeatureObservedAt == null ||
                    snapshot.LastSlowFeatureObservedAt ==
                        state.LastProcessedSlowObservedAt)
                {
                    return;
                }

                state.LastProcessedSlowObservedAt =
                    snapshot.LastSlowFeatureObservedAt;

                var rawMissions = snapshot.LocalPlayer.Missions.Missions
                    .Where(mission => mission.ValidState > 0)
                    .OrderBy(mission => mission.Slot)
                    .ToArray();
                var missions = rawMissions
                    .Where(IsUsableMission)
                    .ToArray();
                // Never expire restored episodes from a partial mission
                // sample. A valid-but-incomplete row can settle on a later
                // ordinary slow observation.
                var missionSetIsComplete =
                    rawMissions.Length == missions.Length;

                if (rawMissions.Length == 0 &&
                    (state.Episodes.Count > 0 ||
                     state.UnboundActiveEntries.Count > 0) &&
                    !state.HasRecentForfeitCue(snapshot.ObservedAt))
                {
                    state.CollapseStartedAt ??= snapshot.ObservedAt;

                    if (snapshot.ObservedAt - state.CollapseStartedAt.Value <
                        missionArrayCollapseConfirmation)
                    {
                        return;
                    }
                }
                else
                {
                    state.CollapseStartedAt = null;
                }

                if (!state.HasBaseline && !missionSetIsComplete)
                {
                    return;
                }

                var currentOutcome = CaptureOutcome(snapshot);
                var changed = this.ProcessMissionSnapshot(
                    snapshot,
                    state,
                    rawMissions,
                    missions,
                    missionSetIsComplete);
                changed |= this.ResolvePendingRemovals(
                    snapshot,
                    state,
                    currentOutcome,
                    missionSetIsComplete);

                if (missionSetIsComplete)
                {
                    changed |= this.ResolveMissingRestoredEpisodes(
                        snapshot,
                        state);
                }
                state.LastOutcome = currentOutcome;
                state.HasBaseline = true;

                if (changed)
                {
                    changedCharacterId = characterId;
                }
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine(
                string.Concat(
                    "Mission Journal observation failed: ",
                    exception),
                "Net7.MissionJournal");
            return;
        }

        if (changedCharacterId.HasValue)
        {
            this.JournalChanged?.Invoke(
                this,
                new MissionJournalChangedEventArgs(
                    changedCharacterId.Value));
        }
    }

    public void ForgetProcess(int processId)
    {
        lock (this.stateLock)
        {
            this.processStates.Remove(processId);
        }
    }

    public bool TryGetActiveContext(
        int processId,
        uint missionAddress,
        out MissionJournalActiveContext context)
    {
        context = null!;

        if (missionAddress == 0)
        {
            return false;
        }

        lock (this.stateLock)
        {
            if (!this.processStates.TryGetValue(processId, out var state))
            {
                return false;
            }

            var episode = state.Episodes.Values.FirstOrDefault(candidate =>
                candidate.Mission.Address == missionAddress);

            if (episode == null)
            {
                return false;
            }

            context = new MissionJournalActiveContext
            {
                EpisodeId = episode.Entry.EpisodeId,
                Source = episode.Entry.Source,
                JobId = episode.Entry.JobId,
                JobCategory = episode.Entry.JobCategory,
                RewardText = episode.Entry.RewardText,
                AcceptedAt = episode.Entry.AcceptedAt,
                AcceptedSystem = episode.Entry.AcceptedSystem,
                AcceptedSector = episode.Entry.AcceptedSector,
                AcceptedStarbase = episode.Entry.AcceptedStarbase,
            };
            return true;
        }
    }

    private bool ProcessMissionSnapshot(
        ClientObservationSnapshot snapshot,
        ProcessState state,
        IReadOnlyList<ClientMissionObservation> rawMissions,
        IReadOnlyList<ClientMissionObservation> missions,
        bool missionSetIsComplete)
    {
        var rawBySlot = rawMissions.ToDictionary(mission => mission.Slot);
        var removedEpisodes = state.Episodes.Values
            .Where(existing =>
            {
                if (!rawBySlot.TryGetValue(existing.Mission.Slot, out var current))
                {
                    return true;
                }

                return IsUsableMission(current) &&
                       !IsSameMissionEpisode(existing.Mission, current);
            })
            .ToArray();

        if (removedEpisodes.Length > 1)
        {
            state.BatchRemovalStartedAt ??= snapshot.ObservedAt;

            if (snapshot.ObservedAt - state.BatchRemovalStartedAt.Value <
                batchRemovalConfirmation)
            {
                return false;
            }
        }
        else
        {
            state.BatchRemovalStartedAt = null;
        }

        var changed = false;

        foreach (var removed in removedEpisodes)
        {
            state.Episodes.Remove(removed.Mission.Slot);
            changed |= this.HandleRemoval(
                snapshot,
                state,
                removed,
                state.LastOutcome,
                removedEpisodes.Length == 1);
        }

        foreach (var mission in missions)
        {
            if (state.Episodes.TryGetValue(mission.Slot, out var episode) &&
                IsSameMissionEpisode(episode.Mission, mission))
            {
                changed |= this.UpdateEpisode(
                    snapshot,
                    state,
                    episode,
                    mission);
                continue;
            }

            var restoredPending = state.TakePendingRemoval(mission);

            if (restoredPending != null)
            {
                var restoredEpisode = new LiveEpisode(
                    restoredPending.Entry with
                    {
                        Status = MissionJournalStatus.Active,
                        EndedAt = null,
                        CompletionSystem = "",
                        CompletionSector = "",
                        CompletionStarbase = "",
                    },
                    mission,
                    snapshot.ObservedAt);
                state.Episodes[mission.Slot] = restoredEpisode;
                changed |= this.UpdateEpisode(
                    snapshot,
                    state,
                    restoredEpisode,
                    mission,
                    forceSave: true);
                continue;
            }

            var restored = state.TakeRestoredEntry(mission);

            if (restored != null)
            {
                var restoredEpisode = new LiveEpisode(
                    ApplyMission(restored, mission, snapshot.ObservedAt),
                    mission,
                    snapshot.ObservedAt);
                state.Episodes[mission.Slot] = restoredEpisode;
                this.store.SaveEpisode(restoredEpisode.Entry);
                changed = true;
                continue;
            }

            var isObservedAcceptance = state.HasBaseline;
            var newEpisode = this.CreateEpisode(
                snapshot,
                state,
                mission,
                isObservedAcceptance);
            state.Episodes[mission.Slot] = newEpisode;
            changed = true;
        }

        if (missionSetIsComplete)
        {
            foreach (var restored in state.UnboundActiveEntries)
            {
                state.MissingRestoredEpisodes.TryAdd(
                    restored.EpisodeId,
                    snapshot.ObservedAt);
            }
        }

        return changed;
    }

    private LiveEpisode CreateEpisode(
        ClientObservationSnapshot snapshot,
        ProcessState state,
        ClientMissionObservation mission,
        bool acceptedNow)
    {
        var descriptor = state.ResolveJobDescriptor(mission);
        var source = descriptor != null
            ? MissionJournalSource.JobTerminal
            : acceptedNow
                ? state.ResolveSource(snapshot.ObservedAt)
                : MissionJournalSource.Unknown;
        var jobTerminal = source == MissionJournalSource.JobTerminal
            ? state.GetRecentJobTerminal(snapshot.ObservedAt)
            : null;
        var jobCategory = descriptor?.Category ??
            jobTerminal?.Category ??
            MissionJournalJobCategory.Unknown;
        var npc = source == MissionJournalSource.Npc
            ? state.GetRecentNpc(snapshot.ObservedAt)
            : null;
        var acceptedAt = acceptedNow
            ? snapshot.ObservedAt
            : (DateTimeOffset?)null;
        var location = CaptureLocation(snapshot);
        var acceptedLocation = source switch
        {
            MissionJournalSource.JobTerminal when jobTerminal != null =>
                new LocationContext(
                    jobTerminal.SystemName,
                    jobTerminal.SectorName,
                    jobTerminal.StarbaseName),
            MissionJournalSource.Npc when npc != null =>
                new LocationContext(
                    npc.SystemName,
                    npc.SectorName,
                    npc.StarbaseName),
            MissionJournalSource.JobTerminal or
            MissionJournalSource.Npc =>
                new LocationContext("", "", ""),
            _ => location,
        };
        var objective = GetCurrentObjective(mission);
        var entry = new MissionJournalEntry
        {
            EpisodeId = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture),
            CharacterId = state.CharacterId,
            PilotName = state.PilotName,
            Source = source,
            Status = MissionJournalStatus.Active,
            JobId = descriptor?.JobId ??
                (jobTerminal?.JobId is > 0
                    ? jobTerminal.JobId
                    : null),
            JobCategory = jobCategory,
            MissionRawId = mission.RawId,
            MissionStartTime = mission.StartTime,
            SemanticFingerprint = ComputeSemanticFingerprint(mission),
            Name = mission.Name.Trim(),
            Summary = mission.Summary.Trim(),
            RewardText = FirstAvailable(
                mission.Reward,
                descriptor?.Reward,
                jobTerminal?.Reward),
            FailureConsequence = mission.FailureConsequence.Trim(),
            IssuingFaction = mission.IssuingFaction.Trim(),
            AcceptedAt = acceptedAt,
            FirstObservedAt = snapshot.ObservedAt,
            LastObservedAt = snapshot.ObservedAt,
            Stage = mission.Stage,
            StageCount = mission.StageCount,
            CurrentObjective = objective,
            AcceptedSystem = acceptedNow
                ? acceptedLocation.SystemName
                : "",
            AcceptedSector = acceptedNow
                ? acceptedLocation.SectorName
                : "",
            AcceptedStarbase = acceptedNow
                ? acceptedLocation.StarbaseName
                : "",
            IssuerNpcName = npc?.Name ?? "",
        };
        var eventKind = acceptedNow
            ? MissionJournalEventKind.Accepted
            : MissionJournalEventKind.Observed;
        var details = acceptedNow
            ? BuildAcceptanceDetails(source, descriptor, npc)
            : descriptor != null
                ? "Identified as a Job Terminal assignment."
                : "Mission first observed while already active.";
        this.store.SaveEpisode(entry, eventKind, details);

        if (eventKind == MissionJournalEventKind.Accepted)
        {
            this.PublishLifecycleEvent(
                entry,
                eventKind,
                acceptedAt ?? snapshot.ObservedAt,
                acceptedLocation);
        }

        return new LiveEpisode(entry, mission, snapshot.ObservedAt);
    }

    private bool UpdateEpisode(
        ClientObservationSnapshot snapshot,
        ProcessState state,
        LiveEpisode episode,
        ClientMissionObservation mission,
        bool forceSave = false)
    {
        var previous = episode.Entry;
        var previousObjective = previous.CurrentObjective;
        var previousStage = previous.Stage;
        var updated = ApplyMission(previous, mission, snapshot.ObservedAt) with
        {
            PilotName = state.PilotName,
        };

        var sourceIdentified = false;
        var sourceDetails = "";
        var descriptor = state.ResolveJobDescriptor(mission);

        if (updated.Source == MissionJournalSource.JobTerminal &&
            descriptor != null)
        {
            updated = updated with
            {
                JobId = updated.JobId ?? descriptor.JobId,
                JobCategory = updated.JobCategory ==
                    MissionJournalJobCategory.Unknown
                        ? descriptor.Category
                        : updated.JobCategory,
                RewardText = FirstAvailable(
                    updated.RewardText,
                    descriptor.Reward),
            };
        }

        if (updated.Source == MissionJournalSource.Unknown)
        {
            if (descriptor != null)
            {
                updated = updated with
                {
                    Source = MissionJournalSource.JobTerminal,
                    JobId = descriptor.JobId,
                    JobCategory = descriptor.Category,
                    RewardText = FirstAvailable(
                        updated.RewardText,
                        descriptor.Reward),
                };
                sourceIdentified = true;
                sourceDetails = "Identified as a Job Terminal assignment.";
            }
            else if (updated.AcceptedAt.HasValue &&
                     snapshot.ObservedAt - updated.AcceptedAt.Value <=
                         recentJobTerminalRetention)
            {
                var source = state.ResolveSource(snapshot.ObservedAt);

                if (source != MissionJournalSource.Unknown)
                {
                    var npc = source == MissionJournalSource.Npc
                        ? state.GetRecentNpc(snapshot.ObservedAt)
                        : null;
                    var jobTerminal = source ==
                        MissionJournalSource.JobTerminal
                            ? state.GetRecentJobTerminal(snapshot.ObservedAt)
                            : null;
                    var location = source switch
                    {
                        MissionJournalSource.JobTerminal when
                            jobTerminal != null =>
                            new LocationContext(
                                jobTerminal.SystemName,
                                jobTerminal.SectorName,
                                jobTerminal.StarbaseName),
                        MissionJournalSource.Npc when npc != null =>
                            new LocationContext(
                                npc.SystemName,
                                npc.SectorName,
                                npc.StarbaseName),
                        _ => CaptureLocation(snapshot),
                    };
                    updated = updated with
                    {
                        Source = source,
                        JobId = jobTerminal?.JobId is > 0
                            ? jobTerminal.JobId
                            : updated.JobId,
                        JobCategory = jobTerminal?.Category ??
                            MissionJournalJobCategory.Unknown,
                        RewardText = FirstAvailable(
                            updated.RewardText,
                            jobTerminal?.Reward),
                        AcceptedSystem = location.SystemName,
                        AcceptedSector = location.SectorName,
                        AcceptedStarbase = location.StarbaseName,
                        IssuerNpcName = npc?.Name ?? "",
                    };
                    sourceIdentified = true;
                    sourceDetails = BuildAcceptanceDetails(
                        source,
                        descriptor: null,
                        npc);
                }
            }
        }

        var progressChanged = previousStage != updated.Stage ||
            !string.Equals(
                previousObjective,
                updated.CurrentObjective,
                StringComparison.Ordinal);
        var contentChanged = !EqualsIgnoringObservationTime(previous, updated);
        var periodicSave = snapshot.ObservedAt - episode.LastStoredAt >=
            periodicPersistenceInterval;

        episode.Entry = updated;
        episode.Mission = mission;

        if (!forceSave && !contentChanged && !periodicSave)
        {
            return false;
        }

        this.store.SaveEpisode(
            updated,
            sourceIdentified
                ? MissionJournalEventKind.SourceIdentified
                : progressChanged
                    ? MissionJournalEventKind.Progressed
                    : null,
            sourceIdentified
                ? sourceDetails
                : progressChanged
                    ? "The mission step or objective changed."
                    : "");

        if (progressChanged)
        {
            this.PublishLifecycleEvent(
                updated,
                MissionJournalEventKind.Progressed,
                snapshot.ObservedAt,
                CaptureLocation(snapshot));
        }

        episode.LastStoredAt = snapshot.ObservedAt;
        return contentChanged || progressChanged;
    }

    private bool HandleRemoval(
        ClientObservationSnapshot snapshot,
        ProcessState state,
        LiveEpisode episode,
        PilotOutcome baselineOutcome,
        bool allowForfeitCue)
    {
        var mission = episode.Mission;

        if (allowForfeitCue &&
            state.HasRecentForfeitCue(snapshot.ObservedAt))
        {
            this.EndEpisode(
                snapshot,
                episode.Entry,
                MissionJournalStatus.Forfeited,
                MissionJournalEventKind.Forfeited,
                "The game reported that the mission was forfeited.");
            return true;
        }

        if (mission.IsExpired == true)
        {
            this.EndEpisode(
                snapshot,
                episode.Entry,
                MissionJournalStatus.Expired,
                MissionJournalEventKind.Expired,
                "The mission expired.");
            return true;
        }

        if (mission.IsFailed == true)
        {
            this.EndEpisode(
                snapshot,
                episode.Entry,
                MissionJournalStatus.Failed,
                MissionJournalEventKind.Failed,
                "The mission ended in failure.");
            return true;
        }

        if (mission.IsComplete == true)
        {
            this.EndEpisode(
                snapshot,
                episode.Entry,
                MissionJournalStatus.Completed,
                MissionJournalEventKind.Completed,
                "Mission completed.");
            return true;
        }

        state.PendingRemovals.Add(
            new PendingRemoval(
                episode.Entry,
                baselineOutcome,
                snapshot.ObservedAt + completionOutcomeWindow));
        return false;
    }

    private bool ResolvePendingRemovals(
        ClientObservationSnapshot snapshot,
        ProcessState state,
        PilotOutcome currentOutcome,
        bool missionSetIsComplete)
    {
        if (state.PendingRemovals.Count == 0)
        {
            return false;
        }

        var changed = false;
        var canInferCompletion = state.PendingRemovals.Count == 1;

        foreach (var pending in state.PendingRemovals.ToArray())
        {
            if (canInferCompletion &&
                HasPositiveOutcomeDelta(pending.BaselineOutcome, currentOutcome))
            {
                this.EndEpisode(
                    snapshot,
                    pending.Entry,
                    MissionJournalStatus.Completed,
                    MissionJournalEventKind.Completed,
                    "Mission completed.");
                state.PendingRemovals.Remove(pending);
                changed = true;
                continue;
            }

            if (!missionSetIsComplete ||
                snapshot.ObservedAt < pending.ExpiresAt)
            {
                continue;
            }

            this.EndEpisode(
                snapshot,
                pending.Entry,
                MissionJournalStatus.NoLongerActive,
                MissionJournalEventKind.NoLongerActive,
                "The mission is no longer active.");
            state.PendingRemovals.Remove(pending);
            changed = true;
        }

        return changed;
    }

    private bool ResolveMissingRestoredEpisodes(
        ClientObservationSnapshot snapshot,
        ProcessState state)
    {
        if (state.UnboundActiveEntries.Count == 0 ||
            state.MissingRestoredEpisodes.Count == 0)
        {
            return false;
        }

        var changed = false;

        foreach (var entry in state.UnboundActiveEntries.ToArray())
        {
            if (!state.MissingRestoredEpisodes.TryGetValue(
                    entry.EpisodeId,
                    out var missingSince) ||
                snapshot.ObservedAt - missingSince <
                    restoredMissionMissingConfirmation)
            {
                continue;
            }

            this.EndEpisode(
                snapshot,
                entry,
                MissionJournalStatus.NoLongerActive,
                MissionJournalEventKind.NoLongerActive,
                "The mission is no longer active.");
            state.UnboundActiveEntries.Remove(entry);
            state.MissingRestoredEpisodes.Remove(entry.EpisodeId);
            changed = true;
        }

        return changed;
    }

    private void EndEpisode(
        ClientObservationSnapshot snapshot,
        MissionJournalEntry entry,
        MissionJournalStatus status,
        MissionJournalEventKind eventKind,
        string details)
    {
        var location = status == MissionJournalStatus.NoLongerActive
            ? new LocationContext("", "", "")
            : CaptureLocation(snapshot);
        var ended = entry with
        {
            Status = status,
            LastObservedAt = status ==
                MissionJournalStatus.NoLongerActive
                    ? entry.LastObservedAt
                    : snapshot.ObservedAt,
            EndedAt = snapshot.ObservedAt,
            CompletionSystem = location.SystemName,
            CompletionSector = location.SectorName,
            CompletionStarbase = location.StarbaseName,
        };

        if (Volatile.Read(ref this.retainFinishedHistory))
        {
            this.store.SaveEpisode(ended, eventKind, details);
        }
        else
        {
            // Active episodes stay durable for restart-stable job guidance.
            // When history recording is off, discard the episode only after it ends.
            this.store.DeleteEpisode(entry.EpisodeId);
        }

        this.PublishLifecycleEvent(
            ended,
            eventKind,
            snapshot.ObservedAt,
            location);
    }

    private void PublishLifecycleEvent(
        MissionJournalEntry entry,
        MissionJournalEventKind kind,
        DateTimeOffset occurredAt,
        LocationContext location)
    {
        try
        {
            this.LifecycleEvent?.Invoke(
                this,
                new MissionJournalLifecycleEventArgs
                {
                    Entry = entry,
                    Kind = kind,
                    OccurredAt = occurredAt,
                    SystemName = location.SystemName,
                    SectorName = location.SectorName,
                    StarbaseName = location.StarbaseName,
                });
        }
        catch (Exception exception)
        {
            Debug.WriteLine(
                string.Concat(
                    "Mission Journal lifecycle event failed: ",
                    exception),
                "Net7.MissionJournal");
        }
    }

    private void ObserveSourceContext(
        ClientObservationSnapshot snapshot,
        ProcessState state)
    {
        if (snapshot.AudioCue.IsCurrent &&
            string.Equals(
                NormalizeCompact(snapshot.AudioCue.ResourceName),
                "MISSIONFORFEITED",
                StringComparison.Ordinal))
        {
            state.LastForfeitCueAt = snapshot.ObservedAt;
        }

        this.ObserveKnownJobDescriptions(snapshot, state);
        var interaction = snapshot.StarbaseContext.Interaction;

        if (interaction.IsActive &&
            interaction.Kind == ClientStarbaseInteractionKind.JobsTerminal)
        {
            var location = CaptureLocation(snapshot);
            state.RememberJobTerminalContext(
                snapshot.JobTerminal.SelectedJobId,
                MapJobCategory(snapshot.JobTerminal.SelectedCategory),
                FirstAvailable(
                    snapshot.JobTerminal.SelectedDescription?.Reward,
                    snapshot.JobTerminal.SelectedOffer?.Reward),
                location.SystemName,
                location.SectorName,
                location.StarbaseName,
                snapshot.ObservedAt);
            return;
        }

        if (!interaction.IsActive ||
            interaction.Kind != ClientStarbaseInteractionKind.TalkTree ||
            interaction.NpcSlot < 0)
        {
            return;
        }

        var room = snapshot.StarbaseContext.CurrentRoom;
        var npc = room?.Npcs.FirstOrDefault(candidate =>
            candidate.Slot == interaction.NpcSlot);

        if (npc == null || string.IsNullOrWhiteSpace(npc.Name))
        {
            return;
        }

        var location2 = CaptureLocation(snapshot);
        state.RecentNpc = new NpcContext(
            npc.Name.Trim(),
            location2.SystemName,
            location2.SectorName,
            location2.StarbaseName,
            snapshot.ObservedAt);
    }

    private void ObserveKnownJobDescriptions(
        ClientObservationSnapshot snapshot,
        ProcessState state)
    {
        if (!snapshot.JobTerminal.IsAvailable)
        {
            return;
        }

        var offers = snapshot.JobTerminal.Offers
            .Where(offer => offer.JobId != 0)
            .GroupBy(offer => offer.JobId)
            .ToDictionary(group => group.Key, group => group.Last());

        foreach (var description in snapshot.JobTerminal.Descriptions
                     .Where(description =>
                         description.StillAvailable &&
                         description.JobId != 0 &&
                         !string.IsNullOrWhiteSpace(description.Title) &&
                         !string.IsNullOrWhiteSpace(description.Description)))
        {
            offers.TryGetValue(description.JobId, out var offer);
            state.RememberJobDescriptor(
                CreateMissionObjectiveKey(
                    description.Title,
                    description.Description),
                new JobDescriptor(
                    description.JobId,
                    MapJobCategory(
                        offer?.Category ?? ClientJobCategory.Unknown),
                    FirstAvailable(
                        description.Reward,
                        offer?.Reward)));
        }
    }

    private static MissionJournalEntry ApplyMission(
        MissionJournalEntry entry,
        ClientMissionObservation mission,
        DateTimeOffset observedAt)
    {
        return entry with
        {
            MissionRawId = entry.MissionRawId ?? mission.RawId,
            MissionStartTime = entry.MissionStartTime ?? mission.StartTime,
            Name = PreferRich(entry.Name, mission.Name),
            Summary = PreferRich(entry.Summary, mission.Summary),
            RewardText = PreferRich(entry.RewardText, mission.Reward),
            FailureConsequence = PreferRich(
                entry.FailureConsequence,
                mission.FailureConsequence),
            IssuingFaction = PreferRich(
                entry.IssuingFaction,
                mission.IssuingFaction),
            LastObservedAt = observedAt,
            Stage = mission.Stage ?? entry.Stage,
            StageCount = mission.StageCount ?? entry.StageCount,
            CurrentObjective = PreferCurrentObjective(
                entry.CurrentObjective,
                GetCurrentObjective(mission)),
        };
    }

    private static bool EqualsIgnoringObservationTime(
        MissionJournalEntry left,
        MissionJournalEntry right)
    {
        return left with { LastObservedAt = right.LastObservedAt } == right;
    }

    private static string PreferRich(string existing, string incoming) =>
        !string.IsNullOrWhiteSpace(incoming)
            ? incoming.Trim()
            : existing;

    private static string PreferCurrentObjective(
        string existing,
        string incoming) =>
        !string.IsNullOrWhiteSpace(incoming)
            ? incoming.Trim()
            : existing;

    private static string FirstAvailable(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            ?.Trim() ?? "";

    private static string GetCurrentObjective(
        ClientMissionObservation mission)
    {
        return !string.IsNullOrWhiteSpace(mission.CurrentStageText)
            ? mission.CurrentStageText.Trim()
            : mission.Summary.Trim();
    }

    private static bool IsUsableMission(ClientMissionObservation mission)
    {
        return mission.ValidState > 0 &&
               mission.StageCount is > 0 and <= 20 &&
               !string.IsNullOrWhiteSpace(mission.Name) &&
               !string.IsNullOrWhiteSpace(mission.Summary) &&
               mission.Stages.Any(stage =>
                   stage.Index == 0 &&
                   !string.IsNullOrWhiteSpace(stage.Text));
    }

    private static bool IsSameMissionEpisode(
        ClientMissionObservation existing,
        ClientMissionObservation current)
    {
        if (!string.Equals(
                NormalizeText(existing.Name),
                NormalizeText(current.Name),
                StringComparison.Ordinal) ||
            existing.StageCount != current.StageCount ||
            (existing.StartTime.HasValue &&
             current.StartTime.HasValue &&
             existing.StartTime.Value != current.StartTime.Value))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(existing.Summary) &&
            !string.IsNullOrWhiteSpace(current.Summary) &&
            !string.Equals(
                NormalizeText(existing.Summary),
                NormalizeText(current.Summary),
                StringComparison.Ordinal))
        {
            return false;
        }

        var existingFirstStage = existing.Stages
            .FirstOrDefault(stage => stage.Index == 0)?.Text ?? "";
        var currentFirstStage = current.Stages
            .FirstOrDefault(stage => stage.Index == 0)?.Text ?? "";
        return string.IsNullOrWhiteSpace(existingFirstStage) ||
               string.IsNullOrWhiteSpace(currentFirstStage) ||
               string.Equals(
                   NormalizeText(existingFirstStage),
                   NormalizeText(currentFirstStage),
                   StringComparison.Ordinal);
    }

    private static string ComputeSemanticFingerprint(
        ClientMissionObservation mission)
    {
        var firstStage = mission.Stages
            .OrderBy(stage => stage.Index)
            .Select(stage => stage.Text.Trim())
            .FirstOrDefault(text => text.Length > 0) ?? "";
        var source = string.Join(
            "|",
            NormalizeText(mission.Name),
            NormalizeText(mission.Summary),
            (mission.StageCount ?? mission.StageCapacity)
                .ToString(CultureInfo.InvariantCulture),
            NormalizeText(firstStage));
        return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(source)))
            .ToLowerInvariant();
    }

    private static string CreateMissionObjectiveKey(
        string? missionName,
        string? objective)
    {
        return string.Concat(
            NormalizeCompact(missionName),
            "|",
            NormalizeCompact(objective));
    }

    private static string NormalizeCompact(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? ""
            : string.Concat(value.Where(char.IsLetterOrDigit))
                .ToUpperInvariant();
    }

    private static string NormalizeText(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? ""
            : string.Join(
                " ",
                value.Trim()
                    .ToLowerInvariant()
                    .Split(
                        (char[]?)null,
                        StringSplitOptions.RemoveEmptyEntries));

    private static MissionJournalJobCategory MapJobCategory(
        ClientJobCategory category) => category switch
        {
            ClientJobCategory.Combat => MissionJournalJobCategory.Combat,
            ClientJobCategory.Trade => MissionJournalJobCategory.Trade,
            ClientJobCategory.Explore => MissionJournalJobCategory.Explore,
            _ => MissionJournalJobCategory.Unknown,
        };

    private static string BuildAcceptanceDetails(
        MissionJournalSource source,
        JobDescriptor? descriptor,
        NpcContext? npc)
    {
        return source switch
        {
            MissionJournalSource.JobTerminal when descriptor != null =>
                "Accepted from a Job Terminal.",
            MissionJournalSource.JobTerminal =>
                "Accepted from a Job Terminal.",
            MissionJournalSource.Npc when npc != null =>
                string.Concat("Accepted from ", npc.Name, "."),
            MissionJournalSource.Npc =>
                "Accepted from an NPC.",
            _ => string.Concat(
                "The mission appeared in the active log, but its source could ",
                "not be established reliably."),
        };
    }

    private static LocationContext CaptureLocation(
        ClientObservationSnapshot snapshot) =>
        new(
            snapshot.World.CurrentSystemName.Trim(),
            snapshot.World.CurrentSectorName.Trim(),
            snapshot.World.CurrentStarbaseName.Trim());

    private static PilotOutcome CaptureOutcome(
        ClientObservationSnapshot snapshot)
    {
        var player = snapshot.LocalPlayer;
        Dictionary<string, float> reputation =
            new(StringComparer.Ordinal);
        Dictionary<int, int> rewardItems = [];

        foreach (var faction in player.Reputation.Factions.Where(faction =>
                     !string.IsNullOrWhiteSpace(faction.FactionKey) &&
                     faction.Reaction.HasValue))
        {
            reputation[faction.FactionKey] = faction.Reaction!.Value;
        }

        if (player.RewardOverflowInventory.IsAvailable &&
            player.RewardOverflowInventory.RewardUnavailableSlotCount == 0 &&
            player.RewardOverflowInventory.OverflowUnavailableSlotCount == 0)
        {
            AddItemCounts(
                rewardItems,
                player.RewardOverflowInventory.RewardItems);
            AddItemCounts(
                rewardItems,
                player.RewardOverflowInventory.OverflowItems);
        }

        return new PilotOutcome(
            player.CharacterDetails.Credits,
            ExperienceTrack.From(player.CharacterProgression.Combat),
            ExperienceTrack.From(player.CharacterProgression.Explore),
            ExperienceTrack.From(player.CharacterProgression.Trade),
            reputation,
            rewardItems);
    }

    private static void AddItemCounts(
        Dictionary<int, int> target,
        IEnumerable<ClientInventoryItemObservation> items)
    {
        foreach (var item in items)
        {
            if (item.ItemTemplateId is not > 0)
            {
                continue;
            }

            var quantity = Math.Max(1, item.StackCount ?? 1);
            target[item.ItemTemplateId.Value] =
                target.GetValueOrDefault(item.ItemTemplateId.Value) +
                quantity;
        }
    }

    private static bool HasPositiveOutcomeDelta(
        PilotOutcome previous,
        PilotOutcome current)
    {
        if (previous.Credits.HasValue && current.Credits.HasValue &&
            current.Credits.Value > previous.Credits.Value)
        {
            return true;
        }

        if (CalculateExperienceDelta(previous.Combat, current.Combat) > 0 ||
            CalculateExperienceDelta(previous.Explore, current.Explore) > 0 ||
            CalculateExperienceDelta(previous.Trade, current.Trade) > 0)
        {
            return true;
        }

        foreach (var pair in current.Reputation)
        {
            if (previous.Reputation.TryGetValue(pair.Key, out var oldValue) &&
                pair.Value - oldValue > 0.0001f)
            {
                return true;
            }
        }

        return current.RewardItems.Any(pair =>
            pair.Value > previous.RewardItems.GetValueOrDefault(pair.Key));
    }

    private static int CalculateExperienceDelta(
        ExperienceTrack previous,
        ExperienceTrack current)
    {
        if (!previous.Level.HasValue ||
            !current.Level.HasValue ||
            !previous.Earned.HasValue ||
            !current.Earned.HasValue)
        {
            return 0;
        }

        if (current.Level == previous.Level)
        {
            return Math.Max(0, current.Earned.Value - previous.Earned.Value);
        }

        if (current.Level == previous.Level + 1 && previous.Required.HasValue)
        {
            return Math.Max(
                0,
                previous.Required.Value - previous.Earned.Value +
                current.Earned.Value);
        }

        return 0;
    }

    private sealed class ProcessState
    {
        private readonly Queue<string> knownJobOrder = new();
        private readonly Dictionary<string, JobDescriptor> knownJobs =
            new(StringComparer.Ordinal);

        public ProcessState(
            uint characterId,
            string pilotName,
            IReadOnlyList<MissionJournalEntry> activeEntries)
        {
            this.CharacterId = characterId;
            this.PilotName = pilotName;
            this.UnboundActiveEntries.AddRange(activeEntries);
        }

        public uint CharacterId { get; }

        public string PilotName { get; set; }

        public DateTimeOffset? LastProcessedSlowObservedAt { get; set; }

        public JobTerminalContext? RecentJobTerminal { get; set; }

        public DateTimeOffset? LastForfeitCueAt { get; set; }

        public DateTimeOffset? CollapseStartedAt { get; set; }

        public DateTimeOffset? BatchRemovalStartedAt { get; set; }

        public NpcContext? RecentNpc { get; set; }

        public bool HasBaseline { get; set; }

        public PilotOutcome LastOutcome { get; set; } = PilotOutcome.Empty;

        public Dictionary<int, LiveEpisode> Episodes { get; } = [];

        public List<MissionJournalEntry> UnboundActiveEntries { get; } = [];

        public Dictionary<string, DateTimeOffset> MissingRestoredEpisodes
        { get; } = new(StringComparer.Ordinal);

        public List<PendingRemoval> PendingRemovals { get; } = [];

        public bool HasRecentForfeitCue(DateTimeOffset now) =>
            this.LastForfeitCueAt.HasValue &&
            now - this.LastForfeitCueAt.Value <= recentForfeitRetention;

        public NpcContext? GetRecentNpc(DateTimeOffset now) =>
            this.RecentNpc != null &&
            now - this.RecentNpc.ObservedAt <= recentNpcRetention
                ? this.RecentNpc
                : null;

        public JobTerminalContext? GetRecentJobTerminal(
            DateTimeOffset now) =>
            this.RecentJobTerminal != null &&
            now - this.RecentJobTerminal.ObservedAt <=
                recentJobTerminalRetention
                ? this.RecentJobTerminal
                : null;

        public MissionJournalSource ResolveSource(DateTimeOffset now)
        {
            var npc = this.GetRecentNpc(now);
            var jobTerminal = this.GetRecentJobTerminal(now);

            if (npc != null &&
                (jobTerminal == null ||
                 npc.ObservedAt >= jobTerminal.ObservedAt))
            {
                return MissionJournalSource.Npc;
            }

            return jobTerminal != null
                ? MissionJournalSource.JobTerminal
                : MissionJournalSource.Unknown;
        }

        public void RememberJobDescriptor(
            string key,
            JobDescriptor descriptor)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            if (this.knownJobs.TryGetValue(key, out var existing))
            {
                descriptor = new JobDescriptor(
                    descriptor.JobId != 0
                        ? descriptor.JobId
                        : existing.JobId,
                    descriptor.Category != MissionJournalJobCategory.Unknown
                        ? descriptor.Category
                        : existing.Category,
                    FirstAvailable(
                        descriptor.Reward,
                        existing.Reward));
            }
            else
            {
                this.knownJobOrder.Enqueue(key);
            }

            this.knownJobs[key] = descriptor;

            while (this.knownJobOrder.Count > MaximumKnownJobObjectives)
            {
                this.knownJobs.Remove(this.knownJobOrder.Dequeue());
            }
        }

        public void RememberJobTerminalContext(
            uint jobId,
            MissionJournalJobCategory category,
            string reward,
            string systemName,
            string sectorName,
            string starbaseName,
            DateTimeOffset observedAt)
        {
            var previous = this.RecentJobTerminal;
            var canMerge = previous != null &&
                observedAt - previous.ObservedAt <= recentJobTerminalRetention &&
                (jobId == 0 ||
                 previous.JobId == 0 ||
                 previous.JobId == jobId);

            if (canMerge)
            {
                this.RecentJobTerminal = new JobTerminalContext(
                    jobId != 0 ? jobId : previous!.JobId,
                    category != MissionJournalJobCategory.Unknown
                        ? category
                        : previous!.Category,
                    FirstAvailable(reward, previous!.Reward),
                    FirstAvailable(systemName, previous!.SystemName),
                    FirstAvailable(sectorName, previous!.SectorName),
                    FirstAvailable(starbaseName, previous!.StarbaseName),
                    observedAt);
                return;
            }

            this.RecentJobTerminal = new JobTerminalContext(
                jobId,
                category,
                reward.Trim(),
                systemName.Trim(),
                sectorName.Trim(),
                starbaseName.Trim(),
                observedAt);
        }

        public JobDescriptor? ResolveJobDescriptor(
            ClientMissionObservation mission)
        {
            var objective = GetCurrentObjective(mission);
            this.knownJobs.TryGetValue(
                CreateMissionObjectiveKey(mission.Name, objective),
                out var descriptor);
            return descriptor;
        }

        public MissionJournalEntry? TakeRestoredEntry(
            ClientMissionObservation mission)
        {
            var fingerprint = ComputeSemanticFingerprint(mission);
            var scored = this.UnboundActiveEntries
                .Select(entry => new
                {
                    Entry = entry,
                    Score = GetStoredMatchScore(
                        entry,
                        mission,
                        fingerprint,
                        rejectIdentityConflict: false),
                })
                .Where(candidate => candidate.Score > 0)
                .OrderByDescending(candidate => candidate.Score)
                .ToArray();

            if (scored.Length == 0 ||
                (scored.Length > 1 && scored[0].Score == scored[1].Score))
            {
                return null;
            }

            var match = scored[0].Entry;
            this.UnboundActiveEntries.Remove(match);
            this.MissingRestoredEpisodes.Remove(match.EpisodeId);
            return match;
        }

        public PendingRemoval? TakePendingRemoval(
            ClientMissionObservation mission)
        {
            var fingerprint = ComputeSemanticFingerprint(mission);
            var scored = this.PendingRemovals
                .Select(candidate => new
                {
                    Pending = candidate,
                    Score = GetStoredMatchScore(
                        candidate.Entry,
                        mission,
                        fingerprint,
                        rejectIdentityConflict: true),
                })
                .Where(candidate => candidate.Score > 0)
                .OrderByDescending(candidate => candidate.Score)
                .ToArray();

            if (scored.Length == 0 ||
                (scored.Length > 1 && scored[0].Score == scored[1].Score))
            {
                return null;
            }

            var match = scored[0].Pending;
            this.PendingRemovals.Remove(match);
            return match;
        }

        private static int GetStoredMatchScore(
            MissionJournalEntry entry,
            ClientMissionObservation mission,
            string fingerprint,
            bool rejectIdentityConflict)
        {
            var rawIdMatches = entry.MissionRawId.HasValue &&
                mission.RawId.HasValue &&
                entry.MissionRawId.Value == mission.RawId.Value;
            var startTimeMatches = entry.MissionStartTime.HasValue &&
                mission.StartTime.HasValue &&
                entry.MissionStartTime.Value == mission.StartTime.Value;
            var rawIdConflicts = entry.MissionRawId.HasValue &&
                mission.RawId.HasValue &&
                entry.MissionRawId.Value != mission.RawId.Value;
            var startTimeConflicts = entry.MissionStartTime.HasValue &&
                mission.StartTime.HasValue &&
                entry.MissionStartTime.Value != mission.StartTime.Value;
            var fingerprintMatches = string.Equals(
                entry.SemanticFingerprint,
                fingerprint,
                StringComparison.Ordinal);

            if (!fingerprintMatches ||
                (rejectIdentityConflict &&
                 (rawIdConflicts || startTimeConflicts)))
            {
                return 0;
            }

            var score = rawIdMatches ? 1000 : 0;
            score += startTimeMatches ? 500 : 0;
            score += fingerprintMatches ? 200 : 0;
            score += string.Equals(
                NormalizeText(entry.Name),
                NormalizeText(mission.Name),
                StringComparison.Ordinal)
                ? 50
                : 0;
            score += entry.StageCount == mission.StageCount ? 10 : 0;
            return score;
        }

    }

    private sealed class LiveEpisode(
        MissionJournalEntry entry,
        ClientMissionObservation mission,
        DateTimeOffset lastStoredAt)
    {
        public MissionJournalEntry Entry { get; set; } = entry;

        public ClientMissionObservation Mission { get; set; } = mission;

        public DateTimeOffset LastStoredAt { get; set; } = lastStoredAt;
    }

    private sealed record PendingRemoval(
        MissionJournalEntry Entry,
        PilotOutcome BaselineOutcome,
        DateTimeOffset ExpiresAt);

    private sealed record JobDescriptor(
        uint JobId,
        MissionJournalJobCategory Category,
        string Reward);

    private sealed record JobTerminalContext(
        uint JobId,
        MissionJournalJobCategory Category,
        string Reward,
        string SystemName,
        string SectorName,
        string StarbaseName,
        DateTimeOffset ObservedAt);

    private sealed record NpcContext(
        string Name,
        string SystemName,
        string SectorName,
        string StarbaseName,
        DateTimeOffset ObservedAt);

    private sealed record LocationContext(
        string SystemName,
        string SectorName,
        string StarbaseName);

    private sealed record ExperienceTrack(
        int? Level,
        int? Required,
        int? Earned)
    {
        public static ExperienceTrack From(
            ClientCharacterExperienceTrackObservation value) =>
            new(
                value.Level,
                value.ExperienceRequiredForNextLevel,
                value.ExperienceEarnedInCurrentLevel);
    }

    private sealed record PilotOutcome(
        ulong? Credits,
        ExperienceTrack Combat,
        ExperienceTrack Explore,
        ExperienceTrack Trade,
        IReadOnlyDictionary<string, float> Reputation,
        IReadOnlyDictionary<int, int> RewardItems)
    {
        public static PilotOutcome Empty { get; } = new(
            null,
            new ExperienceTrack(null, null, null),
            new ExperienceTrack(null, null, null),
            new ExperienceTrack(null, null, null),
            new Dictionary<string, float>(StringComparer.Ordinal),
            new Dictionary<int, int>());
    }
}
