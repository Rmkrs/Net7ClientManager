namespace Net7ClientManager.Navigation;

public sealed class GalaxyRoutePlanner(GalaxyTopology topology)
{
    public GalaxyRouteResult FindRoute(
        string? fromSectorNameOrKey,
        string? toSectorNameOrKey,
        string? pilotProfession,
        Func<GalaxySectorDefinition, GalaxySectorDefinition, bool>?
            canTraverse,
        NavigationWormholeAvailability? wormholes = null)
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

        var previous = new Dictionary<string, GalaxyRouteTransition>(
            StringComparer.Ordinal);
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
                return BuildRoute(previous, destination.Key);
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

                TryEnqueue(
                    neighbour,
                    new GalaxyRouteTransition
                    {
                        Kind = GalaxyRouteTransitionKind.Departure,
                        From = current,
                        To = neighbour,
                    },
                    currentDistance,
                    distance,
                    previous,
                    frontier);
            }

            if (string.Equals(current.Key, source.Key, StringComparison.Ordinal))
            {
                foreach (var wormhole in wormholes?.Destinations ?? [])
                {
                    if (string.Equals(
                            wormhole.Destination.SectorKey,
                            current.Key,
                            StringComparison.Ordinal) ||
                        !topology.TryGetByKey(
                            wormhole.Destination.SectorKey,
                            out var wormholeDestination) ||
                        !wormholeDestination.CanEnter(pilotProfession))
                    {
                        continue;
                    }

                    TryEnqueue(
                        wormholeDestination,
                        new GalaxyRouteTransition
                        {
                            Kind = GalaxyRouteTransitionKind.Wormhole,
                            From = current,
                            To = wormholeDestination,
                            Wormhole = wormhole,
                        },
                        currentDistance,
                        distance,
                        previous,
                        frontier);
                }
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
            canTraverse,
        NavigationWormholeAvailability? wormholes = null)
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

                TryEnqueueDistance(
                    neighbour,
                    currentDistance,
                    distance,
                    frontier);
            }

            if (string.Equals(current.Key, source.Key, StringComparison.Ordinal))
            {
                foreach (var wormhole in wormholes?.Destinations ?? [])
                {
                    if (string.Equals(
                            wormhole.Destination.SectorKey,
                            current.Key,
                            StringComparison.Ordinal) ||
                        !topology.TryGetByKey(
                            wormhole.Destination.SectorKey,
                            out var wormholeDestination) ||
                        !wormholeDestination.CanEnter(pilotProfession))
                    {
                        continue;
                    }

                    TryEnqueueDistance(
                        wormholeDestination,
                        currentDistance,
                        distance,
                        frontier);
                }
            }
        }

        return GalaxyRouteDistanceResult.Success(
            source.Key,
            new Dictionary<string, int>(
                distance,
                StringComparer.Ordinal));
    }

    private static void TryEnqueue(
        GalaxySectorDefinition to,
        GalaxyRouteTransition transition,
        int currentDistance,
        IDictionary<string, int> distance,
        IDictionary<string, GalaxyRouteTransition> previous,
        PriorityQueue<string, RoutePriority> frontier)
    {
        var candidateDistance = currentDistance + 1;

        if (distance.TryGetValue(to.Key, out var knownDistance) &&
            knownDistance <= candidateDistance)
        {
            return;
        }

        distance[to.Key] = candidateDistance;
        previous[to.Key] = transition;
        frontier.Enqueue(
            to.Key,
            new RoutePriority(candidateDistance, to.Key));
    }

    private static void TryEnqueueDistance(
        GalaxySectorDefinition to,
        int currentDistance,
        IDictionary<string, int> distance,
        PriorityQueue<string, RoutePriority> frontier)
    {
        var candidateDistance = currentDistance + 1;

        if (distance.TryGetValue(to.Key, out var knownDistance) &&
            knownDistance <= candidateDistance)
        {
            return;
        }

        distance[to.Key] = candidateDistance;
        frontier.Enqueue(
            to.Key,
            new RoutePriority(candidateDistance, to.Key));
    }

    private GalaxyRouteResult BuildRoute(
        IReadOnlyDictionary<string, GalaxyRouteTransition> previous,
        string destinationKey)
    {
        List<GalaxyRouteTransition> transitions = [];
        var currentKey = destinationKey;

        while (previous.TryGetValue(currentKey, out var transition))
        {
            transitions.Add(transition);
            currentKey = transition.From.Key;
        }

        transitions.Reverse();

        List<GalaxySectorDefinition> sectors = [];

        if (transitions.Count == 0)
        {
            sectors.Add(topology.GetByKey(destinationKey));
        }
        else
        {
            sectors.Add(transitions[0].From);
            sectors.AddRange(transitions.Select(transition => transition.To));
        }

        return GalaxyRouteResult.Success(sectors, transitions);
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
