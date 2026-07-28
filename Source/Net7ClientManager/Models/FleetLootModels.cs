namespace Net7ClientManager.Models;

public sealed record FleetLootWindowSnapshot(
    string Header,
    string TargetName,
    IReadOnlyList<FleetLootLooterOption> Looters,
    IReadOnlyList<FleetLootItemRow> Items,
    bool RoundRobinEnabled,
    uint TargetObjectId,
    int? ActiveLooterProcessId,
    string? ActiveLooterName,
    bool IsBusy);

public sealed record FleetLootLooterOption(
    int ProcessId,
    string Name,
    int UsedCargoSlots,
    int? CargoCapacity,
    bool IsSelected);

public sealed record FleetLootItemRow(
    int Slot,
    int CargoSlot,
    int? ItemTemplateId,
    string Name,
    int? StackCount,
    float? QualityPercent);

public sealed record FleetLootActionResult(
    bool Succeeded,
    string Message,
    bool CloseWindow = false)
{
    public static FleetLootActionResult Success(
        string message,
        bool closeWindow = false) =>
        new(true, message, closeWindow);

    public static FleetLootActionResult Failure(string message) =>
        new(false, message);
}
