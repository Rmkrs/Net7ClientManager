// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using Net7ClientManager.Core;
using Net7ClientManager.Models;

public sealed partial class MainForm : ThemedForm
{
    private readonly ClientManager clientManager;
    private readonly System.Windows.Forms.Timer refreshTimer;
    private readonly Dictionary<int, long> runningClientFirstSeenOrders = [];
    private readonly ActionToolTip dashboardToolTip = new();
    private long nextRunningClientFirstSeenOrder;

    private ComboBox profileComboBox = null!;
    private Button addProfileButton = null!;
    private Button renameProfileButton = null!;
    private Button duplicateProfileButton = null!;
    private Button deleteProfileButton = null!;

    private Button addSlotButton = null!;
    private Button editLayoutButton = null!;
    private FlowLayoutPanel slotsFlowPanel = null!;
    private Label slotsSummaryLabel = null!;

    private FlowLayoutPanel runningClientsFlowPanel = null!;
    private Label runningClientsSummaryLabel = null!;

    private ComboBox quickLaunchHostResolutionComboBox = null!;
    private ThemedCheckBox quickLaunchMatchGameResolutionCheckBox = null!;
    private ComboBox quickLaunchGameResolutionComboBox = null!;
    private Button startClientButton = null!;
    private Button accountsButton = null!;
    private Button showHelpButton = null!;
    private Button autoLoginReadinessButton = null!;
    private Button pilotArchiveButton = null!;
    private Button gameSettingsButton = null!;
    private Button createMissingClientsButton = null!;
    private CheckBox keepClientsAliveCheckBox = null!;
    private Control profileCard = null!;
    private Control accountsToolsCard = null!;
    private Control quickLaunchCard = null!;
    private Control slotsSection = null!;
    private Control runningClientsSection = null!;

    private bool isRefreshingProfileComboBox;
    private bool isRefreshingProfileControls;
    private bool isRefreshingQuickLaunchControls;
    private QuickLaunchResolutionItem? independentQuickLaunchGameResolution;
    private string? profileComboBoxSignature;

    private bool inGameOptionsOpen;
    private int? inGameOptionsProcessId;
    private InGameOptionsForm? inGameOptionsForm;
    private CommandOverlayForm? commandOverlayForm;
    private Net7ClientManager.Services.CommandPaletteKeyboardHook?
        commandPaletteKeyboardHook;

    private NavigationPlannerForm? navigationPlannerForm;
    private GalaxyAtlasForm? galaxyAtlasForm;
    private WorldFindForm? worldFindForm;

    public MainForm(ClientManager clientManager)
    {
        this.clientManager = clientManager;

        this.Text = "Net7 Client Manager";
        this.Icon = ResourceLoader.Net7ClientManagerIcon;
        this.StartPosition = FormStartPosition.CenterScreen;
        this.Size = new Size(width: 1480, height: 820);
        this.MinimumSize = new Size(width: 1120, height: 700);
        this.BackColor = MainWindowTheme.Background;
        this.ForeColor = MainWindowTheme.Text;
        this.Font = MainWindowTheme.CreateBodyFont();
        this.DoubleBuffered = true;
        this.ConfigureWindowChrome(
            allowResize: true,
            showMinimizeButton: true,
            showMaximizeButton: true);
        this.ConfigureHelpTopic(HelpTopicIds.Home);
        this.ConfigureHelpTour(this.ShowMainScreenHelpTour);
        HelpCenterLauncher.Configure(this.OpenHelpCenter);
        this.RestoreWindowPlacement();

        var topPanel = this.CreateTopPanel();
        var dashboardPanel = this.CreateDashboardPanel();

        this.Controls.Add(dashboardPanel);
        this.Controls.Add(topPanel);

        this.refreshTimer = new System.Windows.Forms.Timer
        {
            Interval = 1000,
        };

        this.refreshTimer.Tick += this.RefreshTimer_OnTick;
        this.refreshTimer.Start();

        this.RefreshAll(refreshRuntimeFeatures: false);

        if (this.clientManager.KeepClientsAlive)
        {
            this.clientManager.CreateMissingClients(this);
        }

        this.clientManager.NavigationPlannerRequested +=
            this.ClientManager_OnNavigationPlannerRequested;

        this.clientManager.GalaxyAtlasRequested +=
            this.ClientManager_OnGalaxyAtlasRequested;

        this.clientManager.WorldFindRequested +=
            this.ClientManager_OnWorldFindRequested;

        this.clientManager.InGameOptionsRequested +=
            this.ClientManager_OnInGameOptionsRequested;

        this.clientManager.HelpRequested +=
            this.ClientManager_OnHelpRequested;

        this.ApplyCommandPaletteRuntimeSettings();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        this.SaveWindowPlacement();
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        this.refreshTimer.Stop();
        this.refreshTimer.Tick -= this.RefreshTimer_OnTick;
        this.refreshTimer.Dispose();
        this.dashboardToolTip.Dispose();

        this.profileComboBox.SelectedIndexChanged -= this.ProfileComboBox_OnSelectedIndexChanged;
        this.addProfileButton.Click -= this.AddProfileButton_OnClick;
        this.renameProfileButton.Click -= this.RenameProfileButton_OnClick;
        this.duplicateProfileButton.Click -= this.DuplicateProfileButton_OnClick;
        this.deleteProfileButton.Click -= this.DeleteProfileButton_OnClick;

        this.addSlotButton.Click -= this.AddSlotButton_OnClick;
        this.editLayoutButton.Click -= this.EditLayoutButton_OnClick;
        this.slotsFlowPanel.SizeChanged -= this.DashboardFlowPanel_OnSizeChanged;
        this.runningClientsFlowPanel.SizeChanged -= this.DashboardFlowPanel_OnSizeChanged;

        this.quickLaunchHostResolutionComboBox.SelectedIndexChanged -=
            this.QuickLaunchHostResolutionComboBox_OnSelectedIndexChanged;
        this.quickLaunchMatchGameResolutionCheckBox.CheckedChanged -=
            this.QuickLaunchMatchGameResolutionCheckBox_OnCheckedChanged;
        this.quickLaunchGameResolutionComboBox.SelectedIndexChanged -=
            this.QuickLaunchGameResolutionComboBox_OnSelectedIndexChanged;
        this.startClientButton.Click -= this.StartClientButton_OnClick;
        this.accountsButton.Click -= this.AccountsButton_OnClick;
        this.showHelpButton.Click -= this.ShowHelpButton_OnClick;
        this.autoLoginReadinessButton.Click -=
            this.AutoLoginReadinessButton_OnClick;
        this.pilotArchiveButton.Click -= this.PilotArchiveButton_OnClick;
        this.gameSettingsButton.Click -= this.GameSettingsButton_OnClick;

        this.clientManager.NavigationPlannerRequested -=
            this.ClientManager_OnNavigationPlannerRequested;

        this.clientManager.GalaxyAtlasRequested -=
            this.ClientManager_OnGalaxyAtlasRequested;

        this.clientManager.WorldFindRequested -=
            this.ClientManager_OnWorldFindRequested;

        this.clientManager.InGameOptionsRequested -=
            this.ClientManager_OnInGameOptionsRequested;

        this.clientManager.HelpRequested -=
            this.ClientManager_OnHelpRequested;

        this.createMissingClientsButton.Click -= this.CreateMissingClientsButton_OnClick;
        this.keepClientsAliveCheckBox.CheckedChanged -= this.KeepClientsAliveCheckBox_OnCheckedChanged;

        this.CloseCommandOverlay();
        this.commandPaletteKeyboardHook?.Dispose();
        this.commandPaletteKeyboardHook = null;

        if (this.inGameOptionsForm is { IsDisposed: false })
        {
            this.inGameOptionsForm.Close();
        }

        this.inGameOptionsForm = null;

        if (this.helpCenterForm is { IsDisposed: false })
        {
            this.helpCenterForm.Close();
        }

        this.helpCenterForm = null;
        HelpCenterLauncher.Clear(this.OpenHelpCenter);

        base.OnFormClosed(e);
    }

    private void RestoreWindowPlacement()
    {
        var settings = this.clientManager.MainWindowSettings;

        if (settings.Bounds is not { } savedBounds)
        {
            return;
        }

        var candidate = new Rectangle(
            savedBounds.Left,
            savedBounds.Top,
            Math.Max(this.MinimumSize.Width, savedBounds.Width),
            Math.Max(this.MinimumSize.Height, savedBounds.Height));

        var savedMonitor = Screen.AllScreens.FirstOrDefault(screen =>
            string.Equals(
                screen.DeviceName,
                settings.MonitorDeviceName,
                StringComparison.OrdinalIgnoreCase));

        if (savedMonitor != null)
        {
            candidate.Location = new Point(
                savedMonitor.WorkingArea.Left +
                settings.MonitorOffsetLeft,
                savedMonitor.WorkingArea.Top +
                settings.MonitorOffsetTop);

            candidate = this.ClampWindowBoundsToWorkingArea(
                candidate,
                savedMonitor.WorkingArea);
        }
        else if (!HasAccessibleTitleBar(candidate))
        {
            var fallbackScreen = Screen.FromRectangle(candidate);
            candidate = this.ClampWindowBoundsToWorkingArea(
                candidate,
                fallbackScreen.WorkingArea);
        }

        this.StartPosition = FormStartPosition.Manual;
        this.Bounds = candidate;

        if (settings.Maximized)
        {
            this.WindowState = FormWindowState.Maximized;
        }
    }

    private void SaveWindowPlacement()
    {
        var bounds = this.WindowState == FormWindowState.Normal
            ? this.Bounds
            : this.RestoreBounds;

        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var monitor = this.WindowState == FormWindowState.Normal
            ? Screen.FromRectangle(bounds)
            : Screen.FromHandle(this.Handle);
        var settings = this.clientManager.MainWindowSettings;

        settings.Bounds = new WindowBounds
        {
            Left = bounds.Left,
            Top = bounds.Top,
            Width = bounds.Width,
            Height = bounds.Height,
        };
        settings.MonitorDeviceName = monitor.DeviceName;
        settings.MonitorOffsetLeft =
            bounds.Left - monitor.WorkingArea.Left;
        settings.MonitorOffsetTop =
            bounds.Top - monitor.WorkingArea.Top;
        settings.Maximized =
            this.WindowState == FormWindowState.Maximized;

        this.clientManager.SaveMainWindowSettings();
    }

    private Rectangle ClampWindowBoundsToWorkingArea(
        Rectangle candidate,
        Rectangle workingArea)
    {
        var width = Math.Max(
            this.MinimumSize.Width,
            candidate.Width);
        var height = Math.Max(
            this.MinimumSize.Height,
            candidate.Height);

        if (workingArea.Width >= this.MinimumSize.Width)
        {
            width = Math.Min(width, workingArea.Width);
        }

        if (workingArea.Height >= this.MinimumSize.Height)
        {
            height = Math.Min(height, workingArea.Height);
        }

        var maximumLeft = Math.Max(
            workingArea.Left,
            workingArea.Right - width);
        var maximumTop = Math.Max(
            workingArea.Top,
            workingArea.Bottom - height);

        return new Rectangle(
            Math.Clamp(
                candidate.Left,
                workingArea.Left,
                maximumLeft),
            Math.Clamp(
                candidate.Top,
                workingArea.Top,
                maximumTop),
            width,
            height);
    }

    private static bool HasAccessibleTitleBar(Rectangle candidate)
    {
        const int minimumVisibleWidth = 160;
        const int titleBarHeight = 40;

        var titleBarBounds = new Rectangle(
            candidate.Left,
            candidate.Top,
            candidate.Width,
            Math.Min(titleBarHeight, candidate.Height));

        return Screen.AllScreens.Any(screen =>
        {
            var visibleBounds = Rectangle.Intersect(
                screen.WorkingArea,
                titleBarBounds);

            return visibleBounds.Width >=
                   Math.Min(minimumVisibleWidth, candidate.Width) &&
                   visibleBounds.Height >= titleBarBounds.Height;
        });
    }

    private void StartClientButton_OnClick(object? sender, EventArgs e)
    {
        _ = this.clientManager.StartUnassignedClient(
            this,
            out _);
        this.RefreshAll();
    }

    private void CreateMissingClientsButton_OnClick(object? sender, EventArgs e)
    {
        this.clientManager.CreateMissingClients(this);
        this.RefreshAll();
    }

    private void KeepClientsAliveCheckBox_OnCheckedChanged(object? sender, EventArgs e)
    {
        if (this.isRefreshingProfileControls)
        {
            return;
        }

        this.clientManager.SetCurrentProfileKeepAlive(
            this.keepClientsAliveCheckBox.Checked,
            this);
        this.RefreshAll();
    }

    private void PilotArchiveButton_OnClick(object? sender, EventArgs e)
    {
        this.clientManager.OpenPilotArchive(this);
    }

    private void GameSettingsButton_OnClick(object? sender, EventArgs e)
    {
        this.OpenGameSettings(showTour: false);
    }

    private void OpenGameSettings(bool showTour)
    {
        using var form = new GameSettingsForm(
            this.clientManager,
            this);

        if (showTour)
        {
            form.Shown += (_, _) =>
                form.BeginInvoke(() => form.ShowHelpTour());
        }

        form.ShowDialog(this);
    }

    private void AccountsButton_OnClick(object? sender, EventArgs e)
    {
        using var form = new AccountsForm(
            this.clientManager.ConfiguredAccounts,
            accounts =>
            {
                this.clientManager.SaveConfiguredAccounts(accounts);
                this.RefreshAll();
            });
        using var windowPlacement =
            this.clientManager.BindGlobalWindowPlacement(
                form,
                Net7ClientManager.Services.WindowPlacementIds.Accounts,
                this);

        form.ShowDialog(this);
        this.RefreshAll();
    }

    private void RefreshTimer_OnTick(object? sender, EventArgs e)
    {
        this.RefreshAll();
    }

    private void RefreshAll(bool refreshRuntimeFeatures = true)
    {
        this.RefreshProfileComboBox();
        this.RefreshSlots();
        this.RefreshRunningClients();

        if (refreshRuntimeFeatures)
        {
            this.RefreshCommandPaletteRuntime();
            this.RefreshInGameOptionsLifecycle();
        }
    }

    private void ClientManager_OnHelpRequested(
        object? sender,
        HelpRequestedEventArgs e)
    {
        this.OpenHelpCenter(
            e.Owner,
            new HelpLaunchRequest(
                HelpTopicIds.Home,
                e.ProcessId));
    }

    private void ClientManager_OnGalaxyAtlasRequested(
        object? sender,
        Net7ClientManager.Navigation.GalaxyAtlasRequestedEventArgs e)
    {
        if (this.IsDisposed || this.Disposing)
        {
            return;
        }

        if (this.InvokeRequired)
        {
            this.BeginInvoke(
                () => this.ShowGalaxyAtlas(e.ProcessId));
            return;
        }

        this.ShowGalaxyAtlas(e.ProcessId);
    }

    private void ShowGalaxyAtlas(int? processId)
    {
        if (this.galaxyAtlasForm is { IsDisposed: false })
        {
            this.galaxyAtlasForm.SelectProcess(processId);
            this.galaxyAtlasForm.Show();
            this.galaxyAtlasForm.WindowState =
                FormWindowState.Normal;
            this.galaxyAtlasForm.Activate();
            return;
        }

        this.galaxyAtlasForm = new GalaxyAtlasForm(
            this.clientManager,
            processId);

        this.galaxyAtlasForm.FormClosed +=
            (_, _) => this.galaxyAtlasForm = null;

        this.galaxyAtlasForm.Show();
        this.galaxyAtlasForm.Activate();
    }

    private void ClientManager_OnWorldFindRequested(
        object? sender,
        Net7ClientManager.Navigation.WorldFindRequestedEventArgs e)
    {
        if (this.IsDisposed || this.Disposing)
        {
            return;
        }

        if (this.InvokeRequired)
        {
            this.BeginInvoke(
                () => this.ShowWorldFind(
                    e.ProcessId,
                    e.Query));
            return;
        }

        this.ShowWorldFind(
            e.ProcessId,
            e.Query);
    }

    private void ShowWorldFind(
        int? processId,
        string? query = null)
    {
        if (this.worldFindForm is { IsDisposed: false })
        {
            this.worldFindForm.SelectProcess(processId);
            if (!string.IsNullOrWhiteSpace(query))
            {
                this.worldFindForm.Search(query);
            }
            this.worldFindForm.Show();

            if (this.worldFindForm.WindowState ==
                FormWindowState.Minimized)
            {
                this.worldFindForm.WindowState =
                    FormWindowState.Normal;
            }

            this.worldFindForm.Activate();
            return;
        }

        this.worldFindForm = new WorldFindForm(
            this.clientManager,
            processId);
        if (!string.IsNullOrWhiteSpace(query))
        {
            this.worldFindForm.Search(query);
        }

        this.worldFindForm.FormClosed +=
            (_, _) => this.worldFindForm = null;

        this.worldFindForm.Show();
        this.worldFindForm.Activate();
    }

    private void ClientManager_OnNavigationPlannerRequested(
        object? sender,
        Net7ClientManager.Navigation.NavigationPlannerRequestedEventArgs e)
    {
        if (this.IsDisposed || this.Disposing)
        {
            return;
        }

        if (this.InvokeRequired)
        {
            this.BeginInvoke(
                () => this.ShowNavigationPlanner(e.ProcessId));
            return;
        }

        this.ShowNavigationPlanner(e.ProcessId);
    }

    private void ShowNavigationPlanner(int? processId)
    {
        if (this.navigationPlannerForm is { IsDisposed: false })
        {
            this.navigationPlannerForm.SelectProcess(processId);
            this.navigationPlannerForm.Show();
            this.navigationPlannerForm.WindowState =
                FormWindowState.Normal;
            this.navigationPlannerForm.Activate();
            return;
        }

        this.navigationPlannerForm = new NavigationPlannerForm(
            this.clientManager,
            processId);

        this.navigationPlannerForm.FormClosed +=
            (_, _) => this.navigationPlannerForm = null;

        this.navigationPlannerForm.Show();
        this.navigationPlannerForm.Activate();
    }
}
