namespace Net7ClientManager.Navigation;

public static class NavigationWormholeCatalog
{
    public const string CreateWormholeFamilyName = "Create Wormhole";
    public const string ExtendedWormholeFamilyName = "Extended Wormhole";

    private static readonly IReadOnlyList<NavigationWormholeDestination>
        destinations =
        [
            Destination(CreateWormholeFamilyName, "Kailaasa Gate", "kailaasa", 1, 0),
            Destination(CreateWormholeFamilyName, "Jupiter Gate", "jupiter", 2, 1),
            Destination(CreateWormholeFamilyName, "Swooping Eagle Gate", "swooping-eagle", 3, 2),
            Destination(CreateWormholeFamilyName, "Valkyrie Twins Gate", "valkyrie-twins", 4, 3),
            Destination(CreateWormholeFamilyName, "Asteroid Belt Beta Gate", "asteroid-belt-beta", 5, 4),
            Destination(CreateWormholeFamilyName, "Carpenter Gate", "carpenter", 6, 5),
            Destination(CreateWormholeFamilyName, "Endriago Gate", "endriago", 7, 6),
            Destination(ExtendedWormholeFamilyName, "Witberg Gate", "witberg", 1, 0),
            Destination(ExtendedWormholeFamilyName, "Vishao's Cove Gate", "vishaos-cove", 2, 1),
            Destination(ExtendedWormholeFamilyName, "Venus Gate", "venus", 3, 2),
        ];

    public static IReadOnlyList<NavigationWormholeDestination> Destinations =>
        destinations;

    public static IReadOnlyList<NavigationWormholeDestination>
        GetUnlockedDestinations(
            string? skillFamilyName,
            int rank)
    {
        if (rank <= 0 || string.IsNullOrWhiteSpace(skillFamilyName))
        {
            return [];
        }

        return
        [
            .. destinations.Where(destination =>
                FamilyMatches(destination.SkillFamilyName, skillFamilyName) &&
                destination.RequiredRank <= rank),
        ];
    }

    public static bool TryGetBySectorKey(
        string? sectorKey,
        out NavigationWormholeDestination destination)
    {
        destination = destinations.FirstOrDefault(candidate =>
            string.Equals(
                candidate.SectorKey,
                sectorKey,
                StringComparison.Ordinal))!;

        return destination != null;
    }

    public static bool AbilityMatches(
        NavigationWormholeDestination destination,
        string? observedAbilityName)
    {
        ArgumentNullException.ThrowIfNull(destination);

        if (string.IsNullOrWhiteSpace(observedAbilityName))
        {
            return false;
        }

        var expected = NormalizeAbilityName(destination.AbilityName);
        var observed = NormalizeAbilityName(observedAbilityName);

        return string.Equals(expected, observed, StringComparison.Ordinal) ||
               observed.EndsWith(
                   string.Concat(" ", expected),
                   StringComparison.Ordinal) ||
               expected.EndsWith(
                   string.Concat(" ", observed),
                   StringComparison.Ordinal);
    }

    public static bool FamilyMatches(
        string? expectedFamilyName,
        string? observedFamilyName)
    {
        return !string.IsNullOrWhiteSpace(expectedFamilyName) &&
               !string.IsNullOrWhiteSpace(observedFamilyName) &&
               string.Equals(
                   GalaxyTopology.NormalizeName(expectedFamilyName),
                   GalaxyTopology.NormalizeName(observedFamilyName),
                   StringComparison.Ordinal);
    }

    private static NavigationWormholeDestination Destination(
        string family,
        string ability,
        string sectorKey,
        int rank,
        int menuIndex)
    {
        return new NavigationWormholeDestination
        {
            SkillFamilyName = family,
            AbilityName = ability,
            SectorKey = sectorKey,
            RequiredRank = rank,
            MenuIndex = menuIndex,
        };
    }

    private static string NormalizeAbilityName(string value)
    {
        var normalized = GalaxyTopology.NormalizeName(value);

        if (normalized.EndsWith(" gate", StringComparison.Ordinal))
        {
            normalized = normalized[..^5].TrimEnd();
        }

        return normalized;
    }
}
