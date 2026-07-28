# Maintainer coverage ledger

This page is intentionally not in the player-facing navigation. It records how the v1 source snapshot was mapped into the guide and highlights areas that deserve a final runtime screenshot or wording check.

## Source areas covered

| Source area | User-facing coverage |
|---|---|
| Main dashboard, profiles, slots, layout, quick launch | `fleet-setup.md`, `running-your-fleet.md` |
| Accounts, passwords, pilot roster | `fleet-setup.md` |
| Launcher handoff, client hosting, automatic login and pilot entry | `getting-started.md`, `running-your-fleet.md` |
| In-game Client Manager overlay and window placement | `in-game-tools.md` |
| Command Palette and fleet formations | `in-game-tools.md` |
| Action HUD and Fleet Loot | `in-game-tools.md` |
| Route Planner and Auto Pilot support | `navigation.md` |
| Galaxy Atlas, layers, presence, and destination actions | `navigation.md` |
| Jobs Terminal route companion | `navigation.md` |
| Mission Wiki | `navigation.md` |
| Faction details overlay | `navigation.md` |
| Galaxy Finder, detailed items, sources, and routes | `galaxy-finder.md` |
| Shopping lists, recipes, ownership, and vendor companion | `galaxy-finder.md` |
| Pilot Archive, search, launch, and all tabs | `pilot-archive.md` |
| Mission, activity, combat, and reputation journals | `pilot-archive.md` |
| Build Board, equipment picker, companions, Forge versions and stars | `builds.md` |
| Presence, LFG, and guild recruitment | `social.md` |
| Addon Discover, Installed, Activity, details, pinning, uninstall | `addons.md` |
| Addon development workspace, editor, API catalog, validation, publishing | `addons.md` plus technical `Documentation/Lua-Addon-Guide.md` |
| Forge contribution categories, attribution, statistics, profile recovery, data revisions | `forge-contributions.md` |
| Game Settings camera, interface, graphics, sound, privacy, chat font, bulk apply | `game-settings.md` |
| Enhanced game and manager item tooltips | `in-game-tools.md`, `pilot-archive.md` |

## Deliberate boundaries

- Internal memory observation, process detection, storage schemas, API contracts, and compatibility layers are not described in the player guide.
- The exact bundled add-on catalog is not listed because packages can change independently of the host and were not present as package files in this snapshot.
- Auto Pilot is described as route-execution support started by an installed navigation add-on because the host contains the travel engine while the player-facing start control belongs to an add-on.
- Forge moderation, authentication, and recovery implementation details are omitted; only the player-visible recovery flow is described.
- Vendor prices, personal discounts, mob drop rates, and kill counts are explicitly excluded from contribution descriptions, matching the UI promises.

## Final runtime verification targets

Before release, verify these terms against the built application:

1. The exact labels shown for Social Atlas visibility values.
2. The final wording of build availability and update indicators.
3. The complete Chat Font row labels at each supported resolution.
4. The player-facing Auto Pilot start/stop control supplied by the bundled navigation add-on.
5. Which enhanced faction sections are visible for factions with sparse source data.
6. The names and descriptions of bundled official add-ons included in the release artifact.

These checks should adjust wording or screenshots, not expand the release scope.
