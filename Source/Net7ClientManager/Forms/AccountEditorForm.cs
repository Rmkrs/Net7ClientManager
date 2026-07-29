// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using Net7ClientManager.Core;
using Net7ClientManager.Models;
using Net7ClientManager.Services;

public sealed class AccountEditorForm : ThemedForm
{
    private readonly TextBox displayNameTextBox = new();
    private readonly TextBox loginNameTextBox = new();
    private readonly TextBox passwordTextBox = new();
    private readonly ThemedCheckBox clearPasswordCheckBox = new();

    private readonly GameAccount account;
    private readonly AccountEditorGuidanceTarget guidanceTarget;

    public AccountEditorForm(GameAccount account)
        : this(
            account,
            AccountEditorGuidanceTarget.None,
            isNew: false)
    {
    }

    internal AccountEditorForm(
        GameAccount account,
        AccountEditorGuidanceTarget guidanceTarget,
        bool isNew = false)
    {
        this.account = account;
        this.guidanceTarget = guidanceTarget;

        this.Text = isNew
            ? "Add account"
            : "Edit account";

        this.Icon = ResourceLoader.Net7ClientManagerIcon;
        this.StartPosition = FormStartPosition.CenterParent;
        this.ShowInTaskbar = false;
        this.ClientSize = new Size(width: 520, height: 330);
        this.BackColor = MainWindowTheme.Background;
        this.ForeColor = MainWindowTheme.Text;
        this.Font = MainWindowTheme.CreateBodyFont();
        this.ConfigureWindowChrome(
            allowResize: false,
            showMinimizeButton: false,
            showMaximizeButton: false);

        this.BuildUi();

        this.displayNameTextBox.Text = account.DisplayName;
        this.loginNameTextBox.Text = account.LoginName;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        var target = this.guidanceTarget switch
        {
            AccountEditorGuidanceTarget.LoginName =>
                this.loginNameTextBox,
            AccountEditorGuidanceTarget.Password =>
                this.passwordTextBox,
            _ => null,
        };

        if (target != null)
        {
            this.BeginInvoke(() => ControlGuidancePulse.Start(target));
        }
    }

    private void BuildUi()
    {
        MainWindowTheme.StyleTextBox(this.displayNameTextBox);
        MainWindowTheme.StyleTextBox(this.loginNameTextBox);
        MainWindowTheme.StyleTextBox(this.passwordTextBox);

        this.displayNameTextBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        this.loginNameTextBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        this.passwordTextBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        this.passwordTextBox.UseSystemPasswordChar = true;
        this.passwordTextBox.PlaceholderText =
            string.IsNullOrEmpty(this.account.ProtectedPassword)
                ? "Enter password"
                : "Leave empty to keep current password";

        this.clearPasswordCheckBox.Text = "Clear stored password";
        this.clearPasswordCheckBox.AutoSize = true;
        this.clearPasswordCheckBox.Anchor = AnchorStyles.Left;
        this.clearPasswordCheckBox.ForeColor = MainWindowTheme.Text;

        var saveButton = new Button
        {
            Text = "Save",
            DialogResult = DialogResult.OK,
            Width = 92,
            Height = 34,
            Margin = new Padding(left: 6, top: 0, right: 0, bottom: 0),
        };
        MainWindowTheme.StyleButton(saveButton, primary: true);
        saveButton.Click += this.SaveButton_OnClick;

        var cancelButton = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Width = 92,
            Height = 34,
            Margin = Padding.Empty,
        };
        MainWindowTheme.StyleButton(cancelButton);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 5,
            Padding = new Padding(all: 22),
            BackColor = MainWindowTheme.Background,
        };

        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, width: 126));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, width: 100));

        root.RowStyles.Add(new RowStyle(SizeType.Absolute, height: 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, height: 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, height: 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, height: 42));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, height: 100));

        root.Controls.Add(this.CreateLabel("Display name"), column: 0, row: 0);
        root.Controls.Add(this.displayNameTextBox, column: 1, row: 0);

        root.Controls.Add(this.CreateLabel("Login name"), column: 0, row: 1);
        root.Controls.Add(this.loginNameTextBox, column: 1, row: 1);

        root.Controls.Add(this.CreateLabel("Password"), column: 0, row: 2);
        root.Controls.Add(this.passwordTextBox, column: 1, row: 2);

        root.Controls.Add(this.clearPasswordCheckBox, column: 1, row: 3);

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(left: 0, top: 12, right: 0, bottom: 0),
            BackColor = MainWindowTheme.Background,
        };

        buttonPanel.Controls.Add(saveButton);
        buttonPanel.Controls.Add(cancelButton);

        root.Controls.Add(buttonPanel, column: 0, row: 4);
        root.SetColumnSpan(buttonPanel, value: 2);
        this.Controls.Add(root);

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
            ForeColor = MainWindowTheme.MutedText,
        };
    }

    private void SaveButton_OnClick(object? sender, EventArgs e)
    {
        var displayName = this.displayNameTextBox.Text.Trim();
        var loginName = this.loginNameTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(displayName))
        {
            ThemedMessageDialog.ShowWarning(
                this,
                "Account",
                "Display name is required.");

            this.DialogResult = DialogResult.None;
            return;
        }

        if (string.IsNullOrWhiteSpace(loginName))
        {
            ThemedMessageDialog.ShowWarning(
                this,
                "Account",
                "Login name is required.");

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
            this.account.ProtectedPassword =
                PasswordProtector.Protect(this.passwordTextBox.Text);
        }
    }
}
