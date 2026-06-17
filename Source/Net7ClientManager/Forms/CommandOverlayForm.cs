namespace Net7ClientManager.Forms;

using Net7ClientManager.Core;
using Net7ClientManager.Models;
using Net7ClientManager.Win32;

public sealed class CommandOverlayForm : Form
{
    private readonly ClientManager clientManager;
    private readonly FleetCommandInvocationContext invocationContext;
    private readonly Keys commandMenuHotKey;
    private readonly System.Windows.Forms.Timer releaseTimer = new();

    private readonly CommandTileLabel assistMeTile = new(CommandOverlayCommand.AssistMe);
    private readonly CommandTileLabel stopTile = new(CommandOverlayCommand.Stop);

    private CommandOverlayCommand hoveredCommand = CommandOverlayCommand.None;
    private bool completed;

    public CommandOverlayForm(
        ClientManager clientManager,
        FleetCommandInvocationContext invocationContext,
        Keys commandMenuHotKey)
    {
        this.clientManager = clientManager;
        this.invocationContext = invocationContext;
        this.commandMenuHotKey = commandMenuHotKey;

        this.Text = "Fleet Commands";
        this.FormBorderStyle = FormBorderStyle.None;
        this.StartPosition = FormStartPosition.Manual;
        this.TopMost = true;
        this.ShowInTaskbar = false;
        this.ClientSize = new Size(170, 74);
        this.Padding = new Padding(4);
        this.KeyPreview = true;
        this.BackColor = Color.FromArgb(28, 28, 28);
        this.Font = new Font(
            SystemFonts.MessageBoxFont?.FontFamily ?? SystemFonts.DefaultFont.FontFamily,
            9.5f,
            FontStyle.Bold);

        this.BuildUi();

        this.releaseTimer.Interval = 25;
        this.releaseTimer.Tick += this.ReleaseTimer_OnTick;

        this.Deactivate += this.CommandOverlayForm_OnDeactivate;
        this.KeyDown += this.CommandOverlayForm_OnKeyDown;
        this.MouseUp += this.CommandOverlayForm_OnMouseUp;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        this.releaseTimer.Start();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        this.releaseTimer.Stop();
        this.releaseTimer.Tick -= this.ReleaseTimer_OnTick;

        this.Deactivate -= this.CommandOverlayForm_OnDeactivate;
        this.KeyDown -= this.CommandOverlayForm_OnKeyDown;
        this.MouseUp -= this.CommandOverlayForm_OnMouseUp;

        this.UnwireTile(this.assistMeTile);
        this.UnwireTile(this.stopTile);

        base.OnFormClosed(e);
    }

    private void BuildUi()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = Padding.Empty,
            Margin = Padding.Empty,
            BackColor = Color.FromArgb(28, 28, 28),
        };

        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        this.ConfigureTile(
            this.assistMeTile,
            "Assist Me",
            bottomMargin: 2);

        this.ConfigureTile(
            this.stopTile,
            "Stop",
            bottomMargin: 0);

        layout.Controls.Add(this.assistMeTile, column: 0, row: 0);
        layout.Controls.Add(this.stopTile, column: 0, row: 1);

        this.Controls.Add(layout);
    }

    private void ConfigureTile(
        CommandTileLabel tile,
        string text,
        int bottomMargin)
    {
        tile.Dock = DockStyle.Fill;
        tile.Margin = new Padding(0, 0, 0, bottomMargin);
        tile.Text = text;
        tile.TextAlign = ContentAlignment.MiddleCenter;
        tile.ForeColor = Color.White;
        tile.BackColor = Color.FromArgb(52, 52, 52);
        tile.Cursor = Cursors.Hand;

        tile.MouseEnter += this.CommandTile_OnMouseEnter;
        tile.MouseLeave += this.CommandTile_OnMouseLeave;
        tile.MouseUp += this.CommandOverlayForm_OnMouseUp;
    }

    private void UnwireTile(CommandTileLabel tile)
    {
        tile.MouseEnter -= this.CommandTile_OnMouseEnter;
        tile.MouseLeave -= this.CommandTile_OnMouseLeave;
        tile.MouseUp -= this.CommandOverlayForm_OnMouseUp;
    }

    private void CommandTile_OnMouseEnter(object? sender, EventArgs e)
    {
        if (sender is CommandTileLabel tile)
        {
            this.SetHoveredCommand(tile.Command);
        }
    }

    private void CommandTile_OnMouseLeave(object? sender, EventArgs e)
    {
        this.SetHoveredCommand(CommandOverlayCommand.None);
    }

    private void SetHoveredCommand(CommandOverlayCommand command)
    {
        this.hoveredCommand = command;

        this.SetTileState(
            this.assistMeTile,
            command == CommandOverlayCommand.AssistMe);

        this.SetTileState(
            this.stopTile,
            command == CommandOverlayCommand.Stop);
    }

    private void SetTileState(Label tile, bool selected)
    {
        tile.BackColor = selected
            ? Color.FromArgb(84, 112, 164)
            : Color.FromArgb(52, 52, 52);

        tile.ForeColor = Color.White;
    }

    private async void ReleaseTimer_OnTick(object? sender, EventArgs e)
    {
        if (!this.Bounds.Contains(Cursor.Position))
        {
            await this.CompleteAsync(
                CommandOverlayCommand.None,
                restoreOriginalFocusAndMouse: false).ConfigureAwait(true);

            return;
        }

        if (this.IsAnyCommandMenuHotKeyPartDown())
        {
            return;
        }

        await this.CompleteAsync(
            this.hoveredCommand,
            restoreOriginalFocusAndMouse: true).ConfigureAwait(true);
    }

    private async void CommandOverlayForm_OnMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
        {
            await this.CompleteAsync(
                CommandOverlayCommand.None,
                restoreOriginalFocusAndMouse: true).ConfigureAwait(true);

            return;
        }

        if (e.Button == MouseButtons.Left && this.hoveredCommand != CommandOverlayCommand.None)
        {
            await this.CompleteAsync(
                this.hoveredCommand,
                restoreOriginalFocusAndMouse: true).ConfigureAwait(true);
        }
    }

    private async void CommandOverlayForm_OnDeactivate(object? sender, EventArgs e)
    {
        await this.CompleteAsync(
            CommandOverlayCommand.None,
            restoreOriginalFocusAndMouse: true).ConfigureAwait(true);
    }

    private async void CommandOverlayForm_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            await this.CompleteAsync(
                CommandOverlayCommand.None,
                restoreOriginalFocusAndMouse: true).ConfigureAwait(true);
        }
    }

    private async Task CompleteAsync(
        CommandOverlayCommand command,
        bool restoreOriginalFocusAndMouse)
    {
        if (this.completed)
        {
            return;
        }

        this.completed = true;
        this.releaseTimer.Stop();

        try
        {
            switch (command)
            {
                case CommandOverlayCommand.AssistMe:
                    this.Hide();
                    await this.clientManager.AssistMeAsync(this.invocationContext).ConfigureAwait(true);
                    return;

                case CommandOverlayCommand.Stop:
                    this.clientManager.CancelFleetCommand();

                    if (restoreOriginalFocusAndMouse)
                    {
                        this.RestoreOriginalFocusAndMouse();
                    }

                    return;

                case CommandOverlayCommand.None:
                    if (restoreOriginalFocusAndMouse)
                    {
                        this.RestoreOriginalFocusAndMouse();
                    }

                    return;

                default:
                    if (restoreOriginalFocusAndMouse)
                    {
                        this.RestoreOriginalFocusAndMouse();
                    }

                    return;
            }
        }
        finally
        {
            this.Close();
        }
    }

    private void RestoreOriginalFocusAndMouse()
    {
        if (this.invocationContext.ActiveClient.GameWindowHandle == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.FocusWindow(this.invocationContext.ActiveClient.GameWindowHandle);

        if (this.invocationContext.ActiveClientMousePosition is not { } mousePosition)
        {
            return;
        }

        _ = NativeMethods.MoveCursorToClientPoint(
            this.invocationContext.ActiveClient.GameWindowHandle,
            mousePosition.X,
            mousePosition.Y);
    }

    private bool IsAnyCommandMenuHotKeyPartDown()
    {
        var keyCode = this.commandMenuHotKey & Keys.KeyCode;

        if (keyCode != Keys.None && NativeMethods.IsKeyDown(keyCode))
        {
            return true;
        }

        var requiredModifiers = this.commandMenuHotKey & Keys.Modifiers;

        if ((requiredModifiers & Keys.Control) == Keys.Control &&
            this.IsAnyControlKeyDown())
        {
            return true;
        }

        if ((requiredModifiers & Keys.Shift) == Keys.Shift &&
            this.IsAnyShiftKeyDown())
        {
            return true;
        }

        if ((requiredModifiers & Keys.Alt) == Keys.Alt &&
            this.IsAnyAltKeyDown())
        {
            return true;
        }

        return false;
    }

    private bool IsAnyControlKeyDown()
    {
        return NativeMethods.IsKeyDown(Keys.ControlKey) ||
               NativeMethods.IsKeyDown(Keys.LControlKey) ||
               NativeMethods.IsKeyDown(Keys.RControlKey);
    }

    private bool IsAnyShiftKeyDown()
    {
        return NativeMethods.IsKeyDown(Keys.ShiftKey) ||
               NativeMethods.IsKeyDown(Keys.LShiftKey) ||
               NativeMethods.IsKeyDown(Keys.RShiftKey);
    }

    private bool IsAnyAltKeyDown()
    {
        return NativeMethods.IsKeyDown(Keys.Menu) ||
               NativeMethods.IsKeyDown(Keys.LMenu) ||
               NativeMethods.IsKeyDown(Keys.RMenu);
    }

    private enum CommandOverlayCommand
    {
        None,
        AssistMe,
        Stop,
    }

    private sealed class CommandTileLabel(CommandOverlayCommand command) : Label
    {
        [System.ComponentModel.Browsable(false)]
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public CommandOverlayCommand Command { get; } = command;
    }
}
