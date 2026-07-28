namespace Net7ClientManager.SkillPlanning;

internal static class SkillPlannerRules
{
    private const int MaximumTrackLevel = 50;

    private static readonly IReadOnlyDictionary<
        SkillPlannerRequirementCurve,
        IReadOnlyList<int>> RequirementLevels =
        new Dictionary<SkillPlannerRequirementCurve, IReadOnlyList<int>>
        {
            [SkillPlannerRequirementCurve.PrimaryWeapon] =
                [0, 0, 12, 18, 24, 30, 36, 42, 50],
            [SkillPlannerRequirementCurve.SecondaryWeapon] =
                [0, 7, 14, 21, 28, 35, 42, 49, -1],
            [SkillPlannerRequirementCurve.PrimaryTech] =
                [0, 0, 20, 40, 60, 80, 100, 120, 140],
            [SkillPlannerRequirementCurve.SecondaryTech] =
                [0, 0, 30, 50, 70, 90, 110, 130, -1],
            [SkillPlannerRequirementCurve.Standard] =
                [0, 0, 5, 15, 25, 35, 45, -1, -1],
            [SkillPlannerRequirementCurve.Extended] =
                [75, 90, 105, -1, -1, -1, -1, -1, -1],
        };

    public static int ClampTrackLevel(int level) =>
        Math.Clamp(level, 0, MaximumTrackLevel);

    public static int CalculateEarnedSkillPoints(
        int combatLevel,
        int exploreLevel,
        int tradeLevel)
    {
        return checked(
            CalculateTrackSkillPoints(combatLevel) +
            CalculateTrackSkillPoints(exploreLevel) +
            CalculateTrackSkillPoints(tradeLevel));
    }

    public static int CalculateTrackSkillPoints(int level)
    {
        var clampedLevel = ClampTrackLevel(level);

        if (clampedLevel < 20)
        {
            return clampedLevel;
        }

        if (clampedLevel < 40)
        {
            return checked(
                19 +
                ((clampedLevel - 19) * 2));
        }

        return checked(
            59 +
            ((clampedLevel - 39) * 3));
    }

    public static int CalculatePaidSkillPointsToRank(int rank)
    {
        var effectiveRank = Math.Max(0, rank);

        return checked(
            effectiveRank *
            (effectiveRank - 1) /
            2);
    }

    public static int ResolveRankRequirementLevel(
        SkillPlannerRequirementCurve curve,
        int rank)
    {
        if (rank is < 1 or > 9)
        {
            return -1;
        }

        return RequirementLevels[curve][rank - 1];
    }

    public static SkillPlannerLevelTrack ResolveRequirementTrack(
        SkillPlannerCategory category)
    {
        return category switch
        {
            SkillPlannerCategory.Combat => SkillPlannerLevelTrack.Combat,
            SkillPlannerCategory.Explore => SkillPlannerLevelTrack.Explore,
            SkillPlannerCategory.Trade => SkillPlannerLevelTrack.Trade,
            _ => SkillPlannerLevelTrack.Overall,
        };
    }
}
