namespace Net7ClientManager.Services;

using Net7ClientManager.Models;
using Net7ClientManager.Win32;

public sealed class FleetCommandService
{
    private static readonly TimeSpan assistMeTitleStatusDuration = TimeSpan.FromSeconds(5);

    private readonly InputActionStore inputActionStore = new();
    private readonly InputActionExecutor inputActionExecutor = new();

    private CancellationTokenSource? activeCommandCancellation;
    private bool commandRunning;

    public async Task AssistMeAsync(
        FleetCommandInvocationContext invocationContext,
        IReadOnlyCollection<ClientInstance> clients,
        LayoutProfile profile,
        FleetCommandSettings settings,
        string mainPilotName)
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
            await this.AssistMeCoreAsync(
                invocationContext,
                clients,
                profile,
                settings,
                mainPilotName,
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

    private async Task AssistMeCoreAsync(
        FleetCommandInvocationContext invocationContext,
        IReadOnlyCollection<ClientInstance> clients,
        LayoutProfile profile,
        FleetCommandSettings settings,
        string mainPilotName,
        CancellationToken cancellationToken)
    {
        var actions = this.inputActionStore.LoadActions();

        var targetAction = FindAction(actions, settings.AssistMeTargetActionName);
        var fireAction = FindAction(actions, settings.AssistMeFireActionName);

        if (targetAction == null || fireAction == null)
        {
            this.RestorePilotFocus(invocationContext, settings);
            return;
        }

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
                       item.Client.State == ClientState.EnteringGame &&
                       item.Slot?.Slot.IncludeInAssistMe == true)
            .OrderBy(item => item.Slot?.Index ?? int.MaxValue)
            .Select(item => item.Client)
            .ToList();

        try
        {
            foreach (var follower in followers)
            {
                cancellationToken.ThrowIfCancellationRequested();

                follower.HostForm?.SetTemporaryTitleStatus(
                    string.Concat("Assisting ", mainPilotName),
                    assistMeTitleStatusDuration);

                await this.inputActionExecutor.ExecuteAsync(
                    follower,
                    targetAction,
                    cancellationToken).ConfigureAwait(true);

                await Task.Delay(
                    settings.DelayAfterAssistMilliseconds,
                    cancellationToken).ConfigureAwait(true);

                await this.inputActionExecutor.ExecuteAsync(
                    follower,
                    fireAction,
                    cancellationToken).ConfigureAwait(true);

                await Task.Delay(
                    settings.DelayBetweenClientsMilliseconds,
                    cancellationToken).ConfigureAwait(true);
            }
        }
        finally
        {
            this.RestorePilotFocus(invocationContext, settings);
        }
    }

    private void RestorePilotFocus(
        FleetCommandInvocationContext invocationContext,
        FleetCommandSettings settings)
    {
        if (!settings.ReturnFocusToMain)
        {
            return;
        }

        if (invocationContext.ActiveClient.GameWindowHandle == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.FocusWindow(invocationContext.ActiveClient.GameWindowHandle);

        if (invocationContext.ActiveClientMousePosition is not { } mousePosition)
        {
            return;
        }

        _ = NativeMethods.MoveCursorToClientPoint(
            invocationContext.ActiveClient.GameWindowHandle,
            mousePosition.X,
            mousePosition.Y);
    }

    private static InputActionDefinition? FindAction(
        IEnumerable<InputActionDefinition> actions,
        string actionName)
    {
        return actions.FirstOrDefault(action =>
            string.Equals(
                action.Name,
                actionName,
                StringComparison.OrdinalIgnoreCase));
    }
}
