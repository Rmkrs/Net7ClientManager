namespace Net7ClientManager.Observations.Models;

public sealed record ClientLootingObservation
{
    public bool IsAvailable { get; init; }

    public string Status { get; init; } = "";

    public uint MainViewAddress { get; init; }

    public uint CockpitControllerAddress { get; init; }

    public uint CockpitControllerVTableAddress { get; init; }

    public uint LootPanelViewAddress { get; init; }

    public uint LootPanelViewVTableAddress { get; init; }

    public uint LootTargetBindingAddress { get; init; }

    public uint LootTargetBindingVTableAddress { get; init; }

    public bool IsLootPanelDisplayed { get; init; }

    public uint LootTargetObjectId { get; init; }

    public bool HasAttachedLootTarget =>
        this.LootTargetObjectId != 0;

    public bool IsLootSessionActive =>
        this.IsLootPanelDisplayed &&
        this.HasAttachedLootTarget;

    public static ClientLootingObservation Unavailable(
        string status,
        uint mainViewAddress = 0,
        uint cockpitControllerAddress = 0,
        uint cockpitControllerVTableAddress = 0,
        uint lootPanelViewAddress = 0,
        uint lootPanelViewVTableAddress = 0,
        uint lootTargetBindingAddress = 0,
        uint lootTargetBindingVTableAddress = 0)
    {
        return new ClientLootingObservation
        {
            Status = status,
            MainViewAddress = mainViewAddress,
            CockpitControllerAddress =
                cockpitControllerAddress,
            CockpitControllerVTableAddress =
                cockpitControllerVTableAddress,
            LootPanelViewAddress =
                lootPanelViewAddress,
            LootPanelViewVTableAddress =
                lootPanelViewVTableAddress,
            LootTargetBindingAddress =
                lootTargetBindingAddress,
            LootTargetBindingVTableAddress =
                lootTargetBindingVTableAddress,
        };
    }
}
