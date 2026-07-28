# Equipment-centred builds

This folder contains the local, equipment-centred Build feature shown beside a
live character's native Equip screen.

A Build describes one target state for one profession. It does not contain a
training route, stages, checkpoints, or a historical reconstruction of how a
character arrived there.

## Authoring contract

The author chooses:

- weapons, shield, reactor, engine, and devices;
- accepted alternatives for each equipment requirement;
- optional recommended skill ranks above the equipment-derived minimum;
- a local name and purpose.

N7CM derives:

- the minimum equipment skill ranks;
- the minimum Combat, Explore, and Trade levels;
- direct Overall and hull-slot requirements;
- the Overall level needed to earn enough skill points for the effective skill
  targets.

Weapons and devices are unordered one-to-one requirements. Their stored order
is presentation-only. One equipped item cannot satisfy two requirements.

## Shared board

Creation, editing, and application use the same board:

- every skill available to the profession is always shown alphabetically;
- in edit mode, gold pips are equipment-derived and blue pips are optional
  author recommendations;
- in use mode, gold pips are trained and blue pips are still required;
- equipment follows the game's familiar geography, with weapons on the left,
  devices on the right, and shield/reactor/engine in the middle;
- the companion can always be collapsed with its visible Hide/Show control.

Local builds are mutable. A character may select one profession-compatible
build as active. Forge publication, immutable public versions, ratings, and
publisher identity remain outside this local contract.

## Starting state and skill points

The canonical character-creation baseline, runtime-verified for all nine
professions, is:

- Overall 0, Combat 0, Explore 0, Trade 0;
- Beam Weapon 1;
- Device Tech 1;
- Engine Tech 1;
- Reactor Tech 1;
- Shield Tech 1.

Those five initial ranks cost no skill points.

Generic Build requirements assume no bonus skill points. Live application uses
the observed character's current ranks and authoritative available skill-point
value. Bonus points already trained into useful ranks or still available can
therefore lower that character's effective Overall requirement.

The live `QuestOnlyLevels` field is interpreted as the first quest-only rank.
For example, a value of 8 on an eight-rank skill means rank 8 consumes no skill
points. This is used for both generic and live point accounting whenever live
skill metadata is available.

Combat, Explore, and Trade remain their true independent minima. They are never
inflated merely to satisfy Overall. Overall is shown separately as the maximum
of direct requirements, hull-slot requirements, the sum of the discipline
minima, and the skill-point-driven requirement.

## Local persistence

Mutable local documents and the active character selection are stored in:

`%AppData%\\Net7ClientManager\\skill-builds.db`

Schema version 3 intentionally replaces the unreleased stage/goal prototype
schema. No compatibility scaffolding for that rejected experiment remains.

## Current boundary

This slice provides local creation from current equipment or from an empty
board, equipment catalogue selection, alternatives, recommended skill targets,
derived requirements, persistence, active-build selection, live skill/level
comparison, equipped/missing status, and a visible Hide/Show control.

Inventory, vault, and Galaxy Finder integration, equipment icons, themed
scrollbars, Forge publication, ranking, and ratings are follow-up work.
