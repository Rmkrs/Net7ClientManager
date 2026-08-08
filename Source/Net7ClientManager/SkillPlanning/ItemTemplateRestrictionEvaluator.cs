namespace Net7ClientManager.SkillPlanning;

using System.Runtime.CompilerServices;
using Net7ClientManager.Observations.Models;

/// <summary>
/// Shared interpretation of the native item race/profession exclusion
/// attributes. Builds and item tooltips must agree on these rules.
/// </summary>
internal static class ItemTemplateRestrictionEvaluator
{
    private static readonly ConditionalWeakTable<
        ClientRuntimeItemTemplateObservation,
        RuntimeRestrictionDefinition> runtimeRestrictionCache = new();

    private static readonly string[] professionRestrictionText =
    [
        "",
        "Warrior Restricted",
        "Tradesman Restricted",
        "Explorer Only",
        "Explorer Restricted",
        "Tradesman Only",
        "Warrior Only",
        "Profession Restricted",
    ];

    private static readonly string[] raceRestrictionText =
    [
        "",
        "Terran Restricted",
        "Jenquai Restricted",
        "Progen Only",
        "Progen Restricted",
        "Jenquai Only",
        "Terran Only",
        "Race Restricted",
    ];

    public static bool IsCompatible(
        int professionRestriction,
        int raceRestriction,
        int loreRestriction,
        SkillPlannerProfessionDefinition profession)
    {
        ArgumentNullException.ThrowIfNull(profession);

        var professionMask = professionRestriction & 0x07;
        var raceMask = NormalizeRaceMask(raceRestriction, loreRestriction);

        return (professionMask & (1 << profession.ProfessionIndex)) == 0 &&
               (raceMask & (1 << profession.RaceIndex)) == 0;
    }

    public static string FormatRestrictionText(
        int professionRestriction,
        int raceRestriction,
        int loreRestriction)
    {
        var professionMask = professionRestriction & 0x07;
        var raceMask = NormalizeRaceMask(raceRestriction, loreRestriction);
        var professionText = professionRestrictionText[professionMask];
        var raceText = raceRestrictionText[raceMask];

        return professionText.Length == 0
            ? raceText
            : raceText.Length == 0
                ? professionText
                : string.Concat(professionText, " · ", raceText);
    }

    public static ItemTemplateRestrictionEvaluation Evaluate(
        ClientRuntimeItemTemplateObservation? template,
        SkillPlannerProfessionDefinition? profession)
    {
        if (template == null)
        {
            return ItemTemplateRestrictionEvaluation.None;
        }

        var restriction = runtimeRestrictionCache.GetValue(
            template,
            CreateRuntimeRestrictionDefinition);

        var professionCompatible = profession == null
            ? default(bool?)
            : (restriction.ProfessionMask &
               (1 << profession.ProfessionIndex)) == 0;
        var raceCompatible = profession == null
            ? default(bool?)
            : (restriction.RaceMask &
               (1 << profession.RaceIndex)) == 0;
        List<ItemTemplateRestrictionLine> lines = [];

        if (!string.IsNullOrWhiteSpace(restriction.ProfessionText))
        {
            lines.Add(
                new ItemTemplateRestrictionLine(
                    restriction.ProfessionText,
                    professionCompatible));
        }

        if (!string.IsNullOrWhiteSpace(restriction.RaceText))
        {
            lines.Add(
                new ItemTemplateRestrictionLine(
                    restriction.RaceText,
                    raceCompatible));
        }

        return new ItemTemplateRestrictionEvaluation(
            restriction.Text,
            profession == null
                ? null
                : professionCompatible != false && raceCompatible != false,
            lines);
    }

    private static RuntimeRestrictionDefinition
        CreateRuntimeRestrictionDefinition(
            ClientRuntimeItemTemplateObservation template)
    {
        var professionMask = 0;
        var raceMask = 0;
        var loreRestriction = 0;

        foreach (var attribute in template.Attributes)
        {
            switch (attribute.ItemInfoId)
            {
                case 0x03:
                    professionMask |=
                        attribute.Int32Value.GetValueOrDefault();
                    break;
                case 0x11:
                    loreRestriction = Math.Max(
                        loreRestriction,
                        attribute.Int32Value.GetValueOrDefault());
                    break;
                case 0x12:
                    raceMask |= attribute.Int32Value.GetValueOrDefault();
                    break;
            }
        }

        professionMask &= 0x07;
        raceMask &= 0x07;
        var normalizedRaceMask =
            NormalizeRaceMask(raceMask, loreRestriction);

        var professionText = professionRestrictionText[professionMask];
        var raceText = raceRestrictionText[normalizedRaceMask];

        return new RuntimeRestrictionDefinition(
            professionMask,
            normalizedRaceMask,
            professionText,
            raceText,
            FormatRestrictionText(
                professionMask,
                raceMask,
                loreRestriction));
    }

    private static int NormalizeRaceMask(
        int raceRestriction,
        int loreRestriction)
    {
        var raceMask = raceRestriction & 0x07;
        raceMask |= loreRestriction switch
        {
            1 => 1 << 2,
            2 => 1 << 1,
            _ => 0,
        };
        return raceMask;
    }

    private sealed record RuntimeRestrictionDefinition(
        int ProfessionMask,
        int RaceMask,
        string ProfessionText,
        string RaceText,
        string Text);
}

internal sealed record ItemTemplateRestrictionLine(
    string Text,
    bool? IsCompatible);

internal sealed record ItemTemplateRestrictionEvaluation(
    string Text,
    bool? IsCompatible,
    IReadOnlyList<ItemTemplateRestrictionLine> Lines)
{
    public static ItemTemplateRestrictionEvaluation None { get; } =
        new("", null, []);

    public bool IsRestrictedForCharacter => this.IsCompatible == false;
}
