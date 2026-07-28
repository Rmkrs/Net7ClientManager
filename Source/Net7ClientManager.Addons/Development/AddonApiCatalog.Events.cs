namespace Net7ClientManager.Addons.Development;

public static partial class AddonApiCatalog
{
    private static IReadOnlyList<AddonApiEventDefinition>
        BuildEventDefinitions()
    {
        List<AddonApiEventDefinition> events =
        [
            Event(
                "lifecycle.changed",
                "The game client moved between login, character selection, loading, in-game or unavailable states.",
                "previous, current"),
            Event(
                "world.environment_changed",
                "The pilot moved between space, a starbase interior or an unknown environment.",
                "previous, current"),
            Event(
                "world.location_changed",
                "The current system, sector or starbase changed.",
                "previous, current location tables",
                "Ordinary X/Y/Z movement does not generate events."),

            Event(
                "character.changed",
                "The pilot information available through game.character changed, including identity, progression, economy, skills or position availability.",
                "domain",
                "Ordinary X/Y/Z movement does not emit character.changed; read game.character.spatial whenever another relevant event or UI refresh occurs."),
            Domain("ship", "ship vitals, flags, movement, equipment readiness or temporary state"),
            Event(
                "target.changed",
                "The current target or its useful details changed.",
                "domain",
                "Ordinary target distance and coordinate movement remain readable but do not emit target.changed."),
            Event(
                "nearby_targets.changed",
                "The nearby-target roster, selection, vitals or viewport state changed.",
                "domain",
                "Moving gutter coordinates remain readable but do not emit nearby_targets.changed."),
            Domain("group", "group membership or group settings"),
            Domain("inventory", "owned inventory, equipment, ammo, vault or overflow state"),
            Domain("buffs", "the active buff collection"),
            Domain("missions", "the live mission log"),
            Domain("reputations", "faction standings"),
            Event(
                "navigation.changed",
                "Navigation targets, the route, or Warp readiness changed.",
                "domain",
                "Per-tick target distance and reactor-energy drift remain readable but do not emit navigation.changed."),
            Domain("starbase", "starbase rooms, NPCs, facilities or active interaction"),
            Domain("panels", "visible game panels and their current tabs or details"),
            Domain("jobs", "the Jobs terminal catalogue or selection"),
            Domain("shortcuts", "visible shortcut bars or known shortcut contents"),
            Domain("tooltips", "tooltip timing or the control under the mouse"),
            Domain("production", "the selected manufacturing or refining recipe"),
            Domain("audio", "the current recognized gameplay sound"),
            Domain("loot", "loot panel or tractor state"),
            Domain("stats", "network or frame-rate statistics"),
            Domain("combat", "combat state or the recent damage-event window"),
            Domain("chat", "chat event delivery state"),

            Event(
                "game.updated",
                "One or more parts of the current game state changed together.",
                "domains: string[]",
                "Use this for broad repaint/recompute work. Prefer a narrower event when only one behavior matters."),
            Event(
                "combat.event",
                "One new incoming or outgoing damage event was added to the recent combat list.",
                "observed_at, damage, unmodified_damage, modifier, damage_type, critical, direction, source, victim"),
            Event(
                "chat.message",
                "One chat line became available for this game client.",
                "text, observed_at, from_history",
                "Earlier visible lines can be replayed when the addon attaches; check from_history when an addon only wants new messages."),

            Event("loot.tractor_started", "A loot item began tractoring toward the pilot.", "item_name, started_at, completed_at"),
            Event("loot.tractor_completed", "A loot tractor completed successfully.", "item_name, started_at, completed_at"),
            Event("loot.tractor_interrupted", "A loot tractor stopped before completion.", "item_name, started_at, completed_at"),

            Event("character.credits_gained", "The pilot's credit balance increased.", "previous, current, change, amount"),
            Event("character.credits_lost", "The pilot's credit balance decreased.", "previous, current, change, amount"),
            Event("character.experience_debt_increased", "The pilot's experience debt increased.", "previous, current, change, amount"),
            Event("character.experience_debt_reduced", "The pilot's experience debt decreased.", "previous, current, change, amount"),
            Event("character.level_up", "Combat, Explore or Trade level increased.", "track, previous_level, current_level, levels_gained, overall_level"),
            Event("character.overall_level_up", "The combined Combat + Explore + Trade level increased.", "previous_level, current_level, levels_gained"),
            Event("character.skill_points_changed", "The unspent skill-point total changed.", "previous, current, change, amount"),
            Event("character.hull_upgraded", "The pilot received a hull upgrade.", "previous, current, change, amount"),

            Event("reputation.increased", "Standing with one faction increased.", "name, previous, current, change, disposition"),
            Event("reputation.decreased", "Standing with one faction decreased.", "name, previous, current, change, disposition"),

            Event("target.acquired", "The pilot selected a target after having none.", "name, kind, relation, self, group_member"),
            Event("target.cleared", "The pilot cleared the current target.", "name, kind, relation, self, group_member"),
            Event("target.switched", "The selected target changed directly from one target to another.", "previous and current target tables"),
            Event("group.member_joined", "A pilot appeared in the pilot's group roster.", "the same member fields exposed by game.group.members[]"),
            Event("group.member_left", "A pilot disappeared from the pilot's group roster.", "the same member fields exposed by game.group.members[]"),
            Event("inventory.item_gained", "The total owned quantity of an item increased across cargo, equipment, ammo, vault, reward and overflow storage.", "name, quantity, previous_quantity, current_quantity",
                "Moving an item between owned containers does not count as a gain."),
            Event("inventory.item_lost", "The total owned quantity of an item decreased across owned storage.", "name, quantity, previous_quantity, current_quantity",
                "Moving an item between owned containers does not count as a loss."),
            Event("buff.applied", "An active buff appeared.", "the same fields exposed by game.buffs.items[]"),
            Event("buff.removed", "An active buff disappeared.", "the same fields exposed by game.buffs.items[]"),
            Event("panel.opened", "A supported game panel became visible.", "panel, state, details",
                "panel is inventory, character, mission_details, faction_details, star_map, loot or job_terminal."),
            Event("panel.closed", "A supported game panel stopped being visible.", "panel, state, details"),
            Event("ship.incapacitated", "The current pilot's ship became incapacitated.", "previous, current"),
            Event("ship.recovered", "The current pilot's ship recovered from incapacitation.", "previous, current"),
            Event("ship.cloaked", "The current pilot's ship became cloaked.", "previous, current"),
            Event("ship.uncloaked", "The current pilot's ship stopped being cloaked.", "previous, current"),
            Event("navigation.warp_started", "The pilot entered active Warp travel.", "previous, current"),
            Event("navigation.warp_ended", "The pilot left active Warp travel.", "previous, current"),
            Event("navigation.journey_changed", "The Client Manager route journey changed state.", "previous, current, status_text, stop_reason, pause_reason, expected_target_name, expected_sector_name"),
            Event("production.recipe_selected", "A manufacturing or refining recipe became selected or changed.", "the game.production table"),
            Event("production.recipe_cleared", "The previously selected production recipe was cleared.", "the previous game.production table"),
            Event("audio.cue_changed", "The recognized game cue changed.", "previous, current"),
            Event("starbase.interaction_started", "The player began interacting with a station facility or NPC.", "the game.starbase.interaction table"),
            Event("starbase.interaction_ended", "The active station interaction ended.", "the previous game.starbase.interaction table"),

            Mission("accepted", "A mission or job was accepted or first established as active."),
            Mission("source_identified", "An active mission was linked to the NPC or Jobs terminal that provided it."),
            Mission("progressed", "The mission stage or current objective changed."),
            Mission("completed", "The mission completed."),
            Mission("forfeited", "The player forfeited the mission."),
            Mission("failed", "The mission failed."),
            Mission("expired", "The mission expired."),
            Mission("no_longer_active", "A mission disappeared from the active log without a confirmed completion, forfeiture, failure or expiry."),

            Event("activity.recorded", "The Activity Journal recorded one player activity.",
                "kind, category, pilot_name, summary, details, location fields",
                "This event is available when Activity History recording is enabled."),
            Event("activity.reputation_changed", "The Activity Journal recorded a faction-standing change with its best known cause.",
                "name, previous, current, change, reason, location fields",
                "This richer companion to reputation.increased/decreased is available when Activity History recording is enabled."),
            Event("activity.loot_updated", "The Activity Journal created or updated one corpse or wreck loot record.",
                "source_name, credits, started_at, last_updated_at, items, location fields",
                "The same session can be emitted more than once as synchronously tractored items and credits arrive."),

            Event("combat.encounter_ended", "Client Manager finished tracking one combat encounter and calculated its outcome and totals.",
                "pilot_name, outcome, target_name, target_combat_level, started_at, ended_at, duration_milliseconds, location fields, damage, hit and critical totals",
                "combat.event reports individual hits; this event provides the completed encounter summary."),

            InstallationEvent("forge.contribution_succeeded", "A Forge contribution batch completed successfully.", "batch_count, facts_submitted, evidence_accepted, status"),
            InstallationEvent("forge.contribution_failed", "A Forge contribution batch failed.", "batch_count, status"),
            InstallationEvent("forge.revision_published", "A contribution caused Forge to publish a new authority revision.", "revision"),
            InstallationEvent("forge.dataset_update_downloaded", "A newer Forge world-data package was downloaded and staged.", "dataset_epoch, from_revision, to_revision, mode, reason, package_size"),
            InstallationEvent("forge.dataset_update_activated", "A staged Forge world-data package became the active local authority.", "dataset_epoch, from_revision, to_revision"),
            InstallationEvent("forge.recipe_catalog_updated", "The local Forge production-recipe catalogue was replaced by a newer revision.", "previous_revision, revision, recipe_count, generated_at"),
            InstallationEvent("forge.mission_catalog_updated", "The local Forge mission catalogue was replaced by a newer revision.", "previous_revision, revision, mission_count, generated_at"),
            InstallationEvent("social.pilot_online", "A pilot who enabled Social presence became publicly visible as online.", "pilot_name, freshness, presence_updated_at",
                "No sector, station, navigation target or coordinates are included."),
            InstallationEvent("social.pilot_offline", "A publicly visible Social pilot went offline or was no longer present in the latest Social results.", "pilot_name, freshness, presence_updated_at",
                "No position information is included."),
        ];

        return events;
    }

    private static AddonApiEventDefinition Domain(
        string domain,
        string meaning)
    {
        return Event(
            string.Concat(domain, ".changed"),
            string.Concat("The game.", domain, " state changed: ", meaning, "."),
            "domain",
            "Read the new state from game." + domain + ". Several changes that happen close together may be reported once.");
    }

    private static AddonApiEventDefinition Mission(
        string suffix,
        string summary)
    {
        return Event(
            string.Concat("mission.", suffix),
            summary,
            "pilot_name, source, status, job_category, name, summary, reward, failure_consequence, issuing_faction, stage, stage_count, objective, issuer_npc_name, accepted_at, ended_at, duration_milliseconds, system_name, sector_name, starbase_name");
    }

    private static AddonApiEventDefinition InstallationEvent(
        string name,
        string summary,
        string payload,
        string? notes = null)
    {
        var installationNote =
            "This event concerns the whole Client Manager installation rather than one pilot. It is sent to addons running for each active game client and includes scope = installation.";
        return Event(
            name,
            summary,
            string.Concat("scope, ", payload),
            string.IsNullOrWhiteSpace(notes)
                ? installationNote
                : string.Concat(installationNote, " ", notes));
    }

    private static AddonApiEventDefinition Event(
        string name,
        string summary,
        string payload,
        string? notes = null)
    {
        return new AddonApiEventDefinition(
            name,
            summary,
            payload,
            notes);
    }
}

public sealed record AddonApiEventDefinition(
    string Name,
    string Summary,
    string Payload,
    string? Notes = null)
{
    public string Description => string.IsNullOrWhiteSpace(this.Notes)
        ? string.Concat(this.Summary, " Fields: ", this.Payload, ".")
        : string.Concat(
            this.Summary,
            " Fields: ",
            this.Payload,
            ". ",
            this.Notes);
}
