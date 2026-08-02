# Automation control with net7cmctl

`net7cmctl.exe` is the supported local automation interface for Net7 Client Manager. It lets AutoHotkey, PowerShell, AC Tools, Stream Deck profiles, and other local tools query player-facing fleet state and request normal Client Manager actions without finding windows, sending clicks, or reading text from fixed desktop coordinates.

The helper is included beside `Net7ClientManager.exe` in release packages. Net7 Client Manager must already be running. Communication stays on the local computer and is restricted to the current signed-in Windows user. Automation tools and `net7cmctl` should run normally; they do not need administrator access even when Net7 Client Manager itself is elevated.

The automation interface exposes capabilities already available through the supported Client Manager UI or Lua API. Some commands combine existing actions with a blocking wait so the calling tool receives one final result, but they do not add new gameplay capabilities.

## First commands

List the slots in the active profile:

```powershell
net7cmctl slots
```

Read the current location of one slot:

```powershell
net7cmctl get location --slot "Alt 1"
```

Launch a configured slot and wait until its pilot is genuinely in the game:

```powershell
net7cmctl launch --slot "Alt 1" --wait --timeout 180
```

Set a destination, start Auto Pilot, and wait for arrival:

```powershell
net7cmctl autopilot-to "Orsini Mining Platform" --slot "Alt 1" --timeout 900
```

Destination names must identify one known sector or navigation target. An ambiguous name is rejected and the JSON response lists the matching candidates.

## Queries

```text
net7cmctl get status --slot "Alt 1"
net7cmctl get location --slot "Alt 1"
net7cmctl get target --slot "Alt 1"
net7cmctl get interaction --slot "Alt 1"
net7cmctl get group --slot "Alt 1"
net7cmctl get missions --slot "Alt 1"
net7cmctl get inventory --slot "Alt 1"
net7cmctl get route --slot "Alt 1"
net7cmctl get autopilot --slot "Alt 1"
```

These queries expose the same player-facing state already used by Client Manager windows and the supported Lua API. `get location` includes the current system, sector, station or starbase, environment, and nearest known navigation point. `get interaction` reports the current target's observed interaction actions. Group, mission, and inventory queries expose their existing readable Client Manager state without adding new gameplay capabilities.

Plain output is designed for simple scripts. Add `--json` for versioned structured output:

```powershell
net7cmctl get location --slot "Alt 1" --json
```

Use `--field` when a macro needs one scalar rather than a JSON document:

```powershell
net7cmctl get location --slot "Alt 1" --field environment
net7cmctl get location --slot "Alt 1" --field nearest-nav
net7cmctl get interaction --slot "Alt 1" --field verb
net7cmctl get group --slot "Alt 1" --field member-count
```

Field matching ignores case, hyphens, underscores, and camel-case differences. Nested fields use dots, such as `cargo.free`. A requested object or collection is rejected instead of being flattened ambiguously.

Check for one group member without parsing the member list:

```powershell
net7cmctl get group --slot "Main" --member "SpaJE" --field present
```

Filter the current mission log by name, state, or the existing Mission Wiki job classification:

```powershell
net7cmctl get missions --slot "Main" --state active --field count
net7cmctl get missions --slot "Main" --type trade-job --field count
net7cmctl get missions --slot "Main" --name "Satellite" --json
```

Supported mission states are `active`, `complete`, `failed`, `expired`, `terminal`, and `all`. Supported types are `mission`, `job`, `combat-job`, `trade-job`, `explore-job`, and `all`. Job classification and destination details are returned only when Client Manager's existing mission journal has identified the mission as a terminal job.

Read an inventory collection or count one exact item name:

```powershell
net7cmctl get inventory --slot "Main" --collection cargo --json
net7cmctl get inventory --slot "Main" --collection cargo --field free
net7cmctl get inventory --slot "Main" --item "Survey Satellite" --field quantity
```

Collections are `cargo`, `equipment`, `ammo`, `secure` or `vault`, `reward`, `overflow`, and `vendor`. An item query without `--collection` deliberately searches current cargo, which is the useful and deterministic default for job-running macros.

Unavailable observations are reported explicitly and return a non-zero exit code. They are never disguised as empty strings or a false zero.

## Window and navigation commands

```text
net7cmctl launch --slot "Alt 1"
net7cmctl focus game --slot "Alt 1"
net7cmctl focus navigation --slot "Alt 1"
net7cmctl show navigation --slot "Alt 1"
net7cmctl show mission-wiki --slot "Alt 1"
net7cmctl set-destination "Orsini Mining Platform" --slot "Alt 1"
net7cmctl start-autopilot --slot "Alt 1"
net7cmctl stop-autopilot --slot "Alt 1"
net7cmctl plan-return --slot "Alt 1"
net7cmctl clear-route --slot "Alt 1"
```

Commands use the same guarded Client Manager services as the normal UI. The helper does not expose arbitrary mouse clicks, keyboard injection, memory addresses, internal objects, or gameplay actions that Client Manager does not already support.

## Synchronous convenience commands

Automation tools should not need to reproduce an asynchronous Client Manager workflow with guessed sleeps.

```powershell
net7cmctl launch --slot "Alt 1" --wait --timeout 180
```

This starts the configured slot through the existing Client Manager launch action and returns only when the pilot enters the game, the launch fails, or the timeout expires.

```powershell
net7cmctl autopilot-to "Prasad Station" --slot "Alt 1" --timeout 900
```

This uses the existing destination and Auto Pilot actions, then blocks until Auto Pilot arrives, stops, or times out. A known Auto Pilot stop is reported immediately as a failed condition instead of being misreported later as a timeout.

## Waiting for real state

The individual waits remain available when a macro wants to conduct the steps itself:

```powershell
net7cmctl wait in-game --slot "Alt 1" --timeout 120
net7cmctl wait autopilot-complete --slot "Alt 1" --timeout 600
```

Ron-style recovery and job-running macros can also block on the same readable state exposed by the query commands:

```powershell
net7cmctl wait environment --slot "Main" --value station --timeout 120
net7cmctl wait location --slot "Main" --station "Prasad Station" --timeout 300
net7cmctl wait interaction --slot "Main" --verb Dock --timeout 60
net7cmctl wait group-member --slot "Main" --member "SpaJE" --timeout 30
net7cmctl wait mission-count --slot "Main" --state active --at-least 6 --timeout 60
net7cmctl wait inventory --slot "Main" --collection cargo --item "Survey Satellite" --at-least 6 --timeout 60
```

`station` is accepted as a friendly alias for the canonical `starbase` environment. An interaction wait completes only when the requested observed verb is executable, so `--verb Dock` waits until Dock is actually ready rather than merely visible. Mission-count waits accept the same `--name`, `--state`, and `--type` filters as `get missions`. Inventory waits count the same exact item name as `get inventory`; omitting `--collection` searches cargo.

Each wait returns immediately when the condition is already true, polls while the observed state is still changing, returns a normal command error if the slot disappears or the query is invalid, and returns exit code `6` when the timeout expires. The timeout is measured in seconds. All waits support `--json` and `--result-file`.

## Result files for older automation tools

Tools such as AC Tools can launch a process but cannot naturally wait for it or read its exit code. Add `--result-file` to any command:

```text
net7cmctl autopilot-to "Prasad Station" --slot "Client 1" --timeout 900 --result-file "C:\Macros\net7-result.txt"
```

The helper clears the file when the command starts. When the operation finishes, it atomically publishes three UTF-8 lines without a byte-order mark:

```text
0
arrived
Arrived at Prasad Station.
```

The fixed lines are:

1. numeric process exit code;
2. stable machine-readable result code;
3. human-readable message.

For a successful `--field` query, the third line is the scalar value itself. This gives AC Tools a direct branch value such as `starbase`, `true`, or `6` without JSON parsing.

A stopped journey can produce:

```text
7
autopilot_stopped
Auto Pilot stopped because warp was interrupted.
```

An AC Tools macro only needs to launch `net7cmctl.exe`, poll until the result file is non-empty, read the three lines, and branch on the numeric or symbolic result. No PowerShell wrapper or companion command file is needed.

## AutoHotkey example

```ahk
launchResult := RunWait(
    'net7cmctl.exe launch --slot "Alt 1" --wait --timeout 180',
    ,
    'Hide')

travelResult := RunWait(
    'net7cmctl.exe autopilot-to "Prasad Station" --slot "Alt 1" --timeout 900',
    ,
    'Hide')
```

Use the process exit code to distinguish success, unavailable state, rejection, timeout, and a known failed condition. Run `net7cmctl help` for the current command list and exit-code table.

## Exit codes

```text
0   Success
2   Invalid arguments
3   Slot, destination, or other requested item not found
4   Requested Client Manager state is currently unavailable
5   Existing Client Manager action rejected the request
6   Timed out
7   Waited operation reached a known unsuccessful terminal condition
10  Internal helper or control-plane error
```

## Administrator access

Net7 Client Manager currently runs elevated for its launcher and hosted-client responsibilities. The automation helper deliberately remains a normal user process. Client Manager exposes only the curated commands and read-only queries documented here through a same-user local pipe; it does not expose arbitrary process execution, file access, mouse input, keyboard input, or memory access.

Do not add `*RunAs` to AutoHotkey scripts merely to call `net7cmctl`. If Windows reports that the connection was denied, first confirm that Client Manager and `net7cmctl.exe` came from the same release and are running under the same signed-in Windows account.
