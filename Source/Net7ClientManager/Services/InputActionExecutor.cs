namespace Net7ClientManager.Services;

using Net7ClientManager.Models;
using Net7ClientManager.Win32;

public sealed class InputActionExecutor
{
    public async Task ExecuteAsync(
        ClientInstance client,
        InputActionDefinition action,
        CancellationToken cancellationToken)
    {
        if (client.GameWindowHandle == IntPtr.Zero)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        switch (action.Kind)
        {
            case InputActionKind.KeyTap:
                await NativeMethods.ForegroundTapKeyAsync(
                    client.GameWindowHandle,
                    action.Key).ConfigureAwait(true);
                return;

            case InputActionKind.MouseClick:
                this.ExecuteMouseClick(client, action);
                return;

            default:
                throw new InvalidOperationException($"Unsupported input action kind '{action.Kind}'.");
        }
    }

    private void ExecuteMouseClick(ClientInstance client, InputActionDefinition action)
    {
        if (!NativeMethods.TryGetClientSize(client.GameWindowHandle, out var clientSize))
        {
            throw new InvalidOperationException("Could not get client size.");
        }

        var x = (int)Math.Round(action.BaseX * clientSize.Width / action.BaseWidth);
        var y = (int)Math.Round(action.BaseY * clientSize.Height / action.BaseHeight);

        var clicked = NativeMethods.ForegroundLeftClick(
            client.GameWindowHandle,
            x,
            y);

        if (!clicked)
        {
            throw new InvalidOperationException($"Click failed for action '{action.Name}'.");
        }
    }
}
