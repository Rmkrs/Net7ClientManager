namespace Net7ClientManager.ActivityJournal;

using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Net7ClientManager.CombatJournal;
using Net7ClientManager.MissionJournal;
using Net7ClientManager.Observations;
using Net7ClientManager.Observations.Models;
using Net7ClientManager.RecipeMapping;

internal sealed class ActivityJournalCoordinator
{
    private static readonly TimeSpan locationSettleDuration =
        TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan valueSettleDuration =
        TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan missionCorrelationWindow =
        TimeSpan.FromSeconds(5);
    // Reputation updates and corpse-backed encounter completion can arrive in
    // either order. Keep a bounded causal window and drain pending changes as
    // soon as the killed encounter is proven.
    private static readonly TimeSpan recentCombatContextRetentionDuration =
        TimeSpan.FromSeconds(30);
    private static readonly TimeSpan combatReputationLeadWindow =
        TimeSpan.FromSeconds(2);
    private static readonly TimeSpan combatReputationTrailWindow =
        TimeSpan.FromSeconds(10);
    private static readonly TimeSpan reputationReasonFallbackDuration =
        TimeSpan.FromSeconds(30);
    private static readonly TimeSpan pendingLootWindow =
        TimeSpan.FromSeconds(15);
    private static readonly TimeSpan recentLootWindow =
        TimeSpan.FromSeconds(15);
    private static readonly TimeSpan recentLootCreditWindow =
        TimeSpan.FromSeconds(5);
    private static readonly TimeSpan vendorCorrelationWindow =
        TimeSpan.FromSeconds(5);
    private static readonly TimeSpan pendingVendorCreditWindow =
        TimeSpan.FromSeconds(2);
    private static readonly TimeSpan craftingCreditCorrelationWindow =
        TimeSpan.FromSeconds(8);

    private readonly ActivityJournalStore store;
    private readonly Lock stateLock = new();
    private readonly Dictionary<int, ActivityProcessState> processStates = [];
    private readonly Dictionary<uint, RecentMissionContext>
        recentMissionContexts = [];
    private readonly Dictionary<uint, RecentCombatContext>
        recentCombatContexts = [];
    private bool enabled;

    public ActivityJournalCoordinator(
        ActivityJournalStore store,
        bool enabled)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.enabled = enabled;
    }

    public event EventHandler<ActivityJournalChangedEventArgs>? JournalChanged;

    internal event EventHandler<ActivityJournalEntryRecordedEventArgs>?
        EntryRecorded;

    public void SetEnabled(bool value)
    {
        lock (this.stateLock)
        {
            if (this.enabled == value)
            {
                return;
            }

            this.enabled = value;
            this.processStates.Clear();
            this.recentMissionContexts.Clear();
            this.recentCombatContexts.Clear();
        }
    }

    public IReadOnlyList<ActivityJournalEntry> GetHistory(
        uint characterId,
        int maximumResults = 5000) =>
        this.store.GetHistory(characterId, maximumResults);

    public IReadOnlyList<ActivityJournalEntry> GetHistory(
        uint characterId,
        ActivityJournalCategory categories,
        int maximumResults = 5000) =>
        this.store.GetHistory(characterId, categories, maximumResults);

    public IReadOnlyList<ReputationJournalEntry> GetReputationHistory(
        uint characterId,
        string factionKey,
        int maximumResults = 2000) =>
        this.store.GetReputationHistory(
            characterId,
            factionKey,
            maximumResults);

    public LootJournalSession? GetLootSession(string sessionId) =>
        this.store.GetLootSession(sessionId);

    public DateTimeOffset? GetLastRecordedAt(uint characterId) =>
        this.store.GetLastRecordedAt(characterId);

    public void Observe(ClientObservationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        IReadOnlyList<ActivityJournalWrite> writes;

        lock (this.stateLock)
        {
            if (!this.enabled)
            {
                return;
            }

            if (snapshot.LifecycleState != ClientLifecycleState.InGame)
            {
                this.processStates.Remove(snapshot.ProcessId);
                return;
            }

            if (snapshot.LoadingOrTransitionFlag != 0 ||
                !snapshot.World.IsAvailable)
            {
                return;
            }

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

            if (!this.processStates.TryGetValue(snapshot.ProcessId, out var state) ||
                state.CharacterId != characterId)
            {
                this.processStates[snapshot.ProcessId] = new ActivityProcessState(
                    characterId,
                    identity.Name.Trim(),
                    location,
                    snapshot);
                return;
            }

            state.PilotName = identity.Name.Trim();
            this.PruneRecentMissionContext(characterId, snapshot.ObservedAt);
            this.PruneRecentCombatContext(characterId, snapshot.ObservedAt);
            this.recentMissionContexts.TryGetValue(
                characterId,
                out var missionContext);
            this.recentCombatContexts.TryGetValue(
                characterId,
                out var combatContext);
            writes = state.Observe(
                snapshot,
                location,
                missionContext,
                combatContext);
        }

        this.StoreAndPublish(writes);
    }

    internal void RecordMissionEvent(MissionJournalLifecycleEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        ActivityJournalEntry? activity;

        lock (this.stateLock)
        {
            if (!this.enabled)
            {
                return;
            }

            if (e.Kind == MissionJournalEventKind.Completed)
            {
                this.recentMissionContexts[e.Entry.CharacterId] =
                    new RecentMissionContext(
                        e.Entry.EpisodeId,
                        e.Entry.Name,
                        e.Entry.RewardText,
                        e.OccurredAt);
            }

            var liveLocation = this.processStates.Values
                .Where(state => state.CharacterId == e.Entry.CharacterId)
                .OrderByDescending(state => state.LatestObservedAt)
                .Select(state => state.LatestLocation)
                .FirstOrDefault();
            activity = BuildMissionActivity(e, liveLocation);
        }

        if (activity != null)
        {
            this.StoreAndPublish([new ActivityEntryWrite(activity)]);
        }
    }

    internal void RecordCombatEncounter(CombatEncounterEndedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var encounter = e.Encounter;
        List<ActivityJournalWrite> writes = [];

        lock (this.stateLock)
        {
            if (!this.enabled)
            {
                return;
            }

            if (encounter.Outcome == CombatJournalOutcome.Killed)
            {
                var combatContext = new RecentCombatContext(
                    encounter.EncounterId,
                    encounter.TargetName,
                    encounter.LastEventAt,
                    encounter.EndedAt ?? encounter.LastEventAt);
                this.recentCombatContexts[encounter.CharacterId] =
                    combatContext;

                foreach (var state in this.processStates.Values.Where(
                             state => state.CharacterId ==
                                 encounter.CharacterId))
                {
                    writes.AddRange(
                        state.ResolvePendingReputationChanges(
                            combatContext));
                }
            }

            var activity = BuildCombatActivity(encounter);

            if (activity != null)
            {
                writes.Add(new ActivityEntryWrite(activity));
            }
        }

        this.StoreAndPublish(writes);
    }

    internal void RecordCraftingEvent(
        RecipeMappingHistoryEventRecord craftingEvent,
        ClientObservationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(craftingEvent);
        ArgumentNullException.ThrowIfNull(snapshot);

        var identity = ClientLiveCharacterIdentityResolver.Resolve(snapshot);
        if (identity.CharacterObjectId != craftingEvent.CharacterId ||
            string.IsNullOrWhiteSpace(identity.Name))
        {
            return;
        }

        lock (this.stateLock)
        {
            if (!this.enabled)
            {
                return;
            }

            if (this.processStates.TryGetValue(
                    snapshot.ProcessId,
                    out var state) &&
                state.CharacterId == craftingEvent.CharacterId)
            {
                state.RegisterCraftingCredit(craftingEvent);
            }
        }

        var activity = BuildCraftingActivity(
            craftingEvent,
            identity.Name.Trim(),
            JournalLocationResolver.Capture(snapshot));
        if (activity != null)
        {
            this.StoreAndPublish([new ActivityEntryWrite(activity)]);
        }
    }

    public void ForgetProcess(int processId)
    {
        lock (this.stateLock)
        {
            this.processStates.Remove(processId);
        }
    }

    private void PruneRecentMissionContext(
        uint characterId,
        DateTimeOffset observedAt)
    {
        if (this.recentMissionContexts.TryGetValue(
                characterId,
                out var context) &&
            observedAt - context.OccurredAt > missionCorrelationWindow)
        {
            this.recentMissionContexts.Remove(characterId);
        }
    }

    private void PruneRecentCombatContext(
        uint characterId,
        DateTimeOffset observedAt)
    {
        if (this.recentCombatContexts.TryGetValue(
                characterId,
                out var context) &&
            observedAt - context.OccurredAt > recentCombatContextRetentionDuration)
        {
            this.recentCombatContexts.Remove(characterId);
        }
    }

    private void StoreAndPublish(IReadOnlyList<ActivityJournalWrite> writes)
    {
        if (writes.Count == 0)
        {
            return;
        }

        List<uint> changedCharacterIds = [];
        List<ActivityJournalEntryRecordedEventArgs> recorded = [];

        lock (this.stateLock)
        {
            if (!this.enabled)
            {
                return;
            }

            foreach (var write in writes)
            {
                try
                {
                    switch (write)
                    {
                        case ActivityEntryWrite activityWrite:
                            this.store.Append(activityWrite.Entry);
                            recorded.Add(
                                new ActivityJournalEntryRecordedEventArgs(
                                    activityWrite.Entry));
                            break;

                        case ReputationEntryWrite reputationWrite:
                            this.store.AppendReputation(
                                reputationWrite.Activity,
                                reputationWrite.Reputation);
                            recorded.Add(
                                new ActivityJournalEntryRecordedEventArgs(
                                    reputationWrite.Activity,
                                    reputationWrite.Reputation));
                            break;

                        case LootSessionWrite lootWrite:
                            this.store.UpsertLootSession(
                                lootWrite.Activity,
                                lootWrite.Session);
                            recorded.Add(
                                new ActivityJournalEntryRecordedEventArgs(
                                    lootWrite.Activity,
                                    loot: lootWrite.Session));
                            break;
                    }

                    changedCharacterIds.Add(write.CharacterId);
                }
                catch (Exception exception)
                {
                    Debug.WriteLine(
                        string.Concat(
                            "Activity Journal write failed: ",
                            exception),
                        "Net7.ActivityJournal");
                }
            }
        }

        foreach (var entry in recorded)
        {
            this.EntryRecorded?.Invoke(this, entry);
        }

        foreach (var characterId in changedCharacterIds.Distinct())
        {
            this.JournalChanged?.Invoke(
                this,
                new ActivityJournalChangedEventArgs(characterId));
        }
    }

    private static ActivityJournalEntry? BuildCraftingActivity(
        RecipeMappingHistoryEventRecord craftingEvent,
        string pilotName,
        JournalLocationContext location)
    {
        if (craftingEvent.Kind == RecipeMappingHistoryEventKind.RecipeLearnedByScan)
        {
            // The recipe-specific acquisition belongs in Crafting Recipes.
            // Activity History receives one compact event for the completed
            // scan instead of one row per discovered formula.
            return null;
        }

        var itemName = craftingEvent.ItemTemplateId > 0
            ? ClientItemTemplateNameResolver.GetKnownName(
                craftingEvent.ItemTemplateId) ??
                string.Concat("Item ", craftingEvent.ItemTemplateId)
            : "";
        var kind = craftingEvent.Kind switch
        {
            RecipeMappingHistoryEventKind.RecipeScanCompleted =>
                ActivityJournalKind.CraftingRecipeScan,
            RecipeMappingHistoryEventKind.AnalyzeFailed or
                RecipeMappingHistoryEventKind.AnalyzeFailedDamaged =>
                ActivityJournalKind.CraftingAnalyzeFailed,
            RecipeMappingHistoryEventKind.AnalyzeSucceeded =>
                ActivityJournalKind.CraftingAnalyzeSucceeded,
            RecipeMappingHistoryEventKind.AnalyzeCriticalSucceeded =>
                ActivityJournalKind.CraftingAnalyzeCritical,
            RecipeMappingHistoryEventKind.DismantleFailed or
                RecipeMappingHistoryEventKind.DismantleFailedDamaged =>
                ActivityJournalKind.CraftingDismantleFailed,
            RecipeMappingHistoryEventKind.DismantleSucceeded =>
                ActivityJournalKind.CraftingDismantled,
            RecipeMappingHistoryEventKind.DismantleCriticalSucceeded =>
                ActivityJournalKind.CraftingDismantleCritical,
            RecipeMappingHistoryEventKind.ManufactureFailed or
                RecipeMappingHistoryEventKind.ManufactureFailedDamaged =>
                ActivityJournalKind.CraftingManufactureFailed,
            RecipeMappingHistoryEventKind.ManufactureSucceeded =>
                ActivityJournalKind.CraftingManufactured,
            RecipeMappingHistoryEventKind.ManufactureCriticalSucceeded =>
                ActivityJournalKind.CraftingManufactureCritical,
            _ => (ActivityJournalKind?)null,
        };
        if (!kind.HasValue)
        {
            return null;
        }

        var outcome = craftingEvent.Kind switch
        {
            RecipeMappingHistoryEventKind.AnalyzeFailed => "Failed",
            RecipeMappingHistoryEventKind.AnalyzeFailedDamaged =>
                "Failed; item damaged",
            RecipeMappingHistoryEventKind.AnalyzeSucceeded => "Succeeded",
            RecipeMappingHistoryEventKind.AnalyzeCriticalSucceeded =>
                "Critical success",
            RecipeMappingHistoryEventKind.DismantleFailed => "Failed",
            RecipeMappingHistoryEventKind.DismantleFailedDamaged =>
                "Failed; item damaged",
            RecipeMappingHistoryEventKind.DismantleSucceeded => "Succeeded",
            RecipeMappingHistoryEventKind.DismantleCriticalSucceeded =>
                "Critical success",
            RecipeMappingHistoryEventKind.ManufactureFailed => "Failed",
            RecipeMappingHistoryEventKind.ManufactureFailedDamaged =>
                "Failed; item damaged",
            RecipeMappingHistoryEventKind.ManufactureSucceeded => "Succeeded",
            RecipeMappingHistoryEventKind.ManufactureCriticalSucceeded =>
                "Critical success",
            _ => "Completed",
        };

        string summary;
        if (craftingEvent.Kind == RecipeMappingHistoryEventKind.RecipeScanCompleted)
        {
            summary = craftingEvent.NewRecipeCount == 1
                ? string.Create(
                    CultureInfo.CurrentCulture,
                    $"Scanned {craftingEvent.RecipeCount:N0} recipes · 1 new recipe")
                : string.Create(
                    CultureInfo.CurrentCulture,
                    $"Scanned {craftingEvent.RecipeCount:N0} recipes · {craftingEvent.NewRecipeCount:N0} new recipes");
        }
        else if ((craftingEvent.Kind is
                  RecipeMappingHistoryEventKind.ManufactureSucceeded or
                  RecipeMappingHistoryEventKind.ManufactureCriticalSucceeded) &&
                 craftingEvent.OutcomeQualityPercent.HasValue)
        {
            summary = string.Create(
                CultureInfo.CurrentCulture,
                $"Manufactured {itemName} at {craftingEvent.OutcomeQualityPercent.Value:0.#}%");
        }
        else
        {
            summary = craftingEvent.Kind switch
            {
                RecipeMappingHistoryEventKind.AnalyzeFailed or
                    RecipeMappingHistoryEventKind.AnalyzeFailedDamaged =>
                    string.Concat("Analyze failed: ", itemName),
                RecipeMappingHistoryEventKind.AnalyzeSucceeded =>
                    string.Concat("Analyzed ", itemName, " · recipe mapped"),
                RecipeMappingHistoryEventKind.AnalyzeCriticalSucceeded =>
                    string.Concat(
                        "Critical analyze: ",
                        itemName,
                        " · recipe mapped"),
                RecipeMappingHistoryEventKind.DismantleFailed or
                    RecipeMappingHistoryEventKind.DismantleFailedDamaged =>
                    string.Concat("Dismantle failed: ", itemName),
                RecipeMappingHistoryEventKind.DismantleSucceeded =>
                    string.Concat("Dismantled ", itemName),
                RecipeMappingHistoryEventKind.DismantleCriticalSucceeded =>
                    string.Concat("Critical dismantle: ", itemName),
                RecipeMappingHistoryEventKind.ManufactureFailed or
                    RecipeMappingHistoryEventKind.ManufactureFailedDamaged =>
                    string.Concat("Manufacture failed: ", itemName),
                RecipeMappingHistoryEventKind.ManufactureSucceeded =>
                    string.Concat("Manufactured ", itemName),
                RecipeMappingHistoryEventKind.ManufactureCriticalSucceeded =>
                    string.Concat("Critical manufacture: ", itemName),
                _ => string.Concat("Crafting: ", itemName),
            };
        }

        List<string> details = [];
        if (craftingEvent.Kind == RecipeMappingHistoryEventKind.RecipeScanCompleted)
        {
            details.Add(string.Create(
                CultureInfo.CurrentCulture,
                $"Recipes observed: {craftingEvent.RecipeCount:N0}"));
            details.Add(string.Create(
                CultureInfo.CurrentCulture,
                $"New recipes recorded: {craftingEvent.NewRecipeCount:N0}"));
        }
        else
        {
            details.Add(string.Concat("Result: ", outcome));

            if (craftingEvent.Kind is
                RecipeMappingHistoryEventKind.AnalyzeSucceeded or
                RecipeMappingHistoryEventKind.AnalyzeCriticalSucceeded)
            {
                details.Add("Recipe: Mapped");
            }

            if (craftingEvent.CreditsSpent.HasValue)
            {
                details.Add(string.Create(
                    CultureInfo.CurrentCulture,
                    $"Cost: {craftingEvent.CreditsSpent.Value:N0} credits"));
            }

            if (craftingEvent.SuccessProbabilityPercent.HasValue)
            {
                details.Add(string.Create(
                    CultureInfo.CurrentCulture,
                    $"Success chance: {craftingEvent.SuccessProbabilityPercent.Value:0.#}%"));
            }

            if (craftingEvent.CriticalSuccessProbabilityPercent.HasValue)
            {
                details.Add(string.Create(
                    CultureInfo.CurrentCulture,
                    $"Critical chance: {craftingEvent.CriticalSuccessProbabilityPercent.Value:0.#}%"));
            }

            if (craftingEvent.OutputQuantity > 0)
            {
                details.Add(string.Create(
                    CultureInfo.CurrentCulture,
                    $"Output quantity: {craftingEvent.OutputQuantity:N0}"));
            }

            if (craftingEvent.OutcomeQualityPercent.HasValue)
            {
                details.Add(string.Create(
                    CultureInfo.CurrentCulture,
                    $"Output quality: {craftingEvent.OutcomeQualityPercent.Value:0.#}%"));
            }

            if (craftingEvent.ResultItems.Count > 0)
            {
                details.Add("");
                details.Add("Components recovered");
                foreach (var item in craftingEvent.ResultItems)
                {
                    var name = ClientItemTemplateNameResolver.GetKnownName(
                        item.ItemTemplateId) ??
                        string.Concat("Item ", item.ItemTemplateId);
                    details.Add(string.Concat(
                        item.Quantity.ToString(CultureInfo.CurrentCulture),
                        " × ",
                        name,
                        item.QualityPercent.HasValue
                            ? string.Create(
                                CultureInfo.CurrentCulture,
                                $" ({item.QualityPercent.Value:0.#}%)")
                            : ""));
                }
            }
        }

        var payload = JsonSerializer.Serialize(new
        {
            craftingEvent.ItemTemplateId,
            craftingEvent.Kind,
            craftingEvent.ResultValidity,
            craftingEvent.OutcomeQualityPercent,
            craftingEvent.CreditsSpent,
            craftingEvent.SuccessProbabilityPercent,
            craftingEvent.CriticalSuccessProbabilityPercent,
            craftingEvent.OutputQuantity,
            craftingEvent.ResultItems,
            craftingEvent.RecipeCount,
            craftingEvent.NewRecipeCount,
        });

        return new ActivityJournalEntry
        {
            CharacterId = craftingEvent.CharacterId,
            PilotName = pilotName,
            OccurredAt = craftingEvent.ObservedAtUtc,
            Category = craftingEvent.CreditsSpent is > 0
                ? ActivityJournalCategory.Crafting | ActivityJournalCategory.Credits
                : ActivityJournalCategory.Crafting,
            Kind = kind.Value,
            Summary = summary,
            Details = string.Join(Environment.NewLine, details),
            SystemName = location.SystemName,
            SectorName = location.SectorName,
            StarbaseName = location.StarbaseName,
            NearestNavName = location.NearestNavName,
            PayloadVersion = 2,
            PayloadJson = payload,
        };
    }

    private static ActivityJournalEntry? BuildCombatActivity(
        CombatJournalEncounter encounter)
    {
        var kind = encounter.Outcome switch
        {
            CombatJournalOutcome.Killed => ActivityJournalKind.CombatKilled,
            CombatJournalOutcome.Died => ActivityJournalKind.CombatDied,
            CombatJournalOutcome.Disengaged =>
                ActivityJournalKind.CombatDisengaged,
            CombatJournalOutcome.Interrupted =>
                ActivityJournalKind.CombatInterrupted,
            _ => (ActivityJournalKind?)null,
        };

        if (!kind.HasValue)
        {
            return null;
        }

        var summary = encounter.Outcome switch
        {
            CombatJournalOutcome.Killed =>
                string.Concat("Killed ", encounter.TargetName),
            CombatJournalOutcome.Died =>
                string.Concat("Died fighting ", encounter.TargetName),
            CombatJournalOutcome.Disengaged =>
                string.Concat("Disengaged from ", encounter.TargetName),
            CombatJournalOutcome.Interrupted =>
                string.Concat("Encounter with ", encounter.TargetName, " ended"),
            _ => encounter.TargetName,
        };
        var duration = FormatDuration(encounter.Duration);
        var details = string.Join(
            Environment.NewLine,
            new[]
            {
                string.Concat("Duration: ", duration),
                string.Concat(
                    "Damage dealt: ",
                    encounter.OutgoingDamage.ToString(
                        "N0",
                        CultureInfo.CurrentCulture)),
                string.Concat(
                    "Damage received: ",
                    encounter.IncomingDamage.ToString(
                        "N0",
                        CultureInfo.CurrentCulture)),
            });

        return new ActivityJournalEntry
        {
            CharacterId = encounter.CharacterId,
            PilotName = encounter.PilotName,
            OccurredAt = encounter.EndedAt ?? encounter.LastEventAt,
            Category = ActivityJournalCategory.Combat,
            Kind = kind.Value,
            Summary = summary,
            Details = details,
            SystemName = encounter.SystemName,
            SectorName = encounter.SectorName,
            StarbaseName = encounter.StarbaseName,
            NearestNavName = encounter.NearestNavName,
            RelatedCombatEncounterId = encounter.EncounterId,
        };
    }

    private static string FormatDuration(TimeSpan duration)
    {
        duration = duration < TimeSpan.Zero ? TimeSpan.Zero : duration;

        if (duration.TotalMinutes >= 1)
        {
            return string.Create(
                CultureInfo.CurrentCulture,
                $"{(int)duration.TotalMinutes}m {duration.Seconds}s");
        }

        return string.Create(
            CultureInfo.CurrentCulture,
            $"{Math.Max(0, (int)Math.Round(duration.TotalSeconds))}s");
    }

    private static ActivityJournalEntry? BuildMissionActivity(
        MissionJournalLifecycleEventArgs e,
        JournalLocationContext? liveLocation)
    {
        var entry = e.Entry;
        var kind = e.Kind switch
        {
            MissionJournalEventKind.Accepted =>
                ActivityJournalKind.MissionAccepted,
            MissionJournalEventKind.Progressed =>
                ActivityJournalKind.MissionProgressed,
            MissionJournalEventKind.Completed =>
                ActivityJournalKind.MissionCompleted,
            MissionJournalEventKind.Forfeited =>
                ActivityJournalKind.MissionForfeited,
            MissionJournalEventKind.Failed =>
                ActivityJournalKind.MissionFailed,
            MissionJournalEventKind.Expired =>
                ActivityJournalKind.MissionExpired,
            MissionJournalEventKind.NoLongerActive =>
                ActivityJournalKind.MissionNoLongerActive,
            _ => (ActivityJournalKind?)null,
        };

        if (!kind.HasValue)
        {
            return null;
        }

        var summary = kind.Value switch
        {
            ActivityJournalKind.MissionAccepted => entry.Source ==
                MissionJournalSource.JobTerminal
                    ? string.Concat(
                        "Accepted ",
                        entry.TypeDisplay.ToLower(CultureInfo.CurrentCulture),
                        ": ",
                        entry.Name)
                    : string.Concat("Accepted mission: ", entry.Name),
            ActivityJournalKind.MissionProgressed when entry.Stage.HasValue =>
                string.Concat(
                    "Advanced ",
                    entry.Name,
                    " to step ",
                    entry.Stage.Value.ToString(CultureInfo.CurrentCulture)),
            ActivityJournalKind.MissionProgressed =>
                string.Concat("Objective updated: ", entry.Name),
            ActivityJournalKind.MissionCompleted =>
                string.Concat("Completed ", entry.Name),
            ActivityJournalKind.MissionForfeited =>
                string.Concat("Forfeited ", entry.Name),
            ActivityJournalKind.MissionFailed =>
                string.Concat("Failed ", entry.Name),
            ActivityJournalKind.MissionExpired =>
                string.Concat("Expired: ", entry.Name),
            ActivityJournalKind.MissionNoLongerActive =>
                string.Concat("No longer active: ", entry.Name),
            _ => entry.Name,
        };

        var details = BuildMissionDetails(entry, kind.Value);
        var nearestNavName = liveLocation != null &&
            string.IsNullOrWhiteSpace(e.StarbaseName) &&
            Same(liveLocation.SystemName, e.SystemName) &&
            Same(liveLocation.SectorName, e.SectorName)
                ? liveLocation.NearestNavName
                : "";

        return new ActivityJournalEntry
        {
            CharacterId = entry.CharacterId,
            PilotName = entry.PilotName,
            OccurredAt = e.OccurredAt,
            Category = ActivityJournalCategory.Missions,
            Kind = kind.Value,
            Summary = summary,
            Details = details,
            SystemName = e.SystemName,
            SectorName = e.SectorName,
            StarbaseName = e.StarbaseName,
            NearestNavName = nearestNavName,
            RelatedMissionEpisodeId = entry.EpisodeId,
        };
    }

    private static string BuildMissionDetails(
        MissionJournalEntry entry,
        ActivityJournalKind kind)
    {
        List<string> lines = [];

        if (!string.IsNullOrWhiteSpace(entry.CurrentObjective))
        {
            lines.Add(entry.CurrentObjective);
        }

        if (kind == ActivityJournalKind.MissionAccepted &&
            !string.IsNullOrWhiteSpace(entry.IssuerNpcName))
        {
            lines.Add(string.Concat("Accepted from ", entry.IssuerNpcName, "."));
        }

        if (kind == ActivityJournalKind.MissionAccepted &&
            entry.Source == MissionJournalSource.JobTerminal)
        {
            lines.Add("Accepted from a Job Terminal.");
        }

        if (kind == ActivityJournalKind.MissionAccepted &&
            !string.IsNullOrWhiteSpace(entry.RewardText))
        {
            lines.Add(string.Concat("Reward: ", entry.RewardText));
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "" : value.Trim();

    private static bool Same(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private abstract record ActivityJournalWrite(uint CharacterId);

    private sealed record ActivityEntryWrite(ActivityJournalEntry Entry)
        : ActivityJournalWrite(Entry.CharacterId);

    private sealed record ReputationEntryWrite(
        ActivityJournalEntry Activity,
        ReputationJournalEntry Reputation)
        : ActivityJournalWrite(Activity.CharacterId);

    private sealed record LootSessionWrite(
        ActivityJournalEntry Activity,
        LootJournalSession Session)
        : ActivityJournalWrite(Activity.CharacterId);

    private sealed record RecentMissionContext(
        string EpisodeId,
        string Name,
        string RewardText,
        DateTimeOffset OccurredAt);

    private sealed record RecentCombatContext(
        string EncounterId,
        string TargetName,
        DateTimeOffset LastEventAt,
        DateTimeOffset OccurredAt);

    private sealed record PendingReputationChange(
        string FactionKey,
        string DisplayName,
        float PreviousReaction,
        float CurrentReaction,
        JournalLocationContext Location,
        DateTimeOffset OccurredAt);

    private sealed class ActivityProcessState
    {
        private JournalLocationContext stableLocation;
        private JournalLocationContext? candidateLocation;
        private DateTimeOffset candidateLocationFirstSeenAt;
        private int candidateLocationSamples;
        private ulong? stableCredits;
        private ulong? candidateCredits;
        private DateTimeOffset candidateCreditsFirstSeenAt;
        private int candidateCreditsSamples;
        private readonly Dictionary<string, ReputationValueState>
            reputationStates = new(StringComparer.Ordinal);
        private readonly List<PendingReputationChange>
            pendingReputationChanges = [];
        private CargoSnapshot? lastCargo;
        private CorpseSnapshot? lastCorpse;
        private readonly List<PendingLootItem> pendingLootItems = [];
        private readonly List<PendingLootCargoGain> pendingLootCargoGains = [];
        private LootSessionAccumulator? activeLootSession;
        private readonly List<PendingVendorItemChange> pendingVendorItems = [];
        private PendingVendorCredit? pendingVendorCredit;
        private readonly List<PendingCraftingCredit> pendingCraftingCredits = [];
        private PendingCraftingDebit? pendingCraftingDebit;
        private VendorContext? recentVendorContext;

        public ActivityProcessState(
            uint characterId,
            string pilotName,
            JournalLocationContext initialLocation,
            ClientObservationSnapshot initialSnapshot)
        {
            this.CharacterId = characterId;
            this.PilotName = pilotName;
            this.stableLocation = initialLocation;
            this.LatestLocation = initialLocation;
            this.LatestObservedAt = initialSnapshot.ObservedAt;
            this.InitializeEconomyBaselines(initialSnapshot);
        }

        public uint CharacterId { get; }

        public string PilotName { get; set; }

        public JournalLocationContext LatestLocation { get; private set; }

        public DateTimeOffset LatestObservedAt { get; private set; }

        public IReadOnlyList<ActivityJournalWrite> Observe(
            ClientObservationSnapshot snapshot,
            JournalLocationContext location,
            RecentMissionContext? missionContext,
            RecentCombatContext? combatContext)
        {
            this.LatestLocation = location;
            this.LatestObservedAt = snapshot.ObservedAt;
            var currentCargo = CaptureCargo(snapshot);
            var cargoChanges = currentCargo != null && this.lastCargo != null
                ? CalculateCargoChanges(this.lastCargo, currentCargo)
                : [];
            this.ObserveVendorContext(snapshot, location, cargoChanges);

            List<ActivityJournalWrite> writes = [];
            writes.AddRange(this.ObserveLocation(location, snapshot.ObservedAt));
            writes.AddRange(
                this.ObserveReputation(
                    snapshot,
                    location,
                    missionContext,
                    combatContext));
            writes.AddRange(
                this.ObserveLoot(
                    snapshot,
                    location,
                    cargoChanges));
            writes.AddRange(
                this.ObserveCredits(
                    snapshot,
                    location,
                    missionContext));

            if (currentCargo != null)
            {
                this.lastCargo = currentCargo;
            }

            return writes;
        }

        private void InitializeEconomyBaselines(
            ClientObservationSnapshot snapshot)
        {
            if (TryGetCredits(snapshot, out var credits))
            {
                this.stableCredits = credits;
            }

            foreach (var faction in GetUsableFactions(snapshot))
            {
                this.reputationStates[faction.FactionKey] =
                    new ReputationValueState(faction.Reaction);
            }

            this.lastCargo = CaptureCargo(snapshot);
            this.lastCorpse = CaptureCorpse(snapshot);
        }

        private IReadOnlyList<ActivityJournalWrite> ObserveLocation(
            JournalLocationContext location,
            DateTimeOffset observedAt)
        {
            if (location.MatchesWorldState(this.stableLocation))
            {
                this.ClearLocationCandidate();
                return [];
            }

            if (this.candidateLocation == null ||
                !location.MatchesWorldState(this.candidateLocation))
            {
                this.candidateLocation = location;
                this.candidateLocationFirstSeenAt = observedAt;
                this.candidateLocationSamples = 1;
                return [];
            }

            this.candidateLocationSamples++;

            if (this.candidateLocationSamples < 2 ||
                observedAt - this.candidateLocationFirstSeenAt <
                    locationSettleDuration)
            {
                return [];
            }

            var previous = this.stableLocation;
            this.stableLocation = location;
            this.ClearLocationCandidate();
            return this.BuildTransitionEvents(previous, location, observedAt);
        }

        private IReadOnlyList<ActivityJournalWrite> BuildTransitionEvents(
            JournalLocationContext previous,
            JournalLocationContext current,
            DateTimeOffset occurredAt)
        {
            List<ActivityJournalWrite> writes = [];
            var sectorChanged = !Same(previous.SectorName, current.SectorName) ||
                !Same(previous.SystemName, current.SystemName);

            if (sectorChanged && !string.IsNullOrWhiteSpace(current.SectorName))
            {
                var summary = string.IsNullOrWhiteSpace(current.SystemName)
                    ? string.Concat("Entered ", current.SectorName)
                    : string.Concat(
                        "Entered ",
                        current.SectorName,
                        " in ",
                        current.SystemName);
                writes.Add(new ActivityEntryWrite(
                    this.CreateActivity(
                        ActivityJournalCategory.Navigation,
                        ActivityJournalKind.EnteredSector,
                        summary,
                        current,
                        occurredAt)));
                return writes;
            }

            if (Same(previous.StarbaseName, current.StarbaseName))
            {
                return writes;
            }

            if (!string.IsNullOrWhiteSpace(previous.StarbaseName) &&
                string.IsNullOrWhiteSpace(current.StarbaseName))
            {
                writes.Add(new ActivityEntryWrite(
                    this.CreateActivity(
                        ActivityJournalCategory.Navigation,
                        ActivityJournalKind.Undocked,
                        string.Concat("Undocked from ", previous.StarbaseName),
                        current,
                        occurredAt,
                        string.Concat("Departed ", previous.StarbaseName, "."))));
                return writes;
            }

            if (!string.IsNullOrWhiteSpace(current.StarbaseName))
            {
                writes.Add(new ActivityEntryWrite(
                    this.CreateActivity(
                        ActivityJournalCategory.Navigation,
                        ActivityJournalKind.Docked,
                        string.Concat("Docked at ", current.StarbaseName),
                        current,
                        occurredAt)));
            }

            return writes;
        }

        private IReadOnlyList<ActivityJournalWrite> ObserveReputation(
            ClientObservationSnapshot snapshot,
            JournalLocationContext location,
            RecentMissionContext? missionContext,
            RecentCombatContext? combatContext)
        {
            List<ActivityJournalWrite> writes = [];
            writes.AddRange(
                this.ResolvePendingReputationChanges(
                    snapshot.ObservedAt,
                    missionContext,
                    combatContext));

            foreach (var faction in GetUsableFactions(snapshot))
            {
                if (!this.reputationStates.TryGetValue(
                        faction.FactionKey,
                        out var state))
                {
                    this.reputationStates[faction.FactionKey] =
                        new ReputationValueState(faction.Reaction);
                    continue;
                }

                if (!state.Observe(
                        faction.Reaction,
                        snapshot.ObservedAt,
                        out var previous,
                        out var current))
                {
                    continue;
                }

                var delta = current - previous;

                if (Math.Abs(delta) < 0.01f)
                {
                    continue;
                }

                var pending = new PendingReputationChange(
                    faction.FactionKey,
                    faction.DisplayName,
                    previous,
                    current,
                    location,
                    snapshot.ObservedAt);
                var correlatedCombat = IsCombatContextNear(
                        combatContext,
                        snapshot.ObservedAt)
                    ? combatContext
                    : null;
                var correlatedMission = correlatedCombat == null &&
                    IsMissionContextCurrent(
                        missionContext,
                        snapshot.ObservedAt)
                    ? missionContext
                    : null;

                if (correlatedCombat == null &&
                    correlatedMission == null)
                {
                    this.pendingReputationChanges.Add(pending);
                    continue;
                }

                writes.Add(
                    this.BuildReputationWrite(
                        pending,
                        correlatedMission,
                        correlatedCombat));
            }

            return writes;
        }

        public IReadOnlyList<ActivityJournalWrite>
            ResolvePendingReputationChanges(
                RecentCombatContext combatContext)
        {
            if (this.pendingReputationChanges.Count == 0)
            {
                return [];
            }

            List<ActivityJournalWrite> writes = [];

            for (var index = 0;
                 index < this.pendingReputationChanges.Count;)
            {
                var pending = this.pendingReputationChanges[index];

                if (!IsCombatContextNear(
                        combatContext,
                        pending.OccurredAt))
                {
                    index++;
                    continue;
                }

                writes.Add(
                    this.BuildReputationWrite(
                        pending,
                        null,
                        combatContext));
                this.pendingReputationChanges.RemoveAt(index);
            }

            return writes;
        }

        private IReadOnlyList<ActivityJournalWrite>
            ResolvePendingReputationChanges(
                DateTimeOffset observedAt,
                RecentMissionContext? missionContext,
                RecentCombatContext? combatContext)
        {
            if (this.pendingReputationChanges.Count == 0)
            {
                return [];
            }

            List<ActivityJournalWrite> writes = [];

            for (var index = 0;
                 index < this.pendingReputationChanges.Count;)
            {
                var pending = this.pendingReputationChanges[index];
                var correlatedCombat = IsCombatContextNear(
                        combatContext,
                        pending.OccurredAt)
                    ? combatContext
                    : null;
                var correlatedMission = correlatedCombat == null &&
                    IsMissionContextNear(
                        missionContext,
                        pending.OccurredAt)
                    ? missionContext
                    : null;
                var hasSettled =
                    observedAt - pending.OccurredAt >=
                    reputationReasonFallbackDuration;

                if (correlatedCombat == null &&
                    correlatedMission == null &&
                    !hasSettled)
                {
                    index++;
                    continue;
                }

                writes.Add(
                    this.BuildReputationWrite(
                        pending,
                        correlatedMission,
                        correlatedCombat));
                this.pendingReputationChanges.RemoveAt(index);
            }

            return writes;
        }

        private ReputationEntryWrite BuildReputationWrite(
            PendingReputationChange pending,
            RecentMissionContext? correlatedMission,
            RecentCombatContext? correlatedCombat)
        {
            var delta =
                pending.CurrentReaction -
                pending.PreviousReaction;
            var reason = correlatedCombat != null
                ? string.Concat(
                    "Killed ",
                    correlatedCombat.TargetName)
                : correlatedMission != null
                    ? string.Concat(
                        "After completing ",
                        correlatedMission.Name)
                    : "";
            var summary = string.Concat(
                pending.DisplayName,
                " ",
                FormatSigned(delta));
            var details = string.Join(
                Environment.NewLine,
                new[]
                {
                    string.Concat(
                        "Previous: ",
                        FormatReputation(pending.PreviousReaction)),
                    string.Concat(
                        "Current: ",
                        FormatReputation(pending.CurrentReaction)),
                    reason,
                }.Where(value => !string.IsNullOrWhiteSpace(value)));
            var category = ActivityJournalCategory.Reputation;

            if (correlatedMission != null)
            {
                category |= ActivityJournalCategory.Missions;
            }

            if (correlatedCombat != null)
            {
                category |= ActivityJournalCategory.Combat;
            }

            var activity = this.CreateActivity(
                category,
                ActivityJournalKind.ReputationChanged,
                summary,
                pending.Location,
                pending.OccurredAt,
                details,
                correlatedMission?.EpisodeId ?? "",
                relatedCombatEncounterId:
                    correlatedCombat?.EncounterId ?? "");
            var reputation = new ReputationJournalEntry
            {
                CharacterId = this.CharacterId,
                OccurredAt = pending.OccurredAt,
                FactionKey = pending.FactionKey,
                DisplayName = pending.DisplayName,
                PreviousReaction = pending.PreviousReaction,
                CurrentReaction = pending.CurrentReaction,
                SystemName = pending.Location.SystemName,
                SectorName = pending.Location.SectorName,
                StarbaseName = pending.Location.StarbaseName,
                NearestNavName = pending.Location.NearestNavName,
                Reason = reason,
                RelatedMissionEpisodeId =
                    correlatedMission?.EpisodeId ?? "",
                RelatedCombatEncounterId =
                    correlatedCombat?.EncounterId ?? "",
            };

            return new ReputationEntryWrite(activity, reputation);
        }

        public void RegisterCraftingCredit(
            RecipeMappingHistoryEventRecord craftingEvent)
        {
            if (craftingEvent.CreditsSpent is not { } amount || amount <= 0)
            {
                return;
            }

            this.PruneCraftingCredits(craftingEvent.ObservedAtUtc);

            if (this.pendingCraftingDebit is { } pendingDebit &&
                pendingDebit.Amount == amount &&
                craftingEvent.ObservedAtUtc <= pendingDebit.ExpiresAt)
            {
                // The slow credit lane saw the debit before the crafting result
                // arrived. The result now owns that exact debit, so the held
                // anonymous credit row is discarded.
                this.pendingCraftingDebit = null;
                return;
            }

            this.pendingCraftingCredits.Add(new PendingCraftingCredit(
                amount,
                craftingEvent.ObservedAtUtc,
                craftingEvent.ObservedAtUtc + craftingCreditCorrelationWindow));
        }

        private bool TryConsumeCraftingCredits(
            long amount,
            DateTimeOffset observedAt)
        {
            if (amount <= 0)
            {
                return false;
            }

            this.PruneCraftingCredits(observedAt);
            long sum = 0;
            for (var index = this.pendingCraftingCredits.Count - 1;
                 index >= 0;
                 index--)
            {
                var candidate = this.pendingCraftingCredits[index];
                if (candidate.Amount > amount - sum)
                {
                    break;
                }

                sum += candidate.Amount;

                if (sum == amount)
                {
                    this.pendingCraftingCredits.RemoveRange(
                        index,
                        this.pendingCraftingCredits.Count - index);
                    return true;
                }

                if (sum > amount)
                {
                    break;
                }
            }

            return false;
        }

        private void PruneCraftingCredits(DateTimeOffset observedAt)
        {
            this.pendingCraftingCredits.RemoveAll(item =>
                observedAt > item.ExpiresAt);
        }

        private ActivityEntryWrite? ResolvePendingCraftingDebit(
            DateTimeOffset observedAt)
        {
            if (this.pendingCraftingDebit == null ||
                observedAt <= this.pendingCraftingDebit.ExpiresAt)
            {
                return null;
            }

            var pending = this.pendingCraftingDebit;
            this.pendingCraftingDebit = null;
            return this.BuildGenericCreditWrite(
                pending.PreviousCredits,
                pending.CurrentCredits,
                -pending.Amount,
                pending.Location,
                pending.OccurredAt,
                null);
        }

        private static long? ResolveActiveCraftingCost(
            ClientObservationSnapshot snapshot)
        {
            var activity = snapshot.ManufacturingActivity;
            if (!activity.IsAvailable ||
                (!activity.IsAnalyzePanelActive &&
                 !activity.IsManufacturingPanelActive) ||
                activity.NegotiatedCostCredits is not { } cost ||
                cost == 0 ||
                cost > (ulong)long.MaxValue)
            {
                return null;
            }

            return checked((long)cost);
        }

        private IReadOnlyList<ActivityJournalWrite> ObserveCredits(
            ClientObservationSnapshot snapshot,
            JournalLocationContext location,
            RecentMissionContext? missionContext)
        {
            List<ActivityJournalWrite> writes = [];
            var pendingCraftingWrite = this.ResolvePendingCraftingDebit(
                snapshot.ObservedAt);
            if (pendingCraftingWrite != null)
            {
                writes.Add(pendingCraftingWrite);
            }

            var pendingVendorWrite = this.ResolvePendingVendorCredit(
                snapshot.ObservedAt);

            if (pendingVendorWrite != null)
            {
                writes.Add(pendingVendorWrite);
            }

            if (!TryGetCredits(snapshot, out var currentCredits))
            {
                this.ClearCreditsCandidate();
                return writes;
            }

            if (!this.stableCredits.HasValue)
            {
                this.stableCredits = currentCredits;
                this.ClearCreditsCandidate();
                return writes;
            }

            if (this.stableCredits.Value == currentCredits)
            {
                this.ClearCreditsCandidate();
                return writes;
            }

            // A transaction-speed cargo edge is stronger evidence than the
            // slower starbase interaction snapshot. Commit the matching credit
            // balance immediately instead of waiting 500 ms, otherwise several
            // rapid vendor clicks collapse into one fake high-price purchase.
            var hasFreshVendorTransactionEvidence =
                this.pendingVendorItems.Any(item =>
                    Math.Abs(
                        (item.ObservedAt - snapshot.ObservedAt)
                        .TotalMilliseconds) <= 250);

            if (!hasFreshVendorTransactionEvidence)
            {
                if (!this.candidateCredits.HasValue ||
                    this.candidateCredits.Value != currentCredits)
                {
                    this.candidateCredits = currentCredits;
                    this.candidateCreditsFirstSeenAt = snapshot.ObservedAt;
                    this.candidateCreditsSamples = 1;
                    return writes;
                }

                this.candidateCreditsSamples++;

                if (this.candidateCreditsSamples < 2 ||
                    snapshot.ObservedAt - this.candidateCreditsFirstSeenAt <
                        valueSettleDuration)
                {
                    return writes;
                }
            }

            var previousCredits = this.stableCredits.Value;
            this.stableCredits = currentCredits;
            this.ClearCreditsCandidate();
            var delta = CalculateCreditDelta(previousCredits, currentCredits);

            if (delta == 0)
            {
                return writes;
            }

            if (delta < 0)
            {
                var spentAmount = Math.Abs(delta);
                if (this.TryConsumeCraftingCredits(
                        spentAmount,
                        snapshot.ObservedAt))
                {
                    return writes;
                }

                if (ResolveActiveCraftingCost(snapshot) == spentAmount)
                {
                    if (this.pendingCraftingDebit != null)
                    {
                        writes.Add(this.BuildGenericCreditWrite(
                            this.pendingCraftingDebit.PreviousCredits,
                            this.pendingCraftingDebit.CurrentCredits,
                            -this.pendingCraftingDebit.Amount,
                            this.pendingCraftingDebit.Location,
                            this.pendingCraftingDebit.OccurredAt,
                            null));
                    }

                    this.pendingCraftingDebit = new PendingCraftingDebit(
                        previousCredits,
                        currentCredits,
                        spentAmount,
                        location,
                        snapshot.ObservedAt,
                        snapshot.ObservedAt + craftingCreditCorrelationWindow);
                    return writes;
                }
            }

            var correlatedMission = TryCorrelateMissionCreditReward(
                missionContext,
                snapshot.ObservedAt,
                delta);

            if (delta > 0 && correlatedMission == null &&
                this.TryAddLootCredits(
                    snapshot,
                    location,
                    delta,
                    out var lootWrite))
            {
                writes.Add(lootWrite);
                return writes;
            }

            var vendorContext = this.GetRecentVendorContext(snapshot.ObservedAt);
            var vendorCredit = correlatedMission == null && vendorContext != null
                ? new PendingVendorCredit(
                    previousCredits,
                    currentCredits,
                    delta,
                    location,
                    vendorContext,
                    snapshot.ObservedAt,
                    snapshot.ObservedAt + pendingVendorCreditWindow)
                : null;

            if (vendorCredit != null)
            {
                if (this.TryBuildVendorTransactionWrite(
                        vendorCredit,
                        snapshot.ObservedAt,
                        out var vendorWrite))
                {
                    writes.Add(vendorWrite);
                }
                else
                {
                    if (this.pendingVendorCredit != null)
                    {
                        writes.Add(this.BuildGenericCreditWrite(
                            this.pendingVendorCredit.PreviousCredits,
                            this.pendingVendorCredit.CurrentCredits,
                            this.pendingVendorCredit.Delta,
                            this.pendingVendorCredit.Location,
                            this.pendingVendorCredit.OccurredAt,
                            null));
                    }

                    this.pendingVendorCredit = vendorCredit;
                }

                return writes;
            }

            writes.Add(this.BuildGenericCreditWrite(
                previousCredits,
                currentCredits,
                delta,
                location,
                snapshot.ObservedAt,
                correlatedMission));
            return writes;
        }

        private ActivityEntryWrite? ResolvePendingVendorCredit(
            DateTimeOffset observedAt)
        {
            if (this.pendingVendorCredit == null)
            {
                return null;
            }

            if (this.TryBuildVendorTransactionWrite(
                    this.pendingVendorCredit,
                    observedAt,
                    out var vendorWrite))
            {
                this.pendingVendorCredit = null;
                return vendorWrite;
            }

            if (observedAt < this.pendingVendorCredit.ExpiresAt)
            {
                return null;
            }

            var pending = this.pendingVendorCredit;
            this.pendingVendorCredit = null;
            return this.BuildGenericCreditWrite(
                pending.PreviousCredits,
                pending.CurrentCredits,
                pending.Delta,
                pending.Location,
                pending.OccurredAt,
                null);
        }

        private ActivityEntryWrite BuildGenericCreditWrite(
            ulong previousCredits,
            ulong currentCredits,
            long delta,
            JournalLocationContext location,
            DateTimeOffset occurredAt,
            RecentMissionContext? correlatedMission)
        {
            var kind = delta > 0
                ? ActivityJournalKind.CreditsGained
                : ActivityJournalKind.CreditsSpent;
            var verb = delta > 0 ? "Gained" : "Spent";
            var amount = Math.Abs(delta).ToString(
                "N0",
                CultureInfo.CurrentCulture);
            var summary = correlatedMission == null
                ? string.Concat(verb, " ", amount, " credits")
                : string.Concat(
                    verb,
                    " ",
                    amount,
                    " credits from ",
                    correlatedMission.Name);
            var details = string.Join(
                Environment.NewLine,
                new[]
                {
                    string.Concat(
                        "Previous balance: ",
                        previousCredits.ToString(
                            "N0",
                            CultureInfo.CurrentCulture)),
                    string.Concat(
                        "New balance: ",
                        currentCredits.ToString(
                            "N0",
                            CultureInfo.CurrentCulture)),
                    correlatedMission == null
                        ? ""
                        : string.Concat(
                            "Mission reward: ",
                            correlatedMission.Name),
                }.Where(value => !string.IsNullOrWhiteSpace(value)));
            var category = ActivityJournalCategory.Credits;

            if (correlatedMission != null)
            {
                category |= ActivityJournalCategory.Missions;
            }

            return new ActivityEntryWrite(
                this.CreateActivity(
                    category,
                    kind,
                    summary,
                    location,
                    occurredAt,
                    details,
                    correlatedMission?.EpisodeId ?? ""));
        }

        private IReadOnlyList<ActivityJournalWrite> ObserveLoot(
            ClientObservationSnapshot snapshot,
            JournalLocationContext location,
            IReadOnlyList<CargoChange> cargoChanges)
        {
            List<ActivityJournalWrite> writes = [];
            var currentCorpse = CaptureCorpse(snapshot);
            var source = this.ResolveLootSource(snapshot, location, currentCorpse);

            if (source != null)
            {
                this.EnsureLootSession(
                    source.ObjectId,
                    source.SourceName,
                    source.Location,
                    snapshot.ObservedAt);
            }

            if (this.lastCorpse != null)
            {
                if (currentCorpse != null &&
                    this.lastCorpse.ObjectId == currentCorpse.ObjectId)
                {
                    this.QueueExplicitCorpseRemovals(
                        this.lastCorpse,
                        currentCorpse,
                        snapshot.ObservedAt);
                }
                else if (this.IsActiveLootSourceRelevant(
                             snapshot,
                             this.lastCorpse.ObjectId))
                {
                    this.QueueAllRemainingCorpseItems(
                        this.lastCorpse,
                        snapshot.ObservedAt);
                }
            }

            this.PrunePendingLoot(snapshot.ObservedAt);

            if (snapshot.World.Environment != ClientWorldEnvironment.Starbase)
            {
                var cargoSource = source ?? this.GetRecentLootSource(
                    snapshot.ObservedAt);

                if (cargoSource != null)
                {
                    foreach (var increase in cargoChanges.Where(change =>
                                 change.Delta > 0))
                    {
                        this.pendingLootCargoGains.Add(
                            new PendingLootCargoGain(
                                increase.Item,
                                increase.Delta,
                                cargoSource.ObjectId,
                                snapshot.ObservedAt));
                    }
                }
            }

            writes.AddRange(this.MatchPendingLootTransfers(snapshot.ObservedAt));
            this.PrunePendingLoot(snapshot.ObservedAt);
            this.lastCorpse = currentCorpse;

            if (this.activeLootSession != null &&
                !this.IsLootSourceRelevant(
                    snapshot,
                    this.activeLootSession.SourceObjectId) &&
                !this.pendingLootItems.Any(item =>
                    item.SourceObjectId ==
                        this.activeLootSession.SourceObjectId) &&
                !this.pendingLootCargoGains.Any(item =>
                    item.SourceObjectId ==
                        this.activeLootSession.SourceObjectId) &&
                snapshot.ObservedAt - this.activeLootSession.LastUpdatedAt >
                    recentLootWindow)
            {
                this.activeLootSession = null;
            }

            return CoalesceLootWrites(writes);
        }

        private LootSource? GetRecentLootSource(DateTimeOffset observedAt)
        {
            return this.activeLootSession != null &&
                observedAt - this.activeLootSession.LastUpdatedAt <=
                    recentLootWindow
                ? new LootSource(
                    this.activeLootSession.SourceObjectId,
                    this.activeLootSession.SourceName,
                    this.activeLootSession.Location)
                : null;
        }

        private IReadOnlyList<ActivityJournalWrite> MatchPendingLootTransfers(
            DateTimeOffset observedAt)
        {
            List<ActivityJournalWrite> writes = [];

            foreach (var gain in this.pendingLootCargoGains
                         .Where(item => item.RemainingQuantity > 0)
                         .OrderBy(item => item.ObservedAt)
                         .ToArray())
            {
                foreach (var removed in this.pendingLootItems
                             .Where(item =>
                                 item.RemainingQuantity > 0 &&
                                 item.SourceObjectId == gain.SourceObjectId &&
                                 item.Item.ItemTemplateId ==
                                     gain.Item.ItemTemplateId &&
                                 Math.Abs(
                                     (item.RemovedAt - gain.ObservedAt)
                                     .TotalSeconds) <=
                                         pendingLootWindow.TotalSeconds)
                             .OrderBy(item => item.RemovedAt)
                             .ToArray())
                {
                    if (gain.RemainingQuantity <= 0)
                    {
                        break;
                    }

                    var quantity = Math.Min(
                        gain.RemainingQuantity,
                        removed.RemainingQuantity);

                    if (quantity <= 0)
                    {
                        continue;
                    }

                    gain.RemainingQuantity -= quantity;
                    removed.RemainingQuantity -= quantity;
                    var session = this.EnsureLootSession(
                        removed.SourceObjectId,
                        removed.SourceName,
                        removed.Location,
                        observedAt);
                    session.AddItem(
                        removed.Item.ItemTemplateId,
                        string.IsNullOrWhiteSpace(removed.Item.Name)
                            ? gain.Item.Name
                            : removed.Item.Name,
                        quantity,
                        removed.Item.QualityPercent ??
                            gain.Item.QualityPercent,
                        observedAt);
                    writes.Add(this.BuildLootWrite(session));
                }
            }

            return writes;
        }

        private void PrunePendingLoot(DateTimeOffset observedAt)
        {
            this.pendingLootItems.RemoveAll(item =>
                item.RemainingQuantity <= 0 ||
                observedAt - item.RemovedAt > pendingLootWindow);
            this.pendingLootCargoGains.RemoveAll(item =>
                item.RemainingQuantity <= 0 ||
                observedAt - item.ObservedAt > pendingLootWindow);
        }

        private void QueueExplicitCorpseRemovals(
            CorpseSnapshot previous,
            CorpseSnapshot current,
            DateTimeOffset observedAt)
        {
            foreach (var previousItem in previous.ItemsBySlot.Values)
            {
                var removedQuantity = previousItem.Quantity;

                if (current.ItemsBySlot.TryGetValue(
                        previousItem.Slot,
                        out var currentItem) &&
                    currentItem.ItemTemplateId == previousItem.ItemTemplateId)
                {
                    removedQuantity = Math.Max(
                        0,
                        previousItem.Quantity - currentItem.Quantity);
                }

                if (removedQuantity > 0)
                {
                    this.QueuePendingLootItem(
                        previousItem with { Quantity = removedQuantity },
                        previous,
                        observedAt);
                }
            }
        }

        private void QueueAllRemainingCorpseItems(
            CorpseSnapshot corpse,
            DateTimeOffset observedAt)
        {
            foreach (var item in corpse.ItemsBySlot.Values)
            {
                this.QueuePendingLootItem(item, corpse, observedAt);
            }
        }

        private void QueuePendingLootItem(
            CorpseItem item,
            CorpseSnapshot corpse,
            DateTimeOffset observedAt)
        {
            this.pendingLootItems.Add(
                new PendingLootItem(
                    item,
                    corpse.ObjectId,
                    corpse.SourceName,
                    corpse.Location,
                    observedAt));
        }

        private void ObserveVendorContext(
            ClientObservationSnapshot snapshot,
            JournalLocationContext location,
            IReadOnlyList<CargoChange> cargoChanges)
        {
            var observedAt = snapshot.ObservedAt;
            var observedContext = CaptureVendorContext(snapshot, location);

            if (observedContext != null)
            {
                this.recentVendorContext = observedContext;
            }

            this.pendingVendorItems.RemoveAll(item =>
                observedAt - item.ObservedAt > vendorCorrelationWindow);
            var context = observedContext ??
                this.GetRecentVendorContext(observedAt);

            if (context == null)
            {
                return;
            }

            foreach (var change in cargoChanges.Where(change =>
                         change.Delta != 0))
            {
                this.pendingVendorItems.Add(
                    new PendingVendorItemChange(
                        context,
                        change,
                        observedAt));
            }
        }

        private VendorContext? GetRecentVendorContext(
            DateTimeOffset observedAt)
        {
            return this.recentVendorContext != null &&
                observedAt - this.recentVendorContext.ObservedAt <=
                    vendorCorrelationWindow
                ? this.recentVendorContext
                : null;
        }

        private bool TryBuildVendorTransactionWrite(
            PendingVendorCredit credit,
            DateTimeOffset observedAt,
            out ActivityEntryWrite write)
        {
            write = null!;
            this.pendingVendorItems.RemoveAll(item =>
                observedAt - item.ObservedAt > vendorCorrelationWindow);
            var expectedSign = credit.Delta > 0 ? -1 : 1;
            var candidates = this.pendingVendorItems
                .Where(item =>
                    string.Equals(
                        item.Context.Key,
                        credit.Context.Key,
                        StringComparison.Ordinal) &&
                    Math.Sign(item.Change.Delta) == expectedSign &&
                    Math.Abs(
                        (item.ObservedAt - credit.OccurredAt)
                        .TotalSeconds) <= vendorCorrelationWindow.TotalSeconds)
                .OrderBy(item => Math.Abs(
                    (item.ObservedAt - credit.OccurredAt)
                    .TotalMilliseconds))
                .ToArray();

            if (candidates.Length == 0)
            {
                return false;
            }

            var closest = candidates[0].ObservedAt;
            var selected = candidates
                .Where(item => Math.Abs(
                    (item.ObservedAt - closest).TotalMilliseconds) <= 500)
                .ToArray();

            if (selected.Length == 0)
            {
                return false;
            }

            foreach (var item in selected)
            {
                this.pendingVendorItems.Remove(item);
            }

            var items = selected
                .GroupBy(item => new
                {
                    item.Change.Item.ItemTemplateId,
                    item.Change.Item.Name,
                })
                .Select(group => new VendorTransactionItem(
                    group.Key.ItemTemplateId,
                    group.Key.Name,
                    group.Sum(item => Math.Abs(item.Change.Delta))))
                .Where(item => item.Quantity > 0)
                .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();

            if (items.Length == 0)
            {
                return false;
            }

            var isSale = credit.Delta > 0;
            var amount = Math.Abs(credit.Delta).ToString(
                "N0",
                CultureInfo.CurrentCulture);
            var itemUnits = items.Sum(item => item.Quantity);
            var itemDescription = items.Length == 1
                ? string.Concat(
                    items[0].Quantity.ToString(CultureInfo.CurrentCulture),
                    " × ",
                    items[0].Name)
                : string.Concat(
                    itemUnits.ToString(CultureInfo.CurrentCulture),
                    itemUnits == 1 ? " item" : " items");
            var vendorSuffix = string.IsNullOrWhiteSpace(
                credit.Context.DisplayName)
                ? ""
                : string.Concat(
                    isSale ? " to " : " from ",
                    credit.Context.DisplayName);
            var summary = string.Concat(
                isSale ? "Sold " : "Purchased ",
                itemDescription,
                vendorSuffix,
                " for ",
                amount,
                " credits");
            List<string> details = [];

            if (!string.IsNullOrWhiteSpace(credit.Context.DisplayName))
            {
                details.Add("Vendor");
                details.Add(credit.Context.DisplayName);
                details.Add("");
            }

            details.Add(isSale ? "Items sold" : "Items purchased");
            details.AddRange(items.Select(item => string.Concat(
                item.Quantity.ToString(CultureInfo.CurrentCulture),
                " × ",
                item.Name)));
            details.Add("");
            details.Add(string.Concat(
                isSale ? "Credits received: " : "Credits spent: ",
                amount));
            details.Add(string.Concat(
                "Previous balance: ",
                credit.PreviousCredits.ToString(
                    "N0",
                    CultureInfo.CurrentCulture)));
            details.Add(string.Concat(
                "New balance: ",
                credit.CurrentCredits.ToString(
                    "N0",
                    CultureInfo.CurrentCulture)));
            var payloadJson = JsonSerializer.Serialize(new
            {
                direction = isSale ? "sale" : "purchase",
                vendor = credit.Context.DisplayName,
                credits = Math.Abs(credit.Delta),
                items = items.Select(item => new
                {
                    itemTemplateId = item.ItemTemplateId,
                    name = item.Name,
                    quantity = item.Quantity,
                }),
            });
            write = new ActivityEntryWrite(
                this.CreateActivity(
                    ActivityJournalCategory.Credits,
                    isSale
                        ? ActivityJournalKind.VendorSold
                        : ActivityJournalKind.VendorPurchased,
                    summary,
                    credit.Location,
                    credit.OccurredAt,
                    string.Join(Environment.NewLine, details),
                    payloadVersion: 1,
                    payloadJson: payloadJson));
            return true;
        }

        private bool TryAddLootCredits(
            ClientObservationSnapshot snapshot,
            JournalLocationContext location,
            long credits,
            out LootSessionWrite write)
        {
            var source = this.ResolveLootSource(snapshot, location, null);

            if (source == null &&
                this.activeLootSession != null &&
                snapshot.ObservedAt - this.activeLootSession.LastUpdatedAt <=
                    recentLootCreditWindow)
            {
                source = new LootSource(
                    this.activeLootSession.SourceObjectId,
                    this.activeLootSession.SourceName,
                    this.activeLootSession.Location);
            }

            if (source == null)
            {
                write = null!;
                return false;
            }

            var session = this.EnsureLootSession(
                source.ObjectId,
                source.SourceName,
                source.Location,
                snapshot.ObservedAt);
            session.AddCredits(credits, snapshot.ObservedAt);
            write = this.BuildLootWrite(session);
            return true;
        }

        private LootSource? ResolveLootSource(
            ClientObservationSnapshot snapshot,
            JournalLocationContext location,
            CorpseSnapshot? currentCorpse)
        {
            if (currentCorpse != null &&
                (snapshot.Looting.IsLootSessionActive ||
                 snapshot.LootTractor.IsTractoring ||
                 snapshot.LootTractor.WasRecentlyCompleted))
            {
                return new LootSource(
                    currentCorpse.ObjectId,
                    currentCorpse.SourceName,
                    currentCorpse.Location);
            }

            var objectId = ResolveLootSourceObjectId(snapshot);

            if (objectId == 0)
            {
                return null;
            }

            if (this.lastCorpse != null &&
                this.lastCorpse.ObjectId == objectId)
            {
                return new LootSource(
                    objectId,
                    this.lastCorpse.SourceName,
                    this.lastCorpse.Location);
            }

            if (this.activeLootSession != null &&
                this.activeLootSession.SourceObjectId == objectId)
            {
                return new LootSource(
                    objectId,
                    this.activeLootSession.SourceName,
                    this.activeLootSession.Location);
            }

            return null;
        }

        private bool IsActiveLootSourceRelevant(
            ClientObservationSnapshot snapshot,
            uint sourceObjectId)
        {
            return sourceObjectId != 0 &&
                (ResolveLootSourceObjectId(snapshot) == sourceObjectId ||
                 this.activeLootSession != null &&
                 this.activeLootSession.SourceObjectId == sourceObjectId &&
                 snapshot.ObservedAt -
                     this.activeLootSession.LastUpdatedAt <= recentLootWindow);
        }

        private bool IsLootSourceRelevant(
            ClientObservationSnapshot snapshot,
            uint sourceObjectId)
        {
            if (sourceObjectId == 0)
            {
                return false;
            }

            if (snapshot.Target.IsAvailable &&
                snapshot.Target.HasTarget &&
                snapshot.Target.Kind == ClientTargetKind.Corpse &&
                snapshot.Target.ObjectId == sourceObjectId)
            {
                return true;
            }

            return ResolveLootSourceObjectId(snapshot) == sourceObjectId;
        }

        private static uint ResolveLootSourceObjectId(
            ClientObservationSnapshot snapshot)
        {
            if (snapshot.Looting.HasAttachedLootTarget &&
                snapshot.Looting.LootTargetObjectId != 0)
            {
                return snapshot.Looting.LootTargetObjectId;
            }

            if (snapshot.LootTractor.IsAvailable &&
                (snapshot.LootTractor.IsTractoring ||
                 snapshot.LootTractor.WasRecentlyCompleted))
            {
                uint[] candidates =
                [
                    snapshot.LootTractor.CameraTargetObjectId,
                    snapshot.LootTractor.HandlerTractorObjectId,
                    snapshot.LootTractor.ObjectIdReadFromClientObject,
                ];
                var objectId = candidates.FirstOrDefault(candidate =>
                    candidate != 0);

                if (objectId != 0)
                {
                    return objectId;
                }
            }

            return 0;
        }

        private LootSessionAccumulator EnsureLootSession(
            uint sourceObjectId,
            string sourceName,
            JournalLocationContext location,
            DateTimeOffset occurredAt)
        {
            if (this.activeLootSession != null &&
                this.activeLootSession.SourceObjectId == sourceObjectId)
            {
                return this.activeLootSession;
            }

            this.activeLootSession = new LootSessionAccumulator(
                Guid.NewGuid().ToString("N"),
                this.CharacterId,
                this.PilotName,
                occurredAt,
                sourceObjectId,
                sourceName,
                location);
            return this.activeLootSession;
        }

        private LootSessionWrite BuildLootWrite(
            LootSessionAccumulator accumulator)
        {
            var session = accumulator.ToSnapshot();
            var itemQuantity = session.Items.Sum(item => item.Quantity);
            var hasItems = itemQuantity > 0;
            var hasCredits = session.Credits > 0;
            string contents;

            if (hasItems && hasCredits)
            {
                contents = string.Concat(
                    itemQuantity.ToString(CultureInfo.CurrentCulture),
                    itemQuantity == 1 ? " item and " : " items and ",
                    session.Credits.ToString(
                        "N0",
                        CultureInfo.CurrentCulture),
                    " credits");
            }
            else if (hasItems)
            {
                contents = string.Concat(
                    itemQuantity.ToString(CultureInfo.CurrentCulture),
                    itemQuantity == 1 ? " item" : " items");
            }
            else
            {
                contents = string.Concat(
                    session.Credits.ToString(
                        "N0",
                        CultureInfo.CurrentCulture),
                    " credits");
            }

            var sourceSuffix = string.IsNullOrWhiteSpace(session.SourceName)
                ? ""
                : string.Concat(" from ", session.SourceName);
            var summary = string.Concat("Looted ", contents, sourceSuffix);
            var category = ActivityJournalCategory.Loot;

            if (hasCredits)
            {
                category |= ActivityJournalCategory.Credits;
            }

            var activity = this.CreateActivity(
                category,
                ActivityJournalKind.Looted,
                summary,
                session.SystemName,
                session.SectorName,
                session.StarbaseName,
                session.NearestNavName,
                session.LastUpdatedAt,
                relatedLootSessionId: session.SessionId);
            return new LootSessionWrite(activity, session);
        }

        private ActivityJournalEntry CreateActivity(
            ActivityJournalCategory category,
            ActivityJournalKind kind,
            string summary,
            JournalLocationContext location,
            DateTimeOffset occurredAt,
            string details = "",
            string relatedMissionEpisodeId = "",
            string relatedCombatEncounterId = "",
            string relatedLootSessionId = "",
            int payloadVersion = 1,
            string payloadJson = "{}")
        {
            return this.CreateActivity(
                category,
                kind,
                summary,
                location.SystemName,
                location.SectorName,
                location.StarbaseName,
                location.NearestNavName,
                occurredAt,
                details,
                relatedMissionEpisodeId,
                relatedCombatEncounterId,
                relatedLootSessionId,
                payloadVersion,
                payloadJson);
        }

        private ActivityJournalEntry CreateActivity(
            ActivityJournalCategory category,
            ActivityJournalKind kind,
            string summary,
            string systemName,
            string sectorName,
            string starbaseName,
            string nearestNavName,
            DateTimeOffset occurredAt,
            string details = "",
            string relatedMissionEpisodeId = "",
            string relatedCombatEncounterId = "",
            string relatedLootSessionId = "",
            int payloadVersion = 1,
            string payloadJson = "{}")
        {
            return new ActivityJournalEntry
            {
                CharacterId = this.CharacterId,
                PilotName = this.PilotName,
                OccurredAt = occurredAt,
                Category = category,
                Kind = kind,
                Summary = summary,
                Details = details,
                SystemName = systemName,
                SectorName = sectorName,
                StarbaseName = starbaseName,
                NearestNavName = nearestNavName,
                RelatedMissionEpisodeId = relatedMissionEpisodeId,
                RelatedCombatEncounterId = relatedCombatEncounterId,
                RelatedLootSessionId = relatedLootSessionId,
                PayloadVersion = payloadVersion,
                PayloadJson = payloadJson,
            };
        }

        private void ClearLocationCandidate()
        {
            this.candidateLocation = null;
            this.candidateLocationFirstSeenAt = default;
            this.candidateLocationSamples = 0;
        }

        private void ClearCreditsCandidate()
        {
            this.candidateCredits = null;
            this.candidateCreditsFirstSeenAt = default;
            this.candidateCreditsSamples = 0;
        }
    }

    private sealed class ReputationValueState(float initialValue)
    {
        private float stableValue = initialValue;
        private float? candidateValue;
        private DateTimeOffset candidateFirstSeenAt;
        private int candidateSamples;

        public bool Observe(
            float value,
            DateTimeOffset observedAt,
            out float previous,
            out float current)
        {
            previous = 0;
            current = 0;

            if (NearlyEqual(value, this.stableValue))
            {
                this.ClearCandidate();
                return false;
            }

            if (!this.candidateValue.HasValue ||
                !NearlyEqual(value, this.candidateValue.Value))
            {
                this.candidateValue = value;
                this.candidateFirstSeenAt = observedAt;
                this.candidateSamples = 1;
                return false;
            }

            this.candidateSamples++;

            if (this.candidateSamples < 2 ||
                observedAt - this.candidateFirstSeenAt < valueSettleDuration)
            {
                return false;
            }

            previous = this.stableValue;
            current = value;
            this.stableValue = value;
            this.ClearCandidate();
            return true;
        }

        private void ClearCandidate()
        {
            this.candidateValue = null;
            this.candidateFirstSeenAt = default;
            this.candidateSamples = 0;
        }
    }

    private sealed record ReputationSource(
        string FactionKey,
        string DisplayName,
        float Reaction);

    private sealed record CargoItem(
        int ItemTemplateId,
        string Name,
        int Quantity,
        float? QualityPercent);

    private sealed record CargoSnapshot(
        IReadOnlyDictionary<int, CargoItem> ItemsByTemplateId);

    private sealed record CargoChange(
        CargoItem Item,
        int Delta);

    private sealed record CorpseItem(
        int Slot,
        int ItemTemplateId,
        string Name,
        int Quantity,
        float? QualityPercent);

    private sealed record CorpseSnapshot(
        uint ObjectId,
        string SourceName,
        JournalLocationContext Location,
        IReadOnlyDictionary<int, CorpseItem> ItemsBySlot);

    private sealed class PendingLootItem(
        CorpseItem item,
        uint sourceObjectId,
        string sourceName,
        JournalLocationContext location,
        DateTimeOffset removedAt)
    {
        public CorpseItem Item { get; } = item;

        public uint SourceObjectId { get; } = sourceObjectId;

        public string SourceName { get; } = sourceName;

        public JournalLocationContext Location { get; } = location;

        public DateTimeOffset RemovedAt { get; } = removedAt;

        public int RemainingQuantity { get; set; } = item.Quantity;
    }

    private sealed class PendingLootCargoGain(
        CargoItem item,
        int quantity,
        uint sourceObjectId,
        DateTimeOffset observedAt)
    {
        public CargoItem Item { get; } = item;

        public uint SourceObjectId { get; } = sourceObjectId;

        public DateTimeOffset ObservedAt { get; } = observedAt;

        public int RemainingQuantity { get; set; } = quantity;
    }

    private sealed record VendorContext(
        string Key,
        string DisplayName,
        DateTimeOffset ObservedAt);

    private sealed record PendingCraftingDebit(
        ulong PreviousCredits,
        ulong CurrentCredits,
        long Amount,
        JournalLocationContext Location,
        DateTimeOffset OccurredAt,
        DateTimeOffset ExpiresAt);

    private sealed record PendingCraftingCredit(
        long Amount,
        DateTimeOffset OccurredAt,
        DateTimeOffset ExpiresAt);

    private sealed record PendingVendorItemChange(
        VendorContext Context,
        CargoChange Change,
        DateTimeOffset ObservedAt);

    private sealed record PendingVendorCredit(
        ulong PreviousCredits,
        ulong CurrentCredits,
        long Delta,
        JournalLocationContext Location,
        VendorContext Context,
        DateTimeOffset OccurredAt,
        DateTimeOffset ExpiresAt);

    private sealed record VendorTransactionItem(
        int ItemTemplateId,
        string Name,
        int Quantity);

    private sealed record LootSource(
        uint ObjectId,
        string SourceName,
        JournalLocationContext Location);

    private sealed class LootSessionAccumulator
    {
        private readonly Dictionary<string, LootJournalItem> items =
            new(StringComparer.Ordinal);

        public LootSessionAccumulator(
            string sessionId,
            uint characterId,
            string pilotName,
            DateTimeOffset startedAt,
            uint sourceObjectId,
            string sourceName,
            JournalLocationContext location)
        {
            this.SessionId = sessionId;
            this.CharacterId = characterId;
            this.PilotName = pilotName;
            this.StartedAt = startedAt;
            this.LastUpdatedAt = startedAt;
            this.SourceObjectId = sourceObjectId;
            this.SourceName = sourceName;
            this.Location = location;
        }

        public string SessionId { get; }

        public uint CharacterId { get; }

        public string PilotName { get; }

        public DateTimeOffset StartedAt { get; }

        public DateTimeOffset LastUpdatedAt { get; private set; }

        public uint SourceObjectId { get; }

        public string SourceName { get; }

        public JournalLocationContext Location { get; }

        public long Credits { get; private set; }

        public void AddCredits(long credits, DateTimeOffset occurredAt)
        {
            this.Credits = checked(this.Credits + Math.Max(0, credits));
            this.LastUpdatedAt = occurredAt;
        }

        public void AddItem(
            int itemTemplateId,
            string name,
            int quantity,
            float? qualityPercent,
            DateTimeOffset occurredAt)
        {
            var key = string.Create(
                CultureInfo.InvariantCulture,
                $"{itemTemplateId}|{name}|{qualityPercent:0.###}");

            if (this.items.TryGetValue(key, out var existing))
            {
                this.items[key] = existing with
                {
                    Quantity = checked(existing.Quantity + quantity),
                };
            }
            else
            {
                this.items[key] = new LootJournalItem
                {
                    ItemTemplateId = itemTemplateId,
                    Name = name,
                    Quantity = quantity,
                    QualityPercent = qualityPercent,
                };
            }

            this.LastUpdatedAt = occurredAt;
        }

        public LootJournalSession ToSnapshot()
        {
            var ordered = this.items.Values
                .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select((item, index) => item with { Ordinal = index })
                .ToArray();
            return new LootJournalSession
            {
                SessionId = this.SessionId,
                CharacterId = this.CharacterId,
                PilotName = this.PilotName,
                StartedAt = this.StartedAt,
                LastUpdatedAt = this.LastUpdatedAt,
                SourceObjectId = this.SourceObjectId,
                SourceName = this.SourceName,
                Credits = this.Credits,
                SystemName = this.Location.SystemName,
                SectorName = this.Location.SectorName,
                StarbaseName = this.Location.StarbaseName,
                NearestNavName = this.Location.NearestNavName,
                Items = ordered,
            };
        }
    }

    private static bool TryGetCredits(
        ClientObservationSnapshot snapshot,
        out ulong credits)
    {
        var details = snapshot.LocalPlayer.CharacterDetails;

        if (snapshot.LocalPlayer.IsAvailable &&
            details.IsAvailable &&
            details.MoneyValidState != 0 &&
            details.Credits.HasValue)
        {
            credits = details.Credits.Value;
            return true;
        }

        credits = 0;
        return false;
    }

    private static IReadOnlyList<ReputationSource> GetUsableFactions(
        ClientObservationSnapshot snapshot)
    {
        if (!snapshot.LocalPlayer.IsAvailable ||
            !snapshot.LocalPlayer.Reputation.IsAvailable)
        {
            return [];
        }

        return snapshot.LocalPlayer.Reputation.Factions
            .Where(faction =>
                faction.ValidState != 0 &&
                faction.Reaction.HasValue &&
                !string.IsNullOrWhiteSpace(faction.FactionKey))
            .Select(faction => new ReputationSource(
                faction.FactionKey,
                string.IsNullOrWhiteSpace(faction.DisplayName)
                    ? faction.FactionKey
                    : faction.DisplayName.Trim(),
                faction.Reaction.Value))
            .ToArray();
    }

    private static CargoSnapshot? CaptureCargo(
        ClientObservationSnapshot snapshot)
    {
        var inventory = snapshot.LocalPlayer.Inventory;

        if (!snapshot.LocalPlayer.IsAvailable ||
            !inventory.IsAvailable ||
            inventory.CargoCapacity is not { } cargoCapacity ||
            cargoCapacity < 0)
        {
            return null;
        }

        var usableSlots = inventory.CargoSlots
            .Where(slot => slot.Slot >= 0 && slot.Slot < cargoCapacity)
            .ToArray();

        if (usableSlots.Length != cargoCapacity ||
            usableSlots.Any(slot => !slot.IsPresent || !slot.IsValid))
        {
            return null;
        }

        var items = usableSlots
            .Where(item => item.IsOccupied && item.ItemTemplateId is > 0)
            .GroupBy(item => item.ItemTemplateId!.Value)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var observations = group.ToArray();
                    var name = observations
                        .Select(GetCargoItemName)
                        .FirstOrDefault(value =>
                            !string.IsNullOrWhiteSpace(value)) ??
                        "Unknown item";
                    var qualityValues = observations
                        .Select(item => item.QualityPercent)
                        .Where(value => value.HasValue)
                        .Select(value => value!.Value)
                        .ToArray();
                    float? quality = qualityValues.Length > 0 &&
                        qualityValues.All(value =>
                            Math.Abs(value - qualityValues[0]) < 0.01f)
                            ? qualityValues[0]
                            : null;
                    return new CargoItem(
                        group.Key,
                        name,
                        observations.Sum(item =>
                            Math.Max(1, item.StackCount ?? 1)),
                        quality);
                });
        return new CargoSnapshot(items);
    }

    private static string GetCargoItemName(
        ClientInventoryItemObservation item)
    {
        if (item.Template is { IsAvailable: true } template &&
            !string.IsNullOrWhiteSpace(template.Name))
        {
            return template.Name.Trim();
        }

        if (item.ItemTemplateId is > 0)
        {
            var knownName = ClientItemTemplateNameResolver.GetKnownName(
                item.ItemTemplateId.Value);

            if (!string.IsNullOrWhiteSpace(knownName))
            {
                return knownName.Trim();
            }
        }

        return "";
    }

    private static IReadOnlyList<CargoChange> CalculateCargoChanges(
        CargoSnapshot previous,
        CargoSnapshot current)
    {
        List<CargoChange> changes = [];
        HashSet<int> templateIds =
        [
            .. previous.ItemsByTemplateId.Keys,
            .. current.ItemsByTemplateId.Keys,
        ];

        foreach (var itemTemplateId in templateIds)
        {
            previous.ItemsByTemplateId.TryGetValue(
                itemTemplateId,
                out var previousItem);
            current.ItemsByTemplateId.TryGetValue(
                itemTemplateId,
                out var currentItem);
            var previousQuantity = previousItem?.Quantity ?? 0;
            var currentQuantity = currentItem?.Quantity ?? 0;
            var delta = currentQuantity - previousQuantity;

            if (delta == 0)
            {
                continue;
            }

            changes.Add(new CargoChange(
                currentItem ?? previousItem!,
                delta));
        }

        return changes;
    }

    private static CorpseSnapshot? CaptureCorpse(
        ClientObservationSnapshot snapshot)
    {
        if (!snapshot.Target.IsAvailable ||
            !snapshot.Target.HasTarget ||
            snapshot.Target.Kind != ClientTargetKind.Corpse ||
            snapshot.Target.ObjectId == 0 ||
            !snapshot.Target.Corpse.IsHydrated)
        {
            return null;
        }

        var location = JournalLocationResolver.Capture(snapshot);
        var sourceName = NormalizeLootSourceName(snapshot.Target.Name);
        var items = snapshot.Target.Corpse.LootItems
            .Where(item => item.ItemTemplateId is > 0)
            .ToDictionary(
                item => item.Slot,
                item =>
                {
                    var itemTemplateId = item.ItemTemplateId!.Value;
                    var name = ClientItemTemplateNameResolver.GetKnownName(
                        itemTemplateId);
                    return new CorpseItem(
                        item.Slot,
                        itemTemplateId,
                        string.IsNullOrWhiteSpace(name)
                            ? "Unknown item"
                            : name,
                        Math.Max(1, item.StackCount ?? 1),
                        item.QualityPercent);
                });
        return new CorpseSnapshot(
            snapshot.Target.ObjectId,
            sourceName,
            location,
            items);
    }

    private static VendorContext? CaptureVendorContext(
        ClientObservationSnapshot snapshot,
        JournalLocationContext location)
    {
        var interaction = snapshot.StarbaseContext.Interaction;

        if (snapshot.World.Environment != ClientWorldEnvironment.Starbase ||
            !snapshot.StarbaseContext.IsAvailable ||
            interaction.Kind is not (
                ClientStarbaseInteractionKind.TalkTree or
                ClientStarbaseInteractionKind.VendorTrade) ||
            interaction.RoomClass < 0 ||
            interaction.NpcSlot < 0)
        {
            return null;
        }

        var room = snapshot.StarbaseContext.Rooms.FirstOrDefault(candidate =>
            candidate.RoomClass == interaction.RoomClass);
        var npc = room?.Npcs.FirstOrDefault(candidate =>
            candidate.Slot == interaction.NpcSlot);

        if (npc == null ||
            !npc.IsVendor ||
            string.IsNullOrWhiteSpace(npc.Name))
        {
            return null;
        }

        var vendorName = npc.Name.Trim();
        var key = string.Create(
            CultureInfo.InvariantCulture,
            $"{location.StarbaseName}|{interaction.RoomClass}|{interaction.NpcSlot}|{vendorName}");
        return new VendorContext(
            key,
            vendorName,
            snapshot.ObservedAt);
    }

    private static IReadOnlyList<ActivityJournalWrite> CoalesceLootWrites(
        IReadOnlyList<ActivityJournalWrite> writes)
    {
        if (writes.Count <= 1)
        {
            return writes;
        }

        Dictionary<string, LootSessionWrite> sessions =
            new(StringComparer.Ordinal);
        List<ActivityJournalWrite> others = [];

        foreach (var write in writes)
        {
            if (write is LootSessionWrite lootWrite)
            {
                sessions[lootWrite.Session.SessionId] = lootWrite;
            }
            else
            {
                others.Add(write);
            }
        }

        others.AddRange(sessions.Values);
        return others;
    }


    private static RecentMissionContext? TryCorrelateMissionCreditReward(
        RecentMissionContext? context,
        DateTimeOffset observedAt,
        long creditDelta)
    {
        if (creditDelta <= 0 ||
            !IsMissionContextCurrent(context, observedAt) ||
            context == null ||
            !TryReadCreditReward(context.RewardText, out var expectedCredits) ||
            expectedCredits != creditDelta)
        {
            return null;
        }

        return context;
    }

    private static bool TryReadCreditReward(
        string? rewardText,
        out long credits)
    {
        credits = 0;

        if (string.IsNullOrWhiteSpace(rewardText))
        {
            return false;
        }

        var match = Regex.Match(
            rewardText,
            @"(?<![A-Za-z0-9])(?<amount>[0-9][0-9,.' ]*)\s*(?:cr\.?|credits?)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        if (!match.Success)
        {
            return false;
        }

        var digits = new string(
            match.Groups["amount"].Value
                .Where(char.IsAsciiDigit)
                .ToArray());
        return long.TryParse(
            digits,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out credits) &&
            credits > 0;
    }

    private static bool IsMissionContextCurrent(
        RecentMissionContext? context,
        DateTimeOffset observedAt)
    {
        return context != null &&
            observedAt >= context.OccurredAt &&
            observedAt - context.OccurredAt <= missionCorrelationWindow;
    }

    private static bool IsCombatContextNear(
        RecentCombatContext? context,
        DateTimeOffset reputationOccurredAt) =>
        context != null &&
        reputationOccurredAt >=
            context.LastEventAt - combatReputationLeadWindow &&
        reputationOccurredAt <=
            context.OccurredAt + combatReputationTrailWindow;

    private static bool IsMissionContextNear(
        RecentMissionContext? context,
        DateTimeOffset reputationOccurredAt) =>
        context != null &&
        Math.Abs(
            (context.OccurredAt - reputationOccurredAt)
            .TotalMilliseconds) <=
        missionCorrelationWindow.TotalMilliseconds;

    private static long CalculateCreditDelta(ulong previous, ulong current)
    {
        if (current >= previous)
        {
            return checked((long)Math.Min(current - previous, (ulong)long.MaxValue));
        }

        return -checked((long)Math.Min(previous - current, (ulong)long.MaxValue));
    }

    private static string FormatSigned(float value)
    {
        return value >= 0
            ? string.Concat(
                "+",
                value.ToString("0.##", CultureInfo.CurrentCulture))
            : value.ToString("0.##", CultureInfo.CurrentCulture);
    }

    private static string FormatReputation(float value) =>
        value.ToString("0.##", CultureInfo.CurrentCulture);

    private static bool NearlyEqual(float left, float right) =>
        Math.Abs(left - right) < 0.01f;

    private static string NormalizeLootSourceName(string? value)
    {
        var name = Normalize(value);
        const string prefix = "Corpse of ";
        return name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? name[prefix.Length..].Trim()
            : name;
    }
}
