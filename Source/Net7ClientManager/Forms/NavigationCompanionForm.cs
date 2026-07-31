// ReSharper disable LocalizableElement
// ReSharper disable AsyncVoidEventHandlerMethod
namespace Net7ClientManager.Forms;

using System.Diagnostics;
using System.Globalization;
using Net7ClientManager.Addons.Contracts;
using Net7ClientManager.Core;
using Net7ClientManager.Models;
using Net7ClientManager.Navigation;

/// <summary>
/// Native, desktop-level presentation of the active route for one hosted
/// client. Unlike addon windows, this form is not clipped to the game canvas.
/// </summary>
internal sealed class NavigationCompanionForm : ThemedForm
{
    private const string PlacementAddonId =
        NavigationPresentationIds.BuiltInAddonId;
    private const string PlacementWidgetId =
        NavigationPresentationIds.CompanionWindowId;
    private const int OwnerGap = 12;

    private static readonly Size defaultSize = new(460, 510);

    private readonly ClientManager clientManager;
    private readonly int processId;
    private readonly Func<string, string, AddonWindowPlacement?>
        resolveWindowPlacement;
    private readonly Action<string, string, AddonWindowPlacement>
        saveWindowPlacement;
    private readonly Action showInGameRequested;
    private readonly System.Windows.Forms.Timer refreshTimer = new()
    {
        Interval = 300,
    };

    private readonly Panel contentPanel = new();
    private readonly Panel routeCard = new();
    private readonly Panel journeyCard = new();
    private readonly Label pilotLabel = new();
    private readonly Label destinationLabel = new();
    private readonly Label routeStateLabel = new();
    private readonly Label nextTargetLabel = new();
    private readonly Label hopsLabel = new();
    private readonly Label journeyStateLabel = new();
    private readonly Label journeyStatusLabel = new();
    private readonly Label warningLabel = new();
    private readonly Button showInGameButton = new();
    private readonly Button selectNextTargetButton = new();
    private readonly Button autoPilotButton = new();
    private readonly Button openPlannerButton = new();
    private readonly Button secondaryRouteButton = new();
    private readonly Button clearRouteButton = new();

    private Rectangle placementOwnerBounds;
    private bool placementRestored;
    private bool preserveOpenStateOnClose;
    private bool commandBusy;
    private string transientStatus = "";
    private DateTimeOffset transientStatusUntil;

    public NavigationCompanionForm(
        ClientManager clientManager,
        int processId,
        Func<string, string, AddonWindowPlacement?>
            resolveWindowPlacement,
        Action<string, string, AddonWindowPlacement>
            saveWindowPlacement,
        Action showInGameRequested)
    {
        this.clientManager = clientManager ??
            throw new ArgumentNullException(nameof(clientManager));
        this.processId = processId;
        this.resolveWindowPlacement = resolveWindowPlacement ??
            throw new ArgumentNullException(nameof(resolveWindowPlacement));
        this.saveWindowPlacement = saveWindowPlacement ??
            throw new ArgumentNullException(nameof(saveWindowPlacement));
        this.showInGameRequested = showInGameRequested ??
            throw new ArgumentNullException(nameof(showInGameRequested));

        this.Text = "Navigation";
        this.Icon = ResourceLoader.Net7ClientManagerIcon;
        this.StartPosition = FormStartPosition.Manual;
        this.ShowInTaskbar = false;
        this.AutoScaleMode = AutoScaleMode.Dpi;
        this.ClientSize = defaultSize;
        this.MinimumSize = new Size(390, 420);
        this.BackColor = MainWindowTheme.Background;
        this.ForeColor = MainWindowTheme.Text;
        this.Font = MainWindowTheme.CreateBodyFont();
        this.ConfigureWindowChrome(
            allowResize: true,
            showMinimizeButton: true,
            showMaximizeButton: false,
            showCloseButton: true,
            showIcon: true,
            showHelpButton: true);
        this.ConfigureHelpTopic(
            HelpTopicIds.Navigation,
            () => this.processId);
        this.ConfigureHelpTour(this.ShowHelpTour);

        this.ConfigureUi();
        this.Controls.Add(this.contentPanel);

        this.refreshTimer.Tick += this.RefreshTimer_OnTick;
        this.refreshTimer.Start();
        this.FormClosing += this.NavigationCompanionForm_OnFormClosing;
        this.RefreshPresentation();
    }

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

        this.Bounds = ClampToWorkingArea(
            new Rectangle(location, size),
            ownerBounds);
        this.placementRestored = true;
    }

    public void MarkOpen()
    {
        this.preserveOpenStateOnClose = false;
        this.PersistPlacement(
            isVisible: true,
            isClosed: false);
    }

    public void ClosePreservingOpenState()
    {
        this.preserveOpenStateOnClose = true;
        this.Close();
    }

    public void RefreshNow()
    {
        this.RefreshPresentation();
    }

    internal void ShowHelpTour()
    {
        GuidedTourOverlay.Show(
            this,
            [
                new GuidedTourStep(
                    () => this.routeCard,
                    "Keep the route beside the game",
                    "This companion is a normal desktop window, so it can sit to the right, below, or on another monitor without covering the Earth & Beyond view."),
                new GuidedTourStep(
                    () => this.showInGameButton,
                    "Move Navigation back into the game",
                    "Show in game closes this desktop companion and opens the same built-in Navigation window over Earth & Beyond. The route and Auto Pilot state do not restart."),
                new GuidedTourStep(
                    () => this.selectNextTargetButton,
                    "Take one step manually",
                    "Select next target asks the game to cycle to the next gate, navigation point, station, or landing target in the active route."),
                new GuidedTourStep(
                    () => this.autoPilotButton,
                    "Start, resume, or stop Auto Pilot",
                    "Auto Pilot follows supported route steps. If something interrupts it, the same button becomes available again when the journey can safely resume."),
                new GuidedTourStep(
                    () => this.openPlannerButton,
                    "Open the full Route Planner",
                    "Use the planner to choose a destination, inspect the complete route, or switch to another hosted pilot."),
            ]);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.refreshTimer.Stop();
            this.refreshTimer.Tick -= this.RefreshTimer_OnTick;
            this.refreshTimer.Dispose();
            this.FormClosing -=
                this.NavigationCompanionForm_OnFormClosing;
            this.showInGameButton.Click -=
                this.ShowInGameButton_OnClick;
            this.selectNextTargetButton.Click -=
                this.SelectNextTargetButton_OnClick;
            this.autoPilotButton.Click -=
                this.AutoPilotButton_OnClick;
            this.openPlannerButton.Click -=
                this.OpenPlannerButton_OnClick;
            this.secondaryRouteButton.Click -=
                this.SecondaryRouteButton_OnClick;
            this.clearRouteButton.Click -=
                this.ClearRouteButton_OnClick;
        }

        base.Dispose(disposing);
    }

    private void ConfigureUi()
    {
        this.contentPanel.Dock = DockStyle.Fill;
        this.contentPanel.Padding = new Padding(14);
        this.contentPanel.BackColor = MainWindowTheme.Background;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            BackColor = MainWindowTheme.Background,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        this.pilotLabel.AutoSize = true;
        this.pilotLabel.ForeColor = MainWindowTheme.MutedText;
        this.pilotLabel.Font = MainWindowTheme.CreateBodyFont(8.5f);
        this.pilotLabel.Margin = new Padding(2, 0, 2, 8);
        this.pilotLabel.Text = "Waiting for the hosted pilot...";

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));

        this.ConfigureButton(
            this.showInGameButton,
            "Show in game");
        this.showInGameButton.Margin = new Padding(4, 0, 0, 4);
        this.showInGameButton.Click +=
            this.ShowInGameButton_OnClick;

        header.Controls.Add(this.pilotLabel, 0, 0);
        header.Controls.Add(this.showInGameButton, 1, 0);

        this.ConfigureCard(this.routeCard);
        this.ConfigureCard(this.journeyCard);
        this.BuildRouteCard();
        this.BuildJourneyCard();

        var actions = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 2,
            Margin = new Padding(0, 10, 0, 0),
            Padding = Padding.Empty,
        };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        actions.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        actions.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

        this.ConfigureButton(
            this.selectNextTargetButton,
            "Select next target");
        this.ConfigureButton(
            this.autoPilotButton,
            "Start Auto Pilot",
            primary: true);
        this.ConfigureButton(
            this.openPlannerButton,
            "Open planner");
        this.ConfigureButton(
            this.secondaryRouteButton,
            "Plan return trip");

        this.selectNextTargetButton.Click +=
            this.SelectNextTargetButton_OnClick;
        this.autoPilotButton.Click +=
            this.AutoPilotButton_OnClick;
        this.openPlannerButton.Click +=
            this.OpenPlannerButton_OnClick;
        this.secondaryRouteButton.Click +=
            this.SecondaryRouteButton_OnClick;

        actions.Controls.Add(this.selectNextTargetButton, 0, 0);
        actions.Controls.Add(this.autoPilotButton, 1, 0);
        actions.Controls.Add(this.openPlannerButton, 0, 1);
        actions.Controls.Add(this.secondaryRouteButton, 1, 1);

        this.ConfigureButton(this.clearRouteButton, "Clear route");
        this.clearRouteButton.AutoSize = false;
        this.clearRouteButton.Height = 38;
        this.clearRouteButton.Dock = DockStyle.Bottom;
        this.clearRouteButton.Margin = new Padding(0, 8, 0, 0);
        this.clearRouteButton.Click +=
            this.ClearRouteButton_OnClick;

        layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(this.routeCard, 0, 1);
        layout.Controls.Add(this.journeyCard, 0, 2);
        layout.Controls.Add(actions, 0, 3);
        layout.Controls.Add(this.clearRouteButton, 0, 4);
        this.contentPanel.Controls.Add(layout);
    }

    private void ConfigureCard(Panel panel)
    {
        panel.Dock = DockStyle.Fill;
        panel.BackColor = MainWindowTheme.Panel;
        panel.Padding = new Padding(14);
        panel.Margin = new Padding(0, 0, 0, 10);
        panel.Paint += static (sender, e) =>
        {
            if (sender is not Panel card)
            {
                return;
            }

            using var pen = new Pen(MainWindowTheme.Border);
            e.Graphics.DrawRectangle(
                pen,
                0,
                0,
                Math.Max(0, card.ClientSize.Width - 1),
                Math.Max(0, card.ClientSize.Height - 1));
        };
    }

    private void BuildRouteCard()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        this.destinationLabel.AutoSize = true;
        this.destinationLabel.Font = MainWindowTheme.CreateHeadingFont(14);
        this.destinationLabel.ForeColor = MainWindowTheme.Accent;
        this.destinationLabel.Margin = new Padding(0, 0, 0, 8);
        this.destinationLabel.Text = "No destination";

        this.routeStateLabel.AutoSize = true;
        this.routeStateLabel.Font = MainWindowTheme.CreateHeadingFont(9.5f);
        this.routeStateLabel.ForeColor = MainWindowTheme.Text;
        this.routeStateLabel.Margin = new Padding(0, 0, 0, 6);

        this.nextTargetLabel.Dock = DockStyle.Fill;
        this.nextTargetLabel.AutoEllipsis = true;
        this.nextTargetLabel.Font = MainWindowTheme.CreateBodyFont(10);
        this.nextTargetLabel.ForeColor = MainWindowTheme.MutedText;
        this.nextTargetLabel.Margin = new Padding(0, 0, 0, 5);

        this.warningLabel.Dock = DockStyle.Fill;
        this.warningLabel.AutoEllipsis = true;
        this.warningLabel.Font = MainWindowTheme.CreateBodyFont(8.5f);
        this.warningLabel.ForeColor = MainWindowTheme.Warning;
        this.warningLabel.Margin = new Padding(0, 3, 0, 3);

        this.hopsLabel.AutoSize = true;
        this.hopsLabel.Font = MainWindowTheme.CreateHeadingFont(9);
        this.hopsLabel.ForeColor = MainWindowTheme.MutedText;
        this.hopsLabel.Margin = Padding.Empty;

        layout.Controls.Add(this.destinationLabel, 0, 0);
        layout.Controls.Add(this.routeStateLabel, 0, 1);
        layout.Controls.Add(this.nextTargetLabel, 0, 2);
        layout.Controls.Add(this.warningLabel, 0, 3);
        layout.Controls.Add(this.hopsLabel, 0, 4);
        this.routeCard.Controls.Add(layout);
    }

    private void BuildJourneyCard()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        this.journeyStateLabel.AutoSize = true;
        this.journeyStateLabel.Font = MainWindowTheme.CreateHeadingFont(10);
        this.journeyStateLabel.ForeColor = MainWindowTheme.Warning;
        this.journeyStateLabel.Margin = new Padding(0, 0, 0, 7);
        this.journeyStateLabel.Text = "AUTO PILOT · INACTIVE";

        this.journeyStatusLabel.Dock = DockStyle.Fill;
        this.journeyStatusLabel.AutoEllipsis = true;
        this.journeyStatusLabel.Font = MainWindowTheme.CreateBodyFont(9.5f);
        this.journeyStatusLabel.ForeColor = MainWindowTheme.MutedText;
        this.journeyStatusLabel.Margin = Padding.Empty;

        layout.Controls.Add(this.journeyStateLabel, 0, 0);
        layout.Controls.Add(this.journeyStatusLabel, 0, 1);
        this.journeyCard.Controls.Add(layout);
    }

    private void ConfigureButton(
        Button button,
        string text,
        bool primary = false)
    {
        button.Text = text;
        button.Dock = DockStyle.Fill;
        button.Margin = new Padding(4);
        button.Padding = new Padding(5, 0, 5, 0);
        button.AutoEllipsis = true;
        MainWindowTheme.StyleButton(button, primary);
    }

    private void RefreshTimer_OnTick(
        object? sender,
        EventArgs e)
    {
        this.RefreshPresentation();
    }

    private void RefreshPresentation()
    {
        if (this.IsDisposed || this.Disposing)
        {
            return;
        }

        AddonNavigationRouteSnapshot snapshot;

        try
        {
            snapshot = this.clientManager
                .GetNavigationPresentationSnapshot(this.processId);
        }
        catch (Exception exception)
        {
            Debug.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"[Navigation] Could not refresh the companion: {exception}"));
            this.ShowUnavailable("Navigation state could not be refreshed.");
            return;
        }

        var route = snapshot.Route;
        var destinationName = ResolveDestinationName(route?.Destination);
        var pilotName = this.clientManager.Clients
            .FirstOrDefault(client => client.ProcessId == this.processId)
            ?.LiveCharacterIdentity.Name;
        pilotName = string.IsNullOrWhiteSpace(pilotName)
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"PID {this.processId}")
            : pilotName;

        this.Text = string.Create(
            CultureInfo.CurrentCulture,
            $"Navigation · {pilotName}");
        this.pilotLabel.Text = string.Create(
            CultureInfo.CurrentCulture,
            $"Following {pilotName}");
        this.destinationLabel.Text = destinationName;

        if (!snapshot.IsAvailable)
        {
            this.ShowUnavailable(
                string.IsNullOrWhiteSpace(snapshot.StatusText)
                    ? "Navigation is waiting for the hosted client."
                    : snapshot.StatusText);
            return;
        }

        if (!snapshot.HasRoute || route == null)
        {
            this.routeStateLabel.Text = "No active route";
            this.nextTargetLabel.Text =
                string.IsNullOrWhiteSpace(snapshot.StatusText)
                    ? "Open the planner to choose a destination."
                    : snapshot.StatusText;
            this.hopsLabel.Text = "0 hops remaining";
            this.warningLabel.Text = "";
        }
        else if (snapshot.Status == "destination_reached")
        {
            this.routeStateLabel.Text = "Destination reached";
            this.nextTargetLabel.Text = snapshot.Journey.CanPlanReturnTrip
                ? ResolveReturnDescription(route)
                : "Journey complete.";
            this.hopsLabel.Text = string.Create(
                CultureInfo.CurrentCulture,
                $"0 hops remaining · {route.CompletedHopCount} completed");
            this.warningLabel.Text = "";
        }
        else
        {
            this.routeStateLabel.Text = ResolveStepHeading(route.NextStep);
            this.nextTargetLabel.Text = ResolveStepDescription(route.NextStep);
            this.hopsLabel.Text = string.Create(
                CultureInfo.CurrentCulture,
                $"{route.RemainingHopCount} hops remaining · {route.CompletedHopCount} completed");
            this.warningLabel.Text = route.Warnings.FirstOrDefault() ?? "";
        }

        var journey = snapshot.Journey;
        this.journeyStateLabel.Text = string.Create(
            CultureInfo.CurrentCulture,
            $"AUTO PILOT · {FormatState(journey.State)}");
        this.journeyStateLabel.ForeColor = journey.IsActive
            ? MainWindowTheme.Success
            : journey.State is "stopped" or "blocked"
                ? MainWindowTheme.Warning
                : MainWindowTheme.MutedText;

        this.journeyStatusLabel.Text =
            DateTimeOffset.UtcNow < this.transientStatusUntil &&
            !string.IsNullOrWhiteSpace(this.transientStatus)
                ? this.transientStatus
                : string.IsNullOrWhiteSpace(journey.StatusText)
                    ? snapshot.StatusText
                    : journey.StatusText;

        this.selectNextTargetButton.Enabled =
            !this.commandBusy && journey.CanSelectNextTarget;
        this.autoPilotButton.Enabled =
            !this.commandBusy &&
            (journey.CanStart || journey.CanResume || journey.CanStop);
        this.autoPilotButton.Text = journey.CanStop
            ? "Stop Auto Pilot"
            : journey.CanResume
                ? "Resume Auto Pilot"
                : "Start Auto Pilot";
        this.openPlannerButton.Enabled = !this.commandBusy;
        this.secondaryRouteButton.Enabled =
            !this.commandBusy && journey.CanPlanReturnTrip;
        this.secondaryRouteButton.Text = "Plan return trip";
        this.clearRouteButton.Enabled =
            !this.commandBusy && journey.CanClear;
    }

    private void ShowUnavailable(string status)
    {
        this.destinationLabel.Text = "Navigation";
        this.routeStateLabel.Text = "Waiting for route state";
        this.nextTargetLabel.Text = status;
        this.hopsLabel.Text = "";
        this.warningLabel.Text = "";
        this.journeyStateLabel.Text = "AUTO PILOT · UNAVAILABLE";
        this.journeyStateLabel.ForeColor = MainWindowTheme.MutedText;
        this.journeyStatusLabel.Text = status;
        this.selectNextTargetButton.Enabled = false;
        this.autoPilotButton.Enabled = false;
        this.openPlannerButton.Enabled = !this.commandBusy;
        this.secondaryRouteButton.Enabled = false;
        this.clearRouteButton.Enabled = false;
    }

    private void ShowInGameButton_OnClick(
        object? sender,
        EventArgs e)
    {
        this.showInGameRequested();
    }

    private async void SelectNextTargetButton_OnClick(
        object? sender,
        EventArgs e)
    {
        if (!this.TryBeginCommand())
        {
            return;
        }

        try
        {
            var result = await this.clientManager
                .SelectNextNavigationTargetAsync(this.processId)
                .ConfigureAwait(true);
            this.SetTransientStatus(
                result.Succeeded
                    ? string.Create(
                        CultureInfo.CurrentCulture,
                        $"Selected {result.TargetName}.")
                    : result.Error);
        }
        catch (Exception exception)
        {
            this.SetTransientStatus(
                string.Create(
                    CultureInfo.CurrentCulture,
                    $"Could not select the next target: {exception.Message}"));
        }
        finally
        {
            this.EndCommand();
        }
    }

    private void AutoPilotButton_OnClick(
        object? sender,
        EventArgs e)
    {
        if (!this.TryBeginCommand())
        {
            return;
        }

        try
        {
            var snapshot = this.clientManager
                .GetNavigationPresentationSnapshot(this.processId);
            var result = snapshot.Journey.CanStop
                ? this.clientManager.StopNavigationAutoPilot(
                    this.processId)
                : this.clientManager.StartNavigationAutoPilot(
                    this.processId);
            this.SetTransientStatus(
                result.Succeeded
                    ? result.Snapshot?.StatusText ??
                      "Auto Pilot command completed."
                    : result.Error);
        }
        finally
        {
            this.EndCommand();
        }
    }

    private void OpenPlannerButton_OnClick(
        object? sender,
        EventArgs e)
    {
        this.clientManager.RequestNavigationPlanner(this.processId);
    }

    private void SecondaryRouteButton_OnClick(
        object? sender,
        EventArgs e)
    {
        if (!this.TryBeginCommand())
        {
            return;
        }

        try
        {
            var result = this.clientManager
                .PlanNavigationReturnTrip(this.processId);
            this.SetTransientStatus(
                result.Succeeded
                    ? result.Snapshot?.StatusText ??
                      "Return route planned."
                    : result.Error);
        }
        finally
        {
            this.EndCommand();
        }
    }

    private void ClearRouteButton_OnClick(
        object? sender,
        EventArgs e)
    {
        if (!this.TryBeginCommand())
        {
            return;
        }

        try
        {
            var result = this.clientManager
                .ClearNavigationRoute(this.processId);
            this.SetTransientStatus(
                result.Succeeded
                    ? result.Snapshot?.StatusText ??
                      "Route cleared."
                    : result.Error);
        }
        finally
        {
            this.EndCommand();
        }
    }

    private bool TryBeginCommand()
    {
        if (this.commandBusy)
        {
            return false;
        }

        this.commandBusy = true;
        this.RefreshPresentation();
        return true;
    }

    private void EndCommand()
    {
        this.commandBusy = false;
        this.RefreshPresentation();
    }

    private void SetTransientStatus(string? status)
    {
        this.transientStatus = string.IsNullOrWhiteSpace(status)
            ? "Navigation command completed."
            : status.Trim();
        this.transientStatusUntil =
            DateTimeOffset.UtcNow.AddSeconds(5);
    }

    private void NavigationCompanionForm_OnFormClosing(
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

        this.PersistPlacement(
            isVisible: preserveOpenState,
            isClosed: !preserveOpenState);
    }

    private void PersistPlacement(
        bool isVisible,
        bool isClosed)
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
            });
    }

    private static string ResolveDestinationName(
        AddonNavigationDestinationSnapshot? destination)
    {
        return destination?.Target?.Name ??
               destination?.Sector.SectorName ??
               "No destination";
    }


    private static string ResolveReturnDescription(
        AddonNavigationPlannedRouteSnapshot route)
    {
        var originName = route.OriginDestination.Target?.Name ??
                         route.OriginDestination.Sector.SectorName;

        return string.Create(
            CultureInfo.CurrentCulture,
            $"Return route available to {originName}");
    }

    private static string ResolveStepHeading(
        AddonNavigationRouteStepSnapshot? step)
    {
        if (step == null)
        {
            return "Route reconciliation in progress";
        }

        return step.Kind == "sector_transition"
            ? string.Create(
                CultureInfo.CurrentCulture,
                $"Next sector · {step.To.SectorName}")
            : "Final navigation target";
    }

    private static string ResolveStepDescription(
        AddonNavigationRouteStepSnapshot? step)
    {
        if (step == null)
        {
            return "Waiting for the next route step.";
        }

        var target = step.Kind == "sector_transition"
            ? step.DepartureTarget?.Name
            : step.FinalTarget?.Name;

        return string.IsNullOrWhiteSpace(target)
            ? string.Create(
                CultureInfo.CurrentCulture,
                $"→ {step.To.SectorName}")
            : string.Create(
                CultureInfo.CurrentCulture,
                $"→ {target}");
    }

    private static string FormatState(string state)
    {
        return state.Replace('_', ' ').ToUpperInvariant();
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
