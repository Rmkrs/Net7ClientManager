namespace Net7ClientManager.Services;

using System.Text.Json;
using System.Text.Json.Serialization;
using Net7ClientManager.Models;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions jsonSerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string settingsDirectory;
    private readonly string settingsFilePath;

    public SettingsStore()
    {
        this.settingsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Net7ClientManager");

        this.settingsFilePath = Path.Combine(this.settingsDirectory, "settings.json");
    }

    public AppSettings Load()
    {
        if (!File.Exists(this.settingsFilePath))
        {
            var settings = this.CreateDefaultSettings();
            this.Save(settings);

            return settings;
        }

        try
        {
            var json = File.ReadAllText(this.settingsFilePath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, jsonSerializerOptions) ?? new AppSettings();

            settings.EnsureDefaults();
            _ = settings.GetOrCreateCurrentProfile();

            return settings;
        }
        catch
        {
            var backupPath = this.settingsFilePath + ".broken";

            if (File.Exists(backupPath))
            {
                File.Delete(backupPath);
            }

            File.Move(this.settingsFilePath, backupPath);

            var settings = this.CreateDefaultSettings();
            this.Save(settings);

            return settings;
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(this.settingsDirectory);

        var json = JsonSerializer.Serialize(settings, jsonSerializerOptions);
        File.WriteAllText(this.settingsFilePath, json);
    }

    private AppSettings CreateDefaultSettings()
    {
        var settings = new AppSettings();

        settings.EnsureDefaults();
        _ = settings.GetOrCreateCurrentProfile();

        return settings;
    }
}
