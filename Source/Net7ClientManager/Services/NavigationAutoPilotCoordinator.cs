namespace Net7ClientManager.Services;

using Net7ClientManager.Forms;
using Net7ClientManager.Models;
using Net7ClientManager.Navigation;
using Net7ClientManager.Observations;
using Net7ClientManager.Observations.Models;
using Net7ClientManager.Win32;

/// <summary>
/// Executes the narrow, observation-verified Auto Pilot happy path.
/// Decision-making is delegated to a replayable state machine. This class
/// only gathers immutable facts and performs explicitly requested input
/// effects under the shared foreground-input authority.
/// </summary>
internal sealed class NavigationAutoPilotCoordinator : IDisposable
{
    private static readonly TimeSpan pollInterval =
        TimeSpan.FromMilliseconds(50);

    private static readonly TimeSpan targetResolutionPollInterval =
        TimeSpan.FromMilliseconds(250);

    // The native verb state becomes executable slightly before the rendered
    // Gate/Dock/Land button consistently accepts foreground input. Keep this
    // delay compact, then revalidate every observation before clicking.
    private static readonly TimeSpan targetVerbSettleDelay =
        TimeSpan.FromMilliseconds(1000);

    private const int CursorRestoreTolerancePixels = 4;

    private readonly System.Threading.Lock lockObject = new();
    private readonly ClientObservationCoordinator observationCoordinator;
    private readonly NavigationRouteCoordinator routeCoordinator;
    private readonly NavigationTargetSelectionService targetSelectionService;
    private readonly GameCommandCoordinator gameCommandCoordinator;
    private readonly ForegroundInputCoordinator foregroundInputCoordinator;
    private readonly NavigationAutoPilotFleetSupport fleetSupport;
    private readonly Dictionary<int, AutoPilotRun> runs = [];
    private bool disposed;

    public NavigationAutoPilotCoordinator(
        ClientObservationCoordinator observationCoordinator,
        NavigationRouteCoordinator routeCoordinator,
        NavigationTargetSelectionService targetSelectionService,
        GameCommandCoordinator gameCommandCoordinator,
        ForegroundInputCoordinator foregroundInputCoordinator)
    {
        this.observationCoordinator = observationCoordinator;
        this.routeCoordinator = routeCoordinator;
        this.targetSelectionService = targetSelectionService;
        this.gameCommandCoordinator = gameCommandCoordinator;
        this.foregroundInputCoordinator = foregroundInputCoordinator;
        this.fleetSupport = new NavigationAutoPilotFleetSupport(
            observationCoordinator,
            gameCommandCoordinator,
            foregroundInputCoordinator);

#if DEBUG
        NavigationAutoPilotStateMachineScenarios.Validate();
#endif
    }

    public event EventHandler<NavigationAutoPilotStateChangedEventArgs>?
        StateChanged;

    public NavigationAutoPilotSnapshot GetSnapshot(int processId)
    {
        lock (this.lockObject)
        {
            return this.runs.TryGetValue(processId, out var run)
                ? run.Snapshot
                : NavigationAutoPilotSnapshot.Inactive(processId);
        }
    }

    public NavigationAutoPilotCommandResult Start(
        ClientInstance client,
        ClientHostForm hostForm,
        IReadOnlyCollection<ClientInstance> managedClients,
        uint? expectedSectorId = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(hostForm);
        ArgumentNullException.ThrowIfNull(managedClients);

        if (this.disposed)
        {
            return NavigationAutoPilotCommandResult.Failure(
                "Auto Pilot is unavailable because the coordinator is shutting down.");
        }

        if (!IsClientAvailable(client, hostForm))
        {
            return NavigationAutoPilotCommandResult.Failure(
                "The hosted game client is unavailable.");
        }

        if (!this.TryGetStartContext(
                client.ProcessId,
                expectedSectorId,
                out var observation,
                out var route,
                out var error))
        {
            return NavigationAutoPilotCommandResult.Failure(error);
        }

        var routePlan = route.Route!;
        var nextStep = routePlan.NextStep;

        if (route.Status == NavigationRouteStatus.NoRoute)
        {
            return NavigationAutoPilotCommandResult.Failure(
                string.IsNullOrWhiteSpace(route.StatusText)
                    ? "The planned route is blocked."
                    : route.StatusText);
        }

        if (route.Status ==
                NavigationRouteStatus.DestinationReached ||
            nextStep == null)
        {
            return NavigationAutoPilotCommandResult.Failure(
                "The planned route is already at its destination.");
        }

        if (!SupportsStep(nextStep))
        {
            return NavigationAutoPilotCommandResult.Failure(
                "Auto Pilot cannot safely operate the remaining final route target; that step remains manual.");
        }

        if (TryReadWarpState(
                observation,
                out _,
                out var isWarping) &&
            isWarping)
        {
            return NavigationAutoPilotCommandResult.Failure(
                "Auto Pilot cannot start while the ship is already in warp.");
        }

        if (!this.fleetSupport.TryCreateContext(
                client,
                hostForm,
                managedClients,
                out var fleet,
                out var fleetError))
        {
            return NavigationAutoPilotCommandResult.Failure(fleetError);
        }

        AutoPilotRun run;
        NavigationAutoPilotSnapshot startingSnapshot;

        lock (this.lockObject)
        {
            if (this.disposed)
            {
                fleet.Dispose();
                return NavigationAutoPilotCommandResult.Failure(
                    "Auto Pilot is unavailable because the coordinator is shutting down.");
            }

            var conflictingRun = this.runs.Values.FirstOrDefault(
                existing =>
                    existing.Snapshot.IsActive &&
                    fleet.Participants.Any(participant =>
                        existing.Fleet.ContainsProcess(
                            participant.Client.ProcessId)));

            if (conflictingRun != null)
            {
                fleet.Dispose();
                return NavigationAutoPilotCommandResult.Failure(
                    "One of the managed group clients is already participating in Auto Pilot.");
            }

            if (this.runs.TryGetValue(
                    client.ProcessId,
                    out var existing))
            {
                DisposeRunWhenComplete(existing);
            }

            var now = DateTimeOffset.UtcNow;
            var machineState =
                NavigationAutoPilotMachineState.Create(
                    routePlan.RouteId,
                    routePlan.Destination.DisplayName,
                    route.CurrentSector!.Key,
                    route.CurrentSector.Name,
                    now);

            startingSnapshot = BuildSnapshot(
                client.ProcessId,
                machineState,
                isActive: true,
                now);

            run = new AutoPilotRun(
                client,
                hostForm,
                fleet,
                machineState,
                startingSnapshot);

            this.runs[client.ProcessId] = run;
        }

        var startSignal = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        run.Task = Task.Run(
            async () =>
            {
                await startSignal.Task.ConfigureAwait(false);
                await this.RunAsync(run).ConfigureAwait(false);
            },
            CancellationToken.None);

        try
        {
            this.RaiseStateChanged(startingSnapshot);
        }
        finally
        {
            startSignal.TrySetResult(result: true);
        }

        return NavigationAutoPilotCommandResult.Success(
            startingSnapshot);
    }

    public NavigationAutoPilotCommandResult Stop(int processId)
    {
        AutoPilotRun? run;
        NavigationAutoPilotSnapshot snapshot;

        lock (this.lockObject)
        {
            if (!this.runs.TryGetValue(processId, out run) ||
                !run.Snapshot.IsActive)
            {
                return NavigationAutoPilotCommandResult.Failure(
                    "Auto Pilot is not active for this client.");
            }

            run.RequestedStopReason =
                NavigationAutoPilotStopReason.UserStopped;
            run.RequestedStopStatus =
                "Auto Pilot was stopped by the user.";

            snapshot = run.Snapshot with
            {
                StatusText = "Stopping Auto Pilot.",
                UpdatedAt = DateTimeOffset.UtcNow,
            };

            run.Snapshot = snapshot;
        }
        this.RaiseStateChanged(snapshot);
        run.Cancellation.Cancel();

        return NavigationAutoPilotCommandResult.Success(snapshot);
    }

    public void ReconcileDestinationArrival(int processId)
    {
        AutoPilotRun run;
        Guid routeId;

        lock (this.lockObject)
        {
            if (!this.runs.TryGetValue(
                    processId,
                    out var currentRun) ||
                currentRun.Snapshot.IsActive ||
                (currentRun.MachineState.Phase !=
                     NavigationAutoPilotMachinePhase.Stopped &&
                 currentRun.MachineState.Phase !=
                     NavigationAutoPilotMachinePhase.ManualFinalLeg))
            {
                return;
            }

            run = currentRun;
            routeId = run.MachineState.RouteId;
        }

        if (!this.routeCoordinator.IsCurrentDestinationReached(
                processId,
                routeId))
        {
            return;
        }

        NavigationAutoPilotSnapshot snapshot;

        lock (this.lockObject)
        {
            if (!this.runs.TryGetValue(processId, out var current) ||
                !ReferenceEquals(current, run) ||
                current.Snapshot.IsActive ||
                current.MachineState.RouteId != routeId ||
                (current.MachineState.Phase !=
                     NavigationAutoPilotMachinePhase.Stopped &&
                 current.MachineState.Phase !=
                     NavigationAutoPilotMachinePhase.ManualFinalLeg))
            {
                return;
            }

            var now = DateTimeOffset.UtcNow;
            var transition = NavigationAutoPilotStateMachine
                .ConfirmDestinationArrival(
                    current.MachineState,
                    now);

            current.MachineState = transition.State;
            snapshot = BuildSnapshot(
                processId,
                current.MachineState,
                isActive: false,
                now);
            current.Snapshot = snapshot;
        }

        this.RaiseStateChanged(snapshot);
    }

    public void ForgetProcess(int processId)
    {
        AutoPilotRun? run;

        lock (this.lockObject)
        {
            run = this.runs.Values.FirstOrDefault(
                candidate => candidate.Fleet.ContainsProcess(processId));

            if (run == null)
            {
                return;
            }

            this.runs.Remove(run.Client.ProcessId);
            run.RequestedStopReason =
                NavigationAutoPilotStopReason.ClientUnavailable;
            run.RequestedStopStatus = processId == run.Client.ProcessId
                ? "Auto Pilot stopped because the leader game client became unavailable."
                : "Auto Pilot stopped because a managed follower game client became unavailable.";
        }

        if (run.Snapshot.IsActive)
        {
            run.Cancellation.Cancel();
            DisposeRunWhenComplete(run);
        }
        else
        {
            run.Dispose();
        }
    }

    public void Dispose()
    {
        AutoPilotRun[] currentRuns;

        lock (this.lockObject)
        {
            if (this.disposed)
            {
                return;
            }

            this.disposed = true;
            currentRuns = [.. this.runs.Values];
            this.runs.Clear();

            foreach (var run in currentRuns)
            {
                run.RequestedStopReason =
                    NavigationAutoPilotStopReason.ClientUnavailable;
                run.RequestedStopStatus =
                    "Auto Pilot stopped because Client Manager is shutting down.";
            }
        }

        foreach (var run in currentRuns.Where(
                     run => run.Snapshot.IsActive))
        {
            run.Cancellation.Cancel();
        }

        try
        {
            Task.WhenAll(
                    currentRuns
                        .Select(run => run.Task)
                        .OfType<Task>())
                .GetAwaiter()
                .GetResult();
        }
        catch (OperationCanceledException)
        {
        }
        catch (AggregateException exception)
            when (exception.InnerExceptions.All(
                inner => inner is OperationCanceledException))
        {
        }

        foreach (var run in currentRuns)
        {
            run.Dispose();
        }
    }

    internal static bool SupportsStep(NavigationRouteStep? step)
    {
        return step != null &&
            NavigationAutoPilotStepPlan.TryCreate(
                step,
                out _);
    }

    private async Task RunAsync(AutoPilotRun run)
    {
        try
        {
            while (true)
            {
                run.Cancellation.Token.ThrowIfCancellationRequested();

                if (run.Fleet.HasFollowers &&
                    run.MachineState.Phase ==
                        NavigationAutoPilotMachinePhase.ReconcilingStep)
                {
                    var fleetReady = await this.fleetSupport
                        .EnsureReadyForLegAsync(
                            run.Fleet,
                            status => this.PublishFleetStatus(run, status),
                            run.Cancellation.Token)
                        .ConfigureAwait(false);

                    if (!fleetReady.Succeeded)
                    {
                        this.ApplyFleetFailure(run, fleetReady);
                        return;
                    }
                }

                var frame = this.CaptureFrame(run);
                var transition = NavigationAutoPilotStateMachine
                    .Observe(run.MachineState, frame);

                if (transition.State.Phase ==
                        NavigationAutoPilotMachinePhase.Arrived &&
                    run.Fleet.HasFollowers)
                {
                    var fleetArrived = await this.fleetSupport
                        .WaitForFollowersAtDestinationAsync(
                            run.Fleet,
                            transition.State.Step,
                            transition.State.TargetObjectId,
                            frame.ActiveSectorNumber,
                            status => this.PublishFleetStatus(run, status),
                            run.Cancellation.Token)
                        .ConfigureAwait(false);

                    if (!fleetArrived.Succeeded)
                    {
                        transition = CreateFleetFailureTransition(
                            run.MachineState,
                            fleetArrived);
                    }
                }

                this.ApplyTransition(run, transition, frame);

                if (transition.State.IsTerminal)
                {
                    return;
                }

                switch (transition.Effect)
                {
                    case NavigationAutoPilotEffectKind.SelectTarget:
                    {
                        var outcome = await this
                            .ExecuteTargetSelectionAsync(
                                run,
                                frame.ActiveSectorNumber)
                            .ConfigureAwait(false);

                        var completed =
                            NavigationAutoPilotStateMachine
                                .ApplyTargetSelection(
                                    run.MachineState,
                                    outcome,
                                    DateTimeOffset.UtcNow);

                        this.ApplyTransition(
                            run,
                            completed,
                            this.CaptureFrame(run));

                        if (completed.State.IsTerminal)
                        {
                            return;
                        }

                        break;
                    }

                    case NavigationAutoPilotEffectKind.EngageWarp:
                    {
                        var outcome = await this.ExecuteWarpCommandAsync(run)
                            .ConfigureAwait(false);

                        var completed =
                            NavigationAutoPilotStateMachine
                                .ApplyWarpCommand(
                                    run.MachineState,
                                    outcome,
                                    DateTimeOffset.UtcNow);

                        this.ApplyTransition(
                            run,
                            completed,
                            this.CaptureFrame(run));

                        if (completed.State.IsTerminal)
                        {
                            return;
                        }

                        break;
                    }

                    case NavigationAutoPilotEffectKind.ActivateVerb:
                    {
                        NavigationAutoPilotEffectOutcome outcome;
                        var step = run.MachineState.Step;
                        var targetObjectId =
                            run.MachineState.TargetObjectId;

                        if (run.Fleet.HasFollowers &&
                            step != null &&
                            targetObjectId is > 0)
                        {
                            outcome = await this.fleetSupport
                                .ActivateFollowersAsync(
                                    run.Fleet,
                                    step,
                                    targetObjectId.Value,
                                    frame.ActiveSectorNumber,
                                    status => this.PublishFleetStatus(
                                        run,
                                        status),
                                    run.Cancellation.Token)
                                .ConfigureAwait(false);
                        }
                        else
                        {
                            outcome = NavigationAutoPilotEffectOutcome
                                .Success();
                        }

                        if (outcome.Succeeded)
                        {
                            outcome = await this
                                .ExecuteTargetVerbAsync(run)
                                .ConfigureAwait(false);
                        }

                        var completed =
                            NavigationAutoPilotStateMachine
                                .ApplyVerbActivation(
                                    run.MachineState,
                                    outcome,
                                    DateTimeOffset.UtcNow);

                        this.ApplyTransition(
                            run,
                            completed,
                            this.CaptureFrame(run));

                        if (completed.State.IsTerminal)
                        {
                            return;
                        }

                        break;
                    }

                    case NavigationAutoPilotEffectKind.None:
                        await Task.Delay(
                                pollInterval,
                                run.Cancellation.Token)
                            .ConfigureAwait(false);
                        break;
                }
            }
        }
        catch (OperationCanceledException)
            when (run.Cancellation.IsCancellationRequested)
        {
            NavigationAutoPilotStopReason reason;
            string status;

            lock (this.lockObject)
            {
                reason = run.RequestedStopReason ==
                    NavigationAutoPilotStopReason.None
                        ? NavigationAutoPilotStopReason.UserStopped
                        : run.RequestedStopReason;

                status = string.IsNullOrWhiteSpace(
                    run.RequestedStopStatus)
                        ? "Auto Pilot was stopped."
                        : run.RequestedStopStatus;
            }

            var stopped = reason ==
                NavigationAutoPilotStopReason.UserStopped
                ? NavigationAutoPilotStateMachine.StopByUser(
                    run.MachineState,
                    DateTimeOffset.UtcNow)
                : new NavigationAutoPilotMachineTransition(
                    run.MachineState with
                    {
                        Phase = NavigationAutoPilotMachinePhase.Stopped,
                        PhaseStartedAt = DateTimeOffset.UtcNow,
                        PublicState = NavigationAutoPilotState.Stopped,
                        StopReason = reason,
                        StatusText = status,
                    });

            this.ApplyTransition(
                run,
                stopped,
                this.CaptureFrame(run));
        }
        catch (Exception)
        {

            var stopped = new NavigationAutoPilotMachineTransition(
                run.MachineState with
                {
                    Phase = NavigationAutoPilotMachinePhase.Stopped,
                    PhaseStartedAt = DateTimeOffset.UtcNow,
                    PublicState = NavigationAutoPilotState.Stopped,
                    StopReason =
                        NavigationAutoPilotStopReason.InternalError,
                    StatusText =
                        "Auto Pilot stopped after an internal error.",
                });

            this.ApplyTransition(
                run,
                stopped,
                this.CaptureFrame(run));
        }
        finally
        {
            run.Fleet.Dispose();
        }
    }

    private NavigationAutoPilotMachineFrame CaptureFrame(
        AutoPilotRun run)
    {
        var now = DateTimeOffset.UtcNow;
        var clientAvailable = IsClientAvailable(
            run.Client,
            run.HostForm);

        var hasObservation =
            this.observationCoordinator.TryGetSnapshot(
                run.Client.ProcessId,
                out var observation) &&
            observation.IsAvailable;

        var route = this.routeCoordinator.GetSnapshot(
            run.Client.ProcessId);

        var hasNavigationState =
            this.observationCoordinator
                .TryGetNavigationStateObservation(
                    run.Client.ProcessId,
                    out var navigationState) &&
            navigationState.Sequence > 0;

        var currentEnergy = hasNavigationState &&
                            navigationState.CurrentEnergyPower.HasValue
            ? (int)MathF.Round(
                navigationState.CurrentEnergyPower.Value)
            : hasObservation
                ? ReadCurrentEnergy(observation!)
                : null;

        var targetResolution =
            NavigationTargetResolutionResult.Waiting(
                "Target resolution is not active in this phase.");

        if (run.MachineState.Phase ==
            NavigationAutoPilotMachinePhase.ResolvingTarget)
        {
            var targetName =
                run.MachineState.Step?.TargetName ?? "";

            if (!string.Equals(
                    run.TargetResolutionTargetName,
                    targetName,
                    StringComparison.Ordinal) ||
                now >= run.NextTargetResolutionAt)
            {
                run.TargetResolutionTargetName = targetName;
                run.LastTargetResolution =
                    this.targetSelectionService.ResolveNextTarget(
                        run.Client.ProcessId);
                run.NextTargetResolutionAt =
                    now + targetResolutionPollInterval;
            }

            targetResolution = run.LastTargetResolution;
        }

        var selectedTargetKnown = false;
        var hasSelectedTarget = false;
        var selectedTargetObjectId = 0u;
        var verbState = NavigationAutoPilotVerbState.Unknown;
        var targetSectorNumber = hasNavigationState
            ? navigationState.ActiveSectorNumber
            : hasObservation
                ? observation!.World.ActiveSectorNumber
                : 0;

        if (run.MachineState.TargetObjectId is > 0 &&
            hasNavigationState &&
            navigationState.Generation.HasClientObject &&
            navigationState.Generation.HasAuxData)
        {
            selectedTargetKnown =
                navigationState.SelectedTargetKnown &&
                navigationState.PathBuildStateKnown &&
                !navigationState.PathBuildBusy;
            hasSelectedTarget = navigationState.HasSelectedTarget;
            selectedTargetObjectId =
                navigationState.SelectedTargetObjectId;
        }
        else if (hasObservation &&
                 run.MachineState.TargetObjectId is > 0 &&
                 observation!.World.IsAvailable &&
                 observation.World.ActiveSectorNumber != 0)
        {
            selectedTargetKnown =
                this.observationCoordinator
                    .TryReadCurrentTargetObjectId(
                        run.Client.ProcessId,
                        observation.World.ActiveSectorNumber,
                        out hasSelectedTarget,
                        out selectedTargetObjectId,
                        out _);
        }

        if (selectedTargetKnown &&
            hasSelectedTarget &&
            targetSectorNumber != 0 &&
            selectedTargetObjectId ==
                run.MachineState.TargetObjectId.Value &&
            run.MachineState.Step is
            {
                RequiresInteraction: true,
            })
        {
            verbState = this.ReadVerbState(
                run.Client.ProcessId,
                targetSectorNumber,
                run.MachineState.TargetObjectId.Value,
                run.MachineState.Step.Verb);
        }

        return new NavigationAutoPilotMachineFrame
        {
            Now = now,
            ClientAvailable = clientAvailable,
            ObservationAvailable = hasObservation || hasNavigationState,
            LifecycleState = hasNavigationState
                ? navigationState.LifecycleState
                : hasObservation
                    ? observation!.LifecycleState
                    : default,
            LoadingOrTransitionFlag = hasNavigationState
                ? navigationState.IsLoading
                    ? 1u
                    : 0u
                : hasObservation
                    ? observation!.LoadingOrTransitionFlag
                    : 0,
            WorldAvailable = hasNavigationState
                ? navigationState.IsWorldPresent
                : hasObservation &&
                  observation!.World.IsAvailable,
            Environment = hasNavigationState
                ? navigationState.Environment
                : hasObservation
                    ? observation!.World.Environment
                    : default,
            ActiveSectorNumber = hasNavigationState
                ? navigationState.ActiveSectorNumber
                : hasObservation
                    ? observation!.World.ActiveSectorNumber
                    : 0,
            WorldSectorName = hasNavigationState
                ? navigationState.SectorName
                : hasObservation
                    ? observation!.World.CurrentSectorName
                    : "",
            CurrentStarbaseName = hasObservation
                ? observation!.World.CurrentStarbaseName
                : "",
            RouteAvailable = route.IsAvailable,
            RouteId = route.Route?.RouteId,
            RouteStatus = route.Status,
            RouteStatusText = route.StatusText,
            RouteCurrentSectorKey = route.CurrentSector?.Key,
            RouteCurrentSectorName = route.CurrentSector?.Name,
            RouteDestinationName =
                route.Route?.Destination.DisplayName ??
                run.MachineState.DestinationName,
            NextStep = route.Route?.NextStep,
            NavigationStateAvailable = hasNavigationState &&
                navigationState.IsAvailable,
            NavigationPropertiesAvailable = hasNavigationState &&
                navigationState.RequiredPropertiesAvailable,
            NavigationStateSequence = hasNavigationState
                ? navigationState.Sequence
                : 0,
            NavigationGenerationSequence = hasNavigationState
                ? navigationState.GenerationSequence
                : 0,
            NavigationGenerationChanged = hasNavigationState &&
                navigationState.GenerationChanged,
            NavigationPhase = hasNavigationState
                ? navigationState.Phase
                : default,
            NavigationStateStatus = hasNavigationState
                ? navigationState.Status
                : "Dedicated navigation observation is unavailable",
            PrivateWarpState = hasNavigationState &&
                               navigationState.PrivateWarpState.IsAvailable
                ? navigationState.PrivateWarpState.Value
                : null,
            GlobalWarpState = hasNavigationState &&
                              navigationState.GlobalWarpState.IsAvailable
                ? navigationState.GlobalWarpState.Value
                : null,
            IsWarpIdle = hasNavigationState &&
                navigationState.IsWarpIdle,
            IsWarpStarting = hasNavigationState &&
                navigationState.IsWarpStarting,
            IsWarpActive = hasNavigationState &&
                navigationState.IsWarpActive,
            IsWarpRecovering = hasNavigationState &&
                navigationState.IsWarpRecovering,
            IsGateTransitionLocked = hasNavigationState &&
                navigationState.IsGateTransitionLocked,
            IsInteractionControlReady = hasNavigationState &&
                navigationState.IsInteractionControlReady,
            IsClientWarpReady = hasNavigationState &&
                navigationState.IsClientWarpReady,
            ClientWarpReadinessReason = hasNavigationState
                ? navigationState.GetClientWarpReadinessReason()
                : "Dedicated navigation observation is unavailable",
            PathBuildStateKnown = hasNavigationState &&
                navigationState.PathBuildStateKnown,
            PathBuildBusy = hasNavigationState &&
                navigationState.PathBuildBusy,
            LockSpeed = hasNavigationState &&
                        navigationState.LockSpeed.IsAvailable
                ? navigationState.LockSpeed.Value
                : null,
            LockOrient = hasNavigationState &&
                         navigationState.LockOrient.IsAvailable
                ? navigationState.LockOrient.Value
                : null,
            LastTerminalWarpReason = hasNavigationState
                ? navigationState.LastTerminalWarpReason
                : null,
            LastTerminalWarpReasonAt = hasNavigationState
                ? navigationState.LastTerminalWarpReasonAt
                : null,
            TargetDistance = hasNavigationState
                ? navigationState.TargetDistance
                : null,
            CurrentEnergy = currentEnergy,
            PublishedNavigationAvailable = hasObservation &&
                observation!.Navigation.IsAvailable,
            PublishedNavigationSectorNumber = hasObservation
                ? observation!.Navigation.ActiveSectorNumber
                : 0,
            PublishedNavigationTargetCount = hasObservation
                ? observation!.Navigation.Targets.Count(target =>
                    target.IsAvailable &&
                    target.ObjectId != 0)
                : 0,
            PublishedNavigationStatus = hasObservation
                ? observation!.Navigation.Status
                : "",
            TargetResolution = targetResolution,
            SelectedTargetKnown = selectedTargetKnown,
            HasSelectedTarget = hasSelectedTarget,
            SelectedTargetObjectId = selectedTargetObjectId,
            VerbState = verbState,
            DockingRequestObserved = hasObservation &&
                run.MachineState.TargetObjectId is > 0 &&
                (observation!.DockingTargetObjectId ==
                     run.MachineState.TargetObjectId.Value ||
                 observation.PendingLandOrDockTargetObjectId ==
                     run.MachineState.TargetObjectId.Value),
        };
    }

    private NavigationAutoPilotVerbState ReadVerbState(
        int processId,
        uint expectedSectorId,
        uint targetObjectId,
        ClientTargetVerb targetVerb)
    {
        if (!this.observationCoordinator
                .TryReadCurrentTargetInteraction(
                    processId,
                    expectedSectorId,
                    out var interaction,
                    out _) ||
            !interaction.IsAvailable ||
            !interaction.IsActive ||
            !interaction.HasTarget ||
            interaction.TargetObjectId != targetObjectId)
        {
            return NavigationAutoPilotVerbState.Unknown;
        }

        var action = interaction.FindAction(targetVerb);

        if (action == null)
        {
            return NavigationAutoPilotVerbState.Missing;
        }

        if (action.IsExecutable)
        {
            return NavigationAutoPilotVerbState.Executable;
        }

        return action.KnownUnavailableReason ==
            ClientTargetVerbUnavailableReason.TooFar
                ? NavigationAutoPilotVerbState.TooFar
                : NavigationAutoPilotVerbState.Unavailable;
    }

    private async Task<NavigationAutoPilotTargetSelectionOutcome>
        ExecuteTargetSelectionAsync(
            AutoPilotRun run,
            uint expectedSectorId)
    {
        var result = await this.targetSelectionService
            .SelectNextTargetAsync(
                run.Client,
                run.HostForm,
                expectedSectorId == 0
                    ? null
                    : expectedSectorId,
                run.Cancellation.Token)
            .ConfigureAwait(false);

        return result.Succeeded && result.ObjectId != 0
            ? NavigationAutoPilotTargetSelectionOutcome.Success(
                result.ObjectId,
                result.TargetName)
            : NavigationAutoPilotTargetSelectionOutcome.Failure(
                result.Error,
                result.IsTransientFailure);
    }

    private async Task<NavigationAutoPilotEffectOutcome>
        ExecuteWarpCommandAsync(AutoPilotRun run)
    {
        var state = run.MachineState;

        if (state.Step == null ||
            state.TargetObjectId is not > 0)
        {
            return NavigationAutoPilotEffectOutcome.Failure(
                NavigationAutoPilotStopReason.InternalError,
                "Auto Pilot could not prepare the warp command because route-target state was incomplete.");
        }

        var sessionResult = await this.gameCommandCoordinator
            .BeginSessionAsync(
                run.Client,
                [GameCommand.Warp],
                run.Cancellation.Token)
            .ConfigureAwait(false);

        var session = sessionResult.Session;

        if (session == null)
        {
            return NavigationAutoPilotEffectOutcome.Failure(
                NavigationAutoPilotStopReason.WarpUnavailable,
                $"Auto Pilot could not prepare the warp command: {sessionResult.Error}");
        }

        using (session)
        {
            if (!this.TryGetEffectContext(
                    run,
                    out var observation,
                    out var error))
            {
                return NavigationAutoPilotEffectOutcome.Failure(
                    NavigationAutoPilotStopReason.ObservationUnavailable,
                    error);
            }

            if (!this.TryValidateCurrentTarget(
                    run.Client.ProcessId,
                    observation,
                    state.TargetObjectId.Value))
            {
                return NavigationAutoPilotEffectOutcome.Failure(
                    NavigationAutoPilotStopReason.WarpInterrupted,
                    "Auto Pilot was interrupted because the selected route target changed before the warp command was sent.");
            }

            if (!this.observationCoordinator
                    .TryGetNavigationStateObservation(
                        run.Client.ProcessId,
                        out var navigation) ||
                !navigation.IsAvailable ||
                !navigation.RequiredPropertiesAvailable)
            {
                return NavigationAutoPilotEffectOutcome.Failure(
                    NavigationAutoPilotStopReason.ObservationUnavailable,
                    "Auto Pilot stopped because native navigation state was unavailable when the Warp command lease was acquired.");
            }

            if (navigation.GenerationSequence !=
                state.WarpRequestGenerationSequence)
            {
                return NavigationAutoPilotEffectOutcome.Failure(
                    NavigationAutoPilotStopReason.ObservationUnavailable,
                    "Auto Pilot stopped because the local world generation changed before Warp could be requested.");
            }

            if (!navigation.SelectedTargetKnown ||
                !navigation.HasSelectedTarget ||
                navigation.SelectedTargetObjectId !=
                    state.TargetObjectId.Value ||
                !navigation.PathBuildStateKnown ||
                navigation.PathBuildBusy)
            {
                return NavigationAutoPilotEffectOutcome.Failure(
                    NavigationAutoPilotStopReason.WarpInterrupted,
                    "Auto Pilot was interrupted because the selected route target or client path changed before Warp was requested.");
            }

            if (!navigation.IsClientWarpReady)
            {
                return NavigationAutoPilotEffectOutcome.Failure(
                    NavigationAutoPilotStopReason.WarpUnavailable,
                    $"Auto Pilot stopped because native Warp readiness changed before the command was sent: {navigation.GetClientWarpReadinessReason()}.");
            }

            var command = await session.ExecuteAsync(
                    GameCommand.Warp,
                    run.Cancellation.Token)
                .ConfigureAwait(false);

            return command.Succeeded
                ? NavigationAutoPilotEffectOutcome.Success()
                : NavigationAutoPilotEffectOutcome.Failure(
                    NavigationAutoPilotStopReason.WarpUnavailable,
                    $"Auto Pilot could not engage warp: {command.Error}");
        }
    }

    private async Task<NavigationAutoPilotEffectOutcome>
        ExecuteTargetVerbAsync(AutoPilotRun run)
    {
        var state = run.MachineState;
        var step = state.Step;

        if (step == null ||
            !step.RequiresInteraction ||
            state.TargetObjectId is not > 0)
        {
            return NavigationAutoPilotEffectOutcome.Failure(
                NavigationAutoPilotStopReason.InternalError,
                "Auto Pilot could not prepare the target action because route-target state was incomplete.");
        }

        if (!NativeMethods.TryGetClientSize(
                run.Client.GameWindowHandle,
                out var clientSize) ||
            !ClientGameUiCoordinates.TryGetPrimaryTargetVerb(
                clientSize,
                out var verbPoint))
        {
            return NavigationAutoPilotEffectOutcome.Failure(
                step.ActivationFailedReason,
                $"Auto Pilot could not resolve the {step.VerbName} button position in the hosted game viewport.");
        }

        await Task.Delay(
                targetVerbSettleDelay,
                run.Cancellation.Token)
            .ConfigureAwait(false);

        using var foregroundLease =
            await this.foregroundInputCoordinator
                .AcquireAsync(run.Cancellation.Token)
                .ConfigureAwait(false);

        var hasOriginalCursor =
            NativeMethods.TryGetCursorScreenPosition(
                out var originalCursorPosition);

        var commandedCursorPosition = Point.Empty;
        var ownsCursorPosition = false;

        await run.HostForm
            .SetAddonOverlayInputSuppressedAsync(suppressed: true)
            .ConfigureAwait(false);

        try
        {
            if (!this.TryGetEffectContext(
                    run,
                    out var observation,
                    out var error))
            {
                return NavigationAutoPilotEffectOutcome.Failure(
                    NavigationAutoPilotStopReason.ObservationUnavailable,
                    error);
            }

            if (!this.TryValidateCurrentTarget(
                    run.Client.ProcessId,
                    observation,
                    state.TargetObjectId.Value))
            {
                return NavigationAutoPilotEffectOutcome.Failure(
                    NavigationAutoPilotStopReason.WarpInterrupted,
                    $"Auto Pilot was interrupted because the selected route target changed before {step.VerbName} was activated.");
            }

            if (!this.observationCoordinator
                    .TryGetNavigationStateObservation(
                        run.Client.ProcessId,
                        out var navigation) ||
                !navigation.IsAvailable ||
                !navigation.RequiredPropertiesAvailable ||
                navigation.GenerationSequence !=
                    state.StepGenerationSequence ||
                !navigation.IsInteractionControlReady ||
                !navigation.SelectedTargetKnown ||
                !navigation.HasSelectedTarget ||
                navigation.SelectedTargetObjectId !=
                    state.TargetObjectId.Value ||
                !navigation.PathBuildStateKnown ||
                navigation.PathBuildBusy)
            {
                return NavigationAutoPilotEffectOutcome.Failure(
                    step.ActivationFailedReason,
                    $"Auto Pilot stopped because current-generation navigation state no longer permitted {step.VerbName}.");
            }

            if (this.ReadVerbState(
                    run.Client.ProcessId,
                    observation.World.ActiveSectorNumber,
                    state.TargetObjectId.Value,
                    step.Verb) !=
                NavigationAutoPilotVerbState.Executable)
            {
                return NavigationAutoPilotEffectOutcome.Failure(
                    step.ActivationFailedReason,
                    $"Auto Pilot stopped because {step.VerbName} was no longer executable at activation time.");
            }

            if (!NativeMethods.TryConvertClientPointToScreen(
                    run.Client.GameWindowHandle,
                    verbPoint,
                    out commandedCursorPosition))
            {
                return NavigationAutoPilotEffectOutcome.Failure(
                    step.ActivationFailedReason,
                    $"Auto Pilot could not map the {step.VerbName} button to the screen.");
            }

            NativeMethods.FocusWindow(
                run.Client.GameWindowHandle);

            await Task.Delay(
                    TimeSpan.FromMilliseconds(50),
                    run.Cancellation.Token)
                .ConfigureAwait(false);

            if (!NativeMethods.MoveCursorToScreenPoint(
                    commandedCursorPosition))
            {
                return NavigationAutoPilotEffectOutcome.Failure(
                    step.ActivationFailedReason,
                    $"Auto Pilot could not move to the verified {step.VerbName} button.");
            }

            ownsCursorPosition = true;

            await Task.Delay(
                    TimeSpan.FromMilliseconds(100),
                    run.Cancellation.Token)
                .ConfigureAwait(false);

            if (!this.TryGetEffectContext(
                    run,
                    out observation,
                    out error) ||
                !this.observationCoordinator
                    .TryGetNavigationStateObservation(
                        run.Client.ProcessId,
                        out navigation) ||
                !navigation.IsAvailable ||
                !navigation.RequiredPropertiesAvailable ||
                navigation.GenerationSequence !=
                    state.StepGenerationSequence ||
                !navigation.IsInteractionControlReady ||
                !navigation.SelectedTargetKnown ||
                !navigation.HasSelectedTarget ||
                navigation.SelectedTargetObjectId !=
                    state.TargetObjectId.Value ||
                !navigation.PathBuildStateKnown ||
                navigation.PathBuildBusy ||
                !this.TryValidateCurrentTarget(
                    run.Client.ProcessId,
                    observation,
                    state.TargetObjectId.Value) ||
                this.ReadVerbState(
                    run.Client.ProcessId,
                    observation.World.ActiveSectorNumber,
                    state.TargetObjectId.Value,
                    step.Verb) !=
                    NavigationAutoPilotVerbState.Executable)
            {
                return NavigationAutoPilotEffectOutcome.Failure(
                    step.ActivationFailedReason,
                    $"Auto Pilot stopped because {step.VerbName} was no longer executable immediately before activation.");
            }

            await Win32.NativeMethods
                .StableLeftClickAtCurrentCursorAsync(
                    run.Cancellation.Token)
                .ConfigureAwait(false);
            return NavigationAutoPilotEffectOutcome.Success();
        }
        finally
        {
            if (hasOriginalCursor &&
                ownsCursorPosition &&
                CursorRemainsAt(commandedCursorPosition))
            {
                _ = NativeMethods.MoveCursorToScreenPoint(
                    originalCursorPosition);
            }

            await run.HostForm
                .SetAddonOverlayInputSuppressedAsync(suppressed: false)
                .ConfigureAwait(false);
        }
    }

    private bool TryGetStartContext(
        int processId,
        uint? expectedSectorId,
        out ClientObservationSnapshot observation,
        out NavigationRouteSnapshot route,
        out string error)
    {
        observation = null!;
        route = null!;
        error = "";

        if (!this.observationCoordinator.TryGetSnapshot(
                processId,
                out observation) ||
            !observation.IsAvailable ||
            observation.LifecycleState !=
                ClientLifecycleState.InGame ||
            observation.LoadingOrTransitionFlag != 0 ||
            !observation.World.IsAvailable ||
            observation.World.Environment !=
                ClientWorldEnvironment.Space ||
            observation.World.ActiveSectorNumber == 0)
        {
            error =
                "Auto Pilot requires a verified live space-sector context.";
            return false;
        }

        if (expectedSectorId.HasValue &&
            expectedSectorId.Value != 0 &&
            observation.World.ActiveSectorNumber !=
                expectedSectorId.Value)
        {
            error =
                "The client changed sector after the Auto Pilot gesture.";
            return false;
        }

        route = this.routeCoordinator.GetSnapshot(processId);

        if (!route.IsAvailable ||
            route.Route == null ||
            route.CurrentSector == null)
        {
            error = string.IsNullOrWhiteSpace(route.StatusText)
                ? "No route is planned for this character."
                : route.StatusText;
            return false;
        }

        return true;
    }

    private bool TryGetEffectContext(
        AutoPilotRun run,
        out ClientObservationSnapshot observation,
        out string error)
    {
        observation = null!;
        error = "";

        if (!IsClientAvailable(run.Client, run.HostForm) ||
            !this.observationCoordinator.TryGetSnapshot(
                run.Client.ProcessId,
                out observation) ||
            !observation.IsAvailable ||
            observation.LifecycleState !=
                ClientLifecycleState.InGame ||
            observation.LoadingOrTransitionFlag != 0 ||
            !observation.World.IsAvailable ||
            observation.World.Environment !=
                ClientWorldEnvironment.Space ||
            observation.World.ActiveSectorNumber == 0)
        {
            error =
                "Auto Pilot could not verify the live space-sector context before issuing input.";
            return false;
        }

        var route = this.routeCoordinator.GetSnapshot(
            run.Client.ProcessId);

        if (!route.IsAvailable ||
            route.Route?.RouteId != run.MachineState.RouteId ||
            !string.Equals(
                route.CurrentSector?.Key,
                run.MachineState.ExpectedCurrentSectorKey,
                StringComparison.Ordinal) ||
            run.MachineState.Step == null ||
            !run.MachineState.Step.Matches(
                route.Route.NextStep))
        {
            error =
                "Auto Pilot stopped because the planned route changed before input was issued.";
            return false;
        }

        return true;
    }

    private bool TryValidateCurrentTarget(
        int processId,
        ClientObservationSnapshot observation,
        uint targetObjectId)
    {
        return observation.World.IsAvailable &&
            observation.World.ActiveSectorNumber != 0 &&
            this.observationCoordinator.TryReadCurrentTargetObjectId(
                processId,
                observation.World.ActiveSectorNumber,
                out var hasTarget,
                out var currentTargetObjectId,
                out _) &&
            hasTarget &&
            currentTargetObjectId == targetObjectId;
    }

    private void PublishFleetStatus(
        AutoPilotRun run,
        string statusText)
    {
        NavigationAutoPilotSnapshot snapshot;

        lock (this.lockObject)
        {
            if (!this.runs.TryGetValue(
                    run.Client.ProcessId,
                    out var current) ||
                !ReferenceEquals(current, run) ||
                !run.Snapshot.IsActive)
            {
                return;
            }

            snapshot = run.Snapshot with
            {
                StatusText = statusText,
                UpdatedAt = DateTimeOffset.UtcNow,
            };

            run.Snapshot = snapshot;
        }
        this.RaiseStateChanged(snapshot);
    }

    private void ApplyFleetFailure(
        AutoPilotRun run,
        NavigationAutoPilotEffectOutcome outcome)
    {
        var transition = CreateFleetFailureTransition(
            run.MachineState,
            outcome);

        this.ApplyTransition(
            run,
            transition,
            this.CaptureFrame(run));
    }

    private static NavigationAutoPilotMachineTransition
        CreateFleetFailureTransition(
            NavigationAutoPilotMachineState state,
            NavigationAutoPilotEffectOutcome outcome)
    {
        return new NavigationAutoPilotMachineTransition(
            state with
            {
                Phase = NavigationAutoPilotMachinePhase.Stopped,
                PhaseStartedAt = DateTimeOffset.UtcNow,
                PublicState = NavigationAutoPilotState.Stopped,
                StopReason = outcome.StopReason ==
                    NavigationAutoPilotStopReason.None
                        ? NavigationAutoPilotStopReason.ClientUnavailable
                        : outcome.StopReason,
                StatusText = string.IsNullOrWhiteSpace(
                    outcome.StatusText)
                        ? "Fleet Auto Pilot stopped because managed-client coordination failed."
                        : outcome.StatusText,
            });
    }

    private void ApplyTransition(
        AutoPilotRun run,
        NavigationAutoPilotMachineTransition transition,
        NavigationAutoPilotMachineFrame frame)
    {
        var previous = run.MachineState;
        run.MachineState = transition.State;
        var forcePublish = previous.Phase !=
                transition.State.Phase ||
            previous.PublicState != transition.State.PublicState ||
            previous.StopReason != transition.State.StopReason ||
            !string.Equals(
                previous.StatusText,
                transition.State.StatusText,
                StringComparison.Ordinal) ||
            !string.Equals(
                previous.Step?.TargetName,
                transition.State.Step?.TargetName,
                StringComparison.Ordinal) ||
            previous.TargetObjectId !=
                transition.State.TargetObjectId ||
            !string.Equals(
                previous.ExpectedCurrentSectorKey,
                transition.State.ExpectedCurrentSectorKey,
                StringComparison.Ordinal) ||
            transition.State.IsTerminal;

        if (forcePublish)
        {
            this.PublishMachineState(run);
        }
    }

    private void PublishMachineState(AutoPilotRun run)
    {
        NavigationAutoPilotSnapshot snapshot;

        lock (this.lockObject)
        {
            if (!this.runs.TryGetValue(
                    run.Client.ProcessId,
                    out var current) ||
                !ReferenceEquals(current, run))
            {
                return;
            }

            snapshot = BuildSnapshot(
                run.Client.ProcessId,
                run.MachineState,
                isActive: !run.MachineState.IsTerminal,
                DateTimeOffset.UtcNow);

            run.Snapshot = snapshot;
        }

        this.RaiseStateChanged(snapshot);
    }

    private static NavigationAutoPilotSnapshot BuildSnapshot(
        int processId,
        NavigationAutoPilotMachineState state,
        bool isActive,
        DateTimeOffset now)
    {
        return new NavigationAutoPilotSnapshot
        {
            ProcessId = processId,
            State = state.PublicState,
            StopReason = state.StopReason,
            StatusText = state.StatusText,
            IsActive = isActive,
            StartedAt = state.StartedAt,
            UpdatedAt = now,
            RouteId = state.RouteId,
            ExpectedTargetName = state.Step?.TargetName,
            ExpectedTargetObjectId = state.TargetObjectId,
            ExpectedSectorName = state.Step?.ToSectorName ??
                state.ExpectedCurrentSectorName,
            ExpectedSectorKey = state.Step?.ToSectorKey ??
                state.ExpectedCurrentSectorKey,
            CurrentEnergy = state.CurrentEnergy,
            RequiredEnergy = state.RequiredEnergy,
        };
    }

    private void RaiseStateChanged(
        NavigationAutoPilotSnapshot snapshot)
    {
        this.StateChanged?.Invoke(
            this,
            new NavigationAutoPilotStateChangedEventArgs(snapshot));
    }

    private static bool IsClientAvailable(
        ClientInstance client,
        ClientHostForm hostForm)
    {
        return client.GameWindowHandle != IntPtr.Zero &&
            !hostForm.IsDisposed &&
            !hostForm.Disposing;
    }

    private static bool TryReadWarpState(
        ClientObservationSnapshot observation,
        out int warpAvailable,
        out bool isWarping)
    {
        warpAvailable = 0;
        isWarping = false;

        if (!observation.LocalPlayer.IsAvailable ||
            !observation.LocalPlayer.Operational.IsAvailable)
        {
            return false;
        }

        var runtime = observation.LocalPlayer.Operational.Runtime;

        if (!runtime.WarpAvailable.HasValue ||
            !runtime.PrivateWarpState.HasValue ||
            !runtime.GlobalWarpState.HasValue)
        {
            return false;
        }

        warpAvailable = runtime.WarpAvailable.Value;
        isWarping = runtime.HasActiveWarpState;
        return true;
    }

    private static int? ReadCurrentEnergy(
        ClientObservationSnapshot observation)
    {
        var energy = observation.LocalPlayer.Energy;

        return energy.IsAvailable &&
               energy.HasCompleteEnergyData
            ? (int)MathF.Round(
                energy.DerivedCurrentEnergyPower)
            : null;
    }

    private static bool CursorRemainsAt(Point expected)
    {
        if (!NativeMethods.TryGetCursorScreenPosition(
                out var current))
        {
            return false;
        }

        return Math.Abs(current.X - expected.X) <=
                   CursorRestoreTolerancePixels &&
               Math.Abs(current.Y - expected.Y) <=
                   CursorRestoreTolerancePixels;
    }

    private static void DisposeRunWhenComplete(AutoPilotRun run)
    {
        var task = run.Task;

        if (task == null || task.IsCompleted)
        {
            run.Dispose();
            return;
        }

        _ = task.ContinueWith(
            _ => run.Dispose(),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private sealed class AutoPilotRun(
        ClientInstance client,
        ClientHostForm hostForm,
        NavigationAutoPilotFleetContext fleet,
        NavigationAutoPilotMachineState machineState,
        NavigationAutoPilotSnapshot snapshot) : IDisposable
    {
        private bool disposed;

        public ClientInstance Client { get; } = client;

        public ClientHostForm HostForm { get; } = hostForm;

        public NavigationAutoPilotFleetContext Fleet { get; } = fleet;

        public NavigationAutoPilotMachineState MachineState
        { get; set; } = machineState;

        public NavigationAutoPilotSnapshot Snapshot { get; set; } =
            snapshot;

        public CancellationTokenSource Cancellation { get; } = new();

        public Task? Task { get; set; }

        public string TargetResolutionTargetName { get; set; } = "";

        public DateTimeOffset NextTargetResolutionAt { get; set; }

        public NavigationTargetResolutionResult LastTargetResolution
        { get; set; } = NavigationTargetResolutionResult.Waiting(
            "Target resolution has not run yet.");

        public NavigationAutoPilotStopReason RequestedStopReason
        { get; set; }

        public string RequestedStopStatus { get; set; } = "";

        public void Dispose()
        {
            if (this.disposed)
            {
                return;
            }

            this.disposed = true;
            this.Cancellation.Dispose();
            this.Fleet.Dispose();
        }
    }
}
