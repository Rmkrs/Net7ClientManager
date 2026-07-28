namespace Net7ClientManager.Observations.Observers;

internal sealed class ClientMapIdentityReader
{
    private const uint ImageBase = 0x00400000;

    private const uint StringAuxDataPropertyVTableStatic =
        0x00aeb0e4;

    private const uint StringAuxDataPropertyVTableRva =
        StringAuxDataPropertyVTableStatic - ImageBase;

    private const uint MapDisplayNameSeparatorStatic =
        0x00b6e948;

    private const uint MapDisplayNameSeparatorRva =
        MapDisplayNameSeparatorStatic - ImageBase;

    private const uint ObjectAuxDataNameProperty = 0xa0;
    private const uint AuxDataPropertyValid = 0x70;
    private const uint AuxDataPropertyValue = 0x84;

    private const int MaximumNameLength = 256;
    private const int MaximumSeparatorLength = 32;

    private static readonly string[] mapIdentityPropertyNames =
    [
        "Name",
        "Owner",
        "Title",
        "Rank",
    ];

    private readonly ClientAuxDataLookupReader auxDataReader =
        new();

    public string ReadMapDisplayNameSeparator(
        ProcessMemoryReader memory,
        uint moduleBaseAddress)
    {
        try
        {
            var separatorAddress = checked(
                moduleBaseAddress +
                MapDisplayNameSeparatorRva);

            if (memory.TryReadNullTerminatedLatin1String(
                    separatorAddress,
                    MaximumSeparatorLength,
                    out var separator))
            {
                return separator;
            }
        }
        catch (OverflowException)
        {
        }

        return " ";
    }

    public ClientMapIdentityReadResult Read(
        ProcessMemoryReader memory,
        uint moduleBaseAddress,
        uint auxDataAddress,
        byte rawObjectType,
        string separator)
    {
        if (auxDataAddress == 0)
        {
            return BuildMapIdentity(
                rawObjectType,
                new StringPropertyReadResult(IsPresent: false, IsValid: false, ""),
                new StringPropertyReadResult(IsPresent: false, IsValid: false, ""),
                new StringPropertyReadResult(IsPresent: false, IsValid: false, ""),
                new StringPropertyReadResult(IsPresent: false, IsValid: false, ""),
                separator,
                "ObjectAuxData pointer was null");
        }

        if (!this.auxDataReader.TryOpenAvailableFromPropertyVector(
                memory,
                moduleBaseAddress,
                auxDataAddress,
                mapIdentityPropertyNames,
                out var lookup,
                out var openError))
        {
            var fallbackName = ReadDirectName(
                memory,
                moduleBaseAddress,
                auxDataAddress);

            return BuildMapIdentity(
                rawObjectType,
                new StringPropertyReadResult(
                    IsPresent: true,
                    !string.IsNullOrWhiteSpace(
                        fallbackName.Name),
                    fallbackName.Name),
                new StringPropertyReadResult(IsPresent: false, IsValid: false, ""),
                new StringPropertyReadResult(IsPresent: false, IsValid: false, ""),
                new StringPropertyReadResult(IsPresent: false, IsValid: false, ""),
                separator,
                    $"AuxData property-vector lookup failed: {openError}; direct Name fallback: {fallbackName.Status}");
        }

        List<string> errors = [];

        var name = ReadString("Name");
        var owner = ReadString("Owner");
        var title = ReadString("Title");
        var rank = ReadString("Rank");

        return BuildMapIdentity(
            rawObjectType,
            name,
            owner,
            title,
            rank,
            separator,
            errors.Count == 0
                ? "Available"
                : string.Join("; ", errors));

        StringPropertyReadResult ReadString(
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
                return new StringPropertyReadResult(IsPresent: false, IsValid: false, "");
            }

            return new StringPropertyReadResult(
                sample.PropertyAddress != 0,
                sample.IsValid,
                sample.Value);
        }
    }

    private static ClientMapIdentityReadResult BuildMapIdentity(
        byte rawObjectType,
        StringPropertyReadResult name,
        StringPropertyReadResult owner,
        StringPropertyReadResult title,
        StringPropertyReadResult rank,
        string separator,
        string status)
    {
        var baseName = "Unknown";
        var baseSource = "Unknown fallback";

        if (rawObjectType == 0x01)
        {
            if (owner.IsPresentAndValid)
            {
                baseName = owner.Value;
                baseSource = "Owner";
            }
            else if (name.IsPresentAndValid)
            {
                baseName = name.Value;
                baseSource = "Name";
            }
        }
        else if (name.IsPresentAndValid)
        {
            baseName = name.Value;
            baseSource = "Name";
        }
        else if (owner.IsPresentAndValid)
        {
            baseName = owner.Value;
            baseSource = "Owner";
        }

        if (rawObjectType == 0x00)
        {
            return new ClientMapIdentityReadResult(
                name.Value,
                owner.Value,
                title.Value,
                rank.Value,
                baseName,
                baseSource,
                status);
        }

        if (title.IsPresentAndValid &&
            !string.IsNullOrEmpty(title.Value))
        {
            return new ClientMapIdentityReadResult(
                name.Value,
                owner.Value,
                title.Value,
                rank.Value,
                string.Concat(
                    title.Value,
                    separator,
                    baseName),
                string.Concat(
                    "Title + ",
                    baseSource),
                status);
        }

        if (rank.IsPresentAndValid &&
            !string.IsNullOrEmpty(rank.Value))
        {
            return new ClientMapIdentityReadResult(
                name.Value,
                owner.Value,
                title.Value,
                rank.Value,
                string.Concat(
                    rank.Value,
                    separator,
                    baseName),
                string.Concat(
                    "Rank + ",
                    baseSource),
                status);
        }

        return new ClientMapIdentityReadResult(
            name.Value,
            owner.Value,
            title.Value,
            rank.Value,
            baseName,
            baseSource,
            status);
    }

    private static DirectNameReadResult ReadDirectName(
        ProcessMemoryReader memory,
        uint moduleBaseAddress,
        uint auxDataAddress)
    {
        var namePropertyAddress = checked(
            auxDataAddress +
            ObjectAuxDataNameProperty);

        if (!memory.TryReadUInt32(
                namePropertyAddress,
                out var namePropertyVTable))
        {
            return new DirectNameReadResult(
                "",
                    $"Could not read direct Name-property vtable at 0x{namePropertyAddress:X8}");
        }

        var expectedNamePropertyVTable = checked(
            moduleBaseAddress +
            StringAuxDataPropertyVTableRva);

        if (namePropertyVTable !=
            expectedNamePropertyVTable)
        {
            return new DirectNameReadResult(
                "",
                    $"Unexpected direct Name-property vtable 0x{namePropertyVTable:X8}");
        }

        if (!memory.TryReadUInt32(
                checked(
                    namePropertyAddress +
                    AuxDataPropertyValid),
                out var validRaw) ||
            validRaw == 0)
        {
            return new DirectNameReadResult(
                "",
                "Direct Name property was unavailable or invalid");
        }

        if (!memory.TryReadUInt32(
                checked(
                    namePropertyAddress +
                    AuxDataPropertyValue),
                out var nameAddress) ||
            nameAddress == 0)
        {
            return new DirectNameReadResult(
                "",
                "Direct Name pointer was null or unreadable");
        }

        if (!memory.TryReadNullTerminatedLatin1String(
                nameAddress,
                MaximumNameLength,
                out var name))
        {
            return new DirectNameReadResult(
                "",
                    $"Could not read direct Name at 0x{nameAddress:X8}");
        }

        return new DirectNameReadResult(
            name,
            string.IsNullOrWhiteSpace(name)
                ? "Direct Name was empty"
                : "Available");
    }

    private readonly record struct StringPropertyReadResult(
        bool IsPresent,
        bool IsValid,
        string Value)
    {
        public bool IsPresentAndValid =>
            this.IsPresent && this.IsValid;
    }

    private readonly record struct DirectNameReadResult(
        string Name,
        string Status);
}
