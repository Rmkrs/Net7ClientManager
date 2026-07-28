namespace Net7ClientManager.Navigation;

internal static class MissionWikiLocationResolver
{
    public static NavigationDestination? Resolve(
        GalaxyDataSet dataSet,
        MissionWikiLocationHint hint)
    {
        ArgumentNullException.ThrowIfNull(dataSet);
        ArgumentNullException.ThrowIfNull(hint);

        if (TryResolveSector(dataSet.Topology, hint, out var sector))
        {
            return TryResolveTargetInSector(
                    dataSet.Catalog,
                    sector,
                    hint.Context,
                    out var target)
                ? NavigationDestination.ForTarget(sector, target)
                : NavigationDestination.ForSector(sector);
        }

        return TryResolveUniqueExplicitTarget(
                dataSet,
                hint,
                out sector,
                out var explicitTarget)
            ? NavigationDestination.ForTarget(sector, explicitTarget)
            : null;
    }

    private static bool TryResolveSector(
        GalaxyTopology topology,
        MissionWikiLocationHint hint,
        out GalaxySectorDefinition sector)
    {
        return topology.TryResolve(hint.PageTitle, out sector) ||
               topology.TryResolve(hint.LinkText, out sector);
    }

    private static bool TryResolveTargetInSector(
        GalaxyNavigationCatalog catalog,
        GalaxySectorDefinition sector,
        string context,
        out GalaxyNavigationCatalogTarget target)
    {
        target = null!;

        if (string.IsNullOrWhiteSpace(context) ||
            !catalog.TryGetSector(sector.Key, out var catalogSector))
        {
            return false;
        }

        var normalizedContext = GalaxyTopology.NormalizeName(context);
        var excludedNames = new[]
            {
                sector.Name,
                sector.SystemName,
            }
            .Concat(sector.Aliases)
            .Select(GalaxyTopology.NormalizeName)
            .ToHashSet(StringComparer.Ordinal);
        List<TargetMatch> matches = [];

        foreach (var candidate in catalogSector.Targets)
        {
            if (!IsRouteable(candidate))
            {
                continue;
            }

            foreach (var name in GetTargetNames(candidate))
            {
                var normalizedName = GalaxyTopology.NormalizeName(name);

                if (string.IsNullOrWhiteSpace(normalizedName) ||
                    excludedNames.Contains(normalizedName))
                {
                    continue;
                }

                var quality = GetMatchQuality(
                    context,
                    normalizedContext,
                    name,
                    normalizedName);

                if (quality == 0)
                {
                    continue;
                }

                matches.Add(
                    new TargetMatch(
                        candidate,
                        quality,
                        CountTokens(normalizedName),
                        normalizedName.Length,
                        GetKindPriority(candidate.Kind)));
            }
        }

        if (matches.Count == 0)
        {
            return false;
        }

        var ordered = matches
            .OrderByDescending(match => match.Quality)
            .ThenByDescending(match => match.TokenCount)
            .ThenByDescending(match => match.NameLength)
            .ThenByDescending(match => match.KindPriority)
            .ToArray();
        var best = ordered[0];
        var equallyStrongTargets = ordered
            .Where(match =>
                match.Quality == best.Quality &&
                match.TokenCount == best.TokenCount &&
                match.NameLength == best.NameLength &&
                match.KindPriority == best.KindPriority)
            .Select(match => match.Target)
            .Distinct()
            .Take(2)
            .Count();

        if (equallyStrongTargets > 1)
        {
            return false;
        }

        target = best.Target;
        return true;
    }

    private static bool TryResolveUniqueExplicitTarget(
        GalaxyDataSet dataSet,
        MissionWikiLocationHint hint,
        out GalaxySectorDefinition sector,
        out GalaxyNavigationCatalogTarget target)
    {
        sector = null!;
        target = null!;

        var explicitNames = new[]
            {
                hint.PageTitle,
                hint.LinkText,
            }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(GalaxyTopology.NormalizeName)
            .ToHashSet(StringComparer.Ordinal);

        if (explicitNames.Count == 0)
        {
            return false;
        }

        List<ExplicitTargetMatch> matches = [];

        foreach (var candidateSector in dataSet.Topology.Sectors)
        {
            if (!dataSet.Catalog.TryGetSector(
                    candidateSector.Key,
                    out var catalogSector))
            {
                continue;
            }

            foreach (var candidate in catalogSector.Targets)
            {
                if (!IsRouteable(candidate) ||
                    !GetTargetNames(candidate)
                        .Select(GalaxyTopology.NormalizeName)
                        .Any(explicitNames.Contains))
                {
                    continue;
                }

                var key = NavigationDestination.CreateTargetKey(
                    candidateSector.Key,
                    candidate);

                if (matches.Any(match =>
                        string.Equals(
                            match.Key,
                            key,
                            StringComparison.Ordinal)))
                {
                    continue;
                }

                matches.Add(
                    new ExplicitTargetMatch(
                        candidateSector,
                        candidate,
                        key));
            }
        }

        if (matches.Count != 1)
        {
            return false;
        }

        sector = matches[0].Sector;
        target = matches[0].Target;
        return true;
    }

    private static int GetMatchQuality(
        string context,
        string normalizedContext,
        string originalName,
        string normalizedName)
    {
        if (!normalizedName.Contains(' '))
        {
            return normalizedName.Length >= 5 &&
                   ContainsCaseSensitiveWord(
                       context,
                       originalName.Trim())
                ? 2
                : 0;
        }

        if (ContainsPhrase(normalizedContext, normalizedName))
        {
            return 2;
        }

        return ContainsConservativeFuzzyPhrase(
            normalizedContext,
            normalizedName)
                ? 1
                : 0;
    }

    private static bool ContainsPhrase(
        string normalizedContext,
        string normalizedName)
    {
        return string.Concat(" ", normalizedContext, " ")
            .Contains(
                string.Concat(" ", normalizedName, " "),
                StringComparison.Ordinal);
    }

    private static bool ContainsConservativeFuzzyPhrase(
        string normalizedContext,
        string normalizedName)
    {
        var contextTokens = Tokenize(normalizedContext);
        var nameTokens = Tokenize(normalizedName);

        if (nameTokens.Length < 2 ||
            contextTokens.Length < nameTokens.Length)
        {
            return false;
        }

        for (var start = 0;
             start <= contextTokens.Length - nameTokens.Length;
             start++)
        {
            var correctionUsed = false;
            var matched = true;

            for (var offset = 0; offset < nameTokens.Length; offset++)
            {
                var expected = nameTokens[offset];
                var observed = contextTokens[start + offset];

                if (string.Equals(
                        expected,
                        observed,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                if (correctionUsed ||
                    expected.Length < 5 ||
                    observed.Length < 5 ||
                    !IsSingleEditApart(expected, observed))
                {
                    matched = false;
                    break;
                }

                correctionUsed = true;
            }

            if (matched)
            {
                return true;
            }
        }

        return false;
    }

    private static string[] Tokenize(string value)
    {
        return value
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(token =>
                !string.Equals(token, "sic", StringComparison.Ordinal))
            .Select(token => token == "centre" ? "center" : token)
            .ToArray();
    }

    private static bool IsSingleEditApart(string left, string right)
    {
        if (Math.Abs(left.Length - right.Length) > 1)
        {
            return false;
        }

        if (left.Length == right.Length)
        {
            return left
                .Zip(right)
                .Count(pair => pair.First != pair.Second) == 1;
        }

        var shorter = left.Length < right.Length ? left : right;
        var longer = left.Length < right.Length ? right : left;
        var shorterIndex = 0;
        var longerIndex = 0;
        var correctionUsed = false;

        while (shorterIndex < shorter.Length &&
               longerIndex < longer.Length)
        {
            if (shorter[shorterIndex] == longer[longerIndex])
            {
                shorterIndex++;
                longerIndex++;
                continue;
            }

            if (correctionUsed)
            {
                return false;
            }

            correctionUsed = true;
            longerIndex++;
        }

        return true;
    }

    private static bool ContainsCaseSensitiveWord(
        string context,
        string value)
    {
        var searchFrom = 0;

        while (searchFrom < context.Length)
        {
            var index = context.IndexOf(
                value,
                searchFrom,
                StringComparison.Ordinal);

            if (index < 0)
            {
                return false;
            }

            var beforeIsBoundary = index == 0 ||
                                   !char.IsLetterOrDigit(
                                       context[index - 1]);
            var after = index + value.Length;
            var afterIsBoundary = after >= context.Length ||
                                  !char.IsLetterOrDigit(context[after]);

            if (beforeIsBoundary && afterIsBoundary)
            {
                return true;
            }

            searchFrom = after;
        }

        return false;
    }

    private static IReadOnlyList<string> GetTargetNames(
        GalaxyNavigationCatalogTarget target)
    {
        return new[]
            {
                target.Name,
                target.MapDisplayName,
            }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsRouteable(
        GalaxyNavigationCatalogTarget target)
    {
        return target.Kind is
            GalaxyNavigationTargetKind.Station or
            GalaxyNavigationTargetKind.NavigationPoint or
            GalaxyNavigationTargetKind.SectorGate or
            GalaxyNavigationTargetKind.Planet or
            GalaxyNavigationTargetKind.Asteroid;
    }

    private static int GetKindPriority(
        GalaxyNavigationTargetKind kind)
    {
        return kind switch
        {
            GalaxyNavigationTargetKind.Station => 5,
            GalaxyNavigationTargetKind.NavigationPoint => 4,
            GalaxyNavigationTargetKind.SectorGate => 3,
            GalaxyNavigationTargetKind.Planet => 2,
            GalaxyNavigationTargetKind.Asteroid => 1,
            _ => 0,
        };
    }

    private static int CountTokens(string value)
    {
        return value.Count(character => character == ' ') + 1;
    }

    private sealed record TargetMatch(
        GalaxyNavigationCatalogTarget Target,
        int Quality,
        int TokenCount,
        int NameLength,
        int KindPriority);

    private sealed record ExplicitTargetMatch(
        GalaxySectorDefinition Sector,
        GalaxyNavigationCatalogTarget Target,
        string Key);
}
