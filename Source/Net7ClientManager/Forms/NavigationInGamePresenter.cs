// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using System.Globalization;
using Net7ClientManager.Addons.Contracts;
using Net7ClientManager.Core;
using Net7ClientManager.Navigation;

internal sealed class NavigationInGamePresenter : IDisposable
{
    private const string DestinationWidgetId = "destination";
    private const string PopOutWidgetId = "pop-out";
    private const string RouteStateWidgetId = "route-state";
    private const string NextTargetWidgetId = "next-target";
    private const string HopsWidgetId = "hops";
    private const string JourneyStateWidgetId = "journey-state";
    private const string JourneyStatusWidgetId = "journey-status";
    private const string WarningWidgetId = "warning";
    private const string SelectNextTargetWidgetId = "select-next-target";
    private const string AutoPilotWidgetId = "auto-pilot";
    private const string OpenPlannerWidgetId = "open-planner";
    private const string SecondaryRouteWidgetId = "secondary-route";
    private const string ClearRouteWidgetId = "clear-route";

    private static readonly AddonUiColor accentColor =
        new(255, 34, 211, 238);
    private static readonly AddonUiColor mutedTextColor =
        new(255, 190, 199, 211);
    private static readonly AddonUiColor warningColor =
        new(255, 255, 193, 72);
    private static readonly AddonUiColor successColor =
        new(255, 93, 214, 126);

    private readonly ClientManager clientManager;
    private readonly int processId;
    private readonly Action<AddonUiCommand> applyCommand;
    private readonly Action popOutRequested;
    private readonly System.Windows.Forms.Timer refreshTimer = new()
    {
        Interval = 300,
    };

    private bool visible;
    private bool commandBusy;
    private bool disposed;
    private string transientStatus = "";
    private DateTimeOffset transientStatusUntil;
    private string presentationFingerprint = "";

    public NavigationInGamePresenter(
        ClientManager clientManager,
        int processId,
        Action<AddonUiCommand> applyCommand,
        Action popOutRequested)
    {
        this.clientManager = clientManager ??
            throw new ArgumentNullException(nameof(clientManager));
        this.processId = processId;
        this.applyCommand = applyCommand ??
            throw new ArgumentNullException(nameof(applyCommand));
        this.popOutRequested = popOutRequested ??
            throw new ArgumentNullException(nameof(popOutRequested));

        this.refreshTimer.Tick += this.RefreshTimer_OnTick;
    }

    public bool IsVisible => this.visible;

    public bool Owns(AddonUiInteraction interaction)
    {
        ArgumentNullException.ThrowIfNull(interaction);

        return string.Equals(
            interaction.AddonId,
            NavigationPresentationIds.BuiltInAddonId,
            StringComparison.Ordinal);
    }

    public void Show(bool reopen = true)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);

        this.visible = true;
        this.presentationFingerprint = "";
        this.RefreshPresentation();

        if (reopen)
        {
            this.Apply(
                AddonUiCommandKind.ShowWindow,
                NavigationPresentationIds.InGameWindowId);
        }

        this.refreshTimer.Start();
    }

    public void Hide()
    {
        if (this.disposed)
        {
            return;
        }

        this.visible = false;
        this.refreshTimer.Stop();
        this.presentationFingerprint = "";
        this.Apply(AddonUiCommandKind.ClearAddon, widgetId: "");
    }

    public void RefreshNow()
    {
        if (!this.visible || this.disposed)
        {
            return;
        }

        this.presentationFingerprint = "";
        this.RefreshPresentation();
    }

    public async Task HandleInteractionAsync(
        AddonUiInteraction interaction)
    {
        ArgumentNullException.ThrowIfNull(interaction);

        if (!this.Owns(interaction) ||
            interaction.Kind != AddonUiInteractionKind.Click)
        {
            return;
        }

        switch (interaction.WidgetId)
        {
            case PopOutWidgetId:
                this.popOutRequested();
                return;

            case SelectNextTargetWidgetId:
                await this.SelectNextTargetAsync().ConfigureAwait(true);
                return;

            case AutoPilotWidgetId:
                this.ToggleAutoPilot();
                return;

            case OpenPlannerWidgetId:
                this.clientManager.RequestNavigationPlanner(this.processId);
                return;

            case SecondaryRouteWidgetId:
                this.PlanReturnTrip();
                return;

            case ClearRouteWidgetId:
                this.ClearRoute();
                return;
        }
    }

    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;
        this.refreshTimer.Stop();
        this.refreshTimer.Tick -= this.RefreshTimer_OnTick;
        this.refreshTimer.Dispose();
    }

    private void RefreshTimer_OnTick(object? sender, EventArgs e)
    {
        this.RefreshPresentation();
    }

    private void RefreshPresentation()
    {
        if (!this.visible || this.disposed)
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
            this.RenderUnavailable(
                string.Create(
                    CultureInfo.CurrentCulture,
                    $"Navigation could not be refreshed: {exception.Message}"));
            return;
        }

        var route = snapshot.Route;
        var destinationName = ResolveDestinationName(route?.Destination);
        var routeState = "No active route";
        var nextTarget = string.IsNullOrWhiteSpace(snapshot.StatusText)
            ? "Open the planner to choose a destination."
            : snapshot.StatusText;
        var hops = "0 hops remaining";
        var warning = "";

        if (snapshot.HasRoute && route != null)
        {
            if (snapshot.Status == "destination_reached")
            {
                routeState = "Destination reached";
                nextTarget = snapshot.Journey.CanPlanReturnTrip
                    ? ResolveReturnDescription(route)
                    : "Journey complete.";
                hops = string.Create(
                    CultureInfo.CurrentCulture,
                    $"0 hops remaining · {route.CompletedHopCount} completed");
            }
            else
            {
                routeState = ResolveStepHeading(route.NextStep);
                nextTarget = ResolveStepDescription(route.NextStep);
                hops = string.Create(
                    CultureInfo.CurrentCulture,
                    $"{route.RemainingHopCount} hops remaining · {route.CompletedHopCount} completed");
                warning = route.Warnings.FirstOrDefault() ?? "";
            }
        }

        var journey = snapshot.Journey;
        var journeyState = string.Create(
            CultureInfo.CurrentCulture,
            $"AUTO PILOT · {FormatState(journey.State)}");
        var journeyStatus =
            DateTimeOffset.UtcNow < this.transientStatusUntil &&
            !string.IsNullOrWhiteSpace(this.transientStatus)
                ? this.transientStatus
                : string.IsNullOrWhiteSpace(journey.StatusText)
                    ? snapshot.StatusText
                    : journey.StatusText;
        var journeyColor = journey.IsActive
            ? successColor
            : journey.State is "stopped" or "blocked"
                ? warningColor
                : mutedTextColor;
        var autoPilotText = journey.CanStop
            ? "Stop Auto Pilot"
            : journey.CanResume
                ? "Resume Auto Pilot"
                : "Start Auto Pilot";
        var fingerprint = string.Join(
            "\u001f",
            new object?[]
            {
                snapshot.IsAvailable,
                destinationName,
                routeState,
                nextTarget,
                hops,
                warning,
                journeyState,
                journeyStatus,
                journeyColor,
                this.commandBusy,
                journey.CanSelectNextTarget,
                journey.CanStart,
                journey.CanResume,
                journey.CanStop,
                journey.CanPlanReturnTrip,
                journey.CanClear,
                autoPilotText,
            });

        if (string.Equals(
                fingerprint,
                this.presentationFingerprint,
                StringComparison.Ordinal))
        {
            return;
        }

        this.presentationFingerprint = fingerprint;
        this.RenderWindow(
            destinationName,
            routeState,
            nextTarget,
            hops,
            warning,
            journeyState,
            journeyStatus,
            journeyColor,
            snapshot.IsAvailable &&
            !this.commandBusy &&
            journey.CanSelectNextTarget,
            snapshot.IsAvailable &&
            !this.commandBusy &&
            (journey.CanStart || journey.CanResume || journey.CanStop),
            autoPilotText,
            !this.commandBusy,
            snapshot.IsAvailable &&
            !this.commandBusy &&
            journey.CanPlanReturnTrip,
            snapshot.IsAvailable &&
            !this.commandBusy &&
            journey.CanClear);
    }

    private void RenderUnavailable(string status)
    {
        var fingerprint = string.Concat("unavailable\u001f", status);

        if (string.Equals(
                fingerprint,
                this.presentationFingerprint,
                StringComparison.Ordinal))
        {
            return;
        }

        this.presentationFingerprint = fingerprint;
        this.RenderWindow(
            "Navigation",
            "Waiting for route state",
            status,
            "",
            "",
            "AUTO PILOT · UNAVAILABLE",
            status,
            mutedTextColor,
            canSelectNextTarget: false,
            canToggleAutoPilot: false,
            autoPilotText: "Start Auto Pilot",
            canOpenPlanner: !this.commandBusy,
            canPlanReturnTrip: false,
            canClear: false);
    }

    private void RenderWindow(
        string destination,
        string routeState,
        string nextTarget,
        string hops,
        string warning,
        string journeyState,
        string journeyStatus,
        AddonUiColor journeyColor,
        bool canSelectNextTarget,
        bool canToggleAutoPilot,
        string autoPilotText,
        bool canOpenPlanner,
        bool canPlanReturnTrip,
        bool canClear)
    {
        this.Apply(new AddonUiCommand
        {
            Kind = AddonUiCommandKind.UpsertWindow,
            OwnerProcessId = this.processId,
            AddonId = NavigationPresentationIds.BuiltInAddonId,
            WidgetId = NavigationPresentationIds.InGameWindowId,
            Window = new AddonUiWindow
            {
                Title = "NAVIGATION",
                Anchor = AddonUiAnchor.TopRight,
                X = 14,
                Y = 48,
                Width = 560,
                Height = 446,
                CanClose = true,
                CanMinimize = true,
                ContentPadding = 12,
            },
        });

        this.ApplyLabel(
            DestinationWidgetId,
            destination,
            x: 0,
            y: 0,
            width: 376,
            height: 31,
            fontSize: 14,
            textColor: accentColor);
        this.ApplyButton(
            PopOutWidgetId,
            "Pop out",
            "Move Navigation into a desktop companion beside the game.",
            x: 390,
            y: 0,
            width: 146,
            height: 31,
            enabled: true,
            textAlignment: AddonUiTextAlignment.Center);
        this.ApplyLabel(
            RouteStateWidgetId,
            routeState,
            x: 0,
            y: 42,
            width: 536,
            height: 24,
            fontSize: 10.5f,
            textColor: AddonUiColor.White);
        this.ApplyLabel(
            NextTargetWidgetId,
            nextTarget,
            x: 0,
            y: 70,
            width: 536,
            height: 42,
            fontSize: 10,
            textColor: mutedTextColor,
            bold: false);
        this.ApplyLabel(
            HopsWidgetId,
            hops,
            x: 0,
            y: 116,
            width: 536,
            height: 24,
            fontSize: 9.5f,
            textColor: mutedTextColor);
        this.ApplyLabel(
            JourneyStateWidgetId,
            journeyState,
            x: 0,
            y: 150,
            width: 536,
            height: 24,
            fontSize: 10.5f,
            textColor: journeyColor);
        this.ApplyLabel(
            JourneyStatusWidgetId,
            journeyStatus,
            x: 0,
            y: 178,
            width: 536,
            height: 48,
            fontSize: 9.5f,
            textColor: mutedTextColor,
            bold: false);
        this.ApplyLabel(
            WarningWidgetId,
            warning,
            x: 0,
            y: 228,
            width: 536,
            height: 25,
            fontSize: 8.5f,
            textColor: warningColor,
            bold: false);

        this.ApplyButton(
            SelectNextTargetWidgetId,
            "Select next target",
            "Cycle to the next live target required by the active route.",
            x: 0,
            y: 250,
            width: 262,
            height: 40,
            enabled: canSelectNextTarget);
        this.ApplyButton(
            AutoPilotWidgetId,
            autoPilotText,
            "Start, resume, or stop Auto Pilot for this hosted client.",
            x: 274,
            y: 250,
            width: 262,
            height: 40,
            enabled: canToggleAutoPilot);
        this.ApplyButton(
            OpenPlannerWidgetId,
            "Open planner",
            "Open the full Route Planner for this hosted client.",
            x: 0,
            y: 298,
            width: 262,
            height: 40,
            enabled: canOpenPlanner);
        this.ApplyButton(
            SecondaryRouteWidgetId,
            "Plan return trip",
            "Plan a return route to the journey origin when available.",
            x: 274,
            y: 298,
            width: 262,
            height: 40,
            enabled: canPlanReturnTrip);
        this.ApplyButton(
            ClearRouteWidgetId,
            "Clear route",
            "Remove the active route after Auto Pilot has stopped.",
            x: 0,
            y: 346,
            width: 536,
            height: 40,
            enabled: canClear,
            textAlignment: AddonUiTextAlignment.Center);
    }

    private void ApplyLabel(
        string widgetId,
        string text,
        int x,
        int y,
        int width,
        int height,
        float fontSize,
        AddonUiColor textColor,
        bool bold = true)
    {
        this.Apply(new AddonUiCommand
        {
            Kind = AddonUiCommandKind.UpsertLabel,
            OwnerProcessId = this.processId,
            AddonId = NavigationPresentationIds.BuiltInAddonId,
            WidgetId = widgetId,
            Label = new AddonUiLabel
            {
                ParentWidgetId = NavigationPresentationIds.InGameWindowId,
                Text = text,
                X = x,
                Y = y,
                Width = width,
                Height = height,
                FontSize = fontSize,
                IsBold = bold,
                Padding = 0,
                CornerRadius = 0,
                TextColor = textColor,
                BackgroundColor = AddonUiColor.Transparent,
                BorderColor = AddonUiColor.Transparent,
            },
        });
    }

    private void ApplyButton(
        string widgetId,
        string text,
        string tooltip,
        int x,
        int y,
        int width,
        int height,
        bool enabled,
        AddonUiTextAlignment textAlignment = AddonUiTextAlignment.Left)
    {
        this.Apply(new AddonUiCommand
        {
            Kind = AddonUiCommandKind.UpsertButton,
            OwnerProcessId = this.processId,
            AddonId = NavigationPresentationIds.BuiltInAddonId,
            WidgetId = widgetId,
            Button = new AddonUiButton
            {
                ParentWidgetId = NavigationPresentationIds.InGameWindowId,
                Text = text,
                Tooltip = tooltip,
                X = x,
                Y = y,
                Width = width,
                Height = height,
                IsEnabled = enabled,
                TextAlignment = textAlignment,
            },
        });
    }

    private async Task SelectNextTargetAsync()
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

    private void ToggleAutoPilot()
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
                ? this.clientManager.StopNavigationAutoPilot(this.processId)
                : this.clientManager.StartNavigationAutoPilot(this.processId);
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

    private void PlanReturnTrip()
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

    private void ClearRoute()
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
        this.presentationFingerprint = "";
        this.RefreshPresentation();
        return true;
    }

    private void EndCommand()
    {
        this.commandBusy = false;
        this.presentationFingerprint = "";
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

    private void Apply(
        AddonUiCommandKind kind,
        string widgetId)
    {
        this.Apply(new AddonUiCommand
        {
            Kind = kind,
            OwnerProcessId = this.processId,
            AddonId = NavigationPresentationIds.BuiltInAddonId,
            WidgetId = widgetId,
        });
    }

    private void Apply(AddonUiCommand command)
    {
        this.applyCommand(command);
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
}
