namespace Net7ClientManager.Social;

using Net7ClientManager.Models;

internal sealed record SocialPresenceUpsertRequest(
    int ProtocolVersion,
    string ContributorId,
    string RequestId,
    DateTimeOffset SubmittedAtUtc,
    string ClientVersion,
    string PilotName,
    bool IsSharing,
    SocialAtlasVisibilityMode AtlasVisibility,
    string? SectorId,
    string? SectorKey,
    string? SectorName,
    string? SystemName,
    int ActiveSectorNumber,
    double? X,
    double? Y,
    double? Z,
    long? NearestNavObjectId,
    string? NearestNavName,
    string Signature,
    string? StationName = null);

public sealed record SocialPresenceRecord(
    string PilotName,
    SocialAtlasVisibilityMode AtlasVisibility,
    string? SectorId,
    string? SectorKey,
    string? SectorName,
    string? SystemName,
    int ActiveSectorNumber,
    double? X,
    double? Y,
    double? Z,
    long? NearestNavObjectId,
    string? NearestNavName,
    DateTimeOffset UpdatedAtUtc,
    string? StationName = null);

internal sealed record LookingForGuildUpsertRequest(
    int ProtocolVersion,
    string ContributorId,
    string RequestId,
    DateTimeOffset SubmittedAtUtc,
    string ClientVersion,
    string PilotName,
    bool IsLookingForGuild,
    string? ProfessionName,
    int? OverallLevel,
    IReadOnlyList<string> InterestTags,
    IReadOnlyList<string> Languages,
    string? OtherLanguage,
    string? Region,
    string? OtherRegion,
    string? Availability,
    string? Message,
    string Signature);

public sealed record LookingForGuildRecord(
    string PilotName,
    string? ProfessionName,
    int? OverallLevel,
    IReadOnlyList<string> InterestTags,
    IReadOnlyList<string> Languages,
    string? OtherLanguage,
    string? Region,
    string? OtherRegion,
    string? Availability,
    string? Message,
    DateTimeOffset UpdatedAtUtc);

internal sealed record GuildRecruitmentUpsertRequest(
    int ProtocolVersion,
    string ContributorId,
    string RequestId,
    DateTimeOffset SubmittedAtUtc,
    string ClientVersion,
    string GuildName,
    string PublishingPilotName,
    bool IsRecruiting,
    string? OtherContacts,
    IReadOnlyList<string> FocusTags,
    IReadOnlyList<string> WantedProfessions,
    IReadOnlyList<string> Languages,
    string? OtherLanguage,
    string? Region,
    string? OtherRegion,
    string? ActiveTimes,
    string? Requirements,
    string? Message,
    string Signature);

public sealed record GuildRecruitmentRecord(
    string GuildName,
    string PublishingPilotName,
    string? OtherContacts,
    IReadOnlyList<string> FocusTags,
    IReadOnlyList<string> WantedProfessions,
    IReadOnlyList<string> Languages,
    string? OtherLanguage,
    string? Region,
    string? OtherRegion,
    string? ActiveTimes,
    string? Requirements,
    string? Message,
    DateTimeOffset UpdatedAtUtc);

internal sealed record SocialUpsertResponse(
    string Key,
    DateTimeOffset UpdatedAtUtc);

public sealed class SocialSnapshotRefreshedEventArgs(
    SocialDataSnapshot previous,
    SocialDataSnapshot current) : EventArgs
{
    public SocialDataSnapshot Previous { get; } = previous;

    public SocialDataSnapshot Current { get; } = current;
}

public sealed record SocialDataSnapshot(
    IReadOnlyList<SocialPresenceRecord> Presence,
    IReadOnlyList<LookingForGuildRecord> LookingForGuild,
    IReadOnlyList<GuildRecruitmentRecord> GuildRecruitment,
    DateTimeOffset? RefreshedAtUtc,
    string Status,
    long Version)
{
    public static SocialDataSnapshot Empty { get; } = new(
        [],
        [],
        [],
        null,
        "Social data has not been refreshed yet.",
        0);
}

public sealed record SocialLocalPilot(
    string PilotName,
    string? ProfessionName,
    int? OverallLevel,
    string? GuildName,
    bool IsRunning,
    int? ProcessId);

public enum SocialPresenceFreshness
{
    Online,
    RecentlySeen,
    Offline,
}

public static class SocialVocabulary
{
    public static IReadOnlyList<string> InterestTags { get; } =
    [
        "Social",
        "Casual",
        "Combat",
        "Raids / group content",
        "Trade and crafting",
        "Exploration",
        "New-player friendly",
        "Multibox-friendly",
    ];

    public static IReadOnlyList<string> Languages { get; } =
    [
        "English",
        "Dutch",
        "German",
        "French",
        "Spanish",
        "Portuguese",
        "Italian",
        "Polish",
        "Russian",
        "Other",
    ];

    public static IReadOnlyList<string> Regions { get; } =
    [
        "UTC",
        "UK / Ireland (GMT/BST)",
        "Europe (CET/CEST)",
        "Europe (EET/EEST)",
        "North America (Eastern)",
        "North America (Central)",
        "North America (Mountain)",
        "North America (Pacific)",
        "South America",
        "Oceania",
        "Asia",
        "Other",
    ];

    public static IReadOnlyList<string> Professions { get; } =
    [
        "All professions",
        "Jenquai Defender",
        "Jenquai Explorer",
        "Jenquai Seeker",
        "Progen Sentinel",
        "Progen Warrior",
        "Progen Privateer",
        "Terran Enforcer",
        "Terran Trader",
        "Terran Scout",
    ];
}
