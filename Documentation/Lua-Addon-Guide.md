# Net7 Client Manager Lua addon guide

Net7 Client Manager addons are small Lua programs that can read the safe, functional game state for one Earth & Beyond client and draw their own user interface. Addons can react to meaningful game events and request a limited set of approved actions from addon button clicks.

The API is designed around game concepts. It exposes pilots, ships, targets, inventories, missions, routes, panels, Jobs terminals, production recipes, combat, loot and other player-facing state. It does not expose account credentials or low-level game internals.

## One addon per game client

Each addon runs separately for one game client. In an addon, `game` always means the latest safe game state for that client.

An addon cannot list every Client Manager slot or inspect another game client. When the same addon runs for several clients, each one receives its own copy and its own game state.

Some Client Manager and Forge events concern the whole installation rather than one pilot. These events include `scope = "installation"` and are delivered to addons running for each active game client.

## When game state is available

Game state is not available continuously. Login, character selection, sector transitions and loading screens can temporarily make domains unavailable.

```lua
if not game.character.available then
    return
end

local spatial = game.character.spatial
if spatial.available and spatial.position ~= nil then
    addon.log.info(string.format(
        "Position: %.1f, %.1f, %.1f",
        spatial.position.x,
        spatial.position.y,
        spatial.position.z))
end
```

Use these rules:

- Check a domain's `available` field before relying on its contents.
- Treat `nil` as unknown or not currently available, not as zero.
- Read the current table when an event fires. Do not keep table references indefinitely because the game-state tables are refreshed.
- Times are Unix timestamps in milliseconds.
- Coordinates use Earth & Beyond's game-world coordinate system.

## Main game domains

| Domain | What it represents |
|---|---|
| `game.lifecycle` | Login, character-selection, loading and in-game lifecycle state. |
| `game.world` | The current pilot's system, sector, station and environment. |
| `game.character` | Pilot identity, credits, experience debt, progression, skills and X/Y/Z position. |
| `game.ship` | Shield, hull, reactor, ship flags, movement, stats, quadrants and radar state. |
| `game.target` | The selected target, relationship, vitals, spatial state and available interactions. |
| `game.nearby_targets` | Targets currently shown by the game's nearby-target radar. |
| `game.group` | Current group settings and members. |
| `game.inventory` | Cargo, equipment, ammo, vault, reward, overflow and vendor inventory. |
| `game.buffs` | Active effects observed on the current pilot. |
| `game.missions` | Current live mission log and stages. |
| `game.reputations` | Faction standings and player-facing disposition labels. |
| `game.navigation` | Navigation targets, Client Manager route plan and high-level Warp/control readiness. |
| `game.starbase` | Station rooms, NPCs, facilities and the active interaction. |
| `game.panels` | Visible game panels, tabs, mission details, faction details and star map presentation. |
| `game.jobs` | The open Jobs terminal, category, selection and observed offers. |
| `game.shortcuts` | Player-facing shortcut bars and their visible slot contents. |
| `game.tooltips` | Tooltip delay and the control currently under the mouse. |
| `game.production` | The manufacturing or refining recipe currently selected in game. |
| `game.audio` | Recognized game sounds with a clear gameplay meaning. |
| `game.loot` | Loot panel and tractor state. |
| `game.stats` | Network and frame-rate measurements. |
| `game.combat` | Current combat state and the recent per-hit event window. |
| `game.chat` | Chat event availability. Chat lines arrive through `chat.message`. |

The addon editor contains the full field reference, return types and descriptions for every table and function.

## Events

Subscribe with `game.events.on`:

```lua
game.events.on("character.credits_gained", function(event)
    addon.log.info(string.format(
        "Received %d credits; balance is now %d.",
        event.amount,
        event.current))
end)
```

Every event table contains:

- `name`: the event name;
- `observed_at`: when the game or Client Manager event occurred;
- the event-specific fields documented by the editor reference.

Events are grouped into three useful levels:

1. **State-change events**, such as `inventory.changed`, report that one complete game-state table changed.
2. **Derived gameplay events**, such as `inventory.item_gained`, `mission.completed` or `character.level_up`, describe a meaningful transition.
3. **Detailed journal and Client Manager events**, such as `activity.reputation_changed`, `combat.encounter_ended` or `forge.dataset_update_activated`, include related details collected by Client Manager.

### Economy and progression

```lua
game.events.on("character.level_up", function(event)
    addon.log.info(string.format(
        "%s level increased from %d to %d.",
        event.track,
        event.previous_level,
        event.current_level))
end)

game.events.on("character.credits_lost", function(event)
    addon.log.info(string.format("Spent %d credits.", event.amount))
end)
```

Credit balance events fire as soon as Client Manager observes the balance change. When Activity History is enabled, `activity.recorded` may later provide the richer settled cause, such as loot, a vendor purchase, a vendor sale or a mission reward.

### Missions

Mission lifecycle events include accepted, source identified, progressed, completed, forfeited, failed, expired and no longer active.

```lua
game.events.on("mission.completed", function(event)
    addon.log.info(string.format(
        "Completed %s in %s.",
        event.name or "a mission",
        event.sector_name or "an unknown sector"))
end)
```

### Panels and terminals

```lua
game.events.on("panel.opened", function(event)
    if event.panel == "job_terminal" then
        addon.log.info("Jobs terminal opened.")
    end
end)
```

Supported panel names include inventory, character, mission details, faction details, star map, loot and Jobs terminal.

### Forge and Client Manager

Forge events let addons react when contributions succeed, a new authority revision is published, or downloaded world knowledge becomes active.

```lua
game.events.on("forge.dataset_update_activated", function(event)
    addon.log.info(string.format(
        "Forge world data updated to revision %d.",
        event.to_revision))
end)
```

These events concern the whole Client Manager installation and therefore include `scope = "installation"`.

### Social presence

`social.pilot_online` and `social.pilot_offline` report the public lifecycle of pilots who opted into Social presence. They intentionally contain no coordinates, sector, station or nearest-navigation information.

Ordinary movement also produces no position event. Addons can read the latest pilot, selected-target, nearby-target and navigation coordinates or distances, but Client Manager does not turn movement or route-energy drift into a continuous event stream.

## UI and approved actions

Use `ui` to build addon windows and controls. Use `actions` only for the approved actions listed by the API reference.

Actions that affect the game or Client Manager can only run when the player clicks an addon button. Timers and background event handlers cannot silently control the client.

```lua
local window = ui.window({
    title = "Route helper",
    width = 280,
    height = 120,
})

window:button({
    text = "Clear route",
    on_click = function()
        local ok, reason = actions.navigation.clear_route()
        if not ok then
            addon.log.warn(reason or "The route could not be cleared.")
        end
    end,
})
```

## Chat and combat streams

`chat.message` can include earlier visible lines when Client Manager attaches to an already open chat window. Check `event.from_history` when only newly arriving lines should trigger behavior.

`combat.event` reports each detected hit. `combat.encounter_ended` is the richer encounter summary containing the final outcome, target, location, duration and damage totals.

## Privacy and safety boundaries

Lua does not receive:

- account names, passwords or launcher credentials;
- process identifiers;
- game-internal numbers, item template numbers, internal faction keys or sector keys;
- low-level memory and observation details;
- game state from other clients;
- private Social position data;
- arbitrary file-system, process or network access outside the documented sandbox.

The documented API exposes things a player or addon can understand and use. The machinery used to observe the game stays private and may change at any time.

## Compatibility for v1

The v1 contract is intentionally player-facing. A few older values remain only so the bundled v1 addons keep working without a release-eve republish. The editor does not show them, and new addons should use only the fields and actions documented here.

The editor-generated API reference is the source of truth for exact fields, function signatures and event fields. This guide explains the model and common patterns around that reference.
