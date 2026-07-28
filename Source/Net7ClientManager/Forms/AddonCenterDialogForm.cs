// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using Net7ClientManager.Core;
using Net7ClientManager.Win32;

internal abstract class AddonCenterDialogForm : Form
{
    private const int TitleBarHeight = 34;

    private readonly HostedClientTitleBar titleBar = new();

    protected AddonCenterDialogForm(
        string title,
        Size contentSize)
    {
        this.Text = title;
        this.Icon = ResourceLoader.Net7ClientManagerIcon;
        this.StartPosition = FormStartPosition.CenterParent;
        this.FormBorderStyle = FormBorderStyle.None;
        this.MaximizeBox = false;
        this.MinimizeBox = true;
        this.ShowInTaskbar = false;
        this.ClientSize = new Size(
            contentSize.Width,
            contentSize.Height + TitleBarHeight);
        this.Padding = new Padding(1);
        this.BackColor = AddonCenterTheme.Border;
        this.ForeColor = AddonCenterTheme.Text;
        this.Font = new Font("Segoe UI", 9.0f);

        this.titleBar.Dock = DockStyle.Top;
        this.titleBar.Height = TitleBarHeight;
        this.titleBar.TitleText = title.ToUpperInvariant();
        this.titleBar.ShowMaximizeButton = false;
        this.titleBar.AccessibleName = string.Concat(
            title,
            " title bar");
        this.titleBar.DragRequested += this.TitleBar_OnDragRequested;
        this.titleBar.MinimizeRequested +=
            this.TitleBar_OnMinimizeRequested;
        this.titleBar.CloseRequested += this.TitleBar_OnCloseRequested;

        this.ContentPanel.Dock = DockStyle.Fill;
        this.ContentPanel.BackColor = AddonCenterTheme.Background;

        this.Controls.Add(this.ContentPanel);
        this.Controls.Add(this.titleBar);
    }

    protected Panel ContentPanel { get; } = new();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.titleBar.DragRequested -=
                this.TitleBar_OnDragRequested;
            this.titleBar.MinimizeRequested -=
                this.TitleBar_OnMinimizeRequested;
            this.titleBar.CloseRequested -=
                this.TitleBar_OnCloseRequested;
        }

        base.Dispose(disposing);
    }

    private void TitleBar_OnDragRequested(
        object? sender,
        EventArgs e)
    {
        NativeMethods.ReleaseCapture();
        NativeMethods.SendMoveWindowMessage(this.Handle);
    }

    private void TitleBar_OnMinimizeRequested(
        object? sender,
        EventArgs e)
    {
        this.WindowState = FormWindowState.Minimized;
    }

    private void TitleBar_OnCloseRequested(
        object? sender,
        EventArgs e)
    {
        this.Close();
    }
}
