namespace Net7ClientManager.Models;

public enum SocialAtlasVisibilityMode
{
    None = 0,
    SectorOnly = 1,
    NearNav = 2,
    ExactPosition = 3,
}

public sealed class SocialSettings
{
    public int PresencePublishIntervalSeconds { get; set; } = 60;

    public int RefreshIntervalSeconds { get; set; } = 60;

    public int DiscoveryLookbackDays { get; set; } = 30;

    public int OnlineThresholdMinutes { get; set; } = 3;

    public Dictionary<string, SocialPilotSettings> Pilots { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, SocialGuildRecruitmentSettings> Guilds { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public void EnsureDefaults()
    {
        this.PresencePublishIntervalSeconds = Math.Clamp(
            this.PresencePublishIntervalSeconds,
            30,
            600);
        this.RefreshIntervalSeconds = Math.Clamp(
            this.RefreshIntervalSeconds,
            30,
            600);
        this.DiscoveryLookbackDays = Math.Clamp(
            this.DiscoveryLookbackDays,
            1,
            365);
        this.OnlineThresholdMinutes = Math.Clamp(
            this.OnlineThresholdMinutes,
            1,
            60);

        this.Pilots = new Dictionary<string, SocialPilotSettings>(
            (this.Pilots ?? [])
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
                .ToDictionary(
                    pair => pair.Key.Trim(),
                    pair => pair.Value ?? new SocialPilotSettings(),
                    StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);

        foreach (var (pilotName, pilot) in this.Pilots)
        {
            pilot.PilotName = string.IsNullOrWhiteSpace(pilot.PilotName)
                ? pilotName
                : pilot.PilotName.Trim();
            pilot.EnsureDefaults();
        }

        this.Guilds = new Dictionary<string, SocialGuildRecruitmentSettings>(
            (this.Guilds ?? [])
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
                .ToDictionary(
                    pair => pair.Key.Trim(),
                    pair => pair.Value ?? new SocialGuildRecruitmentSettings(),
                    StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);

        foreach (var (guildName, guild) in this.Guilds)
        {
            guild.GuildName = string.IsNullOrWhiteSpace(guild.GuildName)
                ? guildName
                : guild.GuildName.Trim();
            guild.EnsureDefaults();
        }
    }

    public SocialPilotSettings GetOrCreatePilot(string pilotName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pilotName);
        var normalized = pilotName.Trim();

        if (!this.Pilots.TryGetValue(normalized, out var settings))
        {
            settings = new SocialPilotSettings
            {
                PilotName = normalized,
            };
            this.Pilots[normalized] = settings;
        }

        settings.PilotName = normalized;
        settings.EnsureDefaults();
        return settings;
    }

    public SocialGuildRecruitmentSettings GetOrCreateGuild(
        string guildName,
        string publishingPilotName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(guildName);
        ArgumentException.ThrowIfNullOrWhiteSpace(publishingPilotName);
        var normalized = guildName.Trim();

        if (!this.Guilds.TryGetValue(normalized, out var settings))
        {
            settings = new SocialGuildRecruitmentSettings
            {
                GuildName = normalized,
                PublishingPilotName = publishingPilotName.Trim(),
            };
            this.Guilds[normalized] = settings;
        }

        settings.GuildName = normalized;
        if (string.IsNullOrWhiteSpace(settings.PublishingPilotName))
        {
            settings.PublishingPilotName = publishingPilotName.Trim();
        }

        settings.EnsureDefaults();
        return settings;
    }
}

public sealed class SocialPilotSettings
{
    public string PilotName { get; set; } = "";

    public bool PublishPresence { get; set; }

    public SocialAtlasVisibilityMode AtlasVisibility { get; set; } =
        SocialAtlasVisibilityMode.NearNav;

    public bool IsLookingForGuild { get; set; }

    public string? ProfessionNameSnapshot { get; set; }

    public int? OverallLevelSnapshot { get; set; }

    public string? GuildNameSnapshot { get; set; }

    public List<string> InterestTags { get; set; } = [];

    public List<string> Languages { get; set; } = [];

    public string? OtherLanguage { get; set; }

    public string? Region { get; set; }

    public string? OtherRegion { get; set; }

    public string? Availability { get; set; }

    public string? Message { get; set; }

    public void EnsureDefaults()
    {
        if (!Enum.IsDefined(this.AtlasVisibility))
        {
            this.AtlasVisibility = SocialAtlasVisibilityMode.NearNav;
        }

        this.InterestTags = NormalizeList(this.InterestTags);
        this.Languages = NormalizeList(this.Languages);
    }

    private static List<string> NormalizeList(IEnumerable<string>? values)
    {
        return [.. (values ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)];
    }
}

public sealed class SocialGuildRecruitmentSettings
{
    public string GuildName { get; set; } = "";

    public string PublishingPilotName { get; set; } = "";

    public bool IsRecruiting { get; set; }

    public string? OtherContacts { get; set; }

    public List<string> FocusTags { get; set; } = [];

    public List<string> WantedProfessions { get; set; } = [];

    public List<string> Languages { get; set; } = [];

    public string? OtherLanguage { get; set; }

    public string? Region { get; set; }

    public string? OtherRegion { get; set; }

    public string? ActiveTimes { get; set; }

    public string? Requirements { get; set; }

    public string? Message { get; set; }

    public void EnsureDefaults()
    {
        this.FocusTags = NormalizeList(this.FocusTags);
        this.WantedProfessions = NormalizeList(this.WantedProfessions);
        this.Languages = NormalizeList(this.Languages);
    }

    private static List<string> NormalizeList(IEnumerable<string>? values)
    {
        return [.. (values ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)];
    }
}
