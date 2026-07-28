namespace Net7ClientManager.GalaxyKnowledge;

using Net7ClientManager.Navigation;

internal sealed record GalaxyFinderResolvedRoute(
    NavigationDestination Destination,
    GalaxyItemSourceKnowledge Source,
    GalaxyItemSourceLocationKnowledge Location,
    string Description,
    int? Hops,
    int HopSortValue,
    bool UsesExactTarget);

internal static class GalaxyFinderRouteResolver
{
    public static GalaxyFinderResolvedRoute? FindBestRoute(
        GalaxyDataSet dataSet,
        GalaxyItemKnowledge item,
        GalaxyRouteDistanceResult distances)
    {
        ArgumentNullException.ThrowIfNull(dataSet);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(distances);

        return item.Sources
            .SelectMany(source => FindRoutes(dataSet, source, distances))
            .OrderBy(route => route.HopSortValue)
            .ThenBy(route => route.UsesExactTarget ? 0 : 1)
            .ThenBy(route => route.Source.Kind)
            .ThenBy(
                route => route.Source.SourceName,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(
                route => route.Destination.DisplayName,
                StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    public static GalaxyFinderResolvedRoute? FindBestRoute(
        GalaxyDataSet dataSet,
        GalaxyItemSourceKnowledge source,
        GalaxyRouteDistanceResult distances)
    {
        ArgumentNullException.ThrowIfNull(dataSet);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(distances);

        return FindRoutes(dataSet, source, distances)
            .FirstOrDefault();
    }

    public static IReadOnlyList<GalaxyFinderResolvedRoute> FindRoutes(
        GalaxyDataSet dataSet,
        GalaxyItemSourceKnowledge source,
        GalaxyRouteDistanceResult distances)
    {
        ArgumentNullException.ThrowIfNull(dataSet);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(distances);

        return
        [
            .. BuildRouteCandidates(dataSet, source, distances)
                .OrderBy(route => route.HopSortValue)
                .ThenBy(route => route.UsesExactTarget ? 0 : 1)
                .ThenBy(
                    route => route.Destination.SystemName,
                    StringComparer.OrdinalIgnoreCase)
                .ThenBy(
                    route => route.Destination.SectorName,
                    StringComparer.OrdinalIgnoreCase)
                .ThenBy(
                    route => route.Destination.TargetName,
                    StringComparer.OrdinalIgnoreCase),
        ];
    }

    private static IEnumerable<GalaxyFinderResolvedRoute>
        BuildRouteCandidates(
            GalaxyDataSet dataSet,
            GalaxyItemSourceKnowledge source,
            GalaxyRouteDistanceResult distances)
    {
        IReadOnlyList<GalaxyItemSourceLocationKnowledge> locations =
            source.Locations.Count == 0
                ?
                [
                    new GalaxyItemSourceLocationKnowledge
                    {
                        LocationId = source.SourceEntityId,
                        SectorId = source.SectorId,
                        SectorKey = source.SectorKey,
                        SectorName = source.SectorName,
                        LocationName = source.LocationName,
                    },
                ]
                : source.Locations;

        HashSet<string> destinationKeys = new(StringComparer.Ordinal);

        foreach (var location in locations)
        {
            var sectorKey = !string.IsNullOrWhiteSpace(location.SectorKey)
                ? location.SectorKey
                : source.SectorKey;

            if (!dataSet.Topology.TryGetByKey(sectorKey, out var sector))
            {
                continue;
            }

            var destination = ResolveDestination(
                dataSet,
                sector,
                source,
                location,
                out var usesExactTarget);
            var destinationKey = BuildDestinationKey(destination);

            if (!destinationKeys.Add(destinationKey))
            {
                continue;
            }

            int? hops = null;
            if (distances.Succeeded &&
                distances.TryGetDistance(sector.Key, out var distance))
            {
                hops = distance;
            }

            yield return new GalaxyFinderResolvedRoute(
                destination,
                source,
                location,
                BuildDescription(source, destination),
                hops,
                hops ?? int.MaxValue - 1,
                usesExactTarget);
        }
    }

    private static NavigationDestination ResolveDestination(
        GalaxyDataSet dataSet,
        GalaxySectorDefinition sector,
        GalaxyItemSourceKnowledge source,
        GalaxyItemSourceLocationKnowledge location,
        out bool usesExactTarget)
    {
        usesExactTarget = false;

        if (!dataSet.Catalog.TryGetSector(
                sector.Key,
                out var catalogSector))
        {
            return NavigationDestination.ForSector(sector);
        }

        GalaxyNavigationCatalogTarget? target = source.Kind switch
        {
            GalaxyItemSourceKind.Vendor or
            GalaxyItemSourceKind.MissionReward => FindStationTarget(
                catalogSector,
                string.IsNullOrWhiteSpace(location.LocationName)
                    ? source.LocationName
                    : location.LocationName),
            GalaxyItemSourceKind.MobLoot or
            GalaxyItemSourceKind.Harvesting => FindNearestNavigationPoint(
                catalogSector,
                location),
            _ => null,
        };

        if (target == null)
        {
            return NavigationDestination.ForSector(sector);
        }

        usesExactTarget = true;
        return NavigationDestination.ForTarget(sector, target);
    }

    private static GalaxyNavigationCatalogTarget? FindStationTarget(
        GalaxyNavigationCatalogSector sector,
        string stationName)
    {
        if (string.IsNullOrWhiteSpace(stationName))
        {
            return null;
        }

        var normalizedName = GalaxyTopology.NormalizeName(stationName);

        return sector.Targets
            .Where(target =>
                target.Kind == GalaxyNavigationTargetKind.Station)
            .FirstOrDefault(target =>
                StationNamesMatch(target.Name, normalizedName) ||
                StationNamesMatch(
                    target.MapDisplayName,
                    normalizedName));
    }

    private static bool StationNamesMatch(
        string candidate,
        string normalizedStationName)
    {
        var normalizedCandidate =
            GalaxyTopology.NormalizeName(candidate);

        if (string.Equals(
                normalizedCandidate,
                normalizedStationName,
                StringComparison.Ordinal))
        {
            return true;
        }

        return string.Equals(
            RemoveStationSuffix(normalizedCandidate),
            RemoveStationSuffix(normalizedStationName),
            StringComparison.Ordinal);
    }

    private static string RemoveStationSuffix(string value)
    {
        const string suffix = " station";

        return value.EndsWith(
                suffix,
                StringComparison.Ordinal)
            ? value[..^suffix.Length]
            : value;
    }

    private static GalaxyNavigationCatalogTarget?
        FindNearestNavigationPoint(
            GalaxyNavigationCatalogSector sector,
            GalaxyItemSourceLocationKnowledge location)
    {
        if (location.X is not { } x ||
            location.Y is not { } y ||
            location.Z is not { } z)
        {
            return null;
        }

        return sector.Targets
            .Where(target =>
                target.Kind == GalaxyNavigationTargetKind.NavigationPoint &&
                target.HasPosition)
            .OrderBy(target => DistanceSquared(target, x, y, z))
            .ThenBy(
                target => NavigationDestination.GetTargetDisplayName(target),
                StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static double DistanceSquared(
        GalaxyNavigationCatalogTarget target,
        float x,
        float y,
        float z)
    {
        var deltaX = target.X - x;
        var deltaY = target.Y - y;
        var deltaZ = target.Z - z;
        return (deltaX * deltaX) +
               (deltaY * deltaY) +
               (deltaZ * deltaZ);
    }

    private static string BuildDescription(
        GalaxyItemSourceKnowledge source,
        NavigationDestination destination)
    {
        var sourceKind = source.Kind switch
        {
            GalaxyItemSourceKind.Vendor => "Vendor",
            GalaxyItemSourceKind.MobLoot => "Loot",
            GalaxyItemSourceKind.Harvesting => "Harvesting",
            GalaxyItemSourceKind.MissionReward => "Mission",
            _ => "Source",
        };
        var sourceName = string.IsNullOrWhiteSpace(source.SourceName)
            ? "Unknown source"
            : source.SourceName.Trim();

        return string.Concat(
            sourceKind,
            ": ",
            sourceName,
            " · ",
            destination.DisplayName);
    }

    private static string BuildDestinationKey(
        NavigationDestination destination)
    {
        return string.Concat(
            destination.SectorKey,
            "|",
            destination.TargetKey ?? "",
            "|",
            destination.TargetName ?? "");
    }
}
