// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using Net7ClientManager.Navigation;

internal sealed partial class GalaxyAtlasCanvas
{
    private const int SearchFocusDurationMilliseconds = 2600;
    private const int SearchFocusPulseMilliseconds = 650;

    private readonly System.Windows.Forms.Timer searchFocusTimer = new()
    {
        Interval = 80,
    };

    private GalaxyAtlasSearchResult? searchFocusResult;
    private long searchFocusStartedAt;

    internal void FocusSearchResult(GalaxyAtlasSearchResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        this.searchFocusResult = result;
        this.searchFocusStartedAt = Environment.TickCount64;
        this.searchFocusTimer.Stop();
        this.searchFocusTimer.Start();
        this.Invalidate();
    }

    private void SearchFocusTimer_OnTick(object? sender, EventArgs e)
    {
        if (this.searchFocusResult == null ||
            Environment.TickCount64 - this.searchFocusStartedAt >=
            SearchFocusDurationMilliseconds)
        {
            this.searchFocusTimer.Stop();
            this.searchFocusResult = null;
        }

        this.Invalidate();
    }

    private void DrawSearchFocus(Graphics graphics)
    {
        var result = this.searchFocusResult;

        if (result == null ||
            this.sector == null ||
            !string.Equals(
                result.SectorKey,
                this.sector.Key,
                StringComparison.Ordinal))
        {
            return;
        }

        var elapsed = Math.Clamp(
            Environment.TickCount64 - this.searchFocusStartedAt,
            0L,
            SearchFocusDurationMilliseconds);
        var fade = elapsed <= SearchFocusDurationMilliseconds - 500
            ? 1.0f
            : Math.Max(
                0.0f,
                (SearchFocusDurationMilliseconds - elapsed) / 500.0f);
        var pulse = (elapsed % SearchFocusPulseMilliseconds) /
                    (float)SearchFocusPulseMilliseconds;

        switch (result.Kind)
        {
            case GalaxyAtlasSearchResultKind.Sector:
                this.DrawSectorSearchFocus(
                    graphics,
                    result,
                    pulse,
                    fade);
                return;

            case GalaxyAtlasSearchResultKind.Station:
            case GalaxyAtlasSearchResultKind.Gate:
            case GalaxyAtlasSearchResultKind.Planet:
            case GalaxyAtlasSearchResultKind.NavigationPoint:
            {
                var node = this.FindSearchFocusNode(result);

                if (node != null)
                {
                    this.DrawPointSearchFocus(
                        graphics,
                        node.Point,
                        GetNodeRadius(node.Target.Kind),
                        result.Name,
                        pulse,
                        fade);
                }

                return;
            }

            case GalaxyAtlasSearchResultKind.Pilot:
            {
                var marker = this.FindSearchFocusPilotMarker(result);

                if (marker != null)
                {
                    this.DrawPointSearchFocus(
                        graphics,
                        marker.Point,
                        9.0f,
                        result.Name,
                        pulse,
                        fade);
                }

                return;
            }
        }
    }

    private GalaxyAtlasNode? FindSearchFocusNode(
        GalaxyAtlasSearchResult result)
    {
        return this.nodes.FirstOrDefault(node =>
            !string.IsNullOrWhiteSpace(
                node.TargetDestination.TargetKey) &&
            string.Equals(
                result.Identity,
                string.Concat(
                    "target:",
                    node.TargetDestination.TargetKey),
                StringComparison.Ordinal));
    }

    private GalaxyAtlasPilotMarkerNode? FindSearchFocusPilotMarker(
        GalaxyAtlasSearchResult result)
    {
        return this.pilotMarkerNodes.FirstOrDefault(marker =>
            marker.Pilots.Any(pilot =>
                string.Equals(
                    result.Identity,
                    string.Concat(
                        "pilot:",
                        GalaxyTopology.NormalizeName(
                            pilot.PilotName)),
                    StringComparison.Ordinal)));
    }

    private void DrawSectorSearchFocus(
        Graphics graphics,
        GalaxyAtlasSearchResult result,
        float pulse,
        float fade)
    {
        var heading = string.Concat(
            result.SystemName,
            " / ",
            result.SectorName);
        using var headingFont = new Font(
            this.Font.FontFamily,
            11.0f,
            FontStyle.Bold);
        var size = TextRenderer.MeasureText(
            heading,
            headingFont,
            Size.Empty,
            TextFormatFlags.NoPadding |
            TextFormatFlags.NoPrefix |
            TextFormatFlags.SingleLine);
        var bounds = new RectangleF(
            12.0f - (pulse * 4.0f),
            9.0f - (pulse * 3.0f),
            Math.Min(
                this.ClientSize.Width - 24.0f,
                size.Width + 18.0f + (pulse * 8.0f)),
            34.0f + (pulse * 6.0f));

        using var pen = new Pen(
            Color.FromArgb(
                (int)(220.0f * (1.0f - pulse) * fade),
                destinationColor),
            2.0f);
        graphics.DrawRectangle(
            pen,
            bounds.X,
            bounds.Y,
            bounds.Width,
            bounds.Height);
    }

    private void DrawPointSearchFocus(
        Graphics graphics,
        PointF center,
        float markerRadius,
        string label,
        float pulse,
        float fade)
    {
        var ringRadius = markerRadius + 8.0f + (pulse * 20.0f);
        var ringAlpha = (int)(230.0f * (1.0f - pulse) * fade);
        var anchorRadius = markerRadius + 6.0f;

        using (var ringPen = new Pen(
                   Color.FromArgb(ringAlpha, destinationColor),
                   2.2f))
        {
            graphics.DrawEllipse(
                ringPen,
                center.X - ringRadius,
                center.Y - ringRadius,
                ringRadius * 2.0f,
                ringRadius * 2.0f);
        }

        using (var anchorPen = new Pen(
                   Color.FromArgb(
                       (int)(235.0f * fade),
                       destinationColor),
                   2.0f))
        {
            graphics.DrawEllipse(
                anchorPen,
                center.X - anchorRadius,
                center.Y - anchorRadius,
                anchorRadius * 2.0f,
                anchorRadius * 2.0f);
        }

        this.DrawSearchFocusLabel(
            graphics,
            center,
            label,
            fade);
    }

    private void DrawSearchFocusLabel(
        Graphics graphics,
        PointF center,
        string label,
        float fade)
    {
        using var labelFont = new Font(
            this.Font,
            FontStyle.Bold);
        var size = TextRenderer.MeasureText(
            label,
            labelFont,
            Size.Empty,
            TextFormatFlags.NoPadding |
            TextFormatFlags.NoPrefix |
            TextFormatFlags.SingleLine);
        var width = size.Width + 18;
        var height = size.Height + 10;
        var x = center.X + 18.0f;

        if (x + width > this.ClientSize.Width - 10.0f)
        {
            x = center.X - width - 18.0f;
        }

        var y = Math.Clamp(
            center.Y - (height * 0.5f),
            44.0f,
            Math.Max(
                44.0f,
                this.ClientSize.Height - height - 10.0f));
        var bounds = new Rectangle(
            (int)Math.Round(x),
            (int)Math.Round(y),
            width,
            height);

        using var backgroundBrush = new SolidBrush(
            Color.FromArgb(
                (int)(225.0f * fade),
                backgroundColor));
        using var borderPen = new Pen(
            Color.FromArgb(
                (int)(240.0f * fade),
                destinationColor),
            1.5f);
        graphics.FillRectangle(backgroundBrush, bounds);
        graphics.DrawRectangle(borderPen, bounds);

        TextRenderer.DrawText(
            graphics,
            label,
            labelFont,
            bounds,
            Color.FromArgb(
                (int)(255.0f * fade),
                destinationColor),
            TextFormatFlags.HorizontalCenter |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPadding |
            TextFormatFlags.NoPrefix |
            TextFormatFlags.SingleLine |
            TextFormatFlags.EndEllipsis);
    }
}
