// ReSharper disable LocalizableElement
// ReSharper disable AsyncVoidEventHandlerMethod
namespace Net7ClientManager.Forms;

using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Net7ClientManager.Core;
using Net7ClientManager.Services;

internal sealed class HelpCenterForm : ThemedForm
{
    private static readonly JsonSerializerOptions webJsonOptions =
        new()
        {
            PropertyNameCaseInsensitive = true,
        };

    private readonly Func<IReadOnlyList<HelpClientContext>>
        resolveContexts;
    private readonly Func<HelpActionRequest, Task<HelpActionResponse?>>
        handleActionAsync;
    private readonly IReadOnlyList<HelpArticle> articles =
        HelpArticleCatalog.Create();
    private readonly TextBox searchTextBox = new();
    private readonly ListBox topicListBox = new();
    private readonly ComboBox contextComboBox = new();
    private readonly WebView2 webView = new();
    private readonly Label browserStatusLabel = new();
    private readonly System.Windows.Forms.Timer contextRefreshTimer = new();
    private readonly WindowPlacementBinding windowPlacement;

    private string currentTopicId = HelpTopicIds.Home;
    private HelpActionResponse? currentResponse;
    private bool browserInitialized;
    private bool disposed;

    internal HelpCenterForm(
        ClientManager clientManager,
        Func<IReadOnlyList<HelpClientContext>> resolveContexts,
        Func<HelpActionRequest, Task<HelpActionResponse?>>
            handleActionAsync,
        IWin32Window preferredOwner)
    {
        this.resolveContexts = resolveContexts ??
            throw new ArgumentNullException(nameof(resolveContexts));
        this.handleActionAsync = handleActionAsync ??
            throw new ArgumentNullException(nameof(handleActionAsync));

        this.Text = "Help & Assistance";
        this.Icon = ResourceLoader.Net7ClientManagerIcon;
        this.StartPosition = FormStartPosition.CenterParent;
        this.Size = new Size(width: 1260, height: 820);
        this.MinimumSize = new Size(width: 980, height: 650);
        this.BackColor = MainWindowTheme.Background;
        this.ForeColor = MainWindowTheme.Text;
        this.Font = MainWindowTheme.CreateBodyFont();
        this.ConfigureWindowChrome(
            allowResize: true,
            showMinimizeButton: true,
            showMaximizeButton: true,
            showHelpButton: false);

        this.BuildUi();
        this.WireEvents();
        this.windowPlacement = clientManager.BindGlobalWindowPlacement(
            this,
            WindowPlacementIds.HelpCenter,
            preferredOwner);

        this.RefreshContexts();
        this.RefreshTopicList();
        this.contextRefreshTimer.Interval = 1500;
        this.contextRefreshTimer.Start();
    }

    internal void ShowTopic(
        string? topicId,
        int? processId = null)
    {
        if (!string.IsNullOrWhiteSpace(topicId) &&
            this.articles.Any(article => string.Equals(
                article.Id,
                topicId,
                StringComparison.Ordinal)))
        {
            this.currentTopicId = topicId;
        }
        else
        {
            this.currentTopicId = HelpTopicIds.Home;
        }

        if (processId.HasValue)
        {
            this.SelectProcess(processId.Value);
        }

        this.currentResponse = null;
        this.SelectCurrentArticleInList();
        this.RenderCurrentArticle();
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        await this.EnsureBrowserInitializedAsync();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        this.contextRefreshTimer.Stop();
        this.UnwireEvents();
        this.windowPlacement.Dispose();
        base.OnFormClosed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !this.disposed)
        {
            this.disposed = true;

            if (this.webView.CoreWebView2 != null)
            {
                this.webView.CoreWebView2.WebMessageReceived -=
                    this.CoreWebView2_OnWebMessageReceived;
                this.webView.CoreWebView2.NewWindowRequested -=
                    this.CoreWebView2_OnNewWindowRequested;
                this.webView.CoreWebView2.ProcessFailed -=
                    this.CoreWebView2_OnProcessFailed;
            }

            this.contextRefreshTimer.Dispose();
            this.webView.Dispose();
        }

        base.Dispose(disposing);
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = MainWindowTheme.Background,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 286f));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        root.Controls.Add(this.CreateNavigationPanel(), 0, 0);
        root.Controls.Add(this.CreateContentPanel(), 1, 0);
        this.Controls.Add(root);
    }

    private Control CreateNavigationPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            BackColor = MainWindowTheme.Header,
            Padding = new Padding(16, 18, 16, 16),
            Margin = Padding.Empty,
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 48f));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 54f));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        panel.Controls.Add(
            new Label
            {
                Dock = DockStyle.Fill,
                Text = "HELP & ASSISTANCE",
                Font = MainWindowTheme.CreateHeadingFont(size: 11f),
                ForeColor = MainWindowTheme.Accent,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = Padding.Empty,
            },
            0,
            0);

        this.searchTextBox.Dock = DockStyle.Fill;
        this.searchTextBox.PlaceholderText = "Search help";
        this.searchTextBox.Margin = new Padding(0, 4, 0, 8);
        MainWindowTheme.StyleTextBox(this.searchTextBox);
        panel.Controls.Add(this.searchTextBox, 0, 1);

        panel.Controls.Add(
            new Label
            {
                Dock = DockStyle.Fill,
                Text = "HELP FOR",
                Font = MainWindowTheme.CreateBodyFont(size: 8f),
                ForeColor = MainWindowTheme.MutedText,
                TextAlign = ContentAlignment.BottomLeft,
                Margin = Padding.Empty,
            },
            0,
            2);

        this.contextComboBox.Dock = DockStyle.Fill;
        this.contextComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        this.contextComboBox.Margin = new Padding(0, 4, 0, 8);
        MainWindowTheme.StyleComboBox(this.contextComboBox);
        panel.Controls.Add(this.contextComboBox, 0, 3);

        panel.Controls.Add(
            new Label
            {
                Dock = DockStyle.Fill,
                Text = "Some help pages can inspect a specific client. Choose it here before running a check.",
                Font = MainWindowTheme.CreateBodyFont(size: 8.2f),
                ForeColor = MainWindowTheme.MutedText,
                TextAlign = ContentAlignment.TopLeft,
                Margin = new Padding(0, 0, 0, 8),
                AutoEllipsis = true,
            },
            0,
            4);

        this.topicListBox.Dock = DockStyle.Fill;
        this.topicListBox.BorderStyle = BorderStyle.None;
        this.topicListBox.BackColor = MainWindowTheme.Header;
        this.topicListBox.ForeColor = MainWindowTheme.Text;
        this.topicListBox.Font = MainWindowTheme.CreateBodyFont(size: 9.5f);
        this.topicListBox.IntegralHeight = false;
        this.topicListBox.ItemHeight = 30;
        this.topicListBox.Margin = new Padding(0, 8, 0, 0);
        panel.Controls.Add(this.topicListBox, 0, 5);

        return panel;
    }

    private Control CreateContentPanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = MainWindowTheme.Background,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };

        this.webView.Dock = DockStyle.Fill;
        this.webView.DefaultBackgroundColor = MainWindowTheme.Background;
        this.webView.Visible = false;

        this.browserStatusLabel.Dock = DockStyle.Fill;
        this.browserStatusLabel.Text = "Preparing Help & Assistance...";
        this.browserStatusLabel.ForeColor = MainWindowTheme.MutedText;
        this.browserStatusLabel.BackColor = MainWindowTheme.Background;
        this.browserStatusLabel.TextAlign = ContentAlignment.MiddleCenter;
        this.browserStatusLabel.Padding = new Padding(30);

        panel.Controls.Add(this.webView);
        panel.Controls.Add(this.browserStatusLabel);
        return panel;
    }

    private void WireEvents()
    {
        this.searchTextBox.TextChanged +=
            this.SearchTextBox_OnTextChanged;
        this.topicListBox.SelectedIndexChanged +=
            this.TopicListBox_OnSelectedIndexChanged;
        this.contextComboBox.SelectedIndexChanged +=
            this.ContextComboBox_OnSelectedIndexChanged;
        this.contextRefreshTimer.Tick +=
            this.ContextRefreshTimer_OnTick;
    }

    private void UnwireEvents()
    {
        this.searchTextBox.TextChanged -=
            this.SearchTextBox_OnTextChanged;
        this.topicListBox.SelectedIndexChanged -=
            this.TopicListBox_OnSelectedIndexChanged;
        this.contextComboBox.SelectedIndexChanged -=
            this.ContextComboBox_OnSelectedIndexChanged;
        this.contextRefreshTimer.Tick -=
            this.ContextRefreshTimer_OnTick;
    }

    private async Task EnsureBrowserInitializedAsync()
    {
        if (this.browserInitialized || this.disposed)
        {
            return;
        }

        try
        {
            var environment =
                await MissionWikiBrowserEnvironment.GetAsync()
                    .ConfigureAwait(true);
            await this.webView
                .EnsureCoreWebView2Async(environment)
                .ConfigureAwait(true);

            if (this.disposed)
            {
                return;
            }

            var core = this.webView.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = true;
            core.WebMessageReceived +=
                this.CoreWebView2_OnWebMessageReceived;
            core.NewWindowRequested +=
                this.CoreWebView2_OnNewWindowRequested;
            core.ProcessFailed +=
                this.CoreWebView2_OnProcessFailed;

            this.browserInitialized = true;
            this.browserStatusLabel.Visible = false;
            this.webView.Visible = true;
            this.RenderCurrentArticle();
        }
        catch (Exception ex)
        {
            this.browserStatusLabel.Text = string.Concat(
                "Help content could not be displayed.\n\n",
                ex.Message);
            this.browserStatusLabel.ForeColor = MainWindowTheme.Warning;
        }
    }

    private void RefreshContexts()
    {
        var previousKey =
            (this.contextComboBox.SelectedItem as HelpClientContext)?.Key;
        var contexts = this.resolveContexts();
        var signature = string.Join(
            "\u001f",
            contexts.Select(context => string.Concat(
                context.Key,
                "|",
                context.DisplayName,
                "|",
                context.ProcessId,
                "|",
                context.SlotId)));
        var existingSignature = string.Join(
            "\u001f",
            this.contextComboBox.Items
                .Cast<HelpClientContext>()
                .Select(context => string.Concat(
                    context.Key,
                    "|",
                    context.DisplayName,
                    "|",
                    context.ProcessId,
                    "|",
                    context.SlotId)));

        if (string.Equals(
                signature,
                existingSignature,
                StringComparison.Ordinal))
        {
            return;
        }

        this.contextComboBox.BeginUpdate();
        try
        {
            this.contextComboBox.Items.Clear();
            this.contextComboBox.Items.AddRange(contexts.Cast<object>().ToArray());

            var selected = contexts.FirstOrDefault(context =>
                string.Equals(
                    context.Key,
                    previousKey,
                    StringComparison.Ordinal)) ??
                contexts.FirstOrDefault();

            if (selected != null)
            {
                this.contextComboBox.SelectedItem = selected;
            }
        }
        finally
        {
            this.contextComboBox.EndUpdate();
        }
    }

    private void SelectProcess(int processId)
    {
        this.RefreshContexts();

        var context = this.contextComboBox.Items
            .Cast<HelpClientContext>()
            .FirstOrDefault(candidate =>
                candidate.ProcessId == processId);

        if (context != null)
        {
            this.contextComboBox.SelectedItem = context;
        }
    }

    private void RefreshTopicList()
    {
        var search = this.searchTextBox.Text.Trim();
        var filtered = this.articles
            .Where(article =>
                search.Length == 0 ||
                Contains(article.Title, search) ||
                Contains(article.Summary, search) ||
                article.Keywords.Any(keyword => Contains(keyword, search)))
            .ToArray();

        this.topicListBox.BeginUpdate();
        try
        {
            this.topicListBox.Items.Clear();
            this.topicListBox.Items.AddRange(filtered.Cast<object>().ToArray());
            this.topicListBox.DisplayMember = nameof(HelpArticle.Title);
        }
        finally
        {
            this.topicListBox.EndUpdate();
        }

        this.SelectCurrentArticleInList();
    }

    private void SelectCurrentArticleInList()
    {
        var article = this.topicListBox.Items
            .Cast<HelpArticle>()
            .FirstOrDefault(candidate => string.Equals(
                candidate.Id,
                this.currentTopicId,
                StringComparison.Ordinal));

        if (article != null)
        {
            this.topicListBox.SelectedItem = article;
        }
    }

    private void RenderCurrentArticle()
    {
        if (!this.browserInitialized ||
            this.disposed)
        {
            return;
        }

        var article = this.articles.FirstOrDefault(candidate =>
            string.Equals(
                candidate.Id,
                this.currentTopicId,
                StringComparison.Ordinal)) ?? this.articles[0];
        var context = this.SelectedContext;
        var html = HelpHtmlRenderer.Render(
            article,
            this.articles,
            context,
            this.currentResponse);

        this.webView.NavigateToString(html);
    }

    private HelpClientContext SelectedContext =>
        this.contextComboBox.SelectedItem as HelpClientContext ??
        new HelpClientContext(
            "general",
            "General help");

    private async void CoreWebView2_OnWebMessageReceived(
        object? sender,
        CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (this.disposed)
        {
            return;
        }

        HelpWebMessage? message;

        try
        {
            message = JsonSerializer.Deserialize<HelpWebMessage>(
                e.WebMessageAsJson,
                webJsonOptions);
        }
        catch
        {
            return;
        }

        if (message == null ||
            string.IsNullOrWhiteSpace(message.Action))
        {
            return;
        }

        if (string.Equals(
                message.Action,
                "topic",
                StringComparison.Ordinal))
        {
            this.ShowTopic(message.Value);
            return;
        }

        this.currentResponse = new HelpActionResponse(
            HelpResultKind.Information,
            "Checking your setup",
            "Client Manager is inspecting the selected context...");
        this.RenderCurrentArticle();

        try
        {
            this.currentResponse = await this.handleActionAsync(
                new HelpActionRequest(
                    message.Action,
                    this.SelectedContext));
        }
        catch (Exception ex)
        {
            this.currentResponse = new HelpActionResponse(
                HelpResultKind.Error,
                "Assistance could not complete the check",
                ex.Message);
        }

        this.RenderCurrentArticle();
    }


    private void CoreWebView2_OnProcessFailed(
        object? sender,
        CoreWebView2ProcessFailedEventArgs e)
    {
        if (this.disposed)
        {
            return;
        }

        this.browserInitialized = false;
        this.webView.Visible = false;
        this.browserStatusLabel.Text = string.Concat(
            "Help content stopped unexpectedly.\n\n",
            "Close Help and open it again. If the problem returns, ",
            "restart Client Manager.");
        this.browserStatusLabel.ForeColor = MainWindowTheme.Warning;
        this.browserStatusLabel.Visible = true;
    }

    private void CoreWebView2_OnNewWindowRequested(
        object? sender,
        CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
    }

    private void SearchTextBox_OnTextChanged(
        object? sender,
        EventArgs e)
    {
        this.RefreshTopicList();
    }

    private void TopicListBox_OnSelectedIndexChanged(
        object? sender,
        EventArgs e)
    {
        if (this.topicListBox.SelectedItem is not HelpArticle article ||
            string.Equals(
                this.currentTopicId,
                article.Id,
                StringComparison.Ordinal))
        {
            return;
        }

        this.currentTopicId = article.Id;
        this.currentResponse = null;
        this.RenderCurrentArticle();
    }

    private void ContextComboBox_OnSelectedIndexChanged(
        object? sender,
        EventArgs e)
    {
        this.currentResponse = null;
        this.RenderCurrentArticle();
    }

    private void ContextRefreshTimer_OnTick(
        object? sender,
        EventArgs e)
    {
        this.RefreshContexts();
    }

    private static bool Contains(
        string? value,
        string search)
    {
        return value?.Contains(
            search,
            StringComparison.OrdinalIgnoreCase) == true;
    }

    private sealed record HelpWebMessage(
        string Action,
        string? Value);
}
