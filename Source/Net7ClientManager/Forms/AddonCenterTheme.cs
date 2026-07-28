namespace Net7ClientManager.Forms;

internal static class AddonCenterTheme
{
    public static readonly Color Background = Color.FromArgb(12, 17, 24);

    public static readonly Color Panel = Color.FromArgb(18, 26, 36);

    public static readonly Color ElevatedPanel = Color.FromArgb(24, 37, 49);

    public static readonly Color Border = Color.FromArgb(44, 135, 174);

    public static readonly Color SoftBorder = Color.FromArgb(48, 72, 87);

    public static readonly Color Text = Color.FromArgb(235, 242, 247);

    public static readonly Color MutedText = Color.FromArgb(158, 177, 191);

    public static readonly Color Accent = Color.FromArgb(65, 203, 236);

    public static readonly Color Success = Color.FromArgb(76, 210, 139);

    public static readonly Color Danger = Color.FromArgb(236, 94, 94);

    public static readonly Color Warning = Color.FromArgb(242, 188, 73);

    public static readonly Color Button = Color.FromArgb(20, 34, 46);

    public static readonly Color ButtonHover = Color.FromArgb(28, 79, 105);

    public static readonly Color DisabledButton = Color.FromArgb(16, 27, 36);

    public static readonly Color DisabledText = Color.FromArgb(132, 151, 164);

    public static void StyleButton(
        Button button,
        Color? accent = null)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.UseVisualStyleBackColor = false;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = accent ?? SoftBorder;
        button.FlatAppearance.MouseOverBackColor = ButtonHover;
        button.FlatAppearance.MouseDownBackColor = ElevatedPanel;
        button.BackColor = Button;
        button.ForeColor = accent ?? Text;
        button.Cursor = Cursors.Hand;
        button.Font = new Font("Segoe UI", 9.0f, FontStyle.Bold);
        button.EnabledChanged += Button_OnEnabledChanged;
        button.Paint += Button_OnPaint;
    }

    private static void Button_OnEnabledChanged(
        object? sender,
        EventArgs e)
    {
        if (sender is Button button)
        {
            button.Invalidate();
        }
    }

    private static void Button_OnPaint(
        object? sender,
        PaintEventArgs e)
    {
        if (sender is not Button { Enabled: false } button)
        {
            return;
        }

        var bounds = button.ClientRectangle;

        using var backgroundBrush = new SolidBrush(DisabledButton);
        using var borderPen = new Pen(SoftBorder);

        e.Graphics.FillRectangle(backgroundBrush, bounds);
        e.Graphics.DrawRectangle(
            borderPen,
            bounds.Left,
            bounds.Top,
            Math.Max(0, bounds.Width - 1),
            Math.Max(0, bounds.Height - 1));

        var textBounds = new Rectangle(
            bounds.Left + button.Padding.Left + 4,
            bounds.Top + button.Padding.Top + 2,
            Math.Max(0, bounds.Width - button.Padding.Horizontal - 8),
            Math.Max(0, bounds.Height - button.Padding.Vertical - 4));

        TextRenderer.DrawText(
            e.Graphics,
            button.Text,
            button.Font,
            textBounds,
            DisabledText,
            GetTextFormatFlags(button.TextAlign));
    }

    private static TextFormatFlags GetTextFormatFlags(
        ContentAlignment alignment)
    {
        var flags = TextFormatFlags.EndEllipsis |
                    TextFormatFlags.NoPadding |
                    TextFormatFlags.SingleLine;

        flags |= alignment switch
        {
            ContentAlignment.TopLeft => TextFormatFlags.Left |
                                        TextFormatFlags.Top,
            ContentAlignment.TopCenter => TextFormatFlags.HorizontalCenter |
                                          TextFormatFlags.Top,
            ContentAlignment.TopRight => TextFormatFlags.Right |
                                         TextFormatFlags.Top,
            ContentAlignment.MiddleLeft => TextFormatFlags.Left |
                                           TextFormatFlags.VerticalCenter,
            ContentAlignment.MiddleCenter => TextFormatFlags.HorizontalCenter |
                                             TextFormatFlags.VerticalCenter,
            ContentAlignment.MiddleRight => TextFormatFlags.Right |
                                            TextFormatFlags.VerticalCenter,
            ContentAlignment.BottomLeft => TextFormatFlags.Left |
                                           TextFormatFlags.Bottom,
            ContentAlignment.BottomCenter => TextFormatFlags.HorizontalCenter |
                                             TextFormatFlags.Bottom,
            ContentAlignment.BottomRight => TextFormatFlags.Right |
                                            TextFormatFlags.Bottom,
            _ => TextFormatFlags.HorizontalCenter |
                 TextFormatFlags.VerticalCenter,
        };

        return flags;
    }
}
