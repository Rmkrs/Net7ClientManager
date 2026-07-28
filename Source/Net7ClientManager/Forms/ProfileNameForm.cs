// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using Net7ClientManager.Core;

public sealed class ProfileNameForm : ThemedForm
{
    private readonly TextBox profileNameTextBox = new();

    public ProfileNameForm(string title, string labelText, string initialName)
    {
        this.Text = title;
        this.Icon = ResourceLoader.Net7ClientManagerIcon;
        this.StartPosition = FormStartPosition.CenterParent;
        this.ShowInTaskbar = false;
        this.ClientSize = new Size(width: 500, height: 230);
        this.BackColor = MainWindowTheme.Background;
        this.ForeColor = MainWindowTheme.Text;
        this.Font = MainWindowTheme.CreateBodyFont();
        this.ConfigureWindowChrome(
            allowResize: false,
            showMinimizeButton: false,
            showMaximizeButton: false);

        MainWindowTheme.StyleTextBox(this.profileNameTextBox);
        this.profileNameTextBox.Text = initialName;
        this.profileNameTextBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;

        var label = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            Text = labelText,
            TextAlign = ContentAlignment.BottomLeft,
            ForeColor = MainWindowTheme.MutedText,
            Padding = new Padding(left: 0, top: 0, right: 0, bottom: 6),
        };

        var okButton = new Button
        {
            Text = "Save",
            DialogResult = DialogResult.OK,
            Width = 92,
            Height = 34,
            Margin = new Padding(left: 6, top: 0, right: 0, bottom: 0),
        };
        MainWindowTheme.StyleButton(okButton, primary: true);

        var cancelButton = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Width = 92,
            Height = 34,
            Margin = Padding.Empty,
        };
        MainWindowTheme.StyleButton(cancelButton);

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(left: 0, top: 12, right: 0, bottom: 0),
            BackColor = MainWindowTheme.Background,
        };
        buttonPanel.Controls.Add(okButton);
        buttonPanel.Controls.Add(cancelButton);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(all: 22),
            BackColor = MainWindowTheme.Background,
        };

        root.RowStyles.Add(new RowStyle(SizeType.Absolute, height: 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, height: 42));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, height: 100));

        root.Controls.Add(label, column: 0, row: 0);
        root.Controls.Add(this.profileNameTextBox, column: 0, row: 1);
        root.Controls.Add(buttonPanel, column: 0, row: 2);
        this.Controls.Add(root);

        this.AcceptButton = okButton;
        this.CancelButton = cancelButton;

        this.Shown += (_, _) =>
        {
            this.profileNameTextBox.SelectAll();
            this.profileNameTextBox.Focus();
        };
    }

    public string ProfileName
    {
        get
        {
            var profileName = this.profileNameTextBox.Text.Trim();

            return string.IsNullOrWhiteSpace(profileName)
                ? "Unnamed Profile"
                : profileName;
        }
    }
}
