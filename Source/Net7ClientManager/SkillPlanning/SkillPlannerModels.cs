namespace Net7ClientManager.SkillPlanning;

internal sealed record SkillPlannerCharacterBaseline
{
    public int ProfessionIndex { get; init; }

    public int CombatLevel { get; init; }

    public int ExploreLevel { get; init; }

    public int TradeLevel { get; init; }

    public int AvailableSkillPoints { get; init; }

    public int? HullTier { get; init; }

    public IReadOnlyDictionary<int, SkillPlannerOwnedSkill> Skills { get; init; } =
        new Dictionary<int, SkillPlannerOwnedSkill>();

    public int OverallLevel =>
        checked(
            this.CombatLevel +
            this.ExploreLevel +
            this.TradeLevel);
}

internal sealed record SkillPlannerOwnedSkill
{
    public int SkillId { get; init; }

    public string Name { get; init; } = "";

    public int CurrentRank { get; init; }

    public int MaximumRank { get; init; }

    // Despite the client property name, this is the first quest-only rank.
    // For example, 8 on an eight-rank skill means only rank 8 is quest-only.
    public int QuestOnlyLevels { get; init; }

    public int AvailabilityCode { get; init; }
}

internal sealed record SkillPlannerLevelRequirement(
    SkillPlannerLevelTrack Track,
    int Level);

internal enum SkillPlannerLevelTrack
{
    Combat,
    Explore,
    Trade,
    Overall,
}
