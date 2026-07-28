// ReSharper disable GrammarMistakeInComment
namespace Net7ClientManager.Observations.Observers;

using System.Globalization;
using System.Text;
using Net7ClientManager.Observations.Models;

[Flags]
internal enum ClientPanelPresentationChanges
{
    None = 0,
    Mission = 1,
    Faction = 2,
    Skill = 4,
    Inventory = 8,
}

internal sealed class ClientPanelPresentationObserver
{
    /*
     * Stable ownership path:
     *   SClient +0x1284 -> GameplayUtilityController
     *
     * Direct utility-view pointers:
     *   controller +0x98 -> EquipmentView
     *   controller +0x9C -> InventoryView
     *   controller +0xA0 -> VaultView
     *   controller +0xA4 -> CharacterInfoView
     *
     * All four views inherit their displayed/active byte at +0x7C.
     * Equipment and Vault are sibling subviews inside the Inventory shell:
     *   Inventory only             -> Cargo
     *   Inventory + Equipment      -> Equipment
     *   Inventory + Vault          -> Vault
     *
     * CharacterInfoView additionally exposes:
     *   +0xAC active tab (0 closed, 1 Skills, 2 Missions, 3 Factions)
     *   +0xB0 last selected tab, retained while the panel is closed
     *   +0xB8 -> CharacterMissionsPanel
     *   +0xBC -> CharacterFactionPanel
     *
     * CharacterMissionsPanel exposes:
     *   +0x7C displayed byte
     *   +0xAC -> details view
     *   +0xBC first visible mission offset
     *   +0xC4 -> owned UI_MISSN confirmation dialog
     *   +0xC8 selected visible row (-1 none, 0..2 selected)
     *   +0xCC/+0xD0/+0xD4 visible mission pointers
     *
     * CharacterFactionPanel exposes:
     *   +0x7C displayed byte
     *   +0xAC -> details view, whose displayed byte is also +0x7C
     *   +0xC0 first visible faction offset
     *   +0xC4 selected visible row (-1 none, 0..5 selected)
     *   +0xC8..+0xDC six stable row pointers
     *
     * Faction rows are reused while scrolling. Their SharedString faction key
     * begins at +0x64. The selected row number therefore cannot identify the
     * already-open faction after scrolling; this observer latches the key only
     * when details open or the selected row changes.
     */
    private const uint ImageBase = 0x00400000;

    private const uint EquipmentViewVTableStatic =
        0x00af4c74;

    private const uint EquipmentViewVTableRva =
        EquipmentViewVTableStatic - ImageBase;

    private const uint InventoryViewVTableStatic =
        0x00af4ad4;

    private const uint InventoryViewVTableRva =
        InventoryViewVTableStatic - ImageBase;

    private const uint VaultViewVTableStatic =
        0x00af92f0;

    private const uint VaultViewVTableRva =
        VaultViewVTableStatic - ImageBase;

    private const uint CharacterInfoViewVTableStatic =
        0x00af06e4;

    private const uint CharacterInfoViewVTableRva =
        CharacterInfoViewVTableStatic - ImageBase;

    private const uint CharacterMissionsPanelVTableStatic =
        0x00af3c9c;

    private const uint CharacterMissionsPanelVTableRva =
        CharacterMissionsPanelVTableStatic - ImageBase;

    private const uint CharacterFactionPanelVTableStatic =
        0x00af1774;

    private const uint CharacterFactionPanelVTableRva =
        CharacterFactionPanelVTableStatic - ImageBase;

    private const uint CharacterFactionDetailsViewVTableStatic =
        0x00af1988;

    private const uint CharacterFactionDetailsViewVTableRva =
        CharacterFactionDetailsViewVTableStatic - ImageBase;

    private const uint ClientContextGameplayUtilityControllerOffset =
        0x1284;

    private const uint GameplayUtilityControllerEquipmentViewOffset =
        0x98;

    private const uint GameplayUtilityControllerInventoryViewOffset =
        0x9c;

    private const uint GameplayUtilityControllerVaultViewOffset =
        0xa0;

    private const uint GameplayUtilityControllerCharacterInfoViewOffset =
        0xa4;

    private const uint ObjectVTableOffset =
        0x00;

    private const uint InterfaceViewDisplayedOffset =
        0x7c;

    private const uint CharacterInfoActiveTabOffset =
        0xac;

    private const uint CharacterInfoLastSelectedTabOffset =
        0xb0;

    private const uint CharacterInfoMissionsPanelOffset =
        0xb8;

    private const uint CharacterInfoFactionPanelOffset =
        0xbc;

    private const uint MissionPanelDetailsViewOffset =
        0xac;

    private const uint MissionPanelForfeitDialogOffset =
        0xc4;

    private const uint MissionPanelFirstVisibleMissionOffset =
        0xbc;

    private const uint MissionPanelSelectedVisibleRowOffset =
        0xc8;

    private const uint MissionPanelVisibleMissionAddressesOffset =
        0xcc;

    private const uint MissionNamePointerOffset =
        0x1a8;

    private const int MissionPanelVisibleMissionCount =
        3;

    private const int MaximumMissionNameLength =
        512;

    private const uint FactionPanelDetailsViewOffset =
        0xac;

    private const uint FactionPanelFirstVisibleFactionOffset =
        0xc0;

    private const uint FactionPanelSelectedVisibleRowOffset =
        0xc4;

    private const uint FactionPanelVisibleFactionRowsOffset =
        0xc8;

    private const uint FactionRowFactionKeyOffset =
        0x64;

    private const uint SharedStringBufferPointerOffset =
        0x04;

    private const uint SharedStringLengthOffset =
        0x08;

    private const int FactionPanelVisibleFactionCount =
        6;

    private const int MaximumFactionKeyLength =
        128;

    private readonly Lock factionSelectionLock = new();

    private readonly Dictionary<int, FactionSelectionState>
        factionSelections = [];

    public void Forget(int processId)
    {
        lock (this.factionSelectionLock)
        {
            this.factionSelections.Remove(processId);
        }
    }

    public void Refresh(
        ProcessMemoryReader memory,
        ObservedClientState state)
    {
        if (!state.HasDirectClientState ||
            state.ModuleBaseAddress == 0 ||
            state.ClientContextAddress == 0)
        {
            state.PanelPresentation =
                ClientPanelPresentationObservation.Unavailable(
                    "Direct SClient state is unavailable");

            return;
        }

        if (!TryReadPointer(
                memory,
                state.ClientContextAddress,
                ClientContextGameplayUtilityControllerOffset,
                "SClient.GameplayUtilityController",
                out var gameplayUtilityControllerAddress,
                out var error))
        {
            state.PanelPresentation =
                ClientPanelPresentationObservation.Unavailable(
                    error);

            return;
        }

        if (gameplayUtilityControllerAddress == 0)
        {
            state.PanelPresentation =
                ClientPanelPresentationObservation.Unavailable(
                    "SClient gameplay utility controller is null");

            return;
        }

        uint inventoryViewAddress = 0;
        uint inventoryViewVTableAddress = 0;
        uint vaultViewAddress = 0;
        uint vaultViewVTableAddress = 0;
        uint characterInfoViewAddress = 0;
        uint characterInfoViewVTableAddress = 0;

        if (!TryReadAndValidateView(
                memory,
                state.ModuleBaseAddress,
                gameplayUtilityControllerAddress,
                GameplayUtilityControllerEquipmentViewOffset,
                EquipmentViewVTableRva,
                "GameplayUtilityController",
                "EquipmentView",
                out var equipmentViewAddress,
                out var equipmentViewVTableAddress,
                out error) ||
            !TryReadAndValidateView(
                memory,
                state.ModuleBaseAddress,
                gameplayUtilityControllerAddress,
                GameplayUtilityControllerInventoryViewOffset,
                InventoryViewVTableRva,
                "GameplayUtilityController",
                "InventoryView",
                out inventoryViewAddress,
                out inventoryViewVTableAddress,
                out error) ||
            !TryReadAndValidateView(
                memory,
                state.ModuleBaseAddress,
                gameplayUtilityControllerAddress,
                GameplayUtilityControllerVaultViewOffset,
                VaultViewVTableRva,
                "GameplayUtilityController",
                "VaultView",
                out vaultViewAddress,
                out vaultViewVTableAddress,
                out error) ||
            !TryReadAndValidateView(
                memory,
                state.ModuleBaseAddress,
                gameplayUtilityControllerAddress,
                GameplayUtilityControllerCharacterInfoViewOffset,
                CharacterInfoViewVTableRva,
                "GameplayUtilityController",
                "CharacterInfoView",
                out characterInfoViewAddress,
                out characterInfoViewVTableAddress,
                out error))
        {
            state.PanelPresentation =
                ClientPanelPresentationObservation.Unavailable(
                    error,
                    gameplayUtilityControllerAddress,
                    equipmentViewAddress,
                    equipmentViewVTableAddress,
                    inventoryViewAddress,
                    inventoryViewVTableAddress,
                    vaultViewAddress,
                    vaultViewVTableAddress,
                    characterInfoViewAddress,
                    characterInfoViewVTableAddress);

            return;
        }

        if (!TryReadBooleanByte(
                memory,
                equipmentViewAddress,
                InterfaceViewDisplayedOffset,
                "EquipmentView.IsDisplayed",
                out var isEquipmentDisplayed,
                out error) ||
            !TryReadBooleanByte(
                memory,
                inventoryViewAddress,
                InterfaceViewDisplayedOffset,
                "InventoryView.IsDisplayed",
                out var isInventoryDisplayed,
                out error) ||
            !TryReadBooleanByte(
                memory,
                vaultViewAddress,
                InterfaceViewDisplayedOffset,
                "VaultView.IsDisplayed",
                out var isVaultDisplayed,
                out error) ||
            !TryReadBooleanByte(
                memory,
                characterInfoViewAddress,
                InterfaceViewDisplayedOffset,
                "CharacterInfoView.IsDisplayed",
                out var isCharacterInfoDisplayed,
                out error) ||
            !TryReadInt32(
                memory,
                characterInfoViewAddress,
                CharacterInfoActiveTabOffset,
                "CharacterInfoView.ActiveTab",
                out var activeCharacterInfoTabRaw,
                out error) ||
            !TryReadInt32(
                memory,
                characterInfoViewAddress,
                CharacterInfoLastSelectedTabOffset,
                "CharacterInfoView.LastSelectedTab",
                out var lastSelectedCharacterInfoTabRaw,
                out error))
        {
            state.PanelPresentation =
                ClientPanelPresentationObservation.Unavailable(
                    error,
                    gameplayUtilityControllerAddress,
                    equipmentViewAddress,
                    equipmentViewVTableAddress,
                    inventoryViewAddress,
                    inventoryViewVTableAddress,
                    vaultViewAddress,
                    vaultViewVTableAddress,
                    characterInfoViewAddress,
                    characterInfoViewVTableAddress);

            return;
        }

        var inventoryMode = DetermineInventoryMode(
            isInventoryDisplayed,
            isEquipmentDisplayed,
            isVaultDisplayed);

        var activeCharacterInfoTab =
            ParseCharacterInfoTab(
                activeCharacterInfoTabRaw);

        var lastSelectedCharacterInfoTab =
            ParseCharacterInfoTab(
                lastSelectedCharacterInfoTabRaw);

        var missionDetails = ObserveMissionDetails(
            memory,
            state.ModuleBaseAddress,
            characterInfoViewAddress,
            isCharacterInfoDisplayed,
            activeCharacterInfoTab);

        var factionDetails = this.ObserveFactionDetails(
            memory,
            state.ProcessId,
            state.ModuleBaseAddress,
            characterInfoViewAddress,
            isCharacterInfoDisplayed,
            activeCharacterInfoTab);

        var status =
            inventoryMode == ClientInventoryPanelMode.Unknown
                ? "Available; inventory child-view state is inconsistent"
                : "Available; client-owned inventory and character-panel presentation state";

        state.PanelPresentation =
            new ClientPanelPresentationObservation
            {
                IsAvailable = true,
                Status = status,
                GameplayUtilityControllerAddress =
                    gameplayUtilityControllerAddress,
                EquipmentViewAddress =
                    equipmentViewAddress,
                EquipmentViewVTableAddress =
                    equipmentViewVTableAddress,
                InventoryViewAddress =
                    inventoryViewAddress,
                InventoryViewVTableAddress =
                    inventoryViewVTableAddress,
                VaultViewAddress =
                    vaultViewAddress,
                VaultViewVTableAddress =
                    vaultViewVTableAddress,
                CharacterInfoViewAddress =
                    characterInfoViewAddress,
                CharacterInfoViewVTableAddress =
                    characterInfoViewVTableAddress,
                IsInventoryDisplayed =
                    isInventoryDisplayed,
                IsEquipmentDisplayed =
                    isEquipmentDisplayed,
                IsVaultDisplayed =
                    isVaultDisplayed,
                InventoryMode =
                    inventoryMode,
                IsCharacterInfoDisplayed =
                    isCharacterInfoDisplayed,
                ActiveCharacterInfoTabRaw =
                    activeCharacterInfoTabRaw,
                ActiveCharacterInfoTab =
                    activeCharacterInfoTab,
                LastSelectedCharacterInfoTabRaw =
                    lastSelectedCharacterInfoTabRaw,
                LastSelectedCharacterInfoTab =
                    lastSelectedCharacterInfoTab,
                MissionDetails = missionDetails,
                FactionDetails = factionDetails,
            };
    }

    public ClientPanelPresentationChanges RefreshPresentationFast(
        ProcessMemoryReader memory,
        ObservedClientState state)
    {
        var previous = state.PanelPresentation;

        if (!state.HasDirectClientState ||
            state.ModuleBaseAddress == 0 ||
            state.ClientContextAddress == 0 ||
            !previous.IsAvailable ||
            previous.EquipmentViewAddress == 0 ||
            previous.InventoryViewAddress == 0 ||
            previous.VaultViewAddress == 0 ||
            previous.CharacterInfoViewAddress == 0)
        {
            this.Refresh(memory, state);
            return GetPresentationChanges(
                previous,
                state.PanelPresentation);
        }

        if (!TryReadBooleanByte(
                memory,
                previous.EquipmentViewAddress,
                InterfaceViewDisplayedOffset,
                "EquipmentView.IsDisplayed",
                out var isEquipmentDisplayed,
                out _) ||
            !TryReadBooleanByte(
                memory,
                previous.InventoryViewAddress,
                InterfaceViewDisplayedOffset,
                "InventoryView.IsDisplayed",
                out var isInventoryDisplayed,
                out _) ||
            !TryReadBooleanByte(
                memory,
                previous.VaultViewAddress,
                InterfaceViewDisplayedOffset,
                "VaultView.IsDisplayed",
                out var isVaultDisplayed,
                out _) ||
            !TryReadBooleanByte(
                memory,
                previous.CharacterInfoViewAddress,
                InterfaceViewDisplayedOffset,
                "CharacterInfoView.IsDisplayed",
                out var isCharacterInfoDisplayed,
                out _) ||
            !TryReadInt32(
                memory,
                previous.CharacterInfoViewAddress,
                CharacterInfoActiveTabOffset,
                "CharacterInfoView.ActiveTab",
                out var activeCharacterInfoTabRaw,
                out _))
        {
            this.Refresh(memory, state);
            return GetPresentationChanges(
                previous,
                state.PanelPresentation);
        }

        var activeCharacterInfoTab =
            ParseCharacterInfoTab(activeCharacterInfoTabRaw);
        var inventoryMode = DetermineInventoryMode(
            isInventoryDisplayed,
            isEquipmentDisplayed,
            isVaultDisplayed);

        var missionDetails = ObserveMissionDetails(
            memory,
            state.ModuleBaseAddress,
            previous.CharacterInfoViewAddress,
            isCharacterInfoDisplayed,
            activeCharacterInfoTab);

        var factionDetails = this.ObserveFactionDetails(
            memory,
            state.ProcessId,
            state.ModuleBaseAddress,
            previous.CharacterInfoViewAddress,
            isCharacterInfoDisplayed,
            activeCharacterInfoTab);

        state.PanelPresentation =
            previous with
            {
                Status =
                    inventoryMode == ClientInventoryPanelMode.Unknown
                        ? "Available; inventory child-view state is inconsistent"
                        : "Available; client-owned inventory and character-panel presentation state",
                IsInventoryDisplayed =
                    isInventoryDisplayed,
                IsEquipmentDisplayed =
                    isEquipmentDisplayed,
                IsVaultDisplayed =
                    isVaultDisplayed,
                InventoryMode =
                    inventoryMode,
                IsCharacterInfoDisplayed =
                    isCharacterInfoDisplayed,
                ActiveCharacterInfoTabRaw =
                    activeCharacterInfoTabRaw,
                ActiveCharacterInfoTab =
                    activeCharacterInfoTab,
                MissionDetails = missionDetails,
                FactionDetails = factionDetails,
            };

        return GetPresentationChanges(
            previous,
            state.PanelPresentation);
    }

    private static ClientPanelPresentationChanges GetPresentationChanges(
        ClientPanelPresentationObservation previous,
        ClientPanelPresentationObservation current)
    {
        var changes = ClientPanelPresentationChanges.None;

        if (HasMissionPresentationChanged(previous, current))
        {
            changes |= ClientPanelPresentationChanges.Mission;
        }

        if (HasFactionPresentationChanged(previous, current))
        {
            changes |= ClientPanelPresentationChanges.Faction;
        }

        if (HasSkillPresentationChanged(previous, current))
        {
            changes |= ClientPanelPresentationChanges.Skill;
        }

        if (HasInventoryPresentationChanged(previous, current))
        {
            changes |= ClientPanelPresentationChanges.Inventory;
        }

        return changes;
    }

    public static bool HasInventoryPresentationChanged(
        ClientPanelPresentationObservation previous,
        ClientPanelPresentationObservation current)
    {
        return previous.IsAvailable != current.IsAvailable ||
               previous.IsInventoryDisplayed !=
               current.IsInventoryDisplayed ||
               previous.IsEquipmentDisplayed !=
               current.IsEquipmentDisplayed ||
               previous.IsVaultDisplayed !=
               current.IsVaultDisplayed ||
               previous.InventoryMode != current.InventoryMode;
    }


    public static bool HasSkillPresentationChanged(
        ClientPanelPresentationObservation previous,
        ClientPanelPresentationObservation current)
    {
        return previous.IsAvailable != current.IsAvailable ||
               previous.IsEquipmentDisplayed !=
               current.IsEquipmentDisplayed ||
               previous.IsCharacterInfoDisplayed !=
               current.IsCharacterInfoDisplayed ||
               previous.ActiveCharacterInfoTab !=
               current.ActiveCharacterInfoTab;
    }

    public static bool HasMissionPresentationChanged(
        ClientPanelPresentationObservation previous,
        ClientPanelPresentationObservation current)
    {
        var previousDetails = previous.MissionDetails;
        var currentDetails = current.MissionDetails;

        return previous.IsAvailable != current.IsAvailable ||
               previous.IsCharacterInfoDisplayed !=
               current.IsCharacterInfoDisplayed ||
               previous.ActiveCharacterInfoTab !=
               current.ActiveCharacterInfoTab ||
               previousDetails.IsAvailable !=
               currentDetails.IsAvailable ||
               previousDetails.IsMissionPanelDisplayed !=
               currentDetails.IsMissionPanelDisplayed ||
               previousDetails.ForfeitDialogAddress !=
               currentDetails.ForfeitDialogAddress ||
               previousDetails.IsForfeitConfirmationDisplayed !=
               currentDetails.IsForfeitConfirmationDisplayed ||
               previousDetails.IsDisplayed !=
               currentDetails.IsDisplayed ||
               previousDetails.FirstVisibleMissionOffset !=
               currentDetails.FirstVisibleMissionOffset ||
               previousDetails.SelectedVisibleRow !=
               currentDetails.SelectedVisibleRow ||
               previousDetails.SelectedMissionAddress !=
               currentDetails.SelectedMissionAddress ||
               !string.Equals(
                   previousDetails.SelectedMissionName,
                   currentDetails.SelectedMissionName,
                   StringComparison.Ordinal);
    }

    public static bool HasFactionPresentationChanged(
        ClientPanelPresentationObservation previous,
        ClientPanelPresentationObservation current)
    {
        var previousDetails = previous.FactionDetails;
        var currentDetails = current.FactionDetails;

        return previous.IsAvailable != current.IsAvailable ||
               previous.IsCharacterInfoDisplayed !=
               current.IsCharacterInfoDisplayed ||
               previous.ActiveCharacterInfoTab !=
               current.ActiveCharacterInfoTab ||
               previousDetails.IsAvailable !=
               currentDetails.IsAvailable ||
               previousDetails.IsFactionPanelDisplayed !=
               currentDetails.IsFactionPanelDisplayed ||
               previousDetails.IsDetailsViewDisplayed !=
               currentDetails.IsDetailsViewDisplayed ||
               previousDetails.IsDisplayed !=
               currentDetails.IsDisplayed ||
               previousDetails.SelectedVisibleRow !=
               currentDetails.SelectedVisibleRow ||
               !string.Equals(
                   previousDetails.SelectedFactionKey,
                   currentDetails.SelectedFactionKey,
                   StringComparison.Ordinal);
    }

    private static ClientMissionDetailsPresentationObservation ObserveMissionDetails(
        ProcessMemoryReader memory,
        uint moduleBaseAddress,
        uint characterInfoViewAddress,
        bool isCharacterInfoDisplayed,
        ClientCharacterInfoTab activeCharacterInfoTab)
    {
        if (!TryReadAndValidateView(
                memory,
                moduleBaseAddress,
                characterInfoViewAddress,
                CharacterInfoMissionsPanelOffset,
                CharacterMissionsPanelVTableRva,
                "CharacterInfoView",
                "CharacterMissionsPanel",
                out var missionPanelAddress,
                out var missionPanelVTableAddress,
                out var error))
        {
            return ClientMissionDetailsPresentationObservation.Unavailable(
                error,
                missionPanelAddress,
                missionPanelVTableAddress);
        }

        uint detailsViewAddress = 0;
        uint forfeitDialogAddress = 0;

        if (!TryReadBooleanByte(
                memory,
                missionPanelAddress,
                InterfaceViewDisplayedOffset,
                "CharacterMissionsPanel.IsDisplayed",
                out var isMissionPanelDisplayed,
                out error) ||
            !TryReadPointer(
                memory,
                missionPanelAddress,
                MissionPanelDetailsViewOffset,
                "CharacterMissionsPanel.DetailsView",
                out detailsViewAddress,
                out error) ||
            !TryReadPointer(
                memory,
                missionPanelAddress,
                MissionPanelForfeitDialogOffset,
                "CharacterMissionsPanel.ForfeitDialog",
                out forfeitDialogAddress,
                out error) ||
            !TryReadInt32(
                memory,
                missionPanelAddress,
                MissionPanelFirstVisibleMissionOffset,
                "CharacterMissionsPanel.FirstVisibleMissionOffset",
                out var firstVisibleMissionOffset,
                out error) ||
            !TryReadInt32(
                memory,
                missionPanelAddress,
                MissionPanelSelectedVisibleRowOffset,
                "CharacterMissionsPanel.SelectedVisibleRow",
                out var selectedVisibleRow,
                out error))
        {
            return ClientMissionDetailsPresentationObservation.Unavailable(
                error,
                missionPanelAddress,
                missionPanelVTableAddress,
                detailsViewAddress,
                forfeitDialogAddress);
        }

        var isForfeitConfirmationDisplayed = false;

        if (forfeitDialogAddress != 0 &&
            !TryReadBooleanByte(
                memory,
                forfeitDialogAddress,
                InterfaceViewDisplayedOffset,
                "MissionForfeitDialog.IsDisplayed",
                out isForfeitConfirmationDisplayed,
                out error))
        {
            return ClientMissionDetailsPresentationObservation.Unavailable(
                error,
                missionPanelAddress,
                missionPanelVTableAddress,
                detailsViewAddress,
                forfeitDialogAddress);
        }

        var visibleMissionAddresses =
            new uint[MissionPanelVisibleMissionCount];

        for (var index = 0;
             index < visibleMissionAddresses.Length;
             index++)
        {
            var offset = checked(
                MissionPanelVisibleMissionAddressesOffset +
                (uint)(index * sizeof(uint)));

            if (!TryReadPointer(
                    memory,
                    missionPanelAddress,
                    offset,
                    string.Create(CultureInfo.InvariantCulture, $"CharacterMissionsPanel.VisibleMission[{index}]"),
                    out visibleMissionAddresses[index],
                    out error))
            {
                return ClientMissionDetailsPresentationObservation.Unavailable(
                    error,
                    missionPanelAddress,
                    missionPanelVTableAddress,
                    detailsViewAddress,
                    forfeitDialogAddress);
            }
        }

        if (selectedVisibleRow is < -1 or >= MissionPanelVisibleMissionCount)
        {
            return ClientMissionDetailsPresentationObservation.Unavailable(
                string.Create(CultureInfo.InvariantCulture, $"Unexpected selected mission row {selectedVisibleRow}"),
                missionPanelAddress,
                missionPanelVTableAddress,
                detailsViewAddress,
                forfeitDialogAddress);
        }

        var selectedMissionAddress =
            selectedVisibleRow >= 0
                ? visibleMissionAddresses[selectedVisibleRow]
                : 0;

        var selectedMissionName = "";

        if (selectedMissionAddress != 0 &&
            TryReadPointer(
                memory,
                selectedMissionAddress,
                MissionNamePointerOffset,
                "SelectedMission.Name",
                out var selectedMissionNameAddress,
                out _) &&
            selectedMissionNameAddress != 0 &&
            memory.TryReadNullTerminatedLatin1String(
                selectedMissionNameAddress,
                MaximumMissionNameLength,
                out var observedMissionName))
        {
            selectedMissionName =
                observedMissionName.Trim();
        }

        var hasConsistentSelection =
            selectedVisibleRow == -1 ||
            selectedMissionAddress != 0;

        var isDisplayed =
            isCharacterInfoDisplayed &&
            activeCharacterInfoTab ==
                ClientCharacterInfoTab.Missions &&
            isMissionPanelDisplayed &&
            selectedVisibleRow >= 0 &&
            selectedMissionAddress != 0;

        var status = hasConsistentSelection
            ? "Available; client-owned mission-list selection and details presentation state"
            : "Available; selected mission row currently has no mission pointer";

        return new ClientMissionDetailsPresentationObservation
        {
            IsAvailable = true,
            Status = status,
            MissionPanelAddress = missionPanelAddress,
            MissionPanelVTableAddress =
                missionPanelVTableAddress,
            DetailsViewAddress = detailsViewAddress,
            ForfeitDialogAddress =
                forfeitDialogAddress,
            IsForfeitConfirmationDisplayed =
                isForfeitConfirmationDisplayed,
            IsMissionPanelDisplayed =
                isMissionPanelDisplayed,
            FirstVisibleMissionOffset =
                firstVisibleMissionOffset,
            SelectedVisibleRow = selectedVisibleRow,
            VisibleMissionAddresses =
                visibleMissionAddresses,
            SelectedMissionAddress =
                selectedMissionAddress,
            SelectedMissionName =
                selectedMissionName,
            IsDisplayed = isDisplayed,
        };
    }

    private ClientFactionDetailsPresentationObservation ObserveFactionDetails(
        ProcessMemoryReader memory,
        int processId,
        uint moduleBaseAddress,
        uint characterInfoViewAddress,
        bool isCharacterInfoDisplayed,
        ClientCharacterInfoTab activeCharacterInfoTab)
    {
        if (!TryReadAndValidateView(
                memory,
                moduleBaseAddress,
                characterInfoViewAddress,
                CharacterInfoFactionPanelOffset,
                CharacterFactionPanelVTableRva,
                "CharacterInfoView",
                "CharacterFactionPanel",
                out var factionPanelAddress,
                out var factionPanelVTableAddress,
                out var error))
        {
            return ClientFactionDetailsPresentationObservation.Unavailable(
                error,
                factionPanelAddress,
                factionPanelVTableAddress);
        }

        if (!TryReadAndValidateView(
                memory,
                moduleBaseAddress,
                factionPanelAddress,
                FactionPanelDetailsViewOffset,
                CharacterFactionDetailsViewVTableRva,
                "CharacterFactionPanel",
                "FactionDetailsView",
                out var detailsViewAddress,
                out var detailsViewVTableAddress,
                out error))
        {
            return ClientFactionDetailsPresentationObservation.Unavailable(
                error,
                factionPanelAddress,
                factionPanelVTableAddress,
                detailsViewAddress,
                detailsViewVTableAddress);
        }

        if (!TryReadBooleanByte(
                memory,
                factionPanelAddress,
                InterfaceViewDisplayedOffset,
                "CharacterFactionPanel.IsDisplayed",
                out var isFactionPanelDisplayed,
                out error) ||
            !TryReadBooleanByte(
                memory,
                detailsViewAddress,
                InterfaceViewDisplayedOffset,
                "FactionDetailsView.IsDisplayed",
                out var isDetailsViewDisplayed,
                out error) ||
            !TryReadInt32(
                memory,
                factionPanelAddress,
                FactionPanelFirstVisibleFactionOffset,
                "CharacterFactionPanel.FirstVisibleFactionOffset",
                out var firstVisibleFactionOffset,
                out error) ||
            !TryReadInt32(
                memory,
                factionPanelAddress,
                FactionPanelSelectedVisibleRowOffset,
                "CharacterFactionPanel.SelectedVisibleRow",
                out var selectedVisibleRow,
                out error))
        {
            return ClientFactionDetailsPresentationObservation.Unavailable(
                error,
                factionPanelAddress,
                factionPanelVTableAddress,
                detailsViewAddress,
                detailsViewVTableAddress);
        }

        if (selectedVisibleRow is < -1 or >= FactionPanelVisibleFactionCount)
        {
            return ClientFactionDetailsPresentationObservation.Unavailable(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Unexpected selected faction row {selectedVisibleRow}"),
                factionPanelAddress,
                factionPanelVTableAddress,
                detailsViewAddress,
                detailsViewVTableAddress);
        }

        var rowAddresses = new uint[FactionPanelVisibleFactionCount];
        var factionKeys = new string[FactionPanelVisibleFactionCount];
        var hasIncompleteRowKeys = false;

        for (var index = 0; index < rowAddresses.Length; index++)
        {
            var rowOffset = checked(
                FactionPanelVisibleFactionRowsOffset +
                (uint)(index * sizeof(uint)));

            if (!TryReadPointer(
                    memory,
                    factionPanelAddress,
                    rowOffset,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"CharacterFactionPanel.VisibleRow[{index}]"),
                    out rowAddresses[index],
                    out error))
            {
                return ClientFactionDetailsPresentationObservation.Unavailable(
                    error,
                    factionPanelAddress,
                    factionPanelVTableAddress,
                    detailsViewAddress,
                    detailsViewVTableAddress);
            }

            if (rowAddresses[index] == 0)
            {
                factionKeys[index] = "";
                continue;
            }

            if (!TryReadSharedString(
                    memory,
                    rowAddresses[index],
                    FactionRowFactionKeyOffset,
                    MaximumFactionKeyLength,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"CharacterFactionPanel.VisibleRow[{index}].FactionKey"),
                    out factionKeys[index],
                    out _))
            {
                // Row SharedStrings are rewritten while the native list scrolls.
                // Treat an individual key as temporarily unsettled rather than
                // withdrawing the entire details presentation and flashing the
                // overlay. A newly clicked row is committed once its key becomes
                // readable on a subsequent ordinary presentation sample.
                factionKeys[index] = "";
                hasIncompleteRowKeys = true;
            }
        }

        var isFactionContext =
            isCharacterInfoDisplayed &&
            activeCharacterInfoTab == ClientCharacterInfoTab.Factions &&
            isFactionPanelDisplayed;
        var currentRowKey = selectedVisibleRow >= 0
            ? factionKeys[selectedVisibleRow]
            : "";
        string selectedFactionKey;

        lock (this.factionSelectionLock)
        {
            var selection = this.GetFactionSelectionState(processId);

            if (!isFactionContext)
            {
                selection.Reset();
            }
            else
            {
                selection.Observe(
                    isDetailsViewDisplayed,
                    selectedVisibleRow,
                    currentRowKey);
            }

            selectedFactionKey =
                isFactionContext && isDetailsViewDisplayed
                    ? selection.SelectedFactionKey
                    : "";
        }
        var isDisplayed =
            isFactionContext &&
            isDetailsViewDisplayed &&
            !string.IsNullOrWhiteSpace(selectedFactionKey);

        return new ClientFactionDetailsPresentationObservation
        {
            IsAvailable = true,
            Status = hasIncompleteRowKeys
                ? "Available; one or more native faction rows are settling"
                : isDisplayed
                    ? "Available; faction details are open with a latched faction identity"
                    : "Available; client-owned faction-list and details presentation state",
            FactionPanelAddress = factionPanelAddress,
            FactionPanelVTableAddress = factionPanelVTableAddress,
            DetailsViewAddress = detailsViewAddress,
            DetailsViewVTableAddress = detailsViewVTableAddress,
            IsFactionPanelDisplayed = isFactionPanelDisplayed,
            IsDetailsViewDisplayed = isDetailsViewDisplayed,
            FirstVisibleFactionOffset = firstVisibleFactionOffset,
            SelectedVisibleRow = selectedVisibleRow,
            VisibleFactionRowAddresses = rowAddresses,
            VisibleFactionKeys = factionKeys,
            SelectedFactionKey = selectedFactionKey,
            IsDisplayed = isDisplayed,
        };
    }

    private FactionSelectionState GetFactionSelectionState(int processId)
    {
        if (!this.factionSelections.TryGetValue(processId, out var state))
        {
            state = new FactionSelectionState();
            this.factionSelections[processId] = state;
        }

        return state;
    }

    private static bool TryReadSharedString(
        ProcessMemoryReader memory,
        uint ownerAddress,
        uint sharedStringOffset,
        int maximumLength,
        string fieldName,
        out string value,
        out string error)
    {
        value = "";

        if (!TryReadPointer(
                memory,
                ownerAddress,
                checked(sharedStringOffset + SharedStringBufferPointerOffset),
                $"{fieldName}.Buffer",
                out var bufferAddress,
                out error) ||
            !TryReadInt32(
                memory,
                ownerAddress,
                checked(sharedStringOffset + SharedStringLengthOffset),
                $"{fieldName}.Length",
                out var length,
                out error))
        {
            return false;
        }

        if (length < 0 || length > maximumLength)
        {
            error = string.Create(
                CultureInfo.InvariantCulture,
                $"Unexpected {fieldName} length {length}");
            return false;
        }

        if (length == 0)
        {
            error = "";
            return true;
        }

        if (bufferAddress == 0)
        {
            error = $"{fieldName} has length {length} but a null buffer";
            return false;
        }

        if (!memory.TryReadBytes(bufferAddress, length, out var bytes))
        {
            error = $"Could not read {fieldName} at {FormatPointer(bufferAddress)}";
            return false;
        }

        value = Encoding.Latin1.GetString(bytes).Trim();
        error = "";
        return true;
    }

    private sealed class FactionSelectionState
    {
        public bool DetailsDisplayed { get; private set; }

        public int SelectedVisibleRow { get; private set; } = -1;

        public string PendingFactionKey { get; private set; } = "";

        public string SelectedFactionKey { get; private set; } = "";

        public bool IsAwaitingSelectedRowKey { get; private set; }

        public void Observe(
            bool detailsDisplayed,
            int selectedVisibleRow,
            string currentRowKey)
        {
            var rowChanged = selectedVisibleRow != this.SelectedVisibleRow;

            if (!detailsDisplayed)
            {
                this.SelectedFactionKey = "";
                this.IsAwaitingSelectedRowKey = false;

                if (selectedVisibleRow >= 0 &&
                    rowChanged &&
                    !string.IsNullOrWhiteSpace(currentRowKey))
                {
                    this.PendingFactionKey = currentRowKey;
                }
                else if (selectedVisibleRow < 0)
                {
                    this.PendingFactionKey = "";
                }
            }
            else if (!this.DetailsDisplayed)
            {
                var openingFactionKey =
                    !string.IsNullOrWhiteSpace(this.PendingFactionKey)
                        ? this.PendingFactionKey
                        : currentRowKey;

                if (!string.IsNullOrWhiteSpace(openingFactionKey))
                {
                    this.SelectedFactionKey = openingFactionKey;
                    this.IsAwaitingSelectedRowKey = false;
                }
                else
                {
                    this.IsAwaitingSelectedRowKey = true;
                }

                this.PendingFactionKey = "";
            }
            else if (rowChanged)
            {
                if (!string.IsNullOrWhiteSpace(currentRowKey))
                {
                    this.SelectedFactionKey = currentRowKey;
                    this.IsAwaitingSelectedRowKey = false;
                }
                else
                {
                    this.IsAwaitingSelectedRowKey = true;
                }

                this.PendingFactionKey = "";
            }
            else if ((this.IsAwaitingSelectedRowKey ||
                      string.IsNullOrWhiteSpace(this.SelectedFactionKey)) &&
                     !string.IsNullOrWhiteSpace(currentRowKey))
            {
                this.SelectedFactionKey = currentRowKey;
                this.IsAwaitingSelectedRowKey = false;
            }

            this.DetailsDisplayed = detailsDisplayed;
            this.SelectedVisibleRow = selectedVisibleRow;
        }

        public void Reset()
        {
            this.DetailsDisplayed = false;
            this.SelectedVisibleRow = -1;
            this.PendingFactionKey = "";
            this.SelectedFactionKey = "";
            this.IsAwaitingSelectedRowKey = false;
        }
    }

    private static ClientInventoryPanelMode DetermineInventoryMode(
        bool isInventoryDisplayed,
        bool isEquipmentDisplayed,
        bool isVaultDisplayed)
    {
        if (!isInventoryDisplayed)
        {
            return !isEquipmentDisplayed && !isVaultDisplayed
                ? ClientInventoryPanelMode.Closed
                : ClientInventoryPanelMode.Unknown;
        }

        if (isEquipmentDisplayed == isVaultDisplayed)
        {
            return !isEquipmentDisplayed
                ? ClientInventoryPanelMode.Cargo
                : ClientInventoryPanelMode.Unknown;
        }

        return isEquipmentDisplayed
            ? ClientInventoryPanelMode.Equipment
            : ClientInventoryPanelMode.Vault;
    }

    private static ClientCharacterInfoTab ParseCharacterInfoTab(
        int rawValue)
    {
        return rawValue switch
        {
            0 => ClientCharacterInfoTab.None,
            1 => ClientCharacterInfoTab.Skills,
            2 => ClientCharacterInfoTab.Missions,
            3 => ClientCharacterInfoTab.Factions,
            _ => ClientCharacterInfoTab.Unknown,
        };
    }

    private static bool TryReadAndValidateView(
        ProcessMemoryReader memory,
        uint moduleBaseAddress,
        uint ownerAddress,
        uint viewOffset,
        uint expectedVTableRva,
        string ownerName,
        string viewName,
        out uint viewAddress,
        out uint viewVTableAddress,
        out string error)
    {
        viewAddress = 0;
        viewVTableAddress = 0;

        if (!TryReadPointer(
                memory,
                ownerAddress,
                viewOffset,
                $"{ownerName}.{viewName}",
                out viewAddress,
                out error))
        {
            return false;
        }

        if (viewAddress == 0)
        {
            error =
                $"{ownerName} {viewName} is null";

            return false;
        }

        if (!TryReadPointer(
                memory,
                viewAddress,
                ObjectVTableOffset,
                $"{viewName}.VTable",
                out viewVTableAddress,
                out error))
        {
            return false;
        }

        if (!TryResolveRelocatedAddress(
                moduleBaseAddress,
                expectedVTableRva,
                $"{viewName} vtable",
                out var expectedVTableAddress,
                out error))
        {
            return false;
        }

        if (viewVTableAddress != expectedVTableAddress)
        {
            error = $"Unexpected {viewName} vtable {FormatPointer(viewVTableAddress)}; expected {FormatPointer(expectedVTableAddress)}";

            return false;
        }

        error = "";
        return true;
    }

    private static bool TryResolveRelocatedAddress(
        uint moduleBaseAddress,
        uint relativeVirtualAddress,
        string fieldName,
        out uint address,
        out string error)
    {
        try
        {
            address = checked(
                moduleBaseAddress +
                relativeVirtualAddress);

            error = "";
            return true;
        }
        catch (OverflowException)
        {
            address = 0;
            error =
                $"Address overflow while resolving {fieldName}";

            return false;
        }
    }

    private static bool TryReadBooleanByte(
        ProcessMemoryReader memory,
        uint baseAddress,
        uint offset,
        string fieldName,
        out bool value,
        out string error)
    {
        if (!TryReadByte(
                memory,
                baseAddress,
                offset,
                fieldName,
                out var rawValue,
                out error))
        {
            value = false;
            return false;
        }

        if (rawValue > 1)
        {
            value = false;
            error = $"Unexpected boolean value {rawValue} in {fieldName}";

            return false;
        }

        value = rawValue != 0;
        error = "";
        return true;
    }

    private static bool TryReadByte(
        ProcessMemoryReader memory,
        uint baseAddress,
        uint offset,
        string fieldName,
        out byte value,
        out string error)
    {
        uint address;

        try
        {
            address = checked(
                baseAddress + offset);
        }
        catch (OverflowException)
        {
            value = 0;
            error =
                $"Address overflow while reading {fieldName}";

            return false;
        }

        if (!memory.TryReadBytes(
                address,
                sizeof(byte),
                out var bytes))
        {
            value = 0;
            error = $"Could not read {fieldName} at {FormatPointer(address)}";

            return false;
        }

        value = bytes[0];
        error = "";
        return true;
    }

    private static bool TryReadInt32(
        ProcessMemoryReader memory,
        uint baseAddress,
        uint offset,
        string fieldName,
        out int value,
        out string error)
    {
        if (!TryReadPointer(
                memory,
                baseAddress,
                offset,
                fieldName,
                out var rawValue,
                out error))
        {
            value = 0;
            return false;
        }

        value = unchecked((int)rawValue);
        error = "";
        return true;
    }

    private static bool TryReadPointer(
        ProcessMemoryReader memory,
        uint baseAddress,
        uint offset,
        string fieldName,
        out uint value,
        out string error)
    {
        uint address;

        try
        {
            address = checked(
                baseAddress + offset);
        }
        catch (OverflowException)
        {
            value = 0;
            error =
                $"Address overflow while reading {fieldName}";

            return false;
        }

        if (!memory.TryReadUInt32(
                address,
                out value))
        {
            error = $"Could not read {fieldName} at {FormatPointer(address)}";

            return false;
        }

        error = "";
        return true;
    }

    private static string FormatPointer(
        uint address)
    {
        return address == 0
            ? "None"
            : $"0x{address:X8}";
    }
}
