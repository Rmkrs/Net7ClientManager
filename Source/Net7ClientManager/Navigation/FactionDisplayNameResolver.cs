namespace Net7ClientManager.Navigation;

internal static class FactionDisplayNameResolver
{
    private static readonly IReadOnlyDictionary<string, string> DisplayNamesByKey =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["TT"] = "InfinitiCorp Traders",
            ["TW"] = "EarthCorps Warriors",
            ["TE"] = "Hyperia Explorers",
            ["PT"] = "Collegia Traders",
            ["PW"] = "Centuriata Warriors",
            ["PE"] = "Sabine Explorers",
            ["JT"] = "Sharim Traders",
            ["JW"] = "Shinwa Warriors",
            ["JE"] = "Sha'ha'dem Explorers",
            ["Kokura"] = "The Kokura",
            ["Mordana"] = "The Mordana",
            ["FS"] = "Free Spacers",
            ["V'Rix"] = "V'Rix",
            ["Anseria"] = "Anseria",
            ["Pirate"] = "The Red Dragon",
            ["Bogeril"] = "Bogeril",
            ["JingLeung"] = "Jing Leung Red Dragons",
            ["GC"] = "Glenn Commission",
            ["N7"] = "Net-7",
            ["TerranPsi"] = "Psionics",
            ["GETCo"] = "Good Earth Trading Company",
            ["Alliance"] = "The Alliance",
            ["PC"] = "Progen Combine",
            ["TA"] = "Terran Alliance",
            ["RP"] = "Renegade Progen",
            ["CHVZ"] = "Chavez",
        };

    private static readonly IReadOnlyDictionary<string, string>
        DisplayNamesByNormalizedKey = DisplayNamesByKey
            .GroupBy(
                pair => Normalize(pair.Key),
                StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.First().Value,
                StringComparer.Ordinal);

    public static string GetDisplayName(string? factionIdentifier)
    {
        if (string.IsNullOrWhiteSpace(factionIdentifier))
        {
            return "";
        }

        var trimmed = factionIdentifier.Trim();

        if (DisplayNamesByKey.TryGetValue(trimmed, out var displayName))
        {
            return displayName;
        }

        if (DisplayNamesByNormalizedKey.TryGetValue(
                Normalize(trimmed),
                out displayName))
        {
            return displayName;
        }

        return trimmed;
    }

    public static string GetDisplayNameOrDefault(
        string? factionIdentifier,
        string fallback)
    {
        var displayName = GetDisplayName(factionIdentifier);
        return string.IsNullOrWhiteSpace(displayName)
            ? fallback
            : displayName;
    }

    private static string Normalize(string value)
    {
        return string.Concat(value.Where(char.IsLetterOrDigit))
            .ToUpperInvariant();
    }
}
