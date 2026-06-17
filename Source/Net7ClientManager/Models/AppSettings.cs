namespace Net7ClientManager.Models;

public sealed class AppSettings
{
    private static readonly SlotResolutionPreset[] defaultSlotResolutionPresets =
    [
        new() { Name = "640×480", Width = 640, Height = 480 },
        new() { Name = "720×480", Width = 720, Height = 480 },
        new() { Name = "720×576", Width = 720, Height = 576 },
        new() { Name = "800×600", Width = 800, Height = 600 },
        new() { Name = "1024×768", Width = 1024, Height = 768 },
        new() { Name = "1152×864", Width = 1152, Height = 864 },
        new() { Name = "1176×664", Width = 1176, Height = 664 },
        new() { Name = "1280×720", Width = 1280, Height = 720 },
        new() { Name = "1280×768", Width = 1280, Height = 768 },
        new() { Name = "1280×800", Width = 1280, Height = 800 },
        new() { Name = "1280×960", Width = 1280, Height = 960 },
        new() { Name = "1280×1024", Width = 1280, Height = 1024 },
        new() { Name = "1360×768", Width = 1360, Height = 768 },
        new() { Name = "1366×768", Width = 1366, Height = 768 },
        new() { Name = "1440×1080", Width = 1440, Height = 1080 },
        new() { Name = "1600×900", Width = 1600, Height = 900 },
        new() { Name = "1600×1024", Width = 1600, Height = 1024 },
        new() { Name = "1600×1200", Width = 1600, Height = 1200 },
        new() { Name = "1680×1050", Width = 1680, Height = 1050 },
        new() { Name = "1920×1080", Width = 1920, Height = 1080 },
        new() { Name = "1920×1200", Width = 1920, Height = 1200 },
        new() { Name = "1920×1440", Width = 1920, Height = 1440 },
        new() { Name = "2048×1536", Width = 2048, Height = 1536 },
        new() { Name = "2560×1440", Width = 2560, Height = 1440 },
        new() { Name = "2560×1600", Width = 2560, Height = 1600 },
        new() { Name = "3840×2160", Width = 3840, Height = 2160 },
        new() { Name = "1920×2160", Width = 1920, Height = 2160 },
        new() { Name = "1440×900", Width = 1440, Height = 900 },
];
    public bool StartMinimizedToTray { get; init; }

    public bool AutoDockNewClients { get; init; } = true;

    public bool AutoAssignNewClients { get; init; } = true;

    public string? PathToNet7Launcher { get; set; }

    public Guid? CurrentProfileId { get; set; }

    public List<LayoutProfile> Profiles { get; init; } = [];

    public bool HasProfiles => this.Profiles.Count > 0;

    public string DefaultSlotResolutionPresetName { get; set; } = "1280×720";

    public List<SlotResolutionPreset> SlotResolutionPresets { get; set; } = [];

    public FleetCommandSettings FleetCommands { get; set; } = new();

    public LayoutProfile GetOrCreateCurrentProfile()
    {
        if (this.CurrentProfileId != null)
        {
            var currentProfile = this.Profiles.FirstOrDefault(profile => profile.Id == this.CurrentProfileId.Value);

            if (currentProfile != null)
            {
                return currentProfile;
            }
        }

        if (this.Profiles.Count > 0)
        {
            var firstProfile = this.Profiles[0];
            this.CurrentProfileId = firstProfile.Id;

            return firstProfile;
        }

        return this.CreateProfile("Default");
    }

    public LayoutProfile CreateProfile(string name)
    {
        var profile = new LayoutProfile
        {
            Name = string.IsNullOrWhiteSpace(name)
                ? "Default"
                : name.Trim(),
        };

        this.Profiles.Add(profile);
        this.CurrentProfileId = profile.Id;

        return profile;
    }

    public void EnsureDefaults()
    {
        foreach (var defaultPreset in defaultSlotResolutionPresets)
        {
            var existingPreset = this.SlotResolutionPresets.FirstOrDefault(preset =>
                preset.Width == defaultPreset.Width &&
                preset.Height == defaultPreset.Height);

            if (existingPreset == null)
            {
                this.SlotResolutionPresets.Add(new SlotResolutionPreset
                {
                    Name = defaultPreset.Name,
                    Width = defaultPreset.Width,
                    Height = defaultPreset.Height,
                });

                continue;
            }

            existingPreset.Name = defaultPreset.Name;
        }

        var defaultPresetOrder = defaultSlotResolutionPresets
            .Select((preset, index) => new
            {
                Key = (preset.Width, preset.Height),
                Index = index,
            })
            .ToDictionary(item => item.Key, item => item.Index);

        this.SlotResolutionPresets = [.. this.SlotResolutionPresets
        .Select((preset, index) => new
        {
            Preset = preset,
            OriginalIndex = index,
            DefaultIndex = defaultPresetOrder.GetValueOrDefault((preset.Width, preset.Height), int.MaxValue),
        })
        .OrderBy(item => item.DefaultIndex)
        .ThenBy(item => item.OriginalIndex)
        .Select(item => item.Preset)];

        if (string.IsNullOrWhiteSpace(this.DefaultSlotResolutionPresetName)
            || this.SlotResolutionPresets.TrueForAll(preset => !string.Equals(preset.Name, this.DefaultSlotResolutionPresetName, StringComparison.Ordinal)))
        {
            var preferredDefault = this.SlotResolutionPresets.FirstOrDefault(preset =>
                                       string.Equals(preset.Name, "1280×720", StringComparison.Ordinal))
                                   ?? this.SlotResolutionPresets[0];

            this.DefaultSlotResolutionPresetName = preferredDefault.Name;
        }
    }
}
