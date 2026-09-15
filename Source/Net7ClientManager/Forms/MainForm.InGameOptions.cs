// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using Net7ClientManager.Models;
using Net7ClientManager.Observations;
using Net7ClientManager.Services;

public sealed partial class MainForm
{
    private void ClientManager_OnInGameOptionsRequested(
        object? sender,
        InGameOptionsRequestedEventArgs e)
    {
        this.OpenInGameOptions(
            e.ProcessId,
            e.Owner,
            showTour: false);
    }

    private void OpenInGameOptions(
        int processId,
        IWin32Window owner,
        bool showTour)
    {
        if (this.inGameOptionsOpen)
        {
            return;
        }

        var client = this.clientManager.Clients.FirstOrDefault(candidate =>
            candidate.ProcessId == processId);

        if (client == null ||
            client.LifecycleState != ClientLifecycleState.InGame)
        {
            return;
        }

        var settings = this.clientManager.FleetCommandSettings;
        settings.EnsureDefaults();

        var historySettings = this.clientManager.HistorySettings;
        var worldFindSettings = this.clientManager.WorldFindSettings;
        var itemToolTipSettings =
            this.clientManager.GameItemToolTipSettings;
        var buffOverlaySettings =
            this.clientManager.GameBuffOverlaySettings;
        var initialValues = new InGameOptionsValues(
            settings.EffectiveCommandMenuShowMode,
            settings.CommandMenuPlacement,
            settings.CommandMenuHotKey,
            settings.FleetFireAllHotKey,
            new CommandPaletteFixedPosition(
                settings.CommandMenuFixedPositionX,
                settings.CommandMenuFixedPositionY),
            this.clientManager.IsMissionWikiFeatureEnabled(
                processId),
            historySettings.RecordMissionHistory,
            historySettings.RecordActivityHistory,
            historySettings.RecordCombatHistory,
            this.clientManager.NavigationPlannerSettings.UseWormholes,
            worldFindSettings.KeepSearchOpenInTab,
            worldFindSettings.ShowVendorCompanion,
            buffOverlaySettings.ShowDurations,
            itemToolTipSettings.Enabled,
            itemToolTipSettings.HorizontalOffset,
            itemToolTipSettings.VerticalOffset);

        this.inGameOptionsOpen = true;
        this.inGameOptionsProcessId = processId;
        this.CloseCommandOverlay();

        try
        {
            using var form = new InGameOptionsForm(
                initialValues,
                this.clientManager.ValidateCommandPaletteHotKeyAsync,
                this.clientManager.ValidateFleetFireAllHotKeyAsync,
                previewOwner =>
                    this.PickCommandPaletteFixedPosition(
                        processId,
                        previewOwner),
                (enabled, horizontalOffset, verticalOffset) =>
                    this.clientManager.PreviewGameItemToolTipOptions(
                        enabled,
                        horizontalOffset,
                        verticalOffset),
                values => this.TryApplyInGameOptions(
                    processId,
                    values),
                this.CreateSupportDiagnosticsReport);

            if (showTour)
            {
                form.Shown += (_, _) =>
                    form.BeginInvoke(() => form.ShowHelpTour());
            }

            this.inGameOptionsForm = form;
            _ = form.ShowDialog(owner);
        }
        finally
        {
            this.inGameOptionsForm = null;
            this.inGameOptionsProcessId = null;
            this.inGameOptionsOpen = false;
            this.ApplyCommandPaletteRuntimeSettings();
            this.ApplyFleetFireAllHotKeyRuntimeSettings();
        }
    }

    private string? TryApplyInGameOptions(
        int processId,
        InGameOptionsValues values)
    {
        var client = this.clientManager.Clients.FirstOrDefault(candidate =>
            candidate.ProcessId == processId);

        if (client?.LifecycleState != ClientLifecycleState.InGame)
        {
            return "The owning game client is no longer in game.";
        }

        var settings = this.clientManager.FleetCommandSettings;
        var previousShowMode = settings.EffectiveCommandMenuShowMode;
        var previousHotKey = settings.CommandMenuHotKey;
        var previousFleetFireAllHotKey =
            settings.FleetFireAllHotKey;
        var previousPlacement = settings.CommandMenuPlacement;
        var previousPositionX = settings.CommandMenuFixedPositionX;
        var previousPositionY = settings.CommandMenuFixedPositionY;
        var previousMissionWikiEnabled =
            this.clientManager.IsMissionWikiFeatureEnabled(processId);
        var historySettings = this.clientManager.HistorySettings;
        var previousRecordMissionHistory =
            historySettings.RecordMissionHistory;
        var previousRecordActivityHistory =
            historySettings.RecordActivityHistory;
        var previousRecordCombatHistory =
            historySettings.RecordCombatHistory;
        var navigationPlannerSettings =
            this.clientManager.NavigationPlannerSettings;
        var previousUseWormholes =
            navigationPlannerSettings.UseWormholes;
        var worldFindSettings = this.clientManager.WorldFindSettings;
        var previousKeepGalaxyFinderSearchOpen =
            worldFindSettings.KeepSearchOpenInTab;
        var previousShowVendorCompanion =
            worldFindSettings.ShowVendorCompanion;
        var itemToolTipSettings =
            this.clientManager.GameItemToolTipSettings;
        var buffOverlaySettings =
            this.clientManager.GameBuffOverlaySettings;
        var previousItemToolTipsEnabled =
            itemToolTipSettings.Enabled;
        var previousItemToolTipHorizontalOffset =
            itemToolTipSettings.HorizontalOffset;
        var previousItemToolTipVerticalOffset =
            itemToolTipSettings.VerticalOffset;
        var previousShowBuffDurations =
            buffOverlaySettings.ShowDurations;

        settings.SetCommandMenuShowMode(values.ShowMode);
        settings.CommandMenuHotKey = values.HotKey;
        settings.CommandMenuPlacement = values.PlacementMode;
        settings.CommandMenuFixedPositionX = Math.Clamp(
            values.FixedPosition.X,
            0.0,
            1.0);
        settings.CommandMenuFixedPositionY = Math.Clamp(
            values.FixedPosition.Y,
            0.0,
            1.0);

        this.ApplyCommandPaletteRuntimeSettings();
        if (values.ShowMode == CommandPaletteShowMode.Keybinding &&
            this.commandPaletteKeyboardHook == null)
        {
            this.RestoreCommandPaletteSettings(
                previousShowMode,
                previousHotKey,
                previousPlacement,
                previousPositionX,
                previousPositionY);

            return this.commandPaletteKeyboardHookError ??
                   "The Command Palette keyboard hook is unavailable.";
        }

        settings.FleetFireAllHotKey = values.FleetFireAllHotKey;
        this.ApplyFleetFireAllHotKeyRuntimeSettings();

        if (values.FleetFireAllHotKey != Keys.None &&
            this.fleetFireAllKeyboardHook == null)
        {
            this.RestoreCommandPaletteSettings(
                previousShowMode,
                previousHotKey,
                previousPlacement,
                previousPositionX,
                previousPositionY);
            settings.FleetFireAllHotKey =
                previousFleetFireAllHotKey;
            this.ApplyFleetFireAllHotKeyRuntimeSettings();

            return this.fleetFireAllKeyboardHookError ??
                   "The Fleet Fire All keyboard hook is unavailable.";
        }

        this.clientManager.SetMissionWikiFeatureEnabled(
            processId,
            values.MissionWikiEnabled,
            persist: false);
        this.clientManager.SetHistoryRecordingOptions(
            values.RecordMissionHistory,
            values.RecordActivityHistory,
            values.RecordCombatHistory);
        navigationPlannerSettings.UseWormholes =
            values.UseWormholes;
        worldFindSettings.KeepSearchOpenInTab =
            values.KeepGalaxyFinderSearchOpen;
        worldFindSettings.ShowVendorCompanion =
            values.ShowVendorCompanion;
        this.worldFindForm?.ApplyCurrentSettings();
        this.clientManager.SetVendorShoppingCompanionEnabled(
            values.ShowVendorCompanion);
        this.clientManager.SetGameBuffOverlayOptions(
            values.ShowBuffDurations);
        this.clientManager.SetGameItemToolTipOptions(
            values.EnhancedItemToolTipsEnabled,
            values.ItemToolTipHorizontalOffset,
            values.ItemToolTipVerticalOffset);

        try
        {
            this.clientManager.SaveSettings();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            this.RestoreCommandPaletteSettings(
                previousShowMode,
                previousHotKey,
                previousPlacement,
                previousPositionX,
                previousPositionY);
            settings.FleetFireAllHotKey =
                previousFleetFireAllHotKey;
            this.ApplyFleetFireAllHotKeyRuntimeSettings();

            this.clientManager.SetMissionWikiFeatureEnabled(
                processId,
                previousMissionWikiEnabled,
                persist: false);
            this.clientManager.SetHistoryRecordingOptions(
                previousRecordMissionHistory,
                previousRecordActivityHistory,
                previousRecordCombatHistory);
            navigationPlannerSettings.UseWormholes =
                previousUseWormholes;
            worldFindSettings.KeepSearchOpenInTab =
                previousKeepGalaxyFinderSearchOpen;
            worldFindSettings.ShowVendorCompanion =
                previousShowVendorCompanion;
            this.worldFindForm?.ApplyCurrentSettings();
            this.clientManager.SetVendorShoppingCompanionEnabled(
                previousShowVendorCompanion);
            this.clientManager.SetGameBuffOverlayOptions(
                previousShowBuffDurations);
            this.clientManager.SetGameItemToolTipOptions(
                previousItemToolTipsEnabled,
                previousItemToolTipHorizontalOffset,
                previousItemToolTipVerticalOffset);

            return string.Concat(
                "The in-game options could not be saved: ",
                exception.Message);
        }

        this.clientManager.ApplyHistoryRecordingOptions();
        this.RecordCommandPaletteDiagnostic(
            "Settings applied",
            client,
            string.Concat(
                "showMode=",
                values.ShowMode,
                "; shortcut=",
                CommandPaletteHotKeyValidator.FormatHotKey(values.HotKey),
                "; placement=",
                values.PlacementMode,
                "; fleetFireAll=",
                values.FleetFireAllHotKey == Keys.None
                    ? "Not set"
                    : CommandPaletteHotKeyValidator.FormatHotKey(
                        values.FleetFireAllHotKey)));
        return null;
    }

    private void RestoreCommandPaletteSettings(
        CommandPaletteShowMode showMode,
        Keys hotKey,
        CommandPalettePlacementMode placementMode,
        double positionX,
        double positionY)
    {
        var settings = this.clientManager.FleetCommandSettings;
        settings.SetCommandMenuShowMode(showMode);
        settings.CommandMenuHotKey = hotKey;
        settings.CommandMenuPlacement = placementMode;
        settings.CommandMenuFixedPositionX = positionX;
        settings.CommandMenuFixedPositionY = positionY;
        this.ApplyCommandPaletteRuntimeSettings();
    }

    private void RefreshInGameOptionsLifecycle()
    {
        if (!this.inGameOptionsOpen ||
            this.inGameOptionsProcessId is not { } processId ||
            this.inGameOptionsForm is not { IsDisposed: false } form)
        {
            return;
        }

        var client = this.clientManager.Clients.FirstOrDefault(candidate =>
            candidate.ProcessId == processId);

        if (client?.LifecycleState == ClientLifecycleState.InGame)
        {
            return;
        }

        form.Close();
    }
}
