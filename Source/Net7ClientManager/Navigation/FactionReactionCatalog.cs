namespace Net7ClientManager.Navigation;

internal sealed record FactionReactionRule(
    string DefeatedFactionKey,
    float Multiplier,
    bool IsEstimated = false);

internal static class FactionReactionCatalog
{
    public const string Revision =
        "net7-wiki-improved-faction-chart-2026-07-19";

    // Source: Net-7 Wiki "Improved Faction Chart", revision visible on
    // 2026-07-19. Rows are the faction whose reputation changes. Columns are
    // the primary faction of the defeated mob. Multipliers are relative to the
    // absolute primary-faction loss and the game truncates fractional results.
    // The wiki marks non-diagonal Alliance-column magnitudes as estimates
    // because no Alliance-faction mobs are currently known.
    private static readonly IReadOnlyDictionary<
        string,
        IReadOnlyList<FactionReactionRule>> RulesByAffectedFaction =
        new Dictionary<string, IReadOnlyList<FactionReactionRule>>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["Anseria"] =
            [
                new("Anseria", -1.0f),
                new("PW", 0.5f),
                new("TW", 0.5f),
                new("TerranPsi", -0.5f),
                new("JE", 0.5f),
            ],
            ["Bogeril"] =
            [
                new("Bogeril", -1.0f),
                new("TW", 0.5f),
            ],
            ["PW"] =
            [
                new("PW", -1.0f),
                new("PT", -0.5f),
                new("TT", -0.5f),
                new("PE", -0.5f),
                new("JE", 0.5f),
                new("Mordana", 0.5f),
                new("Pirate", 0.5f),
                new("V'Rix", 0.5f),
            ],
            ["CHVZ"] =
            [
                new("CHVZ", -1.0f),
                new("Pirate", 0.5f),
            ],
            ["PT"] =
            [
                new("PW", -0.5f),
                new("PT", -1.0f),
                new("PE", 0.5f),
                new("JT", 0.5f),
                new("Mordana", 0.5f),
                new("Pirate", 0.5f),
                new("V'Rix", 0.5f),
            ],
            ["TW"] =
            [
                new("Anseria", 0.5f),
                new("Bogeril", 0.5f),
                new("TW", -1.0f),
                new("TT", -0.5f),
                new("TerranPsi", -0.5f),
                new("Alliance", -0.5f, IsEstimated: true),
                new("V'Rix", 0.5f),
            ],
            ["GC"] =
            [
                new("GC", -1.0f),
            ],
            ["TE"] =
            [
                new("Bogeril", 0.5f),
                new("TW", -0.5f),
                new("TE", -1.0f),
                new("TT", 0.5f),
                new("PE", -0.5f),
                new("JE", -0.5f),
                new("V'Rix", 0.5f),
            ],
            ["TT"] =
            [
                new("Anseria", 0.5f),
                new("Bogeril", 0.5f),
                new("PW", -0.5f),
                new("TW", -0.5f),
                new("TE", 0.5f),
                new("TT", -1.0f),
                new("TerranPsi", 0.5f),
                new("JE", 0.5f),
                new("Alliance", 0.5f, IsEstimated: true),
                new("V'Rix", 0.5f),
            ],
            ["TerranPsi"] =
            [
                new("PW", 0.5f),
                new("TE", -0.5f),
                new("TT", 0.5f),
                new("TerranPsi", -1.0f),
                new("JT", -0.5f),
                new("Alliance", -0.5f, IsEstimated: true),
                new("Mordana", 0.5f),
                new("Pirate", -0.5f),
                new("V'Rix", 0.5f),
            ],
            ["PE"] =
            [
                new("PW", -0.5f),
                new("PT", 0.5f),
                new("TE", -0.5f),
                new("PE", -1.0f),
                new("Mordana", 0.5f),
                new("V'Rix", 0.5f),
            ],
            ["JE"] =
            [
                new("TerranPsi", -0.5f),
                new("JE", -1.0f),
                new("JT", -0.5f),
                new("V'Rix", 0.5f),
            ],
            ["JT"] =
            [
                new("PT", 0.5f),
                new("TerranPsi", -0.5f),
                new("JE", -0.5f),
                new("JT", -1.0f),
                new("V'Rix", 0.5f),
            ],
            ["JW"] =
            [
                new("JT", -0.5f),
                new("JW", -1.0f),
                new("Mordana", 0.5f),
                new("V'Rix", 0.5f),
            ],
            ["Alliance"] =
            [
                new("PW", 0.5f),
                new("TW", -0.5f),
                new("TT", 0.5f),
                new("TerranPsi", -0.5f),
                new("Alliance", -1.0f),
                new("Pirate", -0.5f),
                new("V'Rix", 0.5f),
            ],
            ["Mordana"] =
            [
                new("PW", 0.5f),
                new("PT", 0.5f),
                new("TW", 0.5f),
                new("TerranPsi", 0.5f),
                new("JW", 0.5f),
                new("Alliance", -0.5f, IsEstimated: true),
                new("Mordana", -1.0f),
                new("V'Rix", -0.5f),
            ],
            ["Pirate"] =
            [
                new("PW", 0.5f),
                new("CHVZ", 0.5f),
                new("PT", 0.5f),
                new("TerranPsi", -0.5f),
                new("Alliance", -0.5f, IsEstimated: true),
                new("Pirate", -1.0f),
                new("V'Rix", 1.0f),
            ],
            ["V'Rix"] =
            [
                new("Bogeril", 0.5f),
                new("PW", 0.5f),
                new("PT", 0.5f),
                new("TW", 0.5f),
                new("TE", 0.5f),
                new("TT", 0.5f),
                new("TerranPsi", 0.5f),
                new("PE", 0.5f),
                new("JT", 0.5f),
                new("JW", 0.5f),
                new("Alliance", 0.5f, IsEstimated: true),
                new("Mordana", -0.5f),
                new("Pirate", 1.0f),
                new("V'Rix", -1.0f),
            ],
            ["PC"] =
            [
                new("PC", -1.0f),
                new("TA", 0.5f),
            ],
            ["TA"] =
            [
                new("PC", 0.5f),
                new("TA", -1.0f),
            ],
        };

    public static IReadOnlyList<FactionReactionRule> GetRules(
        string? affectedFactionKey)
    {
        if (string.IsNullOrWhiteSpace(affectedFactionKey))
        {
            return [];
        }

        return RulesByAffectedFaction.TryGetValue(
                affectedFactionKey.Trim(),
                out var rules)
            ? rules
            : [];
    }
}
