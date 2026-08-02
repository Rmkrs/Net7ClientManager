// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using System.ComponentModel;
using System.Globalization;
using Net7ClientManager.Core;
using Net7ClientManager.GalaxyKnowledge;
using Net7ClientManager.Models;
using Net7ClientManager.Navigation;
using Net7ClientManager.Observations.Models;
using Net7ClientManager.Observations.Observers;
using Net7ClientManager.Services;
using Net7ClientManager.Shopping;

public sealed class WorldFindForm : ThemedForm
{
    private const int MaximumResults = 300;
    private const int SearchCandidateLimit = 5000;

    private readonly ClientManager clientManager;
    private readonly WindowPlacementBinding windowPlacement;
    private readonly ThemedTabHost tabs = new();
    private readonly ThemedTabPage searchPage = new("Search");
    private readonly ThemedTabPage shoppingListPage = new("Shopping List");
    private readonly Dictionary<int, ThemedTabPage> itemDetailPages = [];
    private readonly Dictionary<ThemedTabPage, int> detailPageItemIds = [];
    private readonly Dictionary<ThemedTabPage, ThemedTabPage>
        detailPageReturnPages = [];
    private readonly HashSet<int> expandedVendorItemIds = [];
    private readonly ComboBox clientComboBox = new();
    private readonly ComboBox scopeComboBox = new();
    private readonly ComboBox categoryComboBox = new();
    private readonly ComboBox effectComboBox = new();
    private readonly TextBox searchTextBox = new();
    private readonly ThemedCheckBox vendorSourceCheckBox = new();
    private readonly ThemedCheckBox lootSourceCheckBox = new();
    private readonly ThemedCheckBox craftedSourceCheckBox = new();
    private readonly ThemedCheckBox refinedSourceCheckBox = new();
    private readonly ThemedCheckBox harvestedSourceCheckBox = new();
    private readonly ThemedCheckBox missionSourceCheckBox = new();
    private readonly Button shoppingListButton = new();
    private readonly DataGridView resultsGrid = new();
    private readonly ActionToolTip itemToolTip = new();
    private readonly ActionToolTip contextualToolTip = new();
    private readonly Label statusLabel = new();
    private readonly Label dataSetLabel = new();
    private readonly System.Windows.Forms.Timer refreshTimer = new();
    private readonly System.Windows.Forms.Timer searchDebounceTimer = new();
    private ContextMenuStrip? resultsContextMenu;

    private WorldSearchIndex worldSearchIndex;
    private GalaxyFinderItemSearchIndex itemSearchIndex;
    private int? requestedProcessId;
    private bool isRefreshingClients;
    private bool isRefreshingResults;
    private bool isRefreshingFilters;
    private string clientFingerprint = "";
    private string routeContextFingerprint = "";
    private string? transientStatus;
    private FinderGridMode gridMode = FinderGridMode.Mixed;
    private string? activeSortColumnName;
    private ListSortDirection activeSortDirection =
        ListSortDirection.Ascending;
    private bool knowledgeFilterChoicesPending;
    private bool searchResultsStale;
    private IReadOnlyList<FinderSearchRow> currentSearchRows = [];
    private GalaxyRouteDistanceResult currentSearchItemDistances =
        GalaxyRouteDistanceResult.Failure(
            "Select a hosted pilot to calculate hops.");
    private string currentSearchQuery = "";
    private ClientReputationObservation currentPilotReputation =
        ClientReputationObservation.Unavailable(
            "Select a hosted pilot to determine mob disposition.");
    private int? hoveredItemToolTipTemplateId;
    private int hoveredResultRowIndex = -1;
    private int hoveredResultColumnIndex = -1;
    private GalaxyFinderShoppingListView? shoppingListView;

    public WorldFindForm(
        ClientManager clientManager,
        int? initialProcessId = null)
    {
        ArgumentNullException.ThrowIfNull(clientManager);
        this.clientManager = clientManager;
        this.requestedProcessId = initialProcessId;
        this.worldSearchIndex = new WorldSearchIndex(
            this.clientManager.NavigationData);
        this.itemSearchIndex = new GalaxyFinderItemSearchIndex(
            this.clientManager.GalaxyKnowledge);

        this.Text = "Galaxy Finder";
        this.Icon = ResourceLoader.Net7ClientManagerIcon;
        this.StartPosition = FormStartPosition.CenterScreen;
        this.Size = new Size(width: 1320, height: 780);
        this.MinimumSize = new Size(width: 980, height: 620);
        this.BackColor = MainWindowTheme.Background;
        this.ForeColor = MainWindowTheme.Text;
        this.Font = MainWindowTheme.CreateBodyFont();
        this.ConfigureWindowChrome(
            allowResize: true,
            showMinimizeButton: true,
            showMaximizeButton: true);
        this.ConfigureHelpTopic(
            HelpTopicIds.GalaxyFinder,
            () => this.SelectedProcessId);

        this.BuildUi();
        this.ConfigureHelpTour(this.ShowHelpTour);
        this.windowPlacement =
            clientManager.BindClientWindowPlacement(
                this,
                WindowPlacementIds.WorldFind,
                initialProcessId);
        this.RestoreSettings();
        this.WireEvents();

        this.refreshTimer.Interval = 750;
        this.refreshTimer.Tick += this.RefreshTimer_OnTick;
        this.refreshTimer.Start();

        this.searchDebounceTimer.Interval = 250;
        this.searchDebounceTimer.Tick +=
            this.SearchDebounceTimer_OnTick;

        this.RefreshClients(force: true);
        this.SelectRequestedProcess();
        this.RefreshResults();
    }


    internal void ShowFinderHelpTour()
    {
        this.tabs.SetPageVisible(this.searchPage, visible: true);
        this.tabs.SelectedPage = this.searchPage;
        GuidedTourOverlay.Show(
            this,
            [
                new GuidedTourStep(
                    () => this.clientComboBox,
                    "Choose the pilot who needs it",
                    "Finder uses the selected pilot for route distances, mob disposition, inventory-aware details, and Set destination actions."),
                new GuidedTourStep(
                    () => this.scopeComboBox,
                    "Choose what you are looking for",
                    "Search Places, NPCs, Mobs, Harvestables, or Items. Each scope changes the result columns so the useful details stay visible."),
                new GuidedTourStep(
                    () => this.searchTextBox,
                    "Describe the thing",
                    "Type any part of a name. Item searches can also be narrowed by category, effect, and where the item comes from."),
                new GuidedTourStep(
                    () => this.vendorSourceCheckBox,
                    "Filter by source",
                    "For items, choose vendors, loot, crafting, refining, harvesting, or missions. You can combine sources instead of starting separate searches."),
                new GuidedTourStep(
                    () => this.resultsGrid,
                    "Inspect and act",
                    "Select a result for details. Item pages show effects and real sources such as vendors, mobs, fields, recipes, and refining paths. Useful locations can be sent straight to Navigation."),
                new GuidedTourStep(
                    () => this.shoppingListButton,
                    "Turn finds into a plan",
                    "Add equipment, ammunition, or components to a Shopping List. Client Manager expands recipes and shows what to buy, loot, harvest, or manufacture."),
            ]);
    }

    internal void ShowShoppingHelpTour()
    {
        this.ShowShoppingListPage();
        GuidedTourOverlay.Show(
            this,
            [
                new GuidedTourStep(
                    () => this.tabs,
                    "Search and planning live together",
                    "Keep Search open in its own tab when you want to move between finding items and reviewing the active Shopping List."),
                new GuidedTourStep(
                    () => this.shoppingListView,
                    "Build the acquisition plan",
                    "Requested outputs are things you want to obtain in addition to what you already own. The plan expands recipes, counts useful inventory, and updates as you acquire materials."),
                new GuidedTourStep(
                    () => this.shoppingListView,
                    "Use it while playing",
                    "When a docked vendor sells something useful, the in-game vendor companion calls it out and reduces the remaining need as purchases are made."),
            ]);
    }

    private void ShowHelpTour()
    {
        if (ReferenceEquals(this.tabs.SelectedPage, this.shoppingListPage))
        {
            this.ShowShoppingHelpTour();
            return;
        }

        this.ShowFinderHelpTour();
    }

    public void SelectProcess(int? processId)
    {
        this.windowPlacement.Rebind(processId);
        this.requestedProcessId = processId;
        this.RefreshClients(force: true);
        this.SelectRequestedProcess();
        this.RefreshResults();
    }

    public void Search(string? query)
    {
        this.ShowSearchPage();

        if (!string.IsNullOrWhiteSpace(query))
        {
            this.PrepareForContextualItemSearch();
            this.searchTextBox.Text = query.Trim();
        }

        this.searchDebounceTimer.Stop();
        this.RefreshResults();
        this.searchTextBox.Focus();
        this.searchTextBox.SelectAll();
    }

    private void PrepareForContextualItemSearch()
    {
        this.isRefreshingFilters = true;

        try
        {
            this.scopeComboBox.SelectedItem =
                this.scopeComboBox.Items
                    .Cast<SearchScopeChoice>()
                    .First(choice =>
                        choice.Scope ==
                        GalaxyFinderSearchScope.Items);
            this.categoryComboBox.SelectedItem =
                this.categoryComboBox.Items
                    .Cast<ItemCategoryChoice>()
                    .First(choice =>
                        choice.Category ==
                        GalaxyFinderItemCategory.All);
            this.vendorSourceCheckBox.Checked = false;
            this.lootSourceCheckBox.Checked = false;
            this.craftedSourceCheckBox.Checked = false;
            this.refinedSourceCheckBox.Checked = false;
            this.harvestedSourceCheckBox.Checked = false;
            this.missionSourceCheckBox.Checked = false;
        }
        finally
        {
            this.isRefreshingFilters = false;
        }

        this.RefreshFilterAvailability();
        this.RefreshEffectChoices();

        this.isRefreshingFilters = true;

        try
        {
            this.effectComboBox.SelectedIndex = 0;
        }
        finally
        {
            this.isRefreshingFilters = false;
        }

        this.RefreshGridMode();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        this.searchTextBox.Focus();
        this.searchTextBox.SelectAll();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        this.SaveSettings();

        this.refreshTimer.Stop();
        this.refreshTimer.Tick -= this.RefreshTimer_OnTick;
        this.refreshTimer.Dispose();

        this.searchDebounceTimer.Stop();
        this.searchDebounceTimer.Tick -=
            this.SearchDebounceTimer_OnTick;
        this.searchDebounceTimer.Dispose();

        this.clientComboBox.SelectedIndexChanged -=
            this.ClientComboBox_OnSelectedIndexChanged;
        this.scopeComboBox.SelectedIndexChanged -=
            this.ScopeComboBox_OnSelectedIndexChanged;
        this.categoryComboBox.SelectedIndexChanged -=
            this.CategoryComboBox_OnSelectedIndexChanged;
        this.effectComboBox.SelectedIndexChanged -=
            this.EffectComboBox_OnSelectedIndexChanged;
        this.searchTextBox.TextChanged -=
            this.SearchTextBox_OnTextChanged;
        this.searchTextBox.KeyDown -=
            this.SearchTextBox_OnKeyDown;
        this.shoppingListButton.Click -=
            this.ShoppingListButton_OnClick;
        this.vendorSourceCheckBox.CheckedChanged -=
            this.SourceCheckBox_OnCheckedChanged;
        this.lootSourceCheckBox.CheckedChanged -=
            this.SourceCheckBox_OnCheckedChanged;
        this.craftedSourceCheckBox.CheckedChanged -=
            this.SourceCheckBox_OnCheckedChanged;
        this.refinedSourceCheckBox.CheckedChanged -=
            this.SourceCheckBox_OnCheckedChanged;
        this.harvestedSourceCheckBox.CheckedChanged -=
            this.SourceCheckBox_OnCheckedChanged;
        this.missionSourceCheckBox.CheckedChanged -=
            this.SourceCheckBox_OnCheckedChanged;
        this.resultsGrid.CellClick -=
            this.ResultsGrid_OnCellContentClick;
        this.resultsGrid.CellDoubleClick -=
            this.ResultsGrid_OnCellDoubleClick;
        this.resultsGrid.CellMouseDown -=
            this.ResultsGrid_OnCellMouseDown;
        this.resultsGrid.DataError -=
            this.ResultsGrid_OnDataError;
        this.resultsGrid.MouseMove -=
            this.ResultsGrid_OnMouseMove;
        this.resultsGrid.MouseLeave -=
            this.ResultsGrid_OnMouseLeave;
        this.resultsGrid.Scroll -=
            this.ResultsGrid_OnScroll;
        this.resultsGrid.KeyDown -=
            this.ResultsGrid_OnKeyDown;
        this.tabs.PageCloseRequested -=
            this.Tabs_OnPageCloseRequested;
        this.clientManager.NavigationDataChanged -=
            this.ClientManager_OnNavigationDataChanged;
        this.clientManager.GalaxyKnowledgeChanged -=
            this.ClientManager_OnGalaxyKnowledgeChanged;
        this.resultsContextMenu?.Dispose();
        this.resultsContextMenu = null;
        this.itemToolTip.Dispose();
        this.contextualToolTip.Dispose();
        this.windowPlacement.Dispose();

        base.OnFormClosed(e);
    }

    private void BuildUi()
    {
        this.tabs.Dock = DockStyle.Fill;
        this.tabs.AddPage(this.searchPage);
        this.tabs.AddPage(this.shoppingListPage);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = MainWindowTheme.Background,
            Padding = new Padding(10),
        };
        root.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(
            new RowStyle(SizeType.Absolute, 178f));
        root.RowStyles.Add(
            new RowStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(
            new RowStyle(SizeType.Absolute, 46f));

        root.Controls.Add(this.CreateSearchPanel(), 0, 0);
        root.Controls.Add(this.CreateResultsGrid(), 0, 1);
        root.Controls.Add(this.CreateFooterPanel(), 0, 2);

        this.searchPage.Controls.Add(root);

        this.shoppingListView = new GalaxyFinderShoppingListView(
            this.clientManager,
            () => this.SelectedProcessId,
            this.ResolveItemDetailIcon,
            this.QueueOpenItemDetail,
            this.QueueShowSearchPage);
        this.shoppingListView.ShowBackButton = !this.clientManager
            .WorldFindSettings
            .KeepSearchOpenInTab;
        this.shoppingListPage.Controls.Add(this.shoppingListView);

        this.Controls.Add(this.tabs);
    }

    private Control CreateSearchPanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = MainWindowTheme.Panel,
            Padding = new Padding(16, 10, 16, 10),
            Margin = new Padding(0, 0, 0, 10),
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 6,
            RowCount = 4,
        };
        layout.ColumnStyles.Add(
            new ColumnStyle(SizeType.Absolute, 58f));
        layout.ColumnStyles.Add(
            new ColumnStyle(SizeType.Absolute, 260f));
        layout.ColumnStyles.Add(
            new ColumnStyle(SizeType.Absolute, 64f));
        layout.ColumnStyles.Add(
            new ColumnStyle(SizeType.Absolute, 190f));
        layout.ColumnStyles.Add(
            new ColumnStyle(SizeType.Absolute, 58f));
        layout.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(
            new RowStyle(SizeType.Absolute, 38f));
        layout.RowStyles.Add(
            new RowStyle(SizeType.Absolute, 40f));
        layout.RowStyles.Add(
            new RowStyle(SizeType.Absolute, 40f));
        layout.RowStyles.Add(
            new RowStyle(SizeType.Absolute, 34f));

        var headingLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
        };
        headingLayout.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 100f));
        headingLayout.ColumnStyles.Add(
            new ColumnStyle(SizeType.Absolute, 118f));

        var heading = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Find places, people, items, effects, and known sources",
            ForeColor = MainWindowTheme.Accent,
            Font = MainWindowTheme.CreateHeadingFont(11.0f),
            TextAlign = ContentAlignment.MiddleLeft,
        };

        this.shoppingListButton.Text = "Shopping List";
        this.shoppingListButton.Dock = DockStyle.Fill;
        this.shoppingListButton.Margin = new Padding(6, 2, 0, 2);
        MainWindowTheme.StyleButton(
            this.shoppingListButton,
            primary: false);

        headingLayout.Controls.Add(heading, 0, 0);
        headingLayout.Controls.Add(this.shoppingListButton, 1, 0);
        layout.SetColumnSpan(headingLayout, 6);

        this.ConfigureComboBox(this.clientComboBox);
        this.ConfigureComboBox(this.scopeComboBox);
        this.ConfigureComboBox(this.categoryComboBox);
        this.ConfigureComboBox(this.effectComboBox);
        this.effectComboBox.DropDownWidth = 440;

        this.scopeComboBox.Items.AddRange(
        [
            new SearchScopeChoice(
                GalaxyFinderSearchScope.All,
                "All"),
            new SearchScopeChoice(
                GalaxyFinderSearchScope.Places,
                "Places"),
            new SearchScopeChoice(
                GalaxyFinderSearchScope.Npcs,
                "NPCs"),
            new SearchScopeChoice(
                GalaxyFinderSearchScope.Mobs,
                "Mobs"),
            new SearchScopeChoice(
                GalaxyFinderSearchScope.Harvestables,
                "Harvestables"),
            new SearchScopeChoice(
                GalaxyFinderSearchScope.Items,
                "Items"),
        ]);
        this.scopeComboBox.SelectedIndex = 0;

        this.categoryComboBox.Items.AddRange(
        [
            new ItemCategoryChoice(
                GalaxyFinderItemCategory.All,
                "All item categories"),
            new ItemCategoryChoice(
                GalaxyFinderItemCategory.Equipment,
                "Equipment"),
            new ItemCategoryChoice(
                GalaxyFinderItemCategory.Weapon,
                "Weapons"),
            new ItemCategoryChoice(
                GalaxyFinderItemCategory.Engine,
                "Engines"),
            new ItemCategoryChoice(
                GalaxyFinderItemCategory.Shield,
                "Shields"),
            new ItemCategoryChoice(
                GalaxyFinderItemCategory.Reactor,
                "Reactors"),
            new ItemCategoryChoice(
                GalaxyFinderItemCategory.Device,
                "Devices"),
            new ItemCategoryChoice(
                GalaxyFinderItemCategory.Ammo,
                "Ammo"),
            new ItemCategoryChoice(
                GalaxyFinderItemCategory.Component,
                "Components"),
            new ItemCategoryChoice(
                GalaxyFinderItemCategory.RawResource,
                "Raw resources"),
            new ItemCategoryChoice(
                GalaxyFinderItemCategory.MobLoot,
                "Mob loot"),
            new ItemCategoryChoice(
                GalaxyFinderItemCategory.RefinedMaterial,
                "Refined materials"),
            new ItemCategoryChoice(
                GalaxyFinderItemCategory.TradeGood,
                "Trade goods"),
            new ItemCategoryChoice(
                GalaxyFinderItemCategory.Other,
                "Other items"),
        ]);
        this.categoryComboBox.SelectedIndex = 0;

        this.searchTextBox.Dock = DockStyle.Fill;
        this.searchTextBox.Margin = new Padding(0, 4, 0, 4);
        this.searchTextBox.PlaceholderText =
            "Name, manufacturer, effect, source, place, NPC, mob, or harvestable";
        MainWindowTheme.StyleTextBox(this.searchTextBox);

        layout.Controls.Add(headingLayout, 0, 0);
        layout.Controls.Add(CreateControlLabel("Pilot"), 0, 1);
        layout.Controls.Add(this.clientComboBox, 1, 1);
        layout.Controls.Add(CreateControlLabel("Find"), 2, 1);
        layout.Controls.Add(this.searchTextBox, 3, 1);
        layout.SetColumnSpan(this.searchTextBox, 3);

        layout.Controls.Add(CreateControlLabel("Scope"), 0, 2);
        layout.Controls.Add(this.scopeComboBox, 1, 2);
        layout.Controls.Add(CreateControlLabel("Category"), 2, 2);
        layout.Controls.Add(this.categoryComboBox, 3, 2);
        layout.Controls.Add(CreateControlLabel("Effect"), 4, 2);
        layout.Controls.Add(this.effectComboBox, 5, 2);

        var sourcePanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = new Padding(0, 2, 0, 0),
            BackColor = MainWindowTheme.Panel,
        };
        this.ConfigureSourceCheckBox(
            this.vendorSourceCheckBox,
            "Vendor");
        this.ConfigureSourceCheckBox(
            this.lootSourceCheckBox,
            "Loot");
        this.ConfigureSourceCheckBox(
            this.craftedSourceCheckBox,
            "Crafted");
        this.ConfigureSourceCheckBox(
            this.refinedSourceCheckBox,
            "Refined");
        this.ConfigureSourceCheckBox(
            this.harvestedSourceCheckBox,
            "Harvested");
        this.ConfigureSourceCheckBox(
            this.missionSourceCheckBox,
            "Mission");
        sourcePanel.Controls.Add(this.vendorSourceCheckBox);
        sourcePanel.Controls.Add(this.lootSourceCheckBox);
        sourcePanel.Controls.Add(this.craftedSourceCheckBox);
        sourcePanel.Controls.Add(this.refinedSourceCheckBox);
        sourcePanel.Controls.Add(this.harvestedSourceCheckBox);
        sourcePanel.Controls.Add(this.missionSourceCheckBox);

        layout.Controls.Add(CreateControlLabel("Sources"), 0, 3);
        layout.Controls.Add(sourcePanel, 1, 3);
        layout.SetColumnSpan(sourcePanel, 5);

        panel.Controls.Add(layout);
        return panel;
    }

    private Control CreateResultsGrid()
    {
        this.resultsGrid.Dock = DockStyle.Fill;
        this.resultsGrid.Margin = Padding.Empty;
        this.resultsGrid.AllowUserToAddRows = false;
        this.resultsGrid.AllowUserToDeleteRows = false;
        this.resultsGrid.AllowUserToResizeRows = false;
        this.resultsGrid.AutoGenerateColumns = false;
        this.resultsGrid.AutoSizeRowsMode =
            DataGridViewAutoSizeRowsMode.DisplayedCellsExceptHeaders;
        this.resultsGrid.BackgroundColor = MainWindowTheme.Panel;
        this.resultsGrid.BorderStyle = BorderStyle.None;
        this.resultsGrid.CellBorderStyle =
            DataGridViewCellBorderStyle.SingleHorizontal;
        this.resultsGrid.ColumnHeadersBorderStyle =
            DataGridViewHeaderBorderStyle.Single;
        this.resultsGrid.EnableHeadersVisualStyles = false;
        this.resultsGrid.GridColor = MainWindowTheme.Border;
        this.resultsGrid.MultiSelect = false;
        this.resultsGrid.ReadOnly = true;
        this.resultsGrid.RowHeadersVisible = false;
        this.resultsGrid.SelectionMode =
            DataGridViewSelectionMode.FullRowSelect;
        this.resultsGrid.RowTemplate.Height = 34;
        this.resultsGrid.RowTemplate.MinimumHeight = 34;

        this.resultsGrid.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = MainWindowTheme.Panel,
            ForeColor = MainWindowTheme.Text,
            SelectionBackColor = MainWindowTheme.ButtonHover,
            SelectionForeColor = MainWindowTheme.Text,
            Font = MainWindowTheme.CreateBodyFont(),
            Padding = new Padding(5, 0, 5, 0),
        };

        this.resultsGrid.AlternatingRowsDefaultCellStyle =
            new DataGridViewCellStyle
            {
                BackColor = MainWindowTheme.ElevatedPanel,
                ForeColor = MainWindowTheme.Text,
                SelectionBackColor = MainWindowTheme.ButtonHover,
                SelectionForeColor = MainWindowTheme.Text,
                Font = MainWindowTheme.CreateBodyFont(),
                Padding = new Padding(5, 0, 5, 0),
            };

        this.resultsGrid.ColumnHeadersDefaultCellStyle =
            new DataGridViewCellStyle
            {
                BackColor = MainWindowTheme.Header,
                ForeColor = MainWindowTheme.Accent,
                SelectionBackColor = MainWindowTheme.Header,
                SelectionForeColor = MainWindowTheme.Accent,
                Font = MainWindowTheme.CreateHeadingFont(9.0f),
                Alignment = DataGridViewContentAlignment.MiddleLeft,
                Padding = new Padding(5, 0, 5, 0),
            };
        this.resultsGrid.ColumnHeadersHeight = 36;
        this.resultsGrid.ColumnHeadersHeightSizeMode =
            DataGridViewColumnHeadersHeightSizeMode.DisableResizing;

        this.ConfigureGrid(FinderGridMode.Mixed, saveCurrent: false);
        return this.resultsGrid;
    }

    private Control CreateFooterPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = MainWindowTheme.Header,
            Padding = new Padding(12, 4, 12, 4),
            Margin = new Padding(0, 8, 0, 0),
        };
        panel.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 58f));
        panel.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 42f));

        this.statusLabel.Dock = DockStyle.Fill;
        this.statusLabel.ForeColor = MainWindowTheme.MutedText;
        this.statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        this.statusLabel.AutoEllipsis = true;

        this.dataSetLabel.Dock = DockStyle.Fill;
        this.dataSetLabel.ForeColor = MainWindowTheme.MutedText;
        this.dataSetLabel.TextAlign = ContentAlignment.MiddleRight;
        this.dataSetLabel.AutoEllipsis = true;

        panel.Controls.Add(this.statusLabel, 0, 0);
        panel.Controls.Add(this.dataSetLabel, 1, 0);
        return panel;
    }

    private void WireEvents()
    {
        this.clientComboBox.SelectedIndexChanged +=
            this.ClientComboBox_OnSelectedIndexChanged;
        this.scopeComboBox.SelectedIndexChanged +=
            this.ScopeComboBox_OnSelectedIndexChanged;
        this.categoryComboBox.SelectedIndexChanged +=
            this.CategoryComboBox_OnSelectedIndexChanged;
        this.effectComboBox.SelectedIndexChanged +=
            this.EffectComboBox_OnSelectedIndexChanged;
        this.searchTextBox.TextChanged +=
            this.SearchTextBox_OnTextChanged;
        this.searchTextBox.KeyDown +=
            this.SearchTextBox_OnKeyDown;
        this.shoppingListButton.Click +=
            this.ShoppingListButton_OnClick;
        this.vendorSourceCheckBox.CheckedChanged +=
            this.SourceCheckBox_OnCheckedChanged;
        this.lootSourceCheckBox.CheckedChanged +=
            this.SourceCheckBox_OnCheckedChanged;
        this.craftedSourceCheckBox.CheckedChanged +=
            this.SourceCheckBox_OnCheckedChanged;
        this.refinedSourceCheckBox.CheckedChanged +=
            this.SourceCheckBox_OnCheckedChanged;
        this.harvestedSourceCheckBox.CheckedChanged +=
            this.SourceCheckBox_OnCheckedChanged;
        this.missionSourceCheckBox.CheckedChanged +=
            this.SourceCheckBox_OnCheckedChanged;
        this.resultsGrid.CellClick +=
            this.ResultsGrid_OnCellContentClick;
        this.resultsGrid.CellDoubleClick +=
            this.ResultsGrid_OnCellDoubleClick;
        this.resultsGrid.CellMouseDown +=
            this.ResultsGrid_OnCellMouseDown;
        this.resultsGrid.DataError +=
            this.ResultsGrid_OnDataError;
        this.resultsGrid.MouseMove +=
            this.ResultsGrid_OnMouseMove;
        this.resultsGrid.MouseLeave +=
            this.ResultsGrid_OnMouseLeave;
        this.resultsGrid.Scroll +=
            this.ResultsGrid_OnScroll;
        this.resultsGrid.ColumnHeaderMouseClick +=
            this.ResultsGrid_OnColumnHeaderMouseClick;
        this.resultsGrid.KeyDown +=
            this.ResultsGrid_OnKeyDown;
        this.tabs.PageCloseRequested +=
            this.Tabs_OnPageCloseRequested;
        this.clientManager.NavigationDataChanged +=
            this.ClientManager_OnNavigationDataChanged;
        this.clientManager.GalaxyKnowledgeChanged +=
            this.ClientManager_OnGalaxyKnowledgeChanged;
    }

    private void RefreshTimer_OnTick(object? sender, EventArgs e)
    {
        this.RefreshClients(force: false);

        var fingerprint = this.BuildRouteContextFingerprint();

        if (string.Equals(
                fingerprint,
                this.routeContextFingerprint,
                StringComparison.Ordinal))
        {
            return;
        }

        // Keep the visible result set stable while live pilot and route
        // context continues changing behind it. The next user-initiated
        // search refresh uses the latest route context.
        this.routeContextFingerprint = fingerprint;
    }

    private void SearchDebounceTimer_OnTick(
        object? sender,
        EventArgs e)
    {
        this.searchDebounceTimer.Stop();
        this.RefreshResults();
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
        this.transientStatus = null;
        this.routeContextFingerprint =
            this.BuildRouteContextFingerprint();
        this.RefreshResults();
        this.RefreshOpenItemDetailPages();
        this.shoppingListView?.RefreshNow(force: true);
    }

    private void ScopeComboBox_OnSelectedIndexChanged(
        object? sender,
        EventArgs e)
    {
        if (this.isRefreshingFilters)
        {
            return;
        }

        this.transientStatus = null;
        this.RefreshFilterAvailability();
        this.RefreshEffectChoices();
        this.RefreshGridMode();
        this.RefreshResults();
    }

    private void CategoryComboBox_OnSelectedIndexChanged(
        object? sender,
        EventArgs e)
    {
        if (this.isRefreshingFilters)
        {
            return;
        }

        this.transientStatus = null;
        this.RefreshEffectChoices();
        this.RefreshGridMode();
        this.RefreshResults();
    }

    private void EffectComboBox_OnSelectedIndexChanged(
        object? sender,
        EventArgs e)
    {
        if (this.isRefreshingFilters)
        {
            return;
        }

        this.transientStatus = null;
        this.RefreshGridMode();
        this.RefreshResults();
    }

    private void SourceCheckBox_OnCheckedChanged(
        object? sender,
        EventArgs e)
    {
        if (this.isRefreshingFilters)
        {
            return;
        }

        this.transientStatus = null;
        this.RefreshGridMode();
        this.RefreshResults();
    }

    private void SearchTextBox_OnTextChanged(
        object? sender,
        EventArgs e)
    {
        this.transientStatus = null;
        this.searchDebounceTimer.Stop();

        this.statusLabel.Text = "Searching...";
        this.searchDebounceTimer.Start();
    }

    private void SearchTextBox_OnKeyDown(
        object? sender,
        KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            this.searchDebounceTimer.Stop();
            this.transientStatus = null;
            this.RefreshResults();
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }

        if (e.KeyCode != Keys.Down ||
            this.resultsGrid.Rows.Count == 0)
        {
            return;
        }

        this.resultsGrid.Focus();
        this.resultsGrid.Rows[0].Selected = true;
        this.resultsGrid.CurrentCell =
            this.resultsGrid.Rows[0].Cells[0];
        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    private void ShoppingListButton_OnClick(
        object? sender,
        EventArgs e)
    {
        this.ShowShoppingListPage();
    }

    private void ResultsGrid_OnCellContentClick(
        object? sender,
        DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 ||
            e.ColumnIndex < 0)
        {
            return;
        }

        var columnName = this.resultsGrid.Columns[e.ColumnIndex].Name;
        if (string.Equals(
                columnName,
                "Sources",
                StringComparison.Ordinal) &&
            this.TryToggleVendorExpansion(e.RowIndex))
        {
            return;
        }

        if (!string.Equals(
                columnName,
                "Destination",
                StringComparison.Ordinal))
        {
            return;
        }

        this.SetDestinationForRow(e.RowIndex);
    }

    private bool TryToggleVendorExpansion(int rowIndex)
    {
        if (rowIndex < 0 ||
            rowIndex >= this.resultsGrid.Rows.Count ||
            this.resultsGrid.Rows[rowIndex].Tag is not
                FinderSearchRow row ||
            row.ItemMatch is not { } itemMatch ||
            row.IsVendorRouteRow)
        {
            return false;
        }

        var additionalRoutes = this.GetAdditionalVendorRoutes(row);
        if (additionalRoutes.Count == 0)
        {
            return false;
        }

        var itemTemplateId = itemMatch.Item.ItemTemplateId;
        if (!this.expandedVendorItemIds.Add(itemTemplateId))
        {
            this.expandedVendorItemIds.Remove(itemTemplateId);
        }

        this.RenderCurrentSearchRows(row.Identity);
        return true;
    }

    private void ResultsGrid_OnCellDoubleClick(
        object? sender,
        DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 ||
            e.ColumnIndex < 0 ||
            this.resultsGrid.Columns[e.ColumnIndex].Name ==
            "Destination" ||
            this.resultsGrid.Rows[e.RowIndex].Tag is not
                FinderSearchRow row)
        {
            return;
        }

        if (row.ItemMatch != null)
        {
            this.OpenItemDetail(
                row.ItemMatch.Item.ItemTemplateId);
            return;
        }

        if (row.WorldMatch != null)
        {
            this.SetDestinationForRow(e.RowIndex);
        }
    }

    private void ResultsGrid_OnCellMouseDown(
        object? sender,
        DataGridViewCellMouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right ||
            e.RowIndex < 0 ||
            e.RowIndex >= this.resultsGrid.Rows.Count ||
            this.resultsGrid.Rows[e.RowIndex].Tag is not
                FinderSearchRow { ItemMatch: not null } row)
        {
            return;
        }

        this.resultsGrid.ClearSelection();
        this.resultsGrid.Rows[e.RowIndex].Selected = true;
        this.resultsGrid.CurrentCell =
            this.resultsGrid.Rows[e.RowIndex]
                .Cells[Math.Max(0, e.ColumnIndex)];

        this.ShowShoppingListContextMenu(
            row.ItemMatch.Item);
    }

    private void ShowShoppingListContextMenu(
        GalaxyItemKnowledge item)
    {
        this.resultsContextMenu?.Dispose();
        var menu = new ContextMenuStrip
        {
            ShowImageMargin = false,
            ShowCheckMargin = false,
        };
        var lists = this.clientManager.GetShoppingLists();
        var activeListId = this.clientManager.GetActiveShoppingListId();
        var orderedLists = lists
            .OrderBy(summary =>
                string.Equals(
                    summary.ListId,
                    activeListId,
                    StringComparison.Ordinal)
                    ? 0
                    : 1)
            .ThenBy(
                summary => summary.Name,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var addMenu = new ToolStripMenuItem("Add to");
        var addMultipleMenu = new ToolStripMenuItem("Add multiple to");
        if (orderedLists.Length == 0)
        {
            addMenu.DropDownItems.Add(
                this.CreateShoppingListTargetMenuItem(
                    "Shopping List",
                    item.ItemTemplateId,
                    listId: null,
                    promptForQuantity: false));
            addMultipleMenu.DropDownItems.Add(
                this.CreateShoppingListTargetMenuItem(
                    "Shopping List",
                    item.ItemTemplateId,
                    listId: null,
                    promptForQuantity: true));
        }
        else
        {
            foreach (var list in orderedLists)
            {
                var label = string.Equals(
                        list.ListId,
                        activeListId,
                        StringComparison.Ordinal)
                    ? string.Concat(list.Name, " (active)")
                    : list.Name;
                addMenu.DropDownItems.Add(
                    this.CreateShoppingListTargetMenuItem(
                        label,
                        item.ItemTemplateId,
                        list.ListId,
                        promptForQuantity: false));
                addMultipleMenu.DropDownItems.Add(
                    this.CreateShoppingListTargetMenuItem(
                        label,
                        item.ItemTemplateId,
                        list.ListId,
                        promptForQuantity: true));
            }
        }

        menu.Items.Add(addMenu);
        menu.Items.Add(addMultipleMenu);
        menu.Items.Add(new ToolStripSeparator());

        var newListItem = new ToolStripMenuItem(
            "New shopping list...");
        newListItem.Click += (_, _) =>
            this.CreateListAndAddItem(item.ItemTemplateId);
        menu.Items.Add(newListItem);

        menu.Closed += (_, _) =>
            this.QueueResultsContextMenuDisposal(menu);
        MainWindowTheme.StyleContextMenu(menu);
        this.resultsContextMenu = menu;
        menu.Show(Cursor.Position);
    }

    private void QueueResultsContextMenuDisposal(
        ContextMenuStrip menu)
    {
        if (menu.IsDisposed)
        {
            if (ReferenceEquals(this.resultsContextMenu, menu))
            {
                this.resultsContextMenu = null;
            }

            return;
        }

        if (!this.IsHandleCreated ||
            this.IsDisposed ||
            this.Disposing)
        {
            return;
        }

        try
        {
            this.BeginInvoke(new Action(() =>
            {
                if (!menu.IsDisposed)
                {
                    menu.Dispose();
                }

                if (ReferenceEquals(this.resultsContextMenu, menu))
                {
                    this.resultsContextMenu = null;
                }
            }));
        }
        catch (InvalidOperationException)
        {
            // Form disposal owns the menu once the window starts closing.
        }
    }

    private ToolStripMenuItem CreateShoppingListTargetMenuItem(
        string text,
        int itemTemplateId,
        string? listId,
        bool promptForQuantity)
    {
        var menuItem = new ToolStripMenuItem(text);
        menuItem.Click += (_, _) =>
        {
            long quantity = 1;
            if (promptForQuantity)
            {
                this.clientManager.GalaxyKnowledge.TryGetItem(
                    itemTemplateId,
                    out var item);
                using var dialog = new ShoppingListQuantityDialog(
                    "Add Multiple",
                    item?.Family == GalaxyItemFamily.Ammo
                        ? "How many stacks should be added?"
                        : "How many should be added to the shopping list?");
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                quantity = dialog.Quantity;
            }

            this.AddItemToShoppingList(
                itemTemplateId,
                quantity,
                listId);
        };
        return menuItem;
    }

    private void CreateListAndAddItem(int itemTemplateId)
    {
        using var dialog = new ShoppingListNameDialog(
            "New Shopping List",
            "Choose a name for the shopping list.",
            "Shopping List");
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var created = this.clientManager.CreateShoppingList(
            dialog.ListName);
        this.clientManager.SetActiveShoppingList(created.ListId);
        this.AddItemToShoppingList(
            itemTemplateId,
            quantity: 1,
            created.ListId);
    }

    private void AddItemToShoppingList(
        int itemTemplateId,
        long quantity,
        string? listId)
    {
        ShoppingListDocument saved;
        try
        {
            saved = this.clientManager.AddShoppingListRequestedOutput(
                itemTemplateId,
                quantity,
                listId);
        }
        catch (OverflowException)
        {
            ThemedMessageDialog.ShowWarning(
                this,
                "Shopping List",
                "That quantity is too large for one shopping-list entry.");
            return;
        }

        var itemName = this.clientManager.GalaxyKnowledge.TryGetItem(
                itemTemplateId,
                out var item)
            ? item.Name
            : "item";
        this.transientStatus = string.Format(
            CultureInfo.CurrentCulture,
            "Added {0:N0} × {1} to {2}.",
            quantity,
            itemName,
            saved.Name);
        this.statusLabel.Text = this.transientStatus;
        this.shoppingListView?.RefreshNow(force: true);
    }

    private void ResultsGrid_OnColumnHeaderMouseClick(
        object? sender,
        DataGridViewCellMouseEventArgs e)
    {
        if (e.ColumnIndex < 0 ||
            e.ColumnIndex >= this.resultsGrid.Columns.Count)
        {
            return;
        }

        var column = this.resultsGrid.Columns[e.ColumnIndex];

        if (column.SortMode == DataGridViewColumnSortMode.NotSortable)
        {
            return;
        }

        this.activeSortDirection =
            string.Equals(
                this.activeSortColumnName,
                column.Name,
                StringComparison.Ordinal) &&
            this.activeSortDirection == ListSortDirection.Ascending
                ? ListSortDirection.Descending
                : ListSortDirection.Ascending;
        this.activeSortColumnName = column.Name;
        this.RenderCurrentSearchRows(this.GetSelectedIdentity());
    }

    private void ResultsGrid_OnDataError(
        object? sender,
        DataGridViewDataErrorEventArgs e)
    {
        // A malformed result must never trap the application behind the
        // DataGridView default modal error dialog. Keep the affected cell
        // empty and leave the rest of Galaxy Finder usable.
        e.ThrowException = false;
        e.Cancel = true;
    }

    private void ResultsGrid_OnMouseMove(
        object? sender,
        MouseEventArgs e)
    {
        if (sender is not DataGridView grid)
        {
            return;
        }

        var hit = grid.HitTest(e.X, e.Y);
        if (hit.RowIndex < 0 ||
            hit.ColumnIndex < 0 ||
            hit.RowIndex >= grid.Rows.Count ||
            grid.Rows[hit.RowIndex].Tag is not FinderSearchRow row)
        {
            this.ClearResultsHoverStyle();
            this.HideResultToolTips();
            return;
        }

        if (this.hoveredResultRowIndex != hit.RowIndex ||
            this.hoveredResultColumnIndex != hit.ColumnIndex)
        {
            this.ClearResultsHoverStyle();
            this.HideResultToolTips();
            this.hoveredResultRowIndex = hit.RowIndex;
            this.hoveredResultColumnIndex = hit.ColumnIndex;
        }

        var columnName = grid.Columns[hit.ColumnIndex].Name;
        var cell = grid.Rows[hit.RowIndex].Cells[hit.ColumnIndex];

        if (string.Equals(
                columnName,
                "Destination",
                StringComparison.Ordinal))
        {
            var hasAction = row.CanSetDestination &&
                !string.IsNullOrWhiteSpace(row.DestinationText);
            if (hasAction)
            {
                cell.Style.BackColor = MainWindowTheme.ButtonHover;
                cell.Style.ForeColor = MainWindowTheme.Accent;
                cell.Style.SelectionBackColor =
                    MainWindowTheme.ButtonHover;
                cell.Style.SelectionForeColor = MainWindowTheme.Accent;
                grid.Cursor = Cursors.Hand;
            }

            if (!string.IsNullOrWhiteSpace(row.RouteDescription))
            {
                this.contextualToolTip.SetHoveredToolTip(
                    grid,
                    BuildContextualToolTipContent(
                        "Route",
                        row.RouteDescription),
                    this.GetItemToolTipDelayMilliseconds());
            }

            return;
        }

        grid.Cursor = Cursors.Default;

        if (row.ItemMatch is not { } itemMatch)
        {
            return;
        }

        var item = itemMatch.Item;
        if (!row.IsVendorRouteRow &&
            (columnName == "Icon" ||
             columnName == "Name" ||
             columnName == "Type" ||
             columnName == "Category" ||
             columnName == "Level"))
        {
            if (this.hoveredItemToolTipTemplateId ==
                item.ItemTemplateId)
            {
                return;
            }

            ClientRuntimeItemTemplateObservation? template = null;
            if (item.ItemTemplateId > 0 &&
                ClientItemTemplateCatalog.TryGetRuntimeObservation(
                    item.ItemTemplateId,
                    out var resolvedTemplate))
            {
                template = resolvedTemplate;
            }

            var content = PilotArchiveItemToolTipBuilder.BuildCatalogItem(
                item.ItemTemplateId,
                item.Name,
                template,
                this.ResolveItemDetailIcon(
                    item.ItemTemplateId,
                    new Size(36, 36)));

            this.hoveredItemToolTipTemplateId = item.ItemTemplateId;
            this.itemToolTip.SetHoveredToolTip(
                grid,
                content,
                this.GetItemToolTipDelayMilliseconds());
            return;
        }

        if (string.Equals(
                columnName,
                "Sources",
                StringComparison.Ordinal))
        {
            if (row.IsVendorRouteRow)
            {
                if (!string.IsNullOrWhiteSpace(row.RouteDescription))
                {
                    this.contextualToolTip.SetHoveredToolTip(
                        grid,
                        BuildContextualToolTipContent(
                            "Vendor destination",
                            row.RouteDescription),
                        this.GetItemToolTipDelayMilliseconds());
                }

                return;
            }

            if (this.GetAdditionalVendorRoutes(row).Count != 0)
            {
                grid.Cursor = Cursors.Hand;
            }

            var details = GalaxyFinderSourcePresentation.BuildDetails(
                item);
            if (details.Length != 0)
            {
                this.contextualToolTip.SetHoveredToolTip(
                    grid,
                    BuildContextualToolTipContent(
                        "Known sources",
                        details),
                    this.GetItemToolTipDelayMilliseconds());
            }

            return;
        }

        if (string.Equals(
                columnName,
                "Hops",
                StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(row.RouteDescription))
        {
            this.contextualToolTip.SetHoveredToolTip(
                grid,
                BuildContextualToolTipContent(
                    "Nearest source",
                    row.RouteDescription),
                this.GetItemToolTipDelayMilliseconds());
        }
    }

    private static ActionToolTipContent BuildContextualToolTipContent(
        string heading,
        string body)
    {
        return new ActionToolTipContent(
        [
            new ActionToolTipParagraph(
            [
                new ActionToolTipRun(
                    heading,
                    ActionToolTipTextRole.Accent,
                    Bold: true),
            ],
                ActionToolTipParagraphStyle.Header,
                SpaceAfter: 4),
            new ActionToolTipParagraph(
            [
                new ActionToolTipRun(body),
            ]),
        ],
            Icon: null);
    }

    private void ClearResultsHoverStyle()
    {
        if (this.hoveredResultRowIndex >= 0 &&
            this.hoveredResultColumnIndex >= 0 &&
            this.hoveredResultRowIndex < this.resultsGrid.Rows.Count &&
            this.hoveredResultColumnIndex <
                this.resultsGrid.Columns.Count)
        {
            var cell = this.resultsGrid
                .Rows[this.hoveredResultRowIndex]
                .Cells[this.hoveredResultColumnIndex];
            if (string.Equals(
                    this.resultsGrid.Columns[
                        this.hoveredResultColumnIndex].Name,
                    "Destination",
                    StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(
                    Convert.ToString(
                        cell.Value,
                        CultureInfo.CurrentCulture)))
            {
                cell.Style.BackColor = MainWindowTheme.Button;
                cell.Style.ForeColor = MainWindowTheme.Accent;
                cell.Style.SelectionBackColor = MainWindowTheme.Button;
                cell.Style.SelectionForeColor = MainWindowTheme.Accent;
            }
        }

        this.hoveredResultRowIndex = -1;
        this.hoveredResultColumnIndex = -1;
        this.resultsGrid.Cursor = Cursors.Default;
    }

    private int GetItemToolTipDelayMilliseconds()
    {
        if (this.SelectedProcessId is not { } processId)
        {
            return ClientTooltipDelayObservation
                .DefaultDelayMilliseconds;
        }

        var snapshot = this.clientManager
            .GetClientObservationSnapshots()
            .FirstOrDefault(candidate =>
                candidate.ProcessId == processId);

        if (snapshot == null ||
            !snapshot.TooltipDelay.IsAvailable)
        {
            return ClientTooltipDelayObservation
                .DefaultDelayMilliseconds;
        }

        return snapshot.TooltipDelay.DelayMilliseconds;
    }

    private void ResultsGrid_OnMouseLeave(
        object? sender,
        EventArgs e)
    {
        this.ClearResultsHoverStyle();
        this.HideResultToolTips();
    }

    private void ResultsGrid_OnScroll(
        object? sender,
        ScrollEventArgs e)
    {
        this.ClearResultsHoverStyle();
        this.HideResultToolTips();
    }

    private void HideItemToolTip()
    {
        this.HideResultToolTips();
    }

    private void HideResultToolTips()
    {
        this.hoveredItemToolTipTemplateId = null;
        this.itemToolTip.Remove(this.resultsGrid);
        this.contextualToolTip.Remove(this.resultsGrid);
    }

    private void ResultsGrid_OnKeyDown(
        object? sender,
        KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter ||
            this.resultsGrid.SelectedRows.Count == 0 ||
            this.resultsGrid.SelectedRows[0].Tag is not
                FinderSearchRow row ||
            row.ItemMatch == null)
        {
            return;
        }

        this.OpenItemDetail(row.ItemMatch.Item.ItemTemplateId);
        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    private void Tabs_OnPageCloseRequested(
        object? sender,
        ThemedTabPageEventArgs e)
    {
        this.RemoveItemDetailPage(e.Page);
    }

    private void ClientManager_OnNavigationDataChanged(
        object? sender,
        EventArgs e)
    {
        if (this.IsDisposed || this.Disposing)
        {
            return;
        }

        if (this.InvokeRequired)
        {
            this.BeginInvoke(() =>
                this.ClientManager_OnNavigationDataChanged(
                    sender,
                    e));
            return;
        }

        this.worldSearchIndex = new WorldSearchIndex(
            this.clientManager.NavigationData);
        this.routeContextFingerprint =
            this.BuildRouteContextFingerprint();
        this.MarkSearchResultsStale();
        this.RefreshOpenItemDetailPages();
    }

    private void ClientManager_OnGalaxyKnowledgeChanged(
        object? sender,
        GalaxyKnowledgeSnapshotChangedEventArgs e)
    {
        if (this.IsDisposed || this.Disposing)
        {
            return;
        }

        if (this.InvokeRequired)
        {
            this.BeginInvoke(() =>
                this.ClientManager_OnGalaxyKnowledgeChanged(
                    sender,
                    e));
            return;
        }

        this.itemSearchIndex =
            new GalaxyFinderItemSearchIndex(e.Snapshot);
        this.knowledgeFilterChoicesPending = true;
        this.routeContextFingerprint =
            this.BuildRouteContextFingerprint();
        this.MarkSearchResultsStale();
        this.RefreshOpenItemDetailPages();
    }

    private void MarkSearchResultsStale()
    {
        if (this.searchResultsStale)
        {
            return;
        }

        this.searchResultsStale = true;
        this.AppendStaleSearchNotice();
    }

    private void AppendStaleSearchNotice()
    {
        if (!this.searchResultsStale ||
            this.resultsGrid.Rows.Count == 0)
        {
            return;
        }

        const string notice =
            " New Galaxy data is available. Change the search or press Enter to refresh.";

        if (!this.statusLabel.Text.EndsWith(
                notice,
                StringComparison.Ordinal))
        {
            this.statusLabel.Text = string.Concat(
                this.statusLabel.Text.TrimEnd(),
                notice);
        }
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
                client.ProcessId,
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

    private void RefreshResults()
    {
        if (this.isRefreshingResults)
        {
            return;
        }

        this.isRefreshingResults = true;

        try
        {
            if (this.knowledgeFilterChoicesPending)
            {
                this.RefreshEffectChoices();
                this.RefreshGridMode();
                this.knowledgeFilterChoicesPending = false;
            }

            this.searchResultsStale = false;
            var query = this.searchTextBox.Text.Trim();
            var selectedIdentity = this.GetSelectedIdentity() ??
                                   this.clientManager
                                       .WorldFindSettings
                                       .SelectedIdentity;

            this.UpdateDataSetLabel();
            this.currentPilotReputation =
                this.SelectedProcessId is { } reputationProcessId
                    ? this.clientManager.GetNavigationRouteSnapshot(
                        reputationProcessId).CurrentPilotReputation
                    : ClientReputationObservation.Unavailable(
                        "Select a hosted pilot to determine mob disposition.");

            var scope = this.SelectedSearchScope;
            var category = this.SelectedItemCategory;
            var sourceFilters = this.SelectedSourceFilters;
            var effectIdentity = this.SelectedEffectIdentity;
            var hasItemFilters =
                category != GalaxyFinderItemCategory.All ||
                sourceFilters != GalaxyFinderSourceFilter.None ||
                effectIdentity.Length != 0;
            var searchItems = scope is
                GalaxyFinderSearchScope.All or
                GalaxyFinderSearchScope.Items;
            var searchWorld =
                (scope is
                    GalaxyFinderSearchScope.Places or
                    GalaxyFinderSearchScope.Npcs or
                    GalaxyFinderSearchScope.Mobs or
                    GalaxyFinderSearchScope.Harvestables) ||
                (scope == GalaxyFinderSearchScope.All &&
                 !hasItemFilters);
            List<FinderSearchRow> rows = [];
            var itemDistances =
                this.SelectedProcessId is { } itemProcessId
                    ? this.clientManager.GetNavigationRouteDistances(
                        itemProcessId)
                    : GalaxyRouteDistanceResult.Failure(
                        "Select a hosted pilot to calculate hops.");

            if (searchItems)
            {
                var itemMatches = this.itemSearchIndex.Search(
                    query,
                    category,
                    sourceFilters,
                    effectIdentity,
                    this.itemSearchIndex.Count);
                rows.AddRange(
                    this.gridMode switch
                    {
                        FinderGridMode.HarvestSources =>
                            itemMatches.SelectMany(match =>
                                this.CreateItemSourceRows(
                                    match,
                                    GalaxyItemSourceKind.Harvesting,
                                    itemDistances)),
                        FinderGridMode.MobLootSources =>
                            itemMatches.SelectMany(match =>
                                this.CreateItemSourceRows(
                                    match,
                                    GalaxyItemSourceKind.MobLoot,
                                    itemDistances)),
                        _ => itemMatches.Select(match =>
                            CreateItemRow(match)),
                    });
            }

            if (searchWorld)
            {
                var worldMatches = this.worldSearchIndex.Search(
                    query,
                    kind: null,
                    maximumResults: SearchCandidateLimit)
                    .Where(match =>
                        IsWorldKindVisible(match.Entry.Kind) &&
                        MatchesWorldScope(match.Entry.Kind, scope));
                var worldDistances =
                    this.SelectedProcessId is { } worldProcessId
                        ? this.clientManager.GetNavigationRouteDistances(
                            worldProcessId)
                        : GalaxyRouteDistanceResult.Failure(
                            "Select a hosted pilot to calculate hops.");
                var worldLocation = this.GetCurrentWorldLocation();

                rows.AddRange(worldMatches.Select(match =>
                    CreateWorldRow(
                        match,
                        worldDistances,
                        worldLocation)));
            }

            this.currentSearchRows = rows.ToArray();
            this.currentSearchItemDistances = itemDistances;
            this.currentSearchQuery = query;
            this.RenderCurrentSearchRows(selectedIdentity);
        }
        finally
        {
            this.isRefreshingResults = false;
        }
    }

    private void RenderCurrentSearchRows(string? selectedIdentity)
    {
        this.HideItemToolTip();
        this.resultsGrid.Rows.Clear();
        IReadOnlyList<FinderSearchRow> rows = this.currentSearchRows;

        if (string.Equals(
                this.activeSortColumnName,
                "Hops",
                StringComparison.Ordinal))
        {
            rows =
            [
                .. rows.Select(row =>
                    this.ResolveItemRoute(
                        row,
                        this.currentSearchItemDistances)),
            ];
        }

        var orderedRows = this.ApplyActiveSort(rows);
        var visibleParentRows = orderedRows
            .Take(MaximumResults)
            .Select(row => this.ResolveItemRoute(
                row,
                this.currentSearchItemDistances))
            .ToArray();

        var visibleRows = visibleParentRows
            .SelectMany(this.ExpandVendorRows)
            .ToArray();

        foreach (var rowModel in visibleRows)
        {
            this.AddGridRow(rowModel);
        }

        this.UpdateSortGlyph();
        this.RestoreSelectedIdentity(selectedIdentity);

        if (!string.IsNullOrWhiteSpace(this.transientStatus))
        {
            this.statusLabel.Text = this.transientStatus;
            this.AppendStaleSearchNotice();
            return;
        }

        var pilotText =
            this.resultsGrid.Columns.Contains("Hops") &&
            this.clientComboBox.SelectedItem is ClientChoice choice
                ? $" Hops are sector transitions from {choice.DisplayName}."
                : "";

        var sortText = this.BuildSortStatusText();

        this.statusLabel.Text = visibleParentRows.Length == 0
            ? BuildNoMatchesText(this.currentSearchQuery)
            : orderedRows.Length > visibleParentRows.Length
                ? string.IsNullOrWhiteSpace(sortText)
                    ? string.Create(
                        CultureInfo.InvariantCulture,
                        $"Showing the best {visibleParentRows.Length:N0} of {orderedRows.Length:N0} matches.{pilotText}")
                    : string.Create(
                        CultureInfo.InvariantCulture,
                        $"Showing {visibleParentRows.Length:N0} of {orderedRows.Length:N0} matches.{sortText}{pilotText}")
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"{visibleParentRows.Length:N0} match{(visibleParentRows.Length == 1 ? "" : "es")}.{sortText}{pilotText}");
        this.AppendStaleSearchNotice();
    }

    private string BuildSortStatusText()
    {
        if (string.IsNullOrWhiteSpace(this.activeSortColumnName) ||
            !this.resultsGrid.Columns.Contains(
                this.activeSortColumnName))
        {
            return "";
        }

        var heading = this.resultsGrid.Columns[
                this.activeSortColumnName]
            .HeaderText;

        return string.Concat(
            " Sorted by ",
            heading,
            ".");
    }

    private FinderSearchRow[] ApplyActiveSort(
        IReadOnlyList<FinderSearchRow> rows)
    {
        if (string.IsNullOrWhiteSpace(this.activeSortColumnName) ||
            !this.resultsGrid.Columns.Contains(
                this.activeSortColumnName))
        {
            return
            [
                .. rows
                    .OrderBy(row => row.Rank)
                    .ThenBy(row => row.HopSortValue)
                    .ThenBy(
                        row => row.Name,
                        StringComparer.OrdinalIgnoreCase)
                    .ThenBy(
                        row => row.Identity,
                        StringComparer.Ordinal),
            ];
        }

        var columnName = this.activeSortColumnName;
        var comparer = Comparer<FinderSearchRow>.Create(
            (left, right) => this.CompareRows(
                left,
                right,
                columnName,
                this.activeSortDirection));

        return
        [
            .. rows.OrderBy(row => row, comparer),
        ];
    }

    private int CompareRows(
        FinderSearchRow left,
        FinderSearchRow right,
        string columnName,
        ListSortDirection direction)
    {
        var comparison = CompareSortValues(
            this.GetSortValue(left, columnName),
            this.GetSortValue(right, columnName),
            direction);

        if (comparison != 0)
        {
            return comparison;
        }

        comparison = StringComparer.OrdinalIgnoreCase.Compare(
            left.Name,
            right.Name);

        return comparison != 0
            ? comparison
            : StringComparer.Ordinal.Compare(
                left.Identity,
                right.Identity);
    }

    private object? GetSortValue(
        FinderSearchRow row,
        string columnName)
    {
        if (columnName == "Hops")
        {
            return GetDestination(row) != null &&
                   row.HopSortValue < int.MaxValue - 1
                ? row.HopSortValue
                : null;
        }

        if (row.ItemMatch is { } itemMatch)
        {
            var item = itemMatch.Item;

            if (row.ItemSource is { } itemSource)
            {
                return columnName switch
                {
                    "Icon" => this.ResolveItemDetailIcon(
                        item.ItemTemplateId,
                        new Size(24, 24)),
                    "Name" => item.Name,
                    "Level" => item.TechLevel > 0
                        ? item.TechLevel
                        : null,
                    "Source" => itemSource.SourceName,
                    "SourceLevel" => itemSource.Kind ==
                        GalaxyItemSourceKind.MobLoot
                            ? itemSource.CombatLevel
                            : itemSource.TechLevel,
                    "System" => this.GetItemSourceSystem(row),
                    "Sector" => this.GetItemSourceSector(row),
                    "NearNav" => this.GetItemSourceNearNavigation(row),
                    _ => null,
                };
            }

            return columnName switch
            {
                "Icon" => this.ResolveItemDetailIcon(
                    item.ItemTemplateId,
                    new Size(24, 24)),
                "Name" => item.Name,
                "Type" => GalaxyFinderItemFacts.GetTypeText(item),
                "Category" =>
                    GalaxyFinderItemSearchIndex.GetFamilyDisplayName(
                        item.Family),
                "Level" => item.TechLevel > 0
                    ? item.TechLevel
                    : null,
                "Match" => itemMatch.MatchSummary,
                "Location" =>
                    GalaxyFinderItemFacts.GetManufacturerText(item),
                "Sources" =>
                    GalaxyFinderSourcePresentation.BuildCompactSummary(
                        item,
                        row.ResolvedRoute),
                "Manufacturer" =>
                    GalaxyFinderItemFacts.GetManufacturerText(item),
                "Effects" => itemMatch.EffectSummary,
                "Damage" =>
                    GalaxyFinderItemFacts.GetAttributeNumber(
                        item,
                        0x05),
                "Range" =>
                    GalaxyFinderItemFacts.GetAttributeNumber(
                        item,
                        0x07),
                "Energy" =>
                    GalaxyFinderItemFacts.GetAttributeNumber(
                        item,
                        0x09),
                "Reload" =>
                    GalaxyFinderItemFacts.GetAttributeNumber(
                        item,
                        0x15),
                "Rounds" =>
                    GalaxyFinderItemFacts.GetAttributeNumber(
                        item,
                        0x16),
                "Ammo" =>
                    GalaxyFinderItemFacts.GetAttributeText(
                        item,
                        0x01),
                "Signature" =>
                    GalaxyFinderItemFacts.GetAttributeNumber(
                        item,
                        0x1d),
                "Thrust" =>
                    GalaxyFinderItemFacts.GetAttributeNumber(
                        item,
                        0x1f),
                "Warp" =>
                    GalaxyFinderItemFacts.GetAttributeNumber(
                        item,
                        0x21),
                "WarpDrain" =>
                    GalaxyFinderItemFacts.GetAttributeNumber(
                        item,
                        0x22),
                "Capacity" =>
                    GalaxyFinderItemFacts.GetAttributeNumber(
                        item,
                        0x18),
                "Recharge" =>
                    item.Family == GalaxyItemFamily.Shield
                        ? GalaxyFinderItemFacts.GetAttributeNumber(
                            item,
                            0x1a)
                        : GalaxyFinderItemFacts.GetAttributeNumber(
                            item,
                            0x14),
                "MaxPower" =>
                    GalaxyFinderItemFacts.GetAttributeNumber(
                        item,
                        0x10),
                "Stack" => item.MaximumStack > 0
                    ? item.MaximumStack
                    : null,
                "UsedIn" => item.UsedByRecipes.Count,
                "RefinesTo" => item.RefinesTo.Count,
                _ => null,
            };
        }

        if (row.WorldMatch is not { } worldMatch)
        {
            return null;
        }

        var entry = worldMatch.Entry;
        return columnName switch
        {
            "Name" => entry.Name,
            "Type" => this.GetWorldTypeText(entry),
            "Level" => entry.Level,
            "System" => entry.SystemName,
            "Sector" => entry.SectorName,
            "NearNav" => entry.NearNavigationName,
            "Location" => entry.Location,
            _ => null,
        };
    }

    private static int CompareSortValues(
        object? left,
        object? right,
        ListSortDirection direction)
    {
        if (left == null && right == null)
        {
            return 0;
        }

        if (left == null)
        {
            return 1;
        }

        if (right == null)
        {
            return -1;
        }

        int comparison;

        if (TryGetNumericValue(left, out var leftNumber) &&
            TryGetNumericValue(right, out var rightNumber))
        {
            comparison = leftNumber.CompareTo(rightNumber);
        }
        else
        {
            comparison = StringComparer.CurrentCultureIgnoreCase.Compare(
                Convert.ToString(
                    left,
                    CultureInfo.CurrentCulture),
                Convert.ToString(
                    right,
                    CultureInfo.CurrentCulture));
        }

        return direction == ListSortDirection.Descending
            ? -comparison
            : comparison;
    }

    private static bool TryGetNumericValue(
        object value,
        out double number)
    {
        switch (value)
        {
            case byte byteValue:
                number = byteValue;
                return true;
            case sbyte signedByteValue:
                number = signedByteValue;
                return true;
            case short shortValue:
                number = shortValue;
                return true;
            case ushort unsignedShortValue:
                number = unsignedShortValue;
                return true;
            case int intValue:
                number = intValue;
                return true;
            case uint unsignedIntValue:
                number = unsignedIntValue;
                return true;
            case long longValue:
                number = longValue;
                return true;
            case ulong unsignedLongValue:
                number = unsignedLongValue;
                return true;
            case float floatValue when float.IsFinite(floatValue):
                number = floatValue;
                return true;
            case double doubleValue when double.IsFinite(doubleValue):
                number = doubleValue;
                return true;
            case decimal decimalValue:
                number = (double)decimalValue;
                return true;
            default:
                number = 0;
                return false;
        }
    }

    private FinderSearchRow ResolveItemRoute(
        FinderSearchRow row,
        GalaxyRouteDistanceResult distances)
    {
        if (row.ItemMatch == null ||
            row.ItemSource != null ||
            row.Destination != null)
        {
            return row;
        }

        var route = GalaxyFinderRouteResolver.FindBestRoute(
            this.clientManager.NavigationData,
            row.ItemMatch.Item,
            distances);

        if (route == null)
        {
            return row;
        }

        if (this.IsAlreadyAtDestination(route))
        {
            return row with
            {
                HopText = "Here",
                HopSortValue = 0,
                DestinationText = "",
                CanSetDestination = false,
                Destination = null,
                RouteDescription = route.Description,
                ResolvedRoute = route,
            };
        }

        return row with
        {
            HopText = FormatHopText(route.Hops),
            HopSortValue = route.HopSortValue,
            DestinationText = "Set destination",
            CanSetDestination = true,
            Destination = route.Destination,
            RouteDescription = route.Description,
            ResolvedRoute = route,
        };
    }

    private IEnumerable<FinderSearchRow> ExpandVendorRows(
        FinderSearchRow row)
    {
        yield return row;

        if (row.ItemMatch is not { } itemMatch ||
            row.IsVendorRouteRow ||
            !this.expandedVendorItemIds.Contains(
                itemMatch.Item.ItemTemplateId))
        {
            yield break;
        }

        var additionalRoutes = this.GetAdditionalVendorRoutes(row);
        for (var index = 0; index < additionalRoutes.Count; index++)
        {
            yield return this.CreateVendorRouteRow(
                row,
                additionalRoutes[index],
                index);
        }
    }

    private IReadOnlyList<GalaxyFinderResolvedRoute>
        GetAdditionalVendorRoutes(FinderSearchRow row)
    {
        if (row.ItemMatch is not { } itemMatch)
        {
            return [];
        }

        var routes = itemMatch.Item.Sources
            .Where(source =>
                source.Kind == GalaxyItemSourceKind.Vendor)
            .SelectMany(source =>
                GalaxyFinderRouteResolver.FindRoutes(
                    this.clientManager.NavigationData,
                    source,
                    this.currentSearchItemDistances))
            .OrderBy(route => route.HopSortValue)
            .ThenBy(
                route => GalaxyFinderSourcePresentation
                    .GetSourceDisplayName(route.Source),
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(
                route => route.Destination.SystemName,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(
                route => route.Destination.SectorName,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(
                route => route.Destination.TargetName,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (routes.Length <= 1)
        {
            return [];
        }

        return
        [
            .. routes.Where(route =>
                !VendorRoutesMatch(route, row.ResolvedRoute)),
        ];
    }

    private FinderSearchRow CreateVendorRouteRow(
        FinderSearchRow parent,
        GalaxyFinderResolvedRoute route,
        int index)
    {
        var isCurrentLocation = this.IsAlreadyAtDestination(route);
        var sourceName = GalaxyFinderSourcePresentation
            .GetSourceDisplayName(route.Source);
        var locationParts = new[]
            {
                route.Destination.SystemName,
                route.Destination.SectorName,
                route.Destination.TargetName,
            }
            .Where(value => !string.IsNullOrWhiteSpace(value));
        var sourceText = string.Concat(
            sourceName,
            Environment.NewLine,
            string.Join(" · ", locationParts));

        return new FinderSearchRow(
            Identity: string.Create(
                CultureInfo.InvariantCulture,
                $"item-vendor-route:{parent.ItemMatch!.Item.ItemTemplateId}:{route.Source.RelationshipId}:{route.Destination.SectorKey}:{route.Destination.TargetKey}:{index}"),
            Name: parent.Name,
            Rank: parent.Rank,
            HopText: isCurrentLocation
                ? "Here"
                : FormatHopText(route.Hops),
            HopSortValue: isCurrentLocation
                ? 0
                : route.HopSortValue,
            DestinationText: isCurrentLocation
                ? ""
                : "Set destination",
            CanSetDestination: !isCurrentLocation,
            IsCurrentLocation: isCurrentLocation,
            ItemMatch: parent.ItemMatch,
            WorldMatch: null,
            Destination: isCurrentLocation
                ? null
                : route.Destination,
            RouteDescription: route.Description,
            ResolvedRoute: route,
            IsVendorRouteRow: true,
            SourcesTextOverride: sourceText);
    }

    private static bool VendorRoutesMatch(
        GalaxyFinderResolvedRoute left,
        GalaxyFinderResolvedRoute? right)
    {
        if (right == null ||
            right.Source.Kind != GalaxyItemSourceKind.Vendor)
        {
            return false;
        }

        return string.Equals(
                   left.Source.RelationshipId,
                   right.Source.RelationshipId,
                   StringComparison.Ordinal) &&
               string.Equals(
                   left.Destination.SectorKey,
                   right.Destination.SectorKey,
                   StringComparison.Ordinal) &&
               string.Equals(
                   left.Destination.TargetKey,
                   right.Destination.TargetKey,
                   StringComparison.Ordinal);
    }

    private static string FormatHopText(int? hops)
    {
        return hops switch
        {
            null => "—",
            0 => "Local",
            { } value => value.ToString(
                CultureInfo.InvariantCulture),
        };
    }

    private void UpdateSortGlyph()
    {
        foreach (DataGridViewColumn column in
                 this.resultsGrid.Columns)
        {
            column.HeaderCell.SortGlyphDirection =
                SortOrder.None;
        }

        if (string.IsNullOrWhiteSpace(this.activeSortColumnName) ||
            !this.resultsGrid.Columns.Contains(
                this.activeSortColumnName))
        {
            return;
        }

        this.resultsGrid.Columns[this.activeSortColumnName]
            .HeaderCell.SortGlyphDirection =
                this.activeSortDirection ==
                ListSortDirection.Ascending
                    ? SortOrder.Ascending
                    : SortOrder.Descending;
    }

    private void AddGridRow(FinderSearchRow rowModel)
    {
        var values = this.resultsGrid.Columns
            .Cast<DataGridViewColumn>()
            .Select(column => this.GetCellValue(rowModel, column.Name))
            .ToArray();
        var rowIndex = this.resultsGrid.Rows.Add(values);
        var row = this.resultsGrid.Rows[rowIndex];
        row.Tag = rowModel;
        this.ApplyWorldScopeCellStyle(row, rowModel);

        if (this.resultsGrid.Columns.Contains("Sources"))
        {
            var sourcesCell = row.Cells["Sources"];
            if (rowModel.IsVendorRouteRow)
            {
                sourcesCell.Style.Padding = new Padding(
                    left: 12,
                    top: 0,
                    right: 0,
                    bottom: 0);
            }
            else if (this.GetAdditionalVendorRoutes(rowModel).Count != 0)
            {
                sourcesCell.Style.ForeColor = MainWindowTheme.Accent;
                sourcesCell.Style.SelectionForeColor =
                    MainWindowTheme.Accent;
            }
        }

        if (!this.resultsGrid.Columns.Contains("Destination"))
        {
            return;
        }

        var destinationColumn =
            this.resultsGrid.Columns["Destination"];
        var destinationCell = row.Cells[destinationColumn.Index];

        var destination = GetDestination(rowModel);

        if (destination == null)
        {
            row.Cells[destinationColumn.Index] =
                new DataGridViewTextBoxCell
                {
                    Value = "",
                    Style = new DataGridViewCellStyle
                    {
                        BackColor = row.DefaultCellStyle.BackColor,
                        SelectionBackColor =
                            this.resultsGrid.DefaultCellStyle.SelectionBackColor,
                    },
                };
            return;
        }

        if (!rowModel.CanSetDestination)
        {
            destinationCell.Style.ForeColor =
                MainWindowTheme.DisabledText;
        }
    }

    private object? GetCellValue(
        FinderSearchRow row,
        string columnName)
    {
        if (row.ItemMatch is { } itemMatch)
        {
            var item = itemMatch.Item;
            var snapshot = this.clientManager.GalaxyKnowledge;

            if (row.IsVendorRouteRow)
            {
                return columnName switch
                {
                    "Sources" => row.SourcesTextOverride,
                    "Hops" => row.HopText,
                    "Destination" => row.DestinationText,
                    _ => null,
                };
            }

            if (row.ItemSource is { } itemSource)
            {
                return columnName switch
                {
                    "Icon" => this.ResolveItemDetailIcon(
                        item.ItemTemplateId,
                        new Size(24, 24)),
                    "Name" => item.Name,
                    "Level" => GalaxyFinderItemFacts.GetLevelText(item),
                    "Source" => string.IsNullOrWhiteSpace(
                        itemSource.SourceName)
                            ? "Source"
                            : itemSource.SourceName.Trim(),
                    "SourceLevel" => itemSource.Kind ==
                        GalaxyItemSourceKind.MobLoot
                            ? itemSource.CombatLevel?.ToString(
                                CultureInfo.InvariantCulture) ?? ""
                            : itemSource.TechLevel?.ToString(
                                CultureInfo.InvariantCulture) ?? "",
                    "System" => this.GetItemSourceSystem(row),
                    "Sector" => this.GetItemSourceSector(row),
                    "NearNav" => this.GetItemSourceNearNavigation(row),
                    "Hops" => row.HopText,
                    "Destination" => row.DestinationText,
                    _ => "",
                };
            }

            return columnName switch
            {
                "Icon" => this.ResolveItemDetailIcon(
                    item.ItemTemplateId,
                    new Size(24, 24)),
                "Name" => item.Name,
                "Type" => GalaxyFinderItemFacts.GetTypeText(item),
                "Category" =>
                    GalaxyFinderItemSearchIndex.GetFamilyDisplayName(
                        item.Family),
                "Match" => itemMatch.MatchSummary,
                "Location" =>
                    GalaxyFinderItemFacts.GetManufacturerText(item),
                "Level" => GalaxyFinderItemFacts.GetLevelText(item),
                "Sources" => this.BuildItemSourcesCellText(row),
                "Manufacturer" =>
                    GalaxyFinderItemFacts.GetManufacturerText(item),
                "Effects" => itemMatch.EffectSummary,
                "Damage" =>
                    GalaxyFinderItemFacts.GetAttributeText(
                        item,
                        0x05,
                        " units"),
                "Range" =>
                    GalaxyFinderItemFacts.GetAttributeText(
                        item,
                        0x07,
                        " units"),
                "Energy" =>
                    GalaxyFinderItemFacts.GetAttributeText(
                        item,
                        0x09,
                        " units"),
                "Reload" =>
                    GalaxyFinderItemFacts.GetAttributeText(
                        item,
                        0x15,
                        " sec"),
                "Rounds" =>
                    GalaxyFinderItemFacts.GetAttributeText(
                        item,
                        0x16),
                "Ammo" =>
                    GalaxyFinderItemFacts.GetAttributeText(item, 0x01),
                "Signature" =>
                    GalaxyFinderItemFacts.GetAttributeText(
                        item,
                        0x1d,
                        " units"),
                "Thrust" =>
                    GalaxyFinderItemFacts.GetAttributeText(
                        item,
                        0x1f,
                        " units"),
                "Warp" =>
                    GalaxyFinderItemFacts.GetAttributeText(
                        item,
                        0x21,
                        " units"),
                "WarpDrain" =>
                    GalaxyFinderItemFacts.GetAttributeText(
                        item,
                        0x22,
                        " units/sec"),
                "Capacity" =>
                    GalaxyFinderItemFacts.GetAttributeText(
                        item,
                        0x18,
                        " units"),
                "Recharge" =>
                    item.Family == GalaxyItemFamily.Shield
                        ? GalaxyFinderItemFacts.GetAttributeText(
                            item,
                            0x1a,
                            " units/sec")
                        : GalaxyFinderItemFacts.GetAttributeText(
                            item,
                            0x14,
                            " units/sec"),
                "MaxPower" =>
                    GalaxyFinderItemFacts.GetAttributeText(
                        item,
                        0x10,
                        " units"),
                "Stack" =>
                    GalaxyFinderItemFacts.GetMaximumStackText(item),
                "UsedIn" =>
                    GalaxyFinderItemFacts.GetRecipeUseText(
                        snapshot,
                        item),
                "RefinesTo" =>
                    GalaxyFinderItemFacts.GetRefinesToText(
                        snapshot,
                        item),
                "Hops" => row.HopText,
                "Destination" => row.DestinationText,
                _ => "",
            };
        }

        if (row.WorldMatch is not { } worldMatch)
        {
            return "";
        }

        var entry = worldMatch.Entry;
        return columnName switch
        {
            "Icon" => null,
            "Name" => entry.Name,
            "Type" => this.GetWorldTypeText(entry),
            "Level" => entry.Level,
            "Match" => "",
            "System" => entry.SystemName,
            "Sector" => entry.SectorName,
            "NearNav" => entry.NearNavigationName,
            "Location" => entry.Location,
            "Hops" => row.HopText,
            "Destination" => row.DestinationText,
            _ => "",
        };
    }

    private string BuildItemSourcesCellText(FinderSearchRow row)
    {
        if (row.ItemMatch is not { } itemMatch)
        {
            return "";
        }

        var summary = GalaxyFinderSourcePresentation.BuildCompactSummary(
            itemMatch.Item,
            row.ResolvedRoute);
        var additionalRoutes = this.GetAdditionalVendorRoutes(row);

        if (additionalRoutes.Count == 0)
        {
            return summary;
        }

        var expanded = this.expandedVendorItemIds.Contains(
            itemMatch.Item.ItemTemplateId);
        var instruction = expanded
            ? string.Create(
                CultureInfo.CurrentCulture,
                $"{additionalRoutes.Count:N0} more vendor destination{(additionalRoutes.Count == 1 ? "" : "s")} shown below · click to hide")
            : string.Create(
                CultureInfo.CurrentCulture,
                $"Click to show {additionalRoutes.Count:N0} more vendor destination{(additionalRoutes.Count == 1 ? "" : "s")}");

        return string.Concat(
            summary,
            Environment.NewLine,
            instruction);
    }

    private string GetItemSourceSystem(FinderSearchRow row)
    {
        if (row.ResolvedRoute != null)
        {
            return row.ResolvedRoute.Destination.SystemName;
        }

        var sectorKey = FirstNonEmpty(
            row.ItemSourceLocation?.SectorKey,
            row.ItemSource?.SectorKey);
        return sectorKey.Length != 0 &&
               this.clientManager.NavigationData.Topology.TryGetByKey(
                   sectorKey,
                   out var sector)
            ? sector.SystemName
            : "";
    }

    private string GetItemSourceSector(FinderSearchRow row)
    {
        return row.ResolvedRoute?.Destination.SectorName ??
               FirstNonEmpty(
                   row.ItemSourceLocation?.SectorName,
                   row.ItemSource?.SectorName);
    }

    private string GetItemSourceNearNavigation(
        FinderSearchRow row)
    {
        var destination = row.ResolvedRoute?.Destination;
        return destination is
        {
            Kind: NavigationDestinationKind.Target,
            TargetName: not null,
        }
            ? destination.TargetName
            : "";
    }

    private string GetWorldTypeText(WorldSearchEntry entry)
    {
        if (this.gridMode == FinderGridMode.Mobs)
        {
            return GetMobDispositionText(
                this.ResolveMobDisposition(entry));
        }

        return this.gridMode == FinderGridMode.Mixed ||
               string.IsNullOrWhiteSpace(entry.ScopeType)
            ? entry.KindDisplayName
            : entry.ScopeType;
    }

    private GalaxyAtlasSafetyBand ResolveMobDisposition(
        WorldSearchEntry entry)
    {
        return EncounterDispositionResolver.Resolve(
            entry.MobFactionBindingKind,
            entry.MobFactionIdentifier,
            entry.MobIntrinsicRelationshipRaw,
            this.currentPilotReputation);
    }

    private static string GetMobDispositionText(
        GalaxyAtlasSafetyBand disposition)
    {
        return disposition switch
        {
            GalaxyAtlasSafetyBand.Danger => "Hostile",
            GalaxyAtlasSafetyBand.Neutral => "Neutral",
            GalaxyAtlasSafetyBand.Safe => "Friendly",
            _ => "",
        };
    }

    private void ApplyWorldScopeCellStyle(
        DataGridViewRow row,
        FinderSearchRow rowModel)
    {
        if (this.gridMode != FinderGridMode.Mobs ||
            rowModel.WorldMatch?.Entry is not
            {
                Kind: WorldSearchKind.Mob,
            } entry ||
            !this.resultsGrid.Columns.Contains("Type"))
        {
            return;
        }

        var disposition = this.ResolveMobDisposition(entry);
        var color = disposition switch
        {
            GalaxyAtlasSafetyBand.Danger => MainWindowTheme.Danger,
            GalaxyAtlasSafetyBand.Neutral => MainWindowTheme.Warning,
            GalaxyAtlasSafetyBand.Safe => MainWindowTheme.Success,
            _ => Color.Empty,
        };

        if (color.IsEmpty)
        {
            return;
        }

        var typeColumn = this.resultsGrid.Columns["Type"];
        var cell = row.Cells[typeColumn.Index];
        cell.Style.ForeColor = color;
        cell.Style.SelectionForeColor = color;
    }

    private void SetDestinationForRow(int rowIndex)
    {
        if (rowIndex < 0 ||
            rowIndex >= this.resultsGrid.Rows.Count ||
            this.resultsGrid.Rows[rowIndex].Tag is not
                FinderSearchRow row)
        {
            return;
        }

        var destination = GetDestination(row);

        if (!row.CanSetDestination)
        {
            this.transientStatus = row.IsCurrentLocation
                ? $"{row.Name} is already at your current location."
                : $"{row.Name} does not have a routable destination in the active dataset.";
            this.statusLabel.Text = this.transientStatus;
            return;
        }

        if (destination == null)
        {
            return;
        }

        if (this.SelectedProcessId is not { } processId)
        {
            this.transientStatus =
                "Select a hosted pilot before setting a destination.";
            this.statusLabel.Text = this.transientStatus;
            return;
        }

        var result = this.clientManager.SetNavigationDestination(
            processId,
            destination);

        if (!result.Succeeded)
        {
            this.transientStatus = result.Error;
            this.statusLabel.Text = this.transientStatus;
            return;
        }

        this.transientStatus = string.Concat(
            "Destination set: ",
            destination.DisplayName,
            ". Start Auto Pilot when ready.");
        this.statusLabel.Text = this.transientStatus;
    }

    private static NavigationDestination? GetDestination(
        FinderSearchRow row)
    {
        return row.Destination ??
               row.WorldMatch?.Entry.Destination;
    }

    private void RefreshFilterAvailability()
    {
        var itemFiltersAvailable = this.SelectedSearchScope is
            GalaxyFinderSearchScope.All or
            GalaxyFinderSearchScope.Items;

        this.categoryComboBox.Enabled = itemFiltersAvailable;
        this.effectComboBox.Enabled = itemFiltersAvailable;
        this.vendorSourceCheckBox.Enabled = itemFiltersAvailable;
        this.lootSourceCheckBox.Enabled = itemFiltersAvailable;
        this.craftedSourceCheckBox.Enabled = itemFiltersAvailable;
        this.refinedSourceCheckBox.Enabled = itemFiltersAvailable;
        this.harvestedSourceCheckBox.Enabled = itemFiltersAvailable;
        this.missionSourceCheckBox.Enabled = itemFiltersAvailable;
    }

    private void RefreshEffectChoices()
    {
        var selectedIdentity = this.SelectedEffectIdentity;
        var choices = GalaxyFinderItemSearchIndex.BuildEffectChoices(
            this.clientManager.GalaxyKnowledge,
            this.SelectedItemCategory);

        this.isRefreshingFilters = true;

        try
        {
            this.effectComboBox.BeginUpdate();
            this.effectComboBox.Items.Clear();
            this.effectComboBox.Items.Add(
                new GalaxyFinderEffectChoice("", "Any effect"));

            foreach (var choice in choices)
            {
                this.effectComboBox.Items.Add(choice);
            }

            var selected = this.effectComboBox.Items
                .Cast<GalaxyFinderEffectChoice>()
                .FirstOrDefault(choice =>
                    choice.MatchesIdentity(selectedIdentity));
            this.effectComboBox.SelectedItem = selected ??
                                               this.effectComboBox.Items[0];
        }
        finally
        {
            this.effectComboBox.EndUpdate();
            this.isRefreshingFilters = false;
        }
    }

    private void RefreshGridMode()
    {
        var next = ResolveGridMode(
            this.SelectedSearchScope,
            this.SelectedItemCategory,
            this.SelectedSourceFilters,
            this.SelectedEffectIdentity);
        this.ConfigureGrid(next, saveCurrent: true);
    }

    private void ConfigureGrid(
        FinderGridMode mode,
        bool saveCurrent)
    {
        if (mode == this.gridMode &&
            this.resultsGrid.Columns.Count != 0)
        {
            if (!saveCurrent)
            {
                this.ApplyColumnWidths();
            }

            return;
        }

        if (saveCurrent)
        {
            this.SaveCurrentColumnWidths();
        }

        this.gridMode = mode;
        this.activeSortColumnName = null;
        this.activeSortDirection =
            ListSortDirection.Ascending;
        this.resultsGrid.Rows.Clear();
        this.resultsGrid.Columns.Clear();

        if (!IsWorldScopeGridMode(mode))
        {
            this.AddIconColumn();
        }

        switch (mode)
        {
            case FinderGridMode.Places:
                this.AddTextColumn("Name", "Name", 280);
                this.AddTextColumn("Type", "Type", 150);
                this.AddTextColumn("System", "System", 220);
                this.AddFillTextColumn("Sector", "Sector", 240);
                this.AddItemRouteColumns();
                break;

            case FinderGridMode.Npcs:
                this.AddTextColumn("Name", "Name", 280);
                this.AddTextColumn("Type", "Type", 110);
                this.AddTextColumn("System", "System", 220);
                this.AddFillTextColumn("Sector", "Sector", 240);
                this.AddItemRouteColumns();
                break;

            case FinderGridMode.Mobs:
                this.AddTextColumn("Name", "Name", 260);
                this.AddTextColumn("Type", "Disposition", 120);
                this.AddCenteredTextColumn("Level", "CL", 66);
                this.AddTextColumn("System", "System", 190);
                this.AddTextColumn("Sector", "Sector", 220);
                this.AddFillTextColumn("NearNav", "Near nav", 240);
                this.AddItemRouteColumns();
                break;

            case FinderGridMode.Harvestables:
                this.AddTextColumn("Name", "Name", 260);
                this.AddTextColumn("Type", "Type", 130);
                this.AddCenteredTextColumn("Level", "Level", 70);
                this.AddTextColumn("System", "System", 190);
                this.AddTextColumn("Sector", "Sector", 220);
                this.AddFillTextColumn("NearNav", "Near nav", 240);
                this.AddItemRouteColumns();
                break;

            case FinderGridMode.Items:
                this.AddTextColumn("Name", "Name", 260);
                this.AddTextColumn("Category", "Category", 130);
                this.AddCenteredTextColumn("Level", "Level", 72);
                this.AddWrappedTextColumn("Match", "Match", 260);
                this.AddFillTextColumn("Sources", "Sources", 220);
                this.AddTextColumn("Manufacturer", "Manufacturer", 180);
                break;

            case FinderGridMode.Equipment:
                this.AddTextColumn("Name", "Name", 250);
                this.AddTextColumn("Category", "Category", 110);
                this.AddCenteredTextColumn("Level", "Level", 70);
                this.AddWrappedTextColumn("Match", "Match", 260);
                this.AddFillWrappedTextColumn("Effects", "Effects", 260);
                this.AddTextColumn("Sources", "Sources", 170);
                this.AddTextColumn("Manufacturer", "Manufacturer", 170);
                break;

            case FinderGridMode.Weapon:
                this.AddTextColumn("Name", "Name", 230);
                this.AddWrappedTextColumn("Match", "Match", 220);
                this.AddCenteredTextColumn("Level", "Level", 66);
                this.AddTextColumn("Type", "Weapon type", 150);
                this.AddTextColumn("Damage", "Damage", 92);
                this.AddTextColumn("Range", "Range", 100);
                this.AddTextColumn("Energy", "Energy use", 100);
                this.AddTextColumn("Reload", "Reload", 90);
                this.AddTextColumn("Rounds", "Rounds", 80);
                this.AddTextColumn("Ammo", "Ammo", 120);
                this.AddFillWrappedTextColumn("Effects", "Effects", 220);
                this.AddTextColumn("Sources", "Sources", 160);
                this.AddTextColumn(
                    "Manufacturer",
                    "Manufacturer",
                    170);
                break;

            case FinderGridMode.Engine:
                this.AddTextColumn("Name", "Name", 240);
                this.AddWrappedTextColumn("Match", "Match", 220);
                this.AddCenteredTextColumn("Level", "Level", 66);
                this.AddTextColumn("Signature", "Signature", 105);
                this.AddTextColumn("Thrust", "Thrust", 105);
                this.AddTextColumn("Warp", "Warp", 105);
                this.AddTextColumn("WarpDrain", "Warp drain", 115);
                this.AddFillWrappedTextColumn("Effects", "Effects", 250);
                this.AddTextColumn("Sources", "Sources", 170);
                this.AddTextColumn(
                    "Manufacturer",
                    "Manufacturer",
                    170);
                break;

            case FinderGridMode.Shield:
                this.AddTextColumn("Name", "Name", 250);
                this.AddWrappedTextColumn("Match", "Match", 220);
                this.AddCenteredTextColumn("Level", "Level", 66);
                this.AddTextColumn("Capacity", "Capacity", 120);
                this.AddTextColumn("Recharge", "Recharge", 120);
                this.AddFillWrappedTextColumn("Effects", "Effects", 280);
                this.AddTextColumn("Sources", "Sources", 180);
                this.AddTextColumn(
                    "Manufacturer",
                    "Manufacturer",
                    170);
                break;

            case FinderGridMode.Reactor:
                this.AddTextColumn("Name", "Name", 250);
                this.AddWrappedTextColumn("Match", "Match", 220);
                this.AddCenteredTextColumn("Level", "Level", 66);
                this.AddTextColumn("MaxPower", "Max power", 120);
                this.AddTextColumn("Recharge", "Recharge", 120);
                this.AddFillWrappedTextColumn("Effects", "Effects", 280);
                this.AddTextColumn("Sources", "Sources", 180);
                this.AddTextColumn(
                    "Manufacturer",
                    "Manufacturer",
                    170);
                break;

            case FinderGridMode.Device:
                this.AddTextColumn("Name", "Name", 260);
                this.AddWrappedTextColumn("Match", "Match", 220);
                this.AddCenteredTextColumn("Level", "Level", 66);
                this.AddFillWrappedTextColumn("Effects", "Effects", 340);
                this.AddTextColumn("Sources", "Sources", 180);
                this.AddTextColumn("Manufacturer", "Manufacturer", 180);
                break;

            case FinderGridMode.Ammo:
                this.AddTextColumn("Name", "Name", 250);
                this.AddWrappedTextColumn("Match", "Match", 220);
                this.AddCenteredTextColumn("Level", "Level", 66);
                this.AddTextColumn("Ammo", "Ammo type", 130);
                this.AddTextColumn("Damage", "Damage", 110);
                this.AddTextColumn("Stack", "Max stack", 100);
                this.AddFillTextColumn("Sources", "Sources", 240);
                this.AddTextColumn("Manufacturer", "Manufacturer", 170);
                break;

            case FinderGridMode.Component:
                this.AddTextColumn("Name", "Name", 250);
                this.AddWrappedTextColumn("Match", "Match", 220);
                this.AddCenteredTextColumn("Level", "Level", 66);
                this.AddFillTextColumn("UsedIn", "Used in", 320);
                this.AddTextColumn("Sources", "Sources", 180);
                this.AddTextColumn("Manufacturer", "Manufacturer", 180);
                break;

            case FinderGridMode.HarvestSources:
                this.AddTextColumn("Name", "Item", 230);
                this.AddCenteredTextColumn("Level", "Level", 66);
                this.AddTextColumn("Source", "Harvestable", 190);
                this.AddCenteredTextColumn(
                    "SourceLevel",
                    "Source level",
                    92);
                this.AddTextColumn("System", "System", 160);
                this.AddTextColumn("Sector", "Sector", 210);
                this.AddFillTextColumn("NearNav", "Near nav", 230);
                break;

            case FinderGridMode.MobLootSources:
                this.AddTextColumn("Name", "Item", 220);
                this.AddCenteredTextColumn("Level", "Level", 66);
                this.AddTextColumn("Source", "Mob", 210);
                this.AddCenteredTextColumn("SourceLevel", "CL", 60);
                this.AddTextColumn("System", "System", 155);
                this.AddTextColumn("Sector", "Sector", 205);
                this.AddFillTextColumn("NearNav", "Near nav", 220);
                break;

            case FinderGridMode.Resource:
                this.AddTextColumn("Name", "Name", 250);
                this.AddWrappedTextColumn("Match", "Match", 220);
                this.AddCenteredTextColumn("Level", "Level", 66);
                this.AddFillTextColumn("RefinesTo", "Refines to", 260);
                this.AddFillTextColumn("UsedIn", "Used in", 260);
                this.AddTextColumn("Sources", "Sources", 180);
                break;

            case FinderGridMode.TradeGood:
                this.AddTextColumn("Name", "Name", 280);
                this.AddWrappedTextColumn("Match", "Match", 220);
                this.AddCenteredTextColumn("Level", "Level", 66);
                this.AddFillTextColumn("Sources", "Known sources", 300);
                this.AddTextColumn("Manufacturer", "Manufacturer", 200);
                break;

            case FinderGridMode.Other:
                this.AddTextColumn("Name", "Name", 280);
                this.AddWrappedTextColumn("Match", "Match", 220);
                this.AddTextColumn("Type", "Catalog type", 200);
                this.AddCenteredTextColumn("Level", "Level", 66);
                this.AddFillTextColumn("Sources", "Known sources", 280);
                this.AddTextColumn("Manufacturer", "Manufacturer", 180);
                break;

            default:
                this.AddTextColumn("Name", "Name", 260);
                this.AddTextColumn("Type", "Type", 130);
                this.AddCenteredTextColumn("Level", "Level", 72);
                this.AddFillWrappedTextColumn("Match", "Match", 360);
                this.AddTextColumn(
                    "Location",
                    "Location / Manufacturer",
                    240);
                this.AddCenteredTextColumn("Hops", "Hops", 96);
                this.AddDestinationColumn();
                break;
        }

        if (this.resultsGrid.Columns.Contains("Sources") &&
            this.resultsGrid.Columns["Sources"] is
            DataGridViewTextBoxColumn sourcesColumn)
        {
            ConfigureWrappedColumn(sourcesColumn);
        }

        if (mode != FinderGridMode.Mixed &&
            !IsWorldScopeGridMode(mode))
        {
            this.AddItemRouteColumns();
        }

        this.ApplyColumnWidths();
        this.UpdateSortGlyph();
    }

    private static bool IsWorldScopeGridMode(FinderGridMode mode)
    {
        return mode is
            FinderGridMode.Places or
            FinderGridMode.Npcs or
            FinderGridMode.Mobs or
            FinderGridMode.Harvestables;
    }

    private void AddIconColumn()
    {
        this.resultsGrid.Columns.Add(
            new DataGridViewImageColumn
            {
                Name = "Icon",
                HeaderText = "",
                Width = 38,
                MinimumWidth = 38,
                Resizable = DataGridViewTriState.False,
                ImageLayout = DataGridViewImageCellLayout.Zoom,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    NullValue = null,
                    Alignment = DataGridViewContentAlignment.MiddleCenter,
                    Padding = new Padding(4),
                },
            });
    }

    private void AddItemRouteColumns()
    {
        this.AddCenteredTextColumn("Hops", "Hops", 76);
        this.AddDestinationColumn();
    }

    private void AddTextColumn(
        string name,
        string heading,
        int width)
    {
        this.resultsGrid.Columns.Add(
            CreateTextColumn(name, heading, width));
    }

    private void AddWrappedTextColumn(
        string name,
        string heading,
        int width)
    {
        var column = CreateTextColumn(name, heading, width);
        ConfigureWrappedColumn(column);
        this.resultsGrid.Columns.Add(column);
    }

    private void AddCenteredTextColumn(
        string name,
        string heading,
        int width)
    {
        var column = CreateTextColumn(name, heading, width);
        column.DefaultCellStyle.Alignment =
            DataGridViewContentAlignment.MiddleCenter;
        this.resultsGrid.Columns.Add(column);
    }

    private void AddFillTextColumn(
        string name,
        string heading,
        int minimumWidth)
    {
        var column = CreateTextColumn(name, heading, minimumWidth);
        column.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        column.MinimumWidth = minimumWidth;
        this.resultsGrid.Columns.Add(column);
    }

    private void AddFillWrappedTextColumn(
        string name,
        string heading,
        int minimumWidth)
    {
        var column = CreateTextColumn(name, heading, minimumWidth);
        ConfigureWrappedColumn(column);
        column.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        column.MinimumWidth = minimumWidth;
        this.resultsGrid.Columns.Add(column);
    }

    private static void ConfigureWrappedColumn(
        DataGridViewTextBoxColumn column)
    {
        column.DefaultCellStyle.WrapMode =
            DataGridViewTriState.True;
        column.DefaultCellStyle.Alignment =
            DataGridViewContentAlignment.TopLeft;
        column.DefaultCellStyle.Padding =
            new Padding(5, 5, 5, 5);
    }

    private void AddDestinationColumn()
    {
        this.resultsGrid.Columns.Add(
            new DataGridViewTextBoxColumn
            {
                Name = "Destination",
                HeaderText = "Route",
                Width = 132,
                MinimumWidth = 112,
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    BackColor = MainWindowTheme.Button,
                    ForeColor = MainWindowTheme.Accent,
                    SelectionBackColor = MainWindowTheme.Button,
                    SelectionForeColor = MainWindowTheme.Accent,
                    Alignment = DataGridViewContentAlignment.MiddleCenter,
                    Padding = new Padding(3),
                    NullValue = "",
                },
            });
    }

    private void OpenItemDetail(int itemTemplateId)
    {
        if (!this.clientManager.GalaxyKnowledge.TryGetItem(
                itemTemplateId,
                out var item))
        {
            this.transientStatus = "That item is no longer present in the current game catalog.";
            this.ShowSearchPage();
            this.RefreshResults();
            return;
        }

        var keepSearchOpen = this.clientManager
            .WorldFindSettings
            .KeepSearchOpenInTab;
        var returnPage = ReferenceEquals(
                this.tabs.SelectedPage,
                this.shoppingListPage)
            ? this.shoppingListPage
            : this.searchPage;

        if (!keepSearchOpen)
        {
            this.CloseAllItemDetailPages();
            this.tabs.SetPageVisible(this.searchPage, visible: false);
            this.tabs.SetPageVisible(this.shoppingListPage, visible: false);
        }
        else
        {
            this.tabs.SetPageVisible(this.searchPage, visible: true);

            if (this.itemDetailPages.TryGetValue(
                    itemTemplateId,
                    out var existingPage))
            {
                this.detailPageReturnPages[existingPage] = returnPage;
                this.tabs.SelectedPage = existingPage;
                return;
            }
        }

        var page = new ThemedTabPage(
            item.Name,
            canClose: keepSearchOpen);
        this.PopulateItemDetailPage(
            page,
            item,
            showBackButton: !keepSearchOpen,
            returnPage);
        this.itemDetailPages[itemTemplateId] = page;
        this.detailPageItemIds[page] = itemTemplateId;
        this.detailPageReturnPages[page] = returnPage;
        this.tabs.AddPage(page);
        this.tabs.SelectedPage = page;
    }

    private void PopulateItemDetailPage(
        ThemedTabPage page,
        GalaxyItemKnowledge item,
        bool showBackButton,
        ThemedTabPage returnPage)
    {
        page.SuspendLayout();

        try
        {
            while (page.Controls.Count != 0)
            {
                var control = page.Controls[0];
                page.Controls.RemoveAt(0);
                control.Dispose();
            }

            page.Controls.Add(
                new GalaxyFinderItemDetailView(
                    this.clientManager,
                    this.clientManager.GalaxyKnowledge,
                    item,
                    this.ResolveItemDetailIcon,
                    this.QueueOpenItemDetail,
                    this.ResolveItemSourceRoutes,
                    this.SetDestinationForItemSourceRoute,
                    this.QueueShowShoppingList,
                    showBackButton
                        ? () => this.QueueShowPrimaryPage(returnPage)
                        : null));
        }
        finally
        {
            page.ResumeLayout(performLayout: true);
        }
    }

    private void QueueOpenItemDetail(int itemTemplateId)
    {
        if (this.IsDisposed || this.Disposing)
        {
            return;
        }

        this.BeginInvoke(() => this.OpenItemDetail(itemTemplateId));
    }

    private void QueueShowShoppingList(string listId)
    {
        if (this.IsDisposed || this.Disposing)
        {
            return;
        }

        this.BeginInvoke(() => this.ShowShoppingListPage(listId));
    }

    private void QueueShowPrimaryPage(ThemedTabPage page)
    {
        if (this.IsDisposed || this.Disposing)
        {
            return;
        }

        this.BeginInvoke(() => this.ShowPrimaryPage(page));
    }

    private void QueueShowSearchPage()
    {
        if (this.IsDisposed || this.Disposing)
        {
            return;
        }

        this.BeginInvoke(() => this.ShowSearchPage());
    }

    private IReadOnlyList<GalaxyFinderResolvedRoute>
        ResolveItemSourceRoutes(
            GalaxyItemSourceKnowledge source)
    {
        var distances =
            this.SelectedProcessId is { } processId
                ? this.clientManager.GetNavigationRouteDistances(
                    processId)
                : GalaxyRouteDistanceResult.Failure(
                    "Select a hosted pilot to calculate hops.");

        return GalaxyFinderRouteResolver.FindRoutes(
            this.clientManager.NavigationData,
            source,
            distances);
    }

    private bool IsAlreadyAtDestination(
        GalaxyFinderResolvedRoute? route)
    {
        if (route == null ||
            this.SelectedProcessId is not { } processId)
        {
            return false;
        }

        var current = this.clientManager.GetNavigationCurrentLocation(
            processId);
        if (current == null)
        {
            return false;
        }

        if (!string.Equals(
                current.SectorKey,
                route.Destination.SectorKey,
                StringComparison.Ordinal))
        {
            return false;
        }

        if (route.Destination.Kind !=
                NavigationDestinationKind.Target)
        {
            return current.Kind != NavigationDestinationKind.Target;
        }

        if (current.Kind != NavigationDestinationKind.Target ||
            current.TargetKind != route.Destination.TargetKind)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(
                route.Destination.TargetKey) &&
            string.Equals(
                current.TargetKey,
                route.Destination.TargetKey,
                StringComparison.Ordinal))
        {
            return true;
        }

        return string.Equals(
            NormalizeDestinationName(current.TargetName),
            NormalizeDestinationName(route.Destination.TargetName),
            StringComparison.Ordinal);
    }

    private static string NormalizeDestinationName(string? value)
    {
        var normalized = GalaxyTopology.NormalizeName(value ?? "");
        const string suffix = " station";
        return normalized.EndsWith(
                suffix,
                StringComparison.Ordinal)
            ? normalized[..^suffix.Length]
            : normalized;
    }

    private void SetDestinationForItemSourceRoute(
        GalaxyFinderResolvedRoute route)
    {
        ArgumentNullException.ThrowIfNull(route);

        if (this.SelectedProcessId is not { } processId)
        {
            ThemedMessageDialog.ShowWarning(
                this,
                "Galaxy Finder",
                "Select a hosted pilot before setting a destination.");
            return;
        }

        var result = this.clientManager.SetNavigationDestination(
            processId,
            route.Destination);

        if (!result.Succeeded)
        {
            ThemedMessageDialog.ShowWarning(
                this,
                "Galaxy Finder",
                result.Error);
            return;
        }

        this.transientStatus = string.Concat(
            "Destination set: ",
            route.Destination.DisplayName,
            ". Start Auto Pilot when ready.");
        this.statusLabel.Text = this.transientStatus;
    }

    private Image? ResolveItemDetailIcon(
        int itemTemplateId,
        Size size)
    {
        return this.SelectedProcessId is { } processId
            ? this.clientManager.GetGameItemIcon(
                processId,
                itemTemplateId,
                size)
            : null;
    }

    private void ShowSearchPage()
    {
        if (!this.clientManager.WorldFindSettings.KeepSearchOpenInTab)
        {
            this.CloseAllItemDetailPages();
            this.tabs.SetPageVisible(
                this.shoppingListPage,
                visible: false);
        }

        this.tabs.SetPageVisible(this.searchPage, visible: true);
        this.tabs.SelectedPage = this.searchPage;
        this.searchTextBox.Focus();
    }

    internal void ShowShoppingListFromHelp()
    {
        this.ShowShoppingListPage();
    }

    private void ShowShoppingListPage(string? listId = null)
    {
        var keepSearchOpen = this.clientManager
            .WorldFindSettings
            .KeepSearchOpenInTab;
        if (!keepSearchOpen)
        {
            this.CloseAllItemDetailPages();
            this.tabs.SetPageVisible(this.searchPage, visible: false);
        }

        this.tabs.SetPageVisible(this.shoppingListPage, visible: true);
        if (!string.IsNullOrWhiteSpace(listId))
        {
            this.shoppingListView?.ShowList(listId);
        }
        else
        {
            this.shoppingListView?.RefreshNow(force: true);
        }

        this.tabs.SelectedPage = this.shoppingListPage;
    }

    private void ShowPrimaryPage(ThemedTabPage page)
    {
        if (ReferenceEquals(page, this.shoppingListPage))
        {
            this.ShowShoppingListPage();
        }
        else
        {
            this.ShowSearchPage();
        }
    }

    internal void ApplyCurrentSettings()
    {
        if (this.IsDisposed || this.Disposing)
        {
            return;
        }

        if (this.InvokeRequired)
        {
            try
            {
                this.BeginInvoke(this.ApplyCurrentSettings);
            }
            catch (InvalidOperationException)
            {
            }

            return;
        }

        this.ApplySearchBehaviorSetting();
        this.RefreshOpenItemDetailPages();
    }

    private void ApplySearchBehaviorSetting()
    {
        var keepSearchOpen = this.clientManager
            .WorldFindSettings
            .KeepSearchOpenInTab;
        this.tabs.ShowTabStrip = keepSearchOpen;
        if (this.shoppingListView != null)
        {
            this.shoppingListView.ShowBackButton = !keepSearchOpen;
        }

        if (keepSearchOpen)
        {
            this.tabs.SetPageVisible(this.searchPage, visible: true);
            this.tabs.SetPageVisible(this.shoppingListPage, visible: true);
        }
        else
        {
            this.CloseAllItemDetailPages();
            this.tabs.SetPageVisible(this.shoppingListPage, visible: false);
            this.tabs.SetPageVisible(this.searchPage, visible: true);
            this.tabs.SelectedPage = this.searchPage;
        }
    }

    private void RefreshOpenItemDetailPages()
    {
        var showBackButton = !this.clientManager
            .WorldFindSettings
            .KeepSearchOpenInTab;

        foreach (var pair in this.itemDetailPages.ToArray())
        {
            if (!this.clientManager.GalaxyKnowledge.TryGetItem(
                    pair.Key,
                    out var item))
            {
                this.RemoveItemDetailPage(pair.Value);
                continue;
            }

            var returnPage = this.detailPageReturnPages.GetValueOrDefault(
                pair.Value,
                this.searchPage);
            this.PopulateItemDetailPage(
                pair.Value,
                item,
                showBackButton,
                returnPage);
        }
    }

    private void RemoveItemDetailPage(ThemedTabPage page)
    {
        if (!this.detailPageItemIds.Remove(
                page,
                out var itemTemplateId))
        {
            return;
        }

        this.itemDetailPages.Remove(itemTemplateId);
        var returnPage = this.detailPageReturnPages.Remove(
                page,
                out var rememberedReturnPage)
            ? rememberedReturnPage
            : this.searchPage;
        this.tabs.RemovePage(page);

        if (this.itemDetailPages.Count == 0)
        {
            this.tabs.SetPageVisible(returnPage, visible: true);
            this.tabs.SelectedPage = returnPage;
        }
    }

    private void CloseAllItemDetailPages()
    {
        foreach (var page in this.detailPageItemIds.Keys.ToArray())
        {
            this.RemoveItemDetailPage(page);
        }
    }

    private void SaveCurrentColumnWidths()
    {
        var settings = this.clientManager.WorldFindSettings;

        foreach (DataGridViewColumn column in this.resultsGrid.Columns)
        {
            if (column.AutoSizeMode ==
                DataGridViewAutoSizeColumnMode.Fill)
            {
                continue;
            }

            settings.ColumnWidths[
                GetColumnSettingsKey(this.gridMode, column.Name)] =
                column.Width;
        }
    }

    private void ApplyColumnWidths()
    {
        var settings = this.clientManager.WorldFindSettings;

        foreach (DataGridViewColumn column in this.resultsGrid.Columns)
        {
            if (column.AutoSizeMode ==
                DataGridViewAutoSizeColumnMode.Fill)
            {
                continue;
            }

            var key = GetColumnSettingsKey(
                this.gridMode,
                column.Name);

            if (!settings.ColumnWidths.TryGetValue(key, out var width) &&
                this.gridMode == FinderGridMode.Mixed)
            {
                settings.ColumnWidths.TryGetValue(
                    column.Name,
                    out width);
            }

            if (width > 0)
            {
                column.Width = Math.Max(column.MinimumWidth, width);
            }
        }
    }

    private string BuildRouteContextFingerprint()
    {
        var knowledge = this.clientManager.GalaxyKnowledge;

        if (this.SelectedProcessId is not { } processId)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"none:{this.clientManager.NavigationData.Revision}:" +
                $"{knowledge.Provenance.CdataSha256}:" +
                $"{knowledge.Provenance.RecipeCatalogRevision}:" +
                $"{knowledge.Provenance.MissionCatalogRevision}");
        }

        var snapshot = this.clientManager.GetNavigationRouteSnapshot(
            processId);
        var observation = this.clientManager
            .GetClientObservationSnapshots()
            .FirstOrDefault(candidate =>
                candidate.ProcessId == processId);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{processId}:{snapshot.IsAvailable}:" +
            $"{snapshot.CurrentSector?.Key}:{snapshot.Profession}:" +
            $"{observation?.World.Environment}:" +
            $"{observation?.World.CurrentStarbaseName}:" +
            $"{this.clientManager.NavigationData.Revision}:" +
            $"{knowledge.Provenance.CdataSha256}:" +
            $"{knowledge.Provenance.RecipeCatalogRevision}:" +
            $"{knowledge.Provenance.MissionCatalogRevision}");
    }

    private void RestoreSettings()
    {
        var settings = this.clientManager.WorldFindSettings;
        this.isRefreshingFilters = true;

        try
        {
            var scope = ParseSearchScope(
                settings.SearchScope,
                GalaxyFinderSearchScope.All);
            var category = ParseEnum(
                settings.ItemCategory,
                GalaxyFinderItemCategory.All);
            var sources = (GalaxyFinderSourceFilter)
                settings.SourceFilters;

            ApplyLegacyKindFilter(
                settings.KindFilter,
                ref scope,
                ref category,
                ref sources);

            this.scopeComboBox.SelectedItem =
                this.scopeComboBox.Items
                    .Cast<SearchScopeChoice>()
                    .First(choice => choice.Scope == scope);
            this.categoryComboBox.SelectedItem =
                this.categoryComboBox.Items
                    .Cast<ItemCategoryChoice>()
                    .First(choice => choice.Category == category);
            this.vendorSourceCheckBox.Checked =
                (sources & GalaxyFinderSourceFilter.Vendor) != 0;
            this.lootSourceCheckBox.Checked =
                (sources & GalaxyFinderSourceFilter.Loot) != 0;
            this.craftedSourceCheckBox.Checked =
                (sources & GalaxyFinderSourceFilter.Crafted) != 0;
            this.refinedSourceCheckBox.Checked =
                (sources & GalaxyFinderSourceFilter.Refined) != 0;
            this.harvestedSourceCheckBox.Checked =
                (sources & GalaxyFinderSourceFilter.Harvested) != 0;
            this.missionSourceCheckBox.Checked =
                (sources & GalaxyFinderSourceFilter.Mission) != 0;
            this.searchTextBox.Text = settings.Query ?? "";
            this.tabs.ShowTabStrip = settings.KeepSearchOpenInTab;
        }
        finally
        {
            this.isRefreshingFilters = false;
        }

        this.RefreshFilterAvailability();
        this.RefreshEffectChoices();

        if (!string.IsNullOrWhiteSpace(settings.EffectIdentity))
        {
            var effect = this.effectComboBox.Items
                .Cast<GalaxyFinderEffectChoice>()
                .FirstOrDefault(choice =>
                    choice.MatchesIdentity(settings.EffectIdentity));

            if (effect != null)
            {
                this.effectComboBox.SelectedItem = effect;
            }
        }

        this.ConfigureGrid(
            ResolveGridMode(
                this.SelectedSearchScope,
                this.SelectedItemCategory,
                this.SelectedSourceFilters,
                this.SelectedEffectIdentity),
            saveCurrent: false);
        this.ApplySearchBehaviorSetting();
    }

    private void SaveSettings()
    {
        this.SaveCurrentColumnWidths();
        var settings = this.clientManager.WorldFindSettings;
        settings.Query = this.searchTextBox.Text;
        settings.SearchScope = this.SelectedSearchScope.ToString();
        settings.ItemCategory = this.SelectedItemCategory.ToString();
        settings.SourceFilters = (int)this.SelectedSourceFilters;
        settings.EffectIdentity = this.SelectedEffectIdentity;
        settings.KindFilter = "";
        settings.SelectedIdentity = this.GetSelectedIdentity();
        this.clientManager.SaveWorldFindSettings();
    }

    private string? GetSelectedIdentity()
    {
        return this.resultsGrid.SelectedRows.Count > 0 &&
               this.resultsGrid.SelectedRows[0].Tag is
                   FinderSearchRow row
            ? row.Identity
            : null;
    }

    private void RestoreSelectedIdentity(string? identity)
    {
        if (string.IsNullOrWhiteSpace(identity))
        {
            this.resultsGrid.ClearSelection();
            return;
        }

        foreach (DataGridViewRow gridRow in
                 this.resultsGrid.Rows)
        {
            if (gridRow.Tag is FinderSearchRow row &&
                string.Equals(
                    row.Identity,
                    identity,
                    StringComparison.Ordinal))
            {
                gridRow.Selected = true;
                this.resultsGrid.CurrentCell = gridRow.Cells[0];
                return;
            }
        }

        this.resultsGrid.ClearSelection();
    }

    private int? SelectedProcessId =>
        this.clientComboBox.SelectedItem is ClientChoice choice
            ? choice.ProcessId
            : null;

    private GalaxyFinderSearchScope SelectedSearchScope =>
        this.scopeComboBox.SelectedItem is SearchScopeChoice choice
            ? choice.Scope
            : GalaxyFinderSearchScope.All;

    private GalaxyFinderItemCategory SelectedItemCategory =>
        this.categoryComboBox.SelectedItem is ItemCategoryChoice choice
            ? choice.Category
            : GalaxyFinderItemCategory.All;

    private string SelectedEffectIdentity =>
        this.effectComboBox.SelectedItem is
            GalaxyFinderEffectChoice choice
            ? choice.Identity
            : "";

    private GalaxyFinderSourceFilter SelectedSourceFilters
    {
        get
        {
            var filters = GalaxyFinderSourceFilter.None;
            filters |= this.vendorSourceCheckBox.Checked
                ? GalaxyFinderSourceFilter.Vendor
                : GalaxyFinderSourceFilter.None;
            filters |= this.lootSourceCheckBox.Checked
                ? GalaxyFinderSourceFilter.Loot
                : GalaxyFinderSourceFilter.None;
            filters |= this.craftedSourceCheckBox.Checked
                ? GalaxyFinderSourceFilter.Crafted
                : GalaxyFinderSourceFilter.None;
            filters |= this.refinedSourceCheckBox.Checked
                ? GalaxyFinderSourceFilter.Refined
                : GalaxyFinderSourceFilter.None;
            filters |= this.harvestedSourceCheckBox.Checked
                ? GalaxyFinderSourceFilter.Harvested
                : GalaxyFinderSourceFilter.None;
            filters |= this.missionSourceCheckBox.Checked
                ? GalaxyFinderSourceFilter.Mission
                : GalaxyFinderSourceFilter.None;
            return filters;
        }
    }

    private CurrentWorldLocation GetCurrentWorldLocation()
    {
        if (this.SelectedProcessId is not { } processId)
        {
            return new CurrentWorldLocation(null, null);
        }

        var current = this.clientManager.GetNavigationCurrentLocation(
            processId);
        if (current != null)
        {
            return new CurrentWorldLocation(
                current.SectorKey,
                current.Kind == NavigationDestinationKind.Target &&
                current.TargetKind == GalaxyNavigationTargetKind.Station
                    ? current.TargetName
                    : null);
        }

        var route = this.clientManager.GetNavigationRouteSnapshot(
            processId);
        return new CurrentWorldLocation(
            route.CurrentSector?.Key,
            null);
    }

    private void UpdateDataSetLabel()
    {
        var knowledge = this.clientManager.GalaxyKnowledge;
        var cdataHash = knowledge.Provenance.CdataSha256;
        var shortHash = cdataHash.Length >= 8
            ? cdataHash[..8]
            : cdataHash;

        this.dataSetLabel.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"{knowledge.ItemsByTemplateId.Count:N0} items · " +
            $"{knowledge.EffectsByIdentity.Count:N0} effects · " +
            $"{knowledge.RefiningRelationships.Count:N0} refining links · " +
            $"{knowledge.Recipes.Count:N0} recipes · " +
            $"{knowledge.Missions.Count:N0} missions · " +
            $"Forge r{knowledge.Provenance.ForgeRevision} · " +
            $"CDATA {shortHash}");
    }

    private static FinderSearchRow CreateItemRow(
        GalaxyFinderItemMatch match)
    {
        return new FinderSearchRow(
            Identity: string.Create(
                CultureInfo.InvariantCulture,
                $"item:{match.Item.ItemTemplateId}"),
            Name: match.Item.Name,
            Rank: match.Rank,
            HopText: "—",
            HopSortValue: int.MaxValue,
            DestinationText: "",
            CanSetDestination: false,
            IsCurrentLocation: false,
            ItemMatch: match,
            WorldMatch: null);
    }

    private IEnumerable<FinderSearchRow> CreateItemSourceRows(
        GalaxyFinderItemMatch match,
        GalaxyItemSourceKind sourceKind,
        GalaxyRouteDistanceResult distances)
    {
        foreach (var source in match.Item.Sources.Where(candidate =>
                     candidate.Kind == sourceKind))
        {
            var routes = GalaxyFinderRouteResolver.FindRoutes(
                this.clientManager.NavigationData,
                source,
                distances);

            if (routes.Count != 0)
            {
                foreach (var route in routes)
                {
                    yield return this.CreateItemSourceRow(
                        match,
                        source,
                        route.Location,
                        route);
                }

                continue;
            }

            IReadOnlyList<GalaxyItemSourceLocationKnowledge> locations =
                source.Locations.Count == 0
                    ?
                    [
                        new GalaxyItemSourceLocationKnowledge
                        {
                            LocationId = source.SourceEntityId,
                            SectorId = source.SectorId,
                            SectorKey = source.SectorKey,
                            SectorName = source.SectorName,
                            LocationName = source.LocationName,
                        },
                    ]
                    : source.Locations;

            foreach (var location in locations)
            {
                yield return this.CreateItemSourceRow(
                    match,
                    source,
                    location,
                    route: null);
            }
        }
    }

    private FinderSearchRow CreateItemSourceRow(
        GalaxyFinderItemMatch match,
        GalaxyItemSourceKnowledge source,
        GalaxyItemSourceLocationKnowledge location,
        GalaxyFinderResolvedRoute? route)
    {
        var isCurrentLocation = this.IsAlreadyAtDestination(route);
        var locationIdentity = !string.IsNullOrWhiteSpace(
            location.LocationId)
            ? location.LocationId
            : route?.Destination.TargetKey ??
              route?.Destination.SectorKey ??
              "unlocated";

        return new FinderSearchRow(
            Identity: string.Create(
                CultureInfo.InvariantCulture,
                $"item-source:{match.Item.ItemTemplateId}:{source.RelationshipId}:{locationIdentity}"),
            Name: match.Item.Name,
            Rank: match.Rank,
            HopText: isCurrentLocation
                ? "Here"
                : route == null
                    ? "—"
                    : FormatHopText(route.Hops),
            HopSortValue: isCurrentLocation
                ? 0
                : route?.HopSortValue ?? int.MaxValue - 1,
            DestinationText: isCurrentLocation
                ? ""
                : route == null
                    ? ""
                    : "Set destination",
            CanSetDestination: route != null && !isCurrentLocation,
            IsCurrentLocation: isCurrentLocation,
            ItemMatch: match,
            WorldMatch: null,
            ItemSource: source,
            ItemSourceLocation: location,
            Destination: isCurrentLocation
                ? null
                : route?.Destination,
            RouteDescription: route?.Description ?? "",
            ResolvedRoute: route);
    }

    private static FinderSearchRow CreateWorldRow(
        WorldSearchMatch match,
        GalaxyRouteDistanceResult distances,
        CurrentWorldLocation currentLocation)
    {
        var isCurrentLocation = IsCurrentLocation(
            match.Entry,
            currentLocation);

        if (isCurrentLocation)
        {
            return new FinderSearchRow(
                match.Entry.Identity,
                match.Entry.Name,
                match.Rank,
                "Here",
                0,
                "Here",
                CanSetDestination: false,
                IsCurrentLocation: true,
                ItemMatch: null,
                WorldMatch: match);
        }

        var destinationText = match.Entry.Destination == null
            ? "Unavailable"
            : "Set destination";
        var canSetDestination = match.Entry.Destination != null;

        if (string.IsNullOrWhiteSpace(match.Entry.SectorKey))
        {
            return new FinderSearchRow(
                match.Entry.Identity,
                match.Entry.Name,
                match.Rank,
                "N/A",
                int.MaxValue - 2,
                destinationText,
                canSetDestination,
                IsCurrentLocation: false,
                ItemMatch: null,
                WorldMatch: match);
        }

        if (!distances.Succeeded)
        {
            return new FinderSearchRow(
                match.Entry.Identity,
                match.Entry.Name,
                match.Rank,
                "Unavailable",
                int.MaxValue - 1,
                destinationText,
                canSetDestination,
                IsCurrentLocation: false,
                ItemMatch: null,
                WorldMatch: match);
        }

        if (!distances.TryGetDistance(
                match.Entry.SectorKey,
                out var hops))
        {
            return new FinderSearchRow(
                match.Entry.Identity,
                match.Entry.Name,
                match.Rank,
                "Unreachable",
                int.MaxValue,
                destinationText,
                canSetDestination,
                IsCurrentLocation: false,
                ItemMatch: null,
                WorldMatch: match);
        }

        if (hops == 0)
        {
            return new FinderSearchRow(
                match.Entry.Identity,
                match.Entry.Name,
                match.Rank,
                match.Entry.Kind == WorldSearchKind.Sector
                    ? "Here"
                    : "Local",
                0,
                destinationText,
                canSetDestination,
                IsCurrentLocation: false,
                ItemMatch: null,
                WorldMatch: match);
        }

        return new FinderSearchRow(
            match.Entry.Identity,
            match.Entry.Name,
            match.Rank,
            hops.ToString(CultureInfo.InvariantCulture),
            hops,
            destinationText,
            canSetDestination,
            IsCurrentLocation: false,
            ItemMatch: null,
            WorldMatch: match);
    }

    private static bool IsCurrentLocation(
        WorldSearchEntry entry,
        CurrentWorldLocation currentLocation)
    {
        if (string.IsNullOrWhiteSpace(currentLocation.SectorKey) ||
            !string.Equals(
                entry.SectorKey,
                currentLocation.SectorKey,
                StringComparison.Ordinal))
        {
            return false;
        }

        if (entry.Kind == WorldSearchKind.Sector)
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(
                   currentLocation.StationName) &&
               entry.Destination is
               {
                   TargetKind: GalaxyNavigationTargetKind.Station,
                   TargetName: not null,
               } destination &&
               string.Equals(
                   GalaxyTopology.NormalizeName(
                       destination.TargetName),
                   GalaxyTopology.NormalizeName(
                       currentLocation.StationName),
                   StringComparison.Ordinal);
    }

    private static bool IsWorldKindVisible(WorldSearchKind kind)
    {
        return kind is not
            WorldSearchKind.VendorItem and not
            WorldSearchKind.MobLoot and not
            WorldSearchKind.HarvestableResource and not
            WorldSearchKind.Social and not
            WorldSearchKind.SocialPresence and not
            WorldSearchKind.LookingForGuild and not
            WorldSearchKind.GuildRecruitment;
    }

    private static bool MatchesWorldScope(
        WorldSearchKind kind,
        GalaxyFinderSearchScope scope)
    {
        return scope switch
        {
            GalaxyFinderSearchScope.All => true,
            GalaxyFinderSearchScope.Places => kind is
                WorldSearchKind.Sector or
                WorldSearchKind.NavigationPoint or
                WorldSearchKind.Station or
                WorldSearchKind.Gate or
                WorldSearchKind.Planet or
                WorldSearchKind.StationService,
            GalaxyFinderSearchScope.Npcs => kind is
                WorldSearchKind.Npc,
            GalaxyFinderSearchScope.Mobs => kind is
                WorldSearchKind.Mob,
            GalaxyFinderSearchScope.Harvestables => kind is
                WorldSearchKind.Harvestable,
            _ => false,
        };
    }

    private static FinderGridMode ResolveGridMode(
        GalaxyFinderSearchScope scope,
        GalaxyFinderItemCategory category,
        GalaxyFinderSourceFilter sources,
        string effectIdentity)
    {
        var worldMode = scope switch
        {
            GalaxyFinderSearchScope.Places =>
                FinderGridMode.Places,
            GalaxyFinderSearchScope.Npcs =>
                FinderGridMode.Npcs,
            GalaxyFinderSearchScope.Mobs =>
                FinderGridMode.Mobs,
            GalaxyFinderSearchScope.Harvestables =>
                FinderGridMode.Harvestables,
            _ => FinderGridMode.Mixed,
        };

        if (worldMode != FinderGridMode.Mixed)
        {
            return worldMode;
        }

        var itemOnly = scope == GalaxyFinderSearchScope.Items ||
                       category != GalaxyFinderItemCategory.All ||
                       sources != GalaxyFinderSourceFilter.None ||
                       effectIdentity.Length != 0;

        if (!itemOnly)
        {
            return FinderGridMode.Mixed;
        }

        if (category == GalaxyFinderItemCategory.MobLoot ||
            (sources == GalaxyFinderSourceFilter.Loot))
        {
            return FinderGridMode.MobLootSources;
        }

        if (category == GalaxyFinderItemCategory.RawResource ||
            sources == GalaxyFinderSourceFilter.Harvested)
        {
            return FinderGridMode.HarvestSources;
        }

        return category switch
        {
            GalaxyFinderItemCategory.Equipment =>
                FinderGridMode.Equipment,
            GalaxyFinderItemCategory.Weapon =>
                FinderGridMode.Weapon,
            GalaxyFinderItemCategory.Engine =>
                FinderGridMode.Engine,
            GalaxyFinderItemCategory.Shield =>
                FinderGridMode.Shield,
            GalaxyFinderItemCategory.Reactor =>
                FinderGridMode.Reactor,
            GalaxyFinderItemCategory.Device =>
                FinderGridMode.Device,
            GalaxyFinderItemCategory.Ammo =>
                FinderGridMode.Ammo,
            GalaxyFinderItemCategory.Component =>
                FinderGridMode.Component,
            GalaxyFinderItemCategory.RawResource or
            GalaxyFinderItemCategory.RefinedMaterial =>
                FinderGridMode.Resource,
            GalaxyFinderItemCategory.TradeGood =>
                FinderGridMode.TradeGood,
            GalaxyFinderItemCategory.Other =>
                FinderGridMode.Other,
            _ => FinderGridMode.Items,
        };
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return "";
    }

    private static string BuildNoMatchesText(string query)
    {
        return string.IsNullOrWhiteSpace(query)
            ? "No matches for the selected scope and filters."
            : $"No matches for “{query}”.";
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

    private static Label CreateControlLabel(string text)
    {
        return new Label
        {
            Dock = DockStyle.Fill,
            Text = text,
            ForeColor = MainWindowTheme.MutedText,
            Font = MainWindowTheme.CreateHeadingFont(8.5f),
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 4, 8, 4),
        };
    }

    private void ConfigureComboBox(ComboBox comboBox)
    {
        comboBox.Dock = DockStyle.Fill;
        comboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        comboBox.Margin = new Padding(0, 4, 12, 4);
        MainWindowTheme.StyleComboBox(comboBox);
    }

    private void ConfigureSourceCheckBox(
        ThemedCheckBox checkBox,
        string text)
    {
        checkBox.Text = text;
        checkBox.Font = MainWindowTheme.CreateBodyFont(8.5f);
        checkBox.Margin = new Padding(0, 0, 20, 0);
    }

    private static DataGridViewTextBoxColumn CreateTextColumn(
        string name,
        string heading,
        int width)
    {
        return new DataGridViewTextBoxColumn
        {
            Name = name,
            HeaderText = heading,
            Width = width,
            MinimumWidth = 64,
            SortMode = DataGridViewColumnSortMode.Programmatic,
        };
    }

    private static string GetColumnSettingsKey(
        FinderGridMode mode,
        string columnName)
    {
        return $"{mode}:{columnName}";
    }

    private static T ParseEnum<T>(string? value, T fallback)
        where T : struct, Enum
    {
        return Enum.TryParse<T>(
            value,
            ignoreCase: true,
            out var parsed)
            ? parsed
            : fallback;
    }

    private static GalaxyFinderSearchScope ParseSearchScope(
        string? value,
        GalaxyFinderSearchScope fallback)
    {
        if (string.Equals(
                value,
                "NpcsAndMobs",
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                value,
                "NPCsAndMobs",
                StringComparison.OrdinalIgnoreCase))
        {
            return GalaxyFinderSearchScope.Npcs;
        }

        if (string.Equals(
                value,
                "Social",
                StringComparison.OrdinalIgnoreCase))
        {
            return GalaxyFinderSearchScope.All;
        }

        return ParseEnum(value, fallback);
    }

    private static void ApplyLegacyKindFilter(
        string? kindFilter,
        ref GalaxyFinderSearchScope scope,
        ref GalaxyFinderItemCategory category,
        ref GalaxyFinderSourceFilter sources)
    {
        if (string.IsNullOrWhiteSpace(kindFilter) ||
            !Enum.TryParse<WorldSearchKind>(
                kindFilter,
                ignoreCase: true,
                out var kind))
        {
            return;
        }

        switch (kind)
        {
            case WorldSearchKind.Sector:
            case WorldSearchKind.NavigationPoint:
            case WorldSearchKind.Station:
            case WorldSearchKind.Gate:
            case WorldSearchKind.Planet:
            case WorldSearchKind.StationService:
                scope = GalaxyFinderSearchScope.Places;
                break;

            case WorldSearchKind.Npc:
                scope = GalaxyFinderSearchScope.Npcs;
                break;

            case WorldSearchKind.Mob:
                scope = GalaxyFinderSearchScope.Mobs;
                break;

            case WorldSearchKind.Harvestable:
                scope = GalaxyFinderSearchScope.Harvestables;
                break;

            case WorldSearchKind.VendorItem:
                scope = GalaxyFinderSearchScope.Items;
                sources |= GalaxyFinderSourceFilter.Vendor;
                break;

            case WorldSearchKind.MobLoot:
                scope = GalaxyFinderSearchScope.Items;
                sources |= GalaxyFinderSourceFilter.Loot;
                break;

            case WorldSearchKind.HarvestableResource:
                scope = GalaxyFinderSearchScope.Items;
                sources |= GalaxyFinderSourceFilter.Harvested;
                break;

            case WorldSearchKind.Social:
            case WorldSearchKind.SocialPresence:
            case WorldSearchKind.LookingForGuild:
            case WorldSearchKind.GuildRecruitment:
                scope = GalaxyFinderSearchScope.All;
                break;
        }

        category = GalaxyFinderItemCategory.All;
    }

    private enum GalaxyFinderSearchScope
    {
        All = 0,
        Places = 1,
        Npcs = 2,
        Mobs = 3,
        Harvestables = 4,
        Items = 5,
    }

    private enum FinderGridMode
    {
        Mixed = 0,
        Places = 1,
        Npcs = 2,
        Mobs = 3,
        Harvestables = 4,
        Items = 5,
        Equipment = 6,
        Weapon = 7,
        Engine = 8,
        Shield = 9,
        Reactor = 10,
        Device = 11,
        Ammo = 12,
        Component = 13,
        Resource = 14,
        TradeGood = 15,
        Other = 16,
        HarvestSources = 17,
        MobLootSources = 18,
    }

    private sealed record FinderSearchRow(
        string Identity,
        string Name,
        int Rank,
        string HopText,
        int HopSortValue,
        string DestinationText,
        bool CanSetDestination,
        bool IsCurrentLocation,
        GalaxyFinderItemMatch? ItemMatch,
        WorldSearchMatch? WorldMatch,
        GalaxyItemSourceKnowledge? ItemSource = null,
        GalaxyItemSourceLocationKnowledge? ItemSourceLocation = null,
        NavigationDestination? Destination = null,
        string RouteDescription = "",
        GalaxyFinderResolvedRoute? ResolvedRoute = null,
        bool IsVendorRouteRow = false,
        string SourcesTextOverride = "");

    private sealed record CurrentWorldLocation(
        string? SectorKey,
        string? StationName);

    private sealed record SearchScopeChoice(
        GalaxyFinderSearchScope Scope,
        string DisplayName)
    {
        public override string ToString()
        {
            return this.DisplayName;
        }
    }

    private sealed record ItemCategoryChoice(
        GalaxyFinderItemCategory Category,
        string DisplayName)
    {
        public override string ToString()
        {
            return this.DisplayName;
        }
    }

    private sealed record ClientChoice(
        int ProcessId,
        string DisplayName)
    {
        public override string ToString()
        {
            return this.DisplayName;
        }
    }
}
