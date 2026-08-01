namespace Net7ClientManager.Services;

using Net7ClientManager.Forms;
using Net7ClientManager.Models;
using Net7ClientManager.Navigation;
using Net7ClientManager.Observations;
using Net7ClientManager.Observations.Models;
using Net7ClientManager.Win32;

/// <summary>
/// Coordinates the deliberately small fleet layer around the proven leader
/// Auto Pilot. Followers never plan routes or initiate Warp. They form on the
/// leader, normally inherit its navigation target and Warp through the
/// emulator, and accept the same Gate/Dock interaction before the leader
/// transitions. If a follower missed target propagation, the coordinator
/// copies the leader's target and proves the exact target before any client is
/// allowed through.
/// </summary>
internal sealed class NavigationAutoPilotFleetSupport(
    ClientObservationCoordinator observationCoordinator,
    GameCommandCoordinator gameCommandCoordinator,
    ForegroundInputCoordinator foregroundInputCoordinator)
{
    private static readonly TimeSpan pollInterval =
        TimeSpan.FromMilliseconds(100);

    private static readonly TimeSpan fleetWorldReadyTimeout =
        TimeSpan.FromSeconds(45);

    private static readonly TimeSpan formationSelectionTimeout =
        TimeSpan.FromSeconds(8);

    private static readonly TimeSpan formationJoinTimeout =
        TimeSpan.FromSeconds(45);

    private static readonly TimeSpan formationMenuSettleDelay =
        TimeSpan.FromMilliseconds(1500);

    private static readonly TimeSpan formationSelectionSettleDelay =
        TimeSpan.FromMilliseconds(1000);

    private static readonly TimeSpan targetVerbSettleDelay =
        TimeSpan.FromMilliseconds(1000);

    private static readonly TimeSpan followerVerbReadyTimeout =
        TimeSpan.FromSeconds(10);

    private static readonly TimeSpan followerTargetRecoveryTimeout =
        TimeSpan.FromSeconds(8);

    private static readonly TimeSpan followerTransitionAcceptanceTimeout =
        TimeSpan.FromSeconds(12);

    private static readonly TimeSpan fleetDestinationReadyTimeout =
        TimeSpan.FromSeconds(45);

    private const int CursorRestoreTolerancePixels = 4;

    public bool TryCreateContext(
        ClientInstance leader,
        ClientHostForm leaderHostForm,
        IReadOnlyCollection<ClientInstance> managedClients,
        out NavigationAutoPilotFleetContext context,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(leader);
        ArgumentNullException.ThrowIfNull(leaderHostForm);
        ArgumentNullException.ThrowIfNull(managedClients);

        context = null!;
        error = "";

        if (!observationCoordinator.TryGetSnapshot(
                leader.ProcessId,
                out var leaderObservation) ||
            !leaderObservation.IsAvailable)
        {
            error =
                "Auto Pilot could not inspect live group membership for the selected client.";
            return false;
        }

        var leaderIdentity =
            ClientLiveCharacterIdentityResolver.Resolve(
                leaderObservation);
        var leaderName = leaderIdentity.Name ?? "Leader";
        var followerSpecs = new List<FollowerSpec>();
        var group = leaderObservation.Group;

        if (!group.IsAvailable)
        {
            error =
                "Auto Pilot could not determine whether the selected client is currently grouped.";
            return false;
        }

        if (group.IsInGroup)
        {
            if (!group.IsLeader)
            {
                error =
                    "Group Auto Pilot must be started from the observed group leader.";
                return false;
            }

            var matchedProcessIds = new HashSet<int>
            {
                leader.ProcessId,
            };

            foreach (var member in group.Members
                         .Where(member => member.IsPresent)
                         .OrderBy(member => member.Slot))
            {
                var match = this.FindManagedGroupMember(
                    member,
                    managedClients,
                    matchedProcessIds);

                if (match == null)
                {
                    continue;
                }

                var hostForm = match.HostForm;

                if (hostForm == null ||
                    match.GameWindowHandle == IntPtr.Zero ||
                    hostForm.IsDisposed ||
                    hostForm.Disposing)
                {
                    error =
                        $"Managed group member {member.Name} is not currently hosted and cannot join Auto Pilot.";
                    return false;
                }

                matchedProcessIds.Add(match.ProcessId);
                followerSpecs.Add(
                    new FollowerSpec(
                        match,
                        hostForm,
                        string.IsNullOrWhiteSpace(member.Name)
                            ? $"Group slot {member.Slot}"
                            : member.Name.Trim(),
                        member.Slot));
            }
        }

        var participants = new List<NavigationAutoPilotFleetParticipant>();

        try
        {
            if (!TryAcquireParticipant(
                    leader,
                    leaderHostForm,
                    leaderName,
                    groupSlot: -1,
                    out var leaderParticipant,
                    out error))
            {
                return false;
            }

            participants.Add(leaderParticipant);
            var followers = new List<NavigationAutoPilotFleetParticipant>();

            foreach (var spec in followerSpecs)
            {
                if (!TryAcquireParticipant(
                        spec.Client,
                        spec.HostForm,
                        spec.LiveName,
                        spec.GroupSlot,
                        out var follower,
                        out error))
                {
                    return false;
                }

                participants.Add(follower);
                followers.Add(follower);
            }

            context = new NavigationAutoPilotFleetContext(
                leaderParticipant,
                followers);
            participants.Clear();
            return true;
        }
        finally
        {
            foreach (var participant in participants)
            {
                participant.Dispose();
            }
        }
    }

    public async Task<NavigationAutoPilotEffectOutcome>
        EnsureReadyForLegAsync(
            NavigationAutoPilotFleetContext fleet,
            Action<string> publishStatus,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fleet);
        ArgumentNullException.ThrowIfNull(publishStatus);

        if (!fleet.HasFollowers)
        {
            return NavigationAutoPilotEffectOutcome.Success();
        }

        // Fleet context acquisition starts the compact per-client navigation
        // observers, but their first 100 ms sample may not exist yet. Do not
        // treat that normal warm-up window as a permanent observation failure.
        // A previously prepared generation can still take the fast path once
        // its leader sample is available.
        if (TryGetNavigation(
                fleet.Leader,
                out var leaderNavigation,
                out _) &&
            fleet.PreparedLeaderGenerationSequence ==
                leaderNavigation.GenerationSequence)
        {
            return NavigationAutoPilotEffectOutcome.Success();
        }

        publishStatus(
            $"Waiting for navigation observations from the leader and {fleet.Followers.Count} managed follower{(fleet.Followers.Count == 1 ? "" : "s")}.");

        var worldReady = await this.WaitForFleetInSameSpaceSectorAsync(
                fleet,
                fleetWorldReadyTimeout,
                cancellationToken)
            .ConfigureAwait(false);

        if (!worldReady.Succeeded)
        {
            return worldReady;
        }

        if (!TryGetNavigation(
                fleet.Leader,
                out leaderNavigation,
                out var navigationError))
        {
            return NavigationAutoPilotEffectOutcome.Failure(
                NavigationAutoPilotStopReason.ObservationUnavailable,
                navigationError);
        }

        if (fleet.PreparedLeaderGenerationSequence ==
            leaderNavigation.GenerationSequence)
        {
            return NavigationAutoPilotEffectOutcome.Success();
        }

        if (!this.TryObserveFormationState(
                fleet,
                out var blockFormationActive,
                out var followersNeedingFormation,
                out var formationObservationError))
        {
            return NavigationAutoPilotEffectOutcome.Failure(
                NavigationAutoPilotStopReason.ObservationUnavailable,
                formationObservationError);
        }

        if (blockFormationActive &&
            followersNeedingFormation.Count == 0)
        {
            fleet.PreparedLeaderGenerationSequence =
                leaderNavigation.GenerationSequence;
            publishStatus(
                "Fleet is already fully formed in Block formation; resuming Auto Pilot.");

            return NavigationAutoPilotEffectOutcome.Success();
        }

        // FormationName can remain "Block" across a sector transition even
        // though the old formation anchor no longer exists. A currently
        // parent-relative follower proves that the anchor is live in this
        // world generation. Without that proof, recreate Block formation.
        var hasConfirmedLiveBlockAnchor =
            blockFormationActive &&
            followersNeedingFormation.Count < fleet.Followers.Count;

        if (!hasConfirmedLiveBlockAnchor)
        {
            publishStatus(
                $"Fleet is ready in {leaderNavigation.SectorName}; setting Block formation on the group leader.");

            var formation = await this.SetBlockFormationAsync(
                    fleet.Leader,
                    cancellationToken)
                .ConfigureAwait(false);

            if (!formation.Succeeded)
            {
                return formation;
            }

            await Task.Delay(
                    formationSelectionSettleDelay,
                    cancellationToken)
                .ConfigureAwait(false);

            var formationSelected = await this.WaitForBlockFormationAsync(
                    fleet.Leader.Client.ProcessId,
                    cancellationToken)
                .ConfigureAwait(false);

            if (!formationSelected)
            {
                return NavigationAutoPilotEffectOutcome.Failure(
                    NavigationAutoPilotStopReason.ObservationUnavailable,
                    "Auto Pilot stopped because the leader did not expose Block formation after it was selected.");
            }
        }
        else
        {
            var formedCount =
                fleet.Followers.Count -
                followersNeedingFormation.Count;
            publishStatus(
                $"Block formation is already active; preserving {formedCount} formed follower{(formedCount == 1 ? "" : "s")}.");
        }

        // Re-read after a possible formation change. Selecting a formation can
        // preserve followers that were already attached, so only send Join
        // Formation to clients that still lack proven parent-relative state.
        if (!this.TryObserveFormationState(
                fleet,
                out blockFormationActive,
                out followersNeedingFormation,
                out formationObservationError) ||
            !blockFormationActive)
        {
            return NavigationAutoPilotEffectOutcome.Failure(
                NavigationAutoPilotStopReason.ObservationUnavailable,
                string.IsNullOrWhiteSpace(formationObservationError)
                    ? "Auto Pilot stopped because Block formation was not observable after fleet preparation."
                    : formationObservationError);
        }

        foreach (var follower in followersNeedingFormation)
        {
            cancellationToken.ThrowIfCancellationRequested();
            publishStatus($"Asking {follower.LiveName} to join formation.");

            var command = await gameCommandCoordinator.ExecuteAsync(
                    follower.Client,
                    GameCommand.Formation,
                    cancellationToken)
                .ConfigureAwait(false);

            if (!command.Succeeded)
            {
                return NavigationAutoPilotEffectOutcome.Failure(
                    NavigationAutoPilotStopReason.ObservationUnavailable,
                    $"Auto Pilot could not ask {follower.LiveName} to join formation: {command.Error}");
            }

            await Task.Delay(
                    TimeSpan.FromMilliseconds(250),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var formed = await this.WaitForFollowersFormedAsync(
                fleet,
                publishStatus,
                cancellationToken)
            .ConfigureAwait(false);

        if (!formed.Succeeded)
        {
            return formed;
        }

        if (!TryGetNavigation(
                fleet.Leader,
                out leaderNavigation,
                out navigationError))
        {
            return NavigationAutoPilotEffectOutcome.Failure(
                NavigationAutoPilotStopReason.ObservationUnavailable,
                navigationError);
        }

        fleet.PreparedLeaderGenerationSequence =
            leaderNavigation.GenerationSequence;
        publishStatus("Fleet formation is ready; resuming Auto Pilot.");

        return NavigationAutoPilotEffectOutcome.Success();
    }

    public async Task<NavigationAutoPilotEffectOutcome>
        ActivateFollowersAsync(
            NavigationAutoPilotFleetContext fleet,
            NavigationAutoPilotStepPlan step,
            uint targetObjectId,
            uint expectedSectorNumber,
            Action<string> publishStatus,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fleet);
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(publishStatus);

        if (!this.TryValidateGroupMembership(fleet))
        {
            return NavigationAutoPilotEffectOutcome.Failure(
                NavigationAutoPilotStopReason.ClientUnavailable,
                "Auto Pilot stopped because managed fleet membership changed before the route interaction.");
        }

        var targetsReady = await this.EnsureFollowerTargetsAsync(
                fleet,
                step,
                targetObjectId,
                expectedSectorNumber,
                publishStatus,
                cancellationToken)
            .ConfigureAwait(false);

        if (!targetsReady.Succeeded)
        {
            return targetsReady;
        }

        var ready = await this.WaitForFleetVerbSettleAsync(
                fleet,
                step,
                targetObjectId,
                expectedSectorNumber,
                publishStatus,
                cancellationToken)
            .ConfigureAwait(false);

        if (!ready.Succeeded)
        {
            return ready;
        }

        foreach (var follower in fleet.Followers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!this.TryValidateFollowerMembership(
                    fleet,
                    follower))
            {
                return NavigationAutoPilotEffectOutcome.Failure(
                    NavigationAutoPilotStopReason.ClientUnavailable,
                    $"Auto Pilot stopped because {follower.LiveName} was no longer an observed member of the leader's group before {step.VerbName}.");
            }

            publishStatus(
                $"Activating {step.VerbName} for {follower.LiveName}.");

            var activated = await this.ActivateFollowerVerbAsync(
                    follower,
                    step,
                    targetObjectId,
                    expectedSectorNumber,
                    cancellationToken)
                .ConfigureAwait(false);

            if (!activated.Succeeded)
            {
                return activated;
            }

            publishStatus(
                $"{follower.LiveName} accepted {step.VerbName}; preparing the next client.");
        }

        return NavigationAutoPilotEffectOutcome.Success();
    }

    public async Task<NavigationAutoPilotEffectOutcome>
        WaitForFollowersAtDestinationAsync(
            NavigationAutoPilotFleetContext fleet,
            NavigationAutoPilotStepPlan? step,
            uint? targetObjectId,
            uint expectedSectorNumber,
            Action<string> publishStatus,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fleet);
        ArgumentNullException.ThrowIfNull(publishStatus);

        if (!fleet.HasFollowers)
        {
            return NavigationAutoPilotEffectOutcome.Success();
        }

        var deadline = DateTimeOffset.UtcNow +
            fleetDestinationReadyTimeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? waitingFor = null;

            foreach (var follower in fleet.Followers)
            {
                if (!this.HasFollowerReachedDestination(
                        fleet,
                        follower,
                        step,
                        targetObjectId,
                        expectedSectorNumber))
                {
                    waitingFor = follower.LiveName;
                    break;
                }
            }

            if (waitingFor == null)
            {
                return NavigationAutoPilotEffectOutcome.Success();
            }

            publishStatus(
                step is { RequiresInteraction: true }
                    ? $"Waiting for {waitingFor} to finish docking."
                    : step == null
                        ? $"Waiting for {waitingFor} to enter the destination sector."
                        : $"Waiting for {waitingFor} to finish the final warp to {step.TargetName}.");

            await Task.Delay(pollInterval, cancellationToken)
                .ConfigureAwait(false);
        }

        return NavigationAutoPilotEffectOutcome.Failure(
            NavigationAutoPilotStopReason.ClientUnavailable,
            step is { RequiresInteraction: true }
                ? "Auto Pilot stopped because a managed follower did not finish docking at the destination."
                : step == null
                    ? "Auto Pilot stopped because a managed follower did not enter the destination sector."
                    : $"Auto Pilot stopped because a managed follower did not finish the final warp to {step.TargetName}.");
    }

    private bool HasFollowerReachedDestination(
        NavigationAutoPilotFleetContext fleet,
        NavigationAutoPilotFleetParticipant follower,
        NavigationAutoPilotStepPlan? step,
        uint? targetObjectId,
        uint expectedSectorNumber)
    {
        if (step is { RequiresInteraction: true })
        {
            return observationCoordinator.TryGetSnapshot(
                    follower.Client.ProcessId,
                    out var observation) &&
                observation.IsAvailable &&
                observation.LifecycleState ==
                    ClientLifecycleState.InGame &&
                observation.LoadingOrTransitionFlag == 0 &&
                observation.World.IsAvailable &&
                observation.World.Environment ==
                    ClientWorldEnvironment.Starbase;
        }

        if (!TryGetNavigation(
                follower,
                out var navigation,
                out _) ||
            !IsStableSpace(navigation) ||
            navigation.ActiveSectorNumber != expectedSectorNumber)
        {
            return false;
        }

        if (step == null)
        {
            return true;
        }

        return targetObjectId is > 0 &&
            navigation.SelectedTargetKnown &&
            navigation.HasSelectedTarget &&
            navigation.SelectedTargetObjectId ==
                targetObjectId.Value &&
            this.IsFollowerFullyFormedOnLeader(
                fleet,
                follower);
    }

    private bool IsFollowerFullyFormedOnLeader(
        NavigationAutoPilotFleetContext fleet,
        NavigationAutoPilotFleetParticipant follower)
    {
        return observationCoordinator.TryGetSnapshot(
                fleet.Leader.Client.ProcessId,
                out var leaderObservation) &&
            leaderObservation.Group.IsAvailable &&
            leaderObservation.Group.IsValid &&
            leaderObservation.Group.IsInGroup &&
            leaderObservation.Group.IsLeader &&
            this.FindLeaderGroupMember(
                leaderObservation.Group,
                follower,
                out var member) &&
            IsFullyFormed(member);
    }

    private bool TryAcquireParticipant(
        ClientInstance client,
        ClientHostForm hostForm,
        string liveName,
        int groupSlot,
        out NavigationAutoPilotFleetParticipant participant,
        out string error)
    {
        participant = null!;

        if (!observationCoordinator.TryBeginNavigationStateObservation(
                client.ProcessId,
                out var lease,
                out var observationError))
        {
            error =
                $"Auto Pilot could not start navigation observation for {liveName}: {observationError}";
            return false;
        }

        participant = new NavigationAutoPilotFleetParticipant(
            client,
            hostForm,
            liveName,
            groupSlot,
            lease);
        error = "";
        return true;
    }

    private ClientInstance? FindManagedGroupMember(
        ClientGroupMemberObservation member,
        IReadOnlyCollection<ClientInstance> managedClients,
        IReadOnlySet<int> excludedProcessIds)
    {
        foreach (var client in managedClients)
        {
            if (excludedProcessIds.Contains(client.ProcessId) ||
                !observationCoordinator.TryGetSnapshot(
                    client.ProcessId,
                    out var observation) ||
                !observation.IsAvailable)
            {
                continue;
            }

            var identity = ClientLiveCharacterIdentityResolver.Resolve(
                observation);
            var objectMatches = member.ObjectId is not 0 and not uint.MaxValue &&
                identity.CharacterObjectId == member.ObjectId;
            var nameMatches = !string.IsNullOrWhiteSpace(member.Name) &&
                !string.IsNullOrWhiteSpace(identity.Name) &&
                string.Equals(
                    member.Name.Trim(),
                    identity.Name.Trim(),
                    StringComparison.OrdinalIgnoreCase);

            if (objectMatches || nameMatches)
            {
                return client;
            }
        }

        return null;
    }

    private async Task<NavigationAutoPilotEffectOutcome>
        WaitForFleetInSameSpaceSectorAsync(
            NavigationAutoPilotFleetContext fleet,
            TimeSpan timeout,
            CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (TryGetNavigation(
                    fleet.Leader,
                    out var leader,
                    out _) &&
                IsStableSpace(leader) &&
                leader.ActiveSectorNumber != 0)
            {
                var allReady = true;

                foreach (var follower in fleet.Followers)
                {
                    if (!TryGetNavigation(
                            follower,
                            out var followerNavigation,
                            out _) ||
                        !IsStableSpace(followerNavigation) ||
                        followerNavigation.ActiveSectorNumber !=
                            leader.ActiveSectorNumber)
                    {
                        allReady = false;
                        break;
                    }
                }

                if (allReady && this.TryValidateGroupMembership(fleet))
                {
                    return NavigationAutoPilotEffectOutcome.Success();
                }
            }

            await Task.Delay(pollInterval, cancellationToken)
                .ConfigureAwait(false);
        }

        return NavigationAutoPilotEffectOutcome.Failure(
            NavigationAutoPilotStopReason.ClientUnavailable,
            "Auto Pilot stopped because all managed group members did not become ready in the same space sector.");
    }

    private bool TryObserveFormationState(
        NavigationAutoPilotFleetContext fleet,
        out bool blockFormationActive,
        out List<NavigationAutoPilotFleetParticipant>
            followersNeedingFormation,
        out string error)
    {
        blockFormationActive = false;
        followersNeedingFormation = [];
        error = "";

        if (!observationCoordinator.TryGetSnapshot(
                fleet.Leader.Client.ProcessId,
                out var leaderObservation) ||
            !leaderObservation.Group.IsAvailable ||
            !leaderObservation.Group.IsValid ||
            !leaderObservation.Group.IsInGroup ||
            !leaderObservation.Group.IsLeader)
        {
            error =
                "Auto Pilot could not observe the leader's current formation state.";
            return false;
        }

        blockFormationActive =
            leaderObservation.Group.FormationName.Contains(
                "Block",
                StringComparison.OrdinalIgnoreCase);

        foreach (var follower in fleet.Followers)
        {
            if (!this.FindLeaderGroupMember(
                    leaderObservation.Group,
                    follower,
                    out var member))
            {
                error =
                    $"Auto Pilot could not observe {follower.LiveName} in the leader's current group.";
                return false;
            }

            if (!IsFullyFormed(member))
            {
                followersNeedingFormation.Add(follower);
            }
        }

        return true;
    }

    private bool TryValidateGroupMembership(
        NavigationAutoPilotFleetContext fleet)
    {
        if (!observationCoordinator.TryGetSnapshot(
                fleet.Leader.Client.ProcessId,
                out var leaderObservation) ||
            !leaderObservation.Group.IsAvailable ||
            !leaderObservation.Group.IsValid ||
            !leaderObservation.Group.IsInGroup ||
            !leaderObservation.Group.IsLeader)
        {
            return false;
        }

        foreach (var follower in fleet.Followers)
        {
            if (!observationCoordinator.TryGetSnapshot(
                    follower.Client.ProcessId,
                    out var followerObservation) ||
                !followerObservation.Group.IsAvailable ||
                !followerObservation.Group.IsValid ||
                !followerObservation.Group.IsInGroup ||
                followerObservation.Group.IsLeader)
            {
                return false;
            }

            if (!this.FindLeaderGroupMember(
                    leaderObservation.Group,
                    follower,
                    out _))
            {
                return false;
            }
        }

        return true;
    }

    private async Task<NavigationAutoPilotEffectOutcome>
        SetBlockFormationAsync(
            NavigationAutoPilotFleetParticipant leader,
            CancellationToken cancellationToken)
    {
        if (!NativeMethods.TryGetClientSize(
                leader.Client.GameWindowHandle,
                out var clientSize) ||
            !ClientGameUiCoordinates.TryGetFormationMenu(
                clientSize,
                out var formationMenuPoint) ||
            !ClientGameUiCoordinates.TryGetFormationBlock(
                clientSize,
                out var formationBlockPoint) ||
            !NativeMethods.TryConvertClientPointToScreen(
                leader.Client.GameWindowHandle,
                formationMenuPoint,
                out var formationMenuScreenPoint) ||
            !NativeMethods.TryConvertClientPointToScreen(
                leader.Client.GameWindowHandle,
                formationBlockPoint,
                out var formationBlockScreenPoint))
        {
            return NavigationAutoPilotEffectOutcome.Failure(
                NavigationAutoPilotStopReason.ObservationUnavailable,
                "Auto Pilot could not resolve the Block formation controls in the leader viewport.");
        }

        using var foregroundLease =
            await foregroundInputCoordinator.AcquireAsync(
                    cancellationToken)
                .ConfigureAwait(false);

        var hasOriginalCursor =
            NativeMethods.TryGetCursorScreenPosition(
                out var originalCursorPosition);
        var commandedCursorPosition = Point.Empty;
        var ownsCursorPosition = false;

        await leader.HostForm
            .SetAddonOverlayInputSuppressedAsync(suppressed: true)
            .ConfigureAwait(false);

        try
        {
            NativeMethods.FocusWindow(
                leader.Client.GameWindowHandle);
            await Task.Delay(
                    TimeSpan.FromMilliseconds(50),
                    cancellationToken)
                .ConfigureAwait(false);

            if (!NativeMethods.MoveCursorToScreenPoint(
                    formationMenuScreenPoint))
            {
                return NavigationAutoPilotEffectOutcome.Failure(
                    NavigationAutoPilotStopReason.ObservationUnavailable,
                    "Auto Pilot could not move to the leader Formation control.");
            }

            commandedCursorPosition = formationMenuScreenPoint;
            ownsCursorPosition = true;
            await Task.Delay(
                    TimeSpan.FromMilliseconds(100),
                    cancellationToken)
                .ConfigureAwait(false);
            await Win32.NativeMethods
                .StableLeftClickAtCurrentCursorAsync(
                    cancellationToken)
                .ConfigureAwait(false);

            await Task.Delay(
                    formationMenuSettleDelay,
                    cancellationToken)
                .ConfigureAwait(false);

            if (!NativeMethods.MoveCursorToScreenPoint(
                    formationBlockScreenPoint))
            {
                return NavigationAutoPilotEffectOutcome.Failure(
                    NavigationAutoPilotStopReason.ObservationUnavailable,
                    "Auto Pilot could not move to the Block formation option.");
            }

            commandedCursorPosition = formationBlockScreenPoint;
            await Task.Delay(
                    TimeSpan.FromMilliseconds(100),
                    cancellationToken)
                .ConfigureAwait(false);
            await Win32.NativeMethods
                .StableLeftClickAtCurrentCursorAsync(
                    cancellationToken)
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

            await leader.HostForm
                .SetAddonOverlayInputSuppressedAsync(suppressed: false)
                .ConfigureAwait(false);
        }
    }

    private async Task<bool> WaitForBlockFormationAsync(
        int leaderProcessId,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow +
            formationSelectionTimeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (observationCoordinator.TryGetSnapshot(
                    leaderProcessId,
                    out var observation) &&
                observation.Group.IsAvailable &&
                observation.Group.IsValid &&
                observation.Group.IsInGroup &&
                observation.Group.IsLeader &&
                observation.Group.FormationName.Contains(
                    "Block",
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            await Task.Delay(pollInterval, cancellationToken)
                .ConfigureAwait(false);
        }

        return false;
    }

    private async Task<NavigationAutoPilotEffectOutcome>
        WaitForFollowersFormedAsync(
            NavigationAutoPilotFleetContext fleet,
            Action<string> publishStatus,
            CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow +
            formationJoinTimeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!observationCoordinator.TryGetSnapshot(
                    fleet.Leader.Client.ProcessId,
                    out var leaderObservation) ||
                !leaderObservation.Group.IsAvailable ||
                !leaderObservation.Group.IsValid ||
                !leaderObservation.Group.IsInGroup ||
                !leaderObservation.Group.IsLeader)
            {
                await Task.Delay(pollInterval, cancellationToken)
                    .ConfigureAwait(false);
                continue;
            }

            NavigationAutoPilotFleetParticipant? waiting = null;

            foreach (var follower in fleet.Followers)
            {
                if (!this.FindLeaderGroupMember(
                        leaderObservation.Group,
                        follower,
                        out var member) ||
                    !IsFullyFormed(member))
                {
                    waiting = follower;
                    break;
                }
            }

            if (waiting == null)
            {
                return NavigationAutoPilotEffectOutcome.Success();
            }

            publishStatus(
                $"Waiting for {waiting.LiveName} to settle into Block formation.");

            await Task.Delay(pollInterval, cancellationToken)
                .ConfigureAwait(false);
        }

        return NavigationAutoPilotEffectOutcome.Failure(
            NavigationAutoPilotStopReason.ClientUnavailable,
            "Auto Pilot stopped because not every managed follower fully formed on the leader." );
    }

    private async Task<NavigationAutoPilotEffectOutcome>
        EnsureFollowerTargetsAsync(
            NavigationAutoPilotFleetContext fleet,
            NavigationAutoPilotStepPlan step,
            uint targetObjectId,
            uint expectedSectorNumber,
            Action<string> publishStatus,
            CancellationToken cancellationToken)
    {
        if (!this.IsParticipantTargetReady(
                fleet.Leader,
                targetObjectId,
                expectedSectorNumber))
        {
            return NavigationAutoPilotEffectOutcome.Failure(
                NavigationAutoPilotStopReason.TargetSelectionFailed,
                $"Auto Pilot stopped because the leader no longer had the exact route target before fleet {step.VerbName} preparation.");
        }

        foreach (var follower in fleet.Followers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (this.IsParticipantTargetReady(
                    follower,
                    targetObjectId,
                    expectedSectorNumber))
            {
                continue;
            }

            if (TryGetNavigation(
                    follower,
                    out var followerNavigation,
                    out _) &&
                followerNavigation.ActiveSectorNumber ==
                    expectedSectorNumber &&
                followerNavigation.SelectedTargetKnown &&
                followerNavigation.HasSelectedTarget &&
                followerNavigation.SelectedTargetObjectId ==
                    targetObjectId)
            {
                var settled = await this.WaitForFollowerTargetReadyAsync(
                        fleet,
                        follower,
                        targetObjectId,
                        expectedSectorNumber,
                        followerNavigation.GenerationSequence,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (!settled.Succeeded)
                {
                    return settled;
                }

                continue;
            }

            if (!this.TryValidateFollowerMembership(
                    fleet,
                    follower))
            {
                return NavigationAutoPilotEffectOutcome.Failure(
                    NavigationAutoPilotStopReason.ClientUnavailable,
                    $"Auto Pilot stopped because {follower.LiveName} was no longer an observed member of the leader's group while recovering the route target.");
            }

            publishStatus(
                $"{follower.LiveName} missed the inherited route target; copying the leader's target.");

            var recovered = await this.CopyLeaderTargetAsync(
                    fleet,
                    follower,
                    targetObjectId,
                    expectedSectorNumber,
                    cancellationToken)
                .ConfigureAwait(false);

            if (!recovered.Succeeded)
            {
                return recovered;
            }

            publishStatus(
                $"{follower.LiveName} recovered the exact route target.");
        }

        return NavigationAutoPilotEffectOutcome.Success();
    }

    private async Task<NavigationAutoPilotEffectOutcome>
        CopyLeaderTargetAsync(
            NavigationAutoPilotFleetContext fleet,
            NavigationAutoPilotFleetParticipant follower,
            uint targetObjectId,
            uint expectedSectorNumber,
            CancellationToken cancellationToken)
    {
        if (!TryGetNavigation(
                follower,
                out var initialNavigation,
                out var navigationError))
        {
            return NavigationAutoPilotEffectOutcome.Failure(
                NavigationAutoPilotStopReason.ObservationUnavailable,
                navigationError);
        }

        var generationSequence = initialNavigation.GenerationSequence;

        if (!NativeMethods.TryGetClientSize(
                follower.Client.GameWindowHandle,
                out var clientSize) ||
            !ClientGameUiCoordinates.TryGetTargetGroupMemberTarget(
                clientSize,
                out var targetPoint) ||
            !NativeMethods.TryConvertClientPointToScreen(
                follower.Client.GameWindowHandle,
                targetPoint,
                out var targetScreenPoint))
        {
            return NavigationAutoPilotEffectOutcome.Failure(
                NavigationAutoPilotStopReason.TargetSelectionFailed,
                $"Auto Pilot could not resolve the leader-target control in {follower.LiveName}'s viewport.");
        }

        using var foregroundLease =
            await foregroundInputCoordinator.AcquireAsync(
                    cancellationToken)
                .ConfigureAwait(false);

        var hasOriginalCursor =
            NativeMethods.TryGetCursorScreenPosition(
                out var originalCursorPosition);
        var ownsCursorPosition = false;

        await follower.HostForm
            .SetAddonOverlayInputSuppressedAsync(suppressed: true)
            .ConfigureAwait(false);

        try
        {
            if (!this.TryValidateFollowerMembership(
                    fleet,
                    follower) ||
                !this.IsParticipantTargetReady(
                    fleet.Leader,
                    targetObjectId,
                    expectedSectorNumber))
            {
                return NavigationAutoPilotEffectOutcome.Failure(
                    NavigationAutoPilotStopReason.TargetSelectionFailed,
                    $"Auto Pilot stopped because fleet target context changed before {follower.LiveName} could copy the leader's target.");
            }

            if (!TryGetNavigation(
                    follower,
                    out var navigation,
                    out _) ||
                navigation.GenerationSequence != generationSequence ||
                navigation.ActiveSectorNumber != expectedSectorNumber ||
                !navigation.IsInteractionControlReady)
            {
                return NavigationAutoPilotEffectOutcome.Failure(
                    NavigationAutoPilotStopReason.ObservationUnavailable,
                    $"Auto Pilot stopped because current-generation navigation state was unavailable for {follower.LiveName} before target recovery.");
            }

            if (IsParticipantTargetReady(
                    navigation,
                    targetObjectId,
                    expectedSectorNumber))
            {
                return NavigationAutoPilotEffectOutcome.Success();
            }

            NativeMethods.FocusWindow(
                follower.Client.GameWindowHandle);
            await Task.Delay(
                    TimeSpan.FromMilliseconds(50),
                    cancellationToken)
                .ConfigureAwait(false);

            if (!NativeMethods.MoveCursorToScreenPoint(
                    targetScreenPoint))
            {
                return NavigationAutoPilotEffectOutcome.Failure(
                    NavigationAutoPilotStopReason.TargetSelectionFailed,
                    $"Auto Pilot could not move to the leader-target control for {follower.LiveName}.");
            }

            ownsCursorPosition = true;
            await Task.Delay(
                    TimeSpan.FromMilliseconds(100),
                    cancellationToken)
                .ConfigureAwait(false);

            if (!this.TryValidateFollowerMembership(
                    fleet,
                    follower) ||
                !this.IsParticipantTargetReady(
                    fleet.Leader,
                    targetObjectId,
                    expectedSectorNumber) ||
                !TryGetNavigation(
                    follower,
                    out navigation,
                    out _) ||
                navigation.GenerationSequence != generationSequence ||
                navigation.ActiveSectorNumber != expectedSectorNumber ||
                !navigation.IsInteractionControlReady)
            {
                return NavigationAutoPilotEffectOutcome.Failure(
                    NavigationAutoPilotStopReason.TargetSelectionFailed,
                    $"Auto Pilot stopped because fleet target context changed immediately before {follower.LiveName} copied the leader's target.");
            }

            if (!IsParticipantTargetReady(
                    navigation,
                    targetObjectId,
                    expectedSectorNumber))
            {
                await Win32.NativeMethods
                    .StableLeftClickAtCurrentCursorAsync(
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            if (hasOriginalCursor &&
                ownsCursorPosition &&
                CursorRemainsAt(targetScreenPoint))
            {
                _ = NativeMethods.MoveCursorToScreenPoint(
                    originalCursorPosition);
            }

            await follower.HostForm
                .SetAddonOverlayInputSuppressedAsync(suppressed: false)
                .ConfigureAwait(false);
        }

        return await this.WaitForFollowerTargetReadyAsync(
                fleet,
                follower,
                targetObjectId,
                expectedSectorNumber,
                generationSequence,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<NavigationAutoPilotEffectOutcome>
        WaitForFollowerTargetReadyAsync(
            NavigationAutoPilotFleetContext fleet,
            NavigationAutoPilotFleetParticipant follower,
            uint targetObjectId,
            uint expectedSectorNumber,
            long generationSequence,
            CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow +
            followerTargetRecoveryTimeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!this.TryValidateFollowerMembership(
                    fleet,
                    follower))
            {
                return NavigationAutoPilotEffectOutcome.Failure(
                    NavigationAutoPilotStopReason.ClientUnavailable,
                    $"Auto Pilot stopped because {follower.LiveName} left the observed group while recovering the route target.");
            }

            if (TryGetNavigation(
                    follower,
                    out var navigation,
                    out _))
            {
                if (navigation.GenerationSequence != generationSequence ||
                    navigation.ActiveSectorNumber != expectedSectorNumber)
                {
                    return NavigationAutoPilotEffectOutcome.Failure(
                        NavigationAutoPilotStopReason.ObservationUnavailable,
                        $"Auto Pilot stopped because {follower.LiveName}'s world changed while recovering the route target.");
                }

                if (IsParticipantTargetReady(
                        navigation,
                        targetObjectId,
                        expectedSectorNumber))
                {
                    return NavigationAutoPilotEffectOutcome.Success();
                }
            }

            await Task.Delay(pollInterval, cancellationToken)
                .ConfigureAwait(false);
        }

        return NavigationAutoPilotEffectOutcome.Failure(
            NavigationAutoPilotStopReason.TargetSelectionFailed,
            $"Auto Pilot could not make {follower.LiveName} acquire the leader's exact route target.");
    }

    private bool IsParticipantTargetReady(
        NavigationAutoPilotFleetParticipant participant,
        uint targetObjectId,
        uint expectedSectorNumber)
    {
        return TryGetNavigation(
                participant,
                out var navigation,
                out _) &&
            IsParticipantTargetReady(
                navigation,
                targetObjectId,
                expectedSectorNumber);
    }

    private static bool IsParticipantTargetReady(
        ClientNavigationStateObservation navigation,
        uint targetObjectId,
        uint expectedSectorNumber)
    {
        return navigation.ActiveSectorNumber == expectedSectorNumber &&
            navigation.IsInteractionControlReady &&
            navigation.SelectedTargetKnown &&
            navigation.HasSelectedTarget &&
            navigation.SelectedTargetObjectId == targetObjectId &&
            navigation.PathBuildStateKnown &&
            !navigation.PathBuildBusy;
    }

    private static bool IsParticipantTargetSelected(
        ClientNavigationStateObservation navigation,
        uint targetObjectId,
        uint expectedSectorNumber)
    {
        return navigation.ActiveSectorNumber == expectedSectorNumber &&
            navigation.SelectedTargetKnown &&
            navigation.HasSelectedTarget &&
            navigation.SelectedTargetObjectId == targetObjectId;
    }

    private async Task<NavigationAutoPilotEffectOutcome>
        WaitForFleetVerbSettleAsync(
            NavigationAutoPilotFleetContext fleet,
            NavigationAutoPilotStepPlan step,
            uint targetObjectId,
            uint expectedSectorNumber,
            Action<string> publishStatus,
            CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow +
            followerVerbReadyTimeout +
            targetVerbSettleDelay;
        DateTimeOffset? allReadySince = null;
        string? lastStatus = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            NavigationAutoPilotFleetParticipant? waiting = null;

            foreach (var participant in fleet.Participants)
            {
                if (!TryGetNavigation(
                        participant,
                        out var navigation,
                        out _) ||
                    !IsParticipantTargetSelected(
                        navigation,
                        targetObjectId,
                        expectedSectorNumber))
                {
                    waiting = participant;
                    break;
                }

                var verb = this.ReadVerbState(
                    participant.Client.ProcessId,
                    expectedSectorNumber,
                    targetObjectId,
                    step.Verb);

                if (verb == NavigationAutoPilotVerbState.Unavailable)
                {
                    return NavigationAutoPilotEffectOutcome.Failure(
                        step.UnavailableReason,
                        $"Auto Pilot stopped because {step.VerbName} is unavailable for {participant.LiveName}.");
                }

                if (verb != NavigationAutoPilotVerbState.Executable)
                {
                    waiting = participant;
                    break;
                }
            }

            if (waiting != null)
            {
                allReadySince = null;
                var status =
                    $"Waiting for {waiting.LiveName} to expose {step.VerbName} on the inherited route target.";

                if (!string.Equals(
                        lastStatus,
                        status,
                        StringComparison.Ordinal))
                {
                    publishStatus(status);
                    lastStatus = status;
                }
            }
            else
            {
                allReadySince ??= DateTimeOffset.UtcNow;
                var remaining =
                    targetVerbSettleDelay -
                    (DateTimeOffset.UtcNow - allReadySince.Value);

                if (remaining <= TimeSpan.Zero)
                {
                    return NavigationAutoPilotEffectOutcome.Success();
                }

                var status =
                    $"Allowing fleet {step.VerbName} controls to settle.";

                if (!string.Equals(
                        lastStatus,
                        status,
                        StringComparison.Ordinal))
                {
                    publishStatus(status);
                    lastStatus = status;
                }
            }

            await Task.Delay(pollInterval, cancellationToken)
                .ConfigureAwait(false);
        }

        return NavigationAutoPilotEffectOutcome.Failure(
            step.ActivationFailedReason,
            $"Auto Pilot stopped because the fleet did not keep executable {step.VerbName} ready for the shared settle interval.");
    }

    private async Task<NavigationAutoPilotEffectOutcome>
        ActivateFollowerVerbAsync(
            NavigationAutoPilotFleetParticipant follower,
            NavigationAutoPilotStepPlan step,
            uint targetObjectId,
            uint expectedSectorNumber,
            CancellationToken cancellationToken)
    {
        if (!TryGetNavigation(
                follower,
                out var initialNavigation,
                out var navigationError))
        {
            return NavigationAutoPilotEffectOutcome.Failure(
                NavigationAutoPilotStopReason.ObservationUnavailable,
                navigationError);
        }

        var generationSequence = initialNavigation.GenerationSequence;
        var acceptanceNavigationSequence = initialNavigation.Sequence;
        long? acceptanceObservationSequence = null;

        if (!NativeMethods.TryGetClientSize(
                follower.Client.GameWindowHandle,
                out var clientSize) ||
            !ClientGameUiCoordinates.TryGetPrimaryTargetVerb(
                clientSize,
                out var verbPoint) ||
            !NativeMethods.TryConvertClientPointToScreen(
                follower.Client.GameWindowHandle,
                verbPoint,
                out var verbScreenPoint))
        {
            return NavigationAutoPilotEffectOutcome.Failure(
                step.ActivationFailedReason,
                $"Auto Pilot could not resolve {step.VerbName} in {follower.LiveName}'s viewport.");
        }

        using var foregroundLease =
            await foregroundInputCoordinator.AcquireAsync(
                    cancellationToken)
                .ConfigureAwait(false);

        var hasOriginalCursor =
            NativeMethods.TryGetCursorScreenPosition(
                out var originalCursorPosition);
        var ownsCursorPosition = false;

        await follower.HostForm
            .SetAddonOverlayInputSuppressedAsync(suppressed: true)
            .ConfigureAwait(false);

        try
        {
            if (!this.ValidateFollowerInteraction(
                    follower,
                    step,
                    targetObjectId,
                    expectedSectorNumber,
                    generationSequence))
            {
                return NavigationAutoPilotEffectOutcome.Failure(
                    step.ActivationFailedReason,
                    $"Auto Pilot stopped because {follower.LiveName}'s inherited route target was no longer ready for {step.VerbName}.");
            }

            NativeMethods.FocusWindow(
                follower.Client.GameWindowHandle);
            await Task.Delay(
                    TimeSpan.FromMilliseconds(50),
                    cancellationToken)
                .ConfigureAwait(false);

            if (!NativeMethods.MoveCursorToScreenPoint(
                    verbScreenPoint))
            {
                return NavigationAutoPilotEffectOutcome.Failure(
                    step.ActivationFailedReason,
                    $"Auto Pilot could not move to {step.VerbName} for {follower.LiveName}.");
            }

            ownsCursorPosition = true;
            await Task.Delay(
                    TimeSpan.FromMilliseconds(100),
                    cancellationToken)
                .ConfigureAwait(false);

            if (!TryGetNavigation(
                    follower,
                    out var acceptanceNavigation,
                    out _) ||
                acceptanceNavigation.GenerationSequence !=
                    generationSequence ||
                !IsParticipantTargetSelected(
                    acceptanceNavigation,
                    targetObjectId,
                    expectedSectorNumber) ||
                this.ReadVerbState(
                    follower.Client.ProcessId,
                    expectedSectorNumber,
                    targetObjectId,
                    step.Verb) !=
                    NavigationAutoPilotVerbState.Executable)
            {
                return NavigationAutoPilotEffectOutcome.Failure(
                    step.ActivationFailedReason,
                    $"Auto Pilot stopped because {step.VerbName} stopped being executable for {follower.LiveName} immediately before activation.");
            }

            acceptanceNavigationSequence =
                acceptanceNavigation.Sequence;

            if (observationCoordinator.TryGetSnapshot(
                    follower.Client.ProcessId,
                    out var acceptanceObservation))
            {
                acceptanceObservationSequence =
                    acceptanceObservation.Sequence;
            }

            await Win32.NativeMethods
                .StableLeftClickAtCurrentCursorAsync(
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            if (hasOriginalCursor &&
                ownsCursorPosition &&
                CursorRemainsAt(verbScreenPoint))
            {
                _ = NativeMethods.MoveCursorToScreenPoint(
                    originalCursorPosition);
            }

            await follower.HostForm
                .SetAddonOverlayInputSuppressedAsync(suppressed: false)
                .ConfigureAwait(false);
        }

        return await this.WaitForFollowerInteractionAcceptanceAsync(
                follower,
                step,
                targetObjectId,
                expectedSectorNumber,
                acceptanceNavigationSequence,
                acceptanceObservationSequence,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private bool ValidateFollowerInteraction(
        NavigationAutoPilotFleetParticipant follower,
        NavigationAutoPilotStepPlan step,
        uint targetObjectId,
        uint expectedSectorNumber,
        long generationSequence)
    {
        return TryGetNavigation(
                follower,
                out var navigation,
                out _) &&
            navigation.GenerationSequence == generationSequence &&
            IsParticipantTargetSelected(
                navigation,
                targetObjectId,
                expectedSectorNumber) &&
            this.ReadVerbState(
                follower.Client.ProcessId,
                expectedSectorNumber,
                targetObjectId,
                step.Verb) == NavigationAutoPilotVerbState.Executable;
    }

    private async Task<NavigationAutoPilotEffectOutcome>
        WaitForFollowerInteractionAcceptanceAsync(
            NavigationAutoPilotFleetParticipant follower,
            NavigationAutoPilotStepPlan step,
            uint targetObjectId,
            uint expectedSectorNumber,
            long acceptanceNavigationSequence,
            long? acceptanceObservationSequence,
            CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow +
            followerTransitionAcceptanceTimeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var hasObservationSnapshot =
                observationCoordinator.TryGetSnapshot(
                    follower.Client.ProcessId,
                    out var observation);
            var hasObservation =
                hasObservationSnapshot &&
                acceptanceObservationSequence.HasValue &&
                observation.Sequence >
                    acceptanceObservationSequence.Value;
            var hasNavigation = observationCoordinator
                .TryGetNavigationStateObservation(
                    follower.Client.ProcessId,
                    out var navigation) &&
                navigation.Sequence > acceptanceNavigationSequence;

            var navigationTransition = hasNavigation &&
                (!navigation.IsWorldPresent || navigation.IsLoading);
            var sectorChanged = hasNavigation &&
                navigation.ActiveSectorNumber != 0 &&
                navigation.ActiveSectorNumber != expectedSectorNumber;
            var observationTransition = hasObservation &&
                observation.LoadingOrTransitionFlag != 0;

            if (step.IsSectorTransition)
            {
                if ((hasNavigation &&
                     navigation.IsGateTransitionLocked) ||
                    navigationTransition ||
                    observationTransition ||
                    sectorChanged)
                {
                    return NavigationAutoPilotEffectOutcome.Success();
                }
            }
            else if (navigationTransition ||
                     observationTransition ||
                     sectorChanged ||
                     (hasObservation &&
                      (observation.DockingTargetObjectId == targetObjectId ||
                       observation.PendingLandOrDockTargetObjectId ==
                           targetObjectId ||
                       observation.World.Environment !=
                           ClientWorldEnvironment.Space)))
            {
                return NavigationAutoPilotEffectOutcome.Success();
            }

            await Task.Delay(pollInterval, cancellationToken)
                .ConfigureAwait(false);
        }

        return NavigationAutoPilotEffectOutcome.Failure(
            step.ActivationFailedReason,
            $"Auto Pilot clicked {step.VerbName} for {follower.LiveName}, but the client did not accept the interaction.");
    }

    private NavigationAutoPilotVerbState ReadVerbState(
        int processId,
        uint expectedSectorId,
        uint targetObjectId,
        ClientTargetVerb targetVerb)
    {
        if (!observationCoordinator.TryReadCurrentTargetInteraction(
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

    private bool TryValidateFollowerMembership(
        NavigationAutoPilotFleetContext fleet,
        NavigationAutoPilotFleetParticipant follower)
    {
        if (!observationCoordinator.TryGetSnapshot(
                fleet.Leader.Client.ProcessId,
                out var leaderObservation) ||
            !leaderObservation.Group.IsAvailable ||
            !leaderObservation.Group.IsValid ||
            !leaderObservation.Group.IsInGroup ||
            !leaderObservation.Group.IsLeader ||
            !observationCoordinator.TryGetSnapshot(
                follower.Client.ProcessId,
                out var followerObservation) ||
            !followerObservation.Group.IsAvailable ||
            !followerObservation.Group.IsValid ||
            !followerObservation.Group.IsInGroup ||
            followerObservation.Group.IsLeader)
        {
            return false;
        }

        return this.FindLeaderGroupMember(
            leaderObservation.Group,
            follower,
            out _);
    }

    private bool FindLeaderGroupMember(
        ClientGroupObservation group,
        NavigationAutoPilotFleetParticipant follower,
        out ClientGroupMemberObservation member)
    {
        uint? liveObjectId = null;

        if (observationCoordinator.TryGetSnapshot(
                follower.Client.ProcessId,
                out var observation) &&
            observation.IsAvailable)
        {
            liveObjectId = ClientLiveCharacterIdentityResolver
                .Resolve(observation)
                .CharacterObjectId;
        }

        member = group.Members.FirstOrDefault(candidate =>
            candidate.IsPresent &&
            ((liveObjectId.HasValue &&
              candidate.ObjectId == liveObjectId.Value) ||
             (!string.IsNullOrWhiteSpace(candidate.Name) &&
              string.Equals(
                  candidate.Name.Trim(),
                  follower.LiveName,
                  StringComparison.OrdinalIgnoreCase))))!;

        return member != null;
    }

    private static bool IsFullyFormed(
        ClientGroupMemberObservation member)
    {
        return member.IsObjectResolved &&
            member.FormationPosition >= 0 &&
            member.Distance.IsAvailable &&
            member.Distance.Target.IsAvailable &&
            member.Distance.Target.StateKind ==
                ClientSpatialStateKind.ParentRelative;
    }

    private static bool IsStableSpace(
        ClientNavigationStateObservation navigation)
    {
        return navigation.IsAvailable &&
            navigation.RequiredPropertiesAvailable &&
            navigation.IsWorldPresent &&
            !navigation.IsLoading &&
            navigation.Environment == ClientWorldEnvironment.Space &&
            navigation.PrivateWarpState is
            {
                IsAvailable: true,
                Value: 0 or 3,
            } &&
            navigation.GlobalWarpState is
            {
                IsAvailable: true,
                Value: 0 or 3,
            } &&
            !navigation.IsGateTransitionLocked;
    }

    private bool TryGetNavigation(
        NavigationAutoPilotFleetParticipant participant,
        out ClientNavigationStateObservation navigation,
        out string error)
    {
        if (!IsClientAvailable(participant) ||
            !observationCoordinator.TryGetNavigationStateObservation(
                participant.Client.ProcessId,
                out navigation) ||
            navigation.Sequence == 0 ||
            !navigation.IsAvailable ||
            !navigation.RequiredPropertiesAvailable)
        {
            navigation = null!;
            error =
                $"Auto Pilot could not observe current navigation state for {participant.LiveName}.";
            return false;
        }

        error = "";
        return true;
    }

    private static bool IsClientAvailable(
        NavigationAutoPilotFleetParticipant participant)
    {
        return participant.Client.GameWindowHandle != IntPtr.Zero &&
            !participant.HostForm.IsDisposed &&
            !participant.HostForm.Disposing;
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

    private sealed record FollowerSpec(
        ClientInstance Client,
        ClientHostForm HostForm,
        string LiveName,
        int GroupSlot);
}
