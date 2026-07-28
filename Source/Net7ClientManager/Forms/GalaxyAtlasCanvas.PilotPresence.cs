// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using System.Globalization;
using Net7ClientManager.Navigation;

internal sealed partial class GalaxyAtlasCanvas
{
    private const float PilotClusterDistance = 10.0f;
    private const int MaximumPilotToolTipLines = 12;

    private readonly System.Windows.Forms.Timer toolTipShowTimer = new()
    {
        Interval = 350,
    };

    private GalaxyAtlasPilotMarkerNode? hoveredPilotMarker;
    private string? hoveredPilotMarkerKey;
    private Point hoveredToolTipAnchor;

    private IReadOnlyList<GalaxyAtlasDisplayedPilot>
        BuildDisplayedPilotLocations()
    {
        if (this.sector == null ||
            (!this.ShowCurrentLocation && !this.ShowGroupMembers &&
             !this.ShowSocialPilots))
        {
            return [];
        }

        var displayed = new List<GalaxyAtlasDisplayedPilot>();

        foreach (var group in this.livePilotLocations
                     .Where(location => string.Equals(
                         location.SectorKey,
                         this.sector.Key,
                         StringComparison.Ordinal))
                     .GroupBy(
                         location => location.PilotName,
                         StringComparer.OrdinalIgnoreCase))
        {
            var candidates = group
                .OrderByDescending(location => location.ObservedAt)
                .ThenByDescending(location => location.ProcessId)
                .ToArray();
            var selected = candidates.FirstOrDefault(location =>
                location.IsSelectedPilot);
            var groupMember = candidates.FirstOrDefault(location =>
                location.IsGroupMember);
            var social = candidates.FirstOrDefault(location =>
                location.IsSocialPilot);

            // The selected pilot belongs exclusively to the Me layer. A
            // matching social publication may enrich its tooltip, but must
            // not make the pilot reappear after the Me layer is disabled.
            if (selected != null && !this.ShowCurrentLocation)
            {
                continue;
            }

            GalaxyAtlasPilotLocation? effective;
            GalaxyAtlasPilotDisplayKind displayKind;

            if (this.ShowCurrentLocation && selected != null)
            {
                effective = selected;
                displayKind = GalaxyAtlasPilotDisplayKind.Selected;
            }
            else if (this.ShowGroupMembers && groupMember != null)
            {
                effective = groupMember;
                displayKind = GalaxyAtlasPilotDisplayKind.Group;
            }
            else if (this.ShowSocialPilots && social != null)
            {
                effective = social;
                displayKind = GalaxyAtlasPilotDisplayKind.Social;
            }
            else
            {
                continue;
            }

            displayed.Add(new GalaxyAtlasDisplayedPilot
            {
                PilotName = effective.PilotName,
                X = effective.X,
                Y = effective.Y,
                Z = effective.Z,
                DisplayKind = displayKind,
                IsSelectedPilot = selected != null &&
                    this.ShowCurrentLocation,
                IsGroupMember = groupMember != null &&
                    this.ShowGroupMembers,
                IsSocialPilot = social != null &&
                    this.ShowSocialPilots,
                IsNearNavApproximation = effective.IsNearNavApproximation,
                IsDocked = effective.IsDocked,
                AnchorName = effective.AnchorName,
            });
        }

        return displayed
            .OrderByDescending(pilot => pilot.DisplayKind)
            .ThenBy(pilot => pilot.PilotName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private void BuildPilotMarkerNodes(
        IReadOnlyCollection<GalaxyAtlasDisplayedPilot> pilots,
        Func<float, float, PointF> project)
    {
        var clusters = new List<PilotClusterBuilder>();

        foreach (var pilot in pilots)
        {
            var point = project(pilot.X, pilot.Y);
            var anchorKey = GetPilotAnchorKey(pilot);
            PilotClusterBuilder? cluster = null;

            if (anchorKey != null)
            {
                cluster = clusters.FirstOrDefault(candidate =>
                    string.Equals(
                        candidate.AnchorKey,
                        anchorKey,
                        StringComparison.Ordinal));
            }
            else
            {
                cluster = clusters.FirstOrDefault(candidate =>
                    candidate.AnchorKey == null &&
                    GetSquaredDistance(candidate.Point, point) <=
                    PilotClusterDistance * PilotClusterDistance);
            }

            if (cluster == null)
            {
                cluster = new PilotClusterBuilder
                {
                    AnchorKey = anchorKey,
                    Point = point,
                };
                clusters.Add(cluster);
            }

            cluster.Add(pilot, point);
        }

        foreach (var cluster in clusters)
        {
            var pilotsInCluster = cluster.Pilots
                .OrderByDescending(pilot => pilot.DisplayKind)
                .ThenBy(pilot => pilot.PilotName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var point = cluster.AnchorKey == null
                ? cluster.Point
                : this.OffsetNamedPilotMarker(cluster.Point);
            var radius = pilotsInCluster.Length > 1
                ? 9.0f
                : pilotsInCluster[0].DisplayKind ==
                    GalaxyAtlasPilotDisplayKind.Selected
                    ? 8.0f
                    : 6.5f;
            var hitRadius = radius + 9.0f;
            var key = string.Concat(
                cluster.AnchorKey ?? "space",
                ":",
                point.X.ToString("R", CultureInfo.InvariantCulture),
                ":",
                point.Y.ToString("R", CultureInfo.InvariantCulture),
                ":",
                string.Join(
                    ",",
                    pilotsInCluster.Select(pilot =>
                        string.Concat(
                            pilot.PilotName,
                            ":",
                            pilot.DisplayKind,
                            ":",
                            pilot.IsSelectedPilot,
                            ":",
                            pilot.IsGroupMember,
                            ":",
                            pilot.IsSocialPilot))));

            this.pilotMarkerNodes.Add(new GalaxyAtlasPilotMarkerNode
            {
                Key = key,
                Pilots = pilotsInCluster,
                Point = point,
                HitBounds = RectangleF.FromLTRB(
                    point.X - hitRadius,
                    point.Y - hitRadius,
                    point.X + hitRadius,
                    point.Y + hitRadius),
            });
        }
    }

    private PointF OffsetNamedPilotMarker(PointF anchor)
    {
        var x = anchor.X + 15.0f;
        var y = anchor.Y - 13.0f;

        if (x > this.ClientSize.Width - 28.0f)
        {
            x = anchor.X - 15.0f;
        }

        if (y < 46.0f)
        {
            y = anchor.Y + 13.0f;
        }

        return new PointF(x, y);
    }

    private void DrawPilotMarkerNode(
        Graphics graphics,
        GalaxyAtlasPilotMarkerNode node)
    {
        if (node.Pilots.Count > 1)
        {
            this.DrawPilotClusterMarker(graphics, node);
            return;
        }

        this.DrawSinglePilotMarker(
            graphics,
            node,
            node.Pilots[0]);
    }

    private void DrawSinglePilotMarker(
        Graphics graphics,
        GalaxyAtlasPilotMarkerNode node,
        GalaxyAtlasDisplayedPilot pilot)
    {
        var center = node.Point;
        var color = GetPilotColor(pilot.DisplayKind);
        var radius = pilot.DisplayKind == GalaxyAtlasPilotDisplayKind.Selected
            ? 8.0f
            : 6.5f;

        using var glowPen = new Pen(
            Color.FromArgb(
                pilot.DisplayKind == GalaxyAtlasPilotDisplayKind.Selected
                    ? 96
                    : 60,
                color),
            pilot.DisplayKind == GalaxyAtlasPilotDisplayKind.Selected
                ? 4.0f
                : 3.0f);
        using var markerPen = new Pen(
            color,
            pilot.DisplayKind == GalaxyAtlasPilotDisplayKind.Selected
                ? 2.2f
                : 1.8f);
        using var markerBrush = new SolidBrush(
            Color.FromArgb(
                pilot.DisplayKind == GalaxyAtlasPilotDisplayKind.Selected
                    ? 210
                    : 170,
                color));
        using var labelFont = new Font(
            this.Font,
            pilot.DisplayKind == GalaxyAtlasPilotDisplayKind.Selected
                ? FontStyle.Bold
                : FontStyle.Regular);

        graphics.DrawEllipse(
            glowPen,
            center.X - radius - 5.0f,
            center.Y - radius - 5.0f,
            (radius + 5.0f) * 2.0f,
            (radius + 5.0f) * 2.0f);

        switch (pilot.DisplayKind)
        {
            case GalaxyAtlasPilotDisplayKind.Selected:
            {
                PointF[] shipPoints =
                [
                    new(center.X, center.Y - radius - 3.0f),
                    new(center.X + radius * 0.9f, center.Y + radius),
                    new(center.X, center.Y + radius * 0.45f),
                    new(center.X - radius * 0.9f, center.Y + radius),
                ];

                graphics.FillPolygon(markerBrush, shipPoints);
                graphics.DrawPolygon(markerPen, shipPoints);
                graphics.DrawLine(
                    markerPen,
                    center.X,
                    center.Y - radius + 1.0f,
                    center.X,
                    center.Y + radius * 0.35f);
                break;
            }

            case GalaxyAtlasPilotDisplayKind.Social:
                graphics.FillEllipse(
                    markerBrush,
                    center.X - radius,
                    center.Y - radius,
                    radius * 2.0f,
                    radius * 2.0f);
                graphics.DrawEllipse(
                    markerPen,
                    center.X - radius,
                    center.Y - radius,
                    radius * 2.0f,
                    radius * 2.0f);

                if (pilot.IsNearNavApproximation)
                {
                    graphics.DrawLine(
                        markerPen,
                        center.X - radius * 0.55f,
                        center.Y,
                        center.X + radius * 0.55f,
                        center.Y);
                }

                break;

            default:
            {
                PointF[] points =
                [
                    new(center.X, center.Y - radius),
                    new(center.X + radius, center.Y),
                    new(center.X, center.Y + radius),
                    new(center.X - radius, center.Y),
                ];

                graphics.FillPolygon(markerBrush, points);
                graphics.DrawPolygon(markerPen, points);
                break;
            }
        }

        this.DrawPilotMarkerLabel(
            graphics,
            node.Point,
            GetSinglePilotLabel(pilot),
            labelFont,
            color);
    }

    private void DrawPilotClusterMarker(
        Graphics graphics,
        GalaxyAtlasPilotMarkerNode node)
    {
        var dominant = node.Pilots[0];
        var color = GetPilotColor(dominant.DisplayKind);
        const float radius = 9.0f;
        var center = node.Point;

        using var glowPen = new Pen(Color.FromArgb(80, color), 4.0f);
        using var rearPen = new Pen(Color.FromArgb(150, color), 1.5f);
        using var markerPen = new Pen(color, 2.0f);
        using var rearBrush = new SolidBrush(Color.FromArgb(85, color));
        using var markerBrush = new SolidBrush(Color.FromArgb(225, color));
        using var countFont = new Font(
            this.Font.FontFamily,
            Math.Max(6.5f, this.Font.Size - 1.0f),
            FontStyle.Bold);
        using var labelFont = new Font(this.Font, FontStyle.Bold);

        graphics.DrawEllipse(
            glowPen,
            center.X - radius - 5.0f,
            center.Y - radius - 5.0f,
            (radius + 5.0f) * 2.0f,
            (radius + 5.0f) * 2.0f);
        graphics.FillEllipse(
            rearBrush,
            center.X - radius - 4.0f,
            center.Y - radius + 2.0f,
            radius * 2.0f,
            radius * 2.0f);
        graphics.DrawEllipse(
            rearPen,
            center.X - radius - 4.0f,
            center.Y - radius + 2.0f,
            radius * 2.0f,
            radius * 2.0f);
        graphics.FillEllipse(
            markerBrush,
            center.X - radius,
            center.Y - radius,
            radius * 2.0f,
            radius * 2.0f);
        graphics.DrawEllipse(
            markerPen,
            center.X - radius,
            center.Y - radius,
            radius * 2.0f,
            radius * 2.0f);

        TextRenderer.DrawText(
            graphics,
            node.Pilots.Count.ToString(CultureInfo.InvariantCulture),
            countFont,
            Rectangle.Round(RectangleF.FromLTRB(
                center.X - radius,
                center.Y - radius,
                center.X + radius,
                center.Y + radius)),
            backgroundColor,
            TextFormatFlags.HorizontalCenter |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPadding |
            TextFormatFlags.NoPrefix |
            TextFormatFlags.SingleLine);

        this.DrawPilotMarkerLabel(
            graphics,
            center,
            string.Create(
                CultureInfo.InvariantCulture,
                $"{node.Pilots.Count} pilots"),
            labelFont,
            color);
    }

    private void DrawPilotMarkerLabel(
        Graphics graphics,
        PointF point,
        string text,
        Font font,
        Color color)
    {
        var size = TextRenderer.MeasureText(
            text,
            font,
            Size.Empty,
            TextFormatFlags.NoPadding |
            TextFormatFlags.NoPrefix |
            TextFormatFlags.SingleLine);
        var labelX = point.X + 13.0f;

        if (labelX + size.Width > this.ClientSize.Width - 12)
        {
            labelX = point.X - size.Width - 13.0f;
        }

        var labelY = Math.Clamp(
            point.Y - (size.Height * 0.5f),
            44.0f,
            Math.Max(
                44.0f,
                this.ClientSize.Height - size.Height - 10.0f));
        var bounds = new Rectangle(
            (int)Math.Round(labelX),
            (int)Math.Round(labelY),
            size.Width + 2,
            size.Height);

        using var backgroundBrush = new SolidBrush(
            Color.FromArgb(180, backgroundColor));
        graphics.FillRectangle(
            backgroundBrush,
            Rectangle.Inflate(bounds, 3, 1));

        TextRenderer.DrawText(
            graphics,
            text,
            font,
            bounds,
            color,
            TextFormatFlags.Left |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPadding |
            TextFormatFlags.NoPrefix |
            TextFormatFlags.SingleLine);
    }

    private GalaxyAtlasPilotMarkerNode? FindPilotMarkerAt(Point location)
    {
        return this.pilotMarkerNodes
            .Where(node => node.HitBounds.Contains(location.X, location.Y))
            .OrderBy(node => GetSquaredDistance(node.Point, location))
            .ThenByDescending(node => node.Pilots.Count)
            .FirstOrDefault();
    }

    private GalaxyAtlasToolTipContent BuildToolTip(
        GalaxyAtlasPilotMarkerNode node)
    {
        var primary = node.Pilots[0];
        var anchorName = node.Pilots
            .Select(pilot => pilot.AnchorName)
            .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));
        var title = string.IsNullOrWhiteSpace(anchorName)
            ? node.Pilots.Count == 1
                ? primary.PilotName
                : "Pilots"
            : anchorName;
        var subtitle = primary.IsDocked
            ? node.Pilots.Count == 1
                ? "Docked pilot"
                : "Docked pilots"
            : primary.IsNearNavApproximation
                ? node.Pilots.Count == 1
                    ? "Pilot near nav"
                    : "Pilots near nav"
                : node.Pilots.Count == 1
                    ? "Pilot location"
                    : "Pilots at this location";
        var lines = GetLimitedPilotLines(node.Pilots);

        return new GalaxyAtlasToolTipContent(
            Token: string.Concat("pilots:", node.Key),
            Title: title,
            Subtitle: subtitle,
            Coordinates: string.Create(
                CultureInfo.InvariantCulture,
                $"X {primary.X:0.##}   Y {primary.Y:0.##}   Z {primary.Z:0.##}"),
            DetailLines: lines,
            StationInformation: null,
            ForgeOverlayInformation: null,
            DockedPilotLines: [],
            DockedPilotCount: 0,
            ActionText: "Presence shown by enabled Atlas layers");
    }

    private GalaxyAtlasPilotList GetDockedPilots(
        GalaxyNavigationCatalogTarget station)
    {
        var normalizedName = GalaxyTopology.NormalizeName(station.Name);
        var normalizedMapName = GalaxyTopology.NormalizeName(
            station.MapDisplayName);
        var pilots = this.BuildDisplayedPilotLocations()
            .Where(pilot =>
                pilot.IsDocked &&
                !string.IsNullOrWhiteSpace(pilot.AnchorName) &&
                (string.Equals(
                     GalaxyTopology.NormalizeName(pilot.AnchorName!),
                     normalizedName,
                     StringComparison.Ordinal) ||
                 string.Equals(
                     GalaxyTopology.NormalizeName(pilot.AnchorName!),
                     normalizedMapName,
                     StringComparison.Ordinal)))
            .OrderByDescending(pilot => pilot.DisplayKind)
            .ThenBy(pilot => pilot.PilotName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new GalaxyAtlasPilotList(
            pilots.Length,
            GetLimitedPilotLines(pilots));
    }

    private static IReadOnlyList<string> GetLimitedPilotLines(
        IReadOnlyList<GalaxyAtlasDisplayedPilot> pilots)
    {
        var lines = pilots
            .Select(FormatPilotLine)
            .ToArray();

        if (lines.Length <= MaximumPilotToolTipLines)
        {
            return lines;
        }

        return
        [
            .. lines.Take(MaximumPilotToolTipLines - 1),
            string.Create(
                CultureInfo.InvariantCulture,
                $"… {lines.Length - (MaximumPilotToolTipLines - 1)} more"),
        ];
    }

    private void DrawDockedPilotLines(
        Graphics graphics,
        IReadOnlyList<string> lines,
        int totalCount,
        int left,
        int top,
        int width,
        int lineHeight,
        Font sectionFont)
    {
        TextRenderer.DrawText(
            graphics,
            string.Create(
                CultureInfo.InvariantCulture,
                $"DOCKED PILOTS ({totalCount})"),
            sectionFont,
            new Rectangle(left, top, width, lineHeight),
            socialPilotLocationColor,
            TextFormatFlags.Left |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPadding |
            TextFormatFlags.NoPrefix |
            TextFormatFlags.SingleLine);

        var rowTop = top + lineHeight + 5;

        for (var index = 0; index < lines.Count; index++)
        {
            this.DrawToolTipBulletLine(
                graphics,
                lines[index],
                new Rectangle(
                    left,
                    rowTop + (index * lineHeight),
                    width,
                    lineHeight),
                socialPilotLocationColor,
                textColor);
        }
    }

    private void ScheduleAtlasToolTip(
        GalaxyAtlasToolTipContent? content,
        Point anchor)
    {
        var isReshow = this.hoveredToolTipContent != null;
        this.HideAtlasToolTip();
        this.hoveredToolTipContent = content;
        this.hoveredToolTipAnchor = anchor;

        if (content != null)
        {
            this.toolTipShowTimer.Interval = isReshow
                ? this.toolTip.ReshowDelay
                : this.toolTip.InitialDelay;
            this.toolTipShowTimer.Start();
        }
    }

    private void HideAtlasToolTip()
    {
        this.toolTipShowTimer.Stop();

        if (this.IsHandleCreated && !this.IsDisposed)
        {
            this.toolTip.Hide(this);
        }
    }

    private void ToolTipShowTimer_OnTick(object? sender, EventArgs e)
    {
        this.toolTipShowTimer.Stop();
        var content = this.hoveredToolTipContent;

        if (content == null || this.IsDisposed || !this.IsHandleCreated)
        {
            return;
        }

        var metrics = this.MeasureToolTip(content);
        var location = this.GetContainedToolTipLocation(
            this.hoveredToolTipAnchor,
            new Size(metrics.Width, metrics.Height));

        this.toolTip.Show(
            content.Token,
            this,
            location,
            this.toolTip.AutoPopDelay);
    }

    private Point GetContainedToolTipLocation(
        Point anchor,
        Size toolTipSize)
    {
        const int margin = 8;
        const int gap = 14;
        var x = anchor.X + gap;
        var y = anchor.Y + gap;

        if (x + toolTipSize.Width > this.ClientSize.Width - margin)
        {
            x = anchor.X - toolTipSize.Width - gap;
        }

        if (y + toolTipSize.Height > this.ClientSize.Height - margin)
        {
            y = anchor.Y - toolTipSize.Height - gap;
        }

        var maximumX = Math.Max(
            margin,
            this.ClientSize.Width - toolTipSize.Width - margin);
        var maximumY = Math.Max(
            margin,
            this.ClientSize.Height - toolTipSize.Height - margin);

        return new Point(
            Math.Clamp(x, margin, maximumX),
            Math.Clamp(y, margin, maximumY));
    }

    private static string? GetPilotAnchorKey(
        GalaxyAtlasDisplayedPilot pilot)
    {
        if (string.IsNullOrWhiteSpace(pilot.AnchorName) ||
            (!pilot.IsDocked && !pilot.IsNearNavApproximation))
        {
            return null;
        }

        return string.Concat(
            pilot.IsDocked ? "station:" : "nav:",
            GalaxyTopology.NormalizeName(pilot.AnchorName!));
    }

    private static string GetSinglePilotLabel(
        GalaxyAtlasDisplayedPilot pilot)
    {
        return pilot.DisplayKind switch
        {
            GalaxyAtlasPilotDisplayKind.Selected =>
                string.Concat(pilot.PilotName, " · You"),
            GalaxyAtlasPilotDisplayKind.Social
                when pilot.IsNearNavApproximation =>
                string.Concat(pilot.PilotName, " · Near nav"),
            _ => pilot.PilotName,
        };
    }

    private static string FormatPilotLine(
        GalaxyAtlasDisplayedPilot pilot)
    {
        List<string> roles = [];

        if (pilot.IsSelectedPilot)
        {
            roles.Add("You");
        }

        if (pilot.IsGroupMember)
        {
            roles.Add("Group");
        }

        if (pilot.IsSocialPilot)
        {
            roles.Add("Social");
        }

        return roles.Count == 0
            ? pilot.PilotName
            : string.Concat(
                pilot.PilotName,
                " · ",
                string.Join(" · ", roles));
    }

    private static Color GetPilotColor(
        GalaxyAtlasPilotDisplayKind displayKind)
    {
        return displayKind switch
        {
            GalaxyAtlasPilotDisplayKind.Selected =>
                currentPilotLocationColor,
            GalaxyAtlasPilotDisplayKind.Group =>
                groupMemberLocationColor,
            _ => socialPilotLocationColor,
        };
    }

    private static float GetSquaredDistance(
        PointF left,
        PointF right)
    {
        var deltaX = left.X - right.X;
        var deltaY = left.Y - right.Y;
        return (deltaX * deltaX) + (deltaY * deltaY);
    }

    private static float GetSquaredDistance(
        PointF left,
        Point right)
    {
        return GetSquaredDistance(
            left,
            new PointF(right.X, right.Y));
    }

    private readonly record struct GalaxyAtlasPilotList(
        int TotalCount,
        IReadOnlyList<string> Lines);

    private enum GalaxyAtlasPilotDisplayKind
    {
        Social = 0,
        Group = 1,
        Selected = 2,
    }

    private sealed record GalaxyAtlasDisplayedPilot
    {
        public required string PilotName { get; init; }

        public float X { get; init; }

        public float Y { get; init; }

        public float Z { get; init; }

        public GalaxyAtlasPilotDisplayKind DisplayKind { get; init; }

        public bool IsSelectedPilot { get; init; }

        public bool IsGroupMember { get; init; }

        public bool IsSocialPilot { get; init; }

        public bool IsNearNavApproximation { get; init; }

        public bool IsDocked { get; init; }

        public string? AnchorName { get; init; }
    }

    private sealed class PilotClusterBuilder
    {
        private float totalX;
        private float totalY;

        public string? AnchorKey { get; init; }

        public PointF Point { get; set; }

        public List<GalaxyAtlasDisplayedPilot> Pilots { get; } = [];

        public void Add(
            GalaxyAtlasDisplayedPilot pilot,
            PointF point)
        {
            this.Pilots.Add(pilot);
            this.totalX += point.X;
            this.totalY += point.Y;

            if (this.AnchorKey == null)
            {
                this.Point = new PointF(
                    this.totalX / this.Pilots.Count,
                    this.totalY / this.Pilots.Count);
            }
        }
    }
}
