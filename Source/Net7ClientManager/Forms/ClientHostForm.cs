namespace Net7ClientManager.Forms;

using System.Globalization;
using Net7ClientManager.Core;
using Net7ClientManager.Models;
using Net7ClientManager.Services;
using Net7ClientManager.Win32;

public sealed class ClientHostForm : Form
{
    private const int TitleBarHeight = 28;

    private readonly ClientInstance clientInstance;
    private readonly ClientDockingService clientDockingService;
    private readonly Action<ClientInstance, CloseReason> closeRequested;

    private readonly Panel titleBarPanel;
    private readonly Label titleLabel;
    private readonly Button minimizeButton;
    private readonly Button closeButton;
    private readonly Panel gamePanel;

    private bool closeRequestedByManager;

    public ClientHostForm(
        ClientInstance clientInstance,
        ClientDockingService clientDockingService,
        Action<ClientInstance, CloseReason> closeRequested)
    {
        this.clientInstance = clientInstance;
        this.clientDockingService = clientDockingService;
        this.closeRequested = closeRequested;

        this.Text = string.Create(CultureInfo.InvariantCulture, $"Earth & Beyond - PID {clientInstance.ProcessId}");
        this.Icon = ResourceLoader.EarthAndBeyondIcon;
        this.StartPosition = FormStartPosition.Manual;
        this.FormBorderStyle = FormBorderStyle.None;
        this.MinimumSize = new Size(640, 480 + TitleBarHeight);

        this.titleBarPanel = this.CreateTitleBarPanel();
        this.titleLabel = this.CreateTitleLabel();
        this.minimizeButton = this.CreateTitleButton("─");
        this.closeButton = this.CreateTitleButton("×");
        this.closeButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(red: 232, green: 80, blue: 80);
        this.closeButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(red: 200, green: 55, blue: 55);

        this.gamePanel = this.CreateGamePanel();

        this.titleBarPanel.Controls.Add(this.titleLabel);
        this.titleBarPanel.Controls.Add(this.minimizeButton);
        this.titleBarPanel.Controls.Add(this.closeButton);

        this.Controls.Add(this.gamePanel);
        this.Controls.Add(this.titleBarPanel);

        this.ApplyInitialBoundsFromGameWindow();

        this.Load += this.ClientHostForm_OnLoad;
        this.Shown += this.ClientHostForm_OnShown;
        this.Resize += this.ClientHostForm_OnResize;
        this.FormClosing += this.ClientHostForm_OnFormClosing;

        this.titleBarPanel.MouseDown += this.TitleBarPanel_OnMouseDown;
        this.titleLabel.MouseDown += this.TitleBarPanel_OnMouseDown;
        this.minimizeButton.Click += this.MinimizeButton_OnClick;
        this.closeButton.Click += this.CloseButton_OnClick;
    }

    public void CloseFromManager()
    {
        if (this.IsDisposed || this.Disposing)
        {
            return;
        }

        this.closeRequestedByManager = true;
        this.Close();
    }

    public void ApplySlot(ClientSlot slot)
    {
        var title = string.IsNullOrWhiteSpace(slot.AccountName)
            ? slot.Name
            : string.Concat(slot.Name, " - ", slot.AccountName);

        this.SetHostTitle(title);

        this.StartPosition = FormStartPosition.Manual;
        this.Location = new Point(slot.Bounds.Left, slot.Bounds.Top);

        this.ClientSize = new Size(
            width: slot.Bounds.Width,
            height: slot.Bounds.Height + TitleBarHeight);

        _ = this.clientDockingService.TryResizeDockedWindow(
            this.clientInstance.GameWindowHandle,
            this.gamePanel.ClientSize);
    }

    public void SetUnassignedTitle()
    {
        this.SetHostTitle(string.Create(CultureInfo.InvariantCulture, $"Earth & Beyond - PID {this.clientInstance.ProcessId}"));
    }

    public WindowBounds GetSlotBounds()
    {
        return new WindowBounds
        {
            Left = this.Left,
            Top = this.Top,
            Width = this.gamePanel.ClientSize.Width,
            Height = this.gamePanel.ClientSize.Height,
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.Load -= this.ClientHostForm_OnLoad;
            this.Shown -= this.ClientHostForm_OnShown;
            this.Resize -= this.ClientHostForm_OnResize;
            this.FormClosing -= this.ClientHostForm_OnFormClosing;

            this.titleBarPanel.MouseDown -= this.TitleBarPanel_OnMouseDown;
            this.titleLabel.MouseDown -= this.TitleBarPanel_OnMouseDown;
            this.minimizeButton.Click -= this.MinimizeButton_OnClick;
            this.closeButton.Click -= this.CloseButton_OnClick;
        }

        base.Dispose(disposing);
    }

    private Panel CreateTitleBarPanel()
    {
        return new Panel
        {
            Dock = DockStyle.Top,
            Height = TitleBarHeight,
            BackColor = Color.FromArgb(red: 245, green: 247, blue: 250),
        };
    }

    private Label CreateTitleLabel()
    {
        return new Label
        {
            AutoEllipsis = true,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(left: 8, top: 0, right: 0, bottom: 0),
            ForeColor = Color.FromArgb(red: 28, green: 35, blue: 45),
        };
    }

    private Button CreateTitleButton(string text)
    {
        var button = new Button
        {
            Dock = DockStyle.Right,
            Text = text,
            Width = 36,
            FlatStyle = FlatStyle.Flat,
            TabStop = false,
            BackColor = Color.FromArgb(red: 245, green: 247, blue: 250),
            ForeColor = Color.FromArgb(red: 45, green: 52, blue: 64),
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };

        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(red: 230, green: 235, blue: 243);
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(red: 210, green: 218, blue: 230);

        return button;
    }
    private Panel CreateGamePanel()
    {
        return new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Black,
        };
    }

    private void ApplyInitialBoundsFromGameWindow()
    {
        var bounds = NativeMethods.GetWindowBounds(this.clientInstance.GameWindowHandle);

        if (bounds == null)
        {
            this.StartPosition = FormStartPosition.CenterScreen;
            this.ClientSize = new Size(width: 1280, height: 720 + TitleBarHeight);
            return;
        }

        this.Location = new Point(bounds.Value.Left, bounds.Value.Top);

        this.ClientSize = new Size(
            width: bounds.Value.Width,
            height: bounds.Value.Height + TitleBarHeight);
    }

    private void SetHostTitle(string title)
    {
        this.Text = title;
        this.titleLabel.Text = title;
    }

    private void ClientHostForm_OnLoad(object? sender, EventArgs e)
    {
        var docked = this.clientDockingService.Dock(
            this.clientInstance.GameWindowHandle,
            this.gamePanel.Handle,
            this.gamePanel.ClientSize);

        if (!docked)
        {
            this.closeRequested(this.clientInstance, CloseReason.ProcessExited);
        }
    }

    private void ClientHostForm_OnShown(object? sender, EventArgs e)
    {
        this.BeginInvoke(() =>
        {
            if (this.IsDisposed || this.Disposing)
            {
                return;
            }

            _ = this.clientDockingService.TryResizeDockedWindow(
                this.clientInstance.GameWindowHandle,
                this.gamePanel.ClientSize);
        });
    }

    private void ClientHostForm_OnResize(object? sender, EventArgs e)
    {
        if (this.IsDisposed || this.Disposing)
        {
            return;
        }

        _ = this.clientDockingService.TryResizeDockedWindow(
            this.clientInstance.GameWindowHandle,
            this.gamePanel.ClientSize);
    }

    private void ClientHostForm_OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (this.closeRequestedByManager)
        {
            return;
        }

        this.closeRequested(this.clientInstance, CloseReason.UserRequested);
    }

    private void TitleBarPanel_OnMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        NativeMethods.ReleaseCapture();
        NativeMethods.SendMoveWindowMessage(this.Handle);
    }

    private void MinimizeButton_OnClick(object? sender, EventArgs e)
    {
        this.WindowState = FormWindowState.Minimized;
    }

    private void CloseButton_OnClick(object? sender, EventArgs e)
    {
        this.Close();
    }
}
