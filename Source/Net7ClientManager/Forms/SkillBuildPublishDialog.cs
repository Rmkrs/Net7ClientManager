// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using Net7ClientManager.Core;

internal sealed class SkillBuildPublishDialog : ThemedForm
{
    private SkillBuildPublishDialog(
        string buildTitle,
        bool isNewPublication)
    {
        this.Text = "Publish build";
        this.Icon = ResourceLoader.Net7ClientManagerIcon;
        this.StartPosition = FormStartPosition.CenterParent;
        this.ShowInTaskbar = false;
        this.AutoScaleMode = AutoScaleMode.None;
        this.ClientSize = new Size(560, 250);
        this.MinimumSize = this.Size;
        this.MaximumSize = this.Size;
        this.BackColor = MainWindowTheme.Background;
        this.ForeColor = MainWindowTheme.Text;
        this.Font = MainWindowTheme.CreateBodyFont();
        this.ConfigureWindowChrome(
            allowResize: false,
            showMinimizeButton: false,
            showMaximizeButton: false);

        var heading = new Label
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Text = isNewPublication
                ? "PUBLISH TO NET7 FORGE"
                : "PUBLISH NEW VERSION",
            TextAlign = ContentAlignment.MiddleLeft,
            Font = MainWindowTheme.CreateHeadingFont(13.0f),
            ForeColor = MainWindowTheme.Accent,
            BackColor = Color.Transparent,
        };

        var message = new Label
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Text = isNewPublication
                ? "Publish this build to the Forge?"
                : "Publish a new version of this build to the Forge?",
            TextAlign = ContentAlignment.MiddleLeft,
            Font = MainWindowTheme.CreateBodyFont(10.0f),
            ForeColor = MainWindowTheme.Text,
            BackColor = Color.Transparent,
        };

        var build = new Label
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new Padding(12, 0, 12, 0),
            Text = buildTitle,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = MainWindowTheme.CreateHeadingFont(10.0f),
            ForeColor = MainWindowTheme.Text,
            BackColor = MainWindowTheme.ElevatedPanel,
            AutoEllipsis = true,
        };

        var cancel = new Button
        {
            Dock = DockStyle.Fill,
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Margin = new Padding(0, 8, 6, 8),
        };
        MainWindowTheme.StyleButton(cancel);

        var publish = new Button
        {
            Dock = DockStyle.Fill,
            Text = "Publish",
            DialogResult = DialogResult.OK,
            Margin = new Padding(6, 8, 0, 8),
        };
        MainWindowTheme.StyleButton(publish, primary: true);

        var buttons = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Color.Transparent,
        };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100.0f));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104.0f));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104.0f));
        buttons.RowStyles.Add(new RowStyle(SizeType.Percent, 100.0f));
        buttons.Controls.Add(cancel, 1, 0);
        buttons.Controls.Add(publish, 2, 0);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Margin = Padding.Empty,
            Padding = new Padding(22, 16, 22, 18),
            BackColor = MainWindowTheme.Background,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100.0f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34.0f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42.0f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44.0f));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100.0f));
        root.Controls.Add(heading, 0, 0);
        root.Controls.Add(message, 0, 1);
        root.Controls.Add(build, 0, 2);
        root.Controls.Add(buttons, 0, 3);

        this.AcceptButton = publish;
        this.CancelButton = cancel;
        this.Controls.Add(root);
    }

    public static bool Confirm(
        IWin32Window owner,
        string buildTitle,
        bool isNewPublication)
    {
        using var dialog = new SkillBuildPublishDialog(
            buildTitle,
            isNewPublication);
        return dialog.ShowDialog(owner) == DialogResult.OK;
    }
}
