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

## Add-on creators

Technical add-on documentation is kept separately in [Documentation/Lua-Addon-Guide.md](Documentation/Lua-Addon-Guide.md).

## Development

The application is a .NET 10 Windows desktop application. Build the solution with:

```powershell
dotnet build Net7ClientManager.slnx
```

## License

MIT
