namespace Net7ClientManager.Observations.Observers;

using System.Globalization;
using Net7ClientManager.Observations.Models;

internal sealed class ClientSecureInventoryObserver
{
    private const uint ImageBase = 0x00400000;

    private const uint InventoryCollectionBindingVTableStatic =
        0x00afc9b0;

    private const uint InventoryCollectionBindingVTableRva =
        InventoryCollectionBindingVTableStatic - ImageBase;

    private const uint BindingClientContextOffset = 0x20;
    private const uint BindingKindOffset = 0x50;
    private const uint BindingPrimaryCollectionOffset = 0x54;
    private const uint BindingPrimaryCollectionNameOffset = 0x58;
    private const uint BindingPrimaryCollectionPropertyOffset = 0x8c;

    private const int PlayerSecureBindingKind = 3;
    private const string SecureInventoryCollectionName =
        "SecureInventory";

    private const uint SecureInventorySlotVectorBeginOffset = 0x88;
    private const uint SecureInventorySlotVectorEndOffset = 0x8c;
    private const uint SecureInventorySlotVectorCapacityOffset = 0x90;

    private const uint SlotPropertyVectorBeginOffset = 0x88;
    private const uint SlotPropertyVectorEndOffset = 0x8c;
    private const uint SlotPropertyVectorCapacityOffset = 0x90;
    private const uint SlotPropertyShortNameOffset = 0x08;
    private const uint SlotPropertyQualifiedNameOffset = 0x3c;

    private const uint FloatAuxDataPropertyTypeDescriptorStatic =
        0x00c416f0;

    private const uint FloatAuxDataPropertyTypeDescriptorRva =
        FloatAuxDataPropertyTypeDescriptorStatic - ImageBase;

    private const uint Int32AuxDataPropertyTypeDescriptorStatic =
        0x00c416a8;

    private const uint Int32AuxDataPropertyTypeDescriptorRva =
        Int32AuxDataPropertyTypeDescriptorStatic - ImageBase;

    private const uint StringAuxDataPropertyVTableStatic =
        0x00aeb0e4;

    private const uint StringAuxDataPropertyVTableRva =
        StringAuxDataPropertyVTableStatic - ImageBase;

    private const int SecureInventorySlotCount =
        ClientSecureInventoryObservation.ExpectedSlotCount;

    private const int ExpectedSlotPropertyCount = 10;
    private const int MaximumSlotPropertyCount = 64;
    private const int MaximumPropertyNameLength = 128;
    private const int MaximumCollectionNameLength = 64;

    private static readonly TimeSpan refreshInterval =
        TimeSpan.FromSeconds(5);

    private static readonly TimeSpan discoveryRetryInterval =
        TimeSpan.FromSeconds(30);

    private static readonly TimeSpan visibleVaultDiscoveryRetryInterval =
        TimeSpan.FromSeconds(2);

    private static readonly string[] slotPropertyShortNames =
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

    private static readonly HashSet<string> slotPropertyShortNameSet =
        slotPropertyShortNames.ToHashSet(
            StringComparer.Ordinal);

    private readonly System.Threading.Lock cacheLock = new();

    private readonly Dictionary<int, ProcessCache> processCaches = [];

    private readonly ClientAuxDataLookupReader auxDataReader =
        new();

    public void Refresh(
        ProcessMemoryReader memory,
        ObservedClientState state)
    {
        if (!state.HasDirectClientState ||
            state.ModuleBaseAddress == 0 ||
            state.ClientContextAddress == 0)
        {
            this.Forget(state.ProcessId);

            SetObservation(
                state,
                ClientSecureInventoryObservation.Unavailable(
                    "Direct SClient state is unavailable"));

            return;
        }

        var now = DateTimeOffset.UtcNow;
        var cache = this.GetOrCreateCache(state);
        var isVaultDisplayed = IsVaultDisplayed(state);

        if (cache is { Layout: not null, LastRefreshAt: not null } &&
            now - cache.LastRefreshAt.Value < refreshInterval)
        {
            if (cache.LastObservation != null)
            {
                SetObservation(
                    state,
                    cache.LastObservation);
            }

            return;
        }

        if (cache.Layout == null)
        {
            var retryInterval = isVaultDisplayed
                ? visibleVaultDiscoveryRetryInterval
                : discoveryRetryInterval;

            if (cache.LastDiscoveryAttemptAt.HasValue &&
                now - cache.LastDiscoveryAttemptAt.Value <
                retryInterval)
            {
                if (cache.LastObservation != null)
                {
                    SetObservation(
                        state,
                        cache.LastObservation);
                }

                return;
            }

            cache.LastDiscoveryAttemptAt = now;

            if (!this.TryDiscoverLayout(
                    memory,
                    state.ModuleBaseAddress,
                    state.ClientContextAddress,
                    out var discoveredLayout,
                    out var discoveryError))
            {
                var unavailable =
                    ClientSecureInventoryObservation.Unavailable(
                        discoveryError);

                cache.LastObservation = unavailable;

                SetObservation(
                    state,
                    unavailable);

                return;
            }

            cache.Layout = discoveredLayout;
        }
        else if (!this.TryValidateLayout(
                     memory,
                     state.ModuleBaseAddress,
                     state.ClientContextAddress,
                     cache.Layout,
                     out _))
        {
            cache.Layout = null;
            cache.LastDiscoveryAttemptAt = now;

            if (!this.TryDiscoverLayout(
                    memory,
                    state.ModuleBaseAddress,
                    state.ClientContextAddress,
                    out var discoveredLayout,
                    out var discoveryError))
            {
                var unavailable =
                    ClientSecureInventoryObservation.Unavailable(
                        discoveryError);

                cache.LastObservation = unavailable;

                SetObservation(
                    state,
                    unavailable);

                return;
            }

            cache.Layout = discoveredLayout;
        }

        var observation = this.ObserveSlots(
            memory,
            cache.Layout);

        cache.LastRefreshAt = now;
        cache.LastObservation = observation;

        if (observation.ReadErrorCount ==
            SecureInventorySlotCount)
        {
            cache.Layout = null;
            cache.LastDiscoveryAttemptAt = null;
        }

        SetObservation(
            state,
            observation);
    }

    private static bool IsVaultDisplayed(
        ObservedClientState state)
    {
        lock (state.PanelPresentationLock)
        {
            return state.PanelPresentation.IsAvailable &&
                   state.PanelPresentation.IsVaultDisplayed;
        }
    }

    public void Forget(
        int processId)
    {
        lock (this.cacheLock)
        {
            this.processCaches.Remove(processId);
        }
    }

    private ProcessCache GetOrCreateCache(
        ObservedClientState state)
    {
        lock (this.cacheLock)
        {
            if (this.processCaches.TryGetValue(
                    state.ProcessId,
                    out var existing) &&
                existing.ModuleBaseAddress ==
                    state.ModuleBaseAddress &&
                existing.ClientContextAddress ==
                    state.ClientContextAddress)
            {
                return existing;
            }

            var created = new ProcessCache(
                state.ModuleBaseAddress,
                state.ClientContextAddress);

            this.processCaches[state.ProcessId] = created;

            return created;
        }
    }

    private bool TryDiscoverLayout(
        ProcessMemoryReader memory,
        uint moduleBaseAddress,
        uint clientContextAddress,
        out SecureInventoryLayout layout,
        out string error)
    {
        layout = null!;
        error = "";

        uint expectedVTable;

        try
        {
            expectedVTable = checked(
                moduleBaseAddress +
                InventoryCollectionBindingVTableRva);
        }
        catch (OverflowException)
        {
            error =
                "SecureInventory binding vtable address overflow";

            return false;
        }

        foreach (var candidateAddress
                 in memory.ScanForUInt32(expectedVTable))
        {
            if (!this.TryReadLayoutAtBinding(
                    memory,
                    moduleBaseAddress,
                    clientContextAddress,
                    expectedVTable,
                    candidateAddress,
                    out layout,
                    out _))
            {
                continue;
            }

            return true;
        }

        error =
            "No live PlayerSecure binding for this SClient was found";

        return false;
    }

    private bool TryReadLayoutAtBinding(
        ProcessMemoryReader memory,
        uint moduleBaseAddress,
        uint clientContextAddress,
        uint expectedVTable,
        uint bindingAddress,
        out SecureInventoryLayout layout,
        out string error)
    {
        layout = null!;
        error = "";

        try
        {
            if (!memory.TryReadUInt32(
                    bindingAddress,
                    out var actualVTable) ||
                actualVTable != expectedVTable ||
                !memory.TryReadUInt32(
                    checked(
                        bindingAddress +
                        BindingClientContextOffset),
                    out var observedClientContext) ||
                observedClientContext != clientContextAddress ||
                !memory.TryReadUInt32(
                    checked(
                        bindingAddress +
                        BindingKindOffset),
                    out var kind) ||
                kind != PlayerSecureBindingKind ||
                !memory.TryReadUInt32(
                    checked(
                        bindingAddress +
                        BindingPrimaryCollectionOffset),
                    out var primaryCollectionOwner) ||
                primaryCollectionOwner != clientContextAddress ||
                !memory.TryReadUInt32(
                    checked(
                        bindingAddress +
                        BindingPrimaryCollectionNameOffset),
                    out var collectionNameAddress) ||
                collectionNameAddress == 0 ||
                !memory.TryReadNullTerminatedLatin1String(
                    collectionNameAddress,
                    MaximumCollectionNameLength,
                    out var collectionName) ||
                !string.Equals(
                    collectionName,
                    SecureInventoryCollectionName,
                    StringComparison.Ordinal) ||
                !memory.TryReadUInt32(
                    checked(
                        bindingAddress +
                        BindingPrimaryCollectionPropertyOffset),
                    out var propertyAddress) ||
                propertyAddress == 0)
            {
                error =
                    "Candidate is not the validated PlayerSecure binding";

                return false;
            }

            if (!TryReadPointerVector(
                    memory,
                    propertyAddress,
                    SecureInventorySlotVectorBeginOffset,
                    SecureInventorySlotVectorEndOffset,
                    SecureInventorySlotVectorCapacityOffset,
                    SecureInventorySlotCount,
                    out var slotVectorBegin,
                    out var slotVectorEnd,
                    out var slotVectorCapacity,
                    out var slotObjectAddresses,
                    out error))
            {
                return false;
            }

            if (!TryReadRuntimeValue(
                    memory,
                    moduleBaseAddress,
                    FloatAuxDataPropertyTypeDescriptorRva,
                    "float AuxData property type descriptor",
                    out var floatPropertyTypeDescriptor,
                    out error) ||
                !TryReadRuntimeValue(
                    memory,
                    moduleBaseAddress,
                    Int32AuxDataPropertyTypeDescriptorRva,
                    "Int32 AuxData property type descriptor",
                    out var int32PropertyTypeDescriptor,
                    out error))
            {
                return false;
            }

            List<SecureInventorySlotLayout> slots =
                new(SecureInventorySlotCount);

            for (var slotIndex = 0;
                 slotIndex < SecureInventorySlotCount;
                 slotIndex++)
            {
                var slotObjectAddress =
                    slotObjectAddresses[slotIndex];

                if (slotObjectAddress == 0)
                {
                    error = string.Create(
                        CultureInfo.InvariantCulture,
                        $"SecureInventory slot {slotIndex} has a null slot-object pointer");

                    return false;
                }

                if (!this.TryReadSlotLayout(
                        memory,
                        slotIndex,
                        slotObjectAddress,
                        out var slotLayout,
                        out error))
                {
                    return false;
                }

                slots.Add(slotLayout);
            }

            layout = new SecureInventoryLayout(
                bindingAddress,
                propertyAddress,
                slotVectorBegin,
                slotVectorEnd,
                slotVectorCapacity,
                floatPropertyTypeDescriptor,
                int32PropertyTypeDescriptor,
                checked(
                    moduleBaseAddress +
                    StringAuxDataPropertyVTableRva),
                slots);

            return true;
        }
        catch (OverflowException)
        {
            error =
                "SecureInventory layout address calculation overflow";

            return false;
        }
    }

    private bool TryValidateLayout(
        ProcessMemoryReader memory,
        uint moduleBaseAddress,
        uint clientContextAddress,
        SecureInventoryLayout layout,
        out string error)
    {
        error = "";

        try
        {
            var expectedVTable = checked(
                moduleBaseAddress +
                InventoryCollectionBindingVTableRva);

            if (!memory.TryReadUInt32(
                    layout.BindingAddress,
                    out var actualVTable) ||
                actualVTable != expectedVTable ||
                !memory.TryReadUInt32(
                    checked(
                        layout.BindingAddress +
                        BindingClientContextOffset),
                    out var observedClientContext) ||
                observedClientContext != clientContextAddress ||
                !memory.TryReadUInt32(
                    checked(
                        layout.BindingAddress +
                        BindingKindOffset),
                    out var kind) ||
                kind != PlayerSecureBindingKind ||
                !memory.TryReadUInt32(
                    checked(
                        layout.BindingAddress +
                        BindingPrimaryCollectionPropertyOffset),
                    out var propertyAddress) ||
                propertyAddress != layout.PropertyAddress ||
                !TryReadPointerVector(
                    memory,
                    propertyAddress,
                    SecureInventorySlotVectorBeginOffset,
                    SecureInventorySlotVectorEndOffset,
                    SecureInventorySlotVectorCapacityOffset,
                    SecureInventorySlotCount,
                    out var slotVectorBegin,
                    out var slotVectorEnd,
                    out var slotVectorCapacity,
                    out var slotObjectAddresses,
                    out _) ||
                slotVectorBegin != layout.SlotVectorBegin ||
                slotVectorEnd != layout.SlotVectorEnd ||
                slotVectorCapacity != layout.SlotVectorCapacity ||
                slotObjectAddresses.Count != layout.Slots.Count)
            {
                error =
                    "Cached SecureInventory layout is no longer valid";

                return false;
            }

            for (var slotIndex = 0;
                 slotIndex < slotObjectAddresses.Count;
                 slotIndex++)
            {
                if (slotObjectAddresses[slotIndex] !=
                    layout.Slots[slotIndex].ObjectAddress)
                {
                    error =
                        "SecureInventory slot-object pointers changed";

                    return false;
                }
            }

            return true;
        }
        catch (OverflowException)
        {
            error =
                "SecureInventory validation address calculation overflow";

            return false;
        }
    }

    private bool TryReadSlotLayout(
        ProcessMemoryReader memory,
        int slotIndex,
        uint slotObjectAddress,
        out SecureInventorySlotLayout layout,
        out string error)
    {
        layout = null!;
        error = "";

        try
        {
            if (!TryReadPointerVector(
                    memory,
                    slotObjectAddress,
                    SlotPropertyVectorBeginOffset,
                    SlotPropertyVectorEndOffset,
                    SlotPropertyVectorCapacityOffset,
                    expectedCount: null,
                    out var propertyVectorBegin,
                    out _,
                    out _,
                    out var propertyAddresses,
                    out error))
            {
                return false;
            }

            if (propertyAddresses.Count is 0 or > MaximumSlotPropertyCount)
            {
                error = string.Create(
                    CultureInfo.InvariantCulture,
                    $"SecureInventory slot {slotIndex} exposed unexpected property count {propertyAddresses.Count}");

                return false;
            }

            Dictionary<string, uint> properties =
                new(StringComparer.Ordinal);

            foreach (var propertyAddress in propertyAddresses)
            {
                if (propertyAddress == 0 ||
                    !memory.TryReadUInt32(
                        checked(
                            propertyAddress +
                            SlotPropertyShortNameOffset),
                        out var shortNameAddress) ||
                    shortNameAddress == 0 ||
                    !memory.TryReadNullTerminatedLatin1String(
                        shortNameAddress,
                        MaximumPropertyNameLength,
                        out var shortName) ||
                    !slotPropertyShortNameSet.Contains(
                        shortName))
                {
                    continue;
                }

                var expectedQualifiedName = string.Create(
                    CultureInfo.InvariantCulture,
                    $"Hull.SecureInventory.{slotIndex}.{shortName}");

                if (!memory.TryReadUInt32(
                        checked(
                            propertyAddress +
                            SlotPropertyQualifiedNameOffset),
                        out var qualifiedNameAddress) ||
                    qualifiedNameAddress == 0 ||
                    !memory.TryReadNullTerminatedLatin1String(
                        qualifiedNameAddress,
                        MaximumPropertyNameLength,
                        out var qualifiedName) ||
                    !string.Equals(
                        qualifiedName,
                        expectedQualifiedName,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                properties[expectedQualifiedName] =
                    propertyAddress;
            }

            if (properties.Count !=
                ExpectedSlotPropertyCount)
            {
                error = string.Create(
                    CultureInfo.InvariantCulture,
                    $"SecureInventory slot {slotIndex} exposed {properties.Count}/{ExpectedSlotPropertyCount} expected properties");

                return false;
            }

            layout = new SecureInventorySlotLayout(
                slotIndex,
                slotObjectAddress,
                propertyVectorBegin,
                properties);

            return true;
        }
        catch (OverflowException)
        {
            error = string.Create(
                CultureInfo.InvariantCulture,
                $"SecureInventory slot {slotIndex} layout address calculation overflow");

            return false;
        }
    }

    private ClientSecureInventoryObservation ObserveSlots(
        ProcessMemoryReader memory,
        SecureInventoryLayout layout)
    {
        List<ClientInventoryItemObservation> slots =
            new(SecureInventorySlotCount);

        var readErrorCount = 0;

        foreach (var slotLayout in layout.Slots)
        {
            var slot = this.ObserveSlot(
                memory,
                layout,
                slotLayout,
                out var slotHasReadError);

            if (slotHasReadError)
            {
                readErrorCount++;
            }

            slots.Add(slot);
        }

        var occupiedCount = slots.Count(
            slot => slot.IsOccupied);

        var freeCount = slots.Count(
            slot => slot.IsUsableEmpty);

        var unavailableCount = slots.Count(
            slot => slot.IsUnavailable);

        var status = readErrorCount == 0
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"Available; {occupiedCount}/{slots.Count} occupied, {freeCount} free, {unavailableCount} unavailable")
            : string.Create(
                CultureInfo.InvariantCulture,
                $"Available with {readErrorCount} slot read error(s); {occupiedCount}/{slots.Count} occupied, {freeCount} free, {unavailableCount} unavailable");

        return new ClientSecureInventoryObservation
        {
            IsAvailable = true,
            Status = status,
            BindingAddress = layout.BindingAddress,
            PropertyAddress = layout.PropertyAddress,
            SlotVectorAddress = layout.SlotVectorBegin,
            ReadErrorCount = readErrorCount,
            Slots = slots,
        };
    }

    private ClientInventoryItemObservation ObserveSlot(
        ProcessMemoryReader memory,
        SecureInventoryLayout layout,
        SecureInventorySlotLayout slotLayout,
        out bool hasReadError)
    {
        hasReadError = false;

        var prefix = string.Create(
            CultureInfo.InvariantCulture,
            $"Hull.SecureInventory.{slotLayout.Slot}");

        string Name(
            string shortName)
        {
            return $"{prefix}.{shortName}";
        }

        var lookup = new ClientAuxDataLookupSnapshot(
            slotLayout.PropertyVectorAddress,
            layout.FloatPropertyTypeDescriptor,
            0,
            0,
            layout.Int32PropertyTypeDescriptor,
            layout.StringPropertyVTable,
            slotLayout.Properties);

        var itemTemplateIdName =
            Name("ItemTemplateID");

        if (!this.auxDataReader.TryReadInt32Property(
                memory,
                lookup,
                itemTemplateIdName,
                out var itemTemplateId,
                out var itemTemplateIdError))
        {
            hasReadError = true;

            return new ClientInventoryItemObservation
            {
                Collection =
                    ClientInventoryCollectionKind.Secure,
                Slot = slotLayout.Slot,
                PropertyPrefix = prefix,
                Status = itemTemplateIdError,
                PropertyAddresses =
                    slotLayout.Properties,
            };
        }

        if (itemTemplateId.PropertyAddress == 0)
        {
            hasReadError = true;

            return new ClientInventoryItemObservation
            {
                Collection =
                    ClientInventoryCollectionKind.Secure,
                Slot = slotLayout.Slot,
                PropertyPrefix = prefix,
                Status =
                    "ItemTemplateID is missing from the slot property vector",
                PropertyAddresses =
                    slotLayout.Properties,
            };
        }

        if (!itemTemplateId.IsValid)
        {
            hasReadError = true;

            return new ClientInventoryItemObservation
            {
                Collection =
                    ClientInventoryCollectionKind.Secure,
                Slot = slotLayout.Slot,
                PropertyPrefix = prefix,
                IsPresent = true,
                Status =
                    "ItemTemplateID is present but not valid",
                PropertyAddresses =
                    slotLayout.Properties,
            };
        }

        List<string> errors = [];

        var stackCount = ReadInt32(
            Name("StackCount"));

        if (itemTemplateId.Value <= 0)
        {
            hasReadError = errors.Count > 0;

            var stateText = itemTemplateId.Value switch
            {
                -1 => "Empty",
                -2 => "Unavailable",
                _ => string.Create(
                    CultureInfo.InvariantCulture,
                    $"Non-positive ItemTemplateID {itemTemplateId.Value}"),
            };

            var status = errors.Count == 0
                ? stateText
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"{stateText} with {errors.Count} field read error(s): {errors[0]}");

            return new ClientInventoryItemObservation
            {
                Collection =
                    ClientInventoryCollectionKind.Secure,
                Slot = slotLayout.Slot,
                PropertyPrefix = prefix,
                IsPresent = true,
                IsValid = true,
                Status = status,
                ItemTemplateId = itemTemplateId.Value,
                StackCount = stackCount,
                PropertyAddresses =
                    slotLayout.Properties,
            };
        }

        var quality = ReadFloat(
            Name("Quality"));

        var structure = ReadFloat(
            Name("Structure"));

        var averageCost = ReadFloat(
            Name("AveCost"));

        var builderName = ReadString(
            Name("BuilderName"));

        var instanceInfo = ReadString(
            Name("InstanceInfo"));

        var activatedEffectInfo = ReadString(
            Name("InstanceActivatedEffectInfo"));

        var equipEffectInfo = ReadString(
            Name("InstanceEquipEffectInfo"));

        uint? priceLow = null;
        uint? priceHigh = null;

        if (!this.auxDataReader.TryReadRawProperty(
                memory,
                lookup,
                Name("Price"),
                readSecondaryValue: true,
                out var price,
                out var priceError))
        {
            errors.Add(priceError);
        }
        else if (price.PropertyAddress != 0 &&
                 price.IsValid)
        {
            priceLow = price.PrimaryValue;
            priceHigh = price.SecondaryValue;
        }

        hasReadError = errors.Count > 0;

        var occupiedStatus = errors.Count == 0
            ? "Occupied"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"Occupied with {errors.Count} field read error(s): {errors[0]}");

        return new ClientInventoryItemObservation
        {
            Collection =
                ClientInventoryCollectionKind.Secure,
            Slot = slotLayout.Slot,
            PropertyPrefix = prefix,
            IsPresent = true,
            IsValid = true,
            Status = occupiedStatus,
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
            PropertyAddresses =
                slotLayout.Properties,
        };

        int? ReadInt32(
            string propertyName)
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

            return sample.IsValid
                ? sample.Value
                : null;
        }

        float? ReadFloat(
            string propertyName)
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

            return sample.IsValid
                ? sample.Value
                : null;
        }

        string? ReadString(
            string propertyName)
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

            return sample.IsValid
                ? sample.Value
                : null;
        }
    }

    private static bool TryReadPointerVector(
        ProcessMemoryReader memory,
        uint ownerAddress,
        uint beginOffset,
        uint endOffset,
        uint capacityOffset,
        int? expectedCount,
        out uint begin,
        out uint end,
        out uint capacity,
        out IReadOnlyList<uint> values,
        out string error)
    {
        begin = 0;
        end = 0;
        capacity = 0;
        values = [];
        error = "";

        try
        {
            if (!memory.TryReadUInt32(
                    checked(ownerAddress + beginOffset),
                    out begin) ||
                !memory.TryReadUInt32(
                    checked(ownerAddress + endOffset),
                    out end) ||
                !memory.TryReadUInt32(
                    checked(ownerAddress + capacityOffset),
                    out capacity))
            {
                error = $"Could not read pointer vector at 0x{ownerAddress:X8}";

                return false;
            }

            if (begin == 0 ||
                end < begin ||
                capacity < end ||
                (end - begin) % sizeof(uint) != 0 ||
                (capacity - begin) % sizeof(uint) != 0)
            {
                error = $"Pointer vector is inconsistent: begin=0x{begin:X8}, end=0x{end:X8}, capacity=0x{capacity:X8}";

                return false;
            }

            var count = checked(
                (int)((end - begin) /
                      sizeof(uint)));

            if (expectedCount.HasValue &&
                count != expectedCount.Value)
            {
                error = string.Create(
                    CultureInfo.InvariantCulture,
                    $"Pointer vector contains {count} entries; expected {expectedCount.Value}");

                return false;
            }

            if (count == 0 ||
                count > Math.Max(
                    SecureInventorySlotCount,
                    MaximumSlotPropertyCount))
            {
                error = string.Create(
                    CultureInfo.InvariantCulture,
                    $"Pointer vector contains unexpected entry count {count}");

                return false;
            }

            if (!memory.TryReadBytes(
                    begin,
                    checked(
                        count * sizeof(uint)),
                    out var bytes))
            {
                error = string.Create(
                    CultureInfo.InvariantCulture,
                    $"Could not read {count} pointer-vector entries at 0x{begin:X8}");

                return false;
            }

            List<uint> observed = new(count);

            for (var index = 0;
                 index < count;
                 index++)
            {
                observed.Add(
                    BitConverter.ToUInt32(
                        bytes,
                        index * sizeof(uint)));
            }

            values = observed;
            return true;
        }
        catch (OverflowException)
        {
            error =
                "Pointer-vector address calculation overflow";

            return false;
        }
    }

    private static bool TryReadRuntimeValue(
        ProcessMemoryReader memory,
        uint moduleBaseAddress,
        uint rva,
        string description,
        out uint value,
        out string error)
    {
        value = 0;
        error = "";

        try
        {
            var address = checked(
                moduleBaseAddress + rva);

            if (!memory.TryReadUInt32(
                    address,
                    out value) ||
                value == 0)
            {
                error = $"Could not read {description} at 0x{address:X8}";

                return false;
            }

            return true;
        }
        catch (OverflowException)
        {
            error =
                $"{description} address calculation overflow";

            return false;
        }
    }

    private static void SetObservation(
        ObservedClientState state,
        ClientSecureInventoryObservation observation)
    {
        state.LocalPlayer = state.LocalPlayer with
        {
            SecureInventory = observation,
        };
    }

    private sealed class ProcessCache(
        uint moduleBaseAddress,
        uint clientContextAddress)
    {
        public uint ModuleBaseAddress { get; } =
            moduleBaseAddress;

        public uint ClientContextAddress { get; } =
            clientContextAddress;

        public DateTimeOffset? LastDiscoveryAttemptAt { get; set; }

        public DateTimeOffset? LastRefreshAt { get; set; }

        public ClientSecureInventoryObservation? LastObservation
        { get; set; }

        public SecureInventoryLayout? Layout { get; set; }
    }

    private sealed record SecureInventoryLayout(
        uint BindingAddress,
        uint PropertyAddress,
        uint SlotVectorBegin,
        uint SlotVectorEnd,
        uint SlotVectorCapacity,
        uint FloatPropertyTypeDescriptor,
        uint Int32PropertyTypeDescriptor,
        uint StringPropertyVTable,
        IReadOnlyList<SecureInventorySlotLayout> Slots);

    private sealed record SecureInventorySlotLayout(
        int Slot,
        uint ObjectAddress,
        uint PropertyVectorAddress,
        IReadOnlyDictionary<string, uint> Properties);
}
