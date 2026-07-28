namespace Net7ClientManager.Services;

using Net7ClientManager.Models;

internal static class WindowPlacementService
{
    private const int MinimumVisibleTitleWidth = 160;
    private const int AccessibleTitleBarHeight = 40;

    public static void Restore(
        Form form,
        SavedWindowPlacement placement,
        Screen? preferredScreen)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(placement);

        var candidate = CreateCandidateBounds(
            form,
            placement.Bounds);
        var savedMonitor = Screen.AllScreens.FirstOrDefault(screen =>
            string.Equals(
                screen.DeviceName,
                placement.MonitorDeviceName,
                StringComparison.OrdinalIgnoreCase));

        if (savedMonitor != null)
        {
            candidate.Location = new Point(
                savedMonitor.WorkingArea.Left +
                placement.MonitorOffsetLeft,
                savedMonitor.WorkingArea.Top +
                placement.MonitorOffsetTop);
            candidate = ClampToWorkingArea(
                form,
                candidate,
                savedMonitor.WorkingArea);
        }
        else
        {
            var fallbackScreen = HasAccessibleTitleBar(candidate)
                ? Screen.FromRectangle(candidate)
                : preferredScreen ?? Screen.FromRectangle(candidate);

            candidate = ClampToWorkingArea(
                form,
                candidate,
                fallbackScreen.WorkingArea);
        }

        form.WindowState = FormWindowState.Normal;
        form.StartPosition = FormStartPosition.Manual;
        form.Bounds = candidate;

        if (placement.Maximized)
        {
            form.WindowState = FormWindowState.Maximized;
        }
    }

    public static void CenterDefault(
        Form form,
        Screen preferredScreen,
        Size defaultSize)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(preferredScreen);

        var workingArea = preferredScreen.WorkingArea;
        var candidate = ClampToWorkingArea(
            form,
            new Rectangle(
                workingArea.Left +
                Math.Max(0, (workingArea.Width - defaultSize.Width) / 2),
                workingArea.Top +
                Math.Max(0, (workingArea.Height - defaultSize.Height) / 2),
                defaultSize.Width,
                defaultSize.Height),
            workingArea);

        form.StartPosition = FormStartPosition.Manual;
        form.WindowState = FormWindowState.Normal;
        form.Bounds = candidate;
    }

    public static SavedWindowPlacement Capture(
        Form form,
        Rectangle lastNormalBounds,
        FormWindowState lastNonMinimizedState)
    {
        ArgumentNullException.ThrowIfNull(form);

        var bounds = form.WindowState == FormWindowState.Normal
            ? form.Bounds
            : form.RestoreBounds;

        if (!IsUsable(bounds))
        {
            bounds = lastNormalBounds;
        }

        if (!IsUsable(bounds))
        {
            bounds = new Rectangle(
                form.Left,
                form.Top,
                Math.Max(form.MinimumSize.Width, form.Width),
                Math.Max(form.MinimumSize.Height, form.Height));
        }

        var monitor = Screen.FromRectangle(bounds);

        return new SavedWindowPlacement
        {
            Bounds = new WindowBounds
            {
                Left = bounds.Left,
                Top = bounds.Top,
                Width = bounds.Width,
                Height = bounds.Height,
            },
            MonitorDeviceName = monitor.DeviceName,
            MonitorOffsetLeft =
                bounds.Left - monitor.WorkingArea.Left,
            MonitorOffsetTop =
                bounds.Top - monitor.WorkingArea.Top,
            Maximized = lastNonMinimizedState ==
                        FormWindowState.Maximized,
        };
    }

    private static Rectangle CreateCandidateBounds(
        Form form,
        WindowBounds savedBounds)
    {
        var width = Math.Max(
            form.MinimumSize.Width,
            savedBounds.Width);
        var height = Math.Max(
            form.MinimumSize.Height,
            savedBounds.Height);

        if (form.MaximumSize.Width > 0)
        {
            width = Math.Min(width, form.MaximumSize.Width);
        }

        if (form.MaximumSize.Height > 0)
        {
            height = Math.Min(height, form.MaximumSize.Height);
        }

        return new Rectangle(
            savedBounds.Left,
            savedBounds.Top,
            width,
            height);
    }

    private static Rectangle ClampToWorkingArea(
        Form form,
        Rectangle candidate,
        Rectangle workingArea)
    {
        var minimumWidth = Math.Max(1, form.MinimumSize.Width);
        var minimumHeight = Math.Max(1, form.MinimumSize.Height);
        var width = Math.Max(minimumWidth, candidate.Width);
        var height = Math.Max(minimumHeight, candidate.Height);

        if (form.MaximumSize.Width > 0)
        {
            width = Math.Min(width, form.MaximumSize.Width);
        }

        if (form.MaximumSize.Height > 0)
        {
            height = Math.Min(height, form.MaximumSize.Height);
        }

        if (workingArea.Width >= minimumWidth)
        {
            width = Math.Min(width, workingArea.Width);
        }

        if (workingArea.Height >= minimumHeight)
        {
            height = Math.Min(height, workingArea.Height);
        }

        var maximumLeft = Math.Max(
            workingArea.Left,
            workingArea.Right - width);
        var maximumTop = Math.Max(
            workingArea.Top,
            workingArea.Bottom - height);

        return new Rectangle(
            Math.Clamp(
                candidate.Left,
                workingArea.Left,
                maximumLeft),
            Math.Clamp(
                candidate.Top,
                workingArea.Top,
                maximumTop),
            width,
            height);
    }

    private static bool HasAccessibleTitleBar(
        Rectangle candidate)
    {
        var titleBarBounds = new Rectangle(
            candidate.Left,
            candidate.Top,
            candidate.Width,
            Math.Min(
                AccessibleTitleBarHeight,
                candidate.Height));

        return Screen.AllScreens.Any(screen =>
        {
            var visible = Rectangle.Intersect(
                screen.WorkingArea,
                titleBarBounds);

            return visible.Width >= Math.Min(
                       MinimumVisibleTitleWidth,
                       candidate.Width) &&
                   visible.Height >= titleBarBounds.Height;
        });
    }

    private static bool IsUsable(Rectangle bounds)
    {
        return bounds.Width > 0 && bounds.Height > 0;
    }
}
