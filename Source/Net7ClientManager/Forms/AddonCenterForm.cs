// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using System.Diagnostics;
using System.Globalization;
using Net7ClientManager.Addons.Contracts;
using Net7ClientManager.Core;
using Net7ClientManager.Services;
using Net7ClientManager.Win32;

public sealed partial class AddonCenterForm : Form
{
    private const int TitleBarHeight = 34;
    private const int HeaderHeight = 112;
    private const int SuspensionBannerHeight = 104;
    private const int MaxInstalledContentWidth = 1240;
    private const int ResizeBorderThickness = 6;

    private const int WmNcHitTest = 0x0084;
    private const int HtClient = 0x0001;
    private const int HtLeft = 0x000A;
    private const int HtRight = 0x000B;
    private const int HtTop = 0x000C;
    private const int HtTopLeft = 0x000D;
    private const int HtTopRight = 0x000E;
    private const int HtBottom = 0x000F;
    private const int HtBottomLeft = 0x0010;
    private const int HtBottomRight = 0x0011;

    private readonly ClientManager clientManager;
    private readonly int ownerProcessId;
    private readonly WindowPlacementBinding windowPlacement;
    private readonly HostedClientTitleBar titleBar = new();
    private readonly Label contextLabel = new();
    private readonly Label summaryLabel = new();
    private readonly Button suspendButton = new();
    private readonly Panel suspensionPanel = new();
    private readonly Label suspensionTitleLabel = new();
    private readonly Label suspensionDetailLabel = new();
    private readonly Button resumeButton = new();
    private readonly Panel pageHost = new();
    private readonly Dictionary<AddonCenterPage, Button> navigationButtons = [];
    private readonly Dictionary<string, AddonCardControl> cardsByAddonId =
        new(StringComparer.Ordinal);
    private readonly Panel installedPage = new();
    private readonly Panel installedContentPanel = new();
    private readonly Panel discoverPage = new();
    private readonly Panel developPage = new();
    private readonly Panel activityPage = new();
    private readonly TextBox searchTextBox = new();
    private readonly ComboBox filterComboBox = new();
    private readonly Button refreshButton = new();
    private readonly Panel installedScrollPanel = new();
    private readonly TableLayoutPanel installedSections = new();
    private readonly TableLayoutPanel builtInCardsGrid = new();
    private readonly TableLayoutPanel packageCardsGrid = new();
    private readonly Label builtInCountLabel = new();
    private readonly Label packageCountLabel = new();
    private readonly Label emptyStateLabel = new();
    private readonly TextBox activityTextBox = new();
    private readonly System.Windows.Forms.Timer refreshTimer = new();

    private IReadOnlyList<AddonRuntimeStatus> currentStatuses = [];
    private string statusFingerprint = "";
    private string layoutFingerprint = "";
    private string logFingerprint = "";
    private bool operationInProgress;

    public AddonCenterForm(
        ClientManager clientManager,
        int ownerProcessId)
    {
        this.clientManager = clientManager;
        this.ownerProcessId = ownerProcessId;

        this.SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer,
            value: true);

        this.Text = "Net7 Addon Center";
        this.Icon = ResourceLoader.Net7ClientManagerIcon;
        this.StartPosition = FormStartPosition.CenterParent;
        this.FormBorderStyle = FormBorderStyle.None;
        this.MinimumSize = new Size(980, 680);
        this.Size = new Size(1240, 820);
        this.Padding = new Padding(1);
        this.BackColor = AddonCenterTheme.Border;
        this.ForeColor = AddonCenterTheme.Text;
        this.Font = new Font("Segoe UI", 9.0f);

        this.titleBar.TitleText = "NET7 ADDON CENTER";
        this.titleBar.ShowMaximizeButton = true;
        this.titleBar.AccessibleName = "Net7 Addon Center title bar";
        this.windowPlacement =
            clientManager.BindClientWindowPlacement(
                this,
                WindowPlacementIds.AddonCenter,
                ownerProcessId);

        this.BuildUi();
        this.WireEvents();

        this.refreshTimer.Interval = 750;
        this.refreshTimer.Tick += this.RefreshTimer_OnTick;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmNcHitTest &&
            this.WindowState == FormWindowState.Normal)
        {
            base.WndProc(ref m);

            if (m.Result.ToInt32() == HtClient)
            {
                var cursor = this.PointToClient(Cursor.Position);
                var onLeft = cursor.X <= ResizeBorderThickness;
                var onRight = cursor.X >=
                              this.ClientSize.Width - ResizeBorderThickness;
                var onTop = cursor.Y <= ResizeBorderThickness;
                var onBottom = cursor.Y >=
                               this.ClientSize.Height - ResizeBorderThickness;

                m.Result = (onLeft, onRight, onTop, onBottom) switch
                {
                    (true, _, true, _) => new IntPtr(HtTopLeft),
                    (_, true, true, _) => new IntPtr(HtTopRight),
                    (true, _, _, true) => new IntPtr(HtBottomLeft),
                    (_, true, _, true) => new IntPtr(HtBottomRight),
                    (true, _, _, _) => new IntPtr(HtLeft),
                    (_, true, _, _) => new IntPtr(HtRight),
                    (_, _, true, _) => new IntPtr(HtTop),
                    (_, _, _, true) => new IntPtr(HtBottom),
                    _ => m.Result,
                };
            }

            return;
        }

        base.WndProc(ref m);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        this.refreshTimer.Stop();
        this.UnwireEvents();

        foreach (var card in this.cardsByAddonId.Values)
        {
            card.Dispose();
        }

        this.cardsByAddonId.Clear();
        this.DisposeDiscoverCards();
        this.windowPlacement.Dispose();
        base.OnFormClosed(e);
    }

    private void BuildUi()
    {
        var chrome = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = AddonCenterTheme.Border,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            RowStyles =
            {
                new RowStyle(SizeType.Absolute, TitleBarHeight),
                new RowStyle(SizeType.Percent, 100),
            },
        };

        this.titleBar.Dock = DockStyle.Fill;
        this.titleBar.Margin = Padding.Empty;

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = AddonCenterTheme.Background,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            RowStyles =
            {
                new RowStyle(SizeType.Absolute, HeaderHeight),
                new RowStyle(SizeType.Absolute, 0),
                new RowStyle(SizeType.Percent, 100),
            },
        };

        content.Controls.Add(this.BuildHeader(), 0, 0);
        content.Controls.Add(this.BuildSuspensionBanner(), 0, 1);
        content.Controls.Add(this.BuildBody(), 0, 2);

        chrome.Controls.Add(this.titleBar, 0, 0);
        chrome.Controls.Add(content, 0, 1);
        this.Controls.Add(chrome);
    }

    private Control BuildHeader()
    {
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = AddonCenterTheme.ElevatedPanel,
            Padding = new Padding(22, 12, 22, 10),
            ColumnCount = 2,
            RowCount = 1,
            ColumnStyles =
            {
                new ColumnStyle(SizeType.Percent, 100),
                new ColumnStyle(SizeType.Absolute, 248),
            },
        };

        var textLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            RowStyles =
            {
                new RowStyle(SizeType.Absolute, 38),
                new RowStyle(SizeType.Absolute, 22),
                new RowStyle(SizeType.Absolute, 22),
            },
        };

        var titleLabel = new Label
        {
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            Text = "ADDON CENTER",
            Font = new Font("Segoe UI", 18.0f, FontStyle.Bold),
            ForeColor = AddonCenterTheme.Text,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = Padding.Empty,
        };

        this.contextLabel.Dock = DockStyle.Fill;
        this.contextLabel.AutoEllipsis = true;
        this.contextLabel.Font = new Font("Segoe UI", 9.25f, FontStyle.Bold);
        this.contextLabel.ForeColor = AddonCenterTheme.Accent;
        this.contextLabel.TextAlign = ContentAlignment.MiddleLeft;
        this.contextLabel.Margin = new Padding(1, 0, 0, 0);

        this.summaryLabel.Dock = DockStyle.Fill;
        this.summaryLabel.AutoEllipsis = true;
        this.summaryLabel.Font = new Font("Segoe UI", 8.75f);
        this.summaryLabel.ForeColor = AddonCenterTheme.MutedText;
        this.summaryLabel.TextAlign = ContentAlignment.MiddleLeft;
        this.summaryLabel.Margin = new Padding(1, 0, 0, 0);

        textLayout.Controls.Add(titleLabel, 0, 0);
        textLayout.Controls.Add(this.contextLabel, 0, 1);
        textLayout.Controls.Add(this.summaryLabel, 0, 2);

        AddonCenterTheme.StyleButton(
            this.suspendButton,
            AddonCenterTheme.Warning);
        this.suspendButton.Text = "Suspend all for this session";
        this.suspendButton.Dock = DockStyle.Fill;
        this.suspendButton.Margin = new Padding(16, 20, 0, 20);

        header.Controls.Add(textLayout, 0, 0);
        header.Controls.Add(this.suspendButton, 1, 0);
        return header;
    }

    private Control BuildSuspensionBanner()
    {
        this.suspensionPanel.Dock = DockStyle.Fill;
        this.suspensionPanel.BackColor = Color.FromArgb(57, 44, 22);
        this.suspensionPanel.Padding = new Padding(18, 10, 18, 10);
        this.suspensionPanel.Visible = false;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            ColumnStyles =
            {
                new ColumnStyle(SizeType.Percent, 100),
                new ColumnStyle(SizeType.Absolute, 180),
            },
        };

        var textLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            RowStyles =
            {
                new RowStyle(SizeType.Absolute, 30),
                new RowStyle(SizeType.Percent, 100),
            },
        };

        this.suspensionTitleLabel.Dock = DockStyle.Fill;
        this.suspensionTitleLabel.Text =
            "ALL ADDONS ARE TEMPORARILY SUSPENDED";
        this.suspensionTitleLabel.Font =
            new Font("Segoe UI", 11.0f, FontStyle.Bold);
        this.suspensionTitleLabel.ForeColor = AddonCenterTheme.Warning;
        this.suspensionTitleLabel.TextAlign = ContentAlignment.MiddleLeft;
        this.suspensionTitleLabel.Margin = Padding.Empty;

        this.suspensionDetailLabel.Dock = DockStyle.Fill;
        this.suspensionDetailLabel.AutoEllipsis = false;
        this.suspensionDetailLabel.Text =
            "No addon will run for any client slot until you resume them. " +
            "Normal per-slot enablement returns automatically the next time " +
            "Net7 Client Manager starts.";
        this.suspensionDetailLabel.Font = new Font("Segoe UI", 8.75f);
        this.suspensionDetailLabel.ForeColor = AddonCenterTheme.Text;
        this.suspensionDetailLabel.TextAlign = ContentAlignment.MiddleLeft;
        this.suspensionDetailLabel.Margin = Padding.Empty;

        AddonCenterTheme.StyleButton(
            this.resumeButton,
            AddonCenterTheme.Success);
        this.resumeButton.Text = "Resume addons";
        this.resumeButton.Dock = DockStyle.Fill;
        this.resumeButton.MinimumSize = new Size(170, 44);
        this.resumeButton.Margin = new Padding(12, 10, 0, 10);

        textLayout.Controls.Add(this.suspensionTitleLabel, 0, 0);
        textLayout.Controls.Add(this.suspensionDetailLabel, 0, 1);
        layout.Controls.Add(textLayout, 0, 0);
        layout.Controls.Add(this.resumeButton, 1, 0);
        this.suspensionPanel.Controls.Add(layout);

        return this.suspensionPanel;
    }

    private Control BuildBody()
    {
        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = AddonCenterTheme.Background,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            ColumnStyles =
            {
                new ColumnStyle(SizeType.Absolute, 192),
                new ColumnStyle(SizeType.Percent, 100),
            },
        };

        body.Controls.Add(this.BuildNavigation(), 0, 0);
        body.Controls.Add(this.BuildPageHost(), 1, 0);
        return body;
    }

    private Control BuildNavigation()
    {
        var navigation = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = AddonCenterTheme.Panel,
            Padding = new Padding(12, 16, 12, 12),
        };

        var pages = new[]
        {
            (AddonCenterPage.Discover, "Discover"),
            (AddonCenterPage.Installed, "Installed"),
            (AddonCenterPage.Develop, "Develop"),
            (AddonCenterPage.Activity, "Activity"),
        };

        var top = 16;

        foreach (var (page, text) in pages)
        {
            var button = new Button
            {
                Text = text,
                Tag = page,
                Size = new Size(160, 42),
                Location = new Point(12, top),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(14, 0, 0, 0),
            };

            AddonCenterTheme.StyleButton(button);
            button.Click += this.NavigationButton_OnClick;
            navigation.Controls.Add(button);
            this.navigationButtons.Add(page, button);
            top += 50;
        }

        var hintLabel = new Label
        {
            Text = "Installation is machine-wide.\n" +
                   "Enablement and window layout are stored per client. Unassigned clients share one unassigned profile.",
            Font = new Font("Segoe UI", 8.25f),
            ForeColor = AddonCenterTheme.MutedText,
            Location = new Point(14, top + 14),
            Size = new Size(154, 76),
            Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right,
        };

        navigation.Controls.Add(hintLabel);
        return navigation;
    }

    private Control BuildPageHost()
    {
        this.pageHost.Dock = DockStyle.Fill;
        this.pageHost.BackColor = AddonCenterTheme.Background;
        this.pageHost.Padding = new Padding(20);

        this.installedPage.Dock = DockStyle.Fill;
        this.discoverPage.Dock = DockStyle.Fill;
        this.developPage.Dock = DockStyle.Fill;
        this.activityPage.Dock = DockStyle.Fill;

        this.BuildInstalledPage();
        this.BuildDiscoverPage();
        this.BuildActivityPage();
        this.BuildDevelopPage();

        this.pageHost.Controls.Add(this.installedPage);
        this.pageHost.Controls.Add(this.discoverPage);
        this.pageHost.Controls.Add(this.developPage);
        this.pageHost.Controls.Add(this.activityPage);

        this.ShowPage(AddonCenterPage.Installed);
        return this.pageHost;
    }

    private void BuildInstalledPage()
    {
        var toolsPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 58,
            BackColor = AddonCenterTheme.Background,
            ColumnCount = 3,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = new Padding(0, 8, 0, 8),
            ColumnStyles =
            {
                new ColumnStyle(SizeType.Percent, 100),
                new ColumnStyle(SizeType.Absolute, 190),
                new ColumnStyle(SizeType.Absolute, 176),
            },
        };

        this.searchTextBox.PlaceholderText =
            "Search installed addons, creators, or descriptions...";
        this.searchTextBox.BackColor = AddonCenterTheme.Button;
        this.searchTextBox.ForeColor = AddonCenterTheme.Text;
        this.searchTextBox.BorderStyle = BorderStyle.FixedSingle;
        this.searchTextBox.Font = new Font("Segoe UI", 9.25f);
        this.searchTextBox.Dock = DockStyle.Fill;
        this.searchTextBox.Margin = new Padding(0, 0, 12, 0);

        this.filterComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        this.filterComboBox.BackColor = AddonCenterTheme.Button;
        this.filterComboBox.ForeColor = AddonCenterTheme.Text;
        this.filterComboBox.FlatStyle = FlatStyle.Flat;
        this.filterComboBox.Font = new Font("Segoe UI", 9.0f);
        this.filterComboBox.Dock = DockStyle.Fill;
        this.filterComboBox.Margin = new Padding(0, 0, 12, 0);
        this.filterComboBox.Items.AddRange(
        [
            "All installed",
            "Enabled for client",
            "Disabled for client",
            "Updates available",
            "Development",
        ]);
        this.filterComboBox.SelectedIndex = 0;

        AddonCenterTheme.StyleButton(this.refreshButton);
        this.refreshButton.Text = "Check for updates";
        this.refreshButton.Dock = DockStyle.Fill;
        this.refreshButton.Margin = Padding.Empty;

        toolsPanel.Controls.Add(this.searchTextBox, 0, 0);
        toolsPanel.Controls.Add(this.filterComboBox, 1, 0);
        toolsPanel.Controls.Add(this.refreshButton, 2, 0);

        this.installedScrollPanel.Dock = DockStyle.Fill;
        this.installedScrollPanel.AutoScroll = true;
        this.installedScrollPanel.BackColor = AddonCenterTheme.Background;
        this.installedScrollPanel.Padding = new Padding(0, 0, 8, 24);

        this.installedSections.AutoSize = true;
        this.installedSections.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        this.installedSections.ColumnCount = 1;
        this.installedSections.RowCount = 4;
        this.installedSections.Dock = DockStyle.Top;
        this.installedSections.Margin = Padding.Empty;
        this.installedSections.Padding = new Padding(0, 0, 0, 20);
        this.installedSections.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 100));

        this.builtInCountLabel.Dock = DockStyle.Fill;
        this.builtInCountLabel.Height = 34;
        this.builtInCountLabel.Font =
            new Font("Segoe UI", 10.5f, FontStyle.Bold);
        this.builtInCountLabel.ForeColor = AddonCenterTheme.Text;
        this.builtInCountLabel.Margin = new Padding(0, 8, 0, 8);

        this.packageCountLabel.Dock = DockStyle.Fill;
        this.packageCountLabel.Height = 34;
        this.packageCountLabel.Font =
            new Font("Segoe UI", 10.5f, FontStyle.Bold);
        this.packageCountLabel.ForeColor = AddonCenterTheme.Text;
        this.packageCountLabel.Margin = new Padding(0, 16, 0, 8);

        ConfigureCardsGrid(this.builtInCardsGrid);
        ConfigureCardsGrid(this.packageCardsGrid);

        this.installedSections.Controls.Add(this.builtInCountLabel, 0, 0);
        this.installedSections.Controls.Add(this.builtInCardsGrid, 0, 1);
        this.installedSections.Controls.Add(this.packageCountLabel, 0, 2);
        this.installedSections.Controls.Add(this.packageCardsGrid, 0, 3);

        this.emptyStateLabel.AutoSize = false;
        this.emptyStateLabel.Text =
            "No installed addons match this search or filter.";
        this.emptyStateLabel.Font = new Font("Segoe UI", 10.0f);
        this.emptyStateLabel.ForeColor = AddonCenterTheme.MutedText;
        this.emptyStateLabel.TextAlign = ContentAlignment.MiddleCenter;
        this.emptyStateLabel.Dock = DockStyle.Top;
        this.emptyStateLabel.Height = 96;
        this.emptyStateLabel.Visible = false;

        this.installedScrollPanel.Controls.Add(this.emptyStateLabel);
        this.installedScrollPanel.Controls.Add(this.installedSections);

        this.installedContentPanel.BackColor = AddonCenterTheme.Background;
        this.installedContentPanel.Controls.Add(this.installedScrollPanel);
        this.installedContentPanel.Controls.Add(toolsPanel);
        this.installedPage.Controls.Add(this.installedContentPanel);
        this.UpdateInstalledContentBounds();
    }

    private void BuildActivityPage()
    {
        var heading = new Label
        {
            Dock = DockStyle.Top,
            Height = 46,
            Text = "RUNTIME ACTIVITY",
            Font = new Font("Segoe UI", 13.0f, FontStyle.Bold),
            ForeColor = AddonCenterTheme.Text,
        };

        this.activityTextBox.Dock = DockStyle.Fill;
        this.activityTextBox.Multiline = true;
        this.activityTextBox.ReadOnly = true;
        this.activityTextBox.ScrollBars = ScrollBars.Both;
        this.activityTextBox.WordWrap = false;
        this.activityTextBox.BackColor = AddonCenterTheme.Panel;
        this.activityTextBox.ForeColor = AddonCenterTheme.Text;
        this.activityTextBox.BorderStyle = BorderStyle.FixedSingle;
        this.activityTextBox.Font =
            new Font(FontFamily.GenericMonospace, 9.0f);

        this.activityPage.Controls.Add(this.activityTextBox);
        this.activityPage.Controls.Add(heading);
    }

    private void WireEvents()
    {
        this.Shown += this.AddonCenterForm_OnShown;
        this.titleBar.DragRequested += this.TitleBar_OnDragRequested;
        this.titleBar.MinimizeRequested += this.TitleBar_OnMinimizeRequested;
        this.titleBar.MaximizeRequested += this.TitleBar_OnMaximizeRequested;
        this.titleBar.CloseRequested += this.TitleBar_OnCloseRequested;
        this.Resize += this.AddonCenterForm_OnResize;
        this.suspendButton.Click += this.SuspendButton_OnClick;
        this.resumeButton.Click += this.ResumeButton_OnClick;
        this.refreshButton.Click += this.RefreshButton_OnClick;
        this.searchTextBox.TextChanged += this.SearchTextBox_OnTextChanged;
        this.filterComboBox.SelectedIndexChanged +=
            this.FilterComboBox_OnSelectedIndexChanged;
        this.installedScrollPanel.Resize +=
            this.InstalledScrollPanel_OnResize;
        this.installedPage.Resize += this.InstalledPage_OnResize;
        this.WireDiscoverEvents();
        this.WireDevelopEvents();
    }

    private void UnwireEvents()
    {
        this.Shown -= this.AddonCenterForm_OnShown;
        this.titleBar.DragRequested -= this.TitleBar_OnDragRequested;
        this.titleBar.MinimizeRequested -= this.TitleBar_OnMinimizeRequested;
        this.titleBar.MaximizeRequested -= this.TitleBar_OnMaximizeRequested;
        this.titleBar.CloseRequested -= this.TitleBar_OnCloseRequested;
        this.Resize -= this.AddonCenterForm_OnResize;
        this.suspendButton.Click -= this.SuspendButton_OnClick;
        this.resumeButton.Click -= this.ResumeButton_OnClick;
        this.refreshButton.Click -= this.RefreshButton_OnClick;
        this.searchTextBox.TextChanged -= this.SearchTextBox_OnTextChanged;
        this.filterComboBox.SelectedIndexChanged -=
            this.FilterComboBox_OnSelectedIndexChanged;
        this.installedScrollPanel.Resize -=
            this.InstalledScrollPanel_OnResize;
        this.installedPage.Resize -= this.InstalledPage_OnResize;
        this.UnwireDiscoverEvents();
        this.UnwireDevelopEvents();
        this.refreshTimer.Tick -= this.RefreshTimer_OnTick;

        foreach (var button in this.navigationButtons.Values)
        {
            button.Click -= this.NavigationButton_OnClick;
        }
    }

    private async void AddonCenterForm_OnShown(
        object? sender,
        EventArgs e)
    {
        this.UpdateInstalledContentBounds();
        this.UpdateDiscoverContentBounds();

        if (this.clientManager.CheckAddonUpdatesAutomatically)
        {
            await this.RefreshCatalogAndViewAsync();
        }
        else
        {
            this.RefreshView(force: true);
        }

        this.refreshTimer.Start();
    }

    private void RefreshTimer_OnTick(
        object? sender,
        EventArgs e)
    {
        if (!this.operationInProgress)
        {
            this.RefreshView();
        }
    }

    private void NavigationButton_OnClick(
        object? sender,
        EventArgs e)
    {
        if (sender is Button { Tag: AddonCenterPage page })
        {
            this.ShowPage(page);
        }
    }

    private async void RefreshButton_OnClick(
        object? sender,
        EventArgs e)
    {
        await this.RefreshCatalogAndViewAsync();
    }

    private void SearchTextBox_OnTextChanged(
        object? sender,
        EventArgs e)
    {
        this.RebuildCardLayout(force: true);
    }

    private void FilterComboBox_OnSelectedIndexChanged(
        object? sender,
        EventArgs e)
    {
        this.RebuildCardLayout(force: true);
    }

    private void InstalledScrollPanel_OnResize(
        object? sender,
        EventArgs e)
    {
        this.RebuildCardLayout(force: true);
    }

    private void InstalledPage_OnResize(
        object? sender,
        EventArgs e)
    {
        this.UpdateInstalledContentBounds();
    }

    private void UpdateInstalledContentBounds()
    {
        var availableSize = this.installedPage.ClientSize;
        var contentWidth = Math.Min(
            MaxInstalledContentWidth,
            Math.Max(0, availableSize.Width));
        var left = Math.Max(0, (availableSize.Width - contentWidth) / 2);

        this.installedContentPanel.SetBounds(
            left,
            0,
            contentWidth,
            Math.Max(0, availableSize.Height));
    }

    private void SuspendButton_OnClick(
        object? sender,
        EventArgs e)
    {
        this.clientManager.SetAddonsSuspendedForSession(
            !this.clientManager.AddonsSuspendedForSession);
        this.RefreshView(force: true);
    }

    private void ResumeButton_OnClick(
        object? sender,
        EventArgs e)
    {
        this.clientManager.SetAddonsSuspendedForSession(suspended: false);
        this.RefreshView(force: true);
    }

    private void TitleBar_OnDragRequested(
        object? sender,
        EventArgs e)
    {
        if (this.WindowState == FormWindowState.Maximized)
        {
            return;
        }

        NativeMethods.ReleaseCapture();
        NativeMethods.SendMoveWindowMessage(this.Handle);
    }

    private void TitleBar_OnMinimizeRequested(
        object? sender,
        EventArgs e)
    {
        this.WindowState = FormWindowState.Minimized;
    }

    private void TitleBar_OnMaximizeRequested(
        object? sender,
        EventArgs e)
    {
        this.WindowState = this.WindowState == FormWindowState.Maximized
            ? FormWindowState.Normal
            : FormWindowState.Maximized;
    }

    private void TitleBar_OnCloseRequested(
        object? sender,
        EventArgs e)
    {
        this.Close();
    }

    private void AddonCenterForm_OnResize(
        object? sender,
        EventArgs e)
    {
        this.titleBar.IsMaximized =
            this.WindowState == FormWindowState.Maximized;
        this.UpdateInstalledContentBounds();
        this.UpdateDiscoverContentBounds();
    }

    private async Task RefreshCatalogAndViewAsync()
    {
        this.SetOperationState(
            inProgress: true,
            "Checking Net7 Forge for addon updates...");

        try
        {
            await this.clientManager.RefreshAddonCatalogAsync();
            this.RefreshView(force: true);
        }
        catch (Exception ex)
        {
            this.summaryLabel.Text = string.Concat(
                "Could not refresh the addon catalog: ",
                ex.Message);
            this.summaryLabel.ForeColor = AddonCenterTheme.Danger;
        }
        finally
        {
            this.SetOperationState(inProgress: false);
        }
    }

    private async Task<bool> RunCommandAsync(
        string operationText,
        Func<Task<AddonCommandResult>> command)
    {
        this.SetOperationState(inProgress: true, operationText);
        var succeeded = false;

        try
        {
            var result = await command();
            succeeded = result.Succeeded;
            this.RefreshView(force: true);

            if (!result.Succeeded)
            {
                this.summaryLabel.Text = result.Error;
                this.summaryLabel.ForeColor = AddonCenterTheme.Danger;
            }
        }
        catch (Exception ex)
        {
            this.summaryLabel.Text = ex.Message;
            this.summaryLabel.ForeColor = AddonCenterTheme.Danger;
        }
        finally
        {
            this.SetOperationState(inProgress: false);
        }

        return succeeded;
    }

    private void RefreshView(bool force = false)
    {
        var statuses = this.clientManager.GetAddonStatuses(
            this.ownerProcessId);
        var canPersist = this.clientManager.CanPersistAddonSelection(
            this.ownerProcessId);
        var suspended = this.clientManager.AddonsSuspendedForSession;
        var fingerprint = CreateStatusFingerprint(
            statuses,
            canPersist,
            suspended);

        if (force ||
            !string.Equals(
                this.statusFingerprint,
                fingerprint,
                StringComparison.Ordinal))
        {
            this.statusFingerprint = fingerprint;
            this.currentStatuses = statuses;
            this.SynchronizeCards(statuses, canPersist, force);
            this.RefreshHeader(statuses, canPersist, suspended);
            this.RefreshSuspensionBanner(suspended);
            this.RebuildCardLayout(force);
        }

        this.RefreshDiscoverView(force);
        this.RefreshActivityLog();
    }

    private void SynchronizeCards(
        IReadOnlyList<AddonRuntimeStatus> statuses,
        bool canPersist,
        bool force)
    {
        var statusIds = statuses
            .Select(status => status.AddonId)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var removedId in this.cardsByAddonId.Keys
                     .Where(addonId => !statusIds.Contains(addonId))
                     .ToArray())
        {
            var card = this.cardsByAddonId[removedId];
            this.UnwireCard(card);
            this.cardsByAddonId.Remove(removedId);
            card.Dispose();
        }

        foreach (var status in statuses)
        {
            if (!this.cardsByAddonId.TryGetValue(
                    status.AddonId,
                    out var card))
            {
                card = new AddonCardControl
                {
                    AccessibleName = status.Name,
                };

                this.WireCard(card);
                this.cardsByAddonId.Add(status.AddonId, card);
            }

            card.UpdateStatus(status, canPersist, force);
            card.SetOperationsEnabled(!this.operationInProgress);
        }
    }

    private void RefreshHeader(
        IReadOnlyList<AddonRuntimeStatus> statuses,
        bool canPersist,
        bool suspended)
    {
        this.contextLabel.Text = this.clientManager
            .GetAddonOwnerDisplayName(this.ownerProcessId);

        var enabledCount = statuses.Count(status => status.IsEnabled);
        var runningCount = statuses.Count(status =>
            status.State == AddonRuntimeState.Running);
        var updateCount = statuses.Count(status => status.HasUpdate);

        this.summaryLabel.ForeColor = AddonCenterTheme.MutedText;
        this.summaryLabel.Text = canPersist
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{enabledCount} enabled for this client · " +
                $"{runningCount} running · " +
                $"{updateCount} update(s) available")
            : "Addon enablement will be stored for this client. Unassigned clients share unassigned settings.";

        this.suspendButton.Text = suspended
            ? "Resume addons"
            : "Suspend all for this session";
        this.suspendButton.Enabled = !this.operationInProgress;
    }

    private void RefreshSuspensionBanner(bool suspended)
    {
        var root = this.suspensionPanel.Parent as TableLayoutPanel;

        if (root != null)
        {
            root.RowStyles[1].Height = suspended
                ? SuspensionBannerHeight
                : 0;
        }

        this.suspensionPanel.Visible = suspended;
    }

    private void RebuildCardLayout(bool force = false)
    {
        var filtered = this.currentStatuses
            .Where(this.MatchesCurrentFilter)
            .OrderBy(status => status.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var builtIn = filtered
            .Where(status => string.Equals(
                status.Source,
                "Host",
                StringComparison.Ordinal))
            .ToArray();
        var packages = filtered
            .Where(status => !string.Equals(
                status.Source,
                "Host",
                StringComparison.Ordinal))
            .ToArray();

        var availableWidth = Math.Max(
            360,
            this.installedScrollPanel.ClientSize.Width -
            this.installedScrollPanel.Padding.Horizontal -
            SystemInformation.VerticalScrollBarWidth);
        var columnCount = availableWidth >= 900 ? 2 : 1;
        var fingerprint = string.Concat(
            columnCount,
            "|",
            string.Join(',', builtIn.Select(status => status.AddonId)),
            "|",
            string.Join(',', packages.Select(status => status.AddonId)),
            "|",
            this.searchTextBox.Text,
            "|",
            this.filterComboBox.SelectedIndex);

        if (!force &&
            string.Equals(
                this.layoutFingerprint,
                fingerprint,
                StringComparison.Ordinal))
        {
            return;
        }

        this.layoutFingerprint = fingerprint;
        this.builtInCountLabel.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"BUILT-IN TOOLS  ·  {builtIn.Length}");
        this.packageCountLabel.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"INSTALLED ADDONS  ·  {packages.Length}");

        this.builtInCountLabel.Visible = builtIn.Length > 0;
        this.builtInCardsGrid.Visible = builtIn.Length > 0;
        this.packageCountLabel.Visible = packages.Length > 0;
        this.packageCardsGrid.Visible = packages.Length > 0;
        this.emptyStateLabel.Visible = filtered.Length == 0;
        this.installedSections.Visible = filtered.Length > 0;

        PopulateCardsGrid(
            this.builtInCardsGrid,
            builtIn,
            this.cardsByAddonId,
            columnCount);
        PopulateCardsGrid(
            this.packageCardsGrid,
            packages,
            this.cardsByAddonId,
            columnCount);

        this.installedSections.Width = availableWidth;
    }

    private bool MatchesCurrentFilter(AddonRuntimeStatus status)
    {
        var search = this.searchTextBox.Text.Trim();

        if (search.Length > 0 &&
            !ContainsIgnoreCase(status.Name, search) &&
            !ContainsIgnoreCase(status.AddonId, search) &&
            !ContainsIgnoreCase(status.Author, search) &&
            !ContainsIgnoreCase(status.Description, search) &&
            !ContainsIgnoreCase(status.Source, search))
        {
            return false;
        }

        return this.filterComboBox.SelectedIndex switch
        {
            1 => status.IsEnabled,
            2 => !status.IsEnabled,
            3 => status.HasUpdate,
            4 => status.IsDevelopment,
            _ => true,
        };
    }

    private void RefreshActivityLog()
    {
        var entries = this.clientManager
            .GetAddonRecentLogs(this.ownerProcessId);
        var fingerprint = string.Join(
            "\u001e",
            entries.Select(entry => string.Create(
                CultureInfo.InvariantCulture,
                $"{entry.ObservedAt.UtcDateTime.Ticks}|{entry.Level}|" +
                $"{entry.AddonId}|{entry.Message}")));

        if (string.Equals(
                this.logFingerprint,
                fingerprint,
                StringComparison.Ordinal))
        {
            return;
        }

        this.logFingerprint = fingerprint;
        var lines = entries.Select(entry => string.Create(
            CultureInfo.InvariantCulture,
            $"{entry.ObservedAt.ToLocalTime():HH:mm:ss.fff}  " +
            $"{entry.Level,-11}  {entry.AddonId}  {entry.Message}"));

        this.activityTextBox.Text = string.Join(
            Environment.NewLine,
            lines);
        this.activityTextBox.SelectionStart =
            this.activityTextBox.TextLength;
        this.activityTextBox.ScrollToCaret();
    }

    private void ShowPage(AddonCenterPage page)
    {
        foreach (var (candidate, button) in this.navigationButtons)
        {
            var selected = candidate == page;
            button.BackColor = selected
                ? AddonCenterTheme.ButtonHover
                : AddonCenterTheme.Button;
            button.ForeColor = selected
                ? AddonCenterTheme.Accent
                : AddonCenterTheme.Text;
            button.FlatAppearance.BorderColor = selected
                ? AddonCenterTheme.Border
                : AddonCenterTheme.SoftBorder;
        }

        this.installedPage.Visible = page == AddonCenterPage.Installed;
        this.discoverPage.Visible = page == AddonCenterPage.Discover;
        this.developPage.Visible = page == AddonCenterPage.Develop;
        this.activityPage.Visible = page == AddonCenterPage.Activity;

        if (page == AddonCenterPage.Develop)
        {
            this.EnsureDevelopmentPageInitialized();
        }

        this.GetPageControl(page).BringToFront();
    }

    private Control GetPageControl(AddonCenterPage page)
    {
        return page switch
        {
            AddonCenterPage.Discover => this.discoverPage,
            AddonCenterPage.Installed => this.installedPage,
            AddonCenterPage.Develop => this.developPage,
            AddonCenterPage.Activity => this.activityPage,
            _ => this.installedPage,
        };
    }

    private void WireCard(AddonCardControl card)
    {
        card.ToggleRequested += this.Card_OnToggleRequested;
        card.UpdateRequested += this.Card_OnUpdateRequested;
        card.ReloadRequested += this.Card_OnReloadRequested;
        card.DevelopRequested += this.Card_OnDevelopRequested;
        card.DetailsRequested += this.Card_OnDetailsRequested;
        card.UninstallRequested += this.Card_OnUninstallRequested;
        card.OpenFolderRequested += this.Card_OnOpenFolderRequested;
    }

    private void UnwireCard(AddonCardControl card)
    {
        card.ToggleRequested -= this.Card_OnToggleRequested;
        card.UpdateRequested -= this.Card_OnUpdateRequested;
        card.ReloadRequested -= this.Card_OnReloadRequested;
        card.DevelopRequested -= this.Card_OnDevelopRequested;
        card.DetailsRequested -= this.Card_OnDetailsRequested;
        card.UninstallRequested -= this.Card_OnUninstallRequested;
        card.OpenFolderRequested -= this.Card_OnOpenFolderRequested;
    }

    private async void Card_OnToggleRequested(
        object? sender,
        EventArgs e)
    {
        if (sender is not AddonCardControl
            {
                Status: { } status,
            })
        {
            return;
        }

        if (status.IsEnabled)
        {
            await this.RunCommandAsync(
                string.Concat("Disabling ", status.Name, " for this client..."),
                () => this.clientManager.DisableAddonAsync(
                    this.ownerProcessId,
                    status.AddonId));
        }
        else
        {
            await this.RunCommandAsync(
                string.Concat("Enabling ", status.Name, " for this client..."),
                () => this.clientManager.EnableAddonAsync(
                    this.ownerProcessId,
                    status.AddonId));
        }
    }

    private async void Card_OnUpdateRequested(
        object? sender,
        EventArgs e)
    {
        if (sender is not AddonCardControl
            {
                Status: { } status,
            })
        {
            return;
        }

        await this.RunCommandAsync(
            string.Concat("Updating ", status.Name, "..."),
            () => this.clientManager.UpdateAddonAsync(status.AddonId));
    }

    private async void Card_OnReloadRequested(
        object? sender,
        EventArgs e)
    {
        if (sender is not AddonCardControl
            {
                Status: { } status,
            })
        {
            return;
        }

        await this.RunCommandAsync(
            string.Concat("Reloading ", status.Name, "..."),
            () => this.clientManager.ReloadAddonAsync(
                this.ownerProcessId,
                status.AddonId));
    }

    private async void Card_OnDevelopRequested(
        object? sender,
        EventArgs e)
    {
        if (sender is not AddonCardControl
            {
                Status: { } status,
            })
        {
            return;
        }

        if (!this.ConfirmDevelopmentWorkspaceSwitch(
                status.AddonId,
                out var discardCurrentChanges))
        {
            return;
        }

        if (status.IsDevelopment)
        {
            this.OpenDevelopmentWorkspace(
                status.AddonId,
                discardCurrentChanges);
            return;
        }

        var succeeded = await this.RunCommandAsync(
            string.Concat(
                "Creating an editable copy of ",
                status.Name,
                "..."),
            () => this.clientManager
                .CreateAddonDevelopmentWorkspaceFromInstalledAsync(
                    status.AddonId));

        if (succeeded)
        {
            this.OpenDevelopmentWorkspace(
                status.AddonId,
                discardCurrentChanges);
        }
    }

    private void Card_OnDetailsRequested(
        object? sender,
        EventArgs e)
    {
        if (sender is not AddonCardControl
            {
                Status: { } status,
            })
        {
            return;
        }

        var addon = this.clientManager.AddonRegistry.FirstOrDefault(
            candidate => string.Equals(
                candidate.Id,
                status.AddonId,
                StringComparison.Ordinal));

        if (addon != null)
        {
            this.ShowAddonDetails(addon);
        }
    }

    private async void Card_OnUninstallRequested(
        object? sender,
        EventArgs e)
    {
        if (sender is not AddonCardControl
            {
                Status: { } status,
            })
        {
            return;
        }

        if (status.IsDevelopment)
        {
            var isSelectedWorkspace = string.Equals(
                this.selectedDevelopmentWorkspace?.Id,
                status.AddonId,
                StringComparison.Ordinal);

            if (isSelectedWorkspace &&
                this.IsDevelopmentDocumentDirty &&
                !this.ConfirmDiscardDevelopmentChanges())
            {
                return;
            }

            if (!ThemedMessageDialog.Confirm(
                    this,
                    "Discard Development Copy",
                    string.Concat(
                        "Discard the editable copy of ",
                        status.Name,
                        "? The source package remains untouched. If a packaged version is installed, it becomes active again."),
                    "Discard copy"))
            {
                return;
            }

            var succeeded = await this.RunCommandAsync(
                string.Concat(
                    "Discarding the development copy of ",
                    status.Name,
                    "..."),
                () => this.clientManager
                    .DiscardAddonDevelopmentWorkspaceAsync(
                        status.AddonId));

            if (succeeded && isSelectedWorkspace)
            {
                this.loadedDevelopmentText = this.developmentEditor.Text;
                this.RefreshDevelopmentWorkspaces();
            }

            return;
        }

        using var dialog = new AddonUninstallDialog(status.Name);

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        await this.RunCommandAsync(
            string.Concat("Uninstalling ", status.Name, "..."),
            () => this.clientManager.UninstallAddonAsync(
                status.AddonId,
                dialog.RemoveSettings));
    }

    private void Card_OnOpenFolderRequested(
        object? sender,
        EventArgs e)
    {
        var selectedPath = (sender as AddonCardControl)?.Status?.DirectoryPath;
        var path = Directory.Exists(selectedPath)
            ? selectedPath
            : File.Exists(selectedPath)
                ? Path.GetDirectoryName(selectedPath)
                : this.clientManager.AddonDirectory;

        path ??= this.clientManager.AddonDirectory;
        Directory.CreateDirectory(path);

        Process.Start(
            new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
    }

    private void SetOperationState(
        bool inProgress,
        string? text = null)
    {
        this.operationInProgress = inProgress;

        if (!string.IsNullOrWhiteSpace(text))
        {
            this.summaryLabel.Text = text;
            this.summaryLabel.ForeColor = AddonCenterTheme.Accent;
        }

        this.refreshButton.Enabled = !inProgress;
        this.suspendButton.Enabled = !inProgress;
        this.resumeButton.Enabled = !inProgress;

        foreach (var card in this.cardsByAddonId.Values)
        {
            card.SetOperationsEnabled(!inProgress);
        }

        this.SetDiscoverOperationsEnabled(!inProgress);
        this.SetDevelopOperationsEnabled(!inProgress);
    }

    private static void ConfigureCardsGrid(TableLayoutPanel grid)
    {
        grid.AutoSize = true;
        grid.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        grid.Dock = DockStyle.Top;
        grid.Margin = Padding.Empty;
        grid.Padding = Padding.Empty;
        grid.BackColor = AddonCenterTheme.Background;
    }

    private static void PopulateCardsGrid(
        TableLayoutPanel grid,
        IReadOnlyList<AddonRuntimeStatus> statuses,
        IReadOnlyDictionary<string, AddonCardControl> cards,
        int columnCount)
    {
        grid.SuspendLayout();

        try
        {
            grid.Controls.Clear();
            grid.ColumnStyles.Clear();
            grid.RowStyles.Clear();
            grid.ColumnCount = columnCount;
            grid.RowCount = Math.Max(
                1,
                (int)Math.Ceiling(statuses.Count / (double)columnCount));

            for (var column = 0; column < columnCount; column++)
            {
                grid.ColumnStyles.Add(
                    new ColumnStyle(
                        SizeType.Percent,
                        100f / columnCount));
            }

            for (var row = 0; row < grid.RowCount; row++)
            {
                grid.RowStyles.Add(
                    new RowStyle(SizeType.AutoSize));
            }

            for (var index = 0; index < statuses.Count; index++)
            {
                var status = statuses[index];
                var card = cards[status.AddonId];
                card.Dock = DockStyle.Top;
                card.Margin = new Padding(
                    index % columnCount == columnCount - 1 ? 6 : 0,
                    0,
                    index % columnCount == columnCount - 1 ? 0 : 6,
                    12);

                grid.Controls.Add(
                    card,
                    index % columnCount,
                    index / columnCount);
            }
        }
        finally
        {
            grid.ResumeLayout(performLayout: true);
        }
    }

    private static string CreateStatusFingerprint(
        IReadOnlyList<AddonRuntimeStatus> statuses,
        bool canPersist,
        bool suspended)
    {
        return string.Join(
            "\u001e",
            statuses.Select(status => string.Join(
                "\u001f",
                status.AddonId,
                status.Name,
                status.Version,
                status.AvailableVersion,
                status.Source,
                status.Author,
                status.Description,
                status.IsValid,
                status.IsEnabled,
                status.IsDevelopment,
                status.IsPinned,
                status.PublisherName,
                status.IsOfficial,
                status.State,
                status.Error,
                status.Detail))) +
            string.Concat("|", canPersist, "|", suspended);
    }

    private static bool ContainsIgnoreCase(
        string? value,
        string search)
    {
        return !string.IsNullOrWhiteSpace(value) &&
               value.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private enum AddonCenterPage
    {
        Discover,
        Installed,
        Develop,
        Activity,
    }
}
