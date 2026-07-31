// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using System.Diagnostics;
using Net7ClientManager.Addons.Contracts;
using Net7ClientManager.Core;
using Net7ClientManager.Contributions;
using Net7ClientManager.Models;
using Net7ClientManager.Navigation;
using Net7ClientManager.Observations;
using Net7ClientManager.Observations.Models;
using Net7ClientManager.Services;
using Net7ClientManager.SkillPlanning;
using Net7ClientManager.Win32;

public sealed partial class ClientHostForm : Form
{
    private const int HoverTitleBarRevealZoneHeight = 10;
    private const int HoverTitleBarPollIntervalMilliseconds = 50;

    private const int MissionWikiBaseCanvasWidth = 1280;
    private const int MissionWikiBaseCanvasHeight = 720;
    private const int MissionWikiBaseX = 345;
    private const int MissionWikiBaseY = 268;
    private const int MissionWikiBaseWidth = 700;
    private const int MissionWikiBaseHeight = 420;
    private const int MissionWikiControlBaseOffsetX = 0;
    private const int MissionWikiControlBaseOffsetY = -72;
    private const int MissionWikiControlBaseWidth = 95;
    private const int MissionWikiControlBaseHeight = 30;
    private const int MissionWikiControlMinimumWidth = 80;
    private const int MissionWikiControlMinimumHeight = 24;

    private const int JobTerminalRouteBaseCanvasWidth = 1280;
    private const int JobTerminalRouteBaseCanvasHeight = 720;
    private const int JobTerminalRouteBaseX = 994;
    private const int JobTerminalRouteBaseY = 555;
    private const int JobTerminalRouteBaseWidth = 193;
    private const int JobTerminalRouteBaseHeight = 93;
    private const int JobTerminalRouteMinimumWidth = 183;
    private const int JobTerminalRouteMinimumHeight = 90;

    private const int FactionDetailsBaseCanvasWidth = 1280;
    private const int FactionDetailsBaseCanvasHeight = 720;
    private const int FactionDetailsBaseX = 345;
    private const int FactionDetailsBaseY = 170;
    private const int FactionDetailsBaseWidth = 585;
    private const int FactionDetailsBaseHeight = 450;
    private const int FactionDetailsMinimumWidth = 350;
    private const int FactionDetailsMinimumHeight = 300;

    private const int BuildSkillsCompanionBaseCanvasWidth = 1280;
    private const int BuildSkillsCompanionBaseCanvasHeight = 720;
    private const int BuildSkillsCompanionBaseX = 345;
    private const int BuildSkillsCompanionBaseY = 196;
    private const int BuildSkillsCompanionBaseWidth = 325;
    private const int BuildSkillsCompanionMinimumWidth = 300;
    private const int BuildSkillsCompanionMinimumHeight = 150;
    private const int BuildSkillsToggleBaseX = 27;
    private const int BuildSkillsToggleBaseY = 140;
    private const int BuildSkillsToggleBaseWidth = 88;
    private const int BuildSkillsToggleBaseHeight = 25;
    private const int BuildSkillsToggleMinimumWidth = 48;
    private const int BuildSkillsToggleMinimumHeight = 14;
    private const string BuildSkillsCompanionPlacementAddonId =
        "net7.builds";
    private const string BuildSkillsCompanionPlacementWidgetId =
        "skills-companion-v1";

    private const int BuildEquipmentCompanionBaseCanvasWidth = 1280;
    private const int BuildEquipmentCompanionBaseCanvasHeight = 720;
    private const int BuildEquipmentCompanionBaseX = 1016;
    private const int BuildEquipmentCompanionBaseY = 70;
    private const int BuildEquipmentCompanionBaseWidth = 255;
    private const int BuildEquipmentCompanionMinimumWidth = 245;
    private const int BuildEquipmentCompanionMinimumHeight = 150;
    private const int BuildEquipmentToggleBaseX = 26;
    private const int BuildEquipmentToggleStarbaseBaseY = 163;
    private const int BuildEquipmentToggleUndockedBaseY = 186;
    private const int BuildEquipmentToggleBaseWidth = 190;
    private const int BuildEquipmentToggleBaseHeight = 19;
    private const int BuildEquipmentToggleMinimumWidth = 96;
    private const int BuildEquipmentToggleMinimumHeight = 12;
    private const string BuildEquipmentCompanionPlacementAddonId =
        "net7.builds";
    private const string BuildEquipmentCompanionPlacementWidgetId =
        "equipment-companion-v1";

    private readonly ClientManager clientManager;
    private readonly ClientInstance clientInstance;
    private readonly ClientDockingService clientDockingService;
    private readonly Action<ClientInstance, CloseReason> closeRequested;
    private readonly Action<ClientInstance> openGalaxyAtlasRequested;
    private readonly Action<ClientInstance, string?>
        openWorldFindRequested;
    private readonly Action<ClientInstance, IWin32Window> openForgeContributionsRequested;
    private readonly Action<ClientInstance, IWin32Window> openPilotArchiveRequested;
    private readonly Action<ClientInstance, IWin32Window> openSocialRequested;
    private readonly Action<ClientInstance, IWin32Window> openAddonsRequested;
    private readonly Action<ClientInstance, IWin32Window> openHelpCenterRequested;
    private readonly Action<ClientInstance, IWin32Window>
        openInGameOptionsRequested;
    private readonly Func<
        MissionWikiLocationHint,
        NavigationDestination?> resolveMissionWikiDestination;
    private readonly Func<
        ClientMissionObservation,
        MissionJobGuidance?> resolveMissionJobGuidance;
    private readonly Func<
        NavigationDestination,
        NavigationRouteCommandResult> setMissionWikiDestination;
    private readonly Action<AddonUiInteraction> addonUiInteractionRaised;
    private readonly Func<string, string, AddonWindowPlacement?>
        resolveAddonWindowPlacement;
    private readonly Action<string, string, AddonWindowPlacement>
        saveAddonWindowPlacement;
    private readonly SkillBuildLocalWorkspace skillBuildLocalWorkspace;
    private readonly ForgeContributionCoordinator forgeContributionCoordinator;
    private readonly Func<int?, Size, Image?> resolveBuildItemIcon;
    private readonly Action<int, ClientTooltipHoverObservation>
        requestGameItemToolTipRefresh;
    private static readonly SkillBuildBoardPresentationBuilder
        skillBuildBoardPresentationBuilder =
            new(new SkillPlannerCatalogService().GetCatalog());

    private readonly HostedClientTitleService titleService = new();
    private readonly ActionToolTip gameItemToolTip = new(topMost: false);

    private readonly HostedClientTitleBar titleBar;
    private readonly Panel gamePanel;

    private readonly System.Windows.Forms.Timer titleStatusTimer = new();
    private readonly System.Windows.Forms.Timer titleBlinkTimer = new();
    private readonly System.Windows.Forms.Timer titleBarHoverTimer = new();
    private readonly Lock pendingUiCommandLock = new();
    private readonly List<AddonUiCommand> pendingUiCommands = [];

    private AddonOverlayForm? addonOverlayForm;
    private NavigationCompanionForm? navigationCompanionForm;
    private NavigationInGamePresenter? navigationInGamePresenter;
    private HostedClientMenuTourForm? hostedClientMenuTourForm;
    private MissionWikiWebViewForm? missionWikiWebViewForm;
    private MissionWikiControlForm? missionWikiControlForm;
    private JobTerminalRouteControlForm? jobTerminalRouteControlForm;
    private JobTerminalRoutePresentation jobTerminalRoutePresentation =
        JobTerminalRoutePresentation.Hidden;
    private FactionDetailsOverlayForm? factionDetailsOverlayForm;
    private FactionDetailsPresentation factionDetailsPresentation =
        FactionDetailsPresentation.Hidden;
    private SkillBuildBoardForm? skillBuildBoardForm;
    private SkillBuildBoardPresentation skillBuildBoardPresentation =
        SkillBuildBoardPresentation.Hidden;
    private SkillBuildSkillsCompanionForm? skillBuildSkillsCompanionForm;
    private BuildCompanionToggleForm? skillBuildSkillsToggleForm;
    private SkillBuildSkillsCompanionPresentation
        skillBuildSkillsCompanionPresentation =
            SkillBuildSkillsCompanionPresentation.Hidden;
    private bool? skillBuildSkillsCompanionVisible;
    private string skillBuildSkillsCompanionSourceFingerprint = "";
    private int skillBuildSkillsWorkspaceRevision;
    private SkillBuildEquipmentCompanionForm?
        skillBuildEquipmentCompanionForm;
    private BuildCompanionToggleForm? skillBuildEquipmentToggleForm;
    private SkillBuildEquipmentCompanionPresentation
        skillBuildEquipmentCompanionPresentation =
            SkillBuildEquipmentCompanionPresentation.Hidden;
    private bool? skillBuildEquipmentCompanionVisible;
    private string skillBuildEquipmentCompanionSourceFingerprint = "";
    private int skillBuildEquipmentWorkspaceRevision;
    private ClientPanelPresentationObservation skillPlannerPanelPresentation =
        ClientPanelPresentationObservation.Unavailable(
            "Equipment panel was not observed");
    private ClientObservationSnapshot? gameItemToolTipSnapshot;
    private ClientTooltipHoverObservation gameItemToolTipHover =
        ClientTooltipHoverObservation.Unavailable(
            "Tooltip hover was not observed");
    private GameItemToolTipPreparation? gameItemToolTipPreparation;
    private string gameItemToolTipRefreshRequestKey = "";
    private bool gameItemToolTipsEnabled = true;
    private Point gameItemToolTipOffset;
    private uint missionWikiMissionAddress;
    private string? missionWikiMissionName;
    private MissionJobGuidance? missionJobGuidance;
    private string missionWikiPresentationStatus =
        "Waiting for an open mission-details pane.";
    private bool missionWikiEnabled;
    private bool missionWikiExpanded = true;
    private bool missionWikiForfeitConfirmationDisplayed;
    private bool addonsSuspendedForSession;
    private string? appliedSlotName;
    private string? titleStatusText;
    private bool titleStatusBlinkEnabled;
    private bool titleStatusBlinkVisible = true;
    private ClientLifecycleState addonLifecycleState =
        ClientLifecycleState.Unknown;
    private bool addonTransitioning;
    private bool closeRequestedByManager;
    private ClientTitleBarMode titleBarMode = ClientTitleBarMode.Always;
    private NavigationPresentationMode navigationPresentationMode =
        NavigationPresentationMode.Companion;
    private decimal titleBarHoverDelaySeconds = 0.75m;
    private DateTime? titleBarHoverStartedAtUtc;
    private Guid? appliedSlotId;

    internal ClientHostForm(
        ClientManager clientManager,
        ClientInstance clientInstance,
        ClientDockingService clientDockingService,
        Action<ClientInstance, CloseReason> closeRequested,
        Action<ClientInstance> openGalaxyAtlasRequested,
        Action<ClientInstance, string?> openWorldFindRequested,
        Action<ClientInstance, IWin32Window> openForgeContributionsRequested,
        Action<ClientInstance, IWin32Window> openPilotArchiveRequested,
        Action<ClientInstance, IWin32Window> openSocialRequested,
        Action<ClientInstance, IWin32Window> openAddonsRequested,
        Action<ClientInstance, IWin32Window> openHelpCenterRequested,
        Action<ClientInstance, IWin32Window> openInGameOptionsRequested,
        Func<
            MissionWikiLocationHint,
            NavigationDestination?> resolveMissionWikiDestination,
        Func<
            ClientMissionObservation,
            MissionJobGuidance?> resolveMissionJobGuidance,
        Func<
            NavigationDestination,
            NavigationRouteCommandResult> setMissionWikiDestination,
        Action<AddonUiInteraction> addonUiInteractionRaised,
        Func<string, string, AddonWindowPlacement?>
            resolveAddonWindowPlacement,
        Action<string, string, AddonWindowPlacement>
            saveAddonWindowPlacement,
        SkillBuildLocalWorkspace skillBuildLocalWorkspace,
        ForgeContributionCoordinator forgeContributionCoordinator,
        Func<int?, Size, Image?> resolveBuildItemIcon,
        Action<int, ClientTooltipHoverObservation>
            requestGameItemToolTipRefresh)
    {
        this.clientManager = clientManager ??
            throw new ArgumentNullException(nameof(clientManager));
        this.clientInstance = clientInstance;
        this.clientDockingService = clientDockingService;
        this.closeRequested = closeRequested;
        this.openGalaxyAtlasRequested = openGalaxyAtlasRequested;
        this.openWorldFindRequested = openWorldFindRequested;
        this.openForgeContributionsRequested = openForgeContributionsRequested;
        this.openPilotArchiveRequested = openPilotArchiveRequested;
        this.openSocialRequested = openSocialRequested;
        this.openAddonsRequested = openAddonsRequested;
        this.openHelpCenterRequested = openHelpCenterRequested;
        this.openInGameOptionsRequested =
            openInGameOptionsRequested;
        this.resolveMissionWikiDestination =
            resolveMissionWikiDestination;
        this.resolveMissionJobGuidance =
            resolveMissionJobGuidance;
        this.setMissionWikiDestination =
            setMissionWikiDestination;
        this.addonUiInteractionRaised = addonUiInteractionRaised;
        this.resolveAddonWindowPlacement =
            resolveAddonWindowPlacement;
        this.saveAddonWindowPlacement =
            saveAddonWindowPlacement;
        this.skillBuildLocalWorkspace = skillBuildLocalWorkspace ??
            throw new ArgumentNullException(nameof(skillBuildLocalWorkspace));
        this.forgeContributionCoordinator = forgeContributionCoordinator ??
            throw new ArgumentNullException(nameof(forgeContributionCoordinator));
        this.resolveBuildItemIcon = resolveBuildItemIcon ??
            throw new ArgumentNullException(nameof(resolveBuildItemIcon));
        this.requestGameItemToolTipRefresh =
            requestGameItemToolTipRefresh ??
            throw new ArgumentNullException(
                nameof(requestGameItemToolTipRefresh));

        this.Text = "Earth & Beyond";
        this.Icon = ResourceLoader.EarthAndBeyondIcon;
        this.StartPosition = FormStartPosition.Manual;
        this.FormBorderStyle = FormBorderStyle.None;
        this.MinimumSize = new Size(
            width: 640,
            height: 480 + HostedClientWindowMetrics.TitleBarHeight);

        this.titleBar = this.CreateTitleBar();
        this.gamePanel = this.CreateGamePanel();

        this.Controls.Add(this.gamePanel);
        this.Controls.Add(this.titleBar);

        this.ApplyCurrentHostTitle();
        this.ApplyInitialBoundsFromGameWindow();

        this.titleStatusTimer.Tick += this.TitleStatusTimer_OnTick;

        this.titleBlinkTimer.Interval = 500;
        this.titleBlinkTimer.Tick += this.TitleBlinkTimer_OnTick;

        this.titleBarHoverTimer.Interval =
            HoverTitleBarPollIntervalMilliseconds;
        this.titleBarHoverTimer.Tick += this.TitleBarHoverTimer_OnTick;

        this.Load += this.ClientHostForm_OnLoad;
        this.Shown += this.ClientHostForm_OnShown;
        this.Resize += this.ClientHostForm_OnResize;
        this.Move += this.ClientHostForm_OnMove;
        this.VisibleChanged += this.ClientHostForm_OnVisibleChanged;
        this.FormClosing += this.ClientHostForm_OnFormClosing;
        this.skillBuildLocalWorkspace.Changed +=
            this.SkillBuildLocalWorkspace_OnChanged;

        this.titleBar.DragRequested += this.TitleBar_OnDragRequested;
        this.titleBar.MinimizeRequested += this.TitleBar_OnMinimizeRequested;
        this.titleBar.CloseRequested += this.TitleBar_OnCloseRequested;
    }

    public void CloseFromManager()
    {
        if (this.IsDisposed || this.Disposing)
        {
            return;
        }

        this.closeRequestedByManager = true;
        this.Close();
    }

    public void RefreshRuntimeTitle()
    {
        if (this.IsDisposed || this.Disposing)
        {
            return;
        }

        if (!this.IsHandleCreated)
        {
            return;
        }

        if (this.InvokeRequired)
        {
            try
            {
                this.BeginInvoke(this.RefreshRuntimeTitle);
            }
            catch (InvalidOperationException)
            {
            }

            return;
        }

        this.ApplyCurrentHostTitle();
    }

    public void ApplyManagedHostSize(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        this.ClientSize = new Size(
            width,
            height + this.CurrentTitleBarHeight);

        _ = this.clientDockingService.TryResizeDockedWindow(
            this.clientInstance.GameWindowHandle,
            this.gamePanel.ClientSize);
    }

    public void ApplySlot(ClientSlot slot)
    {
        var slotChanged = this.appliedSlotId != slot.Id;
        this.appliedSlotId = slot.Id;

        if (slotChanged)
        {
            this.CloseNavigationCompanion(
                preserveOpenPreference: true);
            this.navigationInGamePresenter?.Hide();
        }

        this.navigationPresentationMode =
            slot.NavigationPresentationMode;
        this.appliedSlotName = slot.Name;
        this.ApplyTitleBarMode(
            slot.EffectiveTitleBarMode,
            slot.TitleBarHoverDelaySeconds);
        this.ApplyCurrentHostTitle();

        this.StartPosition = FormStartPosition.Manual;
        this.Location = new Point(slot.Bounds.Left, slot.Bounds.Top);

        this.ClientSize = new Size(
            width: slot.Bounds.Width,
            height: slot.Bounds.Height + this.CurrentTitleBarHeight);

        _ = this.clientDockingService.TryResizeDockedWindow(
            this.clientInstance.GameWindowHandle,
            this.gamePanel.ClientSize);

        this.SetMissionWikiEnabled(
            !this.addonsSuspendedForSession &&
            slot.EnabledAddonIds.Contains(
                MissionWikiFeature.AddonId,
                StringComparer.Ordinal));

        if (slotChanged)
        {
            this.skillBuildSkillsCompanionVisible = null;
            this.skillBuildSkillsCompanionSourceFingerprint = "";
            this.skillBuildEquipmentCompanionVisible = null;
            this.skillBuildEquipmentCompanionSourceFingerprint = "";
            this.QueueAddonWindowStateReload();
        }

        this.SyncNavigationPresentation();

        this.SyncMissionWiki();
        this.SyncBuildSkillsCompanion();
        this.SyncBuildEquipmentCompanion();
        this.SyncVendorShoppingCompanion();
    }

    public void SetAddonsSuspendedForSession(bool suspended)
    {
        this.addonsSuspendedForSession = suspended;
    }

    public void SetUnassignedTitle()
    {
        var slotChanged = this.appliedSlotId != null;
        this.appliedSlotId = null;

        this.appliedSlotName = null;
        this.navigationPresentationMode =
            NavigationPresentationMode.Companion;
        this.CloseNavigationCompanion(
            preserveOpenPreference: true);
        this.navigationInGamePresenter?.Hide();
        this.ClearTitleStatus();
        this.SetMissionWikiEnabled(enabled: false);

        if (slotChanged)
        {
            this.skillBuildSkillsCompanionVisible = null;
            this.skillBuildSkillsCompanionSourceFingerprint = "";
            this.skillBuildEquipmentCompanionVisible = null;
            this.skillBuildEquipmentCompanionSourceFingerprint = "";
            this.QueueAddonWindowStateReload();
        }

        this.SyncBuildSkillsCompanion();
        this.SyncBuildEquipmentCompanion();
        this.SyncVendorShoppingCompanion();
    }

    public AddonRuntimeState MissionWikiRuntimeState
    {
        get
        {
            if (!this.missionWikiEnabled)
            {
                return AddonRuntimeState.Disabled;
            }

            if (string.IsNullOrWhiteSpace(
                    this.missionWikiMissionName))
            {
                return AddonRuntimeState.WaitingForContext;
            }

            return this.missionWikiWebViewForm?.RuntimeState ??
                   AddonRuntimeState.Loading;
        }
    }

    public string MissionWikiStatusText
    {
        get
        {
            if (!this.missionWikiEnabled)
            {
                return "Disabled.";
            }

            if (string.IsNullOrWhiteSpace(
                    this.missionWikiMissionName))
            {
                return this.missionWikiPresentationStatus;
            }

            return this.missionWikiWebViewForm?.StatusText ??
                   "Preparing the host-owned browser.";
        }
    }

    public void SetMissionWikiEnabled(bool enabled)
    {
        if (this.IsDisposed || this.Disposing)
        {
            return;
        }

        if (this.InvokeRequired)
        {
            try
            {
                this.BeginInvoke(
                    () => this.SetMissionWikiEnabled(enabled));
            }
            catch (InvalidOperationException)
            {
            }

            return;
        }

        if (this.missionWikiEnabled == enabled)
        {
            this.SyncMissionWiki();
            return;
        }

        this.missionWikiEnabled = enabled;
        if (!enabled)
        {
            this.CloseMissionWikiForm();
            return;
        }

        this.SyncMissionWiki();
    }

    public void UpdateMissionWikiPresentation(
        ClientObservationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        this.UpdateMissionWikiPresentation(
            snapshot.PanelPresentation,
            snapshot.LocalPlayer.Missions);
    }

    public void UpdateMissionWikiPresentation(
        ClientPanelPresentationObservation panelPresentation,
        ClientMissionLogObservation missions)
    {
        ArgumentNullException.ThrowIfNull(panelPresentation);
        ArgumentNullException.ThrowIfNull(missions);

        if (this.IsDisposed || this.Disposing)
        {
            return;
        }

        if (this.InvokeRequired)
        {
            try
            {
                this.BeginInvoke(
                    () => this.UpdateMissionWikiPresentation(
                        panelPresentation,
                        missions));
            }
            catch (InvalidOperationException)
            {
            }

            return;
        }

        var details =
            panelPresentation.MissionDetails;

        var nextMissionAddress =
            details.IsDisplayed
                ? details.SelectedMissionAddress
                : 0;

        var selectedMission =
            details.IsDisplayed &&
            missions.IsAvailable
                ? missions.GetByAddress(
                    details.SelectedMissionAddress)
                : null;

        var observedMissionName =
            selectedMission?.Name;

        if (string.IsNullOrWhiteSpace(observedMissionName))
        {
            observedMissionName =
                details.SelectedMissionName;
        }

        var nextMissionName =
            details.IsDisplayed &&
            !string.IsNullOrWhiteSpace(observedMissionName)
                ? observedMissionName.Trim()
                : null;
        var nextJobGuidance =
            selectedMission != null
                ? this.resolveMissionJobGuidance(selectedMission)
                : null;

        this.missionWikiPresentationStatus =
            BuildMissionWikiPresentationStatus(
                panelPresentation,
                details,
                missions,
                nextMissionName,
                nextJobGuidance != null);

        var missionChanged =
            this.missionWikiMissionAddress !=
                nextMissionAddress ||
            !string.Equals(
                this.missionWikiMissionName,
                nextMissionName,
                StringComparison.Ordinal) ||
            !string.Equals(
                this.missionJobGuidance?.Fingerprint,
                nextJobGuidance?.Fingerprint,
                StringComparison.Ordinal);

        this.missionWikiMissionAddress =
            nextMissionAddress;
        this.missionWikiMissionName =
            nextMissionName;
        this.missionJobGuidance =
            nextJobGuidance;
        this.missionWikiForfeitConfirmationDisplayed =
            details.IsForfeitConfirmationDisplayed;

        if (missionChanged &&
            this.missionWikiWebViewForm != null &&
            nextMissionName != null)
        {
            if (nextJobGuidance != null)
            {
                this.missionWikiWebViewForm.NavigateToJob(
                    nextJobGuidance);
            }
            else
            {
                this.missionWikiWebViewForm.NavigateToMission(
                    nextMissionName);
            }
        }

        this.SyncMissionWiki();
    }

    public void UpdateJobTerminalRoutePresentation(
        JobTerminalRoutePresentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        if (this.IsDisposed || this.Disposing)
        {
            return;
        }

        if (this.InvokeRequired)
        {
            try
            {
                this.BeginInvoke(
                    () => this.UpdateJobTerminalRoutePresentation(
                        presentation));
            }
            catch (InvalidOperationException)
            {
            }

            return;
        }

        this.jobTerminalRoutePresentation = presentation;
        this.SyncJobTerminalRoute();
    }

    public void UpdateFactionDetailsPresentation(
        FactionDetailsPresentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        if (this.IsDisposed || this.Disposing)
        {
            return;
        }

        if (this.InvokeRequired)
        {
            try
            {
                this.BeginInvoke(
                    () => this.UpdateFactionDetailsPresentation(
                        presentation));
            }
            catch (InvalidOperationException)
            {
            }

            return;
        }

        this.factionDetailsPresentation = presentation;
        this.SyncFactionDetails();
    }

    public void UpdateGameItemToolTipSnapshot(
        ClientObservationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (this.IsDisposed || this.Disposing)
        {
            return;
        }

        if (this.InvokeRequired)
        {
            try
            {
                this.BeginInvoke(
                    () => this.UpdateGameItemToolTipSnapshot(snapshot));
            }
            catch (InvalidOperationException)
            {
            }

            return;
        }

        this.gameItemToolTipSnapshot = snapshot;
        this.RefreshGameItemToolTipPreparation();
        this.SyncGameItemToolTip();
    }

    public void SetGameItemToolTipOptions(
        bool enabled,
        int horizontalOffset,
        int verticalOffset)
    {
        if (this.IsDisposed || this.Disposing)
        {
            return;
        }

        if (this.InvokeRequired)
        {
            try
            {
                this.BeginInvoke(
                    () => this.SetGameItemToolTipOptions(
                        enabled,
                        horizontalOffset,
                        verticalOffset));
            }
            catch (InvalidOperationException)
            {
            }

            return;
        }

        this.gameItemToolTipsEnabled = enabled;
        this.gameItemToolTipOffset = new Point(
            Math.Clamp(
                horizontalOffset,
                GameItemToolTipSettings.MinimumOffset,
                GameItemToolTipSettings.MaximumOffset),
            Math.Clamp(
                verticalOffset,
                GameItemToolTipSettings.MinimumOffset,
                GameItemToolTipSettings.MaximumOffset));

        if (!enabled)
        {
            this.gameItemToolTipPreparation = null;
            this.gameItemToolTipRefreshRequestKey = "";
            this.gameItemToolTip.HideExternal();
            return;
        }

        this.RefreshGameItemToolTipPreparation();
        this.SyncGameItemToolTip();
    }

    public void UpdateGameItemToolTipHover(
        ClientTooltipHoverObservation hover)
    {
        ArgumentNullException.ThrowIfNull(hover);

        if (this.IsDisposed || this.Disposing)
        {
            return;
        }

        if (this.InvokeRequired)
        {
            try
            {
                this.BeginInvoke(
                    () => this.UpdateGameItemToolTipHover(hover));
            }
            catch (InvalidOperationException)
            {
            }

            return;
        }

        var previousIdentity =
            GameItemToolTipPresentationBuilder.GetHoverIdentity(
                this.gameItemToolTipHover);
        var nextIdentity =
            GameItemToolTipPresentationBuilder.GetHoverIdentity(
                hover);

        if (!string.Equals(
                previousIdentity,
                nextIdentity,
                StringComparison.Ordinal))
        {
            this.gameItemToolTip.HideExternal();
            this.gameItemToolTipPreparation = null;
            this.gameItemToolTipRefreshRequestKey = "";
        }

        this.gameItemToolTipHover = hover;
        this.RefreshGameItemToolTipPreparation();
        this.SyncGameItemToolTip();
    }

    public void UpdateSkillPlannerPresentation(
        ClientObservationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (this.IsDisposed || this.Disposing)
        {
            return;
        }

        if (this.InvokeRequired)
        {
            try
            {
                this.BeginInvoke(
                    () => this.UpdateSkillPlannerPresentation(snapshot));
            }
            catch (InvalidOperationException)
            {
            }

            return;
        }

        this.skillPlannerPanelPresentation =
            snapshot.PanelPresentation;
        this.skillBuildBoardPresentation =
            skillBuildBoardPresentationBuilder.Build(snapshot);
        if (this.skillBuildBoardForm is
            {
                IsDisposed: false,
                Disposing: false,
            })
        {
            this.skillBuildBoardForm.SetPresentation(
                this.skillBuildBoardPresentation);
        }

        this.SyncBuildSkillsCompanion();
        this.SyncBuildEquipmentCompanion();
    }

    public void UpdateSkillPlannerPanelPresentation(
        ClientPanelPresentationObservation panelPresentation)
    {
        ArgumentNullException.ThrowIfNull(panelPresentation);

        if (this.IsDisposed || this.Disposing)
        {
            return;
        }

        if (this.InvokeRequired)
        {
            try
            {
                this.BeginInvoke(
                    () => this.UpdateSkillPlannerPanelPresentation(
                        panelPresentation));
            }
            catch (InvalidOperationException)
            {
            }

            return;
        }

        this.skillPlannerPanelPresentation = panelPresentation;
        this.SyncBuildSkillsCompanion();
        this.SyncBuildEquipmentCompanion();
    }

    private static string BuildMissionWikiPresentationStatus(
        ClientPanelPresentationObservation panelPresentation,
        ClientMissionDetailsPresentationObservation details,
        ClientMissionLogObservation missions,
        string? missionName,
        bool isJobTerminalMission)
    {
        if (!panelPresentation.IsAvailable)
        {
            return string.Concat(
                "Mission-panel observation is unavailable: ",
                panelPresentation.Status);
        }

        if (!details.IsAvailable)
        {
            return string.Concat(
                "Mission-details observation is unavailable: ",
                details.Status);
        }

        if (!details.IsDisplayed)
        {
            return "Waiting for an open mission-details pane.";
        }

        if (details.SelectedMissionAddress == 0)
        {
            return "Mission details are open, but no mission is selected.";
        }

        if (!string.IsNullOrWhiteSpace(missionName))
        {
            return string.Concat(
                isJobTerminalMission
                    ? "Showing job guidance for "
                    : "Showing mission guidance for ",
                missionName,
                ".");
        }

        return missions.IsAvailable
            ? "Mission details are open, but the selected mission name could not be resolved."
            : string.Concat(
                "Mission details are open, but mission-log observation is unavailable: ",
                missions.Status);
    }

    public void ReloadMissionWiki()
    {
        if (this.IsDisposed || this.Disposing)
        {
            return;
        }

        if (this.InvokeRequired)
        {
            try
            {
                this.BeginInvoke(this.ReloadMissionWiki);
            }
            catch (InvalidOperationException)
            {
            }

            return;
        }

        this.missionWikiWebViewForm?.ReloadCurrentMission();
        this.SyncMissionWiki();
    }

    public void SetPermanentTitleStatus(string statusText, bool blink = false)
    {
        if (string.IsNullOrWhiteSpace(statusText))
        {
            this.ClearTitleStatus();
            return;
        }

        this.titleStatusTimer.Stop();

        this.titleStatusText = statusText.Trim();
        this.titleStatusBlinkEnabled = blink;
        this.titleStatusBlinkVisible = true;

        this.UpdateBlinkTimer();
        this.ApplyCurrentHostTitle();
    }

    public void SetTemporaryTitleStatus(
        string statusText,
        TimeSpan duration,
        bool blink = false)
    {
        if (string.IsNullOrWhiteSpace(statusText))
        {
            return;
        }

        if (duration <= TimeSpan.Zero)
        {
            this.SetPermanentTitleStatus(statusText, blink);
            return;
        }

        this.titleStatusTimer.Stop();

        this.titleStatusText = statusText.Trim();
        this.titleStatusBlinkEnabled = blink;
        this.titleStatusBlinkVisible = true;

        this.titleStatusTimer.Interval = Math.Max(
            1,
            (int)Math.Ceiling(duration.TotalMilliseconds));

        this.titleStatusTimer.Start();

        this.UpdateBlinkTimer();
        this.ApplyCurrentHostTitle();
    }

    private void ClearTitleStatus()
    {
        this.titleStatusTimer.Stop();
        this.titleBlinkTimer.Stop();

        this.titleStatusText = null;
        this.titleStatusBlinkEnabled = false;
        this.titleStatusBlinkVisible = true;

        this.ApplyCurrentHostTitle();
    }

    public void SetAddonPresentationState(
        ClientLifecycleState lifecycleState,
        bool isTransitioning)
    {
        if (this.IsDisposed || this.Disposing)
        {
            return;
        }

        this.addonLifecycleState = lifecycleState;
        this.addonTransitioning = isTransitioning;

        if (!this.IsHandleCreated)
        {
            return;
        }

        if (this.InvokeRequired)
        {
            try
            {
                this.BeginInvoke(
                    () => this.SetAddonPresentationState(
                        lifecycleState,
                        isTransitioning));
            }
            catch (InvalidOperationException)
            {
            }

            return;
        }

        this.ApplyAddonPresentationState();
        this.SyncAddonOverlay();
        this.SyncNavigationPresentation();
        this.SyncMissionWiki();
        this.SyncJobTerminalRoute();
        this.SyncFactionDetails();
        this.SyncBuildSkillsCompanion();
        this.SyncBuildEquipmentCompanion();
        this.SyncVendorShoppingCompanion();
        this.SyncGameItemToolTip();
    }

    public void ApplyAddonUiCommand(AddonUiCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (this.IsDisposed || this.Disposing)
        {
            return;
        }

        if (!this.IsHandleCreated)
        {
            lock (this.pendingUiCommandLock)
            {
                this.pendingUiCommands.Add(command);
            }

            return;
        }

        if (this.InvokeRequired)
        {
            try
            {
                this.BeginInvoke(
                    () => this.ApplyAddonUiCommand(command));
            }
            catch (InvalidOperationException)
            {
            }

            return;
        }

        this.EnsureAddonOverlay();
        this.addonOverlayForm!.Apply(command);
        this.SyncAddonOverlay();
    }

    public Task SetAddonOverlayInputSuppressedAsync(
        bool suppressed)
    {
        if (this.IsDisposed || this.Disposing)
        {
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        void ApplySuppression()
        {
            if (this.IsDisposed || this.Disposing)
            {
                completion.TrySetResult(true);
                return;
            }

            this.EnsureAddonOverlay();
            this.addonOverlayForm!.SetInputSuppressed(suppressed);
            this.SyncAddonOverlay();
            completion.TrySetResult(true);
        }

        if (!this.IsHandleCreated)
        {
            completion.TrySetResult(true);
            return completion.Task;
        }

        if (!this.InvokeRequired)
        {
            ApplySuppression();
            return completion.Task;
        }

        try
        {
            this.BeginInvoke(ApplySuppression);
        }
        catch (InvalidOperationException)
        {
            completion.TrySetResult(true);
        }
        return completion.Task;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.CloseHostedClientMenuTour();

            this.titleStatusTimer.Stop();
            this.titleStatusTimer.Tick -= this.TitleStatusTimer_OnTick;
            this.titleStatusTimer.Dispose();

            this.titleBlinkTimer.Stop();
            this.titleBlinkTimer.Tick -= this.TitleBlinkTimer_OnTick;
            this.titleBlinkTimer.Dispose();

            this.titleBarHoverTimer.Stop();
            this.titleBarHoverTimer.Tick -=
                this.TitleBarHoverTimer_OnTick;
            this.titleBarHoverTimer.Dispose();

            this.Load -= this.ClientHostForm_OnLoad;
            this.Shown -= this.ClientHostForm_OnShown;
            this.Resize -= this.ClientHostForm_OnResize;
            this.Move -= this.ClientHostForm_OnMove;
            this.VisibleChanged -= this.ClientHostForm_OnVisibleChanged;
            this.FormClosing -= this.ClientHostForm_OnFormClosing;
            this.skillBuildLocalWorkspace.Changed -=
                this.SkillBuildLocalWorkspace_OnChanged;

            this.titleBar.DragRequested -= this.TitleBar_OnDragRequested;
            this.titleBar.MinimizeRequested -= this.TitleBar_OnMinimizeRequested;
            this.titleBar.CloseRequested -= this.TitleBar_OnCloseRequested;

            if (this.addonOverlayForm != null)
            {
                this.addonOverlayForm.UiInteractionRaised -=
                    this.AddonOverlayForm_OnUiInteractionRaised;

                this.addonOverlayForm.NavigationRequested -=
                    this.AddonOverlayForm_OnNavigationRequested;

                this.addonOverlayForm.GalaxyAtlasRequested -=
                    this.AddonOverlayForm_OnGalaxyAtlasRequested;

                this.addonOverlayForm.ForgeContributionsRequested -=
                    this.AddonOverlayForm_OnForgeContributionsRequested;

                this.addonOverlayForm.PilotArchiveRequested -=
                    this.AddonOverlayForm_OnPilotArchiveRequested;

                this.addonOverlayForm.BuildsRequested -=
                    this.AddonOverlayForm_OnBuildsRequested;

                this.addonOverlayForm.SocialRequested -=
                    this.AddonOverlayForm_OnSocialRequested;

                this.addonOverlayForm.ManageAddonsRequested -=
                    this.AddonOverlayForm_OnManageAddonsRequested;

                this.addonOverlayForm.HelpCenterRequested -=
                    this.AddonOverlayForm_OnHelpCenterRequested;

                this.addonOverlayForm.InGameOptionsRequested -=
                    this.AddonOverlayForm_OnInGameOptionsRequested;

                this.addonOverlayForm.GameMenuOpened -=
                    this.AddonOverlayForm_OnGameMenuOpened;

                this.addonOverlayForm.Close();
                this.addonOverlayForm.Dispose();
            }
            this.addonOverlayForm = null;

            this.navigationInGamePresenter?.Dispose();
            this.navigationInGamePresenter = null;
            this.CloseNavigationCompanion(
                preserveOpenPreference: true);
            this.CloseMissionWikiForm();
            this.CloseJobTerminalRouteForm();
            this.CloseFactionDetailsForm();
            this.CloseBuildBoardForm();
            this.CloseBuildSkillsCompanionForms();
            this.CloseBuildEquipmentCompanionForms();
            this.CloseVendorShoppingCompanionForm();
            this.gameItemToolTip.Dispose();

            lock (this.pendingUiCommandLock)
            {
                this.pendingUiCommands.Clear();
            }
        }

        base.Dispose(disposing);
    }

    private HostedClientTitleBar CreateTitleBar()
    {
        return new HostedClientTitleBar
        {
            Dock = DockStyle.Top,
            Height = HostedClientWindowMetrics.TitleBarHeight,
            ShowHelpButton = false,
            HelpTopicId = HelpTopicIds.InGameTools,
            HelpProcessIdProvider = () => this.clientInstance.ProcessId,
            HelpOverride = () =>
            {
                this.ShowHostedClientTour();
                return true;
            },
        };
    }

    private Panel CreateGamePanel()
    {
        return new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Black,
        };
    }

    private void ApplyInitialBoundsFromGameWindow()
    {
        var bounds = NativeMethods.GetWindowBounds(this.clientInstance.GameWindowHandle);

        if (bounds == null)
        {
            this.StartPosition = FormStartPosition.CenterScreen;
            this.ClientSize = new Size(
                width: 1280,
                height: 720 + this.CurrentTitleBarHeight);
            return;
        }

        this.Location = new Point(bounds.Value.Left, bounds.Value.Top);

        this.ClientSize = new Size(
            width: bounds.Value.Width,
            height: bounds.Value.Height + this.CurrentTitleBarHeight);
    }

    private int CurrentTitleBarHeight =>
        this.titleBarMode == ClientTitleBarMode.Always
            ? HostedClientWindowMetrics.TitleBarHeight
            : 0;

    private void ApplyTitleBarMode(
        ClientTitleBarMode mode,
        decimal hoverDelaySeconds)
    {
        this.titleBarMode = mode;
        this.titleBarHoverDelaySeconds = Math.Clamp(
            hoverDelaySeconds,
            0.10m,
            5.00m);
        this.titleBarHoverStartedAtUtc = null;
        this.titleBarHoverTimer.Stop();

        this.titleBar.Dock = mode == ClientTitleBarMode.Always
            ? DockStyle.Top
            : DockStyle.None;

        this.UpdateHoverTitleBarBounds();

        this.titleBar.Visible = mode == ClientTitleBarMode.Always;
        this.SyncHoverTitleBarOverlayOcclusion();

        if (mode == ClientTitleBarMode.OnHover)
        {
            this.titleBarHoverTimer.Start();
        }

        this.titleBar.BringToFront();
        this.MinimumSize = new Size(
            width: 640,
            height: 480 + this.CurrentTitleBarHeight);
        this.PerformLayout();
    }

    private void UpdateHoverTitleBarBounds()
    {
        if (this.titleBarMode == ClientTitleBarMode.Always)
        {
            return;
        }

        this.titleBar.Bounds = new Rectangle(
            x: 0,
            y: 0,
            width: this.ClientSize.Width,
            height: HostedClientWindowMetrics.TitleBarHeight);
    }

    private void HideHoverTitleBar()
    {
        if (this.titleBarMode != ClientTitleBarMode.OnHover)
        {
            return;
        }

        this.titleBar.Visible = false;
        this.titleBarHoverStartedAtUtc = null;
        this.SyncHoverTitleBarOverlayOcclusion();
    }

    private void SyncHoverTitleBarOverlayOcclusion()
    {
        if (this.addonOverlayForm == null ||
            this.addonOverlayForm.IsDisposed ||
            this.addonOverlayForm.Disposing)
        {
            return;
        }

        var occlusionHeight = 0;

        if (this.titleBarMode == ClientTitleBarMode.OnHover &&
            this.titleBar.Visible)
        {
            // The host and overlay can run at a scaled monitor DPI. Use the
            // actual on-screen overlap instead of the logical 34-pixel
            // design height, otherwise the lower edge of the in-game menu
            // can still paint over the revealed title bar.
            if (this.titleBar.IsHandleCreated &&
                this.addonOverlayForm.IsHandleCreated)
            {
                var titleBarBounds = this.titleBar.RectangleToScreen(
                    this.titleBar.ClientRectangle);
                var overlayBounds =
                    this.addonOverlayForm.RectangleToScreen(
                        this.addonOverlayForm.ClientRectangle);
                var overlap = Rectangle.Intersect(
                    titleBarBounds,
                    overlayBounds);

                if (!overlap.IsEmpty &&
                    overlap.Top <= overlayBounds.Top)
                {
                    occlusionHeight = Math.Clamp(
                        overlap.Bottom - overlayBounds.Top,
                        0,
                        overlayBounds.Height);
                }
            }
            else
            {
                occlusionHeight = this.titleBar.Height;
            }
        }

        this.addonOverlayForm.SetTopOcclusionHeight(
            occlusionHeight);
    }

    private void ApplyCurrentHostTitle()
    {
        var presentation = this.titleService.BuildPresentation(
            this.clientInstance,
            this.appliedSlotName,
            this.titleStatusText,
            !this.titleStatusBlinkEnabled || this.titleStatusBlinkVisible);

        this.Text = presentation.WindowTitle;
        this.titleBar.Presentation = presentation;
    }

    private void UpdateBlinkTimer()
    {
        if (!this.titleStatusBlinkEnabled)
        {
            this.titleBlinkTimer.Stop();
            this.titleStatusBlinkVisible = true;
            return;
        }

        this.titleBlinkTimer.Start();
    }

    private void TitleStatusTimer_OnTick(object? sender, EventArgs e)
    {
        this.ClearTitleStatus();
    }

    private void TitleBlinkTimer_OnTick(object? sender, EventArgs e)
    {
        this.titleStatusBlinkVisible = !this.titleStatusBlinkVisible;
        this.ApplyCurrentHostTitle();
    }

    private void TitleBarHoverTimer_OnTick(object? sender, EventArgs e)
    {
        if (this.titleBarMode != ClientTitleBarMode.OnHover ||
            this.IsDisposed ||
            this.Disposing ||
            !this.IsHandleCreated ||
            !this.Visible ||
            this.WindowState == FormWindowState.Minimized)
        {
            this.HideHoverTitleBar();
            return;
        }

        var cursorPosition = Cursor.Position;

        if (this.titleBar.Visible)
        {
            if (Control.MouseButtons != MouseButtons.None)
            {
                return;
            }

            var titleBarBounds = this.titleBar.RectangleToScreen(
                this.titleBar.ClientRectangle);

            if (!titleBarBounds.Contains(cursorPosition))
            {
                this.HideHoverTitleBar();
            }

            return;
        }

        if (Control.MouseButtons != MouseButtons.None)
        {
            this.titleBarHoverStartedAtUtc = null;
            return;
        }

        var clientPoint = this.PointToClient(cursorPosition);
        var insideRevealZone =
            clientPoint.X >= 0 &&
            clientPoint.X < this.ClientSize.Width &&
            clientPoint.Y >= 0 &&
            clientPoint.Y < Math.Min(
                HoverTitleBarRevealZoneHeight,
                this.ClientSize.Height);

        if (!insideRevealZone)
        {
            this.titleBarHoverStartedAtUtc = null;
            return;
        }

        this.titleBarHoverStartedAtUtc ??= DateTime.UtcNow;

        if ((DateTime.UtcNow - this.titleBarHoverStartedAtUtc.Value)
            .TotalSeconds < (double)this.titleBarHoverDelaySeconds)
        {
            return;
        }

        this.UpdateHoverTitleBarBounds();
        this.titleBar.Visible = true;
        this.SyncHoverTitleBarOverlayOcclusion();
        this.titleBar.BringToFront();
        this.titleBarHoverStartedAtUtc = null;
    }

    private void ClientHostForm_OnLoad(object? sender, EventArgs e)
    {
        var docked = this.clientDockingService.Dock(
            this.clientInstance.GameWindowHandle,
            this.gamePanel.Handle,
            this.gamePanel.ClientSize);

        if (!docked)
        {
            this.closeRequested(this.clientInstance, CloseReason.ProcessExited);
        }
    }

    private void ClientHostForm_OnShown(object? sender, EventArgs e)
    {
        this.BeginInvoke(() =>
        {
            if (this.IsDisposed || this.Disposing)
            {
                return;
            }

            _ = this.clientDockingService.TryResizeDockedWindow(
                this.clientInstance.GameWindowHandle,
                this.gamePanel.ClientSize);

            this.EnsureAddonOverlay();
            this.ApplyAddonPresentationState();
            this.FlushPendingUiCommands();

            this.SyncNavigationPresentation();

            this.SyncAddonOverlay();
            this.SyncMissionWiki();
            this.SyncJobTerminalRoute();
            this.SyncFactionDetails();
            this.SyncBuildSkillsCompanion();
            this.SyncBuildEquipmentCompanion();
            this.SyncVendorShoppingCompanion();
            this.SyncGameItemToolTip();
        });
    }

    private void ClientHostForm_OnResize(object? sender, EventArgs e)
    {
        if (this.IsDisposed || this.Disposing)
        {
            return;
        }

        this.UpdateHoverTitleBarBounds();

        _ = this.clientDockingService.TryResizeDockedWindow(
            this.clientInstance.GameWindowHandle,
            this.gamePanel.ClientSize);

        this.SyncAddonOverlay();
        this.SyncMissionWiki();
        this.SyncJobTerminalRoute();
        this.SyncFactionDetails();
        this.SyncBuildSkillsCompanion();
        this.SyncBuildEquipmentCompanion();
        this.SyncVendorShoppingCompanion();
        this.gameItemToolTip.HideExternal();
        this.SyncGameItemToolTip();
    }

    private void ClientHostForm_OnMove(object? sender, EventArgs e)
    {
        this.SyncAddonOverlay();
        this.SyncMissionWiki();
        this.SyncJobTerminalRoute();
        this.SyncFactionDetails();
        this.SyncBuildSkillsCompanion();
        this.SyncBuildEquipmentCompanion();
        this.SyncVendorShoppingCompanion();
        this.gameItemToolTip.HideExternal();
        this.SyncGameItemToolTip();
    }

    private void ClientHostForm_OnVisibleChanged(
        object? sender,
        EventArgs e)
    {
        this.SyncAddonOverlay();
        this.SyncMissionWiki();
        this.SyncJobTerminalRoute();
        this.SyncFactionDetails();
        this.SyncBuildSkillsCompanion();
        this.SyncBuildEquipmentCompanion();
        this.SyncVendorShoppingCompanion();
        this.SyncGameItemToolTip();
    }

    private void ClientHostForm_OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        this.gameItemToolTip.HideExternal();

        if (this.closeRequestedByManager)
        {
            return;
        }

        this.closeRequested(this.clientInstance, CloseReason.UserRequested);
    }

    private void TitleBar_OnDragRequested(
        object? sender,
        EventArgs e)
    {
        NativeMethods.ReleaseCapture();
        NativeMethods.SendMoveWindowMessage(this.Handle);
    }

    private void TitleBar_OnMinimizeRequested(
        object? sender,
        EventArgs e)
    {
        this.WindowState = FormWindowState.Minimized;
    }

    private void TitleBar_OnCloseRequested(
        object? sender,
        EventArgs e)
    {
        this.Close();
    }

    private void EnsureAddonOverlay()
    {
        if (this.addonOverlayForm is
            {
                IsDisposed: false,
                Disposing: false,
            })
        {
            return;
        }

        this.addonOverlayForm = new AddonOverlayForm(
            this.clientInstance.ProcessId,
            this.resolveAddonWindowPlacement,
            this.saveAddonWindowPlacement);

        this.addonOverlayForm.UiInteractionRaised +=
            this.AddonOverlayForm_OnUiInteractionRaised;

        this.addonOverlayForm.NavigationRequested +=
            this.AddonOverlayForm_OnNavigationRequested;

        this.addonOverlayForm.GalaxyAtlasRequested +=
            this.AddonOverlayForm_OnGalaxyAtlasRequested;

        this.addonOverlayForm.WorldFindRequested +=
            this.AddonOverlayForm_OnWorldFindRequested;

        this.addonOverlayForm.ForgeContributionsRequested +=
            this.AddonOverlayForm_OnForgeContributionsRequested;

        this.addonOverlayForm.PilotArchiveRequested +=
            this.AddonOverlayForm_OnPilotArchiveRequested;

        this.addonOverlayForm.BuildsRequested +=
            this.AddonOverlayForm_OnBuildsRequested;

        this.addonOverlayForm.SocialRequested +=
            this.AddonOverlayForm_OnSocialRequested;

        this.addonOverlayForm.ManageAddonsRequested +=
            this.AddonOverlayForm_OnManageAddonsRequested;

        this.addonOverlayForm.HelpCenterRequested +=
            this.AddonOverlayForm_OnHelpCenterRequested;

        this.addonOverlayForm.InGameOptionsRequested +=
            this.AddonOverlayForm_OnInGameOptionsRequested;

        this.addonOverlayForm.GameMenuOpened +=
            this.AddonOverlayForm_OnGameMenuOpened;

        this.SyncHoverTitleBarOverlayOcclusion();
        this.ApplyAddonPresentationState();
    }

    private void QueueAddonWindowStateReload()
    {
        if (!this.IsHandleCreated ||
            this.IsDisposed ||
            this.Disposing)
        {
            return;
        }

        try
        {
            this.BeginInvoke(() =>
            {
                if (this.IsDisposed ||
                    this.Disposing ||
                    this.addonOverlayForm == null ||
                    this.addonOverlayForm.IsDisposed ||
                    this.addonOverlayForm.Disposing)
                {
                    return;
                }

                this.addonOverlayForm.ReloadWindowState();
                this.SyncAddonOverlay();
            });
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void ApplyAddonPresentationState()
    {
        var inGame =
            this.addonLifecycleState ==
                ClientLifecycleState.InGame;

        var canPresent =
            !this.addonTransitioning;

        this.titleBar.ShowHelpButton = inGame && canPresent;

        if (!inGame || !canPresent)
        {
            this.CloseHostedClientMenuTour();
        }

        if (this.addonOverlayForm == null ||
            this.addonOverlayForm.IsDisposed ||
            this.addonOverlayForm.Disposing)
        {
            return;
        }

        this.addonOverlayForm.SetGameMenuVisible(
            inGame &&
            canPresent);

        this.addonOverlayForm.SetPresentationEnabled(
            canPresent);
    }

    private void FlushPendingUiCommands()
    {
        AddonUiCommand[] commands;

        lock (this.pendingUiCommandLock)
        {
            commands = [.. this.pendingUiCommands];
            this.pendingUiCommands.Clear();
        }

        foreach (var command in commands)
        {
            this.addonOverlayForm!.Apply(command);
        }
    }

    private void RefreshGameItemToolTipPreparation()
    {
        if (!this.gameItemToolTipsEnabled)
        {
            this.gameItemToolTipPreparation = null;
            this.gameItemToolTipRefreshRequestKey = "";
            this.gameItemToolTip.HideExternal();
            return;
        }

        var snapshot = this.gameItemToolTipSnapshot;

        if (snapshot == null)
        {
            this.gameItemToolTipPreparation = null;
            this.gameItemToolTip.HideExternal();
            return;
        }

        var preparation =
            GameItemToolTipPresentationBuilder.Prepare(
                snapshot,
                this.gameItemToolTipHover,
                this.resolveBuildItemIcon);

        this.gameItemToolTipPreparation = preparation;

        if (preparation != null)
        {
            this.gameItemToolTip.PrepareExternal(
                preparation.Key,
                preparation.Content);
        }
        else
        {
            this.gameItemToolTip.HideExternal();
        }

        var shouldRefresh =
            GameItemToolTipPresentationBuilder
                .ShouldRequestAuthoritativeRefresh(
                    snapshot,
                    this.gameItemToolTipHover,
                    preparation);
        var hoverIdentity =
            GameItemToolTipPresentationBuilder.GetHoverIdentity(
                this.gameItemToolTipHover);

        if (shouldRefresh)
        {
            if (!string.Equals(
                    this.gameItemToolTipRefreshRequestKey,
                    hoverIdentity,
                    StringComparison.Ordinal))
            {
                this.gameItemToolTipRefreshRequestKey =
                    hoverIdentity;
                this.requestGameItemToolTipRefresh(
                    this.clientInstance.ProcessId,
                    this.gameItemToolTipHover);
            }
        }
        else
        {
            this.gameItemToolTipRefreshRequestKey = "";
        }
    }

    private void SyncGameItemToolTip()
    {
        var snapshot = this.gameItemToolTipSnapshot;
        var cursorPosition = Cursor.Position;
        var canPresent =
            this.gameItemToolTipsEnabled &&
            snapshot is
            {
                LifecycleState: ClientLifecycleState.InGame,
                LoadingOrTransitionFlag: 0,
            } &&
            this.addonLifecycleState == ClientLifecycleState.InGame &&
            !this.addonTransitioning &&
            this.Visible &&
            this.WindowState != FormWindowState.Minimized &&
            this.gamePanel.ClientSize is
            {
                Width: > 0,
                Height: > 0,
            } &&
            this.gamePanel.RectangleToScreen(
                    this.gamePanel.ClientRectangle)
                .Contains(cursorPosition);

        if (!canPresent || snapshot == null)
        {
            this.gameItemToolTip.HideExternal();
            return;
        }

        var preparation = this.gameItemToolTipPreparation;

        if (preparation == null)
        {
            this.gameItemToolTip.HideExternal();
            return;
        }

        if (!GameItemToolTipPresentationBuilder.CanShow(
                preparation,
                this.gameItemToolTipHover))
        {
            this.gameItemToolTip.HideExternalPreservePreparation();
            return;
        }

        this.gameItemToolTip.ShowExternalOverNative(
            this,
            preparation.Key,
            preparation.Content,
            cursorPosition,
            this.gameItemToolTipOffset);
    }

    private void SyncAddonOverlay()
    {
        if (this.addonOverlayForm == null ||
            this.addonOverlayForm.IsDisposed ||
            this.addonOverlayForm.Disposing)
        {
            return;
        }

        var shouldShow =
            this.Visible &&
            this.WindowState != FormWindowState.Minimized &&
            this.gamePanel.ClientSize is { Width: > 0, Height: > 0 } &&
            this.addonOverlayForm.HasWidgets;

        if (!shouldShow)
        {
            this.addonOverlayForm.Hide();
            return;
        }

        var screenLocation = this.gamePanel.PointToScreen(Point.Empty);

        this.addonOverlayForm.Bounds = new Rectangle(
            screenLocation,
            this.gamePanel.ClientSize);
        this.SyncHoverTitleBarOverlayOcclusion();

        if (!this.addonOverlayForm.Visible)
        {
            this.addonOverlayForm.Show(this);
        }

        this.addonOverlayForm.Invalidate();
    }

    private void SyncMissionWiki()
    {
        var shouldPresent =
            this.missionWikiEnabled &&
            !string.IsNullOrWhiteSpace(
                this.missionWikiMissionName) &&
            this.addonLifecycleState ==
            ClientLifecycleState.InGame &&
            !this.addonTransitioning &&
            this.Visible &&
            this.WindowState != FormWindowState.Minimized &&
            this.gamePanel.ClientSize is { Width: > 0, Height: > 0 };

        if (!shouldPresent)
        {
            this.missionWikiWebViewForm?.Hide();
            this.missionWikiControlForm?.Hide();
            return;
        }

        this.EnsureMissionWikiForms();

        var bounds = this.CalculateMissionWikiBounds();
        this.missionWikiWebViewForm!.Bounds = bounds;
        this.missionWikiControlForm!.Bounds =
            this.CalculateMissionWikiControlBounds(bounds);

        var contentMismatch =
            !string.Equals(
                this.missionWikiWebViewForm.MissionName,
                this.missionWikiMissionName,
                StringComparison.Ordinal) ||
            !string.Equals(
                this.missionWikiWebViewForm.JobGuidanceFingerprint,
                this.missionJobGuidance?.Fingerprint,
                StringComparison.Ordinal);

        if (contentMismatch)
        {
            if (this.missionJobGuidance != null)
            {
                this.missionWikiWebViewForm.NavigateToJob(
                    this.missionJobGuidance);
            }
            else
            {
                this.missionWikiWebViewForm.NavigateToMission(
                    this.missionWikiMissionName!);
            }
        }

        var suppressExpandedWikiForForfeitConfirmation =
            this.missionWikiExpanded &&
            this.missionWikiForfeitConfirmationDisplayed;

        if (suppressExpandedWikiForForfeitConfirmation)
        {
            this.missionWikiWebViewForm.Hide();
            this.missionWikiControlForm.Hide();
            return;
        }

        this.missionWikiControlForm.SetState(
            this.missionWikiExpanded
                ? MissionWikiControlState.Expanded
                : MissionWikiControlState.Collapsed,
            this.missionJobGuidance != null);

        var showBrowser = this.missionWikiExpanded;

        // Owned overlays stay with the hosted client without activation.
        // Never promote them with HWND_TOP; doing so can raise the game
        // above unrelated applications while the user works elsewhere.
        if (showBrowser)
        {
            if (!this.missionWikiWebViewForm.Visible)
            {
                this.missionWikiWebViewForm.Show(this);
            }
        }
        else
        {
            this.missionWikiWebViewForm.Hide();
        }

        if (!this.missionWikiControlForm.Visible)
        {
            this.missionWikiControlForm.Show(this);
        }
    }

    private void EnsureMissionWikiForms()
    {
        if (this.missionWikiWebViewForm is not
            {
                IsDisposed: false,
                Disposing: false,
            })
        {
            this.missionWikiWebViewForm =
                new MissionWikiWebViewForm(
                    this.resolveMissionWikiDestination,
                    this.setMissionWikiDestination);

            this.missionWikiWebViewForm.ContentStateChanged +=
                this.MissionWikiWebViewForm_OnContentStateChanged;
        }

        if (this.missionWikiControlForm is
            {
                IsDisposed: false,
                Disposing: false,
            })
        {
            return;
        }

        this.missionWikiControlForm =
            new MissionWikiControlForm();

        this.missionWikiControlForm.ToggleRequested +=
            this.MissionWikiControlForm_OnToggleRequested;
    }

    private Rectangle CalculateMissionWikiBounds()
    {
        var scaleX = this.gamePanel.ClientSize.Width /
                     (float)MissionWikiBaseCanvasWidth;
        var scaleY = this.gamePanel.ClientSize.Height /
                     (float)MissionWikiBaseCanvasHeight;

        var x = (int)Math.Round(
            MissionWikiBaseX * scaleX);
        var y = (int)Math.Round(
            MissionWikiBaseY * scaleY);
        var width = (int)Math.Round(
            MissionWikiBaseWidth * scaleX);
        var height = (int)Math.Round(
            MissionWikiBaseHeight * scaleY);

        x = Math.Clamp(
            x,
            0,
            Math.Max(0, this.gamePanel.ClientSize.Width - 1));
        y = Math.Clamp(
            y,
            0,
            Math.Max(0, this.gamePanel.ClientSize.Height - 1));

        width = Math.Clamp(
            width,
            1,
            Math.Max(1, this.gamePanel.ClientSize.Width - x));
        height = Math.Clamp(
            height,
            1,
            Math.Max(1, this.gamePanel.ClientSize.Height - y));

        var screenLocation =
            this.gamePanel.PointToScreen(
                new Point(x, y));

        return new Rectangle(
            screenLocation,
            new Size(width, height));
    }

    private Rectangle CalculateMissionWikiControlBounds(
        Rectangle missionWikiBounds)
    {
        var scaleX = missionWikiBounds.Width /
                     (float)MissionWikiBaseWidth;
        var scaleY = missionWikiBounds.Height /
                     (float)MissionWikiBaseHeight;

        var width = Math.Max(
            MissionWikiControlMinimumWidth,
            (int)Math.Round(
                MissionWikiControlBaseWidth * scaleX));
        var height = Math.Max(
            MissionWikiControlMinimumHeight,
            (int)Math.Round(
                MissionWikiControlBaseHeight * scaleY));

        var x = missionWikiBounds.Left +
                (int)Math.Round(
                    MissionWikiControlBaseOffsetX * scaleX);
        var y = missionWikiBounds.Top +
                (int)Math.Round(
                    MissionWikiControlBaseOffsetY * scaleY);

        var panelScreenLocation =
            this.gamePanel.PointToScreen(Point.Empty);
        var panelBounds = new Rectangle(
            panelScreenLocation,
            this.gamePanel.ClientSize);

        width = Math.Min(width, Math.Max(1, panelBounds.Width));
        height = Math.Min(height, Math.Max(1, panelBounds.Height));

        x = Math.Clamp(
            x,
            panelBounds.Left,
            Math.Max(panelBounds.Left, panelBounds.Right - width));
        y = Math.Clamp(
            y,
            panelBounds.Top,
            Math.Max(panelBounds.Top, panelBounds.Bottom - height));

        return new Rectangle(x, y, width, height);
    }

    private void SyncFactionDetails()
    {
        var shouldPresent =
            this.factionDetailsPresentation.IsVisible &&
            this.addonLifecycleState == ClientLifecycleState.InGame &&
            !this.addonTransitioning &&
            this.Visible &&
            this.WindowState != FormWindowState.Minimized &&
            this.gamePanel.ClientSize is { Width: > 0, Height: > 0 };

        if (!shouldPresent)
        {
            this.factionDetailsOverlayForm?.Hide();
            return;
        }

        this.EnsureFactionDetailsForm();

        this.factionDetailsOverlayForm!.Bounds =
            this.CalculateFactionDetailsBounds();
        this.factionDetailsOverlayForm.SetPresentation(
            this.factionDetailsPresentation);

        if (!this.factionDetailsOverlayForm.Visible)
        {
            this.factionDetailsOverlayForm.Show(this);
        }
    }

    private void EnsureFactionDetailsForm()
    {
        if (this.factionDetailsOverlayForm is
            {
                IsDisposed: false,
                Disposing: false,
            })
        {
            return;
        }

        this.factionDetailsOverlayForm =
            new FactionDetailsOverlayForm();
    }

    private Rectangle CalculateFactionDetailsBounds()
    {
        var scaleX = this.gamePanel.ClientSize.Width /
                     (float)FactionDetailsBaseCanvasWidth;
        var scaleY = this.gamePanel.ClientSize.Height /
                     (float)FactionDetailsBaseCanvasHeight;

        var width = Math.Max(
            FactionDetailsMinimumWidth,
            (int)Math.Round(FactionDetailsBaseWidth * scaleX));
        var height = Math.Max(
            FactionDetailsMinimumHeight,
            (int)Math.Round(FactionDetailsBaseHeight * scaleY));
        var x = (int)Math.Round(FactionDetailsBaseX * scaleY);
        var y = (int)Math.Round(FactionDetailsBaseY * scaleY);

        width = Math.Min(width, this.gamePanel.ClientSize.Width);
        height = Math.Min(height, this.gamePanel.ClientSize.Height);
        x = Math.Clamp(
            x,
            0,
            Math.Max(0, this.gamePanel.ClientSize.Width - width));
        y = Math.Clamp(
            y,
            0,
            Math.Max(0, this.gamePanel.ClientSize.Height - height));

        return new Rectangle(
            this.gamePanel.PointToScreen(new Point(x, y)),
            new Size(width, height));
    }

    private void CloseFactionDetailsForm()
    {
        if (this.factionDetailsOverlayForm == null)
        {
            return;
        }

        if (!this.factionDetailsOverlayForm.IsDisposed)
        {
            this.factionDetailsOverlayForm.Close();
            this.factionDetailsOverlayForm.Dispose();
        }

        this.factionDetailsOverlayForm = null;
    }

    private void ShowBuildBoard()
    {
        if (!this.skillBuildBoardPresentation.HasBuildContext ||
            this.skillBuildBoardPresentation.Baseline == null)
        {
            this.SetTemporaryTitleStatus(
                "Builds are waiting for the live character.",
                TimeSpan.FromSeconds(4));
            return;
        }

        this.EnsureBuildBoardForm();
        this.skillBuildBoardForm!.SetPresentation(
            this.skillBuildBoardPresentation);
        this.skillBuildBoardForm.RestorePlacement(this.Bounds);

        if (!this.skillBuildBoardForm.Visible)
        {
            this.skillBuildBoardForm.Show(this);
        }
        else
        {
            this.skillBuildBoardForm.Activate();
        }
    }

    internal bool ShowBuildBoardForHelp(bool openForge)
    {
        if (!this.skillBuildBoardPresentation.HasBuildContext ||
            this.skillBuildBoardPresentation.Baseline == null)
        {
            return false;
        }

        this.ShowBuildBoard();
        var form = this.skillBuildBoardForm;

        if (form == null || form.IsDisposed)
        {
            return false;
        }

        form.BeginInvoke(() =>
        {
            if (openForge)
            {
                form.ShowForgeHelpTour();
            }
            else
            {
                form.ShowHelpTour();
            }
        });

        return true;
    }

    private void ShowHostedClientTour()
    {
        if (this.IsDisposed ||
            this.Disposing ||
            this.addonLifecycleState != ClientLifecycleState.InGame ||
            this.addonTransitioning)
        {
            return;
        }

        if (this.WindowState == FormWindowState.Minimized)
        {
            this.WindowState = FormWindowState.Normal;
        }

        this.Show();
        this.BringToFront();
        this.Activate();
        this.EnsureAddonOverlay();
        this.SyncAddonOverlay();

        this.CloseHostedClientMenuTour();

        var steps = new HostedClientMenuTourStep[]
        {
            new(
                "menu",
                OpenMenu: false,
                "Open the Client Manager menu",
                "The flashing Client Manager tab appears only after your character is fully in game. Select it whenever you want Client Manager tools without leaving Earth & Beyond."),
            new(
                "help",
                OpenMenu: true,
                "Help & Assistance",
                "Open the full Help Center to discover features, read focused explanations, or run setup checks for this client."),
            new(
                "options",
                OpenMenu: true,
                "Options",
                "Choose how Client Manager behaves for this hosted client, including overlays, histories, tooltips, and other in-game helpers."),
            new(
                "navigation",
                OpenMenu: true,
                "Navigation",
                "Open built-in Navigation in its last chosen presentation. Keep it over Earth & Beyond, or pop it into a desktop companion beside the game or on another monitor."),
            new(
                "atlas",
                OpenMenu: true,
                "Galaxy Atlas",
                "Explore the galaxy visually, inspect systems and destinations, follow pilots, and send a selected location straight to Navigation."),
            new(
                "finder",
                OpenMenu: true,
                "Galaxy Finder",
                "Search for places, NPCs, mobs, harvestables, equipment, components, recipes, vendors, and other useful sources."),
            new(
                "social",
                OpenMenu: true,
                "Social",
                "Control your online presence, choose how precisely your location is shared, find a guild, or advertise guild recruitment."),
            new(
                "archive",
                OpenMenu: true,
                "Pilot Archive",
                "Review the saved equipment, inventory, vault, missions, reputation, activity, and combat history collected for your pilots."),
            new(
                "builds",
                OpenMenu: true,
                "Builds",
                "Open your build workspace to compare planned equipment and skills with the pilot currently using this client."),
            new(
                "contributions",
                OpenMenu: true,
                "Forge Contributions",
                "Choose whether this installation shares supported discoveries with the community Forge dataset, and review exactly what is shared."),
            new(
                "addons",
                OpenMenu: true,
                "Addon Center and addon controls",
                "Discover and manage addons in the Addon Center. Installed addons can also add their own windows and visibility switches beneath the main menu entries."),
        };

        this.hostedClientMenuTourForm = new HostedClientMenuTourForm(
            this,
            steps,
            this.PrepareHostedClientMenuTourStep,
            this.HostedClientMenuTour_OnClosed);
        this.hostedClientMenuTourForm.StartTour();
    }

    private void PrepareHostedClientMenuTourStep(
        HostedClientMenuTourStep step)
    {
        this.EnsureAddonOverlay();
        this.SyncAddonOverlay();
        _ = this.ShowBuiltInMenuGuidance(
            step.MenuItem,
            step.OpenMenu,
            TimeSpan.FromMinutes(5));
    }

    private void HostedClientMenuTour_OnClosed()
    {
        this.hostedClientMenuTourForm = null;
        this.addonOverlayForm?.CloseBuiltInMenuGuidance();

        if (!this.IsDisposed && !this.Disposing)
        {
            this.Activate();
        }
    }

    private void CloseHostedClientMenuTour()
    {
        var form = this.hostedClientMenuTourForm;
        this.hostedClientMenuTourForm = null;

        if (form != null && !form.IsDisposed)
        {
            form.Close();
            form.Dispose();
        }

        this.addonOverlayForm?.CloseBuiltInMenuGuidance();
    }

    internal bool ShowBuiltInMenuGuidance(
        string item,
        bool openMenu = true,
        TimeSpan? duration = null)
    {
        if (this.addonOverlayForm?.ShowBuiltInMenuGuidance(
                item,
                openMenu,
                duration) != true)
        {
            return false;
        }

        if (this.WindowState == FormWindowState.Minimized)
        {
            this.WindowState = FormWindowState.Normal;
        }

        this.Show();
        this.BringToFront();
        this.Activate();
        return true;
    }

    private void EnsureBuildBoardForm()
    {
        if (this.skillBuildBoardForm is
            {
                IsDisposed: false,
                Disposing: false,
            })
        {
            return;
        }

        this.skillBuildBoardForm =
            new SkillBuildBoardForm(
                this.skillBuildLocalWorkspace,
                this.forgeContributionCoordinator,
                this.resolveAddonWindowPlacement,
                this.saveAddonWindowPlacement,
                this.resolveBuildItemIcon);
        this.skillBuildBoardForm.FormClosed +=
            this.SkillBuildBoardForm_OnFormClosed;
    }

    private void SkillBuildBoardForm_OnFormClosed(
        object? sender,
        FormClosedEventArgs e)
    {
        if (this.skillBuildBoardForm != null)
        {
            this.skillBuildBoardForm.FormClosed -=
                this.SkillBuildBoardForm_OnFormClosed;
        }

        this.skillBuildBoardForm = null;
    }

    private void CloseBuildBoardForm()
    {
        var form = this.skillBuildBoardForm;
        this.skillBuildBoardForm = null;
        if (form == null)
        {
            return;
        }

        form.FormClosed -= this.SkillBuildBoardForm_OnFormClosed;
        if (form.IsDisposed)
        {
            return;
        }

        try
        {
            form.Close();
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[Builds] Could not close the Build Board: {exception}");
        }

        try
        {
            form.Dispose();
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[Builds] Could not dispose the Build Board: {exception}");
        }
    }

    private void SyncBuildSkillsCompanion()
    {
        if (this.IsDisposed || this.Disposing || !this.IsHandleCreated)
        {
            return;
        }

        if (this.InvokeRequired)
        {
            try
            {
                this.BeginInvoke(this.SyncBuildSkillsCompanion);
            }
            catch (InvalidOperationException)
            {
            }

            return;
        }

        try
        {
            this.ApplyBuildSkillsCompanionState();
        }
        catch (Exception exception)
        {
            Debug.WriteLine(string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"[Builds] Could not synchronize the Skills companion: {exception}"));
            this.TryHideBuildSkillsCompanionSurfaces();
        }
    }

    private void ApplyBuildSkillsCompanionState()
    {
        var skillsPanelOpen =
            this.skillPlannerPanelPresentation.IsAvailable &&
            this.skillPlannerPanelPresentation.IsCharacterInfoDisplayed &&
            this.skillPlannerPanelPresentation.ActiveCharacterInfoTab ==
                ClientCharacterInfoTab.Skills;
        var shouldPresent =
            skillsPanelOpen &&
            this.addonLifecycleState == ClientLifecycleState.InGame &&
            !this.addonTransitioning &&
            this.Visible &&
            this.WindowState != FormWindowState.Minimized &&
            this.gamePanel.ClientSize is { Width: > 0, Height: > 0 };

        if (!shouldPresent)
        {
            this.TryHideBuildSkillsCompanionSurfaces();
            return;
        }

        this.EnsureBuildSkillsToggleForm();
        var companionVisible =
            this.ResolveBuildSkillsCompanionVisibility();
        this.skillBuildSkillsToggleForm!.SetScale(
            this.CalculateBuildToggleScale());
        this.skillBuildSkillsToggleForm.Bounds =
            this.CalculateBuildSkillsToggleBounds();
        this.skillBuildSkillsToggleForm.SetActive(companionVisible);
        if (!this.skillBuildSkillsToggleForm.Visible)
        {
            this.skillBuildSkillsToggleForm.Show(this);
        }

        if (!companionVisible)
        {
            this.TryHideBuildSkillsCompanionForm();
            return;
        }

        var sourceFingerprint = string.Concat(
            this.skillBuildBoardPresentation.Fingerprint,
            "|workspace:",
            this.skillBuildSkillsWorkspaceRevision.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
        if (!string.Equals(
                this.skillBuildSkillsCompanionSourceFingerprint,
                sourceFingerprint,
                StringComparison.Ordinal))
        {
            this.skillBuildSkillsCompanionPresentation =
                SkillBuildSkillsCompanionPresentation.Create(
                    this.skillBuildBoardPresentation,
                    this.skillBuildLocalWorkspace);
            this.skillBuildSkillsCompanionSourceFingerprint =
                sourceFingerprint;
        }
        this.EnsureBuildSkillsCompanionForm();
        this.skillBuildSkillsCompanionForm!.SetPresentation(
            this.skillBuildSkillsCompanionPresentation);
        this.skillBuildSkillsCompanionForm.Bounds =
            this.CalculateBuildSkillsCompanionBounds();
        if (!this.skillBuildSkillsCompanionForm.Visible)
        {
            this.skillBuildSkillsCompanionForm.Show(this);
        }
    }

    private bool ResolveBuildSkillsCompanionVisibility()
    {
        if (this.skillBuildSkillsCompanionVisible.HasValue)
        {
            return this.skillBuildSkillsCompanionVisible.Value;
        }

        var placement = this.resolveAddonWindowPlacement(
            BuildSkillsCompanionPlacementAddonId,
            BuildSkillsCompanionPlacementWidgetId);
        this.skillBuildSkillsCompanionVisible =
            placement?.IsVisible ?? false;
        return this.skillBuildSkillsCompanionVisible.Value;
    }

    private void SaveBuildSkillsCompanionVisibility(bool visible)
    {
        this.skillBuildSkillsCompanionVisible = visible;
        this.saveAddonWindowPlacement(
            BuildSkillsCompanionPlacementAddonId,
            BuildSkillsCompanionPlacementWidgetId,
            new AddonWindowPlacement
            {
                AddonId = BuildSkillsCompanionPlacementAddonId,
                WidgetId = BuildSkillsCompanionPlacementWidgetId,
                IsVisible = visible,
                IsClosed = false,
            });
    }

    private void EnsureBuildSkillsToggleForm()
    {
        if (this.skillBuildSkillsToggleForm is
            {
                IsDisposed: false,
                Disposing: false,
            })
        {
            return;
        }

        this.skillBuildSkillsToggleForm =
            new BuildCompanionToggleForm("Build");
        this.skillBuildSkillsToggleForm.ToggleRequested +=
            this.BuildSkillsToggleForm_OnToggleRequested;
    }

    private void EnsureBuildSkillsCompanionForm()
    {
        if (this.skillBuildSkillsCompanionForm is
            {
                IsDisposed: false,
                Disposing: false,
            })
        {
            return;
        }

        this.skillBuildSkillsCompanionForm =
            new SkillBuildSkillsCompanionForm();
        this.skillBuildSkillsCompanionForm.OpenBuildsRequested +=
            this.BuildSkillsCompanionForm_OnOpenBuildsRequested;
    }

    private Rectangle CalculateBuildSkillsCompanionBounds()
    {
        var scaleX = this.gamePanel.ClientSize.Width /
                     (float)BuildSkillsCompanionBaseCanvasWidth;
        var scaleY = this.gamePanel.ClientSize.Height /
                     (float)BuildSkillsCompanionBaseCanvasHeight;
        var width = Math.Max(
            BuildSkillsCompanionMinimumWidth,
            (int)Math.Round(BuildSkillsCompanionBaseWidth * scaleX));
        var x = (int)Math.Round(BuildSkillsCompanionBaseX * scaleX);
        var y = (int)Math.Round(BuildSkillsCompanionBaseY * scaleY);

        width = Math.Min(width, this.gamePanel.ClientSize.Width);
        x = Math.Clamp(
            x,
            0,
            Math.Max(0, this.gamePanel.ClientSize.Width - width));
        y = Math.Clamp(
            y,
            0,
            Math.Max(0, this.gamePanel.ClientSize.Height - 1));

        var availableHeight = Math.Max(
            1,
            this.gamePanel.ClientSize.Height - y);
        var height = this.skillBuildSkillsCompanionForm?.GetPreferredHeight(
                availableHeight) ??
            Math.Min(
                availableHeight,
                BuildSkillsCompanionMinimumHeight);

        return new Rectangle(
            this.gamePanel.PointToScreen(new Point(x, y)),
            new Size(width, height));
    }

    private float CalculateBuildToggleScale()
    {
        var scaleX = this.gamePanel.ClientSize.Width /
                     (float)BuildSkillsCompanionBaseCanvasWidth;
        var scaleY = this.gamePanel.ClientSize.Height /
                     (float)BuildSkillsCompanionBaseCanvasHeight;

        return Math.Clamp(
            Math.Min(scaleX, scaleY),
            0.5f,
            2f);
    }

    private Rectangle CalculateBuildSkillsToggleBounds()
    {
        var scaleX = this.gamePanel.ClientSize.Width /
                     (float)BuildSkillsCompanionBaseCanvasWidth;
        var scaleY = this.gamePanel.ClientSize.Height /
                     (float)BuildSkillsCompanionBaseCanvasHeight;
        var width = Math.Max(
            BuildSkillsToggleMinimumWidth,
            (int)Math.Round(BuildSkillsToggleBaseWidth * scaleX));
        var height = Math.Max(
            BuildSkillsToggleMinimumHeight,
            (int)Math.Round(BuildSkillsToggleBaseHeight * scaleY));
        var x = (int)Math.Round(BuildSkillsToggleBaseX * scaleX);
        var y = (int)Math.Round(BuildSkillsToggleBaseY * scaleY);

        width = Math.Min(width, this.gamePanel.ClientSize.Width);
        height = Math.Min(height, this.gamePanel.ClientSize.Height);
        x = Math.Clamp(
            x,
            0,
            Math.Max(0, this.gamePanel.ClientSize.Width - width));
        y = Math.Clamp(
            y,
            0,
            Math.Max(0, this.gamePanel.ClientSize.Height - height));

        return new Rectangle(
            this.gamePanel.PointToScreen(new Point(x, y)),
            new Size(width, height));
    }

    private void BuildSkillsToggleForm_OnToggleRequested(
        object? sender,
        EventArgs e)
    {
        try
        {
            this.SaveBuildSkillsCompanionVisibility(
                !this.ResolveBuildSkillsCompanionVisibility());
            this.SyncBuildSkillsCompanion();
        }
        catch (Exception exception)
        {
            Debug.WriteLine(string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"[Builds] Could not toggle the Skills companion: {exception}"));
            this.TryHideBuildSkillsCompanionForm();
        }
    }

    private void BuildSkillsCompanionForm_OnOpenBuildsRequested(
        object? sender,
        EventArgs e)
    {
        try
        {
            this.ShowBuildBoard();
        }
        catch (Exception exception)
        {
            Debug.WriteLine(string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"[Builds] Could not open the Build Board from the Skills companion: {exception}"));
            this.SetTemporaryTitleStatus(
                "Builds could not open. The game client is still running.",
                TimeSpan.FromSeconds(5));
        }
    }

    private void SkillBuildLocalWorkspace_OnChanged(
        object? sender,
        EventArgs e)
    {
        if (this.IsDisposed || this.Disposing)
        {
            return;
        }

        if (this.InvokeRequired)
        {
            try
            {
                this.BeginInvoke(() =>
                    this.SkillBuildLocalWorkspace_OnChanged(sender, e));
            }
            catch (InvalidOperationException)
            {
            }

            return;
        }

        this.skillBuildSkillsWorkspaceRevision++;
        this.skillBuildSkillsCompanionSourceFingerprint = "";
        this.skillBuildEquipmentWorkspaceRevision++;
        this.skillBuildEquipmentCompanionSourceFingerprint = "";
        this.SyncBuildSkillsCompanion();
        this.SyncBuildEquipmentCompanion();
    }

    private void TryHideBuildSkillsCompanionSurfaces()
    {
        this.TryHideBuildSkillsCompanionForm();
        this.TryHideBuildSkillsToggleForm();
    }

    private void TryHideBuildSkillsCompanionForm()
    {
        var form = this.skillBuildSkillsCompanionForm;
        if (form == null || form.IsDisposed || form.Disposing)
        {
            return;
        }

        try
        {
            form.Hide();
        }
        catch (Exception exception)
        {
            Debug.WriteLine(string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"[Builds] Could not hide the Skills companion: {exception}"));
        }
    }

    private void TryHideBuildSkillsToggleForm()
    {
        var form = this.skillBuildSkillsToggleForm;
        if (form == null || form.IsDisposed || form.Disposing)
        {
            return;
        }

        try
        {
            form.Hide();
        }
        catch (Exception exception)
        {
            Debug.WriteLine(string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"[Builds] Could not hide the Skills Build toggle: {exception}"));
        }
    }

    private void CloseBuildSkillsCompanionForms()
    {
        var companion = this.skillBuildSkillsCompanionForm;
        this.skillBuildSkillsCompanionForm = null;
        if (companion != null)
        {
            companion.OpenBuildsRequested -=
                this.BuildSkillsCompanionForm_OnOpenBuildsRequested;
            this.TryCloseBuildCompanionForm(
                companion,
                "Skills companion");
        }

        var toggle = this.skillBuildSkillsToggleForm;
        this.skillBuildSkillsToggleForm = null;
        if (toggle == null)
        {
            return;
        }

        toggle.ToggleRequested -=
            this.BuildSkillsToggleForm_OnToggleRequested;
        this.TryCloseBuildCompanionForm(
            toggle,
            "Skills Build toggle");
    }

    private void SyncBuildEquipmentCompanion()
    {
        if (this.IsDisposed || this.Disposing || !this.IsHandleCreated)
        {
            return;
        }

        if (this.InvokeRequired)
        {
            try
            {
                this.BeginInvoke(this.SyncBuildEquipmentCompanion);
            }
            catch (InvalidOperationException)
            {
            }

            return;
        }

        try
        {
            this.ApplyBuildEquipmentCompanionState();
        }
        catch (Exception exception)
        {
            Debug.WriteLine(string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"[Builds] Could not synchronize the Equipment companion: {exception}"));
            this.TryHideBuildEquipmentCompanionSurfaces();
        }
    }

    private void ApplyBuildEquipmentCompanionState()
    {
        var equipmentPanelOpen =
            this.skillPlannerPanelPresentation.IsAvailable &&
            this.skillPlannerPanelPresentation.IsInventoryDisplayed;
        var shouldPresent =
            equipmentPanelOpen &&
            this.addonLifecycleState == ClientLifecycleState.InGame &&
            !this.addonTransitioning &&
            this.Visible &&
            this.WindowState != FormWindowState.Minimized &&
            this.gamePanel.ClientSize is { Width: > 0, Height: > 0 };

        if (!shouldPresent)
        {
            this.TryHideBuildEquipmentCompanionSurfaces();
            return;
        }

        this.EnsureBuildEquipmentToggleForm();
        var companionVisible =
            this.ResolveBuildEquipmentCompanionVisibility();
        this.skillBuildEquipmentToggleForm!.SetScale(
            this.CalculateBuildToggleScale());
        this.skillBuildEquipmentToggleForm.Bounds =
            this.CalculateBuildEquipmentToggleBounds();
        this.skillBuildEquipmentToggleForm.SetActive(companionVisible);
        if (!this.skillBuildEquipmentToggleForm.Visible)
        {
            this.skillBuildEquipmentToggleForm.Show(this);
        }

        if (!companionVisible)
        {
            this.TryHideBuildEquipmentCompanionForm();
            return;
        }

        var sourceFingerprint = string.Concat(
            this.skillBuildBoardPresentation.Fingerprint,
            "|workspace:",
            this.skillBuildEquipmentWorkspaceRevision.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
        if (!string.Equals(
                this.skillBuildEquipmentCompanionSourceFingerprint,
                sourceFingerprint,
                StringComparison.Ordinal))
        {
            this.skillBuildEquipmentCompanionPresentation =
                SkillBuildEquipmentCompanionPresentation.Create(
                    this.skillBuildBoardPresentation,
                    this.skillBuildLocalWorkspace);
            this.skillBuildEquipmentCompanionSourceFingerprint =
                sourceFingerprint;
        }

        this.EnsureBuildEquipmentCompanionForm();
        this.skillBuildEquipmentCompanionForm!.SetPresentation(
            this.skillBuildEquipmentCompanionPresentation);
        this.skillBuildEquipmentCompanionForm.Bounds =
            this.CalculateBuildEquipmentCompanionBounds();
        if (!this.skillBuildEquipmentCompanionForm.Visible)
        {
            this.skillBuildEquipmentCompanionForm.Show(this);
        }

    }

    private bool ResolveBuildEquipmentCompanionVisibility()
    {
        if (this.skillBuildEquipmentCompanionVisible.HasValue)
        {
            return this.skillBuildEquipmentCompanionVisible.Value;
        }

        var placement = this.resolveAddonWindowPlacement(
            BuildEquipmentCompanionPlacementAddonId,
            BuildEquipmentCompanionPlacementWidgetId);
        this.skillBuildEquipmentCompanionVisible =
            placement?.IsVisible ?? false;
        return this.skillBuildEquipmentCompanionVisible.Value;
    }

    private void SaveBuildEquipmentCompanionVisibility(bool visible)
    {
        this.skillBuildEquipmentCompanionVisible = visible;
        this.saveAddonWindowPlacement(
            BuildEquipmentCompanionPlacementAddonId,
            BuildEquipmentCompanionPlacementWidgetId,
            new AddonWindowPlacement
            {
                AddonId = BuildEquipmentCompanionPlacementAddonId,
                WidgetId = BuildEquipmentCompanionPlacementWidgetId,
                IsVisible = visible,
                IsClosed = false,
            });
    }

    private void EnsureBuildEquipmentToggleForm()
    {
        if (this.skillBuildEquipmentToggleForm is
            {
                IsDisposed: false,
                Disposing: false,
            })
        {
            return;
        }

        this.skillBuildEquipmentToggleForm =
            new BuildCompanionToggleForm(
                "Build>>",
                BuildCompanionToggleStyle.EquipmentStack);
        this.skillBuildEquipmentToggleForm.ToggleRequested +=
            this.BuildEquipmentToggleForm_OnToggleRequested;
    }

    private void EnsureBuildEquipmentCompanionForm()
    {
        if (this.skillBuildEquipmentCompanionForm is
            {
                IsDisposed: false,
                Disposing: false,
            })
        {
            return;
        }

        this.skillBuildEquipmentCompanionForm =
            new SkillBuildEquipmentCompanionForm(
                this.resolveBuildItemIcon);
        this.skillBuildEquipmentCompanionForm.OpenBuildsRequested +=
            this.BuildEquipmentCompanionForm_OnOpenBuildsRequested;
        this.skillBuildEquipmentCompanionForm.FindItemRequested +=
            this.BuildEquipmentCompanionForm_OnFindItemRequested;
    }

    private Rectangle CalculateBuildEquipmentCompanionBounds()
    {
        var scaleX = this.gamePanel.ClientSize.Width /
                     (float)BuildEquipmentCompanionBaseCanvasWidth;
        var scaleY = this.gamePanel.ClientSize.Height /
                     (float)BuildEquipmentCompanionBaseCanvasHeight;
        var width = Math.Max(
            BuildEquipmentCompanionMinimumWidth,
            (int)Math.Round(BuildEquipmentCompanionBaseWidth * scaleX));
        var x = (int)Math.Round(BuildEquipmentCompanionBaseX * scaleX);
        var y = (int)Math.Round(BuildEquipmentCompanionBaseY * scaleY);

        width = Math.Min(width, this.gamePanel.ClientSize.Width);
        x = Math.Clamp(
            x,
            0,
            Math.Max(0, this.gamePanel.ClientSize.Width - width));
        y = Math.Clamp(
            y,
            0,
            Math.Max(0, this.gamePanel.ClientSize.Height - 1));

        var availableHeight = Math.Max(
            1,
            this.gamePanel.ClientSize.Height - y);
        var height = this.skillBuildEquipmentCompanionForm?.GetPreferredHeight(
                availableHeight) ??
            Math.Min(
                availableHeight,
                BuildEquipmentCompanionMinimumHeight);

        return new Rectangle(
            this.gamePanel.PointToScreen(new Point(x, y)),
            new Size(width, height));
    }

    private Rectangle CalculateBuildEquipmentToggleBounds()
    {
        var scaleX = this.gamePanel.ClientSize.Width /
                     (float)BuildEquipmentCompanionBaseCanvasWidth;
        var scaleY = this.gamePanel.ClientSize.Height /
                     (float)BuildEquipmentCompanionBaseCanvasHeight;
        var width = Math.Max(
            BuildEquipmentToggleMinimumWidth,
            (int)Math.Round(BuildEquipmentToggleBaseWidth * scaleX));
        var height = Math.Max(
            BuildEquipmentToggleMinimumHeight,
            (int)Math.Round(BuildEquipmentToggleBaseHeight * scaleY));
        var x = (int)Math.Round(BuildEquipmentToggleBaseX * scaleX);
        var baseY = this.gameItemToolTipSnapshot?.World.Environment ==
                    ClientWorldEnvironment.Starbase
            ? BuildEquipmentToggleStarbaseBaseY
            : BuildEquipmentToggleUndockedBaseY;
        var y = (int)Math.Round(baseY * scaleY);

        width = Math.Min(width, this.gamePanel.ClientSize.Width);
        height = Math.Min(height, this.gamePanel.ClientSize.Height);
        x = Math.Clamp(
            x,
            0,
            Math.Max(0, this.gamePanel.ClientSize.Width - width));
        y = Math.Clamp(
            y,
            0,
            Math.Max(0, this.gamePanel.ClientSize.Height - height));

        return new Rectangle(
            this.gamePanel.PointToScreen(new Point(x, y)),
            new Size(width, height));
    }

    private void BuildEquipmentToggleForm_OnToggleRequested(
        object? sender,
        EventArgs e)
    {
        try
        {
            this.SaveBuildEquipmentCompanionVisibility(
                !this.ResolveBuildEquipmentCompanionVisibility());
            this.SyncBuildEquipmentCompanion();
        }
        catch (Exception exception)
        {
            Debug.WriteLine(string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"[Builds] Could not toggle the Equipment companion: {exception}"));
            this.TryHideBuildEquipmentCompanionForm();
        }
    }

    private void BuildEquipmentCompanionForm_OnOpenBuildsRequested(
        object? sender,
        EventArgs e)
    {
        try
        {
            this.ShowBuildBoard();
        }
        catch (Exception exception)
        {
            Debug.WriteLine(string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"[Builds] Could not open the Build Board from the Equipment companion: {exception}"));
            this.SetTemporaryTitleStatus(
                "Builds could not open. The game client is still running.",
                TimeSpan.FromSeconds(5));
        }
    }

    private void BuildEquipmentCompanionForm_OnFindItemRequested(
        object? sender,
        SkillBuildEquipmentFindRequestedEventArgs e)
    {
        try
        {
            this.openWorldFindRequested(
                this.clientInstance,
                e.ItemName);
        }
        catch (Exception exception)
        {
            Debug.WriteLine(string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"[Builds] Could not find {e.ItemName} from the Equipment companion: {exception}"));
            this.SetTemporaryTitleStatus(
                "Galaxy Finder could not open. The game client is still running.",
                TimeSpan.FromSeconds(5));
        }
    }

    private void TryHideBuildEquipmentCompanionSurfaces()
    {
        this.TryHideBuildEquipmentCompanionForm();
        this.TryHideBuildEquipmentToggleForm();
    }

    private void TryHideBuildEquipmentCompanionForm()
    {
        var form = this.skillBuildEquipmentCompanionForm;
        if (form == null || form.IsDisposed || form.Disposing)
        {
            return;
        }

        try
        {
            form.Hide();
        }
        catch (Exception exception)
        {
            Debug.WriteLine(string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"[Builds] Could not hide the Equipment companion: {exception}"));
        }
    }

    private void TryHideBuildEquipmentToggleForm()
    {
        var form = this.skillBuildEquipmentToggleForm;
        if (form == null || form.IsDisposed || form.Disposing)
        {
            return;
        }

        try
        {
            form.Hide();
        }
        catch (Exception exception)
        {
            Debug.WriteLine(string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"[Builds] Could not hide the Equipment Build toggle: {exception}"));
        }
    }

    private void CloseBuildEquipmentCompanionForms()
    {
        var companion = this.skillBuildEquipmentCompanionForm;
        this.skillBuildEquipmentCompanionForm = null;
        if (companion != null)
        {
            companion.OpenBuildsRequested -=
                this.BuildEquipmentCompanionForm_OnOpenBuildsRequested;
            companion.FindItemRequested -=
                this.BuildEquipmentCompanionForm_OnFindItemRequested;
            this.TryCloseBuildCompanionForm(
                companion,
                "Equipment companion");
        }

        var toggle = this.skillBuildEquipmentToggleForm;
        this.skillBuildEquipmentToggleForm = null;
        if (toggle == null)
        {
            return;
        }

        toggle.ToggleRequested -=
            this.BuildEquipmentToggleForm_OnToggleRequested;
        this.TryCloseBuildCompanionForm(
            toggle,
            "Equipment Build toggle");
    }

    private void TryCloseBuildCompanionForm(
        Form form,
        string surfaceName)
    {
        if (!form.IsDisposed)
        {
            try
            {
                form.Close();
            }
            catch (Exception exception)
            {
                Debug.WriteLine(string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"[Builds] Could not close the {surfaceName}: {exception}"));
            }
        }

        if (form.IsDisposed)
        {
            return;
        }

        try
        {
            form.Dispose();
        }
        catch (Exception exception)
        {
            Debug.WriteLine(string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"[Builds] Could not dispose the {surfaceName}: {exception}"));
        }
    }

    private void SyncJobTerminalRoute()
    {
        var shouldPresent =
            this.jobTerminalRoutePresentation.IsVisible &&
            this.addonLifecycleState == ClientLifecycleState.InGame &&
            !this.addonTransitioning &&
            this.Visible &&
            this.WindowState != FormWindowState.Minimized &&
            this.gamePanel.ClientSize is { Width: > 0, Height: > 0 };

        if (!shouldPresent)
        {
            this.jobTerminalRouteControlForm?.Hide();
            return;
        }

        this.EnsureJobTerminalRouteForm();

        this.jobTerminalRouteControlForm!.Bounds =
            this.CalculateJobTerminalRouteBounds();
        this.jobTerminalRouteControlForm.SetPresentation(
            this.jobTerminalRoutePresentation);

        if (!this.jobTerminalRouteControlForm.Visible)
        {
            this.jobTerminalRouteControlForm.Show(this);
        }
    }

    private void EnsureJobTerminalRouteForm()
    {
        if (this.jobTerminalRouteControlForm is
            {
                IsDisposed: false,
                Disposing: false,
            })
        {
            return;
        }

        this.jobTerminalRouteControlForm =
            new JobTerminalRouteControlForm();
        this.jobTerminalRouteControlForm.SetDestinationRequested +=
            this.JobTerminalRouteControlForm_OnSetDestinationRequested;
    }

    private Rectangle CalculateJobTerminalRouteBounds()
    {
        var scaleX = this.gamePanel.ClientSize.Width /
                     (float)JobTerminalRouteBaseCanvasWidth;
        var scaleY = this.gamePanel.ClientSize.Height /
                     (float)JobTerminalRouteBaseCanvasHeight;

        var width = Math.Max(
            JobTerminalRouteMinimumWidth,
            (int)Math.Round(JobTerminalRouteBaseWidth * scaleX));
        var height = Math.Max(
            JobTerminalRouteMinimumHeight,
            (int)Math.Round(JobTerminalRouteBaseHeight * scaleY));
        var x = (int)Math.Round(JobTerminalRouteBaseX * scaleX);
        var y = (int)Math.Round(JobTerminalRouteBaseY * scaleY);

        width = Math.Min(
            width,
            Math.Max(1, this.gamePanel.ClientSize.Width));
        height = Math.Min(
            height,
            Math.Max(1, this.gamePanel.ClientSize.Height));
        x = Math.Clamp(
            x,
            0,
            Math.Max(0, this.gamePanel.ClientSize.Width - width));
        y = Math.Clamp(
            y,
            0,
            Math.Max(0, this.gamePanel.ClientSize.Height - height));

        return new Rectangle(
            this.gamePanel.PointToScreen(new Point(x, y)),
            new Size(width, height));
    }

    private void CloseJobTerminalRouteForm()
    {
        if (this.jobTerminalRouteControlForm == null)
        {
            return;
        }

        this.jobTerminalRouteControlForm.SetDestinationRequested -=
            this.JobTerminalRouteControlForm_OnSetDestinationRequested;

        if (!this.jobTerminalRouteControlForm.IsDisposed)
        {
            this.jobTerminalRouteControlForm.Close();
            this.jobTerminalRouteControlForm.Dispose();
        }

        this.jobTerminalRouteControlForm = null;
    }

    private void JobTerminalRouteControlForm_OnSetDestinationRequested(
        NavigationDestination destination)
    {
        var result = this.setMissionWikiDestination(destination);

        if (result.Succeeded)
        {
            var destinationName = string.IsNullOrWhiteSpace(
                    this.jobTerminalRoutePresentation.DestinationName)
                ? destination.DisplayName
                : this.jobTerminalRoutePresentation.DestinationName;

            this.SetTemporaryTitleStatus(
                $"Destination set: {destinationName}",
                TimeSpan.FromSeconds(3));
            return;
        }

        this.SetTemporaryTitleStatus(
            string.IsNullOrWhiteSpace(result.Error)
                ? "Destination could not be set."
                : result.Error,
            TimeSpan.FromSeconds(5));
    }

    private void CloseMissionWikiForm()
    {
        if (this.missionWikiWebViewForm != null)
        {
            this.missionWikiWebViewForm.ContentStateChanged -=
                this.MissionWikiWebViewForm_OnContentStateChanged;

            if (!this.missionWikiWebViewForm.IsDisposed)
            {
                this.missionWikiWebViewForm.Close();
                this.missionWikiWebViewForm.Dispose();
            }

            this.missionWikiWebViewForm = null;
        }

        if (this.missionWikiControlForm != null)
        {
            this.missionWikiControlForm.ToggleRequested -=
                this.MissionWikiControlForm_OnToggleRequested;

            if (!this.missionWikiControlForm.IsDisposed)
            {
                this.missionWikiControlForm.Close();
                this.missionWikiControlForm.Dispose();
            }

            this.missionWikiControlForm = null;
        }
    }

    private void MissionWikiControlForm_OnToggleRequested(
        object? sender,
        EventArgs e)
    {
        this.missionWikiExpanded = !this.missionWikiExpanded;
        this.SyncMissionWiki();
    }

    private void MissionWikiWebViewForm_OnContentStateChanged(
        object? sender,
        EventArgs e)
    {
        this.SyncMissionWiki();
    }

    private void AddonOverlayForm_OnGameMenuOpened(
        object? sender,
        EventArgs e)
    {
        var overlay = this.addonOverlayForm;

        if (overlay == null ||
            overlay.IsDisposed ||
            overlay.Disposing ||
            !overlay.Visible ||
            !overlay.IsHandleCreated)
        {
            return;
        }

        this.PlaceBuildSurfaceBehindAddonOverlay(
            this.skillBuildEquipmentCompanionForm,
            overlay);
        this.PlaceBuildSurfaceBehindAddonOverlay(
            this.skillBuildEquipmentToggleForm,
            overlay);
        this.PlaceBuildSurfaceBehindAddonOverlay(
            this.skillBuildSkillsCompanionForm,
            overlay);
        this.PlaceBuildSurfaceBehindAddonOverlay(
            this.skillBuildSkillsToggleForm,
            overlay);
    }

    private void PlaceBuildSurfaceBehindAddonOverlay(
        Form? buildSurface,
        Form overlay)
    {
        if (buildSurface == null ||
            buildSurface.IsDisposed ||
            buildSurface.Disposing ||
            !buildSurface.Visible ||
            !buildSurface.IsHandleCreated)
        {
            return;
        }

        _ = NativeMethods.TryPlaceWindowBehindWithoutActivation(
            buildSurface.Handle,
            overlay.Handle);
    }

    private async void AddonOverlayForm_OnUiInteractionRaised(
        object? sender,
        AddonUiInteractionEventArgs e)
    {
        if (this.navigationInGamePresenter?.Owns(e.Interaction) == true)
        {
            try
            {
                await this.navigationInGamePresenter
                    .HandleInteractionAsync(e.Interaction)
                    .ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                Debug.WriteLine(
                    $"[Navigation] Built-in interaction failed: {exception}");
            }

            return;
        }

        this.addonUiInteractionRaised(e.Interaction);
    }

    private void AddonOverlayForm_OnNavigationRequested(
        object? sender,
        EventArgs e)
    {
        if (this.navigationPresentationMode ==
            NavigationPresentationMode.InGame)
        {
            this.ShowNavigationInGame();
            return;
        }

        this.ShowNavigationCompanion();
    }

    private bool CanPresentNavigation =>
        this.addonLifecycleState == ClientLifecycleState.InGame;

    private void SyncNavigationPresentation()
    {
        if (!this.IsHandleCreated)
        {
            return;
        }

        if (!this.CanPresentNavigation)
        {
            this.navigationInGamePresenter?.Hide();
            this.CloseNavigationCompanion(
                preserveOpenPreference: true);
            return;
        }

        if (this.navigationPresentationMode ==
            NavigationPresentationMode.InGame)
        {
            this.CloseNavigationCompanion(
                preserveOpenPreference: true);
            this.EnsureNavigationInGamePresenter();
            this.navigationInGamePresenter!.Show(reopen: false);
            return;
        }

        this.navigationInGamePresenter?.Hide();

        var placement = this.resolveAddonWindowPlacement(
            NavigationPresentationIds.BuiltInAddonId,
            NavigationPresentationIds.CompanionWindowId);

        if (placement is { IsVisible: true, IsClosed: false })
        {
            this.ShowNavigationCompanion(
                persistMode: false,
                activate: false);
        }
    }

    private void ShowNavigationCompanion(
        bool persistMode = true,
        bool activate = true)
    {
        this.navigationInGamePresenter?.Hide();

        if (persistMode)
        {
            this.SetNavigationPresentationMode(
                NavigationPresentationMode.Companion);
        }

        if (!this.CanPresentNavigation)
        {
            return;
        }

        if (this.navigationCompanionForm is { IsDisposed: false })
        {
            this.navigationCompanionForm.RestorePlacement(this.Bounds);
            this.navigationCompanionForm.MarkOpen();
            this.navigationCompanionForm.RefreshNow();

            if (this.navigationCompanionForm.WindowState ==
                FormWindowState.Minimized)
            {
                this.navigationCompanionForm.WindowState =
                    FormWindowState.Normal;
            }

            this.navigationCompanionForm.Show();

            if (activate)
            {
                this.navigationCompanionForm.BringToFront();
                this.navigationCompanionForm.Activate();
            }

            return;
        }

        this.navigationCompanionForm = new NavigationCompanionForm(
            this.clientManager,
            this.clientInstance.ProcessId,
            this.resolveAddonWindowPlacement,
            this.saveAddonWindowPlacement,
            () => this.ShowNavigationInGame());
        this.navigationCompanionForm.FormClosed +=
            this.NavigationCompanionForm_OnFormClosed;
        this.navigationCompanionForm.RestorePlacement(this.Bounds);
        this.navigationCompanionForm.Show(this);
        this.navigationCompanionForm.MarkOpen();

        if (activate)
        {
            this.navigationCompanionForm.Activate();
        }
    }

    private void ShowNavigationInGame(bool persistMode = true)
    {
        this.CloseNavigationCompanion(
            preserveOpenPreference: false);

        if (persistMode)
        {
            this.SetNavigationPresentationMode(
                NavigationPresentationMode.InGame);
        }

        if (!this.CanPresentNavigation)
        {
            return;
        }

        this.EnsureNavigationInGamePresenter();
        this.navigationInGamePresenter!.Show();
    }

    private void EnsureNavigationInGamePresenter()
    {
        if (this.navigationInGamePresenter != null)
        {
            return;
        }

        this.navigationInGamePresenter = new NavigationInGamePresenter(
            this.clientManager,
            this.clientInstance.ProcessId,
            this.ApplyBuiltInNavigationUiCommand,
            () => this.ShowNavigationCompanion());
    }

    private void ApplyBuiltInNavigationUiCommand(AddonUiCommand command)
    {
        if (this.IsDisposed || this.Disposing)
        {
            return;
        }

        if (command.Kind != AddonUiCommandKind.ClearAddon &&
            !this.CanPresentNavigation)
        {
            return;
        }

        this.EnsureAddonOverlay();
        this.addonOverlayForm!.Apply(command);
        this.SyncAddonOverlay();
    }

    private void SetNavigationPresentationMode(
        NavigationPresentationMode mode)
    {
        this.navigationPresentationMode = mode;
        this.clientManager.SetNavigationPresentationMode(
            this.clientInstance.ProcessId,
            mode);
    }

    internal bool ShowNavigationCompanionForHelp()
    {
        if (this.IsDisposed ||
            this.Disposing ||
            !this.CanPresentNavigation)
        {
            return false;
        }

        this.ShowNavigationCompanion();
        this.navigationCompanionForm?.BeginInvoke(
            () => this.navigationCompanionForm?.ShowHelpTour());
        return this.navigationCompanionForm is
            { IsDisposed: false, Visible: true };
    }

    private void NavigationCompanionForm_OnFormClosed(
        object? sender,
        FormClosedEventArgs e)
    {
        if (sender is NavigationCompanionForm form)
        {
            form.FormClosed -=
                this.NavigationCompanionForm_OnFormClosed;
        }

        this.navigationCompanionForm = null;
    }

    private void CloseNavigationCompanion(
        bool preserveOpenPreference)
    {
        var form = this.navigationCompanionForm;
        this.navigationCompanionForm = null;

        if (form == null || form.IsDisposed)
        {
            return;
        }

        form.FormClosed -=
            this.NavigationCompanionForm_OnFormClosed;

        if (preserveOpenPreference)
        {
            form.ClosePreservingOpenState();
        }
        else
        {
            form.Close();
        }

        form.Dispose();
    }

    private void AddonOverlayForm_OnGalaxyAtlasRequested(
        object? sender,
        EventArgs e)
    {
        this.openGalaxyAtlasRequested(
            this.clientInstance);
    }

    private void AddonOverlayForm_OnWorldFindRequested(
        object? sender,
        EventArgs e)
    {
        this.openWorldFindRequested(
            this.clientInstance,
            null);
    }

    private void AddonOverlayForm_OnForgeContributionsRequested(
        object? sender,
        EventArgs e)
    {
        this.openForgeContributionsRequested(
            this.clientInstance,
            this);
    }

    private void AddonOverlayForm_OnPilotArchiveRequested(
        object? sender,
        EventArgs e)
    {
        this.openPilotArchiveRequested(
            this.clientInstance,
            this);
    }

    private void AddonOverlayForm_OnBuildsRequested(
        object? sender,
        EventArgs e)
    {
        try
        {
            this.ShowBuildBoard();
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[Builds] Could not open the Build Board: {exception}");
            try
            {
                this.CloseBuildBoardForm();
            }
            catch (Exception closeException)
            {
                Debug.WriteLine($"[Builds] Could not close the failed Build Board: {closeException}");
            }

            this.SetTemporaryTitleStatus(
                "Builds could not open. The game client is still running.",
                TimeSpan.FromSeconds(5));
        }
    }

    private void AddonOverlayForm_OnSocialRequested(
        object? sender,
        EventArgs e)
    {
        this.openSocialRequested(
            this.clientInstance,
            this);
    }

    private void AddonOverlayForm_OnHelpCenterRequested(
        object? sender,
        EventArgs e)
    {
        this.openHelpCenterRequested(
            this.clientInstance,
            this);
    }

    private void AddonOverlayForm_OnManageAddonsRequested(
        object? sender,
        EventArgs e)
    {
        this.openAddonsRequested(
            this.clientInstance,
            this);
    }

    internal bool TryGetAddonMenuToggleState(
        string addonId,
        out bool isChecked)
    {
        isChecked = false;

        return this.addonOverlayForm?.TryGetAddonMenuToggleState(
            addonId,
            out isChecked) == true;
    }

    internal bool ShowAddonMenuToggleGuidance(string addonId)
    {
        this.EnsureAddonOverlay();
        this.SyncAddonOverlay();

        return this.addonOverlayForm?.ShowAddonMenuToggleGuidance(
            addonId) == true;
    }

    private void AddonOverlayForm_OnInGameOptionsRequested(
        object? sender,
        EventArgs e)
    {
        this.openInGameOptionsRequested(
            this.clientInstance,
            this);
    }

}
