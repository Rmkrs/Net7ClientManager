namespace Net7ClientManager.SkillPlanning;

internal sealed record SkillBuildBoardPresentation
{
    public bool IsVisible { get; init; }

    public string Fingerprint { get; init; } = "hidden";

    public uint CharacterId { get; init; }

    public string PilotName { get; init; } = "Current pilot";

    public string ProfessionName { get; init; } = "Profession unavailable";

    public string ProfessionTag { get; init; } = "";

    public int CombatLevel { get; init; }

    public int ExploreLevel { get; init; }

    public int TradeLevel { get; init; }

    public int AvailableSkillPoints { get; init; }

    public bool HasBuildContext { get; init; }

    public bool IsArchivedContext { get; init; }

    public bool HasAvailableSkillPoints { get; init; }

    public bool HasHullTier { get; init; }

    public string ContextText { get; init; } = "";

    public string StatusText { get; init; } =
        "Waiting for character build data.";

    public SkillPlannerCharacterBaseline? Baseline { get; init; }

    public SkillBuildEquipmentBaseline EquipmentBaseline { get; init; } =
        SkillBuildEquipmentBaseline.Unavailable(
            "Equipment is unavailable.");

    public IReadOnlyList<SkillBuildBoardSkillRow> Skills { get; init; } = [];

    public IReadOnlyList<SkillBuildBoardEquipmentRow> Equipment { get; init; } = [];

    public static SkillBuildBoardPresentation Hidden { get; } =
        new();
}

internal sealed record SkillBuildBoardSkillRow(
    int SkillId,
    string GroupName,
    string SkillName,
    int CurrentRank,
    int MinimumRank,
    int MaximumRank);

internal sealed record SkillBuildBoardEquipmentRow(
    SkillBuildEquipmentSlot Slot,
    int ItemTemplateId,
    string SlotName,
    string ItemName);
