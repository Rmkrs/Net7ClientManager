// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using System.ComponentModel;

internal sealed partial class GalaxyAtlasCanvas
{
    private const float DefaultThreeDimensionalYaw = -0.45f;
    private const float DefaultThreeDimensionalPitch = 0.58f;
    private const float ThreeDimensionalOrbitSensitivity = 0.008f;
    private const float MaximumThreeDimensionalPitch = 1.31f;

    private bool useThreeDimensionalView;
    private float threeDimensionalYaw = DefaultThreeDimensionalYaw;
    private float threeDimensionalPitch = DefaultThreeDimensionalPitch;
    private MouseButtons orbitButton = MouseButtons.None;
    private Point orbitStartLocation;
    private float orbitStartYaw;
    private float orbitStartPitch;
    private bool isOrbiting;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool UseThreeDimensionalView
    {
        get => this.useThreeDimensionalView;
        set
        {
            if (this.useThreeDimensionalView == value)
            {
                return;
            }

            this.useThreeDimensionalView = value;
            this.ResetViewCore();
            this.Invalidate();
        }
    }

    private GalaxyAtlasProjectionContext CreateAtlasProjection(
        IReadOnlyCollection<GalaxyAtlasWorldPoint> coordinates,
        RectangleF mapBounds)
    {
        return this.UseThreeDimensionalView
            ? this.CreateThreeDimensionalProjection(coordinates, mapBounds)
            : this.CreateTwoDimensionalProjection(coordinates, mapBounds);
    }

    private GalaxyAtlasProjectionContext CreateTwoDimensionalProjection(
        IReadOnlyCollection<GalaxyAtlasWorldPoint> coordinates,
        RectangleF mapBounds)
    {
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

        GalaxyAtlasProjectedPoint Project(float x, float y, float z)
        {
            var basePoint = new PointF(
                offsetX + ((x - minimumX) * scale),
                offsetY + ((maximumY - y) * scale));

            return new GalaxyAtlasProjectedPoint(
                this.ApplyViewTransform(basePoint, mapCenter),
                Depth: z);
        }

        return new GalaxyAtlasProjectionContext(scale, Project);
    }

    private GalaxyAtlasProjectionContext CreateThreeDimensionalProjection(
        IReadOnlyCollection<GalaxyAtlasWorldPoint> coordinates,
        RectangleF mapBounds)
    {
        var minimumX = coordinates.Min(point => point.X);
        var maximumX = coordinates.Max(point => point.X);
        var minimumY = coordinates.Min(point => point.Y);
        var maximumY = coordinates.Max(point => point.Y);
        var minimumZ = coordinates.Min(point => point.Z);
        var maximumZ = coordinates.Max(point => point.Z);
        var centerX = (minimumX + maximumX) * 0.5f;
        var centerY = (minimumY + maximumY) * 0.5f;
        var centerZ = (minimumZ + maximumZ) * 0.5f;
        var radius = coordinates.Max(point =>
        {
            var deltaX = point.X - centerX;
            var deltaY = point.Y - centerY;
            var deltaZ = point.Z - centerZ;
            return MathF.Sqrt(
                (deltaX * deltaX) +
                (deltaY * deltaY) +
                (deltaZ * deltaZ));
        });
        var paddedDiameter = Math.Max(radius * 2.32f, 1000.0f);
        var scale = Math.Min(
            mapBounds.Width / paddedDiameter,
            mapBounds.Height / paddedDiameter);
        var mapCenter = GetRectangleCenter(mapBounds);

        GalaxyAtlasProjectedPoint Project(float x, float y, float z)
        {
            var point = this.RotateThreeDimensionalPoint(
                x - centerX,
                y - centerY,
                z - centerZ);
            var basePoint = new PointF(
                mapCenter.X + (point.Horizontal * scale),
                mapCenter.Y - (point.Vertical * scale));

            return new GalaxyAtlasProjectedPoint(
                this.ApplyViewTransform(basePoint, mapCenter),
                point.Depth);
        }

        return new GalaxyAtlasProjectionContext(scale, Project);
    }

    private GalaxyAtlasRotatedPoint RotateThreeDimensionalPoint(
        float x,
        float y,
        float z)
    {
        var cosYaw = MathF.Cos(this.threeDimensionalYaw);
        var sinYaw = MathF.Sin(this.threeDimensionalYaw);
        var yawX = (x * cosYaw) - (y * sinYaw);
        var yawY = (x * sinYaw) + (y * cosYaw);
        var cosPitch = MathF.Cos(this.threeDimensionalPitch);
        var sinPitch = MathF.Sin(this.threeDimensionalPitch);

        return new GalaxyAtlasRotatedPoint(
            Horizontal: yawX,
            Vertical: (yawY * cosPitch) + (z * sinPitch),
            Depth: (-yawY * sinPitch) + (z * cosPitch));
    }

    private void BeginOrbit(Point location)
    {
        if (!this.UseThreeDimensionalView)
        {
            return;
        }

        this.orbitButton = MouseButtons.Left;
        this.orbitStartLocation = location;
        this.orbitStartYaw = this.threeDimensionalYaw;
        this.orbitStartPitch = this.threeDimensionalPitch;
        this.isOrbiting = false;
        this.pressedNode = null;
        this.ClearHoverForViewGesture();
        this.Capture = true;
    }

    private bool UpdateOrbit(MouseEventArgs e)
    {
        if (this.orbitButton == MouseButtons.None)
        {
            return false;
        }

        if ((Control.MouseButtons & this.orbitButton) == MouseButtons.None)
        {
            this.EndOrbit();
            return false;
        }

        var deltaX = e.X - this.orbitStartLocation.X;
        var deltaY = e.Y - this.orbitStartLocation.Y;

        if (!this.isOrbiting &&
            ((deltaX * deltaX) + (deltaY * deltaY)) >=
            PanDragThreshold * PanDragThreshold)
        {
            this.isOrbiting = true;
        }

        if (!this.isOrbiting)
        {
            return true;
        }

        this.threeDimensionalYaw =
            this.orbitStartYaw +
            (deltaX * ThreeDimensionalOrbitSensitivity);
        this.threeDimensionalPitch = Math.Clamp(
            this.orbitStartPitch -
            (deltaY * ThreeDimensionalOrbitSensitivity),
            -MaximumThreeDimensionalPitch,
            MaximumThreeDimensionalPitch);
        this.nodes.Clear();
        this.overlayNodes.Clear();
        this.pilotMarkerNodes.Clear();
        this.Cursor = Cursors.SizeAll;
        this.Invalidate();
        return true;
    }

    private bool EndOrbit()
    {
        if (this.orbitButton == MouseButtons.None)
        {
            return false;
        }

        var wasOrbiting = this.isOrbiting;
        this.orbitButton = MouseButtons.None;
        this.isOrbiting = false;
        this.Capture = false;
        this.Cursor = Cursors.Default;
        return wasOrbiting;
    }

    private void ResetThreeDimensionalViewCore()
    {
        this.threeDimensionalYaw = DefaultThreeDimensionalYaw;
        this.threeDimensionalPitch = DefaultThreeDimensionalPitch;
        this.orbitButton = MouseButtons.None;
        this.isOrbiting = false;
    }

    private void ClearHoverForViewGesture()
    {
        this.hoveredTarget = null;
        this.hoveredOverlay = null;
        this.hoveredOverlayKey = null;
        this.hoveredPilotMarker = null;
        this.hoveredPilotMarkerKey = null;
        this.hoveredToolTipContent = null;
        this.HideAtlasToolTip();
    }

    private readonly record struct GalaxyAtlasWorldPoint(
        float X,
        float Y,
        float Z);

    private readonly record struct GalaxyAtlasProjectedPoint(
        PointF Point,
        float Depth);

    private readonly record struct GalaxyAtlasRotatedPoint(
        float Horizontal,
        float Vertical,
        float Depth);

    private sealed record GalaxyAtlasProjectionContext(
        float Scale,
        Func<float, float, float, GalaxyAtlasProjectedPoint> Project);
}
