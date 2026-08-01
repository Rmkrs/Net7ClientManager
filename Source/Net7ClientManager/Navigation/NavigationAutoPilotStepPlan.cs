namespace Net7ClientManager.Navigation;

using Net7ClientManager.Observations.Models;

internal sealed record NavigationAutoPilotStepPlan
{
    private const byte PlanetRawObjectType = 3;

    private const byte StationRawObjectType = 12;

    public required int Number { get; init; }

    public required NavigationRouteStepKind Kind { get; init; }

    public required string FromSectorKey { get; init; }

    public required string FromSectorName { get; init; }

    public required string ToSectorKey { get; init; }

    public required string ToSectorName { get; init; }

    public required string TargetName { get; init; }

    public required byte TargetRawObjectType { get; init; }

    public required ClientTargetVerb Verb { get; init; }

    public required string VerbName { get; init; }

    public required bool RequiresInteraction { get; init; }

    public bool AllowsDetectedInteraction { get; init; }

    public required bool IsSectorTransition { get; init; }

    public string ArrivalSectorKey { get; init; } = "";

    public string ArrivalSectorName { get; init; } = "";

    public bool IsWormholeTransition { get; init; }

    public string WormholeSkillFamilyName { get; init; } = "";

    public int WormholeRequiredRank { get; init; }

    public int WormholeMenuIndex { get; init; } = -1;

    public IReadOnlyList<string> WormholeCasterNames { get; init; } = [];

    public required NavigationAutoPilotState ActivatingState { get; init; }

    public required NavigationAutoPilotStopReason UnavailableReason { get; init; }

    public required NavigationAutoPilotStopReason ActivationFailedReason { get; init; }

    public required NavigationAutoPilotStopReason TransitionTimedOutReason { get; init; }

    public bool Matches(NavigationRouteStep? step)
    {
        if (step == null ||
            step.Number != this.Number ||
            step.Kind != this.Kind ||
            !string.Equals(
                step.FromSectorKey,
                this.FromSectorKey,
                StringComparison.Ordinal) ||
            !string.Equals(
                step.ToSectorKey,
                this.ToSectorKey,
                StringComparison.Ordinal))
        {
            return false;
        }

        if (this.IsWormholeTransition)
        {
            return string.Equals(
                       step.WormholeAbilityName,
                       this.TargetName,
                       StringComparison.Ordinal) &&
                   string.Equals(
                       step.WormholeSkillFamilyName,
                       this.WormholeSkillFamilyName,
                       StringComparison.Ordinal);
        }

        var targetName = step.Kind ==
            NavigationRouteStepKind.SectorTransition
                ? step.DepartureTargetName
                : step.FinalTargetName;

        var rawObjectType = step.Kind ==
            NavigationRouteStepKind.SectorTransition
                ? step.DepartureTargetRawObjectType
                : step.FinalTargetRawObjectType;

        return rawObjectType == this.TargetRawObjectType &&
            string.Equals(
                targetName,
                this.TargetName,
                StringComparison.Ordinal);
    }

    public bool CanUseDetectedInteraction(
        ClientTargetVerb verb)
    {
        if (!this.AllowsDetectedInteraction ||
            !IsRouteInteractionVerb(verb))
        {
            return false;
        }

        // A gate selected as the final destination is an arrival point.
        // Gate is activated only when the route step itself crosses into
        // another sector.
        return this.Kind !=
                   NavigationRouteStepKind.FinalTarget ||
               verb != ClientTargetVerb.Gate;
    }

    public NavigationAutoPilotStepPlan WithDetectedInteraction(
        ClientTargetVerb verb)
    {
        if (!this.CanUseDetectedInteraction(verb) ||
            (this.RequiresInteraction &&
             this.Verb == verb))
        {
            return this;
        }

        return this with
        {
            Verb = verb,
            VerbName = GetVerbName(verb),
            RequiresInteraction = true,
            ActivatingState =
                NavigationAutoPilotState.ActivatingDestination,
            UnavailableReason =
                NavigationAutoPilotStopReason.DestinationUnavailable,
            ActivationFailedReason =
                NavigationAutoPilotStopReason.DestinationActivationFailed,
            TransitionTimedOutReason =
                NavigationAutoPilotStopReason.DestinationTransitionTimedOut,
        };
    }

    public static bool IsRouteInteractionVerb(
        ClientTargetVerb verb)
    {
        return verb is
            ClientTargetVerb.Dock or
            ClientTargetVerb.Gate or
            ClientTargetVerb.Land;
    }

    public static string GetVerbName(ClientTargetVerb verb)
    {
        return verb switch
        {
            ClientTargetVerb.Dock => "Dock",
            ClientTargetVerb.Gate => "Gate",
            ClientTargetVerb.Land => "Land",
            _ => "Interact",
        };
    }

    public static bool TryCreate(
        NavigationRouteStep step,
        out NavigationAutoPilotStepPlan plan)
    {
        ArgumentNullException.ThrowIfNull(step);

        if (step.Kind ==
                NavigationRouteStepKind.WormholeTransition &&
            !string.IsNullOrWhiteSpace(
                step.WormholeSkillFamilyName) &&
            !string.IsNullOrWhiteSpace(
                step.WormholeAbilityName) &&
            step.WormholeRequiredRank > 0)
        {
            plan = new NavigationAutoPilotStepPlan
            {
                Number = step.Number,
                Kind = step.Kind,
                FromSectorKey = step.FromSectorKey,
                FromSectorName = step.FromSectorName,
                ToSectorKey = step.ToSectorKey,
                ToSectorName = step.ToSectorName,
                TargetName = step.WormholeAbilityName,
                TargetRawObjectType = 0,
                Verb = ClientTargetVerb.NotApplicable,
                VerbName = "Wormhole",
                RequiresInteraction = true,
                IsSectorTransition = true,
                IsWormholeTransition = true,
                WormholeSkillFamilyName =
                    step.WormholeSkillFamilyName,
                WormholeRequiredRank =
                    step.WormholeRequiredRank,
                WormholeMenuIndex =
                    step.WormholeMenuIndex,
                WormholeCasterNames =
                    step.WormholeCasterNames,
                ActivatingState =
                    NavigationAutoPilotState.ActivatingWormhole,
                UnavailableReason =
                    NavigationAutoPilotStopReason.WormholeUnavailable,
                ActivationFailedReason =
                    NavigationAutoPilotStopReason.WormholeActivationFailed,
                TransitionTimedOutReason =
                    NavigationAutoPilotStopReason.WormholeTransitionTimedOut,
            };

            return true;
        }

        if (step.Kind ==
                NavigationRouteStepKind.SectorTransition &&
            !string.IsNullOrWhiteSpace(
                step.DepartureTargetName) &&
            step.DepartureTargetRawObjectType.HasValue)
        {
            var targetRawObjectType =
                step.DepartureTargetRawObjectType.Value;
            var verb = targetRawObjectType ==
                    PlanetRawObjectType
                ? ClientTargetVerb.Land
                : ClientTargetVerb.Gate;
            var isLandTransition =
                verb == ClientTargetVerb.Land;

            plan = new NavigationAutoPilotStepPlan
            {
                Number = step.Number,
                Kind = step.Kind,
                FromSectorKey = step.FromSectorKey,
                FromSectorName = step.FromSectorName,
                ToSectorKey = step.ToSectorKey,
                ToSectorName = step.ToSectorName,
                TargetName = step.DepartureTargetName,
                TargetRawObjectType = targetRawObjectType,
                Verb = verb,
                VerbName = GetVerbName(verb),
                RequiresInteraction = true,
                IsSectorTransition = true,
                ActivatingState = isLandTransition
                    ? NavigationAutoPilotState.ActivatingDestination
                    : NavigationAutoPilotState.ActivatingGate,
                UnavailableReason = isLandTransition
                    ? NavigationAutoPilotStopReason.DestinationUnavailable
                    : NavigationAutoPilotStopReason.GateUnavailable,
                ActivationFailedReason = isLandTransition
                    ? NavigationAutoPilotStopReason.DestinationActivationFailed
                    : NavigationAutoPilotStopReason.GateActivationFailed,
                TransitionTimedOutReason =
                    NavigationAutoPilotStopReason.SectorTransitionTimedOut,
            };

            return true;
        }

        if (step.Kind ==
                NavigationRouteStepKind.FinalTarget &&
            step.FinalTargetRawObjectType.HasValue &&
            !string.IsNullOrWhiteSpace(
                step.FinalTargetName))
        {
            var verb = step.FinalTargetVerb;

            if (verb == ClientTargetVerb.NotApplicable &&
                step.FinalTargetRawObjectType ==
                    StationRawObjectType)
            {
                verb = ClientTargetVerb.Dock;
            }

            var requiresInteraction = IsRouteInteractionVerb(verb);

            plan = new NavigationAutoPilotStepPlan
            {
                Number = step.Number,
                Kind = step.Kind,
                FromSectorKey = step.FromSectorKey,
                FromSectorName = step.FromSectorName,
                ToSectorKey = step.ToSectorKey,
                ToSectorName = step.ToSectorName,
                TargetName = step.FinalTargetName,
                TargetRawObjectType =
                    step.FinalTargetRawObjectType.Value,
                Verb = verb,
                VerbName = requiresInteraction
                    ? GetVerbName(verb)
                    : "arrival",
                RequiresInteraction = requiresInteraction,
                AllowsDetectedInteraction = true,
                IsSectorTransition = false,
                ArrivalSectorKey =
                    step.FinalTargetArrivalSectorKey ?? "",
                ArrivalSectorName =
                    step.FinalTargetArrivalSectorName ?? "",
                ActivatingState = requiresInteraction
                    ? NavigationAutoPilotState.ActivatingDestination
                    : NavigationAutoPilotState.VerifyingArrival,
                UnavailableReason = requiresInteraction
                    ? NavigationAutoPilotStopReason.DestinationUnavailable
                    : NavigationAutoPilotStopReason.WarpUnavailable,
                ActivationFailedReason = requiresInteraction
                    ? NavigationAutoPilotStopReason.DestinationActivationFailed
                    : NavigationAutoPilotStopReason.InternalError,
                TransitionTimedOutReason = requiresInteraction
                    ? NavigationAutoPilotStopReason.DestinationTransitionTimedOut
                    : NavigationAutoPilotStopReason.WarpInterrupted,
            };

            return true;
        }

        plan = null!;
        return false;
    }
}
