namespace Net7ClientManager.Navigation;

using Net7ClientManager.Observations.Models;

/// <summary>
/// Pure decision engine for one Auto Pilot route step at a time. Every phase
/// consumes only the observations required for its next concrete operation.
/// Temporary observation gaps wait within a bounded phase; contradictions stop.
/// Input effects are requested explicitly and executed by the coordinator.
/// </summary>
internal static class NavigationAutoPilotStateMachine
{
    private static readonly TimeSpan stepReconcileTimeout =
        TimeSpan.FromSeconds(30);

    private static readonly TimeSpan targetResolutionTimeout =
        TimeSpan.FromSeconds(30);

    private static readonly TimeSpan targetStateTimeout =
        TimeSpan.FromSeconds(4);

    private static readonly TimeSpan warpAvailableTimeout =
        TimeSpan.FromSeconds(30);

    private static readonly TimeSpan warpStartTimeout =
        TimeSpan.FromSeconds(4);

    private static readonly TimeSpan warpCompletionTimeout =
        TimeSpan.FromMinutes(10);

    private static readonly TimeSpan targetVerbTimeout =
        TimeSpan.FromSeconds(5);

    private static readonly TimeSpan targetArrivalTimeout =
        TimeSpan.FromSeconds(5);

    private static readonly TimeSpan sectorTransitionTimeout =
        TimeSpan.FromSeconds(30);

    private static readonly TimeSpan destinationTransitionTimeout =
        TimeSpan.FromSeconds(30);

    public static NavigationAutoPilotMachineTransition Observe(
        NavigationAutoPilotMachineState state,
        NavigationAutoPilotMachineFrame frame)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(frame);

        if (state.IsTerminal)
        {
            return new NavigationAutoPilotMachineTransition(state);
        }

        if (!frame.ClientAvailable)
        {
            return Stop(
                state,
                NavigationAutoPilotStopReason.ClientUnavailable,
                "Auto Pilot stopped because the hosted game client became unavailable.",
                frame.Now);
        }

        var routeFailure = ValidateRouteIdentity(state, frame);

        if (routeFailure.HasValue)
        {
            return routeFailure.Value;
        }

        return state.Phase switch
        {
            NavigationAutoPilotMachinePhase.ReconcilingStep =>
                ObserveReconcilingStep(state, frame),
            NavigationAutoPilotMachinePhase.ResolvingTarget =>
                ObserveResolvingTarget(state, frame),
            NavigationAutoPilotMachinePhase.SelectingTarget =>
                new NavigationAutoPilotMachineTransition(state),
            NavigationAutoPilotMachinePhase.EvaluatingRange =>
                ObserveEvaluatingRange(state, frame),
            NavigationAutoPilotMachinePhase.WaitingForWarp =>
                ObserveWaitingForWarp(state, frame),
            NavigationAutoPilotMachinePhase.EngagingWarp =>
                new NavigationAutoPilotMachineTransition(state),
            NavigationAutoPilotMachinePhase.WaitingForWarpStart =>
                ObserveWaitingForWarpStart(state, frame),
            NavigationAutoPilotMachinePhase.Warping =>
                ObserveWarping(state, frame),
            NavigationAutoPilotMachinePhase.WaitingForArrival =>
                ObserveWaitingForArrival(state, frame),
            NavigationAutoPilotMachinePhase.WaitingForVerb =>
                ObserveWaitingForVerb(state, frame),
            NavigationAutoPilotMachinePhase.ActivatingVerb =>
                new NavigationAutoPilotMachineTransition(state),
            NavigationAutoPilotMachinePhase.WaitingForTransition =>
                ObserveWaitingForTransition(state, frame),
            NavigationAutoPilotMachinePhase.WaitingForDestination =>
                ObserveWaitingForDestination(state, frame),
            _ => new NavigationAutoPilotMachineTransition(state),
        };
    }

    public static NavigationAutoPilotMachineTransition
        ApplyTargetSelection(
            NavigationAutoPilotMachineState state,
            NavigationAutoPilotTargetSelectionOutcome outcome,
            DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.Phase !=
            NavigationAutoPilotMachinePhase.SelectingTarget)
        {
            return Stop(
                state,
                NavigationAutoPilotStopReason.InternalError,
                "Auto Pilot stopped because target-selection state became inconsistent.",
                now);
        }

        if (!outcome.Succeeded)
        {
            if (outcome.IsTransientFailure)
            {
                if (now - state.StepStartedAt <
                    targetResolutionTimeout)
                {
                    return Move(
                        state,
                        NavigationAutoPilotMachinePhase.ResolvingTarget,
                        NavigationAutoPilotState.SelectingTarget,
                        $"Waiting for route target {state.Step?.TargetName ?? "the next target"} to become available.",
                        now);
                }

                return Stop(
                    state,
                    NavigationAutoPilotStopReason.TargetSelectionFailed,
                    $"Auto Pilot stopped because route target {state.Step?.TargetName ?? "the next target"} did not become available.",
                    now);
            }

            return Stop(
                state,
                NavigationAutoPilotStopReason.TargetSelectionFailed,
                string.IsNullOrWhiteSpace(outcome.Error)
                    ? "Auto Pilot could not verify the next route target."
                    : $"Auto Pilot could not select the next route target: {outcome.Error}",
                now);
        }

        if (outcome.ObjectId == 0 || state.Step == null)
        {
            return Stop(
                state,
                NavigationAutoPilotStopReason.TargetSelectionFailed,
                "Auto Pilot could not verify the selected route target.",
                now);
        }

        return new NavigationAutoPilotMachineTransition(
            state with
            {
                Phase = NavigationAutoPilotMachinePhase.EvaluatingRange,
                PhaseStartedAt = now,
                TargetObjectId = outcome.ObjectId,
                VerbMissingSince = null,
                PublicState = NavigationAutoPilotState.VerifyingArrival,
                StopReason = NavigationAutoPilotStopReason.None,
                StatusText = $"Checking approach to {state.Step.TargetName}.",
            });
    }

    public static NavigationAutoPilotMachineTransition ApplyWarpCommand(
        NavigationAutoPilotMachineState state,
        NavigationAutoPilotEffectOutcome outcome,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.Phase !=
            NavigationAutoPilotMachinePhase.EngagingWarp)
        {
            return Stop(
                state,
                NavigationAutoPilotStopReason.InternalError,
                "Auto Pilot stopped because warp-command state became inconsistent.",
                now);
        }

        if (!outcome.Succeeded)
        {
            return Stop(
                state,
                outcome.StopReason ==
                    NavigationAutoPilotStopReason.None
                    ? NavigationAutoPilotStopReason.WarpUnavailable
                    : outcome.StopReason,
                string.IsNullOrWhiteSpace(outcome.StatusText)
                    ? "Auto Pilot could not engage warp."
                    : outcome.StatusText,
                now);
        }

        return Move(
            state,
            NavigationAutoPilotMachinePhase.WaitingForWarpStart,
            NavigationAutoPilotState.EngagingWarp,
            $"Engaging warp toward {state.Step?.TargetName ?? "the route target"}.",
            now);
    }

    public static NavigationAutoPilotMachineTransition
        ApplyVerbActivation(
            NavigationAutoPilotMachineState state,
            NavigationAutoPilotEffectOutcome outcome,
            DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.Phase !=
                NavigationAutoPilotMachinePhase.ActivatingVerb ||
            state.Step == null ||
            !state.Step.RequiresInteraction)
        {
            return Stop(
                state,
                NavigationAutoPilotStopReason.InternalError,
                "Auto Pilot stopped because target-activation state became inconsistent.",
                now);
        }

        if (!outcome.Succeeded)
        {
            return Stop(
                state,
                outcome.StopReason ==
                    NavigationAutoPilotStopReason.None
                    ? state.Step.ActivationFailedReason
                    : outcome.StopReason,
                string.IsNullOrWhiteSpace(outcome.StatusText)
                    ? $"Auto Pilot could not activate {state.Step.VerbName}."
                    : outcome.StatusText,
                now);
        }

        if (state.Step.IsSectorTransition)
        {
            return Move(
                state with
                {
                    TransitionAccepted = false,
                },
                NavigationAutoPilotMachinePhase.WaitingForTransition,
                NavigationAutoPilotState.WaitingForSector,
                $"Waiting to enter sector {state.Step.ToSectorName}.",
                now);
        }

        return Move(
            state,
            NavigationAutoPilotMachinePhase.WaitingForDestination,
            NavigationAutoPilotState.WaitingForDestination,
            $"Docking at {state.Step.TargetName}.",
            now);
    }

    public static NavigationAutoPilotMachineTransition StopByUser(
        NavigationAutoPilotMachineState state,
        DateTimeOffset now)
    {
        return Stop(
            state,
            NavigationAutoPilotStopReason.UserStopped,
            "Auto Pilot was stopped by the user.",
            now);
    }

    public static NavigationAutoPilotMachineTransition
        ConfirmDestinationArrival(
            NavigationAutoPilotMachineState state,
            DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.Phase ==
            NavigationAutoPilotMachinePhase.Arrived)
        {
            return new NavigationAutoPilotMachineTransition(state);
        }

        if (state.Phase !=
                NavigationAutoPilotMachinePhase.Stopped &&
            state.Phase !=
                NavigationAutoPilotMachinePhase.ManualFinalLeg)
        {
            return new NavigationAutoPilotMachineTransition(state);
        }

        return Arrive(
            state,
            $"Destination reached: {state.DestinationName}.",
            now);
    }

    private static NavigationAutoPilotMachineTransition
        ObserveReconcilingStep(
            NavigationAutoPilotMachineState state,
            NavigationAutoPilotMachineFrame frame)
    {
        if (!frame.IsStableSpace ||
            !frame.RouteAvailable ||
            !frame.RouteId.HasValue ||
            !frame.NavigationStateAvailable ||
            !frame.NavigationPropertiesAvailable ||
            string.IsNullOrWhiteSpace(
                frame.RouteCurrentSectorKey))
        {
            return WaitOrStop(
                state,
                frame.Now,
                stepReconcileTimeout,
                NavigationAutoPilotStopReason.ObservationUnavailable,
                "Auto Pilot stopped because the current route step did not become observable.",
                NavigationAutoPilotState.Starting,
                "Reconciling the current route step.");
        }

        if (state.StepGenerationSequence != 0 &&
            frame.NavigationGenerationSequence !=
                state.StepGenerationSequence)
        {
            return Stop(
                state,
                NavigationAutoPilotStopReason.ObservationUnavailable,
                "Auto Pilot stopped because the local world generation changed during the current route step.",
                frame.Now);
        }

        if (!string.Equals(
                frame.RouteCurrentSectorKey,
                state.ExpectedCurrentSectorKey,
                StringComparison.Ordinal))
        {
            return Stop(
                state,
                NavigationAutoPilotStopReason.UnexpectedSector,
                $"Auto Pilot is lost: navigation reports the unexpected sector {frame.RouteCurrentSectorName ?? frame.WorldSectorName}.",
                frame.Now);
        }

        if (!frame.IsWarpIdle)
        {
            return WaitOrStop(
                state,
                frame.Now,
                stepReconcileTimeout,
                NavigationAutoPilotStopReason.WarpUnavailable,
                "Auto Pilot stopped because native Warp state did not return to idle after the world transition.",
                NavigationAutoPilotState.Starting,
                frame.IsGateTransitionLocked
                    ? "Waiting for the gate-control lock to clear."
                    : frame.IsWarpRecovering
                        ? "Waiting for Warp recovery to finish."
                        : "Waiting for native Warp state to become idle.");
        }

        if (frame.RouteStatus == NavigationRouteStatus.NoRoute)
        {
            return Stop(
                state,
                NavigationAutoPilotStopReason.RouteChanged,
                string.IsNullOrWhiteSpace(frame.RouteStatusText)
                    ? "Auto Pilot stopped because the route became blocked."
                    : $"Auto Pilot stopped because {frame.RouteStatusText}",
                frame.Now);
        }

        if (frame.RouteStatus ==
                NavigationRouteStatus.DestinationReached ||
            frame.NextStep == null)
        {
            return Arrive(
                state,
                $"Arrived at destination {state.DestinationName}.",
                frame.Now);
        }

        if (!string.Equals(
                frame.NextStep.FromSectorKey,
                frame.RouteCurrentSectorKey,
                StringComparison.Ordinal))
        {
            return WaitOrStop(
                state,
                frame.Now,
                stepReconcileTimeout,
                NavigationAutoPilotStopReason.RouteChanged,
                "Auto Pilot stopped because the route did not reconcile with the current sector.",
                NavigationAutoPilotState.Starting,
                "Waiting for the route to reconcile with the current sector.");
        }

        if (!NavigationAutoPilotStepPlan.TryCreate(
                frame.NextStep,
                out var step))
        {
            var targetName = frame.NextStep.FinalTargetName ??
                state.DestinationName;

            return ManualFinalLeg(
                state,
                targetName,
                frame.RouteCurrentSectorName ??
                frame.WorldSectorName,
                frame.Now);
        }

        return new NavigationAutoPilotMachineTransition(
            state with
            {
                Phase = NavigationAutoPilotMachinePhase.ResolvingTarget,
                PhaseStartedAt = frame.Now,
                Step = step,
                StepStartedAt = frame.Now,
                TargetObjectId = null,
                VerbMissingSince = null,
                StepGenerationSequence =
                    frame.NavigationGenerationSequence,
                WarpRequestNavigationSequence = 0,
                WarpRequestGenerationSequence = 0,
                WarpRequestedAt = null,
                WarpRequestAccepted = false,
                WarpReachedActiveState = false,
                TransitionAccepted = false,
                ExpectedCurrentSectorName =
                    frame.RouteCurrentSectorName ??
                    state.ExpectedCurrentSectorName,
                PublicState = NavigationAutoPilotState.SelectingTarget,
                StopReason = NavigationAutoPilotStopReason.None,
                StatusText = $"Resolving route target {step.TargetName}.",
                CurrentEnergy = frame.CurrentEnergy,
            });
    }

    private static NavigationAutoPilotMachineTransition
        ObserveResolvingTarget(
            NavigationAutoPilotMachineState state,
            NavigationAutoPilotMachineFrame frame)
    {
        if (frame.Now - state.StepStartedAt >=
            targetResolutionTimeout)
        {
            return Stop(
                state,
                NavigationAutoPilotStopReason.TargetSelectionFailed,
                $"Auto Pilot stopped because route target {state.Step?.TargetName ?? "the next target"} did not become available.",
                frame.Now);
        }

        var contextFailure = ValidateStepContext(
            state,
            frame,
            targetResolutionTimeout,
            "Auto Pilot stopped because the next route target did not become observable.");

        if (contextFailure.HasValue)
        {
            return contextFailure.Value;
        }

        var step = state.Step!;

        if (frame.TargetResolution.Status ==
            NavigationTargetResolutionStatus.Ready &&
            frame.TargetResolution.ObjectId != 0)
        {
            return new NavigationAutoPilotMachineTransition(
                state with
                {
                    Phase = NavigationAutoPilotMachinePhase.SelectingTarget,
                    PhaseStartedAt = frame.Now,
                    PublicState = NavigationAutoPilotState.SelectingTarget,
                    StatusText = $"Selecting route target {step.TargetName}.",
                    CurrentEnergy = frame.CurrentEnergy,
                },
                NavigationAutoPilotEffectKind.SelectTarget);
        }

        if (frame.TargetResolution.Status ==
            NavigationTargetResolutionStatus.Invalid)
        {
            return Stop(
                state,
                NavigationAutoPilotStopReason.TargetSelectionFailed,
                string.IsNullOrWhiteSpace(
                    frame.TargetResolution.Detail)
                    ? "Auto Pilot could not resolve the next route target."
                    : frame.TargetResolution.Detail,
                frame.Now);
        }

        return WaitOrStop(
            state,
            frame.Now,
            targetResolutionTimeout,
            NavigationAutoPilotStopReason.TargetSelectionFailed,
            $"Auto Pilot stopped because route target {step.TargetName} did not become available.",
            NavigationAutoPilotState.SelectingTarget,
            $"Waiting for route target {step.TargetName} to become available.");
    }

    private static NavigationAutoPilotMachineTransition
        ObserveEvaluatingRange(
            NavigationAutoPilotMachineState state,
            NavigationAutoPilotMachineFrame frame)
    {
        var contextFailure = ValidateStepContext(
            state,
            frame,
            targetStateTimeout,
            "Auto Pilot stopped because the selected route target could not be verified.");

        if (contextFailure.HasValue)
        {
            return contextFailure.Value;
        }

        var targetFailure = ValidateSelectedTarget(
            state,
            frame,
            targetStateTimeout,
            NavigationAutoPilotStopReason.TargetSelectionFailed,
            "Auto Pilot stopped because the selected target no longer matches the planned route target.");

        if (targetFailure.HasValue)
        {
            return targetFailure.Value;
        }

        var step = state.Step!;

        if (!step.RequiresInteraction)
        {
            return BeginWaitingForWarp(
                state,
                frame.Now);
        }

        if (frame.VerbState ==
            NavigationAutoPilotVerbState.Executable)
        {
            return BeginVerbActivation(
                state,
                frame.Now,
                $"{step.TargetName} is already in {step.VerbName} range; warp is not required.");
        }

        if (frame.VerbState ==
            NavigationAutoPilotVerbState.TooFar)
        {
            return BeginWaitingForWarp(
                state,
                frame.Now);
        }

        if (frame.VerbState ==
            NavigationAutoPilotVerbState.Unavailable)
        {
            return Stop(
                state,
                step.UnavailableReason,
                $"Auto Pilot stopped because {step.VerbName} is currently unavailable.",
                frame.Now);
        }

        if (frame.VerbState ==
            NavigationAutoPilotVerbState.Missing)
        {
            return BeginWaitingForWarp(
                state,
                frame.Now);
        }

        return WaitOrStop(
            state,
            frame.Now,
            targetStateTimeout,
            step.UnavailableReason,
            $"Auto Pilot could not determine whether {step.VerbName} was available.",
            NavigationAutoPilotState.VerifyingArrival,
            $"Checking approach to {step.TargetName}.");
    }

    private static NavigationAutoPilotMachineTransition
        ObserveWaitingForWarp(
            NavigationAutoPilotMachineState state,
            NavigationAutoPilotMachineFrame frame)
    {
        var contextFailure = ValidateStepContext(
            state,
            frame,
            warpAvailableTimeout,
            "Auto Pilot stopped because warp readiness could not be observed.");

        if (contextFailure.HasValue)
        {
            return contextFailure.Value;
        }

        var targetFailure = ValidateSelectedTarget(
            state,
            frame,
            warpAvailableTimeout,
            NavigationAutoPilotStopReason.WarpInterrupted,
            "Auto Pilot was interrupted because the selected route target changed before warp started.");

        if (targetFailure.HasValue)
        {
            return targetFailure.Value;
        }

        var step = state.Step!;

        if (step.RequiresInteraction &&
            frame.VerbState ==
                NavigationAutoPilotVerbState.Executable)
        {
            return BeginVerbActivation(
                state,
                frame.Now,
                $"{step.TargetName} entered {step.VerbName} range while Auto Pilot was waiting for warp.");
        }

        if (step.RequiresInteraction &&
            frame.VerbState ==
                NavigationAutoPilotVerbState.Unavailable)
        {
            return Stop(
                state,
                step.UnavailableReason,
                $"Auto Pilot stopped because {step.VerbName} became unavailable before Warp was requested.",
                frame.Now);
        }

        if (!frame.NavigationStateAvailable ||
            !frame.NavigationPropertiesAvailable)
        {
            return WaitOrStop(
                state,
                frame.Now,
                warpAvailableTimeout,
                NavigationAutoPilotStopReason.ObservationUnavailable,
                "Auto Pilot stopped because native Warp readiness remained unavailable.",
                NavigationAutoPilotState.WaitingForWarp,
                $"Waiting for native Warp readiness before approaching {step.TargetName}.");
        }

        if (!frame.IsWarpIdle)
        {
            return Stop(
                state,
                NavigationAutoPilotStopReason.WarpInterrupted,
                "Auto Pilot stopped because native Warp state changed before it issued the Warp command.",
                frame.Now);
        }

        if (frame.IsClientWarpReady)
        {
            return new NavigationAutoPilotMachineTransition(
                state with
                {
                    Phase = NavigationAutoPilotMachinePhase.EngagingWarp,
                    PhaseStartedAt = frame.Now,
                    PublicState = NavigationAutoPilotState.EngagingWarp,
                    StatusText = $"Engaging warp toward {step.TargetName}.",
                    CurrentEnergy = frame.CurrentEnergy,
                    WarpRequestNavigationSequence =
                        frame.NavigationStateSequence,
                    WarpRequestGenerationSequence =
                        frame.NavigationGenerationSequence,
                    WarpRequestedAt = frame.Now,
                    WarpRequestAccepted = false,
                    WarpReachedActiveState = false,
                },
                NavigationAutoPilotEffectKind.EngageWarp);
        }

        return WaitOrStop(
            state,
            frame.Now,
            warpAvailableTimeout,
            NavigationAutoPilotStopReason.WarpUnavailable,
            "Auto Pilot stopped because native Warp readiness did not become true within 30 seconds.",
            NavigationAutoPilotState.WaitingForWarp,
            string.IsNullOrWhiteSpace(frame.ClientWarpReadinessReason)
                ? $"Waiting for the warp drive before approaching {step.TargetName}."
                : $"Waiting for the warp drive before approaching {step.TargetName}: {frame.ClientWarpReadinessReason}.");
    }

    private static NavigationAutoPilotMachineTransition
        ObserveWaitingForWarpStart(
            NavigationAutoPilotMachineState state,
            NavigationAutoPilotMachineFrame frame)
    {
        var routeContextFailure = ValidateRouteAndSectorWhileInSpace(
            state,
            frame,
            warpStartTimeout,
            "Auto Pilot stopped because warp start could not be observed.");

        if (routeContextFailure.HasValue)
        {
            return routeContextFailure.Value;
        }

        var terminalFailure = TryStopForTerminalWarpReason(
            state,
            frame);

        if (terminalFailure.HasValue)
        {
            return terminalFailure.Value;
        }

        if (!frame.NavigationStateAvailable ||
            !frame.NavigationPropertiesAvailable ||
            frame.NavigationGenerationSequence !=
                state.WarpRequestGenerationSequence ||
            frame.NavigationStateSequence <=
                state.WarpRequestNavigationSequence)
        {
            return WaitOrStop(
                state,
                frame.Now,
                warpStartTimeout,
                NavigationAutoPilotStopReason.ObservationUnavailable,
                "Auto Pilot issued Warp, but fresh native Warp state did not become observable.",
                NavigationAutoPilotState.EngagingWarp,
                $"Waiting for the game to accept Warp toward {state.Step?.TargetName ?? "the route target"}.");
        }

        if (frame.IsWarpStarting)
        {
            return new NavigationAutoPilotMachineTransition(
                state with
                {
                    Phase = NavigationAutoPilotMachinePhase.Warping,
                    PhaseStartedAt = frame.Now,
                    PublicState = NavigationAutoPilotState.EngagingWarp,
                    StatusText = $"Warp accepted; charging toward {state.Step?.TargetName ?? "the route target"}.",
                    WarpRequestAccepted = true,
                    WarpReachedActiveState = false,
                    CurrentEnergy = frame.CurrentEnergy,
                });
        }

        if (frame.IsWarpActive)
        {
            return new NavigationAutoPilotMachineTransition(
                state with
                {
                    Phase = NavigationAutoPilotMachinePhase.Warping,
                    PhaseStartedAt = frame.Now,
                    PublicState = NavigationAutoPilotState.Warping,
                    StatusText = $"Warping toward {state.Step?.TargetName ?? "the route target"}.",
                    WarpRequestAccepted = true,
                    WarpReachedActiveState = true,
                    CurrentEnergy = frame.CurrentEnergy,
                });
        }

        if (frame.IsWarpRecovering ||
            frame.IsGateTransitionLocked)
        {
            return Stop(
                state,
                NavigationAutoPilotStopReason.WarpInterrupted,
                frame.IsGateTransitionLocked
                    ? "Auto Pilot stopped because the client entered gate-transition control before Warp was observed starting."
                    : "Auto Pilot stopped because Warp entered recovery before active travel was observed.",
                frame.Now);
        }

        return WaitOrStop(
            state,
            frame.Now,
            warpStartTimeout,
            NavigationAutoPilotStopReason.WarpDidNotEngage,
            "Auto Pilot issued the warp command, but the game did not enter warp.",
            NavigationAutoPilotState.EngagingWarp,
            $"Engaging warp toward {state.Step?.TargetName ?? "the route target"}.");
    }

    private static NavigationAutoPilotMachineTransition ObserveWarping(
        NavigationAutoPilotMachineState state,
        NavigationAutoPilotMachineFrame frame)
    {
        var routeContextFailure = ValidateRouteAndSectorWhileInSpace(
            state,
            frame,
            warpCompletionTimeout,
            "Auto Pilot stopped because warp state became unavailable for too long.");

        if (routeContextFailure.HasValue)
        {
            return routeContextFailure.Value;
        }

        var terminalFailure = TryStopForTerminalWarpReason(
            state,
            frame);

        if (terminalFailure.HasValue)
        {
            return terminalFailure.Value;
        }

        if (!frame.NavigationStateAvailable ||
            !frame.NavigationPropertiesAvailable ||
            frame.NavigationGenerationSequence !=
                state.WarpRequestGenerationSequence)
        {
            return WaitOrStop(
                state,
                frame.Now,
                warpCompletionTimeout,
                NavigationAutoPilotStopReason.ObservationUnavailable,
                "Auto Pilot stopped because current-generation native Warp state remained unavailable.",
                NavigationAutoPilotState.Warping,
                $"Warping toward {state.Step?.TargetName ?? "the route target"}.");
        }

        if (frame.IsWarpStarting)
        {
            return new NavigationAutoPilotMachineTransition(
                state with
                {
                    CurrentEnergy = frame.CurrentEnergy,
                    PublicState = NavigationAutoPilotState.EngagingWarp,
                    StatusText = $"Warp drive is charging toward {state.Step?.TargetName ?? "the route target"}.",
                    WarpRequestAccepted = true,
                });
        }

        if (frame.IsWarpActive)
        {
            return new NavigationAutoPilotMachineTransition(
                state with
                {
                    CurrentEnergy = frame.CurrentEnergy,
                    PublicState = NavigationAutoPilotState.Warping,
                    StatusText = $"Warping toward {state.Step?.TargetName ?? "the route target"}.",
                    WarpRequestAccepted = true,
                    WarpReachedActiveState = true,
                });
        }

        if (frame.IsWarpRecovering)
        {
            if (CanActivateExpectedVerbDuringRecovery(
                    state,
                    frame))
            {
                return BeginVerbActivation(
                    state,
                    frame.Now,
                    $"{state.Step!.TargetName} is ready for {state.Step.VerbName}; activating during Warp recovery.");
            }

            return new NavigationAutoPilotMachineTransition(
                state with
                {
                    CurrentEnergy = frame.CurrentEnergy,
                    PublicState = NavigationAutoPilotState.VerifyingArrival,
                    StatusText = $"Warp is ending near {state.Step?.TargetName ?? "the route target"}.",
                });
        }

        if (frame.IsGateTransitionLocked)
        {
            return Stop(
                state,
                NavigationAutoPilotStopReason.WarpInterrupted,
                "Auto Pilot stopped because gate-transition control appeared before the planned target action was activated.",
                frame.Now);
        }

        if (!frame.IsWarpIdle)
        {
            return WaitOrStop(
                state,
                frame.Now,
                warpCompletionTimeout,
                NavigationAutoPilotStopReason.WarpInterrupted,
                "Auto Pilot stopped because native Warp state became unrecognized.",
                NavigationAutoPilotState.Warping,
                $"Observing Warp toward {state.Step?.TargetName ?? "the route target"}.");
        }

        if (!state.WarpReachedActiveState)
        {
            return Stop(
                state,
                NavigationAutoPilotStopReason.WarpDidNotEngage,
                "Auto Pilot stopped because Warp returned to idle before active travel was observed.",
                frame.Now);
        }

        return Move(
            state,
            state.Step is { RequiresInteraction: true }
                ? NavigationAutoPilotMachinePhase.WaitingForVerb
                : NavigationAutoPilotMachinePhase.WaitingForArrival,
            NavigationAutoPilotState.VerifyingArrival,
            $"Verifying arrival at {state.Step?.TargetName ?? "the route target"}.",
            frame.Now);
    }

    private static NavigationAutoPilotMachineTransition
        ObserveWaitingForArrival(
            NavigationAutoPilotMachineState state,
            NavigationAutoPilotMachineFrame frame)
    {
        var contextFailure = ValidateStepContext(
            state,
            frame,
            targetArrivalTimeout,
            "Auto Pilot stopped because the route target did not remain observable after warp.");

        if (contextFailure.HasValue)
        {
            return contextFailure.Value;
        }

        var terminalFailure = TryStopForTerminalWarpReason(
            state,
            frame);

        if (terminalFailure.HasValue)
        {
            return terminalFailure.Value;
        }

        var step = state.Step;

        if (step == null ||
            step.RequiresInteraction)
        {
            return Stop(
                state,
                NavigationAutoPilotStopReason.InternalError,
                "Auto Pilot stopped because final-arrival state became inconsistent.",
                frame.Now);
        }

        if (!frame.SelectedTargetKnown ||
            !frame.HasSelectedTarget ||
            frame.SelectedTargetObjectId !=
                state.TargetObjectId)
        {
            return WaitOrStop(
                state,
                frame.Now,
                targetArrivalTimeout,
                NavigationAutoPilotStopReason.WarpInterrupted,
                "Auto Pilot was interrupted because warp ended without the planned route target selected.",
                NavigationAutoPilotState.VerifyingArrival,
                $"Waiting for {step.TargetName} to become the final warp target.");
        }

        return Arrive(
            state,
            $"Arrived at destination {state.DestinationName}.",
            frame.Now);
    }

    private static NavigationAutoPilotMachineTransition
        ObserveWaitingForVerb(
            NavigationAutoPilotMachineState state,
            NavigationAutoPilotMachineFrame frame)
    {
        var contextFailure = ValidateStepContext(
            state,
            frame,
            targetVerbTimeout,
            "Auto Pilot stopped because the route target did not become ready after warp.");

        if (contextFailure.HasValue)
        {
            return contextFailure.Value;
        }

        var terminalFailure = TryStopForTerminalWarpReason(
            state,
            frame);

        if (terminalFailure.HasValue)
        {
            return terminalFailure.Value;
        }

        var step = state.Step;

        if (step == null ||
            !step.RequiresInteraction)
        {
            return Stop(
                state,
                NavigationAutoPilotStopReason.InternalError,
                "Auto Pilot stopped because target-interaction state became inconsistent.",
                frame.Now);
        }

        if (!frame.SelectedTargetKnown ||
            !frame.HasSelectedTarget ||
            frame.SelectedTargetObjectId !=
                state.TargetObjectId)
        {
            return WaitOrStop(
                state,
                frame.Now,
                targetVerbTimeout,
                NavigationAutoPilotStopReason.WarpInterrupted,
                "Auto Pilot was interrupted because warp ended without the planned route target selected.",
                NavigationAutoPilotState.VerifyingArrival,
                $"Waiting for {step.TargetName} to become the final warp target.");
        }

        if (frame.VerbState ==
            NavigationAutoPilotVerbState.Executable)
        {
            return BeginVerbActivation(
                state,
                frame.Now,
                $"{step.TargetName} is ready for {step.VerbName}.");
        }

        if (frame.VerbState ==
            NavigationAutoPilotVerbState.Unavailable)
        {
            return Stop(
                state,
                step.UnavailableReason,
                $"Auto Pilot stopped because {step.VerbName} is currently unavailable.",
                frame.Now);
        }

        return WaitOrStop(
            state,
            frame.Now,
            targetVerbTimeout,
            NavigationAutoPilotStopReason.WarpInterrupted,
            $"Auto Pilot stopped because Warp ended before {step.VerbName} became available at {step.TargetName}.",
            NavigationAutoPilotState.VerifyingArrival,
            $"Waiting for {step.VerbName} at {step.TargetName}.");
    }

    private static NavigationAutoPilotMachineTransition
        ObserveWaitingForTransition(
            NavigationAutoPilotMachineState state,
            NavigationAutoPilotMachineFrame frame)
    {
        var step = state.Step;

        if (step == null || !step.IsSectorTransition)
        {
            return Stop(
                state,
                NavigationAutoPilotStopReason.InternalError,
                "Auto Pilot stopped because sector-transition state became inconsistent.",
                frame.Now);
        }

        var transitionAccepted =
            state.TransitionAccepted ||
            frame.IsGateTransitionLocked ||
            frame.LoadingOrTransitionFlag != 0 ||
            !frame.WorldAvailable ||
            frame.NavigationGenerationChanged ||
            frame.NavigationPhase is
                ClientNavigationStatePhase.GateTransitionLocked or
                ClientNavigationStatePhase.AwaitingWorldReplacement or
                ClientNavigationStatePhase.Loading;

        if (frame.IsStableSpace &&
            frame.RouteAvailable &&
            string.Equals(
                frame.RouteCurrentSectorKey,
                step.ToSectorKey,
                StringComparison.Ordinal))
        {
            return new NavigationAutoPilotMachineTransition(
                state with
                {
                    ExpectedCurrentSectorKey = step.ToSectorKey,
                    ExpectedCurrentSectorName = step.ToSectorName,
                    Phase = NavigationAutoPilotMachinePhase.ReconcilingStep,
                    PhaseStartedAt = frame.Now,
                    Step = null,
                    TargetObjectId = null,
                    VerbMissingSince = null,
                    StepGenerationSequence = 0,
                    WarpRequestNavigationSequence = 0,
                    WarpRequestGenerationSequence = 0,
                    WarpRequestedAt = null,
                    WarpRequestAccepted = false,
                    WarpReachedActiveState = false,
                    TransitionAccepted = false,
                    PublicState = NavigationAutoPilotState.WaitingForSector,
                    StopReason = NavigationAutoPilotStopReason.None,
                    StatusText = $"Sector {step.ToSectorName} entered; reconciling the next route step.",
                    CurrentEnergy = frame.CurrentEnergy,
                });
        }

        if (frame.IsStableSpace &&
            frame.RouteAvailable &&
            !string.IsNullOrWhiteSpace(
                frame.RouteCurrentSectorKey) &&
            !string.Equals(
                frame.RouteCurrentSectorKey,
                step.FromSectorKey,
                StringComparison.Ordinal) &&
            !string.Equals(
                frame.RouteCurrentSectorKey,
                step.ToSectorKey,
                StringComparison.Ordinal))
        {
            return Stop(
                state,
                NavigationAutoPilotStopReason.UnexpectedSector,
                $"Auto Pilot is lost: expected sector {step.ToSectorName}, but arrived in {frame.RouteCurrentSectorName ?? frame.WorldSectorName}.",
                frame.Now);
        }

        var waitingState = transitionAccepted &&
                           !state.TransitionAccepted
            ? state with
            {
                TransitionAccepted = true,
                StatusText =
                    $"Gate transition accepted; waiting to enter sector {step.ToSectorName}.",
            }
            : state;

        if (frame.Now - state.PhaseStartedAt >=
            sectorTransitionTimeout)
        {
            return Stop(
                waitingState,
                transitionAccepted
                    ? step.TransitionTimedOutReason
                    : NavigationAutoPilotStopReason.GateActivationFailed,
                transitionAccepted
                    ? $"Auto Pilot stopped because sector {step.ToSectorName} did not become ready within 30 seconds."
                    : $"Auto Pilot activated Gate on {step.TargetName}, but the client never entered gate-transition control.",
                frame.Now);
        }

        return new NavigationAutoPilotMachineTransition(
            waitingState with
            {
                PublicState = NavigationAutoPilotState.WaitingForSector,
                StatusText = transitionAccepted
                    ? $"Gate transition accepted; waiting to enter sector {step.ToSectorName}."
                    : $"Waiting for {step.TargetName} to begin the gate transition.",
            });
    }

    private static NavigationAutoPilotMachineTransition
        ObserveWaitingForDestination(
            NavigationAutoPilotMachineState state,
            NavigationAutoPilotMachineFrame frame)
    {
        var step = state.Step;

        if (step == null || step.IsSectorTransition)
        {
            return Stop(
                state,
                NavigationAutoPilotStopReason.InternalError,
                "Auto Pilot stopped because destination-transition state became inconsistent.",
                frame.Now);
        }

        if (frame.IsStableStarbase)
        {
            return Arrive(
                state,
                $"Arrived at destination {state.DestinationName}.",
                frame.Now);
        }

        var status = frame.DockingRequestObserved
            ? $"Docking at {step.TargetName}."
            : $"Waiting for {step.TargetName} to accept the docking request.";

        return WaitOrStop(
            state,
            frame.Now,
            destinationTransitionTimeout,
            step.TransitionTimedOutReason,
            frame.DockingRequestObserved
                ? $"Auto Pilot began docking, but {step.TargetName} did not become ready within 30 seconds."
                : $"Auto Pilot sent {step.VerbName}, but {step.TargetName} did not become ready within 30 seconds.",
            NavigationAutoPilotState.WaitingForDestination,
            status);
    }

    private static NavigationAutoPilotMachineTransition?
        TryStopForTerminalWarpReason(
            NavigationAutoPilotMachineState state,
            NavigationAutoPilotMachineFrame frame)
    {
        if (!state.WarpRequestedAt.HasValue ||
            !frame.LastTerminalWarpReason.HasValue ||
            !frame.LastTerminalWarpReasonAt.HasValue ||
            frame.LastTerminalWarpReasonAt.Value <
                state.WarpRequestedAt.Value)
        {
            return null;
        }

        var reason = frame.LastTerminalWarpReason.Value;

        if (reason == 8)
        {
            return null;
        }

        var targetName = state.Step?.TargetName ??
            "the route target";

        return reason switch
        {
            5 => Stop(
                state,
                NavigationAutoPilotStopReason.WarpUnavailable,
                "Auto Pilot stopped because the Warp engine became unavailable.",
                frame.Now),
            6 => Stop(
                state,
                NavigationAutoPilotStopReason.WarpInterrupted,
                $"Auto Pilot stopped because local gravity interference ended Warp before reaching {targetName}.",
                frame.Now),
            7 => Stop(
                state,
                NavigationAutoPilotStopReason.WarpInterrupted,
                $"Auto Pilot stopped because the Warp target was lost before reaching {targetName}.",
                frame.Now),
            9 => Stop(
                state,
                NavigationAutoPilotStopReason.WarpInterrupted,
                $"Auto Pilot stopped because Warp followers were disrupted while travelling toward {targetName}.",
                frame.Now),
            10 => Stop(
                state,
                NavigationAutoPilotStopReason.WarpUnavailable,
                $"Auto Pilot stopped because reactor energy became too low for Warp toward {targetName}.",
                frame.Now),
            11 => Stop(
                state,
                NavigationAutoPilotStopReason.WarpUnavailable,
                $"Auto Pilot stopped because the ship ran out of Warp energy before reaching {targetName}.",
                frame.Now),
            _ => null,
        };
    }

    private static NavigationAutoPilotMachineTransition?
        ValidateRouteIdentity(
            NavigationAutoPilotMachineState state,
            NavigationAutoPilotMachineFrame frame)
    {
        if (frame.RouteId.HasValue &&
            frame.RouteId.Value != state.RouteId)
        {
            return Stop(
                state,
                NavigationAutoPilotStopReason.RouteChanged,
                "Auto Pilot stopped because the planned route changed.",
                frame.Now);
        }


        return null;
    }

    private static NavigationAutoPilotMachineTransition?
        ValidateStepContext(
            NavigationAutoPilotMachineState state,
            NavigationAutoPilotMachineFrame frame,
            TimeSpan timeout,
            string timeoutStatus)
    {
        if (!frame.IsStableSpace ||
            !frame.RouteAvailable ||
            !frame.RouteId.HasValue ||
            !frame.NavigationStateAvailable ||
            !frame.NavigationPropertiesAvailable ||
            string.IsNullOrWhiteSpace(
                frame.RouteCurrentSectorKey))
        {
            if (frame.Now - state.PhaseStartedAt >= timeout)
            {
                return Stop(
                    state,
                    NavigationAutoPilotStopReason.ObservationUnavailable,
                    timeoutStatus,
                    frame.Now);
            }

            return new NavigationAutoPilotMachineTransition(
                state with
                {
                    CurrentEnergy = frame.CurrentEnergy,
                });
        }

        if (state.StepGenerationSequence != 0 &&
            frame.NavigationGenerationSequence !=
                state.StepGenerationSequence)
        {
            return Stop(
                state,
                NavigationAutoPilotStopReason.ObservationUnavailable,
                "Auto Pilot stopped because the local world generation changed during the current route step.",
                frame.Now);
        }

        if (!string.Equals(
                frame.RouteCurrentSectorKey,
                state.ExpectedCurrentSectorKey,
                StringComparison.Ordinal))
        {
            return Stop(
                state,
                NavigationAutoPilotStopReason.UnexpectedSector,
                $"Auto Pilot is lost: navigation reports the unexpected sector {frame.RouteCurrentSectorName ?? frame.WorldSectorName}.",
                frame.Now);
        }

        if (frame.RouteStatus == NavigationRouteStatus.NoRoute)
        {
            return Stop(
                state,
                NavigationAutoPilotStopReason.RouteChanged,
                "Auto Pilot stopped because the route became blocked.",
                frame.Now);
        }

        if (state.Step == null ||
            !state.Step.Matches(frame.NextStep))
        {
            return Stop(
                state,
                NavigationAutoPilotStopReason.RouteChanged,
                "Auto Pilot stopped because the current route step changed unexpectedly.",
                frame.Now);
        }

        return null;
    }

    private static NavigationAutoPilotMachineTransition?
        ValidateRouteAndSectorWhileInSpace(
            NavigationAutoPilotMachineState state,
            NavigationAutoPilotMachineFrame frame,
            TimeSpan timeout,
            string timeoutStatus)
    {
        if (!frame.IsStableSpace ||
            !frame.RouteAvailable ||
            string.IsNullOrWhiteSpace(
                frame.RouteCurrentSectorKey))
        {
            if (frame.Now - state.PhaseStartedAt >= timeout)
            {
                return Stop(
                    state,
                    NavigationAutoPilotStopReason.ObservationUnavailable,
                    timeoutStatus,
                    frame.Now);
            }

            return new NavigationAutoPilotMachineTransition(state);
        }

        if (!string.Equals(
                frame.RouteCurrentSectorKey,
                state.ExpectedCurrentSectorKey,
                StringComparison.Ordinal))
        {
            return Stop(
                state,
                NavigationAutoPilotStopReason.UnexpectedSector,
                $"Auto Pilot is lost: navigation reports the unexpected sector {frame.RouteCurrentSectorName ?? frame.WorldSectorName}.",
                frame.Now);
        }

        if (state.Step == null ||
            !state.Step.Matches(frame.NextStep))
        {
            return Stop(
                state,
                NavigationAutoPilotStopReason.RouteChanged,
                "Auto Pilot stopped because the current route step changed unexpectedly.",
                frame.Now);
        }

        return null;
    }

    private static NavigationAutoPilotMachineTransition?
        ValidateSelectedTarget(
            NavigationAutoPilotMachineState state,
            NavigationAutoPilotMachineFrame frame,
            TimeSpan timeout,
            NavigationAutoPilotStopReason mismatchReason,
            string mismatchStatus)
    {
        if (!state.TargetObjectId.HasValue ||
            state.TargetObjectId.Value == 0)
        {
            return Stop(
                state,
                NavigationAutoPilotStopReason.InternalError,
                "Auto Pilot stopped because selected-target state became inconsistent.",
                frame.Now);
        }

        if (!frame.SelectedTargetKnown)
        {
            if (frame.Now - state.PhaseStartedAt >= timeout)
            {
                return Stop(
                    state,
                    NavigationAutoPilotStopReason.ObservationUnavailable,
                    "Auto Pilot could not verify the currently selected route target.",
                    frame.Now);
            }

            return new NavigationAutoPilotMachineTransition(state);
        }

        if (!frame.HasSelectedTarget ||
            frame.SelectedTargetObjectId !=
                state.TargetObjectId.Value)
        {
            return Stop(
                state,
                mismatchReason,
                mismatchStatus,
                frame.Now);
        }

        return null;
    }

    private static bool CanActivateExpectedVerbDuringRecovery(
        NavigationAutoPilotMachineState state,
        NavigationAutoPilotMachineFrame frame)
    {
        return state.Step != null &&
               state.Step.RequiresInteraction &&
               state.TargetObjectId.HasValue &&
               frame.IsInteractionControlReady &&
               frame.SelectedTargetKnown &&
               frame.HasSelectedTarget &&
               frame.SelectedTargetObjectId ==
                   state.TargetObjectId.Value &&
               frame.PathBuildStateKnown &&
               !frame.PathBuildBusy &&
               frame.VerbState ==
                   NavigationAutoPilotVerbState.Executable;
    }

    private static NavigationAutoPilotMachineTransition
        BeginWaitingForWarp(
            NavigationAutoPilotMachineState state,
            DateTimeOffset now)
    {
        return Move(
            state with
            {
                VerbMissingSince = null,
            },
            NavigationAutoPilotMachinePhase.WaitingForWarp,
            NavigationAutoPilotState.WaitingForWarp,
            $"Waiting for the warp drive before approaching {state.Step?.TargetName ?? "the route target"}.",
            now);
    }

    private static NavigationAutoPilotMachineTransition
        BeginVerbActivation(
            NavigationAutoPilotMachineState state,
            DateTimeOffset now,
            string status)
    {
        var step = state.Step;

        if (step == null ||
            !step.RequiresInteraction)
        {
            return Stop(
                state,
                NavigationAutoPilotStopReason.InternalError,
                "Auto Pilot stopped because target-activation state became inconsistent.",
                now);
        }

        return new NavigationAutoPilotMachineTransition(
            state with
            {
                Phase = NavigationAutoPilotMachinePhase.ActivatingVerb,
                PhaseStartedAt = now,
                PublicState = step.ActivatingState,
                StopReason = NavigationAutoPilotStopReason.None,
                StatusText = status,
            },
            NavigationAutoPilotEffectKind.ActivateVerb);
    }

    private static NavigationAutoPilotMachineTransition WaitOrStop(
        NavigationAutoPilotMachineState state,
        DateTimeOffset now,
        TimeSpan timeout,
        NavigationAutoPilotStopReason timeoutReason,
        string timeoutStatus,
        NavigationAutoPilotState publicState,
        string waitingStatus)
    {
        if (now - state.PhaseStartedAt >= timeout)
        {
            return Stop(
                state,
                timeoutReason,
                timeoutStatus,
                now);
        }

        return new NavigationAutoPilotMachineTransition(
            state with
            {
                PublicState = publicState,
                StopReason = NavigationAutoPilotStopReason.None,
                StatusText = waitingStatus,
            });
    }

    private static NavigationAutoPilotMachineTransition Move(
        NavigationAutoPilotMachineState state,
        NavigationAutoPilotMachinePhase phase,
        NavigationAutoPilotState publicState,
        string statusText,
        DateTimeOffset now)
    {
        return new NavigationAutoPilotMachineTransition(
            state with
            {
                Phase = phase,
                PhaseStartedAt = now,
                PublicState = publicState,
                StopReason = NavigationAutoPilotStopReason.None,
                StatusText = statusText,
                VerbMissingSince = null,
            });
    }

    private static NavigationAutoPilotMachineTransition Arrive(
        NavigationAutoPilotMachineState state,
        string statusText,
        DateTimeOffset now)
    {
        return new NavigationAutoPilotMachineTransition(
            state with
            {
                Phase = NavigationAutoPilotMachinePhase.Arrived,
                PhaseStartedAt = now,
                PublicState = NavigationAutoPilotState.Arrived,
                StopReason =
                    NavigationAutoPilotStopReason.DestinationReached,
                StatusText = statusText,
            });
    }

    private static NavigationAutoPilotMachineTransition ManualFinalLeg(
        NavigationAutoPilotMachineState state,
        string targetName,
        string sectorName,
        DateTimeOffset now)
    {
        var location = string.IsNullOrWhiteSpace(sectorName)
            ? "the destination sector"
            : sectorName;

        return new NavigationAutoPilotMachineTransition(
            state with
            {
                Phase = NavigationAutoPilotMachinePhase.ManualFinalLeg,
                PhaseStartedAt = now,
                PublicState =
                    NavigationAutoPilotState.ManualFinalLeg,
                StopReason =
                    NavigationAutoPilotStopReason.DestinationReached,
                StatusText = $"Arrived in {location}; final target {targetName} remains the manual final leg.",
            });
    }

    private static NavigationAutoPilotMachineTransition Stop(
        NavigationAutoPilotMachineState state,
        NavigationAutoPilotStopReason stopReason,
        string statusText,
        DateTimeOffset now)
    {
        return new NavigationAutoPilotMachineTransition(
            state with
            {
                Phase = NavigationAutoPilotMachinePhase.Stopped,
                PhaseStartedAt = now,
                PublicState = NavigationAutoPilotState.Stopped,
                StopReason = stopReason,
                StatusText = statusText,
            });
    }
}
