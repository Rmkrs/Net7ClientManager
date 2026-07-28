namespace Net7ClientManager.Addons.Development;

using System.Text;

public static partial class AddonApiCatalog
{
    private static readonly string[] luaKeywords =
    [
        "and", "break", "do", "else", "elseif", "end", "false", "for",
        "function", "goto", "if", "in", "local", "nil", "not", "or",
        "repeat", "return", "then", "true", "until", "while",
    ];

    private static readonly IReadOnlyList<AddonApiEventDefinition>
        eventDefinitions = BuildEventDefinitions();

    private static readonly string[] eventNames =
    [
        .. eventDefinitions.Select(definition => definition.Name),
    ];

    private static readonly IReadOnlyList<AddonApiSymbol> symbols =
        BuildSymbols();

    public static IReadOnlyList<AddonApiSymbol> Symbols => symbols;

    public static IReadOnlyList<string> LuaKeywords => luaKeywords;

    public static IReadOnlyList<string> EventNames => eventNames;

    public static IReadOnlyList<AddonApiEventDefinition> EventDefinitions =>
        eventDefinitions;

    public static IReadOnlyList<AddonApiSymbol> GetChildren(string parentPath)
    {
        return symbols
            .Where(symbol => string.Equals(
                symbol.ParentPath,
                parentPath,
                StringComparison.Ordinal))
            .OrderBy(symbol => symbol.Name, StringComparer.Ordinal)
            .ToArray();
    }

    public static AddonApiSymbol? Find(string path)
    {
        return symbols.FirstOrDefault(symbol => string.Equals(
            symbol.Path,
            path,
            StringComparison.Ordinal));
    }

    public static string GenerateEmmyLuaStub()
    {
        var apiSymbols = symbols
            .Where(IsNet7ApiSymbol)
            .Where(symbol => !symbol.Path.Contains(
                "[]",
                StringComparison.Ordinal))
            .ToArray();
        StringBuilder builder = new();
        builder.AppendLine("---@meta Net7ClientManager");
        builder.AppendLine("-- Generated from the Net7 editor API metadata catalog. Do not edit by hand.");
        builder.AppendLine("-- The declarations live in an unreachable block, so accidentally requiring this file has no runtime side effects.");
        builder.AppendLine("-- Array item shapes are documented in the generated Markdown reference; pseudo-paths containing [] are omitted here to keep this Lua stub syntactically valid.");
        builder.AppendLine();
        builder.AppendLine("if false then");

        foreach (var symbol in apiSymbols
                     .Where(symbol => symbol.Kind == AddonApiSymbolKind.Table)
                     .OrderBy(symbol => symbol.Path.Count(character => character == '.'))
                     .ThenBy(symbol => symbol.Path, StringComparer.Ordinal))
        {
            AppendLuaDescription(builder, symbol.Description);
            builder.Append("    ---@type table<string, any>")
                .AppendLine();
            builder.Append("    ")
                .Append(symbol.Path)
                .AppendLine(" = {}");
        }

        if (apiSymbols.Any(symbol => symbol.Kind == AddonApiSymbolKind.Table))
        {
            builder.AppendLine();
        }

        foreach (var symbol in apiSymbols.Where(symbol =>
                     symbol.Kind == AddonApiSymbolKind.Field))
        {
            AppendLuaDescription(builder, symbol.Description);
            builder.Append("    ---@type ")
                .AppendLine(string.IsNullOrWhiteSpace(symbol.ReturnType)
                    ? "any"
                    : symbol.ReturnType);
            builder.Append("    ")
                .Append(symbol.Path)
                .AppendLine(" = nil");
        }

        if (apiSymbols.Any(symbol => symbol.Kind == AddonApiSymbolKind.Field))
        {
            builder.AppendLine();
        }

        foreach (var symbol in apiSymbols.Where(symbol =>
                     symbol.Kind == AddonApiSymbolKind.Function))
        {
            AppendLuaDescription(builder, symbol.Description);
            foreach (var parameter in symbol.Parameters)
            {
                builder.Append("    ---@param ")
                    .Append(parameter.Name)
                    .Append(' ')
                    .Append(parameter.Type);

                if (!string.IsNullOrWhiteSpace(parameter.Description))
                {
                    builder.Append(' ').Append(parameter.Description);
                }

                builder.AppendLine();
            }

            if (!string.IsNullOrWhiteSpace(symbol.ReturnType))
            {
                builder.Append("    ---@return ")
                    .AppendLine(symbol.ReturnType);
            }

            builder.Append("    ")
                .Append(symbol.Path)
                .Append(" = function(")
                .Append(string.Join(", ", symbol.Parameters.Select(
                    parameter => parameter.Name)))
                .AppendLine(") end");
            builder.AppendLine();
        }

        builder.AppendLine("end");
        return builder.ToString();
    }

    public static string GenerateMarkdownReference()
    {
        var apiSymbols = symbols.Where(IsNet7ApiSymbol).ToArray();
        StringBuilder builder = new();
        builder.AppendLine("# Net7 Addon API");
        builder.AppendLine();
        builder.AppendLine(
            "The Net7 addon API gives each addon a safe, read-only view of " +
            "one Earth & Beyond game client. It exposes game concepts such " +
            "as pilots, ships, targets, inventories, missions, routes and " +
            "panels without exposing the internal machinery Client Manager " +
            "uses to observe the game.");
        builder.AppendLine();
        builder.AppendLine("## Mental model");
        builder.AppendLine();
        builder.AppendLine(
            "- **One addon runs separately for each game client.** `game` is " +
            "that client's latest game state, never a list of every client.");
        builder.AppendLine(
            "- **Game-state tables are read-only and regularly refreshed.** Read the current " +
            "value when handling an event instead of keeping table references " +
            "forever.");
        builder.AppendLine(
            "- **`available` means the game currently exposes enough state to " +
            "trust that domain.** Missing values are `nil`; they are not zero.");
        builder.AppendLine(
            "- **Times are Unix milliseconds.** Convert them only when a " +
            "human-readable time is needed.");
        builder.AppendLine(
            "- **Coordinates use Earth & Beyond game-world units.** Position is " +
            "available in `game`, but ordinary movement deliberately emits " +
            "no position event.");
        builder.AppendLine(
            "- **Actions require a button click.** Addons cannot control the " +
            "game or Client Manager from timers or background events.");
        builder.AppendLine();
        builder.AppendLine("## First script");
        builder.AppendLine();
        builder.AppendLine("```lua");
        builder.AppendLine("local function show_location()");
        builder.AppendLine("    if not game.world.available then");
        builder.AppendLine("        addon.log.info(\"Location is not available yet.\")");
        builder.AppendLine("        return");
        builder.AppendLine("    end");
        builder.AppendLine();
        builder.AppendLine("    local spatial = game.character.spatial");
        builder.AppendLine("    if not spatial.available or spatial.position == nil then");
        builder.AppendLine("        return");
        builder.AppendLine("    end");
        builder.AppendLine();
        builder.AppendLine("    addon.log.info(string.format(");
        builder.AppendLine("        \"%s / %s at %.1f, %.1f, %.1f\",");
        builder.AppendLine("        game.world.system_name or \"Unknown system\",");
        builder.AppendLine("        game.world.sector_name or \"Unknown sector\",");
        builder.AppendLine("        spatial.position.x,");
        builder.AppendLine("        spatial.position.y,");
        builder.AppendLine("        spatial.position.z))");
        builder.AppendLine("end");
        builder.AppendLine();
        builder.AppendLine("addon.on_load(show_location)");
        builder.AppendLine("game.events.on(\"world.location_changed\", show_location)");
        builder.AppendLine("```");
        builder.AppendLine();
        builder.AppendLine("## Working with events");
        builder.AppendLine();
        builder.AppendLine(
            "Every callback receives a read-only event table with `name` and " +
            "`observed_at`, followed by the fields documented below. Events " +
            "describe meaningful transitions; the current complete state " +
            "remains available through `game`.");
        builder.AppendLine();
        builder.AppendLine("```lua");
        builder.AppendLine("game.events.on(\"character.credits_gained\", function(event)");
        builder.AppendLine("    addon.log.info(string.format(");
        builder.AppendLine("        \"Received %d credits; balance is now %d.\",");
        builder.AppendLine("        event.amount,");
        builder.AppendLine("        event.current))");
        builder.AppendLine("end)");
        builder.AppendLine("```");
        builder.AppendLine();
        builder.AppendLine("## API tables and functions");
        builder.AppendLine();

        foreach (var group in apiSymbols
                     .Where(symbol => symbol.Kind != AddonApiSymbolKind.Keyword)
                     .GroupBy(symbol => symbol.Path.Split('.')[0])
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            builder.Append("### ").AppendLine(group.Key);
            builder.AppendLine();

            foreach (var symbol in group.OrderBy(
                         symbol => symbol.Path,
                         StringComparer.Ordinal))
            {
                builder.Append("#### `")
                    .Append(symbol.Path)
                    .AppendLine("`");
                builder.AppendLine();

                if (!string.IsNullOrWhiteSpace(symbol.Signature))
                {
                    builder.Append("```lua\n")
                        .Append(symbol.Signature)
                        .AppendLine("\n```");
                    builder.AppendLine();
                }

                builder.AppendLine(symbol.Description);
                builder.AppendLine();
            }
        }

        builder.AppendLine("## Events");
        builder.AppendLine();

        foreach (var definition in eventDefinitions)
        {
            builder.Append("### `")
                .Append(definition.Name)
                .AppendLine("`");
            builder.AppendLine();
            builder.AppendLine(definition.Summary);
            builder.AppendLine();
            builder.Append("**Fields:** ")
                .AppendLine(definition.Payload);

            if (!string.IsNullOrWhiteSpace(definition.Notes))
            {
                builder.AppendLine();
                builder.Append("**Notes:** ")
                    .AppendLine(definition.Notes);
            }

            builder.AppendLine();
            builder.AppendLine("```lua");
            builder.Append("game.events.on(\"")
                .Append(definition.Name)
                .AppendLine("\", function(event)");
            builder.AppendLine("    -- React to the documented event fields.");
            builder.AppendLine("end)");
            builder.AppendLine("```");
            builder.AppendLine();
        }

        builder.AppendLine("## Privacy and safety boundaries");
        builder.AppendLine();
        builder.AppendLine(
            "The documented Lua API omits account credentials, process identifiers, " +
            "game-internal numbers, item template numbers, internal faction or " +
            "sector keys, memory details, control internals and another game " +
            "client's state. Social online/offline events contain no position, " +
            "sector, station or nearest-navigation data.");
        builder.AppendLine();
        builder.AppendLine(
            "Position can be read for the current pilot because " +
            "many HUD and navigation addons need it. Continuous position " +
            "changes do not generate events, preventing the event stream from " +
            "becoming a movement tracker.");
        builder.AppendLine();
        builder.AppendLine("## Compatibility");
        builder.AppendLine();
        builder.AppendLine(
            "The v1 API is intentionally player-facing. A few older values " +
            "remain only so the bundled v1 addons keep working. The editor does " +
            "not show them, and new addons should use only the fields and " +
            "actions documented here.");

        return builder.ToString();
    }

    private static void AppendLuaDescription(
        StringBuilder builder,
        string description)
    {
        foreach (var line in description
                     .Replace("\r\n", "\n", StringComparison.Ordinal)
                     .Split('\n'))
        {
            builder.Append("    --- ")
                .AppendLine(line.Trim());
        }
    }

    private static bool IsNet7ApiSymbol(AddonApiSymbol symbol)
    {
        return symbol.Path is "addon" or "ui" or "actions" or "game" ||
               symbol.Path.StartsWith("addon.", StringComparison.Ordinal) ||
               symbol.Path.StartsWith("ui.", StringComparison.Ordinal) ||
               symbol.Path.StartsWith("actions.", StringComparison.Ordinal) ||
               symbol.Path.StartsWith("game.", StringComparison.Ordinal);
    }

    private static IReadOnlyList<AddonApiSymbol> BuildSymbols()
    {
        List<AddonApiSymbol> result = [];

        result.AddRange(luaKeywords.Select(keyword => new AddonApiSymbol
        {
            Path = keyword,
            Kind = AddonApiSymbolKind.Keyword,
            Description = "Lua 5.2 keyword.",
        }));

        AddTable(result, "addon", "Metadata, lifecycle, logging, storage and menu API for the current addon.");
        AddField(result, "addon.id", "string", "The addon name used in addon.json and for its private storage.");
        AddField(result, "addon.name", "string", "Display name from addon.json.");
        AddField(result, "addon.version", "string", "Addon version from addon.json.");
        AddField(result, "addon.api_version", "integer", "Net7 addon API version.");
        AddFunction(result, "addon.on_load", "addon.on_load(callback)", "Register a callback invoked after the addon source loads.", "nil", Param("callback", "fun()"));
        AddFunction(result, "addon.on_unload", "addon.on_unload(callback)", "Register a callback invoked before the addon stops or reloads.", "nil", Param("callback", "fun()"));
        AddTable(result, "addon.log", "Structured addon logging.");
        AddFunction(result, "addon.log.info", "addon.log.info(message)", "Write an informational activity entry.", "nil", Param("message", "any"));
        AddFunction(result, "addon.log.warn", "addon.log.warn(message)", "Write a warning activity entry.", "nil", Param("message", "any"));
        AddFunction(result, "addon.log.error", "addon.log.error(message)", "Write an error activity entry.", "nil", Param("message", "any"));
        AddTable(result, "addon.storage", "Persistent storage kept separately for this addon and game client.");
        AddField(result, "addon.storage.quota_bytes", "integer", "Maximum persisted document size.");
        AddField(result, "addon.storage.scope", "string", "How saved values are separated. The current value is owner, meaning each game client has its own saved values.");
        AddFunction(result, "addon.storage.get", "addon.storage.get(key)", "Read a stored value. Returns nil when missing.", "any|nil, string|nil", Param("key", "string"));
        AddFunction(result, "addon.storage.set", "addon.storage.set(key, value)", "Persist a JSON-compatible value.", "boolean, string|nil", Param("key", "string"), Param("value", "any"));
        AddFunction(result, "addon.storage.remove", "addon.storage.remove(key)", "Remove one stored value.", "boolean, string|nil", Param("key", "string"));
        AddFunction(result, "addon.storage.clear", "addon.storage.clear()", "Remove every stored value for this addon on the current game client.", "boolean, string|nil");
        AddTable(result, "addon.menu", "Add windows and options to the Client Manager Addons menu.");
        AddFunction(result, "addon.menu.register_window", "addon.menu.register_window(widget_id, definition)", "Expose an existing addon window as the Show option in the addon section of the Client Manager menu.", "boolean, string|nil", Param("widget_id", "string"), Param("definition", "table"));
        AddFunction(result, "addon.menu.register_toggle", "addon.menu.register_toggle(option_id, definition, callback)", "Register a checked option in the addon section of the Client Manager menu. The callback receives the new checked state.", "boolean, string|nil", Param("option_id", "string"), Param("definition", "table"), Param("callback", "function"));
        AddFunction(result, "addon.menu.remove", "addon.menu.remove(item_id)", "Remove a registered window or toggle from the Client Manager Addons menu.", "boolean, string|nil", Param("item_id", "string"));
        AddTable(result, "addon.time", "Time-formatting helpers.");
        AddFunction(result, "addon.time.format_local", "addon.time.format_local(timestamp_ms)", "Format Unix milliseconds in the user's local time.", "string|nil, string|nil", Param("timestamp_ms", "number"));

        AddTable(result, "ui", "Functions for addon windows, labels and buttons.");
        AddTable(result, "ui.window", "Addon window operations.");
        AddFunction(result, "ui.window.set", "ui.window.set(widget_id, definition)", "Create or replace an addon window definition.", "boolean, string|nil", Param("widget_id", "string"), Param("definition", "table"));
        AddFunction(result, "ui.window.show", "ui.window.show(widget_id, visible)", "Change addon-requested contextual visibility without erasing the user's close choice.", "boolean, string|nil", Param("widget_id", "string"), Param("visible", "boolean"));
        AddFunction(result, "ui.window.set_available", "ui.window.set_available(widget_id, available)", "Mark a window available or unavailable in the current context.", "boolean, string|nil", Param("widget_id", "string"), Param("available", "boolean"));
        AddFunction(result, "ui.window.remove", "ui.window.remove(widget_id)", "Remove a window and its child widgets.", "boolean, string|nil", Param("widget_id", "string"));
        AddTable(result, "ui.label", "Overlay label operations.");
        AddFunction(result, "ui.label.set", "ui.label.set(widget_id, definition)", "Create or replace a label.", "boolean, string|nil", Param("widget_id", "string"), Param("definition", "table"));
        AddFunction(result, "ui.label.remove", "ui.label.remove(widget_id)", "Remove a label.", "boolean, string|nil", Param("widget_id", "string"));
        AddTable(result, "ui.button", "Overlay button operations.");
        AddFunction(result, "ui.button.set", "ui.button.set(widget_id, definition, callback)", "Create or replace an addon button and run the callback when the player clicks it.", "boolean, string|nil", Param("widget_id", "string"), Param("definition", "table"), Param("callback", "fun(interaction: table)"));
        AddFunction(result, "ui.button.remove", "ui.button.remove(widget_id)", "Remove a button and callback.", "boolean, string|nil", Param("widget_id", "string"));
        AddFunction(result, "ui.clear", "ui.clear()", "Remove every UI widget owned by this addon.", "boolean");

        AddTable(result, "actions", "Approved actions that can run when the player clicks an addon button.");
        AddTable(result, "actions.target", "Target actions.");
        AddFunction(result, "actions.target.nearest_navigation", "actions.target.nearest_navigation()", "Use the player's configured Target Near Navigation shortcut. This can only run from an addon button click.", "boolean, string|nil");
        AddFunction(result, "actions.target.previous", "actions.target.previous()", "Use the player's configured Previous Target shortcut. This can only run from an addon button click.", "boolean, string|nil");
        AddTable(result, "actions.combat", "Combat actions.");
        AddFunction(result, "actions.combat.fire_all", "actions.combat.fire_all()", "Use the player's configured Fire All Weapons shortcut. This can only run from an addon button click.", "boolean, string|nil");
        AddTable(result, "actions.navigation", "Navigation actions.");
        AddFunction(result, "actions.navigation.open_planner", "actions.navigation.open_planner()", "Open the Client Manager navigation planner from an addon button click.", "boolean, string|nil");
        AddFunction(result, "actions.navigation.select_next_target", "actions.navigation.select_next_target()", "Select the next target on the current route from an addon button click.", "boolean, string|nil");
        AddFunction(result, "actions.navigation.start_auto_pilot", "actions.navigation.start_auto_pilot()", "Start Client Manager Auto Pilot for the current route from an addon button click.", "boolean, string|nil");
        AddFunction(result, "actions.navigation.stop_auto_pilot", "actions.navigation.stop_auto_pilot()", "Stop Client Manager Auto Pilot from an addon button click.", "boolean, string|nil");
        AddFunction(result, "actions.navigation.clear_route", "actions.navigation.clear_route()", "Clear the current Client Manager route from an addon button click.", "boolean, string|nil");
        AddFunction(result, "actions.navigation.plan_return_trip", "actions.navigation.plan_return_trip()", "Plan a new route back to the precise departure point of the completed route.", "boolean, string|nil");

        AddTable(result, "game", "The latest safe, read-only state for this Earth & Beyond game client. Some tables become unavailable during login, character selection and loading.");
        AddTable(result, "game.meta", "Basic timing and API information for the current game state.");
        AddField(result, "game.meta.api_version", "integer", "Net7 addon API version.");
        AddField(result, "game.meta.observed_at", "integer", "Time of the latest game-state update as Unix milliseconds.");
        AddTable(result, "game.lifecycle", "The game client's current login, loading or in-game state.");
        AddField(result, "game.lifecycle.state", "string", "Current login, character-selection, loading or in-game state.");
        AddField(result, "game.lifecycle.is_in_game", "boolean", "True while a pilot is in game.");
        AddField(result, "game.lifecycle.is_transitioning", "boolean", "True during loading or transition.");
        AddTable(result, "game.world", "The current pilot's system, sector, station and environment. This is the current location, not the complete shared Forge galaxy catalogue.");
        AddField(result, "game.world.available", "boolean", "Whether world state is currently available.");
        AddField(result, "game.world.environment", "string", "space, starbase or unknown.");
        AddField(result, "game.world.system_name", "string|nil", "Current system name.");
        AddField(result, "game.world.sector_name", "string|nil", "Current sector name.");
        AddField(result, "game.world.starbase_name", "string|nil", "Current starbase name.");
        AddTable(result, "game.events", "Meaningful game changes plus selected Client Manager and Forge updates, and Social online/offline events without location details.");
        AddFunction(result, "game.events.on", "game.events.on(event_name, callback)", "Run a callback when a supported event occurs. The callback receives a read-only event table.", "nil", Param("event_name", "string"), Param("callback", "fun(event: table)"));

        AddGameDataSymbols(result);

        result.AddRange(eventDefinitions.Select(definition => new AddonApiSymbol
        {
            Path = definition.Name,
            Kind = AddonApiSymbolKind.Event,
            Signature = string.Concat(
                "game.events.on(\"",
                definition.Name,
                "\", callback)"),
            Description = definition.Description,
        }));

        AddLuaStandardLibrarySymbols(result);

        return result
            .OrderBy(symbol => symbol.Path, StringComparer.Ordinal)
            .ThenBy(symbol => symbol.Kind)
            .ToArray();
    }

    private static AddonApiParameter Param(string name, string type)
    {
        return new AddonApiParameter
        {
            Name = name,
            Type = type,
        };
    }

    private static void AddTable(List<AddonApiSymbol> result, string path, string description)
    {
        result.Add(new AddonApiSymbol
        {
            Path = path,
            Kind = AddonApiSymbolKind.Table,
            Description = description,
        });
    }

    private static void AddField(List<AddonApiSymbol> result, string path, string type, string description)
    {
        result.Add(new AddonApiSymbol
        {
            Path = path,
            Kind = AddonApiSymbolKind.Field,
            ReturnType = type,
            Signature = string.Concat(path, ": ", type),
            Description = description,
        });
    }

    private static void AddFunction(
        List<AddonApiSymbol> result,
        string path,
        string signature,
        string description,
        string returnType,
        params AddonApiParameter[] parameters)
    {
        result.Add(new AddonApiSymbol
        {
            Path = path,
            Kind = AddonApiSymbolKind.Function,
            Signature = signature,
            Description = description,
            ReturnType = returnType,
            Parameters = parameters,
        });
    }
}
