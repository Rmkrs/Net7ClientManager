namespace Net7ClientManager.Navigation;

using System.Globalization;
using Net7ClientManager.Observations;
using Net7ClientManager.Observations.Models;

#if DEBUG
internal static class NavigationAutoPilotStateMachineScenarios
{
    private static readonly Guid routeId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static void Validate()
    {
        ValidateSameSectorStationJourney();
        ValidateAlreadyInRangeStationJourney();
        ValidateGateThenStationJourney();
        ValidateSlowGateTransition();
        ValidateLatePostSectorTargetPublication();
        ValidateRouteLagAfterSectorTransition();
        ValidatePostGateGlobalRecoveryResidue();
        ValidateGateActivationDuringWarpRecovery();
        ValidateMissingVerbRequiresWarp();
        ValidateUnexpectedWarpStateBlocksCommand();
        ValidateGravityInterferenceStopsWarp();
        ValidateUnexpectedWarpAfterCleanBaseline();
        ValidateTransientSelectionRetry();
        ValidateTargetInterruption();
        ValidateUnexpectedSector();
        ValidateRouteChange();
        ValidateUserStop();
        ValidateDestinationArrivalAfterStop();
        ValidateNavigationPointJourney();
        ValidateIncompleteFinalTargetRemainsManual();
    }

    private static void ValidateNavigationPointJourney()
    {
        var now = DateTimeOffset.Parse(
            "2026-07-05T14:00:00+00:00",
            CultureInfo.InvariantCulture);
        var navigationPoint = CreateNavigationPointStep(
            number: 1,
            sectorKey: "jupiter",
            sectorName: "Jupiter",
            targetName: "Jupiter Nav 4");
        var state = CreateState(
            now,
            "jupiter",
            "Jupiter",
            "Jupiter Nav 4");

        state = Reconcile(
            state,
            now,
            "jupiter",
            "Jupiter",
            navigationPoint);
        AssertPhase(
            state,
            NavigationAutoPilotMachinePhase.ResolvingTarget,
            "navigation-point reconcile");

        var selecting = NavigationAutoPilotStateMachine.Observe(
            state,
            CreateFrame(
                state,
                now.AddMilliseconds(50),
                "jupiter",
                "Jupiter",
                navigationPoint,
                targetResolution:
                    NavigationTargetResolutionResult.Ready(
                        navigationPoint.FinalTargetName!,
                        "Navigation target",
                        400,
                        7)));
        AssertEffect(
            selecting,
            NavigationAutoPilotEffectKind.SelectTarget,
            "navigation-point selection");

        state = NavigationAutoPilotStateMachine
            .ApplyTargetSelection(
                selecting.State,
                NavigationAutoPilotTargetSelectionOutcome.Success(
                    400,
                    navigationPoint.FinalTargetName!),
                now.AddMilliseconds(100))
            .State;

        state = NavigationAutoPilotStateMachine.Observe(
            state,
            CreateFrame(
                state,
                now.AddMilliseconds(150),
                "jupiter",
                "Jupiter",
                navigationPoint,
                selectedTargetObjectId: 400,
                verbState: NavigationAutoPilotVerbState.Missing,
                warpAvailable: 0))
            .State;
        AssertPhase(
            state,
            NavigationAutoPilotMachinePhase.WaitingForWarp,
            "navigation-point range");

        var engage = NavigationAutoPilotStateMachine.Observe(
            state,
            CreateFrame(
                state,
                now.AddMilliseconds(200),
                "jupiter",
                "Jupiter",
                navigationPoint,
                selectedTargetObjectId: 400,
                verbState: NavigationAutoPilotVerbState.Missing,
                warpAvailable: 2));
        AssertEffect(
            engage,
            NavigationAutoPilotEffectKind.EngageWarp,
            "navigation-point warp command");

        state = NavigationAutoPilotStateMachine.ApplyWarpCommand(
                engage.State,
                NavigationAutoPilotEffectOutcome.Success(),
                now.AddMilliseconds(250))
            .State;

        state = NavigationAutoPilotStateMachine.Observe(
            state,
            CreateFrame(
                state,
                now.AddMilliseconds(300),
                "jupiter",
                "Jupiter",
                navigationPoint,
                privateWarpState: 1,
                globalWarpState: 1,
                warpAvailable: 0))
            .State;
        AssertPhase(
            state,
            NavigationAutoPilotMachinePhase.Warping,
            "navigation-point warp acceptance");

        state = NavigationAutoPilotStateMachine.Observe(
            state,
            CreateFrame(
                state,
                now.AddSeconds(1),
                "jupiter",
                "Jupiter",
                navigationPoint,
                privateWarpState: 2,
                globalWarpState: 2,
                warpAvailable: 0))
            .State;
        AssertPhase(
            state,
            NavigationAutoPilotMachinePhase.Warping,
            "navigation-point active warp");

        state = NavigationAutoPilotStateMachine.Observe(
            state,
            CreateFrame(
                state,
                now.AddSeconds(2),
                "jupiter",
                "Jupiter",
                navigationPoint,
                selectedTargetObjectId: 400,
                verbState: NavigationAutoPilotVerbState.Missing,
                privateWarpState: 0,
                globalWarpState: 0,
                warpAvailable: 2))
            .State;
        AssertPhase(
            state,
            NavigationAutoPilotMachinePhase.WaitingForArrival,
            "navigation-point final target verification");

        state = NavigationAutoPilotStateMachine.Observe(
            state,
            CreateFrame(
                state,
                now.AddSeconds(2.1),
                "jupiter",
                "Jupiter",
                navigationPoint,
                selectedTargetObjectId: 400,
                verbState: NavigationAutoPilotVerbState.Missing,
                privateWarpState: 0,
                globalWarpState: 0,
                warpAvailable: 2))
            .State;
        AssertPhase(
            state,
            NavigationAutoPilotMachinePhase.Arrived,
            "navigation-point arrival");
    }

    private static void ValidateSameSectorStationJourney()
    {
        var now = DateTimeOffset.Parse(
            "2026-07-02T12:00:00+00:00",
            CultureInfo.InvariantCulture);
        var station = CreateStationStep(
            number: 1,
            sectorKey: "glenn",
            sectorName: "Glenn",
            targetName: "Friendship 7 Recreation Port");
        var state = CreateState(
            now,
            "glenn",
            "Glenn",
            "Friendship 7 Recreation Port");

        state = Reconcile(state, now, "glenn", "Glenn", station);
        AssertPhase(
            state,
            NavigationAutoPilotMachinePhase.ResolvingTarget,
            "same-sector station reconcile");

        var selecting = NavigationAutoPilotStateMachine.Observe(
            state,
            CreateFrame(
                state,
                now.AddMilliseconds(50),
                "glenn",
                "Glenn",
                station,
                targetResolution:
                    NavigationTargetResolutionResult.Ready(
                        station.FinalTargetName!,
                        "Station",
                        100,
                        7)));
        AssertEffect(
            selecting,
            NavigationAutoPilotEffectKind.SelectTarget,
            "same-sector station selection");

        state = NavigationAutoPilotStateMachine
            .ApplyTargetSelection(
                selecting.State,
                NavigationAutoPilotTargetSelectionOutcome.Success(
                    100,
                    station.FinalTargetName!),
                now.AddMilliseconds(100))
            .State;

        state = NavigationAutoPilotStateMachine.Observe(
            state,
            CreateFrame(
                state,
                now.AddMilliseconds(150),
                "glenn",
                "Glenn",
                station,
                selectedTargetObjectId: 100,
                verbState: NavigationAutoPilotVerbState.TooFar,
                warpAvailable: 0))
            .State;
        AssertPhase(
            state,
            NavigationAutoPilotMachinePhase.WaitingForWarp,
            "same-sector station range");

        var engage = NavigationAutoPilotStateMachine.Observe(
            state,
            CreateFrame(
                state,
                now.AddMilliseconds(200),
                "glenn",
                "Glenn",
                station,
                selectedTargetObjectId: 100,
                verbState: NavigationAutoPilotVerbState.TooFar,
                warpAvailable: 2));
        AssertEffect(
            engage,
            NavigationAutoPilotEffectKind.EngageWarp,
            "same-sector station warp command");

        state = NavigationAutoPilotStateMachine.ApplyWarpCommand(
                engage.State,
                NavigationAutoPilotEffectOutcome.Success(),
                now.AddMilliseconds(250))
            .State;

        state = NavigationAutoPilotStateMachine.Observe(
            state,
            CreateFrame(
                state,
                now.AddMilliseconds(300),
                "glenn",
                "Glenn",
                station,
                privateWarpState: 1,
                globalWarpState: 1,
                warpAvailable: 0))
            .State;
        AssertPhase(
            state,
            NavigationAutoPilotMachinePhase.Warping,
            "same-sector station warp acceptance");

        state = NavigationAutoPilotStateMachine.Observe(
            state,
            CreateFrame(
                state,
                now.AddSeconds(1),
                "glenn",
                "Glenn",
                station,
                privateWarpState: 2,
                globalWarpState: 2,
                warpAvailable: 0))
            .State;
        AssertPhase(
            state,
            NavigationAutoPilotMachinePhase.Warping,
            "same-sector station active warp");

        var dock = NavigationAutoPilotStateMachine.Observe(
            state,
            CreateFrame(
                state,
                now.AddSeconds(1.8),
                "glenn",
                "Glenn",
                station,
                selectedTargetObjectId: 100,
                verbState: NavigationAutoPilotVerbState.Executable,
                privateWarpState: 3,
                globalWarpState: 3,
                warpAvailable: 0));
        AssertEffect(
            dock,
            NavigationAutoPilotEffectKind.ActivateVerb,
            "same-sector station dock during Warp recovery");

        state = NavigationAutoPilotStateMachine
            .ApplyVerbActivation(
                dock.State,
                NavigationAutoPilotEffectOutcome.Success(),
                now.AddSeconds(1.9))
            .State;

        state = NavigationAutoPilotStateMachine.Observe(
            state,
            CreateFrame(
                state,
                now.AddSeconds(3),
                "glenn",
                "Glenn",
                station,
                environment:
                    ClientWorldEnvironment.Starbase))
            .State;
        AssertPhase(
            state,
            NavigationAutoPilotMachinePhase.Arrived,
            "same-sector station arrival");
    }

    private static void ValidateAlreadyInRangeStationJourney()
    {
        var now = DateTimeOffset.Parse(
            "2026-07-02T13:00:00+00:00",
            CultureInfo.InvariantCulture);
        var station = CreateStationStep(
            1,
            "saturn",
            "Saturn",
            "Net-7 SOL");
        var state = Reconcile(
            CreateState(now, "saturn", "Saturn", "Net-7 SOL"),
            now,
            "saturn",
            "Saturn",
            station);

        var selecting = NavigationAutoPilotStateMachine.Observe(
            state,
            CreateFrame(
                state,
                now.AddMilliseconds(50),
                "saturn",
                "Saturn",
                station,
                targetResolution:
                    NavigationTargetResolutionResult.Ready(
                        "Net-7 SOL",
                        "Station",
                        200,
                        8)));

        state = NavigationAutoPilotStateMachine
            .ApplyTargetSelection(
                selecting.State,
                NavigationAutoPilotTargetSelectionOutcome.Success(
                    200,
                    "Net-7 SOL"),
                now.AddMilliseconds(100))
            .State;

        var dock = NavigationAutoPilotStateMachine.Observe(
            state,
            CreateFrame(
                state,
                now.AddMilliseconds(150),
                "saturn",
                "Saturn",
                station,
                selectedTargetObjectId: 200,
                verbState: NavigationAutoPilotVerbState.Executable,
                warpAvailable: 2));

        AssertEffect(
            dock,
            NavigationAutoPilotEffectKind.ActivateVerb,
            "already-in-range station skips warp");
    }

    private static void ValidateGateThenStationJourney()
    {
        var now = DateTimeOffset.Parse(
            "2026-07-02T14:00:00+00:00",
            CultureInfo.InvariantCulture);
        var gate = CreateGateStep(
            1,
            "saturn",
            "Saturn",
            "glenn",
            "Glenn",
            "Gate To Beta Hydri System");
        var station = CreateStationStep(
            1,
            "glenn",
            "Glenn",
            "Friendship 7 Recreation Port");
        var state = Reconcile(
            CreateState(
                now,
                "saturn",
                "Saturn",
                "Friendship 7 Recreation Port"),
            now,
            "saturn",
            "Saturn",
            gate);

        var selecting = NavigationAutoPilotStateMachine.Observe(
            state,
            CreateFrame(
                state,
                now.AddMilliseconds(50),
                "saturn",
                "Saturn",
                gate,
                targetResolution:
                    NavigationTargetResolutionResult.Ready(
                        gate.DepartureTargetName!,
                        "Gate",
                        300,
                        8)));
        state = NavigationAutoPilotStateMachine
            .ApplyTargetSelection(
                selecting.State,
                NavigationAutoPilotTargetSelectionOutcome.Success(
                    300,
                    gate.DepartureTargetName!),
                now.AddMilliseconds(100))
            .State;

        var activate = NavigationAutoPilotStateMachine.Observe(
            state,
            CreateFrame(
                state,
                now.AddMilliseconds(150),
                "saturn",
                "Saturn",
                gate,
                selectedTargetObjectId: 300,
                verbState: NavigationAutoPilotVerbState.Executable,
                warpAvailable: 2));
        state = NavigationAutoPilotStateMachine
            .ApplyVerbActivation(
                activate.State,
                NavigationAutoPilotEffectOutcome.Success(),
                now.AddMilliseconds(200))
            .State;
        AssertPhase(
            state,
            NavigationAutoPilotMachinePhase.WaitingForTransition,
            "gate waits for sector");

        state = NavigationAutoPilotStateMachine.Observe(
            state,
            CreateFrame(
                state,
                now.AddSeconds(5),
                "glenn",
                "Glenn",
                station,
                warpAvailable: 0))
            .State;
        AssertPhase(
            state,
            NavigationAutoPilotMachinePhase.ReconcilingStep,
            "gate transition enters reconcile");

        state = NavigationAutoPilotStateMachine.Observe(
            state,
            CreateFrame(
                state,
                now.AddSeconds(5.1),
                "glenn",
                "Glenn",
                station,
                warpAvailable: 0))
            .State;
        AssertPhase(
            state,
            NavigationAutoPilotMachinePhase.ResolvingTarget,
            "gate transition reconciles station step");
    }

    private static void ValidateSlowGateTransition()
    {
        var now = DateTimeOffset.Parse(
            "2026-07-02T15:00:00+00:00",
            CultureInfo.InvariantCulture);
        var gate = CreateGateStep(
            1,
            "saturn",
            "Saturn",
            "glenn",
            "Glenn",
            "Gate To Beta Hydri System");
        var state = CreateState(
            now,
            "saturn",
            "Saturn",
            "Friendship 7 Recreation Port") with
        {
            Phase =
                NavigationAutoPilotMachinePhase.WaitingForTransition,
            PhaseStartedAt = now,
            Step = CreatePlan(gate),
            TargetObjectId = 400,
            PublicState = NavigationAutoPilotState.WaitingForSector,
        };

        var waiting = NavigationAutoPilotStateMachine.Observe(
            state,
            CreateFrame(
                state,
                now.AddSeconds(20),
                "saturn",
                "Saturn",
                gate,
                loading: 1,
                worldAvailable: false));
        AssertPhase(
            waiting.State,
            NavigationAutoPilotMachinePhase.WaitingForTransition,
            "slow gate transition remains patient");

        var entered = NavigationAutoPilotStateMachine.Observe(
            waiting.State,
            CreateFrame(
                waiting.State,
                now.AddSeconds(25),
                "glenn",
                "Glenn",
                CreateStationStep(
                    1,
                    "glenn",
                    "Glenn",
                    "Friendship 7 Recreation Port")));
        AssertPhase(
            entered.State,
            NavigationAutoPilotMachinePhase.ReconcilingStep,
            "slow gate transition eventually continues");
    }

    private static void ValidateLatePostSectorTargetPublication()
    {
        var now = DateTimeOffset.Parse(
            "2026-07-02T16:00:00+00:00",
            CultureInfo.InvariantCulture);
        var station = CreateStationStep(
            1,
            "glenn",
            "Glenn",
            "Friendship 7 Recreation Port");
        var state = Reconcile(
            CreateState(
                now,
                "glenn",
                "Glenn",
                "Friendship 7 Recreation Port"),
            now,
            "glenn",
            "Glenn",
            station);

        var waiting = NavigationAutoPilotStateMachine.Observe(
            state,
            CreateFrame(
                state,
                now.AddSeconds(10),
                "glenn",
                "Glenn",
                station,
                targetResolution:
                    NavigationTargetResolutionResult.Waiting(
                        "Navigation target not published yet",
                        station.FinalTargetName)));
        AssertPhase(
            waiting.State,
            NavigationAutoPilotMachinePhase.ResolvingTarget,
            "late target publication remains active");

        var selecting = NavigationAutoPilotStateMachine.Observe(
            waiting.State,
            CreateFrame(
                waiting.State,
                now.AddSeconds(12),
                "glenn",
                "Glenn",
                station,
                targetResolution:
                    NavigationTargetResolutionResult.Ready(
                        station.FinalTargetName!,
                        "Station",
                        500,
                        7)));
        AssertEffect(
            selecting,
            NavigationAutoPilotEffectKind.SelectTarget,
            "late target publication continues when exact target appears");
    }

    private static void ValidateRouteLagAfterSectorTransition()
    {
        var now = DateTimeOffset.Parse(
            "2026-07-02T16:30:00+00:00",
            CultureInfo.InvariantCulture);
        var gate = CreateGateStep(
            1,
            "saturn",
            "Saturn",
            "glenn",
            "Glenn",
            "Gate To Beta Hydri System");
        var station = CreateStationStep(
            2,
            "glenn",
            "Glenn",
            "Friendship 7 Recreation Port");
        var state = CreateState(
            now,
            "saturn",
            "Saturn",
            "Friendship 7 Recreation Port") with
        {
            Phase =
                NavigationAutoPilotMachinePhase.WaitingForTransition,
            PhaseStartedAt = now,
            Step = CreatePlan(gate),
            TargetObjectId = 550,
            PublicState = NavigationAutoPilotState.WaitingForSector,
        };

        state = NavigationAutoPilotStateMachine.Observe(
            state,
            CreateFrame(
                state,
                now.AddSeconds(5),
                "glenn",
                "Glenn",
                gate))
            .State;
        AssertPhase(
            state,
            NavigationAutoPilotMachinePhase.ReconcilingStep,
            "sector entry starts route reconciliation");

        state = NavigationAutoPilotStateMachine.Observe(
            state,
            CreateFrame(
                state,
                now.AddSeconds(6),
                "glenn",
                "Glenn",
                gate))
            .State;
        AssertPhase(
            state,
            NavigationAutoPilotMachinePhase.ReconcilingStep,
            "old route step remains a transient wait");

        state = NavigationAutoPilotStateMachine.Observe(
            state,
            CreateFrame(
                state,
                now.AddSeconds(7),
                "glenn",
                "Glenn",
                station))
            .State;
        AssertPhase(
            state,
            NavigationAutoPilotMachinePhase.ResolvingTarget,
            "route reconciliation advances to station");
    }

    private static void ValidatePostGateGlobalRecoveryResidue()
    {
        var now = DateTimeOffset.Parse(
            "2026-07-03T19:10:00+00:00",
            CultureInfo.InvariantCulture);
        var station = CreateStationStep(
            number: 2,
            sectorKey: "saturn",
            sectorName: "Saturn",
            targetName: "Net-7 SOL");
        var state = CreateState(
            now,
            "saturn",
            "Saturn",
            "Net-7 SOL");

        state = NavigationAutoPilotStateMachine.Observe(
                state,
                CreateFrame(
                    state,
                    now,
                    "saturn",
                    "Saturn",
                    station,
                    privateWarpState: 0,
                    globalWarpState: 3,
                    generationSequence: 2))
            .State;

        AssertPhase(
            state,
            NavigationAutoPilotMachinePhase.ResolvingTarget,
            "post-gate global recovery residue");
    }

    private static void ValidateGateActivationDuringWarpRecovery()
    {
        var now = DateTimeOffset.Parse(
            "2026-07-03T19:20:00+00:00",
            CultureInfo.InvariantCulture);
        var gate = CreateGateStep(
            1,
            "saturn",
            "Saturn",
            "glenn",
            "Glenn",
            "Gate To Beta Hydri System");
        var state = CreateState(
            now,
            "saturn",
            "Saturn",
            "Friendship 7 Recreation Port") with
        {
            Phase = NavigationAutoPilotMachinePhase.Warping,
            PhaseStartedAt = now,
            StepStartedAt = now,
            Step = CreatePlan(gate),
            TargetObjectId = 600,
            StepGenerationSequence = 1,
            WarpRequestGenerationSequence = 1,
            WarpRequestNavigationSequence = 1,
            WarpRequestedAt = now,
            WarpRequestAccepted = true,
            WarpReachedActiveState = true,
            PublicState = NavigationAutoPilotState.Warping,
        };

        var activate = NavigationAutoPilotStateMachine.Observe(
            state,
            CreateFrame(
                state,
                now.AddSeconds(1),
                "saturn",
                "Saturn",
                gate,
                selectedTargetObjectId: 600,
                verbState: NavigationAutoPilotVerbState.Executable,
                privateWarpState: 3,
                globalWarpState: 3,
                warpAvailable: 0));

        AssertEffect(
            activate,
            NavigationAutoPilotEffectKind.ActivateVerb,
            "gate activation during Warp recovery");

        var waiting = NavigationAutoPilotStateMachine
            .ApplyVerbActivation(
                activate.State,
                NavigationAutoPilotEffectOutcome.Success(),
                now.AddSeconds(1.1));

        AssertPhase(
            waiting.State,
            NavigationAutoPilotMachinePhase.WaitingForTransition,
            "gate transition begins during Warp recovery");
    }

    private static void ValidateMissingVerbRequiresWarp()
    {
        var now = DateTimeOffset.Parse(
            "2026-07-02T16:40:00+00:00",
            CultureInfo.InvariantCulture);
        var gate = CreateGateStep(
            1,
            "saturn",
            "Saturn",
            "glenn",
            "Glenn",
            "Gate To Beta Hydri System");
        var state = Reconcile(
            CreateState(
                now,
                "saturn",
                "Saturn",
                "Friendship 7 Recreation Port"),
            now,
            "saturn",
            "Saturn",
            gate);
        var selecting = NavigationAutoPilotStateMachine.Observe(
            state,
            CreateFrame(
                state,
                now.AddMilliseconds(50),
                "saturn",
                "Saturn",
                gate,
                targetResolution:
                    NavigationTargetResolutionResult.Ready(
                        gate.DepartureTargetName!,
                        "Gate",
                        560,
                        8)));
        state = NavigationAutoPilotStateMachine
            .ApplyTargetSelection(
                selecting.State,
                NavigationAutoPilotTargetSelectionOutcome.Success(
                    560,
                    gate.DepartureTargetName!),
                now.AddMilliseconds(100))
            .State;

        state = NavigationAutoPilotStateMachine.Observe(
            state,
            CreateFrame(
                state,
                now.AddMilliseconds(300),
                "saturn",
                "Saturn",
                gate,
                selectedTargetObjectId: 560,
                verbState: NavigationAutoPilotVerbState.Missing))
            .State;
        AssertPhase(
            state,
            NavigationAutoPilotMachinePhase.EvaluatingRange,
            "missing gate verb receives a bounded observation grace");

        state = NavigationAutoPilotStateMachine.Observe(
            state,
            CreateFrame(
                state,
                now.AddMilliseconds(1400),
                "saturn",
                "Saturn",
                gate,
                selectedTargetObjectId: 560,
                verbState: NavigationAutoPilotVerbState.Missing))
            .State;
        AssertPhase(
            state,
            NavigationAutoPilotMachinePhase.WaitingForWarp,
            "persistently missing gate verb enters native Warp readiness evaluation");

        var engage = NavigationAutoPilotStateMachine.Observe(
            state,
            CreateFrame(
                state,
                now.AddMilliseconds(1450),
                "saturn",
                "Saturn",
                gate,
                selectedTargetObjectId: 560,
                verbState: NavigationAutoPilotVerbState.Missing,
                warpAvailable: 2));
        AssertEffect(
            engage,
            NavigationAutoPilotEffectKind.EngageWarp,
            "missing gate verb prepares Warp");

        var redirect = NavigationAutoPilotStateMachine.ApplyWarpCommand(
            engage.State,
            NavigationAutoPilotEffectOutcome.VerbReady(),
            now.AddMilliseconds(1500));
        AssertPhase(
            redirect.State,
            NavigationAutoPilotMachinePhase.EvaluatingRange,
            "late gate verb cancels Warp and returns to range evaluation");

        var activate = NavigationAutoPilotStateMachine.Observe(
            redirect.State,
            CreateFrame(
                redirect.State,
                now.AddMilliseconds(1550),
                "saturn",
                "Saturn",
                gate,
                selectedTargetObjectId: 560,
                verbState: NavigationAutoPilotVerbState.Executable,
                warpAvailable: 2));
        AssertEffect(
            activate,
            NavigationAutoPilotEffectKind.ActivateVerb,
            "late gate verb activation");
    }

    private static void ValidateUnexpectedWarpStateBlocksCommand()
    {
        var now = DateTimeOffset.Parse(
            "2026-07-02T12:30:00+00:00",
            CultureInfo.InvariantCulture);
        var station = CreateStationStep(
            number: 2,
            sectorKey: "saturn",
            sectorName: "Saturn",
            targetName: "Net-7 SOL");
        var state = CreateState(
            now,
            "saturn",
            "Saturn",
            "Net-7 SOL");

        state = Reconcile(state, now, "saturn", "Saturn", station) with
        {
        };

        var selecting = NavigationAutoPilotStateMachine.Observe(
            state,
            CreateFrame(
                state,
                now.AddMilliseconds(50),
                "saturn",
                "Saturn",
                station,
                targetResolution:
                    NavigationTargetResolutionResult.Ready(
                        station.FinalTargetName!,
                        "Station",
                        200,
                        9)));

        state = NavigationAutoPilotStateMachine.ApplyTargetSelection(
                selecting.State,
                NavigationAutoPilotTargetSelectionOutcome.Success(
                    200,
                    station.FinalTargetName!),
                now.AddMilliseconds(100))
            .State;

        state = NavigationAutoPilotStateMachine.Observe(
            state,
            CreateFrame(
                state,
                now.AddMilliseconds(150),
                "saturn",
                "Saturn",
                station,
                selectedTargetObjectId: 200,
                verbState: NavigationAutoPilotVerbState.TooFar,
                isWarping: true,
                warpAvailable: 0))
            .State;

        AssertPhase(
            state,
            NavigationAutoPilotMachinePhase.WaitingForWarp,
            "residual warp state enters wait");

        var residual = NavigationAutoPilotStateMachine.Observe(
            state,
            CreateFrame(
                state,
                now.AddMilliseconds(200),
                "saturn",
                "Saturn",
                station,
                selectedTargetObjectId: 200,
                verbState: NavigationAutoPilotVerbState.TooFar,
                isWarping: true,
                warpAvailable: 0));

        AssertPhase(
            residual.State,
            NavigationAutoPilotMachinePhase.Stopped,
            "unexpected native warp state blocks command");

        if (residual.State.StopReason !=
            NavigationAutoPilotStopReason.WarpInterrupted)
        {
            throw new InvalidOperationException(
                "Unexpected native Warp state did not stop Auto Pilot before the command.");
        }
    }

    private static void ValidateUnexpectedWarpAfterCleanBaseline()
    {
        var now = DateTimeOffset.Parse(
            "2026-07-02T12:35:00+00:00",
            CultureInfo.InvariantCulture);
        var station = CreateStationStep(
            number: 1,
            sectorKey: "glenn",
            sectorName: "Glenn",
            targetName: "Friendship 7 Recreation Port");
        var state = CreateState(
            now,
            "glenn",
            "Glenn",
            "Friendship 7 Recreation Port");

        state = Reconcile(state, now, "glenn", "Glenn", station);

        var selecting = NavigationAutoPilotStateMachine.Observe(
            state,
            CreateFrame(
                state,
                now.AddMilliseconds(50),
                "glenn",
                "Glenn",
                station,
                targetResolution:
                    NavigationTargetResolutionResult.Ready(
                        station.FinalTargetName!,
                        "Station",
                        201,
                        7)));

        state = NavigationAutoPilotStateMachine.ApplyTargetSelection(
                selecting.State,
                NavigationAutoPilotTargetSelectionOutcome.Success(
                    201,
                    station.FinalTargetName!),
                now.AddMilliseconds(100))
            .State;

        state = NavigationAutoPilotStateMachine.Observe(
            state,
            CreateFrame(
                state,
                now.AddMilliseconds(150),
                "glenn",
                "Glenn",
                station,
                selectedTargetObjectId: 201,
                verbState: NavigationAutoPilotVerbState.TooFar,
                isWarping: false,
                warpAvailable: 0))
            .State;

        var interrupted = NavigationAutoPilotStateMachine.Observe(
            state,
            CreateFrame(
                state,
                now.AddMilliseconds(200),
                "glenn",
                "Glenn",
                station,
                selectedTargetObjectId: 201,
                verbState: NavigationAutoPilotVerbState.TooFar,
                isWarping: true,
                warpAvailable: 0));

        AssertPhase(
            interrupted.State,
            NavigationAutoPilotMachinePhase.Stopped,
            "unexpected warp after clean baseline");

        if (interrupted.State.StopReason !=
            NavigationAutoPilotStopReason.WarpInterrupted)
        {
            throw new InvalidOperationException(
                "Unexpected warp after a clean baseline did not stop as an interruption.");
        }
    }

    private static void ValidateGravityInterferenceStopsWarp()
    {
        var now = DateTimeOffset.Parse(
            "2026-07-02T12:32:00+00:00",
            CultureInfo.InvariantCulture);
        var station = CreateStationStep(
            number: 1,
            sectorKey: "glenn",
            sectorName: "Glenn",
            targetName: "Friendship 7 Recreation Port");
        var state = CreateState(
            now,
            "glenn",
            "Glenn",
            station.FinalTargetName!) with
        {
            Phase = NavigationAutoPilotMachinePhase.Warping,
            PhaseStartedAt = now,
            StepStartedAt = now,
            Step = CreatePlan(station),
            TargetObjectId = 205,
            StepGenerationSequence = 1,
            WarpRequestNavigationSequence =
                now.ToUnixTimeMilliseconds(),
            WarpRequestGenerationSequence = 1,
            WarpRequestedAt = now,
            WarpRequestAccepted = true,
            WarpReachedActiveState = true,
            PublicState = NavigationAutoPilotState.Warping,
        };

        var stopped = NavigationAutoPilotStateMachine.Observe(
            state,
            CreateFrame(
                state,
                now.AddSeconds(1),
                "glenn",
                "Glenn",
                station,
                selectedTargetObjectId: 205,
                terminalWarpReason: 6,
                terminalWarpReasonAt: now.AddMilliseconds(900)));

        AssertPhase(
            stopped.State,
            NavigationAutoPilotMachinePhase.Stopped,
            "gravity interference stops Warp");

        if (stopped.State.StopReason !=
                NavigationAutoPilotStopReason.WarpInterrupted ||
            !stopped.State.StatusText.Contains(
                "gravity interference",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Gravity interference did not produce the expected Auto Pilot stop reason.");
        }
    }

    private static void ValidateTransientSelectionRetry()
    {
        var now = DateTimeOffset.Parse(
            "2026-07-02T16:50:00+00:00",
            CultureInfo.InvariantCulture);
        var station = CreateStationStep(
            1,
            "glenn",
            "Glenn",
            "Friendship 7 Recreation Port");
        var state = Reconcile(
            CreateState(
                now,
                "glenn",
                "Glenn",
                station.FinalTargetName!),
            now,
            "glenn",
            "Glenn",
            station);
        var selecting = NavigationAutoPilotStateMachine.Observe(
            state,
            CreateFrame(
                state,
                now.AddMilliseconds(50),
                "glenn",
                "Glenn",
                station,
                targetResolution:
                    NavigationTargetResolutionResult.Ready(
                        station.FinalTargetName!,
                        "Station",
                        570,
                        7)));

        var retry = NavigationAutoPilotStateMachine
            .ApplyTargetSelection(
                selecting.State,
                NavigationAutoPilotTargetSelectionOutcome.Failure(
                    "Navigation is rebuilding.",
                    isTransientFailure: true),
                now.AddSeconds(5));
        AssertPhase(
            retry.State,
            NavigationAutoPilotMachinePhase.ResolvingTarget,
            "transient selection failure retries");
    }

    private static void ValidateTargetInterruption()
    {
        var now = DateTimeOffset.Parse(
            "2026-07-02T17:00:00+00:00",
            CultureInfo.InvariantCulture);
        var station = CreateStationStep(
            1,
            "glenn",
            "Glenn",
            "Friendship 7 Recreation Port");
        var state = Reconcile(
            CreateState(
                now,
                "glenn",
                "Glenn",
                "Friendship 7 Recreation Port"),
            now,
            "glenn",
            "Glenn",
            station);
        var selecting = NavigationAutoPilotStateMachine.Observe(
            state,
            CreateFrame(
                state,
                now.AddMilliseconds(50),
                "glenn",
                "Glenn",
                station,
                targetResolution:
                    NavigationTargetResolutionResult.Ready(
                        station.FinalTargetName!,
                        "Station",
                        600,
                        7)));
        state = NavigationAutoPilotStateMachine
            .ApplyTargetSelection(
                selecting.State,
                NavigationAutoPilotTargetSelectionOutcome.Success(
                    600,
                    station.FinalTargetName!),
                now.AddMilliseconds(100))
            .State;

        var interrupted = NavigationAutoPilotStateMachine.Observe(
            state,
            CreateFrame(
                state,
                now.AddMilliseconds(150),
                "glenn",
                "Glenn",
                station,
                selectedTargetObjectId: 601,
                verbState: NavigationAutoPilotVerbState.Unknown));
        AssertPhase(
            interrupted.State,
            NavigationAutoPilotMachinePhase.Stopped,
            "target interruption stops safely");
    }

    private static void ValidateUnexpectedSector()
    {
        var now = DateTimeOffset.Parse(
            "2026-07-02T17:10:00+00:00",
            CultureInfo.InvariantCulture);
        var gate = CreateGateStep(
            1,
            "saturn",
            "Saturn",
            "glenn",
            "Glenn",
            "Gate To Beta Hydri System");
        var state = CreateState(
            now,
            "saturn",
            "Saturn",
            "Friendship 7 Recreation Port") with
        {
            Phase =
                NavigationAutoPilotMachinePhase.WaitingForTransition,
            PhaseStartedAt = now,
            Step = CreatePlan(gate),
            TargetObjectId = 610,
            PublicState = NavigationAutoPilotState.WaitingForSector,
        };

        var lost = NavigationAutoPilotStateMachine.Observe(
            state,
            CreateFrame(
                state,
                now.AddSeconds(5),
                "earth",
                "Earth",
                CreateStationStep(
                    2,
                    "earth",
                    "Earth",
                    "Earth Station")));
        AssertPhase(
            lost.State,
            NavigationAutoPilotMachinePhase.Stopped,
            "unexpected sector stops as lost");
    }

    private static void ValidateRouteChange()
    {
        var now = DateTimeOffset.Parse(
            "2026-07-02T17:20:00+00:00",
            CultureInfo.InvariantCulture);
        var station = CreateStationStep(
            1,
            "glenn",
            "Glenn",
            "Friendship 7 Recreation Port");
        var state = Reconcile(
            CreateState(
                now,
                "glenn",
                "Glenn",
                station.FinalTargetName!),
            now,
            "glenn",
            "Glenn",
            station);

        var changed = NavigationAutoPilotStateMachine.Observe(
            state,
            CreateFrame(
                state,
                now.AddSeconds(1),
                "glenn",
                "Glenn",
                station,
                observedRouteId:
                    Guid.Parse(
                        "22222222-2222-2222-2222-222222222222")));
        AssertPhase(
            changed.State,
            NavigationAutoPilotMachinePhase.Stopped,
            "route replacement stops safely");
    }

    private static void ValidateUserStop()
    {
        var now = DateTimeOffset.Parse(
            "2026-07-02T17:30:00+00:00",
            CultureInfo.InvariantCulture);
        var stopped = NavigationAutoPilotStateMachine.StopByUser(
            CreateState(
                now,
                "glenn",
                "Glenn",
                "Friendship 7 Recreation Port"),
            now.AddSeconds(1));
        AssertPhase(
            stopped.State,
            NavigationAutoPilotMachinePhase.Stopped,
            "user stop is terminal");
    }

    private static void ValidateDestinationArrivalAfterStop()
    {
        var now = DateTimeOffset.Parse(
            "2026-07-02T17:45:00+00:00",
            CultureInfo.InvariantCulture);
        var stopped = NavigationAutoPilotStateMachine.StopByUser(
            CreateState(
                now,
                "glenn",
                "Glenn",
                "Friendship 7 Recreation Port"),
            now.AddSeconds(1));
        var completed = NavigationAutoPilotStateMachine
            .ConfirmDestinationArrival(
                stopped.State,
                now.AddMinutes(2));

        AssertPhase(
            completed.State,
            NavigationAutoPilotMachinePhase.Arrived,
            "manual destination arrival after stop");

        if (completed.State.StopReason !=
                NavigationAutoPilotStopReason.DestinationReached ||
            completed.State.PublicState !=
                NavigationAutoPilotState.Arrived)
        {
            throw new InvalidOperationException(
                "Auto Pilot scenario 'manual destination arrival after stop' did not publish a completed journey.");
        }

        var station = new NavigationDestination
        {
            Kind = NavigationDestinationKind.Target,
            SectorKey = "glenn",
            SectorName = "Glenn",
            SystemName = "Beta Hydri",
            TargetKey = "glenn:station",
            TargetName = "Friendship 7 Recreation Port",
            TargetKind = GalaxyNavigationTargetKind.Station,
        };
        var navigationPoint = new NavigationDestination
        {
            Kind = NavigationDestinationKind.Target,
            SectorKey = "glenn",
            SectorName = "Glenn",
            SystemName = "Beta Hydri",
            TargetKey = "glenn:nav",
            TargetName = "Glenn Nav 1",
            TargetKind = GalaxyNavigationTargetKind.NavigationPoint,
        };
        var sector = new NavigationDestination
        {
            Kind = NavigationDestinationKind.Sector,
            SectorKey = "glenn",
            SectorName = "Glenn",
            SystemName = "Beta Hydri",
        };

        if (NavigationRouteCoordinator.IsDestinationReached(
                station,
                station,
                ClientWorldEnvironment.Space) ||
            !NavigationRouteCoordinator.IsDestinationReached(
                station,
                station,
                ClientWorldEnvironment.Starbase) ||
            !NavigationRouteCoordinator.IsDestinationReached(
                navigationPoint,
                navigationPoint,
                ClientWorldEnvironment.Space) ||
            NavigationRouteCoordinator.IsDestinationReached(
                sector,
                navigationPoint,
                ClientWorldEnvironment.Space))
        {
            throw new InvalidOperationException(
                "Auto Pilot scenario 'manual destination arrival after stop' accepted an unproven destination location.");
        }
    }

    private static void ValidateIncompleteFinalTargetRemainsManual()
    {
        var now = DateTimeOffset.Parse(
            "2026-07-02T18:00:00+00:00",
            CultureInfo.InvariantCulture);
        var target = new NavigationRouteStep
        {
            Number = 1,
            Kind = NavigationRouteStepKind.FinalTarget,
            FromSectorKey = "glenn",
            FromSectorName = "Glenn",
            FromSystemName = "Beta Hydri",
            ToSectorKey = "glenn",
            ToSectorName = "Glenn",
            ToSystemName = "Beta Hydri",
            FinalTargetName = "A scenic nav marker",
        };
        var state = NavigationAutoPilotStateMachine.Observe(
            CreateState(
                now,
                "glenn",
                "Glenn",
                "A scenic nav marker"),
            CreateFrame(
                CreateState(
                    now,
                    "glenn",
                    "Glenn",
                    "A scenic nav marker"),
                now,
                "glenn",
                "Glenn",
                target))
            .State;
        AssertPhase(
            state,
            NavigationAutoPilotMachinePhase.ManualFinalLeg,
            "incomplete final target remains manual");
    }

    private static NavigationAutoPilotMachineState Reconcile(
        NavigationAutoPilotMachineState state,
        DateTimeOffset now,
        string sectorKey,
        string sectorName,
        NavigationRouteStep step)
    {
        return NavigationAutoPilotStateMachine.Observe(
                state,
                CreateFrame(
                    state,
                    now,
                    sectorKey,
                    sectorName,
                    step))
            .State;
    }

    private static NavigationAutoPilotMachineState CreateState(
        DateTimeOffset now,
        string sectorKey,
        string sectorName,
        string destinationName)
    {
        return NavigationAutoPilotMachineState.Create(
            routeId,
            destinationName,
            sectorKey,
            sectorName,
            now);
    }

    private static NavigationAutoPilotMachineFrame CreateFrame(
        NavigationAutoPilotMachineState state,
        DateTimeOffset now,
        string sectorKey,
        string sectorName,
        NavigationRouteStep? step,
        NavigationTargetResolutionResult? targetResolution = null,
        uint? selectedTargetObjectId = null,
        NavigationAutoPilotVerbState verbState =
            NavigationAutoPilotVerbState.Unknown,
        int? warpAvailable = 2,
        bool isWarping = false,
        int? privateWarpState = null,
        int? globalWarpState = null,
        int? terminalWarpReason = null,
        DateTimeOffset? terminalWarpReasonAt = null,
        long generationSequence = 1,
        bool generationChanged = false,
        uint loading = 0,
        bool worldAvailable = true,
        ClientWorldEnvironment environment =
            ClientWorldEnvironment.Space,
        Guid? observedRouteId = null)
    {
        var privateState = privateWarpState ??
            (isWarping ? 2 : 0);
        var globalState = globalWarpState ??
            (isWarping ? 2 : 0);
        var warpIdle = privateState == 0 &&
            globalState is 0 or 3;
        var warpStarting = privateState == 1 ||
            globalState == 1;
        var warpActive = privateState == 2 ||
            globalState == 2;
        var globalRecoveryResidue =
            privateState == 0 &&
            globalState == 3;
        var warpRecovering = privateState == 3 ||
            (globalState == 3 &&
             !globalRecoveryResidue);
        var gateTransitionLocked = privateState == 4;

        return new NavigationAutoPilotMachineFrame
        {
            Now = now,
            ClientAvailable = true,
            ObservationAvailable = true,
            LifecycleState = ClientLifecycleState.InGame,
            LoadingOrTransitionFlag = loading,
            WorldAvailable = worldAvailable,
            Environment = environment,
            ActiveSectorNumber = worldAvailable ? 7u : 0u,
            WorldSectorName = sectorName,
            RouteAvailable = true,
            RouteId = observedRouteId ?? routeId,
            RouteStatus = NavigationRouteStatus.Planned,
            RouteCurrentSectorKey = sectorKey,
            RouteCurrentSectorName = sectorName,
            RouteDestinationName = state.DestinationName,
            NextStep = step,
            NavigationStateAvailable = true,
            NavigationPropertiesAvailable = true,
            NavigationStateSequence = now.ToUnixTimeMilliseconds(),
            NavigationGenerationSequence = generationSequence,
            NavigationGenerationChanged = generationChanged,
            NavigationPhase = gateTransitionLocked
                ? ClientNavigationStatePhase.GateTransitionLocked
                : warpRecovering
                    ? ClientNavigationStatePhase.WarpRecovery
                    : warpActive
                        ? ClientNavigationStatePhase.Warping
                        : warpStarting
                            ? ClientNavigationStatePhase.WarpStarting
                            : ClientNavigationStatePhase.Idle,
            NavigationStateStatus = "Available",
            PrivateWarpState = privateState,
            GlobalWarpState = globalState,
            IsWarpIdle = warpIdle,
            IsWarpStarting = warpStarting,
            IsWarpActive = warpActive,
            IsWarpRecovering = warpRecovering,
            IsGateTransitionLocked = gateTransitionLocked,
            IsInteractionControlReady =
                privateState is 0 or 3 &&
                globalState is 0 or 3,
            IsClientWarpReady =
                warpIdle &&
                warpAvailable == 2 &&
                selectedTargetObjectId.HasValue,
            ClientWarpReadinessReason =
                !isWarping && warpAvailable == 2
                    ? "Ready"
                    : "Warp capability is unavailable",
            PathBuildStateKnown = true,
            LockSpeed = false,
            LockOrient = false,
            LastTerminalWarpReason = terminalWarpReason,
            LastTerminalWarpReasonAt = terminalWarpReasonAt,
            TargetResolution = targetResolution ??
                NavigationTargetResolutionResult.Waiting(
                    "Not published yet"),
            SelectedTargetKnown =
                selectedTargetObjectId.HasValue,
            HasSelectedTarget =
                selectedTargetObjectId.HasValue,
            SelectedTargetObjectId =
                selectedTargetObjectId ?? 0,
            VerbState = verbState,
        };
    }

    private static NavigationRouteStep CreateGateStep(
        int number,
        string fromSectorKey,
        string fromSectorName,
        string toSectorKey,
        string toSectorName,
        string targetName)
    {
        return new NavigationRouteStep
        {
            Number = number,
            Kind = NavigationRouteStepKind.SectorTransition,
            FromSectorKey = fromSectorKey,
            FromSectorName = fromSectorName,
            FromSystemName = "System A",
            ToSectorKey = toSectorKey,
            ToSectorName = toSectorName,
            ToSystemName = "System B",
            DepartureTargetName = targetName,
            DepartureTargetRawObjectType = 8,
        };
    }

    private static NavigationRouteStep CreateStationStep(
        int number,
        string sectorKey,
        string sectorName,
        string targetName)
    {
        return new NavigationRouteStep
        {
            Number = number,
            Kind = NavigationRouteStepKind.FinalTarget,
            FromSectorKey = sectorKey,
            FromSectorName = sectorName,
            FromSystemName = "System",
            ToSectorKey = sectorKey,
            ToSectorName = sectorName,
            ToSystemName = "System",
            FinalTargetName = targetName,
            FinalTargetRawObjectType = 12,
        };
    }

    private static NavigationRouteStep CreateNavigationPointStep(
        int number,
        string sectorKey,
        string sectorName,
        string targetName)
    {
        return new NavigationRouteStep
        {
            Number = number,
            Kind = NavigationRouteStepKind.FinalTarget,
            FromSectorKey = sectorKey,
            FromSectorName = sectorName,
            FromSystemName = "System",
            ToSectorKey = sectorKey,
            ToSectorName = sectorName,
            ToSystemName = "System",
            FinalTargetName = targetName,
            FinalTargetRawObjectType = 37,
        };
    }

    private static NavigationAutoPilotStepPlan CreatePlan(
        NavigationRouteStep step)
    {
        if (!NavigationAutoPilotStepPlan.TryCreate(
                step,
                out var plan))
        {
            throw new InvalidOperationException(
                "Scenario step is not supported by Auto Pilot.");
        }

        return plan;
    }

    private static void AssertPhase(
        NavigationAutoPilotMachineState state,
        NavigationAutoPilotMachinePhase expected,
        string scenario)
    {
        if (state.Phase != expected)
        {
            throw new InvalidOperationException(
                $"Auto Pilot scenario '{scenario}' expected phase {expected}, observed {state.Phase}: {state.StatusText}");
        }
    }

    private static void AssertEffect(
        NavigationAutoPilotMachineTransition transition,
        NavigationAutoPilotEffectKind expected,
        string scenario)
    {
        if (transition.Effect != expected)
        {
            throw new InvalidOperationException(
                $"Auto Pilot scenario '{scenario}' expected effect {expected}, observed {transition.Effect}: {transition.State.StatusText}");
        }
    }
}
#else
internal static class NavigationAutoPilotStateMachineScenarios
{
    public static void Validate()
    {
    }
}
#endif
