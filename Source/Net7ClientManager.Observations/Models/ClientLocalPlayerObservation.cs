namespace Net7ClientManager.Observations.Models;

public sealed record ClientLocalPlayerObservation
{
    public bool IsAvailable { get; init; }

    public string Status { get; init; } = "";

    public uint ObjectId { get; init; }

    public uint ClientObjectAddress { get; init; }

    public uint AuxDataAddress { get; init; }

    public ClientTargetShieldObservation Shield { get; init; } =
        ClientTargetShieldObservation.Unavailable(
            "Local-player shield state was not observed");

    public ClientTargetHullObservation Hull { get; init; } =
        ClientTargetHullObservation.Unavailable(
            "Local-player hull state was not observed");

    public ClientTargetEnergyObservation Energy { get; init; } =
        ClientTargetEnergyObservation.Unavailable(
            "Local-player energy state was not observed");

    public ClientShipOperationalObservation Operational { get; init; } =
        ClientShipOperationalObservation.Unavailable(
            "Local-player operational state was not observed");

    public ClientSpatialObservation Spatial { get; init; } =
        ClientSpatialObservation.Unavailable(
            "Local-player spatial state was not observed");

    public ClientInventoryObservation Inventory { get; init; } =
        ClientInventoryObservation.Unavailable(
            "Local-player inventory state was not observed");

    public ClientCharacterProgressionObservation CharacterProgression { get; init; } =
        ClientCharacterProgressionObservation.Unavailable(
            "Character progression state was not observed");

    public ClientCharacterDetailsObservation CharacterDetails { get; init; } =
        ClientCharacterDetailsObservation.Unavailable(
            "Character details were not observed");

    public ClientVendorInventoryObservation VendorInventory { get; init; } =
        ClientVendorInventoryObservation.Unavailable(
            "Vendor inventory state was not observed");

    public ClientRewardOverflowInventoryObservation RewardOverflowInventory { get; init; } =
        ClientRewardOverflowInventoryObservation.Unavailable(
            "Reward and overflow inventory state was not observed");

    public ClientBuffsObservation Buffs { get; init; } =
        ClientBuffsObservation.Unavailable(
            "Buff state was not observed");

    public ClientMissionLogObservation Missions { get; init; } =
        ClientMissionLogObservation.Unavailable(
            "Mission state was not observed");

    public ClientReputationObservation Reputation { get; init; } =
        ClientReputationObservation.Unavailable(
            "Reputation state was not observed");

    public ClientSecureInventoryObservation SecureInventory { get; init; } =
        ClientSecureInventoryObservation.Unavailable(
            "Secure inventory state was not observed");

    public static ClientLocalPlayerObservation Unavailable(
        string status)
    {
        return new ClientLocalPlayerObservation
        {
            Status = status,
        };
    }
}
