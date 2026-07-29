namespace Net7ClientManager.Forms;

internal sealed class ControlGuidancePulse : IDisposable
{
    private const int PulseIntervalMilliseconds = 300;
    private const int PulseTickCount = 12;
    private const int ComboBoxOutlinePadding = 3;

    private static readonly HashSet<ControlGuidancePulse> activePulses = [];

    private readonly Control target;
    private readonly Form? ownerForm;
    private readonly Control? outlineHost;
    private readonly System.Windows.Forms.Timer timer;
    private readonly Color originalBackColor;
    private readonly Color originalForeColor;
    private readonly Color? originalButtonBorderColor;

    private int ticks;
    private bool highlighted;
    private bool disposed;

    private ControlGuidancePulse(Control target)
    {
        this.target = target;
        this.ownerForm = target.FindForm();
        this.outlineHost = target is ComboBox
            ? target.Parent
            : null;
        this.originalBackColor = target.BackColor;
        this.originalForeColor = target.ForeColor;
        this.originalButtonBorderColor = target is Button button
            ? button.FlatAppearance.BorderColor
            : null;

        this.timer = new System.Windows.Forms.Timer
        {
            Interval = PulseIntervalMilliseconds,
        };
        this.timer.Tick += this.Timer_OnTick;

        if (this.ownerForm != null)
        {
            this.ownerForm.FormClosed += this.OwnerForm_OnClosed;
        }

        if (this.outlineHost != null)
        {
            this.outlineHost.Paint += this.OutlineHost_OnPaint;
        }
    }

    public static void Start(Control target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (target.IsDisposed)
        {
            return;
        }

        var pulse = new ControlGuidancePulse(target);
        activePulses.Add(pulse);
        pulse.StartCore();
    }

    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;
        this.timer.Stop();
        this.timer.Tick -= this.Timer_OnTick;
        this.timer.Dispose();

        if (this.ownerForm != null)
        {
            this.ownerForm.FormClosed -= this.OwnerForm_OnClosed;
        }

        if (this.outlineHost != null)
        {
            this.outlineHost.Paint -= this.OutlineHost_OnPaint;
        }

        this.RestoreAppearance();
        activePulses.Remove(this);
    }

    private void StartCore()
    {
        _ = this.target.Focus();
        this.ApplyHighlight();
        this.timer.Start();
    }

    private void Timer_OnTick(object? sender, EventArgs e)
    {
        if (this.target.IsDisposed ||
            this.ownerForm is { IsDisposed: true })
        {
            this.Dispose();
            return;
        }

        this.ticks++;

        if (this.ticks >= PulseTickCount)
        {
            this.Dispose();
            return;
        }

        if (this.highlighted)
        {
            this.RestoreAppearance();
        }
        else
        {
            this.ApplyHighlight();
        }
    }

    private void ApplyHighlight()
    {
        this.highlighted = true;

        switch (this.target)
        {
            case Button button:
                button.BackColor = Color.FromArgb(72, 57, 20);
                button.ForeColor = MainWindowTheme.Warning;
                button.FlatAppearance.BorderColor = MainWindowTheme.Warning;
                break;

            case ComboBox:
                this.InvalidateComboBoxOutline();
                break;

            case CheckBox:
                this.target.ForeColor = MainWindowTheme.Warning;
                break;

            default:
                this.target.BackColor = Color.FromArgb(72, 57, 20);
                this.target.ForeColor = MainWindowTheme.Warning;
                break;
        }

        this.target.Invalidate();
    }

    private void RestoreAppearance()
    {
        if (this.target.IsDisposed)
        {
            return;
        }

        this.highlighted = false;
        this.target.BackColor = this.originalBackColor;
        this.target.ForeColor = this.originalForeColor;

        if (this.target is Button button &&
            this.originalButtonBorderColor is { } borderColor)
        {
            button.FlatAppearance.BorderColor = borderColor;
        }

        this.target.Invalidate();
        this.InvalidateComboBoxOutline();
    }

    private void InvalidateComboBoxOutline()
    {
        if (this.outlineHost == null ||
            this.outlineHost.IsDisposed)
        {
            return;
        }

        this.outlineHost.Invalidate(this.GetComboBoxOutlineBounds());
    }

    private Rectangle GetComboBoxOutlineBounds()
    {
        var bounds = this.target.Bounds;
        bounds.Inflate(
            ComboBoxOutlinePadding,
            ComboBoxOutlinePadding);
        return bounds;
    }

    private void OutlineHost_OnPaint(object? sender, PaintEventArgs e)
    {
        if (!this.highlighted ||
            this.target.IsDisposed)
        {
            return;
        }

        var bounds = this.GetComboBoxOutlineBounds();
        bounds.Width = Math.Max(0, bounds.Width - 1);
        bounds.Height = Math.Max(0, bounds.Height - 1);

        using var pen = new Pen(MainWindowTheme.Warning, 2.0f);
        e.Graphics.DrawRectangle(pen, bounds);
    }

    private void OwnerForm_OnClosed(object? sender, FormClosedEventArgs e)
    {
        this.Dispose();
    }
}
