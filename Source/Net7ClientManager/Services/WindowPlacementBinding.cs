namespace Net7ClientManager.Services;

using Net7ClientManager.Models;

public sealed class WindowPlacementBinding : IDisposable
{
    private readonly Form form;
    private readonly Func<int?, WindowPlacementContext?>
        resolveContext;
    private readonly Func<string, SavedWindowPlacement?>
        loadPlacement;
    private readonly Action<string, SavedWindowPlacement>
        savePlacement;
    private readonly bool centerOnPreferredScreenWhenMissing;
    private readonly Size defaultSize;

    private WindowPlacementContext? context;
    private Rectangle lastNormalBounds;
    private FormWindowState lastNonMinimizedState =
        FormWindowState.Normal;
    private bool restoring;
    private bool disposed;

    internal WindowPlacementBinding(
        Form form,
        Func<int?, WindowPlacementContext?> resolveContext,
        Func<string, SavedWindowPlacement?> loadPlacement,
        Action<string, SavedWindowPlacement> savePlacement,
        int? initialProcessId,
        bool centerOnPreferredScreenWhenMissing)
    {
        this.form = form ??
                    throw new ArgumentNullException(nameof(form));
        this.resolveContext = resolveContext ??
                              throw new ArgumentNullException(
                                  nameof(resolveContext));
        this.loadPlacement = loadPlacement ??
                             throw new ArgumentNullException(
                                 nameof(loadPlacement));
        this.savePlacement = savePlacement ??
                             throw new ArgumentNullException(
                                 nameof(savePlacement));
        this.centerOnPreferredScreenWhenMissing =
            centerOnPreferredScreenWhenMissing;
        this.defaultSize = form.Size;
        this.lastNormalBounds = form.Bounds;

        form.Move += this.Form_OnMoveOrResize;
        form.Resize += this.Form_OnMoveOrResize;
        form.FormClosing += this.Form_OnFormClosing;

        this.Rebind(initialProcessId, saveCurrent: false);
    }

    public void Rebind(int? processId)
    {
        this.Rebind(processId, saveCurrent: true);
    }

    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;
        this.form.Move -= this.Form_OnMoveOrResize;
        this.form.Resize -= this.Form_OnMoveOrResize;
        this.form.FormClosing -= this.Form_OnFormClosing;
    }

    private void Rebind(
        int? processId,
        bool saveCurrent)
    {
        if (this.disposed)
        {
            return;
        }

        var nextContext = this.resolveContext(processId);

        if (string.Equals(
                this.context?.Key,
                nextContext?.Key,
                StringComparison.Ordinal))
        {
            this.context = nextContext;
            return;
        }

        if (saveCurrent)
        {
            this.SaveCurrent();
        }

        this.context = nextContext;

        if (nextContext == null)
        {
            return;
        }

        this.restoring = true;

        try
        {
            var placement = this.loadPlacement(nextContext.Key);

            if (placement != null)
            {
                WindowPlacementService.Restore(
                    this.form,
                    placement,
                    nextContext.PreferredScreen);
            }
            else if (this.centerOnPreferredScreenWhenMissing &&
                     nextContext.PreferredScreen != null)
            {
                WindowPlacementService.CenterDefault(
                    this.form,
                    nextContext.PreferredScreen,
                    this.defaultSize);
            }
        }
        finally
        {
            this.restoring = false;
            this.UpdateTrackedState();
        }
    }

    private void Form_OnMoveOrResize(
        object? sender,
        EventArgs e)
    {
        if (!this.restoring)
        {
            this.UpdateTrackedState();
        }
    }

    private void Form_OnFormClosing(
        object? sender,
        FormClosingEventArgs e)
    {
        this.SaveCurrent();
    }

    private void UpdateTrackedState()
    {
        switch (this.form.WindowState)
        {
            case FormWindowState.Normal:
                this.lastNormalBounds = this.form.Bounds;
                this.lastNonMinimizedState =
                    FormWindowState.Normal;
                break;

            case FormWindowState.Maximized:
                this.lastNonMinimizedState =
                    FormWindowState.Maximized;
                break;
        }
    }

    private void SaveCurrent()
    {
        if (this.restoring || this.context == null)
        {
            return;
        }

        var placement = WindowPlacementService.Capture(
            this.form,
            this.lastNormalBounds,
            this.lastNonMinimizedState);

        this.savePlacement(
            this.context.Key,
            placement);
    }
}
