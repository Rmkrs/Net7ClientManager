namespace Net7ClientManager.Services;

using System.Text.Json;
using Net7ClientManager.Models;

public sealed class InputActionStore
{
    private static readonly JsonSerializerOptions jsonOptions = new()
    {
        WriteIndented = true,
    };

    private static readonly InputActionDefinition[] defaultInputActions =
    [
        new()
        {
            Name = "Login Screen Username",
            Kind = InputActionKind.MouseClick,
            Key = Keys.None,
            BaseWidth = 1280,
            BaseHeight = 720,
            BaseX = 299.3333333333333,
            BaseY = 172.66666666666666,
        },
        new()
        {
            Name = "Character Screen Slot 1",
            Kind = InputActionKind.MouseClick,
            Key = Keys.None,
            BaseWidth = 1280,
            BaseHeight = 720,
            BaseX = 254.66666666666666,
            BaseY = 46.666666666666664,
        },
        new()
        {
            Name = "Character Screen Slot 2",
            Kind = InputActionKind.MouseClick,
            Key = Keys.None,
            BaseWidth = 1280,
            BaseHeight = 720,
            BaseX = 248.66666666666666,
            BaseY = 85.33333333333333,
        },
        new()
        {
            Name = "Character Screen Slot 3",
            Kind = InputActionKind.MouseClick,
            Key = Keys.None,
            BaseWidth = 1280,
            BaseHeight = 720,
            BaseX = 254,
            BaseY = 122,
        },
        new()
        {
            Name = "Character Screen Slot 4",
            Kind = InputActionKind.MouseClick,
            Key = Keys.None,
            BaseWidth = 1280,
            BaseHeight = 720,
            BaseX = 248.66666666666666,
            BaseY = 158,
        },
        new()
        {
            Name = "Character Screen Slot 5",
            Kind = InputActionKind.MouseClick,
            Key = Keys.None,
            BaseWidth = 1280,
            BaseHeight = 720,
            BaseX = 245.33333333333334,
            BaseY = 199.33333333333334,
        },
        new()
        {
            Name = "Character Screen Enter Game",
            Kind = InputActionKind.MouseClick,
            Key = Keys.None,
            BaseWidth = 1280,
            BaseHeight = 720,
            BaseX = 1171.3333333333333,
            BaseY = 662,
        },
        new()
        {
            Name = "Target Group Member Target",
            Kind = InputActionKind.MouseClick,
            Key = Keys.None,
            BaseWidth = 1280,
            BaseHeight = 720,
            BaseX = 1219.3333333333333,
            BaseY = 433.3333333333333,
        },
        new()
        {
            Name = "Fire All",
            Kind = InputActionKind.KeyTap,
            Key = Keys.F,
            BaseWidth = 1280,
            BaseHeight = 720,
            BaseX = 0,
            BaseY = 0,
        },
    ];

    private readonly string filePath;

    public InputActionStore()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Net7ClientManager");

        Directory.CreateDirectory(directory);

        this.filePath = Path.Combine(directory, "input-actions.json");
    }

    public IReadOnlyList<InputActionDefinition> LoadActions()
    {
        var config = this.LoadConfig();
        var changed = this.MigrateLegacyClickActions(config);

        changed |= this.EnsureDefaultInputActions(config);
        changed |= NormalizeActions(config);

        if (changed)
        {
            this.SaveConfig(config);
        }

        return config.Actions;
    }

    public IReadOnlyList<InputActionDefinition> LoadClickActions()
    {
        return [.. this.LoadActions()
            .Where(action => action.Kind == InputActionKind.MouseClick)
            .OrderBy(action => action.Name, StringComparer.OrdinalIgnoreCase)];
    }

    public void SaveOrReplaceAction(InputActionDefinition action)
    {
        var config = this.LoadConfig();

        _ = this.MigrateLegacyClickActions(config);
        _ = this.EnsureDefaultInputActions(config);

        action.UpdatedAt = DateTimeOffset.UtcNow;

        var existingIndex = config.Actions.FindIndex(existingAction =>
            string.Equals(existingAction.Name, action.Name, StringComparison.OrdinalIgnoreCase));

        if (existingIndex >= 0)
        {
            config.Actions[existingIndex] = action;
        }
        else
        {
            config.Actions.Add(action);
        }

        _ = NormalizeActions(config);

        this.SaveConfig(config);
    }

    private InputActionConfig LoadConfig()
    {
        if (!File.Exists(this.filePath))
        {
            return new InputActionConfig();
        }

        try
        {
            var json = File.ReadAllText(this.filePath);

            return JsonSerializer.Deserialize<InputActionConfig>(json, jsonOptions)
                ?? new InputActionConfig();
        }
        catch
        {
            return new InputActionConfig();
        }
    }

    private void SaveConfig(InputActionConfig config)
    {
        var json = JsonSerializer.Serialize(config, jsonOptions);
        File.WriteAllText(this.filePath, json);
    }

    private bool MigrateLegacyClickActions(InputActionConfig config)
    {
        if (config.ClickActions.Count == 0)
        {
            return false;
        }

        foreach (var legacyAction in config.ClickActions)
        {
            var exists = config.Actions.Exists(action =>
                string.Equals(action.Name, legacyAction.Name, StringComparison.OrdinalIgnoreCase));

            if (exists)
            {
                continue;
            }

            config.Actions.Add(new InputActionDefinition
            {
                Name = legacyAction.Name,
                Kind = InputActionKind.MouseClick,
                Key = Keys.None,
                BaseWidth = legacyAction.BaseWidth,
                BaseHeight = legacyAction.BaseHeight,
                BaseX = legacyAction.BaseX,
                BaseY = legacyAction.BaseY,
                UpdatedAt = legacyAction.UpdatedAt,
            });
        }

        config.ClickActions.Clear();

        return true;
    }

    private bool EnsureDefaultInputActions(InputActionConfig config)
    {
        var changed = false;

        foreach (var defaultAction in defaultInputActions)
        {
            var exists = config.Actions.Exists(action =>
                string.Equals(action.Name, defaultAction.Name, StringComparison.OrdinalIgnoreCase));

            if (exists)
            {
                continue;
            }

            config.Actions.Add(CloneAction(defaultAction));
            changed = true;
        }

        return changed;
    }

    private static bool NormalizeActions(InputActionConfig config)
    {
        var changed = false;

        foreach (var action in config.Actions)
        {
            if (action.Kind == InputActionKind.MouseClick && action.Key != Keys.None)
            {
                action.Key = Keys.None;
                changed = true;
            }

            if (action.Kind == InputActionKind.KeyTap)
            {
                if (action.BaseWidth != 1280)
                {
                    action.BaseWidth = 1280;
                    changed = true;
                }

                if (action.BaseHeight != 720)
                {
                    action.BaseHeight = 720;
                    changed = true;
                }

                if (Math.Abs(action.BaseX) > double.Epsilon)
                {
                    action.BaseX = 0;
                    changed = true;
                }

                if (Math.Abs(action.BaseY) > double.Epsilon)
                {
                    action.BaseY = 0;
                    changed = true;
                }
            }
        }

        var orderedActions = config.Actions
            .GroupBy(action => action.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .OrderBy(action => action.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (config.Actions.Count != orderedActions.Count)
        {
            changed = true;
        }
        else
        {
            for (var index = 0; index < config.Actions.Count; index++)
            {
                if (string.Equals(config.Actions[index].Name, orderedActions[index].Name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                changed = true;
                break;
            }
        }

        config.Actions = orderedActions;

        return changed;
    }

    private static InputActionDefinition CloneAction(InputActionDefinition action)
    {
        return new InputActionDefinition
        {
            Name = action.Name,
            Kind = action.Kind,
            Key = action.Key,
            BaseWidth = action.BaseWidth,
            BaseHeight = action.BaseHeight,
            BaseX = action.BaseX,
            BaseY = action.BaseY,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
    }

    private sealed class InputActionConfig
    {
        public List<InputActionDefinition> Actions { get; set; } = [];

        public List<InputActionDefinition> ClickActions { get; set; } = [];
    }
}
