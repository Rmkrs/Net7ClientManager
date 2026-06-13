// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

public sealed partial class MainForm
{
    private Control CreateTopPanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 64,
            Padding = new Padding(left: 12, top: 8, right: 12, bottom: 8),
            BackColor = Color.FromArgb(red: 248, green: 250, blue: 253),
        };

        var titleLabel = new Label
        {
            AutoSize = true,
            ForeColor = Color.FromArgb(red: 28, green: 35, blue: 45),
            Font = new Font(this.Font.FontFamily, emSize: 12, FontStyle.Bold),
            Text = "Net7 Client Manager",
            Location = new Point(x: 12, y: 8),
        };

        this.profileComboBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 220,
            Location = new Point(x: 14, y: 34),
        };

        this.addProfileButton = new Button
        {
            Text = "+",
            Width = 30,
            Height = 24,
            Location = new Point(x: 244, y: 33),
        };

        this.renameProfileButton = new Button
        {
            Text = "Rename",
            Width = 70,
            Height = 24,
            Location = new Point(x: 280, y: 33),
        };

        this.duplicateProfileButton = new Button
        {
            Text = "Duplicate",
            Width = 80,
            Height = 24,
            Location = new Point(x: 356, y: 33),
        };

        this.deleteProfileButton = new Button
        {
            Text = "Delete",
            Width = 64,
            Height = 24,
            Location = new Point(x: 442, y: 33),
        };

        this.profileComboBox.SelectedIndexChanged += this.ProfileComboBox_OnSelectedIndexChanged;
        this.addProfileButton.Click += this.AddProfileButton_OnClick;
        this.renameProfileButton.Click += this.RenameProfileButton_OnClick;
        this.duplicateProfileButton.Click += this.DuplicateProfileButton_OnClick;
        this.deleteProfileButton.Click += this.DeleteProfileButton_OnClick;

        panel.Controls.Add(titleLabel);
        panel.Controls.Add(this.profileComboBox);
        panel.Controls.Add(this.addProfileButton);
        panel.Controls.Add(this.renameProfileButton);
        panel.Controls.Add(this.duplicateProfileButton);
        panel.Controls.Add(this.deleteProfileButton);

        return panel;
    }
}
