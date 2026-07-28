namespace Net7ClientManager.Navigation;

public sealed class GalaxyRoutePlanner(GalaxyTopology topology)
{
    public GalaxyRouteResult FindRoute(
        string? fromSectorNameOrKey,
        string? toSectorNameOrKey,
        string? pilotProfession,
        Func<GalaxySectorDefinition, GalaxySectorDefinition, bool>?
            canTraverse)
    {
        if (!topology.TryResolve(fromSectorNameOrKey, out var source))
        {
            return GalaxyRouteResult.Failure(
                    $"Unknown source sector '{fromSectorNameOrKey}'.");
        }

        if (!topology.TryResolve(toSectorNameOrKey, out var destination))
        {
            return GalaxyRouteResult.Failure(
                    $"Unknown destination sector '{toSectorNameOrKey}'.");
        }

        if (string.Equals(source.Key, destination.Key, StringComparison.Ordinal))
        {
            return GalaxyRouteResult.Success([source]);
        }

        if (!destination.CanEnter(pilotProfession))
        {
            return GalaxyRouteResult.Failure(
                    $"{destination.Name}: {destination.GetAccessRequirementDescription()}.");
        }

        var frontier = new PriorityQueue<string, RoutePriority>();
        var distance = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [source.Key] = 0,
        };

        var previous = new Dictionary<string, string>(StringComparer.Ordinal);
        frontier.Enqueue(
            source.Key,
            new RoutePriority(0, source.Key));

        while (frontier.TryDequeue(out var currentKey, out var priority))
        {
            var currentDistance = priority.Distance;

            if (distance.TryGetValue(currentKey, out var bestDistance) &&
                currentDistance != bestDistance)
            {
                continue;
            }

            if (string.Equals(currentKey, destination.Key, StringComparison.Ordinal))
            {
                return GalaxyRouteResult.Success(
                    BuildRoute(topology, previous, destination.Key));
            }

            var current = topology.GetByKey(currentKey);

            foreach (var neighbourKey in current.Connections)
            {
                var neighbour = topology.GetByKey(neighbourKey);

                if (!neighbour.CanEnter(pilotProfession) ||
                    (canTraverse != null &&
                     !canTraverse(current, neighbour)))
                {
                    continue;
                }

                var candidateDistance = currentDistance + 1;

                if (distance.TryGetValue(neighbourKey, out var knownDistance) &&
                    knownDistance <= candidateDistance)
                {
                    continue;
                }

                distance[neighbourKey] = candidateDistance;
                previous[neighbourKey] = currentKey;
                frontier.Enqueue(
                    neighbourKey,
                    new RoutePriority(
                        candidateDistance,
                        neighbourKey));
            }
        }

        var professionText = string.IsNullOrWhiteSpace(pilotProfession)
            ? "the selected pilot"
            : pilotProfession;

        return GalaxyRouteResult.Failure(
                $"No route from {source.Name} to {destination.Name} is available to {professionText}.");
    }

    public GalaxyRouteDistanceResult FindDistances(
        string? fromSectorNameOrKey,
        string? pilotProfession,
        Func<GalaxySectorDefinition, GalaxySectorDefinition, bool>?
            canTraverse)
    {
        if (!topology.TryResolve(fromSectorNameOrKey, out var source))
        {
            return GalaxyRouteDistanceResult.Failure(
                $"Unknown source sector '{fromSectorNameOrKey}'.");
        }

        var frontier = new PriorityQueue<string, RoutePriority>();
        var distance = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [source.Key] = 0,
        };

        frontier.Enqueue(
            source.Key,
            new RoutePriority(0, source.Key));

        while (frontier.TryDequeue(out var currentKey, out var priority))
        {
            var currentDistance = priority.Distance;

            if (distance.TryGetValue(currentKey, out var bestDistance) &&
                currentDistance != bestDistance)
            {
                continue;
            }

            var current = topology.GetByKey(currentKey);

            foreach (var neighbourKey in current.Connections)
            {
                var neighbour = topology.GetByKey(neighbourKey);

                if (!neighbour.CanEnter(pilotProfession) ||
                    (canTraverse != null &&
                     !canTraverse(current, neighbour)))
                {
                    continue;
                }

                var candidateDistance = currentDistance + 1;

                if (distance.TryGetValue(neighbourKey, out var knownDistance) &&
                    knownDistance <= candidateDistance)
                {
                    continue;
                }

                distance[neighbourKey] = candidateDistance;
                frontier.Enqueue(
                    neighbourKey,
                    new RoutePriority(
                        candidateDistance,
                        neighbourKey));
            }
        }

        return GalaxyRouteDistanceResult.Success(
            source.Key,
            new Dictionary<string, int>(
                distance,
                StringComparer.Ordinal));
    }

    private static IReadOnlyList<GalaxySectorDefinition> BuildRoute(
        GalaxyTopology topology,
        IReadOnlyDictionary<string, string> previous,
        string destinationKey)
    {
        List<GalaxySectorDefinition> route = [];
        var currentKey = destinationKey;

        while (true)
        {
            route.Add(topology.GetByKey(currentKey));

            if (!previous.TryGetValue(currentKey, out var previousKey))
            {
                break;
            }

            currentKey = previousKey;
        }

        route.Reverse();
        return route;
    }

    private readonly record struct RoutePriority(
        int Distance,
        string SectorKey)
        : IComparable<RoutePriority>
    {
        public int CompareTo(RoutePriority other)
        {
            var distanceComparison =
                this.Distance.CompareTo(other.Distance);

            return distanceComparison != 0
                ? distanceComparison
                : string.Compare(
                    this.SectorKey,
                    other.SectorKey,
                    StringComparison.Ordinal);
        }

        public static bool operator <(
            RoutePriority left,
            RoutePriority right)
        {
            return left.CompareTo(right) < 0;
        }

        public static bool operator <=(
            RoutePriority left,
            RoutePriority right)
        {
            return left.CompareTo(right) <= 0;
        }

        public static bool operator >(
            RoutePriority left,
            RoutePriority right)
        {
            return left.CompareTo(right) > 0;
        }

        public static bool operator >=(
            RoutePriority left,
            RoutePriority right)
        {
            return left.CompareTo(right) >= 0;
        }
    }
}

