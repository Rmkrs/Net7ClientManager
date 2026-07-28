namespace Net7ClientManager.Services;

using System.Drawing;
using Net7ClientManager.Models;
using Net7ClientManager.Win32;

internal sealed class InputActionExecutor(
    GameCommandCoordinator gameCommandCoordinator,
    ForegroundInputCoordinator foregroundInputCoordinator)
{
    public async Task<InputActionExecutionResult> ExecuteAsync(
        ClientInstance client,
        InputActionDefinition action,
        CancellationToken cancellationToken)
    {
        if (client.GameWindowHandle == IntPtr.Zero)
        {
            return InputActionExecutionResult.Failure(
                "The game window is unavailable.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        switch (action.Kind)
        {
            case InputActionKind.GameCommand:
                return await this.ExecuteGameCommandAsync(
                        client,
                        action,
                        cancellationToken)
                    .ConfigureAwait(false);

            case InputActionKind.KeyTap:
                return await this.ExecuteRawKeyTapAsync(
                        client,
                        action,
                        cancellationToken)
                    .ConfigureAwait(false);

            case InputActionKind.MouseClick:
                return await this.ExecuteMouseClickAsync(
                        client,
                        action,
                        cancellationToken)
                    .ConfigureAwait(false);

            default:
                return InputActionExecutionResult.Failure(
                    $"Unsupported input action kind '{action.Kind}'.");
        }
    }

    private async Task<InputActionExecutionResult> ExecuteGameCommandAsync(
        ClientInstance client,
        InputActionDefinition action,
        CancellationToken cancellationToken)
    {
        if (action.GameCommand is not { } gameCommand)
        {
            return InputActionExecutionResult.Failure(
                $"Semantic action '{action.Name}' does not identify a game command.");
        }

        var result = await gameCommandCoordinator
            .ExecuteAsync(
                client,
                gameCommand,
                cancellationToken)
            .ConfigureAwait(false);

        return result.Succeeded
            ? InputActionExecutionResult.Success()
            : InputActionExecutionResult.Failure(result.Error);
    }

    private async Task<InputActionExecutionResult> ExecuteRawKeyTapAsync(
        ClientInstance client,
        InputActionDefinition action,
        CancellationToken cancellationToken)
    {
        using var foregroundLease =
            await foregroundInputCoordinator
                .AcquireAsync(cancellationToken)
                .ConfigureAwait(false);

        var sent = await NativeMethods.TryForegroundTapKeyAsync(
                client.GameWindowHandle,
                action.Key,
                cancellationToken)
            .ConfigureAwait(false);

        return sent
            ? InputActionExecutionResult.Success()
            : InputActionExecutionResult.Failure(
                $"Could not focus the game window for action '{action.Name}'.");
    }

    private async Task<InputActionExecutionResult> ExecuteMouseClickAsync(
        ClientInstance client,
        InputActionDefinition action,
        CancellationToken cancellationToken)
    {
        using var foregroundLease =
            await foregroundInputCoordinator
                .AcquireAsync(cancellationToken)
                .ConfigureAwait(false);

        if (!NativeMethods.TryGetClientSize(
                client.GameWindowHandle,
                out var clientSize))
        {
            return InputActionExecutionResult.Failure(
                "Could not get the game viewport size.");
        }

        var x = (int)Math.Round(
            action.BaseX * clientSize.Width / action.BaseWidth);

        var y = (int)Math.Round(
            action.BaseY * clientSize.Height / action.BaseHeight);

        if (!NativeMethods.TryConvertClientPointToScreen(
                client.GameWindowHandle,
                new Point(x, y),
                out var screenPoint))
        {
            return InputActionExecutionResult.Failure(
                $"Could not translate click point for action '{action.Name}'.");
        }

        NativeMethods.FocusWindow(client.GameWindowHandle);

        await Task.Delay(
                TimeSpan.FromMilliseconds(75),
                cancellationToken)
            .ConfigureAwait(false);

        if (!NativeMethods.MoveCursorToScreenPoint(screenPoint))
        {
            return InputActionExecutionResult.Failure(
                $"Could not move cursor for action '{action.Name}'.");
        }

        await NativeMethods
            .StableLeftClickAtCurrentCursorAsync(cancellationToken)
            .ConfigureAwait(false);

        return InputActionExecutionResult.Success();
    }
}
