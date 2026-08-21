// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using System.Globalization;
using Net7ClientManager.Core;
using Net7ClientManager.Models;
using Net7ClientManager.Services;
using Net7ClientManager.Win32;

internal enum CommandPaletteBehavior
{
    Transient,
    Persistent,
    PositionPreview,
}

internal sealed class CommandOverlayForm : Form
{
    private const int HeaderHeight = 18;
    private const int TileHeight = 34;
    private const int TileGap = 4;
    private const int SectionGap = 6;
    private const int ColumnCount = 2;
    private const int OverlayWidth = 420;
    private const int PreviewFooterHeight = 28;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;

    private readonly ClientManager clientManager;
    private readonly FleetCommandInvocationContext invocationContext;
    private readonly CommandPaletteBehavior behavior;
    private Rectangle movementBounds;
    private readonly Action<Point>? persistentLocationChanged;
    private readonly System.Windows.Forms.Timer presentationTimer = new();
    private readonly Point? restoreCursorPosition;
    private readonly Action<string, string?>? diagnosticReporter;

    private readonly List<CommandTileLabel> tiles = [];
    private readonly List<Control> dragSurfaces = [];

    private Point cursorAnchorPoint;
    private string? hoveredCommandId;
    private DateTimeOffset nextPresentationRefreshAt;
    private string commandSignature = string.Empty;
    private Point dragCursorOrigin;
    private Point dragLocationOrigin;
    private Control? dragCaptureControl;
    private bool dragging;
    private bool completed;
    private bool executingPersistentCommand;

    internal CommandOverlayForm(
        ClientManager clientManager,
        FleetCommandInvocationContext invocationContext,
        CommandPaletteBehavior behavior = CommandPaletteBehavior.Transient,
        Rectangle? movementBounds = null,
        Action<Point>? persistentLocationChanged = null,
        Point? restoreCursorPosition = null,
        Action<string, string?>? diagnosticReporter = null)
    {
        this.clientManager = clientManager;
        this.invocationContext = invocationContext;
        this.behavior = behavior;
        this.movementBounds = movementBounds ?? Screen.PrimaryScreen?.WorkingArea ?? Rectangle.Empty;
        this.persistentLocationChanged = persistentLocationChanged;
        this.restoreCursorPosition = restoreCursorPosition;
        this.diagnosticReporter = diagnosticReporter;

        this.Text = "Fleet Commands";
        this.FormBorderStyle = FormBorderStyle.None;
        this.StartPosition = FormStartPosition.Manual;
        this.TopMost = behavior != CommandPaletteBehavior.Persistent;
        this.ShowInTaskbar = false;
        this.Padding = new Padding(4);
        this.KeyPreview = true;
        this.BackColor = MainWindowTheme.Background;
        this.Font = MainWindowTheme.CreateHeadingFont(9.0f);
        this.Cursor = behavior == CommandPaletteBehavior.Transient
            ? Cursors.Hand
            : Cursors.Default;

        this.BuildUi();

        this.presentationTimer.Interval = 25;
        this.presentationTimer.Tick += this.PresentationTimer_OnTick;

        this.Deactivate += this.CommandOverlayForm_OnDeactivate;
        this.KeyDown += this.CommandOverlayForm_OnKeyDown;
        this.MouseUp += this.CommandOverlayForm_OnMouseUp;
    }

    public int OwnerProcessId =>
        this.invocationContext.ActiveClient.ProcessId;

    public bool IsPersistent =>
        this.behavior == CommandPaletteBehavior.Persistent;

    public bool IsTransient =>
        this.behavior == CommandPaletteBehavior.Transient;

    protected override bool ShowWithoutActivation =>
        this.behavior == CommandPaletteBehavior.Persistent;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= WsExToolWindow;

            if (this.behavior == CommandPaletteBehavior.Persistent)
            {
                parameters.ExStyle |= WsExNoActivate;
            }

            return parameters;
        }
    }

    public Point GetCursorAnchorPoint()
    {
        return this.cursorAnchorPoint;
    }

    public void UpdatePersistentPlacement(
        Rectangle bounds,
        Point location)
    {
        if (!this.IsPersistent)
        {
            return;
        }

        this.movementBounds = bounds;

        if (!this.dragging)
        {
            this.Location = ClampLocation(
                location,
                this.Size,
                bounds);
        }
    }

    public bool RefreshPersistentPresentation()
    {
        if (!this.IsPersistent)
        {
            return true;
        }

        var commands = this.clientManager.GetFleetCommandDefinitions(
            this.invocationContext);

        if (!string.Equals(
                this.commandSignature,
                BuildCommandSignature(commands),
                StringComparison.Ordinal))
        {
            return false;
        }

        this.RefreshCommandDefinitions(commands);
        return true;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        if (this.behavior == CommandPaletteBehavior.Transient)
        {
            this.presentationTimer.Start();
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        this.presentationTimer.Stop();
        this.presentationTimer.Tick -= this.PresentationTimer_OnTick;
        this.presentationTimer.Dispose();

        this.Deactivate -= this.CommandOverlayForm_OnDeactivate;
        this.KeyDown -= this.CommandOverlayForm_OnKeyDown;
        this.MouseUp -= this.CommandOverlayForm_OnMouseUp;

        foreach (var tile in this.tiles)
        {
            this.UnwireTile(tile);
        }

        this.tiles.Clear();

        foreach (var surface in this.dragSurfaces)
        {
            this.UnwireDragSurface(surface);
        }

        this.dragSurfaces.Clear();
        base.OnFormClosed(e);
    }

    private void BuildUi()
    {
        var commands = this.clientManager.GetFleetCommandDefinitions(this.invocationContext);
        var layout = this.BuildLayout(commands);
        this.commandSignature = BuildCommandSignature(commands);

        var rowCount = layout.RowHeights.Count;
        var contentHeight = layout.RowHeights.Sum() + Math.Max(0, rowCount - 1) * TileGap;

        this.ClientSize = new Size(
            OverlayWidth,
            this.Padding.Vertical + contentHeight +
            (this.behavior == CommandPaletteBehavior.PositionPreview
                ? PreviewFooterHeight
                : 0));

        this.cursorAnchorPoint = this.CalculateCursorAnchorPoint(layout.Items);

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = ColumnCount,
            RowCount = rowCount,
            Padding = Padding.Empty,
            Margin = Padding.Empty,
            BackColor = MainWindowTheme.Background,
            Cursor = this.behavior == CommandPaletteBehavior.Transient
                ? Cursors.Hand
                : Cursors.Default,
        };

        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        for (var row = 0; row < layout.RowHeights.Count; row++)
        {
            var rowGap = row == layout.RowHeights.Count - 1
                ? 0
                : TileGap;

            table.RowStyles.Add(new RowStyle(
                SizeType.Absolute,
                layout.RowHeights[row] + rowGap));
        }

        foreach (var item in layout.Items)
        {
            Control control = item.IsHeader
                ? this.CreateHeaderLabel(item.Label)
                : this.CreateCommandTile(
                    item.CommandId,
                    item.Label,
                    item.IsEnabled);

            table.Controls.Add(
                control,
                item.Column,
                item.Row);

            if (item.ColumnSpan > 1)
            {
                table.SetColumnSpan(
                    control,
                    item.ColumnSpan);
            }
        }

        if (this.behavior == CommandPaletteBehavior.PositionPreview)
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                BackColor = MainWindowTheme.Background,
            };

            root.ColumnStyles.Add(
                new ColumnStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(
                new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(
                new RowStyle(SizeType.Absolute, PreviewFooterHeight));

            var footer = new Label
            {
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                Text = "Drag a section heading · Enter saves · Esc cancels",
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = MainWindowTheme.Accent,
                BackColor = MainWindowTheme.ElevatedPanel,
                Font = MainWindowTheme.CreateBodyFont(8.2f),
            };

            root.Controls.Add(table, 0, 0);
            root.Controls.Add(footer, 0, 1);
            this.Controls.Add(root);
        }
        else
        {
            this.Controls.Add(table);
        }

        if (this.behavior != CommandPaletteBehavior.Transient)
        {
            this.WireDragSurface(this);
            this.WireDragSurface(table);
        }
    }

    private CommandPaletteLayout BuildLayout(
        IReadOnlyCollection<FleetCommandDefinition> commands)
    {
        var layout = new CommandPaletteLayout();

        var visibleCommands = commands
            .Where(command => command.ShowInOverlay)
            .ToList();

        this.AddLinearCategory(
            layout,
            "GROUP",
            visibleCommands
                .Where(command => command.Category == FleetCommandCategory.Group)
                .ToList(),
            emptyPlaceholder: "No live pilots");

        this.AddFormationCategory(layout, visibleCommands);

        this.AddCombatCategory(layout, visibleCommands);

        this.AddSingleSlotCategory(
            layout,
            "INTERACT",
            visibleCommands.FirstOrDefault(command =>
                command.Category == FleetCommandCategory.Interact),
            placeholderLabel: "Interact");

        this.AddSingleSlotCategory(
            layout,
            "MOVE",
            visibleCommands.FirstOrDefault(command =>
                command.Category == FleetCommandCategory.Move &&
                string.Equals(
                    command.Id,
                    BuiltInFleetCommandProvider.ComeToMeCommandId,
                    StringComparison.OrdinalIgnoreCase)),
            placeholderLabel: "Come To Me");

        return layout;
    }

    private void AddLinearCategory(
        CommandPaletteLayout layout,
        string title,
        IReadOnlyList<FleetCommandDefinition> commands,
        string emptyPlaceholder)
    {
        this.AddHeader(layout, title);

        if (commands.Count == 0)
        {
            this.AddPlaceholder(
                layout,
                emptyPlaceholder,
                column: 0);
            return;
        }

        for (var index = 0; index < commands.Count; index++)
        {
            var command = commands[index];

            if (index % ColumnCount == 0)
            {
                layout.RowHeights.Add(TileHeight);
            }

            layout.Items.Add(LayoutItem.Command(
                command.Id,
                command.Label,
                command.IsEnabled,
                row: layout.RowHeights.Count - 1,
                column: index % ColumnCount));
        }
    }

    private void AddFormationCategory(
        CommandPaletteLayout layout,
        IReadOnlyCollection<FleetCommandDefinition> commands)
    {
        this.AddHeader(layout, "FORMATION");

        var formationCommands = commands
            .Where(command => command.Category == FleetCommandCategory.Formation)
            .ToList();

        this.AddExpectedCommandOrPlaceholder(
            layout,
            formationCommands,
            BuiltInFleetCommandProvider.FormationModeCommandId,
            string.Concat(
                "Mode: ",
                FormatFormationMode(
                    this.clientManager.FleetCommandSettings.FormationMode)),
            column: 0,
            startNewRow: true);
        this.AddExpectedCommandOrPlaceholder(
            layout,
            formationCommands,
            BuiltInFleetCommandProvider.FormationToggleCommandId,
            "Form Up",
            column: 1,
            startNewRow: false);
    }

    private static string FormatFormationMode(
        FleetFormationMode mode)
    {
        return mode switch
        {
            FleetFormationMode.Block => "Block",
            FleetFormationMode.SlotBack => "Slot-Back",
            FleetFormationMode.Pipe => "Pipe",
            _ => "Block",
        };
    }

    private void AddCombatCategory(
        CommandPaletteLayout layout,
        IReadOnlyCollection<FleetCommandDefinition> commands)
    {
        this.AddHeader(layout, "COMBAT");

        var combatCommands = commands
            .Where(command => command.Category == FleetCommandCategory.Combat)
            .ToList();

        this.AddExpectedCommandOrPlaceholder(
            layout,
            combatCommands,
            BuiltInFleetCommandProvider.AssistMeCommandId,
            "Assist Me",
            column: 0,
            startNewRow: true);
        this.AddExpectedCommandOrPlaceholder(
            layout,
            combatCommands,
            "ui:group-skills",
            "Action HUD",
            column: 1,
            startNewRow: false);
    }

    private void AddSingleSlotCategory(
        CommandPaletteLayout layout,
        string title,
        FleetCommandDefinition? command,
        string placeholderLabel)
    {
        this.AddHeader(layout, title);

        if (command != null)
        {
            layout.RowHeights.Add(TileHeight);
            layout.Items.Add(LayoutItem.Command(
                command.Id,
                command.Label,
                command.IsEnabled,
                row: layout.RowHeights.Count - 1,
                column: 0));
            return;
        }

        this.AddPlaceholder(
            layout,
            placeholderLabel,
            column: 0);
    }

    private void AddExpectedCommandOrPlaceholder(
        CommandPaletteLayout layout,
        IReadOnlyCollection<FleetCommandDefinition> commands,
        string commandId,
        string placeholderLabel,
        int column,
        bool startNewRow)
    {
        if (startNewRow)
        {
            layout.RowHeights.Add(TileHeight);
        }

        var command = commands.FirstOrDefault(candidate =>
            string.Equals(
                candidate.Id,
                commandId,
                StringComparison.OrdinalIgnoreCase));

        var row = layout.RowHeights.Count - 1;

        layout.Items.Add(command != null
            ? LayoutItem.Command(
                command.Id,
                command.Label,
                command.IsEnabled,
                row,
                column)
            : LayoutItem.Placeholder(
                placeholderLabel,
                row,
                column));
    }

    private void AddHeader(
        CommandPaletteLayout layout,
        string title)
    {
        if (layout.RowHeights.Count > 0)
        {
            layout.RowHeights.Add(SectionGap);
        }

        var row = layout.RowHeights.Count;
        layout.RowHeights.Add(HeaderHeight);
        layout.Items.Add(LayoutItem.Header(
            title,
            row,
            column: 0,
            columnSpan: ColumnCount));
    }

    private void AddPlaceholder(
        CommandPaletteLayout layout,
        string label,
        int column,
        int columnSpan = 1)
    {
        layout.RowHeights.Add(TileHeight);
        layout.Items.Add(LayoutItem.Placeholder(
            label,
            row: layout.RowHeights.Count - 1,
            column,
            columnSpan));
    }

    private Point CalculateCursorAnchorPoint(
        IReadOnlyCollection<LayoutItem> layoutItems)
    {
        _ = layoutItems;

        // The command palette is a cursor palette. Keep the cursor in the middle
        // of the whole overlay. Layout is deliberately heat-mapped around that
        // center, so frequent gameplay actions live near the cursor hearth.
        return new Point(
            this.ClientSize.Width / 2,
            this.ClientSize.Height / 2);
    }

    private Label CreateHeaderLabel(
        string text)
    {
        var label = new Label
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(2, 0, TileGap, 0),
            Text = text,
            TextAlign = ContentAlignment.BottomLeft,
            AutoEllipsis = true,
            ForeColor = MainWindowTheme.MutedText,
            BackColor = MainWindowTheme.Background,
            Font = MainWindowTheme.CreateHeadingFont(7.8f),
            Cursor = this.behavior == CommandPaletteBehavior.Transient
                ? Cursors.Hand
                : Cursors.SizeAll,
        };

        if (this.behavior != CommandPaletteBehavior.Transient)
        {
            this.WireDragSurface(label);
        }

        return label;
    }

    private CommandTileLabel CreateCommandTile(
        string commandId,
        string text,
        bool isEnabled)
    {
        var tile = new CommandTileLabel(commandId, isEnabled)
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, TileGap, TileGap),
            Text = text,
            TextAlign = ContentAlignment.MiddleCenter,
            AutoEllipsis = true,
            ForeColor = isEnabled
                ? MainWindowTheme.Text
                : MainWindowTheme.DisabledText,
            BackColor = isEnabled
                ? MainWindowTheme.Button
                : MainWindowTheme.DisabledButton,
            Cursor = this.behavior == CommandPaletteBehavior.PositionPreview
                ? Cursors.Default
                : Cursors.Hand,
        };

        tile.MouseEnter += this.CommandTile_OnMouseEnter;
        tile.MouseLeave += this.CommandTile_OnMouseLeave;
        tile.MouseUp += this.CommandOverlayForm_OnMouseUp;

        this.tiles.Add(tile);

        return tile;
    }

    private void UnwireTile(CommandTileLabel tile)
    {
        tile.MouseEnter -= this.CommandTile_OnMouseEnter;
        tile.MouseLeave -= this.CommandTile_OnMouseLeave;
        tile.MouseUp -= this.CommandOverlayForm_OnMouseUp;
    }

    private void CommandTile_OnMouseEnter(object? sender, EventArgs e)
    {
        if (sender is CommandTileLabel { IsCommandEnabled: true } tile)
        {
            this.SetHoveredCommand(tile.CommandId);
        }
    }

    private void CommandTile_OnMouseLeave(object? sender, EventArgs e)
    {
        this.SetHoveredCommand(commandId: null);
    }

    private void SetHoveredCommand(string? commandId)
    {
        this.hoveredCommandId = commandId;

        foreach (var tile in this.tiles)
        {
            this.SetTileState(
                tile,
                tile.IsCommandEnabled &&
                string.Equals(tile.CommandId, commandId, StringComparison.Ordinal));
        }
    }

    private void SetTileState(CommandTileLabel tile, bool selected)
    {
        if (!tile.IsCommandEnabled)
        {
            tile.BackColor = MainWindowTheme.DisabledButton;
            tile.ForeColor = MainWindowTheme.DisabledText;
            return;
        }

        tile.BackColor = selected
            ? MainWindowTheme.ButtonHover
            : MainWindowTheme.Button;

        tile.ForeColor = selected
            ? MainWindowTheme.Accent
            : MainWindowTheme.Text;
    }

    private void PresentationTimer_OnTick(object? sender, EventArgs e)
    {
        this.RefreshDynamicCommandPresentation();
    }

    public Task CompleteFromHotKeyReleaseAsync()
    {
        if (this.behavior != CommandPaletteBehavior.Transient ||
            this.completed)
        {
            return Task.CompletedTask;
        }

        var cursorInside = this.Bounds.Contains(Cursor.Position);
        this.RecordDiagnostic(
            "Hotkey released",
            string.Concat(
                "cursorInside=",
                cursorInside,
                "; hovered=",
                this.ResolveCommandLabel(this.hoveredCommandId) ?? "(none)"));

        if (!cursorInside)
        {
            return this.CompleteAsync(
                commandId: null,
                restoreOriginalFocusAndMouse: false);
        }

        return this.CompleteAsync(
            this.hoveredCommandId,
            restoreOriginalFocusAndMouse: true);
    }

    private void RefreshDynamicCommandPresentation()
    {
        var now = DateTimeOffset.UtcNow;

        if (now < this.nextPresentationRefreshAt)
        {
            return;
        }

        this.nextPresentationRefreshAt = now + TimeSpan.FromMilliseconds(75);

        var commands = this.clientManager
            .GetFleetCommandDefinitions(this.invocationContext);

        this.RefreshCommandDefinitions(commands);
    }

    private void RefreshCommandDefinitions(
        IReadOnlyCollection<FleetCommandDefinition> commands)
    {
        var visibleCommands = new Dictionary<string, FleetCommandDefinition>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var command in commands.Where(command => command.ShowInOverlay))
        {
            visibleCommands[command.Id] = command;
        }

        foreach (var tile in this.tiles)
        {
            if (!visibleCommands.TryGetValue(
                    tile.CommandId,
                    out var command))
            {
                tile.IsCommandEnabled = false;

                if (string.Equals(
                        this.hoveredCommandId,
                        tile.CommandId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    this.hoveredCommandId = null;
                }

                this.SetTileState(tile, selected: false);
                continue;
            }

            tile.Text = command.Label;
            tile.IsCommandEnabled = command.IsEnabled;

            if (!tile.IsCommandEnabled &&
                string.Equals(
                    this.hoveredCommandId,
                    tile.CommandId,
                    StringComparison.OrdinalIgnoreCase))
            {
                this.hoveredCommandId = null;
            }

            this.SetTileState(
                tile,
                tile.IsCommandEnabled &&
                string.Equals(
                    this.hoveredCommandId,
                    tile.CommandId,
                    StringComparison.OrdinalIgnoreCase));
        }
    }

    private async void CommandOverlayForm_OnMouseUp(object? sender, MouseEventArgs e)
    {
        if (this.dragging || this.behavior == CommandPaletteBehavior.PositionPreview)
        {
            return;
        }

        if (this.behavior == CommandPaletteBehavior.Persistent)
        {
            if (e.Button == MouseButtons.Left && this.hoveredCommandId != null)
            {
                await this.ExecutePersistentCommandAsync(
                    this.hoveredCommandId).ConfigureAwait(true);
            }

            return;
        }

        if (e.Button == MouseButtons.Right)
        {
            await this.CompleteAsync(
                commandId: null,
                restoreOriginalFocusAndMouse: true).ConfigureAwait(true);
            return;
        }

        if (e.Button == MouseButtons.Left && this.hoveredCommandId != null)
        {
            await this.CompleteAsync(
                this.hoveredCommandId,
                restoreOriginalFocusAndMouse: true).ConfigureAwait(true);
        }
    }

    private async void CommandOverlayForm_OnDeactivate(object? sender, EventArgs e)
    {
        if (this.behavior != CommandPaletteBehavior.Transient)
        {
            return;
        }

        await this.CompleteAsync(
            commandId: null,
            restoreOriginalFocusAndMouse: true).ConfigureAwait(true);
    }

    private async void CommandOverlayForm_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (this.behavior == CommandPaletteBehavior.PositionPreview)
        {
            if (e.KeyCode == Keys.Enter)
            {
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
            else if (e.KeyCode == Keys.Escape)
            {
                this.DialogResult = DialogResult.Cancel;
                this.Close();
            }

            return;
        }

        if (this.behavior == CommandPaletteBehavior.Transient &&
            e.KeyCode == Keys.Escape)
        {
            await this.CompleteAsync(
                commandId: null,
                restoreOriginalFocusAndMouse: true).ConfigureAwait(true);
        }
    }

    private async Task ExecutePersistentCommandAsync(string commandId)
    {
        if (this.executingPersistentCommand)
        {
            return;
        }

        var command = this.clientManager
            .GetFleetCommandDefinitions(this.invocationContext)
            .FirstOrDefault(candidate => string.Equals(
                candidate.Id,
                commandId,
                StringComparison.OrdinalIgnoreCase));

        if (command is not { IsEnabled: true })
        {
            return;
        }

        this.executingPersistentCommand = true;
        var cursorPosition = Cursor.Position;

        try
        {
            var restoresGameFocus = await this.clientManager.ExecuteFleetCommandAsync(
                command,
                this.invocationContext).ConfigureAwait(true);

            this.RecordDiagnostic(
                "Persistent command executed",
                command.Label);

            if (restoresGameFocus)
            {
                this.RestoreOriginalFocusAndMouse(cursorPosition);
            }
        }
        finally
        {
            this.executingPersistentCommand = false;
            this.SetHoveredCommand(commandId: null);
        }
    }

    private async Task CompleteAsync(
        string? commandId,
        bool restoreOriginalFocusAndMouse)
    {
        if (this.completed)
        {
            return;
        }

        this.completed = true;
        this.presentationTimer.Stop();

        var completionCursorPosition =
            this.restoreCursorPosition ?? Cursor.Position;

        try
        {
            var command = string.IsNullOrWhiteSpace(commandId)
                ? null
                : this.clientManager
                    .GetFleetCommandDefinitions(this.invocationContext)
                    .FirstOrDefault(candidate =>
                        candidate.IsEnabled &&
                        string.Equals(
                            candidate.Id,
                            commandId,
                            StringComparison.OrdinalIgnoreCase));

            if (command != null)
            {
                this.Hide();

                var restoresGameFocus = await this.clientManager.ExecuteFleetCommandAsync(
                    command,
                    this.invocationContext).ConfigureAwait(true);

                this.RecordDiagnostic(
                    "Transient command executed",
                    command.Label);

                if (restoresGameFocus)
                {
                    this.RestoreOriginalFocusAndMouse(completionCursorPosition);
                }
                else if (this.restoreCursorPosition.HasValue)
                {
                    _ = NativeMethods.MoveCursorToScreenPoint(
                        completionCursorPosition);
                }

                return;
            }

            if (restoreOriginalFocusAndMouse)
            {
                this.RestoreOriginalFocusAndMouse(completionCursorPosition);
            }
        }
        finally
        {
            this.Close();
        }
    }

    private string? ResolveCommandLabel(string? commandId)
    {
        if (string.IsNullOrWhiteSpace(commandId))
        {
            return null;
        }

        return this.clientManager
                   .GetFleetCommandDefinitions(this.invocationContext)
                   .FirstOrDefault(candidate => string.Equals(
                       candidate.Id,
                       commandId,
                       StringComparison.OrdinalIgnoreCase))?
                   .Label ??
               commandId;
    }

    private void RecordDiagnostic(string eventName, string? details = null)
    {
        this.diagnosticReporter?.Invoke(eventName, details);
    }

    private void RestoreOriginalFocusAndMouse(Point screenPoint)
    {
        if (this.invocationContext.ActiveClient.GameWindowHandle == IntPtr.Zero)
        {
            return;
        }

        // The palette must not play global Cursor.Show/Hide games. While the
        // overlay is open, the Windows cursor is the right cursor. Once the
        // overlay closes, restore the original game window and put the hardware
        // cursor back where the command was chosen so the game owns cursor
        // presentation again.
        NativeMethods.FocusWindow(this.invocationContext.ActiveClient.GameWindowHandle);
        _ = NativeMethods.MoveCursorToScreenPoint(screenPoint);
    }

    private void WireDragSurface(Control surface)
    {
        if (this.dragSurfaces.Contains(surface))
        {
            return;
        }

        surface.MouseDown += this.DragSurface_OnMouseDown;
        surface.MouseMove += this.DragSurface_OnMouseMove;
        surface.MouseUp += this.DragSurface_OnMouseUp;
        this.dragSurfaces.Add(surface);
    }

    private void UnwireDragSurface(Control surface)
    {
        surface.MouseDown -= this.DragSurface_OnMouseDown;
        surface.MouseMove -= this.DragSurface_OnMouseMove;
        surface.MouseUp -= this.DragSurface_OnMouseUp;
    }

    private void DragSurface_OnMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left ||
            this.behavior == CommandPaletteBehavior.Transient)
        {
            return;
        }

        this.dragging = true;
        this.dragCursorOrigin = Cursor.Position;
        this.dragLocationOrigin = this.Location;
        this.dragCaptureControl = sender as Control ?? this;
        this.dragCaptureControl.Capture = true;
    }

    private void DragSurface_OnMouseMove(object? sender, MouseEventArgs e)
    {
        if (!this.dragging)
        {
            return;
        }

        var cursor = Cursor.Position;
        var requested = new Point(
            this.dragLocationOrigin.X + cursor.X - this.dragCursorOrigin.X,
            this.dragLocationOrigin.Y + cursor.Y - this.dragCursorOrigin.Y);

        this.Location = ClampLocation(
            requested,
            this.Size,
            this.movementBounds);
    }

    private void DragSurface_OnMouseUp(object? sender, MouseEventArgs e)
    {
        if (!this.dragging || e.Button != MouseButtons.Left)
        {
            return;
        }

        this.dragging = false;

        if (this.dragCaptureControl != null)
        {
            this.dragCaptureControl.Capture = false;
            this.dragCaptureControl = null;
        }

        if (this.behavior == CommandPaletteBehavior.Persistent)
        {
            this.persistentLocationChanged?.Invoke(this.Location);
        }
    }

    private static Point ClampLocation(
        Point requested,
        Size size,
        Rectangle bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return requested;
        }

        var maximumX = Math.Max(bounds.Left, bounds.Right - size.Width);
        var maximumY = Math.Max(bounds.Top, bounds.Bottom - size.Height);

        return new Point(
            Math.Clamp(requested.X, bounds.Left, maximumX),
            Math.Clamp(requested.Y, bounds.Top, maximumY));
    }

    private static string BuildCommandSignature(
        IEnumerable<FleetCommandDefinition> commands)
    {
        return string.Join(
            "\u001F",
            commands
                .Where(command => command.ShowInOverlay)
                .Select(command => string.Concat(
                    command.Id,
                    "\u001E",
                    command.Category.ToString())));
    }

    private sealed class CommandPaletteLayout
    {
        public List<LayoutItem> Items { get; } = [];

        public List<int> RowHeights { get; } = [];
    }

    private sealed record LayoutItem(
        string CommandId,
        string Label,
        int Row,
        int Column,
        int ColumnSpan,
        bool IsHeader,
        bool IsEnabled)
    {
        public static LayoutItem Header(
            string label,
            int row,
            int column,
            int columnSpan)
        {
            return new LayoutItem(
                string.Concat("__header:", label),
                label,
                row,
                column,
                columnSpan,
                IsHeader: true,
                IsEnabled: false);
        }

        public static LayoutItem Command(
            string commandId,
            string label,
            bool isEnabled,
            int row,
            int column,
            int columnSpan = 1)
        {
            return new LayoutItem(
                commandId,
                label,
                row,
                column,
                columnSpan,
                IsHeader: false,
                IsEnabled: isEnabled);
        }

        public static LayoutItem Placeholder(
            string label,
            int row,
            int column,
            int columnSpan = 1)
        {
            return new LayoutItem(
                string.Concat("__placeholder:", row.ToString(CultureInfo.InvariantCulture), ":", column.ToString(CultureInfo.InvariantCulture)),
                label,
                row,
                column,
                columnSpan,
                IsHeader: false,
                IsEnabled: false);
        }
    }

    private sealed class CommandTileLabel(string commandId, bool isCommandEnabled) : Label
    {
        [System.ComponentModel.Browsable(false)]
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public string CommandId { get; } = commandId;

        [System.ComponentModel.Browsable(false)]
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool IsCommandEnabled { get; set; } = isCommandEnabled;
    }
}
