namespace Net7ClientManager.PilotArchive;

using Net7ClientManager.Addons.Contracts;

public static class PilotArchiveSections
{
    public const string Overview = "overview";
    public const string Location = "location";
    public const string Progression = "progression";
    public const string Skills = "skills";
    public const string CraftingRecipes = "crafting-recipes";
    public const string Cargo = "cargo";
    public const string Equipment = "equipment";
    public const string Vault = "vault";
    public const string Missions = "missions";
    public const string MissionHistory = "mission-history";
    public const string ActivityHistory = "activity-history";
    public const string CombatHistory = "combat-history";
    public const string Reputations = "reputations";
}

internal static class PilotArchiveIdentityPresentation
{
    private static readonly string[] knownRaces =
    [
        "Jenquai",
        "Progen",
        "Terran",
    ];

    public static (string? Race, string? Profession) Resolve(
        string? race,
        string? profession)
    {
        race = Normalize(race);
        profession = Normalize(profession);

        if (string.IsNullOrWhiteSpace(profession))
        {
            return (race, profession);
        }

        if (!string.IsNullOrWhiteSpace(race))
        {
            var prefix = string.Concat(race, " ");

            if (profession.StartsWith(
                    prefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                profession = Normalize(profession[prefix.Length..]);
            }

            return (race, profession);
        }

        foreach (var knownRace in knownRaces)
        {
            var prefix = string.Concat(knownRace, " ");

            if (!profession.StartsWith(
                    prefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return (
                knownRace,
                Normalize(profession[prefix.Length..]));
        }

        return (race, profession);
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }
}

public sealed record PilotArchivePilotSnapshot
{
    public required uint CharacterId { get; init; }
    public required string Name { get; init; }
    public string? Race { get; init; }
    public string? Profession { get; init; }
    public string? ProfessionCode { get; init; }
    public string? Affiliation { get; init; }
    public string? GuildName { get; init; }
    public string? GuildRank { get; init; }
    public int? CombatLevel { get; init; }
    public int? ExploreLevel { get; init; }
    public int? TradeLevel { get; init; }
    public int? OverallLevel { get; init; }
    public int? AvailableSkillPoints { get; init; }
    public int? HullTier { get; init; }
    public string? CurrentSystem { get; init; }
    public string? CurrentSector { get; init; }
    public string? CurrentStarbase { get; init; }
    public string? RegistrationStarbase { get; init; }
    public string? RegistrationSector { get; init; }
    public ulong? Credits { get; init; }
    public DateTimeOffset FirstObservedAt { get; init; }
    public DateTimeOffset LastObservedAt { get; init; }
}

public sealed record PilotArchiveSkill
{
    public required int Index { get; init; }
    public required string Name { get; init; }
    public string Category { get; init; } = "";
    public bool IsActive { get; init; }
    public int CurrentRank { get; init; }
    public int MaximumRank { get; init; }
    public int QuestOnlyLevels { get; init; }
    public int SpentSkillPoints { get; init; }
}

public sealed record PilotArchiveMission
{
    public required int Slot { get; init; }
    public int? RawId { get; init; }
    public required string Name { get; init; }
    public string Summary { get; init; } = "";
    public string Reward { get; init; } = "";
    public string FailureConsequence { get; init; } = "";
    public string IssuingFaction { get; init; } = "";
    public int? Stage { get; init; }
    public int? StageCount { get; init; }
    public bool? IsTimed { get; init; }
    public bool? IsForfeitable { get; init; }
    public bool? IsComplete { get; init; }
    public bool? IsFailed { get; init; }
    public bool? IsExpired { get; init; }
    public string CurrentStageText { get; init; } = "";
    public IReadOnlyList<string> Stages { get; init; } = [];
}

public sealed record PilotArchiveReputation
{
    public required int Slot { get; init; }
    public required string FactionKey { get; init; }
    public required string DisplayName { get; init; }
    public string Description { get; init; } = "";
    public float? Reaction { get; init; }
    public float? NormalizedReaction { get; init; }
    public int? Order { get; init; }
}

public sealed record PilotArchivePilotDetails
{
    public required PilotArchivePilotSnapshot Pilot { get; init; }
    public IReadOnlyDictionary<string, DateTimeOffset> SectionObservedAt { get; init; } =
        new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
    public IReadOnlyList<AddonInventorySlotSnapshot> CargoSlots { get; init; } = [];
    public IReadOnlyList<AddonInventorySlotSnapshot> EquipmentSlots { get; init; } = [];
    public IReadOnlyList<AddonInventorySlotSnapshot> AmmoSlots { get; init; } = [];
    public IReadOnlyList<AddonInventorySlotSnapshot> VaultSlots { get; init; } = [];
    public IReadOnlyList<PilotArchiveSkill> Skills { get; init; } = [];
    public IReadOnlyList<PilotArchiveMission> Missions { get; init; } = [];
    public IReadOnlyList<PilotArchiveReputation> Reputations { get; init; } = [];
}

public sealed record PilotArchiveSearchResult
{
    public required uint CharacterId { get; init; }
    public required string PilotName { get; init; }
    public required string Category { get; init; }
    public required string Result { get; init; }
    public required string TargetSection { get; init; }
    public required string TargetEntityKey { get; init; }
}

internal sealed record PilotArchiveCapture
{
    public required uint CharacterId { get; init; }
    public required string Name { get; init; }
    public required DateTimeOffset ObservedAt { get; init; }
    public required IReadOnlyList<PilotArchiveSectionCapture> Sections { get; init; }
}

internal sealed record PilotArchiveSectionCapture
{
    public required string Section { get; init; }
    public required string Fingerprint { get; init; }
    public required object Value { get; init; }
}

internal sealed record PilotArchiveOverviewCapture
{
    public string? Race { get; init; }
    public string? Profession { get; init; }
    public string? ProfessionCode { get; init; }
    public string? Affiliation { get; init; }
    public string? GuildName { get; init; }
    public string? GuildRank { get; init; }
}

internal sealed record PilotArchiveLocationCapture
{
    public string? CurrentSystem { get; init; }
    public string? CurrentSector { get; init; }
    public string? CurrentStarbase { get; init; }
    public string? RegistrationStarbase { get; init; }
    public string? RegistrationSector { get; init; }
}

internal sealed record PilotArchiveProgressionCapture
{
    public int? CombatLevel { get; init; }
    public int? ExploreLevel { get; init; }
    public int? TradeLevel { get; init; }
    public int? OverallLevel { get; init; }
    public int? AvailableSkillPoints { get; init; }
    public int? HullTier { get; init; }
    public ulong? Credits { get; init; }
}

internal sealed record PilotArchiveEquipmentCapture
{
    public required IReadOnlyList<AddonInventorySlotSnapshot> EquipmentSlots { get; init; }
    public required IReadOnlyList<AddonInventorySlotSnapshot> AmmoSlots { get; init; }
}
