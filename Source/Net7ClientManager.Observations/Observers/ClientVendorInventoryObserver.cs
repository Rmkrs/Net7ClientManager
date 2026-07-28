// ReSharper disable GrammarMistakeInComment
namespace Net7ClientManager.Observations.Observers;

using System.Buffers.Binary;
using System.Globalization;
using Net7ClientManager.Observations.Models;

internal sealed class ClientVendorInventoryObserver
{
    /*
     * VendorInventory is a fixed 128-slot cache on the SClient Hull
     * AuxData (+0x12C0). Opening a vendor populates the complete catalog.
     * Changing vendor tabs does not rewrite it, purchases do not remove
     * entries, and closing the window does not clear it. Opening another
     * vendor rewrites the same slots with the new catalog.
     *
     * Therefore this observer deliberately exposes the last loaded vendor
     * snapshot. It cannot infer the vendor identity or whether the vendor
     * window is currently open. ItemTemplateID > 0 is authoritative
     * occupancy; empty and cleared tail slots use ItemTemplateID == -2.
     */
    private const uint ImageBase = 0x00400000;

    private const string VendorInventoryPropertyName =
        "Hull.VendorInventory";

    private const uint UInt64AuxDataPropertyVTableRva =
        0x00aeafb8 - ImageBase;

    private const uint UInt64AuxDataPropertyTypeDescriptorRva =
        0x00be3a94 - ImageBase;

    private const int VendorInventorySlotCount = 128;

    private const uint AuxDataPropertyVTable = 0x00;
    private const uint AuxDataPropertyTypeDescriptor = 0x04;
    private const uint AuxDataPropertyValid = 0x70;
    private const uint UInt64AuxDataPropertyLowWord = 0x88;
    private const uint UInt64AuxDataPropertyHighWord = 0x8c;

    private const int UInt64PropertySnapshotLength =
        (int)UInt64AuxDataPropertyHighWord + sizeof(uint);

    private static readonly string[] itemPropertySuffixes =
    [
        "ItemTemplateID",
        "StackCount",
        "Quality",
        "Structure",
        "Price",
    ];

    private static readonly string[] hullPropertyNames =
    [
        VendorInventoryPropertyName,
    ];

    private static readonly string[] propertyNames =
        BuildPropertyNames();

    private readonly ClientAuxDataLookupReader auxDataReader =
        new();

    private readonly Dictionary<int, CachedVendorLookup>
        cachedLookups = [];

    public ClientVendorInventoryObservation Observe(
        ProcessMemoryReader memory,
        uint moduleBaseAddress,
        int processId,
        uint hullAuxDataAddress,
        ulong? currentCredits)
    {
        if (hullAuxDataAddress == 0)
        {
            return ClientVendorInventoryObservation.Unavailable(
                "SClient Hull AuxData (+0x12C0) is unavailable");
        }

        ClientVendorInventoryObservation? lastObservation = null;

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
                if (!this.TryOpenVendorLookup(
                        memory,
                        moduleBaseAddress,
                        hullAuxDataAddress,
                        out cachedLookup,
                        out var openError))
                {
                    this.cachedLookups.Remove(
                        processId);

                    return ClientVendorInventoryObservation.Unavailable(
                        openError,
                        hullAuxDataAddress);
                }

                this.cachedLookups[processId] =
                    cachedLookup;
            }

            lastObservation = this.ObserveCached(
                memory,
                moduleBaseAddress,
                cachedLookup,
                currentCredits);

            if (lastObservation.IsAvailable)
            {
                return lastObservation;
            }

            // A relog, docking transition, or reconstructed Hull can leave
            // cached property addresses stale. Discard them and make one
            // immediate bounded rediscovery attempt.
            this.cachedLookups.Remove(
                processId);
        }

        return lastObservation ??
               ClientVendorInventoryObservation.Unavailable(
                   "VendorInventory could not be observed",
                   hullAuxDataAddress);
    }

    private ClientVendorInventoryObservation ObserveCached(
        ProcessMemoryReader memory,
        uint moduleBaseAddress,
        CachedVendorLookup cachedLookup,
        ulong? currentCredits)
    {
        var lookup = cachedLookup.Lookup;
        List<string> errors = [];

        var hasPriceMetadata =
            TryResolveUInt64PropertyMetadata(
                memory,
                moduleBaseAddress,
                out var expectedPriceVTable,
                out var expectedPriceTypeDescriptor,
                out var priceMetadataError);

        if (!hasPriceMetadata)
        {
            errors.Add(priceMetadataError);
        }

        List<ClientVendorInventoryItemObservation> slots = [];

        for (var slotIndex = 0;
             slotIndex < VendorInventorySlotCount;
             slotIndex++)
        {
            var slot = this.ObserveSlot(
                memory,
                lookup,
                slotIndex,
                currentCredits,
                hasPriceMetadata,
                expectedPriceVTable,
                expectedPriceTypeDescriptor,
                out var slotErrors);

            slots.Add(slot);
            errors.AddRange(slotErrors);
        }

        var presentSlotCount = slots.Count(
            slot => slot.IsPresent);

        var occupiedSlotCount = slots.Count(
            slot => slot.IsOccupied);

        if (presentSlotCount == 0)
        {
            return ClientVendorInventoryObservation.Unavailable(
                "VendorInventory fields are not present in the SClient Hull AuxData lookup",
                cachedLookup.HullAuxDataAddress,
                cachedLookup.VendorInventoryAddress,
                lookup.LookupAddress,
                cachedLookup.LookupTraversalNodeCount);
        }

        string status;

        if (occupiedSlotCount == 0)
        {
            status = errors.Count == 0
                ? "Available; no vendor catalog has been loaded into the 128-slot cache"
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"Available with {errors.Count} field read error(s); no vendor catalog is currently present in the cache: {errors[0]}");
        }
        else
        {
            status = errors.Count == 0
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"Available; cached last-loaded vendor catalog contains {occupiedSlotCount}/{VendorInventorySlotCount} item(s)")
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"Available with {errors.Count} field read error(s); cached last-loaded vendor catalog contains {occupiedSlotCount}/{VendorInventorySlotCount} item(s): {errors[0]}");
        }

        return new ClientVendorInventoryObservation
        {
            IsAvailable = true,
            Status = status,
            HullAuxDataAddress =
                cachedLookup.HullAuxDataAddress,
            VendorInventoryAddress =
                cachedLookup.VendorInventoryAddress,
            AuxDataLookupAddress =
                lookup.LookupAddress,
            LookupTraversalNodeCount =
                cachedLookup.LookupTraversalNodeCount,
            CurrentCredits = currentCredits,
            ReadErrorCount = errors.Count,
            Slots = slots,
        };
    }

    private bool TryOpenVendorLookup(
        ProcessMemoryReader memory,
        uint moduleBaseAddress,
        uint hullAuxDataAddress,
        out CachedVendorLookup cachedLookup,
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
                VendorInventoryPropertyName,
                out var vendorInventoryAddress) ||
            vendorInventoryAddress == 0)
        {
            error =
                "Hull.VendorInventory was not found in the SClient Hull property vector";

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
                $"SClient Hull AuxData lookup resolved {lookup.Properties.Count}/{propertyNames.Length} VendorInventory fields; first missing: {firstMissing}");

            return false;
        }

        cachedLookup = new CachedVendorLookup(
            moduleBaseAddress,
            hullAuxDataAddress,
            vendorInventoryAddress,
            lookup,
            lookupTraversalNodeCount);

        return true;
    }

    private ClientVendorInventoryItemObservation ObserveSlot(
        ProcessMemoryReader memory,
        ClientAuxDataLookupSnapshot lookup,
        int slotIndex,
        ulong? currentCredits,
        bool hasPriceMetadata,
        uint expectedPriceVTable,
        uint expectedPriceTypeDescriptor,
        out IReadOnlyList<string> errors)
    {
        List<string> slotErrors = [];

        var propertyPrefix = string.Create(
            CultureInfo.InvariantCulture,
            $"VendorInventory.{slotIndex}");

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

            return new ClientVendorInventoryItemObservation
            {
                Slot = slotIndex,
                PropertyPrefix = propertyPrefix,
                Status = itemTemplateIdError,
                ReadErrorCount = slotErrors.Count,
            };
        }

        if (itemTemplateId.PropertyAddress == 0)
        {
            errors = slotErrors;

            return new ClientVendorInventoryItemObservation
            {
                Slot = slotIndex,
                PropertyPrefix = propertyPrefix,
                Status =
                    "Vendor cache slot is not present in the AuxData lookup",
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

            return new ClientVendorInventoryItemObservation
            {
                Slot = slotIndex,
                PropertyPrefix = propertyPrefix,
                IsPresent = true,
                Status =
                    "Vendor cache slot is present but not valid",
                PropertyAddresses = propertyAddresses,
            };
        }

        if (itemTemplateId.Value <= 0)
        {
            errors = slotErrors;

            return new ClientVendorInventoryItemObservation
            {
                Slot = slotIndex,
                PropertyPrefix = propertyPrefix,
                IsPresent = true,
                IsValid = true,
                Status = string.Create(
                    CultureInfo.InvariantCulture,
                    $"Empty vendor cache slot; ItemTemplateID={itemTemplateId.Value}"),
                ItemTemplateId = itemTemplateId.Value,
                PropertyAddresses = propertyAddresses,
            };
        }

        var stackCount = this.ReadInt32(
            memory,
            lookup,
            $"{propertyPrefix}.StackCount",
            slotErrors,
            propertyAddresses);

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

        ulong? price = null;
        uint priceValidState = 0;

        if (hasPriceMetadata)
        {
            var priceName =
                $"{propertyPrefix}.Price";

            if (lookup.Properties.TryGetValue(
                    priceName,
                    out var pricePropertyAddress) &&
                pricePropertyAddress != 0)
            {
                propertyAddresses[priceName] =
                    pricePropertyAddress;

                if (TryReadUInt64Property(
                        memory,
                        pricePropertyAddress,
                        expectedPriceVTable,
                        expectedPriceTypeDescriptor,
                        priceName,
                        out priceValidState,
                        out var observedPrice,
                        out var priceError))
                {
                    if (priceValidState != 0)
                    {
                        price = observedPrice;
                    }
                }
                else
                {
                    slotErrors.Add(priceError);
                }
            }
            else
            {
                slotErrors.Add(
                    $"{priceName} is not present in the AuxData lookup");
            }
        }

        bool? isAffordable =
            currentCredits.HasValue &&
            price.HasValue
                ? currentCredits.Value >= price.Value
                : null;

        errors = slotErrors;

        return new ClientVendorInventoryItemObservation
        {
            Slot = slotIndex,
            PropertyPrefix = propertyPrefix,
            IsPresent = true,
            IsValid = true,
            Status = slotErrors.Count == 0
                ? "Occupied vendor cache slot"
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"Occupied vendor cache slot with {slotErrors.Count} field read error(s): {slotErrors[0]}"),
            ReadErrorCount = slotErrors.Count,
            ItemTemplateId = itemTemplateId.Value,
            ItemName = ClientItemTemplateCatalog.GetKnownName(
                itemTemplateId.Value),
            StackCount = stackCount,
            Quality = quality,
            Structure = structure,
            Price = price,
            IsAffordable = isAffordable,
            PriceValidState = priceValidState,
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

    private static bool TryResolveUInt64PropertyMetadata(
        ProcessMemoryReader memory,
        uint moduleBaseAddress,
        out uint expectedVTable,
        out uint expectedTypeDescriptor,
        out string error)
    {
        expectedVTable = 0;
        expectedTypeDescriptor = 0;
        error = "";

        uint descriptorAddress;

        try
        {
            expectedVTable = checked(
                moduleBaseAddress +
                UInt64AuxDataPropertyVTableRva);

            descriptorAddress = checked(
                moduleBaseAddress +
                UInt64AuxDataPropertyTypeDescriptorRva);
        }
        catch (OverflowException)
        {
            error =
                "Vendor price metadata address calculation overflow";

            return false;
        }

        if (!memory.TryReadUInt32(
                descriptorAddress,
                out expectedTypeDescriptor) ||
            expectedTypeDescriptor == 0)
        {
            error = $"Could not read UInt64 AuxData property type descriptor at 0x{descriptorAddress:X8}";

            return false;
        }

        return true;
    }

    private static bool TryReadUInt64Property(
        ProcessMemoryReader memory,
        uint propertyAddress,
        uint expectedVTable,
        uint expectedTypeDescriptor,
        string propertyName,
        out uint validState,
        out ulong value,
        out string error)
    {
        validState = 0;
        value = 0;
        error = "";

        if (!memory.TryReadBytes(
                propertyAddress,
                UInt64PropertySnapshotLength,
                out var bytes))
        {
            error = $"Could not read {propertyName} at 0x{propertyAddress:X8}";

            return false;
        }

        var actualVTable = BinaryPrimitives.ReadUInt32LittleEndian(
            bytes.AsSpan(
                (int)AuxDataPropertyVTable,
                sizeof(uint)));

        if (actualVTable != expectedVTable)
        {
            error = $"Unexpected {propertyName} vtable 0x{actualVTable:X8} at 0x{propertyAddress:X8}; expected 0x{expectedVTable:X8}";

            return false;
        }

        var actualTypeDescriptor =
            BinaryPrimitives.ReadUInt32LittleEndian(
                bytes.AsSpan(
                    (int)AuxDataPropertyTypeDescriptor,
                    sizeof(uint)));

        if (actualTypeDescriptor != expectedTypeDescriptor)
        {
            error = $"Unexpected {propertyName} type descriptor 0x{actualTypeDescriptor:X8} at 0x{propertyAddress:X8}; expected 0x{expectedTypeDescriptor:X8}";

            return false;
        }

        validState = BinaryPrimitives.ReadUInt32LittleEndian(
            bytes.AsSpan(
                (int)AuxDataPropertyValid,
                sizeof(uint)));

        var lowWord = BinaryPrimitives.ReadUInt32LittleEndian(
            bytes.AsSpan(
                (int)UInt64AuxDataPropertyLowWord,
                sizeof(uint)));

        var highWord = BinaryPrimitives.ReadUInt32LittleEndian(
            bytes.AsSpan(
                (int)UInt64AuxDataPropertyHighWord,
                sizeof(uint)));

        value =
            ((ulong)highWord << 32) |
            lowWord;

        return true;
    }

    private static string[] BuildPropertyNames()
    {
        List<string> names = [];

        for (var slotIndex = 0;
             slotIndex < VendorInventorySlotCount;
             slotIndex++)
        {
            var prefix = string.Create(
                CultureInfo.InvariantCulture,
                $"VendorInventory.{slotIndex}");

            foreach (var suffix in itemPropertySuffixes)
            {
                names.Add(
                    $"{prefix}.{suffix}");
            }
        }

        return [.. names];
    }

    private readonly record struct CachedVendorLookup(
        uint ModuleBaseAddress,
        uint HullAuxDataAddress,
        uint VendorInventoryAddress,
        ClientAuxDataLookupSnapshot Lookup,
        int LookupTraversalNodeCount);

}
