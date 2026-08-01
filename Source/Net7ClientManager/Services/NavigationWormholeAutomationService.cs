namespace Net7ClientManager.Services;

using System.Globalization;
using Net7ClientManager.Models;
using Net7ClientManager.Navigation;
using Net7ClientManager.Observations;
using Net7ClientManager.Observations.Models;
using Net7ClientManager.Win32;

/// <summary>
/// Executes a group wormhole route step without requiring fixed shortcut
/// positions. The caster and shortcut are resolved from live group state. The
/// native right-click destination menu is selected through normalized game
/// coordinates and every menu click is verified against the observed shortcut
/// ability before the skill is invoked.
/// </summary>
internal sealed class NavigationWormholeAutomationService(
    ClientObservationCoordinator observationCoordinator,
    GameShortcutPaletteService shortcutPaletteService,
    ForegroundInputCoordinator foregroundInputCoordinator)
{
    private static readonly TimeSpan shortcutSelectionTimeout =
        TimeSpan.FromMilliseconds(1400);

    private static readonly TimeSpan shortcutSelectionPollInterval =
        TimeSpan.FromMilliseconds(75);

    private static readonly TimeSpan shortcutBankSettleTimeout =
        TimeSpan.FromMilliseconds(600);

    private static readonly TimeSpan shortcutBankSettlePollInterval =
        TimeSpan.FromMilliseconds(25);

    private static readonly TimeSpan wormholeOpeningTimeout =
        TimeSpan.FromSeconds(45);

    private static readonly TimeSpan wormholeDialogSettleDelay =
        TimeSpan.FromMilliseconds(250);

    private static readonly TimeSpan confirmationClickSpacing =
        TimeSpan.FromMilliseconds(175);

    private const int CursorRestoreTolerancePixels = 4;

    public async Task<NavigationAutoPilotEffectOutcome> ExecuteAsync(
        NavigationAutoPilotFleetContext fleet,
        NavigationAutoPilotStepPlan step,
        Action<string> publishStatus,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fleet);
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(publishStatus);

        if (!step.IsWormholeTransition ||
            string.IsNullOrWhiteSpace(step.WormholeSkillFamilyName) ||
            string.IsNullOrWhiteSpace(step.TargetName))
        {
            return NavigationAutoPilotEffectOutcome.Failure(
                NavigationAutoPilotStopReason.InternalError,
                "Auto Pilot could not prepare the wormhole because the route step was incomplete.");
        }

        if (!this.TryResolveCaster(
                fleet,
                step,
                out var caster,
                out var shortcut,
                out var skillRank,
                out var error))
        {
            return NavigationAutoPilotEffectOutcome.Failure(
                NavigationAutoPilotStopReason.WormholeUnavailable,
                error);
        }

        publishStatus(string.Create(
            CultureInfo.CurrentCulture,
            $"Preparing {step.TargetName} on {caster.LiveName}."));

        if (!NavigationWormholeCatalog.AbilityMatches(
                new NavigationWormholeDestination
                {
                    SkillFamilyName = step.WormholeSkillFamilyName,
                    AbilityName = step.TargetName,
                    SectorKey = step.ToSectorKey,
                    RequiredRank = step.WormholeRequiredRank,
                    MenuIndex = step.WormholeMenuIndex,
                },
                shortcut.Name))
        {
            var selection = await this.SelectDestinationAsync(
                    caster,
                    shortcut,
                    skillRank,
                    step,
                    publishStatus,
                    cancellationToken)
                .ConfigureAwait(false);

            if (!selection.Succeeded || selection.Shortcut == null)
            {
                return NavigationAutoPilotEffectOutcome.Failure(
                    NavigationAutoPilotStopReason.WormholeActivationFailed,
                    selection.Error);
            }

            shortcut = selection.Shortcut;
        }

        publishStatus(string.Create(
            CultureInfo.CurrentCulture,
            $"Opening {step.TargetName} with {caster.LiveName}."));

        var openingSignal = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var invocationStartedAt = DateTimeOffset.UtcNow;

        void OnChatMessageObserved(
            object? sender,
            ClientChatMessageObservedEventArgs e)
        {
            var message = e.Message;

            if (message.ProcessId == caster.Client.ProcessId &&
                !message.IsSnapshot &&
                message.ObservedAt >= invocationStartedAt &&
                IsWormholeOpeningMessage(
                    message.Text,
                    caster.LiveName))
            {
                openingSignal.TrySetResult(result: true);
            }
        }

        observationCoordinator.ChatMessageObserved +=
            OnChatMessageObserved;

        try
        {
            var invocation = await this.InvokeShortcutAsync(
                    caster,
                    shortcut,
                    cancellationToken)
                .ConfigureAwait(false);

            if (!invocation.Succeeded)
            {
                return NavigationAutoPilotEffectOutcome.Failure(
                    NavigationAutoPilotStopReason.WormholeActivationFailed,
                    invocation.Error);
            }

            publishStatus(string.Create(
                CultureInfo.CurrentCulture,
                $"Waiting for {caster.LiveName} to finish opening {step.TargetName}."));

            if (!await WaitForWormholeOpeningSignalAsync(
                    openingSignal.Task,
                    cancellationToken)
                .ConfigureAwait(false))
            {
                return NavigationAutoPilotEffectOutcome.Failure(
                    NavigationAutoPilotStopReason.WormholeActivationFailed,
                    string.Concat(
                        "Auto Pilot invoked ",
                        step.TargetName,
                        " on ",
                        caster.LiveName,
                        ", but the game never reported that the wormhole was opening."));
            }
        }
        finally
        {
            observationCoordinator.ChatMessageObserved -=
                OnChatMessageObserved;
        }

        await Task.Delay(
                wormholeDialogSettleDelay,
                cancellationToken)
            .ConfigureAwait(false);

        publishStatus(string.Create(
            CultureInfo.CurrentCulture,
            $"Accepting {step.TargetName} for {fleet.Participants.Count} managed pilot{(fleet.Participants.Count == 1 ? "" : "s")}."));

        var confirmationOrder = fleet.Participants
            .Where(participant =>
                participant.Client.ProcessId !=
                caster.Client.ProcessId)
            .Concat([caster])
            .ToArray();

        foreach (var participant in confirmationOrder)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var confirmation = await this.AcceptWormholeAsync(
                    participant,
                    cancellationToken)
                .ConfigureAwait(false);

            if (!confirmation.Succeeded)
            {
                return confirmation;
            }

            await Task.Delay(
                    confirmationClickSpacing,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return NavigationAutoPilotEffectOutcome.Success();
    }

    private bool TryResolveCaster(
        NavigationAutoPilotFleetContext fleet,
        NavigationAutoPilotStepPlan step,
        out NavigationAutoPilotFleetParticipant caster,
        out GameShortcutPaletteEntry shortcut,
        out int skillRank,
        out string error)
    {
        caster = null!;
        shortcut = null!;
        skillRank = 0;
        error = "";

        var candidates = new List<CasterCandidate>();

        foreach (var participant in fleet.Participants)
        {
            if (!observationCoordinator.TryGetSnapshot(
                    participant.Client.ProcessId,
                    out var snapshot) ||
                snapshot.LifecycleState != ClientLifecycleState.InGame ||
                snapshot.LoadingOrTransitionFlag != 0 ||
                snapshot.World.Environment != ClientWorldEnvironment.Space)
            {
                continue;
            }

            var skill = snapshot.LocalPlayer.CharacterProgression
                .Skills.Skills
                .FirstOrDefault(candidate =>
                    NavigationWormholeCatalog.FamilyMatches(
                        step.WormholeSkillFamilyName,
                        candidate.Name));

            if (skill == null ||
                skill.CurrentRank < step.WormholeRequiredRank)
            {
                continue;
            }

            var shortcuts = observationCoordinator.TryReadShortcutState(
                participant.Client.ProcessId,
                out var directShortcuts,
                out _)
                ? directShortcuts
                : snapshot.Shortcuts;

            var familyShortcuts = shortcutPaletteService
                .BuildEntries(
                    participant.Client,
                    snapshot,
                    shortcuts)
                .Where(entry =>
                    entry.Kind == GameShortcutKind.Skill &&
                    ShortcutMatchesFamily(
                        entry,
                        step.WormholeSkillFamilyName))
                .ToArray();

            foreach (var familyShortcut in familyShortcuts)
            {
                candidates.Add(new CasterCandidate(
                    participant,
                    familyShortcut,
                    skill.CurrentRank,
                    NavigationWormholeCatalog.AbilityMatches(
                        new NavigationWormholeDestination
                        {
                            SkillFamilyName =
                                step.WormholeSkillFamilyName,
                            AbilityName = step.TargetName,
                            SectorKey = step.ToSectorKey,
                            RequiredRank =
                                step.WormholeRequiredRank,
                            MenuIndex = step.WormholeMenuIndex,
                        },
                        familyShortcut.Name)));
            }
        }

        var selected = candidates
            .OrderByDescending(candidate => candidate.AlreadySelected)
            .ThenByDescending(candidate => candidate.SkillRank)
            .ThenBy(candidate => candidate.Participant.GroupSlot)
            .ThenBy(
                candidate => candidate.Participant.LiveName,
                StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (selected == null)
        {
            var expectedCasters = step.WormholeCasterNames.Count == 0
                ? "an eligible managed pilot"
                : string.Join(
                    ", ",
                    step.WormholeCasterNames);

            error = string.Concat(
                "Auto Pilot needs ",
                step.WormholeSkillFamilyName,
                " rank ",
                step.WormholeRequiredRank.ToString(
                    CultureInfo.InvariantCulture),
                " on any shortcut of ",
                expectedCasters,
                ", but no usable live shortcut was found.");
            return false;
        }

        caster = selected.Participant;
        shortcut = selected.Shortcut;
        skillRank = selected.SkillRank;
        return true;
    }

    private async Task<ShortcutSelectionResult> SelectDestinationAsync(
        NavigationAutoPilotFleetParticipant caster,
        GameShortcutPaletteEntry shortcut,
        int skillRank,
        NavigationAutoPilotStepPlan step,
        Action<string> publishStatus,
        CancellationToken cancellationToken)
    {
        if (!NativeMethods.TryGetClientSize(
                caster.Client.GameWindowHandle,
                out var clientSize) ||
            !ClientGameUiCoordinates.TryGetShortcutBarSlot(
                shortcut.VisibleKey,
                clientSize,
                out var shortcutClientPoint) ||
            !NativeMethods.TryConvertClientPointToScreen(
                caster.Client.GameWindowHandle,
                shortcutClientPoint,
                out var shortcutScreenPoint))
        {
            return ShortcutSelectionResult.Failure(
                "Auto Pilot could not use the wormhole shortcut.");
        }

        var familyDestinationCount = NavigationWormholeCatalog
            .GetUnlockedDestinations(
                step.WormholeSkillFamilyName,
                skillRank)
            .Count;

        if (familyDestinationCount <= 0)
        {
            return ShortcutSelectionResult.Failure(
                "No wormhole destinations are available for this pilot.");
        }

        if (!ClientGameUiCoordinates.TryGetWormholeDestinationMenuItem(
                shortcutClientPoint,
                familyDestinationCount,
                step.WormholeMenuIndex,
                clientSize,
                out var menuClientPoint) ||
            !NativeMethods.TryConvertClientPointToScreen(
                caster.Client.GameWindowHandle,
                menuClientPoint,
                out var menuScreenPoint))
        {
            return ShortcutSelectionResult.Failure(
                "Auto Pilot could not select the wormhole destination.");
        }

        publishStatus(string.Create(
            CultureInfo.CurrentCulture,
            $"Selecting {step.TargetName} from {caster.LiveName}'s {step.WormholeSkillFamilyName} menu."));

        var hasOriginalCursor = NativeMethods.TryGetCursorScreenPosition(
            out var originalCursorPosition);
        var ownsCursor = false;

        await caster.HostForm
            .SetAddonOverlayInputSuppressedAsync(suppressed: true)
            .ConfigureAwait(false);

        try
        {
            using var foregroundLease =
                await foregroundInputCoordinator
                    .AcquireAsync(cancellationToken)
                    .ConfigureAwait(false);

            if (!await this.EnsureShortcutGroupAsync(
                    caster.Client,
                    shortcut.Bar,
                    shortcut.Group,
                    cancellationToken)
                .ConfigureAwait(false))
            {
                return ShortcutSelectionResult.Failure(
                    "Auto Pilot could not open the wormhole shortcut.");
            }

            var selected = await NativeMethods
                .TryForegroundOpenShortcutMenuAndSelectAsync(
                    caster.Client.GameWindowHandle,
                    shortcutScreenPoint,
                    menuScreenPoint,
                    heldChord: null,
                    waitAfterHeldChordAsync: null,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            ownsCursor = selected;

            if (!selected)
            {
                return ShortcutSelectionResult.Failure(string.Concat(
                    "Auto Pilot could not select ",
                    step.TargetName,
                    "."));
            }

            var observed = await this.WaitForSelectedShortcutAsync(
                    caster.Client,
                    shortcut,
                    step,
                    cancellationToken)
                .ConfigureAwait(false);

            return observed == null
                ? ShortcutSelectionResult.Failure(string.Concat(
                    "Auto Pilot could not select ",
                    step.TargetName,
                    "."))
                : ShortcutSelectionResult.Success(observed);
        }
        finally
        {
            if (hasOriginalCursor &&
                ownsCursor &&
                CursorRemainsAt(menuScreenPoint))
            {
                _ = NativeMethods.MoveCursorToScreenPoint(
                    originalCursorPosition);
            }

            await caster.HostForm
                .SetAddonOverlayInputSuppressedAsync(suppressed: false)
                .ConfigureAwait(false);
        }
    }

    private async Task<GameShortcutPaletteEntry?> WaitForSelectedShortcutAsync(
        ClientInstance client,
        GameShortcutPaletteEntry originalShortcut,
        NavigationAutoPilotStepPlan step,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + shortcutSelectionTimeout;

        do
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (observationCoordinator.TryGetSnapshot(
                    client.ProcessId,
                    out var snapshot))
            {
                var shortcuts = observationCoordinator.TryReadShortcutState(
                    client.ProcessId,
                    out var directShortcuts,
                    out _)
                    ? directShortcuts
                    : snapshot.Shortcuts;

                var observed = shortcutPaletteService
                    .BuildEntries(client, snapshot, shortcuts)
                    .FirstOrDefault(entry =>
                        entry.Kind == GameShortcutKind.Skill &&
                        entry.Bar == originalShortcut.Bar &&
                        entry.Group == originalShortcut.Group &&
                        entry.Button == originalShortcut.Button &&
                        ShortcutMatchesFamily(
                            entry,
                            step.WormholeSkillFamilyName));

                if (observed != null &&
                    NavigationWormholeCatalog.AbilityMatches(
                        new NavigationWormholeDestination
                        {
                            SkillFamilyName =
                                step.WormholeSkillFamilyName,
                            AbilityName = step.TargetName,
                            SectorKey = step.ToSectorKey,
                            RequiredRank =
                                step.WormholeRequiredRank,
                            MenuIndex = step.WormholeMenuIndex,
                        },
                        observed.Name))
                {
                    return observed;
                }
            }

            await Task.Delay(
                    shortcutSelectionPollInterval,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        while (DateTimeOffset.UtcNow < deadline);

        return null;
    }

    private async Task<NavigationAutoPilotEffectOutcome> AcceptWormholeAsync(
        NavigationAutoPilotFleetParticipant participant,
        CancellationToken cancellationToken)
    {
        if (!observationCoordinator.TryGetSnapshot(
                participant.Client.ProcessId,
                out var before))
        {
            return NavigationAutoPilotEffectOutcome.Failure(
                NavigationAutoPilotStopReason.ObservationUnavailable,
                $"Auto Pilot could not inspect {participant.LiveName} before accepting the wormhole.");
        }

        if (before.LoadingOrTransitionFlag != 0 ||
            before.LifecycleState != ClientLifecycleState.InGame ||
            before.World.Environment != ClientWorldEnvironment.Space)
        {
            // The client may already have accepted and entered transition.
            return NavigationAutoPilotEffectOutcome.Success();
        }

        if (!NativeMethods.TryGetClientSize(
                participant.Client.GameWindowHandle,
                out var clientSize) ||
            !ClientGameUiCoordinates.TryGetConfirmDialog(
                clientSize,
                out var confirmClientPoint) ||
            !NativeMethods.TryConvertClientPointToScreen(
                participant.Client.GameWindowHandle,
                confirmClientPoint,
                out var confirmScreenPoint))
        {
            return NavigationAutoPilotEffectOutcome.Failure(
                NavigationAutoPilotStopReason.WormholeActivationFailed,
                $"Auto Pilot could not map the wormhole confirmation for {participant.LiveName}.");
        }

        using var foregroundLease = await foregroundInputCoordinator
            .AcquireAsync(cancellationToken)
            .ConfigureAwait(false);

        var hasOriginalCursor = NativeMethods.TryGetCursorScreenPosition(
            out var originalCursorPosition);
        var ownsCursor = false;

        await participant.HostForm
            .SetAddonOverlayInputSuppressedAsync(suppressed: true)
            .ConfigureAwait(false);

        try
        {
            if (!NativeMethods.TryFocusWindowForKeyboardInput(
                    participant.Client.GameWindowHandle))
            {
                return NavigationAutoPilotEffectOutcome.Failure(
                    NavigationAutoPilotStopReason.WormholeActivationFailed,
                    $"Auto Pilot could not focus {participant.LiveName} to accept the wormhole.");
            }

            await Task.Delay(
                    TimeSpan.FromMilliseconds(50),
                    cancellationToken)
                .ConfigureAwait(false);

            if (!NativeMethods.MoveCursorToScreenPoint(confirmScreenPoint))
            {
                return NavigationAutoPilotEffectOutcome.Failure(
                    NavigationAutoPilotStopReason.WormholeActivationFailed,
                    $"Auto Pilot could not move to {participant.LiveName}'s wormhole confirmation.");
            }

            ownsCursor = true;

            await NativeMethods.StableLeftClickAtCurrentCursorAsync(
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            if (hasOriginalCursor &&
                ownsCursor &&
                CursorRemainsAt(confirmScreenPoint))
            {
                _ = NativeMethods.MoveCursorToScreenPoint(
                    originalCursorPosition);
            }

            await participant.HostForm
                .SetAddonOverlayInputSuppressedAsync(suppressed: false)
                .ConfigureAwait(false);
        }

        return NavigationAutoPilotEffectOutcome.Success();
    }

    private static bool ShortcutMatchesFamily(
        GameShortcutPaletteEntry entry,
        string familyName)
    {
        return NavigationWormholeCatalog.FamilyMatches(
                   familyName,
                   entry.FamilyName) ||
               NavigationWormholeCatalog.FamilyMatches(
                   familyName,
                   entry.SkillDetails?.SkillFamilyName) ||
               entry.Name.Contains(
                   "Wormhole",
                   StringComparison.OrdinalIgnoreCase) &&
               entry.Name.Contains(
                   familyName.StartsWith("Extended", StringComparison.Ordinal)
                       ? "Extended"
                       : "Gate",
                   StringComparison.OrdinalIgnoreCase);
    }

    private bool TryReadShortcutGroup(
        int processId,
        int bar,
        out int currentGroup,
        out string error)
    {
        currentGroup = -1;

        if (!observationCoordinator.TryReadShortcutState(
                processId,
                out var shortcuts,
                out error))
        {
            return false;
        }

        var shortcutBar = shortcuts.GetBar(bar);

        if (shortcutBar?.CurrentGroup is not { } observedGroup)
        {
            error = string.Concat(
                "Shortcut bar ",
                bar.ToString(CultureInfo.InvariantCulture),
                " is unavailable: ",
                shortcutBar?.Status ?? shortcuts.Status);
            return false;
        }

        currentGroup = observedGroup;
        return true;
    }

    private async Task<bool> EnsureShortcutGroupAsync(
        ClientInstance client,
        int bar,
        int expectedGroup,
        CancellationToken cancellationToken)
    {
        if (!this.TryReadShortcutGroup(
                client.ProcessId,
                bar,
                out var currentGroup,
                out _) ||
            currentGroup is < 0 or > 1)
        {
            return false;
        }

        if (currentGroup == expectedGroup)
        {
            return true;
        }

        if (!NativeMethods.TryGetClientSize(
                client.GameWindowHandle,
                out var clientSize) ||
            !ClientGameUiCoordinates.TryGetShortcutBankToggle(
                bar,
                clientSize,
                out var toggleClientPoint) ||
            !NativeMethods.TryConvertClientPointToScreen(
                client.GameWindowHandle,
                toggleClientPoint,
                out var toggleScreenPoint) ||
            !NativeMethods.TryFocusWindowForKeyboardInput(
                client.GameWindowHandle))
        {
            return false;
        }

        await Task.Delay(
                TimeSpan.FromMilliseconds(50),
                cancellationToken)
            .ConfigureAwait(false);

        if (!NativeMethods.MoveCursorToScreenPoint(toggleScreenPoint))
        {
            return false;
        }

        await NativeMethods.StableLeftClickAtCurrentCursorAsync(
                cancellationToken)
            .ConfigureAwait(false);

        return await this.WaitForShortcutGroupAsync(
                client.ProcessId,
                bar,
                expectedGroup,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ShortcutInvocationResult> InvokeShortcutAsync(
        NavigationAutoPilotFleetParticipant caster,
        GameShortcutPaletteEntry shortcut,
        CancellationToken cancellationToken)
    {
        if (!NativeMethods.TryGetClientSize(
                caster.Client.GameWindowHandle,
                out var clientSize) ||
            !ClientGameUiCoordinates.TryGetShortcutBarSlot(
                shortcut.VisibleKey,
                clientSize,
                out var shortcutClientPoint) ||
            !NativeMethods.TryConvertClientPointToScreen(
                caster.Client.GameWindowHandle,
                shortcutClientPoint,
                out var shortcutScreenPoint))
        {
            return ShortcutInvocationResult.Failure(
                "Auto Pilot could not open the wormhole.");
        }

        var hasOriginalCursor = NativeMethods.TryGetCursorScreenPosition(
            out var originalCursorPosition);
        var ownsCursor = false;

        await caster.HostForm
            .SetAddonOverlayInputSuppressedAsync(suppressed: true)
            .ConfigureAwait(false);

        try
        {
            using var foregroundLease =
                await foregroundInputCoordinator
                    .AcquireAsync(cancellationToken)
                    .ConfigureAwait(false);

            if (!await this.EnsureShortcutGroupAsync(
                    caster.Client,
                    shortcut.Bar,
                    shortcut.Group,
                    cancellationToken)
                .ConfigureAwait(false) ||
                !NativeMethods.TryFocusWindowForKeyboardInput(
                    caster.Client.GameWindowHandle))
            {
                return ShortcutInvocationResult.Failure(
                    "Auto Pilot could not open the wormhole.");
            }

            await Task.Delay(
                    TimeSpan.FromMilliseconds(50),
                    cancellationToken)
                .ConfigureAwait(false);

            if (!NativeMethods.MoveCursorToScreenPoint(
                    shortcutScreenPoint))
            {
                return ShortcutInvocationResult.Failure(
                    "Auto Pilot could not open the wormhole.");
            }

            ownsCursor = true;

            await NativeMethods.StableLeftClickAtCurrentCursorAsync(
                    cancellationToken)
                .ConfigureAwait(false);

            return ShortcutInvocationResult.Success();
        }
        finally
        {
            if (hasOriginalCursor &&
                ownsCursor &&
                CursorRemainsAt(shortcutScreenPoint))
            {
                _ = NativeMethods.MoveCursorToScreenPoint(
                    originalCursorPosition);
            }

            await caster.HostForm
                .SetAddonOverlayInputSuppressedAsync(suppressed: false)
                .ConfigureAwait(false);
        }
    }

    private async Task<bool> WaitForShortcutGroupAsync(
        int processId,
        int bar,
        int expectedGroup,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + shortcutBankSettleTimeout;

        do
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (this.TryReadShortcutGroup(
                    processId,
                    bar,
                    out var currentGroup,
                    out _) &&
                currentGroup == expectedGroup)
            {
                return true;
            }

            await Task.Delay(
                    shortcutBankSettlePollInterval,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        while (DateTimeOffset.UtcNow < deadline);

        return false;
    }

    private static async Task<bool> WaitForWormholeOpeningSignalAsync(
        Task openingSignal,
        CancellationToken cancellationToken)
    {
        var timeout = Task.Delay(
            wormholeOpeningTimeout,
            cancellationToken);
        var completed = await Task.WhenAny(
                openingSignal,
                timeout)
            .ConfigureAwait(false);

        if (ReferenceEquals(completed, openingSignal))
        {
            await openingSignal.ConfigureAwait(false);
            return true;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return false;
    }

    private static bool IsWormholeOpeningMessage(
        string? text,
        string casterName)
    {
        return !string.IsNullOrWhiteSpace(text) &&
               !string.IsNullOrWhiteSpace(casterName) &&
               text.Contains(
                   string.Concat(
                       casterName.Trim(),
                       " is opening a wormhole."),
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool CursorRemainsAt(Point commandedPosition)
    {
        return NativeMethods.TryGetCursorScreenPosition(
                   out var currentPosition) &&
               Math.Abs(currentPosition.X - commandedPosition.X) <=
                   CursorRestoreTolerancePixels &&
               Math.Abs(currentPosition.Y - commandedPosition.Y) <=
                   CursorRestoreTolerancePixels;
    }

    private sealed record ShortcutInvocationResult(
        bool Succeeded,
        string Error)
    {
        public static ShortcutInvocationResult Success() =>
            new(true, "");

        public static ShortcutInvocationResult Failure(string error) =>
            new(false, error);
    }

    private sealed record CasterCandidate(
        NavigationAutoPilotFleetParticipant Participant,
        GameShortcutPaletteEntry Shortcut,
        int SkillRank,
        bool AlreadySelected);

    private sealed record ShortcutSelectionResult
    {
        public bool Succeeded { get; init; }

        public GameShortcutPaletteEntry? Shortcut { get; init; }

        public string Error { get; init; } = "";

        public static ShortcutSelectionResult Success(
            GameShortcutPaletteEntry shortcut)
        {
            return new ShortcutSelectionResult
            {
                Succeeded = true,
                Shortcut = shortcut,
            };
        }

        public static ShortcutSelectionResult Failure(string error)
        {
            return new ShortcutSelectionResult
            {
                Error = error,
            };
        }
    }
}
