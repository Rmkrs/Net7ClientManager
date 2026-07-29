// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using Net7ClientManager.Core;

internal sealed class AutoLoginReadinessDialog : ThemedForm
{
    public AutoLoginReadinessDialog(AutoLoginReadinessIssue issue)
    {
        var isReady = issue.Kind == AutoLoginReadinessIssueKind.Ready;

        this.Text = "Auto-login setup";
        this.Icon = ResourceLoader.Net7ClientManagerIcon;
        this.StartPosition = FormStartPosition.CenterParent;
        this.ShowInTaskbar = false;
        this.ClientSize = new Size(width: 590, height: 250);
        this.BackColor = MainWindowTheme.Background;
        this.ForeColor = MainWindowTheme.Text;
        this.Font = MainWindowTheme.CreateBodyFont();
        this.ConfigureWindowChrome(
            allowResize: false,
            showMinimizeButton: false,
            showMaximizeButton: false);

        var statusLabel = new Label
        {
            Text = isReady
                ? "READY"
                : "NEXT STEP",
            AutoSize = true,
            Font = MainWindowTheme.CreateBodyFont(size: 8.0f),
            ForeColor = isReady
                ? MainWindowTheme.Success
                : MainWindowTheme.Warning,
            Margin = Padding.Empty,
        };

        var titleLabel = new Label
        {
            Text = issue.Title,
            AutoSize = false,
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            Font = MainWindowTheme.CreateHeadingFont(size: 15.0f),
            ForeColor = MainWindowTheme.Text,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = Padding.Empty,
        };

        var messageLabel = new Label
        {
            Text = issue.Message,
            AutoSize = false,
            Dock = DockStyle.Fill,
            ForeColor = MainWindowTheme.MutedText,
            TextAlign = ContentAlignment.TopLeft,
            Margin = Padding.Empty,
        };

        var closeButton = new Button
        {
            Text = isReady
                ? "Done"
                : "Close",
            DialogResult = DialogResult.Cancel,
            Width = 96,
            Height = 34,
            Margin = Padding.Empty,
        };
        MainWindowTheme.StyleButton(closeButton);

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(left: 0, top: 10, right: 0, bottom: 0),
            BackColor = MainWindowTheme.Background,
        };
        buttonPanel.Controls.Add(closeButton);

        if (!isReady)
        {
            var showMeButton = new Button
            {
                Text = "Show me",
                DialogResult = DialogResult.OK,
                Width = 108,
                Height = 34,
                Margin = new Padding(left: 0, top: 0, right: 8, bottom: 0),
            };
            MainWindowTheme.StyleButton(showMeButton, primary: true);
            buttonPanel.Controls.Add(showMeButton);
            this.AcceptButton = showMeButton;
        }
        else
        {
            this.AcceptButton = closeButton;
        }

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(all: 24),
            BackColor = MainWindowTheme.Background,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, height: 24));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, height: 48));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, height: 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, height: 54));

        root.Controls.Add(statusLabel, column: 0, row: 0);
        root.Controls.Add(titleLabel, column: 0, row: 1);
        root.Controls.Add(messageLabel, column: 0, row: 2);
        root.Controls.Add(buttonPanel, column: 0, row: 3);

        this.Controls.Add(root);
        this.CancelButton = closeButton;
    }
}
