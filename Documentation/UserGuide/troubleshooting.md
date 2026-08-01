# Troubleshooting and useful notes

## A client does not start

- Confirm `LaunchNet7.exe` still works on its own.
- Start the client again and reselect the launcher when prompted.
- Let the Net-7 launcher finish its checks. The manager waits for the launcher to become ready before pressing Play.
- Only one managed launch is performed at a time because the Net-7 launcher behaves as a single shared launcher.

The Running clients area reports the current launch stage or the reason the launch stopped.

## Login automation does not act

Check the slot:

- A saved account is selected.
- A password is stored when automatic login is enabled.
- **Automatically log in** is enabled.
- A saved pilot is selected when automatic pilot entry is enabled.
- The slot is not sharing the same account with another slot in the active profile.

Earth & Beyond may reject managed input on a monitor positioned to the left of the primary monitor. Move the affected slot to a non-negative horizontal position and test again.

## The wrong pilot appears in a slot

The slot describes the expected pilot, while the Running clients card reports the pilot actually observed. A manual login choice or externally started game client can create a mismatch without losing either record.

Correct the game selection, edit the slot, or leave the client unassigned as appropriate.

## A contextual helper is missing

Contextual tools appear only when their game panel and required information are present:

- Vendor shopping needs an active list, a matching vendor, and the option enabled.
- Build companions need an active build and the matching Equipment or Skills panel.
- Jobs Terminal routing needs a selected offer with a recognized destination.
- The in-game Mission Wiki needs a selected mission and the option enabled. The popped-out companion can select from the full current mission list.
- Fleet Loot needs observed loot on the current corpse.
- Enhanced faction details need the native faction detail panel and a selected faction.

Briefly close and reopen the source game panel after enabling a feature.

## An add-on is installed but not visible

Open **Addon Center → Installed** and confirm it is enabled for the current client. Read its runtime state and then check **Activity** for warnings.

Some add-ons wait for an in-game pilot, a target, a particular panel, or other suitable context before showing anything.

Use **Suspend all for this session** and **Resume addons** to reset the current session without changing individual choices. Development workspaces can override packaged releases until discarded.

## Pilot Archive information is missing or old

The archive can only preserve information that has been observed. Log the pilot in and open the relevant game panel when needed:

- Inventory or vault for stored items.
- Equipment for fitted items.
- Skills for skill ranks.
- Missions for the current journal.
- Factions for reputation detail.

Check the timestamp at the top of each archive section.

## Histories are empty

Open **Client Manager → Options → History** and enable the relevant journal. Recording begins from that point; it does not reconstruct events that happened before the feature was enabled or while the manager was not observing the pilot.

## A route is unavailable

The route catalog may know that a destination is blocked or conditional, or may lack enough information to connect it safely. Read the access message in the Route Planner and try a different destination or pilot.

Forge data and new personal observations can improve route knowledge over time.

## Game Settings will not edit a pilot

Running pilots are intentionally left alone. Log the pilot out, reopen or reload Game Settings, and try again.

Ordinary settings marked not configured must first be created by Earth & Beyond. Chat Font copy actions are the exception and can create the missing resolution record.

## Enhanced item tooltip is awkwardly positioned

Open **Client Manager → Options → Item tooltips**, adjust the horizontal and vertical offsets, or use **Reset**. The setting applies to the manager’s enhanced tooltip without moving the native game panels.

## Fleet actions are unavailable

Open the Action HUD and read the reason shown for the action. Common causes include:

- The pilot is not fully in game.
- A confirmation or text-entry panel is active.
- No suitable target is selected.
- The target relationship is wrong for the action.
- A skill or item is cooling down.
- Reactor energy is insufficient.
- The target is out of range.
- Required ammunition or equipment is missing.

The manager favors refusing an unsafe action over broadcasting a hopeful keypress into the void.
