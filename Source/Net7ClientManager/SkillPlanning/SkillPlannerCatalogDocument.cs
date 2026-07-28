namespace Net7ClientManager.SkillPlanning;

internal sealed record SkillPlannerCatalogDocument
{
    public int SchemaVersion { get; init; }

    public string Revision { get; init; } = "";

    public IReadOnlyList<SkillPlannerProfessionDocument> Professions { get; init; } =
        [];

    public IReadOnlyList<SkillPlannerGroupDocument> Groups { get; init; } =
        [];

    public IReadOnlyList<SkillPlannerSkillDocument> Skills { get; init; } =
        [];
}

internal sealed record SkillPlannerProfessionDocument
{
    public int Index { get; init; }

    public int RaceIndex { get; init; }

    public int ProfessionIndex { get; init; }

    public string Tag { get; init; } = "";

    public string RaceName { get; init; } = "";

    public string ProfessionName { get; init; } = "";
}

internal sealed record SkillPlannerGroupDocument
{
    public string Name { get; init; } = "";

    public IReadOnlyList<int> SkillIds { get; init; } = [];
}

internal sealed record SkillPlannerSkillDocument
{
    public int Id { get; init; }

    public string Name { get; init; } = "";

    public string Description { get; init; } = "";

    public string Category { get; init; } = "";

    public int MinimumRank { get; init; }

    public string Group { get; init; } = "";

    public IReadOnlyList<SkillPlannerProfessionRuleDocument>
        ProfessionRules { get; init; } = [];

    public IReadOnlyList<SkillPlannerRankDocument> Ranks { get; init; } = [];
}

internal sealed record SkillPlannerProfessionRuleDocument
{
    public int ProfessionIndex { get; init; }

    public int MaximumRank { get; init; }

    public string RequirementCurve { get; init; } = "";

    public int LearnLevel { get; init; }
}

internal sealed record SkillPlannerRankDocument
{
    public int Rank { get; init; }

    public string Description { get; init; } = "";
}
