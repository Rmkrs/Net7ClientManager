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
    private static readonly TimeSpan MissionWikiTransitionSettleDuration =
        TimeSpan.FromSeconds(8);

    private static readonly TimeSpan MissionWikiDestructiveSnapshotConfirmationDuration =
        TimeSpan.FromSeconds(8);

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
    private int? missionWikiCompanionMissionRawId;
    private string? missionWikiCompanionMissionName;
    private int? missionWikiCompanionMissionSlot;
    private string? missionWikiCompanionPilotName;
    private DateTimeOffset missionWikiMissionSnapshotHoldUntil;
    private DateTimeOffset? missionWikiDestructiveSnapshotObservedAt;
    private string? missionWikiDestructiveSnapshotFingerprint;
    private double missionWikiLeftPaneRatio = 0.42;
    private double missionWikiListPaneRatio = 0.24;
    private double missionWikiDetailsPaneRatio = 0.28;

    private bool CanPresentMissionWikiCompanion =>
        this.missionWikiEnabled &&
        this.addonLifecycleState == ClientLifecycleState.InGame &&
        !this.addonTransitioning;

    private void ResetMissionWikiSessionState()
    {
        this.missionWikiMissions =
            ClientMissionLogObservation.Unavailable(
                "No active gameplay session.");
        this.missionWikiGameMissionAddress = 0;
        this.missionWikiGameMissionName = null;
        this.missionWikiGameJobGuidance = null;
        this.missionWikiCompanionPilotName = null;
        this.missionWikiMissionSnapshotHoldUntil = default;
        this.missionWikiDestructiveSnapshotObservedAt = null;
        this.missionWikiDestructiveSnapshotFingerprint = null;
        this.RememberMissionWikiCompanionSelection(null);
        this.missionWikiMissionAddress = 0;
        this.missionWikiMissionName = null;
        this.missionJobGuidance = null;
    }

    private void HoldMissionWikiMissionSnapshot()
    {
        var holdUntil =
            DateTimeOffset.UtcNow +
            MissionWikiTransitionSettleDuration;

        if (holdUntil > this.missionWikiMissionSnapshotHoldUntil)
        {
            this.missionWikiMissionSnapshotHoldUntil = holdUntil;
        }

        this.missionWikiDestructiveSnapshotObservedAt = null;
        this.missionWikiDestructiveSnapshotFingerprint = null;
    }

    private void RefreshMissionWikiPilotSession()
    {
        var observedPilotName =
            this.clientInstance.LiveCharacterIdentity.Name;

        if (string.IsNullOrWhiteSpace(observedPilotName))
        {
            return;
        }

        observedPilotName = observedPilotName.Trim();

        if (!string.IsNullOrWhiteSpace(
                this.missionWikiCompanionPilotName) &&
            !string.Equals(
                this.missionWikiCompanionPilotName,
                observedPilotName,
                StringComparison.Ordinal))
        {
            this.ResetMissionWikiSessionState();
        }

        this.missionWikiCompanionPilotName = observedPilotName;
    }

    private bool ShouldAcceptMissionWikiMissionSnapshot(
        ClientMissionLogObservation missions)
    {
        if (!missions.IsAvailable)
        {
            return false;
        }

        var incomingMissions = missions.Missions
            .Where(IsMissionWikiCompanionMissionUsable)
            .ToArray();
        var retainedMissions = this.missionWikiMissions.IsAvailable
            ? this.missionWikiMissions.Missions
                .Where(IsMissionWikiCompanionMissionUsable)
                .ToArray()
            : [];

        if (retainedMissions.Length == 0)
        {
            this.missionWikiDestructiveSnapshotObservedAt = null;
            this.missionWikiDestructiveSnapshotFingerprint = null;
            return true;
        }

        var incomingIdentities = incomingMissions
            .Select(BuildMissionWikiMissionIdentity)
            .ToHashSet(StringComparer.Ordinal);
        var removesRetainedMission = retainedMissions.Any(mission =>
            !incomingIdentities.Contains(
                BuildMissionWikiMissionIdentity(mission)));

        if (!removesRetainedMission)
        {
            this.missionWikiMissionSnapshotHoldUntil = default;
            this.missionWikiDestructiveSnapshotObservedAt = null;
            this.missionWikiDestructiveSnapshotFingerprint = null;
            return true;
        }

        var now = DateTimeOffset.UtcNow;

        if (now < this.missionWikiMissionSnapshotHoldUntil)
        {
            this.missionWikiDestructiveSnapshotObservedAt = null;
            this.missionWikiDestructiveSnapshotFingerprint = null;
            return false;
        }

        var destructiveSnapshotFingerprint = string.Join(
            "|",
            incomingIdentities.OrderBy(
                identity => identity,
                StringComparer.Ordinal));

        if (!string.Equals(
                this.missionWikiDestructiveSnapshotFingerprint,
                destructiveSnapshotFingerprint,
                StringComparison.Ordinal))
        {
            this.missionWikiDestructiveSnapshotFingerprint =
                destructiveSnapshotFingerprint;
            this.missionWikiDestructiveSnapshotObservedAt = now;
            return false;
        }

        this.missionWikiDestructiveSnapshotObservedAt ??= now;

        if (now - this.missionWikiDestructiveSnapshotObservedAt.Value <
            MissionWikiDestructiveSnapshotConfirmationDuration)
        {
            return false;
        }

        this.missionWikiDestructiveSnapshotObservedAt = null;
        this.missionWikiDestructiveSnapshotFingerprint = null;
        return true;
    }

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
                this.missionWikiCompanionMissionRawId.HasValue)
            {
                selectedMission = usableMissions.FirstOrDefault(mission =>
                    mission.RawId == this.missionWikiCompanionMissionRawId);
            }

            if (selectedMission == null &&
                !string.IsNullOrWhiteSpace(
                    this.missionWikiCompanionMissionName))
            {
                selectedMission = usableMissions.FirstOrDefault(mission =>
                    string.Equals(
                        mission.Name,
                        this.missionWikiCompanionMissionName,
                        StringComparison.Ordinal) &&
                    (!this.missionWikiCompanionMissionSlot.HasValue ||
                     mission.Slot ==
                     this.missionWikiCompanionMissionSlot.Value));

                selectedMission ??= usableMissions.FirstOrDefault(mission =>
                    string.Equals(
                        mission.Name,
                        this.missionWikiCompanionMissionName,
                        StringComparison.Ordinal));
            }

            if (selectedMission == null &&
                this.missionWikiGameMissionAddress != 0)
            {
                selectedMission = usableMissions.FirstOrDefault(mission =>
                    mission.Address == this.missionWikiGameMissionAddress);
            }

            if (selectedMission == null &&
                !string.IsNullOrWhiteSpace(
                    this.missionWikiGameMissionName))
            {
                selectedMission = usableMissions.FirstOrDefault(mission =>
                    string.Equals(
                        mission.Name,
                        this.missionWikiGameMissionName,
                        StringComparison.Ordinal));
            }

            selectedMission ??= usableMissions.FirstOrDefault();
            this.RememberMissionWikiCompanionSelection(selectedMission);
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

    internal bool ShowMissionWikiCompanionFromControlPlane()
    {
        if (this.IsDisposed ||
            this.Disposing ||
            !this.CanPresentMissionWikiCompanion)
        {
            return false;
        }

        this.ShowMissionWikiCompanion();
        return this.missionWikiCompanionForm is
            { IsDisposed: false, Visible: true };
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

        var selectedMission = this.missionWikiMissions.IsAvailable
            ? this.missionWikiMissions.Missions.FirstOrDefault(mission =>
                mission.Address == address)
            : null;

        this.RememberMissionWikiCompanionSelection(selectedMission);

        if (selectedMission == null)
        {
            this.missionWikiCompanionMissionAddress = address;
        }

        this.RefreshMissionWikiActiveSelection();
        this.SyncMissionWikiCompanionPresentation();
    }

    private void RememberMissionWikiCompanionSelection(
        ClientMissionObservation? mission)
    {
        if (mission == null)
        {
            this.missionWikiCompanionMissionAddress = 0;
            this.missionWikiCompanionMissionRawId = null;
            this.missionWikiCompanionMissionName = null;
            this.missionWikiCompanionMissionSlot = null;
            return;
        }

        this.missionWikiCompanionMissionAddress = mission.Address;
        this.missionWikiCompanionMissionRawId = mission.RawId;
        this.missionWikiCompanionMissionName =
            string.IsNullOrWhiteSpace(mission.Name)
                ? null
                : mission.Name.Trim();
        this.missionWikiCompanionMissionSlot = mission.Slot;
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
        var observedPilotName =
            this.clientInstance.LiveCharacterIdentity.Name;

        if (!string.IsNullOrWhiteSpace(observedPilotName))
        {
            this.missionWikiCompanionPilotName =
                observedPilotName.Trim();
        }

        var pilotName =
            this.missionWikiCompanionPilotName ??
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

    private static string BuildMissionWikiMissionIdentity(
        ClientMissionObservation mission)
    {
        return mission.RawId.HasValue
            ? string.Concat("id:", mission.RawId.Value)
            : string.Concat(
                "slot:",
                mission.Slot,
                ":name:",
                mission.Name);
    }
}
