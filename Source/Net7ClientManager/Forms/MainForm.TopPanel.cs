// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using System.Diagnostics;

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

        this.startClientButton = new Button
        {
            Text = "Start Client",
            Width = 90,
            Height = 24,
            Location = new Point(x: 520, y: 33),
        };

        this.createMissingClientsButton = new Button
        {
            Text = "Create Missing",
            Width = 105,
            Height = 24,
            Location = new Point(x: 620, y: 33),
        };

        this.keepClientsAliveCheckBox = new CheckBox
        {
            Text = "Keep alive",
            AutoSize = true,
            Location = new Point(x: 735, y: 36),
        };

        this.accountsButton = new Button
        {
            Text = "Accounts",
            Width = 90,
            Height = 24,
            Location = new Point(x: 835, y: 33),
        };

        this.inputLabButton = new Button
        {
            Text = "Input Lab",
            Width = 90,
            Height = 24,
            Location = new Point(x: 935, y: 33),
        };

        this.accountsButton.Click += this.AccountsButton_OnClick;
        this.profileComboBox.SelectedIndexChanged += this.ProfileComboBox_OnSelectedIndexChanged;
        this.addProfileButton.Click += this.AddProfileButton_OnClick;
        this.renameProfileButton.Click += this.RenameProfileButton_OnClick;
        this.duplicateProfileButton.Click += this.DuplicateProfileButton_OnClick;
        this.deleteProfileButton.Click += this.DeleteProfileButton_OnClick;
        this.startClientButton.Click += this.StartClientButton_OnClick;
        this.createMissingClientsButton.Click += this.CreateMissingClientsButton_OnClick;
        this.keepClientsAliveCheckBox.CheckedChanged += this.KeepClientsAliveCheckBox_OnCheckedChanged;
        this.inputLabButton.Click += this.InputLabButton_OnClick;

        panel.Controls.Add(titleLabel);
        panel.Controls.Add(this.profileComboBox);
        panel.Controls.Add(this.addProfileButton);
        panel.Controls.Add(this.renameProfileButton);
        panel.Controls.Add(this.duplicateProfileButton);
        panel.Controls.Add(this.deleteProfileButton);
        panel.Controls.Add(this.startClientButton);
        panel.Controls.Add(this.createMissingClientsButton);
        panel.Controls.Add(this.keepClientsAliveCheckBox);
        panel.Controls.Add(this.accountsButton);
        panel.Controls.Add(this.inputLabButton);

        this.inputLabButton.Visible = Debugger.IsAttached;

        return panel;
    }
}
