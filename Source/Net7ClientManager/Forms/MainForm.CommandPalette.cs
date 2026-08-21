// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using System.ComponentModel;
using System.Diagnostics;
using Net7ClientManager.Models;
using Net7ClientManager.Observations;
using Net7ClientManager.Services;
using Net7ClientManager.Win32;

public sealed partial class MainForm
{
    private string? commandPaletteKeyboardHookError;

    private void ApplyCommandPaletteRuntimeSettings()
    {
        var settings = this.clientManager.FleetCommandSettings;
        settings.EnsureDefaults();

        var showMode = settings.EffectiveCommandMenuShowMode;

        if (showMode == CommandPaletteShowMode.Keybinding)
        {
            this.EnsureCommandPaletteKeyboardHook(
                settings.CommandMenuHotKey);
        }
        else
        {
            this.commandPaletteKeyboardHook?.Dispose();
            this.commandPaletteKeyboardHook = null;
            this.commandPaletteKeyboardHookError = null;
        }

        if (showMode == CommandPaletteShowMode.Never)
        {
            this.CloseCommandOverlay();
        }
        else if (showMode == CommandPaletteShowMode.Always)
        {
            if (this.commandOverlayForm is { IsPersistent: false })
            {
                this.CloseCommandOverlay();
            }

            this.RefreshCommandPaletteRuntime();
        }
        else if (this.commandOverlayForm is { IsPersistent: true })
        {
            this.CloseCommandOverlay();
        }
    }

    private void EnsureCommandPaletteKeyboardHook(Keys hotKey)
    {
        if (this.commandPaletteKeyboardHook != null)
        {
            this.commandPaletteKeyboardHook.UpdateHotKey(hotKey);
            this.commandPaletteKeyboardHookError = null;
            return;
        }

        try
        {
            var hook = new CommandPaletteKeyboardHook(
                hotKey,
                this.TryHandleCommandPaletteHotKey,
                this.HandleCommandPaletteHotKeyReleased);

            hook.Start();
            this.commandPaletteKeyboardHook = hook;
            this.commandPaletteKeyboardHookError = null;
        }
        catch (Win32Exception exception)
        {
            this.commandPaletteKeyboardHook?.Dispose();
            this.commandPaletteKeyboardHook = null;
            this.commandPaletteKeyboardHookError = string.Concat(
                "The Command Palette keyboard hook could not be installed: ",
                exception.Message);
        }
    }

    private bool TryHandleCommandPaletteHotKey()
    {
        var settings = this.clientManager.FleetCommandSettings;
        var shortcut = CommandPaletteHotKeyValidator.FormatHotKey(
            settings.CommandMenuHotKey);

        if (settings.EffectiveCommandMenuShowMode !=
                CommandPaletteShowMode.Keybinding)
        {
            this.RecordCommandPaletteDiagnostic(
                "Hotkey ignored",
                details: string.Concat(
                    "shortcut=",
                    shortcut,
                    "; reason=show mode is ",
                    settings.EffectiveCommandMenuShowMode));
            return false;
        }

        if (this.inGameOptionsOpen)
        {
            this.RecordCommandPaletteDiagnostic(
                "Hotkey ignored",
                details: string.Concat(
                    "shortcut=",
                    shortcut,
                    "; reason=In-Game Options is open"));
            return false;
        }

        var activeClient = this.clientManager.FindForegroundHostedClient();

        if (activeClient == null)
        {
            this.RecordCommandPaletteDiagnostic(
                "Hotkey ignored",
                details: string.Concat(
                    "shortcut=",
                    shortcut,
                    "; reason=no managed game client has foreground focus"));
            return false;
        }

        if (activeClient.LifecycleState != ClientLifecycleState.InGame)
        {
            this.RecordCommandPaletteDiagnostic(
                "Hotkey ignored",
                activeClient,
                string.Concat(
                    "shortcut=",
                    shortcut,
                    "; reason=client lifecycle is ",
                    activeClient.LifecycleState));
            return false;
        }

        var chatInputState = this.clientManager.ReadChatInputState(
            activeClient.ProcessId,
            out var chatInputStatus);

        if (chatInputState != ClientChatInputState.Inactive)
        {
            if (chatInputState == ClientChatInputState.Unknown)
            {
                Debug.WriteLine(
                    $"Command Palette shortcut passed through because chat input state is unknown: {chatInputStatus}");
            }

            this.RecordCommandPaletteDiagnostic(
                "Hotkey ignored",
                activeClient,
                string.Concat(
                    "shortcut=",
                    shortcut,
                    "; reason=chat input is ",
                    chatInputState,
                    "; status=",
                    chatInputStatus));
            return false;
        }

        var cursorPosition = NativeMethods.TryGetCursorScreenPosition(
            out var resolvedCursorPosition)
            ? resolvedCursorPosition
            : Cursor.Position;

        try
        {
            this.BeginInvoke(
                () => this.ShowTransientCommandOverlay(
                    activeClient,
                    cursorPosition));
        }
        catch (InvalidOperationException)
        {
            this.RecordCommandPaletteDiagnostic(
                "Hotkey ignored",
                activeClient,
                string.Concat(
                    "shortcut=",
                    shortcut,
                    "; reason=main window is unavailable"));
            return false;
        }

        this.RecordCommandPaletteDiagnostic(
            "Hotkey accepted",
            activeClient,
            string.Concat(
                "shortcut=",
                shortcut,
                "; placement=",
                settings.CommandMenuPlacement));
        return true;
    }

    private void HandleCommandPaletteHotKeyReleased()
    {
        try
        {
            this.BeginInvoke(
                this.CompleteTransientCommandOverlayFromHotKeyRelease);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private async void CompleteTransientCommandOverlayFromHotKeyRelease()
    {
        if (this.commandOverlayForm is
            {
                IsDisposed: false,
                Disposing: false,
                IsTransient: true,
            } form)
        {
            await form.CompleteFromHotKeyReleaseAsync()
                .ConfigureAwait(true);
        }
    }

    private void RefreshCommandPaletteRuntime()
    {
        var settings = this.clientManager.FleetCommandSettings;

        if (settings.EffectiveCommandMenuShowMode !=
                CommandPaletteShowMode.Always ||
            this.inGameOptionsOpen)
        {
            if (this.commandOverlayForm is { IsPersistent: true })
            {
                this.CloseCommandOverlay();
            }

            return;
        }

        var foregroundClient =
            this.clientManager.FindForegroundHostedClient();

        if (foregroundClient?.LifecycleState != ClientLifecycleState.InGame)
        {
            foregroundClient = null;
        }

        if (this.commandOverlayForm is
            {
                IsDisposed: false,
                Disposing: false,
                IsPersistent: true,
            } existing)
        {
            var owner = this.clientManager.Clients.FirstOrDefault(client =>
                client.ProcessId == existing.OwnerProcessId);

            if (owner?.LifecycleState != ClientLifecycleState.InGame)
            {
                this.CloseCommandOverlay();
            }
            else if (foregroundClient != null &&
                     foregroundClient.ProcessId != owner.ProcessId)
            {
                this.CloseCommandOverlay();
                this.ShowPersistentCommandOverlay(foregroundClient);
                return;
            }
            else
            {
                var gameBounds = this.GetGameScreenBounds(owner);
                existing.UpdatePersistentPlacement(
                    gameBounds,
                    ResolveFixedLocation(
                        gameBounds,
                        existing.Size,
                        settings.CommandMenuFixedPositionX,
                        settings.CommandMenuFixedPositionY));

                if (existing.RefreshPersistentPresentation())
                {
                    return;
                }

                this.CloseCommandOverlay();
                this.ShowPersistentCommandOverlay(owner);
                return;
            }
        }
        else if (this.commandOverlayForm != null)
        {
            this.CloseCommandOverlay();
        }

        if (foregroundClient != null)
        {
            this.ShowPersistentCommandOverlay(foregroundClient);
        }
    }

    private void ShowPersistentCommandOverlay(
        ClientInstance activeClient)
    {
        if (activeClient.LifecycleState != ClientLifecycleState.InGame)
        {
            return;
        }

        var settings = this.clientManager.FleetCommandSettings;
        var invocationContext = this.CreateInvocationContext(activeClient);
        var gameBounds = this.GetGameScreenBounds(activeClient);
        CommandOverlayForm? form = null;

        form = new CommandOverlayForm(
            this.clientManager,
            invocationContext,
            CommandPaletteBehavior.Persistent,
            gameBounds,
            location =>
            {
                if (form is { IsDisposed: false })
                {
                    this.SaveCommandPaletteFixedPosition(
                        activeClient,
                        form.Size,
                        location);
                }
            },
            diagnosticReporter: (eventName, details) =>
                this.RecordCommandPaletteDiagnostic(
                    eventName,
                    activeClient,
                    details));

        this.commandOverlayForm = form;
        form.FormClosed += this.CommandOverlayForm_OnFormClosed;
        form.Location = ResolveFixedLocation(
            gameBounds,
            form.Size,
            settings.CommandMenuFixedPositionX,
            settings.CommandMenuFixedPositionY);

        if (activeClient.HostForm is { IsDisposed: false } owner)
        {
            form.Show(owner);
        }
        else
        {
            form.Show();
        }
    }

    private void ShowTransientCommandOverlay(
        ClientInstance activeClient,
        Point cursorPosition)
    {
        var foregroundClient =
            this.clientManager.FindForegroundHostedClient();

        if (activeClient.LifecycleState != ClientLifecycleState.InGame ||
            foregroundClient?.ProcessId != activeClient.ProcessId ||
            this.clientManager.FleetCommandSettings
                .EffectiveCommandMenuShowMode !=
            CommandPaletteShowMode.Keybinding)
        {
            return;
        }

        if (this.commandOverlayForm is { IsDisposed: false })
        {
            this.CloseCommandOverlay();
            return;
        }

        var settings = this.clientManager.FleetCommandSettings;
        var invocationContext = this.CreateInvocationContext(activeClient);
        var gameBounds = this.GetGameScreenBounds(activeClient);
        var fixedPlacement = settings.CommandMenuPlacement ==
            CommandPalettePlacementMode.Fixed;

        var form = new CommandOverlayForm(
            this.clientManager,
            invocationContext,
            CommandPaletteBehavior.Transient,
            gameBounds,
            restoreCursorPosition: fixedPlacement
                ? cursorPosition
                : null,
            diagnosticReporter: (eventName, details) =>
                this.RecordCommandPaletteDiagnostic(
                    eventName,
                    activeClient,
                    details));

        this.commandOverlayForm = form;
        form.FormClosed += this.CommandOverlayForm_OnFormClosed;

        if (fixedPlacement)
        {
            form.Location = ResolveFixedLocation(
                gameBounds,
                form.Size,
                settings.CommandMenuFixedPositionX,
                settings.CommandMenuFixedPositionY);
        }
        else
        {
            var cursorAnchorPoint = form.GetCursorAnchorPoint();
            form.Location = ClampLocation(
                new Point(
                    cursorPosition.X - cursorAnchorPoint.X,
                    cursorPosition.Y - cursorAnchorPoint.Y),
                form.Size,
                gameBounds);
        }

        form.Show();
        form.Activate();
        this.RecordCommandPaletteDiagnostic(
            "Transient overlay opened",
            activeClient,
            string.Concat(
                "placement=",
                settings.CommandMenuPlacement));

        var anchorScreenPoint = form.PointToScreen(
            form.GetCursorAnchorPoint());

        _ = NativeMethods.MoveCursorToScreenPoint(
            fixedPlacement
                ? anchorScreenPoint
                : cursorPosition);
    }

    private FleetCommandInvocationContext CreateInvocationContext(
        ClientInstance activeClient)
    {
        Point? activeClientMousePosition = null;

        if (NativeMethods.TryGetCursorPositionRelativeToClient(
                activeClient.GameWindowHandle,
                out var mousePosition))
        {
            activeClientMousePosition = mousePosition;
        }

        return new FleetCommandInvocationContext
        {
            ActiveClient = activeClient,
            ActiveClientMousePosition = activeClientMousePosition,
        };
    }

    private CommandPaletteFixedPosition? PickCommandPaletteFixedPosition(
        int processId,
        IWin32Window owner)
    {
        var client = this.clientManager.Clients.FirstOrDefault(candidate =>
            candidate.ProcessId == processId);

        if (client == null ||
            client.LifecycleState != ClientLifecycleState.InGame)
        {
            return null;
        }

        this.CloseCommandOverlay();

        var settings = this.clientManager.FleetCommandSettings;
        var invocationContext = this.CreateInvocationContext(client);
        var gameBounds = this.GetGameScreenBounds(client);

        using var preview = new CommandOverlayForm(
            this.clientManager,
            invocationContext,
            CommandPaletteBehavior.PositionPreview,
            gameBounds);

        preview.Location = ResolveFixedLocation(
            gameBounds,
            preview.Size,
            settings.CommandMenuFixedPositionX,
            settings.CommandMenuFixedPositionY);

        var result = preview.ShowDialog(owner);

        return result == DialogResult.OK
            ? NormalizeFixedPosition(
                gameBounds,
                preview.Size,
                preview.Location)
            : null;
    }

    private void SaveCommandPaletteFixedPosition(
        ClientInstance client,
        Size formSize,
        Point location)
    {
        if (client.LifecycleState != ClientLifecycleState.InGame ||
            formSize.Width <= 0 ||
            formSize.Height <= 0)
        {
            return;
        }

        var normalized = NormalizeFixedPosition(
            this.GetGameScreenBounds(client),
            formSize,
            location);

        var settings = this.clientManager.FleetCommandSettings;
        var previousX = settings.CommandMenuFixedPositionX;
        var previousY = settings.CommandMenuFixedPositionY;
        settings.CommandMenuFixedPositionX = normalized.X;
        settings.CommandMenuFixedPositionY = normalized.Y;

        try
        {
            this.clientManager.SaveSettings();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            settings.CommandMenuFixedPositionX = previousX;
            settings.CommandMenuFixedPositionY = previousY;
            Debug.WriteLine(
                $"Command Palette position could not be saved: {exception}");
        }
    }

    private Rectangle GetGameScreenBounds(ClientInstance client)
    {
        var windowBounds = NativeMethods.GetWindowBounds(
            client.GameWindowHandle);

        if (windowBounds != null &&
            windowBounds.Value.Width > 0 &&
            windowBounds.Value.Height > 0)
        {
            return new Rectangle(
                windowBounds.Value.Left,
                windowBounds.Value.Top,
                windowBounds.Value.Width,
                windowBounds.Value.Height);
        }

        return client.HostForm?.Bounds ??
               Screen.FromPoint(Cursor.Position).WorkingArea;
    }

    private void CommandOverlayForm_OnFormClosed(
        object? sender,
        FormClosedEventArgs e)
    {
        if (sender is CommandOverlayForm form)
        {
            form.FormClosed -= this.CommandOverlayForm_OnFormClosed;
        }

        if (ReferenceEquals(this.commandOverlayForm, sender))
        {
            this.commandOverlayForm = null;
        }
    }

    private void CloseCommandOverlay()
    {
        var form = this.commandOverlayForm;
        this.commandOverlayForm = null;

        if (form == null)
        {
            return;
        }

        form.FormClosed -= this.CommandOverlayForm_OnFormClosed;

        if (!form.IsDisposed && !form.Disposing)
        {
            form.Close();
        }
    }

    private static Point ResolveFixedLocation(
        Rectangle gameBounds,
        Size formSize,
        double normalizedX,
        double normalizedY)
    {
        var availableWidth = Math.Max(0, gameBounds.Width - formSize.Width);
        var availableHeight = Math.Max(0, gameBounds.Height - formSize.Height);

        return ClampLocation(
            new Point(
                gameBounds.Left + (int)Math.Round(
                    availableWidth * Math.Clamp(normalizedX, 0.0, 1.0)),
                gameBounds.Top + (int)Math.Round(
                    availableHeight * Math.Clamp(normalizedY, 0.0, 1.0))),
            formSize,
            gameBounds);
    }

    private static CommandPaletteFixedPosition NormalizeFixedPosition(
        Rectangle gameBounds,
        Size formSize,
        Point location)
    {
        var availableWidth = Math.Max(0, gameBounds.Width - formSize.Width);
        var availableHeight = Math.Max(0, gameBounds.Height - formSize.Height);

        return new CommandPaletteFixedPosition(
            availableWidth == 0
                ? 0.0
                : Math.Clamp(
                    (location.X - gameBounds.Left) /
                    (double)availableWidth,
                    0.0,
                    1.0),
            availableHeight == 0
                ? 0.0
                : Math.Clamp(
                    (location.Y - gameBounds.Top) /
                    (double)availableHeight,
                    0.0,
                    1.0));
    }

    private static Point ClampLocation(
        Point requested,
        Size size,
        Rectangle bounds)
    {
        var maximumX = Math.Max(bounds.Left, bounds.Right - size.Width);
        var maximumY = Math.Max(bounds.Top, bounds.Bottom - size.Height);

        return new Point(
            Math.Clamp(requested.X, bounds.Left, maximumX),
            Math.Clamp(requested.Y, bounds.Top, maximumY));
    }
}
