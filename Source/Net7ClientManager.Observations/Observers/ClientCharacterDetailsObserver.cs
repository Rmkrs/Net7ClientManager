namespace Net7ClientManager.Observations.Observers;

using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.InteropServices;
using Net7ClientManager.Observations.Models;

internal sealed class ClientCharacterDetailsObserver
{
    private const uint ImageBase = 0x00400000;

    private const uint UInt64PropertyVTableRva =
        0x00aeafb8 - ImageBase;

    private const uint UInt64PropertyTypeDescriptorRva =
        0x00be3a94 - ImageBase;

    private const uint Int32PropertyVTableRva =
        0x00b00d34 - ImageBase;

    private const uint Int32PropertyTypeDescriptorRva =
        0x00c416a8 - ImageBase;

    private const uint StringPropertyVTableRva =
        0x00aeb0e4 - ImageBase;

    private const uint MoneyPropertyOffset = 0x00b0;
    private const uint ExperienceDebtPropertyOffset = 0x0140;
    private const uint RegistrationStarbasePropertyOffset = 0x1184;
    private const uint RegistrationStarbaseSectorPropertyOffset = 0x120c;

    private const uint PropertyTypeDescriptorOffset = 0x04;
    private const uint PropertyValidOffset = 0x70;
    private const uint OrdinaryPropertyValueOffset = 0x84;
    private const uint UInt64PropertyLowWordOffset = 0x88;
    private const uint UInt64PropertyHighWordOffset = 0x8c;

    private const int UInt64PropertySnapshotLength = 0x90;
    private const int MaximumStringLength = 512;

    public ClientCharacterDetailsObservation Observe(
        ProcessMemoryReader memory,
        uint moduleBaseAddress,
        uint hullAuxDataAddress)
    {
        if (hullAuxDataAddress == 0)
        {
            return ClientCharacterDetailsObservation.Unavailable(
                "SClient Hull AuxData (+0x12C0) is unavailable");
        }

        if (!TryResolveExpectedTypes(
                memory,
                moduleBaseAddress,
                out var expectedTypes,
                out var typeError))
        {
            return ClientCharacterDetailsObservation.Unavailable(
                typeError,
                hullAuxDataAddress);
        }

        if (!TryAdd(
                hullAuxDataAddress,
                MoneyPropertyOffset,
                out var moneyPropertyAddress) ||
            !TryAdd(
                hullAuxDataAddress,
                ExperienceDebtPropertyOffset,
                out var experienceDebtPropertyAddress) ||
            !TryAdd(
                hullAuxDataAddress,
                RegistrationStarbasePropertyOffset,
                out var registrationStarbasePropertyAddress) ||
            !TryAdd(
                hullAuxDataAddress,
                RegistrationStarbaseSectorPropertyOffset,
                out var registrationStarbaseSectorPropertyAddress))
        {
            return ClientCharacterDetailsObservation.Unavailable(
                "Character-details property address calculation overflow",
                hullAuxDataAddress);
        }

        List<string> errors = [];

        var money = ReadUInt64Property(
            memory,
            moneyPropertyAddress,
            expectedTypes.UInt64VTable,
            expectedTypes.UInt64TypeDescriptor,
            "Hull.Money",
            errors);

        var experienceDebt = ReadInt32Property(
            memory,
            experienceDebtPropertyAddress,
            expectedTypes.Int32VTable,
            expectedTypes.Int32TypeDescriptor,
            "Hull.XPDebt",
            errors);

        var registrationStarbase = ReadStringProperty(
            memory,
            registrationStarbasePropertyAddress,
            expectedTypes.StringVTable,
            "Hull.RegistrationStarbase",
            errors);

        var registrationStarbaseSector = ReadStringProperty(
            memory,
            registrationStarbaseSectorPropertyAddress,
            expectedTypes.StringVTable,
            "Hull.RegistrationStarbaseSector",
            errors);

        var validValueCount =
            (money.Value.HasValue ? 1 : 0) +
            (experienceDebt.Value.HasValue ? 1 : 0) +
            (registrationStarbase.IsValid ? 1 : 0) +
            (registrationStarbaseSector.IsValid ? 1 : 0);

        var status = validValueCount switch
        {
            4 when errors.Count == 0 =>
                "Available; credits, XP debt, and registration are valid",

            > 0 when errors.Count == 0 =>
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Partially available; {validValueCount}/4 values are valid"),

            > 0 =>
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Partially available; {validValueCount}/4 values are valid; {errors[0]}"),

            _ when errors.Count > 0 =>
                $"Unavailable: {errors[0]}",

            _ =>
                "Character details are present but not valid",
        };

        return new ClientCharacterDetailsObservation
        {
            IsAvailable =
                validValueCount > 0,
            Status = status,
            HullAuxDataAddress =
                hullAuxDataAddress,
            MoneyPropertyAddress =
                moneyPropertyAddress,
            MoneyValidState =
                money.ValidState,
            Credits =
                money.Value,
            ExperienceDebtPropertyAddress =
                experienceDebtPropertyAddress,
            ExperienceDebtValidState =
                experienceDebt.ValidState,
            ExperienceDebt =
                experienceDebt.Value,
            RegistrationStarbasePropertyAddress =
                registrationStarbasePropertyAddress,
            RegistrationStarbaseValidState =
                registrationStarbase.ValidState,
            RegistrationStarbaseStringAddress =
                registrationStarbase.StringAddress,
            RegistrationStarbase =
                registrationStarbase.Value ?? "",
            RegistrationStarbaseSectorPropertyAddress =
                registrationStarbaseSectorPropertyAddress,
            RegistrationStarbaseSectorValidState =
                registrationStarbaseSector.ValidState,
            RegistrationStarbaseSectorStringAddress =
                registrationStarbaseSector.StringAddress,
            RegistrationStarbaseSector =
                registrationStarbaseSector.Value ?? "",
        };
    }

    private static UInt64PropertySample ReadUInt64Property(
        ProcessMemoryReader memory,
        uint propertyAddress,
        uint expectedVTable,
        uint expectedTypeDescriptor,
        string propertyName,
        ICollection<string> errors)
    {
        if (!memory.TryReadBytes(
                propertyAddress,
                UInt64PropertySnapshotLength,
                out var bytes))
        {
            errors.Add(
                    $"Could not read {propertyName} at 0x{propertyAddress:X8}");

            return default;
        }

        if (!ValidatePropertyHeader(
                bytes,
                propertyAddress,
                expectedVTable,
                expectedTypeDescriptor,
                propertyName,
                errors))
        {
            return default;
        }

        var validState = ReadUInt32(
            bytes,
            PropertyValidOffset);

        if (validState == 0)
        {
            return new UInt64PropertySample(
                validState,
                Value: null);
        }

        var lowWord = ReadUInt32(
            bytes,
            UInt64PropertyLowWordOffset);

        var highWord = ReadUInt32(
            bytes,
            UInt64PropertyHighWordOffset);

        return new UInt64PropertySample(
            validState,
            ((ulong)highWord << 32) |
            lowWord);
    }

    private static Int32PropertySample ReadInt32Property(
        ProcessMemoryReader memory,
        uint propertyAddress,
        uint expectedVTable,
        uint expectedTypeDescriptor,
        string propertyName,
        ICollection<string> errors)
    {
        if (!TryValidatePropertyHeader(
                memory,
                propertyAddress,
                expectedVTable,
                expectedTypeDescriptor,
                propertyName,
                errors))
        {
            return default;
        }

        if (!memory.TryReadUInt32(
                propertyAddress + PropertyValidOffset,
                out var validState))
        {
            errors.Add(
                    $"Could not read {propertyName} validity at 0x{propertyAddress + PropertyValidOffset:X8}");

            return default;
        }

        if (validState == 0)
        {
            return new Int32PropertySample(
                validState,
                Value: null);
        }

        if (!memory.TryReadUInt32(
                propertyAddress + OrdinaryPropertyValueOffset,
                out var rawValue))
        {
            errors.Add(
                    $"Could not read {propertyName} value at 0x{propertyAddress + OrdinaryPropertyValueOffset:X8}");

            return new Int32PropertySample(
                validState,
                Value: null);
        }

        return new Int32PropertySample(
            validState,
            unchecked((int)rawValue));
    }

    private static StringPropertySample ReadStringProperty(
        ProcessMemoryReader memory,
        uint propertyAddress,
        uint expectedVTable,
        string propertyName,
        ICollection<string> errors)
    {
        if (!TryValidatePropertyVTable(
                memory,
                propertyAddress,
                expectedVTable,
                propertyName,
                errors))
        {
            return default;
        }

        if (!memory.TryReadUInt32(
                propertyAddress + PropertyValidOffset,
                out var validState) ||
            !memory.TryReadUInt32(
                propertyAddress + OrdinaryPropertyValueOffset,
                out var stringAddress))
        {
            errors.Add(
                    $"Could not read {propertyName} string state at 0x{propertyAddress:X8}");

            return default;
        }

        if (validState == 0 ||
            stringAddress == 0)
        {
            return new StringPropertySample(
                ReadSucceeded: true,
                validState,
                stringAddress,
                "");
        }

        if (!memory.TryReadNullTerminatedLatin1String(
                stringAddress,
                MaximumStringLength,
                out var value))
        {
            errors.Add(
                    $"Could not read {propertyName} string at 0x{stringAddress:X8}");

            return new StringPropertySample(
                ReadSucceeded: false,
                validState,
                stringAddress,
                Value: null);
        }

        return new StringPropertySample(
            ReadSucceeded: true,
            validState,
            stringAddress,
            value);
    }

    private static bool TryResolveExpectedTypes(
        ProcessMemoryReader memory,
        uint moduleBaseAddress,
        out ExpectedTypes expectedTypes,
        out string error)
    {
        expectedTypes = default;
        error = "";

        try
        {
            var uint64VTable = checked(
                moduleBaseAddress +
                UInt64PropertyVTableRva);

            var int32VTable = checked(
                moduleBaseAddress +
                Int32PropertyVTableRva);

            var stringVTable = checked(
                moduleBaseAddress +
                StringPropertyVTableRva);

            if (!memory.TryReadUInt32(
                    checked(
                        moduleBaseAddress +
                        UInt64PropertyTypeDescriptorRva),
                    out var uint64TypeDescriptor) ||
                !memory.TryReadUInt32(
                    checked(
                        moduleBaseAddress +
                        Int32PropertyTypeDescriptorRva),
                    out var int32TypeDescriptor))
            {
                error =
                    "Could not read character-details AuxData type descriptors";

                return false;
            }

            expectedTypes = new ExpectedTypes(
                uint64VTable,
                uint64TypeDescriptor,
                int32VTable,
                int32TypeDescriptor,
                stringVTable);

            return true;
        }
        catch (OverflowException)
        {
            error =
                "Character-details type address calculation overflow";

            return false;
        }
    }

    private static bool TryValidatePropertyHeader(
        ProcessMemoryReader memory,
        uint propertyAddress,
        uint expectedVTable,
        uint expectedTypeDescriptor,
        string propertyName,
        ICollection<string> errors)
    {
        if (!TryValidatePropertyVTable(
                memory,
                propertyAddress,
                expectedVTable,
                propertyName,
                errors))
        {
            return false;
        }

        if (!memory.TryReadUInt32(
                propertyAddress + PropertyTypeDescriptorOffset,
                out var actualTypeDescriptor))
        {
            errors.Add(
                    $"Could not read {propertyName} type descriptor at 0x{propertyAddress + PropertyTypeDescriptorOffset:X8}");

            return false;
        }

        if (actualTypeDescriptor != expectedTypeDescriptor)
        {
            errors.Add(
                    $"Unexpected {propertyName} type descriptor 0x{actualTypeDescriptor:X8}; expected 0x{expectedTypeDescriptor:X8}");

            return false;
        }

        return true;
    }

    private static bool TryValidatePropertyVTable(
        ProcessMemoryReader memory,
        uint propertyAddress,
        uint expectedVTable,
        string propertyName,
        ICollection<string> errors)
    {
        if (!memory.TryReadUInt32(
                propertyAddress,
                out var actualVTable))
        {
            errors.Add(
                    $"Could not read {propertyName} vtable at 0x{propertyAddress:X8}");

            return false;
        }

        if (actualVTable != expectedVTable)
        {
            errors.Add(
                    $"Unexpected {propertyName} vtable 0x{actualVTable:X8}; expected 0x{expectedVTable:X8}");

            return false;
        }

        return true;
    }

    private static bool ValidatePropertyHeader(
        byte[] bytes,
        uint propertyAddress,
        uint expectedVTable,
        uint expectedTypeDescriptor,
        string propertyName,
        ICollection<string> errors)
    {
        var actualVTable = ReadUInt32(
            bytes,
            0);

        var actualTypeDescriptor = ReadUInt32(
            bytes,
            PropertyTypeDescriptorOffset);

        if (actualVTable != expectedVTable)
        {
            errors.Add(
                    $"Unexpected {propertyName} vtable 0x{actualVTable:X8} at 0x{propertyAddress:X8}; expected 0x{expectedVTable:X8}");

            return false;
        }

        if (actualTypeDescriptor != expectedTypeDescriptor)
        {
            errors.Add(
                    $"Unexpected {propertyName} type descriptor 0x{actualTypeDescriptor:X8}; expected 0x{expectedTypeDescriptor:X8}");

            return false;
        }

        return true;
    }

    private static uint ReadUInt32(
        byte[] bytes,
        uint offset)
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(
            bytes.AsSpan(
                checked((int)offset),
                sizeof(uint)));
    }

    private static bool TryAdd(
        uint left,
        uint right,
        out uint result)
    {
        try
        {
            result = checked(
                left + right);

            return true;
        }
        catch (OverflowException)
        {
            result = 0;
            return false;
        }
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct ExpectedTypes(
        uint UInt64VTable,
        uint UInt64TypeDescriptor,
        uint Int32VTable,
        uint Int32TypeDescriptor,
        uint StringVTable);

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct UInt64PropertySample(
        uint ValidState,
        ulong? Value);

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct Int32PropertySample(
        uint ValidState,
        int? Value);

    private readonly record struct StringPropertySample(
        bool ReadSucceeded,
        uint ValidState,
        uint StringAddress,
        string? Value)
    {
        public bool IsValid =>
            this.ReadSucceeded &&
            this.ValidState != 0;
    }
}
