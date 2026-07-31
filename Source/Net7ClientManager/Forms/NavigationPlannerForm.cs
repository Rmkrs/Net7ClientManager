// ReSharper disable LocalizableElement
// ReSharper disable StringLiteralTypo
// ReSharper disable AsyncVoidEventHandlerMethod
// ReSharper disable RedundantSwitchExpressionArms
namespace Net7ClientManager.Forms;

using System.Globalization;
using Net7ClientManager.Core;
using Net7ClientManager.Models;
using Net7ClientManager.Navigation;
using Net7ClientManager.Observations;
using Net7ClientManager.Observations.Models;
using Net7ClientManager.Services;
using Net7ClientManager.Win32;

public sealed class NavigationPlannerForm : Form
{
    private const int TitleBarHeight = 34;
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

    private static readonly Color backgroundColor =
        Color.FromArgb(12, 17, 24);

    private static readonly Color panelColor =
        Color.FromArgb(18, 26, 36);

    private static readonly Color headerColor =
        Color.FromArgb(24, 37, 49);

    private static readonly Color borderColor =
        Color.FromArgb(44, 135, 174);

    private static readonly Color textColor =
        Color.FromArgb(235, 242, 247);

    private static readonly Color mutedTextColor =
        Color.FromArgb(158, 177, 191);

    private static readonly Color accentColor =
        Color.FromArgb(65, 203, 236);

    private readonly ClientManager clientManager;
    private readonly WindowPlacementBinding windowPlacement;
    private readonly HostedClientTitleBar titleBar = new();
    private readonly ComboBox clientComboBox = new();
    private readonly TextBox searchTextBox = new();
    private readonly ComboBox destinationFilterComboBox = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Location = new Point(820, 31),
        Width = 170,
        BackColor = panelColor,
        ForeColor = textColor,
        FlatStyle = FlatStyle.Flat,
        Items =
        {
            "All destinations",
            "Sectors",
            "Navigation points",
            "Stations",
            "Landable planets",
            "Gates & accelerators",
        },
        SelectedIndex = 0,
    };
    private readonly CheckBox nearestFirstCheckBox = new()
    {
        AutoSize = true,
        Text = "Nearest first",
        ForeColor = textColor,
        BackColor = headerColor,
        Location = new Point(1010, 33),
    };
    private readonly TreeView catalogTree = new();
    private readonly Label pilotValueLabel = new();
    private readonly Label currentValueLabel = new();
    private readonly Label destinationValueLabel = new();
    private readonly Label statusValueLabel = new();
    private readonly Label hopValueLabel = new();
    private readonly Label routeModeLabel = new();
    private readonly ListBox routeListBox = new();
    private readonly ListBox warningListBox = new();
    private readonly Button setDestinationButton =
        new PlannerButton();
    private readonly Button clearRouteButton =
        new PlannerButton();
    private readonly Button showCurrentRouteButton =
        new PlannerButton();
    private readonly Button selectNextTargetButton =
        new PlannerButton();
    private readonly CheckBox closeAfterSettingDestinationCheckBox =
        new()
        {
            AutoSize = true,
            Text = "Close after setting destination",
            ForeColor = textColor,
            BackColor = panelColor,
            Anchor = AnchorStyles.Left,
        };
    private readonly System.Windows.Forms.Timer refreshTimer = new();

    private int? requestedProcessId;
    private bool isRefreshingClients;
    private bool isRebuildingTree;
    private NavigationDestination? previewDestination;
    private string clientFingerprint = "";
    private string catalogContextFingerprint = "";
    private string routeFingerprint = "";
    private bool isTargetSelectionBusy;
    private CancellationTokenSource? targetSelectionCancellationTokenSource;

    public NavigationPlannerForm(
        ClientManager clientManager,
        int? initialProcessId = null)
    {
        this.clientManager = clientManager;
        this.requestedProcessId = initialProcessId;

        this.Text = "Net7 Route Planner";
        this.Icon = ResourceLoader.Net7ClientManagerIcon;
        this.StartPosition = FormStartPosition.CenterScreen;
        this.FormBorderStyle = FormBorderStyle.None;
        this.Size = new Size(1180, 760);
        this.MinimumSize = new Size(1120, 620);
        this.Padding = new Padding(1);
        this.BackColor = borderColor;
        this.ForeColor = textColor;
        this.Font = new Font("Segoe UI", 9.0f);

        this.titleBar.Dock = DockStyle.Top;
        this.titleBar.Height = TitleBarHeight;
        this.titleBar.TitleText = "NET7 ROUTE PLANNER";
        this.titleBar.ShowMaximizeButton = true;
        this.titleBar.ShowHelpButton = true;
        this.titleBar.HelpTopicId = HelpTopicIds.Navigation;
        this.titleBar.HelpProcessIdProvider =
            () => this.SelectedProcessId;
        this.titleBar.HelpOverride = () =>
        {
            this.ShowHelpTour();
            return true;
        };
        this.titleBar.AccessibleName = "Net7 Route Planner title bar";

        this.BuildUi();
        this.windowPlacement =
            clientManager.BindClientWindowPlacement(
                this,
                WindowPlacementIds.NavigationPlanner,
                initialProcessId);

        this.closeAfterSettingDestinationCheckBox.Checked =
            this.clientManager
                .NavigationPlannerSettings
                .CloseAfterSettingDestination;

        this.clientComboBox.SelectedIndexChanged +=
            this.ClientComboBox_OnSelectedIndexChanged;

        this.searchTextBox.TextChanged +=
            this.SearchTextBox_OnTextChanged;

        this.destinationFilterComboBox.SelectedIndexChanged +=
            this.DestinationFilterComboBox_OnSelectedIndexChanged;

        this.nearestFirstCheckBox.CheckedChanged +=
            this.NearestFirstCheckBox_OnCheckedChanged;

        this.closeAfterSettingDestinationCheckBox.CheckedChanged +=
            this.CloseAfterSettingDestinationCheckBox_OnCheckedChanged;

        this.catalogTree.AfterSelect +=
            this.CatalogTree_OnAfterSelect;

        this.setDestinationButton.Click +=
            this.SetDestinationButton_OnClick;

        this.clearRouteButton.Click +=
            this.ClearRouteButton_OnClick;

        this.showCurrentRouteButton.Click +=
            this.ShowCurrentRouteButton_OnClick;

        this.selectNextTargetButton.Click +=
            this.SelectNextTargetButton_OnClick;

        this.titleBar.DragRequested +=
            this.TitleBar_OnDragRequested;

        this.titleBar.MinimizeRequested +=
            this.TitleBar_OnMinimizeRequested;

        this.titleBar.MaximizeRequested +=
            this.TitleBar_OnMaximizeRequested;

        this.titleBar.CloseRequested +=
            this.TitleBar_OnCloseRequested;

        this.clientManager.NavigationDataChanged +=
            this.ClientManager_OnNavigationDataChanged;

        this.Resize += this.NavigationPlannerForm_OnResize;

        this.refreshTimer.Interval = 750;
        this.refreshTimer.Tick += this.RefreshTimer_OnTick;
        this.refreshTimer.Start();

        this.RefreshAll(forceCatalogRefresh: true);
    }

    public void SelectProcess(int? processId)
    {
        this.windowPlacement.Rebind(processId);
        this.requestedProcessId = processId;
        this.RefreshClients(force: true);
        this.SelectRequestedProcess();
        this.ShowCurrentRoute();
    }


    internal void ShowHelpTour()
    {
        GuidedTourOverlay.Show(
            this,
            [
                new GuidedTourStep(
                    () => this.clientComboBox,
                    "Choose the pilot",
                    "Routes start from the live position of the selected hosted pilot. Change this when you want to plan for another client."),
                new GuidedTourStep(
                    () => this.searchTextBox,
                    "Find a destination",
                    "Type part of a sector, station, planet, gate, or navigation-point name. The destination list narrows as you type."),
                new GuidedTourStep(
                    () => this.destinationFilterComboBox,
                    "Narrow the kind of place",
                    "Use Type when you only want sectors, stations, gates, planets, or navigation points. Nearest first can move nearby matches to the top."),
                new GuidedTourStep(
                    () => this.catalogTree,
                    "Choose from the results",
                    "Select a destination to preview its route. The right side immediately shows the hops, warnings, and the next travel target."),
                new GuidedTourStep(
                    () => this.setDestinationButton,
                    "Send the route to the game",
                    "Set destination makes the previewed route active for the selected pilot. Built-in Navigation and Auto Pilot can then use it."),
                new GuidedTourStep(
                    () => this.routeListBox,
                    "Read the journey",
                    "The route lists each supported travel step in order. Warnings below it call out restrictions, unavailable shortcuts, or anything that needs your attention."),
            ]);
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
                var onRight =
                    cursor.X >=
                    this.ClientSize.Width - ResizeBorderThickness;
                var onTop = cursor.Y <= ResizeBorderThickness;
                var onBottom =
                    cursor.Y >=
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
        this.refreshTimer.Tick -= this.RefreshTimer_OnTick;
        this.refreshTimer.Dispose();

        this.clientComboBox.SelectedIndexChanged -=
            this.ClientComboBox_OnSelectedIndexChanged;

        this.searchTextBox.TextChanged -=
            this.SearchTextBox_OnTextChanged;

        this.destinationFilterComboBox.SelectedIndexChanged -=
            this.DestinationFilterComboBox_OnSelectedIndexChanged;

        this.nearestFirstCheckBox.CheckedChanged -=
            this.NearestFirstCheckBox_OnCheckedChanged;

        this.closeAfterSettingDestinationCheckBox.CheckedChanged -=
            this.CloseAfterSettingDestinationCheckBox_OnCheckedChanged;

        this.catalogTree.AfterSelect -=
            this.CatalogTree_OnAfterSelect;

        this.setDestinationButton.Click -=
            this.SetDestinationButton_OnClick;

        this.clearRouteButton.Click -=
            this.ClearRouteButton_OnClick;

        this.showCurrentRouteButton.Click -=
            this.ShowCurrentRouteButton_OnClick;

        this.selectNextTargetButton.Click -=
            this.SelectNextTargetButton_OnClick;

        this.targetSelectionCancellationTokenSource?.Cancel();
        this.targetSelectionCancellationTokenSource?.Dispose();
        this.targetSelectionCancellationTokenSource = null;

        this.titleBar.DragRequested -=
            this.TitleBar_OnDragRequested;

        this.titleBar.MinimizeRequested -=
            this.TitleBar_OnMinimizeRequested;

        this.titleBar.MaximizeRequested -=
            this.TitleBar_OnMaximizeRequested;

        this.titleBar.CloseRequested -=
            this.TitleBar_OnCloseRequested;

        this.clientManager.NavigationDataChanged -=
            this.ClientManager_OnNavigationDataChanged;

        this.Resize -= this.NavigationPlannerForm_OnResize;
        this.windowPlacement.Dispose();

        base.OnFormClosed(e);
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = backgroundColor,
            Padding = new Padding(12),
        };

        root.RowStyles.Add(
            new RowStyle(SizeType.Absolute, 72));
        root.RowStyles.Add(
            new RowStyle(SizeType.Percent, 100));

        root.Controls.Add(this.BuildHeader(), 0, 0);
        root.Controls.Add(this.BuildBody(), 0, 1);

        this.Controls.Add(root);
        this.Controls.Add(this.titleBar);
    }

    private Control BuildHeader()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = headerColor,
            Padding = new Padding(14, 10, 14, 10),
        };

        var title = new Label
        {
            AutoSize = true,
            Text = "ROUTE PLANNER",
            ForeColor = accentColor,
            Font = new Font(this.Font.FontFamily, 12.0f, FontStyle.Bold),
            Location = new Point(14, 8),
        };

        var clientLabel = CreateSmallLabel("PILOT", 230, 9);

        this.clientComboBox.DropDownStyle =
            ComboBoxStyle.DropDownList;
        this.clientComboBox.Location = new Point(230, 31);
        this.clientComboBox.Width = 270;
        this.clientComboBox.BackColor = panelColor;
        this.clientComboBox.ForeColor = textColor;
        this.clientComboBox.FlatStyle = FlatStyle.Flat;

        var searchLabel = CreateSmallLabel("SEARCH", 520, 9);

        this.searchTextBox.Location = new Point(520, 31);
        this.searchTextBox.Width = 280;
        this.searchTextBox.PlaceholderText =
            "name or system:, sector:, target: ...";
        this.searchTextBox.BackColor = panelColor;
        this.searchTextBox.ForeColor = textColor;
        this.searchTextBox.BorderStyle = BorderStyle.FixedSingle;

        var filterLabel = CreateSmallLabel("TYPE", 820, 9);

        panel.Controls.Add(title);
        panel.Controls.Add(clientLabel);
        panel.Controls.Add(this.clientComboBox);
        panel.Controls.Add(searchLabel);
        panel.Controls.Add(this.searchTextBox);
        panel.Controls.Add(filterLabel);
        panel.Controls.Add(this.destinationFilterComboBox);
        panel.Controls.Add(this.nearestFirstCheckBox);

        return panel;
    }

    private Control BuildBody()
    {
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            BackColor = borderColor,

            // Give the control valid construction-time dimensions before
            // applying panel minimums and the initial splitter position.
            Size = new Size(1120, 600),
            Panel1MinSize = 300,
            Panel2MinSize = 420,
            SplitterDistance = 430,
            Panel1 =
            {
                BackColor = panelColor,
                Padding = new Padding(8),
            },
            Panel2 =
            {
                BackColor = panelColor,
                Padding = new Padding(12),
            },
        };

        var catalogTitle = new Label
        {
            Dock = DockStyle.Top,
            Height = 28,
            Text = "GALAXY CATALOG",
            ForeColor = accentColor,
            Font = new Font(this.Font.FontFamily, 10.0f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
        };

        this.catalogTree.Dock = DockStyle.Fill;
        this.catalogTree.BackColor = panelColor;
        this.catalogTree.ForeColor = textColor;
        this.catalogTree.BorderStyle = BorderStyle.None;
        this.catalogTree.HideSelection = false;
        this.catalogTree.FullRowSelect = true;
        this.catalogTree.ShowLines = true;
        this.catalogTree.ShowRootLines = false;

        split.Panel1.Controls.Add(this.catalogTree);
        split.Panel1.Controls.Add(catalogTitle);
        split.Panel2.Controls.Add(this.BuildRoutePanel());

        return split;
    }

    private Control BuildRoutePanel()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            BackColor = panelColor,
        };

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 158));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 70));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

        layout.Controls.Add(this.BuildSummaryPanel(), 0, 0);

        this.routeModeLabel.Dock = DockStyle.Fill;
        this.routeModeLabel.ForeColor = accentColor;
        this.routeModeLabel.Font = new Font(
            this.Font.FontFamily,
            10.0f,
            FontStyle.Bold);
        this.routeModeLabel.TextAlign = ContentAlignment.MiddleLeft;
        layout.Controls.Add(this.routeModeLabel, 0, 1);

        this.routeListBox.Dock = DockStyle.Fill;
        this.routeListBox.BackColor = backgroundColor;
        this.routeListBox.ForeColor = textColor;
        this.routeListBox.BorderStyle = BorderStyle.FixedSingle;
        this.routeListBox.IntegralHeight = false;
        this.routeListBox.Font = new Font("Consolas", 9.5f);
        layout.Controls.Add(this.routeListBox, 0, 2);

        this.warningListBox.Dock = DockStyle.Fill;
        this.warningListBox.BackColor = panelColor;
        this.warningListBox.ForeColor = Color.FromArgb(246, 190, 90);
        this.warningListBox.BorderStyle = BorderStyle.FixedSingle;
        this.warningListBox.IntegralHeight = false;
        layout.Controls.Add(this.warningListBox, 0, 3);

        layout.Controls.Add(this.BuildButtonPanel(), 0, 4);

        return layout;
    }

    private Control BuildSummaryPanel()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 5,
            BackColor = panelColor,
        };

        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddSummaryRow(layout, 0, "PILOT", this.pilotValueLabel);
        AddSummaryRow(layout, 1, "CURRENT", this.currentValueLabel);
        AddSummaryRow(layout, 2, "DESTINATION", this.destinationValueLabel);
        AddSummaryRow(layout, 3, "STATUS", this.statusValueLabel);
        AddSummaryRow(layout, 4, "HOPS", this.hopValueLabel);

        return layout;
    }

    private Control BuildButtonPanel()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = panelColor,
            Padding = new Padding(0, 8, 0, 0),
            ColumnStyles =
            {
                new ColumnStyle(SizeType.Percent, 100),
                new ColumnStyle(SizeType.Absolute, 540),
            },
        };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = panelColor,
            Margin = Padding.Empty,
        };

        ConfigureButton(
            this.setDestinationButton,
            "Set destination",
            132);

        ConfigureButton(
            this.clearRouteButton,
            "Clear route",
            104);

        ConfigureButton(
            this.selectNextTargetButton,
            "Select next target",
            144);

        ConfigureButton(
            this.showCurrentRouteButton,
            "Current route",
            112);

        buttons.Controls.Add(this.setDestinationButton);
        buttons.Controls.Add(this.clearRouteButton);
        buttons.Controls.Add(this.selectNextTargetButton);
        buttons.Controls.Add(this.showCurrentRouteButton);

        layout.Controls.Add(
            this.closeAfterSettingDestinationCheckBox,
            0,
            0);
        layout.Controls.Add(buttons, 1, 0);

        return layout;
    }

    private void ClientManager_OnNavigationDataChanged(
        object? sender,
        EventArgs e)
    {
        this.catalogContextFingerprint = "";
        this.routeFingerprint = "";
        this.RefreshAll(forceCatalogRefresh: true);
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

    private void NavigationPlannerForm_OnResize(
        object? sender,
        EventArgs e)
    {
        this.titleBar.IsMaximized =
            this.WindowState == FormWindowState.Maximized;
    }

    private void RefreshTimer_OnTick(object? sender, EventArgs e)
    {
        this.RefreshAll(forceCatalogRefresh: false);
    }

    private void RefreshAll(bool forceCatalogRefresh)
    {
        this.RefreshClients(force: false);

        var currentCatalogContextFingerprint =
            this.BuildCatalogContextFingerprint();

        if (forceCatalogRefresh ||
            this.catalogTree.Nodes.Count == 0 ||
            !string.Equals(
                this.catalogContextFingerprint,
                currentCatalogContextFingerprint,
                StringComparison.Ordinal))
        {
            this.RebuildCatalogTree();
        }

        this.RefreshRouteDisplay();
    }

    private void RefreshClients(bool force)
    {
        if (this.isRefreshingClients)
        {
            return;
        }

        var selectedProcessId = this.SelectedProcessId;
        var choices = this.clientManager.Clients
            .OrderBy(client => client.ProcessId)
            .Select(client => new ClientChoice(
                client,
                BuildClientDisplayName(
                    client,
                    this.clientManager.GetNavigationRouteSnapshot(
                        client.ProcessId))))
            .ToArray();

        var fingerprint = string.Join(
            '\u001f',
            choices.Select(choice =>
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{choice.ProcessId}:{choice.DisplayName}")));

        if (!force &&
            string.Equals(
                this.clientFingerprint,
                fingerprint,
                StringComparison.Ordinal))
        {
            return;
        }

        this.clientFingerprint = fingerprint;
        this.isRefreshingClients = true;

        try
        {
            this.clientComboBox.BeginUpdate();
            this.clientComboBox.Items.Clear();

            foreach (var choice in choices)
            {
                this.clientComboBox.Items.Add(choice);
            }

            var desiredProcessId =
                this.requestedProcessId ?? selectedProcessId;

            var selected = choices.FirstOrDefault(choice =>
                choice.ProcessId == desiredProcessId);

            if (selected != null)
            {
                this.clientComboBox.SelectedItem = selected;
            }
            else if (choices.Length > 0)
            {
                this.clientComboBox.SelectedIndex = 0;
            }
        }
        finally
        {
            this.clientComboBox.EndUpdate();
            this.isRefreshingClients = false;
        }

        this.requestedProcessId = null;
        this.windowPlacement.Rebind(this.SelectedProcessId);
    }

    private void SelectRequestedProcess()
    {
        if (!this.requestedProcessId.HasValue)
        {
            return;
        }

        var selected = this.clientComboBox.Items
            .Cast<ClientChoice>()
            .FirstOrDefault(choice =>
                choice.ProcessId == this.requestedProcessId.Value);

        if (selected != null)
        {
            this.clientComboBox.SelectedItem = selected;
        }

        this.requestedProcessId = null;
    }

    private void RebuildCatalogTree()
    {
        if (this.isRebuildingTree)
        {
            return;
        }

        this.isRebuildingTree = true;

        try
        {
            var selectedIdentity = GetNodeIdentity(
                this.catalogTree.SelectedNode);

            var search = CatalogSearch.Parse(
                this.searchTextBox.Text);

            var filter = this.SelectedDestinationFilter;
            var distances = this.SelectedProcessId is { } processId
                ? this.clientManager.GetNavigationRouteDistances(processId)
                : GalaxyRouteDistanceResult.Failure(
                    "No hosted client is selected.");

            this.catalogContextFingerprint =
                this.BuildCatalogContextFingerprint();

            this.catalogTree.BeginUpdate();
            this.catalogTree.Nodes.Clear();

            List<(TreeNode Node, int Distance, string Name)>
                systemNodes = [];

            foreach (var systemGroup in this.clientManager
                         .NavigationRoutes
                         .Topology
                         .Sectors
                         .GroupBy(
                             sector => sector.SystemName,
                             StringComparer.OrdinalIgnoreCase))
            {
                var systemMatches = search.MatchesSystem(systemGroup.Key);
                List<(TreeNode Node, int Distance, string Name)>
                    sectorNodes = [];

                foreach (var sector in systemGroup)
                {
                    var sectorMatchesSearch =
                        systemMatches || search.MatchesSector(sector);

                    var includeSectorDestination =
                        this.MatchesSectorFilter(filter, sector) &&
                        sectorMatchesSearch;

                    TreeNode[] targetNodes = [];

                    if (this.clientManager.NavigationRoutes.Catalog.TryGetSector(
                            sector.Key,
                            out var catalogSector))
                    {
                        targetNodes =
                        [
                            .. catalogSector.Targets
                                .Where(target =>
                                    this.MatchesTargetFilter(
                                        filter,
                                        sector.Key,
                                        target) &&
                                    search.MatchesTarget(target))
                                .OrderBy(
                                    NavigationDestination.GetTargetDisplayName,
                                    StringComparer.OrdinalIgnoreCase)
                                .Select(target =>
                                {
                                    var destination =
                                        NavigationDestination.ForTarget(
                                            sector,
                                            target);

                                    return new TreeNode(
                                        string.Concat(
                                            destination.TargetName,
                                            "  [",
                                            destination.TargetType,
                                            "]",
                                            FormatTargetDistance(
                                                distances,
                                                sector.Key)))
                                    {
                                        Tag = new CatalogNodeTag(destination),
                                        ForeColor = mutedTextColor,
                                    };
                                }),
                        ];
                    }

                    if (!includeSectorDestination &&
                        targetNodes.Length == 0)
                    {
                        continue;
                    }

                    var sectorDestination =
                        NavigationDestination.ForSector(sector);

                    var sectorNode = new TreeNode(
                        this.BuildSectorNodeText(
                            sector,
                            distances))
                    {
                        Tag = includeSectorDestination
                            ? new CatalogNodeTag(sectorDestination)
                            : null,
                        ForeColor = includeSectorDestination
                            ? textColor
                            : mutedTextColor,
                    };

                    sectorNode.Nodes.AddRange(targetNodes);

                    var sectorDistance = distances.TryGetDistance(
                        sector.Key,
                        out var distance)
                            ? distance
                            : int.MaxValue;

                    sectorNodes.Add(
                        (sectorNode, sectorDistance, sector.Name));
                }

                if (sectorNodes.Count == 0)
                {
                    continue;
                }

                var orderedSectorNodes = this.nearestFirstCheckBox.Checked
                    ? sectorNodes
                        .OrderBy(item => item.Distance)
                        .ThenBy(
                            item => item.Name,
                            StringComparer.OrdinalIgnoreCase)
                    : sectorNodes
                        .OrderBy(
                            item => item.Name,
                            StringComparer.OrdinalIgnoreCase);

                var nearestDistance = sectorNodes
                    .Min(item => item.Distance);

                var systemNode = new TreeNode(
                    nearestDistance == int.MaxValue
                        ? systemGroup.Key
                        : string.Concat(
                            systemGroup.Key,
                            "  · nearest ",
                            FormatTransitionCount(nearestDistance)))
                {
                    ForeColor = accentColor,
                    NodeFont = new Font(
                        this.Font.FontFamily,
                        9.0f,
                        FontStyle.Bold),
                };

                systemNode.Nodes.AddRange(
                    [.. orderedSectorNodes.Select(item => item.Node)]);

                systemNodes.Add(
                    (systemNode, nearestDistance, systemGroup.Key));
            }

            var orderedSystemNodes = this.nearestFirstCheckBox.Checked
                ? systemNodes
                    .OrderBy(item => item.Distance)
                    .ThenBy(
                        item => item.Name,
                        StringComparer.OrdinalIgnoreCase)
                : systemNodes
                    .OrderBy(
                        item => item.Name,
                        StringComparer.OrdinalIgnoreCase);

            foreach (var item in orderedSystemNodes)
            {
                this.catalogTree.Nodes.Add(item.Node);

                if (!search.IsEmpty ||
                    filter != NavigationDestinationFilter.All)
                {
                    item.Node.Expand();

                    foreach (TreeNode sectorNode in item.Node.Nodes)
                    {
                        sectorNode.Expand();
                    }
                }
            }

            this.RestoreNodeSelection(selectedIdentity);
        }
        finally
        {
            this.catalogTree.EndUpdate();
            this.isRebuildingTree = false;
        }
    }

    private void RestoreNodeSelection(string? identity)
    {
        if (string.IsNullOrWhiteSpace(identity))
        {
            return;
        }

        foreach (TreeNode systemNode in this.catalogTree.Nodes)
        {
            foreach (TreeNode sectorNode in systemNode.Nodes)
            {
                if (string.Equals(
                        GetNodeIdentity(sectorNode),
                        identity,
                        StringComparison.Ordinal))
                {
                    this.catalogTree.SelectedNode = sectorNode;
                    sectorNode.EnsureVisible();
                    return;
                }

                foreach (TreeNode targetNode in sectorNode.Nodes)
                {
                    if (!string.Equals(
                            GetNodeIdentity(targetNode),
                            identity,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    this.catalogTree.SelectedNode = targetNode;
                    targetNode.EnsureVisible();
                    return;
                }
            }
        }
    }

    private void RefreshRouteDisplay()
    {
        var processId = this.SelectedProcessId;

        if (!processId.HasValue)
        {
            this.ShowUnavailable("No hosted client is selected.");
            return;
        }

        if (this.previewDestination != null)
        {
            var preview = this.clientManager.PreviewNavigationRoute(
                processId.Value,
                this.previewDestination);

            if (!preview.Succeeded || preview.Plan == null)
            {
                this.ShowPreviewFailure(
                    processId.Value,
                    this.previewDestination,
                    preview.Error);
                return;
            }

            this.ShowPlan(
                processId.Value,
                preview.Plan,
                isPreview: true);
            return;
        }

        var snapshot = this.clientManager.GetNavigationRouteSnapshot(
            processId.Value);

        if (snapshot.Route == null)
        {
            this.ShowSnapshotWithoutRoute(snapshot);
            return;
        }

        this.ShowPlan(
            processId.Value,
            snapshot.Route,
            isPreview: false);
    }

    private void ShowPlan(
        int processId,
        NavigationRoutePlan plan,
        bool isPreview)
    {
        var fingerprint = string.Create(
            CultureInfo.InvariantCulture,
            $"{processId}|{isPreview}|{(isPreview ? Guid.Empty : plan.RouteId)}|{plan.UpdatedAt:O}|{plan.Current.Key}|{plan.Destination.SectorKey}|{plan.Destination.TargetKey}|{plan.Status}|{plan.StatusText}|{plan.CompletedHopCount}|{plan.RemainingHopCount}|{string.Join(';', plan.Steps.Select(step => $"{step.Kind}:{step.FromSectorKey}:{step.ToSectorKey}:{step.DepartureTargetName}:{step.FinalTargetKey}"))}|{string.Join(';', plan.Warnings)}");

        this.pilotValueLabel.Text = plan.CharacterName;
        this.currentValueLabel.Text = $"{plan.Current.SystemName} / {plan.Current.Name}";
        this.destinationValueLabel.Text = plan.Destination.DisplayName;
        this.statusValueLabel.Text = plan.StatusText;
        this.hopValueLabel.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"{plan.RemainingHopCount} remaining · {plan.CompletedHopCount} completed · {plan.TotalHopCount} total");
        this.routeModeLabel.Text = isPreview
            ? "ROUTE PREVIEW"
            : "CURRENT ROUTE";

        this.setDestinationButton.Enabled =
            isPreview && this.previewDestination != null;
        this.clearRouteButton.Enabled = !isPreview ||
            this.clientManager.GetNavigationRouteSnapshot(processId).HasRoute;
        this.showCurrentRouteButton.Enabled = isPreview;
        var liveSnapshot = this.clientManager
            .GetClientObservationSnapshots()
            .FirstOrDefault(candidate =>
                candidate.ProcessId == processId);

        var canSelectNextTarget = liveSnapshot is
        {
            LifecycleState: ClientLifecycleState.InGame,
            LoadingOrTransitionFlag: 0,
            World:
            {
                IsAvailable: true,
                Environment: ClientWorldEnvironment.Space,
            },
            Navigation.IsAvailable: true,
        };

        this.selectNextTargetButton.Enabled =
            !isPreview &&
            !this.isTargetSelectionBusy &&
            canSelectNextTarget &&
            plan.NextStep != null;

        if (string.Equals(
                this.routeFingerprint,
                fingerprint,
                StringComparison.Ordinal))
        {
            return;
        }

        this.routeFingerprint = fingerprint;
        this.routeListBox.BeginUpdate();
        this.warningListBox.BeginUpdate();

        try
        {
            this.routeListBox.Items.Clear();

            if (plan.Status == NavigationRouteStatus.NoRoute)
            {
                this.routeListBox.Items.Add(plan.StatusText);
            }
            else if (plan.Steps.Count == 0)
            {
                this.routeListBox.Items.Add(
                    "✓ Destination sector reached");
            }
            else
            {
                foreach (var step in plan.Steps)
                {
                    this.routeListBox.Items.Add(
                        FormatRouteStep(step));
                }
            }

            this.warningListBox.Items.Clear();

            foreach (var warning in plan.Warnings)
            {
                this.warningListBox.Items.Add(
                    string.Concat("⚠ ", warning));
            }

            if (plan.Warnings.Count == 0)
            {
                this.warningListBox.Items.Add(
                    "No access warnings for this plan.");
            }
        }
        finally
        {
            this.routeListBox.EndUpdate();
            this.warningListBox.EndUpdate();
        }
    }

    private void ShowSnapshotWithoutRoute(
        NavigationRouteSnapshot snapshot)
    {
        var fingerprint = string.Create(
            CultureInfo.InvariantCulture,
            $"snapshot|{snapshot.ProcessId}|{snapshot.Status}|{snapshot.StatusText}|{snapshot.CurrentSector?.Key}|{snapshot.HasRoute}");

        this.pilotValueLabel.Text =
            snapshot.CharacterName ?? "Unavailable";

        this.currentValueLabel.Text = snapshot.CurrentSector == null
            ? "Unavailable"
            : $"{snapshot.CurrentSector.SystemName} / {snapshot.CurrentSector.Name}";

        this.destinationValueLabel.Text = "Not set";
        this.statusValueLabel.Text = snapshot.StatusText;
        this.hopValueLabel.Text = "—";
        this.routeModeLabel.Text = "CURRENT ROUTE";
        this.setDestinationButton.Enabled = false;
        this.clearRouteButton.Enabled = snapshot.HasRoute;
        this.showCurrentRouteButton.Enabled = false;
        this.selectNextTargetButton.Enabled = false;

        if (string.Equals(
                this.routeFingerprint,
                fingerprint,
                StringComparison.Ordinal))
        {
            return;
        }

        this.routeFingerprint = fingerprint;
        this.routeListBox.Items.Clear();
        this.routeListBox.Items.Add(
            snapshot.IsAvailable
                ? "Choose a sector or navigation target from the catalog."
                : snapshot.StatusText);
        this.warningListBox.Items.Clear();
    }

    private void ShowPreviewFailure(
        int processId,
        NavigationDestination destination,
        string error)
    {
        var snapshot = this.clientManager.GetNavigationRouteSnapshot(
            processId);

        this.pilotValueLabel.Text =
            snapshot.CharacterName ?? "Unavailable";
        this.currentValueLabel.Text = snapshot.CurrentSector == null
            ? "Unavailable"
            : $"{snapshot.CurrentSector.SystemName} / {snapshot.CurrentSector.Name}";
        this.destinationValueLabel.Text = destination.DisplayName;
        this.statusValueLabel.Text = error;
        this.hopValueLabel.Text = "—";
        this.routeModeLabel.Text = "ROUTE PREVIEW";
        this.setDestinationButton.Enabled = false;
        this.clearRouteButton.Enabled = snapshot.HasRoute;
        this.showCurrentRouteButton.Enabled = true;
        this.selectNextTargetButton.Enabled = false;
        this.routeListBox.Items.Clear();
        this.routeListBox.Items.Add(error);
        this.warningListBox.Items.Clear();
    }

    private void ShowUnavailable(string message)
    {
        this.pilotValueLabel.Text = "Unavailable";
        this.currentValueLabel.Text = "Unavailable";
        this.destinationValueLabel.Text = "Not set";
        this.statusValueLabel.Text = message;
        this.hopValueLabel.Text = "—";
        this.routeModeLabel.Text = "CURRENT ROUTE";
        this.routeListBox.Items.Clear();
        this.routeListBox.Items.Add(message);
        this.warningListBox.Items.Clear();
        this.setDestinationButton.Enabled = false;
        this.clearRouteButton.Enabled = false;
        this.showCurrentRouteButton.Enabled = false;
        this.selectNextTargetButton.Enabled = false;
    }

    private void ClientComboBox_OnSelectedIndexChanged(
        object? sender,
        EventArgs e)
    {
        if (this.isRefreshingClients)
        {
            return;
        }

        this.windowPlacement.Rebind(this.SelectedProcessId);
        this.RebuildCatalogTree();
        this.ShowCurrentRoute();
    }

    private void SearchTextBox_OnTextChanged(
        object? sender,
        EventArgs e)
    {
        this.RebuildCatalogTree();
    }

    private void DestinationFilterComboBox_OnSelectedIndexChanged(
        object? sender,
        EventArgs e)
    {
        this.previewDestination = null;
        this.routeFingerprint = "";
        this.RebuildCatalogTree();
        this.RefreshRouteDisplay();
    }

    private void NearestFirstCheckBox_OnCheckedChanged(
        object? sender,
        EventArgs e)
    {
        this.RebuildCatalogTree();
    }

    private void CloseAfterSettingDestinationCheckBox_OnCheckedChanged(
        object? sender,
        EventArgs e)
    {
        this.clientManager
            .NavigationPlannerSettings
            .CloseAfterSettingDestination =
                this.closeAfterSettingDestinationCheckBox.Checked;

        this.clientManager.SaveSettings();
    }

    private void CatalogTree_OnAfterSelect(
        object? sender,
        TreeViewEventArgs e)
    {
        this.previewDestination =
            (e.Node?.Tag as CatalogNodeTag)?.Destination;
        this.routeFingerprint = "";
        this.RefreshRouteDisplay();
    }

    private void SetDestinationButton_OnClick(
        object? sender,
        EventArgs e)
    {
        var processId = this.SelectedProcessId;

        if (!processId.HasValue || this.previewDestination == null)
        {
            return;
        }

        var result = this.clientManager.SetNavigationDestination(
            processId.Value,
            this.previewDestination);

        if (!result.Succeeded)
        {
            MessageBox.Show(
                this,
                result.Error,
                "Could not set destination",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        if (this.closeAfterSettingDestinationCheckBox.Checked)
        {
            this.Close();
            return;
        }

        this.ShowCurrentRoute();
    }

    private void ClearRouteButton_OnClick(
        object? sender,
        EventArgs e)
    {
        var processId = this.SelectedProcessId;

        if (!processId.HasValue)
        {
            return;
        }

        var result = this.clientManager.ClearNavigationRoute(
            processId.Value);

        if (!result.Succeeded)
        {
            MessageBox.Show(
                this,
                result.Error,
                "Could not clear route",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        this.ShowCurrentRoute();
    }

    private void ShowCurrentRouteButton_OnClick(
        object? sender,
        EventArgs e)
    {
        this.ShowCurrentRoute();
    }

    private async void SelectNextTargetButton_OnClick(
        object? sender,
        EventArgs e)
    {
        var processId = this.SelectedProcessId;

        if (!processId.HasValue || this.isTargetSelectionBusy)
        {
            return;
        }

        this.isTargetSelectionBusy = true;
        this.selectNextTargetButton.Enabled = false;
        this.selectNextTargetButton.Text = "Selecting...";

        await this.targetSelectionCancellationTokenSource?.CancelAsync()!;
        this.targetSelectionCancellationTokenSource?.Dispose();
        this.targetSelectionCancellationTokenSource =
            new CancellationTokenSource();

        NavigationTargetSelectionResult result;
        var wasVisible = this.Visible;

        if (wasVisible)
        {
            this.Hide();
        }

        try
        {
            result = await this.clientManager
                .SelectNextNavigationTargetAsync(
                    processId.Value,
                    this.targetSelectionCancellationTokenSource.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            result = NavigationTargetSelectionResult.Failure(
                string.Concat(
                    "Route-target selection failed: ",
                    ex.Message));
        }
        finally
        {
            this.isTargetSelectionBusy = false;
            this.targetSelectionCancellationTokenSource?.Dispose();
            this.targetSelectionCancellationTokenSource = null;

            if (!this.IsDisposed && !this.Disposing)
            {
                this.selectNextTargetButton.Text = "Select next target";
                this.routeFingerprint = "";
                this.RefreshRouteDisplay();

                if (wasVisible)
                {
                    this.Show();
                    this.Activate();
                }
            }
        }

        if (!this.IsDisposed && !this.Disposing)
        {
            this.statusValueLabel.Text = result.Succeeded
                ? string.Concat(
                    "Selected ",
                    result.TargetName,
                    ".")
                : result.Error;
        }
    }

    private void ShowCurrentRoute()
    {
        this.previewDestination = null;
        this.catalogTree.SelectedNode = null;
        this.routeFingerprint = "";
        this.RefreshRouteDisplay();
    }

    private string BuildCatalogContextFingerprint()
    {
        var processId = this.SelectedProcessId;

        if (!processId.HasValue)
        {
            return $"none|{this.SelectedDestinationFilter}|{this.nearestFirstCheckBox.Checked}";
        }

        var snapshot = this.clientManager
            .GetNavigationRouteSnapshot(processId.Value);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{processId.Value}|{snapshot.CurrentSector?.Key}|{snapshot.Profession}|{this.SelectedDestinationFilter}|{this.nearestFirstCheckBox.Checked}");
    }

    private NavigationDestinationFilter SelectedDestinationFilter =>
        this.destinationFilterComboBox.SelectedIndex switch
        {
            1 => NavigationDestinationFilter.Sectors,
            2 => NavigationDestinationFilter.NavigationPoints,
            3 => NavigationDestinationFilter.Stations,
            4 => NavigationDestinationFilter.LandablePlanets,
            5 => NavigationDestinationFilter.GatesAndAccelerators,
            _ => NavigationDestinationFilter.All,
        };

    private bool MatchesSectorFilter(
        NavigationDestinationFilter filter,
        GalaxySectorDefinition sector)
    {
        return filter switch
        {
            NavigationDestinationFilter.All => true,
            NavigationDestinationFilter.Sectors => true,
            NavigationDestinationFilter.LandablePlanets =>
                this.clientManager
                    .NavigationRoutes
                    .Catalog
                    .IsLandablePlanetSector(sector.Key),
            NavigationDestinationFilter.NavigationPoints => false,
            NavigationDestinationFilter.Stations => false,
            NavigationDestinationFilter.GatesAndAccelerators => false,
            _ => false,
        };
    }

    private bool MatchesTargetFilter(
        NavigationDestinationFilter filter,
        string sectorKey,
        GalaxyNavigationCatalogTarget target)
    {
        if (filter == NavigationDestinationFilter.All)
        {
            return true;
        }

        if (filter == NavigationDestinationFilter.NavigationPoints)
        {
            return target.Kind ==
                       GalaxyNavigationTargetKind.NavigationPoint ||
                   (target.Kind == GalaxyNavigationTargetKind.Planet &&
                    !this.clientManager
                        .NavigationRoutes
                        .Catalog
                        .IsLandablePlanetTarget(
                            sectorKey,
                            target));
        }

        if (filter == NavigationDestinationFilter.Stations)
        {
            return target.Kind ==
                   GalaxyNavigationTargetKind.Station;
        }

        if (filter ==
            NavigationDestinationFilter.GatesAndAccelerators)
        {
            return target.Kind ==
                   GalaxyNavigationTargetKind.SectorGate;
        }

        return false;
    }

    private string BuildSectorNodeText(
        GalaxySectorDefinition sector,
        GalaxyRouteDistanceResult distances)
    {
        var category = this.clientManager
            .NavigationRoutes
            .Catalog
            .IsLandablePlanetSector(sector.Key)
                ? "  [landable planet]"
                : "";

        return string.Concat(
            sector.Name,
            category,
            FormatSectorDistance(distances, sector.Key));
    }

    private static string FormatSectorDistance(
        GalaxyRouteDistanceResult distances,
        string sectorKey)
    {
        if (!distances.Succeeded)
        {
            return "";
        }

        if (!distances.TryGetDistance(sectorKey, out var distance))
        {
            return "  · unreachable";
        }

        return distance == 0
            ? "  · here"
            : string.Concat(
                "  · ",
                FormatTransitionCount(distance));
    }

    private static string FormatTargetDistance(
        GalaxyRouteDistanceResult distances,
        string sectorKey)
    {
        if (!distances.Succeeded)
        {
            return "";
        }

        if (!distances.TryGetDistance(sectorKey, out var distance))
        {
            return "  · unreachable";
        }

        return distance == 0
            ? "  · local target"
            : string.Concat(
                "  · ",
                FormatTransitionCount(distance),
                " + target");
    }

    private static string FormatTransitionCount(int count)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{count} transition{(count == 1 ? "" : "s")}");
    }

    private int? SelectedProcessId =>
        this.clientComboBox.SelectedItem is ClientChoice choice
            ? choice.ProcessId
            : null;

    private static string FormatRouteStep(
        NavigationRouteStep step)
    {
        return step.Kind == NavigationRouteStepKind.SectorTransition
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{step.Number,2}. {step.DepartureTargetName}  →  {step.ToSystemName} / {step.ToSectorName}")
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{step.Number,2}. Final nav  →  {step.FinalTargetName}");
    }

    private static string BuildClientDisplayName(
        ClientInstance client,
        NavigationRouteSnapshot snapshot)
    {
        var pilot = client.LiveCharacterIdentity.Name ??
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"PID {client.ProcessId}");

        var location = snapshot.CurrentSector == null
            ? client.LifecycleState.ToString()
            : $"{snapshot.CurrentSector.SystemName} / {snapshot.CurrentSector.Name}";

        return $"{pilot} · {location}";
    }

    private static Label CreateSmallLabel(
        string text,
        int x,
        int y)
    {
        return new Label
        {
            AutoSize = true,
            Text = text,
            ForeColor = mutedTextColor,
            Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
            Location = new Point(x, y),
        };
    }

    private static void AddSummaryRow(
        TableLayoutPanel layout,
        int row,
        string label,
        Label valueLabel)
    {
        layout.RowStyles.Add(
            new RowStyle(SizeType.Percent, 20));

        var nameLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = label,
            ForeColor = mutedTextColor,
            Font = new Font("Segoe UI", 8.0f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
        };

        valueLabel.Dock = DockStyle.Fill;
        valueLabel.ForeColor = textColor;
        valueLabel.TextAlign = ContentAlignment.MiddleLeft;
        valueLabel.AutoEllipsis = true;

        layout.Controls.Add(nameLabel, 0, row);
        layout.Controls.Add(valueLabel, 1, row);
    }

    private static void ConfigureButton(
        Button button,
        string text,
        int width)
    {
        button.Text = text;
        button.Width = width;
        button.Height = 30;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = borderColor;
        button.FlatAppearance.MouseOverBackColor =
            Color.FromArgb(32, 65, 82);
        button.BackColor = headerColor;
        button.ForeColor = textColor;
        button.Margin = new Padding(8, 0, 0, 0);
    }

    private static string? GetNodeIdentity(TreeNode? node)
    {
        if (node?.Tag is not CatalogNodeTag tag)
        {
            return null;
        }

        return tag.Destination.Kind == NavigationDestinationKind.Target
            ? tag.Destination.TargetKey
            : tag.Destination.SectorKey;
    }


    private sealed class PlannerButton : Button
    {
        private bool isHovered;
        private bool isPressed;

        public PlannerButton()
        {
            this.SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer,
                value: true);

            this.UseVisualStyleBackColor = false;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            this.isHovered = true;
            this.Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            this.isHovered = false;
            this.isPressed = false;
            this.Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            if (e.Button == MouseButtons.Left)
            {
                this.isPressed = true;
                this.Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            this.isPressed = false;
            this.Invalidate();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            this.isPressed = false;
            this.Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var bounds = this.ClientRectangle;

            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return;
            }

            var background = !this.Enabled
                ? Color.FromArgb(28, 38, 48)
                : this.isPressed
                    ? Color.FromArgb(28, 79, 105)
                    : this.isHovered
                        ? Color.FromArgb(32, 65, 82)
                        : headerColor;

            var border = this.Enabled
                ? borderColor
                : Color.FromArgb(69, 88, 101);

            var foreground = this.Enabled
                ? textColor
                : mutedTextColor;

            using var backgroundBrush = new SolidBrush(background);
            using var borderPen = new Pen(border);

            e.Graphics.FillRectangle(backgroundBrush, bounds);
            e.Graphics.DrawRectangle(
                borderPen,
                bounds.Left,
                bounds.Top,
                Math.Max(0, bounds.Width - 1),
                Math.Max(0, bounds.Height - 1));

            TextRenderer.DrawText(
                e.Graphics,
                this.Text,
                this.Font,
                bounds,
                foreground,
                TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix |
                TextFormatFlags.NoPadding |
                TextFormatFlags.SingleLine);

            if (this.Focused && this.ShowFocusCues)
            {
                ControlPaint.DrawFocusRectangle(
                    e.Graphics,
                    Rectangle.Inflate(bounds, -4, -4),
                    foreground,
                    background);
            }
        }
    }

    private sealed record CatalogNodeTag(
        NavigationDestination Destination);

    private sealed record ClientChoice(
        ClientInstance Client,
        string DisplayName)
    {
        public int ProcessId => this.Client.ProcessId;

        public override string ToString()
        {
            return this.DisplayName;
        }
    }

    private readonly record struct CatalogSearch(
        string Type,
        string Text)
    {
        public bool IsEmpty =>
            string.IsNullOrWhiteSpace(this.Type) &&
            string.IsNullOrWhiteSpace(this.Text);

        public static CatalogSearch Parse(string? value)
        {
            var trimmed = value?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return new CatalogSearch("", "");
            }

            var separator = trimmed.IndexOf(':', StringComparison.Ordinal);

            if (separator > 0)
            {
                return new CatalogSearch(
                    trimmed[..separator].Trim().ToLowerInvariant(),
                    trimmed[(separator + 1)..].Trim());
            }

            var firstSpace = trimmed.IndexOf(' ', StringComparison.Ordinal);

            if (firstSpace > 0)
            {
                var possibleType = trimmed[..firstSpace]
                    .Trim()
                    .ToLowerInvariant();

                if (possibleType is
                    "system" or
                    "sector" or
                    "target" or
                    "gate" or
                    "station" or
                    "planet" or
                    "accelerator")
                {
                    return new CatalogSearch(
                        possibleType,
                        trimmed[(firstSpace + 1)..].Trim());
                }
            }

            return new CatalogSearch("", trimmed);
        }

        public bool MatchesSystem(string systemName)
        {
            if (this.IsEmpty)
            {
                return true;
            }

            return (string.IsNullOrWhiteSpace(this.Type) ||
                    string.Equals(this.Type, "system", StringComparison.Ordinal)) &&
                   Contains(systemName, this.Text);
        }

        public bool MatchesSector(GalaxySectorDefinition sector)
        {
            if (this.IsEmpty)
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(this.Type) &&
                !string.Equals(this.Type, "sector", StringComparison.Ordinal))
            {
                return false;
            }

            var search = this;
            return Contains(sector.Name, this.Text) ||
                   sector.Aliases.Any(alias =>
                                          Contains(alias, search.Text));
        }

        public bool MatchesTarget(
            GalaxyNavigationCatalogTarget target)
        {
            if (this.IsEmpty)
            {
                return true;
            }

            if (string.Equals(this.Type, "system", StringComparison.Ordinal) ||
                string.Equals(this.Type, "sector", StringComparison.Ordinal))
            {
                return false;
            }

            var displayName =
                NavigationDestination.GetTargetDisplayName(target);

            if (!Contains(displayName, this.Text) &&
                !Contains(target.MapDisplayName, this.Text))
            {
                return false;
            }

            var targetType =
                NavigationDestination.ClassifyTarget(target);

            return string.IsNullOrWhiteSpace(this.Type) ||
                   string.Equals(this.Type, "target", StringComparison.Ordinal) ||
                   string.Equals(this.Type, targetType, StringComparison.Ordinal);
        }

        private static bool Contains(
            string value,
            string search)
        {
            return value.Contains(
                search,
                StringComparison.OrdinalIgnoreCase);
        }
    }
}

