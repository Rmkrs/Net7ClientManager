// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

internal static class HelpArticleCatalog
{
    public static IReadOnlyList<HelpArticle> Create()
    {
        return
        [
            new HelpArticle(
                HelpTopicIds.Home,
                "Help & Assistance",
                "YOUR CO-PILOT",
                "Discover what Client Manager can do, or let it inspect your setup and guide you to the next fix.",
                [],
                [
                    new HelpArticleSection(
                        "Help that knows where you are",
                        "Open help from a screen's ? button and Client Manager can introduce the controls on that screen. Setup checks use your real configuration instead of asking you to diagnose it first."),
                    new HelpArticleSection(
                        "Built for action",
                        "Clickable help opens the real feature, focuses the useful area, and walks through it with Previous and Next. You can close an introduction at any time."),
                ],
                ["help", "assistance", "features", "getting started"]),

            new HelpArticle(
                HelpTopicIds.AutoLogin,
                "Automatic login",
                "START IN SPACE, NOT IN SETTINGS",
                "Launch a client, sign in, and enter the chosen character without repeating the login ritual every time.",
                [
                    "Stores account details securely for your Windows user",
                    "Assigns an account and character to each client slot",
                    "Checks every requirement and guides you to missing setup",
                ],
                [
                    new HelpArticleSection(
                        "The easy path",
                        "Use Check automatic login setup on the main screen. It completes the account first, then the client slot, and stops at the first missing requirement."),
                    new HelpArticleSection(
                        "Multiple clients",
                        "Each slot can use its own account, character, window position, game resolution, and automation choices."),
                ],
                ["login", "password", "character", "account", "automatic", "slot"],
                Featured: true,
                DiagnosticAction: "diagnose:auto-login",
                DiagnosticText: "Check automatic login",
                OpenAction: "open:auto-login",
                OpenActionText: "Open the readiness assistant"),

            new HelpArticle(
                HelpTopicIds.Fleet,
                "Run your fleet",
                "ONE COCKPIT, MANY CLIENTS",
                "Turn one game window or a whole multibox team into a repeatable layout you can launch again in seconds.",
                [
                    "Launch one client, or create every missing client in the selected profile",
                    "Place client windows visually across your monitors, including always-visible and hover title bars",
                    "Keep the fleet alive and replace a client that closes unexpectedly",
                ],
                [
                    new HelpArticleSection(
                        "Profiles",
                        "A profile is one saved fleet arrangement. Keep separate profiles for solo play, a combat group, trade runs, or any other combination you want to launch together."),
                    new HelpArticleSection(
                        "Client slots",
                        "Each slot remembers its account, character, automatic-login choices, monitor position, hosted window size, game resolution, and title-bar mode. The Layout Editor reserves extra space only for an always-visible title bar, so rows do not overlap."),
                    new HelpArticleSection(
                        "Macro compatibility",
                        "Choose Always show for normal use, Hide for strict macro compatibility, or Show on hover to keep Earth & Beyond at window coordinate 0,0 while still revealing the title bar after the pointer rests at the top edge. The hover delay prevents brief visits to the gutter from covering game controls."),
                ],
                ["fleet", "profile", "slot", "layout", "window", "multibox", "title bar", "hover", "macro", "autohotkey", "ahk"],
                Featured: true,
                OpenAction: "show:fleet-tour",
                OpenActionText: "Tour the main screen"),

            new HelpArticle(
                HelpTopicIds.Navigation,
                "Navigation & Auto Pilot",
                "TURN THE GALAXY INTO A ROUTE",
                "Plan journeys from the live pilot location, set destinations from across Client Manager, and let Auto Pilot handle supported travel steps.",
                [
                    "Live route planning across systems, sectors, gates, and wormholes",
                    "Set destinations from the Atlas, Finder, missions, vendors, mobs, and resources",
                    "Guide a fleet while followers recover and rejoin",
                ],
                [
                    new HelpArticleSection(
                        "Route Planner",
                        "Search for a destination, filter by type, preview every hop and warning, then send the route to the selected hosted pilot."),
                    new HelpArticleSection(
                        "Group wormholes",
                        "Routes combine the learned Create Wormhole and Extended Wormhole destinations of every managed pilot in the in-game group, regardless of who leads. Put each usable wormhole skill on any normal or alternate shortcut slot; Auto Pilot finds the caster and shortcut, selects the required destination, and accepts the trip for the managed fleet."),
                    new HelpArticleSection(
                        "Pop out or show in game",
                        "Open Navigation from the in-game Client Manager menu. Use Pop out to move the same route into a desktop companion beside or below the game, or onto another monitor. Use Show in game to return it to a movable window over Earth & Beyond. Route and Auto Pilot state continue unchanged."),
                    new HelpArticleSection(
                        "Built in",
                        "Both presentations are part of Client Manager and share the same route controls. The retired Navigation HUD addon is uninstalled automatically during upgrade, so players do not need to install or manage a separate package."),
                    new HelpArticleSection(
                        "Auto Pilot",
                        "Auto Pilot acts on the current route and supports a limited unattended run. Route warnings and follower recovery remain visible so you know when intervention is needed."),
                ],
                ["navigation", "route", "autopilot", "hud", "companion", "pop out", "destination", "wormhole"],
                Featured: true,
                DiagnosticAction: "diagnose:navigation",
                DiagnosticText: "Open Navigation",
                OpenAction: "open:navigation",
                OpenActionText: "Tour the Route Planner"),

            new HelpArticle(
                HelpTopicIds.GalaxyAtlas,
                "Galaxy Atlas",
                "THE WHOLE GALAXY, ON YOUR DESK",
                "Explore the galaxy visually, inspect anything under the cursor, follow pilots, and turn a click on the map into a route.",
                [
                    "Search systems, sectors, stations, planets, navigation objects, and visible pilots",
                    "Hover for details and click gates or destinations to inspect and act",
                    "Show your pilot, group, social pilots, encounters, resource fields, and gravity wells",
                ],
                [
                    new HelpArticleSection(
                        "Explore",
                        "Drag to pan and use the mouse wheel to zoom. Hovering reveals more information about the object under the cursor. Clickable gates and destinations let you inspect where they lead."),
                    new HelpArticleSection(
                        "Act",
                        "Search or click a destination to set a route for the selected pilot. Current location returns to your pilot, Back retraces your map visits, and the filters along the bottom decide which layers are visible."),
                ],
                ["atlas", "map", "galaxy", "system", "sector", "planet", "gate", "layers"],
                Featured: true,
                OpenAction: "open:atlas",
                OpenActionText: "Tour the Galaxy Atlas"),

            new HelpArticle(
                HelpTopicIds.GalaxyFinder,
                "Galaxy Finder",
                "ASK THE GALAXY A BETTER QUESTION",
                "Find places, NPCs, mobs, harvestables, equipment, components, recipes, and the real sources that lead to them.",
                [
                    "Purpose-built result columns for every search scope",
                    "Item details with effects, vendors, loot, refining, crafting, and mission sources",
                    "Set destinations directly from useful places, vendors, mobs, and fields",
                ],
                [
                    new HelpArticleSection(
                        "Search broadly",
                        "Choose Places, NPCs, Mobs, Harvestables, or Items. Item searches can be narrowed by category, effect, and source without losing the details that matter."),
                    new HelpArticleSection(
                        "Follow the source",
                        "Item pages name actual vendors, mobs, resource fields, recipes, and refining paths. Hover item identity cells for rich details, then route the selected pilot to a useful source."),
                ],
                ["finder", "item", "npc", "mob", "harvestable", "vendor", "loot"],
                Featured: true,
                OpenAction: "open:finder",
                OpenActionText: "Tour Galaxy Finder"),

            new HelpArticle(
                HelpTopicIds.ShoppingLists,
                "Shopping lists",
                "FROM WISH LIST TO FLIGHT PLAN",
                "Turn requested equipment, ammunition, or components into a practical plan for buying, looting, harvesting, refining, and manufacturing.",
                [
                    "Expands recipes into every required ingredient",
                    "Counts useful materials already owned by the selected pilot",
                    "Shows vendor opportunities while you are docked",
                ],
                [
                    new HelpArticleSection(
                        "Requested outputs",
                        "Quantities mean what you want to obtain in addition to what you already own. Existing inventory reduces ingredient needs rather than silently completing the request."),
                    new HelpArticleSection(
                        "Vendor companion",
                        "When a vendor sells something useful for the active list, a compact companion appears above the vendor tabs and updates as purchases are made."),
                ],
                ["shopping", "recipe", "manufacture", "ingredient", "vendor", "ammo"],
                Featured: true,
                OpenAction: "open:shopping",
                OpenActionText: "Tour Shopping Lists"),

            new HelpArticle(
                HelpTopicIds.Builds,
                "Builds",
                "A BUILD GUIDE THAT LIVES WITH YOUR PILOT",
                "Find a community guide, see exactly what the pilot can already use, and follow equipment and skill milestones as the character grows.",
                [
                    "Search the Forge for builds by purpose, name, notes, publisher, or popularity",
                    "See equipped, cargo, vault, and missing items at a glance",
                    "Compare current skills and levels with every milestone in the guide",
                ],
                [
                    new HelpArticleSection(
                        "Choose a guide",
                        "Open Forge from the Build Board, preview a published build, and use the version you choose. Stars and update notices help you discover alternatives without replacing your active guide automatically."),
                    new HelpArticleSection(
                        "Follow it while playing",
                        "The Build Board shows required levels, equipment, and skills together. Hover items and skills for details and prerequisites. Small companions beside Equip, Vault, and Skills keep the relevant part of the guide beside the game screen."),
                ],
                ["build", "skills", "equipment", "guide", "forge", "milestone", "missing"],
                Featured: true,
                OpenAction: "open:builds",
                OpenActionText: "Open and tour Builds"),

            new HelpArticle(
                HelpTopicIds.PilotArchive,
                "Pilot Archive",
                "YOUR PILOTS, EVEN WHEN THEY ARE OFFLINE",
                "Keep the latest known state of every pilot and turn fleeting missions, loot, travel, combat, and reputation changes into histories you can inspect later.",
                [
                    "Docking refreshes cargo, equipment, vault, credits, skills, missions, reputation, location, and guild details",
                    "Loot, travel, credits, missions, combat, and reputation changes become searchable histories",
                    "Launch the correct account, slot, and character directly from the archive",
                ],
                [
                    new HelpArticleSection(
                        "What gets remembered",
                        "When a pilot enters a station, Client Manager records the station snapshot, including cargo, equipped items, vault, credits, skills, missions, reputation, location, and guild. The latest known state remains after the game closes."),
                    new HelpArticleSection(
                        "What becomes history",
                        "Mission updates build a mission history. Looting, travelling, gaining or spending credits, and other actions build an activity history. Combat records encounters, kills, damage, and rewards for later review."),
                    new HelpArticleSection(
                        "Search every pilot",
                        "Search for an item, skill, mission, reputation, guild, or location, then jump directly to the matching pilot and section."),
                ],
                ["pilot", "archive", "inventory", "vault", "history", "combat", "mission", "loot"],
                Featured: true,
                OpenAction: "open:archive",
                OpenActionText: "Tour Pilot Archive"),

            new HelpArticle(
                HelpTopicIds.Addons,
                "Addons",
                "MAKE CLIENT MANAGER YOURS",
                "Install official and community additions, enable them per client slot, and keep each pilot's in-game workspace independent.",
                [
                    "Discover, install, update, enable, and inspect addons",
                    "Per-slot enablement and remembered window placement",
                    "Built-in development tools for Lua addon authors",
                ],
                [
                    new HelpArticleSection(
                        "Installed is not the same as enabled",
                        "An addon package is installed once, then enabled separately for the client slots that should use it."),
                    new HelpArticleSection(
                        "Visible controls",
                        "Some addons register windows or Show options in the in-game Client Manager menu. A running addon can still be hidden until that option is checked."),
                ],
                ["addon", "install", "enable", "lua", "package", "update"],
                Featured: true,
                DiagnosticAction: "diagnose:addons",
                DiagnosticText: "Check addons for this client",
                OpenAction: "open:addons",
                OpenActionText: "Open Addon Center"),

            new HelpArticle(
                HelpTopicIds.ForgeContributions,
                "Forge Contributions",
                "HELP THE SHARED GALAXY GROW",
                "Choose whether Client Manager shares supported discoveries with Net7 Forge, control what is public, and keep the shared world data current.",
                [
                    "Contribution is optional, off by default, and controlled by category",
                    "Account names, login details, saved accounts, and machine information are never shared",
                    "Shared discoveries improve the world data used by Client Manager",
                ],
                [
                    new HelpArticleSection(
                        "Share only what you choose",
                        "Enable categories such as NPCs, navigation objects, station services, vendors, mobs, loot, resources, recipes, missions, and Job Terminal offers. Leave any category off when you do not want to contribute it."),
                    new HelpArticleSection(
                        "Choose public attribution",
                        "Contributions can appear anonymously or with the pilot name that discovered them. Forge still keeps private abuse-protection information separate from what the community sees."),
                    new HelpArticleSection(
                        "Receive newer shared world data",
                        "Client Manager checks for newer Forge world-data revisions and activates them when it is safe. Routes, Atlas data, and contribution coverage then benefit from discoveries shared by the community."),
                    new HelpArticleSection(
                        "See what this installation has contributed",
                        "The overview shows whether contribution is active, which world-data revision is installed, and summaries for the current session and the lifetime of this installation."),
                ],
                ["forge", "contributions", "world data", "community", "privacy", "attribution", "revision"],
                Featured: true,
                OpenAction: "open:forge-contributions",
                OpenActionText: "Open and tour Forge Contributions"),

            new HelpArticle(
                HelpTopicIds.Social,
                "Social",
                "FIND PEOPLE WITHOUT GIVING UP CONTROL",
                "Choose whether a pilot is visible, find other players, look for a guild, or advertise recruitment with the details that matter.",
                [
                    "Publish presence and choose whether others see no location, a sector, a nearby nav, or the exact position",
                    "Browse pilots who are looking for a guild by play style, language, region, and availability",
                    "Publish a guild recruitment listing with active times, needs, requirements, and contact details",
                ],
                [
                    new HelpArticleSection(
                        "Presence and the Atlas",
                        "Visible pilots can appear in the Galaxy Atlas Social layer. You decide whether the location is hidden, shown only as a sector, placed near a navigation point, or shared exactly."),
                    new HelpArticleSection(
                        "Looking for Guild and Recruitment",
                        "Players can describe what they want from a guild, while guilds can describe what they offer and who they need. Search the public lists without leaving Client Manager."),
                ],
                ["social", "presence", "guild", "recruitment", "privacy", "atlas"],
                OpenAction: "open:social",
                OpenActionText: "Open and tour Social"),

            new HelpArticle(
                HelpTopicIds.InGameTools,
                "In-game tools",
                "CLIENT MANAGER, WITHOUT LEAVING THE GAME",
                "Open the Client Manager menu inside a hosted game window to reach major features, addon windows, visibility controls, and per-game helpers.",
                [
                    "Open Atlas, Finder, Social, Pilot Archive, Builds, Forge Contributions, and Addon Center from one in-game menu",
                    "Use the Command Palette and Action HUD for fast keyboard and on-screen actions",
                    "Open Mission Wiki in game or beside the game, with a live mission list, current objective, and observed mission history",
                ],
                [
                    new HelpArticleSection(
                        "The Client Manager menu",
                        "The small Client Manager tab belongs to the hosted game window. Open it to launch the major features and toggle addon windows without returning to the desktop dashboard."),
                    new HelpArticleSection(
                        "Mission Wiki in game or beside it",
                        "Enable Mission Wiki in In-game Options. The in-game reader follows the mission selected in Earth & Beyond. Pop it out for a resizable desktop companion with the pilot's mission list, current objective, observed mission steps, Wiki guidance, and Set destination actions."),
                    new HelpArticleSection(
                        "Options for the active game experience",
                        "In-game Options controls the menu, Command Palette, history recording, Mission Wiki, Finder behaviour, vendor companion, and enhanced item tooltips."),
                ],
                ["ingame", "menu", "command palette", "tooltip", "mission wiki", "options"],
                OpenAction: "show:ingame-menu",
                OpenActionText: "Show the in-game menu"),

            new HelpArticle(
                HelpTopicIds.GameSettings,
                "Game settings",
                "ONE PLACE TO TUNE THE WHOLE FLEET",
                "Compare settings across every pilot, change one character, or synchronize a choice across the offline fleet without logging each character in and out.",
                [
                    "Compare Camera, Interface, Graphics, Sound, Privacy, and Chat Font settings across pilots",
                    "Edit one pilot directly, or right-click a value to apply it to every offline pilot",
                    "Copy complete chat-font setups between pilots and create missing resolution records",
                ],
                [
                    new HelpArticleSection(
                        "Stop remembering which character was configured",
                        "Earth & Beyond stores many settings per pilot. Client Manager places those pilots side by side, so you can see who differs and synchronize common choices without repeated login, character selection, logout, and note-taking."),
                    new HelpArticleSection(
                        "Safe fleet-wide changes",
                        "Right-click a setting to copy the chosen value to every offline pilot. Online pilots are left alone until they are safely offline. Changes made here save automatically."),
                ],
                ["settings", "resolution", "chat", "font", "graphics", "fleet", "synchronize"],
                OpenAction: "open:game-settings",
                OpenActionText: "Open and tour Game Settings"),
        ];
    }
}
