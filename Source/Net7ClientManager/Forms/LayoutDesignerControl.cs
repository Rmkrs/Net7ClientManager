namespace Net7ClientManager.Forms;

using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Runtime.InteropServices;
using Net7ClientManager.Models;
using Net7ClientManager.Win32;

public sealed class LayoutDesignerControl : Control
{
    private const int SnapThreshold = 20;
    private const float MinimumZoomFactor = 0.50f;
    private const float MaximumZoomFactor = 4.00f;
    private const float ZoomStepFactor = 1.12f;
    private const float MonitorGap = 48.0f;

    private static readonly Color backgroundColor = Color.FromArgb(red: 244, green: 247, blue: 251);
    private static readonly Color monitorColor = Color.FromArgb(red: 232, green: 237, blue: 245);
    private static readonly Color primaryMonitorColor = Color.FromArgb(red: 226, green: 235, blue: 248);
    private static readonly Color selectedMonitorColor = Color.FromArgb(red: 215, green: 231, blue: 255);
    private static readonly Color monitorBorderColor = Color.FromArgb(red: 176, green: 188, blue: 204);
    private static readonly Color selectedMonitorBorderColor = Color.FromArgb(red: 58, green: 123, blue: 213);
    private static readonly Color slotColor = Color.FromArgb(red: 255, green: 255, blue: 255);
    private static readonly Color slotAssignedColor = Color.FromArgb(red: 232, green: 245, blue: 255);
    private static readonly Color slotSelectedColor = Color.FromArgb(red: 58, green: 123, blue: 213);
    private static readonly Color slotBorderColor = Color.FromArgb(red: 168, green: 181, blue: 198);
    private static readonly Color snapGuideColor = Color.FromArgb(red: 58, green: 123, blue: 213);
    private static readonly Color textColor = Color.FromArgb(red: 28, green: 35, blue: 45);
    private static readonly Color mutedTextColor = Color.FromArgb(red: 86, green: 99, blue: 116);

    private static readonly Color inputRiskMonitorColor = Color.FromArgb(red: 255, green: 246, blue: 222);
    private static readonly Color inputRiskMonitorBorderColor = Color.FromArgb(red: 210, green: 153, blue: 36);
    private static readonly Color inputRiskSlotColor = Color.FromArgb(red: 255, green: 252, blue: 240);
    private static readonly Color inputRiskSlotBorderColor = Color.FromArgb(red: 210, green: 153, blue: 36);
    private static readonly Color inputRiskTextColor = Color.FromArgb(red: 150, green: 92, blue: 0);

    private readonly Dictionary<Guid, RectangleF> renderedSlotRectangles = [];
    private readonly List<RenderedMonitor> renderedMonitors = [];
    private readonly List<SnapGuide> activeSnapGuides = [];

    private IReadOnlyList<DisplayMonitor>? displayMonitors;

    private LayoutProfile? profile;
    private Guid? currentProfileId;
    private IReadOnlyCollection<ClientInstance> clients = [];
    private string? selectedMonitorDeviceName;

    private bool isDraggingSlot;
    private float dragSlotMouseOffsetRatioX;
    private float dragSlotMouseOffsetRatioY;

    private bool isPanning;
    private Point panStartScreenPoint;
    private PointF panStartOffset;

    private float zoomFactor = 1.0f;
    private PointF panOffset;

    public LayoutDesignerControl()
    {
        this.DoubleBuffered = true;
        this.SetStyle(ControlStyles.ResizeRedraw, value: true);
        this.SetStyle(ControlStyles.Selectable, value: true);
        this.BackColor = backgroundColor;
        this.MinimumSize = new Size(width: 700, height: 420);
    }

    public event EventHandler<SelectedSlotChangedEventArgs>? SelectedSlotChanged;

    public event EventHandler<SlotBoundsChangedEventArgs>? SlotBoundsChanged;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public LayoutProfile? Profile
    {
        get => this.profile;
        set
        {
            var nextProfileId = value?.Id;
            var profileChanged = this.currentProfileId != nextProfileId;

            this.profile = value;
            this.currentProfileId = nextProfileId;

            if (this.SelectedSlot != null
                && this.profile?.Slots.TrueForAll(slot => slot.Id != this.SelectedSlot.Id) == true)
            {
                this.SelectedSlot = null;
                this.SelectedSlotChanged?.Invoke(this, new SelectedSlotChangedEventArgs(this.SelectedSlot));
            }

            if (profileChanged)
            {
                this.FitToView();
                return;
            }

            this.Invalidate();
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IReadOnlyCollection<ClientInstance> Clients
    {
        get => this.clients;
        set
        {
            this.clients = value;
            this.Invalidate();
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ClientSlot? SelectedSlot { get; private set; }

    public void SelectSlot(ClientSlot? slot)
    {
        if (this.SelectedSlot?.Id == slot?.Id)
        {
            return;
        }

        this.SelectedSlot = slot;

        if (slot != null)
        {
            var layoutMonitors = this.BuildMonitorLayout();
            var monitor = this.FindBestLayoutMonitorForBounds(slot.Bounds, layoutMonitors);
            this.selectedMonitorDeviceName = monitor?.Monitor.DeviceName;
        }

        this.SelectedSlotChanged?.Invoke(this, new SelectedSlotChangedEventArgs(this.SelectedSlot));
        this.Invalidate();
    }

    public WindowBounds CreateDefaultSlotBounds()
    {
        var monitors = this.GetDisplayMonitors();

        var monitor = this.GetSelectedDisplayMonitor()
                      ?? FirstOrNull(monitors, monitor => monitor.IsPrimary)
                      ?? FirstOrNull(monitors, _ => true);

        var monitorBounds = monitor?.RealBounds
                            ?? Screen.PrimaryScreen?.Bounds
                            ?? SystemInformation.VirtualScreen;

        var inset = Math.Min(40, Math.Max(16, monitorBounds.Width / 20));
        var width = Math.Min(1280, Math.Max(640, monitorBounds.Width / 2));
        var height = Math.Min(720, Math.Max(480, monitorBounds.Height / 2));

        return new WindowBounds
        {
            Left = monitorBounds.Left + inset,
            Top = monitorBounds.Top + inset,
            Width = width,
            Height = height,
        };
    }

    private void FitToView()
    {
        this.zoomFactor = 1.0f;
        this.panOffset = PointF.Empty;
        this.Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        this.Focus();

        if (e.Button == MouseButtons.Middle)
        {
            this.BeginPan(e.Location);
            return;
        }

        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        var clickedSlot = this.FindSlotAt(e.Location);

        if (clickedSlot != null)
        {
            this.SelectSlot(clickedSlot);
            this.BeginSlotDrag(clickedSlot, e.Location);
            return;
        }

        var clickedMonitor = this.FindMonitorAt(e.Location);

        if (clickedMonitor != null)
        {
            this.selectedMonitorDeviceName = clickedMonitor.Value.Monitor.Monitor.DeviceName;
        }

        this.SelectSlot(slot: null);
        this.Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (this.isPanning)
        {
            this.PanTo(e.Location);
            return;
        }

        if (this.isDraggingSlot && this.SelectedSlot != null)
        {
            this.MoveSelectedSlot(e.Location);
            return;
        }

        if (this.FindSlotAt(e.Location) != null)
        {
            this.Cursor = Cursors.SizeAll;
            return;
        }

        this.Cursor = this.FindMonitorAt(e.Location) == null
            ? Cursors.Default
            : Cursors.Hand;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (this.isPanning && e.Button == MouseButtons.Middle)
        {
            this.isPanning = false;
            this.Capture = false;
            this.Cursor = Cursors.Default;
            return;
        }

        if (!this.isDraggingSlot || this.SelectedSlot == null || e.Button != MouseButtons.Left)
        {
            return;
        }

        this.isDraggingSlot = false;
        this.Capture = false;
        this.Cursor = Cursors.SizeAll;
        this.activeSnapGuides.Clear();

        var layoutMonitors = this.BuildMonitorLayout();
        var monitor = this.FindBestLayoutMonitorForBounds(this.SelectedSlot.Bounds, layoutMonitors);
        this.selectedMonitorDeviceName = monitor?.Monitor.DeviceName;

        this.SlotBoundsChanged?.Invoke(this, new SlotBoundsChangedEventArgs(this.SelectedSlot));
        this.Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);

        var layoutMonitors = this.BuildMonitorLayout();

        if (layoutMonitors.Count == 0)
        {
            return;
        }

        var oldZoomFactor = this.zoomFactor;
        var zoomDirection = e.Delta > 0 ? ZoomStepFactor : 1 / ZoomStepFactor;
        var newZoomFactor = Math.Clamp(oldZoomFactor * zoomDirection, MinimumZoomFactor, MaximumZoomFactor);

        if (Math.Abs(newZoomFactor - oldZoomFactor) < 0.001f)
        {
            return;
        }

        var viewWorldBounds = this.GetViewWorldBounds(layoutMonitors);
        var baseScreenBounds = this.GetBaseCanvasScreenBounds(viewWorldBounds);
        var viewPointUnderMouse = this.ScreenToViewWorld(e.Location, viewWorldBounds, baseScreenBounds, oldZoomFactor, this.panOffset);

        this.zoomFactor = newZoomFactor;

        var screenPointAfterZoom = this.ViewWorldToScreen(viewPointUnderMouse, viewWorldBounds, baseScreenBounds, this.zoomFactor, this.panOffset);

        this.panOffset = new PointF(
            x: this.panOffset.X + e.Location.X - screenPointAfterZoom.X,
            y: this.panOffset.Y + e.Location.Y - screenPointAfterZoom.Y);

        this.Invalidate();
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);

        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        if (this.FindSlotAt(e.Location) != null)
        {
            return;
        }

        this.FitToView();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);

        if (!this.isDraggingSlot && !this.isPanning)
        {
            this.Cursor = Cursors.Default;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        e.Graphics.Clear(backgroundColor);

        this.renderedSlotRectangles.Clear();
        this.renderedMonitors.Clear();

        var layoutMonitors = this.BuildMonitorLayout();

        if (layoutMonitors.Count == 0)
        {
            this.DrawEmptyState(e.Graphics);
            return;
        }

        var viewWorldBounds = this.GetViewWorldBounds(layoutMonitors);
        var screenBounds = this.GetCanvasScreenBounds(viewWorldBounds);

        this.DrawMonitors(e.Graphics, layoutMonitors, viewWorldBounds, screenBounds);

        if (this.profile != null)
        {
            foreach (var slot in this.profile.Slots)
            {
                var slotRectangle = this.ProjectSlotToScreen(slot, layoutMonitors, viewWorldBounds, screenBounds);
                this.renderedSlotRectangles[slot.Id] = slotRectangle;

                var assignedClient = this.clients.FirstOrDefault(client => client.AssignedSlotId == slot.Id);
                var isSelected = this.SelectedSlot?.Id == slot.Id;

                this.DrawSlot(e.Graphics, slot, assignedClient, slotRectangle, isSelected);
            }
        }

        this.DrawSnapGuides(e.Graphics, viewWorldBounds, screenBounds);
        this.DrawZoomHint(e.Graphics);
    }

    private static DisplayMonitor? FirstOrNull(
        IEnumerable<DisplayMonitor> monitors,
        Func<DisplayMonitor, bool> predicate)
    {
        foreach (var monitor in monitors)
        {
            if (predicate(monitor))
            {
                return monitor;
            }
        }

        return null;
    }

    private ClientSlot? FindSlotAt(Point location)
    {
        foreach (var renderedSlot in this.renderedSlotRectangles.Reverse())
        {
            if (!renderedSlot.Value.Contains(location.X, location.Y))
            {
                continue;
            }

            return this.profile?.Slots.FirstOrDefault(slot => slot.Id == renderedSlot.Key);
        }

        return null;
    }

    private RenderedMonitor? FindMonitorAt(Point location)
    {
        foreach (var renderedMonitor in this.renderedMonitors.AsEnumerable().Reverse())
        {
            if (renderedMonitor.ScreenBounds.Contains(location.X, location.Y))
            {
                return renderedMonitor;
            }
        }

        return null;
    }

    private void BeginSlotDrag(ClientSlot slot, Point mouseLocation)
    {
        var layoutMonitors = this.BuildMonitorLayout();
        var viewWorldBounds = this.GetViewWorldBounds(layoutMonitors);
        var screenBounds = this.GetCanvasScreenBounds(viewWorldBounds);
        var slotViewBounds = this.ProjectSlotToView(slot, layoutMonitors);
        var mouseViewPoint = this.ScreenToViewWorld(mouseLocation, viewWorldBounds, this.GetBaseCanvasScreenBounds(viewWorldBounds), this.zoomFactor, this.panOffset);

        this.dragSlotMouseOffsetRatioX = slotViewBounds.Width <= 0
            ? 0.5f
            : Math.Clamp((mouseViewPoint.X - slotViewBounds.Left) / slotViewBounds.Width, 0, 1);

        this.dragSlotMouseOffsetRatioY = slotViewBounds.Height <= 0
            ? 0.5f
            : Math.Clamp((mouseViewPoint.Y - slotViewBounds.Top) / slotViewBounds.Height, 0, 1);

        _ = screenBounds;

        this.isDraggingSlot = true;
        this.Capture = true;
        this.Cursor = Cursors.SizeAll;
    }

    private void BeginPan(Point mouseLocation)
    {
        this.isPanning = true;
        this.panStartScreenPoint = mouseLocation;
        this.panStartOffset = this.panOffset;
        this.Capture = true;
        this.Cursor = Cursors.Hand;
    }

    private void PanTo(Point mouseLocation)
    {
        this.panOffset = new PointF(
            x: this.panStartOffset.X + mouseLocation.X - this.panStartScreenPoint.X,
            y: this.panStartOffset.Y + mouseLocation.Y - this.panStartScreenPoint.Y);

        this.Invalidate();
    }

    private void MoveSelectedSlot(Point mouseLocation)
    {
        if (this.SelectedSlot == null)
        {
            return;
        }

        var layoutMonitors = this.BuildMonitorLayout();

        if (layoutMonitors.Count == 0)
        {
            return;
        }

        var viewWorldBounds = this.GetViewWorldBounds(layoutMonitors);
        var baseScreenBounds = this.GetBaseCanvasScreenBounds(viewWorldBounds);
        var mouseViewPoint = this.ScreenToViewWorld(mouseLocation, viewWorldBounds, baseScreenBounds, this.zoomFactor, this.panOffset);

        var targetMonitor = this.FindLayoutMonitorAtViewPoint(layoutMonitors, mouseViewPoint)
                            ?? this.FindNearestLayoutMonitor(layoutMonitors, mouseViewPoint);

        if (targetMonitor == null)
        {
            return;
        }

        var slotViewSize = this.GetSlotViewSize(this.SelectedSlot, targetMonitor.Value);

        var proposedViewBounds = new RectangleF(
            x: mouseViewPoint.X - slotViewSize.Width * this.dragSlotMouseOffsetRatioX,
            y: mouseViewPoint.Y - slotViewSize.Height * this.dragSlotMouseOffsetRatioY,
            width: slotViewSize.Width,
            height: slotViewSize.Height);

        var screenBounds = this.GetCanvasScreenBounds(viewWorldBounds);
        var horizontalThreshold = SnapThreshold * viewWorldBounds.Width / screenBounds.Width;
        var verticalThreshold = SnapThreshold * viewWorldBounds.Height / screenBounds.Height;

        var snappedViewBounds = this.SnapViewBounds(
            this.SelectedSlot,
            proposedViewBounds,
            layoutMonitors,
            horizontalThreshold,
            verticalThreshold);

        this.ApplyViewBoundsToSlot(this.SelectedSlot, snappedViewBounds, targetMonitor.Value);
        this.selectedMonitorDeviceName = targetMonitor.Value.Monitor.DeviceName;

        this.Invalidate();
    }

    private RectangleF SnapViewBounds(
        ClientSlot movingSlot,
        RectangleF proposedBounds,
        IReadOnlyList<LayoutMonitor> layoutMonitors,
        float horizontalThreshold,
        float verticalThreshold)
    {
        this.activeSnapGuides.Clear();

        proposedBounds.X = this.SnapHorizontal(movingSlot, proposedBounds, layoutMonitors, horizontalThreshold);
        proposedBounds.Y = this.SnapVertical(movingSlot, proposedBounds, layoutMonitors, verticalThreshold);

        return proposedBounds;
    }

    private float SnapHorizontal(
        ClientSlot movingSlot,
        RectangleF proposedBounds,
        IReadOnlyList<LayoutMonitor> layoutMonitors,
        float threshold)
    {
        var bestDelta = 0.0f;
        var bestDistance = threshold + 1.0f;
        float? guideX = null;

        foreach (var targetX in this.GetHorizontalSnapTargets(movingSlot, layoutMonitors))
        {
            var candidates = new[]
            {
            targetX - proposedBounds.Left,
            targetX - proposedBounds.Right,
        };

            foreach (var delta in candidates)
            {
                var distance = Math.Abs(delta);

                if (distance >= bestDistance)
                {
                    continue;
                }

                bestDistance = distance;
                bestDelta = delta;
                guideX = targetX;
            }
        }

        if (guideX == null)
        {
            return proposedBounds.Left;
        }

        this.activeSnapGuides.Add(SnapGuide.Vertical(guideX.Value));

        return proposedBounds.Left + bestDelta;
    }

    private float SnapVertical(
        ClientSlot movingSlot,
        RectangleF proposedBounds,
        IReadOnlyList<LayoutMonitor> layoutMonitors,
        float threshold)
    {
        var bestDelta = 0.0f;
        var bestDistance = threshold + 1.0f;
        float? guideY = null;

        foreach (var targetY in this.GetVerticalSnapTargets(movingSlot, layoutMonitors))
        {
            var candidates = new[]
            {
            targetY - proposedBounds.Top,
            targetY - proposedBounds.Bottom,
        };

            foreach (var delta in candidates)
            {
                var distance = Math.Abs(delta);

                if (distance >= bestDistance)
                {
                    continue;
                }

                bestDistance = distance;
                bestDelta = delta;
                guideY = targetY;
            }
        }

        if (guideY == null)
        {
            return proposedBounds.Top;
        }

        this.activeSnapGuides.Add(SnapGuide.Horizontal(guideY.Value));

        return proposedBounds.Top + bestDelta;
    }

    private IEnumerable<float> GetHorizontalSnapTargets(ClientSlot movingSlot, IReadOnlyList<LayoutMonitor> layoutMonitors)
    {
        foreach (var monitor in layoutMonitors)
        {
            yield return monitor.ViewBounds.Left;
            yield return monitor.ViewBounds.Right;
        }

        if (this.profile == null)
        {
            yield break;
        }

        foreach (var slot in this.profile.Slots)
        {
            if (slot.Id == movingSlot.Id)
            {
                continue;
            }

            var slotBounds = this.ProjectSlotToView(slot, layoutMonitors);

            yield return slotBounds.Left;
            yield return slotBounds.Right;
        }
    }

    private IEnumerable<float> GetVerticalSnapTargets(ClientSlot movingSlot, IReadOnlyList<LayoutMonitor> layoutMonitors)
    {
        foreach (var monitor in layoutMonitors)
        {
            yield return monitor.ViewBounds.Top;
            yield return monitor.ViewBounds.Bottom;
        }

        if (this.profile == null)
        {
            yield break;
        }

        foreach (var slot in this.profile.Slots)
        {
            if (slot.Id == movingSlot.Id)
            {
                continue;
            }

            var slotBounds = this.ProjectSlotToView(slot, layoutMonitors);

            yield return slotBounds.Top;
            yield return slotBounds.Bottom;
        }
    }

    private void DrawMonitors(
        Graphics graphics,
        IReadOnlyList<LayoutMonitor> layoutMonitors,
        RectangleF viewWorldBounds,
        RectangleF screenBounds)
    {
        foreach (var layoutMonitor in layoutMonitors)
        {
            var monitorScreenBounds = this.ProjectViewToScreen(layoutMonitor.ViewBounds, viewWorldBounds, screenBounds);
            var isSelected = string.Equals(this.selectedMonitorDeviceName, layoutMonitor.Monitor.DeviceName, StringComparison.Ordinal);
            var hasInputRisk = HasInputRisk(layoutMonitor.Monitor);

            var fillColor = hasInputRisk
                ? inputRiskMonitorColor
                : isSelected
                    ? selectedMonitorColor
                    : layoutMonitor.Monitor.IsPrimary
                        ? primaryMonitorColor
                        : monitorColor;

            var borderColor = hasInputRisk
                ? inputRiskMonitorBorderColor
                : isSelected
                    ? selectedMonitorBorderColor
                    : monitorBorderColor;

            using var monitorBrush = new SolidBrush(fillColor);
            using var borderPen = new Pen(borderColor, isSelected ? 2.5f : 1.5f);

            graphics.FillRoundedRectangle(monitorBrush, monitorScreenBounds, radius: 14);
            graphics.DrawRoundedRectangle(borderPen, monitorScreenBounds, radius: 14);

            this.DrawMonitorLabel(graphics, layoutMonitor.Monitor, monitorScreenBounds);

            this.renderedMonitors.Add(new RenderedMonitor(layoutMonitor, monitorScreenBounds));
        }
    }

    private void DrawMonitorLabel(Graphics graphics, DisplayMonitor monitor, RectangleF bounds)
    {
        var logicalSize = string.Create(
            CultureInfo.InvariantCulture,
            $"{monitor.RealBounds.Width}×{monitor.RealBounds.Height} effective");

        var physicalSize = monitor.PhysicalSize == null
            ? null
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{monitor.PhysicalSize.Value.Width}×{monitor.PhysicalSize.Value.Height}");

        var label = physicalSize == null ? $"{monitor.DisplayName} · {logicalSize}" : $"{monitor.DisplayName} · {physicalSize} · {logicalSize}";

        if (monitor.IsPrimary)
        {
            label = string.Concat(label, " · Primary");
        }

        if (HasInputRisk(monitor))
        {
            label = string.Concat(label, " · Input risk");
        }

        using var labelFont = new Font(this.Font.FontFamily, emSize: 9, FontStyle.Regular);

        var labelBounds = Rectangle.Round(new RectangleF(
                                              x: bounds.Left + 14,
                                              y: bounds.Top + 10,
                                              width: Math.Max(1, bounds.Width - 28),
                                              height: 22));

        TextRenderer.DrawText(
            graphics,
            label,
            labelFont,
            labelBounds,
            mutedTextColor,
            TextFormatFlags.Left
            | TextFormatFlags.Top
            | TextFormatFlags.EndEllipsis
            | TextFormatFlags.NoPrefix
            | TextFormatFlags.SingleLine);
    }

    private void DrawSnapGuides(Graphics graphics, RectangleF viewWorldBounds, RectangleF screenBounds)
    {
        if (this.activeSnapGuides.Count == 0)
        {
            return;
        }

        using var guidePen = new Pen(snapGuideColor, width: 1.5f);
        guidePen.DashStyle = DashStyle.Dash;

        foreach (var guide in this.activeSnapGuides)
        {
            if (guide.Orientation == SnapGuideOrientation.Vertical)
            {
                var x = screenBounds.Left
                        + (guide.Position - viewWorldBounds.Left)
                        * screenBounds.Width
                        / viewWorldBounds.Width;

                graphics.DrawLine(guidePen, x, screenBounds.Top, x, screenBounds.Bottom);
                continue;
            }

            var y = screenBounds.Top
                    + (guide.Position - viewWorldBounds.Top)
                    * screenBounds.Height
                    / viewWorldBounds.Height;

            graphics.DrawLine(guidePen, screenBounds.Left, y, screenBounds.Right, y);
        }
    }

    private void DrawEmptyState(Graphics graphics)
    {
        using var font = new Font(this.Font.FontFamily, emSize: 12, FontStyle.Regular);
        using var brush = new SolidBrush(mutedTextColor);

        const string text = "No displays detected.";
        var size = graphics.MeasureString(text, font);

        graphics.DrawString(
            text,
            font,
            brush,
            (this.Width - size.Width) / 2,
            (this.Height - size.Height) / 2);
    }

    private RectangleF ProjectSlotToScreen(
        ClientSlot slot,
        IReadOnlyList<LayoutMonitor> layoutMonitors,
        RectangleF viewWorldBounds,
        RectangleF screenBounds)
    {
        var viewBounds = this.ProjectSlotToView(slot, layoutMonitors);
        return this.ProjectViewToScreen(viewBounds, viewWorldBounds, screenBounds);
    }

    private RectangleF ProjectSlotToView(ClientSlot slot, IReadOnlyList<LayoutMonitor> layoutMonitors)
    {
        var monitor = this.FindBestLayoutMonitorForBounds(slot.Bounds, layoutMonitors)
                      ?? this.FindSelectedLayoutMonitor(layoutMonitors)
                      ?? layoutMonitors[0];

        var realBounds = new Rectangle(
            x: slot.Bounds.Left,
            y: slot.Bounds.Top,
            width: slot.Bounds.Width,
            height: slot.Bounds.Height);

        return this.ProjectRealToView(realBounds, monitor);
    }

    private SizeF GetSlotViewSize(ClientSlot slot, LayoutMonitor monitor)
    {
        return new SizeF(
            width: slot.Bounds.Width * monitor.ViewBounds.Width / monitor.Monitor.RealBounds.Width,
            height: slot.Bounds.Height * monitor.ViewBounds.Height / monitor.Monitor.RealBounds.Height);
    }

    private RectangleF ProjectRealToView(Rectangle realBounds, LayoutMonitor monitor)
    {
        var x = monitor.ViewBounds.Left
                + (realBounds.Left - monitor.Monitor.RealBounds.Left)
                * monitor.ViewBounds.Width
                / monitor.Monitor.RealBounds.Width;

        var y = monitor.ViewBounds.Top
                + (realBounds.Top - monitor.Monitor.RealBounds.Top)
                * monitor.ViewBounds.Height
                / monitor.Monitor.RealBounds.Height;

        var width = realBounds.Width * monitor.ViewBounds.Width / monitor.Monitor.RealBounds.Width;
        var height = realBounds.Height * monitor.ViewBounds.Height / monitor.Monitor.RealBounds.Height;

        return new RectangleF(x, y, width, height);
    }

    private void ApplyViewBoundsToSlot(ClientSlot slot, RectangleF viewBounds, LayoutMonitor monitor)
    {
        slot.Bounds.Left = monitor.Monitor.RealBounds.Left
                           + (int)Math.Round(
                               (viewBounds.Left - monitor.ViewBounds.Left)
                               * monitor.Monitor.RealBounds.Width
                               / monitor.ViewBounds.Width);

        slot.Bounds.Top = monitor.Monitor.RealBounds.Top
                          + (int)Math.Round(
                              (viewBounds.Top - monitor.ViewBounds.Top)
                              * monitor.Monitor.RealBounds.Height
                              / monitor.ViewBounds.Height);
    }

    private IReadOnlyList<LayoutMonitor> BuildMonitorLayout()
    {
        var monitors = this.GetDisplayMonitors()
            .OrderBy(monitor => monitor.RealBounds.Left)
            .ThenBy(monitor => monitor.RealBounds.Top)
            .ToList();

        var result = new List<LayoutMonitor>();

        if (monitors.Count == 0)
        {
            return result;
        }

        var minimumRealTop = monitors.Min(monitor => monitor.RealBounds.Top);
        var nextLeft = 0.0f;

        foreach (var monitor in monitors)
        {
            var viewSize = this.GetMonitorViewSize(monitor);
            var verticalScale = monitor.PhysicalSize == null
                ? 1.0f
                : monitor.PhysicalSize.Value.Height / (float)monitor.RealBounds.Height;

            var viewTop = (monitor.RealBounds.Top - minimumRealTop) * verticalScale;

            var viewBounds = new RectangleF(
                x: nextLeft,
                y: viewTop,
                width: viewSize.Width,
                height: viewSize.Height);

            result.Add(new LayoutMonitor(monitor, viewBounds));

            nextLeft += viewBounds.Width + MonitorGap;
        }

        return result;
    }

    private SizeF GetMonitorViewSize(DisplayMonitor monitor)
    {
        if (monitor.PhysicalSize != null)
        {
            return new SizeF(
                width: monitor.PhysicalSize.Value.Width,
                height: monitor.PhysicalSize.Value.Height);
        }

        return new SizeF(
            width: monitor.RealBounds.Width,
            height: monitor.RealBounds.Height);
    }

    private RectangleF GetViewWorldBounds(IReadOnlyList<LayoutMonitor> layoutMonitors)
    {
        var bounds = layoutMonitors[0].ViewBounds;

        foreach (var monitor in layoutMonitors.Skip(1))
        {
            bounds = RectangleF.Union(bounds, monitor.ViewBounds);
        }

        return bounds;
    }

    private RectangleF GetBaseCanvasScreenBounds(RectangleF viewWorldBounds)
    {
        const int canvasPadding = 28;

        var availableWidth = Math.Max(1, this.ClientSize.Width - canvasPadding * 2);
        var availableHeight = Math.Max(1, this.ClientSize.Height - canvasPadding * 2);

        var scaleX = availableWidth / viewWorldBounds.Width;
        var scaleY = availableHeight / viewWorldBounds.Height;
        var scale = Math.Min(scaleX, scaleY);

        var width = viewWorldBounds.Width * scale;
        var height = viewWorldBounds.Height * scale;

        return new RectangleF(
            x: (this.ClientSize.Width - width) / 2,
            y: (this.ClientSize.Height - height) / 2,
            width: width,
            height: height);
    }

    private RectangleF GetCanvasScreenBounds(RectangleF viewWorldBounds)
    {
        var baseBounds = this.GetBaseCanvasScreenBounds(viewWorldBounds);
        var centerX = baseBounds.Left + baseBounds.Width / 2;
        var centerY = baseBounds.Top + baseBounds.Height / 2;

        var zoomedWidth = baseBounds.Width * this.zoomFactor;
        var zoomedHeight = baseBounds.Height * this.zoomFactor;

        return new RectangleF(
            x: centerX - zoomedWidth / 2 + this.panOffset.X,
            y: centerY - zoomedHeight / 2 + this.panOffset.Y,
            width: zoomedWidth,
            height: zoomedHeight);
    }

    private PointF ScreenToViewWorld(
        PointF screenPoint,
        RectangleF viewWorldBounds,
        RectangleF baseScreenBounds,
        float currentZoomFactor,
        PointF currentPanOffset)
    {
        var centerX = baseScreenBounds.Left + baseScreenBounds.Width / 2;
        var centerY = baseScreenBounds.Top + baseScreenBounds.Height / 2;

        var zoomedWidth = baseScreenBounds.Width * currentZoomFactor;
        var zoomedHeight = baseScreenBounds.Height * currentZoomFactor;

        var screenBounds = new RectangleF(
            x: centerX - zoomedWidth / 2 + currentPanOffset.X,
            y: centerY - zoomedHeight / 2 + currentPanOffset.Y,
            width: zoomedWidth,
            height: zoomedHeight);

        return new PointF(
            x: viewWorldBounds.Left + (screenPoint.X - screenBounds.Left) * viewWorldBounds.Width / screenBounds.Width,
            y: viewWorldBounds.Top + (screenPoint.Y - screenBounds.Top) * viewWorldBounds.Height / screenBounds.Height);
    }

    private PointF ViewWorldToScreen(
        PointF viewPoint,
        RectangleF viewWorldBounds,
        RectangleF baseScreenBounds,
        float currentZoomFactor,
        PointF currentPanOffset)
    {
        var centerX = baseScreenBounds.Left + baseScreenBounds.Width / 2;
        var centerY = baseScreenBounds.Top + baseScreenBounds.Height / 2;

        var zoomedWidth = baseScreenBounds.Width * currentZoomFactor;
        var zoomedHeight = baseScreenBounds.Height * currentZoomFactor;

        var screenBounds = new RectangleF(
            x: centerX - zoomedWidth / 2 + currentPanOffset.X,
            y: centerY - zoomedHeight / 2 + currentPanOffset.Y,
            width: zoomedWidth,
            height: zoomedHeight);

        return new PointF(
            x: screenBounds.Left + (viewPoint.X - viewWorldBounds.Left) * screenBounds.Width / viewWorldBounds.Width,
            y: screenBounds.Top + (viewPoint.Y - viewWorldBounds.Top) * screenBounds.Height / viewWorldBounds.Height);
    }

    private RectangleF ProjectViewToScreen(RectangleF viewBounds, RectangleF viewWorldBounds, RectangleF screenBounds)
    {
        var scaleX = screenBounds.Width / viewWorldBounds.Width;
        var scaleY = screenBounds.Height / viewWorldBounds.Height;

        return new RectangleF(
            x: screenBounds.Left + (viewBounds.Left - viewWorldBounds.Left) * scaleX,
            y: screenBounds.Top + (viewBounds.Top - viewWorldBounds.Top) * scaleY,
            width: viewBounds.Width * scaleX,
            height: viewBounds.Height * scaleY);
    }

    private LayoutMonitor? FindLayoutMonitorAtViewPoint(IReadOnlyList<LayoutMonitor> layoutMonitors, PointF viewPoint)
    {
        foreach (var monitor in layoutMonitors)
        {
            if (monitor.ViewBounds.Contains(viewPoint.X, viewPoint.Y))
            {
                return monitor;
            }
        }

        return null;
    }

    private LayoutMonitor? FindNearestLayoutMonitor(IReadOnlyList<LayoutMonitor> layoutMonitors, PointF viewPoint)
    {
        if (layoutMonitors.Count == 0)
        {
            return null;
        }

        return layoutMonitors
            .OrderBy(monitor => GetDistanceToRectangle(viewPoint, monitor.ViewBounds))
            .First();
    }

    private LayoutMonitor? FindBestLayoutMonitorForBounds(WindowBounds bounds, IReadOnlyList<LayoutMonitor> layoutMonitors)
    {
        if (layoutMonitors.Count == 0)
        {
            return null;
        }

        var slotRectangle = new Rectangle(
            x: bounds.Left,
            y: bounds.Top,
            width: bounds.Width,
            height: bounds.Height);

        return layoutMonitors
            .OrderByDescending(monitor => GetIntersectionArea(monitor.Monitor.RealBounds, slotRectangle))
            .ThenBy(monitor => GetDistanceToRectangle(GetRectangleCenter(slotRectangle), monitor.Monitor.RealBounds))
            .First();
    }

    private LayoutMonitor? FindSelectedLayoutMonitor(IReadOnlyList<LayoutMonitor> layoutMonitors)
    {
        if (this.selectedMonitorDeviceName == null)
        {
            return null;
        }

        return layoutMonitors.FirstOrDefault(monitor => string.Equals(monitor.Monitor.DeviceName, this.selectedMonitorDeviceName, StringComparison.Ordinal));
    }

    private DisplayMonitor? GetSelectedDisplayMonitor()
    {
        if (this.selectedMonitorDeviceName == null)
        {
            return null;
        }

        return this.GetDisplayMonitors().FirstOrDefault(monitor => string.Equals(monitor.DeviceName, this.selectedMonitorDeviceName, StringComparison.Ordinal));
    }

    private IReadOnlyList<DisplayMonitor> GetDisplayMonitors()
    {
        this.displayMonitors ??= [.. Screen.AllScreens
            .Select(screen =>
            {
                var physicalSize = NativeMethods.GetPhysicalDisplaySize(screen.DeviceName);

                return new DisplayMonitor(
                    DisplayName: GetFriendlyDisplayName(screen.DeviceName),
                    DeviceName: screen.DeviceName,
                    RealBounds: screen.Bounds,
                    PhysicalSize: physicalSize,
                    IsPrimary: screen.Primary);
            })];

        return this.displayMonitors;
    }

    private static string GetFriendlyDisplayName(string deviceName)
    {
        const string prefix = @"\\.\DISPLAY";

        if (!deviceName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return deviceName;
        }

        return string.Concat("Display ", deviceName[prefix.Length..]);
    }

    private static int GetIntersectionArea(Rectangle first, Rectangle second)
    {
        var intersection = Rectangle.Intersect(first, second);

        if (intersection.IsEmpty)
        {
            return 0;
        }

        return intersection.Width * intersection.Height;
    }

    private static PointF GetRectangleCenter(Rectangle rectangle)
    {
        return new PointF(
            x: rectangle.Left + rectangle.Width / 2.0f,
            y: rectangle.Top + rectangle.Height / 2.0f);
    }

    private static float GetDistanceToRectangle(PointF point, Rectangle rectangle)
    {
        return GetDistanceToRectangle(
            point,
            new RectangleF(
                x: rectangle.Left,
                y: rectangle.Top,
                width: rectangle.Width,
                height: rectangle.Height));
    }

    private static float GetDistanceToRectangle(PointF point, RectangleF rectangle)
    {
        var nearestX = Math.Clamp(point.X, rectangle.Left, rectangle.Right);
        var nearestY = Math.Clamp(point.Y, rectangle.Top, rectangle.Bottom);

        var deltaX = point.X - nearestX;
        var deltaY = point.Y - nearestY;

        return MathF.Sqrt(deltaX * deltaX + deltaY * deltaY);
    }

    private void DrawZoomHint(Graphics graphics)
    {
        var zoomText = string.Create(CultureInfo.InvariantCulture, $"Zoom {this.zoomFactor:P0} · Wheel to zoom · Middle-drag to pan · Double-click empty space to fit");

        using var font = new Font(this.Font.FontFamily, emSize: 8, FontStyle.Regular);
        using var brush = new SolidBrush(mutedTextColor);

        var size = graphics.MeasureString(zoomText, font);

        graphics.DrawString(
            zoomText,
            font,
            brush,
            this.ClientSize.Width - size.Width - 12,
            this.ClientSize.Height - size.Height - 10);
    }

    private void DrawSlot(
        Graphics graphics,
        ClientSlot slot,
        ClientInstance? assignedClient,
        RectangleF bounds,
        bool isSelected)
    {
        var hasInputRisk = HasInputRisk(slot.Bounds);

        var fillColor = hasInputRisk
            ? inputRiskSlotColor
            : assignedClient == null
                ? slotColor
                : slotAssignedColor;

        var borderColor = isSelected
            ? slotSelectedColor
            : hasInputRisk
                ? inputRiskSlotBorderColor
                : slotBorderColor;

        var borderWidth = isSelected ? 3 : hasInputRisk ? 2 : 1;

        using var fillBrush = new SolidBrush(fillColor);
        using var borderPen = new Pen(borderColor, borderWidth);

        graphics.FillRoundedRectangle(fillBrush, bounds, radius: 8);
        graphics.DrawRoundedRectangle(borderPen, bounds, radius: 8);

        if (this.zoomFactor < 0.60f)
        {
            return;
        }

        // draw title

        if (this.zoomFactor < 0.85f)
        {
            return;
        }

        // draw account / PID / input risk
        using var titleFont = new Font(this.Font.FontFamily, emSize: 9, FontStyle.Bold);
        using var detailFont = new Font(this.Font.FontFamily, emSize: 8, FontStyle.Regular);
        using var textBrush = new SolidBrush(textColor);
        using var mutedBrush = new SolidBrush(mutedTextColor);
        using var riskBrush = new SolidBrush(inputRiskTextColor);

        var x = bounds.Left + 10;
        var y = bounds.Top + 8;

        graphics.DrawString(slot.Name, titleFont, textBrush, x, y);

        y += 20;

        var accountText = string.IsNullOrWhiteSpace(slot.AccountName)
            ? "No account"
            : slot.AccountName;

        graphics.DrawString(accountText, detailFont, mutedBrush, x, y);

        y += 18;

        var clientText = assignedClient == null
            ? "Empty"
            : string.Create(CultureInfo.InvariantCulture, $"PID {assignedClient.ProcessId}");

        graphics.DrawString(clientText, detailFont, hasInputRisk ? riskBrush : mutedBrush, x, y);

        if (!hasInputRisk)
        {
            return;
        }

        y += 18;

        graphics.DrawString("Input risk", detailFont, riskBrush, x, y);
    }

    private static bool HasInputRisk(WindowBounds bounds)
    {
        return bounds.Left < 0;
    }

    private static bool HasInputRisk(DisplayMonitor monitor)
    {
        return monitor.RealBounds.Left < 0;
    }

    private readonly record struct RenderedMonitor(LayoutMonitor Monitor, RectangleF ScreenBounds);

    private readonly record struct LayoutMonitor(DisplayMonitor Monitor, RectangleF ViewBounds);

    private readonly record struct DisplayMonitor(
        string DisplayName,
        string DeviceName,
        Rectangle RealBounds,
        Size? PhysicalSize,
        bool IsPrimary);

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct SnapGuide(SnapGuideOrientation Orientation, float Position)
    {
        public static SnapGuide Vertical(float position)
        {
            return new SnapGuide(SnapGuideOrientation.Vertical, position);
        }

        public static SnapGuide Horizontal(float position)
        {
            return new SnapGuide(SnapGuideOrientation.Horizontal, position);
        }
    }

    private enum SnapGuideOrientation
    {
        Vertical,
        Horizontal,
    }
}
