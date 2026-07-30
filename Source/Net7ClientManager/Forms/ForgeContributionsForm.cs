// ReSharper disable LocalizableElement
// ReSharper disable AsyncVoidEventHandlerMethod
namespace Net7ClientManager.Forms;

using System.Globalization;
using Net7ClientManager.Contributions;
using Net7ClientManager.Core;
using Net7ClientManager.Models;
using Net7ClientManager.Navigation;
using Net7ClientManager.Services;
using Net7ClientManager.Win32;

public sealed class ForgeContributionsForm : Form
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

    private readonly ClientManager clientManager;
    private readonly WindowPlacementBinding windowPlacement;
    private readonly HostedClientTitleBar titleBar = new();
    private readonly Panel contentPanel = new();
    private readonly ThemedCheckBox enabledCheckBox = new();
    private readonly RadioButton anonymousRadioButton = new();
    private readonly RadioButton pilotNameRadioButton = new();
    private readonly ThemedCheckBox npcPresenceCheckBox = new();
    private readonly ThemedCheckBox navigationObjectsCheckBox = new();
    private readonly ThemedCheckBox stationServicesCheckBox = new();
    private readonly ThemedCheckBox vendorInventoriesCheckBox = new();
    private readonly ThemedCheckBox mobObservationsCheckBox = new();
    private readonly ThemedCheckBox mobLootCheckBox = new();
    private readonly ThemedCheckBox harvestableResourcesCheckBox = new();
    private readonly ThemedCheckBox productionRecipesCheckBox = new();
    private readonly ThemedCheckBox missionsCheckBox = new();
    private readonly ThemedCheckBox jobOffersCheckBox = new();
    private readonly Label identityValueLabel = CreateValueLabel();
    private readonly Label forgeIdentityStatusValueLabel = CreateValueLabel();
    private readonly Label forgeIdentityDetailLabel = CreateParagraph("");
    private Panel forgeIdentityPanel = null!;
    private TableLayoutPanel recoveryRow = null!;
    private readonly ComboBox recoveryPilotComboBox = new();
    private readonly Button beginRecoveryButton = new();
    private readonly Button checkRecoveryButton = new();
    private readonly Button copyRecoveryButton = new();
    private readonly Label revisionValueLabel = CreateValueLabel();
    private readonly Label statusValueLabel = CreateValueLabel();
    private readonly Label sessionValueLabel = CreateStatisticsLabel();
    private readonly Label lifetimeValueLabel = CreateStatisticsLabel();
    private readonly Label dataUpdateValueLabel = CreateValueLabel();
    private readonly Label dataUpdateDetailLabel = CreateParagraph("");
    private readonly Button dataUpdateButton = new();
    private readonly Button resetSessionButton = new();
    private readonly System.Windows.Forms.Timer refreshTimer = new();
    private bool updatingUi;
    private bool dataUpdateOperationBusy;
    private bool identityOperationBusy;
    private string identityOperationMessage = "";
    private string dataUpdateOperationMessage = "";
    private string dataUpdateStateKey = "";

    public ForgeContributionsForm(
        ClientManager clientManager,
        IWin32Window? preferredOwner = null)
    {
        ArgumentNullException.ThrowIfNull(clientManager);
        this.clientManager = clientManager;

        this.Text = "Net7 Forge Contributions";
        this.Icon = ResourceLoader.Net7ClientManagerIcon;
        this.StartPosition = FormStartPosition.CenterScreen;
        this.FormBorderStyle = FormBorderStyle.None;
        this.Size = new Size(980, 760);
        this.MinimumSize = new Size(900, 680);
        this.Padding = new Padding(1);
        this.BackColor = AddonCenterTheme.Border;
        this.ForeColor = AddonCenterTheme.Text;
        this.Font = new Font("Segoe UI", 9.0f);

        this.titleBar.Dock = DockStyle.Top;
        this.titleBar.Height = TitleBarHeight;
        this.titleBar.TitleText = "NET7 FORGE CONTRIBUTIONS";
        this.titleBar.ShowMaximizeButton = true;
        this.titleBar.ShowHelpButton = true;
        this.titleBar.HelpTopicId = HelpTopicIds.Addons;
        this.titleBar.HelpOverride = () =>
        {
            this.ShowHelpTour();
            return true;
        };
        this.titleBar.AccessibleName = "Net7 Forge Contributions title bar";
        this.windowPlacement =
            clientManager.BindGlobalWindowPlacement(
                this,
                WindowPlacementIds.ForgeContributions,
                preferredOwner);

        this.BuildUi();
        this.WireEvents();
        this.LoadSettingsIntoUi();
        this.RefreshStatistics();

        this.refreshTimer.Interval = 750;
        this.refreshTimer.Tick += this.RefreshTimer_OnTick;
        this.refreshTimer.Start();
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.refreshTimer.Stop();
            this.refreshTimer.Tick -= this.RefreshTimer_OnTick;
            this.refreshTimer.Dispose();

            this.dataUpdateButton.Click -= this.DataUpdateButton_OnClick;
            this.beginRecoveryButton.Click -= this.BeginRecoveryButton_OnClick;
            this.checkRecoveryButton.Click -= this.CheckRecoveryButton_OnClick;
            this.copyRecoveryButton.Click -= this.CopyRecoveryButton_OnClick;
            this.titleBar.DragRequested -= this.TitleBar_OnDragRequested;
            this.titleBar.MinimizeRequested -=
                this.TitleBar_OnMinimizeRequested;
            this.titleBar.MaximizeRequested -=
                this.TitleBar_OnMaximizeRequested;
            this.titleBar.CloseRequested -= this.TitleBar_OnCloseRequested;
            this.Resize -= this.ForgeContributionsForm_OnResize;
            this.windowPlacement.Dispose();
        }

        base.Dispose(disposing);
    }

    private void BuildUi()
    {
        this.contentPanel.Dock = DockStyle.Fill;
        this.contentPanel.BackColor = AddonCenterTheme.Background;
        this.contentPanel.Padding = new Padding(18);
        this.contentPanel.AutoScroll = true;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 7,
            BackColor = AddonCenterTheme.Background,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        root.Controls.Add(this.CreateIntroductionPanel(), 0, 0);
        root.Controls.Add(this.CreateForgeIdentityPanel(), 0, 1);
        root.Controls.Add(this.CreateAttributionPanel(), 0, 2);
        root.Controls.Add(this.CreateCategoriesPanel(), 0, 3);
        root.Controls.Add(this.CreateDataUpdatePanel(), 0, 4);
        root.Controls.Add(this.CreateOverviewPanel(), 0, 5);
        root.Controls.Add(this.CreateFooterPanel(), 0, 6);

        this.contentPanel.Controls.Add(root);
        this.Controls.Add(this.contentPanel);
        this.Controls.Add(this.titleBar);
    }

    internal void ShowHelpTour(Action? tourClosed = null)
    {
        GuidedTourOverlay.Show(
            this,
            [
                new GuidedTourStep(
                    () => this.enabledCheckBox,
                    "Choose whether to contribute",
                    "Contribution is optional and off by default. Enable it when you want supported world discoveries from your clients to improve the shared Forge dataset.",
                    () => this.ScrollTourTargetIntoView(this.enabledCheckBox)),
                new GuidedTourStep(
                    () => this.identityValueLabel,
                    "See whether Forge is connected",
                    "Forge access shows whether this installation is ready to contribute. If access is ever lost, recovery options appear in the Forge connection section above.",
                    () => this.ScrollTourTargetIntoView(this.identityValueLabel)),
                new GuidedTourStep(
                    () => this.anonymousRadioButton,
                    "Control public attribution",
                    "Choose whether contributions appear anonymously or may show the contributing pilot name publicly. Private abuse protection still remains in place.",
                    () => this.ScrollTourTargetIntoView(this.anonymousRadioButton)),
                new GuidedTourStep(
                    () => this.npcPresenceCheckBox,
                    "Choose the discoveries you share",
                    "Enable only the categories you are comfortable contributing, such as NPCs, navigation objects, vendors, mobs, resources, recipes, missions, and jobs.",
                    () => this.ScrollTourTargetIntoView(this.npcPresenceCheckBox)),
                new GuidedTourStep(
                    () => this.dataUpdateButton,
                    "Refresh shared world data",
                    "Use the data update action when a newer Forge world dataset is available for Client Manager.",
                    () => this.ScrollTourTargetIntoView(this.dataUpdateButton)),
                new GuidedTourStep(
                    () => this.statusValueLabel,
                    "Review contribution activity",
                    "The overview shows whether contribution is active and summarises what this session and installation have shared.",
                    () => this.ScrollTourTargetIntoView(this.statusValueLabel)),
            ],
            tourClosed: tourClosed);
    }

    private void ScrollTourTargetIntoView(Control control)
    {
        this.contentPanel.ScrollControlIntoView(control);
        control.Focus();
    }

    private Control CreateIntroductionPanel()
    {
        var panel = CreateSectionPanel();
        var layout = CreateSectionLayout(rowCount: 4);

        var heading = CreateHeading("Help build the shared Net7 Forge world dataset");
        var explanation = CreateParagraph(
            "Contribution is off by default. When enabled, Client Manager quietly shares supported world discoveries with Forge. Account names, login details, saved accounts, and machine information are never shared.");

        this.enabledCheckBox.Text = "Contribute observations to Net7 Forge";
        this.enabledCheckBox.Font = new Font("Segoe UI", 10.0f, FontStyle.Bold);
        this.enabledCheckBox.ForeColor = AddonCenterTheme.Accent;
        this.enabledCheckBox.Margin = new Padding(0, 8, 0, 3);

        var privacy = CreateParagraph(
            "Forge privately records which character shared each contribution so abuse can be investigated. Choose below whether that character name may also appear publicly.");
        privacy.ForeColor = AddonCenterTheme.Warning;

        layout.Controls.Add(heading, 0, 0);
        layout.Controls.Add(explanation, 0, 1);
        layout.Controls.Add(this.enabledCheckBox, 0, 2);
        layout.Controls.Add(privacy, 0, 3);
        panel.Controls.Add(layout);
        return panel;
    }

    private Control CreateForgeIdentityPanel()
    {
        this.forgeIdentityPanel = CreateSectionPanel();
        var layout = CreateSectionLayout(rowCount: 4);

        layout.Controls.Add(CreateHeading("Forge connection"), 0, 0);
        layout.Controls.Add(
            CreateNameValueRow(
                "Status",
                this.forgeIdentityStatusValueLabel),
            0,
            1);

        this.recoveryRow = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 4,
            RowCount = 1,
            Margin = new Padding(0, 8, 0, 4),
            BackColor = AddonCenterTheme.Panel,
        };
        this.recoveryRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        this.recoveryRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        this.recoveryRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        this.recoveryRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        this.recoveryPilotComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        this.recoveryPilotComboBox.Width = 180;
        this.recoveryPilotComboBox.DropDownWidth = 230;
        this.recoveryPilotComboBox.MaxDropDownItems = 10;
        this.recoveryPilotComboBox.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        this.recoveryPilotComboBox.Margin = new Padding(0, 2, 8, 0);
        MainWindowTheme.StyleComboBox(this.recoveryPilotComboBox);

        ConfigureActionButton(
            this.beginRecoveryButton,
            "Use my existing Forge profile");
        ConfigureActionButton(this.checkRecoveryButton, "Check again");
        ConfigureActionButton(
            this.copyRecoveryButton,
            "Copy message for Huron");

        this.recoveryRow.Controls.Add(this.recoveryPilotComboBox, 0, 0);
        this.recoveryRow.Controls.Add(this.beginRecoveryButton, 1, 0);
        this.recoveryRow.Controls.Add(this.checkRecoveryButton, 2, 0);
        this.recoveryRow.Controls.Add(this.copyRecoveryButton, 3, 0);
        layout.Controls.Add(this.recoveryRow, 0, 2);

        this.forgeIdentityDetailLabel.MaximumSize = new Size(860, 0);
        this.forgeIdentityDetailLabel.Margin = new Padding(0, 2, 0, 0);
        layout.Controls.Add(this.forgeIdentityDetailLabel, 0, 3);

        this.forgeIdentityPanel.Controls.Add(layout);
        return this.forgeIdentityPanel;
    }

    private Control CreateAttributionPanel()
    {
        var panel = CreateSectionPanel();
        var layout = CreateSectionLayout(rowCount: 6);

        layout.Controls.Add(CreateHeading("Public attribution"), 0, 0);
        layout.Controls.Add(
            CreateParagraph(
                "Choose whether your character name may appear on the discoveries you share."),
            0,
            1);

        ConfigureRadioButton(
            this.anonymousRadioButton,
            "Publicly anonymous",
            isBold: true);
        ConfigureRadioButton(
            this.pilotNameRadioButton,
            "Show the character name",
            isBold: true);

        var anonymousDescription = CreateIndentedDescription(
            "Your character name is hidden from public data. Forge keeps it privately only for moderation.");
        var pilotDescription = CreateIndentedDescription(
            "Shared data may show the character you were playing when it was discovered. Account names are never used.");

        layout.Controls.Add(this.anonymousRadioButton, 0, 2);
        layout.Controls.Add(anonymousDescription, 0, 3);
        layout.Controls.Add(this.pilotNameRadioButton, 0, 4);
        layout.Controls.Add(pilotDescription, 0, 5);
        panel.Controls.Add(layout);
        return panel;
    }

    private Control CreateCategoriesPanel()
    {
        var panel = CreateSectionPanel();
        var layout = CreateSectionLayout(rowCount: 22);

        layout.Controls.Add(CreateHeading("What to share"), 0, 0);
        layout.Controls.Add(
            CreateParagraph(
                "Choose which discoveries Client Manager may share with Forge. New categories are enabled when you first turn contributions on."),
            0,
            1);

        this.npcPresenceCheckBox.Text = "NPCs and vendors";
        this.npcPresenceCheckBox.Margin = new Padding(0, 8, 0, 2);
        layout.Controls.Add(this.npcPresenceCheckBox, 0, 2);
        layout.Controls.Add(
            CreateIndentedDescription(
                "Visit a station to share its NPCs and vendors."),
            0,
            3);

        this.navigationObjectsCheckBox.Text = "Navigation objects";
        this.navigationObjectsCheckBox.Margin = new Padding(0, 8, 0, 2);
        layout.Controls.Add(this.navigationObjectsCheckBox, 0, 4);
        layout.Controls.Add(
            CreateIndentedDescription(
                "Visit a sector to share its nav points, stations, gates, and planets."),
            0,
            5);

        this.stationServicesCheckBox.Text = "Station services";
        this.stationServicesCheckBox.Margin = new Padding(0, 8, 0, 2);
        layout.Controls.Add(this.stationServicesCheckBox, 0, 6);
        layout.Controls.Add(
            CreateIndentedDescription(
                "Visit a station to share the services available there."),
            0,
            7);

        this.vendorInventoriesCheckBox.Text = "Vendor items";
        this.vendorInventoriesCheckBox.Margin = new Padding(0, 8, 0, 2);
        layout.Controls.Add(this.vendorInventoriesCheckBox, 0, 8);
        layout.Controls.Add(
            CreateIndentedDescription(
                "Open a vendor to share the items they sell. Prices and character-specific discounts are not shared."),
            0,
            9);

        this.mobObservationsCheckBox.Text = "Mob locations";
        this.mobObservationsCheckBox.Margin = new Padding(0, 8, 0, 2);
        layout.Controls.Add(this.mobObservationsCheckBox, 0, 10);
        layout.Controls.Add(
            CreateIndentedDescription(
                "Encounter mobs to help map where they can be found. Defensive station turrets are ignored."),
            0,
            11);

        this.mobLootCheckBox.Text = "Items dropped by mobs";
        this.mobLootCheckBox.Margin = new Padding(0, 8, 0, 2);
        layout.Controls.Add(this.mobLootCheckBox, 0, 12);
        layout.Controls.Add(
            CreateIndentedDescription(
                "Defeat a mob to share the items it can drop. Drop rates and kill counts are not tracked."),
            0,
            13);

        this.harvestableResourcesCheckBox.Text = "Harvestable resources";
        this.harvestableResourcesCheckBox.Margin = new Padding(0, 8, 0, 2);
        layout.Controls.Add(this.harvestableResourcesCheckBox, 0, 14);
        layout.Controls.Add(
            CreateIndentedDescription(
                "Target a harvestable resource to share where it can be found and what it contains."),
            0,
            15);

        this.productionRecipesCheckBox.Text =
            "Manufacturing and refining recipes";
        this.productionRecipesCheckBox.Margin = new Padding(0, 8, 0, 2);
        layout.Controls.Add(this.productionRecipesCheckBox, 0, 16);
        layout.Controls.Add(
            CreateIndentedDescription(
                "View a manufacturing or refining recipe to share its result and required ingredients. Your inventory and crafting activity are not shared."),
            0,
            17);

        this.missionsCheckBox.Text = "NPC missions";
        this.missionsCheckBox.Margin = new Padding(0, 8, 0, 2);
        layout.Controls.Add(this.missionsCheckBox, 0, 18);
        layout.Controls.Add(
            CreateIndentedDescription(
                "Discover and complete NPC missions to share their objectives, NPCs, and fixed rewards. Failed or forfeited missions are ignored."),
            0,
            19);

        this.jobOffersCheckBox.Text = "Jobs Terminal offers";
        this.jobOffersCheckBox.Margin = new Padding(0, 8, 0, 2);
        layout.Controls.Add(this.jobOffersCheckBox, 0, 20);
        layout.Controls.Add(
            CreateIndentedDescription(
                "Select a Jobs Terminal offer to share its objective, level, sponsor, and advertised reward. Expired offers are ignored."),
            0,
            21);

        panel.Controls.Add(layout);
        return panel;
    }

    private Control CreateDataUpdatePanel()
    {
        var panel = CreateSectionPanel();
        var layout = CreateSectionLayout(rowCount: 4);

        layout.Controls.Add(CreateHeading("Forge dataset updates"), 0, 0);
        layout.Controls.Add(
            CreateParagraph(
                "World data updates are checked automatically and activated in place when it is safe. Downloaded revisions wait only while Fleet Auto Pilot or contribution work is busy."),
            0,
            1);

        var actionRow = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = AddonCenterTheme.Panel,
            Margin = new Padding(0, 7, 0, 4),
        };
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        this.dataUpdateValueLabel.Anchor =
            AnchorStyles.Left | AnchorStyles.Top;
        this.dataUpdateValueLabel.Margin = new Padding(0, 6, 12, 0);

        this.dataUpdateButton.AutoSize = true;
        this.dataUpdateButton.Padding = new Padding(10, 3, 10, 3);
        this.dataUpdateButton.Anchor = AnchorStyles.Right | AnchorStyles.Top;
        AddonCenterTheme.StyleButton(this.dataUpdateButton);

        actionRow.Controls.Add(this.dataUpdateValueLabel, 0, 0);
        actionRow.Controls.Add(this.dataUpdateButton, 1, 0);
        layout.Controls.Add(actionRow, 0, 2);

        this.dataUpdateDetailLabel.MaximumSize = new Size(860, 0);
        this.dataUpdateDetailLabel.Margin = new Padding(0, 2, 0, 0);
        layout.Controls.Add(this.dataUpdateDetailLabel, 0, 3);

        panel.Controls.Add(layout);
        return panel;
    }

    private Control CreateOverviewPanel()
    {
        var panel = CreateSectionPanel();
        var layout = CreateSectionLayout(rowCount: 7);

        layout.Controls.Add(CreateHeading("Contribution overview"), 0, 0);

        var identityRow = CreateNameValueRow("Forge access", this.identityValueLabel);
        var revisionRow = CreateNameValueRow("Active Forge dataset", this.revisionValueLabel);
        var statusRow = CreateNameValueRow("Current status", this.statusValueLabel);

        layout.Controls.Add(identityRow, 0, 1);
        layout.Controls.Add(revisionRow, 0, 2);
        layout.Controls.Add(statusRow, 0, 3);

        var statisticsGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 2,
            Margin = new Padding(0, 12, 0, 0),
            BackColor = AddonCenterTheme.Panel,
        };
        statisticsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        statisticsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        statisticsGrid.Controls.Add(CreateSubheading("This session"), 0, 0);
        statisticsGrid.Controls.Add(CreateSubheading("All time on this installation"), 1, 0);
        statisticsGrid.Controls.Add(this.sessionValueLabel, 0, 1);
        statisticsGrid.Controls.Add(this.lifetimeValueLabel, 1, 1);

        layout.Controls.Add(statisticsGrid, 0, 4);
        layout.SetRowSpan(statisticsGrid, 3);
        panel.Controls.Add(layout);
        return panel;
    }

    private Control CreateFooterPanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 48,
            BackColor = AddonCenterTheme.Background,
            Margin = new Padding(0, 0, 0, 8),
        };

        this.resetSessionButton.Text = "Reset session counters";
        this.resetSessionButton.AutoSize = true;
        this.resetSessionButton.Padding = new Padding(10, 3, 10, 3);
        this.resetSessionButton.Anchor = AnchorStyles.Right | AnchorStyles.Top;
        AddonCenterTheme.StyleButton(this.resetSessionButton);
        panel.Controls.Add(this.resetSessionButton);
        panel.Resize += (_, _) =>
        {
            this.resetSessionButton.Location = new Point(
                Math.Max(0, panel.ClientSize.Width - this.resetSessionButton.Width),
                8);
        };
        return panel;
    }

    private void WireEvents()
    {
        this.enabledCheckBox.CheckedChanged += this.SettingsControl_OnChanged;
        this.anonymousRadioButton.CheckedChanged += this.SettingsControl_OnChanged;
        this.pilotNameRadioButton.CheckedChanged += this.SettingsControl_OnChanged;
        this.npcPresenceCheckBox.CheckedChanged += this.SettingsControl_OnChanged;
        this.navigationObjectsCheckBox.CheckedChanged +=
            this.SettingsControl_OnChanged;
        this.stationServicesCheckBox.CheckedChanged +=
            this.SettingsControl_OnChanged;
        this.vendorInventoriesCheckBox.CheckedChanged +=
            this.SettingsControl_OnChanged;
        this.mobObservationsCheckBox.CheckedChanged +=
            this.SettingsControl_OnChanged;
        this.mobLootCheckBox.CheckedChanged += this.SettingsControl_OnChanged;
        this.harvestableResourcesCheckBox.CheckedChanged +=
            this.SettingsControl_OnChanged;
        this.productionRecipesCheckBox.CheckedChanged +=
            this.SettingsControl_OnChanged;
        this.missionsCheckBox.CheckedChanged +=
            this.SettingsControl_OnChanged;
        this.jobOffersCheckBox.CheckedChanged +=
            this.SettingsControl_OnChanged;
        this.resetSessionButton.Click += this.ResetSessionButton_OnClick;
        this.dataUpdateButton.Click += this.DataUpdateButton_OnClick;
        this.beginRecoveryButton.Click += this.BeginRecoveryButton_OnClick;
        this.checkRecoveryButton.Click += this.CheckRecoveryButton_OnClick;
        this.copyRecoveryButton.Click += this.CopyRecoveryButton_OnClick;
        this.titleBar.DragRequested += this.TitleBar_OnDragRequested;
        this.titleBar.MinimizeRequested += this.TitleBar_OnMinimizeRequested;
        this.titleBar.MaximizeRequested += this.TitleBar_OnMaximizeRequested;
        this.titleBar.CloseRequested += this.TitleBar_OnCloseRequested;
        this.Resize += this.ForgeContributionsForm_OnResize;
    }

    private void LoadSettingsIntoUi()
    {
        this.updatingUi = true;

        try
        {
            var settings = this.clientManager.ForgeContributionSettings;
            settings.EnsureDefaults();
            this.enabledCheckBox.Checked = settings.Enabled;
            this.anonymousRadioButton.Checked =
                settings.Attribution ==
                ForgeContributionAttribution.PubliclyAnonymous;
            this.pilotNameRadioButton.Checked =
                settings.Attribution ==
                ForgeContributionAttribution.LivePilotName;
            this.npcPresenceCheckBox.Checked = settings.Categories.NpcPresence;
            this.navigationObjectsCheckBox.Checked =
                settings.Categories.NavigationObjects;
            this.stationServicesCheckBox.Checked =
                settings.Categories.StationServices;
            this.vendorInventoriesCheckBox.Checked =
                settings.Categories.VendorInventories;
            this.mobObservationsCheckBox.Checked =
                settings.Categories.MobObservations;
            this.mobLootCheckBox.Checked = settings.Categories.LootObservations;
            this.harvestableResourcesCheckBox.Checked =
                settings.Categories.ResourceObservations;
            this.productionRecipesCheckBox.Checked =
                settings.Categories.ProductionRecipes;
            this.missionsCheckBox.Checked = settings.Categories.Missions;
            this.jobOffersCheckBox.Checked = settings.Categories.JobOffers;
            this.UpdateControlAvailability();
        }
        finally
        {
            this.updatingUi = false;
        }
    }

    private void UpdateControlAvailability()
    {
        // Attribution is a saved preference and remains configurable before
        // the user opts into contribution. Only active subject collection is
        // gated by the master participation switch.
        this.npcPresenceCheckBox.Enabled = this.enabledCheckBox.Checked;
        this.navigationObjectsCheckBox.Enabled = this.enabledCheckBox.Checked;
        this.stationServicesCheckBox.Enabled = this.enabledCheckBox.Checked;
        this.vendorInventoriesCheckBox.Enabled = this.enabledCheckBox.Checked;
        this.mobObservationsCheckBox.Enabled = this.enabledCheckBox.Checked;
        this.mobLootCheckBox.Enabled = this.enabledCheckBox.Checked;
        this.harvestableResourcesCheckBox.Enabled = this.enabledCheckBox.Checked;
        this.productionRecipesCheckBox.Enabled = this.enabledCheckBox.Checked;
        this.missionsCheckBox.Enabled = this.enabledCheckBox.Checked;
        this.jobOffersCheckBox.Enabled = this.enabledCheckBox.Checked;
    }

    private void SettingsControl_OnChanged(object? sender, EventArgs e)
    {
        if (this.updatingUi)
        {
            return;
        }

        this.UpdateControlAvailability();
        var settings = this.clientManager.ForgeContributionSettings;
        settings.Enabled = this.enabledCheckBox.Checked;
        settings.Attribution = this.pilotNameRadioButton.Checked
            ? ForgeContributionAttribution.LivePilotName
            : ForgeContributionAttribution.PubliclyAnonymous;
        settings.Categories.NpcPresence = this.npcPresenceCheckBox.Checked;
        settings.Categories.NavigationObjects =
            this.navigationObjectsCheckBox.Checked;
        settings.Categories.StationServices =
            this.stationServicesCheckBox.Checked;
        settings.Categories.VendorInventories =
            this.vendorInventoriesCheckBox.Checked;
        settings.Categories.MobObservations =
            this.mobObservationsCheckBox.Checked;
        settings.Categories.LootObservations = this.mobLootCheckBox.Checked;
        settings.Categories.ResourceObservations =
            this.harvestableResourcesCheckBox.Checked;
        settings.Categories.ProductionRecipes =
            this.productionRecipesCheckBox.Checked;
        settings.Categories.Missions = this.missionsCheckBox.Checked;
        settings.Categories.JobOffers = this.jobOffersCheckBox.Checked;
        this.clientManager.SaveForgeContributionSettings();
        this.RefreshStatistics();
    }

    private void ResetSessionButton_OnClick(object? sender, EventArgs e)
    {
        this.clientManager.ResetForgeContributionSessionStatistics();
        this.RefreshStatistics();
    }

    private async void BeginRecoveryButton_OnClick(
        object? sender,
        EventArgs e)
    {
        if (this.identityOperationBusy ||
            this.recoveryPilotComboBox.SelectedItem is not string pilotName)
        {
            return;
        }

        var choice = MessageBox.Show(
            this,
            "Use this only if you already used Forge on another Client Manager installation. Continue?",
            "Use my existing Forge profile",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        if (choice != DialogResult.Yes)
        {
            return;
        }

        this.identityOperationBusy = true;
        this.identityOperationMessage = "Preparing your request…";
        this.RefreshStatistics();

        try
        {
            var status = await this.clientManager
                .BeginForgeIdentityRecoveryAsync(pilotName);
            this.identityOperationMessage = BuildRecoveryInstruction(status);
        }
        catch (Exception)
        {
            this.identityOperationMessage =
                "Forge could not prepare the request. Please try again.";
        }
        finally
        {
            this.identityOperationBusy = false;
            this.RefreshStatistics();
        }
    }

    private async void CheckRecoveryButton_OnClick(
        object? sender,
        EventArgs e)
    {
        if (this.identityOperationBusy)
        {
            return;
        }

        this.identityOperationBusy = true;
        this.identityOperationMessage = "Checking with Forge…";
        this.RefreshStatistics();

        try
        {
            var status = await this.clientManager
                .RefreshForgeIdentityRecoveryAsync();
            this.identityOperationMessage = status.HasIdentity
                ? "Huron approved the request. Forge is ready on this installation."
                : BuildRecoveryInstruction(status);
        }
        catch (Exception)
        {
            this.identityOperationMessage =
                "Forge could not check the request. Please try again.";
        }
        finally
        {
            this.identityOperationBusy = false;
            this.RefreshStatistics();
        }
    }

    private void CopyRecoveryButton_OnClick(object? sender, EventArgs e)
    {
        var status = this.clientManager.GetForgeIdentityStatus();
        if (!status.HasPendingRecovery ||
            string.IsNullOrWhiteSpace(status.RecoveryCode))
        {
            return;
        }

        var pilotName = string.IsNullOrWhiteSpace(status.RecoveryPilotName)
            ? "Unknown"
            : status.RecoveryPilotName;
        var forumMessage = string.Join(
            Environment.NewLine,
            "Hi Huron,",
            "",
            "I would like to use my existing Net7 Forge profile on this Client Manager installation.",
            "",
            string.Concat("Character: ", pilotName),
            string.Concat("Code: ", status.RecoveryCode));

        try
        {
            Clipboard.SetText(forumMessage);
            this.identityOperationMessage =
                "Message copied. Send it to Huron in a private message on the official Net-7 forums.";
        }
        catch (Exception)
        {
            this.identityOperationMessage =
                "The message could not be copied. Please copy the code manually.";
        }

        this.RefreshStatistics();
    }

    private async void DataUpdateButton_OnClick(
        object? sender,
        EventArgs e)
    {
        if (this.dataUpdateOperationBusy)
        {
            return;
        }

        this.dataUpdateOperationBusy = true;
        this.dataUpdateOperationMessage = "";
        this.RefreshStatistics();

        try
        {
            var status = this.clientManager.GetNavigationDataUpdateStatus();

            if (status.HasPendingUpdate)
            {
                _ = this.clientManager.TryActivatePendingNavigationData(
                    out this.dataUpdateOperationMessage);
            }
            else
            {
                await this.clientManager
                    .CheckForNavigationDataUpdateNowAsync();

                status = this.clientManager.GetNavigationDataUpdateStatus();
                this.dataUpdateOperationMessage = status.HasPendingUpdate
                    ? string.Create(
                        CultureInfo.InvariantCulture,
                        $"Forge revision {status.PendingRevision} was downloaded and will activate automatically when safe.")
                    : !string.IsNullOrWhiteSpace(status.LastError)
                        ? string.Concat(
                            "The update check failed: ",
                            status.LastError)
                        : string.Create(
                            CultureInfo.InvariantCulture,
                            $"Forge revision {status.ActiveRevision} is current.");
            }
        }
        catch (Exception exception)
        {
            this.dataUpdateOperationMessage = string.Concat(
                "The Forge dataset operation failed: ",
                exception.Message);
        }
        finally
        {
            this.dataUpdateOperationBusy = false;
            this.RefreshStatistics();
        }
    }

    private void RefreshTimer_OnTick(object? sender, EventArgs e)
    {
        this.RefreshStatistics();
    }

    private void RefreshStatistics()
    {
        var scrollPosition = new Point(
            -this.contentPanel.AutoScrollPosition.X,
            -this.contentPanel.AutoScrollPosition.Y);
        var statistics = this.clientManager.GetForgeContributionStatistics();
        var updateStatus = this.clientManager.GetNavigationDataUpdateStatus();
        var updateStateKey = string.Create(
            CultureInfo.InvariantCulture,
            $"{updateStatus.ActiveRevision}|{updateStatus.PendingRevision}|{updateStatus.IsChecking}|{updateStatus.LastError}");

        if (!this.dataUpdateOperationBusy &&
            !string.IsNullOrEmpty(this.dataUpdateStateKey) &&
            !string.Equals(
                this.dataUpdateStateKey,
                updateStateKey,
                StringComparison.Ordinal))
        {
            this.dataUpdateOperationMessage = "";
        }

        this.dataUpdateStateKey = updateStateKey;
        var changed = false;

        this.contentPanel.SuspendLayout();

        try
        {
            changed |= this.RefreshForgeIdentityStatus();
            var identityStatus = this.clientManager.GetForgeIdentityStatus();
            changed |= SetText(
                this.identityValueLabel,
                identityStatus.HasIdentity
                    ? "Ready"
                    : identityStatus.HasPendingRecovery
                        ? "Waiting for Huron"
                        : "Connects automatically when needed");
            changed |= SetText(
                this.revisionValueLabel,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Revision {this.clientManager.NavigationData.Revision} · {this.clientManager.NavigationData.NpcCount} canonical NPCs · {this.clientManager.NavigationData.StationFacilityCount} station facilities · {this.clientManager.NavigationData.VendorItemCount} vendor items · {this.clientManager.NavigationData.MobVariantCount} mob variants · {this.clientManager.NavigationData.MobClusterCount} encounter clusters · {this.clientManager.NavigationData.MobLootCount} mob-loot relationships · {this.clientManager.NavigationData.HarvestableFieldCount} harvestable fields · {this.clientManager.NavigationData.HarvestableResourceCount} harvestable-resource relationships · {this.clientManager.NavigationData.GravityWellCount} gravity wells"));
            changed |= SetText(
                this.statusValueLabel,
                statistics.Session.Status);
            changed |= SetColor(
                this.statusValueLabel,
                statistics.Session.FailedBatches > 0
                    ? AddonCenterTheme.Warning
                    : AddonCenterTheme.Text);
            changed |= SetText(
                this.sessionValueLabel,
                FormatStatistics(statistics.Session));
            changed |= SetText(
                this.lifetimeValueLabel,
                FormatStatistics(statistics.Lifetime));
            changed |= this.RefreshDataUpdateStatus(updateStatus);
        }
        finally
        {
            this.contentPanel.ResumeLayout(performLayout: changed);
        }

        if (changed &&
            (scrollPosition.X > 0 || scrollPosition.Y > 0) &&
            this.IsHandleCreated &&
            !this.IsDisposed)
        {
            this.BeginInvoke((MethodInvoker)(() =>
            {
                if (!this.IsDisposed)
                {
                    this.contentPanel.AutoScrollPosition = scrollPosition;
                }
            }));
        }
    }

    private bool RefreshForgeIdentityStatus()
    {
        var status = this.clientManager.GetForgeIdentityStatus();
        var livePilots = this.clientManager.GetLiveForgePilots();
        var selectedPilot = this.recoveryPilotComboBox.SelectedItem as string;
        var itemsChanged = this.recoveryPilotComboBox.Items.Count != livePilots.Count ||
            livePilots.Where((pilot, index) =>
                !string.Equals(
                    pilot,
                    this.recoveryPilotComboBox.Items[index] as string,
                    StringComparison.OrdinalIgnoreCase))
                .Any();

        if (itemsChanged)
        {
            this.recoveryPilotComboBox.BeginUpdate();

            try
            {
                this.recoveryPilotComboBox.Items.Clear();
                this.recoveryPilotComboBox.Items.AddRange(livePilots.ToArray());
            }
            finally
            {
                this.recoveryPilotComboBox.EndUpdate();
            }

            var preferredPilot = livePilots.FirstOrDefault(pilot =>
                    string.Equals(
                        pilot,
                        selectedPilot,
                        StringComparison.OrdinalIgnoreCase)) ??
                livePilots.FirstOrDefault(pilot =>
                    string.Equals(
                        pilot,
                        status.RecoveryPilotName,
                        StringComparison.OrdinalIgnoreCase)) ??
                livePilots.FirstOrDefault();
            this.recoveryPilotComboBox.SelectedItem = preferredPilot;
        }
        else if (this.recoveryPilotComboBox.SelectedIndex < 0 &&
                 livePilots.Count > 0)
        {
            this.recoveryPilotComboBox.SelectedIndex = 0;
        }

        string value;
        string detail;
        Color color;

        if (this.identityOperationBusy)
        {
            value = "Working…";
            detail = this.identityOperationMessage;
            color = AddonCenterTheme.Accent;
        }
        else if (status.HasIdentity)
        {
            value = "Ready";
            detail = "";
            color = AddonCenterTheme.Text;
        }
        else if (status.HasPendingRecovery)
        {
            value = string.Concat("Waiting for Huron · ", status.RecoveryCode);
            detail = string.IsNullOrWhiteSpace(this.identityOperationMessage)
                ? BuildRecoveryInstruction(status)
                : this.identityOperationMessage;
            color = AddonCenterTheme.Warning;
        }
        else if (!string.IsNullOrWhiteSpace(status.RecoveryStatus))
        {
            value = GetRecoveryStatusTitle(status.RecoveryStatus);
            detail = string.IsNullOrWhiteSpace(this.identityOperationMessage)
                ? GetRecoveryStatusDetail(status.RecoveryStatus)
                : this.identityOperationMessage;
            color = AddonCenterTheme.Warning;
        }
        else
        {
            value = "Not connected";
            detail = string.IsNullOrWhiteSpace(this.identityOperationMessage)
                ? livePilots.Count == 0
                    ? "Log into a character before reconnecting to a previous Forge profile."
                    : "Already used Forge on another installation? Choose your character below. New users do not need to do anything."
                : this.identityOperationMessage;
            color = AddonCenterTheme.Text;
        }

        var showBeginRecovery =
            !status.HasIdentity &&
            !status.HasPendingRecovery &&
            livePilots.Count > 0;
        var showPendingActions =
            !status.HasIdentity && status.HasPendingRecovery;

        this.recoveryPilotComboBox.Visible = showBeginRecovery;
        this.recoveryPilotComboBox.Enabled =
            showBeginRecovery && !this.identityOperationBusy;
        this.beginRecoveryButton.Visible = showBeginRecovery;
        this.beginRecoveryButton.Enabled =
            showBeginRecovery && !this.identityOperationBusy;
        this.checkRecoveryButton.Visible = showPendingActions;
        this.checkRecoveryButton.Enabled =
            showPendingActions && !this.identityOperationBusy;
        this.copyRecoveryButton.Visible = showPendingActions;
        this.copyRecoveryButton.Enabled =
            showPendingActions &&
            !this.identityOperationBusy &&
            !string.IsNullOrWhiteSpace(status.RecoveryCode);

        var showRecoveryRow = showBeginRecovery || showPendingActions;
        var showPanel = !status.HasIdentity;
        var showDetail = showPanel && !string.IsNullOrWhiteSpace(detail);

        var changed = itemsChanged;
        changed |= SetVisible(this.forgeIdentityPanel, showPanel);
        changed |= SetVisible(this.recoveryRow, showRecoveryRow);
        changed |= SetVisible(this.forgeIdentityDetailLabel, showDetail);
        changed |= SetText(this.forgeIdentityStatusValueLabel, value);
        changed |= SetColor(this.forgeIdentityStatusValueLabel, color);
        changed |= SetText(this.forgeIdentityDetailLabel, detail);
        changed |= SetColor(
            this.forgeIdentityDetailLabel,
            status.HasPendingRecovery ||
            (!status.HasIdentity && !string.IsNullOrWhiteSpace(status.RecoveryStatus))
                ? AddonCenterTheme.Warning
                : AddonCenterTheme.MutedText);
        return changed;
    }

    private static string BuildRecoveryInstruction(
        ForgeIdentityStatusSnapshot status)
    {
        if (!status.HasPendingRecovery ||
            string.IsNullOrWhiteSpace(status.RecoveryCode))
        {
            return GetRecoveryStatusDetail(status.RecoveryStatus);
        }

        var expiry = status.RecoveryExpiresAtUtc?.ToLocalTime().ToString(
            "g",
            CultureInfo.CurrentCulture);
        return string.Concat(
            "Send code ",
            status.RecoveryCode,
            " for ",
            status.RecoveryPilotName,
            " to Huron in a private message on the official Net-7 forums",
            string.IsNullOrWhiteSpace(expiry)
                ? "."
                : string.Concat(". The code expires ", expiry, "."));
    }

    private static string GetRecoveryStatusTitle(string? status)
    {
        return status?.Trim().ToLowerInvariant() switch
        {
            "rejected" => "Request declined",
            "expired" => "Request expired",
            "not_found" => "Request unavailable",
            "cancelled" => "Request cancelled",
            _ => "Request needs attention",
        };
    }

    private static string GetRecoveryStatusDetail(string? status)
    {
        return status?.Trim().ToLowerInvariant() switch
        {
            "rejected" =>
                "Huron did not approve this request. Start a new one if you still need to reconnect.",
            "expired" =>
                "Start a new request and send the new code to Huron.",
            "not_found" =>
                "Forge no longer has this request. Start a new one if needed.",
            "cancelled" =>
                "Start a new request if you still need to reconnect.",
            _ =>
                "Start a new request if you still need to reconnect.",
        };
    }

    private static void ConfigureActionButton(Button button, string text)
    {
        button.Text = text;
        button.AutoSize = true;
        button.Padding = new Padding(10, 3, 10, 3);
        button.Margin = new Padding(4, 0, 0, 0);
        button.Anchor = AnchorStyles.Right | AnchorStyles.Top;
        AddonCenterTheme.StyleButton(button);
    }

    private bool RefreshDataUpdateStatus(
        NavigationDataUpdateStatus status)
    {
        string value;
        string detail;
        string buttonText;
        Color valueColor;

        if (this.dataUpdateOperationBusy || status.IsChecking)
        {
            value = "Checking Net7 Forge for newer world data…";
            detail = string.IsNullOrWhiteSpace(
                    this.dataUpdateOperationMessage)
                ? "The active dataset remains available while the check runs."
                : this.dataUpdateOperationMessage;
            buttonText = "Checking…";
            valueColor = AddonCenterTheme.Accent;
        }
        else if (status.HasPendingUpdate)
        {
            value = string.Create(
                CultureInfo.InvariantCulture,
                $"Revision {status.PendingRevision} is downloaded and will activate automatically when safe.");
            detail = string.IsNullOrWhiteSpace(
                    this.dataUpdateOperationMessage)
                ? "Routes, Atlas data, and canonical contribution coverage will update as soon as Fleet Auto Pilot and contribution work are idle."
                : this.dataUpdateOperationMessage;
            buttonText = string.Create(
                CultureInfo.InvariantCulture,
                $"Activate revision {status.PendingRevision} now");
            valueColor = AddonCenterTheme.Accent;
        }
        else if (!string.IsNullOrWhiteSpace(status.LastError))
        {
            value = "The latest automatic Forge dataset check failed.";
            detail = string.IsNullOrWhiteSpace(
                    this.dataUpdateOperationMessage)
                ? string.Concat(
                    status.LastError,
                    " ",
                    FormatNextAutomaticCheck(status))
                : this.dataUpdateOperationMessage;
            buttonText = "Check now";
            valueColor = AddonCenterTheme.Warning;
        }
        else
        {
            value = string.Create(
                CultureInfo.InvariantCulture,
                $"Automatic updates are enabled. Revision {status.ActiveRevision} is active.");
            detail = string.IsNullOrWhiteSpace(
                    this.dataUpdateOperationMessage)
                ? FormatAutomaticUpdateDetail(status)
                : this.dataUpdateOperationMessage;
            buttonText = "Check now";
            valueColor = AddonCenterTheme.Text;
        }

        var changed = false;
        changed |= SetText(this.dataUpdateValueLabel, value);
        changed |= SetColor(this.dataUpdateValueLabel, valueColor);
        changed |= SetText(this.dataUpdateDetailLabel, detail);
        changed |= SetColor(
            this.dataUpdateDetailLabel,
            !string.IsNullOrWhiteSpace(status.LastError) &&
            !status.HasPendingUpdate
                ? AddonCenterTheme.Warning
                : AddonCenterTheme.MutedText);
        changed |= SetText(this.dataUpdateButton, buttonText);

        var buttonEnabled =
            !this.dataUpdateOperationBusy &&
            !status.IsChecking;

        if (this.dataUpdateButton.Enabled != buttonEnabled)
        {
            this.dataUpdateButton.Enabled = buttonEnabled;
            changed = true;
        }

        return changed;
    }

    private static string FormatAutomaticUpdateDetail(
        NavigationDataUpdateStatus status)
    {
        var lastChecked = status.LastSuccessfulCheck is { } checkedAt
            ? string.Concat(
                "Last checked ",
                checkedAt.ToLocalTime().ToString(
                    "g",
                    CultureInfo.CurrentCulture),
                ". ")
            : "No automatic check has completed yet. ";

        return string.Concat(
            lastChecked,
            FormatNextAutomaticCheck(status));
    }

    private static string FormatNextAutomaticCheck(
        NavigationDataUpdateStatus status)
    {
        if (status.NextAutomaticCheck is not { } nextCheck)
        {
            return "The next automatic check is being scheduled.";
        }

        var now = DateTimeOffset.UtcNow;
        var remaining = nextCheck - now;

        if (remaining <= TimeSpan.Zero)
        {
            return "The next automatic check is due now.";
        }

        var minutes = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
        return string.Create(
            CultureInfo.InvariantCulture,
            $"Next automatic check in about {minutes} minute{(minutes == 1 ? string.Empty : "s")}.");
    }

    private static bool SetVisible(Control control, bool visible)
    {
        if (control.Visible == visible)
        {
            return false;
        }

        control.Visible = visible;
        return true;
    }

    private static bool SetText(Control control, string text)
    {
        if (string.Equals(
                control.Text,
                text,
                StringComparison.Ordinal))
        {
            return false;
        }

        control.Text = text;
        return true;
    }

    private static bool SetColor(Control control, Color color)
    {
        if (control.ForeColor == color)
        {
            return false;
        }

        control.ForeColor = color;
        return true;
    }

    private static string FormatStatistics(
        ForgeContributionStatisticsSnapshot statistics)
    {
        return string.Join(
            Environment.NewLine,
            string.Create(
                CultureInfo.InvariantCulture,
                $"Stations observed: {statistics.StationsObserved}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"NPCs observed: {statistics.NpcsObserved}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"NPC facts submitted: {statistics.NpcFactsSubmitted}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"Already canonical: {statistics.AlreadyCanonical}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"Evidence accepted: {statistics.EvidenceAccepted}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"NPC conflicts: {statistics.Conflicts}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"Navigation objects observed: {statistics.NavigationObjectsObserved}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"Navigation-object facts submitted: {statistics.NavigationObjectFactsSubmitted}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"Navigation objects already canonical: {statistics.NavigationObjectsAlreadyCanonical}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"Navigation-object evidence accepted: {statistics.NavigationObjectEvidenceAccepted}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"Navigation-object conflicts: {statistics.NavigationObjectConflicts}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"Station facilities observed: {statistics.StationFacilitiesObserved}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"Station facility facts submitted: {statistics.StationFacilityFactsSubmitted}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"Station facilities already canonical: {statistics.StationFacilitiesAlreadyCanonical}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"Station facility evidence accepted: {statistics.StationFacilityEvidenceAccepted}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"Station facility conflicts: {statistics.StationFacilityConflicts}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"Vendors observed: {statistics.VendorsObserved}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"Vendor items observed: {statistics.VendorItemsObserved}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"Vendor item facts submitted: {statistics.VendorItemFactsSubmitted}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"Vendor items already canonical: {statistics.VendorItemsAlreadyCanonical}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"Vendor item evidence accepted: {statistics.VendorItemEvidenceAccepted}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"Vendor item conflicts: {statistics.VendorItemConflicts}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"Vendor items removed: {statistics.VendorItemsRemoved}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"Mob sightings observed: {statistics.MobSightingsObserved}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"Mob-sighting facts submitted: {statistics.MobSightingFactsSubmitted}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"Mob-sighting evidence accepted: {statistics.MobSightingEvidenceAccepted}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"Mob variants created: {statistics.MobVariantsCreated}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"Encounter clusters created: {statistics.MobClustersCreated}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"Encounter clusters updated: {statistics.MobClustersUpdated}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"Loot corpses observed: {statistics.LootCorpsesObserved}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"Mob-loot relationships observed: {statistics.MobLootRelationshipsObserved}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"Mob-loot facts submitted: {statistics.MobLootFactsSubmitted}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"Mob-loot relationships already canonical: {statistics.MobLootAlreadyCanonical}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"Mob-loot evidence accepted: {statistics.MobLootEvidenceAccepted}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"Mob variants created from loot: {statistics.MobLootVariantsCreated}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"Mob-loot relationships created: {statistics.MobLootRelationshipsCreated}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"Mob-loot relationships strengthened: {statistics.MobLootRelationshipsStrengthened}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"Harvestable-resource relationships observed: {statistics.HarvestableResourcesObserved}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"Harvestable observations submitted: {statistics.HarvestableFactsSubmitted}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"Harvestable relationships already canonical: {statistics.HarvestableAlreadyCanonical}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"Harvestable evidence accepted: {statistics.HarvestableEvidenceAccepted}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"Harvestable variants created: {statistics.HarvestableVariantsCreated}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"Harvestable fields created: {statistics.HarvestableFieldsCreated}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"Harvestable fields updated: {statistics.HarvestableFieldsUpdated}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"Harvestable relationships created: {statistics.HarvestableRelationshipsCreated}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"Harvestable relationships strengthened: {statistics.HarvestableRelationshipsStrengthened}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"Recipes found: {statistics.ProductionRecipesObserved}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"Recipes shared: {statistics.ProductionRecipeFactsSubmitted}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"Recipes already known: {statistics.ProductionRecipesAlreadyCanonical}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"Recipes needing review: {statistics.ProductionRecipeConflicts}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"New recipes added: {statistics.ProductionRecipesCreated}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"Missions observed: {statistics.MissionsObserved}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"Mission facts shared: {statistics.MissionFactsSubmitted}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"Missions already known: {statistics.MissionsAlreadyCanonical}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"Mission evidence accepted: {statistics.MissionEvidenceAccepted}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"Mission facts needing review: {statistics.MissionConflicts}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"New missions added: {statistics.MissionsCreated}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"Missions strengthened: {statistics.MissionsStrengthened}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"Job offers observed: {statistics.JobOffersObserved}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"Job offers shared: {statistics.JobOfferFactsSubmitted}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"Job offers already known: {statistics.JobOffersAlreadyCanonical}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"Job-offer evidence accepted: {statistics.JobOfferEvidenceAccepted}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"Job offers needing review: {statistics.JobOfferConflicts}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"New job offers added: {statistics.JobOffersCreated}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"Job offers strengthened: {statistics.JobOffersStrengthened}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"Successful batches: {statistics.SuccessfulBatches}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"Failed batches: {statistics.FailedBatches}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"Forge revisions published: {statistics.PublishedRevisions}"),
            string.Concat(
                "Last success: ",
                FormatTimestamp(statistics.LastSuccessfulContributionUtc)),
            string.Concat(
                "Last failure: ",
                FormatTimestamp(statistics.LastFailedContributionUtc)));
    }

    private static string FormatTimestamp(DateTimeOffset? value)
    {
        return value?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) ??
            "Never";
    }

    private void TitleBar_OnDragRequested(
        object? sender,
        EventArgs e)
    {
        this.BeginTitleBarDrag();
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

    private void ForgeContributionsForm_OnResize(
        object? sender,
        EventArgs e)
    {
        this.titleBar.IsMaximized =
            this.WindowState == FormWindowState.Maximized;
    }

    private void BeginTitleBarDrag()
    {
        if (this.WindowState == FormWindowState.Maximized)
        {
            return;
        }

        NativeMethods.ReleaseCapture();
        NativeMethods.SendMoveWindowMessage(this.Handle);
    }

    private static Panel CreateSectionPanel()
    {
        return new Panel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = AddonCenterTheme.Panel,
            Padding = new Padding(16),
            Margin = new Padding(0, 0, 0, 12),
        };
    }

    private static TableLayoutPanel CreateSectionLayout(int rowCount)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = rowCount,
            BackColor = AddonCenterTheme.Panel,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        return layout;
    }

    private static Label CreateHeading(string text)
    {
        return new Label
        {
            AutoSize = true,
            Text = text,
            ForeColor = AddonCenterTheme.Accent,
            BackColor = AddonCenterTheme.Panel,
            Font = new Font("Segoe UI", 11.0f, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 7),
        };
    }

    private static Label CreateSubheading(string text)
    {
        return new Label
        {
            AutoSize = true,
            Text = text,
            ForeColor = AddonCenterTheme.Text,
            BackColor = AddonCenterTheme.Panel,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            Margin = new Padding(0, 0, 16, 5),
        };
    }

    private static Label CreateParagraph(string text)
    {
        return new Label
        {
            AutoSize = true,
            MaximumSize = new Size(880, 0),
            Text = text,
            ForeColor = AddonCenterTheme.MutedText,
            BackColor = AddonCenterTheme.Panel,
            Margin = new Padding(0, 0, 0, 4),
        };
    }

    private static Label CreateIndentedDescription(string text)
    {
        var label = CreateParagraph(text);
        label.Margin = new Padding(27, 0, 0, 5);
        return label;
    }

    private static Label CreateValueLabel()
    {
        return new Label
        {
            AutoSize = true,
            MaximumSize = new Size(650, 0),
            ForeColor = AddonCenterTheme.Text,
            BackColor = AddonCenterTheme.Panel,
            Margin = new Padding(0),
        };
    }

    private static Label CreateStatisticsLabel()
    {
        return new Label
        {
            AutoSize = true,
            ForeColor = AddonCenterTheme.MutedText,
            BackColor = AddonCenterTheme.Panel,
            Font = new Font("Consolas", 9.0f),
            Margin = new Padding(0, 0, 16, 0),
        };
    }

    private static Control CreateNameValueRow(string name, Label valueLabel)
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = AddonCenterTheme.Panel,
            Margin = new Padding(0, 2, 0, 2),
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 165f));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        row.Controls.Add(new Label
        {
            AutoSize = true,
            Text = name,
            ForeColor = AddonCenterTheme.MutedText,
            BackColor = AddonCenterTheme.Panel,
            Margin = new Padding(0),
        }, 0, 0);
        row.Controls.Add(valueLabel, 1, 0);
        return row;
    }

    private static void ConfigureRadioButton(
        RadioButton radioButton,
        string text,
        bool isBold)
    {
        radioButton.AutoSize = true;
        radioButton.Text = text;
        radioButton.ForeColor = AddonCenterTheme.Text;
        radioButton.BackColor = AddonCenterTheme.Panel;
        radioButton.Margin = new Padding(0, 7, 0, 1);
        radioButton.Font = new Font(
            "Segoe UI",
            9.0f,
            isBold ? FontStyle.Bold : FontStyle.Regular);
    }
}
