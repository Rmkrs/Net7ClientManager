// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using Net7ClientManager.MissionJournal;
using Net7ClientManager.Models;
using Net7ClientManager.Navigation;
using Net7ClientManager.Observations;
using Net7ClientManager.Observations.Models;
using Net7ClientManager.Services;

public sealed partial class ClientHostForm
{
    private MissionWikiCompanionForm? missionWikiCompanionForm;
    private MissionWikiPresentationMode missionWikiPresentationMode =
        MissionWikiPresentationMode.InGame;
    private ClientMissionLogObservation missionWikiMissions =
        ClientMissionLogObservation.Unavailable(
            "Mission log has not been observed yet.");
    private uint missionWikiGameMissionAddress;
    private string? missionWikiGameMissionName;
    private MissionJobGuidance? missionWikiGameJobGuidance;
    private uint missionWikiCompanionMissionAddress;
    private double missionWikiLeftPaneRatio = 0.42;
    private double missionWikiListPaneRatio = 0.24;
    private double missionWikiDetailsPaneRatio = 0.28;

    private bool CanPresentMissionWikiCompanion =>
        this.missionWikiEnabled &&
        this.addonLifecycleState == ClientLifecycleState.InGame &&
        !this.addonTransitioning;

    private void RefreshMissionWikiEnabledFromSettings()
    {
        var enabled =
            !this.addonsSuspendedForSession &&
            this.clientManager.IsMissionWikiFeatureEnabled(
                this.clientInstance.ProcessId);

        if (this.missionWikiEnabled == enabled)
        {
            return;
        }

        this.missionWikiEnabled = enabled;

        if (enabled)
        {
            return;
        }

        this.CloseMissionWikiCompanion(
            preserveOpenPreference: true);
        this.CloseMissionWikiForm();
    }

    private void RefreshMissionWikiActiveSelection()
    {
        uint nextAddress;
        string? nextName;
        MissionJobGuidance? nextGuidance;

        if (this.missionWikiPresentationMode ==
            MissionWikiPresentationMode.InGame)
        {
            nextAddress = this.missionWikiGameMissionAddress;
            nextName = this.missionWikiGameMissionName;
            nextGuidance = this.missionWikiGameJobGuidance;
        }
        else
        {
            var usableMissions = this.missionWikiMissions.IsAvailable
                ? this.missionWikiMissions.Missions
                    .Where(IsMissionWikiCompanionMissionUsable)
                    .OrderBy(mission => mission.Slot)
                    .ToArray()
                : [];
            var selectedMission = usableMissions.FirstOrDefault(mission =>
                mission.Address == this.missionWikiCompanionMissionAddress);

            if (selectedMission == null &&
                this.missionWikiGameMissionAddress != 0)
            {
                selectedMission = usableMissions.FirstOrDefault(mission =>
                    mission.Address == this.missionWikiGameMissionAddress);
            }

            selectedMission ??= usableMissions.FirstOrDefault();
            this.missionWikiCompanionMissionAddress =
                selectedMission?.Address ?? 0;
            nextAddress = selectedMission?.Address ?? 0;
            nextName = string.IsNullOrWhiteSpace(selectedMission?.Name)
                ? null
                : selectedMission.Name.Trim();
            nextGuidance = selectedMission == null
                ? null
                : this.resolveMissionJobGuidance(selectedMission);
        }

        this.missionWikiMissionAddress = nextAddress;
        this.missionWikiMissionName = nextName;
        this.missionJobGuidance = nextGuidance;
    }

    private void NavigateMissionWikiInGameIfNeeded()
    {
        if (this.missionWikiWebViewForm == null ||
            string.IsNullOrWhiteSpace(this.missionWikiMissionName))
        {
            return;
        }

        var contentMismatch =
            !string.Equals(
                this.missionWikiWebViewForm.MissionName,
                this.missionWikiMissionName,
                StringComparison.Ordinal) ||
            !string.Equals(
                this.missionWikiWebViewForm.JobGuidanceFingerprint,
                this.missionJobGuidance?.Fingerprint,
                StringComparison.Ordinal);

        if (!contentMismatch)
        {
            return;
        }

        if (this.missionJobGuidance != null)
        {
            this.missionWikiWebViewForm.NavigateToJob(
                this.missionJobGuidance);
        }
        else
        {
            this.missionWikiWebViewForm.NavigateToMission(
                this.missionWikiMissionName);
        }
    }

    private void HideMissionWikiInGame()
    {
        this.missionWikiWebViewForm?.Hide();
        this.missionWikiControlForm?.Hide();
    }

    private void ShowMissionWikiCompanion(
        bool persistMode = true,
        bool activate = true)
    {
        if (persistMode)
        {
            this.SetMissionWikiPresentationMode(
                MissionWikiPresentationMode.Companion);
        }

        this.HideMissionWikiInGame();
        this.RefreshMissionWikiActiveSelection();

        if (!this.CanPresentMissionWikiCompanion)
        {
            return;
        }

        if (this.missionWikiCompanionForm is
            { IsDisposed: false } existing)
        {
            existing.Owner = null;
            existing.RestorePlacement(this.Bounds);
            existing.PrepareForInitialShow();
            existing.MarkOpen();
            this.SyncMissionWikiCompanionPresentation();

            if (existing.WindowState == FormWindowState.Minimized)
            {
                existing.WindowState = FormWindowState.Normal;
            }

            existing.Show();
            existing.ScheduleInitialDpiReconciliation();

            if (activate)
            {
                existing.BringToFront();
                existing.Activate();
            }

            return;
        }

        this.missionWikiCompanionForm =
            new MissionWikiCompanionForm(
                this.clientInstance.ProcessId,
                this.resolveMissionWikiDestination,
                this.setMissionWikiDestination,
                this.resolveAddonWindowPlacement,
                this.saveAddonWindowPlacement,
                this.ShowMissionWikiInGame,
                this.SelectMissionWikiCompanionMission,
                this.SaveMissionWikiPaneRatios,
                this.missionWikiLeftPaneRatio,
                this.missionWikiListPaneRatio,
                this.missionWikiDetailsPaneRatio);
        this.missionWikiCompanionForm.UserClosed +=
            this.MissionWikiCompanionForm_OnUserClosed;
        this.missionWikiCompanionForm.RestorePlacement(this.Bounds);
        this.missionWikiCompanionForm.PrepareForInitialShow();
        this.SyncMissionWikiCompanionPresentation();
        this.missionWikiCompanionForm.Show();
        this.missionWikiCompanionForm
            .ScheduleInitialDpiReconciliation();
        this.missionWikiCompanionForm.MarkOpen();

        if (activate)
        {
            this.missionWikiCompanionForm.Activate();
        }
    }

    private void ShowMissionWikiInGame()
    {
        this.CloseMissionWikiCompanionForPresentationSwitch();
        this.SetMissionWikiPresentationMode(
            MissionWikiPresentationMode.InGame);
        this.RefreshMissionWikiActiveSelection();
        this.SyncMissionWiki();
    }

    private void SetMissionWikiPresentationMode(
        MissionWikiPresentationMode mode)
    {
        this.missionWikiPresentationMode = mode;
        this.clientManager.SetMissionWikiPresentationMode(
            this.clientInstance.ProcessId,
            mode);
    }

    private void SelectMissionWikiCompanionMission(uint address)
    {
        if (address == 0)
        {
            return;
        }

        this.missionWikiCompanionMissionAddress = address;
        this.RefreshMissionWikiActiveSelection();
        this.SyncMissionWikiCompanionPresentation();
    }

    private void SyncMissionWikiCompanionPresentation()
    {
        if (this.missionWikiCompanionForm is not
            { IsDisposed: false } form)
        {
            return;
        }

        var missions = this.missionWikiMissions.IsAvailable
            ? this.missionWikiMissions.Missions
                .Where(IsMissionWikiCompanionMissionUsable)
                .OrderBy(mission => mission.Slot)
                .ToArray()
            : [];
        var pilotName =
            this.clientInstance.LiveCharacterIdentity.Name ??
            this.appliedSlotName ??
            "hosted pilot";

        form.UpdatePresentation(
            pilotName,
            missions,
            this.missionWikiMissionAddress,
            this.missionJobGuidance);
    }

    private void MissionWikiCompanionForm_OnUserClosed(
        object? sender,
        EventArgs e)
    {
        if (sender is MissionWikiCompanionForm form)
        {
            form.UserClosed -=
                this.MissionWikiCompanionForm_OnUserClosed;
        }

        this.missionWikiCompanionForm = null;
        this.SetMissionWikiPresentationMode(
            MissionWikiPresentationMode.InGame);
        this.RefreshMissionWikiActiveSelection();
        this.SyncMissionWiki();
    }

    private void CloseMissionWikiCompanion(
        bool preserveOpenPreference)
    {
        var form = this.missionWikiCompanionForm;
        this.missionWikiCompanionForm = null;

        if (form == null || form.IsDisposed)
        {
            return;
        }

        form.UserClosed -=
            this.MissionWikiCompanionForm_OnUserClosed;

        if (preserveOpenPreference)
        {
            form.ClosePreservingOpenState();
        }
        else
        {
            form.CloseForPresentationSwitch();
        }

        form.Dispose();
    }

    private void CloseMissionWikiCompanionForPresentationSwitch()
    {
        this.CloseMissionWikiCompanion(
            preserveOpenPreference: false);
    }

    private void SaveMissionWikiPaneRatios(
        double leftPaneRatio,
        double missionListPaneRatio,
        double detailsPaneRatio)
    {
        this.missionWikiLeftPaneRatio = leftPaneRatio;
        this.missionWikiListPaneRatio = missionListPaneRatio;
        this.missionWikiDetailsPaneRatio = detailsPaneRatio;
        this.clientManager.SetMissionWikiPaneRatios(
            this.clientInstance.ProcessId,
            leftPaneRatio,
            missionListPaneRatio,
            detailsPaneRatio);
    }

    private static bool IsMissionWikiCompanionMissionUsable(
        ClientMissionObservation mission)
    {
        return mission.Address != 0 &&
               mission.ValidState > 0 &&
               !string.IsNullOrWhiteSpace(mission.Name);
    }
}
