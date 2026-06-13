namespace Net7ClientManager.Forms;

using System.Drawing.Drawing2D;

internal static class GraphicsExtensions
{
    public static void FillRoundedRectangle(this Graphics graphics, Brush brush, RectangleF bounds, float radius)
    {
        using var path = CreateRoundedRectanglePath(bounds, radius);
        graphics.FillPath(brush, path);
    }

    public static void DrawRoundedRectangle(this Graphics graphics, Pen pen, RectangleF bounds, float radius)
    {
        using var path = CreateRoundedRectanglePath(bounds, radius);
        graphics.DrawPath(pen, path);
    }

    private static GraphicsPath CreateRoundedRectanglePath(RectangleF bounds, float radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();

        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, startAngle: 180, sweepAngle: 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, startAngle: 270, sweepAngle: 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, startAngle: 0, sweepAngle: 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, startAngle: 90, sweepAngle: 90);
        path.CloseFigure();

        return path;
    }
}
