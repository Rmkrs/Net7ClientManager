namespace Net7ClientManager.Navigation;

using Net7ClientManager.Observations;
using Net7ClientManager.Observations.Models;

public sealed class NavigationRouteCoordinator
{
    private const string CharacterEnvironmentKey = "net7";
    private const float PreciseLocationMaximumSurfaceDistance = 5000.0f;

    private readonly System.Threading.Lock lockObject = new();
    private GalaxyRoutePlanner routePlanner;
    private readonly NavigationRouteStore store = new();
    private readonly Dictionary<string, NavigationPersistedRoute> routesByCharacter;
    private readonly Dictionary<int, ProcessRouteState> processStates = [];
    private string persistenceWarning = "";

    public NavigationRouteCoordinator(
        GalaxyTopology topology,
        GalaxyNavigationCatalog catalog)
    {
        this.Topology = topology;
        this.Catalog = catalog;
        this.routePlanner = new GalaxyRoutePlanner(topology);
        this.routesByCharacter = this.store
            .Load()
            .ToDictionary(
                item => item.Key,
                item => item.Value,
                StringComparer.Ordinal);
    }

    public event EventHandler<NavigationRouteChangedEventArgs>? RouteChanged;

    public GalaxyTopology Topology { get; private set; }

    public GalaxyNavigationCatalog Catalog { get; private set; }

    public void UpdateData(
        GalaxyTopology topology,
        GalaxyNavigationCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(catalog);

        lock (this.lockObject)
        {
            this.Topology = topology;
            this.Catalog = catalog;
            this.routePlanner = new GalaxyRoutePlanner(topology);
            this.processStates.Clear();
        }
    }

    public NavigationRouteSnapshot Observe(
        ClientObservationSnapshot observation,
        ClientLiveCharacterIdentity identity,
        NavigationWormholeAvailability? wormholes = null)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(identity);
        wormholes ??= NavigationWormholeAvailability.None;

        lock (this.lockObject)
        {
            if (observation.LifecycleState !=
                    ClientLifecycleState.InGame ||
                string.IsNullOrWhiteSpace(identity.Name))
            {
                var unavailable = NavigationRouteSnapshot.Unavailable(
                    observation.ProcessId,
                    string.IsNullOrWhiteSpace(identity.Name)
                        ? "Live character name is unavailable"
                        : "Navigation routes are available only in game",
                    observation.ObservedAt);

                this.processStates[observation.ProcessId] =
                    new ProcessRouteState
                    {
                        Snapshot = unavailable,
                    };

                return unavailable;
            }

            if (!this.TryResolveCurrentSector(
                    observation,
                    out var currentSector,
                    out var locationError))
            {
                var characterKey2 = CreateCharacterKey(identity.Name);
                var unavailable = NavigationRouteSnapshot.Unavailable(
                    observation.ProcessId,
                    locationError,
                    observation.ObservedAt) with
                {
                    CharacterKey = characterKey2,
                    CharacterName = identity.Name,
                    Profession = identity.Profession,
                    CurrentPilotReputation = observation.LocalPlayer.Reputation,
                    HasRoute = this.routesByCharacter.ContainsKey(
                        characterKey2),
                };

                this.processStates[observation.ProcessId] =
                    new ProcessRouteState
                    {
                        CharacterKey = unavailable.CharacterKey,
                        CharacterName = identity.Name,
                        Profession = identity.Profession,
                        Snapshot = unavailable,
                    };

                return unavailable;
            }

            var currentLocation = this.ResolveCurrentLocation(
                observation,
                currentSector);
            var characterKey = CreateCharacterKey(identity.Name);
            var changed = false;

            if (this.routesByCharacter.TryGetValue(
                    characterKey,
                    out var persisted))
            {
                var visited = persisted.VisitedSectorKeys.ToList();

                if (visited.Count == 0)
                {
                    visited.Add(currentSector.Key);
                    changed = true;
                }
                else if (!string.Equals(
                             visited[^1],
                             currentSector.Key,
                             StringComparison.Ordinal))
                {
                    visited.Add(currentSector.Key);
                    changed = true;
                }

                if (!string.Equals(
                        persisted.CharacterName,
                        identity.Name,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        persisted.LastKnownSectorKey,
                        currentSector.Key,
                        StringComparison.Ordinal))
                {
                    changed = true;
                }

                if (changed)
                {
                    persisted = persisted with
                    {
                        CharacterName = identity.Name,
                        UpdatedAt = observation.ObservedAt,
                        VisitedSectorKeys = visited,
                        LastKnownSectorKey = currentSector.Key,
                    };

                    this.routesByCharacter[characterKey] = persisted;
                    _ = this.TrySaveRoutes(out _);
                }
            }

            var snapshot = this.BuildSnapshot(
                observation.ProcessId,
                observation.ObservedAt,
                characterKey,
                identity.Name,
                identity.Profession,
                currentSector,
                persisted,
                observation.LocalPlayer.Reputation,
                wormholes);

            this.processStates[observation.ProcessId] =
                new ProcessRouteState
                {
                    CharacterKey = characterKey,
                    CharacterName = identity.Name,
                    Profession = identity.Profession,
                    CurrentSector = currentSector,
                    CurrentLocation = currentLocation,
                    Environment = observation.World.Environment,
                    Wormholes = wormholes,
                    Snapshot = snapshot,
                };

            return snapshot;
        }
    }

    public NavigationRouteSnapshot GetSnapshot(int processId)
    {
        lock (this.lockObject)
        {
            return this.processStates.TryGetValue(
                    processId,
                    out var state)
                ? state.Snapshot
                : NavigationRouteSnapshot.Unavailable(
                    processId,
                    "No live navigation context is available");
        }
    }

    public NavigationDestination? GetCurrentLocation(int processId)
    {
        lock (this.lockObject)
        {
            return this.processStates.TryGetValue(
                    processId,
                    out var state)
                ? state.CurrentLocation
                : null;
        }
    }

    public GalaxyRouteDistanceResult GetDistances(int processId)
    {
        lock (this.lockObject)
        {
            if (!this.TryGetAvailableProcessState(
                    processId,
                    out var state,
                    out var error))
            {
                return GalaxyRouteDistanceResult.Failure(error);
            }

            return this.routePlanner.FindDistances(
                state.CurrentSector!.Key,
                state.Profession,
                (from, to) =>
                    this.Catalog.TryGetVerifiedDeparture(
                        from.Key,
                        to.Key,
                        out _) &&
                    (!string.IsNullOrWhiteSpace(state.Profession) ||
                     string.IsNullOrWhiteSpace(
                         to.RequiredProfession)),
                state.Wormholes);
        }
    }

    public NavigationRoutePlanResult PreviewRoute(
        int processId,
        NavigationDestination destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        lock (this.lockObject)
        {
            if (!this.TryGetAvailableProcessState(
                    processId,
                    out var state,
                    out var error))
            {
                return NavigationRoutePlanResult.Failure(error);
            }

            if (!this.TryValidateDestination(
                    destination,
                    out var validatedDestination,
                    out error))
            {
                return NavigationRoutePlanResult.Failure(error);
            }

            var now = DateTimeOffset.UtcNow;
            var preview = new NavigationPersistedRoute
            {
                CharacterKey = state.CharacterKey!,
                CharacterName = state.CharacterName!,
                RouteId = Guid.Empty,
                CreatedAt = now,
                UpdatedAt = now,
                OriginSectorKey = state.CurrentSector!.Key,
                OriginDestination = state.CurrentLocation!,
                Destination = validatedDestination,
                VisitedSectorKeys = [state.CurrentSector!.Key],
                LastKnownSectorKey = state.CurrentSector!.Key,
            };

            return this.BuildPlan(
                preview,
                state.Profession,
                state.CurrentSector!,
                state.Wormholes);
        }
    }

    public NavigationRouteCommandResult SetDestination(
        int processId,
        NavigationDestination destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        NavigationRouteSnapshot snapshot;

        lock (this.lockObject)
        {
            if (!this.TryGetAvailableProcessState(
                    processId,
                    out var state,
                    out var error))
            {
                return NavigationRouteCommandResult.Failure(error);
            }

            if (!this.TryValidateDestination(
                    destination,
                    out var validatedDestination,
                    out error))
            {
                return NavigationRouteCommandResult.Failure(error);
            }

            var now = DateTimeOffset.UtcNow;
            var route = new NavigationPersistedRoute
            {
                CharacterKey = state.CharacterKey!,
                CharacterName = state.CharacterName!,
                RouteId = Guid.NewGuid(),
                CreatedAt = now,
                UpdatedAt = now,
                OriginSectorKey = state.CurrentSector!.Key,
                OriginDestination = state.CurrentLocation!,
                Destination = validatedDestination,
                VisitedSectorKeys = [state.CurrentSector!.Key],
                LastKnownSectorKey = state.CurrentSector!.Key,
            };

            var planResult = this.BuildPlan(
                route,
                state.Profession,
                state.CurrentSector!,
                state.Wormholes);

            if (!planResult.Succeeded || planResult.Plan == null)
            {
                return NavigationRouteCommandResult.Failure(
                    planResult.Error);
            }

            var hadPreviousRoute = this.routesByCharacter.TryGetValue(
                route.CharacterKey,
                out var previousRoute);

            this.routesByCharacter[route.CharacterKey] = route;

            if (!this.TrySaveRoutes(out var persistenceError))
            {
                if (hadPreviousRoute)
                {
                    this.routesByCharacter[route.CharacterKey] =
                        previousRoute!;
                }
                else
                {
                    this.routesByCharacter.Remove(route.CharacterKey);
                }

                return NavigationRouteCommandResult.Failure(
                    persistenceError);
            }

            snapshot = state.Snapshot = this.BuildSnapshot(
                processId,
                now,
                route.CharacterKey,
                route.CharacterName,
                state.Profession,
                state.CurrentSector!,
                route,
                state.Snapshot.CurrentPilotReputation,
                state.Wormholes);
        }

        this.RouteChanged?.Invoke(
            this,
            new NavigationRouteChangedEventArgs(snapshot));

        return NavigationRouteCommandResult.Success(snapshot);
    }

    internal bool IsCurrentDestinationReached(
        int processId,
        Guid routeId)
    {
        lock (this.lockObject)
        {
            if (!this.processStates.TryGetValue(
                    processId,
                    out var state) ||
                !state.Snapshot.IsAvailable)
            {
                return false;
            }

            var currentLocation = state.CurrentLocation;
            var route = state.Snapshot.Route;

            return currentLocation != null &&
                route != null &&
                route.RouteId == routeId &&
                IsDestinationReached(
                    currentLocation,
                    route.Destination,
                    state.Environment);
        }
    }

    internal static bool IsDestinationReached(
        NavigationDestination currentLocation,
        NavigationDestination destination,
        ClientWorldEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(currentLocation);
        ArgumentNullException.ThrowIfNull(destination);

        return DestinationsMatch(
                currentLocation,
                destination) &&
            (destination.TargetKind !=
                 GalaxyNavigationTargetKind.Station ||
             environment ==
                 ClientWorldEnvironment.Starbase);
    }

    public NavigationRouteCommandResult PlanReturnTrip(
        int processId,
        bool destinationArrivalConfirmed = false)
    {
        NavigationRouteSnapshot snapshot;

        lock (this.lockObject)
        {
            if (!this.TryGetAvailableProcessState(
                    processId,
                    out var state,
                    out var error))
            {
                return NavigationRouteCommandResult.Failure(error);
            }

            if (!this.routesByCharacter.TryGetValue(
                    state.CharacterKey!,
                    out var previousRoute))
            {
                return NavigationRouteCommandResult.Failure(
                    "No route is available for a return trip.");
            }

            var previousPlan = this.BuildPlan(
                previousRoute,
                state.Profession,
                state.CurrentSector!,
                state.Wormholes);

            if (!previousPlan.Succeeded || previousPlan.Plan == null)
            {
                return NavigationRouteCommandResult.Failure(
                    previousPlan.Error);
            }

            if (previousPlan.Plan.Status !=
                    NavigationRouteStatus.DestinationReached &&
                !destinationArrivalConfirmed)
            {
                return NavigationRouteCommandResult.Failure(
                    "The current destination has not been reached yet.");
            }

            var returnDestination =
                previousPlan.Plan.OriginDestination;

            if (!this.TryValidateDestination(
                    returnDestination,
                    out returnDestination,
                    out error))
            {
                return NavigationRouteCommandResult.Failure(
                    string.Concat(
                        "The original departure point is no longer available: ",
                        error));
            }

            var currentLocation = state.CurrentLocation!;

            if (destinationArrivalConfirmed &&
                currentLocation.Kind ==
                    NavigationDestinationKind.Sector &&
                previousPlan.Plan.Destination.Kind ==
                    NavigationDestinationKind.Target &&
                string.Equals(
                    currentLocation.SectorKey,
                    previousPlan.Plan.Destination.SectorKey,
                    StringComparison.Ordinal))
            {
                currentLocation = previousPlan.Plan.Destination;
            }

            if (DestinationsMatch(
                    currentLocation,
                    returnDestination))
            {
                return NavigationRouteCommandResult.Failure(
                    "The client is already at the original departure point.");
            }

            var now = DateTimeOffset.UtcNow;
            var route = new NavigationPersistedRoute
            {
                CharacterKey = state.CharacterKey!,
                CharacterName = state.CharacterName!,
                RouteId = Guid.NewGuid(),
                CreatedAt = now,
                UpdatedAt = now,
                OriginSectorKey = state.CurrentSector!.Key,
                OriginDestination = currentLocation,
                Destination = returnDestination,
                VisitedSectorKeys = [state.CurrentSector!.Key],
                LastKnownSectorKey = state.CurrentSector!.Key,
            };

            var planResult = this.BuildPlan(
                route,
                state.Profession,
                state.CurrentSector!,
                state.Wormholes);

            if (!planResult.Succeeded || planResult.Plan == null)
            {
                return NavigationRouteCommandResult.Failure(
                    planResult.Error);
            }

            this.routesByCharacter[route.CharacterKey] = route;

            if (!this.TrySaveRoutes(out var persistenceError))
            {
                this.routesByCharacter[route.CharacterKey] =
                    previousRoute;

                return NavigationRouteCommandResult.Failure(
                    persistenceError);
            }

            snapshot = state.Snapshot = this.BuildSnapshot(
                processId,
                now,
                route.CharacterKey,
                route.CharacterName,
                state.Profession,
                state.CurrentSector!,
                route,
                state.Snapshot.CurrentPilotReputation,
                state.Wormholes);
        }

        this.RouteChanged?.Invoke(
            this,
            new NavigationRouteChangedEventArgs(snapshot));

        return NavigationRouteCommandResult.Success(snapshot);
    }

    public NavigationRouteCommandResult ClearRoute(int processId)
    {
        NavigationRouteSnapshot snapshot;

        lock (this.lockObject)
        {
            if (!this.processStates.TryGetValue(
                    processId,
                    out var state) ||
                string.IsNullOrWhiteSpace(state.CharacterKey))
            {
                return NavigationRouteCommandResult.Failure(
                    "No live character route context is available");
            }

            var hadRoute = this.routesByCharacter.Remove(
                state.CharacterKey,
                out var removedRoute);

            if (hadRoute &&
                !this.TrySaveRoutes(out var persistenceError))
            {
                this.routesByCharacter[state.CharacterKey] =
                    removedRoute!;

                return NavigationRouteCommandResult.Failure(
                    persistenceError);
            }

            snapshot = state.Snapshot = new NavigationRouteSnapshot
            {
                ProcessId = processId,
                ObservedAt = DateTimeOffset.UtcNow,
                IsAvailable = state.CurrentSector != null,
                Status = state.CurrentSector != null
                    ? NavigationRouteStatus.Ready
                    : NavigationRouteStatus.Unavailable,
                StatusText = state.CurrentSector != null
                    ? "No route is planned"
                    : "The current location is unavailable",
                CharacterKey = state.CharacterKey,
                CharacterName = state.CharacterName,
                Profession = state.Profession,
                CurrentSector = state.CurrentSector,
                CurrentPilotReputation = state.Snapshot.CurrentPilotReputation,
                HasRoute = false,
            };
        }

        this.RouteChanged?.Invoke(
            this,
            new NavigationRouteChangedEventArgs(snapshot));

        return NavigationRouteCommandResult.Success(snapshot);
    }

    public void DetachProcess(int processId)
    {
        lock (this.lockObject)
        {
            this.processStates.Remove(processId);
        }
    }

    private NavigationRouteSnapshot BuildSnapshot(
        int processId,
        DateTimeOffset observedAt,
        string characterKey,
        string characterName,
        string? profession,
        GalaxySectorDefinition currentSector,
        NavigationPersistedRoute? route,
        ClientReputationObservation currentPilotReputation,
        NavigationWormholeAvailability wormholes)
    {
        if (route == null)
        {
            return new NavigationRouteSnapshot
            {
                ProcessId = processId,
                ObservedAt = observedAt,
                IsAvailable = true,
                Status = NavigationRouteStatus.Ready,
                StatusText = "No route is planned",
                CharacterKey = characterKey,
                CharacterName = characterName,
                Profession = profession,
                CurrentSector = currentSector,
                CurrentPilotReputation = currentPilotReputation,
                HasRoute = false,
            };
        }

        var planResult = this.BuildPlan(
            route,
            profession,
            currentSector,
            wormholes);

        if (!planResult.Succeeded || planResult.Plan == null)
        {
            var unavailablePlan = this.BuildNoRoutePlan(
                route,
                currentSector,
                planResult.Error);

            return new NavigationRouteSnapshot
            {
                ProcessId = processId,
                ObservedAt = observedAt,
                IsAvailable = true,
                Status = NavigationRouteStatus.NoRoute,
                StatusText = planResult.Error,
                CharacterKey = characterKey,
                CharacterName = characterName,
                Profession = profession,
                CurrentSector = currentSector,
                CurrentPilotReputation = currentPilotReputation,
                Route = unavailablePlan,
                HasRoute = true,
            };
        }

        return new NavigationRouteSnapshot
        {
            ProcessId = processId,
            ObservedAt = observedAt,
            IsAvailable = true,
            Status = planResult.Plan.Status,
            StatusText = planResult.Plan.StatusText,
            CharacterKey = characterKey,
            CharacterName = characterName,
            Profession = profession,
            CurrentSector = currentSector,
            CurrentPilotReputation = currentPilotReputation,
            Route = planResult.Plan,
            HasRoute = true,
        };
    }


    private NavigationRoutePlan BuildNoRoutePlan(
        NavigationPersistedRoute route,
        GalaxySectorDefinition currentSector,
        string error)
    {
        if (!this.Topology.TryGetByKey(
                route.OriginSectorKey,
                out var origin))
        {
            origin = currentSector;
        }

        var originDestination = this.ResolveOriginDestination(
            route,
            origin);

        var destination = this.TryValidateDestination(
                route.Destination,
                out var validatedDestination,
                out _)
            ? validatedDestination
            : route.Destination;

        return new NavigationRoutePlan
        {
            RouteId = route.RouteId,
            CreatedAt = route.CreatedAt,
            UpdatedAt = route.UpdatedAt,
            CharacterKey = route.CharacterKey,
            CharacterName = route.CharacterName,
            Origin = origin,
            OriginDestination = originDestination,
            Current = currentSector,
            Destination = destination,
            Status = NavigationRouteStatus.NoRoute,
            StatusText = error,
            CompletedHopCount = Math.Max(
                0,
                route.VisitedSectorKeys.Count - 1),
            RemainingHopCount = 0,
            Warnings =
            [
                .. new[]
                    {
                        error,
                        this.persistenceWarning,
                    }
                    .Where(warning =>
                        !string.IsNullOrWhiteSpace(warning))
                    .Distinct(StringComparer.Ordinal),
            ],
        };
    }

    private NavigationRoutePlanResult BuildPlan(
        NavigationPersistedRoute route,
        string? profession,
        GalaxySectorDefinition currentSector,
        NavigationWormholeAvailability wormholes)
    {
        if (!this.TryValidateDestination(
                route.Destination,
                out var destination,
                out var destinationError))
        {
            return NavigationRoutePlanResult.Failure(
                destinationError);
        }

        if (!this.Topology.TryGetByKey(
                route.OriginSectorKey,
                out var origin))
        {
            origin = currentSector;
        }

        var originDestination = this.ResolveOriginDestination(
            route,
            origin);

        var sectorRoute = this.routePlanner.FindRoute(
            currentSector.Key,
            destination.SectorKey,
            profession,
            (from, to) =>
                this.Catalog.TryGetVerifiedDeparture(
                    from.Key,
                    to.Key,
                    out _) &&
                (!string.IsNullOrWhiteSpace(profession) ||
                 string.IsNullOrWhiteSpace(
                     to.RequiredProfession)),
            wormholes);

        if (!sectorRoute.Succeeded)
        {
            var error = sectorRoute.Error;

            if (string.IsNullOrWhiteSpace(profession))
            {
                error = string.Concat(
                    error,
                    " Live profession is unavailable; profession-restricted sectors were excluded.");
            }

            return NavigationRoutePlanResult.Failure(error);
        }

        List<NavigationRouteStep> steps = [];
        List<string> warnings = [];

        if (string.IsNullOrWhiteSpace(profession))
        {
            warnings.Add(
                "Live profession is unavailable; profession-restricted sectors were excluded from this plan.");
        }

        if (!string.IsNullOrWhiteSpace(this.persistenceWarning))
        {
            warnings.Add(this.persistenceWarning);
        }

        foreach (var transition in sectorRoute.Transitions)
        {
            var from = transition.From;
            var to = transition.To;

            if (!string.IsNullOrWhiteSpace(
                    to.RequiredProfession) ||
                !string.IsNullOrWhiteSpace(
                    to.RequiredFaction))
            {
                warnings.Add(
                        $"{to.Name}: {to.GetAccessRequirementDescription()}");
            }

            if (transition.Kind == GalaxyRouteTransitionKind.Wormhole)
            {
                var wormhole = transition.Wormhole;

                if (wormhole == null)
                {
                    return NavigationRoutePlanResult.Failure(
                        $"The wormhole route from {from.Name} to {to.Name} has no caster information.");
                }

                if (!wormhole.HasReadyCaster &&
                    wormhole.IsShortcutReadinessKnown)
                {
                    warnings.Add(string.Concat(
                        wormhole.Destination.SkillFamilyName,
                        " is learned for ",
                        to.Name,
                        ", but no eligible managed pilot currently has that skill on a shortcut."));
                }

                steps.Add(new NavigationRouteStep
                {
                    Number = steps.Count + 1,
                    Kind = NavigationRouteStepKind.WormholeTransition,
                    FromSectorKey = from.Key,
                    FromSectorName = from.Name,
                    FromSystemName = from.SystemName,
                    ToSectorKey = to.Key,
                    ToSectorName = to.Name,
                    ToSystemName = to.SystemName,
                    WormholeSkillFamilyName =
                        wormhole.Destination.SkillFamilyName,
                    WormholeAbilityName =
                        wormhole.Destination.AbilityName,
                    WormholeRequiredRank =
                        wormhole.Destination.RequiredRank,
                    WormholeMenuIndex =
                        wormhole.Destination.MenuIndex,
                    WormholeHasReadyCaster =
                        wormhole.HasReadyCaster,
                    WormholeCasterNames =
                        wormhole.CasterNames,
                });

                continue;
            }

            if (!this.Catalog.TryGetVerifiedDeparture(
                    from.Key,
                    to.Key,
                    out var departure))
            {
                return NavigationRoutePlanResult.Failure(
                        $"The catalog has no verified departure from {from.Name} to {to.Name}.");
            }

            if (!string.IsNullOrWhiteSpace(
                    departure.AccessRequirement))
            {
                warnings.Add(
                        $"{from.Name} → {to.Name}: {departure.AccessRequirement}");
            }

            steps.Add(new NavigationRouteStep
            {
                Number = steps.Count + 1,
                Kind = NavigationRouteStepKind.SectorTransition,
                FromSectorKey = from.Key,
                FromSectorName = from.Name,
                FromSystemName = from.SystemName,
                ToSectorKey = to.Key,
                ToSectorName = to.Name,
                ToSystemName = to.SystemName,
                DepartureTargetKey =
                    NavigationDestination.CreateDepartureTargetKey(
                        from.Key,
                        to.Key,
                        departure),
                DepartureTargetName = departure.DepartureTargetName,
                DepartureTargetType = departure.Kind.ToPublicName(),
                DepartureTargetRawObjectType =
                    departure.RawObjectType,
                DepartureTargetSelectionContext =
                    GalaxyNavigationTargetSelectionContext.Navigation,
                HasDepartureTargetPosition =
                    departure.HasExpectedPosition,
                DepartureTargetX = departure.ExpectedX,
                DepartureTargetY = departure.ExpectedY,
                DepartureTargetZ = departure.ExpectedZ,
                AccessRequirement = departure.AccessRequirement,
            });
        }

        if (destination.Kind ==
            NavigationDestinationKind.Target)
        {
            steps.Add(new NavigationRouteStep
            {
                Number = steps.Count + 1,
                Kind = NavigationRouteStepKind.FinalTarget,
                FromSectorKey = destination.SectorKey,
                FromSectorName = destination.SectorName,
                FromSystemName = destination.SystemName,
                ToSectorKey = destination.SectorKey,
                ToSectorName = destination.SectorName,
                ToSystemName = destination.SystemName,
                FinalTargetKey = destination.TargetKey,
                FinalTargetName = destination.TargetName,
                FinalTargetType = destination.TargetType,
                FinalTargetRawObjectType =
                    destination.TargetRawObjectType,
                FinalTargetSelectionContext =
                    destination.TargetSelectionContext,
                HasFinalTargetPosition = destination.HasTargetPosition,
                FinalTargetX = destination.TargetX,
                FinalTargetY = destination.TargetY,
                FinalTargetZ = destination.TargetZ,
            });
        }

        var completedHopCount = Math.Max(
            0,
            route.VisitedSectorKeys.Count - 1);

        // Target destinations must retain their final target step until the
        // Auto Pilot completes docking, landing, or in-space arrival. A
        // selected nearby target is useful for remembering a precise route
        // origin, but it is not proof that the destination interaction has
        // finished.
        var destinationReached =
            string.Equals(
                currentSector.Key,
                destination.SectorKey,
                StringComparison.Ordinal) &&
            destination.Kind == NavigationDestinationKind.Sector;

        var status = destinationReached
            ? NavigationRouteStatus.DestinationReached
            : NavigationRouteStatus.Planned;

        var statusText = destinationReached
            ? $"Destination reached: {destination.SectorName}"
            : $"Route planned to {destination.DisplayName}";

        return NavigationRoutePlanResult.Success(
            new NavigationRoutePlan
            {
                RouteId = route.RouteId,
                CreatedAt = route.CreatedAt,
                UpdatedAt = route.UpdatedAt,
                CharacterKey = route.CharacterKey,
                CharacterName = route.CharacterName,
                Origin = origin,
                OriginDestination = originDestination,
                Current = currentSector,
                Destination = destination,
                Status = status,
                StatusText = statusText,
                CompletedHopCount = completedHopCount,
                RemainingHopCount = sectorRoute.HopCount,
                Steps = steps,
                Warnings = [.. warnings.Distinct(
                    StringComparer.Ordinal)],
            });
    }

    private bool TryValidateDestination(
        NavigationDestination destination,
        out NavigationDestination validated,
        out string error)
    {
        validated = null!;
        error = "";

        if (!this.Topology.TryGetByKey(
                destination.SectorKey,
                out var sector))
        {
            error = $"Unknown destination sector '{destination.SectorKey}'.";
            return false;
        }

        if (destination.Kind == NavigationDestinationKind.Sector)
        {
            validated = NavigationDestination.ForSector(sector);
            return true;
        }

        if (string.IsNullOrWhiteSpace(destination.TargetKey) ||
            !this.Catalog.TryGetSector(
                sector.Key,
                out var catalogSector))
        {
            error = "The selected navigation target is not present in the built-in catalog.";
            return false;
        }

        var target = catalogSector.Targets.FirstOrDefault(candidate =>
            string.Equals(
                NavigationDestination.CreateTargetKey(
                    sector.Key,
                    candidate),
                destination.TargetKey,
                StringComparison.Ordinal));

        if (target == null)
        {
            error = "The selected navigation target is no longer present in the built-in catalog.";
            return false;
        }

        validated = NavigationDestination.ForTarget(sector, target);
        return true;
    }

    private NavigationDestination ResolveOriginDestination(
        NavigationPersistedRoute route,
        GalaxySectorDefinition fallbackSector)
    {
        if (route.OriginDestination != null &&
            this.TryValidateDestination(
                route.OriginDestination,
                out var originDestination,
                out _))
        {
            return originDestination;
        }

        return NavigationDestination.ForSector(fallbackSector);
    }

    private NavigationDestination ResolveCurrentLocation(
        ClientObservationSnapshot observation,
        GalaxySectorDefinition currentSector)
    {
        if (this.TryResolveDockedTarget(
                observation,
                currentSector,
                out var dockedTarget))
        {
            return dockedTarget;
        }

        if (this.TryResolveSelectedNearbyNavigationTarget(
                observation,
                currentSector,
                out var selectedTarget))
        {
            return selectedTarget;
        }

        return NavigationDestination.ForSector(currentSector);
    }

    private bool TryResolveDockedTarget(
        ClientObservationSnapshot observation,
        GalaxySectorDefinition currentSector,
        out NavigationDestination destination)
    {
        destination = null!;

        if (observation.World.Environment !=
                ClientWorldEnvironment.Starbase ||
            string.IsNullOrWhiteSpace(
                observation.World.CurrentStarbaseName) ||
            !this.Catalog.TryGetSector(
                currentSector.Key,
                out var catalogSector))
        {
            return false;
        }

        var stationName = GalaxyTopology.NormalizeName(
            observation.World.CurrentStarbaseName);
        var matches = catalogSector.Targets
            .Where(target =>
                target.Kind == GalaxyNavigationTargetKind.Station &&
                TargetNameMatches(target, stationName))
            .Take(2)
            .ToArray();

        if (matches.Length != 1)
        {
            return false;
        }

        destination = NavigationDestination.ForTarget(
            currentSector,
            matches[0]);
        return true;
    }

    private bool TryResolveSelectedNearbyNavigationTarget(
        ClientObservationSnapshot observation,
        GalaxySectorDefinition currentSector,
        out NavigationDestination destination)
    {
        destination = null!;

        if (!observation.Target.IsAvailable ||
            !observation.Target.HasTarget ||
            observation.Target.ObjectId == 0 ||
            !observation.Target.Distance.IsAvailable ||
            !float.IsFinite(
                observation.Target.Distance.SurfaceDistance) ||
            observation.Target.Distance.SurfaceDistance < 0 ||
            observation.Target.Distance.SurfaceDistance >
                PreciseLocationMaximumSurfaceDistance ||
            !this.Catalog.TryGetSector(
                currentSector.Key,
                out var catalogSector))
        {
            return false;
        }

        var observedTarget = observation.Navigation.Targets
            .FirstOrDefault(target =>
                target.IsAvailable &&
                target.ObjectId == observation.Target.ObjectId);

        if (observedTarget == null)
        {
            return false;
        }

        var observedNames = new HashSet<string>(
            StringComparer.Ordinal);

        AddNormalizedName(observedNames, observedTarget.Name);
        AddNormalizedName(
            observedNames,
            observedTarget.MapDisplayName);
        AddNormalizedName(observedNames, observation.Target.Name);

        if (observedNames.Count == 0)
        {
            return false;
        }

        var matches = catalogSector.Targets
            .Where(target =>
                target.RawObjectType ==
                    observedTarget.RawObjectType &&
                observedNames.Any(name =>
                    TargetNameMatches(target, name)))
            .Take(2)
            .ToArray();

        if (matches.Length != 1)
        {
            return false;
        }

        destination = NavigationDestination.ForTarget(
            currentSector,
            matches[0]);
        return true;
    }

    private static void AddNormalizedName(
        HashSet<string> names,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            names.Add(GalaxyTopology.NormalizeName(value));
        }
    }

    private static bool TargetNameMatches(
        GalaxyNavigationCatalogTarget target,
        string normalizedName)
    {
        return string.Equals(
                   GalaxyTopology.NormalizeName(target.Name),
                   normalizedName,
                   StringComparison.Ordinal) ||
               string.Equals(
                   GalaxyTopology.NormalizeName(
                       target.MapDisplayName),
                   normalizedName,
                   StringComparison.Ordinal);
    }

    private static bool DestinationsMatch(
        NavigationDestination left,
        NavigationDestination right)
    {
        if (!string.Equals(
                left.SectorKey,
                right.SectorKey,
                StringComparison.Ordinal))
        {
            return false;
        }

        if (right.Kind == NavigationDestinationKind.Sector)
        {
            return true;
        }

        return left.Kind == NavigationDestinationKind.Target &&
               !string.IsNullOrWhiteSpace(left.TargetKey) &&
               string.Equals(
                   left.TargetKey,
                   right.TargetKey,
                   StringComparison.Ordinal);
    }

    private bool TryResolveCurrentSector(
        ClientObservationSnapshot observation,
        out GalaxySectorDefinition sector,
        out string error)
    {
        sector = null!;
        error = "";

        GalaxySectorDefinition? sectorByName = null;
        GalaxySectorDefinition? sectorByNumber = null;

        if (this.Topology.TryResolve(
                observation.World.CurrentSectorName,
                out var resolvedByName))
        {
            sectorByName = resolvedByName;
        }

        var navigationSectorNumber =
            observation.Navigation.ActiveSectorNumber;
        var worldSectorNumber =
            observation.World.ActiveSectorNumber;

        if (navigationSectorNumber != 0 &&
            worldSectorNumber != 0 &&
            navigationSectorNumber != worldSectorNumber)
        {
            error = $"Live navigation and world observations disagree about the active sector number ({navigationSectorNumber} versus {worldSectorNumber}).";

            return false;
        }

        var activeSectorNumber = navigationSectorNumber != 0
            ? navigationSectorNumber
            : worldSectorNumber;

        var catalogSector =
            this.Catalog.FindSectorByActiveSectorNumber(
                activeSectorNumber);

        if (catalogSector != null)
        {
            sectorByNumber = this.Topology.GetByKey(
                catalogSector.SectorKey);
        }

        if (sectorByName != null &&
            sectorByNumber != null &&
            !string.Equals(
                sectorByName.Key,
                sectorByNumber.Key,
                StringComparison.Ordinal))
        {
            error = $"Live sector name '{observation.World.CurrentSectorName}' resolves to {sectorByName.Name}, but active sector number {activeSectorNumber} resolves to {sectorByNumber.Name}.";

            return false;
        }

        sector = sectorByName ?? sectorByNumber!;

        if (sector != null)
        {
            return true;
        }

        var observedName = string.IsNullOrWhiteSpace(
                observation.World.CurrentSectorName)
            ? "<unavailable>"
            : observation.World.CurrentSectorName.Trim();

        error = $"The live location is not present in the galaxy topology (name '{observedName}', active sector number {activeSectorNumber}).";

        return false;
    }

    private bool TryGetAvailableProcessState(
        int processId,
        out ProcessRouteState state,
        out string error)
    {
        error = "";

        if (!this.processStates.TryGetValue(
                processId,
                out state!) ||
            string.IsNullOrWhiteSpace(state.CharacterKey) ||
            string.IsNullOrWhiteSpace(state.CharacterName) ||
            state.CurrentSector == null ||
            state.CurrentLocation == null)
        {
            error = "The client does not currently have a live character and resolved sector.";
            return false;
        }

        return true;
    }

    private bool TrySaveRoutes(out string error)
    {
        var saved = this.store.TrySave(
            this.routesByCharacter.Values,
            out error);

        this.persistenceWarning = saved
            ? ""
            : error;

        return saved;
    }

    private static string CreateCharacterKey(string characterName)
    {
        return string.Concat(
            CharacterEnvironmentKey,
            "|",
            GalaxyTopology.NormalizeName(characterName));
    }

    private sealed class ProcessRouteState
    {
        public string? CharacterKey { get; init; }

        public string? CharacterName { get; init; }

        public string? Profession { get; init; }

        public GalaxySectorDefinition? CurrentSector { get; init; }

        public NavigationDestination? CurrentLocation { get; init; }

        public ClientWorldEnvironment Environment { get; init; }

        public NavigationWormholeAvailability Wormholes { get; init; } =
            NavigationWormholeAvailability.None;

        public required NavigationRouteSnapshot Snapshot { get; set; }
    }
}
