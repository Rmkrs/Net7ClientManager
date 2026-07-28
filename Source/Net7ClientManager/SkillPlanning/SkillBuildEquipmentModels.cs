namespace Net7ClientManager.SkillPlanning;

internal sealed record SkillBuildEquipmentAlternative
{
    public int ItemTemplateId { get; init; }

    // Display text is denormalized for offline readability. Identity is always
    // ItemTemplateId.
    public string ItemName { get; init; } = "";

    public string Notes { get; init; } = "";
}

internal readonly record struct SkillBuildEquipmentSlot(
    SkillBuildEquipmentKind Kind,
    int Ordinal)
{
    public static SkillBuildEquipmentSlot Shield =>
        new(SkillBuildEquipmentKind.Shield, 1);

    public static SkillBuildEquipmentSlot Reactor =>
        new(SkillBuildEquipmentKind.Reactor, 1);

    public static SkillBuildEquipmentSlot Engine =>
        new(SkillBuildEquipmentKind.Engine, 1);

    public static SkillBuildEquipmentSlot Weapon(int ordinal) =>
        new(SkillBuildEquipmentKind.Weapon, ordinal);

    public static SkillBuildEquipmentSlot Device(int ordinal) =>
        new(SkillBuildEquipmentKind.Device, ordinal);
}

internal enum SkillBuildEquipmentKind
{
    Shield,
    Reactor,
    Engine,
    Weapon,
    Device,
}

internal sealed record SkillBuildEquipmentBaseline
{
    public bool IsAvailable { get; init; }

    public string Status { get; init; } = "";

    public int WeaponSlotCount { get; init; }

    public int DeviceSlotCount { get; init; }

    public bool HasVariableSlotCounts { get; init; }

    public IReadOnlyDictionary<SkillBuildEquipmentSlot, SkillBuildEquippedItem>
        Items { get; init; } =
            new Dictionary<SkillBuildEquipmentSlot, SkillBuildEquippedItem>();

    public IReadOnlyDictionary<int, int> InventoryItemCounts { get; init; } =
        new Dictionary<int, int>();

    public IReadOnlyDictionary<int, int> VaultItemCounts { get; init; } =
        new Dictionary<int, int>();

    public static SkillBuildEquipmentBaseline Unavailable(string status) =>
        new()
        {
            Status = status,
        };
}

internal sealed record SkillBuildEquippedItem(
    int ItemTemplateId,
    string ItemName);
