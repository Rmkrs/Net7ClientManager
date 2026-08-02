namespace Net7ClientManager.ControlPlane;

using System.Globalization;
using System.Text.Json;
using Net7ClientManager.ActivityJournal;
using Net7ClientManager.ControlPlane.Contracts;
using Net7ClientManager.Core;
using Net7ClientManager.Forms;
using Net7ClientManager.Models;
using Net7ClientManager.Navigation;
using Net7ClientManager.Observations;
using Net7ClientManager.Observations.Models;

internal sealed partial class ClientManagerControlPlaneService
{
    private readonly ClientManager clientManager;
    private readonly MainForm mainForm;
    private readonly JsonSerializerOptions jsonOptions =
        ControlPlaneProtocol.CreateJsonOptions();

    public ClientManagerControlPlaneService(
        ClientManager clientManager,
        MainForm mainForm)
    {
        this.clientManager = clientManager;
        this.mainForm = mainForm;
    }

    public Task<ControlPlaneResponse> ExecuteAsync(
        ControlPlaneRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Version != ControlPlaneProtocol.Version)
        {
            return Task.FromResult(this.Failure(
                request,
                ControlPlaneExitCode.InvalidArguments,
                $"Protocol version {request.Version} is not supported."));
        }

        if (string.IsNullOrWhiteSpace(request.Operation))
        {
            return Task.FromResult(this.Failure(
                request,
                ControlPlaneExitCode.InvalidArguments,
                "No operation was supplied."));
        }

        return request.Operation.Trim().ToLowerInvariant() switch
        {
            "slots.list" => Task.FromResult(this.ListSlots(request)),
            "slot.status" => Task.FromResult(this.GetSlotStatus(request)),
            "slot.location" => Task.FromResult(this.GetLocation(request)),
            "slot.target" => Task.FromResult(this.GetTarget(request)),
            "slot.interaction" =>
                Task.FromResult(this.GetInteraction(request)),
            "slot.group" => Task.FromResult(this.GetGroup(request)),
            "slot.missions" => Task.FromResult(this.GetMissions(request)),
            "slot.inventory" => Task.FromResult(this.GetInventory(request)),
            "fleet.commands" => this.OnUiThreadAsync(
                request,
                () => this.ListFleetCommands(request),
                cancellationToken),
            "fleet.execute" => this.OnUiThreadAsync(
                request,
                () => this.ExecuteFleetCommandAsync(request),
                cancellationToken),
            "fleet.invite" => this.OnUiThreadAsync(
                request,
                () => this.InviteFleetClientAsync(request),
                cancellationToken),
            "navigation.route" => Task.FromResult(this.GetRoute(request)),
            "navigation.autopilot" =>
                Task.FromResult(this.GetAutoPilot(request)),
            "slot.launch" => this.OnUiThreadAsync(
                request,
                () => this.LaunchSlot(request),
                cancellationToken),
            "window.focus_game" => this.OnUiThreadAsync(
                request,
                () => this.FocusGame(request),
                cancellationToken),
            "window.focus_navigation" or
            "window.show_navigation" => this.OnUiThreadAsync(
                request,
                () => this.ShowNavigation(request),
                cancellationToken),
            "window.show_mission_wiki" => this.OnUiThreadAsync(
                request,
                () => this.ShowMissionWiki(request),
                cancellationToken),
            "navigation.set_destination" => this.OnUiThreadAsync(
                request,
                () => this.SetDestination(request),
                cancellationToken),
            "navigation.start_autopilot" => this.OnUiThreadAsync(
                request,
                () => this.StartAutoPilot(request),
                cancellationToken),
            "navigation.stop_autopilot" => this.OnUiThreadAsync(
                request,
                () => this.StopAutoPilot(request),
                cancellationToken),
            "navigation.plan_return" => this.OnUiThreadAsync(
                request,
                () => this.PlanReturn(request),
                cancellationToken),
            "navigation.clear_route" => this.OnUiThreadAsync(
                request,
                () => this.ClearRoute(request),
                cancellationToken),
            _ => Task.FromResult(this.Failure(
                request,
                ControlPlaneExitCode.InvalidArguments,
                $"Unknown operation '{request.Operation}'.")),
        };
    }

    private ControlPlaneResponse ListSlots(ControlPlaneRequest request)
    {
        var profile = this.clientManager.ActiveProfile;

        if (profile == null)
        {
            return this.Failure(
                request,
                ControlPlaneExitCode.Unavailable,
                "No profile is active.");
        }

        var clients = this.clientManager.Clients;
        var slots = profile.Slots.Select(slot =>
        {
            var client = clients.FirstOrDefault(candidate =>
                candidate.AssignedSlotId == slot.Id &&
                candidate.State is not
                    ClientState.Closing and not ClientState.Stopped);

            return new
            {
                id = slot.Id,
                name = slot.Name,
                running = client != null,
                processId = client?.ProcessId,
                lifecycle = client == null
                    ? null
                    : NormalizeEnum(client.LifecycleState),
                pilot = client?.LiveCharacterIdentity.Name,
                profession = client?.LiveCharacterIdentity.Profession,
            };
        }).ToArray();

        var output = string.Join(
            Environment.NewLine,
            slots.Select(slot => string.Create(
                CultureInfo.InvariantCulture,
                $"{slot.name}\t{(slot.running ? "RUNNING" : "AVAILABLE")}\t{slot.pilot ?? ""}\t{slot.lifecycle ?? ""}")));

        return this.Success(
            request,
            $"Found {slots.Length} configured slot(s).",
            output,
            new
            {
                profile = profile.Name,
                profileId = profile.Id,
                slots,
            });
    }

    private ControlPlaneResponse GetSlotStatus(ControlPlaneRequest request)
    {
        var resolution = this.ResolveSlot(request, requireRunning: false);

        if (resolution.Failure != null)
        {
            return resolution.Failure;
        }

        var slot = resolution.Slot!;
        var client = resolution.Client;
        var account = this.clientManager.FindConfiguredAccount(slot.AccountId);
        var character = this.clientManager.FindConfiguredCharacter(
            slot.AccountId,
            slot.CharacterId);

        var lifecycle = client == null
            ? "not_running"
            : NormalizeEnum(client.LifecycleState);
        var output = client == null
            ? "Not running"
            : FormatLifecycle(client.LifecycleState);

        return this.Success(
            request,
            output,
            output,
            new
            {
                slot = slot.Name,
                slotId = slot.Id,
                configuredAccount = account?.DisplayName ?? account?.LoginName,
                configuredPilot = character?.Name,
                running = client != null,
                processId = client?.ProcessId,
                clientState = client == null
                    ? null
                    : NormalizeEnum(client.State),
                lifecycle,
                pilot = client?.LiveCharacterIdentity.Name,
                profession = client?.LiveCharacterIdentity.Profession,
                overallLevel = client?.LiveCharacterIdentity.OverallLevel,
                observedAt = client?.LastObservedAt,
                observationStatus = client?.ObservationStatus,
            });
    }

    private ControlPlaneResponse GetLocation(ControlPlaneRequest request)
    {
        var resolution = this.ResolveSlot(request, requireRunning: true);

        if (resolution.Failure != null)
        {
            return resolution.Failure;
        }

        var client = resolution.Client!;
        var observation = this.FindObservation(client.ProcessId);

        if (observation == null ||
            !observation.IsAvailable ||
            !observation.World.IsAvailable)
        {
            return this.Failure(
                request,
                ControlPlaneExitCode.Unavailable,
                "Location is not available yet.",
                new
                {
                    slot = resolution.Slot!.Name,
                    processId = client.ProcessId,
                    lifecycle = NormalizeEnum(client.LifecycleState),
                });
        }

        var world = observation.World;
        var location = JournalLocationResolver.Capture(
            observation);
        var displayName = !string.IsNullOrWhiteSpace(location.StarbaseName)
            ? location.StarbaseName
            : location.SectorName;

        return this.Success(
            request,
            displayName,
            displayName,
            new
            {
                slot = resolution.Slot!.Name,
                pilot = client.LiveCharacterIdentity.Name,
                lifecycle = NormalizeEnum(observation.LifecycleState),
                environment = NormalizeEnum(world.Environment),
                system = location.SystemName,
                sector = location.SectorName,
                starbase = string.IsNullOrWhiteSpace(location.StarbaseName)
                    ? null
                    : location.StarbaseName,
                station = string.IsNullOrWhiteSpace(location.StarbaseName)
                    ? null
                    : location.StarbaseName,
                nearestNav = string.IsNullOrWhiteSpace(location.NearestNavName)
                    ? null
                    : location.NearestNavName,
                activeSectorNumber = world.ActiveSectorNumber,
                observedAt = observation.ObservedAt,
            });
    }

    private ControlPlaneResponse GetTarget(ControlPlaneRequest request)
    {
        var resolution = this.ResolveSlot(request, requireRunning: true);

        if (resolution.Failure != null)
        {
            return resolution.Failure;
        }

        var client = resolution.Client!;
        var observation = this.FindObservation(client.ProcessId);

        if (observation == null || !observation.Target.IsAvailable)
        {
            return this.Failure(
                request,
                ControlPlaneExitCode.Unavailable,
                "Target information is not available yet.");
        }

        var target = observation.Target;

        if (!target.HasTarget)
        {
            return this.Success(
                request,
                "No target",
                "No target",
                new
                {
                    slot = resolution.Slot!.Name,
                    hasTarget = false,
                    observedAt = observation.ObservedAt,
                });
        }

        return this.Success(
            request,
            target.Name,
            target.Name,
            new
            {
                slot = resolution.Slot!.Name,
                hasTarget = true,
                name = target.Name,
                kind = NormalizeEnum(target.Kind),
                relation = NormalizeEnum(target.Relation),
                distance = target.Distance.IsAvailable
                    ? target.Distance.SurfaceDistance
                    : default(float?),
                distanceText = target.Distance.IsAvailable
                    ? target.Distance.NativeReadoutText
                    : null,
                observedAt = observation.ObservedAt,
            });
    }

    private ControlPlaneResponse GetRoute(ControlPlaneRequest request)
    {
        var resolution = this.ResolveSlot(request, requireRunning: true);

        if (resolution.Failure != null)
        {
            return resolution.Failure;
        }

        var client = resolution.Client!;
        var snapshot = this.clientManager.GetNavigationRouteSnapshot(
            client.ProcessId);
        var route = snapshot.Route;

        if (route == null)
        {
            return this.Success(
                request,
                "No active route",
                "No active route",
                new
                {
                    slot = resolution.Slot!.Name,
                    hasRoute = false,
                    status = NormalizeEnum(snapshot.Status),
                    statusText = snapshot.StatusText,
                    observedAt = snapshot.ObservedAt,
                });
        }

        var nextStep = route.NextStep;
        var output = string.Create(
            CultureInfo.InvariantCulture,
            $"{route.Destination.DisplayName} ({route.RemainingHopCount} remaining)");

        return this.Success(
            request,
            output,
            output,
            new
            {
                slot = resolution.Slot!.Name,
                hasRoute = true,
                routeId = route.RouteId,
                status = NormalizeEnum(route.Status),
                statusText = route.StatusText,
                origin = MapDestination(route.OriginDestination),
                current = new
                {
                    sector = route.Current.Name,
                    system = route.Current.SystemName,
                },
                destination = MapDestination(route.Destination),
                completedHopCount = route.CompletedHopCount,
                remainingHopCount = route.RemainingHopCount,
                totalHopCount = route.TotalHopCount,
                nextStep = nextStep == null
                    ? null
                    : new
                    {
                        number = nextStep.Number,
                        kind = NormalizeEnum(nextStep.Kind),
                        fromSector = nextStep.FromSectorName,
                        toSector = nextStep.ToSectorName,
                        target = nextStep.DepartureTargetName ??
                                 nextStep.FinalTargetName,
                        verb = NormalizeEnum(nextStep.FinalTargetVerb),
                    },
                warnings = route.Warnings,
                observedAt = snapshot.ObservedAt,
            });
    }

    private ControlPlaneResponse GetAutoPilot(ControlPlaneRequest request)
    {
        var resolution = this.ResolveSlot(request, requireRunning: true);

        if (resolution.Failure != null)
        {
            return resolution.Failure;
        }

        var client = resolution.Client!;
        var snapshot = this.clientManager.GetNavigationAutoPilotSnapshot(
            client.ProcessId);
        var state = NormalizeEnum(snapshot.State);

        return this.Success(
            request,
            state,
            state,
            new
            {
                slot = resolution.Slot!.Name,
                state,
                active = snapshot.IsActive,
                stopReason = snapshot.StopReason ==
                             NavigationAutoPilotStopReason.None
                    ? null
                    : NormalizeEnum(snapshot.StopReason),
                statusText = snapshot.StatusText,
                routeId = snapshot.RouteId,
                expectedTarget = snapshot.ExpectedTargetName,
                expectedSector = snapshot.ExpectedSectorName,
                startedAt = snapshot.StartedAt,
                updatedAt = snapshot.UpdatedAt,
            });
    }

    private ControlPlaneResponse LaunchSlot(ControlPlaneRequest request)
    {
        var resolution = this.ResolveSlot(request, requireRunning: false);

        if (resolution.Failure != null)
        {
            return resolution.Failure;
        }

        if (resolution.Client != null)
        {
            return this.Failure(
                request,
                ControlPlaneExitCode.Rejected,
                $"{resolution.Slot!.Name} is already running.");
        }

        var started = this.clientManager.StartProfileSlot(
            resolution.Slot!.Id,
            this.mainForm,
            out var status);

        return started
            ? this.Success(request, status, status)
            : this.Failure(
                request,
                ControlPlaneExitCode.Rejected,
                status);
    }

    private ControlPlaneResponse FocusGame(ControlPlaneRequest request)
    {
        var resolution = this.ResolveSlot(request, requireRunning: true);

        if (resolution.Failure != null)
        {
            return resolution.Failure;
        }

        var host = resolution.Client!.HostForm;

        if (host == null || !host.FocusHostedGameFromControlPlane())
        {
            return this.Failure(
                request,
                ControlPlaneExitCode.Unavailable,
                "The game window is not ready yet.");
        }

        var message = $"Focused {resolution.Slot!.Name}.";
        return this.Success(request, message, message);
    }

    private ControlPlaneResponse ShowNavigation(ControlPlaneRequest request)
    {
        var resolution = this.ResolveSlot(request, requireRunning: true);

        if (resolution.Failure != null)
        {
            return resolution.Failure;
        }

        var host = resolution.Client!.HostForm;

        if (host == null ||
            !host.ShowNavigationCompanionFromControlPlane())
        {
            return this.Failure(
                request,
                ControlPlaneExitCode.Unavailable,
                "Navigation is available after the pilot enters the game.");
        }

        var message = $"Navigation shown for {resolution.Slot!.Name}.";
        return this.Success(request, message, message);
    }

    private ControlPlaneResponse ShowMissionWiki(ControlPlaneRequest request)
    {
        var resolution = this.ResolveSlot(request, requireRunning: true);

        if (resolution.Failure != null)
        {
            return resolution.Failure;
        }

        var host = resolution.Client!.HostForm;

        if (host == null ||
            !host.ShowMissionWikiCompanionFromControlPlane())
        {
            return this.Failure(
                request,
                ControlPlaneExitCode.Unavailable,
                "Mission Wiki is disabled or the pilot is not in the game.");
        }

        var message = $"Mission Wiki shown for {resolution.Slot!.Name}.";
        return this.Success(request, message, message);
    }

    private ControlPlaneResponse SetDestination(ControlPlaneRequest request)
    {
        var resolution = this.ResolveSlot(request, requireRunning: true);

        if (resolution.Failure != null)
        {
            return resolution.Failure;
        }

        if (!request.Arguments.TryGetValue(
                "destination",
                out var destinationText) ||
            string.IsNullOrWhiteSpace(destinationText))
        {
            return this.Failure(
                request,
                ControlPlaneExitCode.InvalidArguments,
                "A destination name is required.");
        }

        var destinationResolution = this.ResolveDestination(destinationText);

        if (destinationResolution.Destination == null)
        {
            return this.Failure(
                request,
                destinationResolution.IsAmbiguous
                    ? ControlPlaneExitCode.InvalidArguments
                    : ControlPlaneExitCode.NotFound,
                destinationResolution.Message,
                new
                {
                    query = destinationText,
                    candidates = destinationResolution.Candidates,
                });
        }

        var result = this.clientManager.SetNavigationDestination(
            resolution.Client!.ProcessId,
            destinationResolution.Destination);

        return result.Succeeded
            ? this.Success(
                request,
                result.Snapshot?.StatusText ?? "Destination set.",
                destinationResolution.Destination.DisplayName,
                new
                {
                    slot = resolution.Slot!.Name,
                    destination = MapDestination(
                        destinationResolution.Destination),
                })
            : this.Failure(
                request,
                ControlPlaneExitCode.Rejected,
                result.Error);
    }

    private ControlPlaneResponse StartAutoPilot(ControlPlaneRequest request)
    {
        var resolution = this.ResolveSlot(request, requireRunning: true);

        if (resolution.Failure != null)
        {
            return resolution.Failure;
        }

        var result = this.clientManager.StartNavigationAutoPilot(
            resolution.Client!.ProcessId);

        var message = result.Succeeded
            ? result.Snapshot?.StatusText ?? "Auto Pilot started."
            : result.Error;

        return result.Succeeded
            ? this.Success(request, message, message)
            : this.Failure(
                request,
                ControlPlaneExitCode.Rejected,
                message);
    }

    private ControlPlaneResponse StopAutoPilot(ControlPlaneRequest request)
    {
        var resolution = this.ResolveSlot(request, requireRunning: true);

        if (resolution.Failure != null)
        {
            return resolution.Failure;
        }

        var result = this.clientManager.StopNavigationAutoPilot(
            resolution.Client!.ProcessId);

        var message = result.Succeeded
            ? result.Snapshot?.StatusText ?? "Auto Pilot stopped."
            : result.Error;

        return result.Succeeded
            ? this.Success(request, message, message)
            : this.Failure(
                request,
                ControlPlaneExitCode.Rejected,
                message);
    }

    private ControlPlaneResponse PlanReturn(ControlPlaneRequest request)
    {
        var resolution = this.ResolveSlot(request, requireRunning: true);

        if (resolution.Failure != null)
        {
            return resolution.Failure;
        }

        var result = this.clientManager.PlanNavigationReturnTrip(
            resolution.Client!.ProcessId);

        var message = result.Succeeded
            ? result.Snapshot?.StatusText ?? "Return trip planned."
            : result.Error;

        return result.Succeeded
            ? this.Success(request, message, message)
            : this.Failure(
                request,
                ControlPlaneExitCode.Rejected,
                message);
    }

    private ControlPlaneResponse ClearRoute(ControlPlaneRequest request)
    {
        var resolution = this.ResolveSlot(request, requireRunning: true);

        if (resolution.Failure != null)
        {
            return resolution.Failure;
        }

        var result = this.clientManager.ClearNavigationRoute(
            resolution.Client!.ProcessId);

        var message = result.Succeeded
            ? result.Snapshot?.StatusText ?? "Route cleared."
            : result.Error;

        return result.Succeeded
            ? this.Success(request, message, message)
            : this.Failure(
                request,
                ControlPlaneExitCode.Rejected,
                message);
    }

    private SlotResolution ResolveSlot(
        ControlPlaneRequest request,
        bool requireRunning)
    {
        if (!request.Arguments.TryGetValue("slot", out var selector) ||
            string.IsNullOrWhiteSpace(selector))
        {
            return SlotResolution.Failed(this.Failure(
                request,
                ControlPlaneExitCode.InvalidArguments,
                "A slot name is required."));
        }

        var profile = this.clientManager.ActiveProfile;

        if (profile == null)
        {
            return SlotResolution.Failed(this.Failure(
                request,
                ControlPlaneExitCode.Unavailable,
                "No profile is active."));
        }

        ClientSlot? slot = null;

        if (Guid.TryParse(selector, out var slotId))
        {
            slot = profile.Slots.FirstOrDefault(candidate =>
                candidate.Id == slotId);
        }

        slot ??= profile.Slots.FirstOrDefault(candidate =>
            string.Equals(
                candidate.Name,
                selector.Trim(),
                StringComparison.OrdinalIgnoreCase));

        if (slot == null)
        {
            return SlotResolution.Failed(this.Failure(
                request,
                ControlPlaneExitCode.NotFound,
                $"Slot '{selector}' was not found in profile {profile.Name}."));
        }

        var client = this.clientManager.Clients.FirstOrDefault(candidate =>
            candidate.AssignedSlotId == slot.Id &&
            candidate.State is not ClientState.Closing and not ClientState.Stopped);

        if (requireRunning && client == null)
        {
            return SlotResolution.Failed(this.Failure(
                request,
                ControlPlaneExitCode.Unavailable,
                $"{slot.Name} is not running."));
        }

        return new SlotResolution(slot, client, null);
    }

    private DestinationResolution ResolveDestination(string query)
    {
        var normalizedQuery = GalaxyTopology.NormalizeName(query);

        if (string.IsNullOrWhiteSpace(normalizedQuery))
        {
            return DestinationResolution.NotFound(
                "The destination name is empty.");
        }

        var exact = this.FindDestinationCandidates(
            normalizedQuery,
            exact: true);

        if (exact.Count == 1)
        {
            return DestinationResolution.Resolved(exact[0].Destination);
        }

        if (exact.Count > 1)
        {
            return DestinationResolution.Ambiguous(exact);
        }

        var partial = this.FindDestinationCandidates(
            normalizedQuery,
            exact: false);

        if (partial.Count == 1)
        {
            return DestinationResolution.Resolved(partial[0].Destination);
        }

        if (partial.Count > 1)
        {
            return DestinationResolution.Ambiguous(partial);
        }

        return DestinationResolution.NotFound(
            $"Destination '{query}' was not found.");
    }

    private List<DestinationCandidate> FindDestinationCandidates(
        string normalizedQuery,
        bool exact)
    {
        var data = this.clientManager.NavigationData;
        Dictionary<string, DestinationCandidate> candidates =
            new(StringComparer.Ordinal);

        foreach (var sector in data.Topology.Sectors)
        {
            var normalizedSector = GalaxyTopology.NormalizeName(sector.Name);
            var sectorMatches = exact
                ? string.Equals(
                    normalizedSector,
                    normalizedQuery,
                    StringComparison.Ordinal)
                : normalizedSector.Contains(
                    normalizedQuery,
                    StringComparison.Ordinal);

            if (sectorMatches)
            {
                var destination = NavigationDestination.ForSector(sector);
                candidates[$"sector:{sector.Key}"] = new DestinationCandidate(
                    destination,
                    destination.DisplayName);
            }
        }

        foreach (var catalogSector in data.Catalog.Sectors)
        {
            if (!data.Topology.TryGetByKey(
                    catalogSector.SectorKey,
                    out var sector))
            {
                continue;
            }

            foreach (var target in catalogSector.Targets)
            {
                var names = new[] { target.Name, target.MapDisplayName }
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Select(name => GalaxyTopology.NormalizeName(name!))
                    .Distinct(StringComparer.Ordinal);
                var targetMatches = names.Any(name => exact
                    ? string.Equals(
                        name,
                        normalizedQuery,
                        StringComparison.Ordinal)
                    : name.Contains(
                        normalizedQuery,
                        StringComparison.Ordinal));

                if (!targetMatches)
                {
                    continue;
                }

                var destination = NavigationDestination.ForTarget(
                    sector,
                    target);
                candidates[$"target:{destination.TargetKey}"] =
                    new DestinationCandidate(
                        destination,
                        destination.DisplayName);
            }
        }

        return
        [
            .. candidates.Values
                .OrderBy(candidate => candidate.DisplayName,
                    StringComparer.OrdinalIgnoreCase),
        ];
    }

    private ClientObservationSnapshot? FindObservation(int processId)
    {
        return this.clientManager.GetClientObservationSnapshots()
            .FirstOrDefault(snapshot => snapshot.ProcessId == processId);
    }

    private Task<ControlPlaneResponse> OnUiThreadAsync(
        ControlPlaneRequest request,
        Func<ControlPlaneResponse> action,
        CancellationToken cancellationToken)
    {
        if (this.mainForm.IsDisposed || this.mainForm.Disposing)
        {
            return Task.FromResult(this.Failure(
                request,
                ControlPlaneExitCode.Unavailable,
                "Net7 Client Manager is closing."));
        }

        if (!this.mainForm.InvokeRequired)
        {
            return Task.FromResult(action());
        }

        var completion = new TaskCompletionSource<ControlPlaneResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var registration = cancellationToken.Register(() =>
            completion.TrySetCanceled(cancellationToken));

        try
        {
            this.mainForm.BeginInvoke(() =>
            {
                try
                {
                    if (!completion.Task.IsCompleted)
                    {
                        completion.TrySetResult(action());
                    }
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
                finally
                {
                    registration.Dispose();
                }
            });
        }
        catch (Exception exception)
        {
            registration.Dispose();
            completion.TrySetException(exception);
        }

        return completion.Task;
    }

    private Task<ControlPlaneResponse> OnUiThreadAsync(
        ControlPlaneRequest request,
        Func<Task<ControlPlaneResponse>> action,
        CancellationToken cancellationToken)
    {
        if (this.mainForm.IsDisposed || this.mainForm.Disposing)
        {
            return Task.FromResult(this.Failure(
                request,
                ControlPlaneExitCode.Unavailable,
                "Net7 Client Manager is closing."));
        }

        if (!this.mainForm.InvokeRequired)
        {
            return action();
        }

        var completion = new TaskCompletionSource<ControlPlaneResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var registration = cancellationToken.Register(() =>
            completion.TrySetCanceled(cancellationToken));

        try
        {
            this.mainForm.BeginInvoke(new Action(async () =>
            {
                try
                {
                    if (!completion.Task.IsCompleted)
                    {
                        var response = await action().ConfigureAwait(true);
                        completion.TrySetResult(response);
                    }
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
                finally
                {
                    registration.Dispose();
                }
            }));
        }
        catch (Exception exception)
        {
            registration.Dispose();
            completion.TrySetException(exception);
        }

        return completion.Task;
    }

    private ControlPlaneResponse Success(
        ControlPlaneRequest request,
        string message,
        string? output = null,
        object? data = null)
    {
        return new ControlPlaneResponse
        {
            RequestId = request.RequestId,
            ExitCode = ControlPlaneExitCode.Success,
            Code = ControlPlaneProtocol.GetDefaultResultCode(
                ControlPlaneExitCode.Success),
            Message = message,
            Output = output,
            Data = data == null
                ? null
                : JsonSerializer.SerializeToElement(
                    data,
                    this.jsonOptions),
        };
    }

    private ControlPlaneResponse Failure(
        ControlPlaneRequest request,
        ControlPlaneExitCode exitCode,
        string message,
        object? data = null)
    {
        return new ControlPlaneResponse
        {
            RequestId = request.RequestId,
            ExitCode = exitCode,
            Code = ControlPlaneProtocol.GetDefaultResultCode(exitCode),
            Message = message,
            Data = data == null
                ? null
                : JsonSerializer.SerializeToElement(
                    data,
                    this.jsonOptions),
        };
    }

    private static object MapDestination(NavigationDestination destination)
    {
        return new
        {
            kind = NormalizeEnum(destination.Kind),
            displayName = destination.DisplayName,
            system = destination.SystemName,
            sector = destination.SectorName,
            sectorKey = destination.SectorKey,
            target = destination.TargetName,
            targetKey = destination.TargetKey,
            targetType = destination.TargetType,
        };
    }

    private static string FormatLifecycle(ClientLifecycleState state)
    {
        return state switch
        {
            ClientLifecycleState.InGame => "In game",
            ClientLifecycleState.LoginScreen => "Login screen",
            ClientLifecycleState.CharacterSelection => "Character selection",
            ClientLifecycleState.IntroScene => "Intro",
            _ => state.ToString(),
        };
    }

    private static string NormalizeEnum<TEnum>(TEnum value)
        where TEnum : struct, Enum
    {
        var text = value.ToString();
        var result = new System.Text.StringBuilder(text.Length + 4);

        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];

            if (char.IsUpper(character) && index > 0)
            {
                result.Append('_');
            }

            result.Append(char.ToLowerInvariant(character));
        }

        return result.ToString();
    }

    private sealed record SlotResolution(
        ClientSlot? Slot,
        ClientInstance? Client,
        ControlPlaneResponse? Failure)
    {
        public static SlotResolution Failed(
            ControlPlaneResponse failure) =>
            new(null, null, failure);
    }

    private sealed record DestinationCandidate(
        NavigationDestination Destination,
        string DisplayName);

    private sealed record DestinationResolution(
        NavigationDestination? Destination,
        bool IsAmbiguous,
        string Message,
        IReadOnlyList<string> Candidates)
    {
        public static DestinationResolution Resolved(
            NavigationDestination destination) =>
            new(destination, false, "", []);

        public static DestinationResolution NotFound(string message) =>
            new(null, false, message, []);

        public static DestinationResolution Ambiguous(
            IReadOnlyCollection<DestinationCandidate> candidates)
        {
            var names = candidates
                .Select(candidate => candidate.DisplayName)
                .Take(12)
                .ToArray();
            return new DestinationResolution(
                null,
                true,
                string.Concat(
                    "The destination is ambiguous. Matches: ",
                    string.Join(", ", names),
                    "."),
                names);
        }
    }
}
