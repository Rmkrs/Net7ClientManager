// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using Net7ClientManager.Core;

internal enum ClientProcessActionChoice
{
    Cancel,
    Restart,
    ForceClose,
}

internal sealed class ClientProcessActionDialog : ThemedForm
{
    private ClientProcessActionChoice choice =
        ClientProcessActionChoice.Cancel;

    private ClientProcessActionDialog(
        string clientName,
        string? restartSlotName)
    {
        var canRestart = !string.IsNullOrWhiteSpace(restartSlotName);

        this.Text = string.Concat("Close ", clientName, "?");
        this.Icon = ResourceLoader.Net7ClientManagerIcon;
        this.StartPosition = FormStartPosition.CenterParent;
        this.ShowInTaskbar = false;
        this.ClientSize = new Size(width: 540, height: 238);
        this.BackColor = MainWindowTheme.Background;
        this.ForeColor = MainWindowTheme.Text;
        this.Font = MainWindowTheme.CreateBodyFont();
        this.ConfigureWindowChrome(
            allowResize: false,
            showMinimizeButton: false,
            showMaximizeButton: false);

        var icon = new PictureBox
        {
            Image = SystemIcons.Warning.ToBitmap(),
            SizeMode = PictureBoxSizeMode.CenterImage,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
        };

        var message = canRestart
            ? string.Concat(
                "This immediately closes the game client. Use this when the game is stuck.",
                Environment.NewLine,
                Environment.NewLine,
                "Restart starts ",
                restartSlotName,
                " again. Close client closes it immediately.")
            : string.Concat(
                "This immediately closes the game client. Use this when the game is stuck.",
                Environment.NewLine,
                Environment.NewLine,
                "This client is not assigned to a slot, so it cannot be restarted automatically.");

        var messageLabel = new Label
        {
            Text = message,
            Dock = DockStyle.Fill,
            ForeColor = MainWindowTheme.Text,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(left: 6, top: 0, right: 0, bottom: 0),
        };

        var cancelButton = new Button
        {
            Text = "Cancel",
            Width = 96,
            Height = 34,
            Margin = Padding.Empty,
        };
        MainWindowTheme.StyleButton(cancelButton);
        cancelButton.Click += (_, _) =>
        {
            this.choice = ClientProcessActionChoice.Cancel;
            this.Close();
        };

        var restartButton = new Button
        {
            Text = "Restart",
            Width = 96,
            Height = 34,
            Visible = canRestart,
            Margin = new Padding(left: 6, top: 0, right: 0, bottom: 0),
        };
        MainWindowTheme.StyleButton(restartButton, primary: true);
        restartButton.Click += (_, _) =>
        {
            this.choice = ClientProcessActionChoice.Restart;
            this.Close();
        };

        var closeButton = new Button
        {
            Text = "Close client",
            Width = 112,
            Height = 34,
            Margin = new Padding(left: 6, top: 0, right: 0, bottom: 0),
        };
        MainWindowTheme.StyleButton(closeButton, danger: true);
        closeButton.Click += (_, _) =>
        {
            this.choice = ClientProcessActionChoice.ForceClose;
            this.Close();
        };

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(left: 0, top: 12, right: 0, bottom: 0),
            BackColor = MainWindowTheme.Background,
        };

        buttonPanel.Controls.Add(closeButton);

        if (canRestart)
        {
            buttonPanel.Controls.Add(restartButton);
        }

        buttonPanel.Controls.Add(cancelButton);

        this.AcceptButton = cancelButton;
        this.CancelButton = cancelButton;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Padding = new Padding(left: 22, top: 18, right: 22, bottom: 18),
            BackColor = MainWindowTheme.Background,
        };

        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, width: 54));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, width: 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, height: 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, height: 56));

        root.Controls.Add(icon, column: 0, row: 0);
        root.Controls.Add(messageLabel, column: 1, row: 0);
        root.Controls.Add(buttonPanel, column: 0, row: 1);
        root.SetColumnSpan(buttonPanel, value: 2);

        this.Controls.Add(root);
    }

    public static ClientProcessActionChoice Show(
        IWin32Window owner,
        string clientName,
        string? restartSlotName)
    {
        using var dialog = new ClientProcessActionDialog(
            clientName,
            restartSlotName);

        _ = dialog.ShowDialog(owner);
        return dialog.choice;
    }
}
