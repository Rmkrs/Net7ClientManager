// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using Net7ClientManager.Win32;

public abstract class ThemedForm : Form
{
    private const int WmNcHitTest = 0x0084;
    private const int HtClient = 1;
    private const int HtLeft = 10;
    private const int HtRight = 11;
    private const int HtTop = 12;
    private const int HtTopLeft = 13;
    private const int HtTopRight = 14;
    private const int HtBottom = 15;
    private const int HtBottomLeft = 16;
    private const int HtBottomRight = 17;
    private const int ResizeGripSize = 7;
    private const int TitleBarHeight = 38;
    private const int WindowBorderSize = 1;

    private readonly MainWindowTitleBar titleBar;
    private bool allowResize = true;
    private bool showWindowIcon = true;
    private bool showTitleBar = true;

    protected ThemedForm()
    {
        this.FormBorderStyle = FormBorderStyle.None;
        this.SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            value: true);

        this.titleBar = new MainWindowTitleBar
        {
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            TitleText = this.Text,
            WindowIcon = this.Icon,
        };

        this.titleBar.DragRequested += this.TitleBar_OnDragRequested;
        this.titleBar.MinimizeRequested += this.TitleBar_OnMinimizeRequested;
        this.titleBar.MaximizeRequested += this.TitleBar_OnMaximizeRequested;
        this.titleBar.CloseRequested += this.TitleBar_OnCloseRequested;

        this.Controls.Add(this.titleBar);
        this.UpdateWindowChromeLayout();
    }

    protected void ConfigureWindowChrome(
        bool allowResize,
        bool showMinimizeButton,
        bool showMaximizeButton,
        bool showCloseButton = true,
        bool showIcon = true)
    {
        this.showTitleBar = true;
        this.titleBar.Visible = true;
        this.allowResize = allowResize;
        this.showWindowIcon = showIcon;
        this.titleBar.WindowIcon = this.showWindowIcon ? this.Icon : null;
        this.titleBar.ShowMinimizeButton = showMinimizeButton;
        this.titleBar.ShowMaximizeButton = showMaximizeButton;
        this.titleBar.ShowCloseButton = showCloseButton;
        this.titleBar.IsMaximized = this.WindowState ==
                                    FormWindowState.Maximized;
        this.UpdateWindowChromeLayout();
    }


    protected void ConfigureCompactOverlayChrome()
    {
        this.allowResize = false;
        this.showTitleBar = false;
        this.titleBar.Visible = false;
        this.UpdateWindowChromeLayout();
    }


    protected void PositionNearCursor()
    {
        var cursor = Cursor.Position;
        var workingArea = Screen.FromPoint(cursor).WorkingArea;
        var x = Math.Clamp(
            cursor.X - 20,
            workingArea.Left,
            Math.Max(workingArea.Left, workingArea.Right - this.Width));
        var y = Math.Clamp(
            cursor.Y - 20,
            workingArea.Top,
            Math.Max(workingArea.Top, workingArea.Bottom - this.Height));

        this.StartPosition = FormStartPosition.Manual;
        this.Location = new Point(x, y);
    }

    protected override void OnControlAdded(ControlEventArgs e)
    {
        base.OnControlAdded(e);

        if (this.titleBar != null &&
            this.showTitleBar &&
            !ReferenceEquals(e.Control, this.titleBar))
        {
            this.titleBar.BringToFront();
        }
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);

        if (this.titleBar != null)
        {
            this.titleBar.TitleText = this.Text;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        if (this.titleBar != null)
        {
            this.titleBar.TitleText = this.Text;
            this.titleBar.WindowIcon = this.showWindowIcon ? this.Icon : null;
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);

        if (this.titleBar != null)
        {
            this.titleBar.IsMaximized = this.WindowState ==
                                        FormWindowState.Maximized;
            this.UpdateWindowChromeLayout();
        }

        this.Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        if (this.WindowState == FormWindowState.Maximized)
        {
            return;
        }

        using var pen = new Pen(MainWindowTheme.Border);
        e.Graphics.DrawRectangle(
            pen,
            x: 0,
            y: 0,
            width: Math.Max(0, this.ClientSize.Width - 1),
            height: Math.Max(0, this.ClientSize.Height - 1));
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);

        if (m.Msg != WmNcHitTest ||
            !this.allowResize ||
            this.WindowState != FormWindowState.Normal ||
            m.Result.ToInt32() != HtClient)
        {
            return;
        }

        var packedPoint = m.LParam.ToInt64();
        var screenPoint = new Point(
            unchecked((short)packedPoint),
            unchecked((short)(packedPoint >> 16)));
        var clientPoint = this.PointToClient(screenPoint);

        var isLeft = clientPoint.X <= ResizeGripSize;
        var isRight = clientPoint.X >=
                      this.ClientSize.Width - ResizeGripSize;
        var isTop = clientPoint.Y <= ResizeGripSize;
        var isBottom = clientPoint.Y >=
                       this.ClientSize.Height - ResizeGripSize;

        m.Result = (isLeft, isRight, isTop, isBottom) switch
        {
            (true, _, true, _) => new IntPtr(HtTopLeft),
            (_, true, true, _) => new IntPtr(HtTopRight),
            (true, _, _, true) => new IntPtr(HtBottomLeft),
            (_, true, _, true) => new IntPtr(HtBottomRight),
            (true, _, _, _) => new IntPtr(HtLeft),
            (_, true, _, _) => new IntPtr(HtRight),
            (_, _, true, _) => new IntPtr(HtTop),
            (_, _, _, true) => new IntPtr(HtBottom),
            _ => m.Result,
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && this.titleBar != null)
        {
            this.titleBar.DragRequested -= this.TitleBar_OnDragRequested;
            this.titleBar.MinimizeRequested -= this.TitleBar_OnMinimizeRequested;
            this.titleBar.MaximizeRequested -= this.TitleBar_OnMaximizeRequested;
            this.titleBar.CloseRequested -= this.TitleBar_OnCloseRequested;
        }

        base.Dispose(disposing);
    }

    private void TitleBar_OnDragRequested(
        object? sender,
        EventArgs e)
    {
        _ = NativeMethods.ReleaseCapture();
        NativeMethods.SendMoveWindowMessage(this.Handle);
    }

    private void TitleBar_OnMinimizeRequested(
        object? sender,
        EventArgs e)
    {
        this.WindowState = FormWindowState.Minimized;
    }

    private void TitleBar_OnMaximizeRequested(
        object? sender,
        EventArgs e)
    {
        if (!this.allowResize)
        {
            return;
        }

        this.WindowState = this.WindowState == FormWindowState.Maximized
            ? FormWindowState.Normal
            : FormWindowState.Maximized;
    }

    private void TitleBar_OnCloseRequested(
        object? sender,
        EventArgs e)
    {
        this.Close();
    }

    private void UpdateWindowChromeLayout()
    {
        var isMaximized = this.WindowState == FormWindowState.Maximized;
        var borderSize = isMaximized
            ? 0
            : WindowBorderSize;

        this.Padding = new Padding(
            left: borderSize,
            top: this.showTitleBar ? TitleBarHeight + borderSize : borderSize,
            right: borderSize,
            bottom: borderSize);

        this.titleBar.Visible = this.showTitleBar;

        if (!this.showTitleBar)
        {
            return;
        }

        this.titleBar.Bounds = new Rectangle(
            x: borderSize,
            y: borderSize,
            width: Math.Max(0, this.ClientSize.Width - (borderSize * 2)),
            height: TitleBarHeight);
        this.titleBar.BringToFront();
    }
}
