namespace Net7ClientManager.Addons.Development;

public static partial class AddonApiCatalog
{
    private static void AddGameDataSymbols(List<AddonApiSymbol> result)
    {
        AddCharacterSymbols(result);
        AddShipSymbols(result);
        AddTargetSymbols(result);
        AddNearbyTargetSymbols(result);
        AddGroupSymbols(result);
        AddInventorySymbols(result);
        AddBuffSymbols(result);
        AddMissionSymbols(result);
        AddReputationSymbols(result);
        AddNavigationSymbols(result);
        AddStarbaseSymbols(result);
        AddPanelSymbols(result);
        AddJobSymbols(result);
        AddShortcutSymbols(result);
        AddTooltipSymbols(result);
        AddProductionSymbols(result);
        AddAudioSymbols(result);
        AddLootSymbols(result);
        AddStatsSymbols(result);
        AddCombatSymbols(result);
        AddChatSymbols(result);
    }

    private static void AddCharacterSymbols(List<AddonApiSymbol> result)
    {
        AddTable(result, "game.character", "Current pilot state read from the running game. A configured slot alone never fills this table.");
        AddFields(
            result,
            "game.character",
            ("available", "boolean", "Whether a pilot is currently available in the running game."));

        AddTable(result, "game.character.identity", "The current pilot as shown by the game.");
        AddFields(
            result,
            "game.character.identity",
            ("name", "string|nil", "Pilot name."),
            ("title", "string|nil", "Character title."),
            ("rank", "string|nil", "Character rank."),
            ("race", "string|nil", "Race."),
            ("profession", "string|nil", "Profession name."),
            ("affiliation", "string|nil", "Faction affiliation."),
            ("guild_name", "string|nil", "Guild name."),
            ("guild_rank", "string|nil", "Guild rank."),
            ("combat_level", "integer|nil", "Combat level."));

        AddTable(result, "game.character.details", "Current credits, experience debt and registered base.");
        AddFields(
            result,
            "game.character.details",
            ("available", "boolean", "Whether character details are available."),
            ("credits", "number|nil", "Current credits."),
            ("experience_debt", "number|nil", "Current experience debt."),
            ("registration_starbase", "string|nil", "Registered starbase name."),
            ("registration_sector", "string|nil", "Registered sector name."));

        AddTable(result, "game.character.progression", "Combat, explore, trade and hull progression.");
        AddFields(
            result,
            "game.character.progression",
            ("available", "boolean", "Whether progression is available."),
            ("skill_points", "integer|nil", "Unspent skill points."),
            ("hull_upgrade_level", "integer|nil", "Current hull-upgrade level."),
            ("hull_tier", "integer|nil", "Current hull tier."),
            ("current_hull_upgrade_overall_level", "integer|nil", "Overall level of the current hull milestone."),
            ("next_hull_upgrade_overall_level", "integer|nil", "Overall level of the next hull milestone."),
            ("overall_level", "integer|nil", "Combined combat, explore and trade level."),
            ("overall_levels_until_next_hull_upgrade", "integer|nil", "Overall levels remaining until the next hull milestone."),
            ("maximum_hull_tier", "boolean", "True when the pilot is already at the final hull tier."));

        foreach (var track in new[] { "combat", "explore", "trade" })
        {
            AddTable(result, string.Concat("game.character.progression.", track), string.Concat("", track, " experience track."));
            AddExperienceTrackFields(result, string.Concat("game.character.progression.", track));
        }

        AddTable(result, "game.character.skills", "The pilot's learned skills.");
        AddFields(
            result,
            "game.character.skills",
            ("available", "boolean", "Whether skill data is available."),
            ("learned_count", "integer", "Number of learned skill entries."),
            ("spent_skill_points", "integer", "Total points spent across the listed skills."),
            ("items", "table[]", "Learned skills. Each item exposes name, category, active, current_rank, maximum_rank, quest_only_levels, learned, maxed and spent_skill_points."));

        AddSpatialSymbols(result, "game.character.spatial");
    }

    private static void AddExperienceTrackFields(
        List<AddonApiSymbol> result,
        string path)
    {
        AddFields(
            result,
            path,
            ("level", "integer|nil", "Current level."),
            ("progress", "number|nil", "Progress toward the next level from 0.0 to 1.0."),
            ("progress_percent", "number|nil", "Progress toward the next level as a percentage."),
            ("experience_required", "number|nil", "Experience required for the current level step."),
            ("experience_earned", "number|nil", "Experience earned within the current level step."),
            ("experience_remaining", "number|nil", "Experience remaining to the next level."),
            ("maximum_level", "boolean", "True when this track is already at the level cap."),
            ("next_level", "integer|nil", "Next level, or nil at the cap."),
            ("complete", "boolean", "True when the track is complete."));
    }

    private static void AddShipSymbols(List<AddonApiSymbol> result)
    {
        AddTable(result, "game.ship", "Read-only local ship state.");
        AddFields(result, "game.ship", ("available", "boolean", "Whether local ship state is available."));
        AddVitalsSymbols(result, "game.ship.vitals");
        AddShipOperationalSymbols(result, "game.ship");
    }

    private static void AddVitalsSymbols(
        List<AddonApiSymbol> result,
        string path)
    {
        AddTable(result, path, "Shield, hull and energy vitals.");
        AddVitalSymbols(result, string.Concat(path, ".shield"), includeEnergyFlow: false);
        AddVitalSymbols(result, string.Concat(path, ".hull"), includeEnergyFlow: false);
        AddVitalSymbols(result, string.Concat(path, ".energy"), includeEnergyFlow: true);
    }

    private static void AddVitalSymbols(
        List<AddonApiSymbol> result,
        string path,
        bool includeEnergyFlow)
    {
        AddTable(result, path, "Current shield, hull or reactor value.");
        AddFields(
            result,
            path,
            ("available", "boolean", "Whether this vital can currently be read."),
            ("has_data", "boolean", "Whether current and maximum values are known."),
            ("current", "number|nil", "Current value."),
            ("maximum", "number|nil", "Maximum value."),
            ("percent", "number|nil", "Current percentage."));

        if (includeEnergyFlow)
        {
            AddFields(
                result,
                path,
                ("percent_change_per_tick", "number|nil", "Change in reactor percentage between game-state updates."),
                ("draining", "boolean", "True while energy is decreasing."),
                ("recovering", "boolean", "True while energy is increasing."));
        }
    }

    private static void AddShipOperationalSymbols(
        List<AddonApiSymbol> result,
        string path)
    {
        var flagsPath = string.Concat(path, ".flags");
        AddTable(result, flagsPath, "Current ship state flags.");
        AddFields(
            result,
            flagsPath,
            ("lock_speed", "boolean", "Speed lock flag."),
            ("lock_orientation", "boolean", "Orientation lock flag."),
            ("auto_level", "boolean", "Auto-level flag."),
            ("cloaked", "boolean", "Cloak flag."),
            ("countermeasure_active", "boolean", "Countermeasure-active flag."),
            ("incapacitated", "boolean", "Incapacitated flag."),
            ("organic", "boolean", "Organic-ship flag."),
            ("pvp", "boolean", "PvP flag."),
            ("auto_following", "boolean", "Auto-follow flag."),
            ("rescue_beacon_active", "boolean", "Rescue-beacon flag."));

        var runtimePath = string.Concat(path, ".runtime");
        AddTable(result, runtimePath, "Current ship activity and temporary state.");
        AddFields(
            result,
            runtimePath,
            ("warping", "boolean", "True while warping."),
            ("warp_available", "boolean", "Whether warp is currently available."),
            ("engine_thrust", "number|nil", "Current engine thrust."),
            ("target_threat", "boolean", "Whether the target-threat state is active."),
            ("target_threat_sound", "boolean", "Whether target-threat audio is active."),
            ("target_threat_level", "number|nil", "Current target-threat level."),
            ("interruptible_ability", "boolean", "Whether an interruptible ability is active."),
            ("interrupt_progress_percent", "number|nil", "Interruptible ability progress percentage."));

        var movementPath = string.Concat(path, ".movement");
        AddTable(result, movementPath, "Current ship movement limits.");
        AddFields(
            result,
            movementPath,
            ("maximum_tilt_rate", "number|nil", "Maximum tilt rate."),
            ("maximum_turn_rate", "number|nil", "Maximum turn rate."),
            ("maximum_tilt_angle", "number|nil", "Maximum tilt angle."),
            ("maximum_speed", "number|nil", "Maximum normal-space speed."),
            ("minimum_speed", "number|nil", "Minimum normal-space speed."),
            ("acceleration", "number|nil", "Acceleration."));

        AddShipStatsSymbols(result, string.Concat(path, ".base_stats"), "Unmodified ship statistics.");
        AddShipStatsSymbols(result, string.Concat(path, ".current_stats"), "Current ship statistics after active modifiers.");

        AddFields(
            result,
            path,
            ("quadrants", "table[]", "Ship quadrants. Each item exposes index, health, health_percent, damage and damage_percent."));

        var radarPath = string.Concat(path, ".radar");
        AddTable(result, radarPath, "Current radar state.");
        AddFields(
            result,
            radarPath,
            ("available", "boolean", "Whether radar state is available."),
            ("appears", "boolean", "Whether the ship currently appears on radar."),
            ("range", "number|nil", "Current radar range."));
    }

    private static void AddShipStatsSymbols(
        List<AddonApiSymbol> result,
        string path,
        string description)
    {
        AddTable(result, path, description);
        AddFields(
            result,
            path,
            ("defense", "number|nil", "Defense."),
            ("missile_defense", "number|nil", "Missile defense."),
            ("speed", "number|nil", "Normal-space speed."),
            ("warp_speed", "number|nil", "Warp speed."),
            ("warp_power_level", "number|nil", "Warp power level."),
            ("turn_rate", "number|nil", "Turn rate."),
            ("scan_range", "number|nil", "Scan range."),
            ("visibility", "number|nil", "Visibility."),
            ("resist_impact", "number|nil", "Impact resistance."),
            ("resist_explosive", "number|nil", "Explosive resistance."),
            ("resist_plasma", "number|nil", "Plasma resistance."),
            ("resist_energy", "number|nil", "Energy resistance."),
            ("resist_emp", "number|nil", "EMP resistance."),
            ("resist_chemical", "number|nil", "Chemical resistance."),
            ("resist_psionic", "number|nil", "Psionic resistance."));
    }

    private static void AddTargetSymbols(List<AddonApiSymbol> result)
    {
        AddTable(result, "game.target", "Read-only current target state.");
        AddFields(
            result,
            "game.target",
            ("available", "boolean", "Whether target information is currently available."),
            ("has_target", "boolean", "Whether the client currently has a target."),
            ("name", "string|nil", "Target display name."),
            ("kind", "string|nil", "Target kind."),
            ("relation", "string|nil", "Relationship to the pilot."),
            ("self", "boolean", "True when targeting the local character."),
            ("group_member", "boolean", "True when the target is a group member."),
            ("hostile_attacking", "boolean", "True when the hostile target is attacking."));

        AddTable(result, "game.target.identity", "Player-facing target identity when the game provides it.");
        AddFields(
            result,
            "game.target.identity",
            ("available", "boolean", "Whether detailed target information is available."),
            ("name", "string|nil", "Known target name."),
            ("owner_name", "string|nil", "Owner name when the target belongs to another entity."),
            ("title", "string|nil", "Known title."),
            ("rank", "string|nil", "Known rank."),
            ("profession", "string|nil", "Known profession name."),
            ("guild_name", "string|nil", "Known guild name."),
            ("guild_rank", "string|nil", "Known guild rank."),
            ("combat_level", "integer|nil", "Known combat level."));

        AddTable(result, "game.target.interaction", "Actions currently offered for the selected target.");
        AddFields(
            result,
            "game.target.interaction",
            ("available", "boolean", "Whether interaction state matches the current target."),
            ("active", "boolean", "Whether the current target interaction is active."),
            ("actions", "table[]", "Available target verbs. Each item exposes verb, executable and unavailable_reason."));
        AddTable(result, "game.target.interaction.can_execute", "Convenience flags for currently executable target verbs.");
        AddBooleanFields(
            result,
            "game.target.interaction.can_execute",
            "scan", "land", "trade", "tractor", "dock", "gate", "register", "jumpstart", "follow");

        AddTable(result, "game.target.distance", "Current distance to the selected target.");
        AddFields(
            result,
            "game.target.distance",
            ("available", "boolean", "Whether target distance is available."),
            ("surface", "number|nil", "Surface-to-surface distance."),
            ("display_text", "string|nil", "Distance text shown by the game."));

        AddTable(result, "game.target.spatial", "Current pilot and target positions.");
        AddSpatialSymbols(result, "game.target.spatial.local");
        AddSpatialSymbols(result, "game.target.spatial.target");
        AddVitalsSymbols(result, "game.target.vitals");

        AddTable(result, "game.target.ship", "Ship status and equipment readiness for a ship target.");
        AddFields(result, "game.target.ship", ("available", "boolean", "Whether ship status and equipment readiness are available for this target."));
        AddShipOperationalSymbols(result, "game.target.ship");

        AddTable(result, "game.target.corpse", "Known contents of the selected corpse or wreck.");
        AddFields(
            result,
            "game.target.corpse",
            ("available", "boolean", "Whether corpse or wreck details are currently available."),
            ("has_loot", "boolean", "Whether known loot is present."),
            ("known_empty", "boolean", "Whether the corpse is known empty."),
            ("occupied_count", "integer", "Known occupied loot slots."),
            ("items", "table[]", "Known loot items. Each item exposes slot, name, stack_count, quality_percent, structure_percent, average_cost, builder_name, instance_info, activated_effect_info and equip_effect_info."));

        AddTable(result, "game.target.asteroid", "Known resources in the selected asteroid.");
        AddFields(
            result,
            "game.target.asteroid",
            ("available", "boolean", "Whether asteroid details are currently available."),
            ("tech_level", "integer|nil", "Asteroid tech level."),
            ("percent_full", "number|nil", "Remaining resource percentage."),
            ("depleted", "boolean", "Whether the asteroid is depleted."),
            ("resources_may_be_outdated", "boolean", "Whether the asteroid resource list may be out of date."),
            ("resource_count", "integer", "Number of known resources."),
            ("resources", "table[]", "Known resources with the same item fields exposed by corpse items."));
    }

    private static void AddSpatialSymbols(
        List<AddonApiSymbol> result,
        string path)
    {
        AddTable(result, path, "Game-world position and targeting radius in Earth & Beyond units.");
        AddFields(
            result,
            path,
            ("available", "boolean", "Whether spatial state is available."),
            ("position", "table|nil", "Position table with x, y and z coordinates."),
            ("targeting_distance_radius", "number|nil", "Targeting distance radius."));
        AddTable(
            result,
            string.Concat(path, ".position"),
            "Earth & Beyond game-world coordinates. This table is nil while spatial state is unavailable.");
        AddFields(
            result,
            string.Concat(path, ".position"),
            ("x", "number", "X coordinate."),
            ("y", "number", "Y coordinate."),
            ("z", "number", "Z coordinate."));
    }

    private static void AddNearbyTargetSymbols(List<AddonApiSymbol> result)
    {
        AddTable(result, "game.nearby_targets", "Targets currently shown by the game's nearby-target radar.");
        AddFields(
            result,
            "game.nearby_targets",
            ("available", "boolean", "Whether nearby-target radar information is currently available."),
            ("count", "integer", "Number of nearby targets."),
            ("inside_viewport_count", "integer", "Targets with markers inside the radar viewport."),
            ("gutter_count", "integer", "Targets represented on the radar gutter."),
            ("targets", "table[]", "Targets exposing name, display_name, owner_name, title, rank, kind, inside_viewport, on_gutter, hovered, selected, hull, shield and screen_position."));
        AddTable(result, "game.nearby_targets.targets[]", "One target in the player-facing nearby-target list.");
    }

    private static void AddGroupSymbols(List<AddonApiSymbol> result)
    {
        AddTable(result, "game.group", "Current group state.");
        AddFields(
            result,
            "game.group",
            ("available", "boolean", "Whether group information is currently available."),
            ("in_group", "boolean", "Whether the local character is grouped."),
            ("leader", "boolean", "Whether the local character is the group leader."),
            ("looking_for_group", "boolean", "Looking-for-group flag."),
            ("allows_invites", "boolean", "Allow-invites flag."),
            ("shows_non_combat_activities", "boolean", "Show non-combat activities flag."),
            ("auto_split", "boolean", "Auto-split flag."),
            ("restricted_looting", "boolean", "Restricted-looting flag."),
            ("auto_release_loot_restrictions", "boolean", "Auto-release loot restrictions flag."),
            ("formation", "table", "The current formation name and the current pilot's position in it."),
            ("members", "table[]", "Other members exposing slot, name, formation_position, details_available, distance, shield and hull."));

        AddTable(result, "game.group.formation", "The group's current formation and the current pilot's place in it.");
        AddFields(
            result,
            "game.group.formation",
            ("name", "string|nil", "Current formation name."),
            ("position", "integer|nil", "Current pilot's formation position."));
    }

    private static void AddInventorySymbols(List<AddonApiSymbol> result)
    {
        AddTable(result, "game.inventory", "Cargo, equipment, ammo and storage inventories.");
        AddFields(result, "game.inventory", ("available", "boolean", "Whether inventory information is currently available."));

        AddTable(result, "game.inventory.cargo", "Cargo hold.");
        AddFields(
            result,
            "game.inventory.cargo",
            ("capacity", "integer|nil", "Cargo capacity."),
            ("used", "integer|nil", "Used cargo slots."),
            ("free", "integer|nil", "Free cargo slots."),
            ("unavailable", "integer|nil", "Unavailable cargo slots."),
            (
                "slots",
                "table[]",
                "All cargo slots, including empty and unavailable slots. " +
                "Each slot exposes collection, slot, state, is_usable and " +
                "the common item fields when occupied."),
            (
                "items",
                "table[]",
                "Occupied cargo slots only, derived from slots for compatibility."));

        AddTable(result, "game.inventory.equipment", "Equipped items and slot readiness.");
        AddFields(
            result,
            "game.inventory.equipment",
            ("weapon_slot_count", "integer", "Future weapon-slot count."),
            ("device_slot_count", "integer", "Future device-slot count."),
            ("occupied_weapon_count", "integer", "Occupied weapon slots."),
            ("occupied_device_count", "integer", "Occupied device slots."),
            ("usable_weapon_slot_count", "integer", "Usable weapon slots."),
            ("usable_device_slot_count", "integer", "Usable device slots."),
            ("busy_count", "integer", "Busy equipment slots."),
            ("ready_weapon_count", "integer", "Weapons currently ready to fire."),
            ("ready_device_count", "integer", "Devices currently ready to activate."),
            (
                "slots",
                "table[]",
                "All real equipment slots, including empty and unavailable slots. " +
                "Each slot exposes state, is_usable, equipment_kind, " +
                "equipment_ordinal and readiness."),
            (
                "items",
                "table[]",
                "Occupied equipment slots only, derived from slots for compatibility."));

        AddTable(result, "game.inventory.ammo", "Ammunition inventory.");
        AddFields(
            result,
            "game.inventory.ammo",
            (
                "slots",
                "table[]",
                "Ammunition subslots for equipped ammo-using weapons. " +
                "Empty ammo slots are included; beams and non-weapon equipment " +
                "do not manufacture unavailable ammo rows."),
            (
                "items",
                "table[]",
                "Occupied ammunition slots only, derived from slots for compatibility."));

        AddTable(result, "game.inventory.secure", "Secure-vault inventory.");
        AddFields(
            result,
            "game.inventory.secure",
            ("available", "boolean", "Whether secure inventory is available."),
            ("used", "integer|nil", "Used slots."),
            ("free", "integer|nil", "Free slots."),
            ("unavailable", "integer|nil", "Unavailable slots."),
            (
                "slots",
                "table[]",
                "All secure-vault slots, including empty and unavailable slots."),
            (
                "items",
                "table[]",
                "Occupied secure-vault slots only, derived from slots for compatibility."));

        AddSecondaryInventorySymbols(result, "game.inventory.reward", "Mission reward inventory.");
        AddSecondaryInventorySymbols(result, "game.inventory.overflow", "Overflow inventory.");

        AddTable(result, "game.inventory.vendor", "Current vendor inventory.");
        AddFields(
            result,
            "game.inventory.vendor",
            ("available", "boolean", "Whether an open vendor inventory is currently available."),
            ("loaded", "boolean", "Whether the vendor inventory is loaded."),
            ("current_credits", "number|nil", "Current character credits used for affordability."),
            ("item_count", "integer", "Vendor item count."),
            ("affordable_count", "integer", "Affordable item count."),
            ("unaffordable_count", "integer", "Unaffordable item count."),
            ("items", "table[]", "Vendor items exposing slot, name, stack_count, quality_percent, structure_percent, price and affordable."));
    }

    private static void AddSecondaryInventorySymbols(
        List<AddonApiSymbol> result,
        string path,
        string description)
    {
        AddTable(result, path, description);
        AddFields(
            result,
            path,
            ("available", "boolean", "Whether this inventory is available."),
            ("used", "integer", "item count."),
            ("unavailable", "integer", "Unavailable slots."),
            (
                "slots",
                "table[]",
                "All physical slots, including empty and unavailable slots. Each exposes collection, slot, state and is_usable."),
            (
                "items",
                "table[]",
                "Occupied slots only, derived from slots for compatibility."));
    }

    private static void AddBuffSymbols(List<AddonApiSymbol> result)
    {
        AddTable(result, "game.buffs", "Active effects on the current pilot.");
        AddFields(
            result,
            "game.buffs",
            ("available", "boolean", "Whether active-effect information is currently available."),
            ("active_count", "integer", "Active buff count."),
            ("permanent_count", "integer", "Permanent buff count."),
            ("timed_count", "integer", "Timed buff count."),
            ("nominally_expired_count", "integer", "Effects whose displayed timer has elapsed but which are still active."),
            ("items", "table[]", "Buffs exposing slot, name, type, permanent, timed, remaining_milliseconds and nominally_expired."));
    }

    private static void AddMissionSymbols(List<AddonApiSymbol> result)
    {
        AddTable(result, "game.missions", "Current mission log.");
        AddFields(
            result,
            "game.missions",
            ("available", "boolean", "Whether the mission log is currently available."),
            ("capacity", "integer|nil", "Mission journal capacity."),
            ("count", "integer", "Number of missions in the log."),
            ("items", "table[]", "Missions exposing slot, name, summary, reward, failure_consequence, issuing_faction, stage, stage_count, timed, forfeitable, complete, failed, expired, fully_visible, remaining_milliseconds, terminal, current_stage_text and stages."));
    }

    private static void AddReputationSymbols(List<AddonApiSymbol> result)
    {
        AddTable(result, "game.reputations", "Current faction standings.");
        AddFields(
            result,
            "game.reputations",
            ("available", "boolean", "Whether faction standings are currently available."),
            ("affiliation", "string|nil", "Current pilot affiliation."),
            ("count", "integer", "Faction record count."),
            ("factions", "table[]", "Factions exposing name, description, reaction and disposition."));
    }

    private static void AddNavigationSymbols(List<AddonApiSymbol> result)
    {
        AddTable(result, "game.navigation", "Navigation targets in the current sector and the current Client Manager route.");
        AddFields(
            result,
            "game.navigation",
            ("available", "boolean", "Whether navigation information is currently available."),
            ("target_count", "integer", "Number of navigation targets."),
            ("visited_count", "integer", "Visited navigation target count."),
            ("undiscovered_count", "integer", "Undiscovered navigation target count."),
            ("route_candidate_count", "integer", "Targets suitable as route candidates."),
            ("huge_count", "integer", "Huge navigation target count."),
            ("targets", "table[]", "Navigation targets exposing name, owner_name, title, rank, kind, display_name, signature, visited, huge, route_candidate and spatial."));

        AddTable(result, "game.navigation.control", "Navigation and Warp state useful to addons.");
        AddFields(
            result,
            "game.navigation.control",
            ("available", "boolean", "Whether navigation and Warp state can currently be read."),
            ("phase", "string|nil", "Current navigation phase."),
            ("world_present", "boolean", "Whether the client currently has a usable world."),
            ("loading", "boolean", "Whether the world is loading or transitioning."),
            ("selected_target_known", "boolean", "Whether the game currently reports if a navigation target is selected."),
            ("has_selected_target", "boolean", "Whether a navigation target is currently selected."),
            ("target_distance", "number|nil", "Distance to the selected navigation target."),
            ("path_build_known", "boolean", "Whether the game currently reports route-building progress."),
            ("path_build_busy", "boolean", "Whether the client is still building the selected path."),
            ("warp_idle", "boolean", "Whether Warp controls are idle."),
            ("warp_starting", "boolean", "Whether Warp is starting."),
            ("warp_active", "boolean", "Whether the ship is in active Warp travel."),
            ("warp_recovering", "boolean", "Whether Warp controls are recovering after travel."),
            ("gate_transition_locked", "boolean", "Whether a gate/world transition currently owns navigation control."),
            ("interaction_ready", "boolean", "Whether ordinary target interactions are ready after Warp or transition."),
            ("warp_ready", "boolean", "Whether the client is currently ready to begin Warp to the selected target."),
            ("warp_readiness_reason", "string", "Player-readable explanation of Warp readiness."),
            ("cloaked", "boolean|nil", "Whether the ship is cloaked."),
            ("current_energy", "number|nil", "Current reactor energy used by navigation readiness."),
            ("maximum_energy", "number|nil", "Maximum reactor energy."),
            ("energy_fraction", "number|nil", "Current reactor energy as a value from 0 to 1."));

        AddTable(result, "game.navigation.route", "Current Client Manager route and Auto Pilot state.");
        AddFields(
            result,
            "game.navigation.route",
            ("available", "boolean", "Whether route state is available."),
            ("status", "string|nil", "Current route status."),
            ("status_text", "string|nil", "User-facing route status."),
            ("has_route", "boolean", "Whether a route plan exists."));

        AddTable(result, "game.navigation.route.plan", "Current route plan. This table is nil when no route exists.");
        AddFields(
            result,
            "game.navigation.route.plan",
            ("created_at", "integer", "Creation time as Unix milliseconds."),
            ("updated_at", "integer", "Last update time as Unix milliseconds."),
            ("completed_hops", "integer", "Completed hop count."),
            ("remaining_hops", "integer", "Remaining hop count."),
            ("total_hops", "integer", "Total hop count."),
            ("next_step", "table|nil", "Next route step."),
            ("steps", "table[]", "Route steps exposing number, kind, from, to, departure_target, final_target and access_requirement."),
            ("warnings", "string[]", "Route warnings."));

        AddRouteLocationSymbols(result, "game.navigation.route.plan.origin", "Original route sector.");
        AddNavigationDestinationSymbols(
            result,
            "game.navigation.route.plan.origin_destination",
            "Precise original departure point when known.");
        AddRouteLocationSymbols(result, "game.navigation.route.plan.current", "Current route location.");
        AddNavigationDestinationSymbols(
            result,
            "game.navigation.route.plan.destination",
            "Route destination.");

        AddTable(result, "game.navigation.route.journey", "Runtime journey controls and state.");
        AddFields(
            result,
            "game.navigation.route.journey",
            ("state", "string|nil", "Journey or Auto Pilot state."),
            ("status_text", "string|nil", "Human-readable Auto Pilot status."),
            ("stop_reason", "string|nil", "Stable Auto Pilot stop reason."),
            ("started_at", "integer|nil", "Journey start time as Unix milliseconds."),
            ("pause_reason", "string|nil", "Reserved pause reason."),
            ("is_active", "boolean", "Whether Auto Pilot is currently active."),
            ("expected_target_name", "string|nil", "Current expected route target."),
            ("expected_sector_name", "string|nil", "Current expected destination sector."),
            ("current_energy", "integer|nil", "Current reactor energy when available."),
            ("required_energy", "integer|nil", "Reactor energy required to start Auto Pilot when available."),
            ("can_start", "boolean", "Whether Auto Pilot can start."),
            ("can_stop", "boolean", "Whether Auto Pilot can stop."),
            ("can_select_next_target", "boolean", "Whether the next target can be selected manually."),
            ("can_pause", "boolean", "Whether the journey can pause."),
            ("can_resume", "boolean", "Whether the journey can resume."),
            ("can_clear", "boolean", "Whether the route can be cleared."),
            ("can_plan_return_trip", "boolean", "Whether the completed route can be reversed back to its departure point."));
    }

    private static void AddNavigationDestinationSymbols(
        List<AddonApiSymbol> result,
        string path,
        string description)
    {
        AddTable(result, path, description);
        AddFields(
            result,
            path,
            ("kind", "string", "Destination kind."),
            ("target", "table|nil", "Optional target with name, type, has_position, x, y and z."));
        AddRouteLocationSymbols(
            result,
            string.Concat(path, ".sector"),
            "Destination sector.");
    }

    private static void AddRouteLocationSymbols(
        List<AddonApiSymbol> result,
        string path,
        string description)
    {
        AddTable(result, path, description);
        AddFields(
            result,
            path,
            ("sector_name", "string", "Sector display name."),
            ("system_name", "string", "System display name."));
    }

    private static void AddStarbaseSymbols(List<AddonApiSymbol> result)
    {
        AddTable(result, "game.starbase", "Current station interior, room and interaction.");
        AddFields(
            result,
            "game.starbase",
            ("available", "boolean", "Whether station information is currently available."),
            ("interaction", "table|nil", "Current facility or NPC interaction."),
            ("current_room", "table|nil", "Current room summary."),
            ("rooms", "table[]", "Known rooms exposing current, facilities and NPCs."));

        AddTable(result, "game.starbase.interaction", "Current starbase interaction.");
        AddFields(
            result,
            "game.starbase.interaction",
            ("active", "boolean", "Whether an interaction is active."),
            ("kind", "string", "Type of station interaction."),
            ("facility_name", "string|nil", "Active facility display name."),
            ("npc_name", "string|nil", "Active NPC display name."),
            ("npc", "table|nil", "Current NPC details with name, vendor_type, ambient_type and is_vendor."));

        AddTable(result, "game.starbase.rooms[]", "One known station room.");
        AddFields(
            result,
            "game.starbase.rooms[]",
            ("current", "boolean", "Whether this is the current room."),
            ("facilities", "table[]", "Facilities available in the room."),
            ("npcs", "table[]", "NPCs present in the room."));

        AddTable(result, "game.starbase.rooms[].npcs[]", "One NPC in a station room.");
        AddFields(
            result,
            "game.starbase.rooms[].npcs[]",
            ("name", "string|nil", "NPC name shown by the game."),
            ("vendor_type", "string", "Vendor category: invalid, none, weapon, system, core, consumable, junk, component, resource or black_market."),
            ("ambient_type", "string", "Ambient category: none, npc, vendor or bartender."),
            ("is_vendor", "boolean", "True when vendor_type is an actual vendor category."));
    }

    private static void AddPanelSymbols(List<AddonApiSymbol> result)
    {
        AddTable(result, "game.panels", "Visible game panels, tabs and selected details.");
        AddFields(result, "game.panels", ("available", "boolean", "Whether game-panel information is currently available."));

        AddTable(result, "game.panels.inventory", "Inventory panel state.");
        AddFields(
            result,
            "game.panels.inventory",
            ("displayed", "boolean", "Whether the inventory panel is displayed."),
            ("mode", "string|nil", "Current inventory panel mode."));

        AddTable(result, "game.panels.character", "Character panel state.");
        AddFields(
            result,
            "game.panels.character",
            ("displayed", "boolean", "Whether the character panel is displayed."),
            ("active_tab", "string|nil", "Active character tab."),
            ("last_selected_tab", "string|nil", "Last selected character tab."));

        AddTable(result, "game.panels.character.mission_details", "Mission details panel state.");
        AddFields(
            result,
            "game.panels.character.mission_details",
            ("available", "boolean", "Whether mission-details information is currently available."),
            ("displayed", "boolean", "Whether mission details are displayed."),
            ("has_mission", "boolean", "Whether the selected mission details are available."),
            ("mission", "table|nil", "Selected mission summary with slot, name, stage, stage_count and current_stage_text."));

        AddTable(result, "game.panels.character.faction_details", "Faction-detail presentation inside the Character panel.");
        AddFields(
            result,
            "game.panels.character.faction_details",
            ("available", "boolean", "Whether faction-detail information is currently available."),
            ("displayed", "boolean", "Whether any faction detail UI is displayed."),
            ("panel_displayed", "boolean", "Whether the faction list panel is displayed."),
            ("details_displayed", "boolean", "Whether the selected faction's detail view is displayed."),
            ("has_selection", "boolean", "Whether a faction is selected."),
            ("selected_faction", "table|nil", "Selected faction details with name, description, reaction and disposition."),
            ("visible_factions", "string[]", "Names currently visible in the faction list."));

        AddTable(result, "game.panels.star_map", "Star-map panel state.");
        AddFields(
            result,
            "game.panels.star_map",
            ("displayed", "boolean", "Whether the star map is displayed."),
            ("maximized", "boolean", "Whether the star map is maximized."),
            ("presentation", "table|nil", "Current star-map view details."));
    }

    private static void AddJobSymbols(List<AddonApiSymbol> result)
    {
        AddTable(result, "game.jobs", "The currently open Jobs terminal and its available offers. This domain is unavailable away from a Jobs terminal.");
        AddFields(
            result,
            "game.jobs",
            ("available", "boolean", "Whether Jobs-terminal information is currently available."),
            ("open", "boolean", "Whether the Jobs terminal panel is open."),
            ("selected_category", "string|nil", "Selected combat, trade or explore category."),
            ("offer_count", "integer", "Total offers found in the current catalogue."),
            ("displayed_offer_count", "integer", "Offers currently displayed by the selected filter."),
            ("catalogue_settled", "boolean", "Whether the visible catalogue has stopped changing long enough to be treated as complete."),
            ("offers", "table[]", "Offers exposing category, type, level, sponsor, reward, displayed, selected, still_available, title, description, detail_reward, source and described_at."));
    }

    private static void AddShortcutSymbols(List<AddonApiSymbol> result)
    {
        AddTable(result, "game.shortcuts", "Visible game shortcut bars and their player-facing contents.");
        AddFields(
            result,
            "game.shortcuts",
            ("available", "boolean", "Whether shortcut-bar information is currently available."),
            ("bars", "table[]", "Shortcut bars exposing bar, available, current_group and slots."));
        AddTable(result, "game.shortcuts.bars[].slots[]", "One visible or occupied shortcut slot.");
        AddFields(
            result,
            "game.shortcuts.bars[].slots[]",
            ("bar", "integer", "Shortcut bar number."),
            ("group", "integer", "Shortcut bank/group number."),
            ("button", "integer", "Button position inside the bank."),
            ("visible_key", "integer", "Player-facing shortcut number for the visible bank."),
            ("visible", "boolean", "Whether this bank is currently visible."),
            ("occupied", "boolean", "Whether the slot contains a shortcut."),
            ("identified", "boolean", "Whether the shortcut could be identified as a skill or item."),
            ("kind", "string|nil", "Shortcut kind, such as a skill or item."),
            ("name", "string|nil", "Player-facing shortcut name."),
            ("name_exact", "boolean", "Whether the name identifies the exact skill or item rather than only its family."),
            ("family_name", "string|nil", "Skill or item family name when available."),
            ("inventory_collection", "string|nil", "Inventory collection for an item shortcut."));
    }

    private static void AddTooltipSymbols(List<AddonApiSymbol> result)
    {
        AddTable(result, "game.tooltips", "Game tooltip timing and hover state. This is useful for UI companions and accessibility addons.");
        AddFields(result, "game.tooltips", ("available", "boolean", "Whether tooltip timing or mouse-hover information is currently available."));
        AddTable(result, "game.tooltips.delay", "Current game tooltip delay setting.");
        AddFields(
            result,
            "game.tooltips.delay",
            ("available", "boolean", "Whether the tooltip-delay setting is currently available."),
            ("milliseconds", "integer|nil", "Current tooltip delay in milliseconds."),
            ("percent", "number|nil", "Tooltip-delay setting as a percentage."));
        AddTable(result, "game.tooltips.hover", "Current hovered control and tooltip state.");
        AddFields(
            result,
            "game.tooltips.hover",
            ("available", "boolean", "Whether the control under the mouse can currently be read."),
            ("view", "string|nil", "Main game view or starbase view."),
            ("has_control", "boolean", "Whether a game control is currently hovered."),
            ("displayed", "boolean", "Whether the game tooltip is displayed."),
            ("control_name", "string|nil", "Known name of the control under the mouse."),
            ("text", "string|nil", "Text currently displayed by the game tooltip."));
    }

    private static void AddProductionSymbols(List<AddonApiSymbol> result)
    {
        AddTable(result, "game.production", "The manufacturing or refining recipe currently selected in the game.");
        AddFields(
            result,
            "game.production",
            ("available", "boolean", "Whether a manufacturing or refining recipe is currently selected."),
            ("kind", "string|nil", "Manufacturing or refining recipe kind."),
            ("output_name", "string|nil", "Known player-facing output item name."),
            ("ingredients", "table[]", "Required ingredients exposing name and quantity."));
    }

    private static void AddAudioSymbols(List<AddonApiSymbol> result)
    {
        AddTable(result, "game.audio", "A recognized game sound that has a clear player-facing meaning. Unrecognized sound resources are not exposed.");
        AddFields(
            result,
            "game.audio",
            ("available", "boolean", "Whether a recognized gameplay sound is currently available."),
            ("current", "boolean", "Whether a recognized cue is active."),
            ("cue", "string|nil", "Player-facing cue name, such as mission_forfeited."));
    }

    private static void AddLootSymbols(List<AddonApiSymbol> result)
    {
        AddTable(result, "game.loot", "Current loot panel and tractor state.");
        AddFields(
            result,
            "game.loot",
            ("available", "boolean", "Whether loot information is currently available."),
            ("panel_displayed", "boolean", "Whether the loot panel is displayed."),
            ("has_target", "boolean", "Whether a loot target is selected."),
            ("active", "boolean", "Whether looting is active."),
            ("tractor", "table", "Current loot tractor state."));
        AddTable(result, "game.loot.tractor", "Current or most recent loot-tractor action.");
        AddFields(
            result,
            "game.loot.tractor",
            ("available", "boolean", "Whether loot-tractor information is currently available."),
            ("is_tractoring", "boolean", "Whether the local player is currently tractoring a loot item."),
            ("recently_completed", "boolean", "Whether a loot tractor completed recently."),
            ("recently_interrupted", "boolean", "Whether a loot tractor was interrupted recently."),
            ("item_name", "string|nil", "Display name of the item currently or most recently tractored."),
            ("started_at", "integer|nil", "Start time as Unix milliseconds."),
            ("completed_at", "integer|nil", "Completion or interruption time as Unix milliseconds."));
    }

    private static void AddStatsSymbols(List<AddonApiSymbol> result)
    {
        AddTable(result, "game.stats", "Game-client performance statistics.");
        AddTable(result, "game.stats.network", "Recent network throughput.");
        AddFields(
            result,
            "game.stats.network",
            ("available", "boolean", "Whether network statistics are available."),
            ("receive_bytes_per_second", "number|nil", "Average receive bytes per second."),
            ("send_bytes_per_second", "number|nil", "Average send bytes per second."),
            ("averaging_window_seconds", "number|nil", "Averaging window in seconds."));
        AddTable(result, "game.stats.frame_rate", "Recent frame-rate statistics.");
        AddFields(
            result,
            "game.stats.frame_rate",
            ("available", "boolean", "Whether frame-rate statistics are available."),
            ("smoothed", "number|nil", "Smoothed frames per second."),
            ("minimum", "number|nil", "Lowest recent frames per second."),
            ("maximum", "number|nil", "Highest recent frames per second."));
    }

    private static void AddCombatSymbols(List<AddonApiSymbol> result)
    {
        AddTable(result, "game.combat", "Current combat state and recent damage events.");
        AddFields(
            result,
            "game.combat",
            ("available", "boolean", "Whether combat information is currently available."),
            ("active", "boolean", "Whether combat is currently active."),
            ("combat_music", "boolean", "Whether combat music is active."),
            ("last_damage_observed_at", "integer|nil", "Time of the most recent damage event as Unix milliseconds."),
            ("event_count", "integer", "Recent combat event count."),
            ("recent_events", "table[]", "Damage events exposing observed_at, damage, modifier, unmodified_damage, damage_type, critical, direction, source and victim."));
    }

    private static void AddChatSymbols(List<AddonApiSymbol> result)
    {
        AddTable(result, "game.chat", "Chat domain. Message content is delivered through chat.message events.");
        AddFields(
            result,
            "game.chat",
            ("available", "boolean", "Whether chat event delivery is available."),
            ("event_name", "string", "The chat event name, currently chat.message."));
    }

    private static void AddFields(
        List<AddonApiSymbol> result,
        string parentPath,
        params (string Name, string Type, string Description)[] fields)
    {
        foreach (var field in fields)
        {
            AddField(
                result,
                string.Concat(parentPath, ".", field.Name),
                field.Type,
                field.Description);
        }
    }

    private static void AddBooleanFields(
        List<AddonApiSymbol> result,
        string parentPath,
        params string[] fieldNames)
    {
        foreach (var fieldName in fieldNames)
        {
            AddField(
                result,
                string.Concat(parentPath, ".", fieldName),
                "boolean",
                string.Concat("Whether the ", fieldName.Replace('_', ' '), " target verb can execute now."));
        }
    }
}
