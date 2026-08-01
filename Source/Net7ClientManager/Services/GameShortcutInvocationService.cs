namespace Net7ClientManager.Services;

using Net7ClientManager.Models;
using Net7ClientManager.Observations;
using Net7ClientManager.Observations.Models;

internal sealed class GameShortcutInvocationService(
    GameCommandCoordinator gameCommandCoordinator,
    ClientObservationCoordinator observationCoordinator)
{
    private static readonly TimeSpan BankSettleTimeout =
        TimeSpan.FromMilliseconds(500);

    private static readonly TimeSpan BankSettlePollInterval =
        TimeSpan.FromMilliseconds(25);

    public async Task<GameCommandExecutionResult> ExecuteAsync(
        ClientInstance client,
        GameShortcutInvocation shortcut,
        CancellationToken cancellationToken)
    {
        if (client.GameWindowHandle == IntPtr.Zero)
        {
            return GameCommandExecutionResult.Failure(
                "The game window is unavailable.");
        }

        var slotCommand = GetSlotCommand(shortcut.VisibleKey);

        if (!this.TryReadCurrentGroup(
                client.ProcessId,
                shortcut.Bar,
                out var currentGroup,
                out var error))
        {
            return GameCommandExecutionResult.Failure(error);
        }

        if (shortcut.Group == 0)
        {
            if (currentGroup != 0 &&
                !await this.EnsureNormalGroupAsync(
                        client,
                        shortcut.Bar,
                        cancellationToken)
                    .ConfigureAwait(false))
            {
                return GameCommandExecutionResult.Failure(
                    string.Concat(
                        "Cannot invoke '",
                        shortcut.Name,
                        "' because shortcut bar ",
                        shortcut.Bar.ToString(
                            System.Globalization.CultureInfo.InvariantCulture),
                        " did not return to its normal bank."));
            }

            return await gameCommandCoordinator
                .ExecuteAsync(
                    client,
                    slotCommand,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        // Alternate shortcuts are momentary. Always hold the configured
        // Swap Shortcut Banks command while invoking the slot rather than
        // trusting a transient CurrentGroup sample from the game.
        return await gameCommandCoordinator
            .ExecuteWithHeldCommandAsync(
                client,
                slotCommand,
                GameCommand.SwapShortcutBanks,
                waitAfterHeldCommandAsync: token =>
                    this.WaitForGroupAsync(
                        client.ProcessId,
                        shortcut.Bar,
                        expectedGroup: 1,
                        token),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<bool> EnsureNormalGroupAsync(
        ClientInstance client,
        int bar,
        CancellationToken cancellationToken)
    {
        // A released Alt modifier normally returns the bar immediately. Give
        // the observer a compact grace period first so a stale sample does not
        // trigger unnecessary input.
        if (await this.WaitForGroupAsync(
                client.ProcessId,
                bar,
                expectedGroup: 0,
                cancellationToken)
            .ConfigureAwait(false))
        {
            return true;
        }

        // Shift Shortcuts is a hold-style Alt command. A clean tap followed by
        // release safely normalizes a bar that remained on the alternate bank
        // after an interrupted interaction.
        var normalized = await gameCommandCoordinator
            .ExecuteAsync(
                client,
                GameCommand.SwapShortcutBanks,
                cancellationToken)
            .ConfigureAwait(false);

        return normalized.Succeeded &&
            await this.WaitForGroupAsync(
                    client.ProcessId,
                    bar,
                    expectedGroup: 0,
                    cancellationToken)
                .ConfigureAwait(false);
    }

    private async Task<bool> WaitForGroupAsync(
        int processId,
        int bar,
        int expectedGroup,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + BankSettleTimeout;

        do
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (this.TryReadCurrentGroup(
                    processId,
                    bar,
                    out var currentGroup,
                    out _) &&
                currentGroup == expectedGroup)
            {
                return true;
            }

            await Task.Delay(
                    BankSettlePollInterval,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        while (DateTimeOffset.UtcNow < deadline);

        return false;
    }

    private bool TryReadCurrentGroup(
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
                bar.ToString(System.Globalization.CultureInfo.InvariantCulture),
                " is unavailable: ",
                shortcutBar?.Status ?? shortcuts.Status);
            return false;
        }

        currentGroup = observedGroup;
        return true;
    }

    private static GameCommand GetSlotCommand(int visibleKey)
    {
        return visibleKey switch
        {
            1 => GameCommand.FireActivateSlot1,
            2 => GameCommand.FireActivateSlot2,
            3 => GameCommand.FireActivateSlot3,
            4 => GameCommand.FireActivateSlot4,
            5 => GameCommand.FireActivateSlot5,
            6 => GameCommand.FireActivateSlot6,
            _ => throw new ArgumentOutOfRangeException(
                nameof(visibleKey),
                visibleKey,
                "Shortcut visible key must be 1..6."),
        };
    }
}
