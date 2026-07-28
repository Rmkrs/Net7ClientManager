
namespace Net7ClientManager.Services;

using System.Diagnostics;
using System.Globalization;
using System.Text;
using Net7ClientManager.Models;
using Net7ClientManager.Observations;
using Net7ClientManager.Win32;

internal sealed class FleetCommandService
{
    private readonly BuiltInFleetCommandProvider builtInFleetCommandProvider = new();
    private readonly BuiltInInputActionProvider builtInInputActionProvider = new();
    private readonly InputActionExecutor inputActionExecutor;
    private readonly ForegroundInputCoordinator foregroundInputCoordinator;
    private readonly ClientObservationCoordinator clientObservationCoordinator;

    private readonly FleetCommandCatalog commandCatalog;
    private readonly InputActionCatalog inputActionCatalog;

    private CancellationTokenSource? activeCommandCancellation;
    private bool commandRunning;

    public FleetCommandService(
        GameCommandCoordinator gameCommandCoordinator,
        ForegroundInputCoordinator foregroundInputCoordinator,
        ClientObservationCoordinator clientObservationCoordinator)
    {
        this.foregroundInputCoordinator = foregroundInputCoordinator;
        this.clientObservationCoordinator = clientObservationCoordinator;
        this.inputActionExecutor = new InputActionExecutor(
            gameCommandCoordinator,
            foregroundInputCoordinator);

        this.commandCatalog = new FleetCommandCatalog(this.builtInFleetCommandProvider);
        this.inputActionCatalog = new InputActionCatalog(this.builtInInputActionProvider);
    }

    public IReadOnlyList<FleetCommandDefinition> GetOverlayCommands()
    {
        return this.commandCatalog.GetOverlayCommands();
    }

    public async Task ExecuteAsync(
        FleetCommandDefinition command,
        FleetCommandInvocationContext invocationContext,
        IReadOnlyCollection<ClientInstance> clients,
        LayoutProfile profile,
        FleetCommandSettings settings,
        string pilotName)
    {
        if (this.commandRunning)
        {
            return;
        }

        this.commandRunning = true;

        using var cancellation = new CancellationTokenSource();
        this.activeCommandCancellation = cancellation;

        try
        {
            var executionContext = this.BuildExecutionContext(
                command,
                invocationContext,
                clients,
                profile,
                settings,
                pilotName);

            await this.ExecuteCommandAsync(
                command,
                executionContext,
                cancellation.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Command was cancelled by the operator.
        }
        finally
        {
            this.activeCommandCancellation = null;
            this.commandRunning = false;
        }
    }

    public void Cancel()
    {
        this.activeCommandCancellation?.Cancel();
    }

    private async Task ExecuteCommandAsync(
        FleetCommandDefinition command,
        FleetCommandExecutionContext context,
        CancellationToken cancellationToken)
    {
        foreach (var block in command.Blocks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await this.ExecuteBlockAsync(
                block,
                context,
                cancellationToken).ConfigureAwait(true);
        }
    }

    private async Task ExecuteBlockAsync(
        FleetCommandBlock block,
        FleetCommandExecutionContext context,
        CancellationToken cancellationToken)
    {
        switch (block.Scope)
        {
            case FleetCommandScope.Pilot:
                await this.ExecuteClientStepsAsync(
                    context.Pilot,
                    block.Steps,
                    context,
                    cancellationToken).ConfigureAwait(true);
                return;

            case FleetCommandScope.Followers:
                foreach (var follower in context.Followers)
                {
                    await this.ExecuteClientStepsAsync(
                        follower,
                        block.Steps,
                        context,
                        cancellationToken).ConfigureAwait(true);
                }

                return;

            case FleetCommandScope.All:
                foreach (var follower in context.Followers)
                {
                    await this.ExecuteClientStepsAsync(
                        follower,
                        block.Steps,
                        context,
                        cancellationToken).ConfigureAwait(true);
                }

                await this.ExecuteClientStepsAsync(
                    context.Pilot,
                    block.Steps,
                    context,
                    cancellationToken).ConfigureAwait(true);
                return;

            case FleetCommandScope.Target:
                if (context.Target != null)
                {
                    await this.ExecuteClientStepsAsync(
                        context.Target,
                        block.Steps,
                        context,
                        cancellationToken).ConfigureAwait(true);
                }

                return;

            case FleetCommandScope.System:
                await this.ExecuteSystemStepsAsync(
                    block.Steps,
                    context,
                    cancellationToken).ConfigureAwait(true);
                return;

            default:
                throw new InvalidOperationException($"Unsupported fleet command scope '{block.Scope}'.");
        }
    }

    private async Task ExecuteClientStepsAsync(
        ClientInstance client,
        IEnumerable<FleetCommandStep> steps,
        FleetCommandExecutionContext context,
        CancellationToken cancellationToken)
    {
        foreach (var step in steps)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var shouldContinue = await this.ExecuteStepAsync(
                client,
                step,
                context,
                cancellationToken).ConfigureAwait(true);

            if (!shouldContinue)
            {
                return;
            }
        }
    }

    private async Task ExecuteSystemStepsAsync(
        IEnumerable<FleetCommandStep> steps,
        FleetCommandExecutionContext context,
        CancellationToken cancellationToken)
    {
        foreach (var step in steps)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var shouldContinue = await this.ExecuteStepAsync(
                client: null,
                step,
                context,
                cancellationToken).ConfigureAwait(true);

            if (!shouldContinue)
            {
                return;
            }
        }
    }

    private async Task<bool> ExecuteStepAsync(
        ClientInstance? client,
        FleetCommandStep step,
        FleetCommandExecutionContext context,
        CancellationToken cancellationToken)
    {
        switch (step.Kind)
        {
            case FleetCommandStepKind.Action:
                await this.ExecuteActionStepAsync(
                    client,
                    step,
                    cancellationToken).ConfigureAwait(true);
                return true;

            case FleetCommandStepKind.Delay:
                await ExecuteDelayStepAsync(
                    step,
                    cancellationToken).ConfigureAwait(true);
                return true;

            case FleetCommandStepKind.SetTitle:
                ExecuteSetTitleStep(client, step, context);
                return true;

            case FleetCommandStepKind.RestorePilotFocus:
                RestorePilotFocus(context);
                return true;

            case FleetCommandStepKind.TapKey:
                await ExecuteTapKeyStepAsync(
                    client,
                    step,
                    cancellationToken).ConfigureAwait(true);
                return true;

            case FleetCommandStepKind.TypeText:
                await ExecuteTypeTextStepAsync(
                    client,
                    step,
                    context,
                    cancellationToken).ConfigureAwait(true);
                return true;

            case FleetCommandStepKind.ChatCommand:
                return await ExecuteChatCommandStepAsync(
                    client,
                    step,
                    context,
                    cancellationToken).ConfigureAwait(true);

            case FleetCommandStepKind.TargetInvokingPilot:
                return await this.ExecuteTargetInvokingPilotStepAsync(
                    client,
                    context,
                    selectTargetOfPilot: false,
                    cancellationToken).ConfigureAwait(true);

            case FleetCommandStepKind.TargetInvokingPilotTarget:
                return await this.ExecuteTargetInvokingPilotStepAsync(
                    client,
                    context,
                    selectTargetOfPilot: true,
                    cancellationToken).ConfigureAwait(true);

            default:
                throw new InvalidOperationException($"Unsupported fleet command step kind '{step.Kind}'.");
        }
    }

    private async Task ExecuteActionStepAsync(
        ClientInstance? client,
        FleetCommandStep step,
        CancellationToken cancellationToken)
    {
        if (client == null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(step.ActionName))
        {
            return;
        }

        var action = this.inputActionCatalog.FindByName(step.ActionName);

        if (action == null)
        {
            client.AutomationStatus = $"Missing action: {step.ActionName}";
            return;
        }

        var result = await this.inputActionExecutor.ExecuteAsync(
                client,
                action,
                cancellationToken)
            .ConfigureAwait(true);

        if (!result.Succeeded)
        {
            client.AutomationStatus = result.Error;
        }
    }

    private async Task<bool> ExecuteTargetInvokingPilotStepAsync(
        ClientInstance? client,
        FleetCommandExecutionContext context,
        bool selectTargetOfPilot,
        CancellationToken cancellationToken)
    {
        if (client == null)
        {
            return false;
        }

        var targetPlan = this.ResolveInvokingPilotTargetPlan(
            client,
            context,
            selectTargetOfPilot);

        if (targetPlan == null)
        {
            return false;
        }

        if (targetPlan.IsAlreadySelected(client, this.clientObservationCoordinator))
        {
            return true;
        }

        if (selectTargetOfPilot)
        {
            var result = await this.ExecuteNamedActionWithResultAsync(
                    client,
                    GetTargetOfGroupMemberInputActionName(targetPlan.GroupMemberSlot),
                    cancellationToken)
                .ConfigureAwait(true);

            if (!result.Succeeded)
            {
                client.AutomationStatus = result.Error;
                return false;
            }

            if (await targetPlan.WaitUntilSelectedAsync(
                    client,
                    this.clientObservationCoordinator,
                    cancellationToken).ConfigureAwait(true))
            {
                return true;
            }

            client.AutomationStatus = string.Concat(
                "Target-of-",
                targetPlan.InvokingPilotName,
                " did not resolve to the invoking pilot's current target.");
            return false;
        }

        var keyResult = await this.ExecuteNamedActionWithResultAsync(
                client,
                GetSelectGroupMemberInputActionName(targetPlan.GroupMemberSlot),
                cancellationToken)
            .ConfigureAwait(true);

        if (keyResult.Succeeded &&
            await targetPlan.WaitUntilSelectedAsync(
                    client,
                    this.clientObservationCoordinator,
                    cancellationToken).ConfigureAwait(true))
        {
            return true;
        }

        var clickResult = await this.inputActionExecutor.ExecuteAsync(
                client,
                CreateSelectGroupMemberClickAction(targetPlan.GroupMemberSlot),
                cancellationToken)
            .ConfigureAwait(true);

        if (!clickResult.Succeeded)
        {
            client.AutomationStatus = string.Concat(
                "Could not target ",
                targetPlan.InvokingPilotName,
                " via keybinding (",
                keyResult.Error,
                ") or click fallback (",
                clickResult.Error,
                ").");
            return false;
        }

        if (await targetPlan.WaitUntilSelectedAsync(
                client,
                this.clientObservationCoordinator,
                cancellationToken).ConfigureAwait(true))
        {
            return true;
        }

        client.AutomationStatus = string.Concat(
            "Selected Group Member ",
            targetPlan.GroupMemberSlot.ToString(CultureInfo.InvariantCulture),
            ", but target did not become ",
            targetPlan.InvokingPilotName,
            ".");
        return false;
    }

    private InvokingPilotTargetPlan? ResolveInvokingPilotTargetPlan(
        ClientInstance actingClient,
        FleetCommandExecutionContext context,
        bool selectTargetOfPilot)
    {
        if (string.IsNullOrWhiteSpace(context.PilotName))
        {
            actingClient.AutomationStatus = "Cannot target invoking pilot: pilot name is unknown.";
            return null;
        }

        if (!this.clientObservationCoordinator.TryGetSnapshot(
                actingClient.ProcessId,
                out var actingSnapshot))
        {
            actingClient.AutomationStatus = "Cannot target invoking pilot: acting client snapshot is unavailable.";
            return null;
        }

        if (!this.clientObservationCoordinator.TryGetSnapshot(
                context.InvocationContext.ActiveClient.ProcessId,
                out var invokingSnapshot))
        {
            actingClient.AutomationStatus = "Cannot target invoking pilot: invoking client snapshot is unavailable.";
            return null;
        }

        var member = actingSnapshot.Group.Members.FirstOrDefault(candidate =>
            candidate.IsPresent &&
            string.Equals(
                candidate.Name,
                context.PilotName,
                StringComparison.OrdinalIgnoreCase));

        if (member == null)
        {
            actingClient.AutomationStatus = string.Concat(
                "Cannot target ",
                context.PilotName,
                ": pilot is not visible in this client's group list.");
            return null;
        }

        if (selectTargetOfPilot &&
            !invokingSnapshot.Target.HasTarget)
        {
            actingClient.AutomationStatus = string.Concat(
                "Cannot target ",
                context.PilotName,
                "'s target: invoking pilot has no current target.");
            return null;
        }

        return new InvokingPilotTargetPlan(
            context.PilotName,
            member.Slot,
            selectTargetOfPilot
                ? invokingSnapshot.Target.ObjectId
                : invokingSnapshot.LocalPlayerObjectId,
            selectTargetOfPilot
                ? invokingSnapshot.Target.Name
                : context.PilotName);
    }

    private async Task<InputActionExecutionResult> ExecuteNamedActionWithResultAsync(
        ClientInstance client,
        string actionName,
        CancellationToken cancellationToken)
    {
        var action = this.inputActionCatalog.FindByName(actionName);

        if (action == null)
        {
            return InputActionExecutionResult.Failure($"Missing action: {actionName}");
        }

        return await this.inputActionExecutor.ExecuteAsync(
                client,
                action,
                cancellationToken)
            .ConfigureAwait(true);
    }

    private async Task ExecuteNamedActionAsync(
        ClientInstance client,
        string actionName,
        CancellationToken cancellationToken)
    {
        var result = await this.ExecuteNamedActionWithResultAsync(
                client,
                actionName,
                cancellationToken)
            .ConfigureAwait(true);

        if (!result.Succeeded)
        {
            client.AutomationStatus = result.Error;
        }
    }

    private static InputActionDefinition CreateSelectGroupMemberClickAction(
        int memberIndex)
    {
        var point = memberIndex switch
        {
            1 => ClientGameUiCoordinates.SelectGroupMember1,
            2 => ClientGameUiCoordinates.SelectGroupMember2,
            3 => ClientGameUiCoordinates.SelectGroupMember3,
            4 => ClientGameUiCoordinates.SelectGroupMember4,
            5 => ClientGameUiCoordinates.SelectGroupMember5,
            _ => throw new ArgumentOutOfRangeException(
                nameof(memberIndex),
                memberIndex,
                "Group member index must be 1..5."),
        };

        return new InputActionDefinition
        {
            Name = string.Create(
                CultureInfo.InvariantCulture,
                $"Click Select Group Member {memberIndex}"),
            Kind = InputActionKind.MouseClick,
            Key = Keys.None,
            BaseWidth = point.BaseWidth,
            BaseHeight = point.BaseHeight,
            BaseX = point.BaseX,
            BaseY = point.BaseY,
        };
    }

    private static string GetSelectGroupMemberInputActionName(
        int memberIndex)
    {
        return memberIndex switch
        {
            1 => BuiltInInputActionProvider.SelectGroupMember1Name,
            2 => BuiltInInputActionProvider.SelectGroupMember2Name,
            3 => BuiltInInputActionProvider.SelectGroupMember3Name,
            4 => BuiltInInputActionProvider.SelectGroupMember4Name,
            5 => BuiltInInputActionProvider.SelectGroupMember5Name,
            _ => throw new ArgumentOutOfRangeException(
                nameof(memberIndex),
                memberIndex,
                "Group member index must be 1..5."),
        };
    }

    private static string GetTargetOfGroupMemberInputActionName(
        int memberIndex)
    {
        return memberIndex switch
        {
            1 => BuiltInInputActionProvider.TargetOfGroupMember1Name,
            2 => BuiltInInputActionProvider.TargetOfGroupMember2Name,
            3 => BuiltInInputActionProvider.TargetOfGroupMember3Name,
            4 => BuiltInInputActionProvider.TargetOfGroupMember4Name,
            5 => BuiltInInputActionProvider.TargetOfGroupMember5Name,
            _ => throw new ArgumentOutOfRangeException(
                nameof(memberIndex),
                memberIndex,
                "Group member index must be 1..5."),
        };
    }

    private static async Task ExecuteDelayStepAsync(
        FleetCommandStep step,
        CancellationToken cancellationToken)
    {
        if (step.Milliseconds <= 0)
        {
            return;
        }

        await Task.Delay(
            step.Milliseconds,
            cancellationToken).ConfigureAwait(true);
    }

    private static void ExecuteSetTitleStep(
        ClientInstance? client,
        FleetCommandStep step,
        FleetCommandExecutionContext context)
    {
        if (client == null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(step.Text))
        {
            return;
        }

        var text = FormatTemplateText(step.Text, context);

        if (step.DurationMilliseconds <= 0)
        {
            client.HostForm?.SetPermanentTitleStatus(
                text,
                step.Blink);

            return;
        }

        client.HostForm?.SetTemporaryTitleStatus(
            text,
            TimeSpan.FromMilliseconds(step.DurationMilliseconds),
            step.Blink);
    }

    private async Task ExecuteTapKeyStepAsync(
        ClientInstance? client,
        FleetCommandStep step,
        CancellationToken cancellationToken)
    {
        if (client == null || client.GameWindowHandle == IntPtr.Zero)
        {
            return;
        }

        using var foregroundLease =
            await this.foregroundInputCoordinator
                .AcquireAsync(cancellationToken)
                .ConfigureAwait(true);

        var sent = await NativeMethods.TryForegroundTapKeyAsync(
                client.GameWindowHandle,
                step.Key,
                cancellationToken)
            .ConfigureAwait(true);

        if (!sent)
        {
            client.AutomationStatus =
                $"Could not focus the game window to send {step.Key}.";
        }
    }

    private async Task ExecuteTypeTextStepAsync(
        ClientInstance? client,
        FleetCommandStep step,
        FleetCommandExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (client == null || client.GameWindowHandle == IntPtr.Zero)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(step.Text))
        {
            return;
        }

        using var foregroundLease =
            await this.foregroundInputCoordinator
                .AcquireAsync(cancellationToken)
                .ConfigureAwait(true);

        var text = FormatTemplateText(step.Text, context);

        NativeMethods.FocusWindow(client.GameWindowHandle);

        await Task.Delay(
            50,
            cancellationToken).ConfigureAwait(true);

        SendKeys.SendWait(EscapeSendKeysText(text));

        await Task.Delay(
            50,
            cancellationToken).ConfigureAwait(true);
    }

    private async Task<bool> ExecuteChatCommandStepAsync(
        ClientInstance? client,
        FleetCommandStep step,
        FleetCommandExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (client == null ||
            client.GameWindowHandle == IntPtr.Zero ||
            string.IsNullOrWhiteSpace(step.Text))
        {
            return false;
        }

        var text = FormatTemplateText(step.Text, context);

        if (text.Length == 0 || text[0] != '/')
        {
            client.AutomationStatus =
                "Chat commands must start with a slash.";
            return false;
        }

        var capsLockWasEnabled = NativeMethods.IsCapsLockEnabled();

        try
        {
            if (capsLockWasEnabled &&
                !await NativeMethods.TrySetCapsLockEnabledAsync(
                        enabled: false,
                        cancellationToken)
                    .ConfigureAwait(true))
            {
                client.AutomationStatus =
                    "Could not turn Caps Lock off before sending the chat command.";
                return false;
            }

            var beginChatResult =
                await this.ExecuteNamedActionWithResultAsync(
                        client,
                        BuiltInInputActionProvider.BeginChannelMessageName,
                        cancellationToken)
                    .ConfigureAwait(true);

            if (!beginChatResult.Succeeded)
            {
                client.AutomationStatus = beginChatResult.Error;
                return false;
            }

            await Task.Delay(
                100,
                cancellationToken).ConfigureAwait(true);

            using var foregroundLease =
                await this.foregroundInputCoordinator
                    .AcquireAsync(cancellationToken)
                    .ConfigureAwait(true);

            NativeMethods.FocusWindow(client.GameWindowHandle);

            await Task.Delay(
                50,
                cancellationToken).ConfigureAwait(true);

            SendKeys.SendWait(EscapeSendKeysText(text));

            await Task.Delay(
                100,
                cancellationToken).ConfigureAwait(true);

            var submitted = await NativeMethods.TryForegroundTapKeyAsync(
                    client.GameWindowHandle,
                    Keys.Enter,
                    cancellationToken)
                .ConfigureAwait(true);

            if (!submitted)
            {
                client.AutomationStatus =
                    "Could not submit the chat command.";
            }

            return submitted;
        }
        finally
        {
            if (capsLockWasEnabled)
            {
                await RestoreCapsLockAsync(client).ConfigureAwait(true);
            }
        }
    }

    private static async Task RestoreCapsLockAsync(ClientInstance client)
    {
        try
        {
            if (await NativeMethods.TrySetCapsLockEnabledAsync(
                    enabled: true,
                    CancellationToken.None)
                .ConfigureAwait(true))
            {
                return;
            }

            client.AutomationStatus =
                "Caps Lock could not be restored after the chat-command attempt.";
            Debug.WriteLine(
                "Fleet chat command could not restore Caps Lock to its original state.");
        }
        catch (Exception exception)
        {
            client.AutomationStatus =
                "Caps Lock could not be restored after the chat-command attempt.";
            Debug.WriteLine(
                $"Fleet chat command Caps Lock restoration failed: {exception}");
        }
    }

    private static string EscapeSendKeysText(string text)
    {
        var result = new StringBuilder();

        foreach (var character in text)
        {
            if (character is '+' or '^' or '%' or '~' or '(' or ')' or '[' or ']' or '{' or '}')
            {
                result.Append('{');
                result.Append(character);
                result.Append('}');
                continue;
            }

            result.Append(character);
        }

        return result.ToString();
    }

    private static string FormatTemplateText(
        string text,
        FleetCommandExecutionContext context)
    {
        var result = text.Replace(
            "{pilot}",
            context.PilotName,
            StringComparison.OrdinalIgnoreCase);

        foreach (var argument in context.Arguments)
        {
            result = result.Replace(
                string.Concat("{", argument.Key, "}"),
                argument.Value,
                StringComparison.OrdinalIgnoreCase);
        }

        return result;
    }

    private FleetCommandExecutionContext BuildExecutionContext(
        FleetCommandDefinition command,
        FleetCommandInvocationContext invocationContext,
        IReadOnlyCollection<ClientInstance> clients,
        LayoutProfile profile,
        FleetCommandSettings settings,
        string pilotName)
    {
        var indexedSlots = profile.Slots
            .Select((slot, index) => new
            {
                Slot = slot,
                Index = index,
            })
            .ToList();

        var followers = clients
            .Select(client => new
            {
                Client = client,
                Slot = indexedSlots.FirstOrDefault(slot => slot.Slot.Id == client.AssignedSlotId),
            })
            .Where(item =>
                       item.Client.ProcessId != invocationContext.ActiveClient.ProcessId &&
                       item.Client.GameWindowHandle != IntPtr.Zero &&
                       item.Client.LifecycleState == ClientLifecycleState.InGame &&
                       item.Slot?.Slot.IncludeInAssistMe == true)
            .OrderBy(item => item.Slot?.Index ?? int.MaxValue)
            .Select(item => item.Client)
            .ToList();

        var target = this.ResolveTargetClient(
            command,
            clients);

        return new FleetCommandExecutionContext(
            invocationContext,
            invocationContext.ActiveClient,
            target,
            followers,
            settings,
            pilotName,
            command.Arguments);
    }

    private ClientInstance? ResolveTargetClient(
        FleetCommandDefinition command,
        IReadOnlyCollection<ClientInstance> clients)
    {
        if (!command.Arguments.TryGetValue("targetProcessId", out var value))
        {
            return null;
        }

        if (!int.TryParse(
                value,
                CultureInfo.InvariantCulture,
                out var processId))
        {
            return null;
        }

        return clients.FirstOrDefault(client => client.ProcessId == processId);
    }

    private static void RestorePilotFocus(FleetCommandExecutionContext context)
    {
        if (!context.Settings.ReturnFocusToMain)
        {
            return;
        }

        if (context.InvocationContext.ActiveClient.GameWindowHandle == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.FocusWindow(context.InvocationContext.ActiveClient.GameWindowHandle);
    }


    private sealed record InvokingPilotTargetPlan(
        string InvokingPilotName,
        int GroupMemberSlot,
        uint ExpectedTargetObjectId,
        string ExpectedTargetName)
    {
        private static readonly TimeSpan TargetSettleTimeout = TimeSpan.FromMilliseconds(1250);
        private static readonly TimeSpan TargetSettlePollInterval = TimeSpan.FromMilliseconds(50);

        public bool IsAlreadySelected(
            ClientInstance actingClient,
            ClientObservationCoordinator observationCoordinator)
        {
            return observationCoordinator.TryGetSnapshot(
                    actingClient.ProcessId,
                    out var snapshot) &&
                this.Matches(snapshot);
        }

        public async Task<bool> WaitUntilSelectedAsync(
            ClientInstance actingClient,
            ClientObservationCoordinator observationCoordinator,
            CancellationToken cancellationToken)
        {
            var startedAt = DateTimeOffset.UtcNow;

            while (DateTimeOffset.UtcNow - startedAt < TargetSettleTimeout)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (observationCoordinator.TryGetSnapshot(
                        actingClient.ProcessId,
                        out var snapshot) &&
                    this.Matches(snapshot))
                {
                    return true;
                }

                await Task.Delay(
                        TargetSettlePollInterval,
                        cancellationToken)
                    .ConfigureAwait(true);
            }

            return false;
        }

        private bool Matches(ClientObservationSnapshot snapshot)
        {
            if (!snapshot.Target.HasTarget)
            {
                return false;
            }

            if (this.ExpectedTargetObjectId != 0 &&
                snapshot.Target.ObjectId == this.ExpectedTargetObjectId)
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(this.ExpectedTargetName) &&
                string.Equals(
                    snapshot.Target.Name,
                    this.ExpectedTargetName,
                    StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed record FleetCommandExecutionContext(
        FleetCommandInvocationContext InvocationContext,
        ClientInstance Pilot,
        ClientInstance? Target,
        IReadOnlyList<ClientInstance> Followers,
        FleetCommandSettings Settings,
        string PilotName,
        IReadOnlyDictionary<string, string> Arguments);
}
