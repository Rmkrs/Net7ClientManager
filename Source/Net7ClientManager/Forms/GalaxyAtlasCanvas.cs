// ReSharper disable LocalizableElement
// ReSharper disable UnusedMember.Global
// ReSharper disable RedundantSwitchExpressionArms
namespace Net7ClientManager.Forms;

using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Globalization;
using Net7ClientManager.Navigation;
using Net7ClientManager.Observations.Models;

internal sealed partial class GalaxyAtlasCanvas : Control
{
    private const float MinimumZoomFactor = 0.65f;
    private const float MaximumZoomFactor = 8.0f;
    private const float MouseWheelZoomStep = 1.2f;
    private const int PanDragThreshold = 4;
    private const float PanOverscroll = 72.0f;

    private static readonly Color backgroundColor =
        Color.FromArgb(8, 13, 20);

    private static readonly Color borderColor =
        Color.FromArgb(38, 91, 116);

    private static readonly Color textColor =
        Color.FromArgb(225, 236, 243);

    private static readonly Color mutedTextColor =
        Color.FromArgb(129, 154, 171);

    private static readonly Color navigationPointColor =
        Color.FromArgb(73, 180, 213);

    private static readonly Color stationColor =
        Color.FromArgb(235, 183, 82);

    private static readonly Color harvestableFieldColor =
        Color.FromArgb(82, 214, 190);

    private static readonly Color gravityWellColor =
        Color.FromArgb(196, 124, 255);

    private static readonly Color planetColor =
        Color.FromArgb(99, 145, 235);

    private static readonly Color asteroidColor =
        Color.FromArgb(151, 162, 169);

    private static readonly Color unknownColor =
        Color.FromArgb(195, 105, 211);

    private static readonly Color accessibleColor =
        Color.FromArgb(76, 210, 139);

    private static readonly Color blockedColor =
        Color.FromArgb(236, 94, 94);

    private static readonly Color conditionalColor =
        Color.FromArgb(242, 188, 73);

    private static readonly Color unavailableColor =
        Color.FromArgb(116, 132, 143);

    private static readonly Color destinationColor =
        Color.FromArgb(124, 231, 255);

    private static readonly Color currentPilotLocationColor =
        Color.FromArgb(78, 116, 232);

    private static readonly Color groupMemberLocationColor =
        Color.FromArgb(214, 92, 255);

    private static readonly Color socialPilotLocationColor =
        Color.FromArgb(70, 205, 177);

    private static readonly Color toolTipBackgroundTopColor =
        Color.FromArgb(252, 15, 25, 35);

    private static readonly Color toolTipBackgroundBottomColor =
        Color.FromArgb(252, 9, 16, 24);

    private static readonly Color toolTipPanelColor =
        Color.FromArgb(28, 43, 56);

    private static readonly Color toolTipVendorColor =
        Color.FromArgb(116, 218, 239);

    private static readonly Color toolTipWarningColor =
        Color.FromArgb(242, 188, 73);

    private GalaxyTopology topology;
    private GalaxyNavigationCatalog catalog;
    private GalaxyAtlasStationInformationIndex stationInformationIndex;
    private GalaxyAtlasForgeOverlayIndex forgeOverlayIndex;
    private readonly ToolTip toolTip = new()
    {
        AutoPopDelay = 20000,
        InitialDelay = 350,
        ReshowDelay = 100,
        ShowAlways = true,
        OwnerDraw = true,
    };
    private readonly List<GalaxyAtlasNode> nodes = [];
    private readonly List<GalaxyAtlasForgeOverlayNode> overlayNodes = [];
    private readonly List<GalaxyAtlasPilotMarkerNode> pilotMarkerNodes = [];
    private readonly ContextMenuStrip targetContextMenu = new()
    {
        BackColor = Color.FromArgb(15, 24, 33),
        ForeColor = textColor,
        ShowImageMargin = false,
        ShowCheckMargin = false,
        Padding = new Padding(1),
        Renderer = new GalaxyAtlasContextMenuRenderer(),
    };
    private readonly ToolStripMenuItem setDestinationMenuItem = new()
    {
        Text = "Set destination",
        AutoSize = false,
        Size = new Size(122, 24),
        ForeColor = textColor,
        BackColor = Color.FromArgb(15, 24, 33),
        DisplayStyle = ToolStripItemDisplayStyle.Text,
        Margin = Padding.Empty,
        Padding = Padding.Empty,
        TextAlign = ContentAlignment.MiddleLeft,
    };

    private GalaxySectorDefinition? sector;
    private GalaxyNavigationCatalogSector? catalogSector;
    private string? pilotProfession;
    private string statusText = "Select a hosted pilot to open the atlas.";
    private bool isCurrentSector;
    private NavigationDestination? activeDestination;
    private NavigationDestination? contextDestination;
    private IReadOnlyList<GalaxyAtlasPilotLocation> livePilotLocations = [];
    private string livePilotLocationFingerprint = "";
    private float zoomFactor = 1.0f;
    private PointF viewOffset = PointF.Empty;
    private GalaxyNavigationCatalogTarget? hoveredTarget;
    private GalaxyAtlasForgeOverlayNode? hoveredOverlay;
    private string? hoveredOverlayKey;
    private GalaxyAtlasToolTipContent? hoveredToolTipContent;
    private GalaxyAtlasNode? pressedNode;
    private MouseButtons panButton = MouseButtons.None;
    private Point panStartLocation;
    private PointF panStartOffset;
    private bool isPanning;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Control? HoverFocusGuardControl { get; set; }

    public GalaxyAtlasCanvas(GalaxyDataSet dataSet)
    {
        ArgumentNullException.ThrowIfNull(dataSet);

        this.topology = dataSet.Topology;
        this.catalog = dataSet.Catalog;
        this.stationInformationIndex =
            new GalaxyAtlasStationInformationIndex(dataSet);
        this.forgeOverlayIndex =
            new GalaxyAtlasForgeOverlayIndex(dataSet);

        this.DoubleBuffered = true;
        this.ResizeRedraw = true;
        this.SetStyle(ControlStyles.Selectable, value: true);
        this.BackColor = backgroundColor;
        this.ForeColor = textColor;
        this.TabStop = true;

        this.toolTip.Popup += this.ToolTip_OnPopup;
        this.toolTip.Draw += this.ToolTip_OnDraw;
        this.toolTipShowTimer.Tick += this.ToolTipShowTimer_OnTick;
        this.searchFocusTimer.Tick += this.SearchFocusTimer_OnTick;

        this.targetContextMenu.ItemClicked +=
            this.TargetContextMenu_OnItemClicked;

        this.targetContextMenu.Items.Add(this.setDestinationMenuItem);
    }

    public event EventHandler<GalaxyAtlasDepartureRequestedEventArgs>?
        DepartureRequested;

    public event EventHandler<GalaxyAtlasDestinationRequestedEventArgs>?
        DestinationRequested;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool ShowLabels
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            this.Invalidate();
        }
    } = true;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool ShowMobEncounters
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            this.ClearHover();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool ShowHarvestableFields
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            this.ClearHover();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool ShowGravityWells
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            this.ClearHover();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool ShowCurrentLocation
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            this.ClearHover();
        }
    } = true;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool ShowGroupMembers
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            this.ClearHover();
        }
    } = true;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool ShowSocialPilots
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            this.ClearHover();
        }
    } = true;

    private ClientReputationObservation currentPilotReputation =
        ClientReputationObservation.Unavailable(
            "No current pilot reputation is selected");

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ClientReputationObservation CurrentPilotReputation
    {
        get => this.currentPilotReputation;
        set
        {
            if (this.currentPilotReputation == value)
            {
                return;
            }

            this.currentPilotReputation = value;
            this.Invalidate();
        }
    }

    public (int MobEncounters, int HarvestableFields, int GravityWells) GetLayerCounts(
        string? sectorKey)
    {
        return (
            this.forgeOverlayIndex.GetMobs(sectorKey).Count,
            this.forgeOverlayIndex.GetResources(sectorKey).Count,
            this.forgeOverlayIndex.GetGravityWells(sectorKey).Count);
    }

    public void SetLivePilotLocations(
        IReadOnlyList<GalaxyAtlasPilotLocation> locations)
    {
        ArgumentNullException.ThrowIfNull(locations);

        var orderedLocations = locations
            .OrderByDescending(location => location.IsSelectedPilot)
            .ThenBy(location => location.PilotName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(location => location.ProcessId)
            .ToArray();
        var fingerprint = string.Join(
            '\u001f',
            orderedLocations.Select(location =>
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{location.ProcessId}:{location.SectorKey}:" +
                    $"{location.X:R}:{location.Y:R}:{location.Z:R}:" +
                    $"{location.IsSelectedPilot}:{location.IsGroupMember}:" +
                    $"{location.IsSocialPilot}:{location.IsNearNavApproximation}:" +
                    $"{location.IsDocked}:{location.AnchorName}:" +
                    $"{location.PilotName}")));

        if (string.Equals(
                this.livePilotLocationFingerprint,
                fingerprint,
                StringComparison.Ordinal))
        {
            return;
        }

        this.livePilotLocationFingerprint = fingerprint;
        this.livePilotLocations = orderedLocations;
        this.Invalidate();
    }

    public void ResetView()
    {
        this.ResetViewCore();
        this.Invalidate();
    }

    public void UpdateData(GalaxyDataSet dataSet)
    {
        ArgumentNullException.ThrowIfNull(dataSet);

        this.topology = dataSet.Topology;
        this.catalog = dataSet.Catalog;
        this.stationInformationIndex =
            new GalaxyAtlasStationInformationIndex(dataSet);
        this.forgeOverlayIndex =
            new GalaxyAtlasForgeOverlayIndex(dataSet);
        this.targetContextMenu.Close();
        this.contextDestination = null;
        this.hoveredTarget = null;
        this.hoveredOverlay = null;
        this.hoveredOverlayKey = null;
        this.hoveredPilotMarker = null;
        this.hoveredPilotMarkerKey = null;
        this.hoveredToolTipContent = null;
        this.pressedNode = null;
        this.nodes.Clear();
        this.overlayNodes.Clear();
        this.pilotMarkerNodes.Clear();
        this.HideAtlasToolTip();
        this.Invalidate();
    }

    public void SetSector(
        GalaxySectorDefinition? sectorToSet,
        GalaxyNavigationCatalogSector? catalogSectorToSet,
        string? pilotProfessionToSet,
        NavigationDestination? activeDestinationToSet,
        bool isCurrentSectorToSet,
        string statusTextToSet)
    {
        var sectorChanged = !string.Equals(
            this.sector?.Key,
            sectorToSet?.Key,
            StringComparison.Ordinal);

        this.sector = sectorToSet;
        this.catalogSector = catalogSectorToSet;
        this.pilotProfession = pilotProfessionToSet;
        this.activeDestination = activeDestinationToSet;
        this.isCurrentSector = isCurrentSectorToSet;
        this.statusText = statusTextToSet;
        this.hoveredTarget = null;
        this.hoveredOverlay = null;
        this.hoveredOverlayKey = null;
        this.hoveredPilotMarker = null;
        this.hoveredPilotMarkerKey = null;
        this.hoveredToolTipContent = null;
        this.nodes.Clear();
        this.overlayNodes.Clear();
        this.pilotMarkerNodes.Clear();
        this.HideAtlasToolTip();
        this.Cursor = Cursors.Default;

        if (sectorChanged)
        {
            this.ResetViewCore();
        }

        this.Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.toolTip.Popup -= this.ToolTip_OnPopup;
            this.toolTip.Draw -= this.ToolTip_OnDraw;
            this.toolTipShowTimer.Tick -= this.ToolTipShowTimer_OnTick;
            this.toolTipShowTimer.Dispose();
            this.searchFocusTimer.Tick -= this.SearchFocusTimer_OnTick;
            this.searchFocusTimer.Dispose();
            this.toolTip.Dispose();

            this.targetContextMenu.ItemClicked -=
                this.TargetContextMenu_OnItemClicked;
            this.targetContextMenu.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);

        this.ClampViewOffset();
        this.ClearHover();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);

        if (this.HoverFocusGuardControl?.ContainsFocus == true)
        {
            return;
        }

        this.Focus();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);

        if (this.Capture)
        {
            return;
        }

        this.ClearHover();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (this.panButton != MouseButtons.None &&
            (Control.MouseButtons & this.panButton) != MouseButtons.None)
        {
            var deltaX = e.X - this.panStartLocation.X;
            var deltaY = e.Y - this.panStartLocation.Y;

            if (!this.isPanning &&
                ((deltaX * deltaX) + (deltaY * deltaY)) >=
                PanDragThreshold * PanDragThreshold)
            {
                this.isPanning = true;
                this.pressedNode = null;
                this.hoveredTarget = null;
                this.hoveredOverlay = null;
                this.hoveredOverlayKey = null;
                this.hoveredPilotMarker = null;
                this.hoveredPilotMarkerKey = null;
                this.hoveredToolTipContent = null;
                this.HideAtlasToolTip();
            }

            if (this.isPanning)
            {
                this.viewOffset = new PointF(
                    this.panStartOffset.X + deltaX,
                    this.panStartOffset.Y + deltaY);
                this.ClampViewOffset();
                this.nodes.Clear();
                this.overlayNodes.Clear();
                this.Cursor = Cursors.SizeAll;
                this.Invalidate();
                return;
            }
        }

        if (this.pressedNode != null)
        {
            return;
        }

        this.UpdateHover(e.Location);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);

        if (this.sector == null || e.Delta == 0)
        {
            return;
        }

        var wheelSteps = e.Delta / 120.0f;
        var requestedZoom = this.zoomFactor *
                            MathF.Pow(MouseWheelZoomStep, wheelSteps);
        var newZoom = Math.Clamp(
            requestedZoom,
            MinimumZoomFactor,
            MaximumZoomFactor);

        if (Math.Abs(newZoom - this.zoomFactor) < 0.001f)
        {
            return;
        }

        var mapCenter = GetRectangleCenter(GetMapBounds(this.ClientRectangle));
        var worldOffsetX =
            (e.X - mapCenter.X - this.viewOffset.X) /
            this.zoomFactor;
        var worldOffsetY =
            (e.Y - mapCenter.Y - this.viewOffset.Y) /
            this.zoomFactor;

        this.viewOffset = new PointF(
            e.X - mapCenter.X - (worldOffsetX * newZoom),
            e.Y - mapCenter.Y - (worldOffsetY * newZoom));
        this.zoomFactor = newZoom;
        this.ClampViewOffset();
        this.ClearHover();
        this.Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        this.Focus();

        if (e.Button == MouseButtons.Right)
        {
            var contextTarget = this.FindNodeAt(e.Location);
            var contextOverlay = contextTarget == null
                ? this.FindOverlayNodeAt(e.Location)
                : null;

            if (contextTarget != null)
            {
                this.contextDestination =
                    contextTarget.TargetDestination;
                this.hoveredTarget = contextTarget.Target;
                this.hoveredOverlay = null;
                this.hoveredOverlayKey = null;
                this.setDestinationMenuItem.Text = "Set destination";
            }
            else if (contextOverlay != null)
            {
                this.contextDestination =
                    contextOverlay.Information.Destination;
                this.hoveredTarget = null;
                this.hoveredOverlay = contextOverlay;
                this.hoveredOverlayKey = contextOverlay.Information.Id;
                this.setDestinationMenuItem.Text =
                    contextOverlay.Information.RouteDescription.Replace(
                        "Route",
                        "Set destination",
                        StringComparison.Ordinal);
            }
            else
            {
                return;
            }

            this.setDestinationMenuItem.Size = new Size(
                Math.Clamp(
                    MeasureTextWidth(
                        this.setDestinationMenuItem.Text,
                        this.Font) + 24,
                    122,
                    360),
                24);
            this.hoveredToolTipContent = null;
            this.Cursor = Cursors.Hand;
            this.HideAtlasToolTip();
            this.Invalidate();
            this.targetContextMenu.Show(
                this,
                new Point(e.X + 8, e.Y + 8));
            return;
        }

        if (e.Button == MouseButtons.Middle)
        {
            this.BeginPan(e.Button, e.Location);
            return;
        }

        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        var node = this.FindNodeAt(e.Location);

        if (node?.IsClickable == true)
        {
            this.pressedNode = node;
            this.Capture = true;
            return;
        }

        if (node == null &&
            this.FindOverlayNodeAt(e.Location) == null &&
            this.FindPilotMarkerAt(e.Location) == null &&
            this.CanPan)
        {
            this.BeginPan(e.Button, e.Location);
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (e.Button == this.panButton)
        {
            var wasPanning = this.isPanning;
            this.EndPan();

            if (wasPanning)
            {
                this.UpdateHover(e.Location);
                return;
            }
        }

        if (e.Button != MouseButtons.Left || this.pressedNode == null)
        {
            return;
        }

        var node = this.pressedNode;
        this.pressedNode = null;
        this.Capture = false;

        var releasedNode = this.FindNodeAt(e.Location);

        if (!ReferenceEquals(node, releasedNode) ||
            node.Departure == null)
        {
            this.UpdateHover(e.Location);
            return;
        }

        this.DepartureRequested?.Invoke(
            this,
            new GalaxyAtlasDepartureRequestedEventArgs(
                node.Departure));
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);

        if (this.Capture)
        {
            return;
        }

        this.panButton = MouseButtons.None;
        this.isPanning = false;
        this.pressedNode = null;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        if (this.ClientSize.Width <= 0 ||
            this.ClientSize.Height <= 0)
        {
            return;
        }

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.TextRenderingHint =
            System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        this.DrawBackground(e.Graphics);

        if (this.sector == null)
        {
            this.DrawCenteredMessage(
                e.Graphics,
                this.statusText);
            return;
        }

        var targets = this.catalogSector?.Targets
            .Where(target => target.HasPosition)
            .ToArray() ?? [];
        var enabledOverlays = this.GetEnabledOverlays();
        var enabledPilotLocations = this.GetEnabledPilotLocations();

        if (targets.Length == 0 &&
            enabledOverlays.Count == 0 &&
            enabledPilotLocations.Count == 0)
        {
            this.DrawSectorHeading(e.Graphics);
            this.DrawSearchFocus(e.Graphics);
            this.DrawCenteredMessage(
                e.Graphics,
                this.catalogSector == null
                    ? "No surveyed navigation inventory is available for this sector."
                    : "This sector has no navigation targets with known coordinates.");
            return;
        }

        this.BuildScene(
            targets,
            enabledOverlays,
            enabledPilotLocations);
        this.DrawSectorHeading(e.Graphics);
        this.DrawOverlayNodes(e.Graphics);
        this.DrawOverlayLabels(e.Graphics);
        this.DrawNodes(e.Graphics);
        this.DrawLabels(e.Graphics);
        this.DrawPilotMarkers(e.Graphics);
        this.DrawSearchFocus(e.Graphics);
        this.DrawZoomIndicator(e.Graphics);
    }

    private void DrawBackground(Graphics graphics)
    {
        using var backgroundBrush = new LinearGradientBrush(
            this.ClientRectangle,
            backgroundColor,
            Color.FromArgb(14, 25, 35),
            LinearGradientMode.Vertical);

        graphics.FillRectangle(
            backgroundBrush,
            this.ClientRectangle);

        using var borderPen = new Pen(borderColor);
        graphics.DrawRectangle(
            borderPen,
            0,
            0,
            Math.Max(0, this.ClientSize.Width - 1),
            Math.Max(0, this.ClientSize.Height - 1));
    }

    private void DrawSectorHeading(Graphics graphics)
    {
        if (this.sector == null)
        {
            return;
        }

        var heading = string.Concat(
            this.sector.SystemName,
            " / ",
            this.sector.Name);

        using var headingFont = new Font(
            this.Font.FontFamily,
            11.0f,
            FontStyle.Bold);

        TextRenderer.DrawText(
            graphics,
            heading,
            headingFont,
            new Rectangle(18, 14, this.ClientSize.Width - 36, 24),
            textColor,
            TextFormatFlags.Left |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPrefix |
            TextFormatFlags.NoPadding |
            TextFormatFlags.SingleLine);

        if (!this.isCurrentSector)
        {
            return;
        }

        const string currentText = "CURRENT LOCATION";
        using var currentLocationFont = new Font(
            this.Font.FontFamily,
            7.5f,
            FontStyle.Bold);

        var size = TextRenderer.MeasureText(
            currentText,
            currentLocationFont,
            Size.Empty,
            TextFormatFlags.NoPadding |
            TextFormatFlags.NoPrefix |
            TextFormatFlags.SingleLine);

        var bounds = new Rectangle(
            this.ClientSize.Width - size.Width - 34,
            14,
            size.Width + 16,
            22);

        using var backgroundBrush = new SolidBrush(
            Color.FromArgb(36, accessibleColor));
        using var borderPen = new Pen(accessibleColor);

        graphics.FillRectangle(backgroundBrush, bounds);
        graphics.DrawRectangle(borderPen, bounds);

        TextRenderer.DrawText(
            graphics,
            currentText,
            currentLocationFont,
            bounds,
            accessibleColor,
            TextFormatFlags.HorizontalCenter |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPadding |
            TextFormatFlags.NoPrefix |
            TextFormatFlags.SingleLine);
    }

    private void DrawCenteredMessage(
        Graphics graphics,
        string message)
    {
        var bounds = Rectangle.Inflate(
            this.ClientRectangle,
            -80,
            -80);

        TextRenderer.DrawText(
            graphics,
            message,
            this.Font,
            bounds,
            mutedTextColor,
            TextFormatFlags.HorizontalCenter |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.WordBreak |
            TextFormatFlags.NoPrefix);
    }

    private void BuildScene(
        IReadOnlyCollection<GalaxyNavigationCatalogTarget> targets,
        IReadOnlyCollection<GalaxyAtlasForgeOverlayInformation> overlays,
        IReadOnlyCollection<GalaxyAtlasDisplayedPilot> pilotLocations)
    {
        this.nodes.Clear();
        this.overlayNodes.Clear();
        this.pilotMarkerNodes.Clear();

        var staticCoordinates = targets
            .Select(target => new PointF(target.X, target.Y))
            .Concat(overlays.Select(overlay =>
                new PointF(overlay.CenterX, overlay.CenterY)))
            .ToArray();
        var coordinates = staticCoordinates.Length == 0
            ? pilotLocations
                .Select(location => new PointF(location.X, location.Y))
                .ToArray()
            : staticCoordinates;

        if (coordinates.Length == 0)
        {
            return;
        }

        var minimumX = coordinates.Min(point => point.X);
        var maximumX = coordinates.Max(point => point.X);
        var minimumY = coordinates.Min(point => point.Y);
        var maximumY = coordinates.Max(point => point.Y);

        var spanX = Math.Max(maximumX - minimumX, 1000.0f);
        var spanY = Math.Max(maximumY - minimumY, 1000.0f);

        minimumX -= spanX * 0.08f;
        maximumX += spanX * 0.08f;
        minimumY -= spanY * 0.08f;
        maximumY += spanY * 0.08f;

        spanX = maximumX - minimumX;
        spanY = maximumY - minimumY;

        var mapBounds = GetMapBounds(this.ClientRectangle);
        var mapCenter = GetRectangleCenter(mapBounds);

        var scale = Math.Min(
            mapBounds.Width / spanX,
            mapBounds.Height / spanY);

        var contentWidth = spanX * scale;
        var contentHeight = spanY * scale;
        var offsetX = mapBounds.Left +
                      ((mapBounds.Width - contentWidth) * 0.5f);
        var offsetY = mapBounds.Top +
                      ((mapBounds.Height - contentHeight) * 0.5f);

        PointF Project(float x, float y)
        {
            var basePoint = new PointF(
                offsetX + ((x - minimumX) * scale),
                offsetY + ((maximumY - y) * scale));

            return this.ApplyViewTransform(basePoint, mapCenter);
        }

        foreach (var overlay in overlays)
        {
            var point = Project(overlay.CenterX, overlay.CenterY);
            var fieldRadius = Math.Clamp(
                overlay.Radius * scale * this.zoomFactor,
                10.0f,
                70.0f);
            const float markerRadius = 7.0f;
            var hitRadius = Math.Max(markerRadius + 6.0f, 12.0f);

            this.overlayNodes.Add(new GalaxyAtlasForgeOverlayNode
            {
                Information = overlay,
                Point = point,
                FieldRadius = fieldRadius,
                HitBounds = RectangleF.FromLTRB(
                    point.X - hitRadius,
                    point.Y - hitRadius,
                    point.X + hitRadius,
                    point.Y + hitRadius),
            });
        }

        foreach (var target in targets)
        {
            var point = Project(target.X, target.Y);
            var departure = this.FindDeparture(target);
            var destination = this.ResolveDestination(departure);
            var access = destination == null || departure == null
                ? new GalaxyAtlasDepartureAccess
                {
                    State = GalaxyAtlasAccessState.Unavailable,
                    Description = "The atlas does not have a verified connected sector for this target.",
                }
                : GalaxyAtlasAccessEvaluator.Evaluate(
                    destination,
                    departure,
                    this.pilotProfession);

            var radius = GetNodeRadius(target.Kind);

            this.nodes.Add(new GalaxyAtlasNode
            {
                Target = target,
                TargetDestination = NavigationDestination.ForTarget(
                    this.sector!,
                    target),
                Point = point,
                HitBounds = RectangleF.FromLTRB(
                    point.X - Math.Max(radius + 5.0f, 9.0f),
                    point.Y - Math.Max(radius + 5.0f, 9.0f),
                    point.X + Math.Max(radius + 5.0f, 9.0f),
                    point.Y + Math.Max(radius + 5.0f, 9.0f)),
                Departure = departure,
                Destination = destination,
                Access = access,
                IsLandablePlanet = this.catalog.IsLandablePlanetTarget(
                    this.sector!.Key,
                    target),
            });
        }

        this.BuildPilotMarkerNodes(
            pilotLocations,
            Project);

        if (this.hoveredOverlayKey != null)
        {
            this.hoveredOverlay = this.overlayNodes.FirstOrDefault(node =>
                string.Equals(
                    node.Information.Id,
                    this.hoveredOverlayKey,
                    StringComparison.Ordinal));
        }
    }

    private IReadOnlyList<GalaxyAtlasForgeOverlayInformation>
        GetEnabledOverlays()
    {
        if (this.sector == null)
        {
            return [];
        }

        List<GalaxyAtlasForgeOverlayInformation> overlays = [];

        if (this.ShowMobEncounters)
        {
            overlays.AddRange(
                this.forgeOverlayIndex.GetMobs(this.sector.Key));
        }

        if (this.ShowHarvestableFields)
        {
            overlays.AddRange(
                this.forgeOverlayIndex.GetResources(this.sector.Key));
        }

        if (this.ShowGravityWells)
        {
            overlays.AddRange(
                this.forgeOverlayIndex.GetGravityWells(this.sector.Key));
        }

        return overlays;
    }

    private IReadOnlyList<GalaxyAtlasDisplayedPilot> GetEnabledPilotLocations()
    {
        return this.BuildDisplayedPilotLocations();
    }

    private void DrawOverlayNodes(Graphics graphics)
    {
        foreach (var node in this.overlayNodes
                     .OrderBy(item => item.Information.Kind))
        {
            this.DrawOverlayNode(
                graphics,
                node,
                ReferenceEquals(node, this.hoveredOverlay));
        }
    }

    private void DrawOverlayNode(
        Graphics graphics,
        GalaxyAtlasForgeOverlayNode node,
        bool isHovered)
    {
        var color = this.GetOverlayColor(node.Information);
        var center = node.Point;
        var fieldRadius = node.FieldRadius;
        const float markerRadius = 7.0f;

        using var areaPen = new Pen(
            Color.FromArgb(isHovered ? 92 : 42, color),
            isHovered ? 1.6f : 1.0f)
        {
            DashStyle = DashStyle.Dash,
        };
        using var markerPen = new Pen(
            color,
            isHovered ? 2.5f : 1.7f);
        using var markerBrush = new SolidBrush(
            Color.FromArgb(isHovered ? 190 : 125, color));

        graphics.DrawEllipse(
            areaPen,
            center.X - fieldRadius,
            center.Y - fieldRadius,
            fieldRadius * 2.0f,
            fieldRadius * 2.0f);

        if (node.Information.Kind ==
            GalaxyAtlasForgeOverlayKind.MobEncounter)
        {
            graphics.FillEllipse(
                markerBrush,
                center.X - markerRadius,
                center.Y - markerRadius,
                markerRadius * 2.0f,
                markerRadius * 2.0f);
            graphics.DrawEllipse(
                markerPen,
                center.X - markerRadius,
                center.Y - markerRadius,
                markerRadius * 2.0f,
                markerRadius * 2.0f);
            graphics.DrawLine(
                markerPen,
                center.X - markerRadius - 3.0f,
                center.Y,
                center.X + markerRadius + 3.0f,
                center.Y);
            graphics.DrawLine(
                markerPen,
                center.X,
                center.Y - markerRadius - 3.0f,
                center.X,
                center.Y + markerRadius + 3.0f);
        }
        else if (node.Information.Kind ==
                 GalaxyAtlasForgeOverlayKind.GravityWell)
        {
            graphics.FillEllipse(
                markerBrush,
                center.X - markerRadius,
                center.Y - markerRadius,
                markerRadius * 2.0f,
                markerRadius * 2.0f);
            graphics.DrawEllipse(
                markerPen,
                center.X - markerRadius,
                center.Y - markerRadius,
                markerRadius * 2.0f,
                markerRadius * 2.0f);
            graphics.DrawEllipse(
                markerPen,
                center.X - markerRadius - 4.0f,
                center.Y - markerRadius - 4.0f,
                (markerRadius + 4.0f) * 2.0f,
                (markerRadius + 4.0f) * 2.0f);
        }
        else
        {
            PointF[] points =
            [
                new(center.X, center.Y - markerRadius - 1.0f),
                new(center.X + markerRadius, center.Y - 3.5f),
                new(center.X + markerRadius, center.Y + 3.5f),
                new(center.X, center.Y + markerRadius + 1.0f),
                new(center.X - markerRadius, center.Y + 3.5f),
                new(center.X - markerRadius, center.Y - 3.5f),
            ];

            graphics.FillPolygon(markerBrush, points);
            graphics.DrawPolygon(markerPen, points);
            graphics.DrawLine(
                markerPen,
                center.X - 3.0f,
                center.Y,
                center.X + 3.0f,
                center.Y);
        }

        if (!isHovered)
        {
            return;
        }

        using var hoverPen = new Pen(
            Color.FromArgb(220, textColor),
            1.0f);
        graphics.DrawEllipse(
            hoverPen,
            center.X - markerRadius - 6.0f,
            center.Y - markerRadius - 6.0f,
            (markerRadius + 6.0f) * 2.0f,
            (markerRadius + 6.0f) * 2.0f);
    }

    private void DrawOverlayLabels(Graphics graphics)
    {
        List<Rectangle> occupiedBounds = [];

        foreach (var node in this.overlayNodes
                     .OrderByDescending(item =>
                         ReferenceEquals(item, this.hoveredOverlay))
                     .ThenBy(item => item.Information.MapLabel,
                         StringComparer.OrdinalIgnoreCase))
        {
            var isHovered = ReferenceEquals(node, this.hoveredOverlay);

            if (!isHovered &&
                (!this.ShowLabels || this.zoomFactor < 2.2f))
            {
                continue;
            }

            var text = node.Information.MapLabel;
            var size = TextRenderer.MeasureText(
                text,
                this.Font,
                Size.Empty,
                TextFormatFlags.NoPadding |
                TextFormatFlags.NoPrefix |
                TextFormatFlags.SingleLine);
            var labelX = node.Point.X + 11.0f;

            if (labelX + size.Width > this.ClientSize.Width - 12)
            {
                labelX = node.Point.X - size.Width - 11.0f;
            }

            var labelY = Math.Clamp(
                node.Point.Y - (size.Height * 0.5f),
                44.0f,
                Math.Max(
                    44.0f,
                    this.ClientSize.Height - size.Height - 10.0f));
            var bounds = new Rectangle(
                (int)Math.Round(labelX),
                (int)Math.Round(labelY),
                size.Width + 2,
                size.Height);

            if (!isHovered && occupiedBounds.Exists(existing =>
                    existing.IntersectsWith(bounds)))
            {
                continue;
            }

            occupiedBounds.Add(bounds);

            TextRenderer.DrawText(
                graphics,
                text,
                this.Font,
                bounds,
                isHovered
                    ? textColor
                    : this.GetOverlayColor(node.Information),
                TextFormatFlags.Left |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPadding |
                TextFormatFlags.NoPrefix |
                TextFormatFlags.SingleLine);
        }
    }

    private Color GetOverlayColor(
        GalaxyAtlasForgeOverlayInformation information)
    {
        return information.Kind switch
        {
            GalaxyAtlasForgeOverlayKind.MobEncounter =>
                this.ResolveMobEncounterColor(information),
            GalaxyAtlasForgeOverlayKind.HarvestableField =>
                harvestableFieldColor,
            GalaxyAtlasForgeOverlayKind.GravityWell =>
                gravityWellColor,
            _ => navigationPointColor,
        };
    }

    private Color ResolveMobEncounterColor(
        GalaxyAtlasForgeOverlayInformation information)
    {
        return ResolveAtlasSafety(
            information,
            this.CurrentPilotReputation) switch
        {
            GalaxyAtlasSafetyBand.Danger => blockedColor,
            GalaxyAtlasSafetyBand.Neutral => conditionalColor,
            GalaxyAtlasSafetyBand.Safe => accessibleColor,
            _ => unavailableColor,
        };
    }

    private static GalaxyAtlasSafetyBand ResolveAtlasSafety(
        GalaxyAtlasForgeOverlayInformation information,
        ClientReputationObservation reputation)
    {
        return EncounterDispositionResolver.Resolve(
            information.MobFactionBindingKind,
            information.MobFactionIdentifier,
            information.IntrinsicRelationshipRaw,
            reputation);
    }

    private void DrawNodes(Graphics graphics)
    {
        foreach (var node in this.nodes
                     .OrderBy(item => GetDrawingPriority(item.Target.Kind)))
        {
            this.DrawNode(
                graphics,
                node,
                ReferenceEquals(node.Target, this.hoveredTarget));
        }
    }

    private void DrawNode(
        Graphics graphics,
        GalaxyAtlasNode node,
        bool isHovered)
    {
        var color = this.GetNodeColor(node);
        var radius = GetNodeRadius(node.Target.Kind);
        var center = node.Point;
        var isDestination = this.IsRouteDestination(node);

        using var pen = new Pen(color, isHovered ? 2.4f : 1.5f);
        using var brush = new SolidBrush(
            Color.FromArgb(isHovered ? 210 : 150, color));

        switch (node.Target.Kind)
        {
            case GalaxyNavigationTargetKind.Planet:
                graphics.DrawEllipse(
                    pen,
                    center.X - radius,
                    center.Y - radius,
                    radius * 2.0f,
                    radius * 2.0f);

                if (node.IsLandablePlanet)
                {
                    graphics.FillEllipse(
                        brush,
                        center.X - 2.5f,
                        center.Y - 2.5f,
                        5.0f,
                        5.0f);
                }

                break;

            case GalaxyNavigationTargetKind.SectorGate:
                PointF[] gatePoints =
                [
                    center with { Y = center.Y - radius },
                    center with { X = center.X + radius },
                    center with { Y = center.Y + radius },
                    center with { X = center.X - radius },
                ];

                graphics.FillPolygon(brush, gatePoints);
                graphics.DrawPolygon(pen, gatePoints);
                break;

            case GalaxyNavigationTargetKind.Station:
                graphics.FillRectangle(
                    brush,
                    center.X - radius,
                    center.Y - radius,
                    radius * 2.0f,
                    radius * 2.0f);
                graphics.DrawRectangle(
                    pen,
                    center.X - radius,
                    center.Y - radius,
                    radius * 2.0f,
                    radius * 2.0f);
                break;

            case GalaxyNavigationTargetKind.Asteroid:
                PointF[] asteroidPoints =
                [
                    center with { Y = center.Y - radius },
                    center with { X = center.X + radius, Y = center.Y - (radius * 0.35f) },
                    center with { X = center.X + (radius * 0.65f), Y = center.Y + radius },
                    center with { X = center.X - (radius * 0.65f), Y = center.Y + radius },
                    center with { X = center.X - radius, Y = center.Y - (radius * 0.35f) },
                ];

                graphics.FillPolygon(brush, asteroidPoints);
                graphics.DrawPolygon(pen, asteroidPoints);
                break;

            case GalaxyNavigationTargetKind.NavigationPoint:
                graphics.FillEllipse(
                    brush,
                    center.X - radius,
                    center.Y - radius,
                    radius * 2.0f,
                    radius * 2.0f);
                break;

            case GalaxyNavigationTargetKind.Unknown:
            default:
                graphics.DrawLine(
                    pen,
                    center.X - radius,
                    center.Y - radius,
                    center.X + radius,
                    center.Y + radius);
                graphics.DrawLine(
                    pen,
                    center.X + radius,
                    center.Y - radius,
                    center.X - radius,
                    center.Y + radius);
                break;
        }

        if (isDestination)
        {
            this.DrawDestinationMarker(
                graphics,
                center,
                radius);
        }

        if (isHovered)
        {
            using var hoverPen = new Pen(
                Color.FromArgb(210, textColor),
                1.0f);

            graphics.DrawEllipse(
                hoverPen,
                center.X - radius - 5.0f,
                center.Y - radius - 5.0f,
                (radius + 5.0f) * 2.0f,
                (radius + 5.0f) * 2.0f);
        }
    }

    private void DrawLabels(Graphics graphics)
    {
        List<Rectangle> occupiedBounds = [];

        var labelCandidates = this.nodes
            .OrderByDescending(node => ReferenceEquals(node.Target, this.hoveredTarget))
            .ThenByDescending(this.IsRouteDestination)
            .ThenByDescending(node => GetLabelPriority(node.Target.Kind))
            .ThenBy(node => NavigationDestination.GetTargetDisplayName(node.Target), StringComparer.OrdinalIgnoreCase);

        foreach (var node in labelCandidates)
        {
            var isHovered = ReferenceEquals(node.Target, this.hoveredTarget);
            var isDestination = this.IsRouteDestination(node);

            if (!this.ShowLabels && !isHovered && !isDestination)
            {
                continue;
            }

            var isImportant = GetLabelPriority(node.Target.Kind) > 0;

            if (!isHovered && !isImportant && this.nodes.Count > 34)
            {
                continue;
            }

            var text = NavigationDestination.GetTargetDisplayName(
                node.Target);

            var size = TextRenderer.MeasureText(
                text,
                this.Font,
                Size.Empty,
                TextFormatFlags.NoPadding |
                TextFormatFlags.NoPrefix |
                TextFormatFlags.SingleLine);

            var labelX = node.Point.X + 9.0f;

            if (labelX + size.Width > this.ClientSize.Width - 12)
            {
                labelX = node.Point.X - size.Width - 9.0f;
            }

            var labelY = Math.Clamp(
                node.Point.Y - (size.Height * 0.5f),
                44.0f,
                Math.Max(44.0f, this.ClientSize.Height - size.Height - 10.0f));

            var bounds = new Rectangle(
                (int)Math.Round(labelX),
                (int)Math.Round(labelY),
                size.Width + 2,
                size.Height);

            if (!isHovered && occupiedBounds.Exists(existing =>
                    existing.IntersectsWith(bounds)))
            {
                continue;
            }

            occupiedBounds.Add(bounds);

            TextRenderer.DrawText(
                graphics,
                text,
                this.Font,
                bounds,
                isHovered
                    ? textColor
                    : isDestination
                        ? destinationColor
                        : this.GetNodeColor(node),
                TextFormatFlags.Left |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPadding |
                TextFormatFlags.NoPrefix |
                TextFormatFlags.SingleLine);
        }
    }

    private void DrawPilotMarkers(Graphics graphics)
    {
        foreach (var node in this.pilotMarkerNodes
                     .OrderBy(marker => marker.Pilots[0].DisplayKind))
        {
            this.DrawPilotMarkerNode(graphics, node);
        }
    }

    private void DrawZoomIndicator(Graphics graphics)
    {
        var instruction = this.CanPan
            ? "Wheel to zoom · drag to pan"
            : "Wheel to zoom";
        var text = string.Create(
            CultureInfo.InvariantCulture,
            $"{instruction} · {this.zoomFactor:0.0}×");
        var size = TextRenderer.MeasureText(
            text,
            this.Font,
            Size.Empty,
            TextFormatFlags.NoPadding |
            TextFormatFlags.NoPrefix |
            TextFormatFlags.SingleLine);
        var bounds = new Rectangle(
            Math.Max(12, this.ClientSize.Width - size.Width - 20),
            Math.Max(44, this.ClientSize.Height - size.Height - 14),
            size.Width,
            size.Height);

        TextRenderer.DrawText(
            graphics,
            text,
            this.Font,
            bounds,
            mutedTextColor,
            TextFormatFlags.Left |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPadding |
            TextFormatFlags.NoPrefix |
            TextFormatFlags.SingleLine);
    }

    private bool CanPan => this.zoomFactor > 1.001f;

    private void BeginPan(
        MouseButtons button,
        Point location)
    {
        if (!this.CanPan)
        {
            return;
        }

        this.panButton = button;
        this.panStartLocation = location;
        this.panStartOffset = this.viewOffset;
        this.isPanning = button == MouseButtons.Middle;
        this.pressedNode = null;
        this.hoveredTarget = null;
        this.hoveredOverlay = null;
        this.hoveredOverlayKey = null;
        this.hoveredPilotMarker = null;
        this.hoveredPilotMarkerKey = null;
        this.hoveredToolTipContent = null;
        this.HideAtlasToolTip();
        this.Capture = true;
        this.Cursor = Cursors.SizeAll;
    }

    private void EndPan()
    {
        this.panButton = MouseButtons.None;
        this.isPanning = false;
        this.Capture = false;
        this.Cursor = Cursors.Default;
    }

    private void UpdateHover(Point location)
    {
        var pilotMarker = this.FindPilotMarkerAt(location);
        var node = pilotMarker == null
            ? this.FindNodeAt(location)
            : null;
        var overlay = pilotMarker == null && node == null
            ? this.FindOverlayNodeAt(location)
            : null;

        var overlayKey = overlay?.Information.Id;

        if (ReferenceEquals(node?.Target, this.hoveredTarget) &&
            string.Equals(
                overlayKey,
                this.hoveredOverlayKey,
                StringComparison.Ordinal) &&
            string.Equals(
                pilotMarker?.Key,
                this.hoveredPilotMarkerKey,
                StringComparison.Ordinal) &&
            this.hoveredToolTipContent != null)
        {
            // Overlay scene nodes are rebuilt for every paint. Rebind the
            // current node by its stable Forge ID without restarting the
            // tooltip whenever the mouse moves inside the same overlay.
            this.hoveredOverlay = overlay;
            this.hoveredPilotMarker = pilotMarker;
            this.Cursor = this.GetCursor(
                node,
                overlay,
                pilotMarker);
            return;
        }

        this.hoveredTarget = node?.Target;
        this.hoveredOverlay = overlay;
        this.hoveredOverlayKey = overlayKey;
        this.hoveredPilotMarker = pilotMarker;
        this.hoveredPilotMarkerKey = pilotMarker?.Key;
        var content = pilotMarker != null
            ? this.BuildToolTip(pilotMarker)
            : node != null
                ? this.BuildToolTip(node)
                : overlay != null
                    ? this.BuildToolTip(overlay)
                    : null;
        this.Cursor = this.GetCursor(
            node,
            overlay,
            pilotMarker);
        var anchor = pilotMarker != null
            ? Point.Round(pilotMarker.Point)
            : node != null
                ? Point.Round(node.Point)
                : overlay != null
                    ? Point.Round(overlay.Point)
                    : location;

        this.ScheduleAtlasToolTip(content, anchor);
        this.Invalidate();
    }

    private void ClearHover()
    {
        this.hoveredTarget = null;
        this.hoveredOverlay = null;
        this.hoveredOverlayKey = null;
        this.hoveredPilotMarker = null;
        this.hoveredPilotMarkerKey = null;
        this.hoveredToolTipContent = null;
        this.nodes.Clear();
        this.overlayNodes.Clear();
        this.pilotMarkerNodes.Clear();
        this.HideAtlasToolTip();
        this.Cursor = this.CanPan
            ? Cursors.SizeAll
            : Cursors.Default;
        this.Invalidate();
    }

    private Cursor GetCursor(
        GalaxyAtlasNode? node,
        GalaxyAtlasForgeOverlayNode? overlay,
        GalaxyAtlasPilotMarkerNode? pilotMarker)
    {
        if (node?.IsClickable == true)
        {
            return Cursors.Hand;
        }

        return this.CanPan &&
               node == null &&
               overlay == null &&
               pilotMarker == null
            ? Cursors.SizeAll
            : Cursors.Default;
    }

    private void ClampViewOffset()
    {
        var mapBounds = GetMapBounds(this.ClientRectangle);
        var extraWidth = Math.Max(
            0.0f,
            (mapBounds.Width * (this.zoomFactor - 1.0f)) * 0.5f);
        var extraHeight = Math.Max(
            0.0f,
            (mapBounds.Height * (this.zoomFactor - 1.0f)) * 0.5f);
        var overscroll = this.CanPan
            ? PanOverscroll
            : 0.0f;

        this.viewOffset = new PointF(
            Math.Clamp(
                this.viewOffset.X,
                -extraWidth - overscroll,
                extraWidth + overscroll),
            Math.Clamp(
                this.viewOffset.Y,
                -extraHeight - overscroll,
                extraHeight + overscroll));
    }

    private PointF ApplyViewTransform(
        PointF point,
        PointF mapCenter)
    {
        return new PointF(
            mapCenter.X +
            ((point.X - mapCenter.X) * this.zoomFactor) +
            this.viewOffset.X,
            mapCenter.Y +
            ((point.Y - mapCenter.Y) * this.zoomFactor) +
            this.viewOffset.Y);
    }

    private void ResetViewCore()
    {
        this.zoomFactor = 1.0f;
        this.viewOffset = PointF.Empty;
        this.panButton = MouseButtons.None;
        this.isPanning = false;
        this.pressedNode = null;
        this.ClearHover();
    }

    private static PointF GetRectangleCenter(RectangleF rectangle)
    {
        return new PointF(
            rectangle.Left + (rectangle.Width * 0.5f),
            rectangle.Top + (rectangle.Height * 0.5f));
    }

    private static RectangleF GetMapBounds(Rectangle clientRectangle)
    {
        var mapBounds = RectangleF.Inflate(
            new RectangleF(
                clientRectangle.X,
                clientRectangle.Y,
                clientRectangle.Width,
                clientRectangle.Height),
            -54.0f,
            -58.0f);

        mapBounds.Y += 16.0f;
        mapBounds.Height -= 16.0f;

        return mapBounds;
    }

    private void ToolTip_OnPopup(
        object? sender,
        PopupEventArgs e)
    {
        if (this.hoveredToolTipContent == null)
        {
            e.Cancel = true;
            return;
        }

        var metrics = this.MeasureToolTip(
            this.hoveredToolTipContent);
        e.ToolTipSize = new Size(
            metrics.Width,
            metrics.Height);
    }

    private void ToolTip_OnDraw(
        object? sender,
        DrawToolTipEventArgs e)
    {
        var content = this.hoveredToolTipContent;

        if (content == null)
        {
            return;
        }

        var metrics = this.MeasureToolTip(content);
        var accentColor = this.GetHoveredAccentColor();

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.TextRenderingHint =
            System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        using var backgroundBrush = new LinearGradientBrush(
            e.Bounds,
            toolTipBackgroundTopColor,
            toolTipBackgroundBottomColor,
            LinearGradientMode.Vertical);
        using var borderPen = new Pen(borderColor);
        using var accentBrush = new SolidBrush(accentColor);
        using var dividerPen = new Pen(
            Color.FromArgb(70, borderColor));
        using var titleFont = new Font(
            this.Font.FontFamily,
            this.Font.Size + 1.25f,
            FontStyle.Bold);
        using var sectionFont = new Font(
            this.Font,
            FontStyle.Bold);

        e.Graphics.FillRectangle(backgroundBrush, e.Bounds);
        e.Graphics.FillRectangle(
            accentBrush,
            0,
            0,
            e.Bounds.Width,
            3);
        e.Graphics.DrawRectangle(
            borderPen,
            0,
            0,
            Math.Max(0, e.Bounds.Width - 1),
            Math.Max(0, e.Bounds.Height - 1));

        var contentLeft = 16;
        var contentWidth = Math.Max(1, e.Bounds.Width - 32);
        var y = 12;

        TextRenderer.DrawText(
            e.Graphics,
            content.Title,
            titleFont,
            new Rectangle(
                contentLeft,
                y,
                contentWidth,
                metrics.TitleHeight),
            accentColor,
            TextFormatFlags.Left |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPadding |
            TextFormatFlags.NoPrefix |
            TextFormatFlags.SingleLine);

        y += metrics.TitleHeight + 3;

        TextRenderer.DrawText(
            e.Graphics,
            content.Subtitle,
            this.Font,
            new Rectangle(
                contentLeft,
                y,
                contentWidth / 2,
                metrics.LineHeight),
            textColor,
            TextFormatFlags.Left |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPadding |
            TextFormatFlags.NoPrefix |
            TextFormatFlags.SingleLine);

        TextRenderer.DrawText(
            e.Graphics,
            content.Coordinates,
            this.Font,
            new Rectangle(
                contentLeft + (contentWidth / 2),
                y,
                contentWidth - (contentWidth / 2),
                metrics.LineHeight),
            mutedTextColor,
            TextFormatFlags.Right |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPadding |
            TextFormatFlags.NoPrefix |
            TextFormatFlags.SingleLine);

        y += metrics.LineHeight;

        if (content.DetailLines.Count > 0)
        {
            y += 8;

            foreach (var detail in content.DetailLines)
            {
                this.DrawToolTipBulletLine(
                    e.Graphics,
                    detail,
                    new Rectangle(
                        contentLeft,
                        y,
                        contentWidth,
                        metrics.LineHeight),
                    accentColor,
                    mutedTextColor);
                y += metrics.LineHeight;
            }
        }

        if (content.StationInformation != null)
        {
            y += 10;
            e.Graphics.DrawLine(
                dividerPen,
                contentLeft,
                y,
                e.Bounds.Width - contentLeft,
                y);
            y += 11;

            if (!content.StationInformation.HasAnyContribution)
            {
                this.DrawMissingStationInformation(
                    e.Graphics,
                    new Rectangle(
                        contentLeft,
                        y,
                        contentWidth,
                        54),
                    sectionFont);
                y += 54;
            }
            else
            {
                this.DrawStationInformation(
                    e.Graphics,
                    content.StationInformation,
                    metrics,
                    contentLeft,
                    y,
                    sectionFont);
                var rowCount = Math.Max(
                    this.GetFacilityDisplayLines(
                        content.StationInformation).Count,
                    this.GetNpcDisplayLines(
                        content.StationInformation).Count);
                y += metrics.LineHeight + 5 +
                     (rowCount * metrics.LineHeight) + 8;
            }
        }
        else if (content.ForgeOverlayInformation != null)
        {
            y += 10;
            e.Graphics.DrawLine(
                dividerPen,
                contentLeft,
                y,
                e.Bounds.Width - contentLeft,
                y);
            y += 11;

            this.DrawForgeOverlayInformation(
                e.Graphics,
                content.ForgeOverlayInformation,
                contentLeft,
                y,
                contentWidth,
                metrics.LineHeight,
                sectionFont);
        }

        if (content.DockedPilotLines.Count > 0)
        {
            y += 10;
            e.Graphics.DrawLine(
                dividerPen,
                contentLeft,
                y,
                e.Bounds.Width - contentLeft,
                y);
            y += 11;
            this.DrawDockedPilotLines(
                e.Graphics,
                content.DockedPilotLines,
                content.DockedPilotCount,
                contentLeft,
                y,
                contentWidth,
                metrics.LineHeight,
                sectionFont);
        }

        var footerBounds = new Rectangle(
            1,
            e.Bounds.Height - 33,
            Math.Max(1, e.Bounds.Width - 2),
            32);
        using var footerBrush = new SolidBrush(
            Color.FromArgb(185, toolTipPanelColor));
        e.Graphics.FillRectangle(footerBrush, footerBounds);
        e.Graphics.DrawLine(
            dividerPen,
            footerBounds.Left,
            footerBounds.Top,
            footerBounds.Right,
            footerBounds.Top);

        TextRenderer.DrawText(
            e.Graphics,
            content.ActionText,
            this.Font,
            Rectangle.Inflate(footerBounds, -15, 0),
            mutedTextColor,
            TextFormatFlags.Left |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPadding |
            TextFormatFlags.NoPrefix |
            TextFormatFlags.SingleLine);
    }

    private ToolTipMetrics MeasureToolTip(
        GalaxyAtlasToolTipContent content)
    {
        using var titleFont = new Font(
            this.Font.FontFamily,
            this.Font.Size + 1.25f,
            FontStyle.Bold);
        var lineHeight = TextRenderer.MeasureText(
            "Ag",
            this.Font,
            Size.Empty,
            TextFormatFlags.NoPadding).Height;
        var titleHeight = TextRenderer.MeasureText(
            "Ag",
            titleFont,
            Size.Empty,
            TextFormatFlags.NoPadding).Height;
        var stationInformation = content.StationInformation;
        var overlayInformation = content.ForgeOverlayInformation;
        var leftColumnWidth = 0;
        var rightColumnWidth = 0;
        int width;

        if (stationInformation != null &&
            stationInformation.HasAnyContribution)
        {
            var facilityLines = this.GetFacilityDisplayLines(
                stationInformation);
            var npcLines = this.GetNpcDisplayLines(
                stationInformation);
            var facilityWidth = facilityLines
                .Append(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"FACILITIES ({stationInformation.Facilities.Count})"))
                .Select(line => MeasureTextWidth(line, this.Font))
                .DefaultIfEmpty(180)
                .Max();
            var npcWidth = npcLines
                .Append(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"NPCS ({stationInformation.Npcs.Count})"))
                .Select(line => MeasureTextWidth(line, this.Font))
                .DefaultIfEmpty(220)
                .Max();

            leftColumnWidth = Math.Clamp(
                facilityWidth + 24,
                190,
                300);
            rightColumnWidth = Math.Clamp(
                npcWidth + 24,
                240,
                390);
            var dockedPilotWidth = content.DockedPilotLines
                .Append(string.Create(
                    CultureInfo.InvariantCulture,
                    $"DOCKED PILOTS ({content.DockedPilotCount})"))
                .Select(line => MeasureTextWidth(line, this.Font))
                .DefaultIfEmpty(0)
                .Max();
            width = Math.Clamp(
                Math.Max(
                    16 + leftColumnWidth + 22 + rightColumnWidth + 16,
                    dockedPilotWidth + 40),
                520,
                760);
        }
        else
        {
            var overlayLines = overlayInformation?.Sections
                .SelectMany(section =>
                    this.GetOverlayDisplayLines(section)
                        .Prepend(section.Title)) ??
                Enumerable.Empty<string>();
            var measuredWidth = new[]
                {
                    content.Title,
                    content.Subtitle,
                    content.Coordinates,
                    content.ActionText,
                }
                .Concat(content.DetailLines)
                .Concat(content.DockedPilotLines)
                .Concat(overlayLines)
                .Select(line => MeasureTextWidth(line, this.Font))
                .DefaultIfEmpty(260)
                .Max();
            var minimumWidth = stationInformation != null
                ? 500
                : overlayInformation != null
                    ? 420
                    : 280;
            var maximumWidth = stationInformation != null
                ? 620
                : overlayInformation != null
                    ? 720
                    : 580;

            width = Math.Clamp(
                measuredWidth + 40,
                minimumWidth,
                maximumWidth);
        }

        var height = 12 + titleHeight + 3 + lineHeight;

        if (content.DetailLines.Count > 0)
        {
            height += 8 + (content.DetailLines.Count * lineHeight);
        }

        if (stationInformation != null)
        {
            height += 22;

            if (!stationInformation.HasAnyContribution)
            {
                height += 54;
            }
            else
            {
                var rowCount = Math.Max(
                    this.GetFacilityDisplayLines(stationInformation).Count,
                    this.GetNpcDisplayLines(stationInformation).Count);
                height += lineHeight + 5 + (rowCount * lineHeight) + 8;
            }
        }
        else if (overlayInformation != null)
        {
            height += 22;

            foreach (var section in overlayInformation.Sections)
            {
                height += lineHeight + 5;
                height += this.GetOverlayDisplayLines(section).Count * lineHeight;
                height += 8;
            }
        }

        if (content.DockedPilotLines.Count > 0)
        {
            height += 22;
            height += lineHeight + 5 +
                      (content.DockedPilotLines.Count * lineHeight) + 8;
        }

        height += 34;

        return new ToolTipMetrics(
            width,
            height,
            lineHeight,
            titleHeight,
            leftColumnWidth,
            rightColumnWidth);
    }

    private void DrawForgeOverlayInformation(
        Graphics graphics,
        GalaxyAtlasForgeOverlayInformation information,
        int left,
        int top,
        int width,
        int lineHeight,
        Font sectionFont)
    {
        var color = this.GetOverlayColor(information);
        var y = top;

        foreach (var section in information.Sections)
        {
            TextRenderer.DrawText(
                graphics,
                section.Title,
                sectionFont,
                new Rectangle(left, y, width, lineHeight),
                color,
                TextFormatFlags.Left |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPadding |
                TextFormatFlags.NoPrefix |
                TextFormatFlags.SingleLine);
            y += lineHeight + 5;

            foreach (var line in this.GetOverlayDisplayLines(section))
            {
                this.DrawToolTipBulletLine(
                    graphics,
                    line,
                    new Rectangle(left, y, width, lineHeight),
                    color,
                    textColor);
                y += lineHeight;
            }

            y += 8;
        }
    }

    private IReadOnlyList<string> GetOverlayDisplayLines(
        GalaxyAtlasForgeOverlaySection section)
    {
        const int maximumLines = 12;

        if (section.Lines.Count <= maximumLines)
        {
            return section.Lines;
        }

        return
        [
            .. section.Lines.Take(maximumLines - 1),
            string.Create(
                CultureInfo.InvariantCulture,
                $"… {section.Lines.Count - (maximumLines - 1)} more"),
        ];
    }

    private void DrawStationInformation(
        Graphics graphics,
        GalaxyAtlasStationInformation information,
        ToolTipMetrics metrics,
        int left,
        int top,
        Font sectionFont)
    {
        var facilityLines = this.GetFacilityDisplayLines(information);
        var npcLines = this.GetNpcDisplayLines(information);
        var leftBounds = new Rectangle(
            left,
            top,
            metrics.LeftColumnWidth,
            metrics.LineHeight);
        var rightBounds = new Rectangle(
            left + metrics.LeftColumnWidth + 22,
            top,
            metrics.RightColumnWidth,
            metrics.LineHeight);

        TextRenderer.DrawText(
            graphics,
            string.Create(
                CultureInfo.InvariantCulture,
                $"FACILITIES ({information.Facilities.Count})"),
            sectionFont,
            leftBounds,
            stationColor,
            TextFormatFlags.Left |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPadding |
            TextFormatFlags.NoPrefix |
            TextFormatFlags.SingleLine);
        TextRenderer.DrawText(
            graphics,
            string.Create(
                CultureInfo.InvariantCulture,
                $"NPCS ({information.Npcs.Count})"),
            sectionFont,
            rightBounds,
            navigationPointColor,
            TextFormatFlags.Left |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPadding |
            TextFormatFlags.NoPrefix |
            TextFormatFlags.SingleLine);

        var rowTop = top + metrics.LineHeight + 5;
        this.DrawStationColumn(
            graphics,
            facilityLines,
            left,
            rowTop,
            metrics.LeftColumnWidth,
            metrics.LineHeight,
            stationColor,
            information.HasFacilityContribution);
        this.DrawStationColumn(
            graphics,
            npcLines,
            rightBounds.Left,
            rowTop,
            metrics.RightColumnWidth,
            metrics.LineHeight,
            navigationPointColor,
            information.HasNpcContribution);
    }

    private void DrawStationColumn(
        Graphics graphics,
        IReadOnlyList<string> lines,
        int left,
        int top,
        int width,
        int lineHeight,
        Color bulletColor,
        bool hasContribution)
    {
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var isPlaceholder = index == 0 &&
                                ((!hasContribution &&
                                  line == "Not contributed yet.") ||
                                 line.StartsWith(
                                     "No named ",
                                     StringComparison.Ordinal));
            var lineColor = isPlaceholder
                ? toolTipWarningColor
                : line.Contains(" vendor", StringComparison.OrdinalIgnoreCase)
                    ? toolTipVendorColor
                    : textColor;

            this.DrawToolTipBulletLine(
                graphics,
                line,
                new Rectangle(
                    left,
                    top + (index * lineHeight),
                    width,
                    lineHeight),
                isPlaceholder ? toolTipWarningColor : bulletColor,
                lineColor);
        }
    }

    private void DrawMissingStationInformation(
        Graphics graphics,
        Rectangle bounds,
        Font titleFont)
    {
        using var panelBrush = new SolidBrush(
            Color.FromArgb(55, toolTipWarningColor));
        using var panelPen = new Pen(
            Color.FromArgb(150, toolTipWarningColor));

        graphics.FillRectangle(panelBrush, bounds);
        graphics.DrawRectangle(
            panelPen,
            bounds.Left,
            bounds.Top,
            Math.Max(0, bounds.Width - 1),
            Math.Max(0, bounds.Height - 1));

        TextRenderer.DrawText(
            graphics,
            "STATION DATA NOT YET CONTRIBUTED",
            titleFont,
            new Rectangle(
                bounds.Left + 12,
                bounds.Top + 7,
                bounds.Width - 24,
                18),
            toolTipWarningColor,
            TextFormatFlags.Left |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPadding |
            TextFormatFlags.NoPrefix |
            TextFormatFlags.SingleLine);
        TextRenderer.DrawText(
            graphics,
            "Visit this station to report its facilities and NPC roster to Forge.",
            this.Font,
            new Rectangle(
                bounds.Left + 12,
                bounds.Top + 27,
                bounds.Width - 24,
                18),
            textColor,
            TextFormatFlags.Left |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPadding |
            TextFormatFlags.NoPrefix |
            TextFormatFlags.SingleLine);
    }

    private void DrawToolTipBulletLine(
        Graphics graphics,
        string text,
        Rectangle bounds,
        Color bulletColor,
        Color lineColor)
    {
        using var bulletBrush = new SolidBrush(bulletColor);
        graphics.FillEllipse(
            bulletBrush,
            bounds.Left + 2,
            bounds.Top + ((bounds.Height - 4) / 2),
            4,
            4);

        TextRenderer.DrawText(
            graphics,
            text,
            this.Font,
            new Rectangle(
                bounds.Left + 12,
                bounds.Top,
                Math.Max(1, bounds.Width - 12),
                bounds.Height),
            lineColor,
            TextFormatFlags.Left |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPadding |
            TextFormatFlags.NoPrefix |
            TextFormatFlags.SingleLine);
    }

    private IReadOnlyList<string> GetFacilityDisplayLines(
        GalaxyAtlasStationInformation information)
    {
        if (information.Facilities.Count > 0)
        {
            return information.Facilities;
        }

        return
        [
            information.HasFacilityContribution
                ? "No named facilities available."
                : "Not contributed yet.",
        ];
    }

    private IReadOnlyList<string> GetNpcDisplayLines(
        GalaxyAtlasStationInformation information)
    {
        if (information.Npcs.Count > 0)
        {
            return
            [
                .. information.Npcs.Select(npc =>
                    string.IsNullOrWhiteSpace(npc.VendorDescription)
                        ? npc.Name
                        : string.Concat(
                            npc.Name,
                            " · ",
                            npc.VendorDescription)),
            ];
        }

        return
        [
            information.HasNpcContribution
                ? "No named NPCs available."
                : "Not contributed yet.",
        ];
    }

    private static int MeasureTextWidth(string text, Font font)
    {
        return TextRenderer.MeasureText(
            text,
            font,
            Size.Empty,
            TextFormatFlags.NoPadding |
            TextFormatFlags.NoPrefix |
            TextFormatFlags.SingleLine).Width;
    }

    private Color GetHoveredAccentColor()
    {
        if (this.hoveredPilotMarker != null)
        {
            return GetPilotColor(
                this.hoveredPilotMarker.Pilots[0].DisplayKind);
        }

        if (this.hoveredOverlay != null)
        {
            return this.GetOverlayColor(
                this.hoveredOverlay.Information);
        }

        var hoveredNode = this.nodes.FirstOrDefault(node =>
            ReferenceEquals(node.Target, this.hoveredTarget));

        return hoveredNode == null
            ? navigationPointColor
            : this.GetNodeColor(hoveredNode);
    }

    private GalaxyAtlasForgeOverlayNode? FindOverlayNodeAt(
        Point location)
    {
        return this.overlayNodes
            .Where(node => node.HitBounds.Contains(location.X, location.Y))
            .OrderBy(node =>
            {
                var deltaX = node.Point.X - location.X;
                var deltaY = node.Point.Y - location.Y;
                return (deltaX * deltaX) + (deltaY * deltaY);
            })
            .ThenByDescending(node => node.Information.Kind ==
                GalaxyAtlasForgeOverlayKind.MobEncounter)
            .FirstOrDefault();
    }

    private GalaxyAtlasNode? FindNodeAt(Point location)
    {
        return this.nodes
            .Where(node => node.HitBounds.Contains(location.X, location.Y))
            .OrderBy(node =>
            {
                var deltaX = node.Point.X - location.X;
                var deltaY = node.Point.Y - location.Y;
                return (deltaX * deltaX) + (deltaY * deltaY);
            })
            .ThenByDescending(node =>
                GetDrawingPriority(node.Target.Kind))
            .FirstOrDefault();
    }

    private GalaxyNavigationCatalogDeparture? FindDeparture(
        GalaxyNavigationCatalogTarget target)
    {
        if (this.catalogSector == null)
        {
            return null;
        }

        var normalizedName = GalaxyTopology.NormalizeName(target.Name);
        var normalizedMapName = GalaxyTopology.NormalizeName(
            target.MapDisplayName);

        return this.catalogSector.Departures.FirstOrDefault(departure =>
            departure.RawObjectType == target.RawObjectType &&
            (string.Equals(
                 GalaxyTopology.NormalizeName(
                     departure.DepartureTargetName),
                 normalizedName,
                 StringComparison.Ordinal) ||
             string.Equals(
                 GalaxyTopology.NormalizeName(
                     departure.DepartureTargetName),
                 normalizedMapName,
                 StringComparison.Ordinal)));
    }

    private GalaxySectorDefinition? ResolveDestination(
        GalaxyNavigationCatalogDeparture? departure)
    {
        return departure != null &&
               !string.IsNullOrWhiteSpace(departure.ToSectorKey) &&
               this.topology.TryGetByKey(
                   departure.ToSectorKey,
                   out var destination)
            ? destination
            : null;
    }

    private GalaxyAtlasToolTipContent BuildToolTip(
        GalaxyAtlasNode node)
    {
        var name = NavigationDestination.GetTargetDisplayName(
            node.Target);
        List<string> details = [];

        if (this.IsRouteDestination(node))
        {
            details.Add("Current route destination.");
        }

        if (node.Destination != null)
        {
            details.Add(string.Concat(
                "Leads to ",
                node.Destination.SystemName,
                " / ",
                node.Destination.Name));
            details.Add(node.Access.Description);
        }
        else if (node.Departure != null)
        {
            details.Add(node.Access.Description);
        }

        GalaxyAtlasStationInformation? stationInformation = null;
        var dockedPilots = new GalaxyAtlasPilotList(0, []);

        if (node.Target.Kind == GalaxyNavigationTargetKind.Station &&
            this.sector != null &&
            this.catalogSector != null)
        {
            stationInformation = this.stationInformationIndex.Find(
                this.sector,
                this.catalogSector,
                node.Target);
            dockedPilots = this.GetDockedPilots(node.Target);
        }

        return new GalaxyAtlasToolTipContent(
            Token: string.Create(
                CultureInfo.InvariantCulture,
                $"{node.Target.RawObjectType}:{node.Target.Name}:" +
                $"{node.Target.X:0.###}:{node.Target.Y:0.###}:" +
                $"{node.Target.Z:0.###}:" +
                $"{string.Join(",", dockedPilots.Lines)}"),
            Title: name,
            Subtitle: node.IsLandablePlanet
                ? "Landable planet"
                : node.Target.Kind.ToPublicName(),
            Coordinates: string.Create(
                CultureInfo.InvariantCulture,
                $"X {node.Target.X:0.##}   Y {node.Target.Y:0.##}   Z {node.Target.Z:0.##}"),
            DetailLines: details,
            StationInformation: stationInformation,
            ForgeOverlayInformation: null,
            DockedPilotLines: dockedPilots.Lines,
            DockedPilotCount: dockedPilots.TotalCount,
            ActionText: node.Destination != null
                ? "Left-click to browse the connected sector · Right-click to set destination"
                : "Right-click to set destination");
    }

    private GalaxyAtlasToolTipContent BuildToolTip(
        GalaxyAtlasForgeOverlayNode node)
    {
        var information = node.Information;

        return new GalaxyAtlasToolTipContent(
            Token: information.Id,
            Title: information.Title,
            Subtitle: information.Subtitle,
            Coordinates: string.Create(
                CultureInfo.InvariantCulture,
                $"X {information.CenterX:0.##}   Y {information.CenterY:0.##}   Z {information.CenterZ:0.##}"),
            DetailLines: information.DetailLines,
            StationInformation: null,
            ForgeOverlayInformation: information,
            DockedPilotLines: [],
            DockedPilotCount: 0,
            ActionText: information.RouteDescription.Replace(
                "Route",
                "Right-click to route",
                StringComparison.Ordinal));
    }

    private void DrawDestinationMarker(
        Graphics graphics,
        PointF center,
        float radius)
    {
        var outerRadius = radius + 8.0f;
        var cornerLength = 5.0f;

        using var glowPen = new Pen(
            Color.FromArgb(72, destinationColor),
            4.0f);
        using var markerPen = new Pen(
            destinationColor,
            1.8f);

        graphics.DrawEllipse(
            glowPen,
            center.X - outerRadius,
            center.Y - outerRadius,
            outerRadius * 2.0f,
            outerRadius * 2.0f);

        graphics.DrawLine(
            markerPen,
            center.X - outerRadius,
            center.Y - outerRadius,
            center.X - outerRadius + cornerLength,
            center.Y - outerRadius);
        graphics.DrawLine(
            markerPen,
            center.X - outerRadius,
            center.Y - outerRadius,
            center.X - outerRadius,
            center.Y - outerRadius + cornerLength);
        graphics.DrawLine(
            markerPen,
            center.X + outerRadius,
            center.Y - outerRadius,
            center.X + outerRadius - cornerLength,
            center.Y - outerRadius);
        graphics.DrawLine(
            markerPen,
            center.X + outerRadius,
            center.Y - outerRadius,
            center.X + outerRadius,
            center.Y - outerRadius + cornerLength);
        graphics.DrawLine(
            markerPen,
            center.X - outerRadius,
            center.Y + outerRadius,
            center.X - outerRadius + cornerLength,
            center.Y + outerRadius);
        graphics.DrawLine(
            markerPen,
            center.X - outerRadius,
            center.Y + outerRadius,
            center.X - outerRadius,
            center.Y + outerRadius - cornerLength);
        graphics.DrawLine(
            markerPen,
            center.X + outerRadius,
            center.Y + outerRadius,
            center.X + outerRadius - cornerLength,
            center.Y + outerRadius);
        graphics.DrawLine(
            markerPen,
            center.X + outerRadius,
            center.Y + outerRadius,
            center.X + outerRadius,
            center.Y + outerRadius - cornerLength);
    }

    private bool IsRouteDestination(GalaxyAtlasNode node)
    {
        return this.activeDestination is
        {
            Kind: NavigationDestinationKind.Target,
            TargetKey: not null,
        } destination &&
               string.Equals(
                   destination.SectorKey,
                   this.sector?.Key,
                   StringComparison.Ordinal) &&
               string.Equals(
                   destination.TargetKey,
                   node.TargetDestination.TargetKey,
                   StringComparison.Ordinal);
    }

    private void TargetContextMenu_OnItemClicked(
        object? sender,
        ToolStripItemClickedEventArgs e)
    {
        if (!ReferenceEquals(
                e.ClickedItem,
                this.setDestinationMenuItem))
        {
            return;
        }

        var destination = this.contextDestination;
        this.contextDestination = null;

        if (destination == null)
        {
            return;
        }

        this.DestinationRequested?.Invoke(
            this,
            new GalaxyAtlasDestinationRequestedEventArgs(
                destination));
    }

    private Color GetNodeColor(GalaxyAtlasNode node)
    {
        if (node.Departure != null)
        {
            return node.Access.State switch
            {
                GalaxyAtlasAccessState.Accessible => accessibleColor,
                GalaxyAtlasAccessState.Blocked => blockedColor,
                GalaxyAtlasAccessState.Conditional => conditionalColor,
                GalaxyAtlasAccessState.Unavailable => unavailableColor,
                _ => unavailableColor,
            };
        }

        return node.Target.Kind switch
        {
            GalaxyNavigationTargetKind.Planet => planetColor,
            GalaxyNavigationTargetKind.Station => stationColor,
            GalaxyNavigationTargetKind.NavigationPoint => navigationPointColor,
            GalaxyNavigationTargetKind.Asteroid => asteroidColor,
            GalaxyNavigationTargetKind.Unknown => unknownColor,
            GalaxyNavigationTargetKind.SectorGate => unknownColor,
            _ => unknownColor,
        };
    }

    private static float GetNodeRadius(
        GalaxyNavigationTargetKind kind)
    {
        return kind switch
        {
            GalaxyNavigationTargetKind.Planet => 7.5f,
            GalaxyNavigationTargetKind.SectorGate => 6.5f,
            GalaxyNavigationTargetKind.Station => 6.0f,
            GalaxyNavigationTargetKind.Asteroid => 4.5f,
            GalaxyNavigationTargetKind.NavigationPoint => 3.2f,
            GalaxyNavigationTargetKind.Unknown => 4.0f,
            _ => 4.0f,
        };
    }

    private static int GetDrawingPriority(
        GalaxyNavigationTargetKind kind)
    {
        return kind switch
        {
            GalaxyNavigationTargetKind.NavigationPoint => 0,
            GalaxyNavigationTargetKind.Asteroid => 1,
            GalaxyNavigationTargetKind.Planet => 2,
            GalaxyNavigationTargetKind.Station => 3,
            GalaxyNavigationTargetKind.SectorGate => 4,
            GalaxyNavigationTargetKind.Unknown => 5,
            _ => 5,
        };
    }

    private static int GetLabelPriority(
        GalaxyNavigationTargetKind kind)
    {
        return kind switch
        {
            GalaxyNavigationTargetKind.SectorGate => 4,
            GalaxyNavigationTargetKind.Station => 3,
            GalaxyNavigationTargetKind.Planet => 2,
            GalaxyNavigationTargetKind.Asteroid => 1,
            GalaxyNavigationTargetKind.NavigationPoint => 0,
            GalaxyNavigationTargetKind.Unknown => 1,
            _ => 0,
        };
    }

    private sealed record GalaxyAtlasForgeOverlayNode
    {
        public required GalaxyAtlasForgeOverlayInformation Information
        { get; init; }

        public PointF Point { get; init; }

        public RectangleF HitBounds { get; init; }

        public float FieldRadius { get; init; }
    }

    private sealed record GalaxyAtlasPilotMarkerNode
    {
        public required string Key { get; init; }

        public required IReadOnlyList<GalaxyAtlasDisplayedPilot> Pilots
        { get; init; }

        public PointF Point { get; init; }

        public RectangleF HitBounds { get; init; }
    }

    private sealed record GalaxyAtlasToolTipContent(
        string Token,
        string Title,
        string Subtitle,
        string Coordinates,
        IReadOnlyList<string> DetailLines,
        GalaxyAtlasStationInformation? StationInformation,
        GalaxyAtlasForgeOverlayInformation? ForgeOverlayInformation,
        IReadOnlyList<string> DockedPilotLines,
        int DockedPilotCount,
        string ActionText);

    private readonly record struct ToolTipMetrics(
        int Width,
        int Height,
        int LineHeight,
        int TitleHeight,
        int LeftColumnWidth,
        int RightColumnWidth);
}
