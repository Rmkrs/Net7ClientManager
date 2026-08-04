// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using System.Globalization;
using Net7ClientManager.Addons.Contracts;
using Net7ClientManager.Core;
using Net7ClientManager.MissionJournal;
using Net7ClientManager.Models;
using Net7ClientManager.Navigation;
using Net7ClientManager.Observations.Models;
using Net7ClientManager.Services;

internal sealed class MissionWikiCompanionForm : ThemedForm
{
    private const string PlacementAddonId =
        MissionWikiPresentationIds.BuiltInAddonId;
    private const string PlacementWidgetId =
        MissionWikiPresentationIds.CompanionWindowId;
    private const int OwnerGap = 12;
    private const int SplitterWidth = 6;
    private const int MinimumLeftPaneWidth = 320;
    private const int MinimumWikiPaneWidth = 360;
    private const int MinimumMissionPaneHeight = 100;
    private const int MinimumDetailsPaneHeight = 130;
    private const int MinimumSummaryPaneHeight = 130;

    private static readonly Size defaultSize = new(1240, 760);

    private readonly int processId;
    private readonly Func<string, string, AddonWindowPlacement?>
        resolveWindowPlacement;
    private readonly Action<string, string, AddonWindowPlacement>
        saveWindowPlacement;
    private readonly Action<uint> missionSelected;
    private readonly Action<double, double, double> paneRatiosChanged;

    private readonly Panel contentPanel = new();
    private readonly Panel workspacePanel = new();
    private readonly Panel leftPane = new();
    private readonly Panel missionPane = new();
    private readonly Panel detailsPane = new();
    private readonly Panel summaryPane = new();
    private readonly Panel wikiPane = new();
    private readonly Panel firstSplitter = new();
    private readonly Panel secondSplitter = new();
    private readonly Panel thirdSplitter = new();
    private readonly ListView missionList = new();
    private readonly Label missionCountLabel = new();
    private readonly Label missionNameLabel = new();
    private readonly Label stageLabel = new();
    private readonly Label stateLabel = new();
    private readonly TextBox objectiveTextBox = new();
    private readonly TextBox summaryTextBox = new();
    private readonly Label rewardLabel = new();
    private readonly Label factionLabel = new();
    private readonly Label timingLabel = new();
    private readonly MissionWikiWebViewForm browserForm;

    private IReadOnlyList<ClientMissionObservation> missions = [];
    private uint selectedMissionAddress;
    private string? selectedMissionIdentity;
    private string missionListFingerprint = "";
    private string missionDetailsFingerprint = "";
    private string missionTimingFingerprint = "";
    private Rectangle placementOwnerBounds;
    private Rectangle pendingInitialBounds;
    private bool placementRestored;
    private bool restoreMaximized;
    private bool initialPlacementApplied;
    private bool initialDpiReconciliationScheduled;
    private bool initialDpiReconciliationCompleted;
    private bool preserveOpenStateOnClose;
    private bool closeForPresentationSwitch;
    private bool suppressSelectionChanged;
    private int draggingSplitter;
    private int dragStartScreenX;
    private int dragStartScreenY;
    private int dragStartLeftWidth;
    private int dragStartMissionHeight;
    private int dragStartDetailsHeight;
    private double leftPaneRatio;
    private double missionPaneRatio;
    private double detailsPaneRatio;

    public MissionWikiCompanionForm(
        int processId,
        Func<MissionWikiLocationHint, NavigationDestination?>
            resolveNavigationDestination,
        Func<NavigationDestination, NavigationRouteCommandResult>
            setNavigationDestination,
        Func<string, string, AddonWindowPlacement?>
            resolveWindowPlacement,
        Action<string, string, AddonWindowPlacement>
            saveWindowPlacement,
        Action<uint> missionSelected,
        Action<double, double, double> paneRatiosChanged,
        double leftPaneRatio,
        double missionPaneRatio,
        double detailsPaneRatio)
    {
        this.processId = processId;
        this.resolveWindowPlacement = resolveWindowPlacement ??
            throw new ArgumentNullException(nameof(resolveWindowPlacement));
        this.saveWindowPlacement = saveWindowPlacement ??
            throw new ArgumentNullException(nameof(saveWindowPlacement));
        this.missionSelected = missionSelected ??
            throw new ArgumentNullException(nameof(missionSelected));
        this.paneRatiosChanged = paneRatiosChanged ??
            throw new ArgumentNullException(nameof(paneRatiosChanged));
        this.leftPaneRatio = Math.Clamp(leftPaneRatio, 0.30, 0.62);
        this.missionPaneRatio = Math.Clamp(missionPaneRatio, 0.18, 0.60);
        this.detailsPaneRatio = Math.Clamp(detailsPaneRatio, 0.18, 0.60);

        if (this.missionPaneRatio + this.detailsPaneRatio > 0.82)
        {
            this.detailsPaneRatio = 0.82 - this.missionPaneRatio;
        }

        this.Text = "Mission Wiki";
        this.Icon = ResourceLoader.Net7ClientManagerIcon;
        this.StartPosition = FormStartPosition.Manual;
        this.ShowInTaskbar = false;
        this.AutoScaleMode = AutoScaleMode.Dpi;
        this.ClientSize = defaultSize;
        this.MinimumSize = new Size(850, 520);
        this.BackColor = MainWindowTheme.Background;
        this.ForeColor = MainWindowTheme.Text;
        this.Font = MainWindowTheme.CreateBodyFont();
        this.ConfigureWindowChrome(
            allowResize: true,
            showMinimizeButton: true,
            showMaximizeButton: true,
            showCloseButton: true,
            showIcon: true,
            showHelpButton: true);
        this.ConfigureHelpTopic(
            HelpTopicIds.InGameTools,
            () => this.processId);
        this.ConfigureHelpTour(this.ShowHelpTour);

        this.browserForm = new MissionWikiWebViewForm(
            resolveNavigationDestination,
            setNavigationDestination)
        {
            TopLevel = false,
            Dock = DockStyle.Fill,
        };

        this.ConfigureUi();
        this.Controls.Add(this.contentPanel);

        this.FormClosing += this.MissionWikiCompanionForm_OnFormClosing;
        this.FormClosed += this.MissionWikiCompanionForm_OnFormClosed;
        this.workspacePanel.Resize += this.WorkspacePanel_OnResize;
        this.missionList.SelectedIndexChanged +=
            this.MissionList_OnSelectedIndexChanged;
        this.AttachVerticalSplitter(this.firstSplitter);
        this.AttachHorizontalSplitter(this.secondSplitter, splitterIndex: 2);
        this.AttachHorizontalSplitter(this.thirdSplitter, splitterIndex: 3);
    }

    public AddonRuntimeState RuntimeState =>
        this.browserForm.RuntimeState;

    public string StatusText =>
        this.browserForm.StatusText;

    public event EventHandler? UserClosed;

    public void RestorePlacement(Rectangle ownerBounds)
    {
        this.placementOwnerBounds = ownerBounds;

        if (this.placementRestored)
        {
            return;
        }

        var placement = this.resolveWindowPlacement(
            PlacementAddonId,
            PlacementWidgetId);
        this.restoreMaximized = placement?.IsMaximized == true;
        var size = placement is { Width: > 0, Height: > 0 }
            ? new Size(
                Math.Max(this.MinimumSize.Width, placement.Width),
                Math.Max(this.MinimumSize.Height, placement.Height))
            : defaultSize;
        var defaultLocation = ResolveDefaultLocation(ownerBounds, size);
        var location = placement == null
            ? defaultLocation
            : new Point(
                defaultLocation.X + placement.OffsetX,
                defaultLocation.Y + placement.OffsetY);

        this.pendingInitialBounds = ClampToWorkingArea(
            new Rectangle(location, size),
            ownerBounds);
        this.Location = this.pendingInitialBounds.Location;
        this.placementRestored = true;
    }

    public void PrepareForInitialShow()
    {
        if (!this.placementRestored || this.initialPlacementApplied)
        {
            return;
        }

        _ = this.Handle;
        this.Bounds = ClampToWorkingArea(
            this.pendingInitialBounds,
            this.placementOwnerBounds);

        if (this.restoreMaximized)
        {
            this.WindowState = FormWindowState.Maximized;
        }

        this.initialPlacementApplied = true;
    }

    public void ScheduleInitialDpiReconciliation()
    {
        if (this.initialDpiReconciliationScheduled ||
            this.initialDpiReconciliationCompleted ||
            this.IsDisposed ||
            !this.IsHandleCreated ||
            !this.Visible)
        {
            return;
        }

        this.initialDpiReconciliationScheduled = true;
        this.BeginInvoke(() =>
        {
            this.initialDpiReconciliationScheduled = false;

            if (this.initialDpiReconciliationCompleted ||
                this.IsDisposed ||
                this.Disposing ||
                !this.Visible ||
                this.WindowState != FormWindowState.Normal)
            {
                return;
            }

            var originalLocation = this.Location;
            var workingArea = Screen.FromRectangle(this.Bounds).WorkingArea;
            var offsetX = originalLocation.X + this.Width < workingArea.Right
                ? 1
                : -1;

            if (offsetX < 0 && originalLocation.X <= workingArea.Left)
            {
                this.initialDpiReconciliationCompleted = true;
                return;
            }

            this.Location = new Point(
                originalLocation.X + offsetX,
                originalLocation.Y);
            this.Location = originalLocation;
            this.initialDpiReconciliationCompleted = true;
        });
    }

    public void MarkOpen()
    {
        this.preserveOpenStateOnClose = false;
        this.PersistPlacement(isVisible: true, isClosed: false);
    }

    public void ClosePreservingOpenState()
    {
        this.preserveOpenStateOnClose = true;
        this.Close();
    }

    public void CloseForPresentationSwitch()
    {
        this.closeForPresentationSwitch = true;
        this.Close();
    }

    public void UpdatePresentation(
        string pilotName,
        IReadOnlyList<ClientMissionObservation> missions,
        uint selectedMissionAddress,
        MissionJobGuidance? jobGuidance)
    {
        var nextWindowTitle = string.IsNullOrWhiteSpace(pilotName)
            ? "Mission Wiki"
            : string.Concat("Mission Wiki · ", pilotName.Trim());

        if (!string.Equals(
                this.Text,
                nextWindowTitle,
                StringComparison.Ordinal))
        {
            this.Text = nextWindowTitle;
        }

        var nextMissions = missions ?? [];
        var nextSelectedMission = nextMissions.FirstOrDefault(mission =>
            mission.Address == selectedMissionAddress);

        if (nextSelectedMission == null &&
            !string.IsNullOrWhiteSpace(this.selectedMissionIdentity))
        {
            nextSelectedMission = nextMissions.FirstOrDefault(mission =>
                string.Equals(
                    BuildMissionIdentity(mission),
                    this.selectedMissionIdentity,
                    StringComparison.Ordinal));
        }

        var nextSelectedMissionIdentity = nextSelectedMission == null
            ? null
            : BuildMissionIdentity(nextSelectedMission);
        var nextListFingerprint = BuildMissionListFingerprint(
            nextMissions);
        var selectionChanged =
            !string.Equals(
                this.selectedMissionIdentity,
                nextSelectedMissionIdentity,
                StringComparison.Ordinal);

        this.missions = nextMissions;
        this.selectedMissionAddress =
            nextSelectedMission?.Address ?? selectedMissionAddress;
        this.selectedMissionIdentity = nextSelectedMissionIdentity;
        this.RefreshMissionListItemAddresses();

        if (selectionChanged ||
            !string.Equals(
                this.missionListFingerprint,
                nextListFingerprint,
                StringComparison.Ordinal))
        {
            if (!string.Equals(
                    this.missionListFingerprint,
                    nextListFingerprint,
                    StringComparison.Ordinal))
            {
                this.missionListFingerprint = nextListFingerprint;
                this.PopulateMissionList();
            }
            else
            {
                this.ApplyMissionListSelection();
            }
        }

        var selectedMission = nextSelectedMission;
        var nextDetailsFingerprint = BuildMissionDetailsFingerprint(
            selectedMission,
            jobGuidance);

        if (!string.Equals(
                this.missionDetailsFingerprint,
                nextDetailsFingerprint,
                StringComparison.Ordinal))
        {
            this.missionDetailsFingerprint = nextDetailsFingerprint;
            this.ShowSelectedMission(jobGuidance);
        }

        var nextTimingFingerprint = BuildMissionTimingFingerprint(
            selectedMission);

        if (!string.Equals(
                this.missionTimingFingerprint,
                nextTimingFingerprint,
                StringComparison.Ordinal))
        {
            this.missionTimingFingerprint = nextTimingFingerprint;
            this.timingLabel.Text = selectedMission == null
                ? ""
                : ResolveTimingText(selectedMission);
        }
    }

    public void ReloadCurrentMission()
    {
        this.browserForm.ReloadCurrentMission();
    }


    internal void ShowHelpTour()
    {
        GuidedTourOverlay.Show(
            this,
            [
                new GuidedTourStep(
                    () => this.missionPane,
                    "Choose a current mission",
                    "The companion reads this pilot's mission log. Select any mission here without keeping Earth & Beyond's mission window open."),
                new GuidedTourStep(
                    () => this.detailsPane,
                    "Read the current objective",
                    "The middle-left section shows the selected mission, its current step, and the latest objective reported by the game."),
                new GuidedTourStep(
                    () => this.summaryPane,
                    "Review the mission so far",
                    "The lower-left section combines the mission summary with every observed step up to the current one."),
                new GuidedTourStep(
                    () => this.wikiPane,
                    "Use Wiki guidance and routes",
                    "The right side shows the same Net-7 Wiki guidance as the in-game view. Recognized locations keep their Set destination actions."),
                new GuidedTourStep(
                    () => this.firstSplitter,
                    "Resize the mission and Wiki sides",
                    "Drag the vertical divider to give the mission information or Wiki page more room."),
                new GuidedTourStep(
                    () => this.secondSplitter,
                    "Resize the mission sections",
                    "Drag either horizontal divider to vary the height of the mission list, current objective, and mission summary. The complete layout is remembered for this slot."),
            ]);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.FormClosing -= this.MissionWikiCompanionForm_OnFormClosing;
            this.FormClosed -= this.MissionWikiCompanionForm_OnFormClosed;
            this.workspacePanel.Resize -= this.WorkspacePanel_OnResize;
            this.missionList.SelectedIndexChanged -=
                this.MissionList_OnSelectedIndexChanged;
            this.missionList.DrawColumnHeader -=
                this.MissionList_OnDrawColumnHeader;
            this.missionList.DrawItem -=
                this.MissionList_OnDrawItem;
            this.missionList.DrawSubItem -=
                this.MissionList_OnDrawSubItem;
            this.DetachSplitter(this.firstSplitter);
            this.DetachSplitter(this.secondSplitter);
            this.DetachSplitter(this.thirdSplitter);
            this.browserForm.Dispose();
        }

        base.Dispose(disposing);
    }

    private void ConfigureUi()
    {
        this.contentPanel.Dock = DockStyle.Fill;
        this.contentPanel.Padding = new Padding(14);
        this.contentPanel.BackColor = MainWindowTheme.Background;

        this.workspacePanel.Dock = DockStyle.Fill;
        this.workspacePanel.BackColor = MainWindowTheme.Background;

        this.ConfigureMissionPane();
        this.ConfigureDetailsPane();
        this.ConfigureSummaryPane();
        this.ConfigureWikiPane();

        this.leftPane.BackColor = MainWindowTheme.Background;
        this.firstSplitter.BackColor = MainWindowTheme.Border;
        this.firstSplitter.Cursor = Cursors.VSplit;

        foreach (var splitter in new[]
                 {
                     this.secondSplitter,
                     this.thirdSplitter,
                 })
        {
            splitter.BackColor = MainWindowTheme.Border;
            splitter.Cursor = Cursors.HSplit;
        }

        this.leftPane.Controls.Add(this.missionPane);
        this.leftPane.Controls.Add(this.secondSplitter);
        this.leftPane.Controls.Add(this.detailsPane);
        this.leftPane.Controls.Add(this.thirdSplitter);
        this.leftPane.Controls.Add(this.summaryPane);

        this.workspacePanel.Controls.Add(this.leftPane);
        this.workspacePanel.Controls.Add(this.firstSplitter);
        this.workspacePanel.Controls.Add(this.wikiPane);

        this.contentPanel.Controls.Add(this.workspacePanel);
    }

    private void ConfigureMissionPane()
    {
        this.missionPane.BackColor = MainWindowTheme.Panel;
        this.missionPane.Padding = new Padding(10);

        var heading = new Label
        {
            Dock = DockStyle.Top,
            Height = 30,
            Text = "Current missions",
            ForeColor = MainWindowTheme.Text,
            Font = MainWindowTheme.CreateHeadingFont(11.0f),
            TextAlign = ContentAlignment.MiddleLeft,
        };

        this.missionCountLabel.Dock = DockStyle.Bottom;
        this.missionCountLabel.Height = 24;
        this.missionCountLabel.ForeColor = MainWindowTheme.MutedText;
        this.missionCountLabel.TextAlign = ContentAlignment.MiddleLeft;

        this.missionList.Dock = DockStyle.Fill;
        this.missionList.View = View.Details;
        this.missionList.FullRowSelect = true;
        this.missionList.HideSelection = false;
        this.missionList.MultiSelect = false;
        this.missionList.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        this.missionList.BorderStyle = BorderStyle.FixedSingle;
        this.missionList.BackColor = MainWindowTheme.ElevatedPanel;
        this.missionList.ForeColor = MainWindowTheme.Text;
        this.missionList.Font = MainWindowTheme.CreateBodyFont(9.0f);
        this.missionList.OwnerDraw = true;
        this.missionList.Columns.Add("Mission", 200);
        this.missionList.Columns.Add(
            "Step",
            58,
            HorizontalAlignment.Center);
        this.missionList.DrawColumnHeader +=
            this.MissionList_OnDrawColumnHeader;
        this.missionList.DrawItem += this.MissionList_OnDrawItem;
        this.missionList.DrawSubItem += this.MissionList_OnDrawSubItem;

        this.missionPane.Controls.Add(this.missionList);
        this.missionPane.Controls.Add(this.missionCountLabel);
        this.missionPane.Controls.Add(heading);
    }

    private void ConfigureDetailsPane()
    {
        this.detailsPane.BackColor = MainWindowTheme.Panel;
        this.detailsPane.Padding = new Padding(12);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            BackColor = MainWindowTheme.Panel,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        this.missionNameLabel.AutoSize = true;
        this.missionNameLabel.MaximumSize = new Size(900, 0);
        this.missionNameLabel.ForeColor = MainWindowTheme.Accent;
        this.missionNameLabel.Font = MainWindowTheme.CreateHeadingFont(13.0f);
        this.missionNameLabel.Margin = new Padding(0, 0, 0, 6);

        this.stageLabel.AutoSize = true;
        this.stageLabel.ForeColor = MainWindowTheme.Text;
        this.stageLabel.Margin = new Padding(0, 0, 0, 3);

        this.stateLabel.AutoSize = true;
        this.stateLabel.ForeColor = MainWindowTheme.Warning;
        this.stateLabel.Margin = new Padding(0, 0, 0, 6);

        var objectiveHeading = this.CreateSectionHeading("Current objective");
        this.ConfigureReadOnlyTextBox(this.objectiveTextBox);

        layout.Controls.Add(this.missionNameLabel, 0, 0);
        layout.Controls.Add(this.stageLabel, 0, 1);
        layout.Controls.Add(this.stateLabel, 0, 2);
        layout.Controls.Add(objectiveHeading, 0, 3);
        layout.Controls.Add(this.objectiveTextBox, 0, 4);

        this.detailsPane.Controls.Add(layout);
    }

    private void ConfigureSummaryPane()
    {
        this.summaryPane.BackColor = MainWindowTheme.Panel;
        this.summaryPane.Padding = new Padding(12);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            BackColor = MainWindowTheme.Panel,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var summaryHeading = this.CreateSectionHeading("Mission summary");
        this.ConfigureReadOnlyTextBox(this.summaryTextBox);

        this.rewardLabel.AutoSize = true;
        this.rewardLabel.MaximumSize = new Size(900, 0);
        this.rewardLabel.ForeColor = MainWindowTheme.Text;
        this.rewardLabel.Margin = new Padding(0, 8, 0, 2);

        this.factionLabel.AutoSize = true;
        this.factionLabel.MaximumSize = new Size(900, 0);
        this.factionLabel.ForeColor = MainWindowTheme.MutedText;
        this.factionLabel.Margin = new Padding(0, 2, 0, 2);

        this.timingLabel.AutoSize = true;
        this.timingLabel.MaximumSize = new Size(900, 0);
        this.timingLabel.ForeColor = MainWindowTheme.MutedText;
        this.timingLabel.Margin = new Padding(0, 2, 0, 0);

        layout.Controls.Add(summaryHeading, 0, 0);
        layout.Controls.Add(this.summaryTextBox, 0, 1);
        layout.Controls.Add(this.rewardLabel, 0, 2);
        layout.Controls.Add(this.factionLabel, 0, 3);
        layout.Controls.Add(this.timingLabel, 0, 4);

        this.summaryPane.Controls.Add(layout);
    }

    private void ConfigureWikiPane()
    {
        this.wikiPane.BackColor = MainWindowTheme.Panel;
        this.wikiPane.Padding = new Padding(1);
        this.wikiPane.Controls.Add(this.browserForm);
        this.browserForm.Show();
    }

    private Label CreateSectionHeading(string text)
    {
        return new Label
        {
            AutoSize = true,
            Text = text,
            ForeColor = MainWindowTheme.MutedText,
            Font = MainWindowTheme.CreateHeadingFont(9.0f),
            Margin = new Padding(0, 4, 0, 4),
        };
    }

    private void ConfigureReadOnlyTextBox(TextBox textBox)
    {
        textBox.Dock = DockStyle.Fill;
        textBox.Multiline = true;
        textBox.ReadOnly = true;
        textBox.ScrollBars = ScrollBars.Vertical;
        textBox.BorderStyle = BorderStyle.FixedSingle;
        textBox.BackColor = MainWindowTheme.ElevatedPanel;
        textBox.ForeColor = MainWindowTheme.Text;
        textBox.Font = MainWindowTheme.CreateBodyFont(9.0f);
        textBox.Margin = new Padding(0, 0, 0, 4);
    }

    private void PopulateMissionList()
    {
        var topMissionIdentity =
            (this.missionList.TopItem?.Tag as MissionListItemTag)?.Identity;
        ListViewItem? restoredTopItem = null;

        this.suppressSelectionChanged = true;
        this.missionList.BeginUpdate();
        try
        {
            this.missionList.Items.Clear();

            foreach (var mission in this.missions
                         .Where(IsUsableMission)
                         .OrderBy(mission => mission.Slot))
            {
                var step = mission.Stage is >= 1
                    ? mission.StageCount is > 0
                        ? string.Create(
                            CultureInfo.CurrentCulture,
                            $"{mission.Stage}/{mission.StageCount}")
                        : mission.Stage.Value.ToString(
                            CultureInfo.CurrentCulture)
                    : "";
                var item = new ListViewItem(mission.Name)
                {
                    Tag = new MissionListItemTag(
                        mission.Address,
                        BuildMissionIdentity(mission)),
                };
                item.SubItems.Add(step);
                this.missionList.Items.Add(item);

                if (string.Equals(
                        topMissionIdentity,
                        ((MissionListItemTag)item.Tag).Identity,
                        StringComparison.Ordinal))
                {
                    restoredTopItem = item;
                }

                if (string.Equals(
                        BuildMissionIdentity(mission),
                        this.selectedMissionIdentity,
                        StringComparison.Ordinal))
                {
                    item.Selected = true;
                    item.Focused = true;
                }
            }

            this.missionCountLabel.Text = string.Create(
                CultureInfo.CurrentCulture,
                $"{this.missionList.Items.Count} mission{(this.missionList.Items.Count == 1 ? "" : "s")} in the log");

            if (this.missionList.SelectedItems.Count == 0 &&
                this.missionList.Items.Count > 0)
            {
                this.missionList.Items[0].Selected = true;
                this.missionList.Items[0].Focused = true;
                this.selectedMissionAddress =
                    ((MissionListItemTag)this.missionList.Items[0].Tag!)
                    .Address;
                this.selectedMissionIdentity =
                    ((MissionListItemTag)this.missionList.Items[0].Tag!)
                    .Identity;
            }
        }
        finally
        {
            this.missionList.EndUpdate();

            if (restoredTopItem != null)
            {
                try
                {
                    this.missionList.TopItem = restoredTopItem;
                }
                catch (InvalidOperationException)
                {
                }
            }

            this.suppressSelectionChanged = false;
        }
    }

    private void ApplyMissionListSelection()
    {
        this.suppressSelectionChanged = true;

        try
        {
            foreach (ListViewItem item in this.missionList.Items)
            {
                var shouldSelect =
                    item.Tag is MissionListItemTag tag &&
                    string.Equals(
                        tag.Identity,
                        this.selectedMissionIdentity,
                        StringComparison.Ordinal);

                item.Selected = shouldSelect;
                item.Focused = shouldSelect;
            }
        }
        finally
        {
            this.suppressSelectionChanged = false;
        }
    }

    private void RefreshMissionListItemAddresses()
    {
        foreach (ListViewItem item in this.missionList.Items)
        {
            if (item.Tag is not MissionListItemTag tag)
            {
                continue;
            }

            var mission = this.missions.FirstOrDefault(candidate =>
                string.Equals(
                    BuildMissionIdentity(candidate),
                    tag.Identity,
                    StringComparison.Ordinal));

            if (mission != null && mission.Address != tag.Address)
            {
                item.Tag = new MissionListItemTag(
                    mission.Address,
                    tag.Identity);
            }
        }
    }

    private void ShowSelectedMission(MissionJobGuidance? jobGuidance)
    {
        var mission = this.missions.FirstOrDefault(candidate =>
            candidate.Address == this.selectedMissionAddress);

        if (mission == null)
        {
            this.missionNameLabel.Text = "No mission selected";
            this.stageLabel.Text = "Choose a mission from the list.";
            this.stateLabel.Text = "";
            this.stateLabel.Visible = false;
            this.objectiveTextBox.Text = "";
            this.summaryTextBox.Text = "";
            this.rewardLabel.Text = "";
            this.factionLabel.Text = "";
            this.timingLabel.Text = "";
            return;
        }

        this.missionNameLabel.Text = mission.Name;
        this.stageLabel.Text = mission.Stage is >= 1
            ? mission.StageCount is > 0
                ? string.Create(
                    CultureInfo.CurrentCulture,
                    $"Step {mission.Stage} of {mission.StageCount}")
                : string.Create(
                    CultureInfo.CurrentCulture,
                    $"Step {mission.Stage}")
            : "Current step";
        this.stateLabel.Text = ResolveMissionState(mission);
        this.stateLabel.Visible =
            !string.IsNullOrWhiteSpace(this.stateLabel.Text);
        SetTextIfChanged(
            this.objectiveTextBox,
            string.IsNullOrWhiteSpace(mission.CurrentStageText)
                ? "No current objective text is available."
                : mission.CurrentStageText.Trim());
        SetTextIfChanged(
            this.summaryTextBox,
            BuildMissionSummary(mission));
        this.rewardLabel.Text = string.IsNullOrWhiteSpace(mission.Reward)
            ? ""
            : string.Concat("Reward: ", mission.Reward.Trim());
        this.factionLabel.Text = string.IsNullOrWhiteSpace(
                mission.IssuingFaction)
            ? ""
            : string.Concat(
                "Issuing faction: ",
                mission.IssuingFaction.Trim());
        if (jobGuidance != null)
        {
            this.browserForm.NavigateToJob(jobGuidance);
        }
        else
        {
            this.browserForm.NavigateToMission(mission.Name);
        }
    }

    private void ApplyPaneLayout()
    {
        var bounds = this.workspacePanel.ClientRectangle;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var availableWidth = Math.Max(
            1,
            bounds.Width - SplitterWidth);
        var minimumLeftWidth = Math.Min(
            MinimumLeftPaneWidth,
            availableWidth);
        var maximumLeftWidth = Math.Max(
            minimumLeftWidth,
            availableWidth - MinimumWikiPaneWidth);
        var leftWidth = Math.Clamp(
            (int)Math.Round(availableWidth * this.leftPaneRatio),
            minimumLeftWidth,
            maximumLeftWidth);

        var x = bounds.Left;
        this.leftPane.Bounds = new Rectangle(
            x,
            bounds.Top,
            leftWidth,
            bounds.Height);
        x += leftWidth;
        this.firstSplitter.Bounds = new Rectangle(
            x,
            bounds.Top,
            SplitterWidth,
            bounds.Height);
        x += SplitterWidth;
        this.wikiPane.Bounds = new Rectangle(
            x,
            bounds.Top,
            Math.Max(1, bounds.Right - x),
            bounds.Height);

        this.ApplyLeftPaneLayout();

        this.missionList.Columns[0].Width = Math.Max(
            90,
            this.missionList.ClientSize.Width - 66);
        this.missionList.Columns[1].Width = 58;
    }

    private void ApplyLeftPaneLayout()
    {
        var bounds = this.leftPane.ClientRectangle;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var availableHeight = Math.Max(
            1,
            bounds.Height - (SplitterWidth * 2));
        var minimumMissionHeight = Math.Min(
            MinimumMissionPaneHeight,
            availableHeight);
        var maximumMissionHeight = Math.Max(
            minimumMissionHeight,
            availableHeight - MinimumDetailsPaneHeight -
            MinimumSummaryPaneHeight);
        var missionHeight = Math.Clamp(
            (int)Math.Round(availableHeight * this.missionPaneRatio),
            minimumMissionHeight,
            maximumMissionHeight);

        var minimumDetailsHeight = Math.Min(
            MinimumDetailsPaneHeight,
            Math.Max(1, availableHeight - missionHeight));
        var maximumDetailsHeight = Math.Max(
            minimumDetailsHeight,
            availableHeight - missionHeight -
            MinimumSummaryPaneHeight);
        var detailsHeight = Math.Clamp(
            (int)Math.Round(availableHeight * this.detailsPaneRatio),
            minimumDetailsHeight,
            maximumDetailsHeight);

        var y = bounds.Top;
        this.missionPane.Bounds = new Rectangle(
            bounds.Left,
            y,
            bounds.Width,
            missionHeight);
        y += missionHeight;
        this.secondSplitter.Bounds = new Rectangle(
            bounds.Left,
            y,
            bounds.Width,
            SplitterWidth);
        y += SplitterWidth;
        this.detailsPane.Bounds = new Rectangle(
            bounds.Left,
            y,
            bounds.Width,
            detailsHeight);
        y += detailsHeight;
        this.thirdSplitter.Bounds = new Rectangle(
            bounds.Left,
            y,
            bounds.Width,
            SplitterWidth);
        y += SplitterWidth;
        this.summaryPane.Bounds = new Rectangle(
            bounds.Left,
            y,
            bounds.Width,
            Math.Max(1, bounds.Bottom - y));
    }

    private void AttachVerticalSplitter(Panel splitter)
    {
        splitter.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            this.draggingSplitter = 1;
            this.dragStartScreenX = Cursor.Position.X;
            this.dragStartLeftWidth = this.leftPane.Width;
            splitter.Capture = true;
        };
        splitter.MouseMove += (_, _) =>
        {
            if (this.draggingSplitter != 1 ||
                (Control.MouseButtons & MouseButtons.Left) == 0)
            {
                return;
            }

            var availableWidth = Math.Max(
                1,
                this.workspacePanel.ClientSize.Width - SplitterWidth);
            var minimumLeftWidth = Math.Min(
                MinimumLeftPaneWidth,
                availableWidth);
            var maximumLeftWidth = Math.Max(
                minimumLeftWidth,
                availableWidth - MinimumWikiPaneWidth);
            var nextLeftWidth = Math.Clamp(
                this.dragStartLeftWidth +
                Cursor.Position.X - this.dragStartScreenX,
                minimumLeftWidth,
                maximumLeftWidth);

            this.leftPaneRatio = nextLeftWidth /
                (double)availableWidth;
            this.ApplyPaneLayout();
        };
        splitter.MouseUp += (_, _) =>
        {
            if (this.draggingSplitter != 1)
            {
                return;
            }

            this.draggingSplitter = 0;
            splitter.Capture = false;
            this.PersistPaneRatios();
        };
    }

    private void AttachHorizontalSplitter(
        Panel splitter,
        int splitterIndex)
    {
        splitter.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            this.draggingSplitter = splitterIndex;
            this.dragStartScreenY = Cursor.Position.Y;
            this.dragStartMissionHeight = this.missionPane.Height;
            this.dragStartDetailsHeight = this.detailsPane.Height;
            splitter.Capture = true;
        };
        splitter.MouseMove += (_, _) =>
        {
            if (this.draggingSplitter != splitterIndex ||
                (Control.MouseButtons & MouseButtons.Left) == 0)
            {
                return;
            }

            var delta = Cursor.Position.Y - this.dragStartScreenY;
            var availableHeight = Math.Max(
                1,
                this.leftPane.ClientSize.Height -
                (SplitterWidth * 2));

            if (splitterIndex == 2)
            {
                var minimumMissionHeight = Math.Min(
                    MinimumMissionPaneHeight,
                    availableHeight);
                var maximumMissionHeight = Math.Max(
                    minimumMissionHeight,
                    availableHeight - MinimumDetailsPaneHeight -
                    MinimumSummaryPaneHeight);
                var nextMissionHeight = Math.Clamp(
                    this.dragStartMissionHeight + delta,
                    minimumMissionHeight,
                    maximumMissionHeight);
                this.missionPaneRatio = nextMissionHeight /
                    (double)availableHeight;
            }
            else
            {
                var minimumDetailsHeight = Math.Min(
                    MinimumDetailsPaneHeight,
                    Math.Max(
                        1,
                        availableHeight - this.missionPane.Height));
                var maximumDetailsHeight = Math.Max(
                    minimumDetailsHeight,
                    availableHeight - this.missionPane.Height -
                    MinimumSummaryPaneHeight);
                var nextDetailsHeight = Math.Clamp(
                    this.dragStartDetailsHeight + delta,
                    minimumDetailsHeight,
                    maximumDetailsHeight);
                this.detailsPaneRatio = nextDetailsHeight /
                    (double)availableHeight;
            }

            this.ApplyLeftPaneLayout();
        };
        splitter.MouseUp += (_, _) =>
        {
            if (this.draggingSplitter != splitterIndex)
            {
                return;
            }

            this.draggingSplitter = 0;
            splitter.Capture = false;
            this.PersistPaneRatios();
        };
    }

    private void DetachSplitter(Panel splitter)
    {
        // Splitter handlers are anonymous and owned only by this form. They
        // disappear with the panel; no global hooks are involved.
        splitter.Capture = false;
    }

    private void PersistPaneRatios()
    {
        this.leftPaneRatio = Math.Clamp(
            this.leftPaneRatio,
            0.30,
            0.62);
        this.missionPaneRatio = Math.Clamp(
            this.missionPaneRatio,
            0.18,
            0.60);
        this.detailsPaneRatio = Math.Clamp(
            this.detailsPaneRatio,
            0.18,
            0.60);

        if (this.missionPaneRatio + this.detailsPaneRatio > 0.82)
        {
            this.detailsPaneRatio = 0.82 - this.missionPaneRatio;
        }

        this.paneRatiosChanged(
            this.leftPaneRatio,
            this.missionPaneRatio,
            this.detailsPaneRatio);
    }

    private void MissionList_OnDrawColumnHeader(
        object? sender,
        DrawListViewColumnHeaderEventArgs e)
    {
        using var background = new SolidBrush(MainWindowTheme.Header);
        e.Graphics.FillRectangle(background, e.Bounds);
        using var border = new Pen(MainWindowTheme.Border);
        e.Graphics.DrawRectangle(
            border,
            e.Bounds.X,
            e.Bounds.Y,
            Math.Max(0, e.Bounds.Width - 1),
            Math.Max(0, e.Bounds.Height - 1));

        var flags = TextFormatFlags.VerticalCenter |
                    TextFormatFlags.EndEllipsis |
                    TextFormatFlags.NoPadding;
        if (e.Header.TextAlign == HorizontalAlignment.Center)
        {
            flags |= TextFormatFlags.HorizontalCenter;
        }

        TextRenderer.DrawText(
            e.Graphics,
            e.Header.Text,
            this.missionList.Font,
            Rectangle.Inflate(e.Bounds, -7, 0),
            MainWindowTheme.Text,
            flags);
    }

    private void MissionList_OnDrawItem(
        object? sender,
        DrawListViewItemEventArgs e)
    {
        e.DrawDefault = false;
    }

    private void MissionList_OnDrawSubItem(
        object? sender,
        DrawListViewSubItemEventArgs e)
    {
        var selected = e.Item.Selected;
        using var background = new SolidBrush(
            selected
                ? MainWindowTheme.ButtonHover
                : e.ItemIndex % 2 == 0
                    ? MainWindowTheme.ElevatedPanel
                    : MainWindowTheme.Panel);
        e.Graphics.FillRectangle(background, e.Bounds);

        var flags = TextFormatFlags.VerticalCenter |
                    TextFormatFlags.EndEllipsis |
                    TextFormatFlags.NoPadding;
        if (e.Header.TextAlign == HorizontalAlignment.Center)
        {
            flags |= TextFormatFlags.HorizontalCenter;
        }

        TextRenderer.DrawText(
            e.Graphics,
            e.SubItem.Text,
            this.missionList.Font,
            Rectangle.Inflate(e.Bounds, -7, 0),
            MainWindowTheme.Text,
            flags);
    }

    private void MissionList_OnSelectedIndexChanged(
        object? sender,
        EventArgs e)
    {
        if (this.suppressSelectionChanged ||
            this.missionList.SelectedItems.Count == 0 ||
            this.missionList.SelectedItems[0].Tag is not
                MissionListItemTag itemTag)
        {
            return;
        }

        this.selectedMissionAddress = itemTag.Address;
        this.selectedMissionIdentity = itemTag.Identity;
        this.missionSelected(itemTag.Address);
    }

    private void WorkspacePanel_OnResize(object? sender, EventArgs e)
    {
        this.ApplyPaneLayout();
    }

    private void MissionWikiCompanionForm_OnFormClosing(
        object? sender,
        FormClosingEventArgs e)
    {
        var preserveOpenState =
            this.preserveOpenStateOnClose ||
            e.CloseReason is
                System.Windows.Forms.CloseReason.FormOwnerClosing or
                System.Windows.Forms.CloseReason.ApplicationExitCall or
                System.Windows.Forms.CloseReason.WindowsShutDown or
                System.Windows.Forms.CloseReason.TaskManagerClosing;

        if (preserveOpenState)
        {
            this.preserveOpenStateOnClose = true;
        }

        this.PersistPlacement(
            isVisible: preserveOpenState,
            isClosed: !preserveOpenState);
        this.PersistPaneRatios();
    }

    private void MissionWikiCompanionForm_OnFormClosed(
        object? sender,
        FormClosedEventArgs e)
    {
        if (!this.preserveOpenStateOnClose &&
            !this.closeForPresentationSwitch)
        {
            this.UserClosed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void PersistPlacement(bool isVisible, bool isClosed)
    {
        if (!this.placementRestored)
        {
            return;
        }

        var bounds = this.WindowState == FormWindowState.Normal
            ? this.Bounds
            : this.RestoreBounds;
        var defaultLocation = ResolveDefaultLocation(
            this.placementOwnerBounds,
            bounds.Size);

        this.saveWindowPlacement(
            PlacementAddonId,
            PlacementWidgetId,
            new AddonWindowPlacement
            {
                AddonId = PlacementAddonId,
                WidgetId = PlacementWidgetId,
                OffsetX = bounds.Left - defaultLocation.X,
                OffsetY = bounds.Top - defaultLocation.Y,
                Width = bounds.Width,
                Height = bounds.Height,
                IsVisible = isVisible,
                IsClosed = isClosed,
                IsMaximized =
                    this.WindowState == FormWindowState.Maximized,
            });
    }

    private static string BuildMissionListFingerprint(
        IReadOnlyList<ClientMissionObservation> missions)
    {
        return string.Join(
            "|",
            missions
                .Where(IsUsableMission)
                .OrderBy(mission => mission.Slot)
                .Select(mission => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{BuildMissionIdentity(mission)}:{mission.Name}:{mission.Stage}:{mission.StageCount}:{mission.IsComplete}:{mission.IsFailed}:{mission.IsExpired}")));
    }

    private static string BuildMissionDetailsFingerprint(
        ClientMissionObservation? mission,
        MissionJobGuidance? guidance)
    {
        if (mission == null)
        {
            return "none";
        }

        var stageFingerprint = string.Join(
            "~",
            mission.Stages
                .Where(stage => stage.IsAvailable)
                .OrderBy(stage => stage.Index)
                .Select(stage => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{stage.Index}:{stage.ValidState}:{stage.IsTimed}:{stage.Text}")));

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{mission.Name}|{mission.Stage}|{mission.StageCount}|{mission.CurrentStageText}|{mission.Summary}|{stageFingerprint}|{mission.Reward}|{mission.IssuingFaction}|{mission.IsComplete}|{mission.IsFailed}|{mission.IsExpired}|{guidance?.Fingerprint}");
    }

    private static string BuildMissionTimingFingerprint(
        ClientMissionObservation? mission)
    {
        return mission == null
            ? "none"
            : ResolveTimingText(mission);
    }

    private static string BuildMissionIdentity(
        ClientMissionObservation mission)
    {
        return mission.RawId.HasValue
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"id:{mission.RawId.Value}")
            : string.Create(
                CultureInfo.InvariantCulture,
                $"slot:{mission.Slot}:name:{mission.Name}");
    }

    private static void SetTextIfChanged(
        TextBox textBox,
        string text)
    {
        if (string.Equals(
                textBox.Text,
                text,
                StringComparison.Ordinal))
        {
            return;
        }

        textBox.Text = text;
    }

    private sealed record MissionListItemTag(
        uint Address,
        string Identity);

    private static bool IsUsableMission(ClientMissionObservation mission)
    {
        return mission.Address != 0 &&
               mission.ValidState > 0 &&
               !string.IsNullOrWhiteSpace(mission.Name);
    }

    private static string ResolveMissionState(
        ClientMissionObservation mission)
    {
        if (mission.IsComplete == true)
        {
            return "Completed";
        }

        if (mission.IsFailed == true)
        {
            return "Failed";
        }

        if (mission.IsExpired == true)
        {
            return "Expired";
        }

        return "";
    }

    private static string BuildMissionSummary(
        ClientMissionObservation mission)
    {
        var sections = new List<string>();

        if (!string.IsNullOrWhiteSpace(mission.Summary))
        {
            sections.Add(mission.Summary.Trim());
        }

        var knownSteps = mission.Stages
            .Where(stage =>
                stage.IsAvailable &&
                !string.IsNullOrWhiteSpace(stage.Text))
            .OrderBy(stage => stage.Index)
            .Select(stage => string.Create(
                CultureInfo.CurrentCulture,
                $"{stage.Index + 1}. {stage.Text.Trim()}"))
            .ToArray();

        if (knownSteps.Length > 0)
        {
            sections.Add(string.Concat(
                "Mission steps",
                Environment.NewLine,
                string.Join(Environment.NewLine, knownSteps)));
        }

        return sections.Count > 0
            ? string.Join(
                string.Concat(
                    Environment.NewLine,
                    Environment.NewLine),
                sections)
            : "No mission summary is available.";
    }

    private static string ResolveTimingText(
        ClientMissionObservation mission)
    {
        if (mission.RemainingMilliseconds is not > 0)
        {
            return mission.IsTimed == true ||
                   mission.CurrentStage?.IsTimed == true
                ? "Timed mission"
                : "";
        }

        var remaining = TimeSpan.FromMilliseconds(
            mission.RemainingMilliseconds.Value);
        return string.Create(
            CultureInfo.CurrentCulture,
            $"Time remaining: {remaining:hh\\:mm\\:ss}");
    }

    private static Point ResolveDefaultLocation(
        Rectangle ownerBounds,
        Size size)
    {
        var screen = Screen.FromRectangle(ownerBounds);
        var workingArea = screen.WorkingArea;
        var right = new Point(
            ownerBounds.Right + OwnerGap,
            ownerBounds.Top);

        if (right.X + size.Width <= workingArea.Right)
        {
            return right;
        }

        var left = new Point(
            ownerBounds.Left - OwnerGap - size.Width,
            ownerBounds.Top);

        if (left.X >= workingArea.Left)
        {
            return left;
        }

        return new Point(
            Math.Clamp(
                ownerBounds.Right - size.Width,
                workingArea.Left,
                Math.Max(workingArea.Left, workingArea.Right - size.Width)),
            Math.Clamp(
                ownerBounds.Top,
                workingArea.Top,
                Math.Max(workingArea.Top, workingArea.Bottom - size.Height)));
    }

    private static Rectangle ClampToWorkingArea(
        Rectangle bounds,
        Rectangle ownerBounds)
    {
        var screen = Screen.FromRectangle(
            bounds.Width > 0 && bounds.Height > 0
                ? bounds
                : ownerBounds);
        var workingArea = screen.WorkingArea;
        var width = Math.Min(bounds.Width, workingArea.Width);
        var height = Math.Min(bounds.Height, workingArea.Height);
        var x = Math.Clamp(
            bounds.Left,
            workingArea.Left,
            Math.Max(workingArea.Left, workingArea.Right - width));
        var y = Math.Clamp(
            bounds.Top,
            workingArea.Top,
            Math.Max(workingArea.Top, workingArea.Bottom - height));

        return new Rectangle(x, y, width, height);
    }
}
