namespace Net7ClientManager.Contributions;

using System.Globalization;
using System.Text;
using Net7ClientManager.Models;
using Net7ClientManager.Navigation;
using Net7ClientManager.Observations;
using Net7ClientManager.Observations.Models;

internal sealed partial class ForgeContributionCoordinator
{
    private static readonly TimeSpan MissionNpcRetention =
        TimeSpan.FromSeconds(5);

    private static readonly TimeSpan MissionJobTerminalRetention =
        TimeSpan.FromSeconds(10);

    private static readonly TimeSpan MissionForfeitCueRetention =
        TimeSpan.FromSeconds(3);

    private static readonly TimeSpan MissionCompletionOutcomeWindow =
        TimeSpan.FromSeconds(5);

    private static readonly TimeSpan MissionRewardSettleTime =
        TimeSpan.FromMilliseconds(2500);

    private static readonly TimeSpan MissionArrayCollapseConfirmation =
        TimeSpan.FromSeconds(3);

    private readonly Dictionary<int, MissionProcessState>
        missionProcessStates = [];

    private readonly HashSet<string> inFlightMissionKeys =
        new(StringComparer.Ordinal);

    private readonly HashSet<string> completedMissionKeys =
        new(StringComparer.Ordinal);

    private readonly HashSet<string> sessionObservedMissionKeys =
        new(StringComparer.Ordinal);

    private readonly HashSet<string> lifetimeObservedMissionKeys;

    private void ObserveMissions(ClientObservationSnapshot snapshot)
    {
        if (snapshot.LifecycleState != ClientLifecycleState.InGame)
        {
            lock (this.stateLock)
            {
                this.missionProcessStates.Remove(snapshot.ProcessId);
            }

            return;
        }

        var identity = ClientLiveCharacterIdentityResolver.Resolve(snapshot);

        if (string.IsNullOrWhiteSpace(identity.Name))
        {
            return;
        }

        List<ObservedMissionContribution> submissions = [];
        var settingsChanged = false;

        lock (this.stateLock)
        {
            if (!this.missionProcessStates.TryGetValue(
                    snapshot.ProcessId,
                    out var state))
            {
                state = new MissionProcessState();
                this.missionProcessStates.Add(snapshot.ProcessId, state);
            }

            state.LivePilotName = identity.Name.Trim();
            this.ObserveRecentMissionContext(snapshot, state);
            this.ResolvePendingMissionCompletions(
                snapshot,
                state,
                submissions,
                ref settingsChanged);

            var shouldProcessMissionSnapshot =
                snapshot.LoadingOrTransitionFlag == 0 &&
                snapshot.LocalPlayer.Missions.IsAvailable &&
                snapshot.LastSlowFeatureObservedAt != null &&
                snapshot.LastSlowFeatureObservedAt !=
                    state.LastProcessedSlowObservedAt;

            if (shouldProcessMissionSnapshot)
            {
                state.LastProcessedSlowObservedAt =
                    snapshot.LastSlowFeatureObservedAt;

                var rawMissions = snapshot.LocalPlayer.Missions.Missions
                    .Where(mission => mission.ValidState > 0)
                    .OrderBy(mission => mission.Slot)
                    .ToArray();
                var usableMissions = rawMissions
                    .Where(IsUsableMission)
                    .ToArray();

                if (rawMissions.Length == 0 &&
                    state.Episodes.Count > 0 &&
                    !state.HasRecentForfeitCue(snapshot.ObservedAt))
                {
                    state.CollapseStartedAt ??= snapshot.ObservedAt;

                    if (snapshot.ObservedAt - state.CollapseStartedAt <
                        MissionArrayCollapseConfirmation)
                    {
                        this.status =
                            "Holding a transient empty mission array before sharing lifecycle changes.";
                        shouldProcessMissionSnapshot = false;
                    }
                }
                else
                {
                    state.CollapseStartedAt = null;
                }

                if (shouldProcessMissionSnapshot)
                {
                    var rawBySlot = rawMissions.ToDictionary(
                        mission => mission.Slot);
                    var removedEpisodes = state.Episodes.Values
                        .Where(existing =>
                        {
                            if (!rawBySlot.TryGetValue(
                                    existing.Slot,
                                    out var current))
                            {
                                return true;
                            }

                            return IsUsableMission(current) &&
                                   !IsSameMissionEpisode(
                                       existing.LastMission,
                                       current);
                        })
                        .ToArray();

                    if (removedEpisodes.Length > 1)
                    {
                        state.BatchRemovalStartedAt ??= snapshot.ObservedAt;

                        if (snapshot.ObservedAt -
                            state.BatchRemovalStartedAt.Value <
                            MissionArrayCollapseConfirmation)
                        {
                            this.status =
                                "Holding simultaneous mission removals before sharing lifecycle changes.";
                            shouldProcessMissionSnapshot = false;
                        }
                    }
                    else
                    {
                        state.BatchRemovalStartedAt = null;
                    }

                    if (!shouldProcessMissionSnapshot)
                    {
                        // Preserve all current episodes and their source/NPC
                        // relationships until the suspicious read settles.
                    }
                    else
                    {
                        if (removedEpisodes.Length == 1)
                        {
                            this.HandleMissionRemoval(
                                snapshot,
                                state,
                                removedEpisodes[0],
                                submissions,
                                ref settingsChanged);
                        }

                        foreach (var removed in removedEpisodes)
                        {
                            state.Episodes.Remove(removed.Slot);
                        }

                        foreach (var mission in usableMissions)
                        {
                            var semanticFingerprint =
                                ComputeMissionSemanticFingerprint(mission);

                            if (!state.Episodes.TryGetValue(
                                    mission.Slot,
                                    out var episode) ||
                                !IsSameMissionEpisode(
                                    episode.LastMission,
                                    mission))
                            {
                                var detectedSource = state.ResolveSource(
                                    snapshot.ObservedAt);
                                var source = state.HasBaseline
                                    ? detectedSource == MissionSourceKind.Unknown
                                        ? MissionSourceKind.OrdinaryMission
                                        : detectedSource
                                    : MissionSourceKind.Unknown;
                                var issuer = source ==
                                    MissionSourceKind.OrdinaryMission
                                        ? state.GetRecentNpc(snapshot.ObservedAt)
                                        : null;
                                episode = MissionEpisode.Create(
                                    mission,
                                    semanticFingerprint,
                                    snapshot.ObservedAt,
                                    source,
                                    issuer);
                                state.Episodes[mission.Slot] = episode;

                                if (episode.CanContribute)
                                {
                                    var observation = episode.CreateContribution(
                                        "acceptance",
                                        completionNpc: null,
                                        reward: null,
                                        snapshot.ObservedAt);
                                    this.QueueMissionObservation(
                                        observation,
                                        state.LivePilotName,
                                        submissions,
                                        ref settingsChanged);
                                }

                                continue;
                            }

                            var changed = episode.Merge(
                                mission,
                                snapshot.ObservedAt);

                            if (!changed)
                            {
                                continue;
                            }

                            episode.ApplySource(
                                state.ResolveSource(snapshot.ObservedAt));

                            if (!episode.CanContribute)
                            {
                                continue;
                            }

                            var observation2 = episode.CreateContribution(
                                "progress",
                                completionNpc: null,
                                reward: null,
                                snapshot.ObservedAt);
                            this.QueueMissionObservation(
                                observation2,
                                state.LivePilotName,
                                submissions,
                                ref settingsChanged);
                        }

                        state.HasBaseline = true;
                        state.LastOutcome = CaptureMissionOutcome(snapshot);
                    }
                }
            }
        }

        if (settingsChanged)
        {
            this.saveSettings();
        }

        this.RaiseStatisticsChanged();

        foreach (var observation in submissions)
        {
            this.StartMissionSubmission(observation);
        }
    }

    private void ObserveRecentMissionContext(
        ClientObservationSnapshot snapshot,
        MissionProcessState state)
    {
        if (snapshot.AudioCue.IsCurrent &&
            string.Equals(
                NormalizeKey(snapshot.AudioCue.ResourceName),
                "MISSIONFORFEITED",
                StringComparison.Ordinal))
        {
            state.LastForfeitCueAt = snapshot.ObservedAt;
        }

        var interaction = snapshot.StarbaseContext.Interaction;

        if (interaction.IsActive &&
            interaction.Kind == ClientStarbaseInteractionKind.JobsTerminal)
        {
            state.LastJobTerminalAt = snapshot.ObservedAt;
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

        if (room == null || npc == null ||
            string.IsNullOrWhiteSpace(npc.Name))
        {
            return;
        }

        var starbaseId = snapshot.World.CurrentStarbaseId != 0
            ? snapshot.World.CurrentStarbaseId
            : snapshot.StarbaseContext.StarbaseId;
        var stationName = snapshot.World.CurrentStarbaseName.Trim();
        var sectorName = snapshot.World.CurrentSectorName.Trim();
        var npcId = CreateNpcId(
            starbaseId,
            stationName,
            sectorName,
            npc.DefinitionKey,
            npc.DefinitionSecondaryId,
            npc.Name);

        state.RecentNpc = new MissionNpcContext(
            npcId,
            npc.Name.Trim(),
            sectorName,
            stationName,
            snapshot.ObservedAt);
    }

    private void HandleMissionRemoval(
        ClientObservationSnapshot snapshot,
        MissionProcessState state,
        MissionEpisode episode,
        List<ObservedMissionContribution> submissions,
        ref bool settingsChanged)
    {
        episode.ApplySource(
            state.ResolveSource(snapshot.ObservedAt));

        if (!episode.CanContribute ||
            episode.LastMission.IsFailed == true ||
            episode.LastMission.IsExpired == true ||
            state.HasRecentForfeitCue(snapshot.ObservedAt))
        {
            return;
        }

        if (state.PendingCompletions.Count > 0)
        {
            // Multiple unexplained removals inside one reward window make
            // the eventual outcome ambiguous. Prefer losing both completion
            // claims over attaching one pilot-state delta to two missions.
            state.PendingCompletions.Clear();
            return;
        }

        state.PendingCompletions.Add(
            new PendingMissionCompletion(
                episode,
                state.GetRecentNpc(snapshot.ObservedAt),
                state.LastOutcome,
                snapshot.ObservedAt,
                snapshot.ObservedAt + MissionCompletionOutcomeWindow));
    }

    private void ResolvePendingMissionCompletions(
        ClientObservationSnapshot snapshot,
        MissionProcessState state,
        List<ObservedMissionContribution> submissions,
        ref bool settingsChanged)
    {
        if (state.PendingCompletions.Count == 0)
        {
            return;
        }

        var currentOutcome = CaptureMissionOutcome(snapshot);

        foreach (var pending in state.PendingCompletions.ToArray())
        {
            var hasCorrelatedCompletionNpc =
                pending.CompletionNpc != null;
            var reward = CreateMissionReward(
                pending.BaselineOutcome,
                currentOutcome,
                includePilotOutcomeRewards: hasCorrelatedCompletionNpc,
                includeCargoItems: hasCorrelatedCompletionNpc);

            if (reward != null && reward.HasPositiveEvidence)
            {
                var rewardFingerprint =
                    ComputeMissionRewardFingerprint(reward);

                if (!string.Equals(
                        pending.LatestRewardFingerprint,
                        rewardFingerprint,
                        StringComparison.Ordinal))
                {
                    pending.LatestReward = reward;
                    pending.LatestRewardFingerprint = rewardFingerprint;
                    pending.LastRewardChangedAt = snapshot.ObservedAt;
                }
            }

            var rewardSettled = pending.LatestReward != null &&
                pending.LastRewardChangedAt.HasValue &&
                snapshot.ObservedAt - pending.LastRewardChangedAt.Value >=
                    MissionRewardSettleTime;
            var windowExpired = snapshot.ObservedAt >= pending.ExpiresAt;

            if (pending.LatestReward != null &&
                (rewardSettled || windowExpired))
            {
                var observation = pending.Episode.CreateContribution(
                    "completion",
                    pending.CompletionNpc,
                    pending.LatestReward.ToContract(),
                    snapshot.ObservedAt);
                this.QueueMissionObservation(
                    observation,
                    state.LivePilotName,
                    submissions,
                    ref settingsChanged);
                state.PendingCompletions.Remove(pending);
                continue;
            }

            if (windowExpired)
            {
                state.PendingCompletions.Remove(pending);
            }
        }

        state.LastOutcome = currentOutcome;
    }

    private void QueueMissionObservation(
        ObservedMissionContribution observation,
        string livePilotName,
        List<ObservedMissionContribution> submissions,
        ref bool settingsChanged)
    {
        observation = observation.WithPilot(livePilotName);
        var factKey = observation.FactFingerprint;

        if (!this.completedMissionKeys.Contains(factKey) &&
            !this.inFlightMissionKeys.Contains(factKey))
        {
            submissions.Add(observation);
        }

        if (this.sessionObservedMissionKeys.Add(
                observation.SemanticFingerprint))
        {
            this.session.MissionsObserved =
                this.sessionObservedMissionKeys.Count;
        }

        if (this.lifetimeObservedMissionKeys.Add(
                observation.SemanticFingerprint))
        {
            this.settings.Lifetime.ObservedMissionKeys.Add(
                observation.SemanticFingerprint);
            this.settings.Lifetime.MissionsObserved =
                this.lifetimeObservedMissionKeys.Count;
            settingsChanged = true;
        }
    }

    private void StartMissionSubmission(
        ObservedMissionContribution observation)
    {
        CancellationToken participationToken;

        lock (this.stateLock)
        {
            if (this.completedMissionKeys.Contains(observation.FactFingerprint) ||
                !this.inFlightMissionKeys.Add(observation.FactFingerprint))
            {
                return;
            }

            participationToken = this.participationCancellation.Token;
            this.status = string.Create(
                CultureInfo.InvariantCulture,
                $"Sharing mission knowledge for {observation.Name} with Forge.");
        }

        var submissionCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                this.cancellation.Token,
                participationToken);
        var task = this.SubmitMissionAsync(
            observation,
            submissionCancellation);
        this.Track(task);
    }

    private async Task SubmitMissionAsync(
        ObservedMissionContribution observation,
        CancellationTokenSource submissionCancellation)
    {
        var cancellationToken = submissionCancellation.Token;

        try
        {
            var identity = await this.identityService.EnsureAsync(
                    observation.LivePilotName,
                    cancellationToken)
                .ConfigureAwait(false);
            var unsignedRequest = new ForgeMissionContributionRequest
            {
                ContributorId = identity.ContributorId,
                RequestId = Guid.NewGuid().ToString(
                    "D",
                    CultureInfo.InvariantCulture),
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
                Missions = [observation.Item],
            };
            var request = unsignedRequest with
            {
                Signature = identity.Sign(unsignedRequest),
            };
            var response = await this.client.SubmitMissionsAsync(
                    request,
                    cancellationToken)
                .ConfigureAwait(false);

            lock (this.stateLock)
            {
                this.completedMissionKeys.Add(observation.FactFingerprint);
                this.session.MissionFactsSubmitted += response.Received;
                this.session.MissionsAlreadyCanonical += response.AlreadyCanonical;
                this.session.MissionEvidenceAccepted += response.EvidenceAccepted;
                this.session.MissionConflicts += response.Conflicts;
                this.session.MissionsCreated += response.Created;
                this.session.MissionsStrengthened += response.Strengthened;
                this.session.SuccessfulBatches++;
                this.session.LastSuccessfulContributionUtc = DateTimeOffset.UtcNow;

                var lifetime = this.settings.Lifetime;
                lifetime.MissionFactsSubmitted += response.Received;
                lifetime.MissionsAlreadyCanonical += response.AlreadyCanonical;
                lifetime.MissionEvidenceAccepted += response.EvidenceAccepted;
                lifetime.MissionConflicts += response.Conflicts;
                lifetime.MissionsCreated += response.Created;
                lifetime.MissionsStrengthened += response.Strengthened;
                lifetime.SuccessfulBatches++;
                lifetime.LastSuccessfulContributionUtc = DateTimeOffset.UtcNow;

                this.status = string.Create(
                    CultureInfo.InvariantCulture,
                    $"Forge accepted mission knowledge for {observation.Name}.");
            }

            this.saveSettings();
            this.RaiseStatisticsChanged();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
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
                System.Diagnostics.Debug.WriteLine(
                    string.Concat("[Forge mission] ", exception));
                this.status = string.Create(
                    CultureInfo.InvariantCulture,
                    $"Forge could not share mission knowledge for {observation.Name}. A later richer observation can retry it.");
            }

            this.saveSettings();
            this.RaiseStatisticsChanged();
        }
        finally
        {
            lock (this.stateLock)
            {
                this.inFlightMissionKeys.Remove(observation.FactFingerprint);
            }

            submissionCancellation.Dispose();
        }
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
                NormalizeKey(existing.Name),
                NormalizeKey(current.Name),
                StringComparison.Ordinal) ||
            existing.StageCount != current.StageCount)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(existing.Summary) &&
            !string.IsNullOrWhiteSpace(current.Summary) &&
            !string.Equals(
                NormalizeKey(existing.Summary),
                NormalizeKey(current.Summary),
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
                   NormalizeKey(existingFirstStage),
                   NormalizeKey(currentFirstStage),
                   StringComparison.Ordinal);
    }

    private static string ComputeMissionSemanticFingerprint(
        ClientMissionObservation mission)
    {
        var firstStage = mission.Stages
            .OrderBy(stage => stage.Index)
            .Select(stage => stage.Text.Trim())
            .FirstOrDefault(text => text.Length > 0) ?? "";
        var source = string.Join(
            "|",
            NormalizeKey(mission.Name),
            NormalizeKey(mission.Summary),
            (mission.StageCount ?? mission.StageCapacity)
                .ToString(CultureInfo.InvariantCulture),
            NormalizeKey(firstStage));
        return ForgeNavigationHash.ComputeSha256(
            Encoding.UTF8.GetBytes(source));
    }

    private static MissionOutcome CaptureMissionOutcome(
        ClientObservationSnapshot snapshot)
    {
        var player = snapshot.LocalPlayer;
        Dictionary<int, int> cargoItemCounts = [];
        Dictionary<int, int> rewardItemCounts = [];
        var cargoItemsAvailable =
            player.Inventory.IsAvailable &&
            player.Inventory.CargoUnavailableSlotCount == 0;
        var rewardItemsAvailable =
            player.RewardOverflowInventory.IsAvailable &&
            player.RewardOverflowInventory.RewardUnavailableSlotCount == 0 &&
            player.RewardOverflowInventory.OverflowUnavailableSlotCount == 0;

        if (cargoItemsAvailable)
        {
            AddItemCounts(cargoItemCounts, player.Inventory.CargoItems);
        }

        if (rewardItemsAvailable)
        {
            AddItemCounts(
                rewardItemCounts,
                player.RewardOverflowInventory.RewardItems);
            AddItemCounts(
                rewardItemCounts,
                player.RewardOverflowInventory.OverflowItems);
        }

        return new MissionOutcome(
            player.CharacterDetails.Credits,
            ExperienceTrack.Create(player.CharacterProgression.Combat),
            ExperienceTrack.Create(player.CharacterProgression.Explore),
            ExperienceTrack.Create(player.CharacterProgression.Trade),
            player.Reputation.Factions
                .Where(faction =>
                    !string.IsNullOrWhiteSpace(faction.FactionKey) &&
                    faction.Reaction.HasValue)
                .ToDictionary(
                    faction => faction.FactionKey,
                    faction => new ReputationValue(
                        faction.DisplayName,
                        faction.Reaction!.Value),
                    StringComparer.Ordinal),
            cargoItemsAvailable,
            cargoItemCounts,
            rewardItemsAvailable,
            rewardItemCounts);
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
                target.GetValueOrDefault(item.ItemTemplateId.Value) + quantity;
        }
    }

    private static MissionRewardResult? CreateMissionReward(
        MissionOutcome baseline,
        MissionOutcome current,
        bool includePilotOutcomeRewards,
        bool includeCargoItems)
    {
        var creditDifference = includePilotOutcomeRewards &&
                               baseline.Credits.HasValue &&
                               current.Credits.HasValue &&
                               current.Credits.Value > baseline.Credits.Value
            ? current.Credits.Value - baseline.Credits.Value
            : 0UL;
        var credits = creditDifference > long.MaxValue
            ? long.MaxValue
            : (long)creditDifference;
        var combat = includePilotOutcomeRewards
            ? CalculateExperienceDelta(baseline.Combat, current.Combat)
            : 0;
        var explore = includePilotOutcomeRewards
            ? CalculateExperienceDelta(baseline.Explore, current.Explore)
            : 0;
        var trade = includePilotOutcomeRewards
            ? CalculateExperienceDelta(baseline.Trade, current.Trade)
            : 0;
        List<ForgeMissionReputationRewardContribution> reputation = [];

        if (includePilotOutcomeRewards)
        {
            foreach (var pair in current.Reputation)
            {
                if (!baseline.Reputation.TryGetValue(pair.Key, out var previous))
                {
                    continue;
                }

                var delta = pair.Value.Reaction - previous.Reaction;

                if (delta <= 0.0001f || !float.IsFinite(delta))
                {
                    continue;
                }

                reputation.Add(
                    new ForgeMissionReputationRewardContribution
                    {
                        FactionKey = pair.Key,
                        DisplayName = pair.Value.DisplayName,
                        ReactionDelta = MathF.Round(delta, 4),
                    });
            }
        }

        Dictionary<int, int> baselineItems = [];
        Dictionary<int, int> currentItems = [];

        if (baseline.RewardItemsAvailable && current.RewardItemsAvailable)
        {
            AddItemCountMap(baselineItems, baseline.RewardItemCounts);
            AddItemCountMap(currentItems, current.RewardItemCounts);
        }

        // Ordinary NPC turn-ins can place a fixed item reward directly in
        // cargo, as observed for Retrieve Josher's Money. For completions
        // without a correlated NPC, cargo gains are deliberately excluded:
        // they can be ordinary combat loot arriving beside an automatic
        // mission completion rather than the mission's canonical reward.
        if (includeCargoItems &&
            baseline.CargoItemsAvailable &&
            current.CargoItemsAvailable)
        {
            AddItemCountMap(baselineItems, baseline.CargoItemCounts);
            AddItemCountMap(currentItems, current.CargoItemCounts);
        }

        var items = currentItems
            .Select(pair => new
            {
                pair.Key,
                Quantity = pair.Value -
                    baselineItems.GetValueOrDefault(pair.Key),
            })
            .Where(pair => pair.Quantity > 0)
            .OrderBy(pair => pair.Key)
            .Select(pair => new ForgeMissionItemRewardContribution
            {
                ItemTemplateId = pair.Key,
                Quantity = pair.Quantity,
            })
            .ToArray();

        var result = new MissionRewardResult(
            credits,
            combat,
            explore,
            trade,
            reputation,
            items);
        return result.HasPositiveEvidence ? result : null;
    }


    private static void AddItemCountMap(
        Dictionary<int, int> target,
        IReadOnlyDictionary<int, int> source)
    {
        foreach (var pair in source)
        {
            target[pair.Key] =
                target.GetValueOrDefault(pair.Key) + pair.Value;
        }
    }

    private static string ComputeMissionRewardFingerprint(
        MissionRewardResult reward)
    {
        var builder = new StringBuilder();
        builder.Append(reward.Credits).Append('|')
            .Append(reward.CombatExperience).Append('|')
            .Append(reward.ExploreExperience).Append('|')
            .Append(reward.TradeExperience).Append('|');

        foreach (var reputation in reward.Reputation
                     .OrderBy(value => value.FactionKey, StringComparer.Ordinal))
        {
            builder.Append(reputation.FactionKey).Append(':')
                .Append(reputation.ReactionDelta.ToString(
                    "R",
                    CultureInfo.InvariantCulture)).Append('|');
        }

        foreach (var item in reward.Items
                     .OrderBy(value => value.ItemTemplateId))
        {
            builder.Append(item.ItemTemplateId).Append(':')
                .Append(item.Quantity).Append('|');
        }

        return ForgeNavigationHash.ComputeSha256(
            Encoding.UTF8.GetBytes(builder.ToString()));
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

        if (current.Level == previous.Level + 1 &&
            previous.Required.HasValue)
        {
            return Math.Max(
                0,
                previous.Required.Value - previous.Earned.Value +
                current.Earned.Value);
        }

        return 0;
    }

    private sealed class MissionProcessState
    {
        public string LivePilotName { get; set; } = "";

        public DateTimeOffset? LastProcessedSlowObservedAt { get; set; }

        public bool HasBaseline { get; set; }

        public DateTimeOffset? LastForfeitCueAt { get; set; }

        public DateTimeOffset? CollapseStartedAt { get; set; }

        public DateTimeOffset? BatchRemovalStartedAt { get; set; }

        public DateTimeOffset? LastJobTerminalAt { get; set; }

        public MissionNpcContext? RecentNpc { get; set; }

        public Dictionary<int, MissionEpisode> Episodes { get; } = [];

        public List<PendingMissionCompletion> PendingCompletions { get; } = [];

        public MissionOutcome LastOutcome { get; set; } = MissionOutcome.Empty;

        public bool HasRecentForfeitCue(DateTimeOffset now) =>
            this.LastForfeitCueAt.HasValue &&
            now - this.LastForfeitCueAt.Value <= MissionForfeitCueRetention;

        public MissionNpcContext? GetRecentNpc(DateTimeOffset now) =>
            this.RecentNpc != null &&
            now - this.RecentNpc.ObservedAt <= MissionNpcRetention
                ? this.RecentNpc
                : null;

        public MissionSourceKind ResolveSource(DateTimeOffset now)
        {
            var npc = this.GetRecentNpc(now);
            var hasRecentJobTerminal = this.LastJobTerminalAt.HasValue &&
                now - this.LastJobTerminalAt.Value <=
                    MissionJobTerminalRetention;

            if (npc != null &&
                (!hasRecentJobTerminal ||
                 npc.ObservedAt >= this.LastJobTerminalAt!.Value))
            {
                return MissionSourceKind.OrdinaryMission;
            }

            return hasRecentJobTerminal
                ? MissionSourceKind.JobTerminal
                : MissionSourceKind.Unknown;
        }
    }

    private sealed class MissionEpisode
    {
        private MissionEpisode(
            int slot,
            string semanticFingerprint,
            ClientMissionObservation mission,
            DateTimeOffset firstObservedAt,
            MissionSourceKind source,
            MissionNpcContext? issuer)
        {
            this.Slot = slot;
            this.SemanticFingerprint = semanticFingerprint;
            this.LastMission = mission;
            this.FirstObservedAt = firstObservedAt;
            this.LastObservedAt = firstObservedAt;
            this.Source = source;
            this.Issuer = issuer;
            this.ObservationCount = 1;
        }

        public int Slot { get; }

        public string SemanticFingerprint { get; }

        public ClientMissionObservation LastMission { get; private set; }

        public DateTimeOffset FirstObservedAt { get; }

        public DateTimeOffset LastObservedAt { get; private set; }

        public int ObservationCount { get; private set; }

        public MissionSourceKind Source { get; private set; }

        public MissionNpcContext? Issuer { get; private set; }

        public bool CanContribute =>
            this.Source == MissionSourceKind.OrdinaryMission;

        public static MissionEpisode Create(
            ClientMissionObservation mission,
            string semanticFingerprint,
            DateTimeOffset observedAt,
            MissionSourceKind source,
            MissionNpcContext? issuer) =>
            new(
                mission.Slot,
                semanticFingerprint,
                mission,
                observedAt,
                source,
                issuer);

        public void ApplySource(MissionSourceKind source)
        {
            if (this.Source == MissionSourceKind.Unknown &&
                source != MissionSourceKind.Unknown)
            {
                this.Source = source;
            }
        }

        public bool Merge(
            ClientMissionObservation mission,
            DateTimeOffset observedAt)
        {
            var mergedStages = this.LastMission.Stages
                .ToDictionary(stage => stage.Index);
            var changed = false;

            foreach (var stage in mission.Stages)
            {
                if (!mergedStages.TryGetValue(stage.Index, out var existing))
                {
                    mergedStages[stage.Index] = stage;
                    changed |= !string.IsNullOrWhiteSpace(stage.Text);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(existing.Text) &&
                    !string.IsNullOrWhiteSpace(stage.Text))
                {
                    mergedStages[stage.Index] = stage;
                    changed = true;
                }
            }

            var stage2 = Math.Max(
                this.LastMission.Stage ?? 0,
                mission.Stage ?? 0);
            changed |= stage2 != (this.LastMission.Stage ?? 0);
            changed |= string.IsNullOrWhiteSpace(this.LastMission.Summary) &&
                       !string.IsNullOrWhiteSpace(mission.Summary);
            changed |= string.IsNullOrWhiteSpace(this.LastMission.Reward) &&
                       !string.IsNullOrWhiteSpace(mission.Reward);
            changed |= string.IsNullOrWhiteSpace(this.LastMission.FailureConsequence) &&
                       !string.IsNullOrWhiteSpace(mission.FailureConsequence);
            changed |= string.IsNullOrWhiteSpace(this.LastMission.IssuingFaction) &&
                       !string.IsNullOrWhiteSpace(mission.IssuingFaction);
            changed |= !this.LastMission.IsTimed.HasValue &&
                       mission.IsTimed.HasValue;
            changed |= !this.LastMission.IsForfeitable.HasValue &&
                       mission.IsForfeitable.HasValue;

            this.LastMission = mission with
            {
                Stage = stage2 > 0 ? stage2 : mission.Stage,
                Summary = PreferRich(this.LastMission.Summary, mission.Summary),
                Reward = PreferRich(this.LastMission.Reward, mission.Reward),
                FailureConsequence = PreferRich(
                    this.LastMission.FailureConsequence,
                    mission.FailureConsequence),
                IssuingFaction = PreferRich(
                    this.LastMission.IssuingFaction,
                    mission.IssuingFaction),
                StageCount = this.LastMission.StageCount ?? mission.StageCount,
                IsTimed = this.LastMission.IsTimed ?? mission.IsTimed,
                IsForfeitable =
                    this.LastMission.IsForfeitable ?? mission.IsForfeitable,
                Stages = [.. mergedStages.Values.OrderBy(value => value.Index)],
            };
            this.LastObservedAt = observedAt;
            this.ObservationCount++;
            return changed;
        }

        public ObservedMissionContribution CreateContribution(
            string evidenceKind,
            MissionNpcContext? completionNpc,
            ForgeMissionRewardContribution? reward,
            DateTimeOffset observedAt)
        {
            var item = new ForgeMissionContributionItem
            {
                SemanticFingerprint = this.SemanticFingerprint,
                EvidenceKind = evidenceKind,
                Name = this.LastMission.Name.Trim(),
                Summary = this.LastMission.Summary.Trim(),
                RewardText = this.LastMission.Reward.Trim(),
                FailureConsequence = this.LastMission.FailureConsequence.Trim(),
                IssuingFaction = this.LastMission.IssuingFaction.Trim(),
                StageCount = this.LastMission.StageCount ??
                    Math.Max(
                        1,
                        this.LastMission.Stages.Count == 0
                            ? this.LastMission.Stage ?? 1
                            : this.LastMission.Stages.Max(stage => stage.Index) + 1),
                IsTimed = this.LastMission.IsTimed,
                IsForfeitable = this.LastMission.IsForfeitable,
                Stages =
                [
                    .. this.LastMission.Stages
                        .OrderBy(stage => stage.Index)
                        .Select(stage =>
                            new ForgeMissionStageContributionItem
                            {
                                Index = stage.Index + 1,
                                Text = stage.Text.Trim(),
                                IsTimed = stage.IsTimed,
                            }),
                ],
                IssuerNpcId = this.Issuer?.NpcId ?? "",
                CompletionNpcId = completionNpc?.NpcId ?? "",
                CompletionSectorName = completionNpc?.SectorName ?? "",
                CompletionStationName = completionNpc?.StationName ?? "",
                Reward = reward,
            };
            var factFingerprint = ComputeMissionFactFingerprint(item);
            return new ObservedMissionContribution(
                this.Slot,
                this.SemanticFingerprint,
                this.LastMission.Name.Trim(),
                this.FirstObservedAt,
                observedAt,
                this.ObservationCount,
                item,
                factFingerprint,
                "");
        }

        private static string PreferRich(string existing, string incoming) =>
            !string.IsNullOrWhiteSpace(existing)
                ? existing
                : incoming;
    }

    private static string ComputeMissionFactFingerprint(
        ForgeMissionContributionItem item)
    {
        var builder = new StringBuilder();
        builder.Append(item.SemanticFingerprint).Append('|')
            .Append(item.EvidenceKind).Append('|')
            .Append(NormalizeKey(item.Name)).Append('|')
            .Append(NormalizeKey(item.Summary)).Append('|')
            .Append(NormalizeKey(item.RewardText)).Append('|')
            .Append(NormalizeKey(item.FailureConsequence)).Append('|')
            .Append(NormalizeKey(item.IssuingFaction)).Append('|')
            .Append(item.StageCount).Append('|')
            .Append(FormatMissionBoolean(item.IsTimed)).Append('|')
            .Append(FormatMissionBoolean(item.IsForfeitable)).Append('|')
            .Append(item.IssuerNpcId).Append('|')
            .Append(item.CompletionNpcId).Append('|')
            .Append(NormalizeKey(item.CompletionSectorName)).Append('|')
            .Append(NormalizeKey(item.CompletionStationName)).Append('|');

        foreach (var stage in item.Stages.OrderBy(stage => stage.Index))
        {
            builder.Append(stage.Index).Append(':')
                .Append(NormalizeKey(stage.Text)).Append(':')
                .Append(FormatMissionBoolean(stage.IsTimed)).Append('|');
        }

        if (item.Reward != null)
        {
            builder.Append(item.Reward.Credits).Append('|')
                .Append(item.Reward.CombatExperience).Append('|')
                .Append(item.Reward.ExploreExperience).Append('|')
                .Append(item.Reward.TradeExperience).Append('|');

            foreach (var reputation in item.Reward.Reputation
                         .OrderBy(value => value.FactionKey, StringComparer.Ordinal))
            {
                builder.Append(reputation.FactionKey).Append(':')
                    .Append(reputation.ReactionDelta.ToString(
                        "R",
                        CultureInfo.InvariantCulture)).Append('|');
            }

            foreach (var rewardItem in item.Reward.Items
                         .OrderBy(value => value.ItemTemplateId))
            {
                builder.Append(rewardItem.ItemTemplateId).Append(':')
                    .Append(rewardItem.Quantity).Append('|');
            }
        }

        return ForgeNavigationHash.ComputeSha256(
            Encoding.UTF8.GetBytes(builder.ToString()));
    }

    private static string FormatMissionBoolean(bool? value) =>
        value.HasValue ? value.Value ? "1" : "0" : "";

    private enum MissionSourceKind
    {
        Unknown,
        OrdinaryMission,
        JobTerminal,
    }

    private sealed record MissionNpcContext(
        string NpcId,
        string Name,
        string SectorName,
        string StationName,
        DateTimeOffset ObservedAt);

    private sealed class PendingMissionCompletion(
        MissionEpisode episode,
        MissionNpcContext? completionNpc,
        MissionOutcome baselineOutcome,
        DateTimeOffset removedAt,
        DateTimeOffset expiresAt)
    {
        public MissionEpisode Episode { get; } = episode;

        public MissionNpcContext? CompletionNpc { get; } = completionNpc;

        public MissionOutcome BaselineOutcome { get; } = baselineOutcome;

        public DateTimeOffset RemovedAt { get; } = removedAt;

        public DateTimeOffset ExpiresAt { get; } = expiresAt;

        public MissionRewardResult? LatestReward { get; set; }

        public string LatestRewardFingerprint { get; set; } = "";

        public DateTimeOffset? LastRewardChangedAt { get; set; }
    }

    private sealed record MissionOutcome(
        ulong? Credits,
        ExperienceTrack Combat,
        ExperienceTrack Explore,
        ExperienceTrack Trade,
        IReadOnlyDictionary<string, ReputationValue> Reputation,
        bool CargoItemsAvailable,
        IReadOnlyDictionary<int, int> CargoItemCounts,
        bool RewardItemsAvailable,
        IReadOnlyDictionary<int, int> RewardItemCounts)
    {
        public static MissionOutcome Empty { get; } = new(
            null,
            new ExperienceTrack(null, null, null),
            new ExperienceTrack(null, null, null),
            new ExperienceTrack(null, null, null),
            new Dictionary<string, ReputationValue>(StringComparer.Ordinal),
            false,
            new Dictionary<int, int>(),
            false,
            new Dictionary<int, int>());
    }

    private sealed record ExperienceTrack(
        int? Level,
        int? Earned,
        int? Required)
    {
        public static ExperienceTrack Create(
            ClientCharacterExperienceTrackObservation observation) =>
            new(
                observation.Level,
                observation.ExperienceEarnedInCurrentLevel,
                observation.ExperienceRequiredForNextLevel);
    }

    private sealed record ReputationValue(
        string DisplayName,
        float Reaction);

    private sealed record MissionRewardResult(
        long Credits,
        int CombatExperience,
        int ExploreExperience,
        int TradeExperience,
        IReadOnlyList<ForgeMissionReputationRewardContribution> Reputation,
        IReadOnlyList<ForgeMissionItemRewardContribution> Items)
    {
        public bool HasPositiveEvidence =>
            this.Credits > 0 ||
            this.CombatExperience > 0 ||
            this.ExploreExperience > 0 ||
            this.TradeExperience > 0 ||
            this.Reputation.Count > 0 ||
            this.Items.Count > 0;

        public ForgeMissionRewardContribution ToContract() =>
            new()
            {
                Credits = this.Credits,
                CombatExperience = this.CombatExperience,
                ExploreExperience = this.ExploreExperience,
                TradeExperience = this.TradeExperience,
                Reputation = this.Reputation,
                Items = this.Items,
            };

    }

    private sealed record ObservedMissionContribution(
        int Slot,
        string SemanticFingerprint,
        string Name,
        DateTimeOffset FirstObservedAtUtc,
        DateTimeOffset LastObservedAtUtc,
        int ObservationCount,
        ForgeMissionContributionItem Item,
        string FactFingerprint,
        string LivePilotName)
    {
        public ObservedMissionContribution WithPilot(string livePilotName) =>
            this with { LivePilotName = livePilotName };
    }
}
