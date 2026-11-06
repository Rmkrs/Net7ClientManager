# Net7 Client Manager

![Build](https://github.com/Rmkrs/Net7ClientManager/actions/workflows/ci.yml/badge.svg)
![.NET](https://img.shields.io/badge/.NET-10.0-blue)
![Platform](https://img.shields.io/badge/Platform-Windows-lightgrey)
![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)

Net7 Client Manager is a Windows companion for Earth & Beyond / Net-7. It launches and hosts multiple game clients in reusable fleet layouts, then adds player-facing tools for navigation, galaxy search, shopping lists, pilot history, builds, social discovery, add-ons, and optional Forge contributions.

## User guide

The complete player documentation starts at:

**[Documentation/UserGuide/index.md](Documentation/UserGuide/index.md)**

## Highlights

- Profile-based client layouts with independent host and game resolutions.
- Saved accounts and pilots with optional automatic login and pilot entry.
- Quick launch, Create missing, Keep alive, and clean hosting of existing clients.
- In-game Client Manager, Command Palette, Action HUD, and Fleet Loot.
- Route Planner, Galaxy Atlas, Auto Pilot support, mission help, and Jobs Terminal routes.
- Galaxy Finder, rich item details, shopping lists, recipes, and vendor companion.
- Pilot Archive with cargo, equipment, vault, skills, missions, reputations, and optional histories.
- Equipment-and-skill Builds with ownership checks and immutable Forge versions.
- Presence, Looking for Guild, and Guild Recruitment.
- Addon Center for discovery, per-client enablement, updates, activity, and development.
- Optional, category-controlled Net7 Forge contributions and shared galaxy-data updates.
- Cross-pilot Earth & Beyond Game Settings editor.
- `net7cmctl` local automation commands and live-state queries for AutoHotkey and scripts.

## Add-on creators

Technical add-on documentation is kept separately in [Documentation/Lua-Addon-Guide.md](Documentation/Lua-Addon-Guide.md).

## Development

The application is a .NET 10 Windows desktop application. Build the solution with:

```powershell
dotnet build Net7ClientManager.slnx
```

The account-sync regression suite runs offline using synthetic accounts and temporary storage:

```powershell
dotnet test Tests/Net7ClientManager.Tests -c Release
```

It covers malformed API responses, account/profile transfers, protected sessions and the sync dialog's preview state. It does not sign in to Net-7 or modify your saved accounts.

## License

MIT

### Forge identity and recovery

Open **Forge Contributions → Connect / recover through Net-7** to create your first Forge identity. Sign in to Net-7, select a game account for recovery, and save the issued Forge recovery code separately (for example in a password manager).

On another installation, choose **Recover using saved code** and verify the same game account through Net-7. Both are required. Successful recovery issues a replacement code; save that one instead. Existing installations can use **Manage Net-7 recovery** to link an account or replace the recovery code. Publishing continues to use the existing local Forge credential and does not require repeated Net-7 sign-ins.

Your password goes only to Net-7. Forge temporarily receives the session to verify your selected account and does not store it. The recovery code is shown once and is not kept in Client Manager settings. Legacy identities and lost-code cases can still use manual recovery. The new connection/recovery option becomes available when Forge supports it. Client and server can be updated in either order: older servers retain automatic registration and manual recovery, while updated servers continue supporting released clients without an upgrade deadline.
