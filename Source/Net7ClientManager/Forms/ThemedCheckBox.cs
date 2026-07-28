namespace Net7ClientManager.Forms;

internal sealed class ThemedCheckBox : CheckBox
{
    private const int BoxSize = 17;
    private const int TextGap = 9;
    private bool isHovered;

    public ThemedCheckBox()
    {
        this.SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor |
            ControlStyles.UserPaint,
            value: true);

        this.AutoSize = true;
        this.BackColor = Color.Transparent;
        this.ForeColor = MainWindowTheme.Text;
        this.Cursor = Cursors.Hand;
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var textSize = TextRenderer.MeasureText(
            this.Text,
            this.Font,
            proposedSize,
            TextFormatFlags.NoPadding |
            TextFormatFlags.SingleLine);

        return new Size(
            BoxSize + TextGap + textSize.Width + 2,
            Math.Max(BoxSize, textSize.Height) + 6);
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        pevent.Graphics.Clear(this.Parent?.BackColor ?? MainWindowTheme.Panel);

        var boxBounds = new Rectangle(
            x: 1,
            y: Math.Max(1, (this.ClientSize.Height - BoxSize) / 2),
            BoxSize,
            BoxSize);

        var borderColor = !this.Enabled
            ? MainWindowTheme.Border
            : this.Focused || this.isHovered
                ? MainWindowTheme.Accent
                : MainWindowTheme.AccentBorder;

        using (var backgroundBrush = new SolidBrush(
                   this.Checked
                       ? MainWindowTheme.Accent
                       : MainWindowTheme.ElevatedPanel))
        using (var borderPen = new Pen(borderColor))
        {
            pevent.Graphics.FillRectangle(backgroundBrush, boxBounds);
            pevent.Graphics.DrawRectangle(borderPen, boxBounds);
        }

        if (this.Checked)
        {
            using var checkPen = new Pen(
                MainWindowTheme.Background,
                width: 2.0f)
            {
                StartCap = System.Drawing.Drawing2D.LineCap.Round,
                EndCap = System.Drawing.Drawing2D.LineCap.Round,
            };

            pevent.Graphics.DrawLines(
                checkPen,
                [
                    new Point(boxBounds.Left + 4, boxBounds.Top + 9),
                    new Point(boxBounds.Left + 7, boxBounds.Top + 12),
                    new Point(boxBounds.Left + 13, boxBounds.Top + 5),
                ]);
        }

        var textBounds = new Rectangle(
            boxBounds.Right + TextGap,
            y: 0,
            width: Math.Max(0, this.ClientSize.Width - boxBounds.Right - TextGap),
            this.ClientSize.Height);

        TextRenderer.DrawText(
            pevent.Graphics,
            this.Text,
            this.Font,
            textBounds,
            this.Enabled
                ? this.ForeColor
                : MainWindowTheme.MutedText,
            TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPadding |
            TextFormatFlags.SingleLine |
            TextFormatFlags.VerticalCenter);

        if (this.Focused && this.ShowFocusCues)
        {
            ControlPaint.DrawFocusRectangle(
                pevent.Graphics,
                Rectangle.Inflate(textBounds, -1, -3),
                this.ForeColor,
                this.Parent?.BackColor ?? MainWindowTheme.Panel);
        }
    }

    protected override void OnMouseEnter(EventArgs eventArgs)
    {
        base.OnMouseEnter(eventArgs);
        this.isHovered = true;
        this.Invalidate();
    }

    protected override void OnMouseLeave(EventArgs eventArgs)
    {
        base.OnMouseLeave(eventArgs);
        this.isHovered = false;
        this.Invalidate();
    }

    protected override void OnCheckedChanged(EventArgs eventArgs)
    {
        base.OnCheckedChanged(eventArgs);
        this.Invalidate();
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        this.Invalidate();
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        this.Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        this.Invalidate();
    }
}
