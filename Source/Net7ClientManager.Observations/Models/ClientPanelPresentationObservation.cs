namespace Net7ClientManager.Observations.Models;

public sealed record ClientPanelPresentationObservation
{
    public bool IsAvailable { get; init; }

    public string Status { get; init; } = "";

    public uint GameplayUtilityControllerAddress { get; init; }

    public uint EquipmentViewAddress { get; init; }

    public uint EquipmentViewVTableAddress { get; init; }

    public uint InventoryViewAddress { get; init; }

    public uint InventoryViewVTableAddress { get; init; }

    public uint VaultViewAddress { get; init; }

    public uint VaultViewVTableAddress { get; init; }

    public uint CharacterInfoViewAddress { get; init; }

    public uint CharacterInfoViewVTableAddress { get; init; }

    public bool IsInventoryDisplayed { get; init; }

    public bool IsEquipmentDisplayed { get; init; }

    public bool IsVaultDisplayed { get; init; }

    public ClientInventoryPanelMode InventoryMode { get; init; }

    public bool IsCharacterInfoDisplayed { get; init; }

    public int ActiveCharacterInfoTabRaw { get; init; }

    public ClientCharacterInfoTab ActiveCharacterInfoTab { get; init; }

    public int LastSelectedCharacterInfoTabRaw { get; init; }

    public ClientCharacterInfoTab LastSelectedCharacterInfoTab { get; init; }

    public ClientMissionDetailsPresentationObservation MissionDetails { get; init; } =
        ClientMissionDetailsPresentationObservation.Unavailable(
            "Mission details were not observed");

    public ClientFactionDetailsPresentationObservation FactionDetails { get; init; } =
        ClientFactionDetailsPresentationObservation.Unavailable(
            "Faction details were not observed");

    public static ClientPanelPresentationObservation Unavailable(
        string status,
        uint gameplayUtilityControllerAddress = 0,
        uint equipmentViewAddress = 0,
        uint equipmentViewVTableAddress = 0,
        uint inventoryViewAddress = 0,
        uint inventoryViewVTableAddress = 0,
        uint vaultViewAddress = 0,
        uint vaultViewVTableAddress = 0,
        uint characterInfoViewAddress = 0,
        uint characterInfoViewVTableAddress = 0)
    {
        return new ClientPanelPresentationObservation
        {
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
            MissionDetails =
                ClientMissionDetailsPresentationObservation.Unavailable(
                    status),
            FactionDetails =
                ClientFactionDetailsPresentationObservation.Unavailable(
                    status),
        };
    }
}
