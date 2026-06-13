# Net7 Client Manager
![Build](https://github.com/Rmkrs/Net7ClientManager/actions/workflows/ci.yml/badge.svg)
![.NET](https://img.shields.io/badge/.NET-10.0-blue)
![Platform](https://img.shields.io/badge/Platform-Windows-lightgrey)
![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)

Net7 Client Manager is a small Windows companion tool for players running multiple Earth & Beyond / Net-7 game clients. It automatically detects game clients, hosts their windows inside one management surface, and applies profile-based layouts so your fleet of clients stays tidy instead of forming a feral window-swarm across your monitors.

The tool is built for practical multiboxing comfort: launch clients, let the manager gobble them up, assign them to profile slots, and keep the layout consistent every time.

## Current status

The current version focuses on:

- Detecting and hosting running game clients.
- Managing one active profile at all times.
- Assigning hosted clients to profile slots.
- Keeping profile layouts as first-class templates.
- Switching profiles and immediately moving hosted clients into the selected layout.
- Supporting unassigned hosted clients when there are more clients than available slots.
- Automatically assigning unassigned clients when new slots become available.
- Editing slots visually through the layout designer.
- Using preset slot resolutions instead of arbitrary resize handles.
- Showing running clients as compact cards.
- Warning about unsafe negative-X slot positions, because the game can lose input when hosted on a left-side monitor with negative coordinates.

## Why this exists

Earth & Beyond / Net-7 clients are old-school Windows creatures. They work, but they were not designed for smooth modern multi-client management.

Running multiple clients usually means manually moving, resizing, identifying, and babysitting windows. Net7 Client Manager turns that into a repeatable profile-driven workflow.

Instead of treating window placement as something you fix every session, the manager treats layouts as reusable templates.

## Core concepts

### Profiles

A profile is a saved layout template.

The app always has exactly one active profile. If no settings exist yet, it creates an empty `Default` profile and opens the main screen.
Switching profiles immediately reassigns and moves currently hosted clients according to the selected profile.

### Slots

A slot is a target position and resolution inside a profile.

Slots can have:

- A name.
- An optional account/login name.
- An auto-login flag for future login-assist work.
- A resolution preset.
- X/Y placement.

Resolution presets own the slot size. The tool intentionally avoids freeform resize handles because the game behaves best at known/native-ish resolutions.

### Hosted clients

Detected game clients are automatically embedded into the manager.

If free slots exist, new clients are assigned to the next available slot. If there are more clients than slots, extra clients remain hosted but unassigned.

When a new slot is added and an unassigned hosted client exists, the client is assigned immediately.

Deleting an assigned slot makes that client unassigned instead of losing track of it.

## Current UI shape

The main screen contains:

- A top area for profile actions.
- A visual layout designer canvas.
- A selected-slot inspector.
- A running-clients card panel.

The selected-slot inspector currently supports editing:

- Slot name.
- Account/login name.
- Auto-login flag.
- Resolution preset.
- X/Y position.

The running-clients panel uses cards rather than a dense table. Cards show the assigned slot name, account if known, and muted technical details such as docked state and process ID.

## Important behavior notes

### Closing hosted clients

Closing the hosted game form should kill the underlying game client process cleanly. The tool should not leave `client.exe` running windowless in the background.


### Negative monitor coordinates

The Earth & Beyond game client can stop accepting input correctly when hosted at negative X coordinates, such as on a monitor positioned to the left of the primary display.

Net7 Client Manager should allow advanced users to place slots there if they insist, but it should clearly warn that negative-X slots or monitor regions are unsafe / not recommended.

The warning should primarily key off:

```text
slot.Bounds.Left < 0
monitor.RealBounds.Left < 0
```

Slightly negative top coordinates are not considered the same class of problem.

## Planned next features

### 1. Auto-fill login/account name

The next goal is to auto-fill the configured account/login name into the actual game client once it is hosted and ready.

This should happen after the real game client exists. The manager should not automate the launcher consent, Play, or TOS flow before the game window appears.

### 2. Secure password storage and login assist

A later goal is optional secure password handling and fuller login assistance:

- Store passwords securely.
- Auto-fill the password field.
- Click Accept/Login when safe and reliable.

This should be treated carefully. Credential storage and input automation need to be boring, explicit, and dependable rather than clever.

## Development notes

The app is a normal Windows desktop application. It minimizes to the taskbar and exits when closed.

The codebase has been divided into partial classes around the main form and related UI areas, including profile actions, slot editor/actions, top panel pieces, combo item helpers, and the layout designer control.

## License

MIT