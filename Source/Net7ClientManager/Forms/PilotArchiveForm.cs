// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using System.ComponentModel;
using System.Globalization;
using Net7ClientManager.Addons.Contracts;
using Net7ClientManager.Core;
using Net7ClientManager.MissionJournal;
using Net7ClientManager.Models;
using Net7ClientManager.Observations.Models;
using Net7ClientManager.Observations.Observers;
using Net7ClientManager.PilotArchive;
using Net7ClientManager.Services;
using Net7ClientManager.Win32;

public sealed partial class PilotArchiveForm : ThemedForm
{
    private const string PilotBuildColumnName = "pilot_builds";
    private const string PilotLaunchColumnName = "pilot_launch";
    private const int ArchiveIconColumnWidth = 34;
    private static readonly Size ArchiveIconSize = new(22, 22);

    private readonly ClientManager clientManager;
    private readonly WindowPlacementBinding windowPlacement;
    private readonly DataGridView pilotGrid;
    private readonly TextBox searchTextBox = new();
    private readonly Button searchButton = new();
    private readonly Label searchStatusLabel = new();
    private readonly ThemedTabHost detailTabs = new();
    private readonly SplitContainer contentSplit = new();
    private readonly SplitContainer missionHistorySplit = new();
    private readonly ToolTip toolTip = new();
    private readonly ActionToolTip itemToolTip = new();
    private readonly System.Windows.Forms.Timer launchStateTimer = new();

    private readonly Dictionary<string, Label> overviewValues =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, Label> sectionTimestampLabels =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, ThemedTabPage> sectionTabs =
        new(StringComparer.Ordinal);
    private readonly Dictionary<DataGridView, string> gridStateKeys = [];
    private readonly HashSet<Control> darkScrollbarThemeControls = [];

    private readonly DataGridView cargoGrid;
    private readonly DataGridView equipmentGrid;
    private readonly DataGridView vaultGrid;
    private readonly DataGridView skillsGrid;
    private readonly DataGridView missionsGrid;
    private readonly DataGridView missionHistoryGrid;
    private readonly TextBox missionHistoryDetailsTextBox = new();
    private readonly Dictionary<string, MissionJournalEntry>
        missionHistoryEntriesById = new(StringComparer.Ordinal);
    private readonly DataGridView reputationsGrid;
    private readonly DataGridView searchGrid;
    private readonly Button startCharacterButton = new();
    private readonly Label launchStatusLabel = new();

    private uint? selectedCharacterId;
    private uint? pendingCharacterId;
    private bool refreshingPilotList;
    private bool restoringUiState;
    private bool contentSplitterUserMovePending;
    private bool missionHistorySplitterUserMovePending;

    public PilotArchiveForm(
        ClientManager clientManager,
        IWin32Window? preferredOwner = null)
    {
        this.clientManager = clientManager;

        this.Text = "Pilot Archive";
        this.Icon = ResourceLoader.Net7ClientManagerIcon;
        this.StartPosition = FormStartPosition.CenterScreen;
        this.Size = new Size(1320, 860);
        this.MinimumSize = new Size(1050, 680);
        this.BackColor = MainWindowTheme.Background;
        this.ForeColor = MainWindowTheme.Text;
        this.Font = MainWindowTheme.CreateBodyFont();
        this.ConfigureWindowChrome(
            allowResize: true,
            showMinimizeButton: true,
            showMaximizeButton: true);
        this.ConfigureHelpTopic(HelpTopicIds.PilotArchive);
        this.windowPlacement =
            clientManager.BindGlobalWindowPlacement(
                this,
                WindowPlacementIds.PilotArchive,
                preferredOwner);

        this.pilotGrid = CreateGrid();
        this.cargoGrid = CreateGrid();
        this.equipmentGrid = CreateGrid();
        this.vaultGrid = CreateGrid();
        this.skillsGrid = CreateGrid();
        this.missionsGrid = CreateGrid();
        this.missionHistoryGrid = CreateGrid();
        this.reputationsGrid = CreateGrid();
        this.searchGrid = CreateGrid();

        this.RegisterGridState(this.pilotGrid, "pilots");
        this.RegisterGridState(this.cargoGrid, PilotArchiveSections.Cargo);
        this.RegisterGridState(
            this.equipmentGrid,
            PilotArchiveSections.Equipment);
        this.RegisterGridState(this.vaultGrid, PilotArchiveSections.Vault);
        this.RegisterGridState(this.skillsGrid, PilotArchiveSections.Skills);
        this.RegisterGridState(
            this.missionsGrid,
            PilotArchiveSections.Missions);
        this.RegisterGridState(
            this.missionHistoryGrid,
            PilotArchiveSections.MissionHistory);
        this.RegisterGridState(
            this.activityHistoryGrid,
            PilotArchiveSections.ActivityHistory);
        this.RegisterGridState(
            this.combatHistoryGrid,
            PilotArchiveSections.CombatHistory);
        this.RegisterGridState(
            this.reputationsGrid,
            PilotArchiveSections.Reputations);
        this.RegisterGridState(
            this.reputationHistoryGrid,
            "reputation-history");
        this.RegisterGridState(this.searchGrid, "search");

        this.BuildLayout();
        this.ConfigureHelpTour(this.ShowHelpTour);
        this.RegisterDarkScrollbarTheme(this);
        this.ApplyHistorySettings();
        this.ConfigureGrids();
        this.ConfigureInventoryToolTips();
        this.RestoreUiStateBeforeData();

        this.clientManager.PilotArchiveChanged +=
            this.ClientManager_OnPilotArchiveChanged;
        this.clientManager.SkillBuildLibraryChanged +=
            this.ClientManager_OnSkillBuildLibraryChanged;
        this.clientManager.MissionJournalChanged +=
            this.ClientManager_OnMissionJournalChanged;
        this.clientManager.ActivityJournalChanged +=
            this.ClientManager_OnActivityJournalChanged;
        this.clientManager.CombatJournalChanged +=
            this.ClientManager_OnCombatJournalChanged;

        this.searchButton.Click += this.SearchButton_OnClick;
        this.searchTextBox.KeyDown += this.SearchTextBox_OnKeyDown;
        this.pilotGrid.SelectionChanged +=
            this.PilotGrid_OnSelectionChanged;
        this.pilotGrid.CellContentClick +=
            this.PilotGrid_OnCellContentClick;
        this.searchGrid.CellDoubleClick +=
            this.SearchGrid_OnCellDoubleClick;
        this.missionHistoryGrid.SelectionChanged +=
            this.MissionHistoryGrid_OnSelectionChanged;
        this.activityHistoryGrid.SelectionChanged +=
            this.ActivityHistoryGrid_OnSelectionChanged;
        this.combatHistoryGrid.SelectionChanged +=
            this.CombatHistoryGrid_OnSelectionChanged;
        this.activityNavigationFilterCheckBox.CheckedChanged +=
            this.ActivityFilterCheckBox_OnCheckedChanged;
        this.activityMissionsFilterCheckBox.CheckedChanged +=
            this.ActivityFilterCheckBox_OnCheckedChanged;
        this.activityReputationFilterCheckBox.CheckedChanged +=
            this.ActivityFilterCheckBox_OnCheckedChanged;
        this.activityCreditsFilterCheckBox.CheckedChanged +=
            this.ActivityFilterCheckBox_OnCheckedChanged;
        this.activityLootFilterCheckBox.CheckedChanged +=
            this.ActivityFilterCheckBox_OnCheckedChanged;
        this.activityCombatFilterCheckBox.CheckedChanged +=
            this.ActivityFilterCheckBox_OnCheckedChanged;
        this.reputationsGrid.SelectionChanged +=
            this.ReputationsGrid_OnSelectionChanged;
        this.startCharacterButton.Click +=
            this.StartCharacterButton_OnClick;
        this.contentSplit.SplitterMoving +=
            this.ContentSplit_OnSplitterMoving;
        this.contentSplit.SplitterMoved +=
            this.ContentSplit_OnSplitterMoved;
        this.missionHistorySplit.SplitterMoving +=
            this.MissionHistorySplit_OnSplitterMoving;
        this.missionHistorySplit.SplitterMoved +=
            this.MissionHistorySplit_OnSplitterMoved;
        this.activityHistorySplit.SplitterMoving +=
            this.ActivityHistorySplit_OnSplitterMoving;
        this.activityHistorySplit.SplitterMoved +=
            this.ActivityHistorySplit_OnSplitterMoved;
        this.combatHistorySplit.SplitterMoving +=
            this.CombatHistorySplit_OnSplitterMoving;
        this.combatHistorySplit.SplitterMoved +=
            this.CombatHistorySplit_OnSplitterMoved;
        this.reputationHistorySplit.SplitterMoving +=
            this.ReputationHistorySplit_OnSplitterMoving;
        this.reputationHistorySplit.SplitterMoved +=
            this.ReputationHistorySplit_OnSplitterMoved;
        this.detailTabs.SelectedPageChanged +=
            this.DetailTabs_OnSelectedPageChanged;
        this.FormClosing += this.PilotArchiveForm_OnFormClosing;
        this.Shown += this.PilotArchiveForm_OnShown;

        this.launchStateTimer.Interval = 1000;
        this.launchStateTimer.Tick += this.LaunchStateTimer_OnTick;
        this.launchStateTimer.Start();

        this.RefreshPilots(this.pendingCharacterId);
    }

    public void SelectPilot(uint characterId)
    {
        this.pendingCharacterId = characterId;
        this.RefreshPilots(characterId);
    }

    internal void ApplyHistorySettings()
    {
        if (this.IsDisposed || this.Disposing)
        {
            return;
        }

        if (this.sectionTabs.TryGetValue(
                PilotArchiveSections.MissionHistory,
                out var missionHistoryTab))
        {
            var missionEnabled =
                this.clientManager.HistorySettings.RecordMissionHistory;
            this.detailTabs.SetPageVisible(
                missionHistoryTab,
                missionEnabled);

            if (!missionEnabled)
            {
                this.missionHistoryGrid.Rows.Clear();
                this.missionHistoryEntriesById.Clear();
                this.missionHistoryDetailsTextBox.Text =
                    "Select a mission to view its history.";
            }
            else if (this.selectedCharacterId is { } missionCharacterId)
            {
                this.RefreshMissionHistory(missionCharacterId);
                this.PopulateMissionHistoryTimestamp(missionCharacterId);
            }
        }

        this.ApplyActivityHistorySettings();
        this.ApplyCombatHistorySettings();
    }


    internal void ShowHelpTour()
    {
        GuidedTourOverlay.Show(
            this,
            [
                new GuidedTourStep(
                    () => this.pilotGrid,
                    "Choose an archived pilot",
                    "Client Manager keeps one lasting record per pilot. Select a pilot here, or use Start to launch the account and slot that belongs to that character."),
                new GuidedTourStep(
                    () => this.detailTabs,
                    "Review the pilot from every angle",
                    "The tabs cover overview, cargo, equipped items, vault, skills, missions, mission history, activity, combat, and reputation."),
                new GuidedTourStep(
                    () => this.cargoGrid,
                    "Snapshots are captured while you play",
                    "Docking at a station refreshes cargo, equipped items, vault contents, credits, skills, missions, reputation, location, and guild details. The latest known state remains available after the client closes.",
                    () => this.SelectArchiveSection(PilotArchiveSections.Cargo)),
                new GuidedTourStep(
                    () => this.missionHistoryGrid,
                    "Histories remember what just happened",
                    "Mission changes become a mission history. Loot, travel, credits, and other activity become an activity history. Combat records kills, damage, and encounters for later review.",
                    () => this.SelectArchiveSection(PilotArchiveSections.MissionHistory)),
                new GuidedTourStep(
                    () => this.searchTextBox,
                    "Search across every pilot",
                    "Search for an item, skill, mission, reputation, guild, or location. Double-click a result to jump directly to the matching pilot and section."),
            ]);
    }

    private void SelectArchiveSection(string section)
    {
        if (this.sectionTabs.TryGetValue(section, out var tab))
        {
            this.detailTabs.SelectedPage = tab;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.clientManager.PilotArchiveChanged -=
                this.ClientManager_OnPilotArchiveChanged;
            this.clientManager.SkillBuildLibraryChanged -=
                this.ClientManager_OnSkillBuildLibraryChanged;
            this.clientManager.MissionJournalChanged -=
                this.ClientManager_OnMissionJournalChanged;
            this.clientManager.ActivityJournalChanged -=
                this.ClientManager_OnActivityJournalChanged;
            this.clientManager.CombatJournalChanged -=
                this.ClientManager_OnCombatJournalChanged;
            this.searchButton.Click -= this.SearchButton_OnClick;
            this.searchTextBox.KeyDown -= this.SearchTextBox_OnKeyDown;
            this.pilotGrid.SelectionChanged -=
                this.PilotGrid_OnSelectionChanged;
            this.pilotGrid.CellContentClick -=
                this.PilotGrid_OnCellContentClick;
            this.searchGrid.CellDoubleClick -=
                this.SearchGrid_OnCellDoubleClick;
            this.UnconfigureInventoryToolTips();
            this.missionHistoryGrid.SelectionChanged -=
                this.MissionHistoryGrid_OnSelectionChanged;
            this.activityHistoryGrid.SelectionChanged -=
                this.ActivityHistoryGrid_OnSelectionChanged;
            this.combatHistoryGrid.SelectionChanged -=
                this.CombatHistoryGrid_OnSelectionChanged;
            this.activityNavigationFilterCheckBox.CheckedChanged -=
                this.ActivityFilterCheckBox_OnCheckedChanged;
            this.activityMissionsFilterCheckBox.CheckedChanged -=
                this.ActivityFilterCheckBox_OnCheckedChanged;
            this.activityReputationFilterCheckBox.CheckedChanged -=
                this.ActivityFilterCheckBox_OnCheckedChanged;
            this.activityCreditsFilterCheckBox.CheckedChanged -=
                this.ActivityFilterCheckBox_OnCheckedChanged;
            this.activityLootFilterCheckBox.CheckedChanged -=
                this.ActivityFilterCheckBox_OnCheckedChanged;
            this.activityCombatFilterCheckBox.CheckedChanged -=
                this.ActivityFilterCheckBox_OnCheckedChanged;
            this.reputationsGrid.SelectionChanged -=
                this.ReputationsGrid_OnSelectionChanged;
            this.startCharacterButton.Click -=
                this.StartCharacterButton_OnClick;
            this.contentSplit.SplitterMoving -=
                this.ContentSplit_OnSplitterMoving;
            this.contentSplit.SplitterMoved -=
                this.ContentSplit_OnSplitterMoved;
            this.missionHistorySplit.SplitterMoving -=
                this.MissionHistorySplit_OnSplitterMoving;
            this.missionHistorySplit.SplitterMoved -=
                this.MissionHistorySplit_OnSplitterMoved;
            this.activityHistorySplit.SplitterMoving -=
                this.ActivityHistorySplit_OnSplitterMoving;
            this.activityHistorySplit.SplitterMoved -=
                this.ActivityHistorySplit_OnSplitterMoved;
            this.combatHistorySplit.SplitterMoving -=
                this.CombatHistorySplit_OnSplitterMoving;
            this.combatHistorySplit.SplitterMoved -=
                this.CombatHistorySplit_OnSplitterMoved;
            this.reputationHistorySplit.SplitterMoving -=
                this.ReputationHistorySplit_OnSplitterMoving;
            this.reputationHistorySplit.SplitterMoved -=
                this.ReputationHistorySplit_OnSplitterMoved;
            this.detailTabs.SelectedPageChanged -=
                this.DetailTabs_OnSelectedPageChanged;
            this.FormClosing -= this.PilotArchiveForm_OnFormClosing;
            this.Shown -= this.PilotArchiveForm_OnShown;

            foreach (var grid in this.gridStateKeys.Keys)
            {
                grid.Sorted -= this.Grid_OnSorted;
            }

            foreach (var control in this.darkScrollbarThemeControls)
            {
                control.HandleCreated -=
                    this.DarkScrollbarControl_OnHandleCreated;
                control.ControlAdded -=
                    this.DarkScrollbarControl_OnControlAdded;
            }

            this.darkScrollbarThemeControls.Clear();
            this.launchStateTimer.Stop();
            this.launchStateTimer.Tick -= this.LaunchStateTimer_OnTick;
            this.launchStateTimer.Dispose();
            this.toolTip.Dispose();
            this.itemToolTip.Dispose();
            this.windowPlacement.Dispose();
        }

        base.Dispose(disposing);
    }

    private void BuildLayout()
    {
        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 82,
            Padding = new Padding(20, 12, 20, 10),
            BackColor = MainWindowTheme.Header,
        };

        var headingPanel = new Panel { Dock = DockStyle.Fill };
        headingPanel.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "Pilot Archive",
            ForeColor = MainWindowTheme.Text,
            Font = MainWindowTheme.CreateHeadingFont(18.0f),
            Location = new Point(0, 0),
        });
        headingPanel.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "Pilot details, inventories, missions, and history.",
            ForeColor = MainWindowTheme.MutedText,
            Location = new Point(1, 34),
        });
        header.Controls.Add(headingPanel);

        var searchPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 54,
            Padding = new Padding(18, 9, 18, 9),
            BackColor = MainWindowTheme.Panel,
        };
        var searchLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
        };
        searchLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        searchLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 94));
        searchLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 240));

        this.searchTextBox.Dock = DockStyle.Fill;
        this.searchTextBox.PlaceholderText =
            "Search items, skills, missions, reputations, guilds, locations...";
        MainWindowTheme.StyleTextBox(this.searchTextBox);

        this.searchButton.Text = "Search";
        this.searchButton.Dock = DockStyle.Fill;
        this.searchButton.Margin = new Padding(8, 0, 0, 0);
        MainWindowTheme.StyleButton(this.searchButton, primary: true);

        this.searchStatusLabel.Dock = DockStyle.Fill;
        this.searchStatusLabel.TextAlign = ContentAlignment.MiddleRight;
        this.searchStatusLabel.ForeColor = MainWindowTheme.MutedText;
        this.searchStatusLabel.Padding = new Padding(8, 0, 0, 0);

        searchLayout.Controls.Add(this.searchTextBox, 0, 0);
        searchLayout.Controls.Add(this.searchButton, 1, 0);
        searchLayout.Controls.Add(this.searchStatusLabel, 2, 0);
        searchPanel.Controls.Add(searchLayout);

        this.contentSplit.Dock = DockStyle.Fill;
        this.contentSplit.Orientation = Orientation.Horizontal;
        this.contentSplit.Size = new Size(1000, 600);
        this.contentSplit.FixedPanel = FixedPanel.None;
        this.contentSplit.SplitterWidth = 5;
        this.contentSplit.Panel1MinSize = 120;
        this.contentSplit.Panel2MinSize = 260;
        this.contentSplit.BackColor = MainWindowTheme.Border;
        this.contentSplit.Panel1.Padding = new Padding(12);
        this.contentSplit.Panel1.BackColor = MainWindowTheme.Panel;
        this.contentSplit.Panel2.Padding = new Padding(12);
        this.contentSplit.Panel2.BackColor = MainWindowTheme.Background;

        var rosterLabel = new Label
        {
            Text = "Archived pilots",
            Dock = DockStyle.Top,
            Height = 30,
            ForeColor = MainWindowTheme.Text,
            Font = MainWindowTheme.CreateHeadingFont(11.0f),
        };
        this.pilotGrid.Dock = DockStyle.Fill;
        this.contentSplit.Panel1.Controls.Add(this.pilotGrid);
        this.contentSplit.Panel1.Controls.Add(rosterLabel);

        this.detailTabs.Dock = DockStyle.Fill;
        this.detailTabs.Font = MainWindowTheme.CreateBodyFont(9.0f);

        var overviewTab = this.CreateOverviewTab();
        this.AddSectionTab(PilotArchiveSections.Overview, overviewTab);
        this.AddSectionTab(
            PilotArchiveSections.Cargo,
            this.CreateGridTab("Cargo", PilotArchiveSections.Cargo, this.cargoGrid));
        this.AddSectionTab(
            PilotArchiveSections.Equipment,
            this.CreateGridTab("Equipment", PilotArchiveSections.Equipment, this.equipmentGrid));
        this.AddSectionTab(
            PilotArchiveSections.Vault,
            this.CreateGridTab("Vault", PilotArchiveSections.Vault, this.vaultGrid));
        this.AddSectionTab(
            PilotArchiveSections.Skills,
            this.CreateGridTab("Skills", PilotArchiveSections.Skills, this.skillsGrid));
        this.AddSectionTab(
            PilotArchiveSections.Missions,
            this.CreateGridTab("Missions", PilotArchiveSections.Missions, this.missionsGrid));
        this.AddSectionTab(
            PilotArchiveSections.MissionHistory,
            this.CreateMissionHistoryTab());
        this.AddSectionTab(
            PilotArchiveSections.ActivityHistory,
            this.CreateActivityHistoryTab());
        this.AddSectionTab(
            PilotArchiveSections.CombatHistory,
            this.CreateCombatHistoryTab());
        this.AddSectionTab(
            PilotArchiveSections.Reputations,
            this.CreateReputationsTab());

        var searchTab = this.CreateGridTab(
            "Search results",
            "search",
            this.searchGrid);
        this.AddSectionTab("search", searchTab);
        this.sectionTimestampLabels["search"].Text =
            "Double-click a result to open its pilot and section.";

        this.contentSplit.Panel2.Controls.Add(this.detailTabs);

        this.Controls.Add(this.contentSplit);
        this.Controls.Add(searchPanel);
        this.Controls.Add(header);
    }

    private ThemedTabPage CreateOverviewTab()
    {
        var tab = CreateTabPage("Overview");
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Padding = new Padding(18),
            BackColor = MainWindowTheme.Panel,
        };

        var timestamp = CreateTimestampLabel();
        this.sectionTimestampLabels[PilotArchiveSections.Overview] = timestamp;
        timestamp.Dock = DockStyle.Top;
        timestamp.Height = 30;

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 0,
            Padding = new Padding(0, 8, 0, 12),
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        this.AddOverviewRow(table, "Name", "name");
        this.AddOverviewRow(table, "Race", "race");
        this.AddOverviewRow(table, "Profession", "profession");
        this.AddOverviewRow(table, "Levels", "levels");
        this.AddOverviewRow(table, "Hull level", "hull_level");
        this.AddOverviewRow(
            table,
            "Available skill points",
            "available_skill_points");
        this.AddOverviewRow(table, "Build", "active_build");
        this.AddOverviewRow(table, "Current location", "current_location");
        this.AddOverviewRow(table, "Registered starbase", "registration");
        this.AddOverviewRow(table, "Credits", "credits");
        this.AddOverviewRow(table, "Affiliation", "affiliation");
        this.AddOverviewRow(table, "Guild", "guild");
        this.AddOverviewRow(table, "Last observed", "last_observed");

        var launchPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 70,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 8, 0, 0),
        };
        this.startCharacterButton.Text = "Start character";
        this.startCharacterButton.Width = 142;
        this.startCharacterButton.Height = 34;
        MainWindowTheme.StyleButton(this.startCharacterButton, primary: true);
        this.launchStatusLabel.AutoSize = false;
        this.launchStatusLabel.Width = 560;
        this.launchStatusLabel.Height = 40;
        this.launchStatusLabel.Margin = new Padding(12, 0, 0, 0);
        this.launchStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
        this.launchStatusLabel.ForeColor = MainWindowTheme.MutedText;
        launchPanel.Controls.Add(this.startCharacterButton);
        launchPanel.Controls.Add(this.launchStatusLabel);

        panel.Controls.Add(launchPanel);
        panel.Controls.Add(table);
        panel.Controls.Add(timestamp);
        tab.Controls.Add(panel);
        return tab;
    }

    private ThemedTabPage CreateGridTab(
        string title,
        string section,
        DataGridView grid)
    {
        var tab = CreateTabPage(title);
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            BackColor = MainWindowTheme.Panel,
        };
        var timestamp = CreateTimestampLabel();
        this.sectionTimestampLabels[section] = timestamp;
        timestamp.Dock = DockStyle.Top;
        timestamp.Height = 30;
        grid.Dock = DockStyle.Fill;
        panel.Controls.Add(grid);
        panel.Controls.Add(timestamp);
        tab.Controls.Add(panel);
        return tab;
    }

    private ThemedTabPage CreateMissionHistoryTab()
    {
        var tab = CreateTabPage("Mission History");
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            BackColor = MainWindowTheme.Panel,
        };
        var timestamp = CreateTimestampLabel();
        this.sectionTimestampLabels[PilotArchiveSections.MissionHistory] =
            timestamp;
        timestamp.Dock = DockStyle.Top;
        timestamp.Height = 30;

        this.missionHistorySplit.Dock = DockStyle.Fill;
        this.missionHistorySplit.Orientation = Orientation.Horizontal;
        this.missionHistorySplit.Size = new Size(1000, 600);
        this.missionHistorySplit.SplitterWidth = 6;
        this.missionHistorySplit.SplitterDistance = 360;
        this.missionHistorySplit.Panel1MinSize = 160;
        this.missionHistorySplit.Panel2MinSize = 120;
        this.missionHistorySplit.BackColor = MainWindowTheme.Border;
        this.missionHistorySplit.Panel1.BackColor = MainWindowTheme.Panel;
        this.missionHistorySplit.Panel2.BackColor = MainWindowTheme.Panel;
        this.missionHistorySplit.Panel2.Padding = new Padding(0, 10, 0, 0);

        this.missionHistoryGrid.Dock = DockStyle.Fill;
        this.missionHistorySplit.Panel1.Controls.Add(
            this.missionHistoryGrid);

        var detailsHeading = new Label
        {
            Dock = DockStyle.Top,
            Height = 28,
            Text = "Mission details",
            ForeColor = MainWindowTheme.Text,
            Font = MainWindowTheme.CreateHeadingFont(9.0f),
            TextAlign = ContentAlignment.MiddleLeft,
        };
        this.missionHistoryDetailsTextBox.Dock = DockStyle.Fill;
        this.missionHistoryDetailsTextBox.Multiline = true;
        this.missionHistoryDetailsTextBox.ReadOnly = true;
        this.missionHistoryDetailsTextBox.ScrollBars = ScrollBars.Vertical;
        this.missionHistoryDetailsTextBox.WordWrap = true;
        this.missionHistoryDetailsTextBox.BackColor =
            MainWindowTheme.ElevatedPanel;
        this.missionHistoryDetailsTextBox.ForeColor = MainWindowTheme.Text;
        this.missionHistoryDetailsTextBox.BorderStyle =
            BorderStyle.FixedSingle;
        this.missionHistoryDetailsTextBox.Font =
            MainWindowTheme.CreateBodyFont();
        this.missionHistoryDetailsTextBox.Text =
            "Select a mission to view its history.";
        this.missionHistorySplit.Panel2.Controls.Add(
            this.missionHistoryDetailsTextBox);
        this.missionHistorySplit.Panel2.Controls.Add(detailsHeading);

        panel.Controls.Add(this.missionHistorySplit);
        panel.Controls.Add(timestamp);
        tab.Controls.Add(panel);
        return tab;
    }

    private void AddSectionTab(string section, ThemedTabPage tab)
    {
        this.sectionTabs[section] = tab;
        this.detailTabs.AddPage(tab);
    }

    private static ThemedTabPage CreateTabPage(string title)
    {
        return new ThemedTabPage(title);
    }

    private static Label CreateTimestampLabel()
    {
        return new Label
        {
            Text = "Not observed yet",
            ForeColor = MainWindowTheme.MutedText,
            TextAlign = ContentAlignment.MiddleLeft,
        };
    }

    private void AddOverviewRow(
        TableLayoutPanel table,
        string caption,
        string key)
    {
        var row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var captionLabel = new Label
        {
            AutoSize = true,
            Text = caption,
            ForeColor = MainWindowTheme.MutedText,
            Margin = new Padding(0, 6, 12, 6),
        };
        var valueLabel = new Label
        {
            AutoSize = true,
            Text = "—",
            ForeColor = MainWindowTheme.Text,
            Margin = new Padding(0, 6, 0, 6),
        };
        this.overviewValues[key] = valueLabel;
        table.Controls.Add(captionLabel, 0, row);
        table.Controls.Add(valueLabel, 1, row);
    }

    private void RegisterGridState(
        DataGridView grid,
        string stateKey)
    {
        this.gridStateKeys.Add(grid, stateKey);
        grid.Sorted += this.Grid_OnSorted;
    }

    private void RestoreUiStateBeforeData()
    {
        var settings = this.clientManager.PilotArchiveSettings;
        this.pendingCharacterId = settings.SelectedCharacterId;

        if (this.sectionTabs.TryGetValue(
                settings.SelectedSection,
                out var selectedTab))
        {
            this.detailTabs.SelectedPage = selectedTab;
        }
    }

    private void RestoreUiStateAfterShown()
    {
        var settings = this.clientManager.PilotArchiveSettings;
        this.restoringUiState = true;

        try
        {
            if (settings.RosterSplitterDistance.HasValue)
            {
                SetSplitterDistance(
                    this.contentSplit,
                    settings.RosterSplitterDistance.Value);
            }
            else
            {
                this.UpdateRosterHeight();
                settings.RosterSplitterDistance =
                    this.contentSplit.SplitterDistance;
            }

            if (settings.MissionHistorySplitterDistance.HasValue)
            {
                SetSplitterDistance(
                    this.missionHistorySplit,
                    settings.MissionHistorySplitterDistance.Value);
            }
            else
            {
                settings.MissionHistorySplitterDistance =
                    this.missionHistorySplit.SplitterDistance;
            }

            this.RestoreActivityHistoryUiState(settings);
            this.RestoreCombatHistoryUiState(settings);
            this.RestoreReputationHistoryUiState(settings);
        }
        finally
        {
            this.restoringUiState = false;
        }
    }

    private static void SetSplitterDistance(
        SplitContainer split,
        int requestedDistance)
    {
        var available = split.Orientation == Orientation.Horizontal
            ? split.ClientSize.Height
            : split.ClientSize.Width;
        var maximum = available - split.Panel2MinSize -
            split.SplitterWidth;

        if (maximum < split.Panel1MinSize)
        {
            return;
        }

        split.SplitterDistance = Math.Clamp(
            requestedDistance,
            split.Panel1MinSize,
            maximum);
    }

    private void SaveUiState()
    {
        var settings = this.clientManager.PilotArchiveSettings;
        settings.SelectedCharacterId = this.selectedCharacterId;
        settings.SelectedSection = this.GetSelectedSection() ??
            PilotArchiveSections.Overview;

        if (!settings.RosterSplitterDistance.HasValue &&
            this.contentSplit.ClientSize.Height > 0)
        {
            settings.RosterSplitterDistance =
                this.contentSplit.SplitterDistance;
        }

        if (!settings.MissionHistorySplitterDistance.HasValue &&
            this.missionHistorySplit.ClientSize.Height > 0)
        {
            settings.MissionHistorySplitterDistance =
                this.missionHistorySplit.SplitterDistance;
        }

        this.SaveActivityHistoryUiState(settings);
        this.SaveCombatHistoryUiState(settings);
        this.SaveReputationHistoryUiState(settings);

        foreach (var grid in this.gridStateKeys.Keys)
        {
            this.CaptureGridSort(grid);
        }

        this.clientManager.SaveSettings();
    }

    private string? GetSelectedSection()
    {
        return this.sectionTabs
            .FirstOrDefault(entry => ReferenceEquals(
                entry.Value,
                this.detailTabs.SelectedPage))
            .Key;
    }

    private void CaptureGridSort(DataGridView grid)
    {
        if (!this.gridStateKeys.TryGetValue(grid, out var stateKey))
        {
            return;
        }

        var settings = this.clientManager.PilotArchiveSettings.GridSorts;
        var sortedColumn = grid.SortedColumn;

        if (sortedColumn == null || grid.SortOrder == SortOrder.None)
        {
            settings.Remove(stateKey);
            return;
        }

        settings[stateKey] = new PilotArchiveGridSortSettings
        {
            ColumnIndex = sortedColumn.Index,
            Descending = grid.SortOrder == SortOrder.Descending,
        };
    }

    private void ApplyStoredGridSort(DataGridView grid)
    {
        if (!this.gridStateKeys.TryGetValue(grid, out var stateKey) ||
            !this.clientManager.PilotArchiveSettings.GridSorts.TryGetValue(
                stateKey,
                out var sort) ||
            sort.ColumnIndex < 0 ||
            sort.ColumnIndex >= grid.Columns.Count)
        {
            return;
        }

        var column = grid.Columns[sort.ColumnIndex];

        if (column.SortMode == DataGridViewColumnSortMode.NotSortable)
        {
            return;
        }

        this.restoringUiState = true;

        try
        {
            grid.Sort(
                column,
                sort.Descending
                    ? ListSortDirection.Descending
                    : ListSortDirection.Ascending);
        }
        catch (InvalidOperationException)
        {
            this.clientManager.PilotArchiveSettings.GridSorts.Remove(
                stateKey);
        }
        finally
        {
            this.restoringUiState = false;
        }
    }

    private void ConfigureInventoryToolTips()
    {
        foreach (var grid in new[]
                 {
                     this.cargoGrid,
                     this.equipmentGrid,
                     this.vaultGrid,
                 })
        {
            grid.ShowCellToolTips = false;
            grid.CellMouseEnter +=
                this.InventoryGrid_OnCellMouseEnter;
            grid.CellMouseLeave +=
                this.InventoryGrid_OnCellMouseLeave;
            grid.MouseLeave +=
                this.InventoryGrid_OnMouseLeave;
            grid.Scroll +=
                this.InventoryGrid_OnScroll;
        }
    }

    private void UnconfigureInventoryToolTips()
    {
        foreach (var grid in new[]
                 {
                     this.cargoGrid,
                     this.equipmentGrid,
                     this.vaultGrid,
                 })
        {
            grid.CellMouseEnter -=
                this.InventoryGrid_OnCellMouseEnter;
            grid.CellMouseLeave -=
                this.InventoryGrid_OnCellMouseLeave;
            grid.MouseLeave -=
                this.InventoryGrid_OnMouseLeave;
            grid.Scroll -=
                this.InventoryGrid_OnScroll;
            this.itemToolTip.Remove(grid);
        }
    }

    private void InventoryGrid_OnCellMouseEnter(
        object? sender,
        DataGridViewCellEventArgs e)
    {
        if (sender is not DataGridView grid)
        {
            return;
        }

        if (e.RowIndex < 0 ||
            e.ColumnIndex < 0 ||
            grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Tag is not
                PilotArchiveItemCellContext context)
        {
            this.itemToolTip.Remove(grid);
            return;
        }

        ClientRuntimeItemTemplateObservation? template = null;

        if (context.Slot.TemplateId is int itemTemplateId &&
            itemTemplateId > 0)
        {
            template = this.clientManager.PilotArchive.GetItemTemplate(
                itemTemplateId);

            if (template == null &&
                ClientItemTemplateCatalog.TryGetRuntimeObservation(
                    itemTemplateId,
                    out var staticTemplate))
            {
                template = staticTemplate;
            }
        }

        var icon = this.clientManager.GetPilotArchiveItemIcon(
            context.IconOutputDirectory,
            context.Slot.TemplateId,
            new Size(36, 36));
        var content = PilotArchiveItemToolTipBuilder.Build(
            context.Slot,
            template,
            icon);

        this.itemToolTip.SetHoveredToolTip(
            grid,
            content,
            SystemInformation.MouseHoverTime);
    }

    private void InventoryGrid_OnCellMouseLeave(
        object? sender,
        DataGridViewCellEventArgs e)
    {
        if (sender is DataGridView grid)
        {
            this.itemToolTip.Remove(grid);
        }
    }

    private void InventoryGrid_OnMouseLeave(
        object? sender,
        EventArgs e)
    {
        if (sender is DataGridView grid)
        {
            this.itemToolTip.Remove(grid);
        }
    }

    private void InventoryGrid_OnScroll(
        object? sender,
        ScrollEventArgs e)
    {
        if (sender is DataGridView grid)
        {
            this.itemToolTip.Remove(grid);
        }
    }

    private void ConfigureGrids()
    {
        ConfigurePilotGrid(this.pilotGrid);
        ConfigureSlotGrid(this.cargoGrid);
        ConfigureSlotGrid(this.equipmentGrid);
        ConfigureSlotGrid(this.vaultGrid);

        AddColumn(this.skillsGrid, "Category", 150);
        AddIconColumn(this.skillsGrid);
        AddColumn(this.skillsGrid, "Skill", 260);
        AddColumn(this.skillsGrid, "Rank", 90);
        AddColumn(this.skillsGrid, "Active", 80);
        AddColumn(this.skillsGrid, "Spent points", 110);

        AddColumn(this.missionsGrid, "Mission", 260);
        AddColumn(this.missionsGrid, "Stage", 90);
        AddColumn(this.missionsGrid, "Current objective", 380, fill: true);
        AddColumn(this.missionsGrid, "Faction", 180);

        AddColumn(this.missionHistoryGrid, "Accepted / first seen", 165);
        AddColumn(this.missionHistoryGrid, "Mission", 250);
        AddColumn(this.missionHistoryGrid, "Type", 120);
        AddColumn(this.missionHistoryGrid, "Status", 120);
        AddColumn(
            this.missionHistoryGrid,
            "Latest objective",
            360,
            fill: true);
        AddColumn(this.missionHistoryGrid, "Ended / last seen", 145);
        AddColumn(this.missionHistoryGrid, "Duration", 90);

        this.ConfigureActivityHistoryGrid();
        this.ConfigureCombatHistoryGrid();
        this.ConfigureReputationHistoryGrid();

        AddColumn(this.reputationsGrid, "Faction", 260);
        AddColumn(this.reputationsGrid, "Reputation", 120);
        AddColumn(this.reputationsGrid, "Description", 420, fill: true);

        AddColumn(this.searchGrid, "Pilot", 180);
        AddColumn(this.searchGrid, "Category", 130);
        AddIconColumn(this.searchGrid);
        AddColumn(this.searchGrid, "Result", 600, fill: true);
    }

    private static void ConfigurePilotGrid(DataGridView grid)
    {
        AddColumn(grid, "Name", 150);
        AddColumn(grid, "Race", 90);
        AddColumn(grid, "Profession", 160);
        AddColumn(grid, "Class", 80);
        AddColumn(grid, "Overall", 66);
        AddColumn(grid, "Combat", 66);
        AddColumn(grid, "Explore", 66);
        AddColumn(grid, "Trade", 66);
        AddColumn(grid, "Credits", 110);
        AddColumn(grid, "Guild", 180);
        AddColumn(grid, "Current location", 240, fill: true);
        AddColumn(grid, "Registered starbase", 210);
        AddColumn(grid, "Last observed", 132);
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = PilotBuildColumnName,
            HeaderText = "",
            Width = 82,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            SortMode = DataGridViewColumnSortMode.NotSortable,
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = PilotLaunchColumnName,
            HeaderText = "",
            Width = 82,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            SortMode = DataGridViewColumnSortMode.NotSortable,
        });
    }

    private static void ConfigureSlotGrid(DataGridView grid)
    {
        AddColumn(grid, "Collection", 110);
        AddColumn(grid, "Slot", 70);
        AddColumn(grid, "Kind", 110);
        AddColumn(grid, "State", 100);
        AddIconColumn(grid);
        AddColumn(grid, "Item", 280, fill: true);
        AddColumn(grid, "Qty", 70);
        AddColumn(grid, "Quality", 90);
        AddColumn(grid, "Structure", 90);
        AddColumn(grid, "Builder", 150);
    }

    private sealed class DarkScrollbarDataGridView : DataGridView
    {
        private readonly Panel? scrollbarCorner;

        public DarkScrollbarDataGridView()
        {
            var corner = new Panel
            {
                BackColor = MainWindowTheme.Panel,
                Enabled = false,
                TabStop = false,
                Visible = false,
            };
            this.scrollbarCorner = corner;
            this.Controls.Add(corner);
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            this.UpdateScrollbarCorner();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            this.UpdateScrollbarCorner();
        }

        protected override void OnScroll(ScrollEventArgs e)
        {
            base.OnScroll(e);
            this.UpdateScrollbarCorner();
        }

        private void UpdateScrollbarCorner()
        {
            var corner = this.scrollbarCorner;
            if (corner == null)
            {
                return;
            }

            var horizontal = this.Controls
                .OfType<HScrollBar>()
                .FirstOrDefault();
            var vertical = this.Controls
                .OfType<VScrollBar>()
                .FirstOrDefault();

            if (horizontal is not { Visible: true } ||
                vertical is not { Visible: true })
            {
                corner.Visible = false;
                return;
            }

            var cornerBounds = Rectangle.Intersect(
                this.ClientRectangle,
                new Rectangle(
                    vertical.Left,
                    horizontal.Top,
                    vertical.Width,
                    horizontal.Height));

            if (cornerBounds.Width <= 0 || cornerBounds.Height <= 0)
            {
                corner.Visible = false;
                return;
            }

            corner.Bounds = cornerBounds;
            corner.Visible = true;
            corner.BringToFront();
        }
    }

    private static DataGridView CreateGrid()
    {
        var grid = new DarkScrollbarDataGridView
        {
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AutoGenerateColumns = false,
            BackgroundColor = MainWindowTheme.Panel,
            BorderStyle = BorderStyle.None,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single,
            ColumnHeadersHeight = 34,
            EnableHeadersVisualStyles = false,
            GridColor = MainWindowTheme.Border,
            MultiSelect = false,
            ReadOnly = true,
            RowHeadersVisible = false,
            RowTemplate = { Height = 30 },
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            ShowCellToolTips = true,
            DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = MainWindowTheme.Panel,
                ForeColor = MainWindowTheme.Text,
                SelectionBackColor = MainWindowTheme.ButtonHover,
                SelectionForeColor = MainWindowTheme.Text,
                NullValue = "",
            },
            AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = MainWindowTheme.ElevatedPanel,
                ForeColor = MainWindowTheme.Text,
                SelectionBackColor = MainWindowTheme.ButtonHover,
                SelectionForeColor = MainWindowTheme.Text,
            },
            ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = MainWindowTheme.Header,
                ForeColor = MainWindowTheme.Text,
                SelectionBackColor = MainWindowTheme.Header,
                SelectionForeColor = MainWindowTheme.Text,
                Font = MainWindowTheme.CreateHeadingFont(9.0f),
            },
        };

        grid.SortCompare += Grid_OnSortCompare;
        return grid;
    }

    private static void AddIconColumn(DataGridView grid)
    {
        grid.Columns.Add(new DataGridViewImageColumn
        {
            HeaderText = "",
            Width = ArchiveIconColumnWidth,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            ImageLayout = DataGridViewImageCellLayout.Zoom,
            DefaultCellStyle = new DataGridViewCellStyle
            {
                Alignment = DataGridViewContentAlignment.MiddleCenter,
                NullValue = null,
                Padding = new Padding(4, 2, 4, 2),
            },
        });
    }

    private static void AddColumn(
        DataGridView grid,
        string title,
        int width,
        bool fill = false)
    {
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = title,
            Width = width,
            AutoSizeMode = fill
                ? DataGridViewAutoSizeColumnMode.Fill
                : DataGridViewAutoSizeColumnMode.None,
            SortMode = DataGridViewColumnSortMode.Automatic,
        });
    }

    private static void Grid_OnSortCompare(
        object? sender,
        DataGridViewSortCompareEventArgs e)
    {
        e.SortResult = CompareGridValues(
            e.CellValue1,
            e.CellValue2);

        if (e.SortResult == 0)
        {
            e.SortResult = e.RowIndex1.CompareTo(e.RowIndex2);
        }

        e.Handled = true;
    }

    private static int CompareGridValues(
        object? left,
        object? right)
    {
        if (ReferenceEquals(left, right))
        {
            return 0;
        }

        if (left == null || left == DBNull.Value)
        {
            return -1;
        }

        if (right == null || right == DBNull.Value)
        {
            return 1;
        }

        if (left is not string &&
            left is IComparable comparable &&
            left.GetType() == right.GetType())
        {
            return comparable.CompareTo(right);
        }

        var leftText = Convert.ToString(
            left,
            CultureInfo.CurrentCulture) ?? "";
        var rightText = Convert.ToString(
            right,
            CultureInfo.CurrentCulture) ?? "";

        if (TryParseRank(leftText, out var leftRank) &&
            TryParseRank(rightText, out var rightRank))
        {
            var currentRankComparison =
                leftRank.Current.CompareTo(rightRank.Current);

            return currentRankComparison != 0
                ? currentRankComparison
                : leftRank.Maximum.CompareTo(rightRank.Maximum);
        }

        if (TryParseGridNumber(leftText, out var leftNumber) &&
            TryParseGridNumber(rightText, out var rightNumber))
        {
            return leftNumber.CompareTo(rightNumber);
        }

        if (DateTime.TryParse(
                leftText,
                CultureInfo.CurrentCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var leftDate) &&
            DateTime.TryParse(
                rightText,
                CultureInfo.CurrentCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var rightDate))
        {
            return leftDate.CompareTo(rightDate);
        }

        return StringComparer.CurrentCultureIgnoreCase.Compare(
            leftText,
            rightText);
    }

    private static bool TryParseRank(
        string value,
        out (int Current, int Maximum) rank)
    {
        rank = default;
        var parts = value.Split(
            '/',
            StringSplitOptions.TrimEntries);

        if (parts.Length != 2 ||
            !int.TryParse(
                parts[0],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var current) ||
            !int.TryParse(
                parts[1],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maximum))
        {
            return false;
        }

        rank = (current, maximum);
        return true;
    }

    private static bool TryParseGridNumber(
        string value,
        out decimal number)
    {
        value = value.Trim();

        if (value.EndsWith('%'))
        {
            value = value[..^1].TrimEnd();
        }

        return decimal.TryParse(
            value,
            NumberStyles.Number,
            CultureInfo.CurrentCulture,
            out number);
    }

    private void RefreshPilots(uint? preferredCharacterId = null)
    {
        if (this.IsDisposed || this.Disposing)
        {
            return;
        }

        var pilots = this.clientManager.PilotArchive.GetPilots();
        var desired = preferredCharacterId ??
            this.pendingCharacterId ??
            this.selectedCharacterId;

        this.refreshingPilotList = true;
        this.pilotGrid.Rows.Clear();

        foreach (var pilot in pilots)
        {
            var identity = PilotArchiveIdentityPresentation.Resolve(
                pilot.Race,
                pilot.Profession);
            var row = this.pilotGrid.Rows[this.pilotGrid.Rows.Add(
                pilot.Name,
                identity.Race ?? "",
                identity.Profession ?? "",
                pilot.ProfessionCode ?? "",
                pilot.OverallLevel,
                pilot.CombatLevel,
                pilot.ExploreLevel,
                pilot.TradeLevel,
                pilot.Credits?.ToString("N0", CultureInfo.CurrentCulture) ?? "",
                FormatGuild(pilot.GuildName, pilot.GuildRank),
                FormatLocation(
                    pilot.CurrentSystem,
                    pilot.CurrentSector,
                    pilot.CurrentStarbase),
                FormatLocation(
                    pilot.RegistrationSector,
                    pilot.RegistrationStarbase),
                FormatObservedAt(pilot.LastObservedAt),
                "",
                "")];
            row.Tag = pilot;
        }

        this.ApplyStoredGridSort(this.pilotGrid);
        this.RefreshPilotBuildCells();
        this.RefreshPilotLaunchCells();

        var selectedRow = this.pilotGrid.Rows
            .Cast<DataGridViewRow>()
            .FirstOrDefault(row =>
                row.Tag is PilotArchivePilotSnapshot pilot &&
                desired.HasValue &&
                pilot.CharacterId == desired.Value)
            ?? this.pilotGrid.Rows
                .Cast<DataGridViewRow>()
                .FirstOrDefault();

        this.pilotGrid.ClearSelection();

        if (selectedRow != null)
        {
            selectedRow.Selected = true;
            this.pilotGrid.CurrentCell = selectedRow.Cells[0];
        }

        this.refreshingPilotList = false;
        this.pendingCharacterId = null;

        if (selectedRow == null)
        {
            this.selectedCharacterId = null;
            this.ClearDetails();
        }
        else
        {
            this.LoadSelectedPilot();
        }
    }

    private void LoadSelectedPilot()
    {
        if (this.pilotGrid.CurrentRow?.Tag is not
            PilotArchivePilotSnapshot selectedPilot)
        {
            this.ClearDetails();
            return;
        }

        var details = this.clientManager.PilotArchive.GetPilot(
            selectedPilot.CharacterId);

        if (details == null)
        {
            this.ClearDetails();
            return;
        }

        this.selectedCharacterId = details.Pilot.CharacterId;
        this.clientManager.PilotArchiveSettings.SelectedCharacterId =
            this.selectedCharacterId;
        this.PopulateOverview(details);
        var iconOutputDirectory =
            this.clientManager.LocateGameOutputDirectory();
        this.PopulateSlotGrid(
            this.cargoGrid,
            details.CargoSlots,
            iconOutputDirectory);
        this.PopulateEquipmentGrid(
            this.equipmentGrid,
            details.EquipmentSlots,
            details.AmmoSlots,
            iconOutputDirectory);
        this.PopulateSlotGrid(
            this.vaultGrid,
            details.VaultSlots,
            iconOutputDirectory);
        this.PopulateSkills(
            this.skillsGrid,
            details.Skills,
            iconOutputDirectory);
        PopulateMissions(this.missionsGrid, details.Missions);
        this.ApplyStoredGridSort(this.missionsGrid);

        if (this.clientManager.HistorySettings.RecordMissionHistory)
        {
            this.RefreshMissionHistory(details.Pilot.CharacterId);
        }

        if (this.clientManager.HistorySettings.RecordActivityHistory)
        {
            this.RefreshActivityHistory(details.Pilot.CharacterId);
        }

        if (this.clientManager.HistorySettings.RecordCombatHistory)
        {
            this.RefreshCombatHistory(details.Pilot.CharacterId);
        }

        this.PopulateReputations(details.Reputations);
        this.PopulateSectionTimestamps(details);

        if (this.clientManager.HistorySettings.RecordMissionHistory)
        {
            this.PopulateMissionHistoryTimestamp(
                details.Pilot.CharacterId);
        }

        if (this.clientManager.HistorySettings.RecordActivityHistory)
        {
            this.PopulateActivityHistoryTimestamp(
                details.Pilot.CharacterId);
        }

        if (this.clientManager.HistorySettings.RecordCombatHistory)
        {
            this.PopulateCombatHistoryTimestamp(
                details.Pilot.CharacterId);
        }

        this.RefreshLaunchState(details.Pilot.CharacterId);
    }

    private void PopulateOverview(PilotArchivePilotDetails details)
    {
        var pilot = details.Pilot;
        var identity = PilotArchiveIdentityPresentation.Resolve(
            pilot.Race,
            pilot.Profession);
        this.SetOverview("name", pilot.Name);
        this.SetOverview("race", identity.Race);
        this.SetOverview(
            "profession",
            string.Join(
                " · ",
                new[] { identity.Profession, pilot.ProfessionCode }
                    .Where(value => !string.IsNullOrWhiteSpace(value))));
        this.SetOverview(
            "levels",
            string.Join(
                " · ",
                string.Concat(
                    "Overall ",
                    FormatNullable(pilot.OverallLevel)),
                string.Concat(
                    "Combat ",
                    FormatNullable(pilot.CombatLevel)),
                string.Concat(
                    "Explore ",
                    FormatNullable(pilot.ExploreLevel)),
                string.Concat(
                    "Trade ",
                    FormatNullable(pilot.TradeLevel))));
        this.SetOverview("hull_level", FormatNullable(pilot.HullTier));
        this.SetOverview(
            "available_skill_points",
            FormatNullable(pilot.AvailableSkillPoints));
        this.SetOverview(
            "active_build",
            this.clientManager.GetPilotArchiveActiveBuildDisplayName(
                pilot.CharacterId));
        this.SetOverview(
            "current_location",
            string.Join(
                " · ",
                new[] { pilot.CurrentSystem, pilot.CurrentSector, pilot.CurrentStarbase }
                    .Where(value => !string.IsNullOrWhiteSpace(value))));
        this.SetOverview(
            "registration",
            string.Join(
                " · ",
                new[] { pilot.RegistrationSector, pilot.RegistrationStarbase }
                    .Where(value => !string.IsNullOrWhiteSpace(value))));
        this.SetOverview(
            "credits",
            pilot.Credits?.ToString("N0", CultureInfo.CurrentCulture));
        this.SetOverview("affiliation", pilot.Affiliation);
        this.SetOverview(
            "guild",
            FormatGuild(pilot.GuildName, pilot.GuildRank));
        this.SetOverview("last_observed", FormatObservedAt(pilot.LastObservedAt));
    }

    private void PopulateSectionTimestamps(PilotArchivePilotDetails details)
    {
        foreach (var entry in this.sectionTimestampLabels)
        {
            if (entry.Key == "search")
            {
                continue;
            }

            if (entry.Key == PilotArchiveSections.Overview)
            {
                var overviewTimes = new[]
                    {
                        PilotArchiveSections.Overview,
                        PilotArchiveSections.Location,
                        PilotArchiveSections.Progression,
                    }
                    .Where(details.SectionObservedAt.ContainsKey)
                    .Select(section => details.SectionObservedAt[section])
                    .ToArray();
                entry.Value.Text = overviewTimes.Length == 0
                    ? "Not observed yet"
                    : string.Concat(
                        "Last observed: ",
                        FormatObservedAt(overviewTimes.Min()));
                continue;
            }

            entry.Value.Text = details.SectionObservedAt.TryGetValue(
                entry.Key,
                out var timestamp)
                ? string.Concat("Last observed: ", FormatObservedAt(timestamp))
                : "Not observed yet";
        }
    }

    private void RefreshMissionHistory(uint characterId)
    {
        var missionHistory = this.clientManager.GetMissionHistory(characterId);
        this.PopulateMissionHistory(
            this.missionHistoryGrid,
            missionHistory);
    }

    private void PopulateMissionHistoryTimestamp(uint characterId)
    {
        if (!this.sectionTimestampLabels.TryGetValue(
                PilotArchiveSections.MissionHistory,
                out var label))
        {
            return;
        }

        var observedAt =
            this.clientManager.GetMissionHistoryLastObservedAt(characterId);
        label.Text = observedAt.HasValue
            ? string.Concat(
                "Last updated: ",
                FormatObservedAt(observedAt.Value))
            : "No mission history recorded yet";
    }

    private void PopulateSlotGrid(
        DataGridView grid,
        IReadOnlyList<AddonInventorySlotSnapshot> slots,
        string? iconOutputDirectory)
    {
        this.itemToolTip.Remove(grid);
        grid.Rows.Clear();

        foreach (var slot in slots.OrderBy(slot => slot.Slot))
        {
            var row = grid.Rows[grid.Rows.Add(
                DisplayCollection(slot.Collection),
                slot.Slot + 1,
                FormatEquipmentKind(slot),
                DisplayState(slot.State),
                this.clientManager.GetPilotArchiveItemIcon(
                    iconOutputDirectory,
                    slot.TemplateId,
                    ArchiveIconSize),
                slot.Name ?? "",
                slot.IsOccupied ? slot.StackCount.GetValueOrDefault(1) : null,
                FormatPercent(slot.QualityPercent),
                FormatPercent(slot.StructurePercent),
                slot.BuilderName ?? "")];
            row.Tag = string.Create(
                CultureInfo.InvariantCulture,
                $"{slot.Collection}:{slot.Slot}");
            row.DefaultCellStyle.ForeColor = slot.State switch
            {
                "occupied" => MainWindowTheme.Text,
                "empty" => MainWindowTheme.MutedText,
                _ => MainWindowTheme.DisabledText,
            };

            if (slot.IsOccupied)
            {
                var context = new PilotArchiveItemCellContext(
                    slot,
                    iconOutputDirectory);
                row.Cells[4].Tag = context;
                row.Cells[5].Tag = context;
            }
        }

        this.ApplyStoredGridSort(grid);
    }

    private void PopulateEquipmentGrid(
        DataGridView grid,
        IReadOnlyList<AddonInventorySlotSnapshot> equipment,
        IReadOnlyList<AddonInventorySlotSnapshot> ammo,
        string? iconOutputDirectory)
    {
        var equipmentBySlot = equipment
            .ToDictionary(slot => slot.Slot);
        var visibleAmmo = ammo
            .Where(slot =>
                string.Equals(
                    slot.EquipmentKind,
                    "weapon",
                    StringComparison.Ordinal) &&
                (slot.IsOccupied ||
                 (!string.Equals(
                      slot.State,
                      "unavailable",
                      StringComparison.Ordinal) &&
                  equipmentBySlot.TryGetValue(slot.Slot, out var item) &&
                  item.IsOccupied)));
        var combined = equipment
            .Where(slot =>
                !string.Equals(
                    slot.EquipmentKind,
                    "reserved",
                    StringComparison.Ordinal))
            .Concat(visibleAmmo)
            .OrderBy(slot => slot.Slot)
            .ThenBy(slot => slot.Collection == "equipped" ? 0 : 1)
            .ToArray();
        this.PopulateSlotGrid(
            grid,
            combined,
            iconOutputDirectory);
    }

    private void PopulateSkills(
        DataGridView grid,
        IReadOnlyList<PilotArchiveSkill> skills,
        string? iconOutputDirectory)
    {
        grid.Rows.Clear();

        foreach (var skill in skills.Where(skill =>
                     skill.CurrentRank != 0 ||
                     skill.MaximumRank != 0))
        {
            var row = grid.Rows[grid.Rows.Add(
                skill.Category,
                this.clientManager.GetPilotArchiveSkillIcon(
                    iconOutputDirectory,
                    skill,
                    ArchiveIconSize),
                skill.Name,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{skill.CurrentRank}/{skill.MaximumRank}"),
                skill.IsActive ? "Yes" : "No",
                skill.SpentSkillPoints)];
            row.Tag = skill.Index.ToString(CultureInfo.InvariantCulture);
            row.DefaultCellStyle.ForeColor = skill.CurrentRank > 0
                ? MainWindowTheme.Text
                : MainWindowTheme.MutedText;
        }

        this.ApplyStoredGridSort(grid);
    }

    private static void PopulateMissions(
        DataGridView grid,
        IReadOnlyList<PilotArchiveMission> missions)
    {
        grid.Rows.Clear();

        foreach (var mission in missions)
        {
            var stage = mission.Stage.HasValue && mission.StageCount.HasValue
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"{mission.Stage}/{mission.StageCount}")
                : "";
            var row = grid.Rows[grid.Rows.Add(
                mission.Name,
                stage,
                mission.CurrentStageText,
                mission.IssuingFaction)];
            row.Tag = mission.Slot.ToString(CultureInfo.InvariantCulture);
            row.Cells[0].ToolTipText = mission.Summary;
            row.Cells[2].ToolTipText = string.Join(Environment.NewLine, mission.Stages);
        }
    }

    private void PopulateMissionHistory(
        DataGridView grid,
        IReadOnlyList<MissionJournalEntry> entries)
    {
        var selectedEpisodeId = grid.SelectedRows
            .Cast<DataGridViewRow>()
            .Select(row => row.Tag as string)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        grid.Rows.Clear();
        this.missionHistoryEntriesById.Clear();

        foreach (var entry in entries)
        {
            this.missionHistoryEntriesById[entry.EpisodeId] = entry;
            var row = grid.Rows[grid.Rows.Add(
                FormatObservedAt(entry.DisplayStartedAt),
                entry.Name,
                entry.TypeDisplay,
                entry.StatusDisplay,
                entry.CurrentObjective,
                entry.Status == MissionJournalStatus.NoLongerActive
                    ? FormatObservedAt(entry.LastObservedAt)
                    : entry.EndedAt.HasValue
                        ? FormatObservedAt(entry.EndedAt.Value)
                        : "",
                entry.DisplayDuration)];
            row.Tag = entry.EpisodeId;
            var tooltip = BuildMissionHistoryDetails(entry);
            row.Cells[1].ToolTipText = tooltip;
            row.Cells[4].ToolTipText = tooltip;
            row.DefaultCellStyle.ForeColor = entry.Status switch
            {
                MissionJournalStatus.Active => MainWindowTheme.Accent,
                MissionJournalStatus.Completed => MainWindowTheme.Success,
                MissionJournalStatus.Forfeited => MainWindowTheme.Warning,
                MissionJournalStatus.Failed => MainWindowTheme.Danger,
                MissionJournalStatus.Expired => MainWindowTheme.Danger,
                _ => MainWindowTheme.MutedText,
            };
        }

        this.ApplyStoredGridSort(grid);

        if (grid.Rows.Count == 0)
        {
            this.missionHistoryDetailsTextBox.Text =
                "No mission history recorded for this pilot yet.";
            return;
        }

        grid.ClearSelection();
        var selectedRow = grid.Rows
            .Cast<DataGridViewRow>()
            .FirstOrDefault(row => string.Equals(
                row.Tag as string,
                selectedEpisodeId,
                StringComparison.Ordinal)) ??
            grid.Rows[0];
        selectedRow.Selected = true;
        grid.CurrentCell = selectedRow.Cells[0];
        this.UpdateMissionHistoryDetails();
    }

    private static string GetMissionEndLabel(
        MissionJournalStatus status) => status switch
        {
            MissionJournalStatus.Completed => "Completed",
            MissionJournalStatus.Forfeited => "Forfeited",
            MissionJournalStatus.Failed => "Failed",
            MissionJournalStatus.Expired => "Expired",
            _ => "Ended",
        };

    private static string FormatMissionHistoryEvent(
        MissionJournalEventKind kind) => kind switch
        {
            MissionJournalEventKind.Observed => "First observed",
            MissionJournalEventKind.Accepted => "Accepted",
            MissionJournalEventKind.SourceIdentified => "Mission type updated",
            MissionJournalEventKind.Progressed => "Progress updated",
            MissionJournalEventKind.Completed => "Completed",
            MissionJournalEventKind.Forfeited => "Forfeited",
            MissionJournalEventKind.Failed => "Failed",
            MissionJournalEventKind.Expired => "Expired",
            MissionJournalEventKind.NoLongerActive => "No longer active",
            _ => kind.ToString(),
        };

    private static string BuildMissionHistoryDetails(
        MissionJournalEntry entry)
    {
        List<string> lines =
        [
            entry.Name,
            string.Concat(entry.TypeDisplay, " · ", entry.StatusDisplay),
            "",
            string.Concat(
                entry.AcceptedAt.HasValue
                    ? "Accepted: "
                    : "First seen: ",
                FormatObservedAt(entry.DisplayStartedAt)),
        ];

        if (entry.Status == MissionJournalStatus.NoLongerActive)
        {
            lines.Add(string.Concat(
                "Last seen: ",
                FormatObservedAt(entry.LastObservedAt)));

            if (entry.EndedAt.HasValue)
            {
                lines.Add(string.Concat(
                    "No longer active: ",
                    FormatObservedAt(entry.EndedAt.Value)));
            }
        }
        else
        {
            if (entry.EndedAt.HasValue)
            {
                lines.Add(string.Concat(
                    GetMissionEndLabel(entry.Status),
                    ": ",
                    FormatObservedAt(entry.EndedAt.Value)));
            }

            if (entry.Duration.HasValue)
            {
                lines.Add(string.Concat(
                    "Duration: ",
                    entry.DisplayDuration));
            }
        }

        var completionLocation = FormatLocation(
            entry.CompletionSystem,
            entry.CompletionSector,
            entry.CompletionStarbase);

        if (!string.IsNullOrWhiteSpace(completionLocation))
        {
            lines.Add(string.Concat(
                GetMissionEndLabel(entry.Status),
                " at: ",
                completionLocation));
        }

        var acceptedLocation = FormatLocation(
            entry.AcceptedSystem,
            entry.AcceptedSector,
            entry.AcceptedStarbase);

        if (!string.IsNullOrWhiteSpace(acceptedLocation))
        {
            lines.Add(string.Concat("Accepted at: ", acceptedLocation));
        }

        if (!string.IsNullOrWhiteSpace(entry.IssuerNpcName))
        {
            lines.Add(string.Concat("Issuer: ", entry.IssuerNpcName));
        }

        if (!string.IsNullOrWhiteSpace(entry.IssuingFaction))
        {
            lines.Add(string.Concat(
                "Issuing faction: ",
                entry.IssuingFaction));
        }

        if (!string.IsNullOrWhiteSpace(entry.CurrentObjective))
        {
            lines.Add("");
            lines.Add("Latest objective");
            lines.Add(entry.CurrentObjective);
        }

        if (!string.IsNullOrWhiteSpace(entry.Summary) &&
            !string.Equals(
                entry.Summary,
                entry.CurrentObjective,
                StringComparison.Ordinal))
        {
            lines.Add("");
            lines.Add("Mission details");
            lines.Add(entry.Summary);
        }

        if (!string.IsNullOrWhiteSpace(entry.RewardText))
        {
            lines.Add("");
            lines.Add("Reward");
            lines.Add(entry.RewardText);
        }

        if (!string.IsNullOrWhiteSpace(entry.FailureConsequence))
        {
            lines.Add("");
            lines.Add("Failure consequence");
            lines.Add(entry.FailureConsequence);
        }

        if (entry.Events.Count > 0)
        {
            lines.Add("");
            lines.Add("Timeline");

            foreach (var missionEvent in entry.Events)
            {
                var line = string.Concat(
                    FormatObservedAt(missionEvent.OccurredAt),
                    " · ",
                    FormatMissionHistoryEvent(missionEvent.Kind));

                if (missionEvent.Stage.HasValue)
                {
                    line = string.Concat(
                        line,
                        " · Step ",
                        missionEvent.Stage.Value.ToString(
                            CultureInfo.CurrentCulture));
                }

                lines.Add(line);

                if (!string.IsNullOrWhiteSpace(missionEvent.Objective))
                {
                    lines.Add(string.Concat("  ", missionEvent.Objective));
                }

                var eventLocation = FormatLocation(
                    missionEvent.SystemName,
                    missionEvent.SectorName,
                    missionEvent.StarbaseName);

                if (!string.IsNullOrWhiteSpace(eventLocation))
                {
                    lines.Add(string.Concat("  Location: ", eventLocation));
                }

                var eventDetails = GetMissionHistoryEventDetails(
                    entry,
                    missionEvent);

                if (!string.IsNullOrWhiteSpace(eventDetails))
                {
                    lines.Add(string.Concat("  ", eventDetails));
                }
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string GetMissionHistoryEventDetails(
        MissionJournalEntry entry,
        MissionJournalEvent missionEvent)
    {
        return missionEvent.Kind switch
        {
            MissionJournalEventKind.Accepted when
                entry.Source == MissionJournalSource.JobTerminal =>
                "Accepted from a Job Terminal.",
            MissionJournalEventKind.Accepted when
                entry.Source == MissionJournalSource.Npc &&
                !string.IsNullOrWhiteSpace(entry.IssuerNpcName) =>
                string.Concat("Accepted from ", entry.IssuerNpcName, "."),
            MissionJournalEventKind.Accepted when
                entry.Source == MissionJournalSource.Npc =>
                "Accepted from an NPC.",
            MissionJournalEventKind.SourceIdentified when
                entry.Source == MissionJournalSource.JobTerminal =>
                string.Concat("Identified as a ",
                    entry.TypeDisplay.ToLower(CultureInfo.CurrentCulture),
                    "."),
            _ => "",
        };
    }

    private void UpdateMissionHistoryDetails()
    {
        var episodeId = this.missionHistoryGrid.SelectedRows
            .Cast<DataGridViewRow>()
            .Select(row => row.Tag as string)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        this.missionHistoryDetailsTextBox.Text =
            episodeId != null &&
            this.missionHistoryEntriesById.TryGetValue(episodeId, out var entry)
                ? BuildMissionHistoryDetails(entry)
                : "Select a mission to view its history.";
    }

    private void RunSearch(bool selectResultsTab = true)
    {
        var query = this.searchTextBox.Text.Trim();
        this.searchGrid.Rows.Clear();

        if (query.Length == 0)
        {
            this.searchStatusLabel.Text = "Enter a search term";
            return;
        }

        var results = this.clientManager.PilotArchive.Search(query);
        var iconOutputDirectory =
            this.clientManager.LocateGameOutputDirectory();
        var detailsByPilot =
            new Dictionary<uint, PilotArchivePilotDetails?>();

        foreach (var result in results)
        {
            Image? icon = null;

            if (SearchResultCanHaveIcon(result))
            {
                if (!detailsByPilot.TryGetValue(
                        result.CharacterId,
                        out var details))
                {
                    details = this.clientManager.PilotArchive.GetPilot(
                        result.CharacterId);
                    detailsByPilot[result.CharacterId] = details;
                }

                icon = details == null
                    ? null
                    : this.ResolveSearchResultIcon(
                        result,
                        details,
                        iconOutputDirectory);
            }

            var row = this.searchGrid.Rows[this.searchGrid.Rows.Add(
                result.PilotName,
                result.Category,
                icon,
                result.Result)];
            row.Tag = result;
        }

        this.ApplyStoredGridSort(this.searchGrid);
        this.searchStatusLabel.Text = string.Create(
            CultureInfo.CurrentCulture,
            $"{results.Count:N0} result(s)");

        if (selectResultsTab)
        {
            this.detailTabs.SelectedPage = this.sectionTabs["search"];
        }
    }

    private static bool SearchResultCanHaveIcon(
        PilotArchiveSearchResult result)
    {
        return result.TargetSection is
            PilotArchiveSections.Cargo or
            PilotArchiveSections.Equipment or
            PilotArchiveSections.Vault or
            PilotArchiveSections.Skills;
    }

    private Image? ResolveSearchResultIcon(
        PilotArchiveSearchResult result,
        PilotArchivePilotDetails details,
        string? iconOutputDirectory)
    {
        if (result.TargetSection == PilotArchiveSections.Skills &&
            int.TryParse(
                result.TargetEntityKey,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var skillIndex))
        {
            var skill = details.Skills.FirstOrDefault(candidate =>
                candidate.Index == skillIndex);

            return skill == null
                ? null
                : this.clientManager.GetPilotArchiveSkillIcon(
                    iconOutputDirectory,
                    skill,
                    ArchiveIconSize);
        }

        var slots = result.TargetSection switch
        {
            PilotArchiveSections.Cargo => details.CargoSlots,
            PilotArchiveSections.Equipment => details.EquipmentSlots
                .Concat(details.AmmoSlots)
                .ToArray(),
            PilotArchiveSections.Vault => details.VaultSlots,
            _ => [],
        };
        var separatorIndex = result.TargetEntityKey.LastIndexOf(':');

        if (separatorIndex <= 0 ||
            !int.TryParse(
                result.TargetEntityKey[(separatorIndex + 1)..],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var slotIndex))
        {
            return null;
        }

        var collection = result.TargetEntityKey[..separatorIndex];
        var slot = slots.FirstOrDefault(candidate =>
            candidate.Slot == slotIndex &&
            string.Equals(
                candidate.Collection,
                collection,
                StringComparison.OrdinalIgnoreCase));

        return slot == null
            ? null
            : this.clientManager.GetPilotArchiveItemIcon(
                iconOutputDirectory,
                slot.TemplateId,
                ArchiveIconSize);
    }

    private void NavigateToSearchResult(PilotArchiveSearchResult result)
    {
        this.SelectPilot(result.CharacterId);

        var displayedSection = result.TargetSection is
            PilotArchiveSections.Location or PilotArchiveSections.Progression
                ? PilotArchiveSections.Overview
                : result.TargetSection;

        if (!this.sectionTabs.TryGetValue(displayedSection, out var tab))
        {
            return;
        }

        this.detailTabs.SelectedPage = tab;
        var grid = result.TargetSection switch
        {
            PilotArchiveSections.Cargo => this.cargoGrid,
            PilotArchiveSections.Equipment => this.equipmentGrid,
            PilotArchiveSections.Vault => this.vaultGrid,
            PilotArchiveSections.Skills => this.skillsGrid,
            PilotArchiveSections.Missions => this.missionsGrid,
            PilotArchiveSections.Reputations => this.reputationsGrid,
            _ => null,
        };

        if (grid == null)
        {
            return;
        }

        foreach (DataGridViewRow row in grid.Rows)
        {
            if (!string.Equals(
                    row.Tag as string,
                    result.TargetEntityKey,
                    StringComparison.Ordinal))
            {
                continue;
            }

            row.Selected = true;
            grid.CurrentCell = row.Cells[0];
            grid.FirstDisplayedScrollingRowIndex = Math.Max(0, row.Index);
            break;
        }
    }

    private void RefreshLaunchState(uint characterId)
    {
        var state = this.clientManager.GetPilotArchiveLaunchStatus(characterId);
        this.startCharacterButton.Visible = state.ShowButton;
        this.startCharacterButton.Enabled = state.CanStart;
        this.startCharacterButton.Text = state.ButtonText;
        this.launchStatusLabel.Text = state.Status;
        this.launchStatusLabel.ForeColor = MainWindowTheme.MutedText;
        this.toolTip.SetToolTip(this.startCharacterButton, state.Status);
    }

    private void RefreshPilotBuildCells()
    {
        var columnIndex =
            this.pilotGrid.Columns[PilotBuildColumnName].Index;

        foreach (DataGridViewRow row in this.pilotGrid.Rows)
        {
            if (row.Tag is not PilotArchivePilotSnapshot)
            {
                continue;
            }

            var cell = new DataGridViewButtonCell
            {
                FlatStyle = FlatStyle.Flat,
                Value = "Builds",
                ToolTipText =
                    "Open Builds for this pilot.",
            };
            cell.Style.BackColor = MainWindowTheme.Button;
            cell.Style.ForeColor = MainWindowTheme.Text;
            cell.Style.SelectionBackColor = MainWindowTheme.ButtonHover;
            cell.Style.SelectionForeColor = MainWindowTheme.Text;
            row.Cells[columnIndex] = cell;
        }
    }

    private void RefreshPilotLaunchCells()
    {
        var columnIndex =
            this.pilotGrid.Columns[PilotLaunchColumnName].Index;

        foreach (DataGridViewRow row in this.pilotGrid.Rows)
        {
            if (row.Tag is not PilotArchivePilotSnapshot pilot)
            {
                continue;
            }

            var state = this.clientManager.GetPilotArchiveLaunchStatus(
                pilot.CharacterId);

            DataGridViewCell cell;

            if (state.ShowButton && state.CanStart)
            {
                cell = new DataGridViewButtonCell
                {
                    FlatStyle = FlatStyle.Flat,
                    Value = state.ButtonText,
                    ToolTipText = state.Status,
                };
                cell.Style.Alignment =
                    DataGridViewContentAlignment.MiddleLeft;
                cell.Style.BackColor = MainWindowTheme.Button;
                cell.Style.ForeColor = MainWindowTheme.Text;
                cell.Style.SelectionBackColor = MainWindowTheme.ButtonHover;
                cell.Style.SelectionForeColor = MainWindowTheme.Text;
            }
            else
            {
                cell = new DataGridViewTextBoxCell
                {
                    Value = state.ShowButton
                        ? state.ButtonText
                        : "",
                    ToolTipText = state.Status,
                };
                cell.Style.Alignment =
                    DataGridViewContentAlignment.MiddleLeft;
                cell.Style.ForeColor = MainWindowTheme.DisabledText;
                cell.Style.SelectionForeColor = MainWindowTheme.DisabledText;
            }

            row.Cells[columnIndex] = cell;
        }
    }

    private void ClearDetails()
    {
        foreach (var label in this.overviewValues.Values)
        {
            label.Text = "—";
        }

        foreach (var entry in this.sectionTimestampLabels)
        {
            entry.Value.Text = entry.Key == "search"
                ? "Double-click a result to open its pilot and section."
                : "Not observed yet";
        }

        this.itemToolTip.Remove(this.cargoGrid);
        this.itemToolTip.Remove(this.equipmentGrid);
        this.itemToolTip.Remove(this.vaultGrid);
        this.cargoGrid.Rows.Clear();
        this.equipmentGrid.Rows.Clear();
        this.vaultGrid.Rows.Clear();
        this.skillsGrid.Rows.Clear();
        this.missionsGrid.Rows.Clear();
        this.missionHistoryGrid.Rows.Clear();
        this.missionHistoryEntriesById.Clear();
        this.missionHistoryDetailsTextBox.Text =
            "Select a mission to view its history.";
        this.ClearActivityHistory();
        this.ClearCombatHistory();
        this.reputationsGrid.Rows.Clear();
        this.reputationHistoryGrid.Rows.Clear();
        this.startCharacterButton.Visible = false;
        this.startCharacterButton.Enabled = false;
        this.launchStatusLabel.Text = "Select an archived pilot.";
    }

    private void SetOverview(string key, string? value)
    {
        this.overviewValues[key].Text = string.IsNullOrWhiteSpace(value)
            ? "—"
            : value;
    }

    private void RegisterDarkScrollbarTheme(Control control)
    {
        if (!this.darkScrollbarThemeControls.Add(control))
        {
            return;
        }

        control.HandleCreated +=
            this.DarkScrollbarControl_OnHandleCreated;
        control.ControlAdded +=
            this.DarkScrollbarControl_OnControlAdded;

        foreach (Control child in control.Controls)
        {
            this.RegisterDarkScrollbarTheme(child);
        }

        this.ApplyDarkScrollbarTheme(control);
    }

    private void DarkScrollbarControl_OnHandleCreated(
        object? sender,
        EventArgs e)
    {
        if (sender is Control control)
        {
            this.ApplyDarkScrollbarTheme(control);
        }
    }

    private void DarkScrollbarControl_OnControlAdded(
        object? sender,
        ControlEventArgs e)
    {
        this.RegisterDarkScrollbarTheme(e.Control);
    }

    private void ApplyDarkScrollbarTheme(Control control)
    {
        if (control.IsHandleCreated &&
            IsScrollbarThemeTarget(control))
        {
            _ = NativeMethods.TryApplyDarkControlTheme(control.Handle);
            control.Invalidate();
        }

        foreach (Control child in control.Controls)
        {
            this.ApplyDarkScrollbarTheme(child);
        }
    }

    private static bool IsScrollbarThemeTarget(Control control) =>
        control is ScrollBar ||
        control is TextBoxBase { Multiline: true } ||
        control is ScrollableControl { AutoScroll: true };

    private void PilotArchiveForm_OnShown(object? sender, EventArgs e)
    {
        this.BeginInvoke(() =>
        {
            this.RestoreUiStateAfterShown();
            this.ApplyDarkScrollbarTheme(this);
        });
    }

    private void PilotArchiveForm_OnFormClosing(
        object? sender,
        FormClosingEventArgs e)
    {
        this.SaveUiState();
    }

    private void ContentSplit_OnSplitterMoving(
        object? sender,
        SplitterCancelEventArgs e)
    {
        if (!this.restoringUiState)
        {
            this.contentSplitterUserMovePending = true;
        }
    }

    private void ContentSplit_OnSplitterMoved(
        object? sender,
        SplitterEventArgs e)
    {
        if (this.restoringUiState ||
            !this.contentSplitterUserMovePending)
        {
            return;
        }

        this.contentSplitterUserMovePending = false;
        this.clientManager.PilotArchiveSettings
            .RosterSplitterDistance =
            this.contentSplit.SplitterDistance;
        this.clientManager.SaveSettings();
    }

    private void MissionHistorySplit_OnSplitterMoving(
        object? sender,
        SplitterCancelEventArgs e)
    {
        if (!this.restoringUiState)
        {
            this.missionHistorySplitterUserMovePending = true;
        }
    }

    private void MissionHistorySplit_OnSplitterMoved(
        object? sender,
        SplitterEventArgs e)
    {
        if (this.restoringUiState ||
            !this.missionHistorySplitterUserMovePending)
        {
            return;
        }

        this.missionHistorySplitterUserMovePending = false;
        this.clientManager.PilotArchiveSettings
            .MissionHistorySplitterDistance =
            this.missionHistorySplit.SplitterDistance;
        this.clientManager.SaveSettings();
    }

    private void DetailTabs_OnSelectedPageChanged(
        object? sender,
        EventArgs e)
    {
        if (this.restoringUiState)
        {
            return;
        }

        var selectedSection = this.GetSelectedSection();

        if (!string.IsNullOrWhiteSpace(selectedSection))
        {
            this.clientManager.PilotArchiveSettings.SelectedSection =
                selectedSection;
            this.clientManager.SaveSettings();
        }
    }

    private void Grid_OnSorted(object? sender, EventArgs e)
    {
        if (!this.restoringUiState && sender is DataGridView grid)
        {
            this.CaptureGridSort(grid);
            this.clientManager.SaveSettings();
        }
    }

    private void UpdateRosterHeight()
    {
        if (!this.IsHandleCreated ||
            this.contentSplit.IsDisposed ||
            this.contentSplit.ClientSize.Height <= 0)
        {
            return;
        }

        const int minimumRosterHeight = 150;
        const int minimumDetailHeight = 260;
        const int rosterLabelHeight = 30;
        const int gridChromeAllowance = 8;

        var visibleRowCount = Math.Max(1, this.pilotGrid.Rows.Count);
        var desiredHeight =
            this.contentSplit.Panel1.Padding.Vertical +
            rosterLabelHeight +
            this.pilotGrid.ColumnHeadersHeight +
            (visibleRowCount * this.pilotGrid.RowTemplate.Height) +
            gridChromeAllowance;
        var detailLimitedMaximum =
            this.contentSplit.ClientSize.Height -
            minimumDetailHeight -
            this.contentSplit.SplitterWidth;
        var balancedMaximum = Math.Max(
            minimumRosterHeight,
            (int)Math.Round(
                this.contentSplit.ClientSize.Height * 0.38,
                MidpointRounding.AwayFromZero));
        var maximumHeight = Math.Min(
            detailLimitedMaximum,
            balancedMaximum);

        if (maximumHeight < minimumRosterHeight)
        {
            return;
        }

        var targetHeight = Math.Clamp(
            desiredHeight,
            minimumRosterHeight,
            maximumHeight);

        if (this.contentSplit.SplitterDistance != targetHeight)
        {
            this.contentSplit.SplitterDistance = targetHeight;
        }
    }

    private void ClientManager_OnPilotArchiveChanged(
        object? sender,
        PilotArchiveChangedEventArgs e)
    {
        if (this.IsDisposed || this.Disposing)
        {
            return;
        }

        try
        {
            this.BeginInvoke(() =>
            {
                var selected = this.selectedCharacterId;
                this.RefreshPilots(selected ?? e.CharacterId);

                if (!string.IsNullOrWhiteSpace(this.searchTextBox.Text))
                {
                    this.RunSearch(selectResultsTab: false);
                }
            });
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void ClientManager_OnSkillBuildLibraryChanged(
        object? sender,
        EventArgs e)
    {
        if (this.IsDisposed ||
            this.Disposing ||
            !this.selectedCharacterId.HasValue)
        {
            return;
        }

        try
        {
            this.BeginInvoke(() =>
            {
                if (this.selectedCharacterId is not { } characterId)
                {
                    return;
                }

                this.SetOverview(
                    "active_build",
                    this.clientManager
                        .GetPilotArchiveActiveBuildDisplayName(characterId));
            });
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void MissionHistoryGrid_OnSelectionChanged(
        object? sender,
        EventArgs e)
    {
        this.UpdateMissionHistoryDetails();
    }

    private void ClientManager_OnMissionJournalChanged(
        object? sender,
        MissionJournalChangedEventArgs e)
    {
        if (this.IsDisposed ||
            this.Disposing ||
            !this.clientManager.HistorySettings.RecordMissionHistory ||
            this.selectedCharacterId != e.CharacterId)
        {
            return;
        }

        try
        {
            this.BeginInvoke(() =>
            {
                if (!this.clientManager.HistorySettings.RecordMissionHistory ||
                    this.selectedCharacterId != e.CharacterId)
                {
                    return;
                }

                this.RefreshMissionHistory(e.CharacterId);
                this.PopulateMissionHistoryTimestamp(e.CharacterId);
            });
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void SearchButton_OnClick(object? sender, EventArgs e) =>
        this.RunSearch();

    private void SearchTextBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter)
        {
            return;
        }

        e.Handled = true;
        e.SuppressKeyPress = true;
        this.RunSearch();
    }

    private void PilotGrid_OnSelectionChanged(
        object? sender,
        EventArgs e)
    {
        if (!this.refreshingPilotList)
        {
            this.LoadSelectedPilot();
            this.clientManager.SaveSettings();
        }
    }

    private void PilotGrid_OnCellContentClick(
        object? sender,
        DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 ||
            e.ColumnIndex < 0 ||
            this.pilotGrid.Rows[e.RowIndex].Tag is not
                PilotArchivePilotSnapshot pilot)
        {
            return;
        }

        var columnName = this.pilotGrid.Columns[e.ColumnIndex].Name;

        if (string.Equals(
                columnName,
                PilotBuildColumnName,
                StringComparison.Ordinal))
        {
            if (!this.clientManager.OpenPilotArchiveBuilds(
                    pilot.CharacterId,
                    this,
                    out var status))
            {
                this.searchStatusLabel.Text = status;
                this.searchStatusLabel.ForeColor =
                    MainWindowTheme.Warning;
            }

            return;
        }

        if (!string.Equals(
                columnName,
                PilotLaunchColumnName,
                StringComparison.Ordinal))
        {
            return;
        }

        var state = this.clientManager.GetPilotArchiveLaunchStatus(
            pilot.CharacterId);

        if (!state.ShowButton || !state.CanStart)
        {
            return;
        }

        var succeeded2 = this.clientManager.StartPilotArchiveCharacter(
            pilot.CharacterId,
            this,
            out var status2);

        this.RefreshPilotLaunchCells();

        if (this.selectedCharacterId == pilot.CharacterId)
        {
            this.RefreshLaunchState(pilot.CharacterId);
            this.launchStatusLabel.Text = status2;
            this.launchStatusLabel.ForeColor = succeeded2
                ? MainWindowTheme.Success
                : MainWindowTheme.Warning;
        }
    }

    private void LaunchStateTimer_OnTick(
        object? sender,
        EventArgs e)
    {
        if (this.IsDisposed || this.Disposing)
        {
            return;
        }

        this.RefreshPilotLaunchCells();

        if (this.selectedCharacterId.HasValue)
        {
            this.RefreshLaunchState(this.selectedCharacterId.Value);
        }
    }

    private void SearchGrid_OnCellDoubleClick(
        object? sender,
        DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 ||
            this.searchGrid.Rows[e.RowIndex].Tag is not
                PilotArchiveSearchResult result)
        {
            return;
        }

        this.NavigateToSearchResult(result);
    }

    private void StartCharacterButton_OnClick(
        object? sender,
        EventArgs e)
    {
        if (!this.selectedCharacterId.HasValue)
        {
            return;
        }

        var succeeded = this.clientManager.StartPilotArchiveCharacter(
            this.selectedCharacterId.Value,
            this,
            out var status);
        this.RefreshLaunchState(this.selectedCharacterId.Value);
        this.RefreshPilotLaunchCells();
        this.launchStatusLabel.Text = status;
        this.launchStatusLabel.ForeColor = succeeded
            ? MainWindowTheme.Success
            : MainWindowTheme.Warning;
    }

    private static string FormatGuild(
        string? guildName,
        string? guildRank)
    {
        return string.Join(
            " · ",
            new[] { guildName, guildRank }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static string FormatLocation(params string?[] values)
    {
        return string.Join(
            " · ",
            values.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static string DisplayCollection(string collection) =>
        collection switch
        {
            "cargo" => "Cargo",
            "equipped" or "equipment" => "Equipment",
            "ammo" => "Ammo",
            "secure" or "vault" => "Vault",
            _ => collection,
        };

    private static string DisplayState(string state) =>
        state switch
        {
            "occupied" => "Occupied",
            "empty" => "Empty",
            _ => "Unavailable",
        };

    private static string FormatEquipmentKind(AddonInventorySlotSnapshot slot)
    {
        if (string.IsNullOrWhiteSpace(slot.EquipmentKind))
        {
            return "";
        }

        var text = slot.EquipmentKind.Replace('_', ' ');
        return slot.EquipmentOrdinal.HasValue
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{CultureInfo.CurrentCulture.TextInfo.ToTitleCase(text)} {slot.EquipmentOrdinal.Value}")
            : CultureInfo.CurrentCulture.TextInfo.ToTitleCase(text);
    }

    private sealed record PilotArchiveItemCellContext(
        AddonInventorySlotSnapshot Slot,
        string? IconOutputDirectory);

    private static string FormatPercent(float? value) =>
        value?.ToString("0.#'%'", CultureInfo.CurrentCulture) ?? "";

    private static string FormatNullable(int? value) =>
        value?.ToString(CultureInfo.CurrentCulture) ?? "—";

    private static string FormatObservedAt(DateTimeOffset timestamp) =>
        timestamp.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

}
