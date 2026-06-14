namespace Net7ClientManager.Services;

using System.Text.Json;
using Net7ClientManager.Models;

public sealed class InputActionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private static readonly InputClickActionDefinition[] DefaultClickActions =
    [
        new()
        {
            Name = "Login Screen Username",
            BaseWidth = 1280,
            BaseHeight = 720,
            BaseX = 299.3333333333333,
            BaseY = 172.66666666666666,
        },
        new()
        {
            Name = "Character Screen Slot 1",
            BaseWidth = 1280,
            BaseHeight = 720,
            BaseX = 254.66666666666666,
            BaseY = 46.666666666666664,
        },
        new()
        {
            Name = "Character Screen Slot 2",
            BaseWidth = 1280,
            BaseHeight = 720,
            BaseX = 248.66666666666666,
            BaseY = 85.33333333333333,
        },
        new()
        {
            Name = "Character Screen Slot 3",
            BaseWidth = 1280,
            BaseHeight = 720,
            BaseX = 254,
            BaseY = 122,
        },
        new()
        {
            Name = "Character Screen Slot 4",
            BaseWidth = 1280,
            BaseHeight = 720,
            BaseX = 248.66666666666666,
            BaseY = 158,
        },
        new()
        {
            Name = "Character Screen Slot 5",
            BaseWidth = 1280,
            BaseHeight = 720,
            BaseX = 245.33333333333334,
            BaseY = 199.33333333333334,
        },
        new()
        {
            Name = "Character Screen Enter Game",
            BaseWidth = 1280,
            BaseHeight = 720,
            BaseX = 1171.3333333333333,
            BaseY = 662,
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

    public IReadOnlyList<InputClickActionDefinition> LoadClickActions()
    {
        var config = this.LoadConfig();
        var changed = this.EnsureDefaultClickActions(config);

        config.ClickActions = [.. config.ClickActions
            .OrderBy(action => action.Name, StringComparer.OrdinalIgnoreCase)];

        if (changed)
        {
            this.SaveConfig(config);
        }

        return config.ClickActions;
    }

    public void SaveOrReplaceClickAction(InputClickActionDefinition action)
    {
        var config = this.LoadConfig();
        _ = this.EnsureDefaultClickActions(config);

        action.UpdatedAt = DateTimeOffset.UtcNow;

        var existing = config.ClickActions.FindIndex(
            item => string.Equals(item.Name, action.Name, StringComparison.OrdinalIgnoreCase));

        if (existing >= 0)
        {
            config.ClickActions[existing] = action;
        }
        else
        {
            config.ClickActions.Add(action);
        }

        config.ClickActions = [.. config.ClickActions
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)];

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
            return JsonSerializer.Deserialize<InputActionConfig>(json, JsonOptions)
                ?? new InputActionConfig();
        }
        catch
        {
            return new InputActionConfig();
        }
    }

    private void SaveConfig(InputActionConfig config)
    {
        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(this.filePath, json);
    }

    private bool EnsureDefaultClickActions(InputActionConfig config)
    {
        var changed = false;

        foreach (var defaultAction in DefaultClickActions)
        {
            var exists = config.ClickActions.Any(action =>
                string.Equals(action.Name, defaultAction.Name, StringComparison.OrdinalIgnoreCase));

            if (exists)
            {
                continue;
            }

            config.ClickActions.Add(new InputClickActionDefinition
            {
                Name = defaultAction.Name,
                BaseWidth = defaultAction.BaseWidth,
                BaseHeight = defaultAction.BaseHeight,
                BaseX = defaultAction.BaseX,
                BaseY = defaultAction.BaseY,
                UpdatedAt = DateTimeOffset.UtcNow,
            });

            changed = true;
        }

        return changed;
    }

    private sealed class InputActionConfig
    {
        public List<InputClickActionDefinition> ClickActions { get; set; } = [];
    }
}
