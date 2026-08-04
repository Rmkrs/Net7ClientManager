namespace Net7ClientManager.Forms;

using System.Drawing.Text;

internal sealed class BuffToolTipForm : Form
{
    private const int WmNcHitTest = 0x0084;
    private const int HtTransparent = -1;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;

    private const int HorizontalPadding = 12;
    private const int VerticalPadding = 10;
    private const int LineGap = 4;
    private const int MinimumWidth = 320;
    private const int MinimumHeight = 62;
    private const int MaximumTextWidth = 360;
    private const int GameBoundsMargin = 2;
    private const int NativeToolTipVerticalOverlap = 6;
    private const int NativeToolTipCursorOffsetX = 8;
    private const int NativeToolTipRightMargin = 6;
    private const int BaseCanvasWidth = 1280;
    private const int BaseCanvasHeight = 720;

    private readonly Font titleFont =
        new("Segoe UI Semibold", 10.0f, FontStyle.Bold);
    private readonly Font descriptionFont =
        new("Segoe UI", 9.5f, FontStyle.Regular);

    private string contentKey = "";
    private string title = "";
    private string description = "";
    private Rectangle titleBounds;
    private Rectangle descriptionBounds;

    public BuffToolTipForm()
    {
        this.AutoScaleMode = AutoScaleMode.Dpi;
        this.BackColor = MainWindowTheme.ElevatedPanel;
        this.DoubleBuffered = true;
        this.FormBorderStyle = FormBorderStyle.None;
        this.ShowIcon = false;
        this.ShowInTaskbar = false;
        this.StartPosition = FormStartPosition.Manual;

        this.SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.UserPaint,
            true);
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |=
                WsExTransparent |
                WsExToolWindow |
                WsExNoActivate;
            return parameters;
        }
    }

    public void ShowContent(
        Form? owner,
        string key,
        string title,
        string description,
        Point cursorPosition,
        Rectangle gameScreenBounds)
    {
        if (!string.Equals(
                this.contentKey,
                key,
                StringComparison.Ordinal) ||
            !string.Equals(
                this.title,
                title,
                StringComparison.Ordinal) ||
            !string.Equals(
                this.description,
                description,
                StringComparison.Ordinal))
        {
            this.contentKey = key;
            this.title = title.Trim();
            this.description = description.Trim();
            this.RecalculateLayout();
        }

        this.Location = ResolveLocation(
            cursorPosition,
            this.Size,
            gameScreenBounds);

        if (!this.Visible)
        {
            if (owner != null)
            {
                this.Show(owner);
            }
            else
            {
                this.Show();
            }
        }

        this.Invalidate();
    }

    public void HideContent()
    {
        if (this.Visible)
        {
            this.Hide();
        }
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WmNcHitTest)
        {
            message.Result = new IntPtr(HtTransparent);
            return;
        }

        base.WndProc(ref message);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        e.Graphics.TextRenderingHint =
            TextRenderingHint.ClearTypeGridFit;

        using var background =
            new SolidBrush(MainWindowTheme.ElevatedPanel);
        using var border =
            new Pen(MainWindowTheme.AccentBorder);
        e.Graphics.FillRectangle(
            background,
            this.ClientRectangle);
        e.Graphics.DrawRectangle(
            border,
            0,
            0,
            Math.Max(0, this.ClientSize.Width - 1),
            Math.Max(0, this.ClientSize.Height - 1));

        TextRenderer.DrawText(
            e.Graphics,
            this.title,
            this.titleFont,
            this.titleBounds,
            MainWindowTheme.Accent,
            TextFormatFlags.NoPadding |
            TextFormatFlags.NoPrefix |
            TextFormatFlags.SingleLine |
            TextFormatFlags.EndEllipsis);

        if (this.description.Length != 0)
        {
            TextRenderer.DrawText(
                e.Graphics,
                this.description,
                this.descriptionFont,
                this.descriptionBounds,
                MainWindowTheme.Text,
                TextFormatFlags.NoPadding |
                TextFormatFlags.NoPrefix |
                TextFormatFlags.WordBreak);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.titleFont.Dispose();
            this.descriptionFont.Dispose();
        }

        base.Dispose(disposing);
    }

    private void RecalculateLayout()
    {
        var proposed = new Size(
            MaximumTextWidth,
            1000);
        var titleSize = TextRenderer.MeasureText(
            this.title,
            this.titleFont,
            proposed,
            TextFormatFlags.NoPadding |
            TextFormatFlags.NoPrefix |
            TextFormatFlags.SingleLine);
        var descriptionSize = this.description.Length == 0
            ? Size.Empty
            : TextRenderer.MeasureText(
                this.description,
                this.descriptionFont,
                proposed,
                TextFormatFlags.NoPadding |
                TextFormatFlags.NoPrefix |
                TextFormatFlags.WordBreak);
        var contentWidth = Math.Min(
            MaximumTextWidth,
            Math.Max(
                titleSize.Width,
                descriptionSize.Width));
        var width = Math.Max(
            MinimumWidth,
            contentWidth + (HorizontalPadding * 2));
        var titleHeight = Math.Max(
            this.titleFont.Height,
            titleSize.Height);
        var descriptionHeight = descriptionSize.IsEmpty
            ? 0
            : Math.Max(
                this.descriptionFont.Height,
                descriptionSize.Height);
        var contentHeight =
            titleHeight +
            (descriptionHeight > 0 ? LineGap + descriptionHeight : 0);
        var height = Math.Max(
            MinimumHeight,
            VerticalPadding + contentHeight + VerticalPadding);
        var contentTop = Math.Max(
            VerticalPadding,
            (height - contentHeight) / 2);

        this.ClientSize = new Size(width, height);
        this.titleBounds = new Rectangle(
            HorizontalPadding,
            contentTop,
            width - (HorizontalPadding * 2),
            titleHeight);
        this.descriptionBounds = new Rectangle(
            HorizontalPadding,
            this.titleBounds.Bottom + LineGap,
            width - (HorizontalPadding * 2),
            descriptionHeight);
    }

    private static Point ResolveLocation(
        Point cursorPosition,
        Size size,
        Rectangle gameScreenBounds)
    {
        // Native buff tooltips follow the cursor horizontally. Buff slot 1
        // only appeared fixed because the tooltip is clamped against the
        // right edge of the game window. Match that rule for every slot so
        // our wider replacement continues to cover the native tooltip.
        var scale = Math.Max(
            0.5f,
            Math.Min(
                gameScreenBounds.Width / (float)BaseCanvasWidth,
                gameScreenBounds.Height / (float)BaseCanvasHeight));
        var cursorOffsetX = Math.Max(
            4,
            (int)Math.Round(
                NativeToolTipCursorOffsetX * scale));
        var rightMargin = Math.Max(
            GameBoundsMargin,
            (int)Math.Round(
                NativeToolTipRightMargin * scale));
        var requested = new Point(
            cursorPosition.X - cursorOffsetX,
            cursorPosition.Y - size.Height +
            NativeToolTipVerticalOverlap);

        // Native buff tooltips never flip below the cursor. Near the top of
        // the game they simply clamp against the upper edge while remaining
        // above the buff icon. Do the same so our tooltip keeps covering the
        // native one instead of suddenly revealing it.
        return new Point(
            Math.Clamp(
                requested.X,
                gameScreenBounds.Left + GameBoundsMargin,
                Math.Max(
                    gameScreenBounds.Left + GameBoundsMargin,
                    gameScreenBounds.Right - size.Width -
                    rightMargin)),
            Math.Clamp(
                requested.Y,
                gameScreenBounds.Top + GameBoundsMargin,
                Math.Max(
                    gameScreenBounds.Top + GameBoundsMargin,
                    gameScreenBounds.Bottom - size.Height -
                    GameBoundsMargin)));
    }
}
