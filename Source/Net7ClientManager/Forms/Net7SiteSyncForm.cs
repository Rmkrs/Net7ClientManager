// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using System.Globalization;
using System.Text;
using Net7ClientManager.Core;
using Net7ClientManager.Models;
using Net7ClientManager.Net7Site;

// Reads the game accounts and characters of one Net-7.org login and shows what would change.
// Nothing is changed until the user clicks "Apply"; the caller then takes SyncResult.
internal sealed class Net7SiteSyncForm : ThemedForm
{
    private readonly TextBox userNameTextBox = new();
    private readonly TextBox passwordTextBox = new();
    private readonly ThemedCheckBox rememberLoginCheckBox = new();
    private readonly Button forgetLoginButton = new();
    private readonly TextBox resultTextBox = new();
    private readonly Button readButton = new();
    private readonly Button applyButton = new();

    private readonly IReadOnlyList<GameAccount> accounts;
    private readonly Net7SiteSessionStore sessionStore = new();
    private readonly CancellationTokenSource cancellation = new();

    private Net7SiteSession? storedSession;
    private bool isBusy;

    public Net7SiteSyncForm(IReadOnlyList<GameAccount> accounts)
    {
        this.accounts = accounts;
        this.storedSession = this.sessionStore.Load();

        this.Text = "Sync from Net-7.org";
        this.Icon = ResourceLoader.Net7ClientManagerIcon;
        this.StartPosition = FormStartPosition.CenterParent;
        this.ShowInTaskbar = false;
        this.ClientSize = new Size(width: 680, height: 600);
        this.BackColor = MainWindowTheme.Background;
        this.ForeColor = MainWindowTheme.Text;
        this.Font = MainWindowTheme.CreateBodyFont();
        this.ConfigureWindowChrome(
            allowResize: false,
            showMinimizeButton: false,
            showMaximizeButton: false);

        this.BuildUi();
        this.ShowStoredSession();
        this.UpdateButtonState();
    }

    public Net7SiteSyncResult? SyncResult { get; private set; }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        this.cancellation.Cancel();
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.cancellation.Dispose();
        }

        base.Dispose(disposing);
    }

    private void BuildUi()
    {
        var introLabel = new Label
        {
            Text = string.Concat(
                "Reads the game accounts and characters of your Net-7.org login from the website's account API ",
                "and updates them here. Only the game accounts on that login are changed, and their characters ",
                "are made to match exactly, including removing characters that no longer exist. ",
                "Game account passwords are not available from Net-7.org and are left as they are."),
            Dock = DockStyle.Fill,
            ForeColor = MainWindowTheme.MutedText,
        };

        MainWindowTheme.StyleTextBox(this.userNameTextBox);
        MainWindowTheme.StyleTextBox(this.passwordTextBox);
        this.userNameTextBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        this.passwordTextBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        this.passwordTextBox.UseSystemPasswordChar = true;

        this.rememberLoginCheckBox.Text = "Remember login (keeps Net-7.org's sign-in cookie, never the password)";
        this.rememberLoginCheckBox.AutoSize = true;
        this.rememberLoginCheckBox.Anchor = AnchorStyles.Left;
        this.rememberLoginCheckBox.ForeColor = MainWindowTheme.Text;

        this.forgetLoginButton.Text = "Forget login";
        this.forgetLoginButton.Width = 110;
        this.forgetLoginButton.Height = 30;
        this.forgetLoginButton.Anchor = AnchorStyles.Right;
        MainWindowTheme.StyleButton(this.forgetLoginButton, danger: true);
        this.forgetLoginButton.Click += this.ForgetLoginButton_OnClick;

        this.resultTextBox.Multiline = true;
        this.resultTextBox.ReadOnly = true;
        this.resultTextBox.ScrollBars = ScrollBars.Vertical;
        this.resultTextBox.WordWrap = true;
        this.resultTextBox.Dock = DockStyle.Fill;
        MainWindowTheme.StyleTextBox(this.resultTextBox);

        this.readButton.Text = "Read from Net-7.org";
        this.readButton.Width = 160;
        this.readButton.Height = 34;
        this.readButton.Margin = new Padding(left: 6, top: 0, right: 0, bottom: 0);
        MainWindowTheme.StyleButton(this.readButton, primary: true);
        this.readButton.Click += this.ReadButton_OnClick;

        this.applyButton.Text = "Apply";
        this.applyButton.Width = 92;
        this.applyButton.Height = 34;
        this.applyButton.Margin = new Padding(left: 6, top: 0, right: 0, bottom: 0);
        MainWindowTheme.StyleButton(this.applyButton, primary: true);
        this.applyButton.Click += this.ApplyButton_OnClick;

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
            RowCount = 6,
            Padding = new Padding(all: 22),
            BackColor = MainWindowTheme.Background,
        };

        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, width: 126));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, width: 100));

        root.RowStyles.Add(new RowStyle(SizeType.Absolute, height: 78));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, height: 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, height: 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, height: 42));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, height: 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, height: 52));

        root.Controls.Add(introLabel, column: 0, row: 0);
        root.SetColumnSpan(introLabel, value: 2);

        root.Controls.Add(this.CreateLabel("Username"), column: 0, row: 1);
        root.Controls.Add(this.userNameTextBox, column: 1, row: 1);

        root.Controls.Add(this.CreateLabel("Password"), column: 0, row: 2);
        root.Controls.Add(this.passwordTextBox, column: 1, row: 2);

        var rememberPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            BackColor = MainWindowTheme.Background,
        };
        rememberPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, width: 100));
        rememberPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        rememberPanel.Controls.Add(this.rememberLoginCheckBox, column: 0, row: 0);
        rememberPanel.Controls.Add(this.forgetLoginButton, column: 1, row: 0);

        root.Controls.Add(rememberPanel, column: 1, row: 3);

        root.Controls.Add(this.resultTextBox, column: 0, row: 4);
        root.SetColumnSpan(this.resultTextBox, value: 2);

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(left: 0, top: 12, right: 0, bottom: 0),
            BackColor = MainWindowTheme.Background,
        };

        buttonPanel.Controls.Add(this.applyButton);
        buttonPanel.Controls.Add(this.readButton);
        buttonPanel.Controls.Add(cancelButton);

        root.Controls.Add(buttonPanel, column: 0, row: 5);
        root.SetColumnSpan(buttonPanel, value: 2);
        this.Controls.Add(root);

        this.AcceptButton = this.readButton;
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

    private void ShowStoredSession()
    {
        if (this.storedSession == null)
        {
            this.passwordTextBox.PlaceholderText = "Your Net-7.org (forum) password";
            this.rememberLoginCheckBox.Checked = false;
            this.resultTextBox.Text = "Enter your Net-7.org (forum) login and click \"Read from Net-7.org\".";
            return;
        }

        this.userNameTextBox.Text = this.storedSession.UserName;
        this.passwordTextBox.PlaceholderText = "Leave empty to use the remembered login";
        this.rememberLoginCheckBox.Checked = true;
        this.resultTextBox.Text = "A remembered login is available. Click \"Read from Net-7.org\".";
    }

    private void UpdateButtonState()
    {
        this.userNameTextBox.Enabled = !this.isBusy;
        this.passwordTextBox.Enabled = !this.isBusy;
        this.rememberLoginCheckBox.Enabled = !this.isBusy;
        this.forgetLoginButton.Visible = this.storedSession != null;
        this.forgetLoginButton.Enabled = !this.isBusy;
        this.readButton.Enabled = !this.isBusy;
        this.applyButton.Enabled = !this.isBusy && this.SyncResult is { Changes.Count: > 0 };
    }

    private void ForgetLoginButton_OnClick(object? sender, EventArgs e)
    {
        this.sessionStore.Delete();
        this.storedSession = null;
        this.passwordTextBox.Text = "";
        this.ShowStoredSession();
        this.UpdateButtonState();
    }

    private async void ReadButton_OnClick(object? sender, EventArgs e)
    {
        var userName = this.userNameTextBox.Text.Trim();
        var password = this.passwordTextBox.Text;
        var rememberLogin = this.rememberLoginCheckBox.Checked;
        var useStoredSession = string.IsNullOrEmpty(password) &&
            this.storedSession != null &&
            string.Equals(this.storedSession.UserName, userName, StringComparison.OrdinalIgnoreCase);

        if (!useStoredSession && (string.IsNullOrWhiteSpace(userName) || string.IsNullOrEmpty(password)))
        {
            ThemedMessageDialog.ShowWarning(
                this,
                "Net-7.org",
                "Enter your Net-7.org username and password.");
            return;
        }

        this.isBusy = true;
        this.SyncResult = null;
        this.UpdateButtonState();

        IProgress<string> progress = new Progress<string>(message =>
        {
            if (!this.IsDisposed)
            {
                this.resultTextBox.Text = message;
            }
        });

        try
        {
            using var client = new Net7SiteClient();
            var token = this.cancellation.Token;

            if (useStoredSession)
            {
                client.RestoreCookies(this.storedSession!.Cookies);
            }
            else
            {
                progress.Report("Signing in to Net-7.org...");
                await client.SignInAsync(userName, password, rememberLogin, token);
            }

            var roster = await client.GetRosterAsync(progress, token);

            if (this.IsDisposed)
            {
                return;
            }

            // Keep the cookies only when the user asked to remember the login.
            if (rememberLogin)
            {
                this.storedSession = new Net7SiteSession(userName, client.ExportCookies());
                this.sessionStore.Save(this.storedSession);
            }
            else
            {
                this.sessionStore.Delete();
                this.storedSession = null;
            }

            this.passwordTextBox.Text = "";
            this.SyncResult = Net7SiteAccountSync.Merge(this.accounts, roster);
            this.resultTextBox.Text = DescribeResult(roster, this.SyncResult);
        }
        catch (OperationCanceledException) when (this.cancellation.IsCancellationRequested)
        {
            return;
        }
        catch (Net7SiteException exception) when (exception.IsSignedOut && useStoredSession)
        {
            this.sessionStore.Delete();
            this.storedSession = null;

            if (!this.IsDisposed)
            {
                this.ShowStoredSession();
                this.resultTextBox.Text = "The remembered login has expired. Enter your Net-7.org password and try again.";
            }
        }
        catch (Exception exception) when (exception is Net7SiteException or HttpRequestException or TaskCanceledException or IOException)
        {
            if (!this.IsDisposed)
            {
                this.resultTextBox.Text = string.Concat(
                    "Could not read from Net-7.org.",
                    Environment.NewLine,
                    Environment.NewLine,
                    exception.Message);
            }
        }
        finally
        {
            this.isBusy = false;

            if (!this.IsDisposed)
            {
                this.UpdateButtonState();
            }
        }
    }

    private void ApplyButton_OnClick(object? sender, EventArgs e)
    {
        if (this.SyncResult is not { Changes.Count: > 0 } result)
        {
            return;
        }

        var removals = result.RemovedCharacterCount;

        if (removals > 0 &&
            !ThemedMessageDialog.Confirm(
                this,
                "Sync from Net-7.org",
                string.Concat(
                    "This removes ",
                    removals.ToString(CultureInfo.InvariantCulture),
                    removals == 1 ? " character" : " characters",
                    " that no longer exist on Net-7.org. Apply the changes?"),
                confirmButtonText: "Apply"))
        {
            return;
        }

        this.DialogResult = DialogResult.OK;
        this.Close();
    }

    private static string DescribeResult(
        IReadOnlyList<Net7SiteGameAccount> roster,
        Net7SiteSyncResult result)
    {
        var text = new StringBuilder();
        var characterCount = roster.Sum(account => account.Avatars.Count);

        text.Append("Net-7.org lists ")
            .Append(roster.Count.ToString(CultureInfo.InvariantCulture))
            .Append(roster.Count == 1 ? " game account" : " game accounts")
            .Append(" with ")
            .Append(characterCount.ToString(CultureInfo.InvariantCulture))
            .Append(characterCount == 1 ? " character." : " characters.")
            .AppendLine()
            .AppendLine();

        if (result.Changes.Count == 0)
        {
            text.AppendLine("Everything already matches. Nothing to change.");
        }
        else
        {
            text.AppendLine("Changes (click Apply to save them):");

            foreach (var change in result.Changes)
            {
                text.Append("  • ").AppendLine(change);
            }
        }

        if (result.UntouchedAccounts.Count > 0)
        {
            text.AppendLine()
                .AppendLine("Not on this Net-7.org login, left unchanged:");

            foreach (var account in result.UntouchedAccounts)
            {
                text.Append("  • ").AppendLine(account);
            }
        }

        return text.ToString();
    }
}
