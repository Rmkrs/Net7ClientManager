namespace Net7ClientManager.Models;

public sealed class AppSettings
{
    public bool StartMinimizedToTray { get; init; }

    public bool AutoDockNewClients { get; init; } = true;

    public bool AutoAssignNewClients { get; init; } = true;

    public Guid? CurrentProfileId { get; set; }

    public List<LayoutProfile> Profiles { get; init; } = [];

    public bool HasProfiles => this.Profiles.Count > 0;

    public string DefaultSlotResolutionPresetName { get; set; } = "1280×720";

    public List<SlotResolutionPreset> SlotResolutionPresets { get; set; } = [];

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
        if (this.SlotResolutionPresets.Count == 0)
        {
            this.SlotResolutionPresets =
            [
                new SlotResolutionPreset { Name = "800×600", Width = 800, Height = 600 },
                new SlotResolutionPreset { Name = "1024×768", Width = 1024, Height = 768 },
                new SlotResolutionPreset { Name = "1152×864", Width = 1152, Height = 864 },
                new SlotResolutionPreset { Name = "1280×720", Width = 1280, Height = 720 },
                new SlotResolutionPreset { Name = "1280×768", Width = 1280, Height = 768 },
                new SlotResolutionPreset { Name = "1280×960", Width = 1280, Height = 960 },
                new SlotResolutionPreset { Name = "1280×1024", Width = 1280, Height = 1024 },
                new SlotResolutionPreset { Name = "1366×768", Width = 1366, Height = 768 },
                new SlotResolutionPreset { Name = "1600×900", Width = 1600, Height = 900 },
                new SlotResolutionPreset { Name = "1920×1080", Width = 1920, Height = 1080 },
            ];
        }

        if (string.IsNullOrWhiteSpace(this.DefaultSlotResolutionPresetName)
            || this.SlotResolutionPresets.TrueForAll(preset => !string.Equals(preset.Name, this.DefaultSlotResolutionPresetName, StringComparison.Ordinal)))
        {
            var preferredDefault = this.SlotResolutionPresets.FirstOrDefault(preset => string.Equals(preset.Name, "1280×720", StringComparison.Ordinal))
                                   ?? this.SlotResolutionPresets[0];

            this.DefaultSlotResolutionPresetName = preferredDefault.Name;
        }
    }
}
