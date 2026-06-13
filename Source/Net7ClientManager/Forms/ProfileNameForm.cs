// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using Net7ClientManager.Core;

public sealed class ProfileNameForm : Form
{
    private readonly TextBox profileNameTextBox;

    public ProfileNameForm(string title, string labelText, string initialName)
    {
        this.Text = title;
        this.Icon = ResourceLoader.EarthAndBeyondIcon;
        this.StartPosition = FormStartPosition.CenterParent;
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.ClientSize = new Size(width: 360, height: 120);

        var label = new Label
        {
            AutoSize = true,
            Text = labelText,
            Location = new Point(x: 12, y: 14),
        };

        this.profileNameTextBox = new TextBox
        {
            Text = initialName,
            Location = new Point(x: 12, y: 40),
            Width = 330,
        };

        var okButton = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Location = new Point(x: 186, y: 78),
            Width = 75,
        };

        var cancelButton = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Location = new Point(x: 267, y: 78),
            Width = 75,
        };

        this.AcceptButton = okButton;
        this.CancelButton = cancelButton;

        this.Controls.Add(label);
        this.Controls.Add(this.profileNameTextBox);
        this.Controls.Add(okButton);
        this.Controls.Add(cancelButton);
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
