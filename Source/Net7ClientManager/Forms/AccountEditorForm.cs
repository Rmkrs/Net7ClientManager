// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using Net7ClientManager.Models;
using Net7ClientManager.Services;

public sealed class AccountEditorForm : Form
{
    private readonly TextBox displayNameTextBox = new();
    private readonly TextBox loginNameTextBox = new();
    private readonly TextBox passwordTextBox = new();
    private readonly CheckBox clearPasswordCheckBox = new();

    private readonly GameAccount account;

    public AccountEditorForm(GameAccount account)
    {
        this.account = account;

        this.Text = string.IsNullOrWhiteSpace(account.DisplayName)
            ? "Add Account"
            : "Edit Account";

        this.StartPosition = FormStartPosition.CenterParent;
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.ClientSize = new Size(460, 245);

        this.BuildUi();

        this.displayNameTextBox.Text = account.DisplayName;
        this.loginNameTextBox.Text = account.LoginName;
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 5,
            Padding = new Padding(14),
        };

        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        this.Controls.Add(root);

        this.displayNameTextBox.Dock = DockStyle.Fill;
        this.loginNameTextBox.Dock = DockStyle.Fill;
        this.passwordTextBox.Dock = DockStyle.Fill;
        this.passwordTextBox.UseSystemPasswordChar = true;
        this.passwordTextBox.PlaceholderText = string.IsNullOrEmpty(this.account.ProtectedPassword)
            ? "Enter password"
            : "Leave empty to keep current password";

        this.clearPasswordCheckBox.Text = "Clear stored password";
        this.clearPasswordCheckBox.Dock = DockStyle.Fill;

        root.Controls.Add(this.CreateLabel("Display name"), 0, 0);
        root.Controls.Add(this.displayNameTextBox, 1, 0);

        root.Controls.Add(this.CreateLabel("Login name"), 0, 1);
        root.Controls.Add(this.loginNameTextBox, 1, 1);

        root.Controls.Add(this.CreateLabel("Password"), 0, 2);
        root.Controls.Add(this.passwordTextBox, 1, 2);

        root.Controls.Add(new Label(), 0, 3);
        root.Controls.Add(this.clearPasswordCheckBox, 1, 3);

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
        };

        var saveButton = new Button
        {
            Text = "Save",
            DialogResult = DialogResult.OK,
            Width = 90,
        };

        var cancelButton = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Width = 90,
        };

        saveButton.Click += this.SaveButton_OnClick;

        buttonPanel.Controls.Add(saveButton);
        buttonPanel.Controls.Add(cancelButton);

        root.Controls.Add(buttonPanel, 0, 4);
        root.SetColumnSpan(buttonPanel, 2);

        this.AcceptButton = saveButton;
        this.CancelButton = cancelButton;
    }

    private Label CreateLabel(string text)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
        };
    }

    private void SaveButton_OnClick(object? sender, EventArgs e)
    {
        var displayName = this.displayNameTextBox.Text.Trim();
        var loginName = this.loginNameTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(displayName))
        {
            MessageBox.Show(
                this,
                "Display name is required.",
                "Account",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);

            this.DialogResult = DialogResult.None;
            return;
        }

        if (string.IsNullOrWhiteSpace(loginName))
        {
            MessageBox.Show(
                this,
                "Login name is required.",
                "Account",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);

            this.DialogResult = DialogResult.None;
            return;
        }

        this.account.DisplayName = displayName;
        this.account.LoginName = loginName;

        if (this.clearPasswordCheckBox.Checked)
        {
            this.account.ProtectedPassword = null;
        }
        else if (!string.IsNullOrEmpty(this.passwordTextBox.Text))
        {
            this.account.ProtectedPassword = PasswordProtector.Protect(this.passwordTextBox.Text);
        }
    }
}
