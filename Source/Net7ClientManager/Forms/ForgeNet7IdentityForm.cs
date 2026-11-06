namespace Net7ClientManager.Forms;

using Net7ClientManager.Contributions;
using Net7ClientManager.Net7Site;

internal sealed class ForgeNet7IdentityForm : ThemedForm
{
    private readonly TextBox user = new();
    private readonly TextBox password = new() { UseSystemPasswordChar = true };
    private readonly CheckBox remember = new ThemedCheckBox { Text = "Remember Net-7 login on this PC", AutoSize = true };
    private readonly ComboBox accounts = new() { DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = nameof(Net7SiteGameAccount.LoginName) };
    private readonly ComboBox action = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox recovery = new() { UseSystemPasswordChar = true, PlaceholderText = "N7F-... (for recovery only)" };
    private readonly Button read = new() { Text = "Sign in and read accounts", AutoSize = true };
    private readonly Button verify = new() { Text = "Verify and connect", AutoSize = true, Enabled = false };
    private readonly Label status = new() { AutoSize = true, MaximumSize = new Size(610, 0) };
    private readonly Net7SiteSessionStore sessionStore;
    private readonly Func<string, int, string, string?, CancellationToken, Task<Net7IdentityResponse>> submit;
    private readonly CancellationTokenSource cancellation = new();
    private Net7SiteSession? session;
    private string? sessionCookie;
    private bool busy;

    public ForgeNet7IdentityForm(bool hasIdentity,
        Func<string, int, string, string?, CancellationToken, Task<Net7IdentityResponse>> submit)
        : this(hasIdentity, submit, new Net7SiteSessionStore())
    {
    }

    internal ForgeNet7IdentityForm(bool hasIdentity,
        Func<string, int, string, string?, CancellationToken, Task<Net7IdentityResponse>> submit,
        Net7SiteSessionStore sessionStore)
    {
        this.submit = submit;
        this.sessionStore = sessionStore;
        this.session = sessionStore.Load();
        this.Text = "Connect Forge with Net-7";
        this.ClientSize = new Size(700, 650);
        this.StartPosition = FormStartPosition.CenterParent;
        this.ShowInTaskbar = false;
        this.BackColor = MainWindowTheme.Background;
        this.ForeColor = MainWindowTheme.Text;
        this.Font = MainWindowTheme.CreateBodyFont();
        this.ConfigureWindowChrome(allowResize: true, showMinimizeButton: false, showMaximizeButton: false);
        this.MinimumSize = new Size(650, 620);
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(22), ColumnCount = 2, RowCount = 11, AutoScroll = true };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var intro = new Label
        {
            Text = "Your password goes only to Net-7. On Verify, Forge temporarily receives your Net-7 session to check the selected game account. Forge does not store that session. Recovery also requires your saved Forge recovery code.",
            AutoSize = true, MaximumSize = new Size(610, 0), Margin = new Padding(0, 0, 0, 14),
        };
        panel.Controls.Add(intro, 0, 0); panel.SetColumnSpan(intro, 2);
        this.AddRow(panel, 1, "Net-7 username", this.user);
        this.AddRow(panel, 2, "Net-7 password", this.password);
        panel.Controls.Add(this.remember, 1, 3);
        panel.Controls.Add(this.read, 1, 4);
        this.AddRow(panel, 5, "Recovery account", this.accounts);
        this.AddRow(panel, 6, "Action", this.action);
        this.AddRow(panel, 7, "Recovery code", this.recovery);
        panel.Controls.Add(this.verify, 1, 8);
        panel.Controls.Add(this.status, 0, 9); panel.SetColumnSpan(this.status, 2);
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        panel.Controls.Add(cancel, 1, 10);
        foreach (var button in new[] { this.read, this.verify, cancel }) MainWindowTheme.StyleButton(button);
        foreach (var box in new[] { this.user, this.password, this.recovery }) MainWindowTheme.StyleTextBox(box);
        MainWindowTheme.StyleComboBox(this.accounts); MainWindowTheme.StyleComboBox(this.action);
        this.Controls.Add(panel); this.CancelButton = cancel;
        if (hasIdentity) this.action.Items.Add("Link / replace recovery code");
        else this.action.Items.Add("Create new Forge identity");
        this.action.Items.Add("Recover using saved code");
        this.action.SelectedIndex = 0;
        this.action.SelectedIndexChanged += (_, _) => this.UpdateState();
        this.accounts.SelectedIndexChanged += (_, _) => this.UpdateState();
        this.read.Click += this.Read_OnClick;
        this.verify.Click += this.Verify_OnClick;
        this.user.Text = this.session?.UserName ?? "";
        this.remember.Checked = this.session != null;
        this.password.PlaceholderText = this.session == null ? "Forum password" : "Leave blank to reuse remembered login";
        this.UpdateState();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Do not discard a one-time recovery code while verification is in flight.
        if (this.busy) { e.Cancel = true; return; }
        this.cancellation.Cancel();
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) this.cancellation.Dispose();
        base.Dispose(disposing);
    }

    private void AddRow(TableLayoutPanel panel, int row, string label, Control control)
    {
        panel.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 10, 8, 10) }, 0, row);
        control.Anchor = AnchorStyles.Left | AnchorStyles.Right; control.Margin = new Padding(0, 6, 0, 6);
        panel.Controls.Add(control, 1, row);
    }

    private void UpdateState()
    {
        this.read.Enabled = !this.busy;
        this.user.Enabled = this.password.Enabled = this.remember.Enabled = !this.busy;
        this.accounts.Enabled = this.action.Enabled = !this.busy;
        this.recovery.Enabled = !this.busy && this.action.SelectedIndex == 1;
        this.verify.Enabled = !this.busy && this.sessionCookie != null && this.accounts.SelectedItem is Net7SiteGameAccount;
    }

    private async void Read_OnClick(object? sender, EventArgs e)
    {
        this.busy = true; this.sessionCookie = null; this.accounts.Items.Clear(); this.UpdateState();
        this.status.Text = "Reading your Net-7 accounts…";
        try
        {
            using var client = new Net7SiteClient();
            if (this.password.Text.Length == 0 && this.session != null &&
                string.Equals(this.session.UserName, this.user.Text.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                client.RestoreCookies(this.session.Cookies);
            }
            else
            {
                if (string.IsNullOrWhiteSpace(this.user.Text) || this.password.Text.Length == 0)
                    throw new Net7SiteException("Enter your Net-7 username and password.");
                await client.SignInAsync(this.user.Text.Trim(), this.password.Text, this.remember.Checked, this.cancellation.Token);
            }

            var roster = await client.GetRosterAsync(null, this.cancellation.Token);
            var cookies = client.ExportCookies();
            this.sessionCookie = cookies.SingleOrDefault(c => c.Name == "PHPSESSID")?.Value
                ?? throw new Net7SiteException("Net-7 did not provide the expected session cookie.");
            this.session = new Net7SiteSession(this.user.Text.Trim(), cookies);
            if (this.remember.Checked) this.sessionStore.Save(this.session); else this.sessionStore.Delete();
            this.password.Clear();
            this.accounts.Items.AddRange(roster.Cast<object>().ToArray());
            this.status.Text = "Select the game account you will use for recovery. Keep access to this account and save the recovery code Forge issues.";
        }
        catch (Net7SiteException exception) when (exception.IsSignedOut)
        {
            this.session = null; this.sessionCookie = null; this.sessionStore.Delete();
            this.status.Text = "The remembered login expired. Enter your Net-7 password and read again.";
        }
        catch (Exception exception) when (exception is Net7SiteException or HttpRequestException or IOException or OperationCanceledException)
        {
            this.sessionCookie = null;
            this.status.Text = "Could not read Net-7 accounts. " + exception.Message;
        }
        finally { this.busy = false; this.UpdateState(); }
    }

    private async void Verify_OnClick(object? sender, EventArgs e)
    {
        if (this.accounts.SelectedItem is not Net7SiteGameAccount account || this.sessionCookie == null) return;
        var recover = this.action.SelectedIndex == 1;
        if (recover && string.IsNullOrWhiteSpace(this.recovery.Text)) { this.status.Text = "Enter your saved Forge recovery code."; return; }
        this.busy = true; this.UpdateState(); this.status.Text = "Forge is verifying your Net-7 account…";
        try
        {
            var operation = recover ? "recover" : this.action.Items[0]!.ToString()!.StartsWith("Link", StringComparison.Ordinal) ? "link" : "create";
            var result = await this.submit(operation, account.AccountId, this.sessionCookie, recover ? this.recovery.Text : null, this.cancellation.Token);
            this.recovery.Clear();
            using var code = new ForgeRecoveryCodeForm(result.RecoveryCode, account.LoginName);
            code.ShowDialog(this);
            this.busy = false;
            this.DialogResult = DialogResult.OK;
            this.Close();
        }
        catch (Exception exception) when (exception is ForgeIdentityException or ForgeIdentityApiException or HttpRequestException or IOException or OperationCanceledException)
        {
            this.status.Text = "Forge connection failed. " + exception.Message;
        }
        finally { this.busy = false; if (!this.IsDisposed) this.UpdateState(); }
    }
}

internal sealed class ForgeRecoveryCodeForm : ThemedForm
{
    public ForgeRecoveryCodeForm(string code, string accountName)
    {
        this.Text = "Save your Forge recovery code";
        this.ClientSize = new Size(680, 340);
        this.StartPosition = FormStartPosition.CenterParent;
        this.ShowInTaskbar = false;
        this.BackColor = MainWindowTheme.Background; this.ForeColor = MainWindowTheme.Text;
        this.Font = MainWindowTheme.CreateBodyFont();
        this.ConfigureWindowChrome(allowResize: false, showMinimizeButton: false, showMaximizeButton: false);
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(22), FlowDirection = FlowDirection.TopDown, WrapContents = false };
        panel.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(620, 0), Text = "Keep this code somewhere separate from this installation, such as a password manager. Recovery needs this code and Net-7 access to game account " + accountName + ". This replaces your previous Forge recovery code; it is shown only now." });
        var text = new TextBox { ReadOnly = true, Multiline = true, Width = 620, Height = 65, Text = code, Margin = new Padding(0, 15, 0, 10) };
        MainWindowTheme.StyleTextBox(text); panel.Controls.Add(text);
        var copy = new Button { Text = "Copy recovery code", AutoSize = true };
        MainWindowTheme.StyleButton(copy);
        copy.Click += (_, _) => { try { Clipboard.SetText(code); } catch (System.Runtime.InteropServices.ExternalException) { text.SelectAll(); text.Focus(); } };
        panel.Controls.Add(copy);
        var saved = new ThemedCheckBox { Text = "I have saved this recovery code", AutoSize = true };
        panel.Controls.Add(saved);
        var done = new Button { Text = "Done", DialogResult = DialogResult.OK, Enabled = false, AutoSize = true };
        MainWindowTheme.StyleButton(done); panel.Controls.Add(done);
        saved.CheckedChanged += (_, _) => done.Enabled = saved.Checked;
        this.FormClosing += (_, e) => { if (!saved.Checked) e.Cancel = true; };
        this.Controls.Add(panel);
    }
}
