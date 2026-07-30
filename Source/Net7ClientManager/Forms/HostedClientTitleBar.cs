// ReSharper disable StringLiteralTypo
// ReSharper disable LocalizableElement
// ReSharper disable UnusedMember.Global
#pragma warning disable IDE0032
namespace Net7ClientManager.Forms;

using System.ComponentModel;
using System.Drawing.Drawing2D;
using Net7ClientManager.Services;

internal sealed class HostedClientTitleBar : Control
{
    private const int ChromeButtonWidth = 42;
    private const int TextLeftPadding = 13;
    private const int TextRightPadding = 13;
    private const int GroupGap = 12;
    private const int SeparatorGap = 10;

    private static readonly TextFormatFlags textFlags =
        TextFormatFlags.NoPadding |
        TextFormatFlags.SingleLine |
        TextFormatFlags.VerticalCenter;

    private static readonly TextFormatFlags measureTextFlags =
        TextFormatFlags.NoPadding |
        TextFormatFlags.NoClipping |
        TextFormatFlags.SingleLine;

    private static readonly Color topColor =
        Color.FromArgb(red: 70, green: 66, blue: 132);

    private static readonly Color bottomColor =
        Color.FromArgb(red: 35, green: 43, blue: 82);

    private static readonly Color borderColor =
        Color.FromArgb(red: 83, green: 96, blue: 151);

    private static readonly Color labelColor =
        Color.FromArgb(red: 190, green: 192, blue: 225);

    private static readonly Color valueColor =
        Color.FromArgb(red: 248, green: 249, blue: 255);

    private static readonly Color emphasizedValueColor =
        Color.FromArgb(red: 211, green: 247, blue: 255);

    private static readonly Color statusValueColor =
        Color.FromArgb(red: 255, green: 232, blue: 134);

    private static readonly Color separatorColor =
        Color.FromArgb(red: 104, green: 111, blue: 164);

    private static readonly Color chromeDividerColor =
        Color.FromArgb(red: 92, green: 99, blue: 151);

    private static readonly Color chromeHoverColor =
        Color.FromArgb(red: 78, green: 89, blue: 135);

    private static readonly Color chromePressedColor =
        Color.FromArgb(red: 54, green: 68, blue: 121);

    private static readonly Color closeHoverColor =
        Color.FromArgb(red: 196, green: 58, blue: 72);

    private static readonly Color closePressedColor =
        Color.FromArgb(red: 154, green: 39, blue: 54);

    private readonly Font labelFont = new(
        "Trebuchet MS",
        9.0f,
        FontStyle.Regular,
        GraphicsUnit.Point);

    private readonly Font valueFont = new(
        "Trebuchet MS",
        10.0f,
        FontStyle.Bold,
        GraphicsUnit.Point);

    private readonly Font chromeFont = new(
        "Segoe UI",
        10.5f,
        FontStyle.Bold,
        GraphicsUnit.Point);

    private HostedClientTitlePresentation? presentation;
    private string titleText = "";
    private bool showMaximizeButton;
    private bool showHelpButton;
    private string helpTopicId = HelpTopicIds.Home;
    private Func<int?>? helpProcessIdProvider;
    private Func<bool>? helpOverride;
    private bool isMaximized;
    private ChromeRegion hoveredRegion;
    private ChromeRegion pressedRegion;

    public HostedClientTitleBar()
    {
        this.SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.Selectable |
            ControlStyles.UserPaint,
            value: true);

        this.TabStop = false;
        this.AccessibleName = "Hosted client title bar";
    }

    public event EventHandler? DragRequested;

    public event EventHandler? MinimizeRequested;

    public event EventHandler? MaximizeRequested;

    public event EventHandler? CloseRequested;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public HostedClientTitlePresentation? Presentation
    {
        get => this.presentation;
        set
        {
            this.presentation = value;
            this.Invalidate();
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string TitleText
    {
        get => this.titleText;
        set
        {
            this.titleText = value;
            this.Invalidate();
        }
    }

    [Browsable(false)]
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

    [Browsable(false)]
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

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string HelpTopicId
    {
        get => this.helpTopicId;
        set => this.helpTopicId = string.IsNullOrWhiteSpace(value)
            ? HelpTopicIds.Home
            : value;
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<int?>? HelpProcessIdProvider
    {
        get => this.helpProcessIdProvider;
        set => this.helpProcessIdProvider = value;
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<bool>? HelpOverride
    {
        get => this.helpOverride;
        set => this.helpOverride = value;
    }

    [Browsable(false)]
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
            this.InvalidateChrome();
        }
    }

    private int ChromeButtonCount =>
        2 +
        Convert.ToInt32(this.showMaximizeButton) +
        Convert.ToInt32(this.showHelpButton);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.labelFont.Dispose();
            this.valueFont.Dispose();
            this.chromeFont.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var bounds = this.ClientRectangle;

        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        using (var background = new LinearGradientBrush(
                   bounds,
                   topColor,
                   bottomColor,
                   LinearGradientMode.Vertical))
        {
            e.Graphics.FillRectangle(background, bounds);
        }

        this.DrawTitle(e.Graphics, bounds);
        this.DrawChrome(e.Graphics, bounds);

        using var border = new Pen(borderColor);
        e.Graphics.DrawLine(
            border,
            bounds.Left,
            bounds.Bottom - 1,
            bounds.Right,
            bounds.Bottom - 1);
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

        if (this.hoveredRegion == ChromeRegion.None &&
            this.pressedRegion == ChromeRegion.None)
        {
            return;
        }

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

        var chromeRegion = this.pressedRegion;
        var releasedRegion = this.HitTestChrome(e.Location);

        this.pressedRegion = ChromeRegion.None;
        this.Capture = false;
        this.InvalidateChrome();

        if (chromeRegion != releasedRegion)
        {
            return;
        }

        switch (chromeRegion)
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
            case ChromeRegion.None:
                break;
            default:
                throw new InvalidOperationException(nameof(chromeRegion));
        }
    }

    private void DrawTitle(Graphics graphics, Rectangle bounds)
    {
        var textBounds = new Rectangle(
            x: TextLeftPadding,
            y: 0,
            width: Math.Max(
                0,
                bounds.Width -
                (ChromeButtonWidth * this.ChromeButtonCount) -
                TextLeftPadding -
                TextRightPadding),
            height: bounds.Height - 1);

        if (textBounds.Width <= 0)
        {
            return;
        }

        if (this.presentation == null ||
            this.presentation.Segments.Count == 0)
        {
            if (string.IsNullOrWhiteSpace(this.titleText))
            {
                return;
            }

            TextRenderer.DrawText(
                graphics,
                this.titleText,
                this.valueFont,
                textBounds,
                emphasizedValueColor,
                textFlags |
                TextFormatFlags.EndEllipsis);

            return;
        }

        var renderSegments = this.SelectSegments(
            textBounds.Width);

        var x = textBounds.Left;
        var first = true;

        foreach (var segment in renderSegments)
        {
            if (!first)
            {
                x += SeparatorGap;

                using var separatorPen = new Pen(separatorColor);
                var separatorTop = bounds.Top + 9;
                var separatorBottom = bounds.Bottom - 10;

                graphics.DrawLine(
                    separatorPen,
                    x,
                    separatorTop,
                    x,
                    separatorBottom);

                x += SeparatorGap;
            }

            first = false;

            var labelText = string.Concat(segment.Source.Label, ":");
            var labelSize = MeasureText(labelText, this.labelFont);

            if (x + labelSize.Width >= textBounds.Right)
            {
                break;
            }

            TextRenderer.DrawText(
                graphics,
                labelText,
                this.labelFont,
                new Rectangle(
                    x,
                    0,
                    labelSize.Width,
                    textBounds.Height),
                labelColor,
                textFlags);

            x += labelSize.Width + 5;

            var remainingWidth = textBounds.Right - x;

            if (remainingWidth <= 0)
            {
                break;
            }

            var valueColorForSegment =
                segment.Source.Kind ==
                HostedClientTitleSegmentKind.Status
                    ? statusValueColor
                    : segment.Source.IsEmphasized
                        ? emphasizedValueColor
                        : valueColor;

            var measuredValue = MeasureText(
                segment.Value,
                this.valueFont);

            var valueWidth = Math.Min(
                measuredValue.Width,
                remainingWidth);

            var valueRectangle = new Rectangle(
                x,
                0,
                valueWidth,
                textBounds.Height);

            var valueFlags = textFlags;

            if (measuredValue.Width > remainingWidth)
            {
                valueFlags |= TextFormatFlags.EndEllipsis;
            }

            TextRenderer.DrawText(
                graphics,
                segment.Value,
                this.valueFont,
                valueRectangle,
                valueColorForSegment,
                valueFlags);

            x += valueWidth + GroupGap;

            if (measuredValue.Width > remainingWidth)
            {
                break;
            }
        }
    }

    private IReadOnlyList<RenderSegment> SelectSegments(
        int availableWidth)
    {
        var selected = this.presentation!.Segments
            .Select(segment => new RenderSegment(
                segment,
                segment.Value))
            .ToList();

        if (this.MeasureSegments(selected) <= availableWidth)
        {
            return selected;
        }

        for (var index = 0; index < selected.Count; index++)
        {
            var compactValue = selected[index].Source.CompactValue;

            if (!string.IsNullOrWhiteSpace(compactValue))
            {
                selected[index] = selected[index] with
                {
                    Value = compactValue,
                };
            }
        }

        if (this.MeasureSegments(selected) <= availableWidth)
        {
            return selected;
        }

        foreach (var candidate in selected
                     .Where(segment => segment.Source.CanHide)
                     .OrderBy(segment => segment.Source.Priority)
                     .ToArray())
        {
            selected.Remove(candidate);

            if (this.MeasureSegments(selected) <= availableWidth)
            {
                break;
            }
        }

        return selected;
    }

    private int MeasureSegments(
        IReadOnlyList<RenderSegment> segments)
    {
        var width = 0;

        for (var index = 0; index < segments.Count; index++)
        {
            if (index > 0)
            {
                width += SeparatorGap * 2;
            }

            var segment = segments[index];
            var labelText = string.Concat(segment.Source.Label, ":");

            width += MeasureText(labelText, this.labelFont).Width;
            width += 5;
            width += MeasureText(segment.Value, this.valueFont).Width;
            width += GroupGap;
        }

        return width;
    }

    private void DrawChrome(Graphics graphics, Rectangle bounds)
    {
        var regions = this.GetVisibleChromeRegions();

        foreach (var region in regions)
        {
            var regionBounds = this.GetChromeBounds(region);
            this.DrawChromeBackground(
                graphics,
                regionBounds,
                region);

            if (region != ChromeRegion.Close)
            {
                DrawChromeDivider(
                    graphics,
                    regionBounds.Left,
                    bounds);
            }

            var glyph = region switch
            {
                ChromeRegion.Help => "?",
                ChromeRegion.Minimize => "─",
                ChromeRegion.Maximize => this.isMaximized
                    ? "❐"
                    : "□",
                ChromeRegion.Close => "×",
                _ => "",
            };

            TextRenderer.DrawText(
                graphics,
                glyph,
                this.chromeFont,
                regionBounds,
                Color.White,
                textFlags |
                TextFormatFlags.HorizontalCenter);
        }
    }

    private static void DrawChromeDivider(
        Graphics graphics,
        int x,
        Rectangle bounds)
    {
        using var divider = new Pen(chromeDividerColor);

        graphics.DrawLine(
            divider,
            x,
            bounds.Top + 7,
            x,
            bounds.Bottom - 8);
    }

    private void DrawChromeBackground(
        Graphics graphics,
        Rectangle bounds,
        ChromeRegion region)
    {
        Color? color = null;

        if (this.pressedRegion == region)
        {
            color = region == ChromeRegion.Close
                ? closePressedColor
                : chromePressedColor;
        }
        else if (this.hoveredRegion == region)
        {
            color = region == ChromeRegion.Close
                ? closeHoverColor
                : chromeHoverColor;
        }

        if (color.HasValue)
        {
            using var brush = new SolidBrush(color.Value);
            graphics.FillRectangle(brush, bounds);
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

        regions.Add(ChromeRegion.Minimize);

        if (this.showMaximizeButton)
        {
            regions.Add(ChromeRegion.Maximize);
        }

        regions.Add(ChromeRegion.Close);
        return regions;
    }

    private Rectangle GetChromeBounds(
        ChromeRegion region)
    {
        var regions = this.GetVisibleChromeRegions();
        var index = regions.IndexOf(region);

        if (index < 0)
        {
            return Rectangle.Empty;
        }

        var left = this.ClientSize.Width -
                   ((regions.Count - index) * ChromeButtonWidth);

        return new Rectangle(
            Math.Max(0, left),
            0,
            ChromeButtonWidth,
            this.ClientSize.Height);
    }

    private void InvalidateChrome()
    {
        var left = Math.Max(
            0,
            this.ClientSize.Width -
            (ChromeButtonWidth * this.ChromeButtonCount));

        this.Invalidate(new Rectangle(
            left,
            0,
            this.ClientSize.Width - left,
            this.ClientSize.Height));
    }

    private static Size MeasureText(string text, Font font)
    {
        return TextRenderer.MeasureText(
            text,
            font,
            new Size(width: 10_000, height: 1_000),
            measureTextFlags);
    }

    private enum ChromeRegion
    {
        None,
        Help,
        Minimize,
        Maximize,
        Close,
    }

    private sealed record RenderSegment(
        HostedClientTitleSegment Source,
        string Value);
}
