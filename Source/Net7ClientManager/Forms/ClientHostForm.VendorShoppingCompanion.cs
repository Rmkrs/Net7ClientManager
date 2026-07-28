// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using System.Diagnostics;
using System.Globalization;
using Net7ClientManager.Observations;
using Net7ClientManager.Shopping;

public sealed partial class ClientHostForm
{
    private const int VendorShoppingCompanionBaseCanvasWidth = 1280;
    private const int VendorShoppingCompanionBaseCanvasHeight = 720;
    private const int VendorShoppingCompanionBaseX = 1032;
    private const int VendorShoppingCompanionBaseY = 36;
    private const int VendorShoppingCompanionBaseWidth = 240;
    private const int VendorShoppingCompanionBaseBottom = 198;
    private const int VendorShoppingCompanionMinimumWidth = 232;
    private const int VendorShoppingCompanionMinimumCanvasWidth = 1100;
    private const int VendorShoppingCompanionMinimumCanvasHeight = 540;

    private VendorShoppingCompanionForm?
        vendorShoppingCompanionForm;
    private VendorShoppingCompanionPresentation
        vendorShoppingCompanionPresentation =
            VendorShoppingCompanionPresentation.Hidden;
    private bool vendorShoppingCompanionEnabled = true;

    internal void SetVendorShoppingCompanionEnabled(bool enabled)
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
                    () => this.SetVendorShoppingCompanionEnabled(enabled));
            }
            catch (InvalidOperationException)
            {
            }

            return;
        }

        this.vendorShoppingCompanionEnabled = enabled;
        this.SyncVendorShoppingCompanion();
    }

    internal void UpdateVendorShoppingCompanionPresentation(
        VendorShoppingCompanionPresentation presentation)
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
                    () => this.UpdateVendorShoppingCompanionPresentation(
                        presentation));
            }
            catch (InvalidOperationException)
            {
            }

            return;
        }

        this.vendorShoppingCompanionPresentation = presentation;
        this.SyncVendorShoppingCompanion();
    }

    private void SyncVendorShoppingCompanion()
    {
        if (this.IsDisposed || this.Disposing || !this.IsHandleCreated)
        {
            return;
        }

        if (this.InvokeRequired)
        {
            try
            {
                this.BeginInvoke(this.SyncVendorShoppingCompanion);
            }
            catch (InvalidOperationException)
            {
            }

            return;
        }

        try
        {
            this.ApplyVendorShoppingCompanionState();
        }
        catch (Exception exception)
        {
            Debug.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"[Shopping] Could not synchronize the vendor companion: {exception}"));
            this.TryHideVendorShoppingCompanionForm();
        }
    }

    private void ApplyVendorShoppingCompanionState()
    {
        var shouldPresent =
            this.vendorShoppingCompanionEnabled &&
            this.vendorShoppingCompanionPresentation.IsVisible &&
            this.addonLifecycleState == ClientLifecycleState.InGame &&
            !this.addonTransitioning &&
            this.Visible &&
            this.WindowState != FormWindowState.Minimized &&
            this.gamePanel.ClientSize.Width >=
                VendorShoppingCompanionMinimumCanvasWidth &&
            this.gamePanel.ClientSize.Height >=
                VendorShoppingCompanionMinimumCanvasHeight;

        if (!shouldPresent)
        {
            this.TryHideVendorShoppingCompanionForm();
            return;
        }

        this.EnsureVendorShoppingCompanionForm();
        this.vendorShoppingCompanionForm!.SetPresentation(
            this.vendorShoppingCompanionPresentation);
        var bounds = this.CalculateVendorShoppingCompanionBounds();

        if (this.vendorShoppingCompanionForm.Bounds != bounds)
        {
            this.vendorShoppingCompanionForm.Bounds = bounds;
        }

        if (!this.vendorShoppingCompanionForm.Visible)
        {
            this.vendorShoppingCompanionForm.Show(this);
        }
    }

    private void EnsureVendorShoppingCompanionForm()
    {
        if (this.vendorShoppingCompanionForm is
            {
                IsDisposed: false,
                Disposing: false,
            })
        {
            return;
        }

        this.vendorShoppingCompanionForm =
            new VendorShoppingCompanionForm();
    }

    private Rectangle CalculateVendorShoppingCompanionBounds()
    {
        var scaleX = this.gamePanel.ClientSize.Width /
                     (float)VendorShoppingCompanionBaseCanvasWidth;
        var scaleY = this.gamePanel.ClientSize.Height /
                     (float)VendorShoppingCompanionBaseCanvasHeight;
        var width = Math.Max(
            VendorShoppingCompanionMinimumWidth,
            (int)Math.Round(
                VendorShoppingCompanionBaseWidth * scaleX));
        var x = (int)Math.Round(
            VendorShoppingCompanionBaseX * scaleX);
        var y = (int)Math.Round(
            VendorShoppingCompanionBaseY * scaleY);
        var bottom = (int)Math.Round(
            VendorShoppingCompanionBaseBottom * scaleY);

        width = Math.Min(width, this.gamePanel.ClientSize.Width);
        x = Math.Clamp(
            x,
            0,
            Math.Max(0, this.gamePanel.ClientSize.Width - width));
        y = Math.Clamp(
            y,
            0,
            Math.Max(0, this.gamePanel.ClientSize.Height - 1));
        bottom = Math.Clamp(
            bottom,
            y + 1,
            this.gamePanel.ClientSize.Height);

        var availableHeight = Math.Max(1, bottom - y);
        var height = this.vendorShoppingCompanionForm?.GetPreferredHeight(
                availableHeight) ??
            availableHeight;

        return new Rectangle(
            this.gamePanel.PointToScreen(new Point(x, y)),
            new Size(width, height));
    }

    private void TryHideVendorShoppingCompanionForm()
    {
        var form = this.vendorShoppingCompanionForm;

        if (form == null || form.IsDisposed || form.Disposing)
        {
            return;
        }

        try
        {
            form.Hide();
        }
        catch (Exception exception)
        {
            Debug.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"[Shopping] Could not hide the vendor companion: {exception}"));
        }
    }

    private void CloseVendorShoppingCompanionForm()
    {
        var form = this.vendorShoppingCompanionForm;
        this.vendorShoppingCompanionForm = null;

        if (form == null)
        {
            return;
        }

        try
        {
            if (!form.IsDisposed)
            {
                form.Close();
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"[Shopping] Could not close the vendor companion: {exception}"));
        }

        if (form.IsDisposed)
        {
            return;
        }

        try
        {
            form.Dispose();
        }
        catch (Exception exception)
        {
            Debug.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"[Shopping] Could not dispose the vendor companion: {exception}"));
        }
    }
}
