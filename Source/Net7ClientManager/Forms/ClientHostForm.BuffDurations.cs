namespace Net7ClientManager.Forms;

using Net7ClientManager.Models;
using Net7ClientManager.Observations;

public sealed partial class ClientHostForm
{
    private BuffDurationOverlayForm? buffDurationOverlayForm;
    private GameBuffOverlayPresentation buffDurationPresentation =
        GameBuffOverlayPresentation.Empty;
    private bool showBuffDurations;

    private BuffDurationOverlayForm? ActiveBuffDurationOverlayForm =>
        this.buffDurationOverlayForm is
        {
            IsDisposed: false,
            Disposing: false,
        } form
            ? form
            : null;

    public void SetShowBuffDurations(bool enabled)
    {
        if (this.IsDisposed || this.Disposing)
        {
            return;
        }

        if (this.IsHandleCreated && this.InvokeRequired)
        {
            try
            {
                this.BeginInvoke(
                    () => this.SetShowBuffDurations(enabled));
            }
            catch (InvalidOperationException)
            {
            }

            return;
        }

        this.showBuffDurations = enabled;

        if (!enabled)
        {
            this.ClearBuffDurationPresentation();
            return;
        }

        this.SyncBuffDurationOverlay();
    }

    public void UpdateBuffDurationPresentation(
        GameBuffOverlayPresentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        if (this.IsDisposed || this.Disposing)
        {
            return;
        }

        if (this.IsHandleCreated && this.InvokeRequired)
        {
            try
            {
                this.BeginInvoke(
                    () => this.UpdateBuffDurationPresentation(
                        presentation));
            }
            catch (InvalidOperationException)
            {
            }

            return;
        }

        this.buffDurationPresentation = presentation;
        this.ActiveBuffDurationOverlayForm?.SetPresentation(
            presentation);
        this.SyncBuffDurationOverlay();
    }

    private void ClearBuffDurationPresentation()
    {
        this.buffDurationPresentation =
            GameBuffOverlayPresentation.Empty;
        var form = this.ActiveBuffDurationOverlayForm;
        form?.SetPresentation(this.buffDurationPresentation);
        form?.HideToolTip();
        form?.Hide();
    }

    private void SyncBuffDurationOverlay()
    {
        var shouldShow =
            this.showBuffDurations &&
            this.buffDurationPresentation.Buffs.Count != 0 &&
            this.addonLifecycleState == ClientLifecycleState.InGame &&
            !this.addonTransitioning &&
            this.Visible &&
            this.WindowState != FormWindowState.Minimized &&
            this.gamePanel.ClientSize is { Width: > 0, Height: > 0 };

        if (!shouldShow)
        {
            var form = this.ActiveBuffDurationOverlayForm;
            form?.HideToolTip();
            form?.Hide();
            return;
        }

        this.EnsureBuffDurationOverlayForm();

        var screenLocation = this.gamePanel.PointToScreen(Point.Empty);
        this.buffDurationOverlayForm!.Bounds = new Rectangle(
            screenLocation,
            this.gamePanel.ClientSize);

        if (!this.buffDurationOverlayForm.Visible)
        {
            this.buffDurationOverlayForm.Show(this);
        }

        this.buffDurationOverlayForm.Invalidate();
    }

    private void EnsureBuffDurationOverlayForm()
    {
        if (this.buffDurationOverlayForm is
            {
                IsDisposed: false,
                Disposing: false,
            })
        {
            return;
        }

        this.buffDurationOverlayForm = new BuffDurationOverlayForm();
        this.buffDurationOverlayForm.SetPresentation(
            this.buffDurationPresentation);
    }

    private void CloseBuffDurationOverlayForm()
    {
        var form = this.buffDurationOverlayForm;
        this.buffDurationOverlayForm = null;

        if (form is not
            {
                IsDisposed: false,
                Disposing: false,
            })
        {
            return;
        }

        form.HideToolTip();
        form.Close();
        form.Dispose();
    }
}
