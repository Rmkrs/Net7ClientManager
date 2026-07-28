namespace Net7ClientManager.Observations.Observers;

using System.Globalization;
using Net7ClientManager.Observations.Models;

internal sealed class ClientRewardOverflowInventoryObserver
{
    /*
     * LocalPlayerAuxData constructs RewardInventory as one fixed
     * InventoryItemAuxData slot and OverflowInventory as eight fixed
     * InventoryItemAuxData slots. Both use the same item schema already
     * proven by Cargo, SecureInventory, and VendorInventory.
     *
     * These baskets are expected to be empty during ordinary play and are
     * difficult to populate deliberately. The observer therefore exposes
     * their raw fixed-slot state without guessing when or why the server
     * places an item in either collection.
     */
    private const string RewardInventoryPropertyName =
        "Hull.RewardInventory";

    private const string OverflowInventoryPropertyName =
        "Hull.OverflowInventory";

    private const int RewardInventorySlotCount =
        ClientRewardOverflowInventoryObservation.RewardSlotCount;

    private const int OverflowInventorySlotCount =
        ClientRewardOverflowInventoryObservation.OverflowSlotCount;

    private static readonly string[] itemPropertySuffixes =
    [
        "ItemTemplateID",
        "StackCount",
        "Quality",
        "Structure",
        "AveCost",
        "BuilderName",
        "InstanceInfo",
        "InstanceActivatedEffectInfo",
        "InstanceEquipEffectInfo",
        "Price",
    ];

    private static readonly string[] hullPropertyNames =
    [
        RewardInventoryPropertyName,
        OverflowInventoryPropertyName,
    ];

    private static readonly string[] propertyNames =
        BuildPropertyNames();

    private readonly ClientAuxDataLookupReader auxDataReader =
        new();

    private readonly Dictionary<int, CachedLookup>
        cachedLookups = [];

    public ClientRewardOverflowInventoryObservation Observe(
        ProcessMemoryReader memory,
        uint moduleBaseAddress,
        int processId,
        uint hullAuxDataAddress)
    {
        if (hullAuxDataAddress == 0)
        {
            return ClientRewardOverflowInventoryObservation.Unavailable(
                "SClient Hull AuxData (+0x12C0) is unavailable");
        }

        ClientRewardOverflowInventoryObservation? lastObservation = null;

        for (var attempt = 0;
             attempt < 2;
             attempt++)
        {
            if (!this.cachedLookups.TryGetValue(
                    processId,
                    out var cachedLookup) ||
                cachedLookup.ModuleBaseAddress !=
                    moduleBaseAddress ||
                cachedLookup.HullAuxDataAddress !=
                    hullAuxDataAddress)
            {
                if (!this.TryOpenLookup(
                        memory,
                        moduleBaseAddress,
                        hullAuxDataAddress,
                        out cachedLookup,
                        out var openError))
                {
                    this.cachedLookups.Remove(
                        processId);

                    return ClientRewardOverflowInventoryObservation.Unavailable(
                        openError,
                        hullAuxDataAddress);
                }

                this.cachedLookups[processId] =
                    cachedLookup;
            }

            lastObservation = this.ObserveCached(
                memory,
                cachedLookup);

            if (lastObservation.IsAvailable)
            {
                return lastObservation;
            }

            // A relog or reconstructed Hull can leave cached property
            // addresses stale. Discard them and make one bounded immediate
            // rediscovery attempt.
            this.cachedLookups.Remove(
                processId);
        }

        return lastObservation ??
               ClientRewardOverflowInventoryObservation.Unavailable(
                   "RewardInventory and OverflowInventory could not be observed",
                   hullAuxDataAddress);
    }

    private ClientRewardOverflowInventoryObservation ObserveCached(
        ProcessMemoryReader memory,
        CachedLookup cachedLookup)
    {
        List<string> errors = [];

        var rewardSlots = this.ObserveCollection(
            memory,
            cachedLookup.Lookup,
            ClientInventoryCollectionKind.Reward,
            "RewardInventory",
            RewardInventorySlotCount,
            errors);

        var overflowSlots = this.ObserveCollection(
            memory,
            cachedLookup.Lookup,
            ClientInventoryCollectionKind.Overflow,
            "OverflowInventory",
            OverflowInventorySlotCount,
            errors);

        if (rewardSlots.All(slot => !slot.IsPresent) &&
            overflowSlots.All(slot => !slot.IsPresent))
        {
            return ClientRewardOverflowInventoryObservation.Unavailable(
                "RewardInventory and OverflowInventory fields are not present in the SClient Hull AuxData lookup",
                cachedLookup.HullAuxDataAddress,
                cachedLookup.RewardInventoryAddress,
                cachedLookup.OverflowInventoryAddress,
                cachedLookup.Lookup.LookupAddress,
                cachedLookup.LookupTraversalNodeCount);
        }

        var rewardOccupiedCount = rewardSlots.Count(
            slot => slot.IsOccupied);

        var overflowOccupiedCount = overflowSlots.Count(
            slot => slot.IsOccupied);

        var status = errors.Count == 0
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"Available; reward {rewardOccupiedCount}/{RewardInventorySlotCount} occupied, overflow {overflowOccupiedCount}/{OverflowInventorySlotCount} occupied")
            : string.Create(
                CultureInfo.InvariantCulture,
                $"Available with {errors.Count} field read error(s); reward {rewardOccupiedCount}/{RewardInventorySlotCount} occupied, overflow {overflowOccupiedCount}/{OverflowInventorySlotCount} occupied: {errors[0]}");

        return new ClientRewardOverflowInventoryObservation
        {
            IsAvailable = true,
            Status = status,
            HullAuxDataAddress =
                cachedLookup.HullAuxDataAddress,
            RewardInventoryAddress =
                cachedLookup.RewardInventoryAddress,
            OverflowInventoryAddress =
                cachedLookup.OverflowInventoryAddress,
            AuxDataLookupAddress =
                cachedLookup.Lookup.LookupAddress,
            LookupTraversalNodeCount =
                cachedLookup.LookupTraversalNodeCount,
            ReadErrorCount = errors.Count,
            RewardSlots = rewardSlots,
            OverflowSlots = overflowSlots,
        };
    }

    private bool TryOpenLookup(
        ProcessMemoryReader memory,
        uint moduleBaseAddress,
        uint hullAuxDataAddress,
        out CachedLookup cachedLookup,
        out string error)
    {
        cachedLookup = default;
        error = "";

        if (!this.auxDataReader.TryOpenFromPropertyVector(
                memory,
                moduleBaseAddress,
                hullAuxDataAddress,
                hullPropertyNames,
                out var hullLookup,
                out error))
        {
            return false;
        }

        if (!hullLookup.Properties.TryGetValue(
                RewardInventoryPropertyName,
                out var rewardInventoryAddress) ||
            rewardInventoryAddress == 0)
        {
            error =
                "Hull.RewardInventory was not found in the SClient Hull property vector";

            return false;
        }

        if (!hullLookup.Properties.TryGetValue(
                OverflowInventoryPropertyName,
                out var overflowInventoryAddress) ||
            overflowInventoryAddress == 0)
        {
            error =
                "Hull.OverflowInventory was not found in the SClient Hull property vector";

            return false;
        }

        if (!this.auxDataReader.TryOpenTargeted(
                memory,
                moduleBaseAddress,
                hullAuxDataAddress,
                propertyNames,
                out var lookup,
                out var lookupTraversalNodeCount,
                out error))
        {
            return false;
        }

        if (lookup.Properties.Count !=
            propertyNames.Length)
        {
            var firstMissing = propertyNames.First(
                name =>
                    !lookup.Properties.ContainsKey(name));

            error = string.Create(
                CultureInfo.InvariantCulture,
                $"SClient Hull AuxData lookup resolved {lookup.Properties.Count}/{propertyNames.Length} RewardInventory and OverflowInventory fields; first missing: {firstMissing}");

            return false;
        }

        cachedLookup = new CachedLookup(
            moduleBaseAddress,
            hullAuxDataAddress,
            rewardInventoryAddress,
            overflowInventoryAddress,
            lookup,
            lookupTraversalNodeCount);

        return true;
    }

    private IReadOnlyList<ClientInventoryItemObservation> ObserveCollection(
        ProcessMemoryReader memory,
        ClientAuxDataLookupSnapshot lookup,
        ClientInventoryCollectionKind collection,
        string collectionName,
        int slotCount,
        List<string> errors)
    {
        List<ClientInventoryItemObservation> slots =
            new(slotCount);

        for (var slotIndex = 0;
             slotIndex < slotCount;
             slotIndex++)
        {
            var slot = this.ObserveSlot(
                memory,
                lookup,
                collection,
                collectionName,
                slotIndex,
                out var slotErrors);

            slots.Add(slot);
            errors.AddRange(slotErrors);
        }

        return slots;
    }

    private ClientInventoryItemObservation ObserveSlot(
        ProcessMemoryReader memory,
        ClientAuxDataLookupSnapshot lookup,
        ClientInventoryCollectionKind collection,
        string collectionName,
        int slotIndex,
        out IReadOnlyList<string> errors)
    {
        List<string> slotErrors = [];

        var propertyPrefix = string.Create(
            CultureInfo.InvariantCulture,
            $"{collectionName}.{slotIndex}");

        var itemTemplateIdName =
            $"{propertyPrefix}.ItemTemplateID";

        if (!this.auxDataReader.TryReadInt32Property(
                memory,
                lookup,
                itemTemplateIdName,
                out var itemTemplateId,
                out var itemTemplateIdError))
        {
            slotErrors.Add(itemTemplateIdError);
            errors = slotErrors;

            return new ClientInventoryItemObservation
            {
                Collection = collection,
                Slot = slotIndex,
                PropertyPrefix = propertyPrefix,
                Status = itemTemplateIdError,
            };
        }

        if (itemTemplateId.PropertyAddress == 0)
        {
            errors = slotErrors;

            return new ClientInventoryItemObservation
            {
                Collection = collection,
                Slot = slotIndex,
                PropertyPrefix = propertyPrefix,
                Status =
                    "Inventory slot is not present in the AuxData lookup",
            };
        }

        Dictionary<string, uint> propertyAddresses =
            new(StringComparer.Ordinal)
            {
                [itemTemplateIdName] =
                    itemTemplateId.PropertyAddress,
            };

        if (!itemTemplateId.IsValid)
        {
            errors = slotErrors;

            return new ClientInventoryItemObservation
            {
                Collection = collection,
                Slot = slotIndex,
                PropertyPrefix = propertyPrefix,
                IsPresent = true,
                Status =
                    "ItemTemplateID is present but not valid",
                PropertyAddresses = propertyAddresses,
            };
        }

        var stackCount = this.ReadInt32(
            memory,
            lookup,
            $"{propertyPrefix}.StackCount",
            slotErrors,
            propertyAddresses);

        if (itemTemplateId.Value <= 0)
        {
            errors = slotErrors;

            var stateText = itemTemplateId.Value switch
            {
                -1 => "Empty",
                -2 => "Unavailable",
                _ => string.Create(
                    CultureInfo.InvariantCulture,
                    $"Non-positive ItemTemplateID {itemTemplateId.Value}"),
            };

            return new ClientInventoryItemObservation
            {
                Collection = collection,
                Slot = slotIndex,
                PropertyPrefix = propertyPrefix,
                IsPresent = true,
                IsValid = true,
                Status = slotErrors.Count == 0
                    ? stateText
                    : string.Create(
                        CultureInfo.InvariantCulture,
                        $"{stateText} with {slotErrors.Count} field read error(s): {slotErrors[0]}"),
                ItemTemplateId = itemTemplateId.Value,
                StackCount = stackCount,
                PropertyAddresses = propertyAddresses,
            };
        }

        var quality = this.ReadFloat(
            memory,
            lookup,
            $"{propertyPrefix}.Quality",
            slotErrors,
            propertyAddresses);

        var structure = this.ReadFloat(
            memory,
            lookup,
            $"{propertyPrefix}.Structure",
            slotErrors,
            propertyAddresses);

        var averageCost = this.ReadFloat(
            memory,
            lookup,
            $"{propertyPrefix}.AveCost",
            slotErrors,
            propertyAddresses);

        var builderName = this.ReadString(
            memory,
            lookup,
            $"{propertyPrefix}.BuilderName",
            slotErrors,
            propertyAddresses);

        var instanceInfo = this.ReadString(
            memory,
            lookup,
            $"{propertyPrefix}.InstanceInfo",
            slotErrors,
            propertyAddresses);

        var activatedEffectInfo = this.ReadString(
            memory,
            lookup,
            $"{propertyPrefix}.InstanceActivatedEffectInfo",
            slotErrors,
            propertyAddresses);

        var equipEffectInfo = this.ReadString(
            memory,
            lookup,
            $"{propertyPrefix}.InstanceEquipEffectInfo",
            slotErrors,
            propertyAddresses);

        uint? priceLow = null;
        uint? priceHigh = null;
        var priceName =
            $"{propertyPrefix}.Price";

        if (!this.auxDataReader.TryReadRawProperty(
                memory,
                lookup,
                priceName,
                readSecondaryValue: true,
                out var price,
                out var priceError))
        {
            slotErrors.Add(priceError);
        }
        else
        {
            TrackPropertyAddress(
                propertyAddresses,
                priceName,
                price.PropertyAddress);

            if (price.PropertyAddress != 0 &&
                price.IsValid)
            {
                priceLow = price.PrimaryValue;
                priceHigh = price.SecondaryValue;
            }
        }

        errors = slotErrors;

        return new ClientInventoryItemObservation
        {
            Collection = collection,
            Slot = slotIndex,
            PropertyPrefix = propertyPrefix,
            IsPresent = true,
            IsValid = true,
            Status = slotErrors.Count == 0
                ? "Occupied"
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"Occupied with {slotErrors.Count} field read error(s): {slotErrors[0]}"),
            ItemTemplateId = itemTemplateId.Value,
            StackCount = stackCount,
            Quality = quality,
            Structure = structure,
            AverageCost = averageCost,
            BuilderName = builderName,
            InstanceInfo = instanceInfo,
            InstanceActivatedEffectInfo =
                activatedEffectInfo,
            InstanceEquipEffectInfo =
                equipEffectInfo,
            PriceLow = priceLow,
            PriceHigh = priceHigh,
            PropertyAddresses = propertyAddresses,
        };
    }

    private int? ReadInt32(
        ProcessMemoryReader memory,
        ClientAuxDataLookupSnapshot lookup,
        string propertyName,
        List<string> errors,
        IDictionary<string, uint> propertyAddresses)
    {
        if (!this.auxDataReader.TryReadInt32Property(
                memory,
                lookup,
                propertyName,
                out var sample,
                out var error))
        {
            errors.Add(error);
            return null;
        }

        TrackPropertyAddress(
            propertyAddresses,
            propertyName,
            sample.PropertyAddress);

        return sample.IsValid
            ? sample.Value
            : null;
    }

    private float? ReadFloat(
        ProcessMemoryReader memory,
        ClientAuxDataLookupSnapshot lookup,
        string propertyName,
        List<string> errors,
        IDictionary<string, uint> propertyAddresses)
    {
        if (!this.auxDataReader.TryReadFloatProperty(
                memory,
                lookup,
                propertyName,
                out var sample,
                out var error))
        {
            errors.Add(error);
            return null;
        }

        TrackPropertyAddress(
            propertyAddresses,
            propertyName,
            sample.PropertyAddress);

        return sample.IsValid
            ? sample.Value
            : null;
    }

    private string? ReadString(
        ProcessMemoryReader memory,
        ClientAuxDataLookupSnapshot lookup,
        string propertyName,
        List<string> errors,
        IDictionary<string, uint> propertyAddresses)
    {
        if (!this.auxDataReader.TryReadStringProperty(
                memory,
                lookup,
                propertyName,
                out var sample,
                out var error))
        {
            errors.Add(error);
            return null;
        }

        TrackPropertyAddress(
            propertyAddresses,
            propertyName,
            sample.PropertyAddress);

        return sample.IsValid
            ? sample.Value
            : null;
    }

    private static void TrackPropertyAddress(
        IDictionary<string, uint> propertyAddresses,
        string propertyName,
        uint propertyAddress)
    {
        if (propertyAddress != 0)
        {
            propertyAddresses[propertyName] =
                propertyAddress;
        }
    }

    private static string[] BuildPropertyNames()
    {
        List<string> names = [];

        AddCollection(
            names,
            "RewardInventory",
            RewardInventorySlotCount);

        AddCollection(
            names,
            "OverflowInventory",
            OverflowInventorySlotCount);

        return [.. names];

        static void AddCollection(
            ICollection<string> target,
            string collectionName,
            int slotCount)
        {
            for (var slotIndex = 0;
                 slotIndex < slotCount;
                 slotIndex++)
            {
                var prefix = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{collectionName}.{slotIndex}");

                foreach (var suffix in itemPropertySuffixes)
                {
                    target.Add(
                        $"{prefix}.{suffix}");
                }
            }
        }
    }

    private readonly record struct CachedLookup(
        uint ModuleBaseAddress,
        uint HullAuxDataAddress,
        uint RewardInventoryAddress,
        uint OverflowInventoryAddress,
        ClientAuxDataLookupSnapshot Lookup,
        int LookupTraversalNodeCount);
}
