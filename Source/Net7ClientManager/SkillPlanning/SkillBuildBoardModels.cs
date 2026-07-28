namespace Net7ClientManager.SkillPlanning;

using System.Text.Json.Serialization;

/// <summary>
/// One equipment-centred target state for one profession. Local documents are
/// mutable. Publishing can later snapshot this model into an immutable Forge
/// version without changing the local authoring contract.
/// </summary>
internal sealed record SkillBuildDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public string BuildId { get; init; } = "";

    public int ProfessionIndex { get; init; }

    public string Title { get; init; } = "";

    public string Summary { get; init; } = "";

    public string Notes { get; init; } = "";

    public IReadOnlyList<SkillBuildEquipmentRequirement> Equipment { get; init; } = [];

    public IReadOnlyList<SkillBuildSkillRecommendation> RecommendedSkills { get; init; } = [];
}

internal sealed record SkillBuildEquipmentRequirement
{
    public string RequirementId { get; init; } = "";

    public SkillBuildEquipmentKind Kind { get; init; }

    // Presentation order only. Weapon and device order never maps to a native
    // equipment slot and is never part of matching semantics.
    public int Order { get; init; }

    // First entry is preferred. Every later entry is an accepted alternative.
    public IReadOnlyList<SkillBuildEquipmentAlternative> Alternatives { get; init; } = [];
}

internal sealed record SkillBuildSkillRecommendation(
    int SkillId,
    int TargetRank);

internal sealed record SkillBuildAnalysis
{
    public required SkillBuildDocument Build { get; init; }

    public required SkillPlannerProfessionDefinition Profession { get; init; }

    public required SkillBuildLevelTarget GenericLevels { get; init; }

    public required SkillBuildLevelTarget EffectiveLevels { get; init; }

    public required SkillBuildHullDefinition RequiredHull { get; init; }

    public bool HasSupportedEquipmentSlots { get; init; }

    public int GenericRequiredSkillPoints { get; init; }

    public int EffectiveRequiredSkillPoints { get; init; }

    public int RemainingSkillPointCost { get; init; }

    public int AvailableSkillPoints { get; init; }

    public required SkillBuildRequirementReasons GenericReasons { get; init; }

    public required SkillBuildRequirementReasons EffectiveReasons { get; init; }

    public IReadOnlyList<SkillBuildSkillAnalysis> Skills { get; init; } = [];

    public IReadOnlyList<SkillBuildEquipmentRequirementAnalysis> Equipment { get; init; } = [];

    public IReadOnlyList<string> Issues { get; init; } = [];

    [JsonIgnore]
    public bool HasLiveCharacter => this.Skills.Any(value => value.CurrentRank.HasValue);

    [JsonIgnore]
    public bool IsComplete =>
        this.HasLiveCharacter &&
        this.EffectiveLevels.IsComplete &&
        this.Skills.All(value => value.IsComplete) &&
        this.Equipment.All(value => value.IsEquipped);
}

internal sealed record SkillBuildRequirementReasons
{
    public IReadOnlyList<string> Overall { get; init; } = [];

    public IReadOnlyList<string> Hull { get; init; } = [];

    public IReadOnlyList<string> Combat { get; init; } = [];

    public IReadOnlyList<string> Explore { get; init; } = [];

    public IReadOnlyList<string> Trade { get; init; } = [];

    public IReadOnlyList<string> SkillPoints { get; init; } = [];
}

internal sealed record SkillBuildLevelTarget
{
    public int Overall { get; init; }

    public int Combat { get; init; }

    public int Explore { get; init; }

    public int Trade { get; init; }

    public int? CurrentOverall { get; init; }

    public int? CurrentCombat { get; init; }

    public int? CurrentExplore { get; init; }

    public int? CurrentTrade { get; init; }

    [JsonIgnore]
    public bool IsComplete =>
        this.CurrentOverall >= this.Overall &&
        this.CurrentCombat >= this.Combat &&
        this.CurrentExplore >= this.Explore &&
        this.CurrentTrade >= this.Trade;
}

internal sealed record SkillBuildSkillAnalysis
{
    public required SkillPlannerSkillDefinition Skill { get; init; }

    public int StartingRank { get; init; }

    public int EquipmentMinimumRank { get; init; }

    public int RecommendedRank { get; init; }

    public int TargetRank { get; init; }

    public int? CurrentRank { get; init; }

    public int MaximumRank { get; init; }

    public int SkillPointCost { get; init; }

    [JsonIgnore]
    public bool IsComplete => !this.CurrentRank.HasValue || this.CurrentRank >= this.TargetRank;
}

internal sealed record SkillBuildEquipmentRequirementAnalysis
{
    public required SkillBuildEquipmentRequirement Requirement { get; init; }

    public SkillBuildEquipmentAlternative? EffectiveChoice { get; init; }

    public SkillBuildEquipmentAlternative? EquippedChoice { get; init; }

    public int? EquippedItemTemplateId { get; init; }

    public string EquippedItemName { get; init; } = "";

    public bool IsEquipped { get; init; }

    public SkillBuildEquipmentAvailability Availability { get; init; }

    [JsonIgnore]
    public bool IsOwned => this.Availability is not SkillBuildEquipmentAvailability.Missing;
}

internal enum SkillBuildEquipmentAvailability
{
    Missing,
    Vault,
    Inventory,
    Equipped,
}
