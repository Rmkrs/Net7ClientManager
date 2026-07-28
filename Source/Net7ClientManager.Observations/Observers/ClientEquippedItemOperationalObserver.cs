namespace Net7ClientManager.Observations.Observers;

using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.InteropServices;
using Net7ClientManager.Observations.Models;

internal sealed class ClientEquippedItemOperationalObserver
{
    private const uint ImageBase = 0x00400000;

    private const uint ItemStatePropertyVTableRva =
        0x00b017ec - ImageBase;

    private const uint ReadyTimePropertyVTableRva =
        0x00aeb1ec - ImageBase;

    private const uint ItemStatePropertyTypeDescriptor =
        0x000001f0;

    private const uint ReadyTimePropertyTypeDescriptor =
        0x00000005;

    private const uint PropertyTypeDescriptorOffset = 0x04;
    private const uint PropertyValidOffset = 0x70;
    private const uint OrdinaryPropertyValueOffset = 0x84;
    private const uint UInt64PropertyLowWordOffset = 0x88;
    private const uint UInt64PropertyHighWordOffset = 0x8c;

    private const int ItemStatePropertySnapshotLength = 0x88;
    private const int ReadyTimePropertySnapshotLength = 0x90;

    private const uint InventoryItemTemplateIdPropertyOffset = 0x09c;
    private const uint InventoryItemPropertyVectorBegin = 0x88;
    private const uint InventoryItemPropertyVectorEnd = 0x8c;
    private const uint InventoryItemEffectsObjectOffset = 0x798;
    private const int InventoryItemEffectsFieldIndex = 0x0d;
    private const uint Int32PropertyValueOffset = 0x84;
    private const uint EffectsRangeOffset = 0x120;
    private const uint EffectsUsageOffset = 0x1a8;
    private const uint EffectsTargetsOffset = 0x230;
    private const uint EffectsValidityOffset = 0x2b8;

    private readonly ClientAuxDataLookupReader auxDataReader =
        new();

    public ClientEquippedItemOperationalObservation Observe(
        ProcessMemoryReader memory,
        uint moduleBaseAddress,
        uint clientTime,
        ClientAuxDataLookupSnapshot lookup,
        string propertyPrefix,
        int itemTemplateId,
        IDictionary<string, uint> propertyAddresses,
        out IReadOnlyList<string> readErrors)
    {
        List<string> errors = [];

        var itemStateName =
            $"{propertyPrefix}.ItemState";

        var readyTimeName =
            $"{propertyPrefix}.ReadyTime";

        var targetRangeName =
            $"{propertyPrefix}.TargetRange";

        uint expectedItemStateVTable;
        uint expectedReadyTimeVTable;

        try
        {
            expectedItemStateVTable = checked(
                moduleBaseAddress +
                ItemStatePropertyVTableRva);

            expectedReadyTimeVTable = checked(
                moduleBaseAddress +
                ReadyTimePropertyVTableRva);
        }
        catch (OverflowException)
        {
            const string error =
                "Equipped-item operational vtable address calculation overflow";

            readErrors = [error];

            return ClientEquippedItemOperationalObservation.Unavailable(
                error);
        }

        var itemState = ReadItemState(
            memory,
            lookup,
            itemStateName,
            expectedItemStateVTable,
            propertyAddresses,
            errors);

        var readyTime = ReadReadyTime(
            memory,
            lookup,
            readyTimeName,
            expectedReadyTimeVTable,
            propertyAddresses,
            errors);

        var targetRange = this.ReadTargetRange(
            memory,
            lookup,
            targetRangeName,
            propertyAddresses,
            errors);

        var liveEffects = ReadLiveEffects(
            memory,
            propertyPrefix,
            itemTemplateId,
            propertyAddresses,
            errors);

        readErrors = errors;

        var hasAnyProperty =
            itemState.PropertyAddress != 0 ||
            readyTime.PropertyAddress != 0 ||
            targetRange.PropertyAddress != 0 ||
            liveEffects.EffectsObjectAddress != 0;

        if (!hasAnyProperty)
        {
            return ClientEquippedItemOperationalObservation.Unavailable(
                "Equipped slot exposes no promoted operational fields");
        }

        var observation =
            new ClientEquippedItemOperationalObservation
            {
                IsAvailable = true,
                IsOccupied = itemTemplateId > 0,
                CurrentClientTime = clientTime,
                ItemStatePropertyAddress =
                    itemState.PropertyAddress,
                ItemStateValidState =
                    itemState.ValidState,
                RawItemState = itemState.Value,
                ReadyTimePropertyAddress =
                    readyTime.PropertyAddress,
                ReadyTimeValidState =
                    readyTime.ValidState,
                NominalReadyAtClientTime =
                    readyTime.Value,
                TargetRangePropertyAddress =
                    targetRange.PropertyAddress,
                TargetRange = targetRange.Value,
                EffectsObjectAddress =
                    liveEffects.EffectsObjectAddress,
                EffectsObjectIsValid =
                    liveEffects.EffectsObjectIsValid,
                EffectRangeAddress =
                    liveEffects.Range.Address,
                EffectRange = liveEffects.Range.Value,
                EffectUsageAddress =
                    liveEffects.Usage.Address,
                EffectUsage = liveEffects.Usage.Value,
                EffectTargetsAddress =
                    liveEffects.Targets.Address,
                EffectTargets = liveEffects.Targets.Value,
                EffectValidityAddress =
                    liveEffects.Validity.Address,
                EffectValidity = liveEffects.Validity.Value,
            };

        var status = BuildStatus(
            observation,
            errors);

        return observation with
        {
            Status = status,
        };
    }

    private static ItemStateReadResult ReadItemState(
        ProcessMemoryReader memory,
        ClientAuxDataLookupSnapshot lookup,
        string propertyName,
        uint expectedVTable,
        IDictionary<string, uint> propertyAddresses,
        List<string> errors)
    {
        if (!TryGetPropertyAddress(
                lookup,
                propertyName,
                propertyAddresses,
                out var propertyAddress))
        {
            return default;
        }

        if (!memory.TryReadBytes(
                propertyAddress,
                ItemStatePropertySnapshotLength,
                out var bytes))
        {
            errors.Add(
                    $"Could not read {propertyName} property at 0x{propertyAddress:X8}");

            return new ItemStateReadResult(
                propertyAddress,
                0,
                Value: null);
        }

        var actualVTable =
            BinaryPrimitives.ReadUInt32LittleEndian(
                bytes.AsSpan(0x00, sizeof(uint)));

        var actualTypeDescriptor =
            BinaryPrimitives.ReadUInt32LittleEndian(
                bytes.AsSpan(
                    checked((int)PropertyTypeDescriptorOffset),
                    sizeof(uint)));

        if (actualVTable != expectedVTable ||
            actualTypeDescriptor !=
                ItemStatePropertyTypeDescriptor)
        {
            errors.Add(
                    $"Unexpected {propertyName} type at 0x{propertyAddress:X8}: vtable=0x{actualVTable:X8}, descriptor=0x{actualTypeDescriptor:X8}; expected vtable=0x{expectedVTable:X8}, descriptor=0x{ItemStatePropertyTypeDescriptor:X8}");

            return new ItemStateReadResult(
                propertyAddress,
                0,
                Value: null);
        }

        var validState =
            BinaryPrimitives.ReadUInt32LittleEndian(
                bytes.AsSpan(
                    checked((int)PropertyValidOffset),
                    sizeof(uint)));

        if (validState == 0)
        {
            return new ItemStateReadResult(
                propertyAddress,
                validState,
                Value: null);
        }

        var value =
            BinaryPrimitives.ReadUInt32LittleEndian(
                bytes.AsSpan(
                    checked((int)OrdinaryPropertyValueOffset),
                    sizeof(uint)));

        return new ItemStateReadResult(
            propertyAddress,
            validState,
            value);
    }

    private static ReadyTimeReadResult ReadReadyTime(
        ProcessMemoryReader memory,
        ClientAuxDataLookupSnapshot lookup,
        string propertyName,
        uint expectedVTable,
        IDictionary<string, uint> propertyAddresses,
        List<string> errors)
    {
        if (!TryGetPropertyAddress(
                lookup,
                propertyName,
                propertyAddresses,
                out var propertyAddress))
        {
            return default;
        }

        if (!memory.TryReadBytes(
                propertyAddress,
                ReadyTimePropertySnapshotLength,
                out var bytes))
        {
            errors.Add(
                    $"Could not read {propertyName} property at 0x{propertyAddress:X8}");

            return new ReadyTimeReadResult(
                propertyAddress,
                0,
                Value: null);
        }

        var actualVTable =
            BinaryPrimitives.ReadUInt32LittleEndian(
                bytes.AsSpan(0x00, sizeof(uint)));

        var actualTypeDescriptor =
            BinaryPrimitives.ReadUInt32LittleEndian(
                bytes.AsSpan(
                    checked((int)PropertyTypeDescriptorOffset),
                    sizeof(uint)));

        if (actualVTable != expectedVTable ||
            actualTypeDescriptor !=
                ReadyTimePropertyTypeDescriptor)
        {
            errors.Add(
                    $"Unexpected {propertyName} type at 0x{propertyAddress:X8}: vtable=0x{actualVTable:X8}, descriptor=0x{actualTypeDescriptor:X8}; expected vtable=0x{expectedVTable:X8}, descriptor=0x{ReadyTimePropertyTypeDescriptor:X8}");

            return new ReadyTimeReadResult(
                propertyAddress,
                0,
                Value: null);
        }

        var validState =
            BinaryPrimitives.ReadUInt32LittleEndian(
                bytes.AsSpan(
                    checked((int)PropertyValidOffset),
                    sizeof(uint)));

        if (validState == 0)
        {
            return new ReadyTimeReadResult(
                propertyAddress,
                validState,
                Value: null);
        }

        var lowWord =
            BinaryPrimitives.ReadUInt32LittleEndian(
                bytes.AsSpan(
                    checked((int)UInt64PropertyLowWordOffset),
                    sizeof(uint)));

        var highWord =
            BinaryPrimitives.ReadUInt32LittleEndian(
                bytes.AsSpan(
                    checked((int)UInt64PropertyHighWordOffset),
                    sizeof(uint)));

        var value =
            ((ulong)highWord << 32) |
            lowWord;

        return new ReadyTimeReadResult(
            propertyAddress,
            validState,
            value);
    }

    private ClientFloatReadResult ReadTargetRange(
        ProcessMemoryReader memory,
        ClientAuxDataLookupSnapshot lookup,
        string propertyName,
        IDictionary<string, uint> propertyAddresses,
        List<string> errors)
    {
        if (!this.auxDataReader.TryReadFloatProperty(
                memory,
                lookup,
                propertyName,
                out var sample,
                out var error))
        {
            errors.Add(error);
            return default;
        }

        TrackPropertyAddress(
            propertyAddresses,
            propertyName,
            sample.PropertyAddress);

        return new ClientFloatReadResult(
            sample.PropertyAddress,
            sample.IsValid
                ? sample.Value
                : null);
    }

    private static LiveEffectsReadResult ReadLiveEffects(
        ProcessMemoryReader memory,
        string propertyPrefix,
        int itemTemplateId,
        IDictionary<string, uint> propertyAddresses,
        List<string> errors)
    {
        if (itemTemplateId <= 0)
        {
            return default;
        }

        var itemTemplateIdName =
            $"{propertyPrefix}.ItemTemplateID";

        if (!propertyAddresses.TryGetValue(
                itemTemplateIdName,
                out var itemTemplateIdPropertyAddress) ||
            itemTemplateIdPropertyAddress <
                InventoryItemTemplateIdPropertyOffset)
        {
            errors.Add(
                $"Could not derive equipped-item object from {itemTemplateIdName}");
            return default;
        }

        var itemObjectAddress =
            itemTemplateIdPropertyAddress -
            InventoryItemTemplateIdPropertyOffset;

        if (!TryAdd(
                itemTemplateIdPropertyAddress,
                Int32PropertyValueOffset,
                out var itemTemplateIdValueAddress) ||
            !memory.TryReadUInt32(
                itemTemplateIdValueAddress,
                out var observedItemTemplateId) ||
            observedItemTemplateId !=
                unchecked((uint)itemTemplateId))
        {
            errors.Add(
                $"Could not validate equipped ItemTemplateID {itemTemplateId} at 0x{itemTemplateIdPropertyAddress:X8}");
            return default;
        }

        if (!TryAdd(
                itemObjectAddress,
                InventoryItemPropertyVectorBegin,
                out var vectorBeginAddress) ||
            !TryAdd(
                itemObjectAddress,
                InventoryItemPropertyVectorEnd,
                out var vectorEndAddress) ||
            !memory.TryReadUInt32(
                vectorBeginAddress,
                out var vectorBegin) ||
            !memory.TryReadUInt32(
                vectorEndAddress,
                out var vectorEnd) ||
            vectorBegin == 0 ||
            vectorEnd < vectorBegin ||
            (vectorEnd - vectorBegin) % sizeof(uint) != 0)
        {
            errors.Add(
                $"Could not read equipped-item child vector at 0x{itemObjectAddress:X8}");
            return default;
        }

        var vectorCount =
            (vectorEnd - vectorBegin) / sizeof(uint);

        if (vectorCount <= InventoryItemEffectsFieldIndex ||
            !TryAdd(
                vectorBegin,
                checked((uint)(
                    InventoryItemEffectsFieldIndex *
                    sizeof(uint))),
                out var effectsVectorEntryAddress) ||
            !memory.TryReadUInt32(
                effectsVectorEntryAddress,
                out var effectsObjectAddress) ||
            effectsObjectAddress == 0)
        {
            errors.Add(
                $"Could not resolve equipped-item Effects object at child index {InventoryItemEffectsFieldIndex}");
            return default;
        }

        if (TryAdd(
                itemObjectAddress,
                InventoryItemEffectsObjectOffset,
                out var expectedEffectsObjectAddress) &&
            effectsObjectAddress != expectedEffectsObjectAddress)
        {
            errors.Add(
                $"Unexpected equipped-item Effects object 0x{effectsObjectAddress:X8}; expected 0x{expectedEffectsObjectAddress:X8}");
        }

        var effectsObjectIsValid = false;

        if (TryAdd(
                effectsObjectAddress,
                PropertyValidOffset,
                out var effectsValidAddress) &&
            memory.TryReadUInt32(
                effectsValidAddress,
                out var effectsValidState))
        {
            effectsObjectIsValid = effectsValidState != 0;
        }
        else
        {
            errors.Add(
                $"Could not read equipped-item Effects validity at 0x{effectsObjectAddress:X8}");
        }

        return new LiveEffectsReadResult(
            effectsObjectAddress,
            effectsObjectIsValid,
            ReadLiveFloat(
                memory,
                effectsObjectAddress,
                EffectsRangeOffset,
                effectsObjectIsValid,
                "Range",
                errors),
            ReadLiveUInt32(
                memory,
                effectsObjectAddress,
                EffectsUsageOffset,
                effectsObjectIsValid,
                "Usage",
                errors),
            ReadLiveUInt32(
                memory,
                effectsObjectAddress,
                EffectsTargetsOffset,
                effectsObjectIsValid,
                "Targets",
                errors),
            ReadLiveUInt32(
                memory,
                effectsObjectAddress,
                EffectsValidityOffset,
                effectsObjectIsValid,
                "Validity",
                errors));
    }

    private static LiveUInt32ReadResult ReadLiveUInt32(
        ProcessMemoryReader memory,
        uint ownerAddress,
        uint offset,
        bool ownerIsValid,
        string fieldName,
        List<string> errors)
    {
        if (!TryAdd(
                ownerAddress,
                offset,
                out var address) ||
            !memory.TryReadUInt32(
                address,
                out var value))
        {
            errors.Add(
                $"Could not read equipped-item Effects.{fieldName} at 0x{address:X8}");
            return new LiveUInt32ReadResult(
                address,
                null);
        }

        return new LiveUInt32ReadResult(
            address,
            ownerIsValid
                ? value
                : null);
    }

    private static LiveFloatReadResult ReadLiveFloat(
        ProcessMemoryReader memory,
        uint ownerAddress,
        uint offset,
        bool ownerIsValid,
        string fieldName,
        List<string> errors)
    {
        if (!TryAdd(
                ownerAddress,
                offset,
                out var address) ||
            !memory.TryReadBytes(
                address,
                sizeof(float),
                out var bytes))
        {
            errors.Add(
                $"Could not read equipped-item Effects.{fieldName} at 0x{address:X8}");
            return new LiveFloatReadResult(
                address,
                null);
        }

        var value = BitConverter.ToSingle(
            bytes,
            0);

        return new LiveFloatReadResult(
            address,
            ownerIsValid && float.IsFinite(value)
                ? value
                : null);
    }

    private static bool TryAdd(
        uint address,
        uint offset,
        out uint result)
    {
        try
        {
            result = checked(address + offset);
            return true;
        }
        catch (OverflowException)
        {
            result = 0;
            return false;
        }
    }

    private static string BuildStatus(
        ClientEquippedItemOperationalObservation observation,
        IReadOnlyCollection<string> errors)
    {
        if (errors.Count != 0)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"Available with {errors.Count} operational field read error(s): {errors.FirstOrDefault() ?? "Unknown read error"}");
        }

        if (!observation.IsOccupied)
        {
            return "Available; equipped slot is empty or unavailable";
        }

        if (!observation.HasItemState)
        {
            return "Available; item-state value is not valid";
        }

        if (observation.IsInPostDeadlineBusyTail)
        {
            return "Available; busy in post-deadline release tail";
        }

        if (observation.IsBusy)
        {
            var remaining =
                observation.NominalRemainingMilliseconds;

            return remaining.HasValue
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"Available; busy, nominally {remaining.Value} ms remaining")
                : "Available; busy, nominal deadline unavailable";
        }

        return "Available; operationally ready";
    }

    private static bool TryGetPropertyAddress(
        ClientAuxDataLookupSnapshot lookup,
        string propertyName,
        IDictionary<string, uint> propertyAddresses,
        out uint propertyAddress)
    {
        if (!lookup.Properties.TryGetValue(
                propertyName,
                out propertyAddress) ||
            propertyAddress == 0)
        {
            propertyAddress = 0;
            return false;
        }

        TrackPropertyAddress(
            propertyAddresses,
            propertyName,
            propertyAddress);

        return true;
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

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct ItemStateReadResult(
        uint PropertyAddress,
        uint ValidState,
        uint? Value);

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct ReadyTimeReadResult(
        uint PropertyAddress,
        uint ValidState,
        ulong? Value);

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct ClientFloatReadResult(
        uint PropertyAddress,
        float? Value);

    private readonly record struct LiveEffectsReadResult(
        uint EffectsObjectAddress,
        bool EffectsObjectIsValid,
        LiveFloatReadResult Range,
        LiveUInt32ReadResult Usage,
        LiveUInt32ReadResult Targets,
        LiveUInt32ReadResult Validity);

    private readonly record struct LiveUInt32ReadResult(
        uint Address,
        uint? Value);

    private readonly record struct LiveFloatReadResult(
        uint Address,
        float? Value);
}
