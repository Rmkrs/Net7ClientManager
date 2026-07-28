namespace Net7ClientManager.Observations.Models;

public sealed record ClientItemTemplateSummary
{
    public int ItemTemplateId { get; init; }

    public string Name { get; init; } = "";

    public string TypeDisplayName { get; init; } = "";

    public int Category { get; init; }

    public int Subcategory { get; init; }

    public int ItemType { get; init; }

    public int TechLevel { get; init; }

    public int ProfessionRestriction { get; init; }

    public int RaceRestriction { get; init; }

    public int LoreRestriction { get; init; }

    public int RequiredCombatLevel { get; init; }

    public int RequiredExploreLevel { get; init; }

    public int RequiredTradeLevel { get; init; }

    public int RequiredOverallLevel { get; init; }
}
