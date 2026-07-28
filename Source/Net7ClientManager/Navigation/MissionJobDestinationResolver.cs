namespace Net7ClientManager.Navigation;

internal static class MissionJobDestinationResolver
{
    public static MissionJobDestinationResolution? Resolve(
        GalaxyDataSet dataSet,
        params string[] objectiveTexts)
    {
        ArgumentNullException.ThrowIfNull(dataSet);
        ArgumentNullException.ThrowIfNull(objectiveTexts);

        foreach (var text in objectiveTexts.Where(value =>
                     !string.IsNullOrWhiteSpace(value)))
        {
            var resolution = ResolveText(
                dataSet,
                text.Trim());

            if (resolution != null)
            {
                return resolution;
            }
        }

        return null;
    }

    private static MissionJobDestinationResolution? ResolveText(
        GalaxyDataSet dataSet,
        string text)
    {
        var normalizedText = GalaxyTopology.NormalizeName(text);
        var sectorMatches = dataSet.Topology.Sectors
            .Where(sector => GetSectorNames(sector)
                .Select(GalaxyTopology.NormalizeName)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Any(value => ContainsPhrase(normalizedText, value)))
            .Distinct()
            .Take(2)
            .ToArray();

        if (sectorMatches.Length > 1)
        {
            // A generated objective can mention both ends of a journey.
            // Do not guess which one is the actionable destination.
            return null;
        }

        if (sectorMatches.Length == 1)
        {
            var sector = sectorMatches[0];
            var destination = MissionWikiLocationResolver.Resolve(
                dataSet,
                new MissionWikiLocationHint
                {
                    PageTitle = sector.Name,
                    Context = text,
                });

            return CreateResolution(
                sector,
                destination is
                {
                    Kind: NavigationDestinationKind.Target,
                    TargetKey: not null,
                }
                    ? destination
                    : null);
        }

        var globalMatches = FindGlobalTargetMatches(
                dataSet,
                normalizedText)
            .Take(2)
            .ToArray();

        return globalMatches.Length == 1
            ? CreateResolution(
                globalMatches[0].Sector,
                NavigationDestination.ForTarget(
                    globalMatches[0].Sector,
                    globalMatches[0].Target))
            : null;
    }

    private static IEnumerable<TargetMatch> FindGlobalTargetMatches(
        GalaxyDataSet dataSet,
        string normalizedText)
    {
        HashSet<string> emittedTargetKeys = new(StringComparer.Ordinal);

        foreach (var sector in dataSet.Topology.Sectors)
        {
            if (!dataSet.Catalog.TryGetSector(
                    sector.Key,
                    out var catalogSector))
            {
                continue;
            }

            foreach (var target in catalogSector.Targets)
            {
                if (!IsRouteable(target) ||
                    !GetTargetNames(target)
                        .Select(GalaxyTopology.NormalizeName)
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Any(value => ContainsPhrase(
                            normalizedText,
                            value)))
                {
                    continue;
                }

                var targetKey = NavigationDestination.CreateTargetKey(
                    sector.Key,
                    target);

                if (emittedTargetKeys.Add(targetKey))
                {
                    yield return new TargetMatch(
                        sector,
                        target);
                }
            }
        }
    }

    private static MissionJobDestinationResolution CreateResolution(
        GalaxySectorDefinition sector,
        NavigationDestination? exactDestination)
    {
        return new MissionJobDestinationResolution
        {
            SectorKey = sector.Key,
            SectorName = sector.Name,
            SystemName = sector.SystemName,
            ExactDestination = exactDestination,
            RouteDestination = exactDestination ??
                NavigationDestination.ForSector(sector),
        };
    }

    private static IEnumerable<string> GetSectorNames(
        GalaxySectorDefinition sector)
    {
        yield return sector.Name;

        foreach (var alias in sector.Aliases)
        {
            yield return alias;
        }
    }

    private static IEnumerable<string> GetTargetNames(
        GalaxyNavigationCatalogTarget target)
    {
        yield return target.Name;
        yield return target.MapDisplayName;
    }

    private static bool ContainsPhrase(
        string normalizedContext,
        string normalizedValue)
    {
        return string.Concat(" ", normalizedContext, " ")
            .Contains(
                string.Concat(" ", normalizedValue, " "),
                StringComparison.Ordinal);
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

    private sealed record TargetMatch(
        GalaxySectorDefinition Sector,
        GalaxyNavigationCatalogTarget Target);
}
