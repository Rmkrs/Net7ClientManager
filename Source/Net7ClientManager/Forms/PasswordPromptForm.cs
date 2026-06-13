// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

public sealed class PasswordPromptForm : Form
{
    private readonly TextBox passwordTextBox;

    public PasswordPromptForm()
    {
        this.Text = "Set Password";
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.StartPosition = FormStartPosition.CenterParent;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.ClientSize = new Size(width: 320, height: 120);

        var label = new Label
        {
            Text = "Password",
            AutoSize = true,
            Location = new Point(x: 12, y: 18),
        };

        this.passwordTextBox = new TextBox
        {
            UseSystemPasswordChar = true,
            Width = 210,
            Location = new Point(x: 90, y: 14),
        };

        var okButton = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Width = 80,
            Location = new Point(x: 134, y: 72),
        };

        var cancelButton = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Width = 80,
            Location = new Point(x: 220, y: 72),
        };

        this.Controls.Add(label);
        this.Controls.Add(this.passwordTextBox);
        this.Controls.Add(okButton);
        this.Controls.Add(cancelButton);

        this.AcceptButton = okButton;
        this.CancelButton = cancelButton;
    }

    public string Password => this.passwordTextBox.Text;
}
