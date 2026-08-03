// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using Net7ClientManager.Core;

internal sealed class ThemedMessageDialog : ThemedForm
{
    private ThemedMessageDialog(
        string title,
        string message,
        string primaryButtonText,
        DialogResult primaryResult,
        string? secondaryButtonText,
        Action? confirmedAction = null,
        bool autoEllipsis = true)
    {
        this.Text = title;
        this.Icon = ResourceLoader.Net7ClientManagerIcon;
        this.StartPosition = FormStartPosition.CenterParent;
        this.ShowInTaskbar = false;
        this.ClientSize = new Size(width: 500, height: 220);
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

        var messageLabel = new Label
        {
            Text = message,
            Dock = DockStyle.Fill,
            ForeColor = MainWindowTheme.Text,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = autoEllipsis,
            Padding = new Padding(left: 6, top: 0, right: 0, bottom: 0),
        };

        var primaryButton = new Button
        {
            Text = primaryButtonText,
            DialogResult = confirmedAction == null
                ? primaryResult
                : DialogResult.None,
            Width = 96,
            Height = 34,
            Margin = new Padding(left: 6, top: 0, right: 0, bottom: 0),
        };
        MainWindowTheme.StyleButton(
            primaryButton,
            primary: secondaryButtonText == null,
            danger: secondaryButtonText != null);

        if (confirmedAction != null)
        {
            primaryButton.Click += (_, _) =>
            {
                this.Close();
                confirmedAction();
            };
        }

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(left: 0, top: 12, right: 0, bottom: 0),
            BackColor = MainWindowTheme.Background,
        };
        buttonPanel.Controls.Add(primaryButton);

        if (secondaryButtonText == null)
        {
            this.AcceptButton = primaryButton;
        }
        else
        {
            var secondaryButton = new Button
            {
                Text = secondaryButtonText,
                DialogResult = confirmedAction == null
                    ? DialogResult.Cancel
                    : DialogResult.None,
                Width = 96,
                Height = 34,
                Margin = Padding.Empty,
            };
            MainWindowTheme.StyleButton(secondaryButton);

            if (confirmedAction != null)
            {
                secondaryButton.Click += (_, _) => this.Close();
            }

            buttonPanel.Controls.Add(secondaryButton);
            this.AcceptButton = secondaryButton;
            this.CancelButton = secondaryButton;
        }

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

    public static bool Confirm(
        IWin32Window owner,
        string title,
        string message,
        string confirmButtonText)
    {
        using var dialog = new ThemedMessageDialog(
            title,
            message,
            confirmButtonText,
            DialogResult.Yes,
            secondaryButtonText: "Cancel");

        return dialog.ShowDialog(owner) == DialogResult.Yes;
    }

    public static void ConfirmModeless(
        IWin32Window owner,
        string title,
        string message,
        string confirmButtonText,
        Action confirmedAction)
    {
        ArgumentNullException.ThrowIfNull(confirmedAction);

        var dialog = new ThemedMessageDialog(
            title,
            message,
            confirmButtonText,
            DialogResult.None,
            secondaryButtonText: "Cancel",
            confirmedAction: confirmedAction,
            autoEllipsis: false);

        dialog.Load += (_, _) => dialog.CenterToParent();
        dialog.FormClosed += (_, _) => dialog.Dispose();
        dialog.Show(owner);
    }

    public static void ShowWarning(
        IWin32Window owner,
        string title,
        string message)
    {
        using var dialog = new ThemedMessageDialog(
            title,
            message,
            primaryButtonText: "OK",
            primaryResult: DialogResult.OK,
            secondaryButtonText: null);

        _ = dialog.ShowDialog(owner);
    }
}
