// ReSharper disable IdentifierTypo
// ReSharper disable CommentTypo
// ReSharper disable StringLiteralTypo
namespace Net7ClientManager.Services;

using System.Globalization;
using Net7ClientManager.Forms;
using Net7ClientManager.Models;
using Net7ClientManager.Navigation;
using Net7ClientManager.Observations;
using Net7ClientManager.Observations.Models;

/// <summary>
/// User-invoked route-target selection through the player's configured game
/// bindings. Conventional navigation targets start with Target Near Navigation;
/// static world-object targets start with Target Nearest Object. Both paths then
/// use Prev Context Target one verified step at a time. Success requires the live
/// native target identity to match the planned route target exactly.
/// </summary>
internal sealed class NavigationTargetSelectionService(
    ClientObservationCoordinator observationCoordinator,
    NavigationRouteCoordinator routeCoordinator,
    GameCommandCoordinator gameCommandCoordinator)
{
    private static readonly TimeSpan targetAcquireTimeout =
        TimeSpan.FromMilliseconds(1200);

    private static readonly TimeSpan targetChangeTimeout =
        TimeSpan.FromMilliseconds(1200);

    private static readonly TimeSpan pollInterval =
        TimeSpan.FromMilliseconds(20);

    private static readonly TimeSpan commandSettleDelay =
        TimeSpan.FromMilliseconds(35);

    private static readonly TimeSpan targetIdentitySettleDuration =
        TimeSpan.FromMilliseconds(80);

    public async Task<NavigationTargetSelectionResult> SelectNextTargetAsync(
        ClientInstance client,
        ClientHostForm hostForm,
        uint? expectedSectorId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(hostForm);

        if (client.GameWindowHandle == IntPtr.Zero ||
            hostForm.IsDisposed ||
            hostForm.Disposing)
        {
            return NavigationTargetSelectionResult.Failure(
                "The hosted game client is unavailable.");
        }

        if (!this.TryValidateSpaceContext(
                client.ProcessId,
                expectedSectorId,
                out var contextError))
        {
            return NavigationTargetSelectionResult.Failure(
                contextError,
                isTransientFailure: true);
        }

        var initialResolution = this.ResolveNextTarget(
            client.ProcessId);

        if (!initialResolution.IsReady)
        {
            return NavigationTargetSelectionResult.Failure(
                initialResolution.Detail,
                isTransientFailure:
                    initialResolution.Status ==
                    NavigationTargetResolutionStatus.Waiting);
        }

        var initialNearestCommand = initialResolution.SelectionContext ==
            GalaxyNavigationTargetSelectionContext.Navigation
                ? GameCommand.TargetNearestNavigation
                : GameCommand.TargetNearestObject;
        var sessionResult = await gameCommandCoordinator
            .BeginSessionAsync(
                client,
                [
                    initialNearestCommand,
                    GameCommand.PreviousContextTarget,
                ],
                cancellationToken)
            .ConfigureAwait(false);

        var session = sessionResult.Session;

        if (session == null)
        {
            return NavigationTargetSelectionResult.Failure(
                sessionResult.Error);
        }

        using (session)
        {
            if (!this.TryValidateSpaceContext(
                    client.ProcessId,
                    expectedSectorId,
                    out contextError))
            {
                return NavigationTargetSelectionResult.Failure(
                    contextError,
                    isTransientFailure: true);
            }

            var resolution = this.ResolveNextTarget(
                client.ProcessId);

            if (!resolution.IsReady)
            {
                return NavigationTargetSelectionResult.Failure(
                    resolution.Detail,
                    isTransientFailure:
                        resolution.Status ==
                        NavigationTargetResolutionStatus.Waiting);
            }

            if (resolution.SelectionContext !=
                initialResolution.SelectionContext)
            {
                return NavigationTargetSelectionResult.Failure(
                    "The planned route target changed while its game bindings were being prepared.",
                    isTransientFailure: true);
            }

            var expected = new ExpectedNavigationTarget(
                resolution.ObjectId,
                resolution.ActiveSectorNumber,
                resolution.TargetName,
                resolution.TargetKind,
                resolution.SelectionContext);

            if (!this.TryGetTargetSelectionState(
                    client.ProcessId,
                    expected.ObjectId,
                    expected.SelectionContext,
                    out var selectableTargetIds,
                    out var maximumCycleTargetCount,
                    out var nearestCommand,
                    out var stateError))
            {
                return NavigationTargetSelectionResult.Failure(
                    stateError,
                    isTransientFailure: true);
            }

            if (nearestCommand != initialNearestCommand)
            {
                return NavigationTargetSelectionResult.Failure(
                    "The planned route target changed while its game bindings were being prepared.",
                    isTransientFailure: true);
            }

            if (!selectableTargetIds.Contains(
                    expected.ObjectId))
            {
                return NavigationTargetSelectionResult.Failure(
                    $"The planned route target '{expected.DisplayName}' is not present in the live target set.",
                    isTransientFailure: true);
            }

            if (!this.TryReadCurrentTarget(
                    client.ProcessId,
                    expected.ActiveSectorNumber,
                    out var selectedObjectId,
                    out stateError))
            {
                return NavigationTargetSelectionResult.Failure(
                    stateError,
                    isTransientFailure: true);
            }

            List<uint> observedObjectIds = [];

            if (selectedObjectId == expected.ObjectId)
            {
                observedObjectIds.Add(selectedObjectId);

                return CreateSuccess(
                    expected,
                    direction: "already selected",
                    commandCount: 0,
                    observedObjectIds);
            }

            var nearestResult = await session
                .ExecuteAsync(
                    nearestCommand,
                    cancellationToken)
                .ConfigureAwait(false);

            if (!nearestResult.Succeeded)
            {
                return NavigationTargetSelectionResult.Failure(
                    nearestResult.Error);
            }

            var acquireResult =
                await this.WaitForNavigationTargetAsync(
                        client.ProcessId,
                        expected.ActiveSectorNumber,
                        selectableTargetIds,
                        requireKnownTargetId:
                            expected.SelectionContext ==
                            GalaxyNavigationTargetSelectionContext.Navigation,
                        cancellationToken)
                    .ConfigureAwait(false);

            if (!acquireResult.Succeeded)
            {
                return NavigationTargetSelectionResult.Failure(
                    acquireResult.Error);
            }

            selectedObjectId = acquireResult.SelectedObjectId;
            observedObjectIds.Add(selectedObjectId);

            if (selectedObjectId == expected.ObjectId)
            {
                return CreateSuccess(
                    expected,
                    direction: expected.SelectionContext ==
                        GalaxyNavigationTargetSelectionContext.Navigation
                            ? "nearest navigation"
                            : "nearest object",
                    commandCount: 1,
                    observedObjectIds);
            }

            HashSet<uint> seenObjectIds = [selectedObjectId];
            var objectContextMayNeedPriming =
                expected.SelectionContext ==
                GalaxyNavigationTargetSelectionContext.Object;
            var maximumPreviousCommands = Math.Max(
                1,
                maximumCycleTargetCount +
                (objectContextMayNeedPriming ? 1 : 0));

            for (var commandIndex = 0;
                 commandIndex < maximumPreviousCommands;
                 commandIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var previousSelectedObjectId = selectedObjectId;
                var previousResult = await session
                    .ExecuteAsync(
                        GameCommand.PreviousContextTarget,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (!previousResult.Succeeded)
                {
                    return NavigationTargetSelectionResult.Failure(
                        previousResult.Error);
                }

                var changeResult =
                    await this.WaitForNavigationTargetChangeAsync(
                            client.ProcessId,
                            expected.ActiveSectorNumber,
                            previousSelectedObjectId,
                            selectableTargetIds,
                            requireKnownTargetId:
                                expected.SelectionContext ==
                                GalaxyNavigationTargetSelectionContext.Navigation,
                            allowUnchangedAtTimeout:
                                objectContextMayNeedPriming &&
                                commandIndex == 0,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                if (!changeResult.Succeeded)
                {
                    return NavigationTargetSelectionResult.Failure(
                        changeResult.Error);
                }

                selectedObjectId =
                    changeResult.SelectedObjectId;
                var targetAdvanced = selectedObjectId !=
                                     previousSelectedObjectId;

                observedObjectIds.Add(selectedObjectId);

                var latestResolution = this.ResolveNextTarget(
                    client.ProcessId);

                if (!latestResolution.IsReady)
                {
                    return NavigationTargetSelectionResult.Failure(
                        latestResolution.Detail,
                        isTransientFailure:
                            latestResolution.Status ==
                            NavigationTargetResolutionStatus.Waiting);
                }

                var latestExpected = new ExpectedNavigationTarget(
                    latestResolution.ObjectId,
                    latestResolution.ActiveSectorNumber,
                    latestResolution.TargetName,
                    latestResolution.TargetKind,
                    latestResolution.SelectionContext);

                if (latestExpected.ObjectId !=
                    expected.ObjectId)
                {
                    return NavigationTargetSelectionResult.Failure(
                        "The planned next route target changed during key-based target selection.");
                }

                if (selectedObjectId ==
                    expected.ObjectId)
                {
                    return CreateSuccess(
                        expected,
                        direction: expected.SelectionContext ==
                            GalaxyNavigationTargetSelectionContext.Navigation
                                ? "nearest navigation, then previous"
                                : "nearest object, then previous",
                        commandCount: commandIndex + 2,
                        observedObjectIds);
                }

                if (!targetAdvanced)
                {
                    // Target Nearest Object can establish the native object
                    // context only after the first context-cycle command. The
                    // game may keep the same live target for that priming
                    // command; the next bounded command performs the first
                    // actual move through the context ring.
                    continue;
                }

                if (expected.SelectionContext ==
                        GalaxyNavigationTargetSelectionContext.Navigation &&
                    !seenObjectIds.Add(selectedObjectId))
                {
                    return NavigationTargetSelectionResult.Failure(
                        $"The navigation target cycle repeated before reaching '{expected.DisplayName}'.");
                }

                if (expected.SelectionContext ==
                    GalaxyNavigationTargetSelectionContext.Object)
                {
                    // The native object-context ring can briefly revisit an
                    // earlier TargetGameID while its visible target continues
                    // settling to another object. A repeated ID is therefore
                    // not reliable proof that this ring has wrapped. The
                    // object path remains bounded by the live target count and
                    // still succeeds only on the exact expected ObjectId.
                    seenObjectIds.Add(selectedObjectId);
                }
            }

            return NavigationTargetSelectionResult.Failure(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The target cycle exhausted {maximumPreviousCommands + 1} bounded key command(s) without selecting '{expected.DisplayName}'."));
        }
    }

    private async Task<TargetWaitResult> WaitForNavigationTargetAsync(
        int processId,
        uint expectedSectorId,
        IReadOnlySet<uint> selectableTargetIds,
        bool requireKnownTargetId,
        CancellationToken cancellationToken)
    {
        await Task.Delay(
                commandSettleDelay,
                cancellationToken)
            .ConfigureAwait(false);

        var deadline =
            DateTimeOffset.UtcNow +
            targetAcquireTimeout;
        var candidateObjectId = 0u;
        var candidateObservedAt = DateTimeOffset.MinValue;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!this.TryValidateSpaceContext(
                    processId,
                    expectedSectorId,
                    out var contextError))
            {
                return TargetWaitResult.Failure(
                    contextError);
            }

            if (!this.TryReadCurrentTarget(
                    processId,
                    expectedSectorId,
                    out var selectedObjectId,
                    out var targetError))
            {
                return TargetWaitResult.Failure(
                    targetError);
            }

            var observedAt = DateTimeOffset.UtcNow;

            if (selectedObjectId != 0 &&
                (!requireKnownTargetId ||
                 selectableTargetIds.Contains(selectedObjectId)))
            {
                if (candidateObjectId != selectedObjectId)
                {
                    candidateObjectId = selectedObjectId;
                    candidateObservedAt = observedAt;
                }
                else if (observedAt - candidateObservedAt >=
                         targetIdentitySettleDuration)
                {
                    return TargetWaitResult.Success(
                        selectedObjectId);
                }
            }
            else
            {
                candidateObjectId = 0;
                candidateObservedAt = DateTimeOffset.MinValue;
            }

            await Task.Delay(
                    pollInterval,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return TargetWaitResult.Failure(
            "The nearest-target command did not select a live target.");
    }

    private async Task<TargetWaitResult>
        WaitForNavigationTargetChangeAsync(
            int processId,
            uint expectedSectorId,
            uint previousObjectId,
            IReadOnlySet<uint> selectableTargetIds,
            bool requireKnownTargetId,
            bool allowUnchangedAtTimeout,
            CancellationToken cancellationToken)
    {
        await Task.Delay(
                commandSettleDelay,
                cancellationToken)
            .ConfigureAwait(false);

        var deadline =
            DateTimeOffset.UtcNow +
            targetChangeTimeout;
        var previousTargetRemainedSelected = false;
        var candidateObjectId = 0u;
        var candidateObservedAt = DateTimeOffset.MinValue;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!this.TryValidateSpaceContext(
                    processId,
                    expectedSectorId,
                    out var contextError))
            {
                return TargetWaitResult.Failure(
                    contextError);
            }

            if (!this.TryReadCurrentTarget(
                    processId,
                    expectedSectorId,
                    out var selectedObjectId,
                    out var targetError))
            {
                return TargetWaitResult.Failure(
                    targetError);
            }

            var observedAt = DateTimeOffset.UtcNow;

            if (selectedObjectId != 0 &&
                (!requireKnownTargetId ||
                 selectableTargetIds.Contains(selectedObjectId)))
            {
                if (selectedObjectId == previousObjectId)
                {
                    previousTargetRemainedSelected = true;
                    candidateObjectId = 0;
                    candidateObservedAt = DateTimeOffset.MinValue;
                }
                else if (candidateObjectId != selectedObjectId)
                {
                    candidateObjectId = selectedObjectId;
                    candidateObservedAt = observedAt;
                }
                else if (observedAt - candidateObservedAt >=
                         targetIdentitySettleDuration)
                {
                    return TargetWaitResult.Success(
                        selectedObjectId);
                }
            }
            else
            {
                candidateObjectId = 0;
                candidateObservedAt = DateTimeOffset.MinValue;
            }

            await Task.Delay(
                    pollInterval,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (allowUnchangedAtTimeout &&
            previousTargetRemainedSelected)
        {
            return TargetWaitResult.Success(
                previousObjectId);
        }

        return TargetWaitResult.Failure(
            requireKnownTargetId
                ? "Target cycling did not advance to another live navigation target."
                : "Target cycling did not advance to another target.");
    }

    private bool TryReadCurrentTarget(
        int processId,
        uint expectedSectorId,
        out uint objectId,
        out string error)
    {
        objectId = 0;

        if (!observationCoordinator
                .TryReadCurrentTargetObjectId(
                    processId,
                    expectedSectorId,
                    out var hasTarget,
                    out var currentObjectId,
                    out error))
        {
            return false;
        }

        if (hasTarget)
        {
            objectId = currentObjectId;
        }

        return true;
    }

    private bool TryGetTargetSelectionState(
        int processId,
        uint expectedTargetObjectId,
        GalaxyNavigationTargetSelectionContext selectionContext,
        out HashSet<uint> selectableTargetIds,
        out int maximumCycleTargetCount,
        out GameCommand nearestCommand,
        out string error)
    {
        selectableTargetIds = [];
        maximumCycleTargetCount = 0;
        nearestCommand = selectionContext ==
            GalaxyNavigationTargetSelectionContext.Navigation
                ? GameCommand.TargetNearestNavigation
                : GameCommand.TargetNearestObject;
        error = "";

        if (!observationCoordinator.TryGetSnapshot(
                processId,
                out var snapshot) ||
            !snapshot.IsAvailable)
        {
            error =
                "Live game observation is unavailable for the selected client.";

            return false;
        }

        if (selectionContext ==
            GalaxyNavigationTargetSelectionContext.Object)
        {
            if (!snapshot.NearbyTargets.IsAvailable ||
                snapshot.NearbyTargets.ActiveSectorNumber !=
                    snapshot.World.ActiveSectorNumber)
            {
                error =
                    "Live object targets are not ready in the current sector.";
                return false;
            }

            selectableTargetIds = [.. snapshot.NearbyTargets.Targets
                .Where(target =>
                    target.IsAvailable &&
                    target.ObjectId != 0)
                .Select(target => target.ObjectId)
                .Distinct()];

            if (selectableTargetIds.Count == 0)
            {
                error =
                    "The current sector exposes no live object targets.";
                return false;
            }

            // The native object-context ring is broader than the current
            // GutterData presentation map. It can traverse conventional
            // NavigationData targets and objects that temporarily leave the
            // gutter map while selected. Keep the command loop bounded, but
            // size it from both observed rosters plus a reconciliation margin.
            var navigationTargetCount = snapshot.Navigation.IsAvailable &&
                (snapshot.Navigation.ActiveSectorNumber == 0 ||
                 snapshot.World.ActiveSectorNumber == 0 ||
                 snapshot.Navigation.ActiveSectorNumber ==
                    snapshot.World.ActiveSectorNumber)
                    ? snapshot.Navigation.Targets.Count(target =>
                        target.IsAvailable &&
                        target.ObjectId != 0)
                    : 0;
            maximumCycleTargetCount = Math.Clamp(
                snapshot.NearbyTargets.Targets.Count +
                navigationTargetCount +
                16,
                16,
                256);

            return true;
        }

        var navigation = snapshot.Navigation;
        var publishedTargetIds = navigation.IsAvailable &&
            (navigation.ActiveSectorNumber == 0 ||
             snapshot.World.ActiveSectorNumber == 0 ||
             navigation.ActiveSectorNumber ==
                snapshot.World.ActiveSectorNumber)
            ? navigation.Targets
                .Where(target =>
                    target.IsAvailable &&
                    target.ObjectId != 0)
                .Select(target => target.ObjectId)
                .Distinct()
                .ToHashSet()
            : new HashSet<uint>();

        if (publishedTargetIds.Count == 0 ||
            !publishedTargetIds.Contains(
                expectedTargetObjectId))
        {
            if (!snapshot.World.IsAvailable ||
                snapshot.World.ActiveSectorNumber == 0 ||
                !observationCoordinator.TryReadNavigation(
                    processId,
                    snapshot.World.ActiveSectorNumber,
                    out navigation,
                    out _))
            {
                error =
                    "Live navigation targets are not ready in the current sector.";

                return false;
            }
        }

        selectableTargetIds = [.. navigation.Targets
            .Where(target =>
                target.IsAvailable &&
                target.ObjectId != 0)
            .Select(target => target.ObjectId)
            .Distinct()];

        if (selectableTargetIds.Count == 0)
        {
            error =
                "The current sector exposes no live navigation targets.";

            return false;
        }

        maximumCycleTargetCount = selectableTargetIds.Count;
        return true;
    }

    private bool TryValidateSpaceContext(
        int processId,
        uint? expectedSectorId,
        out string error)
    {
        error = "";

        if (!observationCoordinator.TryGetSnapshot(
                processId,
                out var snapshot) ||
            !snapshot.IsAvailable)
        {
            error =
                "Live game observation is unavailable for the selected client.";

            return false;
        }

        if (snapshot.LifecycleState !=
            ClientLifecycleState.InGame)
        {
            error = $"Navigation target selection requires an in-game client; observed {snapshot.LifecycleState}.";

            return false;
        }

        if (snapshot.LoadingOrTransitionFlag != 0)
        {
            error =
                "Navigation target selection is unavailable while the client is transitioning.";

            return false;
        }

        if (!snapshot.World.IsAvailable)
        {
            error = string.IsNullOrWhiteSpace(
                    snapshot.World.Status)
                ? "Live world state is unavailable."
                : snapshot.World.Status;

            return false;
        }

        if (snapshot.World.Environment !=
            ClientWorldEnvironment.Space)
        {
            error = $"Navigation target selection is available only in space; observed {snapshot.World.Environment}.";

            return false;
        }

        if (expectedSectorId.HasValue &&
            expectedSectorId.Value != 0 &&
            snapshot.World.ActiveSectorNumber !=
            expectedSectorId.Value)
        {
            error = "The client changed sector after the addon gesture.";

            return false;
        }

        return true;
    }

    internal NavigationTargetResolutionResult ResolveNextTarget(
        int processId)
    {
        var routeSnapshot =
            routeCoordinator.GetSnapshot(processId);

        var nextStep = routeSnapshot.Route?.NextStep;

        if (!routeSnapshot.IsAvailable ||
            routeSnapshot.Route == null)
        {
            return NavigationTargetResolutionResult.Waiting(
                string.IsNullOrWhiteSpace(
                    routeSnapshot.StatusText)
                    ? "The active route is not observable yet."
                    : routeSnapshot.StatusText);
        }

        if (nextStep == null)
        {
            return NavigationTargetResolutionResult.Invalid(
                "The current route has no remaining navigation target.");
        }

        string expectedName;
        string publicKind;
        byte expectedRawObjectType;
        GalaxyNavigationTargetSelectionContext expectedSelectionContext;
        bool hasExpectedPosition;
        float expectedX;
        float expectedY;
        float expectedZ;

        if (nextStep.Kind ==
            NavigationRouteStepKind.SectorTransition)
        {
            if (string.IsNullOrWhiteSpace(
                    nextStep.DepartureTargetName) ||
                !nextStep.DepartureTargetRawObjectType.HasValue)
            {
                return NavigationTargetResolutionResult.Invalid(
                    "The next sector transition is missing required route-target metadata.");
            }

            expectedName = nextStep.DepartureTargetName;
            expectedRawObjectType =
                nextStep.DepartureTargetRawObjectType.Value;
            expectedSelectionContext =
                nextStep.DepartureTargetSelectionContext;
            publicKind = string.IsNullOrWhiteSpace(
                    nextStep.DepartureTargetType)
                ? GalaxyNavigationTargetKinds
                    .FromRawObjectType(expectedRawObjectType)
                    .ToPublicName()
                : nextStep.DepartureTargetType;
            hasExpectedPosition =
                nextStep.HasDepartureTargetPosition;
            expectedX = nextStep.DepartureTargetX;
            expectedY = nextStep.DepartureTargetY;
            expectedZ = nextStep.DepartureTargetZ;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(
                    nextStep.FinalTargetName) ||
                !nextStep.FinalTargetRawObjectType.HasValue)
            {
                return NavigationTargetResolutionResult.Invalid(
                    "The final route target is missing required target metadata.");
            }

            expectedName = nextStep.FinalTargetName;
            expectedRawObjectType =
                nextStep.FinalTargetRawObjectType.Value;
            expectedSelectionContext =
                nextStep.FinalTargetSelectionContext;
            publicKind = string.IsNullOrWhiteSpace(
                    nextStep.FinalTargetType)
                ? GalaxyNavigationTargetKinds
                    .FromRawObjectType(expectedRawObjectType)
                    .ToPublicName()
                : nextStep.FinalTargetType;
            hasExpectedPosition =
                nextStep.HasFinalTargetPosition;
            expectedX = nextStep.FinalTargetX;
            expectedY = nextStep.FinalTargetY;
            expectedZ = nextStep.FinalTargetZ;
        }

        if (!observationCoordinator.TryGetSnapshot(
                processId,
                out var observation) ||
            !observation.IsAvailable ||
            observation.LifecycleState !=
                ClientLifecycleState.InGame ||
            observation.LoadingOrTransitionFlag != 0 ||
            !observation.World.IsAvailable ||
            observation.World.Environment !=
                ClientWorldEnvironment.Space ||
            observation.World.ActiveSectorNumber == 0)
        {
            return NavigationTargetResolutionResult.Waiting(
                $"Live navigation has not published route target {expectedName} yet.",
                expectedName,
                "snapshot");
        }

        if (expectedSelectionContext ==
            GalaxyNavigationTargetSelectionContext.Object)
        {
            return ResolveTargetFromGutter(
                observation.NearbyTargets,
                observation.World.ActiveSectorNumber,
                expectedName,
                publicKind,
                expectedRawObjectType,
                hasExpectedPosition,
                expectedX,
                expectedY,
                expectedZ,
                source: "published");
        }

        var published = ResolveTargetFromNavigation(
            observation.Navigation,
            observation.World.ActiveSectorNumber,
            expectedName,
            publicKind,
            expectedRawObjectType,
            hasExpectedPosition,
            expectedX,
            expectedY,
            expectedZ,
            source: "published");

        if (published.Status is
            NavigationTargetResolutionStatus.Ready or
            NavigationTargetResolutionStatus.Invalid)
        {
            return published;
        }

        if (!observationCoordinator.TryReadNavigation(
                processId,
                observation.World.ActiveSectorNumber,
                out var freshNavigation,
                out _))
        {
            return published;
        }

        return ResolveTargetFromNavigation(
            freshNavigation,
            observation.World.ActiveSectorNumber,
            expectedName,
            publicKind,
            expectedRawObjectType,
            hasExpectedPosition,
            expectedX,
            expectedY,
            expectedZ,
            source: "fresh");
    }

    private static NavigationTargetResolutionResult
        ResolveTargetFromNavigation(
            ClientNavigationObservation navigation,
            uint expectedSectorNumber,
            string expectedName,
            string publicKind,
            byte expectedRawObjectType,
            bool hasExpectedPosition,
            float expectedX,
            float expectedY,
            float expectedZ,
            string source)
    {
        if (!navigation.IsAvailable)
        {
            return NavigationTargetResolutionResult.Waiting(
                $"Live navigation has not published route target {expectedName} yet.",
                expectedName,
                source);
        }

        if (navigation.ActiveSectorNumber != 0 &&
            expectedSectorNumber != 0 &&
            navigation.ActiveSectorNumber !=
                expectedSectorNumber)
        {
            return NavigationTargetResolutionResult.Waiting(
                $"Live navigation is still reconciling sector data for {expectedName}.",
                expectedName,
                source);
        }

        var normalizedExpectedName =
            GalaxyTopology.NormalizeName(expectedName);

        var candidates = navigation.Targets
            .Where(target =>
                target.IsAvailable &&
                target.ObjectId != 0 &&
                target.RawObjectType == expectedRawObjectType &&
                (string.Equals(
                     GalaxyTopology.NormalizeName(target.Name),
                     normalizedExpectedName,
                     StringComparison.Ordinal) ||
                 string.Equals(
                     GalaxyTopology.NormalizeName(
                         target.MapDisplayName),
                     normalizedExpectedName,
                     StringComparison.Ordinal)))
            .ToArray();

        if (candidates.Length == 0)
        {
            return NavigationTargetResolutionResult.Waiting(
                $"Route target {expectedName} is not present in the current live navigation set yet.",
                expectedName,
                source);
        }

        ClientNavigationTargetObservation target;

        if (candidates.Length == 1)
        {
            target = candidates[0];
        }
        else if (hasExpectedPosition)
        {
            var ordered = candidates
                .Where(candidate =>
                    candidate.Spatial.IsAvailable)
                .Select(candidate => new
                {
                    Target = candidate,
                    DistanceSquared = DistanceSquared(
                        candidate,
                        expectedX,
                        expectedY,
                        expectedZ),
                })
                .OrderBy(candidate =>
                    candidate.DistanceSquared)
                .ToArray();

            if (ordered.Length == 0 ||
                (ordered.Length > 1 &&
                 MathF.Abs(
                     ordered[0].DistanceSquared -
                     ordered[1].DistanceSquared) < 1.0f))
            {
                return NavigationTargetResolutionResult.Invalid(
                    $"The live route target '{expectedName}' is ambiguous.",
                    expectedName);
            }

            target = ordered[0].Target;
        }
        else
        {
            return NavigationTargetResolutionResult.Invalid(
                $"The live route target '{expectedName}' is ambiguous.",
                expectedName);
        }

        return NavigationTargetResolutionResult.Ready(
            expectedName,
            publicKind,
            target.ObjectId,
            target.ActiveSectorNumber == 0
                ? expectedSectorNumber
                : target.ActiveSectorNumber,
            source,
            GalaxyNavigationTargetSelectionContext.Navigation);
    }

    private static NavigationTargetResolutionResult
        ResolveTargetFromGutter(
            ClientGutterRadarObservation gutter,
            uint expectedSectorNumber,
            string expectedName,
            string publicKind,
            byte expectedRawObjectType,
            bool hasExpectedPosition,
            float expectedX,
            float expectedY,
            float expectedZ,
            string source)
    {
        if (!gutter.IsAvailable)
        {
            return NavigationTargetResolutionResult.Waiting(
                $"Live object targeting has not published route target {expectedName} yet.",
                expectedName,
                source);
        }

        if (gutter.ActiveSectorNumber != 0 &&
            expectedSectorNumber != 0 &&
            gutter.ActiveSectorNumber != expectedSectorNumber)
        {
            return NavigationTargetResolutionResult.Waiting(
                $"Live object targeting is still reconciling sector data for {expectedName}.",
                expectedName,
                source);
        }

        var normalizedExpectedName =
            GalaxyTopology.NormalizeName(expectedName);
        var candidates = gutter.Targets
            .Where(target =>
                target.IsAvailable &&
                target.ObjectId != 0 &&
                target.RawObjectType == expectedRawObjectType &&
                (string.Equals(
                     GalaxyTopology.NormalizeName(target.Name),
                     normalizedExpectedName,
                     StringComparison.Ordinal) ||
                 string.Equals(
                     GalaxyTopology.NormalizeName(target.DisplayName),
                     normalizedExpectedName,
                     StringComparison.Ordinal)))
            .ToArray();

        if (candidates.Length == 0)
        {
            return NavigationTargetResolutionResult.Waiting(
                $"Route target {expectedName} is not present in the current live object set yet.",
                expectedName,
                source);
        }

        ClientGutterRadarTargetObservation target;

        if (candidates.Length == 1)
        {
            target = candidates[0];
        }
        else if (hasExpectedPosition)
        {
            var ordered = candidates
                .Where(candidate => candidate.Spatial.IsAvailable)
                .Select(candidate => new
                {
                    Target = candidate,
                    DistanceSquared = DistanceSquared(
                        candidate,
                        expectedX,
                        expectedY,
                        expectedZ),
                })
                .OrderBy(candidate => candidate.DistanceSquared)
                .ToArray();

            if (ordered.Length == 0 ||
                (ordered.Length > 1 &&
                 MathF.Abs(
                     ordered[0].DistanceSquared -
                     ordered[1].DistanceSquared) < 1.0f))
            {
                return NavigationTargetResolutionResult.Invalid(
                    $"The live object target '{expectedName}' is ambiguous.",
                    expectedName);
            }

            target = ordered[0].Target;
        }
        else
        {
            return NavigationTargetResolutionResult.Invalid(
                $"The live object target '{expectedName}' is ambiguous.",
                expectedName);
        }

        return NavigationTargetResolutionResult.Ready(
            expectedName,
            publicKind,
            target.ObjectId,
            target.ActiveSectorNumber == 0
                ? expectedSectorNumber
                : target.ActiveSectorNumber,
            source,
            GalaxyNavigationTargetSelectionContext.Object);
    }

    private static NavigationTargetSelectionResult CreateSuccess(
        ExpectedNavigationTarget expected,
        string direction,
        int commandCount,
        IReadOnlyList<uint> observedObjectIds)
    {
        return new NavigationTargetSelectionResult
        {
            Succeeded = true,
            TargetName = expected.DisplayName,
            TargetKind = expected.PublicKind,
            ObjectId = expected.ObjectId,
            ActiveSectorNumber =
                expected.ActiveSectorNumber,
            TargetCycleDirection = direction,
            TargetCycleClickCount = commandCount,
            TargetCycleObservedObjectIds =
                [.. observedObjectIds],
            ClearTargetWasSent = false,
            SelectedObjectId =
                expected.ObjectId,
        };
    }

    private static float DistanceSquared(
        ClientNavigationTargetObservation target,
        float expectedX,
        float expectedY,
        float expectedZ)
    {
        var dx = target.Spatial.Position.X - expectedX;
        var dy = target.Spatial.Position.Y - expectedY;
        var dz = target.Spatial.Position.Z - expectedZ;

        return (dx * dx) +
               (dy * dy) +
               (dz * dz);
    }

    private static float DistanceSquared(
        ClientGutterRadarTargetObservation target,
        float expectedX,
        float expectedY,
        float expectedZ)
    {
        var dx = target.Spatial.Position.X - expectedX;
        var dy = target.Spatial.Position.Y - expectedY;
        var dz = target.Spatial.Position.Z - expectedZ;

        return (dx * dx) +
               (dy * dy) +
               (dz * dz);
    }

    private readonly record struct ExpectedNavigationTarget(
        uint ObjectId,
        uint ActiveSectorNumber,
        string DisplayName,
        string PublicKind,
        GalaxyNavigationTargetSelectionContext SelectionContext);

    private readonly record struct TargetWaitResult(
        bool Succeeded,
        uint SelectedObjectId,
        string Error)
    {
        public static TargetWaitResult Success(
            uint selectedObjectId)
        {
            return new TargetWaitResult(
                Succeeded: true,
                selectedObjectId,
                "");
        }

        public static TargetWaitResult Failure(
            string error)
        {
            return new TargetWaitResult(
                Succeeded: false,
                0,
                error);
        }
    }
}
