// ReSharper disable StringLiteralTypo
// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using System.Globalization;
using Net7ClientManager.Core;
using Net7ClientManager.Models;
using Net7ClientManager.Navigation;
using Net7ClientManager.Observations;
using Net7ClientManager.Observations.Models;
using Net7ClientManager.Services;
using Net7ClientManager.Social;
using Net7ClientManager.Win32;

public sealed partial class GalaxyAtlasForm : Form
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

    private static readonly TimeSpan LivePilotPositionHoldDuration =
        TimeSpan.FromSeconds(5);

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

    private static readonly Color currentPilotLocationColor =
        Color.FromArgb(78, 116, 232);

    private static readonly Color groupMemberLocationColor =
        Color.FromArgb(214, 92, 255);

    private static readonly Color socialPilotLocationColor =
        Color.FromArgb(70, 205, 177);

    private static readonly Color buttonColor =
        Color.FromArgb(20, 34, 46);

    private static readonly Color unavailableButtonColor =
        Color.FromArgb(16, 27, 36);

    private static readonly Color unavailableButtonBorderColor =
        Color.FromArgb(48, 72, 87);

    private static readonly Color accessibleColor =
        Color.FromArgb(76, 210, 139);

    private static readonly Color blockedColor =
        Color.FromArgb(236, 94, 94);

    private static readonly Color conditionalColor =
        Color.FromArgb(242, 188, 73);

    private static readonly Color unavailableColor =
        Color.FromArgb(116, 132, 143);

    private static readonly Color harvestableFieldColor =
        Color.FromArgb(82, 214, 190);

    private static readonly Color gravityWellColor =
        Color.FromArgb(196, 124, 255);

    private readonly ClientManager clientManager;
    private readonly WindowPlacementBinding windowPlacement;
    private readonly HostedClientTitleBar titleBar = new();
    private readonly ComboBox clientComboBox = new();
    private readonly Label liveLocationValueLabel = new();
    private readonly Label statusValueLabel = new();
    private readonly Button backButton = new();
    private readonly Button currentLocationButton = new();
    private readonly Button resetViewButton = new();
    private readonly Button twoDimensionalViewButton = new();
    private readonly Button threeDimensionalViewButton = new();
    private readonly CheckBox showLabelsCheckBox = new();
    private readonly CheckBox showMobEncountersCheckBox = new();
    private readonly CheckBox showHarvestableFieldsCheckBox = new();
    private readonly CheckBox showGravityWellsCheckBox = new();
    private readonly CheckBox showCurrentLocationCheckBox = new();
    private readonly CheckBox showGroupMembersCheckBox = new();
    private readonly CheckBox showSocialPilotsCheckBox = new();
    private readonly GalaxyAtlasCanvas atlasCanvas;
    private readonly System.Windows.Forms.Timer refreshTimer = new();
    private readonly Stack<string> backStack = new();
    private readonly Dictionary<int, LastKnownPilotLocation>
        lastKnownPilotLocations = new();

    private int? requestedProcessId;
    private bool isRefreshingClients;
    private bool isFollowingCurrentLocation = true;
    private string clientFingerprint = "";
    private string atlasFingerprint = "";
    private string? currentSectorKey;
    private string? viewedSectorKey;
    private string commandStatusText = "";
    private Color commandStatusColor = mutedTextColor;
    private DateTimeOffset commandStatusExpiresAt;

    public GalaxyAtlasForm(
        ClientManager clientManager,
        int? initialProcessId = null)
    {
        this.clientManager = clientManager;
        this.requestedProcessId = initialProcessId;
        this.atlasSearchIndex = new WorldSearchIndex(
            clientManager.NavigationData);
        this.atlasCanvas = new GalaxyAtlasCanvas(
            clientManager.NavigationData)
        {
            ShowLabels = clientManager.GalaxyAtlasSettings.ShowLabels,
            ShowMobEncounters =
                clientManager.GalaxyAtlasSettings.ShowMobEncounters,
            ShowHarvestableFields =
                clientManager.GalaxyAtlasSettings.ShowHarvestableFields,
            ShowGravityWells =
                clientManager.GalaxyAtlasSettings.ShowGravityWells,
            ShowCurrentLocation =
                clientManager.GalaxyAtlasSettings.ShowCurrentLocation,
            ShowGroupMembers =
                clientManager.GalaxyAtlasSettings.ShowGroupMembers,
            ShowSocialPilots =
                clientManager.GalaxyAtlasSettings.ShowSocialPilots,
            UseThreeDimensionalView =
                clientManager.GalaxyAtlasSettings.UseThreeDimensionalView,
            HoverFocusGuardControl = this.atlasSearchTextBox,
        };

        this.showLabelsCheckBox.Checked =
            clientManager.GalaxyAtlasSettings.ShowLabels;
        this.showMobEncountersCheckBox.Checked =
            clientManager.GalaxyAtlasSettings.ShowMobEncounters;
        this.showHarvestableFieldsCheckBox.Checked =
            clientManager.GalaxyAtlasSettings.ShowHarvestableFields;
        this.showGravityWellsCheckBox.Checked =
            clientManager.GalaxyAtlasSettings.ShowGravityWells;
        this.showCurrentLocationCheckBox.Checked =
            clientManager.GalaxyAtlasSettings.ShowCurrentLocation;
        this.showGroupMembersCheckBox.Checked =
            clientManager.GalaxyAtlasSettings.ShowGroupMembers;
        this.showSocialPilotsCheckBox.Checked =
            clientManager.GalaxyAtlasSettings.ShowSocialPilots;

        this.Text = "Net7 Galaxy Atlas";
        this.Icon = ResourceLoader.Net7ClientManagerIcon;
        this.StartPosition = FormStartPosition.CenterScreen;
        this.FormBorderStyle = FormBorderStyle.None;
        this.Size = new Size(1180, 760);
        this.MinimumSize = new Size(900, 600);
        this.Padding = new Padding(1);
        this.BackColor = borderColor;
        this.ForeColor = textColor;
        this.Font = new Font("Segoe UI", 9.0f);

        this.titleBar.Dock = DockStyle.Top;
        this.titleBar.Height = TitleBarHeight;
        this.titleBar.TitleText = "NET7 GALAXY ATLAS";
        this.titleBar.ShowMaximizeButton = true;
        this.titleBar.ShowHelpButton = true;
        this.titleBar.HelpTopicId = HelpTopicIds.GalaxyAtlas;
        this.titleBar.HelpProcessIdProvider =
            () => this.SelectedProcessId;
        this.titleBar.HelpOverride = () =>
        {
            this.ShowHelpTour();
            return true;
        };
        this.titleBar.AccessibleName = "Net7 Galaxy Atlas title bar";

        this.BuildUi();
        this.UpdateViewModeButtons();
        this.windowPlacement =
            clientManager.BindClientWindowPlacement(
                this,
                WindowPlacementIds.GalaxyAtlas,
                initialProcessId);

        this.clientComboBox.SelectedIndexChanged +=
            this.ClientComboBox_OnSelectedIndexChanged;
        this.clientComboBox.Format +=
            this.ClientComboBox_OnFormat;
        this.atlasSearchTextBox.TextChanged +=
            this.AtlasSearchTextBox_OnTextChanged;
        this.atlasSearchTextBox.Enter +=
            this.AtlasSearchTextBox_OnEnter;
        this.atlasSearchTextBox.Leave +=
            this.AtlasSearchControl_OnLeave;
        this.atlasSearchTextBox.KeyDown +=
            this.AtlasSearchTextBox_OnKeyDown;
        this.atlasSearchResultsListBox.Leave +=
            this.AtlasSearchControl_OnLeave;
        this.atlasSearchResultsListBox.MouseClick +=
            this.AtlasSearchResultsListBox_OnMouseClick;
        this.atlasSearchResultsListBox.MouseMove +=
            this.AtlasSearchResultsListBox_OnMouseMove;
        this.atlasSearchResultsListBox.DrawItem +=
            this.AtlasSearchResultsListBox_OnDrawItem;
        this.backButton.Click +=
            this.BackButton_OnClick;
        this.currentLocationButton.Click +=
            this.CurrentLocationButton_OnClick;
        this.resetViewButton.Click +=
            this.ResetViewButton_OnClick;
        this.twoDimensionalViewButton.Click +=
            this.TwoDimensionalViewButton_OnClick;
        this.threeDimensionalViewButton.Click +=
            this.ThreeDimensionalViewButton_OnClick;
        this.showLabelsCheckBox.CheckedChanged +=
            this.ShowLabelsCheckBox_OnCheckedChanged;
        this.showMobEncountersCheckBox.CheckedChanged +=
            this.ShowMobEncountersCheckBox_OnCheckedChanged;
        this.showHarvestableFieldsCheckBox.CheckedChanged +=
            this.ShowHarvestableFieldsCheckBox_OnCheckedChanged;
        this.showGravityWellsCheckBox.CheckedChanged +=
            this.ShowGravityWellsCheckBox_OnCheckedChanged;
        this.showCurrentLocationCheckBox.CheckedChanged +=
            this.ShowCurrentLocationCheckBox_OnCheckedChanged;
        this.showGroupMembersCheckBox.CheckedChanged +=
            this.ShowGroupMembersCheckBox_OnCheckedChanged;
        this.showSocialPilotsCheckBox.CheckedChanged +=
            this.ShowSocialPilotsCheckBox_OnCheckedChanged;
        this.atlasCanvas.DepartureRequested +=
            this.AtlasCanvas_OnDepartureRequested;
        this.atlasCanvas.DestinationRequested +=
            this.AtlasCanvas_OnDestinationRequested;

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

        this.Resize += this.GalaxyAtlasForm_OnResize;
        this.Deactivate += this.GalaxyAtlasForm_OnDeactivate;

        this.refreshTimer.Interval = 750;
        this.refreshTimer.Tick += this.RefreshTimer_OnTick;
        this.refreshTimer.Start();

        this.RefreshAll(forceClients: true);
    }

    public void SelectProcess(int? processId)
    {
        this.windowPlacement.Rebind(processId);
        this.requestedProcessId = processId;
        this.RefreshClients(force: true);
        this.ResetToCurrentLocation();
        this.RefreshAtlas(force: true);
        this.RefreshAtlasSearchResults(force: true);
    }


    internal void ShowHelpTour()
    {
        GuidedTourOverlay.Show(
            this,
            [
                new GuidedTourStep(
                    () => this.clientComboBox,
                    "Choose whose galaxy you are viewing",
                    "The selected pilot supplies the live location and receives any destination you set from the Atlas."),
                new GuidedTourStep(
                    () => this.atlasSearchTextBox,
                    "Jump straight to something",
                    "Search for systems, sectors, stations, planets, navigation objects, or visible pilots. Select a result to move the Atlas there."),
                new GuidedTourStep(
                    () => this.atlasCanvas,
                    "Explore the map",
                    "In 2D, drag to pan and use the mouse wheel to zoom. In 3D, drag empty space to orbit, middle-drag to pan, and use the wheel to zoom. Hover over objects for details."),
                new GuidedTourStep(
                    () => this.threeDimensionalViewButton,
                    "Switch between 2D and 3D",
                    "2D is the familiar flat Atlas. 3D uses the same live and Forge coordinates with their Z depth, and the Atlas remembers which mode you prefer."),
                new GuidedTourStep(
                    () => this.currentLocationButton,
                    "Return to your pilot",
                    "Current location jumps back to the selected pilot. Back retraces your Atlas visits, while Reset view restores the default framing."),
                new GuidedTourStep(
                    () => this.showLabelsCheckBox,
                    "Choose what the Atlas shows",
                    "The filters along the bottom control labels, your pilot, group members, social pilots, gravity wells, encounters, and resource fields."),
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
        this.refreshTimer.Tick -= this.RefreshTimer_OnTick;
        this.refreshTimer.Dispose();

        this.clientComboBox.SelectedIndexChanged -=
            this.ClientComboBox_OnSelectedIndexChanged;
        this.clientComboBox.Format -=
            this.ClientComboBox_OnFormat;
        this.atlasSearchTextBox.TextChanged -=
            this.AtlasSearchTextBox_OnTextChanged;
        this.atlasSearchTextBox.Enter -=
            this.AtlasSearchTextBox_OnEnter;
        this.atlasSearchTextBox.Leave -=
            this.AtlasSearchControl_OnLeave;
        this.atlasSearchTextBox.KeyDown -=
            this.AtlasSearchTextBox_OnKeyDown;
        this.atlasSearchResultsListBox.Leave -=
            this.AtlasSearchControl_OnLeave;
        this.atlasSearchResultsListBox.MouseClick -=
            this.AtlasSearchResultsListBox_OnMouseClick;
        this.atlasSearchResultsListBox.MouseMove -=
            this.AtlasSearchResultsListBox_OnMouseMove;
        this.atlasSearchResultsListBox.DrawItem -=
            this.AtlasSearchResultsListBox_OnDrawItem;
        this.backButton.Click -=
            this.BackButton_OnClick;
        this.currentLocationButton.Click -=
            this.CurrentLocationButton_OnClick;
        this.resetViewButton.Click -=
            this.ResetViewButton_OnClick;
        this.twoDimensionalViewButton.Click -=
            this.TwoDimensionalViewButton_OnClick;
        this.threeDimensionalViewButton.Click -=
            this.ThreeDimensionalViewButton_OnClick;
        this.showLabelsCheckBox.CheckedChanged -=
            this.ShowLabelsCheckBox_OnCheckedChanged;
        this.showMobEncountersCheckBox.CheckedChanged -=
            this.ShowMobEncountersCheckBox_OnCheckedChanged;
        this.showHarvestableFieldsCheckBox.CheckedChanged -=
            this.ShowHarvestableFieldsCheckBox_OnCheckedChanged;
        this.showGravityWellsCheckBox.CheckedChanged -=
            this.ShowGravityWellsCheckBox_OnCheckedChanged;
        this.showCurrentLocationCheckBox.CheckedChanged -=
            this.ShowCurrentLocationCheckBox_OnCheckedChanged;
        this.showGroupMembersCheckBox.CheckedChanged -=
            this.ShowGroupMembersCheckBox_OnCheckedChanged;
        this.showSocialPilotsCheckBox.CheckedChanged -=
            this.ShowSocialPilotsCheckBox_OnCheckedChanged;
        this.atlasCanvas.DepartureRequested -=
            this.AtlasCanvas_OnDepartureRequested;
        this.atlasCanvas.DestinationRequested -=
            this.AtlasCanvas_OnDestinationRequested;

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

        this.Resize -= this.GalaxyAtlasForm_OnResize;
        this.Deactivate -= this.GalaxyAtlasForm_OnDeactivate;
        this.windowPlacement.Dispose();

        base.OnFormClosed(e);
    }

    private void BuildUi()
    {
        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = backgroundColor,
            Margin = Padding.Empty,
            Padding = new Padding(12),
            RowStyles =
            {
                new RowStyle(SizeType.Absolute, 116),
                new RowStyle(SizeType.Percent, 100),
                new RowStyle(SizeType.Absolute, 42),
            },
        };

        content.Controls.Add(this.BuildHeader(), 0, 0);
        content.Controls.Add(this.atlasCanvas, 0, 1);
        content.Controls.Add(this.BuildFooter(), 0, 2);

        this.atlasCanvas.Dock = DockStyle.Fill;
        this.atlasCanvas.Margin = new Padding(0, 8, 0, 8);

        var chrome = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = borderColor,
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

        chrome.Controls.Add(this.titleBar, 0, 0);
        chrome.Controls.Add(content, 0, 1);

        this.Controls.Add(chrome);
        this.Controls.Add(this.atlasSearchResultsPanel);
        this.atlasSearchResultsPanel.BringToFront();
    }

    private Control BuildHeader()
    {
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = headerColor,
            Padding = new Padding(12, 8, 12, 8),
            ColumnCount = 4,
            RowCount = 3,
            ColumnStyles =
            {
                new ColumnStyle(SizeType.Absolute, 150),
                new ColumnStyle(SizeType.Absolute, 250),
                new ColumnStyle(SizeType.Percent, 100),
                new ColumnStyle(SizeType.Absolute, 330),
            },
            RowStyles =
            {
                new RowStyle(SizeType.Absolute, 20),
                new RowStyle(SizeType.Absolute, 36),
                new RowStyle(SizeType.Percent, 100),
            },
        };

        var title = new Label
        {
            Dock = DockStyle.Fill,
            Text = "GALAXY ATLAS",
            ForeColor = accentColor,
            Font = new Font(
                this.Font.FontFamily,
                12.0f,
                FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(2, 0, 10, 0),
        };

        this.clientComboBox.DropDownStyle =
            ComboBoxStyle.DropDownList;
        this.clientComboBox.FormattingEnabled = true;
        this.clientComboBox.BackColor = panelColor;
        this.clientComboBox.ForeColor = textColor;
        this.clientComboBox.FlatStyle = FlatStyle.Flat;
        this.clientComboBox.Dock = DockStyle.Fill;
        this.clientComboBox.Margin = new Padding(8, 2, 8, 4);

        this.liveLocationValueLabel.Dock = DockStyle.Fill;
        this.liveLocationValueLabel.AutoEllipsis = true;
        this.liveLocationValueLabel.ForeColor = textColor;
        this.liveLocationValueLabel.TextAlign =
            ContentAlignment.MiddleLeft;
        this.liveLocationValueLabel.Margin = new Padding(8, 0, 8, 2);

        this.atlasSearchTextBox.Dock = DockStyle.Fill;
        this.atlasSearchTextBox.Margin = new Padding(8, 5, 8, 4);
        this.atlasSearchTextBox.BackColor = panelColor;
        this.atlasSearchTextBox.ForeColor = textColor;
        this.atlasSearchTextBox.BorderStyle = BorderStyle.FixedSingle;
        this.atlasSearchTextBox.PlaceholderText =
            "Search sectors, stations, navs, or pilots...";
        this.atlasSearchTextBox.AccessibleName = "Atlas search";

        this.atlasSearchResultsPanel.BackColor = borderColor;
        this.atlasSearchResultsPanel.Padding = new Padding(1);
        this.atlasSearchResultsPanel.Visible = false;

        this.atlasSearchResultsListBox.Dock = DockStyle.Fill;
        this.atlasSearchResultsListBox.Margin = Padding.Empty;
        this.atlasSearchResultsListBox.BackColor = backgroundColor;
        this.atlasSearchResultsListBox.ForeColor = textColor;
        this.atlasSearchResultsListBox.BorderStyle = BorderStyle.None;
        this.atlasSearchResultsListBox.DrawMode = DrawMode.OwnerDrawFixed;
        this.atlasSearchResultsListBox.ItemHeight =
            AtlasSearchResultHeight;
        this.atlasSearchResultsListBox.IntegralHeight = false;
        this.atlasSearchResultsListBox.TabStop = false;
        this.atlasSearchResultsListBox.AccessibleName =
            "Atlas search results";
        this.atlasSearchResultsPanel.Controls.Add(
            this.atlasSearchResultsListBox);

        ConfigureButton(this.backButton, "Back");
        ConfigureButton(
            this.currentLocationButton,
            "Current location");
        ConfigureButton(this.resetViewButton, "Reset view");
        ConfigureButton(this.twoDimensionalViewButton, "2D");
        ConfigureButton(this.threeDimensionalViewButton, "3D");
        this.twoDimensionalViewButton.AccessibleName = "Use 2D Atlas view";
        this.threeDimensionalViewButton.AccessibleName = "Use 3D Atlas view";

        var actions = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = new Padding(0, 9, 0, 9),
            ColumnStyles =
            {
                new ColumnStyle(SizeType.Absolute, 82),
                new ColumnStyle(SizeType.Percent, 100),
                new ColumnStyle(SizeType.Absolute, 104),
            },
        };

        actions.Controls.Add(this.backButton, 0, 0);
        actions.Controls.Add(this.currentLocationButton, 1, 0);
        actions.Controls.Add(this.resetViewButton, 2, 0);

        header.Controls.Add(title, 0, 0);
        header.SetRowSpan(title, 2);
        header.Controls.Add(
            CreateHeaderCaption("PILOT"),
            1,
            0);
        header.Controls.Add(this.clientComboBox, 1, 1);
        header.Controls.Add(
            CreateHeaderCaption("LIVE LOCATION"),
            2,
            0);
        header.Controls.Add(this.liveLocationValueLabel, 2, 1);
        header.Controls.Add(actions, 3, 0);
        header.SetRowSpan(actions, 2);
        header.Controls.Add(
            CreateHeaderCaption("SEARCH"),
            0,
            2);
        var searchHost = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
            BackColor = headerColor,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            ColumnStyles =
            {
                // Keep the established Atlas header geometry intact.
                new ColumnStyle(SizeType.Absolute, 680),
                new ColumnStyle(SizeType.Absolute, 56),
                new ColumnStyle(SizeType.Absolute, 56),
                new ColumnStyle(SizeType.Percent, 100),
            },
            RowStyles =
            {
                new RowStyle(SizeType.Percent, 100),
            },
        };

        // These are deliberately direct children of the search row. A
        // nested auto-sized layout can clip the button text at some DPI
        // scales and was also what allowed the search box to grow across
        // the entire header. Match the search box vertically instead of
        // filling the whole header row.
        // TextBox keeps its preferred single-line height, while a
        // dock-filled Button would otherwise grow several pixels taller.
        var searchControlHeight = this.atlasSearchTextBox
            .GetPreferredSize(Size.Empty)
            .Height;

        this.twoDimensionalViewButton.Dock = DockStyle.None;
        this.twoDimensionalViewButton.Anchor =
            AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        this.twoDimensionalViewButton.Height = searchControlHeight;
        this.twoDimensionalViewButton.Margin =
            new Padding(4, 5, 0, 4);

        this.threeDimensionalViewButton.Dock = DockStyle.None;
        this.threeDimensionalViewButton.Anchor =
            AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        this.threeDimensionalViewButton.Height = searchControlHeight;
        this.threeDimensionalViewButton.Margin =
            new Padding(4, 5, 4, 4);

        searchHost.Controls.Add(this.atlasSearchTextBox, 0, 0);
        searchHost.Controls.Add(this.twoDimensionalViewButton, 1, 0);
        searchHost.Controls.Add(this.threeDimensionalViewButton, 2, 0);
        header.Controls.Add(searchHost, 1, 2);
        header.SetColumnSpan(searchHost, 3);

        SetButtonAvailability(this.backButton, isAvailable: false);
        SetButtonAvailability(
            this.currentLocationButton,
            isAvailable: false);
        SetButtonAvailability(this.resetViewButton, isAvailable: true);

        return header;
    }

    private Control BuildFooter()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = panelColor,
            Padding = new Padding(10, 5, 10, 5),
            ColumnStyles =
            {
                new ColumnStyle(SizeType.Absolute, 350),
                new ColumnStyle(SizeType.Absolute, 660),
                new ColumnStyle(SizeType.Percent, 100),
            },
        };

        var legend = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = panelColor,
            Margin = Padding.Empty,
        };

        legend.Controls.Add(CreateLegendLabel("Accessible", accessibleColor));
        legend.Controls.Add(CreateLegendLabel("Blocked", blockedColor));
        legend.Controls.Add(CreateLegendLabel("Conditional", conditionalColor));
        legend.Controls.Add(CreateLegendLabel("Unknown", unavailableColor));

        var layers = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = panelColor,
            Margin = Padding.Empty,
        };
        var layersCaption = new Label
        {
            AutoSize = true,
            Text = "Layers:",
            ForeColor = mutedTextColor,
            BackColor = panelColor,
            Margin = new Padding(0, 5, 7, 0),
        };

        ConfigureLayerCheckBox(
            this.showLabelsCheckBox,
            "Labels",
            textColor);
        ConfigureLayerCheckBox(
            this.showCurrentLocationCheckBox,
            "Me",
            currentPilotLocationColor);
        ConfigureLayerCheckBox(
            this.showGroupMembersCheckBox,
            "Group",
            groupMemberLocationColor);
        ConfigureLayerCheckBox(
            this.showSocialPilotsCheckBox,
            "Social",
            socialPilotLocationColor);
        ConfigureLayerCheckBox(
            this.showGravityWellsCheckBox,
            "Gravity wells",
            gravityWellColor);
        ConfigureLayerCheckBox(
            this.showMobEncountersCheckBox,
            "Encounters",
            textColor);
        ConfigureLayerCheckBox(
            this.showHarvestableFieldsCheckBox,
            "Resources",
            harvestableFieldColor);

        layers.Controls.Add(layersCaption);
        layers.Controls.Add(this.showLabelsCheckBox);
        layers.Controls.Add(this.showCurrentLocationCheckBox);
        layers.Controls.Add(this.showGroupMembersCheckBox);
        layers.Controls.Add(this.showSocialPilotsCheckBox);
        layers.Controls.Add(this.showGravityWellsCheckBox);
        layers.Controls.Add(this.showMobEncountersCheckBox);
        layers.Controls.Add(this.showHarvestableFieldsCheckBox);

        this.statusValueLabel.Dock = DockStyle.Fill;
        this.statusValueLabel.ForeColor = mutedTextColor;
        this.statusValueLabel.TextAlign = ContentAlignment.MiddleRight;
        this.statusValueLabel.AutoEllipsis = true;

        panel.Controls.Add(legend, 0, 0);
        panel.Controls.Add(layers, 1, 0);
        panel.Controls.Add(this.statusValueLabel, 2, 0);

        return panel;
    }

    private static void ConfigureLayerCheckBox(
        CheckBox checkBox,
        string text,
        Color color)
    {
        checkBox.AutoSize = true;
        checkBox.Text = text;
        checkBox.ForeColor = color;
        checkBox.BackColor = panelColor;
        checkBox.Margin = new Padding(4, 3, 10, 0);
        checkBox.TextAlign = ContentAlignment.MiddleLeft;
    }

    private static Label CreateHeaderCaption(string text)
    {
        var fontFamily =
            SystemFonts.MessageBoxFont?.FontFamily
            ?? FontFamily.GenericSansSerif;

        return new Label
        {
            Dock = DockStyle.Fill,
            Text = text,
            ForeColor = mutedTextColor,
            Font = new Font(
                fontFamily,
                7.5f,
                FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(8, 0, 8, 0),
        };
    }

    private void RefreshTimer_OnTick(object? sender, EventArgs e)
    {
        this.RefreshAll(forceClients: false);
    }

    private void RefreshAll(bool forceClients)
    {
        this.RefreshClients(forceClients);
        this.RefreshAtlas(force: false);
        this.RefreshAtlasSearchResults(force: false);
    }

    private void RefreshClients(bool force)
    {
        if (this.isRefreshingClients)
        {
            return;
        }

        var selectedProcessId = this.SelectedProcessId;
        var clients = this.clientManager.Clients
            .OrderBy(client => client.ProcessId)
            .ToArray();

        var fingerprint = string.Join(
            '\u001f',
            clients.Select(client =>
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{client.ProcessId}:{BuildClientDisplayName(
                        client,
                        this.clientManager.GetNavigationRouteSnapshot(
                            client.ProcessId))}")));

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

            foreach (var client in clients)
            {
                this.clientComboBox.Items.Add(client);
            }

            var desiredProcessId =
                this.requestedProcessId ?? selectedProcessId;

            var selected = clients.FirstOrDefault(client =>
                client.ProcessId == desiredProcessId);

            if (selected != null)
            {
                this.clientComboBox.SelectedItem = selected;
            }
            else if (clients.Length > 0)
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

    private void RefreshAtlas(bool force)
    {
        var processId = this.SelectedProcessId;

        if (!processId.HasValue)
        {
            this.ShowUnavailable(
                "No hosted pilot is selected.");
            return;
        }

        var snapshot = this.clientManager.GetNavigationRouteSnapshot(
            processId.Value);
        var currentSector = snapshot.CurrentSector;

        if (!snapshot.IsAvailable || currentSector == null)
        {
            this.ShowUnavailable(snapshot.StatusText);
            return;
        }

        this.currentSectorKey = currentSector.Key;
        var sectorKey = this.viewedSectorKey;

        if (this.isFollowingCurrentLocation ||
            string.IsNullOrWhiteSpace(sectorKey) ||
            !this.clientManager.NavigationRoutes.Topology.TryGetByKey(
                sectorKey,
                out _))
        {
            sectorKey = currentSector.Key;
            this.viewedSectorKey = sectorKey;
        }

        var viewedSector = this.clientManager.NavigationRoutes.Topology
            .GetByKey(sectorKey);

        GalaxyNavigationCatalogSector? catalogSector = null;

        if (this.clientManager.NavigationRoutes.Catalog.TryGetSector(
                viewedSector.Key,
                out var observedCatalogSector))
        {
            catalogSector = observedCatalogSector;
        }

        var isCurrentSector = string.Equals(
            viewedSector.Key,
            currentSector.Key,
            StringComparison.Ordinal);

        var routeDestination = snapshot.Route?.Destination;
        var reputationFingerprint = CreateReputationFingerprint(
            snapshot.CurrentPilotReputation);
        var newFingerprint = string.Concat(
            processId.Value.ToString(CultureInfo.InvariantCulture),
            "|",
            currentSector.Key,
            "|",
            viewedSector.Key,
            "|",
            snapshot.Profession,
            "|",
            (catalogSector?.Targets.Count ?? -1).ToString(
                CultureInfo.InvariantCulture),
            "|",
            isCurrentSector ? "true" : "false",
            "|",
            routeDestination?.SectorKey,
            "|",
            routeDestination?.TargetKey,
            "|",
            this.clientManager.NavigationData.Revision.ToString(
                CultureInfo.InvariantCulture),
            "|",
            this.clientManager.GetSocialSnapshot().Version.ToString(
                CultureInfo.InvariantCulture),
            "|",
            reputationFingerprint);

        this.atlasCanvas.CurrentPilotReputation =
            snapshot.CurrentPilotReputation;
        this.atlasCanvas.SetLivePilotLocations(
            this.BuildLivePilotLocations(
                processId.Value,
                snapshot,
                viewedSector.Key));

        this.liveLocationValueLabel.Text = string.Concat(
            currentSector.SystemName,
            " / ",
            currentSector.Name);

        SetButtonAvailability(
            this.backButton,
            this.backStack.Count > 0);
        SetButtonAvailability(
            this.currentLocationButton,
            !isCurrentSector || !this.isFollowingCurrentLocation);

        var targetCount = catalogSector?.Targets.Count ?? 0;
        var layerCounts = this.atlasCanvas.GetLayerCounts(
            viewedSector.Key);
        this.UpdateLayerCheckBoxText(layerCounts);

        var layerStatus = new List<string>();

        if (this.showMobEncountersCheckBox.Checked)
        {
            layerStatus.Add(layerCounts.MobEncounters == 0
                ? "no contributed encounters"
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"{layerCounts.MobEncounters} encounter cluster(s)"));
        }

        if (this.showHarvestableFieldsCheckBox.Checked)
        {
            layerStatus.Add(layerCounts.HarvestableFields == 0
                ? "no contributed harvestable fields"
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"{layerCounts.HarvestableFields} harvestable field(s)"));
        }

        if (this.showGravityWellsCheckBox.Checked)
        {
            layerStatus.Add(layerCounts.GravityWells == 0
                ? "no contributed gravity wells"
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"{layerCounts.GravityWells} gravity well(s)"));
        }

        var baseStatus = catalogSector == null
            ? string.Concat(
                viewedSector.SystemName,
                " / ",
                viewedSector.Name,
                " · topology only · Forge r",
                this.clientManager.NavigationData.Revision.ToString(
                    CultureInfo.InvariantCulture))
            : $"{viewedSector.SystemName} / {viewedSector.Name} · " +
              string.Create(
                  CultureInfo.InvariantCulture,
                  $"{targetCount} target(s) · Forge r{this.clientManager.NavigationData.Revision}");
        var normalStatus = string.Concat(
            baseStatus,
            layerStatus.Count == 0
                ? ""
                : string.Concat(
                    " · ",
                    string.Join(" · ", layerStatus)),
            " · hover for details · right-click to set destination");

        this.UpdateStatusValueLabel(normalStatus);

        if (!force &&
            string.Equals(
                this.atlasFingerprint,
                newFingerprint,
                StringComparison.Ordinal))
        {
            return;
        }

        this.atlasFingerprint = newFingerprint;

        this.atlasCanvas.SetSector(
            viewedSector,
            catalogSector,
            snapshot.Profession,
            routeDestination,
            isCurrentSector,
            normalStatus);
    }

    private IReadOnlyList<GalaxyAtlasPilotLocation> BuildLivePilotLocations(
        int selectedProcessId,
        NavigationRouteSnapshot selectedRouteSnapshot,
        string viewedSectorKey)
    {
        var clients = this.clientManager.Clients
            .OrderBy(item => item.ProcessId)
            .ToArray();
        var activeProcessIds = clients
            .Select(client => client.ProcessId)
            .ToHashSet();
        this.PruneLastKnownPilotLocations(activeProcessIds);

        var observationsByProcessId = this.clientManager
            .GetClientObservationSnapshots()
            .ToDictionary(
                snapshot => snapshot.ProcessId,
                snapshot => snapshot);
        observationsByProcessId.TryGetValue(
            selectedProcessId,
            out var selectedObservation);

        var groupMemberNames = BuildGroupMemberNameSet(
            selectedObservation);
        var groupMemberObjectIds = BuildGroupMemberObjectIdSet(
            selectedObservation);
        var locations = new List<GalaxyAtlasPilotLocation>();
        var now = DateTimeOffset.UtcNow;

        foreach (var client in clients)
        {
            var routeSnapshot = client.ProcessId == selectedProcessId
                ? selectedRouteSnapshot
                : this.clientManager.GetNavigationRouteSnapshot(
                    client.ProcessId);

            if (!routeSnapshot.IsAvailable ||
                routeSnapshot.CurrentSector == null)
            {
                this.ForgetLastKnownPilotLocation(client.ProcessId);
                continue;
            }

            var currentSectorKey = routeSnapshot.CurrentSector.Key;

            if (!string.Equals(
                    currentSectorKey,
                    viewedSectorKey,
                    StringComparison.Ordinal))
            {
                this.ForgetLastKnownPilotLocationIfSectorChanged(
                    client.ProcessId,
                    currentSectorKey);
                continue;
            }

            var hasObservation = observationsByProcessId.TryGetValue(
                client.ProcessId,
                out var observation);
            ClientSpatialPosition position;
            DateTimeOffset observedAt;
            string? anchorName = null;
            var isDocked = false;

            if (hasObservation &&
                observation != null &&
                IsDockedAtStation(observation))
            {
                // Space coordinates can remain populated while the client is
                // docked. Never let those stale values outrank the station.
                this.ForgetLastKnownPilotLocation(client.ProcessId);
                anchorName = observation.World.CurrentStarbaseName;
                isDocked = true;

                if (!this.TryResolveAtlasTargetPosition(
                        currentSectorKey,
                        anchorName,
                        out position))
                {
                    continue;
                }

                observedAt = observation.ObservedAt;
            }
            else if (hasObservation &&
                     observation != null &&
                     TryGetLivePilotPosition(
                         observation,
                         out var freshPosition))
            {
                position = freshPosition;
                observedAt = observation.ObservedAt;
                this.RememberLastKnownPilotLocation(
                    client.ProcessId,
                    currentSectorKey,
                    position,
                    observedAt);
            }
            else if (!this.TryGetLastKnownPilotLocation(
                         client.ProcessId,
                         currentSectorKey,
                         now,
                         out position,
                         out observedAt))
            {
                continue;
            }

            var isSelectedPilot = client.ProcessId == selectedProcessId;
            var isGroupMember = !isSelectedPilot &&
                hasObservation &&
                observation != null &&
                IsObservedGroupMember(
                    client,
                    routeSnapshot,
                    observation,
                    groupMemberNames,
                    groupMemberObjectIds);

            if (!isSelectedPilot && !isGroupMember)
            {
                continue;
            }

            locations.Add(new GalaxyAtlasPilotLocation
            {
                ProcessId = client.ProcessId,
                PilotName = GetPilotName(client, routeSnapshot),
                SectorKey = currentSectorKey,
                X = position.X,
                Y = position.Y,
                Z = position.Z,
                IsSelectedPilot = isSelectedPilot,
                IsGroupMember = isGroupMember,
                IsDocked = isDocked,
                AnchorName = anchorName,
                ObservedAt = observedAt,
            });
        }

        this.AddSocialPilotLocations(
            locations,
            viewedSectorKey);

        return locations;
    }

    private void AddSocialPilotLocations(
        ICollection<GalaxyAtlasPilotLocation> locations,
        string viewedSectorKey)
    {
        foreach (var presence in this.clientManager.GetSocialSnapshot().Presence)
        {
            if (this.clientManager.GetSocialPresenceFreshness(
                    presence.UpdatedAtUtc) != SocialPresenceFreshness.Online ||
                !string.Equals(
                    presence.SectorKey,
                    viewedSectorKey,
                    StringComparison.Ordinal))
            {
                continue;
            }

            float x;
            float y;
            float z;
            var nearNav = false;
            var isDocked = !string.IsNullOrWhiteSpace(
                presence.StationName);
            var anchorName = ResolveSocialLocationName(presence);

            if (isDocked)
            {
                if (presence.AtlasVisibility <
                        SocialAtlasVisibilityMode.NearNav ||
                    !this.TryResolveAtlasTargetPosition(
                        viewedSectorKey,
                        anchorName,
                        out var stationPosition))
                {
                    continue;
                }

                x = stationPosition.X;
                y = stationPosition.Y;
                z = stationPosition.Z;
            }
            else if (presence.AtlasVisibility ==
                         SocialAtlasVisibilityMode.ExactPosition &&
                     presence.X.HasValue &&
                     presence.Y.HasValue &&
                     presence.Z.HasValue)
            {
                x = (float)presence.X.Value;
                y = (float)presence.Y.Value;
                z = (float)presence.Z.Value;
                anchorName = null;
            }
            else if (presence.AtlasVisibility >=
                         SocialAtlasVisibilityMode.NearNav &&
                     this.TryResolveAtlasTargetPosition(
                         viewedSectorKey,
                         anchorName,
                         out var navPosition))
            {
                x = navPosition.X;
                y = navPosition.Y;
                z = navPosition.Z;
                nearNav = true;
            }
            else
            {
                continue;
            }

            locations.Add(new GalaxyAtlasPilotLocation
            {
                ProcessId = 0,
                PilotName = presence.PilotName,
                SectorKey = viewedSectorKey,
                X = x,
                Y = y,
                Z = z,
                IsSocialPilot = true,
                IsNearNavApproximation = nearNav,
                IsDocked = isDocked,
                AnchorName = anchorName,
                ObservedAt = presence.UpdatedAtUtc,
            });
        }
    }

    private static string? ResolveSocialLocationName(
        SocialPresenceRecord presence)
    {
        return string.IsNullOrWhiteSpace(presence.StationName)
            ? presence.NearestNavName
            : presence.StationName;
    }

    private bool TryResolveAtlasTargetPosition(
        string sectorKey,
        string? navigationName,
        out ClientSpatialPosition position)
    {
        position = default;

        if (string.IsNullOrWhiteSpace(navigationName) ||
            !this.clientManager.NavigationData.Catalog.TryGetSector(
                sectorKey,
                out var sector))
        {
            return false;
        }

        var normalized = GalaxyTopology.NormalizeName(navigationName);
        var target = sector.Targets.FirstOrDefault(candidate =>
            candidate.HasPosition &&
            (string.Equals(
                 GalaxyTopology.NormalizeName(candidate.Name),
                 normalized,
                 StringComparison.Ordinal) ||
             string.Equals(
                 GalaxyTopology.NormalizeName(candidate.MapDisplayName),
                 normalized,
                 StringComparison.Ordinal)));

        if (target == null)
        {
            return false;
        }

        position = new ClientSpatialPosition(
            target.X,
            target.Y,
            target.Z);
        return true;
    }

    private static HashSet<string> BuildGroupMemberNameSet(
        ClientObservationSnapshot? selectedObservation)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (selectedObservation?.Group is not
            { IsAvailable: true, IsValid: true, IsInGroup: true } group)
        {
            return names;
        }

        foreach (var member in group.Members)
        {
            if (member.IsPresent &&
                !string.IsNullOrWhiteSpace(member.Name))
            {
                names.Add(member.Name.Trim());
            }
        }

        return names;
    }

    private static HashSet<uint> BuildGroupMemberObjectIdSet(
        ClientObservationSnapshot? selectedObservation)
    {
        var objectIds = new HashSet<uint>();

        if (selectedObservation?.Group is not
            { IsAvailable: true, IsValid: true, IsInGroup: true } group)
        {
            return objectIds;
        }

        foreach (var member in group.Members)
        {
            if (member.IsPresent)
            {
                objectIds.Add(member.ObjectId);
            }
        }

        return objectIds;
    }

    private static bool IsObservedGroupMember(
        ClientInstance client,
        NavigationRouteSnapshot routeSnapshot,
        ClientObservationSnapshot observation,
        HashSet<string> groupMemberNames,
        HashSet<uint> groupMemberObjectIds)
    {
        if (observation.LocalPlayer.ObjectId is not 0 and not uint.MaxValue &&
            groupMemberObjectIds.Contains(observation.LocalPlayer.ObjectId))
        {
            return true;
        }

        var routeName = routeSnapshot.CharacterName;

        if (!string.IsNullOrWhiteSpace(routeName) &&
            groupMemberNames.Contains(routeName.Trim()))
        {
            return true;
        }

        var identityName = client.LiveCharacterIdentity.Name;

        return !string.IsNullOrWhiteSpace(identityName) &&
               groupMemberNames.Contains(identityName.Trim());
    }

    private static bool IsDockedAtStation(
        ClientObservationSnapshot observation)
    {
        return observation.World.IsAvailable &&
               observation.World.Environment ==
                   ClientWorldEnvironment.Starbase;
    }

    private static bool TryGetLivePilotPosition(
        ClientObservationSnapshot observation,
        out ClientSpatialPosition position)
    {
        if (observation.LocalPlayer.Spatial.IsAvailable &&
            IsFinite(observation.LocalPlayer.Spatial.Position))
        {
            position = observation.LocalPlayer.Spatial.Position;
            return true;
        }

        position = default;
        return false;
    }

    private void RememberLastKnownPilotLocation(
        int processId,
        string sectorKey,
        ClientSpatialPosition position,
        DateTimeOffset observedAt)
    {
        if (!IsFinite(position))
        {
            return;
        }

        this.lastKnownPilotLocations[processId] =
            new LastKnownPilotLocation
            {
                SectorKey = sectorKey,
                Position = position,
                ObservedAt = observedAt,
            };
    }

    private bool TryGetLastKnownPilotLocation(
        int processId,
        string sectorKey,
        DateTimeOffset now,
        out ClientSpatialPosition position,
        out DateTimeOffset observedAt)
    {
        if (this.lastKnownPilotLocations.TryGetValue(
                processId,
                out var location) &&
            string.Equals(
                location.SectorKey,
                sectorKey,
                StringComparison.Ordinal))
        {
            var age = now - location.ObservedAt;

            if (age <= LivePilotPositionHoldDuration ||
                age < TimeSpan.Zero)
            {
                position = location.Position;
                observedAt = location.ObservedAt;
                return true;
            }
        }

        this.lastKnownPilotLocations.Remove(processId);
        position = default;
        observedAt = default;
        return false;
    }

    private void ForgetLastKnownPilotLocation(int processId)
    {
        this.lastKnownPilotLocations.Remove(processId);
    }

    private void ForgetLastKnownPilotLocationIfSectorChanged(
        int processId,
        string currentSectorKey)
    {
        if (this.lastKnownPilotLocations.TryGetValue(
                processId,
                out var location) &&
            !string.Equals(
                location.SectorKey,
                currentSectorKey,
                StringComparison.Ordinal))
        {
            this.lastKnownPilotLocations.Remove(processId);
        }
    }

    private void PruneLastKnownPilotLocations(
        IReadOnlySet<int> activeProcessIds)
    {
        foreach (var processId in this.lastKnownPilotLocations.Keys.ToArray())
        {
            if (!activeProcessIds.Contains(processId))
            {
                this.lastKnownPilotLocations.Remove(processId);
            }
        }
    }

    private static bool IsFinite(ClientSpatialPosition position)
    {
        return float.IsFinite(position.X) &&
               float.IsFinite(position.Y) &&
               float.IsFinite(position.Z);
    }

    private static string GetPilotName(
        ClientInstance client,
        NavigationRouteSnapshot routeSnapshot)
    {
        return routeSnapshot.CharacterName ??
               client.LiveCharacterIdentity.Name ??
               string.Create(
                   CultureInfo.InvariantCulture,
                   $"PID {client.ProcessId}");
    }

    private static string CreateReputationFingerprint(
        ClientReputationObservation reputation)
    {
        if (!reputation.IsAvailable)
        {
            return string.Concat(
                "unavailable:",
                reputation.Status);
        }

        return string.Join(
            ",",
            reputation.Factions
                .Where(faction => faction.Reaction.HasValue)
                .OrderBy(faction => faction.FactionKey, StringComparer.Ordinal)
                .Select(faction => string.Concat(
                    faction.FactionKey,
                    ":",
                    faction.Reaction.Value.ToString(
                        "R",
                        CultureInfo.InvariantCulture))));
    }

    private void ShowUnavailable(string status)
    {
        this.currentSectorKey = null;
        this.viewedSectorKey = null;
        this.ClearCommandStatus();
        this.backStack.Clear();
        this.atlasFingerprint = "";
        this.liveLocationValueLabel.Text = status;
        this.statusValueLabel.Text = status;
        this.atlasCanvas.CurrentPilotReputation =
            ClientReputationObservation.Unavailable(status);
        this.atlasCanvas.SetLivePilotLocations([]);
        this.UpdateLayerCheckBoxText((0, 0, 0));
        SetButtonAvailability(this.backButton, isAvailable: false);
        SetButtonAvailability(
            this.currentLocationButton,
            isAvailable: false);
        this.atlasCanvas.SetSector(
            sectorToSet: null,
            catalogSectorToSet: null,
            pilotProfessionToSet: null,
            activeDestinationToSet: null,
            isCurrentSectorToSet: false,
            status);
    }

    private void ResetToCurrentLocation()
    {
        this.atlasCanvas.ResetView();
        this.backStack.Clear();
        this.isFollowingCurrentLocation = true;
        this.viewedSectorKey = this.currentSectorKey;
        this.atlasFingerprint = "";
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
        this.currentSectorKey = null;
        this.ClearCommandStatus();
        this.ResetToCurrentLocation();
        this.RefreshAtlas(force: true);
        this.RefreshAtlasSearchResults(force: true);
    }

    private void ClientComboBox_OnFormat(
        object? sender,
        ListControlConvertEventArgs e)
    {
        if (e.ListItem is not ClientInstance client)
        {
            return;
        }

        e.Value = BuildClientDisplayName(
            client,
            this.clientManager.GetNavigationRouteSnapshot(
                client.ProcessId));
    }

    private void BackButton_OnClick(object? sender, EventArgs e)
    {
        if (this.backStack.Count == 0)
        {
            return;
        }

        this.viewedSectorKey = this.backStack.Pop();
        this.isFollowingCurrentLocation = false;
        this.atlasFingerprint = "";
        this.RefreshAtlas(force: true);
    }

    private void CurrentLocationButton_OnClick(
        object? sender,
        EventArgs e)
    {
        this.ResetToCurrentLocation();
        this.RefreshAtlas(force: true);
    }

    private void ResetViewButton_OnClick(
        object? sender,
        EventArgs e)
    {
        this.atlasCanvas.ResetView();
    }

    private void TwoDimensionalViewButton_OnClick(
        object? sender,
        EventArgs e)
    {
        this.SetAtlasViewMode(useThreeDimensionalView: false);
    }

    private void ThreeDimensionalViewButton_OnClick(
        object? sender,
        EventArgs e)
    {
        this.SetAtlasViewMode(useThreeDimensionalView: true);
    }

    private void SetAtlasViewMode(bool useThreeDimensionalView)
    {
        if (this.atlasCanvas.UseThreeDimensionalView ==
                useThreeDimensionalView &&
            this.clientManager.GalaxyAtlasSettings.UseThreeDimensionalView ==
                useThreeDimensionalView)
        {
            return;
        }

        this.atlasCanvas.UseThreeDimensionalView =
            useThreeDimensionalView;
        this.clientManager.GalaxyAtlasSettings.UseThreeDimensionalView =
            useThreeDimensionalView;
        this.clientManager.SaveSettings();
        this.UpdateViewModeButtons();
    }

    private void UpdateViewModeButtons()
    {
        ConfigureViewModeButton(
            this.twoDimensionalViewButton,
            selected: !this.atlasCanvas.UseThreeDimensionalView);
        ConfigureViewModeButton(
            this.threeDimensionalViewButton,
            selected: this.atlasCanvas.UseThreeDimensionalView);
    }

    private static void ConfigureViewModeButton(
        Button button,
        bool selected)
    {
        button.BackColor = selected
            ? Color.FromArgb(32, 65, 82)
            : buttonColor;
        button.ForeColor = selected
            ? accentColor
            : textColor;
        button.FlatAppearance.BorderColor = selected
            ? accentColor
            : borderColor;
        button.Cursor = Cursors.Hand;
    }

    private void ShowLabelsCheckBox_OnCheckedChanged(
        object? sender,
        EventArgs e)
    {
        var showLabels = this.showLabelsCheckBox.Checked;
        this.atlasCanvas.ShowLabels = showLabels;
        this.clientManager.GalaxyAtlasSettings.ShowLabels = showLabels;
        this.clientManager.SaveSettings();
    }

    private void ShowMobEncountersCheckBox_OnCheckedChanged(
        object? sender,
        EventArgs e)
    {
        var showMobs = this.showMobEncountersCheckBox.Checked;
        this.atlasCanvas.ShowMobEncounters = showMobs;
        this.clientManager.GalaxyAtlasSettings.ShowMobEncounters = showMobs;
        this.clientManager.SaveSettings();
        this.atlasFingerprint = "";
        this.RefreshAtlas(force: true);
    }

    private void ShowHarvestableFieldsCheckBox_OnCheckedChanged(
        object? sender,
        EventArgs e)
    {
        var showResources = this.showHarvestableFieldsCheckBox.Checked;
        this.atlasCanvas.ShowHarvestableFields = showResources;
        this.clientManager.GalaxyAtlasSettings.ShowHarvestableFields =
            showResources;
        this.clientManager.SaveSettings();
        this.atlasFingerprint = "";
        this.RefreshAtlas(force: true);
    }

    private void ShowGravityWellsCheckBox_OnCheckedChanged(
        object? sender,
        EventArgs e)
    {
        var showGravityWells = this.showGravityWellsCheckBox.Checked;
        this.atlasCanvas.ShowGravityWells = showGravityWells;
        this.clientManager.GalaxyAtlasSettings.ShowGravityWells =
            showGravityWells;
        this.clientManager.SaveSettings();
        this.atlasFingerprint = "";
        this.RefreshAtlas(force: true);
    }

    private void ShowCurrentLocationCheckBox_OnCheckedChanged(
        object? sender,
        EventArgs e)
    {
        var showCurrentLocation = this.showCurrentLocationCheckBox.Checked;
        this.atlasCanvas.ShowCurrentLocation = showCurrentLocation;
        this.clientManager.GalaxyAtlasSettings.ShowCurrentLocation =
            showCurrentLocation;
        this.clientManager.SaveSettings();
        this.RefreshAtlas(force: false);
        this.RefreshAtlasSearchResults(force: true);
    }

    private void ShowGroupMembersCheckBox_OnCheckedChanged(
        object? sender,
        EventArgs e)
    {
        var showGroupMembers = this.showGroupMembersCheckBox.Checked;
        this.atlasCanvas.ShowGroupMembers = showGroupMembers;
        this.clientManager.GalaxyAtlasSettings.ShowGroupMembers =
            showGroupMembers;
        this.clientManager.SaveSettings();
        this.RefreshAtlas(force: false);
        this.RefreshAtlasSearchResults(force: true);
    }

    private void ShowSocialPilotsCheckBox_OnCheckedChanged(
        object? sender,
        EventArgs e)
    {
        var showSocialPilots = this.showSocialPilotsCheckBox.Checked;
        this.atlasCanvas.ShowSocialPilots = showSocialPilots;
        this.clientManager.GalaxyAtlasSettings.ShowSocialPilots =
            showSocialPilots;
        this.clientManager.SaveSettings();
        this.RefreshAtlas(force: false);
        this.RefreshAtlasSearchResults(force: true);
    }

    private void UpdateLayerCheckBoxText(
        (int MobEncounters, int HarvestableFields, int GravityWells) counts)
    {
        this.showMobEncountersCheckBox.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"Mobs ({counts.MobEncounters})");
        this.showHarvestableFieldsCheckBox.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"Resources ({counts.HarvestableFields})");
        this.showGravityWellsCheckBox.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"Gravity wells ({counts.GravityWells})");
    }

    private void AtlasCanvas_OnDestinationRequested(
        object? sender,
        GalaxyAtlasDestinationRequestedEventArgs e)
    {
        var processId = this.SelectedProcessId;

        if (!processId.HasValue)
        {
            this.ShowCommandStatus(
                "Could not set destination · no hosted pilot is selected",
                blockedColor,
                TimeSpan.FromSeconds(6));
            return;
        }

        var result = this.clientManager.SetNavigationDestination(
            processId.Value,
            e.Destination);

        if (!result.Succeeded)
        {
            this.ShowCommandStatus(
                string.Concat(
                    "Could not set destination · ",
                    result.Error),
                blockedColor,
                TimeSpan.FromSeconds(6));
            return;
        }

        this.ShowCommandStatus(
            string.Concat(
                "Destination set · ",
                e.Destination.DisplayName),
            accessibleColor,
            TimeSpan.FromSeconds(4));
        this.atlasFingerprint = "";
        this.RefreshAtlas(force: true);
    }

    private void AtlasCanvas_OnDepartureRequested(
        object? sender,
        GalaxyAtlasDepartureRequestedEventArgs e)
    {
        var destinationKey = e.Departure.ToSectorKey;

        if (string.IsNullOrWhiteSpace(destinationKey) ||
            string.Equals(
                destinationKey,
                this.viewedSectorKey,
                StringComparison.Ordinal) ||
            !this.clientManager.NavigationRoutes.Topology.TryGetByKey(
                destinationKey,
                out _))
        {
            return;
        }

        this.NavigateToSector(destinationKey);
    }

    private void ShowCommandStatus(
        string text,
        Color color,
        TimeSpan duration)
    {
        this.commandStatusText = text;
        this.commandStatusColor = color;
        this.commandStatusExpiresAt = DateTimeOffset.UtcNow + duration;
        this.statusValueLabel.Text = text;
        this.statusValueLabel.ForeColor = color;
    }

    private void ClearCommandStatus()
    {
        this.commandStatusText = "";
        this.commandStatusColor = mutedTextColor;
        this.commandStatusExpiresAt = default;
    }

    private void UpdateStatusValueLabel(string normalStatus)
    {
        if (!string.IsNullOrWhiteSpace(this.commandStatusText) &&
            DateTimeOffset.UtcNow < this.commandStatusExpiresAt)
        {
            this.statusValueLabel.Text = this.commandStatusText;
            this.statusValueLabel.ForeColor = this.commandStatusColor;
            return;
        }

        this.commandStatusText = "";
        this.commandStatusExpiresAt = default;
        this.statusValueLabel.Text = normalStatus;
        this.statusValueLabel.ForeColor = mutedTextColor;
    }

    private void ClientManager_OnNavigationDataChanged(
        object? sender,
        EventArgs e)
    {
        this.atlasCanvas.UpdateData(
            this.clientManager.NavigationData);
        this.atlasSearchIndex = new WorldSearchIndex(
            this.clientManager.NavigationData);
        this.atlasFingerprint = "";
        this.atlasSearchFingerprint = "";
        this.RefreshAll(forceClients: false);
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

    private void GalaxyAtlasForm_OnResize(
        object? sender,
        EventArgs e)
    {
        this.titleBar.IsMaximized =
            this.WindowState == FormWindowState.Maximized;
        this.PositionAtlasSearchResultsPanel();
    }

    private void GalaxyAtlasForm_OnDeactivate(
        object? sender,
        EventArgs e)
    {
        this.HideAtlasSearchResults();
    }

    private int? SelectedProcessId =>
        this.clientComboBox.SelectedItem is ClientInstance client
            ? client.ProcessId
            : null;

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
            : string.Concat(
                snapshot.CurrentSector.SystemName,
                " / ",
                snapshot.CurrentSector.Name);

        return string.Concat(
            pilot,
            " · ",
            location);
    }

    private static Label CreateLegendLabel(
        string text,
        Color color)
    {
        return new Label
        {
            AutoSize = true,
            Text = string.Concat("● ", text),
            ForeColor = color,
            Margin = new Padding(0, 5, 18, 0),
        };
    }

    private static void ConfigureButton(
        Button button,
        string text)
    {
        button.Dock = DockStyle.Fill;
        button.Text = text;
        button.Margin = new Padding(4, 0, 4, 0);
        button.Padding = new Padding(4, 0, 4, 0);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor =
            Color.FromArgb(32, 65, 82);
        button.FlatAppearance.MouseDownBackColor =
            Color.FromArgb(28, 79, 105);
        button.BackColor = buttonColor;
        button.ForeColor = textColor;
        button.UseVisualStyleBackColor = false;
    }

    private static void SetButtonAvailability(
        Button button,
        bool isAvailable)
    {
        button.BackColor = isAvailable
            ? buttonColor
            : unavailableButtonColor;
        button.ForeColor = isAvailable
            ? textColor
            : mutedTextColor;
        button.FlatAppearance.BorderColor = isAvailable
            ? borderColor
            : unavailableButtonBorderColor;
        button.Cursor = isAvailable
            ? Cursors.Hand
            : Cursors.Default;
    }

    private sealed record LastKnownPilotLocation
    {
        public required string SectorKey { get; init; }

        public required ClientSpatialPosition Position { get; init; }

        public required DateTimeOffset ObservedAt { get; init; }
    }

}
