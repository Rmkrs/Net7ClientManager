namespace Net7ClientManager.Observations.Observers;

using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.InteropServices;
using Net7ClientManager.Observations.Models;

internal sealed class ClientBuffObserver
{
    private const uint ImageBase = 0x00400000;

    private const uint RemovalTimePropertyVTableRva =
        0x00aeb1ec - ImageBase;

    private const uint RemovalTimePropertyTypeDescriptor =
        0x00000005;

    private const uint PropertyTypeDescriptorOffset = 0x04;
    private const uint PropertyValidOffset = 0x70;
    private const uint UInt64PropertyLowWordOffset = 0x88;
    private const uint UInt64PropertyHighWordOffset = 0x8c;

    private const int RemovalTimePropertySnapshotLength = 0x90;
    private const int MaximumBuffSlotCount = 16;

    private static readonly string[] buffPropertySuffixes =
    [
        "BuffType",
        "ScrubTypeName",
        "IsPermanent",
        "BuffRemovalTime",
    ];

    internal static readonly string[] PropertyNames =
        BuildPropertyNames();

    private readonly ClientAuxDataLookupReader auxDataReader =
        new();

    public ClientBuffsObservation Observe(
        ProcessMemoryReader memory,
        uint moduleBaseAddress,
        uint clientTime,
        ClientAuxDataLookupSnapshot lookup)
    {
        uint expectedRemovalTimeVTable;

        try
        {
            expectedRemovalTimeVTable = checked(
                moduleBaseAddress +
                RemovalTimePropertyVTableRva);
        }
        catch (OverflowException)
        {
            return ClientBuffsObservation.Unavailable(
                "Buff removal-time vtable address calculation overflow",
                lookup.LookupAddress);
        }

        List<ClientBuffObservation> slots = [];
        List<string> errors = [];

        for (var slot = 0;
             slot < MaximumBuffSlotCount;
             slot++)
        {
            var observation = this.ObserveSlot(
                memory,
                clientTime,
                lookup,
                slot,
                expectedRemovalTimeVTable,
                out var slotErrors);

            slots.Add(observation);
            errors.AddRange(slotErrors);
        }

        var activeBuffCount = slots.Count(
            slot =>
                slot.IsOccupied);

        string status;

        if (slots.TrueForAll(
                slot =>
                    !slot.IsPresent))
        {
            status =
                "Local player exposes no promoted BuffArray fields";
        }
        else if (errors.Count == 0)
        {
            status = string.Create(
                CultureInfo.InvariantCulture,
                $"Available; {activeBuffCount}/{MaximumBuffSlotCount} active buff(s)");
        }
        else
        {
            status = string.Create(
                CultureInfo.InvariantCulture,
                $"Available with {errors.Count} buff field read error(s): {errors[0]}");
        }

        return new ClientBuffsObservation
        {
            IsAvailable = true,
            Status = status,
            AuxDataLookupAddress =
                lookup.LookupAddress,
            MaximumSlotCount =
                MaximumBuffSlotCount,
            Slots = slots,
        };
    }

    private ClientBuffObservation ObserveSlot(
        ProcessMemoryReader memory,
        uint clientTime,
        ClientAuxDataLookupSnapshot lookup,
        int slot,
        uint expectedRemovalTimeVTable,
        out IReadOnlyList<string> readErrors)
    {
        List<string> errors = [];

        var prefix = string.Create(
            CultureInfo.InvariantCulture,
            $"BuffArray.{slot}");

        lookup.Properties.TryGetValue(
            prefix,
            out var recordAddress);

        var buffType = this.ReadString(
            memory,
            lookup,
            $"{prefix}.BuffType",
            errors);

        var scrubTypeName = this.ReadString(
            memory,
            lookup,
            $"{prefix}.ScrubTypeName",
            errors);

        var isPermanent = this.ReadBoolean(
            memory,
            lookup,
            $"{prefix}.IsPermanent",
            errors);

        var removalTime = ReadRemovalTime(
            memory,
            lookup,
            $"{prefix}.BuffRemovalTime",
            expectedRemovalTimeVTable,
            errors);

        readErrors = errors;

        var isPresent =
            recordAddress != 0 ||
            buffType.PropertyAddress != 0 ||
            scrubTypeName.PropertyAddress != 0 ||
            isPermanent.PropertyAddress != 0 ||
            removalTime.PropertyAddress != 0;

        var observation =
            new ClientBuffObservation
            {
                Slot = slot,
                PropertyPrefix = prefix,
                IsPresent = isPresent,
                CurrentClientTime = clientTime,
                RecordAddress = recordAddress,
                BuffTypePropertyAddress =
                    buffType.PropertyAddress,
                BuffTypeValid =
                    buffType.IsValid,
                BuffType = buffType.IsValid
                    ? buffType.Value
                    : null,
                ScrubTypeNamePropertyAddress =
                    scrubTypeName.PropertyAddress,
                ScrubTypeNameValid =
                    scrubTypeName.IsValid,
                ScrubTypeName = scrubTypeName.IsValid
                    ? scrubTypeName.Value
                    : null,
                IsPermanentPropertyAddress =
                    isPermanent.PropertyAddress,
                IsPermanentValid =
                    isPermanent.IsValid,
                IsPermanent = isPermanent.IsValid
                    ? isPermanent.Value
                    : null,
                BuffRemovalTimePropertyAddress =
                    removalTime.PropertyAddress,
                BuffRemovalTimeValidState =
                    removalTime.ValidState,
                NominalRemovalAtClientTime =
                    removalTime.Value,
            };

        return observation with
        {
            Status = BuildStatus(
                observation,
                errors),
        };
    }

    private ClientStringAuxDataPropertySample ReadString(
        ProcessMemoryReader memory,
        ClientAuxDataLookupSnapshot lookup,
        string propertyName,
        List<string> errors)
    {
        if (!this.auxDataReader.TryReadStringProperty(
                memory,
                lookup,
                propertyName,
                out var sample,
                out var error))
        {
            errors.Add(error);
        }

        return sample;
    }

    private ClientBooleanAuxDataPropertySample ReadBoolean(
        ProcessMemoryReader memory,
        ClientAuxDataLookupSnapshot lookup,
        string propertyName,
        List<string> errors)
    {
        if (!this.auxDataReader.TryReadBooleanProperty(
                memory,
                lookup,
                propertyName,
                out var sample,
                out var error))
        {
            errors.Add(error);
        }

        return sample;
    }

    private static RemovalTimeReadResult ReadRemovalTime(
        ProcessMemoryReader memory,
        ClientAuxDataLookupSnapshot lookup,
        string propertyName,
        uint expectedVTable,
        List<string> errors)
    {
        if (!lookup.Properties.TryGetValue(
                propertyName,
                out var propertyAddress) ||
            propertyAddress == 0)
        {
            return default;
        }

        if (!memory.TryReadBytes(
                propertyAddress,
                RemovalTimePropertySnapshotLength,
                out var bytes))
        {
            errors.Add(
                    $"Could not read {propertyName} property at 0x{propertyAddress:X8}");

            return new RemovalTimeReadResult(
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
                RemovalTimePropertyTypeDescriptor)
        {
            errors.Add(
                    $"Unexpected {propertyName} type at 0x{propertyAddress:X8}: vtable=0x{actualVTable:X8}, descriptor=0x{actualTypeDescriptor:X8}; expected vtable=0x{expectedVTable:X8}, descriptor=0x{RemovalTimePropertyTypeDescriptor:X8}");

            return new RemovalTimeReadResult(
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
            return new RemovalTimeReadResult(
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

        return new RemovalTimeReadResult(
            propertyAddress,
            validState,
            value);
    }

    private static string BuildStatus(
        ClientBuffObservation observation,
        IReadOnlyCollection<string> errors)
    {
        if (errors.Count != 0)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"Available with {errors.Count} field read error(s): {errors.FirstOrDefault() ?? "Unknown read error"}");
        }

        if (!observation.IsPresent)
        {
            return "Buff slot is unavailable";
        }

        if (!observation.IsOccupied)
        {
            return "Available; empty buff slot";
        }

        if (observation.IsPermanent == true)
        {
            return "Available; permanent buff";
        }

        if (observation.NominalRemainingMilliseconds.HasValue)
        {
            return observation.IsNominallyExpired
                ? "Available; timed buff is nominally expired but remains occupied"
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"Available; timed buff, nominally {observation.NominalRemainingMilliseconds.Value} ms remaining");
        }

        return observation.IsPermanent == false
            ? "Available; timed buff with unavailable removal deadline"
            : "Available; buff permanence is unavailable";
    }

    private static string[] BuildPropertyNames()
    {
        List<string> names = [];

        for (var slot = 0;
             slot < MaximumBuffSlotCount;
             slot++)
        {
            var prefix = string.Create(
                CultureInfo.InvariantCulture,
                $"BuffArray.{slot}");

            names.Add(prefix);

            foreach (var suffix in buffPropertySuffixes)
            {
                names.Add(
                    $"{prefix}.{suffix}");
            }
        }

        return [.. names];
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct RemovalTimeReadResult(
        uint PropertyAddress,
        uint ValidState,
        ulong? Value);
}
