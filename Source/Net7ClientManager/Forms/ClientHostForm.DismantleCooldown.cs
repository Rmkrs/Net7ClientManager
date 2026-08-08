namespace Net7ClientManager.Forms;

using System.Globalization;
using Net7ClientManager.Observations;
using Net7ClientManager.Observations.Models;

public sealed partial class ClientHostForm
{
    private const int DismantleMode = 3;
    private readonly System.Windows.Forms.Timer dismantleReadyHideTimer = new()
    {
        Interval = 1750,
    };

    private DismantleCooldownOverlayForm? dismantleCooldownOverlayForm;
    private string dismantleCooldownText = "";
    private bool dismantleCooldownReady;
    private bool dismantleAttemptObserved;
    private bool dismantleCooldownObserved;
    private DateTimeOffset lastDismantlePacingObservedAt =
        DateTimeOffset.MinValue;
    private readonly object dismantlePacingUiSync = new();
    private ClientManufacturingActivityObservation?
        pendingDismantlePacingActivity;
    private bool dismantlePacingUiPostPending;

    private DismantleCooldownOverlayForm? ActiveDismantleCooldownOverlayForm =>
        this.dismantleCooldownOverlayForm is
        {
            IsDisposed: false,
            Disposing: false,
        } form
            ? form
            : null;

    public void UpdateDismantleCooldownPresentation(
        ClientManufacturingActivityObservation activity)
    {
        ArgumentNullException.ThrowIfNull(activity);

        if (this.IsDisposed || this.Disposing)
        {
            return;
        }

        if (this.IsHandleCreated && this.InvokeRequired)
        {
            var shouldPost = false;
            lock (this.dismantlePacingUiSync)
            {
                this.pendingDismantlePacingActivity = activity;
                if (!this.dismantlePacingUiPostPending)
                {
                    this.dismantlePacingUiPostPending = true;
                    shouldPost = true;
                }
            }

            if (shouldPost)
            {
                try
                {
                    this.BeginInvoke((Action)this.FlushPendingDismantlePacingActivity);
                }
                catch (InvalidOperationException)
                {
                    lock (this.dismantlePacingUiSync)
                    {
                        this.dismantlePacingUiPostPending = false;
                        this.pendingDismantlePacingActivity = null;
                    }
                }
            }

            return;
        }

        if (activity.ObservedAt < this.lastDismantlePacingObservedAt)
        {
            return;
        }

        this.lastDismantlePacingObservedAt = activity.ObservedAt;

        var isDismantlePanelState =
            activity.IsAvailable &&
            activity.IsAnalyzePanelActive &&
            activity.IsAnalyzeUiPacingAvailable &&
            activity.Mode == DismantleMode;

        if (!isDismantlePanelState)
        {
            this.ResetDismantleCooldownPresentation();
            return;
        }

        // +0x110 is set to 1 by ExecuteCurrentAnalyzeOrDismantle before the
        // action packet is queued. Seeing that phase is our proof that this
        // particular native cooldown belongs to an attempt we observed, not
        // an old result or some unrelated UI timer reuse.
        if (activity.IsAnalyzeUiAttemptInProgress)
        {
            this.dismantleReadyHideTimer.Stop();
            this.dismantleAttemptObserved = true;
            this.dismantleCooldownObserved = false;
            this.dismantleCooldownReady = false;
            this.dismantleCooldownText = "";
            this.SyncDismantleCooldownOverlay();
            return;
        }

        if (activity.AnalyzeUiDelayRemainingDeciseconds > 0)
        {
            // We only enter the visible post-result countdown after this
            // dedicated native sampler saw +0x110=1 for the same Dismantle
            // attempt. That cleanly distinguishes the first 2-second attempt
            // delay from the second 2-second post-result lockout.
            if (!this.dismantleAttemptObserved &&
                !this.dismantleCooldownObserved)
            {
                return;
            }

            this.dismantleReadyHideTimer.Stop();
            this.dismantleCooldownObserved = true;
            this.dismantleCooldownReady = false;
            this.dismantleCooldownText = string.Create(
                CultureInfo.InvariantCulture,
                $"Ready for next item in {activity.AnalyzeUiDelayRemainingDeciseconds / 10.0:0.0}s");
            this.SyncDismantleCooldownOverlay();
            return;
        }

        if (this.dismantleCooldownObserved)
        {
            this.dismantleAttemptObserved = false;
            this.dismantleCooldownObserved = false;
            this.dismantleCooldownReady = true;
            this.dismantleCooldownText = "Ready for next item";
            this.SyncDismantleCooldownOverlay();
            this.dismantleReadyHideTimer.Stop();
            this.dismantleReadyHideTimer.Start();
        }
    }

    private void FlushPendingDismantlePacingActivity()
    {
        ClientManufacturingActivityObservation? activity;
        lock (this.dismantlePacingUiSync)
        {
            activity = this.pendingDismantlePacingActivity;
            this.pendingDismantlePacingActivity = null;
            this.dismantlePacingUiPostPending = false;
        }

        if (activity != null)
        {
            this.UpdateDismantleCooldownPresentation(activity);
        }
    }

    private void DismantleReadyHideTimer_OnTick(object? sender, EventArgs e)
    {
        this.dismantleReadyHideTimer.Stop();
        this.dismantleCooldownText = "";
        this.dismantleCooldownReady = false;
        this.SyncDismantleCooldownOverlay();
    }

    private void ResetDismantleCooldownPresentation()
    {
        this.dismantleReadyHideTimer.Stop();
        this.dismantleAttemptObserved = false;
        this.dismantleCooldownObserved = false;
        this.dismantleCooldownReady = false;
        this.dismantleCooldownText = "";
        var form = this.ActiveDismantleCooldownOverlayForm;
        form?.SetStatus(null, isReady: false);
        form?.Hide();
    }

    private void SyncDismantleCooldownOverlay()
    {
        var shouldShow =
            !string.IsNullOrWhiteSpace(this.dismantleCooldownText) &&
            this.addonLifecycleState == ClientLifecycleState.InGame &&
            !this.addonTransitioning &&
            this.Visible &&
            this.WindowState != FormWindowState.Minimized &&
            this.gamePanel.ClientSize is { Width: > 0, Height: > 0 };

        if (!shouldShow)
        {
            this.ActiveDismantleCooldownOverlayForm?.Hide();
            return;
        }

        this.EnsureDismantleCooldownOverlayForm();

        var screenLocation = this.gamePanel.PointToScreen(Point.Empty);
        this.dismantleCooldownOverlayForm!.Bounds = new Rectangle(
            screenLocation,
            this.gamePanel.ClientSize);
        this.dismantleCooldownOverlayForm.SetStatus(
            this.dismantleCooldownText,
            this.dismantleCooldownReady);

        if (!this.dismantleCooldownOverlayForm.Visible)
        {
            this.dismantleCooldownOverlayForm.Show(this);
        }

        this.dismantleCooldownOverlayForm.Invalidate();
    }

    private void EnsureDismantleCooldownOverlayForm()
    {
        if (this.dismantleCooldownOverlayForm is
            {
                IsDisposed: false,
                Disposing: false,
            })
        {
            return;
        }

        this.dismantleCooldownOverlayForm = new DismantleCooldownOverlayForm();
        this.dismantleCooldownOverlayForm.SetStatus(
            this.dismantleCooldownText,
            this.dismantleCooldownReady);
    }

    private void CloseDismantleCooldownOverlayForm()
    {
        lock (this.dismantlePacingUiSync)
        {
            this.pendingDismantlePacingActivity = null;
            this.dismantlePacingUiPostPending = false;
        }

        this.dismantleReadyHideTimer.Stop();
        this.dismantleReadyHideTimer.Tick -=
            this.DismantleReadyHideTimer_OnTick;
        this.dismantleReadyHideTimer.Dispose();

        var form = this.dismantleCooldownOverlayForm;
        this.dismantleCooldownOverlayForm = null;
        if (form is not
            {
                IsDisposed: false,
                Disposing: false,
            })
        {
            return;
        }

        form.Close();
        form.Dispose();
    }
}
