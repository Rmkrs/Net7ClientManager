namespace Net7ClientManager.Forms;

using System.Drawing.Drawing2D;
using Net7ClientManager.Addons.Contracts;
using Net7ClientManager.Models;
using Net7ClientManager.Win32;

internal sealed class AddonOverlayForm : Form
{
    private const int WmNcHitTest = 0x0084;
    private const int WmMouseActivate = 0x0021;
    private const int HtTransparent = -1;
    private const int HtClient = 1;
    private const int MaNoActivate = 3;

    private const int WsExToolWindow = 0x00000080;
    private const int WsExLayered = 0x00080000;
    private const int WsExNoActivate = 0x08000000;

    private const int WindowHeaderHeight = 34;
    private const int WindowChromeSize = 24;
    private const int WindowChromeGap = 3;
    private const int WindowChromeRightMargin = 6;

    // Host-owned in-game Addons menu. These coordinates intentionally use
    // the same 1280x720 reference canvas as FleetCommandLabForm. They are
    // scaled independently to the live hosted client size.
    private const int HostUiBaseWidth = 1280;
    private const int HostUiBaseHeight = 720;
    private const int AddonsTabBaseX = 796;
    private const int AddonsTabBaseY = 17;
    private const int AddonsTabBaseWidth = 132;
    private const int AddonsTabBaseHeight = 19;
    private const int AddonsMenuBaseWidth = 240;
    private const int AddonsMenuItemBaseHeight = 25;
    private const int AddonsMenuPaddingBase = 6;
    private const int AddonsMenuSeparatorBaseHeight = 7;
    private const int AddonsMenuCloseDelayMilliseconds = 300;
    private const int TooltipDelayMilliseconds = 450;

    private static readonly WidgetKey addonsTabKey =
        new("net7.addons.host", "addons-tab");

    private static readonly WidgetKey optionsKey =
        new("net7.addons.host", "options");

    private static readonly WidgetKey galaxyAtlasKey =
        new("net7.addons.host", "galaxy-atlas");

    private static readonly WidgetKey worldFindKey =
        new("net7.addons.host", "world-find");

    private static readonly WidgetKey pilotArchiveKey =
        new("net7.addons.host", "pilot-archive");

    private static readonly WidgetKey buildsKey =
        new("net7.addons.host", "builds");

    private static readonly WidgetKey socialKey =
        new("net7.addons.host", "social");

    private static readonly WidgetKey forgeContributionsKey =
        new("net7.addons.host", "forge-contributions");

    private static readonly WidgetKey manageAddonsKey =
        new("net7.addons.host", "manage-addons");

    private static readonly Color transparencyColor =
        Color.FromArgb(red: 255, green: 0, blue: 255);

    private readonly int ownerProcessId;
    private readonly Func<string, string, AddonWindowPlacement?>
        resolveWindowPlacement;
    private readonly Action<string, string, AddonWindowPlacement>
        saveWindowPlacement;
    private readonly Dictionary<WidgetKey, WindowEntry> windows = [];
    private readonly Dictionary<WidgetKey, AddonUiLabel> labels = [];
    private readonly Dictionary<WidgetKey, AddonUiButton> buttons = [];
    private readonly Dictionary<WidgetKey, AddonUiWindowMenuItem>
        windowMenuItems = [];
    private readonly Dictionary<WidgetKey, AddonUiMenuToggle>
        menuToggles = [];
    private readonly Dictionary<FontKey, Font> fonts = [];
    private readonly System.Windows.Forms.Timer cursorRefreshTimer = new()
    {
        Interval = 30,
    };

    private InteractiveIdentity? pressedElement;
    private WindowDragState? windowDrag;
    private Point lastCursorClientPoint = new(int.MinValue, int.MinValue);
    private OwnedIdentity? lastCursorOwnedElement;
    private InteractiveIdentity? lastCursorInteractiveElement;
    private InteractiveIdentity? tooltipHoverIdentity;
    private DateTimeOffset tooltipHoverStartedAt;
    private DateTimeOffset? menuPointerLeftAt;
    private bool tooltipVisible;
    private bool gameMenuVisible;
    private bool gameMenuOpen;
    private bool presentationEnabled = true;
    private bool inputSuppressed;

    public AddonOverlayForm(
        int ownerProcessId,
        Func<string, string, AddonWindowPlacement?>
            resolveWindowPlacement,
        Action<string, string, AddonWindowPlacement>
            saveWindowPlacement)
    {
        this.ownerProcessId = ownerProcessId;
        this.resolveWindowPlacement = resolveWindowPlacement;
        this.saveWindowPlacement = saveWindowPlacement;
        this.FormBorderStyle = FormBorderStyle.None;
        this.ShowInTaskbar = false;
        this.StartPosition = FormStartPosition.Manual;
        this.BackColor = transparencyColor;
        this.TransparencyKey = transparencyColor;
        this.AutoScaleMode = AutoScaleMode.None;
        this.DoubleBuffered = true;

        this.cursorRefreshTimer.Tick +=
            this.CursorRefreshTimer_OnTick;
        this.cursorRefreshTimer.Start();
    }

    public event EventHandler<AddonUiInteractionEventArgs>?
        UiInteractionRaised;

    public event EventHandler? InGameOptionsRequested;

    public event EventHandler? GalaxyAtlasRequested;

    public event EventHandler? WorldFindRequested;

    public event EventHandler? ForgeContributionsRequested;

    public event EventHandler? PilotArchiveRequested;

    public event EventHandler? BuildsRequested;

    public event EventHandler? SocialRequested;

    public event EventHandler? ManageAddonsRequested;

    public event EventHandler? GameMenuOpened;

    public bool HasWidgets
    {
        get
        {
            if (!this.presentationEnabled ||
                this.inputSuppressed)
            {
                return false;
            }

            var scene = this.ResolveScene();

            return this.gameMenuVisible ||
                   scene.Windows.Count != 0 ||
                   scene.Widgets.Count != 0;
        }
    }

    protected override bool ShowWithoutActivation =>
        true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |=
                WsExToolWindow |
                WsExLayered |
                WsExNoActivate;

            return parameters;
        }
    }

    public void Apply(AddonUiCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var key = new WidgetKey(
            command.AddonId,
            command.WidgetId);

        switch (command.Kind)
        {
            case AddonUiCommandKind.UpsertWindow:
                if (command.Window != null)
                {
                    this.labels.Remove(key);
                    this.buttons.Remove(key);

                    if (this.windows.TryGetValue(
                            key,
                            out var existing))
                    {
                        existing.Definition = command.Window;
                    }
                    else
                    {
                        var persisted =
                            this.ResolvePersistedWindowPlacement(key);

                        this.windows[key] = new WindowEntry
                        {
                            Definition = command.Window,
                            IsClosed = persisted?.IsClosed ?? false,
                            IsVisible = persisted?.IsVisible ?? true,
                            IsMinimized = persisted?.IsMinimized ??
                                command.Window.StartMinimized,
                            PositionOffset = persisted == null
                                ? Point.Empty
                                : new Point(
                                    persisted.OffsetX,
                                    persisted.OffsetY),
                            UserSize = ResolvePersistedWindowSize(
                                persisted),
                            HorizontalEdge = persisted?.HorizontalEdge ??
                                AddonWindowHorizontalEdge.Auto,
                            MinimizedOffsetX =
                                persisted?.MinimizedOffsetX ?? 0,
                            MinimizedOffsetY =
                                persisted?.MinimizedOffsetY ?? 0,
                        };
                    }
                }

                break;

            case AddonUiCommandKind.ShowWindow:
                if (this.windows.TryGetValue(
                        key,
                        out var windowToShow))
                {
                    windowToShow.Definition =
                        windowToShow.Definition with
                        {
                            IsVisible = true,
                        };
                }

                break;

            case AddonUiCommandKind.SetWindowAvailability:
                if (command.WindowAvailability != null &&
                    this.windows.TryGetValue(
                        key,
                        out var availabilityWindow))
                {
                    availabilityWindow.IsAvailable =
                        command.WindowAvailability.IsAvailable;
                    availabilityWindow.UnavailableReason =
                        command.WindowAvailability.Reason;

                    if (!availabilityWindow.IsAvailable)
                    {
                        this.ReleasePressedElementIfRemoved(key);
                    }
                }

                break;

            case AddonUiCommandKind.UpsertLabel:
                if (command.Label != null)
                {
                    this.RemoveWindowAndChildren(key);
                    this.buttons.Remove(key);
                    this.labels[key] = command.Label;
                }

                break;

            case AddonUiCommandKind.UpsertButton:
                if (command.Button != null)
                {
                    this.RemoveWindowAndChildren(key);
                    this.labels.Remove(key);
                    this.buttons[key] = command.Button;
                }

                break;

            case AddonUiCommandKind.RegisterWindowMenuItem:
                if (command.WindowMenuItem != null)
                {
                    this.windowMenuItems[key] =
                        command.WindowMenuItem;
                }

                break;

            case AddonUiCommandKind.RegisterMenuToggle:
                if (command.MenuToggle != null)
                {
                    this.menuToggles[key] =
                        command.MenuToggle;
                }

                break;

            case AddonUiCommandKind.RemoveMenuItem:
                this.windowMenuItems.Remove(key);
                this.menuToggles.Remove(key);
                break;

            case AddonUiCommandKind.RemoveWidget:
                this.RemoveWindowAndChildren(key);
                this.labels.Remove(key);
                this.buttons.Remove(key);
                this.ReleasePressedElementIfRemoved(key);
                break;

            case AddonUiCommandKind.ClearAddon:
                this.RemoveAddon(command.AddonId);
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(command),
                    command.Kind,
                    message: null);
        }

        this.Invalidate();
    }

    public void ReloadWindowState()
    {
        this.CompleteWindowDrag(persist: true);

        foreach (var item in this.windows)
        {
            var persisted =
                this.ResolvePersistedWindowPlacement(item.Key);

            item.Value.PositionOffset = persisted == null
                ? Point.Empty
                : new Point(
                    persisted.OffsetX,
                    persisted.OffsetY);
            item.Value.UserSize = ResolvePersistedWindowSize(
                persisted);
            item.Value.IsClosed = persisted?.IsClosed ?? false;
            item.Value.IsVisible = persisted?.IsVisible ?? true;
            item.Value.IsMinimized = persisted?.IsMinimized ??
                item.Value.Definition.StartMinimized;
            item.Value.HorizontalEdge =
                persisted?.HorizontalEdge ??
                AddonWindowHorizontalEdge.Auto;
            item.Value.MinimizedOffsetX =
                persisted?.MinimizedOffsetX ?? 0;
            item.Value.MinimizedOffsetY =
                persisted?.MinimizedOffsetY ?? 0;
        }

        this.Invalidate();
    }

    public void SetGameMenuVisible(bool visible)
    {
        if (this.gameMenuVisible == visible)
        {
            return;
        }

        this.gameMenuVisible = visible;

        if (!visible)
        {
            this.gameMenuOpen = false;
            this.menuPointerLeftAt = null;
            this.tooltipVisible = false;
            this.tooltipHoverIdentity = null;
        }

        this.CompleteWindowDrag(persist: true);
        this.pressedElement = null;
        this.Capture = false;
        this.Invalidate();
    }

    public void SetPresentationEnabled(bool enabled)
    {
        if (this.presentationEnabled == enabled)
        {
            return;
        }

        this.presentationEnabled = enabled;

        if (!enabled)
        {
            this.gameMenuOpen = false;
            this.menuPointerLeftAt = null;
            this.tooltipVisible = false;
            this.tooltipHoverIdentity = null;
            this.lastCursorOwnedElement = null;
            this.lastCursorInteractiveElement = null;
        }

        this.CompleteWindowDrag(persist: true);
        this.pressedElement = null;
        this.Capture = false;
        this.Invalidate();
    }

    public void SetInputSuppressed(bool suppressed)
    {
        if (this.inputSuppressed == suppressed)
        {
            return;
        }

        this.inputSuppressed = suppressed;

        if (this.IsHandleCreated)
        {
            _ = NativeMethods.TrySetWindowClickThrough(
                this.Handle,
                suppressed);
        }

        if (suppressed)
        {
            this.tooltipVisible = false;
            this.tooltipHoverIdentity = null;
            this.lastCursorOwnedElement = null;
            this.lastCursorInteractiveElement = null;
        }

        this.CompleteWindowDrag(persist: true);
        this.pressedElement = null;
        this.Capture = false;
        this.Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.CompleteWindowDrag(persist: true);

            this.cursorRefreshTimer.Stop();
            this.cursorRefreshTimer.Tick -=
                this.CursorRefreshTimer_OnTick;
            this.cursorRefreshTimer.Dispose();

            foreach (var font in this.fonts.Values)
            {
                font.Dispose();
            }

            this.fonts.Clear();
            this.windows.Clear();
            this.labels.Clear();
            this.buttons.Clear();
            this.windowMenuItems.Clear();
            this.menuToggles.Clear();
        }

        base.Dispose(disposing);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WmNcHitTest)
        {
            if (!this.presentationEnabled ||
                this.inputSuppressed)
            {
                message.Result = new IntPtr(HtTransparent);
                return;
            }

            var screenPoint = GetScreenPoint(message.LParam);
            var clientPoint = this.PointToClient(screenPoint);
            var scene = this.ResolveScene();
            var interactive = FindTopmostInteractive(
                scene,
                clientPoint);

            var overOpenGameMenu =
                scene.GameMenu is { IsOpen: true } menu &&
                menu.DropDownBounds.Contains(clientPoint);

            message.Result =
                interactive.HasValue || overOpenGameMenu
                    ? new IntPtr(HtClient)
                    : new IntPtr(HtTransparent);

            return;
        }

        if (message.Msg == WmMouseActivate)
        {
            message.Result = new IntPtr(MaNoActivate);
            return;
        }

        base.WndProc(ref message);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (!this.presentationEnabled ||
            this.inputSuppressed ||
            e.Button != MouseButtons.Left)
        {
            return;
        }

        var scene = this.ResolveScene();
        var interactive = FindTopmostInteractive(
            scene,
            e.Location);

        if (!interactive.HasValue)
        {
            return;
        }

        if (interactive.Value.Identity.Kind ==
            InteractiveKind.WindowDrag)
        {
            var window = scene.Windows.First(candidate =>
                candidate.Key == interactive.Value.Identity.Key);

            this.windowDrag = new WindowDragState
            {
                Key = window.Key,
                PointerStart = e.Location,
                WindowStart = window.Bounds.Location,
                IsMinimized = window.Entry.IsMinimized,
            };

            this.pressedElement = null;
            this.Capture = true;
            this.Invalidate(window.HeaderBounds);
            return;
        }

        this.pressedElement = interactive.Value.Identity;
        this.Capture = true;
        this.Invalidate(interactive.Value.Bounds);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (this.windowDrag == null)
        {
            return;
        }

        if (!this.presentationEnabled ||
            this.inputSuppressed ||
            (e.Button & MouseButtons.Left) == MouseButtons.None)
        {
            this.CompleteWindowDrag(persist: true);
            this.Invalidate();
            return;
        }

        this.UpdateWindowDrag(e.Location);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (this.windowDrag != null)
        {
            this.CompleteWindowDrag(persist: true);
            this.Invalidate();
            return;
        }

        var pressed = this.pressedElement;
        this.pressedElement = null;
        this.Capture = false;

        if (!this.presentationEnabled ||
            this.inputSuppressed ||
            e.Button != MouseButtons.Left ||
            pressed == null)
        {
            this.Invalidate();
            return;
        }

        var interactive = FindTopmostInteractive(
            this.ResolveScene(),
            e.Location);

        if (!interactive.HasValue ||
            interactive.Value.Identity != pressed.Value)
        {
            this.Invalidate();
            return;
        }

        switch (pressed.Value.Kind)
        {
            case InteractiveKind.Button:
                this.UiInteractionRaised?.Invoke(
                    this,
                    new AddonUiInteractionEventArgs(
                        new AddonUiInteraction
                        {
                            Kind = AddonUiInteractionKind.Click,
                            OwnerProcessId = this.ownerProcessId,
                            AddonId = pressed.Value.Key.AddonId,
                            WidgetId = pressed.Value.Key.WidgetId,
                            ObservedAt = DateTimeOffset.UtcNow,
                        }));
                break;

            case InteractiveKind.WindowMinimize:
                if (this.windows.TryGetValue(
                        pressed.Value.Key,
                        out var minimizeWindow))
                {
                    if (!minimizeWindow.IsMinimized)
                    {
                        var expandedBounds =
                            this.ResolveExpandedWindowBounds(
                                minimizeWindow);

                        minimizeWindow.HorizontalEdge =
                            ResolveNearestHorizontalEdge(
                                expandedBounds,
                                this.ClientRectangle);
                        minimizeWindow.MinimizedOffsetX = 0;
                        minimizeWindow.MinimizedOffsetY = 0;
                    }

                    minimizeWindow.IsMinimized =
                        !minimizeWindow.IsMinimized;
                    this.PersistWindowState(
                        pressed.Value.Key,
                        minimizeWindow);
                }

                break;

            case InteractiveKind.WindowClose:
                if (this.windows.TryGetValue(
                        pressed.Value.Key,
                        out var closeWindow))
                {
                    closeWindow.IsClosed = true;
                    this.PersistWindowState(
                        pressed.Value.Key,
                        closeWindow);
                }

                break;

            case InteractiveKind.AddonsMenuTab:
                this.gameMenuOpen = !this.gameMenuOpen;
                this.menuPointerLeftAt = null;
                if (this.gameMenuOpen)
                {
                    this.GameMenuOpened?.Invoke(
                        this,
                        EventArgs.Empty);
                }
                break;

            case InteractiveKind.AddonsMenuWindow:
                this.ToggleWindowFromMenu(pressed.Value.Key);
                this.menuPointerLeftAt = null;
                break;

            case InteractiveKind.AddonsMenuToggle:
                this.ToggleAddonMenuOption(pressed.Value.Key);
                this.menuPointerLeftAt = null;
                break;

            case InteractiveKind.AddonsMenuOptions:
                this.gameMenuOpen = false;
                this.menuPointerLeftAt = null;
                this.InGameOptionsRequested?.Invoke(
                    this,
                    EventArgs.Empty);
                break;

            case InteractiveKind.AddonsMenuGalaxyAtlas:
                this.gameMenuOpen = false;
                this.menuPointerLeftAt = null;
                this.GalaxyAtlasRequested?.Invoke(
                    this,
                    EventArgs.Empty);
                break;

            case InteractiveKind.AddonsMenuWorldFind:
                this.gameMenuOpen = false;
                this.menuPointerLeftAt = null;
                this.WorldFindRequested?.Invoke(
                    this,
                    EventArgs.Empty);
                break;

            case InteractiveKind.AddonsMenuPilotArchive:
                this.gameMenuOpen = false;
                this.menuPointerLeftAt = null;
                this.PilotArchiveRequested?.Invoke(
                    this,
                    EventArgs.Empty);
                break;

            case InteractiveKind.AddonsMenuBuilds:
                this.gameMenuOpen = false;
                this.menuPointerLeftAt = null;
                this.BuildsRequested?.Invoke(
                    this,
                    EventArgs.Empty);
                break;

            case InteractiveKind.AddonsMenuSocial:
                this.gameMenuOpen = false;
                this.menuPointerLeftAt = null;
                this.SocialRequested?.Invoke(
                    this,
                    EventArgs.Empty);
                break;

            case InteractiveKind.AddonsMenuForgeContributions:
                this.gameMenuOpen = false;
                this.menuPointerLeftAt = null;
                this.ForgeContributionsRequested?.Invoke(
                    this,
                    EventArgs.Empty);
                break;

            case InteractiveKind.AddonsMenuManage:
                this.gameMenuOpen = false;
                this.menuPointerLeftAt = null;
                this.ManageAddonsRequested?.Invoke(
                    this,
                    EventArgs.Empty);
                break;
            case InteractiveKind.WindowDrag:
            default:
                break;
        }

        this.Invalidate();
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);

        if (this.Capture)
        {
            return;
        }

        if (this.windowDrag != null)
        {
            this.CompleteWindowDrag(
                persist: true,
                releaseCapture: false);
        }

        if (this.pressedElement != null)
        {
            this.pressedElement = null;
            this.Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        if (!this.presentationEnabled)
        {
            return;
        }

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var scene = this.ResolveScene();

        foreach (var window in scene.Windows)
        {
            this.DrawWindow(
                e.Graphics,
                window);
        }

        foreach (var widget in scene.Widgets
                     .Where(widget => widget.Kind == WidgetKind.Button))
        {
            this.DrawResolvedWidget(
                e.Graphics,
                widget);
        }

        foreach (var widget in scene.Widgets
                     .Where(widget => widget.Kind == WidgetKind.Label))
        {
            this.DrawResolvedWidget(
                e.Graphics,
                widget);
        }

        if (scene.GameMenu.HasValue)
        {
            this.DrawGameMenu(
                e.Graphics,
                scene.GameMenu.Value);
        }

        this.DrawTooltip(
            e.Graphics,
            scene);

        this.DrawCursorProxy(e.Graphics);
    }

    private void DrawWindow(
        Graphics graphics,
        ResolvedWindow window)
    {
        var definition = window.Entry.Definition;
        var radius = Math.Min(
            definition.CornerRadius,
            Math.Min(
                window.Bounds.Width,
                window.Bounds.Height) / 2);

        FillRoundedRectangle(
            graphics,
            definition.BackgroundColor,
            window.Bounds,
            radius);

        if (definition.HeaderBackgroundColor.Alpha != 0)
        {
            using var headerBrush = new SolidBrush(
                ToColor(definition.HeaderBackgroundColor));

            if (radius <= 0)
            {
                graphics.FillRectangle(
                    headerBrush,
                    window.HeaderBounds);
            }
            else
            {
                graphics.FillRoundedRectangle(
                    headerBrush,
                    window.HeaderBounds,
                    radius);

                var squareLowerHeader = new Rectangle(
                    window.HeaderBounds.Left,
                    window.HeaderBounds.Top + radius,
                    window.HeaderBounds.Width,
                    Math.Max(
                        0,
                        window.HeaderBounds.Height - radius));

                if (squareLowerHeader.Height > 0)
                {
                    graphics.FillRectangle(
                        headerBrush,
                        squareLowerHeader);
                }
            }
        }

        DrawRoundedRectangle(
            graphics,
            definition.BorderColor,
            window.Bounds,
            radius,
            width: 2.0f);

        if (!window.Entry.IsMinimized)
        {
            using var separator = new Pen(
                ToColor(definition.BorderColor),
                width: 1.0f);

            graphics.DrawLine(
                separator,
                window.Bounds.Left + 1,
                window.HeaderBounds.Bottom,
                window.Bounds.Right - 2,
                window.HeaderBounds.Bottom);
        }

        var titleRight = window.Bounds.Right - 8;

        if (!window.CloseBounds.IsEmpty)
        {
            titleRight = Math.Min(
                titleRight,
                window.CloseBounds.Left - 4);
        }

        if (!window.MinimizeBounds.IsEmpty)
        {
            titleRight = Math.Min(
                titleRight,
                window.MinimizeBounds.Left - 4);
        }

        var titleBounds = new Rectangle(
            window.Bounds.Left + 14,
            window.Bounds.Top,
            Math.Max(
                1,
                titleRight - (window.Bounds.Left + 14)),
            WindowHeaderHeight);

        TextRenderer.DrawText(
            graphics,
            definition.Title,
            this.GetFont(
                definition.TitleFontSize,
                bold: true),
            titleBounds,
            ToColor(definition.TitleColor),
            TextFormatFlags.Left |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPrefix |
            TextFormatFlags.NoPadding |
            TextFormatFlags.SingleLine);

        if (!window.MinimizeBounds.IsEmpty)
        {
            this.DrawWindowChrome(
                graphics,
                window,
                window.MinimizeBounds,
                InteractiveKind.WindowMinimize);
        }

        if (!window.CloseBounds.IsEmpty)
        {
            this.DrawWindowChrome(
                graphics,
                window,
                window.CloseBounds,
                InteractiveKind.WindowClose);
        }
    }

    private void DrawWindowChrome(
        Graphics graphics,
        ResolvedWindow window,
        Rectangle bounds,
        InteractiveKind kind)
    {
        var identity = new InteractiveIdentity(
            kind,
            window.Key);

        var isHovered =
            this.lastCursorInteractiveElement == identity;

        var isPressed =
            this.pressedElement == identity;

        if (isHovered || isPressed)
        {
            var color = window.Entry.Definition.ChromeHoverColor;
            using var background = new SolidBrush(
                ToColor(color));

            graphics.FillRoundedRectangle(
                background,
                bounds,
                radius: 4);
        }

        using var pen = new Pen(
            ToColor(window.Entry.Definition.TitleColor),
            width: 1.8f);
        pen.StartCap = LineCap.Round;
        pen.EndCap = LineCap.Round;

        if (kind == InteractiveKind.WindowClose)
        {
            graphics.DrawLine(
                pen,
                bounds.Left + 7,
                bounds.Top + 7,
                bounds.Right - 7,
                bounds.Bottom - 7);

            graphics.DrawLine(
                pen,
                bounds.Right - 7,
                bounds.Top + 7,
                bounds.Left + 7,
                bounds.Bottom - 7);
        }
        else if (window.Entry.IsMinimized)
        {
            var restoreBounds = new Rectangle(
                bounds.Left + 7,
                bounds.Top + 7,
                Math.Max(1, bounds.Width - 14),
                Math.Max(1, bounds.Height - 14));

            graphics.DrawRectangle(
                pen,
                restoreBounds);
        }
        else
        {
            graphics.DrawLine(
                pen,
                bounds.Left + 6,
                bounds.Bottom - 8,
                bounds.Right - 6,
                bounds.Bottom - 8);
        }
    }

    private void DrawGameMenu(
        Graphics graphics,
        ResolvedGameMenu menu)
    {
        var tabIdentity = new InteractiveIdentity(
            InteractiveKind.AddonsMenuTab,
            addonsTabKey);

        var tabHovered =
            this.lastCursorInteractiveElement == tabIdentity;

        using (var tabBrush = new LinearGradientBrush(
                   menu.TabBounds,
                   Color.FromArgb(255, 70, 66, 132),
                   Color.FromArgb(255, 43, 42, 88),
                   LinearGradientMode.Vertical))
        {
            graphics.FillRectangle(
                tabBrush,
                menu.TabBounds);
        }

        using (var tabBorder = new Pen(
                   Color.FromArgb(255, 85, 91, 145),
                   Math.Max(1.0f, menu.Scale)))
        {
            graphics.DrawRectangle(
                tabBorder,
                menu.TabBounds);
        }

        TextRenderer.DrawText(
            graphics,
            "Client Manager",
            this.GetFont(
                9.5f * menu.Scale,
                bold: true),
            menu.TabBounds,
            tabHovered || this.gameMenuOpen
                ? Color.FromArgb(255, 255, 239, 0)
                : Color.White,
            TextFormatFlags.HorizontalCenter |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPrefix |
            TextFormatFlags.NoPadding |
            TextFormatFlags.SingleLine);

        if (!this.gameMenuOpen)
        {
            return;
        }

        using (var menuBrush = new LinearGradientBrush(
                   menu.DropDownBounds,
                   Color.FromArgb(255, 48, 58, 105),
                   Color.FromArgb(255, 35, 43, 82),
                   LinearGradientMode.Vertical))
        {
            graphics.FillRectangle(
                menuBrush,
                menu.DropDownBounds);
        }

        using (var menuBorder = new Pen(
                   Color.FromArgb(255, 83, 96, 151),
                   Math.Max(1.0f, menu.Scale)))
        {
            graphics.DrawRectangle(
                menuBorder,
                menu.DropDownBounds);
        }

        foreach (var item in menu.Items)
        {
            if (item.SeparatorBounds.HasValue)
            {
                using var separator = new Pen(
                    Color.FromArgb(255, 78, 89, 135),
                    Math.Max(1.0f, menu.Scale));

                var separatorBounds =
                    item.SeparatorBounds.Value;

                graphics.DrawLine(
                    separator,
                    separatorBounds.Left,
                    separatorBounds.Top +
                        (separatorBounds.Height / 2),
                    separatorBounds.Right,
                    separatorBounds.Top +
                        (separatorBounds.Height / 2));
            }

            var hovered =
                item.IsEnabled &&
                this.lastCursorInteractiveElement ==
                    item.Identity;

            if (hovered)
            {
                using var hoverBrush = new SolidBrush(
                    Color.FromArgb(255, 54, 68, 121));

                graphics.FillRectangle(
                    hoverBrush,
                    item.Bounds);
            }

            if (item.IsChecked && !item.IsHeader)
            {
                var markerBounds = new Rectangle(
                    item.Bounds.Left +
                        item.TextIndent +
                        Math.Max(4, (int)Math.Round(7 * menu.Scale)),
                    item.Bounds.Top,
                    Math.Max(12, (int)Math.Round(18 * menu.Scale)),
                    item.Bounds.Height);

                TextRenderer.DrawText(
                    graphics,
                    "✓",
                    this.GetFont(
                        10.0f * menu.Scale,
                        bold: true),
                    markerBounds,
                    Color.FromArgb(255, 75, 255, 73),
                    TextFormatFlags.Left |
                    TextFormatFlags.VerticalCenter |
                    TextFormatFlags.NoPrefix |
                    TextFormatFlags.NoPadding |
                    TextFormatFlags.SingleLine);
            }

            var textLeftInset =
                item.TextIndent +
                Math.Max(21, (int)Math.Round(27 * menu.Scale));

            var textBounds = new Rectangle(
                item.Bounds.Left + textLeftInset,
                item.Bounds.Top,
                Math.Max(
                    1,
                    item.Bounds.Width -
                    textLeftInset -
                    Math.Max(4, (int)Math.Round(6 * menu.Scale))),
                item.Bounds.Height);

            TextRenderer.DrawText(
                graphics,
                item.Text,
                this.GetFont(
                    10.5f * menu.Scale,
                    bold: true),
                textBounds,
                item.IsHeader
                    ? Color.FromArgb(255, 192, 202, 232)
                    : !item.IsEnabled
                        ? Color.FromArgb(255, 145, 151, 171)
                        : hovered
                            ? Color.FromArgb(255, 255, 239, 0)
                            : Color.White,
                TextFormatFlags.Left |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix |
                TextFormatFlags.NoPadding |
                TextFormatFlags.SingleLine);
        }
    }

    private void DrawTooltip(
        Graphics graphics,
        ResolvedScene scene)
    {
        if (this.inputSuppressed ||
            !this.tooltipVisible ||
            this.tooltipHoverIdentity == null)
        {
            return;
        }

        var tooltip = GetTooltipText(
            scene,
            this.tooltipHoverIdentity.Value);

        if (string.IsNullOrWhiteSpace(tooltip))
        {
            return;
        }

        var scale = scene.GameMenu?.Scale ?? 1.0f;
        var font = this.GetFont(
            9.5f * scale,
            bold: true);

        var paddingX = Math.Max(
            8,
            (int)Math.Round(10 * scale));

        var paddingY = Math.Max(
            5,
            (int)Math.Round(7 * scale));

        var maximumTextWidth = Math.Max(
            120,
            Math.Min(
                (int)Math.Round(430 * scale),
                Math.Max(
                    120,
                    this.ClientSize.Width -
                    (paddingX * 2) -
                    8)));

        const TextFormatFlags measurementFlags =
            TextFormatFlags.Left |
            TextFormatFlags.Top |
            TextFormatFlags.WordBreak |
            TextFormatFlags.NoPrefix |
            TextFormatFlags.NoPadding;

        var measured = TextRenderer.MeasureText(
            tooltip,
            font,
            new Size(
                maximumTextWidth,
                Math.Max(
                    1,
                    this.ClientSize.Height -
                    (paddingY * 2))),
            measurementFlags);

        var size = new Size(
            Math.Min(
                this.ClientSize.Width,
                measured.Width + (paddingX * 2)),
            Math.Min(
                this.ClientSize.Height,
                measured.Height + (paddingY * 2)));

        var left = this.lastCursorClientPoint.X +
            Math.Max(14, (int)Math.Round(18 * scale));

        var top = this.lastCursorClientPoint.Y -
            Math.Max(4, (int)Math.Round(7 * scale));

        if (scene.GameMenu is { IsOpen: true } gameMenu &&
            this.tooltipHoverIdentity.Value.Kind is
                InteractiveKind.AddonsMenuWindow or
                InteractiveKind.AddonsMenuToggle or
                InteractiveKind.AddonsMenuOptions or
                InteractiveKind.AddonsMenuGalaxyAtlas or
                InteractiveKind.AddonsMenuWorldFind or
                InteractiveKind.AddonsMenuPilotArchive or
                InteractiveKind.AddonsMenuBuilds or
                InteractiveKind.AddonsMenuForgeContributions or
                InteractiveKind.AddonsMenuManage)
        {
            var hoveredItem = gameMenu.Items
                .Where(item =>
                    item.Identity == this.tooltipHoverIdentity.Value)
                .Select(item => (ResolvedGameMenuItem?)item)
                .FirstOrDefault();
            var gap = Math.Max(8, (int)Math.Round(10 * scale));

            left = gameMenu.DropDownBounds.Left >= size.Width + gap
                ? gameMenu.DropDownBounds.Left - size.Width - gap
                : gameMenu.DropDownBounds.Right + gap;

            top = hoveredItem.HasValue
                ? hoveredItem.Value.Bounds.Top +
                  ((hoveredItem.Value.Bounds.Height - size.Height) / 2)
                : gameMenu.DropDownBounds.Top;
        }

        left = Math.Clamp(
            left,
            0,
            Math.Max(0, this.ClientSize.Width - size.Width));

        top = Math.Clamp(
            top,
            0,
            Math.Max(0, this.ClientSize.Height - size.Height));

        var bounds = new Rectangle(
            new Point(left, top),
            size);

        using var background = new SolidBrush(
            Color.FromArgb(248, 12, 18, 35));

        using var border = new Pen(
            Color.FromArgb(255, 67, 91, 145),
            Math.Max(1.0f, scale));

        graphics.FillRectangle(background, bounds);
        graphics.DrawRectangle(border, bounds);

        var textBounds = Rectangle.Inflate(
            bounds,
            -paddingX,
            -paddingY);

        TextRenderer.DrawText(
            graphics,
            tooltip,
            font,
            textBounds,
            Color.FromArgb(255, 210, 235, 242),
            measurementFlags);
    }

    private static string GetTooltipText(
        ResolvedScene scene,
        InteractiveIdentity identity)
    {
        if (identity.Kind == InteractiveKind.Button)
        {
            return scene.Widgets
                .FirstOrDefault(widget =>
                    widget.Kind == WidgetKind.Button &&
                    widget.Key == identity.Key)
                .Button?.Tooltip ?? "";
        }

        if (identity.Kind == InteractiveKind.AddonsMenuTab)
        {
            return string.Empty;
        }

        if (scene.GameMenu is not { } menu)
        {
            return "";
        }

        return menu.Items
            .FirstOrDefault(item => item.Identity == identity)
            .Tooltip ?? "";
    }

    private void ToggleWindowFromMenu(WidgetKey key)
    {
        if (!this.windows.TryGetValue(
                key,
                out var window))
        {
            return;
        }

        var wantsWindowShown =
            window.IsVisible &&
            !window.IsClosed;

        if (wantsWindowShown)
        {
            window.IsVisible = false;
            this.PersistWindowState(key, window);
            return;
        }

        window.IsClosed = false;
        window.IsVisible = true;
        this.PersistWindowState(key, window);
    }

    private void ToggleAddonMenuOption(WidgetKey key)
    {
        if (!this.menuToggles.TryGetValue(
                key,
                out var toggle))
        {
            return;
        }

        var isChecked = !toggle.IsChecked;
        this.menuToggles[key] = toggle with
        {
            IsChecked = isChecked,
        };

        this.UiInteractionRaised?.Invoke(
            this,
            new AddonUiInteractionEventArgs(
                new AddonUiInteraction
                {
                    Kind = AddonUiInteractionKind.MenuToggleChanged,
                    OwnerProcessId = this.ownerProcessId,
                    AddonId = key.AddonId,
                    WidgetId = key.WidgetId,
                    IsChecked = isChecked,
                    ObservedAt = DateTimeOffset.UtcNow,
                }));
    }

    private void DrawResolvedWidget(
        Graphics graphics,
        ResolvedWidget widget)
    {
        var savedState = graphics.Save();

        try
        {
            if (widget.ClipBounds.HasValue)
            {
                graphics.SetClip(
                    widget.ClipBounds.Value,
                    CombineMode.Intersect);
            }

            switch (widget.Kind)
            {
                case WidgetKind.Label:
                    this.DrawLabel(
                        graphics,
                        widget.Bounds,
                        widget.Label!);
                    break;

                case WidgetKind.Button:
                    this.DrawButton(
                        graphics,
                        widget.Key,
                        widget.Bounds,
                        widget.Button!);
                    break;
                case WidgetKind.Window:
                    break;
                case WidgetKind.GameMenu:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(widget), widget, message: null);
            }
        }
        finally
        {
            graphics.Restore(savedState);
        }
    }

    private void DrawLabel(
        Graphics graphics,
        Rectangle bounds,
        AddonUiLabel label)
    {
        FillRoundedRectangle(
            graphics,
            label.BackgroundColor,
            bounds,
            label.CornerRadius);

        DrawRoundedRectangle(
            graphics,
            label.BorderColor,
            bounds,
            label.CornerRadius);

        this.DrawWidgetText(
            graphics,
            bounds,
            label.Text,
            label.FontSize,
            label.IsBold,
            label.TextAlignment,
            label.Padding,
            label.TextColor);
    }

    private void DrawButton(
        Graphics graphics,
        WidgetKey key,
        Rectangle bounds,
        AddonUiButton button)
    {
        var identity = new InteractiveIdentity(
            InteractiveKind.Button,
            key);

        var isPressed =
            this.pressedElement == identity;

        var isHovered =
            this.lastCursorInteractiveElement == identity;

        var backgroundColor = !button.IsEnabled
            ? button.DisabledBackgroundColor
            : isPressed
                ? button.PressedBackgroundColor
                : isHovered
                    ? button.HoverBackgroundColor
                    : button.BackgroundColor;

        var borderColor = !button.IsEnabled
            ? button.DisabledBorderColor
            : button.BorderColor;

        var textColor = !button.IsEnabled
            ? button.DisabledTextColor
            : button.TextColor;

        FillRoundedRectangle(
            graphics,
            backgroundColor,
            bounds,
            button.CornerRadius);

        DrawRoundedRectangle(
            graphics,
            borderColor,
            bounds,
            button.CornerRadius);

        this.DrawWidgetText(
            graphics,
            bounds,
            button.Text,
            button.FontSize,
            button.IsBold,
            button.TextAlignment,
            button.Padding,
            textColor);
    }

    private void DrawWidgetText(
        Graphics graphics,
        Rectangle bounds,
        string text,
        float fontSize,
        bool bold,
        AddonUiTextAlignment alignment,
        int padding,
        AddonUiColor color)
    {
        if (color.Alpha == 0)
        {
            return;
        }

        var font = this.GetFont(fontSize, bold);
        var horizontalPadding = Math.Min(
            padding,
            Math.Max(0, (bounds.Width - 1) / 2));

        // Keep requested padding where space allows, but never shrink the
        // text rectangle below the font's line height. Small transparent HUD
        // labels commonly use 24-26 px rows, where the old fixed 8 px vertical
        // inset clipped the upper and lower halves of the glyphs.
        var verticalPadding = Math.Min(
            padding,
            Math.Max(0, (bounds.Height - font.Height) / 2));

        var textBounds = new Rectangle(
            bounds.Left + horizontalPadding,
            bounds.Top + verticalPadding,
            Math.Max(
                1,
                bounds.Width - (horizontalPadding * 2)),
            Math.Max(
                1,
                bounds.Height - (verticalPadding * 2)));

        var alignmentFlag =
            alignment == AddonUiTextAlignment.Center
                ? TextFormatFlags.HorizontalCenter
                : alignment == AddonUiTextAlignment.Right
                    ? TextFormatFlags.Right
                    : TextFormatFlags.Left;

        TextRenderer.DrawText(
            graphics,
            text,
            font,
            textBounds,
            ToColor(color),
            alignmentFlag |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPrefix |
            TextFormatFlags.NoPadding |
            TextFormatFlags.SingleLine);
    }

    private void DrawCursorProxy(Graphics graphics)
    {
        if (this.inputSuppressed ||
            !this.ClientRectangle.Contains(
                this.lastCursorClientPoint) ||
            this.lastCursorOwnedElement == null)
        {
            return;
        }

        var cursor = this.windowDrag != null ||
                     this.lastCursorInteractiveElement?.Kind ==
                     InteractiveKind.WindowDrag
            ? Cursors.SizeAll
            : this.lastCursorInteractiveElement != null
                ? Cursors.Hand
                : Cursors.Arrow;

        var location = new Point(
            this.lastCursorClientPoint.X - cursor.HotSpot.X,
            this.lastCursorClientPoint.Y - cursor.HotSpot.Y);

        cursor.Draw(
            graphics,
            new Rectangle(location, cursor.Size));
    }

    private AddonWindowPlacement? ResolvePersistedWindowPlacement(
        WidgetKey key)
    {
        return this.resolveWindowPlacement(
            key.AddonId,
            key.WidgetId);
    }

    private void PersistWindowState(
        WidgetKey key,
        WindowEntry entry)
    {
        this.saveWindowPlacement(
            key.AddonId,
            key.WidgetId,
            new AddonWindowPlacement
            {
                AddonId = key.AddonId,
                WidgetId = key.WidgetId,
                OffsetX = entry.PositionOffset.X,
                OffsetY = entry.PositionOffset.Y,
                Width = entry.UserSize?.Width ?? 0,
                Height = entry.UserSize?.Height ?? 0,
                IsClosed = entry.IsClosed,
                IsVisible = entry.IsVisible,
                IsMinimized = entry.IsMinimized,
                HorizontalEdge = entry.HorizontalEdge,
                MinimizedOffsetX = entry.MinimizedOffsetX,
                MinimizedOffsetY = entry.MinimizedOffsetY,
            });
    }

    private static Size? ResolvePersistedWindowSize(
        AddonWindowPlacement? placement)
    {
        return placement is { Width: > 0, Height: > 0 }
            ? new Size(placement.Width, placement.Height)
            : null;
    }

    private void UpdateWindowDrag(Point pointerLocation)
    {
        var drag = this.windowDrag;

        if (drag == null ||
            !this.windows.TryGetValue(
                drag.Key,
                out var entry))
        {
            this.CompleteWindowDrag(persist: false);
            return;
        }

        var delta = new Size(
            pointerLocation.X - drag.PointerStart.X,
            pointerLocation.Y - drag.PointerStart.Y);

        var desiredLocation = new Point(
            drag.WindowStart.X + delta.Width,
            drag.WindowStart.Y + delta.Height);

        if (drag.IsMinimized)
        {
            var fullSize = this.ResolveFullWindowSize(entry);
            var displayedSize = new Size(
                ResolveMinimizedWindowWidth(
                    entry.Definition,
                    fullSize),
                WindowHeaderHeight);

            var displayedLocation = ClampLocation(
                desiredLocation,
                displayedSize,
                this.ClientRectangle);

            var displayedBounds = new Rectangle(
                displayedLocation,
                displayedSize);

            var horizontalEdge = ResolveNearestHorizontalEdge(
                displayedBounds,
                this.ClientRectangle);

            var expandedBounds =
                this.ResolveExpandedWindowBounds(entry);

            var baseMinimizedLeft = horizontalEdge ==
                                    AddonWindowHorizontalEdge.Right
                ? expandedBounds.Right - displayedSize.Width
                : expandedBounds.Left;

            var minimizedOffsetX =
                displayedLocation.X - baseMinimizedLeft;

            var minimizedOffsetY =
                displayedLocation.Y - expandedBounds.Top;

            if (entry.HorizontalEdge == horizontalEdge &&
                entry.MinimizedOffsetX == minimizedOffsetX &&
                entry.MinimizedOffsetY == minimizedOffsetY)
            {
                return;
            }

            entry.HorizontalEdge = horizontalEdge;
            entry.MinimizedOffsetX = minimizedOffsetX;
            entry.MinimizedOffsetY = minimizedOffsetY;
            drag.HasMoved = true;
            this.Invalidate();
            return;
        }

        var definition = entry.Definition;
        var expandedSize = this.ResolveFullWindowSize(entry);
        var expandedLocation = ClampLocation(
            desiredLocation,
            expandedSize,
            this.ClientRectangle);

        var defaultLocation = ResolveUnclampedLocation(
            definition.Anchor,
            definition.X,
            definition.Y,
            expandedSize,
            this.ClientRectangle);

        var offset = new Point(
            expandedLocation.X - defaultLocation.X,
            expandedLocation.Y - defaultLocation.Y);

        var horizontalEdgeNotMinimized = ResolveNearestHorizontalEdge(
            new Rectangle(expandedLocation, expandedSize),
            this.ClientRectangle);

        if (entry.PositionOffset == offset &&
            entry.HorizontalEdge == horizontalEdgeNotMinimized &&
            entry.MinimizedOffsetX == 0 &&
            entry.MinimizedOffsetY == 0)
        {
            return;
        }

        entry.PositionOffset = offset;
        entry.HorizontalEdge = horizontalEdgeNotMinimized;
        entry.MinimizedOffsetX = 0;
        entry.MinimizedOffsetY = 0;
        drag.HasMoved = true;
        this.Invalidate();
    }

    private void CompleteWindowDrag(
        bool persist,
        bool releaseCapture = true)
    {
        var drag = this.windowDrag;

        if (drag == null)
        {
            return;
        }

        this.windowDrag = null;

        if (releaseCapture && this.Capture)
        {
            this.Capture = false;
        }

        if (persist &&
            drag.HasMoved &&
            this.windows.TryGetValue(
                drag.Key,
                out var entry))
        {
            this.PersistWindowState(drag.Key, entry);
        }
    }

    private Size ResolveRequestedFullWindowSize(WindowEntry entry)
    {
        var preferredSize = entry.UserSize ?? new Size(
            entry.Definition.Width,
            entry.Definition.Height);

        return new Size(
            Math.Max(120, preferredSize.Width),
            Math.Max(
                WindowHeaderHeight,
                preferredSize.Height));
    }

    private Size ResolveFullWindowSize(WindowEntry entry)
    {
        return ClampSizeToContainer(
            this.ResolveRequestedFullWindowSize(entry),
            this.ClientRectangle.Size);
    }

    private Rectangle ResolveExpandedWindowBounds(WindowEntry entry)
    {
        var definition = entry.Definition;
        var fullSize = this.ResolveFullWindowSize(entry);
        var defaultLocation = ResolveUnclampedLocation(
            definition.Anchor,
            definition.X,
            definition.Y,
            fullSize,
            this.ClientRectangle);

        var location = ClampLocation(
            new Point(
                defaultLocation.X + entry.PositionOffset.X,
                defaultLocation.Y + entry.PositionOffset.Y),
            fullSize,
            this.ClientRectangle);

        return new Rectangle(location, fullSize);
    }

    private static int ResolveMinimizedWindowWidth(
        AddonUiWindow definition,
        Size fullSize)
    {
        return definition.MinimizedWidth > 0
            ? Math.Min(
                fullSize.Width,
                Math.Max(120, definition.MinimizedWidth))
            : fullSize.Width;
    }

    private ResolvedScene ResolveScene()
    {
        if (!this.presentationEnabled)
        {
            return new ResolvedScene(
                [],
                [],
                GameMenu: null);
        }

        var resolvedWindows = this.windows
            .Where(item =>
                item.Value is
                {
                    IsAvailable: true,
                    Definition.IsVisible: true,
                    IsVisible: true,
                    IsClosed: false,
                })
            .OrderBy(item => item.Key.AddonId, StringComparer.Ordinal)
            .ThenBy(item => item.Key.WidgetId, StringComparer.Ordinal)
            .Select(item => this.ResolveWindow(item.Key, item.Value))
            .ToArray();

        var windowByKey = resolvedWindows.ToDictionary(
            window => window.Key);

        List<ResolvedWidget> resolvedWidgets = [];

        foreach (var item in this.buttons
                     .OrderBy(item => item.Key.AddonId, StringComparer.Ordinal)
                     .ThenBy(item => item.Key.WidgetId, StringComparer.Ordinal))
        {
            if (this.TryResolveButton(
                    item.Key,
                    item.Value,
                    windowByKey,
                    out var resolved))
            {
                resolvedWidgets.Add(resolved);
            }
        }

        foreach (var item in this.labels
                     .OrderBy(item => item.Key.AddonId, StringComparer.Ordinal)
                     .ThenBy(item => item.Key.WidgetId, StringComparer.Ordinal))
        {
            if (this.TryResolveLabel(
                    item.Key,
                    item.Value,
                    windowByKey,
                    out var resolved))
            {
                resolvedWidgets.Add(resolved);
            }
        }

        var gameMenu = this.gameMenuVisible
            ? this.ResolveGameMenu()
            : (ResolvedGameMenu?)null;

        return new ResolvedScene(
            resolvedWindows,
            resolvedWidgets,
            gameMenu);
    }

    private ResolvedGameMenu ResolveGameMenu()
    {
        var scaleX = this.ClientSize.Width /
            (float)HostUiBaseWidth;

        var scaleY = this.ClientSize.Height /
            (float)HostUiBaseHeight;

        var scale = Math.Max(
            0.5f,
            Math.Min(scaleX, scaleY));

        var tabBounds = new Rectangle(
            ScaleCoordinate(AddonsTabBaseX, scaleX),
            ScaleCoordinate(AddonsTabBaseY, scaleY),
            Math.Max(
                1,
                ScaleCoordinate(AddonsTabBaseWidth, scaleX)),
            Math.Max(
                1,
                ScaleCoordinate(AddonsTabBaseHeight, scaleY)));

        tabBounds = ClampRectangleToClient(tabBounds, this.ClientRectangle);

        var itemHeight = Math.Max(
            20,
            ScaleCoordinate(AddonsMenuItemBaseHeight, scaleY));

        var padding = Math.Max(
            4,
            ScaleCoordinate(AddonsMenuPaddingBase, scaleY));

        var separatorHeight = Math.Max(
            4,
            ScaleCoordinate(
                AddonsMenuSeparatorBaseHeight,
                scaleY));

        var optionIndent = Math.Max(
            12,
            ScaleCoordinate(18, scale));

        var registeredItems = this.windowMenuItems
            .Where(item => this.windows.ContainsKey(item.Key))
            .Select(item =>
            {
                var window = this.windows[item.Key];

                return new RegisteredAddonMenuItem(
                    item.Key,
                    item.Value.AddonName,
                    item.Value.Text,
                    item.Value.Tooltip,
                    item.Value.Order,
                    window.IsVisible && !window.IsClosed,
                    AddonMenuItemKind.Window);
            })
            .Concat(
                this.menuToggles.Select(item =>
                    new RegisteredAddonMenuItem(
                        item.Key,
                        item.Value.AddonName,
                        item.Value.Text,
                        item.Value.Tooltip,
                        item.Value.Order,
                        item.Value.IsChecked,
                        AddonMenuItemKind.Toggle)))
            .ToArray();

        var sections = registeredItems
            .GroupBy(
                item => item.Key.AddonId,
                StringComparer.Ordinal)
            .Select(group =>
            {
                var orderedItems = group
                    .OrderBy(item => item.Order)
                    .ThenBy(
                        item => item.Kind)
                    .ThenBy(
                        item => item.Text,
                        StringComparer.OrdinalIgnoreCase)
                    .ThenBy(
                        item => item.Key.WidgetId,
                        StringComparer.Ordinal)
                    .ToArray();

                return new RegisteredAddonMenuSection(
                    group.Key,
                    orderedItems[0].AddonName,
                    orderedItems.Min(item => item.Order),
                    orderedItems);
            })
            .OrderBy(section => section.Order)
            .ThenBy(
                section => section.AddonName,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(
                section => section.AddonId,
                StringComparer.Ordinal)
            .ToArray();

        var menuFont = this.GetFont(
            10.5f * scale,
            bold: true);

        var baseTextInset = Math.Max(
            21,
            (int)Math.Round(27 * scale));

        var textRightPadding = Math.Max(
            8,
            (int)Math.Round(10 * scale));

        var requiredMenuWidth = Math.Max(
            120,
            ScaleCoordinate(AddonsMenuBaseWidth, scaleX));

        static int MeasureMenuTextWidth(
            string text,
            Font font)
        {
            return TextRenderer.MeasureText(
                text,
                font,
                proposedSize: Size.Empty,
                TextFormatFlags.NoPrefix |
                TextFormatFlags.NoPadding |
                TextFormatFlags.SingleLine).Width;
        }

        foreach (var text in new[]
                 {
                     "Options",
                     "Galaxy Atlas",
                     "Galaxy Finder",
                     "Social",
                     "Pilot Archive",
                     "Builds",
                     "Forge Contributions",
                     "Addon Center",
                 })
        {
            requiredMenuWidth = Math.Max(
                requiredMenuWidth,
                (padding * 2) +
                baseTextInset +
                MeasureMenuTextWidth(text, menuFont) +
                textRightPadding);
        }

        foreach (var section in sections)
        {
            requiredMenuWidth = Math.Max(
                requiredMenuWidth,
                (padding * 2) +
                baseTextInset +
                MeasureMenuTextWidth(
                    section.AddonName,
                    menuFont) +
                textRightPadding);

            var windowItemCount = section.Items.Count(item =>
                item.Kind == AddonMenuItemKind.Window);

            foreach (var item in section.Items)
            {
                var text = item.Kind == AddonMenuItemKind.Window &&
                           windowItemCount == 1
                    ? "Show"
                    : item.Text;

                requiredMenuWidth = Math.Max(
                    requiredMenuWidth,
                    (padding * 2) +
                    baseTextInset +
                    optionIndent +
                    MeasureMenuTextWidth(text, menuFont) +
                    textRightPadding);
            }
        }

        var maximumMenuWidth = Math.Max(
            120,
            Math.Min(
                ScaleCoordinate(420, scaleX),
                this.ClientRectangle.Width));

        var menuWidth = Math.Clamp(
            requiredMenuWidth,
            120,
            maximumMenuWidth);

        const int builtInItemCount = 7;
        var registeredRowCount = sections.Sum(section =>
            1 + section.Items.Count);

        var separatorCount = 2 + sections.Length;

        var menuHeight =
            (padding * 2) +
            ((1 + registeredRowCount + builtInItemCount) * itemHeight) +
            (separatorCount * separatorHeight);

        var menuLeft = tabBounds.Left;

        if (menuLeft + menuWidth > this.ClientRectangle.Right)
        {
            menuLeft = Math.Max(
                this.ClientRectangle.Left,
                this.ClientRectangle.Right - menuWidth);
        }

        var menuTop = tabBounds.Bottom;

        if (menuTop + menuHeight > this.ClientRectangle.Bottom)
        {
            menuTop = Math.Max(
                this.ClientRectangle.Top,
                tabBounds.Top - menuHeight);
        }

        var dropDownBounds = new Rectangle(
            menuLeft,
            menuTop,
            menuWidth,
            Math.Min(
                menuHeight,
                Math.Max(1, this.ClientRectangle.Height)));

        List<ResolvedGameMenuItem> items = [];
        var currentTop = dropDownBounds.Top + padding;

        Rectangle CreateItemBounds()
        {
            return new Rectangle(
                dropDownBounds.Left + 1,
                currentTop,
                Math.Max(1, dropDownBounds.Width - 2),
                itemHeight);
        }

        Rectangle CreateSeparatorBounds()
        {
            return new Rectangle(
                dropDownBounds.Left + padding,
                currentTop,
                Math.Max(
                    1,
                    dropDownBounds.Width - (padding * 2)),
                separatorHeight);
        }

        items.Add(
            new ResolvedGameMenuItem(
                new InteractiveIdentity(
                    InteractiveKind.AddonsMenuOptions,
                    optionsKey),
                CreateItemBounds(),
                SeparatorBounds: null,
                "Options",
                string.Empty,
                IsChecked: false,
                IsEnabled: true,
                IsHeader: false,
                TextIndent: 0));

        currentTop += itemHeight;

        var optionsSeparator = CreateSeparatorBounds();
        currentTop += separatorHeight;

        items.Add(
            new ResolvedGameMenuItem(
                new InteractiveIdentity(
                    InteractiveKind.AddonsMenuGalaxyAtlas,
                    galaxyAtlasKey),
                CreateItemBounds(),
                optionsSeparator,
                "Galaxy Atlas",
                string.Empty,
                IsChecked: false,
                IsEnabled: true,
                IsHeader: false,
                TextIndent: 0));

        currentTop += itemHeight;

        items.Add(
            new ResolvedGameMenuItem(
                new InteractiveIdentity(
                    InteractiveKind.AddonsMenuWorldFind,
                    worldFindKey),
                CreateItemBounds(),
                SeparatorBounds: null,
                "Galaxy Finder",
                string.Empty,
                IsChecked: false,
                IsEnabled: true,
                IsHeader: false,
                TextIndent: 0));

        currentTop += itemHeight;

        items.Add(
            new ResolvedGameMenuItem(
                new InteractiveIdentity(
                    InteractiveKind.AddonsMenuSocial,
                    socialKey),
                CreateItemBounds(),
                SeparatorBounds: null,
                "Social",
                string.Empty,
                IsChecked: false,
                IsEnabled: true,
                IsHeader: false,
                TextIndent: 0));

        currentTop += itemHeight;

        items.Add(
            new ResolvedGameMenuItem(
                new InteractiveIdentity(
                    InteractiveKind.AddonsMenuPilotArchive,
                    pilotArchiveKey),
                CreateItemBounds(),
                SeparatorBounds: null,
                "Pilot Archive",
                string.Empty,
                IsChecked: false,
                IsEnabled: true,
                IsHeader: false,
                TextIndent: 0));

        currentTop += itemHeight;

        items.Add(
            new ResolvedGameMenuItem(
                new InteractiveIdentity(
                    InteractiveKind.AddonsMenuBuilds,
                    buildsKey),
                CreateItemBounds(),
                SeparatorBounds: null,
                "Builds",
                string.Empty,
                IsChecked: false,
                IsEnabled: true,
                IsHeader: false,
                TextIndent: 0));

        currentTop += itemHeight;

        var managementSeparator = CreateSeparatorBounds();
        currentTop += separatorHeight;

        items.Add(
            new ResolvedGameMenuItem(
                new InteractiveIdentity(
                    InteractiveKind.AddonsMenuForgeContributions,
                    forgeContributionsKey),
                CreateItemBounds(),
                managementSeparator,
                "Forge Contributions",
                string.Empty,
                IsChecked: false,
                IsEnabled: true,
                IsHeader: false,
                TextIndent: 0));

        currentTop += itemHeight;

        items.Add(
            new ResolvedGameMenuItem(
                new InteractiveIdentity(
                    InteractiveKind.AddonsMenuManage,
                    manageAddonsKey),
                CreateItemBounds(),
                SeparatorBounds: null,
                "Addon Center",
                string.Empty,
                IsChecked: false,
                IsEnabled: true,
                IsHeader: false,
                TextIndent: 0));

        currentTop += itemHeight;

        foreach (var section in sections)
        {
            var sectionSeparator = CreateSeparatorBounds();
            currentTop += separatorHeight;

            var headerKey = new WidgetKey(
                section.AddonId,
                "__menu_header");

            items.Add(
                new ResolvedGameMenuItem(
                    new InteractiveIdentity(
                        InteractiveKind.AddonsMenuHeader,
                        headerKey),
                    CreateItemBounds(),
                    sectionSeparator,
                    section.AddonName,
                    string.Empty,
                    IsChecked: false,
                    IsEnabled: false,
                    IsHeader: true,
                    TextIndent: 0));

            currentTop += itemHeight;

            var windowItemCount = section.Items.Count(item =>
                item.Kind == AddonMenuItemKind.Window);

            foreach (var item in section.Items)
            {
                var text = item.Kind == AddonMenuItemKind.Window &&
                           windowItemCount == 1
                    ? "Show"
                    : item.Text;

                items.Add(
                    new ResolvedGameMenuItem(
                        new InteractiveIdentity(
                            item.Kind == AddonMenuItemKind.Window
                                ? InteractiveKind.AddonsMenuWindow
                                : InteractiveKind.AddonsMenuToggle,
                            item.Key),
                        CreateItemBounds(),
                        SeparatorBounds: null,
                        text,
                        item.Tooltip,
                        item.IsChecked,
                        IsEnabled: true,
                        IsHeader: false,
                        TextIndent: optionIndent));

                currentTop += itemHeight;
            }
        }

        return new ResolvedGameMenu(
            tabBounds,
            dropDownBounds,
            items,
            scale,
            this.gameMenuOpen);
    }

    private ResolvedWindow ResolveWindow(
        WidgetKey key,
        WindowEntry entry)
    {
        var definition = entry.Definition;
        var requestedFullSize =
            this.ResolveRequestedFullWindowSize(entry);
        var fullSize = this.ResolveFullWindowSize(entry);
        var minimizedWidth = ResolveMinimizedWindowWidth(
            definition,
            fullSize);

        var displayedSize = entry.IsMinimized
            ? new Size(minimizedWidth, WindowHeaderHeight)
            : fullSize;

        var defaultLocation = ResolveUnclampedLocation(
            definition.Anchor,
            definition.X,
            definition.Y,
            fullSize,
            this.ClientRectangle);

        var unclampedExpandedLocation = new Point(
            defaultLocation.X + entry.PositionOffset.X,
            defaultLocation.Y + entry.PositionOffset.Y);

        var expandedLocation = ClampLocation(
            unclampedExpandedLocation,
            fullSize,
            this.ClientRectangle);

        var expandedBounds = new Rectangle(
            expandedLocation,
            fullSize);

        Point location;

        if (entry.IsMinimized)
        {
            var horizontalEdge = ResolveHorizontalEdge(
                entry.HorizontalEdge,
                expandedBounds,
                this.ClientRectangle);

            var minimizedLeft = horizontalEdge ==
                                AddonWindowHorizontalEdge.Right
                ? expandedBounds.Right - minimizedWidth
                : expandedBounds.Left;

            var minimizedLocation = new Point(
                minimizedLeft + entry.MinimizedOffsetX,
                expandedBounds.Top + entry.MinimizedOffsetY);

            location = ClampLocation(
                minimizedLocation,
                displayedSize,
                this.ClientRectangle);
        }
        else
        {
            location = expandedLocation;
        }

        var bounds = new Rectangle(
            location,
            displayedSize);

        var headerBounds = new Rectangle(
            bounds.Left,
            bounds.Top,
            bounds.Width,
            WindowHeaderHeight);

        var windowScale = Math.Min(
            1.0f,
            Math.Min(
                fullSize.Width / (float)requestedFullSize.Width,
                fullSize.Height / (float)requestedFullSize.Height));

        var padding = Math.Min(
            Math.Max(
                0,
                ScaleCoordinate(
                    definition.ContentPadding,
                    windowScale)),
            Math.Max(0, fullSize.Width / 3));

        var contentBounds = entry.IsMinimized
            ? Rectangle.Empty
            : new Rectangle(
                bounds.Left + padding,
                bounds.Top + WindowHeaderHeight + padding,
                Math.Max(0, bounds.Width - (padding * 2)),
                Math.Max(
                    0,
                    bounds.Height - WindowHeaderHeight -
                    (padding * 2)));

        var requestedPadding = Math.Min(
            Math.Max(0, definition.ContentPadding),
            Math.Max(0, requestedFullSize.Width / 3));

        var requestedContentWidth = Math.Max(
            1,
            requestedFullSize.Width - (requestedPadding * 2));

        var requestedContentHeight = Math.Max(
            1,
            requestedFullSize.Height - WindowHeaderHeight -
            (requestedPadding * 2));

        var contentScaleX = entry.IsMinimized
            ? 1.0f
            : Math.Min(
                1.0f,
                contentBounds.Width / (float)requestedContentWidth);

        var contentScaleY = entry.IsMinimized
            ? 1.0f
            : Math.Min(
                1.0f,
                contentBounds.Height / (float)requestedContentHeight);

        var chromeRight = bounds.Right -
            WindowChromeRightMargin;

        var closeBounds = Rectangle.Empty;
        var minimizeBounds = Rectangle.Empty;

        if (definition.CanClose)
        {
            closeBounds = new Rectangle(
                chromeRight - WindowChromeSize,
                bounds.Top +
                ((WindowHeaderHeight - WindowChromeSize) / 2),
                WindowChromeSize,
                WindowChromeSize);

            chromeRight = closeBounds.Left -
                WindowChromeGap;
        }

        if (definition.CanMinimize)
        {
            minimizeBounds = new Rectangle(
                chromeRight - WindowChromeSize,
                bounds.Top +
                ((WindowHeaderHeight - WindowChromeSize) / 2),
                WindowChromeSize,
                WindowChromeSize);
        }

        return new ResolvedWindow(
            key,
            entry,
            bounds,
            headerBounds,
            contentBounds,
            contentScaleX,
            contentScaleY,
            minimizeBounds,
            closeBounds);
    }

    private bool TryResolveLabel(
        WidgetKey key,
        AddonUiLabel label,
        IReadOnlyDictionary<WidgetKey, ResolvedWindow> windowsByKey,
        out ResolvedWidget widget)
    {
        var container = this.ClientRectangle;
        Rectangle? clip = null;

        if (!string.IsNullOrEmpty(label.ParentWidgetId))
        {
            var parentKey = key with { WidgetId = label.ParentWidgetId };

            if (!windowsByKey.TryGetValue(
                    parentKey,
                    out var window) ||
                window.Entry.IsMinimized ||
                window.ContentBounds.IsEmpty)
            {
                widget = default;
                return false;
            }

            label = ResolveWindowCoordinateSpace(
                label,
                window);
            container = window.ContentBounds;
            clip = window.ContentBounds;
        }
        else
        {
            label = this.ResolveCoordinateSpace(label);
        }

        var size = this.MeasureLabel(
            label,
            container.Size);

        var location = ResolveLocation(
            label.Anchor,
            label.X,
            label.Y,
            size,
            container);

        widget = new ResolvedWidget(
            key,
            WidgetKind.Label,
            new Rectangle(location, size),
            clip,
            label,
            Button: null);

        return true;
    }

    private bool TryResolveButton(
        WidgetKey key,
        AddonUiButton button,
        IReadOnlyDictionary<WidgetKey, ResolvedWindow> windowsByKey,
        out ResolvedWidget widget)
    {
        var container = this.ClientRectangle;
        Rectangle? clip = null;

        if (!string.IsNullOrEmpty(button.ParentWidgetId))
        {
            var parentKey = key with { WidgetId = button.ParentWidgetId };

            if (!windowsByKey.TryGetValue(
                    parentKey,
                    out var window) ||
                window.Entry.IsMinimized ||
                window.ContentBounds.IsEmpty)
            {
                widget = default;
                return false;
            }

            button = ResolveWindowCoordinateSpace(
                button,
                window);
            container = window.ContentBounds;
            clip = window.ContentBounds;
        }
        else
        {
            button = this.ResolveCoordinateSpace(button);
        }

        var size = this.MeasureButton(
            button,
            container.Size);

        var location = ResolveLocation(
            button.Anchor,
            button.X,
            button.Y,
            size,
            container);

        widget = new ResolvedWidget(
            key,
            WidgetKind.Button,
            new Rectangle(location, size),
            clip,
            Label: null,
            button);

        return true;
    }

    private AddonUiLabel ResolveCoordinateSpace(
        AddonUiLabel label)
    {
        if (label.CoordinateSpace !=
                AddonUiCoordinateSpace.GameCanvas ||
            !string.IsNullOrEmpty(label.ParentWidgetId))
        {
            return label;
        }

        var scaleX = this.ClientSize.Width /
                     (float)HostUiBaseWidth;
        var scaleY = this.ClientSize.Height /
                     (float)HostUiBaseHeight;
        var scale = Math.Max(
            0.5f,
            Math.Min(scaleX, scaleY));

        return label with
        {
            X = ScaleCoordinate(label.X, scaleX),
            Y = ScaleCoordinate(label.Y, scaleY),
            Width = label.Width > 0
                ? Math.Max(1, ScaleCoordinate(label.Width, scaleX))
                : 0,
            Height = label.Height > 0
                ? Math.Max(1, ScaleCoordinate(label.Height, scaleY))
                : 0,
            FontSize = Math.Clamp(
                label.FontSize * scale,
                8.0f,
                48.0f),
            Padding = Math.Max(
                0,
                ScaleCoordinate(label.Padding, scale)),
            CornerRadius = Math.Max(
                0,
                ScaleCoordinate(label.CornerRadius, scale)),
        };
    }

    private AddonUiButton ResolveCoordinateSpace(
        AddonUiButton button)
    {
        if (button.CoordinateSpace !=
                AddonUiCoordinateSpace.GameCanvas ||
            !string.IsNullOrEmpty(button.ParentWidgetId))
        {
            return button;
        }

        var scaleX = this.ClientSize.Width /
                     (float)HostUiBaseWidth;
        var scaleY = this.ClientSize.Height /
                     (float)HostUiBaseHeight;
        var scale = Math.Max(
            0.5f,
            Math.Min(scaleX, scaleY));

        return button with
        {
            X = ScaleCoordinate(button.X, scaleX),
            Y = ScaleCoordinate(button.Y, scaleY),
            Width = button.Width > 0
                ? Math.Max(1, ScaleCoordinate(button.Width, scaleX))
                : 0,
            Height = button.Height > 0
                ? Math.Max(1, ScaleCoordinate(button.Height, scaleY))
                : 0,
            FontSize = Math.Clamp(
                button.FontSize * scale,
                8.0f,
                48.0f),
            Padding = Math.Max(
                0,
                ScaleCoordinate(button.Padding, scale)),
            CornerRadius = Math.Max(
                0,
                ScaleCoordinate(button.CornerRadius, scale)),
        };
    }

    private static AddonUiLabel ResolveWindowCoordinateSpace(
        AddonUiLabel label,
        ResolvedWindow window)
    {
        var scaleX = window.ContentScaleX;
        var scaleY = window.ContentScaleY;
        var scale = Math.Min(scaleX, scaleY);

        if (scaleX >= 1.0f &&
            scaleY >= 1.0f)
        {
            return label;
        }

        return label with
        {
            X = ScaleCoordinate(label.X, scaleX),
            Y = ScaleCoordinate(label.Y, scaleY),
            Width = label.Width > 0
                ? Math.Max(
                    1,
                    ScaleCoordinate(label.Width, scaleX))
                : 0,
            Height = label.Height > 0
                ? Math.Max(
                    1,
                    ScaleCoordinate(label.Height, scaleY))
                : 0,
            FontSize = Math.Clamp(
                label.FontSize * scale,
                8.0f,
                48.0f),
            Padding = Math.Max(
                0,
                ScaleCoordinate(label.Padding, scale)),
            CornerRadius = Math.Max(
                0,
                ScaleCoordinate(label.CornerRadius, scale)),
        };
    }

    private static AddonUiButton ResolveWindowCoordinateSpace(
        AddonUiButton button,
        ResolvedWindow window)
    {
        var scaleX = window.ContentScaleX;
        var scaleY = window.ContentScaleY;
        var scale = Math.Min(scaleX, scaleY);

        if (scaleX >= 1.0f &&
            scaleY >= 1.0f)
        {
            return button;
        }

        return button with
        {
            X = ScaleCoordinate(button.X, scaleX),
            Y = ScaleCoordinate(button.Y, scaleY),
            Width = button.Width > 0
                ? Math.Max(
                    1,
                    ScaleCoordinate(button.Width, scaleX))
                : 0,
            Height = button.Height > 0
                ? Math.Max(
                    1,
                    ScaleCoordinate(button.Height, scaleY))
                : 0,
            FontSize = Math.Clamp(
                button.FontSize * scale,
                8.0f,
                48.0f),
            Padding = Math.Max(
                0,
                ScaleCoordinate(button.Padding, scale)),
            CornerRadius = Math.Max(
                0,
                ScaleCoordinate(button.CornerRadius, scale)),
        };
    }

    private Size MeasureLabel(
        AddonUiLabel label,
        Size containerSize)
    {
        var textSize = TextRenderer.MeasureText(
            label.Text,
            this.GetFont(
                label.FontSize,
                label.IsBold),
            proposedSize: Size.Empty,
            TextFormatFlags.NoPadding |
            TextFormatFlags.SingleLine);

        var width = label.Width > 0
            ? label.Width
            : textSize.Width + (label.Padding * 2);

        var height = label.Height > 0
            ? label.Height
            : textSize.Height +
              (Math.Min(label.Padding, 8) * 2);

        return ClampSizeToContainer(
            new Size(
                Math.Max(1, width),
                Math.Max(1, height)),
            containerSize);
    }

    private Size MeasureButton(
        AddonUiButton button,
        Size containerSize)
    {
        var textSize = TextRenderer.MeasureText(
            button.Text,
            this.GetFont(
                button.FontSize,
                button.IsBold),
            proposedSize: Size.Empty,
            TextFormatFlags.NoPadding |
            TextFormatFlags.SingleLine);

        var width = button.Width > 0
            ? button.Width
            : textSize.Width + (button.Padding * 2) + 4;

        var height = button.Height > 0
            ? button.Height
            : Math.Max(
                26,
                textSize.Height +
                (Math.Min(button.Padding, 8) * 2));

        return ClampSizeToContainer(
            new Size(
                Math.Max(1, width),
                Math.Max(1, height)),
            containerSize);
    }

    private Font GetFont(
        float size,
        bool bold)
    {
        var key = new FontKey(
            (int)Math.Round(size * 4.0f),
            bold);

        if (this.fonts.TryGetValue(
                key,
                out var existing))
        {
            return existing;
        }

        var created = new Font(
            FontFamily.GenericSansSerif,
            Math.Max(1.0f, key.QuarterPoints / 4.0f),
            bold
                ? FontStyle.Bold
                : FontStyle.Regular,
            GraphicsUnit.Point);

        this.fonts[key] = created;
        return created;
    }

    private static ResolvedInteractive? FindTopmostInteractive(
        ResolvedScene scene,
        Point clientPoint)
    {
        if (scene.GameMenu is { } menu)
        {
            if (menu.TabBounds.Contains(clientPoint))
            {
                return new ResolvedInteractive(
                    new InteractiveIdentity(
                        InteractiveKind.AddonsMenuTab,
                        addonsTabKey),
                    menu.TabBounds);
            }

            if (menu.IsOpen)
            {
                foreach (var item in menu.Items.Reverse())
                {
                    if (!item.IsEnabled ||
                        !item.Bounds.Contains(clientPoint))
                    {
                        continue;
                    }

                    return new ResolvedInteractive(
                        item.Identity,
                        item.Bounds);
                }
            }

        }

        foreach (var window in scene.Windows.Reverse())
        {
            if (!window.CloseBounds.IsEmpty &&
                window.CloseBounds.Contains(clientPoint))
            {
                return new ResolvedInteractive(
                    new InteractiveIdentity(
                        InteractiveKind.WindowClose,
                        window.Key),
                    window.CloseBounds);
            }

            if (!window.MinimizeBounds.IsEmpty &&
                window.MinimizeBounds.Contains(clientPoint))
            {
                return new ResolvedInteractive(
                    new InteractiveIdentity(
                        InteractiveKind.WindowMinimize,
                        window.Key),
                    window.MinimizeBounds);
            }

            if (window.HeaderBounds.Contains(clientPoint))
            {
                return new ResolvedInteractive(
                    new InteractiveIdentity(
                        InteractiveKind.WindowDrag,
                        window.Key),
                    window.HeaderBounds);
            }
        }

        foreach (var widget in scene.Widgets.Reverse())
        {
            if (widget is { Kind: WidgetKind.Button, Button.IsEnabled: true } &&
                widget.Bounds.Contains(clientPoint))
            {
                return new ResolvedInteractive(
                    new InteractiveIdentity(
                        InteractiveKind.Button,
                        widget.Key),
                    widget.Bounds);
            }
        }

        return null;
    }

    private static InteractiveIdentity? FindTooltipIdentity(
        ResolvedScene scene,
        Point clientPoint)
    {
        if (scene.GameMenu is { } menu)
        {
            if (menu.TabBounds.Contains(clientPoint))
            {
                return new InteractiveIdentity(
                    InteractiveKind.AddonsMenuTab,
                    addonsTabKey);
            }

            if (menu.IsOpen)
            {
                foreach (var item in menu.Items.Reverse())
                {
                    if (item.Bounds.Contains(clientPoint) &&
                        !string.IsNullOrWhiteSpace(item.Tooltip))
                    {
                        return item.Identity;
                    }
                }
            }
        }

        foreach (var widget in scene.Widgets.Reverse())
        {
            if (widget.Kind != WidgetKind.Button ||
                string.IsNullOrWhiteSpace(widget.Button?.Tooltip) ||
                !widget.Bounds.Contains(clientPoint) ||
                (widget.ClipBounds.HasValue &&
                 !widget.ClipBounds.Value.Contains(clientPoint)))
            {
                continue;
            }

            return new InteractiveIdentity(
                InteractiveKind.Button,
                widget.Key);
        }

        return null;
    }

    private static OwnedIdentity? FindTopmostOwned(
        ResolvedScene scene,
        Point clientPoint)
    {
        if (scene.GameMenu is { } menu)
        {
            if (menu.TabBounds.Contains(clientPoint) ||
                (menu.IsOpen &&
                 menu.DropDownBounds.Contains(clientPoint)))
            {
                return new OwnedIdentity(
                    WidgetKind.GameMenu,
                    addonsTabKey);
            }
        }

        foreach (var widget in scene.Widgets.Reverse())
        {
            if (widget.Bounds.Contains(clientPoint))
            {
                return new OwnedIdentity(
                    widget.Kind,
                    widget.Key);
            }
        }

        foreach (var window in scene.Windows.Reverse())
        {
            if (window.Bounds.Contains(clientPoint))
            {
                return new OwnedIdentity(
                    WidgetKind.Window,
                    window.Key);
            }
        }

        return null;
    }

    private void CursorRefreshTimer_OnTick(
        object? sender,
        EventArgs e)
    {
        if (this.IsDisposed ||
            this.Disposing ||
            !this.Visible ||
            !this.presentationEnabled)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var cursorClientPoint = this.PointToClient(
            System.Windows.Forms.Cursor.Position);

        var scene = this.ResolveScene();
        var menuStateChanged = false;

        if (this.gameMenuOpen &&
            scene.GameMenu is { } openMenu)
        {
            var overMenu =
                openMenu.TabBounds.Contains(cursorClientPoint) ||
                openMenu.DropDownBounds.Contains(cursorClientPoint);

            if (overMenu)
            {
                this.menuPointerLeftAt = null;
            }
            else
            {
                this.menuPointerLeftAt ??= now;

                if ((now - this.menuPointerLeftAt.Value)
                        .TotalMilliseconds >=
                    AddonsMenuCloseDelayMilliseconds)
                {
                    this.gameMenuOpen = false;
                    this.menuPointerLeftAt = null;
                    this.tooltipVisible = false;
                    this.tooltipHoverIdentity = null;
                    menuStateChanged = true;
                    scene = this.ResolveScene();
                }
            }
        }
        else
        {
            this.menuPointerLeftAt = null;
        }

        var owned = this.inputSuppressed
            ? null
            : this.windowDrag != null
                ? new OwnedIdentity(
                    WidgetKind.Window,
                    this.windowDrag.Key)
                : FindTopmostOwned(
                    scene,
                    cursorClientPoint);

        var interactive = this.inputSuppressed
            ? null
            : this.windowDrag != null
                ? new InteractiveIdentity(
                    InteractiveKind.WindowDrag,
                    this.windowDrag.Key)
                : FindTopmostInteractive(
                    scene,
                    cursorClientPoint)
                    ?.Identity;

        var tooltipCandidate =
            this.inputSuppressed ||
            this.windowDrag != null
                ? null
                : FindTooltipIdentity(
                    scene,
                    cursorClientPoint);

        var tooltipStateChanged = false;

        if (tooltipCandidate != this.tooltipHoverIdentity)
        {
            this.tooltipHoverIdentity = tooltipCandidate;
            this.tooltipHoverStartedAt = now;

            if (this.tooltipVisible)
            {
                this.tooltipVisible = false;
            }

            tooltipStateChanged = true;
        }
        else if (tooltipCandidate != null &&
                 !this.tooltipVisible &&
                 (now - this.tooltipHoverStartedAt)
                     .TotalMilliseconds >=
                 TooltipDelayMilliseconds)
        {
            this.tooltipVisible = true;
            tooltipStateChanged = true;
        }
        else if (tooltipCandidate == null &&
                 this.tooltipVisible)
        {
            this.tooltipVisible = false;
            tooltipStateChanged = true;
        }

        if (cursorClientPoint == this.lastCursorClientPoint &&
            owned == this.lastCursorOwnedElement &&
            interactive == this.lastCursorInteractiveElement &&
            !menuStateChanged &&
            !tooltipStateChanged)
        {
            return;
        }

        var previousCursorPoint = this.lastCursorClientPoint;
        var previouslyOwned =
            this.lastCursorOwnedElement != null;

        this.lastCursorClientPoint = cursorClientPoint;
        this.lastCursorOwnedElement = owned;
        this.lastCursorInteractiveElement = interactive;

        if (previouslyOwned)
        {
            this.Invalidate(
                GetCursorInvalidationBounds(
                    previousCursorPoint));
        }

        if (owned != null)
        {
            this.Invalidate(
                GetCursorInvalidationBounds(
                    cursorClientPoint));
        }

        this.Invalidate();
    }

    private void RemoveAddon(string addonId)
    {
        foreach (var key in this.windows.Keys
                     .Where(key => string.Equals(
                         key.AddonId,
                         addonId,
                         StringComparison.Ordinal))
                     .ToArray())
        {
            this.RemoveWindowAndChildren(key);
        }

        foreach (var key in this.labels.Keys
                     .Where(key => string.Equals(
                         key.AddonId,
                         addonId,
                         StringComparison.Ordinal))
                     .ToArray())
        {
            this.labels.Remove(key);
        }

        foreach (var key in this.buttons.Keys
                     .Where(key => string.Equals(
                         key.AddonId,
                         addonId,
                         StringComparison.Ordinal))
                     .ToArray())
        {
            this.buttons.Remove(key);
        }

        foreach (var key in this.windowMenuItems.Keys
                     .Where(key => string.Equals(
                         key.AddonId,
                         addonId,
                         StringComparison.Ordinal))
                     .ToArray())
        {
            this.windowMenuItems.Remove(key);
        }

        foreach (var key in this.menuToggles.Keys
                     .Where(key => string.Equals(
                         key.AddonId,
                         addonId,
                         StringComparison.Ordinal))
                     .ToArray())
        {
            this.menuToggles.Remove(key);
        }

        if (this.pressedElement is { } pressed &&
            string.Equals(
                pressed.Key.AddonId,
                addonId,
                StringComparison.Ordinal))
        {
            this.pressedElement = null;
            this.Capture = false;
        }
    }

    private void RemoveWindowAndChildren(
        WidgetKey key)
    {
        this.windowMenuItems.Remove(key);
        this.ReleasePressedElementIfRemoved(key);

        if (!this.windows.Remove(key))
        {
            return;
        }

        foreach (var child in this.labels
                     .Where(item =>
                         string.Equals(
                             item.Key.AddonId,
                             key.AddonId,
                             StringComparison.Ordinal) &&
                         string.Equals(
                             item.Value.ParentWidgetId,
                             key.WidgetId,
                             StringComparison.Ordinal))
                     .Select(item => item.Key)
                     .ToArray())
        {
            this.labels.Remove(child);
        }

        foreach (var child in this.buttons
                     .Where(item =>
                         string.Equals(
                             item.Key.AddonId,
                             key.AddonId,
                             StringComparison.Ordinal) &&
                         string.Equals(
                             item.Value.ParentWidgetId,
                             key.WidgetId,
                             StringComparison.Ordinal))
                     .Select(item => item.Key)
                     .ToArray())
        {
            this.buttons.Remove(child);
            this.ReleasePressedElementIfRemoved(child);
        }
    }

    private void ReleasePressedElementIfRemoved(
        WidgetKey key)
    {
        if (this.windowDrag?.Key == key)
        {
            this.CompleteWindowDrag(persist: true);
        }

        if (this.pressedElement is not { } pressed ||
            pressed.Key != key)
        {
            return;
        }

        this.pressedElement = null;
        this.Capture = false;
    }

    private static void FillRoundedRectangle(
        Graphics graphics,
        AddonUiColor color,
        Rectangle bounds,
        int radius)
    {
        if (color.Alpha == 0 ||
            bounds.Width <= 0 ||
            bounds.Height <= 0)
        {
            return;
        }

        using var brush = new SolidBrush(
            ToColor(color));

        if (radius <= 0)
        {
            graphics.FillRectangle(brush, bounds);
            return;
        }

        graphics.FillRoundedRectangle(
            brush,
            bounds,
            radius);
    }

    private static void DrawRoundedRectangle(
        Graphics graphics,
        AddonUiColor color,
        Rectangle bounds,
        int radius,
        float width = 1.0f)
    {
        if (color.Alpha == 0 ||
            bounds.Width <= 0 ||
            bounds.Height <= 0)
        {
            return;
        }

        using var pen = new Pen(
            ToColor(color),
            width);

        if (radius <= 0)
        {
            graphics.DrawRectangle(pen, bounds);
            return;
        }

        graphics.DrawRoundedRectangle(
            pen,
            bounds,
            radius);
    }

    private static Color ToColor(
        AddonUiColor color)
    {
        return Color.FromArgb(
            color.Alpha,
            color.Red,
            color.Green,
            color.Blue);
    }

    private static Rectangle GetCursorInvalidationBounds(
        Point point)
    {
        return new Rectangle(
            point.X - 36,
            point.Y - 36,
            72,
            72);
    }

    private static Size ClampSizeToContainer(
        Size size,
        Size containerSize)
    {
        return new Size(
            Math.Min(
                size.Width,
                Math.Max(1, containerSize.Width)),
            Math.Min(
                size.Height,
                Math.Max(1, containerSize.Height)));
    }

    private static Point ResolveLocation(
        AddonUiAnchor anchor,
        int x,
        int y,
        Size widgetSize,
        Rectangle container)
    {
        return ClampLocation(
            ResolveUnclampedLocation(
                anchor,
                x,
                y,
                widgetSize,
                container),
            widgetSize,
            container);
    }

    private static Point ResolveUnclampedLocation(
        AddonUiAnchor anchor,
        int x,
        int y,
        Size widgetSize,
        Rectangle container)
    {
        int left;
        int top;

        switch (anchor)
        {
            case AddonUiAnchor.TopLeft:
                left = container.Left + x;
                top = container.Top + y;
                break;

            case AddonUiAnchor.TopCenter:
                left = container.Left +
                    ((container.Width - widgetSize.Width) / 2) + x;
                top = container.Top + y;
                break;

            case AddonUiAnchor.TopRight:
                left = container.Right - widgetSize.Width - x;
                top = container.Top + y;
                break;

            case AddonUiAnchor.CenterLeft:
                left = container.Left + x;
                top = container.Top +
                    ((container.Height - widgetSize.Height) / 2) + y;
                break;

            case AddonUiAnchor.Center:
                left = container.Left +
                    ((container.Width - widgetSize.Width) / 2) + x;
                top = container.Top +
                    ((container.Height - widgetSize.Height) / 2) + y;
                break;

            case AddonUiAnchor.CenterRight:
                left = container.Right - widgetSize.Width - x;
                top = container.Top +
                    ((container.Height - widgetSize.Height) / 2) + y;
                break;

            case AddonUiAnchor.BottomLeft:
                left = container.Left + x;
                top = container.Bottom - widgetSize.Height - y;
                break;

            case AddonUiAnchor.BottomCenter:
                left = container.Left +
                    ((container.Width - widgetSize.Width) / 2) + x;
                top = container.Bottom - widgetSize.Height - y;
                break;

            case AddonUiAnchor.BottomRight:
                left = container.Right - widgetSize.Width - x;
                top = container.Bottom - widgetSize.Height - y;
                break;

            default:
                left = container.Left + x;
                top = container.Top + y;
                break;
        }

        return new Point(left, top);
    }

    private static AddonWindowHorizontalEdge ResolveHorizontalEdge(
        AddonWindowHorizontalEdge preferredEdge,
        Rectangle windowBounds,
        Rectangle container)
    {
        return preferredEdge is
            AddonWindowHorizontalEdge.Left or
            AddonWindowHorizontalEdge.Right
            ? preferredEdge
            : ResolveNearestHorizontalEdge(
                windowBounds,
                container);
    }

    private static AddonWindowHorizontalEdge ResolveNearestHorizontalEdge(
        Rectangle windowBounds,
        Rectangle container)
    {
        var windowCenter = windowBounds.Left +
                           (windowBounds.Width / 2);

        var containerCenter = container.Left +
                              (container.Width / 2);

        return windowCenter <= containerCenter
            ? AddonWindowHorizontalEdge.Left
            : AddonWindowHorizontalEdge.Right;
    }

    private static Point ClampLocation(
        Point location,
        Size widgetSize,
        Rectangle container)
    {
        return new Point(
            Math.Clamp(
                location.X,
                container.Left,
                Math.Max(
                    container.Left,
                    container.Right - widgetSize.Width)),
            Math.Clamp(
                location.Y,
                container.Top,
                Math.Max(
                    container.Top,
                    container.Bottom - widgetSize.Height)));
    }

    private static int ScaleCoordinate(
        int value,
        float scale)
    {
        return (int)Math.Round(
            value * scale,
            MidpointRounding.AwayFromZero);
    }

    private static Rectangle ClampRectangleToClient(
        Rectangle bounds,
        Rectangle clientBounds)
    {
        var width = Math.Min(
            Math.Max(1, bounds.Width),
            Math.Max(1, clientBounds.Width));

        var height = Math.Min(
            Math.Max(1, bounds.Height),
            Math.Max(1, clientBounds.Height));

        var left = Math.Clamp(
            bounds.Left,
            clientBounds.Left,
            Math.Max(
                clientBounds.Left,
                clientBounds.Right - width));

        var top = Math.Clamp(
            bounds.Top,
            clientBounds.Top,
            Math.Max(
                clientBounds.Top,
                clientBounds.Bottom - height));

        return new Rectangle(
            left,
            top,
            width,
            height);
    }

    private static Point GetScreenPoint(IntPtr lParam)
    {
        var value = lParam.ToInt64();

        return new Point(
            unchecked((short)(value & 0xffff)),
            unchecked((short)((value >> 16) & 0xffff)));
    }

    private enum WidgetKind
    {
        Window,
        Label,
        Button,
        GameMenu,
    }

    private enum InteractiveKind
    {
        Button,
        WindowDrag,
        WindowMinimize,
        WindowClose,
        AddonsMenuTab,
        AddonsMenuWindow,
        AddonsMenuToggle,
        AddonsMenuHeader,
        AddonsMenuOptions,
        AddonsMenuGalaxyAtlas,
        AddonsMenuWorldFind,
        AddonsMenuPilotArchive,
        AddonsMenuBuilds,
        AddonsMenuSocial,
        AddonsMenuForgeContributions,
        AddonsMenuManage,
    }

    private sealed class WindowEntry
    {
        public required AddonUiWindow Definition { get; set; }

        // Cartesian delta from the addon-authored anchored location.
        public Point PositionOffset { get; set; }

        // Null keeps the addon-authored size. Reserved for windows that
        // opt into user resizing.
        public Size? UserSize { get; set; }

        public bool IsVisible { get; set; } = true;

        public bool IsMinimized { get; set; }

        public bool IsClosed { get; set; }

        public AddonWindowHorizontalEdge HorizontalEdge { get; set; }

        public int MinimizedOffsetX { get; set; }

        public int MinimizedOffsetY { get; set; }

        public bool IsAvailable { get; set; } = true;

        public string UnavailableReason { get; set; } = "";
    }

    private sealed class WindowDragState
    {
        public required WidgetKey Key { get; init; }

        public required Point PointerStart { get; init; }

        public required Point WindowStart { get; init; }

        public required bool IsMinimized { get; init; }

        public bool HasMoved { get; set; }
    }

    private readonly record struct WidgetKey(
        string AddonId,
        string WidgetId);

    private readonly record struct FontKey(
        int QuarterPoints,
        bool Bold);

    private readonly record struct InteractiveIdentity(
        InteractiveKind Kind,
        WidgetKey Key);

    private readonly record struct OwnedIdentity(
        WidgetKind Kind,
        WidgetKey Key);

    private readonly record struct ResolvedInteractive(
        InteractiveIdentity Identity,
        Rectangle Bounds);

    private readonly record struct ResolvedWindow(
        WidgetKey Key,
        WindowEntry Entry,
        Rectangle Bounds,
        Rectangle HeaderBounds,
        Rectangle ContentBounds,
        float ContentScaleX,
        float ContentScaleY,
        Rectangle MinimizeBounds,
        Rectangle CloseBounds);

    private readonly record struct ResolvedWidget(
        WidgetKey Key,
        WidgetKind Kind,
        Rectangle Bounds,
        Rectangle? ClipBounds,
        AddonUiLabel? Label,
        AddonUiButton? Button);

    private readonly record struct ResolvedGameMenuItem(
        InteractiveIdentity Identity,
        Rectangle Bounds,
        Rectangle? SeparatorBounds,
        string Text,
        string Tooltip,
        bool IsChecked,
        bool IsEnabled,
        bool IsHeader,
        int TextIndent);

    private enum AddonMenuItemKind
    {
        Window,
        Toggle,
    }

    private readonly record struct RegisteredAddonMenuItem(
        WidgetKey Key,
        string AddonName,
        string Text,
        string Tooltip,
        int Order,
        bool IsChecked,
        AddonMenuItemKind Kind);

    private readonly record struct RegisteredAddonMenuSection(
        string AddonId,
        string AddonName,
        int Order,
        IReadOnlyList<RegisteredAddonMenuItem> Items);

    private readonly record struct ResolvedGameMenu(
        Rectangle TabBounds,
        Rectangle DropDownBounds,
        IReadOnlyList<ResolvedGameMenuItem> Items,
        float Scale,
        bool IsOpen);

    private readonly record struct ResolvedScene(
        IReadOnlyList<ResolvedWindow> Windows,
        IReadOnlyList<ResolvedWidget> Widgets,
        ResolvedGameMenu? GameMenu);
}

