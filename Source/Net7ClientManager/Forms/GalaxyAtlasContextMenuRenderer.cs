namespace Net7ClientManager.Forms;

internal sealed class GalaxyAtlasContextMenuRenderer : ToolStripRenderer
{
    private static readonly Color backgroundColor =
        Color.FromArgb(15, 24, 33);

    private static readonly Color hoverBackgroundColor =
        Color.FromArgb(35, 43, 82);

    private static readonly Color borderColor =
        Color.FromArgb(83, 96, 151);

    private static readonly Color textColor =
        Color.White;

    private static readonly Color hoverTextColor =
        Color.FromArgb(255, 239, 0);

    private static readonly Color disabledTextColor =
        Color.FromArgb(145, 151, 171);

    protected override void OnRenderToolStripBackground(
        ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(backgroundColor);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(
        ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(borderColor);
        var bounds = new Rectangle(
            0,
            0,
            Math.Max(0, e.ToolStrip.Width - 1),
            Math.Max(0, e.ToolStrip.Height - 1));

        e.Graphics.DrawRectangle(pen, bounds);
    }

    protected override void OnRenderMenuItemBackground(
        ToolStripItemRenderEventArgs e)
    {
        var color = e.Item.Selected
            ? hoverBackgroundColor
            : backgroundColor;

        using var brush = new SolidBrush(color);
        e.Graphics.FillRectangle(
            brush,
            new Rectangle(Point.Empty, e.Item.Size));
    }

    protected override void OnRenderItemText(
        ToolStripItemTextRenderEventArgs e)
    {
        var color = !e.Item.Enabled
            ? disabledTextColor
            : e.Item.Selected
                ? hoverTextColor
                : textColor;
        var textBounds = new Rectangle(
            8,
            0,
            Math.Max(0, e.Item.Width - 16),
            e.Item.Height);

        TextRenderer.DrawText(
            e.Graphics,
            e.Text,
            e.TextFont,
            textBounds,
            color,
            TextFormatFlags.Left |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPrefix |
            TextFormatFlags.NoPadding |
            TextFormatFlags.SingleLine |
            TextFormatFlags.EndEllipsis);
    }
}
