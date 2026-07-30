// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using System.ComponentModel;

internal sealed class MainWindowTitleBar : Control
{
    private const int ChromeButtonWidth = 44;
    private const int IconSize = 18;
    private const int LeftPadding = 12;

    private string titleText = "";
    private Icon? windowIcon;
    private bool showMinimizeButton;
    private bool showMaximizeButton;
    private bool showCloseButton = true;
    private bool showHelpButton;
    private string helpTopicId = HelpTopicIds.Home;
    private Func<int?>? helpProcessIdProvider;
    private Func<bool>? helpOverride;
    private bool isMaximized;
    private ChromeRegion hoveredRegion;
    private ChromeRegion pressedRegion;

    public MainWindowTitleBar()
    {
        this.SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            value: true);

        this.TabStop = false;
        this.BackColor = MainWindowTheme.Header;
        this.ForeColor = MainWindowTheme.Text;
    }

    public event EventHandler? DragRequested;

    public event EventHandler? MinimizeRequested;

    public event EventHandler? MaximizeRequested;

    public event EventHandler? CloseRequested;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string TitleText
    {
        get => this.titleText;
        set
        {
            if (string.Equals(this.titleText, value, StringComparison.Ordinal))
            {
                return;
            }

            this.titleText = value;
            this.Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Icon? WindowIcon
    {
        get => this.windowIcon;
        set
        {
            if (ReferenceEquals(this.windowIcon, value))
            {
                return;
            }

            this.windowIcon = value;
            this.Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool ShowMinimizeButton
    {
        get => this.showMinimizeButton;
        set
        {
            if (this.showMinimizeButton == value)
            {
                return;
            }

            this.showMinimizeButton = value;
            this.Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool ShowMaximizeButton
    {
        get => this.showMaximizeButton;
        set
        {
            if (this.showMaximizeButton == value)
            {
                return;
            }

            this.showMaximizeButton = value;
            this.Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool ShowCloseButton
    {
        get => this.showCloseButton;
        set
        {
            if (this.showCloseButton == value)
            {
                return;
            }

            this.showCloseButton = value;
            this.Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool ShowHelpButton
    {
        get => this.showHelpButton;
        set
        {
            if (this.showHelpButton == value)
            {
                return;
            }

            this.showHelpButton = value;
            this.Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string HelpTopicId
    {
        get => this.helpTopicId;
        set => this.helpTopicId = string.IsNullOrWhiteSpace(value)
            ? HelpTopicIds.Home
            : value;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<int?>? HelpProcessIdProvider
    {
        get => this.helpProcessIdProvider;
        set => this.helpProcessIdProvider = value;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<bool>? HelpOverride
    {
        get => this.helpOverride;
        set => this.helpOverride = value;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IsMaximized
    {
        get => this.isMaximized;
        set
        {
            if (this.isMaximized == value)
            {
                return;
            }

            this.isMaximized = value;
            this.Invalidate();
        }
    }

    private int ChromeButtonCount =>
        Convert.ToInt32(this.showHelpButton) +
        Convert.ToInt32(this.showMinimizeButton) +
        Convert.ToInt32(this.showMaximizeButton) +
        Convert.ToInt32(this.showCloseButton);

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        e.Graphics.Clear(MainWindowTheme.Header);

        var iconLeft = LeftPadding;
        var iconTop = Math.Max(0, (this.ClientSize.Height - IconSize) / 2);

        if (this.windowIcon != null)
        {
            e.Graphics.DrawIcon(
                this.windowIcon,
                new Rectangle(
                    iconLeft,
                    iconTop,
                    IconSize,
                    IconSize));
        }

        var titleLeft = this.windowIcon == null
            ? LeftPadding
            : iconLeft + IconSize + 9;
        var titleRight = this.ClientSize.Width -
                         (this.ChromeButtonCount * ChromeButtonWidth) -
                         10;

        TextRenderer.DrawText(
            e.Graphics,
            this.titleText,
            MainWindowTheme.CreateBodyFont(size: 9.5f),
            Rectangle.FromLTRB(
                titleLeft,
                0,
                Math.Max(titleLeft, titleRight),
                this.ClientSize.Height),
            MainWindowTheme.Text,
            TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPrefix |
            TextFormatFlags.NoPadding |
            TextFormatFlags.SingleLine |
            TextFormatFlags.VerticalCenter);

        this.DrawChrome(e.Graphics);

        using var borderPen = new Pen(MainWindowTheme.Border);
        e.Graphics.DrawLine(
            borderPen,
            x1: 0,
            y1: this.ClientSize.Height - 1,
            x2: this.ClientSize.Width,
            y2: this.ClientSize.Height - 1);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var region = this.HitTestChrome(e.Location);

        if (region == this.hoveredRegion)
        {
            return;
        }

        this.hoveredRegion = region;
        this.InvalidateChrome();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);

        this.hoveredRegion = ChromeRegion.None;
        this.pressedRegion = ChromeRegion.None;
        this.Capture = false;
        this.InvalidateChrome();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        var region = this.HitTestChrome(e.Location);

        if (region == ChromeRegion.None)
        {
            if (e.Clicks >= 2)
            {
                this.MaximizeRequested?.Invoke(this, EventArgs.Empty);
                return;
            }

            this.DragRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        this.pressedRegion = region;
        this.Capture = true;
        this.InvalidateChrome();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (e.Button != MouseButtons.Left ||
            this.pressedRegion == ChromeRegion.None)
        {
            return;
        }

        var pressedRegion = this.pressedRegion;
        var releasedRegion = this.HitTestChrome(e.Location);

        this.pressedRegion = ChromeRegion.None;
        this.Capture = false;
        this.InvalidateChrome();

        if (pressedRegion != releasedRegion)
        {
            return;
        }

        switch (pressedRegion)
        {
            case ChromeRegion.Help:
                if (this.helpOverride?.Invoke() != true)
                {
                    HelpCenterLauncher.Show(
                        this.FindForm(),
                        this.helpTopicId,
                        this.helpProcessIdProvider?.Invoke());
                }
                break;

            case ChromeRegion.Minimize:
                this.MinimizeRequested?.Invoke(this, EventArgs.Empty);
                break;

            case ChromeRegion.Maximize:
                this.MaximizeRequested?.Invoke(this, EventArgs.Empty);
                break;

            case ChromeRegion.Close:
                this.CloseRequested?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    private void DrawChrome(Graphics graphics)
    {
        foreach (var region in this.GetVisibleChromeRegions())
        {
            var bounds = this.GetChromeBounds(region);
            var isPressed = this.pressedRegion == region;
            var isHovered = this.hoveredRegion == region;

            if (isPressed || isHovered)
            {
                var background = region == ChromeRegion.Close
                    ? isPressed
                        ? Color.FromArgb(157, 43, 55)
                        : Color.FromArgb(198, 58, 70)
                    : isPressed
                        ? MainWindowTheme.ElevatedPanel
                        : MainWindowTheme.ButtonHover;

                using var brush = new SolidBrush(background);
                graphics.FillRectangle(brush, bounds);
            }

            this.DrawChromeGlyph(graphics, region, bounds);
        }
    }

    private void DrawChromeGlyph(
        Graphics graphics,
        ChromeRegion region,
        Rectangle bounds)
    {
        using var pen = new Pen(MainWindowTheme.Text, width: 1.2f);
        var centerX = bounds.Left + (bounds.Width / 2);
        var centerY = bounds.Top + (bounds.Height / 2);

        switch (region)
        {
            case ChromeRegion.Help:
                TextRenderer.DrawText(
                    graphics,
                    "?",
                    MainWindowTheme.CreateHeadingFont(size: 10.5f),
                    bounds,
                    MainWindowTheme.Text,
                    TextFormatFlags.HorizontalCenter |
                    TextFormatFlags.VerticalCenter |
                    TextFormatFlags.NoPadding |
                    TextFormatFlags.SingleLine);
                break;

            case ChromeRegion.Minimize:
                graphics.DrawLine(
                    pen,
                    centerX - 5,
                    centerY + 4,
                    centerX + 5,
                    centerY + 4);
                break;

            case ChromeRegion.Maximize when this.isMaximized:
                graphics.DrawRectangle(
                    pen,
                    centerX - 4,
                    centerY - 5,
                    width: 8,
                    height: 8);
                graphics.DrawRectangle(
                    pen,
                    centerX - 6,
                    centerY - 3,
                    width: 8,
                    height: 8);
                break;

            case ChromeRegion.Maximize:
                graphics.DrawRectangle(
                    pen,
                    centerX - 5,
                    centerY - 5,
                    width: 10,
                    height: 10);
                break;

            case ChromeRegion.Close:
                graphics.DrawLine(
                    pen,
                    centerX - 5,
                    centerY - 5,
                    centerX + 5,
                    centerY + 5);
                graphics.DrawLine(
                    pen,
                    centerX + 5,
                    centerY - 5,
                    centerX - 5,
                    centerY + 5);
                break;
        }
    }

    private ChromeRegion HitTestChrome(Point location)
    {
        foreach (var region in this.GetVisibleChromeRegions())
        {
            if (this.GetChromeBounds(region).Contains(location))
            {
                return region;
            }
        }

        return ChromeRegion.None;
    }

    private List<ChromeRegion> GetVisibleChromeRegions()
    {
        var regions = new List<ChromeRegion>(capacity: 4);

        if (this.showHelpButton)
        {
            regions.Add(ChromeRegion.Help);
        }

        if (this.showMinimizeButton)
        {
            regions.Add(ChromeRegion.Minimize);
        }

        if (this.showMaximizeButton)
        {
            regions.Add(ChromeRegion.Maximize);
        }

        if (this.showCloseButton)
        {
            regions.Add(ChromeRegion.Close);
        }

        return regions;
    }

    private Rectangle GetChromeBounds(ChromeRegion region)
    {
        var visibleRegions = this.GetVisibleChromeRegions();
        var index = visibleRegions.IndexOf(region);

        if (index < 0)
        {
            return Rectangle.Empty;
        }

        var left = this.ClientSize.Width -
                   ((visibleRegions.Count - index) * ChromeButtonWidth);

        return new Rectangle(
            left,
            y: 0,
            ChromeButtonWidth,
            this.ClientSize.Height - 1);
    }

    private void InvalidateChrome()
    {
        var width = this.ChromeButtonCount * ChromeButtonWidth;

        this.Invalidate(new Rectangle(
            this.ClientSize.Width - width,
            y: 0,
            width,
            this.ClientSize.Height));
    }

    private enum ChromeRegion
    {
        None,
        Help,
        Minimize,
        Maximize,
        Close,
    }
}
