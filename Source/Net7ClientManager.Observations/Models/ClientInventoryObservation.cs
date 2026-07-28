namespace Net7ClientManager.Observations.Models;

public sealed record ClientInventoryObservation
{
    public bool IsAvailable { get; init; }

    public string Status { get; init; } = "";

    public uint AuxDataLookupAddress { get; init; }

    public int? CargoCapacity { get; init; }

    public int? FutureWeaponSlotCount { get; init; }

    public int? FutureDeviceSlotCount { get; init; }

    public string? EquipMountModel { get; init; }

    public IReadOnlyList<ClientInventoryItemObservation> CargoSlots
    { get; init; } = [];

    public IReadOnlyList<ClientInventoryItemObservation> EquippedSlots
    { get; init; } = [];

    public IReadOnlyList<ClientInventoryItemObservation> AmmoSlots
    { get; init; } = [];

    public IReadOnlyList<ClientInventoryItemObservation> CargoItems =>
    [
        .. this.CargoSlots.Where(
            slot => slot.IsOccupied),
    ];

    public IReadOnlyList<ClientInventoryItemObservation> EquippedItems =>
    [
        .. this.EquippedSlots.Where(
            slot => slot.IsOccupied),
    ];

    public IReadOnlyList<ClientInventoryItemObservation> AmmoItems =>
    [
        .. this.AmmoSlots.Where(
            slot => slot.IsOccupied),
    ];

    public int CargoUsedSlotCount =>
        this.CargoSlots.Count(
            slot =>
                slot.IsOccupied &&
                (!this.CargoCapacity.HasValue ||
                 slot.Slot < this.CargoCapacity.Value));

    public int? CargoFreeSlotCount =>
        this.CargoCapacity.HasValue
            ? Math.Max(
                0,
                this.CargoCapacity.Value -
                this.CargoUsedSlotCount)
            : null;

    public int CargoUnavailableSlotCount =>
        this.CargoSlots.Count(
            slot => slot.IsUnavailable);

    public int OccupiedWeaponSlotCount =>
        this.EquippedSlots.Count(
            slot =>
                slot.IsOccupied &&
                this.GetEquipmentSlotKind(slot.Slot) ==
                ClientEquipmentSlotKind.Weapon);

    public int OccupiedDeviceSlotCount =>
        this.EquippedSlots.Count(
            slot =>
                slot.IsOccupied &&
                this.GetEquipmentSlotKind(slot.Slot) ==
                ClientEquipmentSlotKind.Device);

    public int UsableWeaponSlotCount =>
        this.EquippedSlots.Count(
            slot =>
                this.GetEquipmentSlotKind(slot.Slot) ==
                    ClientEquipmentSlotKind.Weapon &&
                slot.IsUsable);

    public int UsableDeviceSlotCount =>
        this.EquippedSlots.Count(
            slot =>
                this.GetEquipmentSlotKind(slot.Slot) ==
                    ClientEquipmentSlotKind.Device &&
                slot.IsUsable);

    public int BusyEquipmentSlotCount =>
        this.EquippedSlots.Count(
            slot =>
                slot.Operational.IsBusy);

    public int OperationallyReadyWeaponCount =>
        this.EquippedSlots.Count(
            slot =>
                this.GetEquipmentSlotKind(slot.Slot) ==
                    ClientEquipmentSlotKind.Weapon &&
                slot.Operational.IsOperationallyReady);

    public int OperationallyReadyDeviceCount =>
        this.EquippedSlots.Count(
            slot =>
                this.GetEquipmentSlotKind(slot.Slot) ==
                    ClientEquipmentSlotKind.Device &&
                slot.Operational.IsOperationallyReady);

    public ClientEquipmentSlotKind GetEquipmentSlotKind(
        int slot)
    {
        return slot switch
        {
            0 => ClientEquipmentSlotKind.Shield,
            1 => ClientEquipmentSlotKind.Reactor,
            2 => ClientEquipmentSlotKind.Engine,
            _ => this.GetVariableEquipmentSlotKind(slot),
        };
    }

    public int? GetEquipmentSlotOrdinal(
        int slot)
    {
        var kind = this.GetEquipmentSlotKind(slot);

        if (kind == ClientEquipmentSlotKind.Weapon)
        {
            return slot - 2;
        }

        if (kind == ClientEquipmentSlotKind.Device &&
            this.FutureWeaponSlotCount.HasValue)
        {
            return slot -
                   (2 + this.FutureWeaponSlotCount.Value);
        }

        return null;
    }

    public ClientInventoryItemObservation? GetAmmoForEquipmentSlot(
        int equipmentSlot)
    {
        return this.AmmoSlots.FirstOrDefault(
            slot => slot.Slot == equipmentSlot);
    }

    public static ClientInventoryObservation Unavailable(
        string status,
        uint auxDataLookupAddress = 0)
    {
        return new ClientInventoryObservation
        {
            Status = status,
            AuxDataLookupAddress = auxDataLookupAddress,
        };
    }

    private ClientEquipmentSlotKind GetVariableEquipmentSlotKind(
        int slot)
    {
        if (!this.FutureWeaponSlotCount.HasValue ||
            !this.FutureDeviceSlotCount.HasValue)
        {
            return ClientEquipmentSlotKind.Reserved;
        }

        var weaponStart = 3;
        var weaponEndExclusive = checked(
            weaponStart +
            Math.Max(
                0,
                this.FutureWeaponSlotCount.Value));

        if (slot >= weaponStart &&
            slot < weaponEndExclusive)
        {
            return ClientEquipmentSlotKind.Weapon;
        }

        var deviceEndExclusive = checked(
            weaponEndExclusive +
            Math.Max(
                0,
                this.FutureDeviceSlotCount.Value));

        return slot >= weaponEndExclusive &&
               slot < deviceEndExclusive
            ? ClientEquipmentSlotKind.Device
            : ClientEquipmentSlotKind.Reserved;
    }
}
