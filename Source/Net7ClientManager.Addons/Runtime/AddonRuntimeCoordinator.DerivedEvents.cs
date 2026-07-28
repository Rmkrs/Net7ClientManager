namespace Net7ClientManager.Addons.Runtime;

using System.Collections;
using System.Globalization;
using Net7ClientManager.Addons.Contracts;

public sealed partial class AddonRuntimeCoordinator
{
    private static void AddDerivedSnapshotEvents(
        List<AddonGameEvent> events,
        AddonGameSnapshot previous,
        AddonGameSnapshot current)
    {
        AddCharacterEconomyEvents(events, previous, current);
        AddProgressionEvents(events, previous, current);
        AddReputationEvents(events, previous, current);
        AddTargetLifecycleEvents(events, previous, current);
        AddGroupMembershipEvents(events, previous, current);
        AddInventoryEvents(events, previous, current);
        AddBuffEvents(events, previous, current);
        AddPanelEvents(events, previous, current);
        AddShipStateEvents(events, previous, current);
        AddNavigationStateEvents(events, previous, current);
        AddProductionEvents(events, previous, current);
        AddAudioEvents(events, previous, current);
        AddStarbaseInteractionEvents(events, previous, current);
    }

    private static void AddCharacterEconomyEvents(
        List<AddonGameEvent> events,
        AddonGameSnapshot previous,
        AddonGameSnapshot current)
    {
        var previousDetails = GetNestedTable(
            GetPublicDomain(previous, "character"),
            "details");
        var currentDetails = GetNestedTable(
            GetPublicDomain(current, "character"),
            "details");

        if (!BothAvailable(previousDetails, currentDetails))
        {
            return;
        }

        AddSignedNumberChange(
            events,
            previous,
            current,
            "character.credits_gained",
            "character.credits_lost",
            "credits",
            GetNumber(previousDetails, "credits"),
            GetNumber(currentDetails, "credits"));

        AddSignedNumberChange(
            events,
            previous,
            current,
            "character.experience_debt_increased",
            "character.experience_debt_reduced",
            "experience_debt",
            GetNumber(previousDetails, "experience_debt"),
            GetNumber(currentDetails, "experience_debt"));
    }

    private static void AddProgressionEvents(
        List<AddonGameEvent> events,
        AddonGameSnapshot previous,
        AddonGameSnapshot current)
    {
        var previousProgression = GetNestedTable(
            GetPublicDomain(previous, "character"),
            "progression");
        var currentProgression = GetNestedTable(
            GetPublicDomain(current, "character"),
            "progression");

        if (!BothAvailable(previousProgression, currentProgression))
        {
            return;
        }

        foreach (var trackName in new[] { "combat", "explore", "trade" })
        {
            var previousTrack = GetNestedTable(previousProgression, trackName);
            var currentTrack = GetNestedTable(currentProgression, trackName);
            var previousLevel = GetInteger(previousTrack, "level");
            var currentLevel = GetInteger(currentTrack, "level");

            if (previousLevel.HasValue &&
                currentLevel.HasValue &&
                currentLevel.Value > previousLevel.Value)
            {
                events.Add(CreateSnapshotEvent(
                    "character.level_up",
                    current,
                    new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["track"] = trackName,
                        ["previous_level"] = previousLevel.Value,
                        ["current_level"] = currentLevel.Value,
                        ["levels_gained"] =
                            currentLevel.Value - previousLevel.Value,
                        ["overall_level"] =
                            GetInteger(currentProgression, "overall_level"),
                    }));
            }
        }

        var previousOverall = GetInteger(
            previousProgression,
            "overall_level");
        var currentOverall = GetInteger(
            currentProgression,
            "overall_level");

        if (previousOverall.HasValue &&
            currentOverall.HasValue &&
            currentOverall.Value > previousOverall.Value)
        {
            events.Add(CreateSnapshotEvent(
                "character.overall_level_up",
                current,
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["previous_level"] = previousOverall.Value,
                    ["current_level"] = currentOverall.Value,
                    ["levels_gained"] =
                        currentOverall.Value - previousOverall.Value,
                }));
        }

        var previousSkillPoints = GetInteger(
            previousProgression,
            "skill_points");
        var currentSkillPoints = GetInteger(
            currentProgression,
            "skill_points");

        if (previousSkillPoints.HasValue &&
            currentSkillPoints.HasValue &&
            previousSkillPoints.Value != currentSkillPoints.Value)
        {
            events.Add(CreateSnapshotEvent(
                "character.skill_points_changed",
                current,
                NumberChangeData(
                    "skill_points",
                    previousSkillPoints.Value,
                    currentSkillPoints.Value)));
        }

        var previousHullTier = GetInteger(
            previousProgression,
            "hull_tier");
        var currentHullTier = GetInteger(
            currentProgression,
            "hull_tier");

        if (previousHullTier.HasValue &&
            currentHullTier.HasValue &&
            currentHullTier.Value > previousHullTier.Value)
        {
            events.Add(CreateSnapshotEvent(
                "character.hull_upgraded",
                current,
                NumberChangeData(
                    "hull_tier",
                    previousHullTier.Value,
                    currentHullTier.Value)));
        }
    }

    private static void AddReputationEvents(
        List<AddonGameEvent> events,
        AddonGameSnapshot previous,
        AddonGameSnapshot current)
    {
        var previousDomain = GetPublicDomain(previous, "reputations");
        var currentDomain = GetPublicDomain(current, "reputations");

        if (!BothAvailable(previousDomain, currentDomain))
        {
            return;
        }

        var previousFactions = GetTables(previousDomain, "factions")
            .Where(faction => GetString(faction, "name") != null)
            .GroupBy(
                faction => GetString(faction, "name")!,
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase);

        foreach (var currentFaction in GetTables(currentDomain, "factions"))
        {
            var name = GetString(currentFaction, "name");
            if (name == null ||
                !previousFactions.TryGetValue(name, out var previousFaction))
            {
                continue;
            }

            var previousValue = GetNumber(previousFaction, "reaction");
            var currentValue = GetNumber(currentFaction, "reaction");

            if (!previousValue.HasValue ||
                !currentValue.HasValue ||
                Math.Abs(currentValue.Value - previousValue.Value) < 0.000001)
            {
                continue;
            }

            var increased = currentValue.Value > previousValue.Value;
            events.Add(CreateSnapshotEvent(
                increased
                    ? "reputation.increased"
                    : "reputation.decreased",
                current,
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["name"] = name,
                    ["previous"] = previousValue.Value,
                    ["current"] = currentValue.Value,
                    ["change"] = currentValue.Value - previousValue.Value,
                    ["disposition"] =
                        GetString(currentFaction, "disposition"),
                }));
        }
    }

    private static void AddTargetLifecycleEvents(
        List<AddonGameEvent> events,
        AddonGameSnapshot previous,
        AddonGameSnapshot current)
    {
        var previousTarget = GetPublicDomain(previous, "target");
        var currentTarget = GetPublicDomain(current, "target");
        var previousHasTarget = GetBoolean(previousTarget, "has_target");
        var currentHasTarget = GetBoolean(currentTarget, "has_target");

        if (!previousHasTarget && currentHasTarget)
        {
            events.Add(CreateSnapshotEvent(
                "target.acquired",
                current,
                TargetData(currentTarget)));
        }
        else if (previousHasTarget && !currentHasTarget)
        {
            events.Add(CreateSnapshotEvent(
                "target.cleared",
                current,
                TargetData(previousTarget)));
        }
        else if (previousHasTarget &&
                 currentHasTarget &&
                 previous.InternalTargetObjectId !=
                 current.InternalTargetObjectId)
        {
            events.Add(CreateSnapshotEvent(
                "target.switched",
                current,
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["previous"] = TargetData(previousTarget),
                    ["current"] = TargetData(currentTarget),
                }));
        }
    }

    private static void AddGroupMembershipEvents(
        List<AddonGameEvent> events,
        AddonGameSnapshot previous,
        AddonGameSnapshot current)
    {
        var previousGroup = GetPublicDomain(previous, "group");
        var currentGroup = GetPublicDomain(current, "group");

        if (!BothAvailable(previousGroup, currentGroup))
        {
            return;
        }

        var previousMembers = GetTables(previousGroup, "members")
            .GroupBy(MemberKey, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.Ordinal);
        var currentMembers = GetTables(currentGroup, "members")
            .GroupBy(MemberKey, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.Ordinal);

        foreach (var pair in currentMembers.Where(pair =>
                     !previousMembers.ContainsKey(pair.Key)))
        {
            events.Add(CreateSnapshotEvent(
                "group.member_joined",
                current,
                CopyData(pair.Value)));
        }

        foreach (var pair in previousMembers.Where(pair =>
                     !currentMembers.ContainsKey(pair.Key)))
        {
            events.Add(CreateSnapshotEvent(
                "group.member_left",
                current,
                CopyData(pair.Value)));
        }
    }

    private static void AddInventoryEvents(
        List<AddonGameEvent> events,
        AddonGameSnapshot previous,
        AddonGameSnapshot current)
    {
        var previousInventory = GetPublicDomain(previous, "inventory");
        var currentInventory = GetPublicDomain(current, "inventory");

        if (!BothAvailable(previousInventory, currentInventory))
        {
            return;
        }

        var previousItems = AggregateOwnedItems(previousInventory);
        var currentItems = AggregateOwnedItems(currentInventory);
        var keys = previousItems.Keys
            .Concat(currentItems.Keys)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(key => key, StringComparer.Ordinal);

        foreach (var key in keys)
        {
            previousItems.TryGetValue(key, out var previousItem);
            currentItems.TryGetValue(key, out var currentItem);
            var previousQuantity = previousItem?.Quantity ?? 0;
            var currentQuantity = currentItem?.Quantity ?? 0;

            if (previousQuantity == currentQuantity)
            {
                continue;
            }

            var item = currentItem ?? previousItem!;
            var gained = currentQuantity > previousQuantity;
            events.Add(CreateSnapshotEvent(
                gained
                    ? "inventory.item_gained"
                    : "inventory.item_lost",
                current,
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["name"] = item.Name,
                    ["quantity"] = Math.Abs(currentQuantity - previousQuantity),
                    ["previous_quantity"] = previousQuantity,
                    ["current_quantity"] = currentQuantity,
                }));
        }
    }

    private static void AddBuffEvents(
        List<AddonGameEvent> events,
        AddonGameSnapshot previous,
        AddonGameSnapshot current)
    {
        var previousBuffs = GetPublicDomain(previous, "buffs");
        var currentBuffs = GetPublicDomain(current, "buffs");

        if (!BothAvailable(previousBuffs, currentBuffs))
        {
            return;
        }

        var previousItems = GetTables(previousBuffs, "items")
            .GroupBy(BuffKey, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.Ordinal);
        var currentItems = GetTables(currentBuffs, "items")
            .GroupBy(BuffKey, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.Ordinal);

        foreach (var pair in currentItems.Where(pair =>
                     !previousItems.ContainsKey(pair.Key)))
        {
            events.Add(CreateSnapshotEvent(
                "buff.applied",
                current,
                CopyData(pair.Value)));
        }

        foreach (var pair in previousItems.Where(pair =>
                     !currentItems.ContainsKey(pair.Key)))
        {
            events.Add(CreateSnapshotEvent(
                "buff.removed",
                current,
                CopyData(pair.Value)));
        }
    }

    private static void AddPanelEvents(
        List<AddonGameEvent> events,
        AddonGameSnapshot previous,
        AddonGameSnapshot current)
    {
        var previousPanels = GetPublicDomain(previous, "panels");
        var currentPanels = GetPublicDomain(current, "panels");

        AddPanelTransition(
            events,
            current,
            "inventory",
            GetNestedTable(previousPanels, "inventory"),
            GetNestedTable(currentPanels, "inventory"));
        AddPanelTransition(
            events,
            current,
            "character",
            GetNestedTable(previousPanels, "character"),
            GetNestedTable(currentPanels, "character"));
        AddPanelTransition(
            events,
            current,
            "mission_details",
            GetNestedTable(
                GetNestedTable(previousPanels, "character"),
                "mission_details"),
            GetNestedTable(
                GetNestedTable(currentPanels, "character"),
                "mission_details"));
        AddPanelTransition(
            events,
            current,
            "faction_details",
            GetNestedTable(
                GetNestedTable(previousPanels, "character"),
                "faction_details"),
            GetNestedTable(
                GetNestedTable(currentPanels, "character"),
                "faction_details"));
        AddPanelTransition(
            events,
            current,
            "star_map",
            GetNestedTable(previousPanels, "star_map"),
            GetNestedTable(currentPanels, "star_map"));

        var previousLoot = GetPublicDomain(previous, "loot");
        var currentLoot = GetPublicDomain(current, "loot");
        AddPanelTransition(
            events,
            current,
            "loot",
            previousLoot,
            currentLoot,
            "panel_displayed");

        var previousJobs = GetPublicDomain(previous, "jobs");
        var currentJobs = GetPublicDomain(current, "jobs");
        AddPanelTransition(
            events,
            current,
            "job_terminal",
            previousJobs,
            currentJobs,
            "open");
    }

    private static void AddShipStateEvents(
        List<AddonGameEvent> events,
        AddonGameSnapshot previous,
        AddonGameSnapshot current)
    {
        var previousShip = GetPublicDomain(previous, "ship");
        var currentShip = GetPublicDomain(current, "ship");

        if (!BothAvailable(previousShip, currentShip))
        {
            return;
        }

        var previousFlags = GetNestedTable(previousShip, "flags");
        var currentFlags = GetNestedTable(currentShip, "flags");

        AddBooleanTransition(
            events,
            current,
            previousFlags,
            currentFlags,
            "incapacitated",
            "ship.incapacitated",
            "ship.recovered");
        AddBooleanTransition(
            events,
            current,
            previousFlags,
            currentFlags,
            "cloaked",
            "ship.cloaked",
            "ship.uncloaked");
    }

    private static void AddNavigationStateEvents(
        List<AddonGameEvent> events,
        AddonGameSnapshot previous,
        AddonGameSnapshot current)
    {
        var previousNavigation = GetPublicDomain(previous, "navigation");
        var currentNavigation = GetPublicDomain(current, "navigation");

        if (previousNavigation == null || currentNavigation == null)
        {
            return;
        }

        var previousControl = GetNestedTable(previousNavigation, "control");
        var currentControl = GetNestedTable(currentNavigation, "control");

        AddBooleanTransition(
            events,
            current,
            previousControl,
            currentControl,
            "warp_active",
            "navigation.warp_started",
            "navigation.warp_ended");

        var previousJourney = GetNestedTable(
            GetNestedTable(previousNavigation, "route"),
            "journey");
        var currentJourney = GetNestedTable(
            GetNestedTable(currentNavigation, "route"),
            "journey");
        var previousState = GetString(previousJourney, "state");
        var currentState = GetString(currentJourney, "state");

        if (!string.Equals(
                previousState,
                currentState,
                StringComparison.Ordinal) &&
            (previousState != null || currentState != null))
        {
            events.Add(CreateSnapshotEvent(
                "navigation.journey_changed",
                current,
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["previous"] = previousState,
                    ["current"] = currentState,
                    ["status_text"] =
                        GetString(currentJourney, "status_text"),
                    ["stop_reason"] =
                        GetString(currentJourney, "stop_reason"),
                    ["pause_reason"] =
                        GetString(currentJourney, "pause_reason"),
                    ["expected_target_name"] =
                        GetString(currentJourney, "expected_target_name"),
                    ["expected_sector_name"] =
                        GetString(currentJourney, "expected_sector_name"),
                }));
        }
    }

    private static void AddProductionEvents(
        List<AddonGameEvent> events,
        AddonGameSnapshot previous,
        AddonGameSnapshot current)
    {
        var previousProduction = GetPublicDomain(previous, "production");
        var currentProduction = GetPublicDomain(current, "production");
        var previousAvailable = GetBoolean(previousProduction, "available");
        var currentAvailable = GetBoolean(currentProduction, "available");
        var previousOutput = GetString(
            previousProduction,
            "output_name");
        var currentOutput = GetString(
            currentProduction,
            "output_name");

        if ((!previousAvailable && currentAvailable) ||
            (currentAvailable && previousOutput != currentOutput))
        {
            events.Add(CreateSnapshotEvent(
                "production.recipe_selected",
                current,
                CopyData(currentProduction)));
        }
        else if (previousAvailable && !currentAvailable)
        {
            events.Add(CreateSnapshotEvent(
                "production.recipe_cleared",
                current,
                CopyData(previousProduction)));
        }
    }

    private static void AddAudioEvents(
        List<AddonGameEvent> events,
        AddonGameSnapshot previous,
        AddonGameSnapshot current)
    {
        var previousAudio = GetPublicDomain(previous, "audio");
        var currentAudio = GetPublicDomain(current, "audio");
        var previousResource = GetString(previousAudio, "cue");
        var currentResource = GetString(currentAudio, "cue");

        if (!string.Equals(
                previousResource,
                currentResource,
                StringComparison.Ordinal) &&
            (previousResource != null || currentResource != null))
        {
            events.Add(CreateSnapshotEvent(
                "audio.cue_changed",
                current,
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["previous"] = previousResource,
                    ["current"] = currentResource,
                }));
        }
    }

    private static void AddStarbaseInteractionEvents(
        List<AddonGameEvent> events,
        AddonGameSnapshot previous,
        AddonGameSnapshot current)
    {
        var previousInteraction = GetNestedTable(
            GetPublicDomain(previous, "starbase"),
            "interaction");
        var currentInteraction = GetNestedTable(
            GetPublicDomain(current, "starbase"),
            "interaction");
        var previousActive = GetBoolean(previousInteraction, "active");
        var currentActive = GetBoolean(currentInteraction, "active");

        if (!previousActive && currentActive)
        {
            events.Add(CreateSnapshotEvent(
                "starbase.interaction_started",
                current,
                CopyData(currentInteraction)));
        }
        else if (previousActive && !currentActive)
        {
            events.Add(CreateSnapshotEvent(
                "starbase.interaction_ended",
                current,
                CopyData(previousInteraction)));
        }
    }

    private static void AddSignedNumberChange(
        List<AddonGameEvent> events,
        AddonGameSnapshot previous,
        AddonGameSnapshot current,
        string increasedEvent,
        string decreasedEvent,
        string valueName,
        double? previousValue,
        double? currentValue)
    {
        if (!previousValue.HasValue ||
            !currentValue.HasValue ||
            Math.Abs(currentValue.Value - previousValue.Value) < 0.000001)
        {
            return;
        }

        events.Add(CreateSnapshotEvent(
            currentValue.Value > previousValue.Value
                ? increasedEvent
                : decreasedEvent,
            current,
            NumberChangeData(
                valueName,
                previousValue.Value,
                currentValue.Value)));
    }

    private static IReadOnlyDictionary<string, object?> NumberChangeData(
        string valueName,
        double previous,
        double current)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["value"] = valueName,
            ["previous"] = previous,
            ["current"] = current,
            ["change"] = current - previous,
            ["amount"] = Math.Abs(current - previous),
        };
    }

    private static void AddPanelTransition(
        List<AddonGameEvent> events,
        AddonGameSnapshot current,
        string panelName,
        IReadOnlyDictionary<string, object?>? previousPanel,
        IReadOnlyDictionary<string, object?>? currentPanel,
        string displayedKey = "displayed")
    {
        var previousDisplayed = GetBoolean(previousPanel, displayedKey);
        var currentDisplayed = GetBoolean(currentPanel, displayedKey);

        if (previousDisplayed == currentDisplayed)
        {
            return;
        }

        events.Add(CreateSnapshotEvent(
            currentDisplayed ? "panel.opened" : "panel.closed",
            current,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["panel"] = panelName,
                ["state"] = currentDisplayed ? "open" : "closed",
                ["details"] = currentDisplayed
                    ? CopyData(currentPanel)
                    : CopyData(previousPanel),
            }));
    }

    private static void AddBooleanTransition(
        List<AddonGameEvent> events,
        AddonGameSnapshot current,
        IReadOnlyDictionary<string, object?>? previousValues,
        IReadOnlyDictionary<string, object?>? currentValues,
        string key,
        string enabledEvent,
        string disabledEvent)
    {
        var previousValue = GetNullableBoolean(previousValues, key);
        var currentValue = GetNullableBoolean(currentValues, key);

        if (!previousValue.HasValue ||
            !currentValue.HasValue ||
            previousValue.Value == currentValue.Value)
        {
            return;
        }

        events.Add(CreateSnapshotEvent(
            currentValue.Value ? enabledEvent : disabledEvent,
            current,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["previous"] = previousValue.Value,
                ["current"] = currentValue.Value,
            }));
    }

    private static AddonGameEvent CreateSnapshotEvent(
        string name,
        AddonGameSnapshot snapshot,
        IReadOnlyDictionary<string, object?> data)
    {
        return new AddonGameEvent
        {
            Name = name,
            Snapshot = snapshot,
            OccurredAt = snapshot.ObservedAt,
            Data = data,
        };
    }

    private static IReadOnlyDictionary<string, object?> TargetData(
        IReadOnlyDictionary<string, object?>? target)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["name"] = GetString(target, "name"),
            ["kind"] = GetString(target, "kind"),
            ["relation"] = GetString(target, "relation"),
            ["self"] = GetBoolean(target, "self"),
            ["group_member"] = GetBoolean(target, "group_member"),
        };
    }

    private static string MemberKey(
        IReadOnlyDictionary<string, object?> member)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{GetInteger(member, "slot") ?? -1}|{GetString(member, "name") ?? ""}|{GetInteger(member, "formation_position") ?? -1}");
    }

    private static string BuffKey(
        IReadOnlyDictionary<string, object?> buff)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{GetInteger(buff, "slot") ?? -1}|{GetString(buff, "type") ?? ""}|{GetString(buff, "name") ?? ""}");
    }

    private static Dictionary<string, AggregatedItem> AggregateOwnedItems(
        IReadOnlyDictionary<string, object?> inventory)
    {
        Dictionary<string, AggregatedItem> result =
            new(StringComparer.Ordinal);

        foreach (var collectionName in new[]
                 {
                     "cargo", "equipment", "ammo", "secure", "reward",
                     "overflow",
                 })
        {
            var collection = GetNestedTable(inventory, collectionName);
            foreach (var item in GetTables(collection, "items"))
            {
                var name = GetString(item, "name");

                if (name == null)
                {
                    continue;
                }

                var key = string.Concat(
                    "name:",
                    name.ToLowerInvariant());
                var quantity = Math.Max(
                    1,
                    GetInteger(item, "stack_count") ?? 1);

                if (result.TryGetValue(key, out var existing))
                {
                    result[key] = existing with
                    {
                        Quantity = existing.Quantity + quantity,
                    };
                }
                else
                {
                    result[key] = new AggregatedItem(
                        name,
                        quantity);
                }
            }
        }

        return result;
    }

    private static IReadOnlyDictionary<string, object?>? GetPublicDomain(
        AddonGameSnapshot snapshot,
        string domain)
    {
        return snapshot.PublicData.TryGetValue(domain, out var value) &&
               value is IReadOnlyDictionary<string, object?> table
            ? table
            : null;
    }

    private static IReadOnlyDictionary<string, object?>? GetNestedTable(
        IReadOnlyDictionary<string, object?>? parent,
        string key)
    {
        return parent != null &&
               parent.TryGetValue(key, out var value) &&
               value is IReadOnlyDictionary<string, object?> table
            ? table
            : null;
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>>
        GetTables(
            IReadOnlyDictionary<string, object?>? parent,
            string key)
    {
        if (parent == null ||
            !parent.TryGetValue(key, out var value) ||
            value is not IEnumerable enumerable)
        {
            return [];
        }

        return enumerable
            .Cast<object?>()
            .OfType<IReadOnlyDictionary<string, object?>>()
            .ToArray();
    }

    private static bool BothAvailable(
        IReadOnlyDictionary<string, object?>? previous,
        IReadOnlyDictionary<string, object?>? current)
    {
        return GetBoolean(previous, "available") &&
               GetBoolean(current, "available");
    }

    private static bool? GetNullableBoolean(
        IReadOnlyDictionary<string, object?>? values,
        string key)
    {
        return values != null &&
               values.TryGetValue(key, out var value) &&
               value is bool boolean
            ? boolean
            : null;
    }

    private static long? GetInteger(
        IReadOnlyDictionary<string, object?>? values,
        string key)
    {
        if (values == null || !values.TryGetValue(key, out var value))
        {
            return null;
        }

        return value switch
        {
            byte number => number,
            sbyte number => number,
            short number => number,
            ushort number => number,
            int number => number,
            uint number => number,
            long number => number,
            ulong number when number <= long.MaxValue => checked((long)number),
            _ => null,
        };
    }

    private static double? GetNumber(
        IReadOnlyDictionary<string, object?>? values,
        string key)
    {
        if (values == null || !values.TryGetValue(key, out var value))
        {
            return null;
        }

        return value switch
        {
            byte number => number,
            sbyte number => number,
            short number => number,
            ushort number => number,
            int number => number,
            uint number => number,
            long number => number,
            ulong number => number,
            float number when float.IsFinite(number) => number,
            double number when double.IsFinite(number) => number,
            decimal number => (double)number,
            _ => null,
        };
    }

    private static IReadOnlyDictionary<string, object?> CopyData(
        IReadOnlyDictionary<string, object?>? source)
    {
        return source == null
            ? new Dictionary<string, object?>(StringComparer.Ordinal)
            : new Dictionary<string, object?>(source, StringComparer.Ordinal);
    }

    private sealed record AggregatedItem(
        string? Name,
        long Quantity);
}
