namespace Net7ClientManager.Observations.Observers;

using System.Globalization;
using System.Text;

internal sealed class ClientAuxDataLookupReader
{
    private const uint ImageBase = 0x00400000;

    private const uint AuxDataTreeNilSentinelStatic =
        0x00be3a84;

    private const uint AuxDataTreeNilSentinelRva =
        AuxDataTreeNilSentinelStatic - ImageBase;

    private const uint FloatAuxDataPropertyTypeDescriptorStatic =
        0x00c416f0;

    private const uint FloatAuxDataPropertyTypeDescriptorRva =
        FloatAuxDataPropertyTypeDescriptorStatic - ImageBase;

    private const uint DeltaInterpolatedFloatPropertyTypeDescriptorStatic =
        0x00be3a74;

    private const uint DeltaInterpolatedFloatPropertyTypeDescriptorRva =
        DeltaInterpolatedFloatPropertyTypeDescriptorStatic - ImageBase;

    private const uint BooleanAuxDataPropertyTypeDescriptorStatic =
        0x00c416ac;

    private const uint BooleanAuxDataPropertyTypeDescriptorRva =
        BooleanAuxDataPropertyTypeDescriptorStatic - ImageBase;

    private const uint Int32AuxDataPropertyTypeDescriptorStatic =
        0x00c416a8;

    private const uint Int32AuxDataPropertyTypeDescriptorRva =
        Int32AuxDataPropertyTypeDescriptorStatic - ImageBase;

    private const uint StringAuxDataPropertyVTableStatic =
        0x00aeb0e4;

    private const uint StringAuxDataPropertyVTableRva =
        StringAuxDataPropertyVTableStatic - ImageBase;

    // ConstructAuxDataName -> InternCString uses this process-global
    // hash table to canonicalize AuxData names. The AuxData tree is keyed
    // by the resulting canonical C-string pointer, not by lexical text.
    private const uint AuxDataInternBucketTableStatic =
        0x00be3b30;

    private const uint AuxDataInternBucketTableRva =
        AuxDataInternBucketTableStatic - ImageBase;

    private const uint AuxDataInternBucketCountStatic =
        0x00be3b34;

    private const uint AuxDataInternBucketCountRva =
        AuxDataInternBucketCountStatic - ImageBase;

    private const uint ObjectAuxDataPropertyVectorBegin = 0x88;
    private const uint ObjectAuxDataPropertyVectorEnd = 0x8c;
    private const uint ObjectAuxDataPropertyVectorCapacity = 0x90;
    private const uint ObjectAuxDataLookup = 0x94;

    private const uint AuxDataLookupTreeHeader = 0x14;
    private const uint AuxDataTreeHeaderRoot = 0x04;

    private const uint AuxDataTreeNodeLeft = 0x00;
    private const uint AuxDataTreeNodeRight = 0x08;
    private const uint AuxDataTreeNodeName = 0x0c;
    private const uint AuxDataTreeNodeProperty = 0x10;

    private const uint AuxDataInternNodeString = 0x00;
    private const uint AuxDataInternNodeHash = 0x04;
    private const uint AuxDataInternNodeNext = 0x08;
    private const int AuxDataInternNodeSize = 0x0c;
    private const int AuxDataTreeNodeSnapshotSize = 0x14;

    private const uint AuxDataPropertyTypeDescriptor = 0x04;
    private const uint AuxDataPropertyShortName = 0x08;
    private const uint AuxDataPropertyQualifiedName = 0x3c;
    private const uint AuxDataPropertyValid = 0x70;
    private const uint FloatAuxDataPropertyValue = 0x84;
    private const uint AuxDataPropertySecondaryValue = 0x88;

    private const uint DeltaFloatTransitionEndTimeLow = 0x128;
    private const uint DeltaFloatTransitionEndTimeHigh = 0x12c;
    private const uint DeltaFloatInterpolationRate = 0x1b4;
    private const uint DeltaFloatTransitionBaseValue = 0x23c;
    private const uint DeltaFloatLastEvaluatedTimeLow = 0x240;
    private const uint DeltaFloatLastEvaluatedTimeHigh = 0x244;
    private const uint DeltaFloatCachedValue = 0x248;
    private const uint DeltaFloatInterpolationComplete = 0x24c;

    private const int MaximumAuxDataTreeNodeCount = 65536;
    private const int MaximumAuxDataPropertyVectorCount = 65536;
    private const int MaximumAuxDataOrderedLookupDepth = 256;
    private const int MaximumAuxDataInternChainLength = 4096;
    private const uint MaximumAuxDataInternBucketCount = 1_048_576;
    private const int MaximumAuxDataNameLength = 128;
    private const int MaximumAuxDataStringLength = 512;
    private const int MaximumDeltaFloatReadAttempts = 3;

    public bool TryOpen(
        ProcessMemoryReader memory,
        uint moduleBaseAddress,
        uint targetAuxDataAddress,
        IReadOnlyCollection<string> propertyNames,
        out ClientAuxDataLookupSnapshot snapshot,
        out string error)
    {
        snapshot = default;
        error = "";

        if (targetAuxDataAddress == 0)
        {
            error = "Target ObjectAuxData address is zero";
            return false;
        }

        try
        {
            var lookupPointerAddress = checked(
                targetAuxDataAddress +
                ObjectAuxDataLookup);

            if (!memory.TryReadUInt32(
                    lookupPointerAddress,
                    out var lookupAddress) ||
                lookupAddress == 0)
            {
                error = $"Target AuxData lookup is unavailable at 0x{lookupPointerAddress:X8}";

                return false;
            }

            if (!TryReadRuntimeValue(
                    memory,
                    moduleBaseAddress,
                    AuxDataTreeNilSentinelRva,
                    "AuxData tree NIL sentinel",
                    out var nilSentinelAddress,
                    out error) ||
                !TryReadRuntimeValue(
                    memory,
                    moduleBaseAddress,
                    FloatAuxDataPropertyTypeDescriptorRva,
                    "float AuxData property type descriptor",
                    out var floatPropertyTypeDescriptor,
                    out error) ||
                !TryReadRuntimeValue(
                    memory,
                    moduleBaseAddress,
                    DeltaInterpolatedFloatPropertyTypeDescriptorRva,
                    "delta-interpolated float property type descriptor",
                    out var deltaFloatPropertyTypeDescriptor,
                    out error) ||
                !TryReadRuntimeValue(
                    memory,
                    moduleBaseAddress,
                    BooleanAuxDataPropertyTypeDescriptorRva,
                    "boolean AuxData property type descriptor",
                    out var booleanPropertyTypeDescriptor,
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

            if (!this.TryFindProperties(
                    memory,
                    lookupAddress,
                    nilSentinelAddress,
                    propertyNames,
                    out var properties,
                    out error))
            {
                return false;
            }

            snapshot = new ClientAuxDataLookupSnapshot(
                lookupAddress,
                floatPropertyTypeDescriptor,
                deltaFloatPropertyTypeDescriptor,
                booleanPropertyTypeDescriptor,
                int32PropertyTypeDescriptor,
                checked(
                    moduleBaseAddress +
                    StringAuxDataPropertyVTableRva),
                properties);

            return true;
        }
        catch (OverflowException)
        {
            error =
                "Target AuxData lookup address calculation overflow";

            return false;
        }
    }

    public bool TryOpenTargeted(
        ProcessMemoryReader memory,
        uint moduleBaseAddress,
        uint targetAuxDataAddress,
        IReadOnlyCollection<string> propertyNames,
        out ClientAuxDataLookupSnapshot snapshot,
        out int traversalNodeCount,
        out string error)
    {
        snapshot = default;
        traversalNodeCount = 0;
        error = "";

        if (targetAuxDataAddress == 0)
        {
            error = "Target ObjectAuxData address is zero";
            return false;
        }

        try
        {
            var lookupPointerAddress = checked(
                targetAuxDataAddress +
                ObjectAuxDataLookup);

            if (!memory.TryReadUInt32(
                    lookupPointerAddress,
                    out var lookupAddress) ||
                lookupAddress == 0)
            {
                error = $"Target AuxData lookup is unavailable at 0x{lookupPointerAddress:X8}";

                return false;
            }

            if (!TryReadRuntimeValue(
                    memory,
                    moduleBaseAddress,
                    AuxDataTreeNilSentinelRva,
                    "AuxData tree NIL sentinel",
                    out var nilSentinelAddress,
                    out error) ||
                !TryReadRuntimeValue(
                    memory,
                    moduleBaseAddress,
                    AuxDataInternBucketTableRva,
                    "AuxData intern bucket table",
                    out var internBucketTableAddress,
                    out error) ||
                !TryReadRuntimeValue(
                    memory,
                    moduleBaseAddress,
                    AuxDataInternBucketCountRva,
                    "AuxData intern bucket count",
                    out var internBucketCount,
                    out error) ||
                !TryReadRuntimeValue(
                    memory,
                    moduleBaseAddress,
                    FloatAuxDataPropertyTypeDescriptorRva,
                    "float AuxData property type descriptor",
                    out var floatPropertyTypeDescriptor,
                    out error) ||
                !TryReadRuntimeValue(
                    memory,
                    moduleBaseAddress,
                    DeltaInterpolatedFloatPropertyTypeDescriptorRva,
                    "delta-interpolated float property type descriptor",
                    out var deltaFloatPropertyTypeDescriptor,
                    out error) ||
                !TryReadRuntimeValue(
                    memory,
                    moduleBaseAddress,
                    BooleanAuxDataPropertyTypeDescriptorRva,
                    "boolean AuxData property type descriptor",
                    out var booleanPropertyTypeDescriptor,
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

            if (!this.TryFindPropertiesByCanonicalNamePointer(
                    memory,
                    lookupAddress,
                    nilSentinelAddress,
                    internBucketTableAddress,
                    internBucketCount,
                    propertyNames,
                    out var properties,
                    out traversalNodeCount,
                    out error))
            {
                return false;
            }

            snapshot = new ClientAuxDataLookupSnapshot(
                lookupAddress,
                floatPropertyTypeDescriptor,
                deltaFloatPropertyTypeDescriptor,
                booleanPropertyTypeDescriptor,
                int32PropertyTypeDescriptor,
                checked(
                    moduleBaseAddress +
                    StringAuxDataPropertyVTableRva),
                properties);

            return true;
        }
        catch (OverflowException)
        {
            error =
                "Target AuxData lookup address calculation overflow";

            return false;
        }
    }

    public bool TryOpenFromPropertyVector(
        ProcessMemoryReader memory,
        uint moduleBaseAddress,
        uint targetAuxDataAddress,
        IReadOnlyCollection<string> propertyNames,
        out ClientAuxDataLookupSnapshot snapshot,
        out string error)
    {
        return this.TryOpenFromPropertyVectorCore(
            memory,
            moduleBaseAddress,
            targetAuxDataAddress,
            propertyNames,
            requireAllRequestedProperties: true,
            out snapshot,
            out error);
    }

    public bool TryOpenAvailableFromPropertyVector(
        ProcessMemoryReader memory,
        uint moduleBaseAddress,
        uint targetAuxDataAddress,
        IReadOnlyCollection<string> propertyNames,
        out ClientAuxDataLookupSnapshot snapshot,
        out string error)
    {
        return this.TryOpenFromPropertyVectorCore(
            memory,
            moduleBaseAddress,
            targetAuxDataAddress,
            propertyNames,
            requireAllRequestedProperties: false,
            out snapshot,
            out error);
    }

    private bool TryOpenFromPropertyVectorCore(
        ProcessMemoryReader memory,
        uint moduleBaseAddress,
        uint targetAuxDataAddress,
        IReadOnlyCollection<string> propertyNames,
        bool requireAllRequestedProperties,
        out ClientAuxDataLookupSnapshot snapshot,
        out string error)
    {
        snapshot = default;
        error = "";

        if (targetAuxDataAddress == 0)
        {
            error = "Target ObjectAuxData address is zero";
            return false;
        }

        try
        {
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
                    DeltaInterpolatedFloatPropertyTypeDescriptorRva,
                    "delta-interpolated float property type descriptor",
                    out var deltaFloatPropertyTypeDescriptor,
                    out error) ||
                !TryReadRuntimeValue(
                    memory,
                    moduleBaseAddress,
                    BooleanAuxDataPropertyTypeDescriptorRva,
                    "boolean AuxData property type descriptor",
                    out var booleanPropertyTypeDescriptor,
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

            if (!this.TryFindPropertiesInPropertyVector(
                    memory,
                    targetAuxDataAddress,
                    propertyNames,
                    requireAllRequestedProperties,
                    out var propertyVectorAddress,
                    out var properties,
                    out error))
            {
                return false;
            }

            memory.TryReadUInt32(
                checked(
                    targetAuxDataAddress +
                    ObjectAuxDataLookup),
                out var lookupAddress);

            snapshot = new ClientAuxDataLookupSnapshot(
                lookupAddress != 0
                    ? lookupAddress
                    : propertyVectorAddress,
                floatPropertyTypeDescriptor,
                deltaFloatPropertyTypeDescriptor,
                booleanPropertyTypeDescriptor,
                int32PropertyTypeDescriptor,
                checked(
                    moduleBaseAddress +
                    StringAuxDataPropertyVTableRva),
                properties);

            return true;
        }
        catch (OverflowException)
        {
            error =
                "Target AuxData property-vector address calculation overflow";

            return false;
        }
    }

    public bool TryReadFloatProperty(
        ProcessMemoryReader memory,
        ClientAuxDataLookupSnapshot snapshot,
        string propertyName,
        out ClientFloatAuxDataPropertySample sample,
        out string error)
    {
        sample = default;
        error = "";

        if (!snapshot.Properties.TryGetValue(
                propertyName,
                out var propertyAddress) ||
            propertyAddress == 0)
        {
            return true;
        }

        if (!this.TryValidatePropertyType(
                memory,
                propertyAddress,
                snapshot.FloatPropertyTypeDescriptor,
                propertyName,
                out error))
        {
            return false;
        }

        if (!memory.TryReadUInt32(
                propertyAddress +
                AuxDataPropertyValid,
                out var validValue))
        {
            error = $"Could not read {propertyName} validity at 0x{propertyAddress + AuxDataPropertyValid:X8}";

            return false;
        }

        if (validValue == 0)
        {
            sample = new ClientFloatAuxDataPropertySample(
                propertyAddress,
                IsValid: false,
                0.0f);

            return true;
        }

        if (!TryReadSingle(
                memory,
                propertyAddress +
                FloatAuxDataPropertyValue,
                out var value))
        {
            error = $"Could not read {propertyName} value at 0x{propertyAddress + FloatAuxDataPropertyValue:X8}";

            return false;
        }

        if (!float.IsFinite(value))
        {
            error = string.Create(
                CultureInfo.InvariantCulture,
$"{propertyName} contained non-finite value {value} at 0x{propertyAddress + FloatAuxDataPropertyValue:X8}");

            return false;
        }

        sample = new ClientFloatAuxDataPropertySample(
            propertyAddress,
            IsValid: true,
            value);

        return true;
    }

    public bool TryReadDeltaInterpolatedFloatProperty(
        ProcessMemoryReader memory,
        ClientAuxDataLookupSnapshot snapshot,
        string propertyName,
        uint clientTime,
        out ClientDeltaFloatAuxDataPropertySample sample,
        out string error)
    {
        sample = default;
        error = "";

        if (!snapshot.Properties.TryGetValue(
                propertyName,
                out var propertyAddress) ||
            propertyAddress == 0)
        {
            return true;
        }

        if (!this.TryValidatePropertyType(
                memory,
                propertyAddress,
                snapshot.DeltaFloatPropertyTypeDescriptor,
                propertyName,
                out error))
        {
            return false;
        }

        if (!memory.TryReadUInt32(
                propertyAddress +
                AuxDataPropertyValid,
                out var validValue))
        {
            error = $"Could not read {propertyName} validity at 0x{propertyAddress + AuxDataPropertyValid:X8}";

            return false;
        }

        if (validValue == 0)
        {
            sample = new ClientDeltaFloatAuxDataPropertySample(
                propertyAddress,
                IsValid: false,
                0.0f);

            return true;
        }

        for (var attempt = 0;
             attempt < MaximumDeltaFloatReadAttempts;
             attempt++)
        {
            if (!this.TryReadDeltaFloatState(
                    memory,
                    propertyAddress,
                    propertyName,
                    out var before,
                    out error))
            {
                return false;
            }

            var evaluated = EvaluateDeltaFloat(
                before,
                clientTime);

            if (!float.IsFinite(evaluated))
            {
                error = $"{propertyName} evaluated to non-finite value at 0x{propertyAddress:X8}";

                return false;
            }

            if (!this.TryReadDeltaFloatState(
                    memory,
                    propertyAddress,
                    propertyName,
                    out var after,
                    out error))
            {
                return false;
            }

            if (before != after)
            {
                continue;
            }

            sample = new ClientDeltaFloatAuxDataPropertySample(
                propertyAddress,
                IsValid: true,
                evaluated);

            return true;
        }

        error =
        $"{propertyName} transition changed while it was being read";

        return false;
    }

    public bool TryReadBooleanProperty(
        ProcessMemoryReader memory,
        ClientAuxDataLookupSnapshot snapshot,
        string propertyName,
        out ClientBooleanAuxDataPropertySample sample,
        out string error)
    {
        sample = default;
        error = "";

        if (!snapshot.Properties.TryGetValue(
                propertyName,
                out var propertyAddress) ||
            propertyAddress == 0)
        {
            return true;
        }

        if (!this.TryValidatePropertyType(
                memory,
                propertyAddress,
                snapshot.BooleanPropertyTypeDescriptor,
                propertyName,
                out error))
        {
            return false;
        }

        if (!memory.TryReadUInt32(
                checked(
                    propertyAddress +
                    AuxDataPropertyValid),
                out var validValue))
        {
            error = $"Could not read {propertyName} validity at 0x{propertyAddress + AuxDataPropertyValid:X8}";

            return false;
        }

        if (!TryReadByte(
                memory,
                checked(
                    propertyAddress +
                    FloatAuxDataPropertyValue),
                out var value))
        {
            error = $"Could not read {propertyName} boolean value at 0x{propertyAddress + FloatAuxDataPropertyValue:X8}";

            return false;
        }

        sample = new ClientBooleanAuxDataPropertySample(
            propertyAddress,
            validValue != 0,
            value != 0);

        return true;
    }

    public bool TryReadInt32Property(
        ProcessMemoryReader memory,
        ClientAuxDataLookupSnapshot snapshot,
        string propertyName,
        out ClientInt32AuxDataPropertySample sample,
        out string error)
    {
        sample = default;
        error = "";

        if (!snapshot.Properties.TryGetValue(
                propertyName,
                out var propertyAddress) ||
            propertyAddress == 0)
        {
            return true;
        }

        if (!this.TryValidatePropertyType(
                memory,
                propertyAddress,
                snapshot.Int32PropertyTypeDescriptor,
                propertyName,
                out error))
        {
            return false;
        }

        if (!memory.TryReadUInt32(
                checked(
                    propertyAddress +
                    AuxDataPropertyValid),
                out var validValue) ||
            !memory.TryReadUInt32(
                checked(
                    propertyAddress +
                    FloatAuxDataPropertyValue),
                out var rawValue))
        {
            error = $"Could not read {propertyName} Int32 value at 0x{propertyAddress:X8}";

            return false;
        }

        sample = new ClientInt32AuxDataPropertySample(
            propertyAddress,
            validValue != 0,
            unchecked((int)rawValue));

        return true;
    }

    public bool TryReadStringProperty(
        ProcessMemoryReader memory,
        ClientAuxDataLookupSnapshot snapshot,
        string propertyName,
        out ClientStringAuxDataPropertySample sample,
        out string error)
    {
        sample = default;
        error = "";

        if (!snapshot.Properties.TryGetValue(
                propertyName,
                out var propertyAddress) ||
            propertyAddress == 0)
        {
            return true;
        }

        if (!memory.TryReadUInt32(
                propertyAddress,
                out var actualVTable))
        {
            error = $"Could not read {propertyName} vtable at 0x{propertyAddress:X8}";

            return false;
        }

        if (actualVTable !=
            snapshot.StringPropertyVTable)
        {
            error = $"Unexpected {propertyName} vtable 0x{actualVTable:X8} at 0x{propertyAddress:X8}; expected 0x{snapshot.StringPropertyVTable:X8}";

            return false;
        }

        if (!memory.TryReadUInt32(
                checked(
                    propertyAddress +
                    AuxDataPropertyValid),
                out var validValue) ||
            !memory.TryReadUInt32(
                checked(
                    propertyAddress +
                    FloatAuxDataPropertyValue),
                out var stringAddress))
        {
            error = $"Could not read {propertyName} string state at 0x{propertyAddress:X8}";

            return false;
        }

        var value = "";

        if (validValue != 0 &&
            stringAddress != 0 &&
            !memory.TryReadNullTerminatedLatin1String(
                stringAddress,
                MaximumAuxDataStringLength,
                out value))
        {
            error = $"Could not read {propertyName} string at 0x{stringAddress:X8}";

            return false;
        }

        sample = new ClientStringAuxDataPropertySample(
            propertyAddress,
            validValue != 0,
            stringAddress,
            value);

        return true;
    }

    public bool TryReadRawProperty(
        ProcessMemoryReader memory,
        ClientAuxDataLookupSnapshot snapshot,
        string propertyName,
        bool readSecondaryValue,
        out ClientRawAuxDataPropertySample sample,
        out string error)
    {
        sample = default;
        error = "";

        if (!snapshot.Properties.TryGetValue(
                propertyName,
                out var propertyAddress) ||
            propertyAddress == 0)
        {
            return true;
        }

        if (!memory.TryReadUInt32(
                checked(
                    propertyAddress +
                    AuxDataPropertyValid),
                out var validValue) ||
            !memory.TryReadUInt32(
                checked(
                    propertyAddress +
                    FloatAuxDataPropertyValue),
                out var primaryValue))
        {
            error = $"Could not read raw {propertyName} value at 0x{propertyAddress:X8}";

            return false;
        }

        var secondaryValue = 0u;

        if (readSecondaryValue &&
            !memory.TryReadUInt32(
                checked(
                    propertyAddress +
                    AuxDataPropertySecondaryValue),
                out secondaryValue))
        {
            error = $"Could not read secondary raw {propertyName} value at 0x{propertyAddress + AuxDataPropertySecondaryValue:X8}";

            return false;
        }

        sample = new ClientRawAuxDataPropertySample(
            propertyAddress,
            validValue != 0,
            primaryValue,
            secondaryValue,
            readSecondaryValue);

        return true;
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

    private bool TryFindPropertiesInPropertyVector(
        ProcessMemoryReader memory,
        uint targetAuxDataAddress,
        IReadOnlyCollection<string> requestedNames,
        bool requireAllRequestedProperties,
        out uint propertyVectorAddress,
        out IReadOnlyDictionary<string, uint> properties,
        out string error)
    {
        propertyVectorAddress = 0;

        Dictionary<string, uint> found =
            new(StringComparer.Ordinal);

        properties = found;
        error = "";

        var wanted = requestedNames
            .Where(
                name =>
                    !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (wanted.Length == 0)
        {
            return true;
        }

        var wantedSet = wanted.ToHashSet(
            StringComparer.Ordinal);

        Dictionary<string, List<uint>> shortNameCandidates =
            new(StringComparer.Ordinal);

        var wantedByShortName = wanted
            .GroupBy(
                GetShortPropertyName,
                StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.ToArray(),
                StringComparer.Ordinal);

        var vectorBeginPointerAddress = checked(
            targetAuxDataAddress +
            ObjectAuxDataPropertyVectorBegin);

        if (!memory.TryReadUInt32(
                vectorBeginPointerAddress,
                out var vectorBegin) ||
            !memory.TryReadUInt32(
                checked(
                    targetAuxDataAddress +
                    ObjectAuxDataPropertyVectorEnd),
                out var vectorEnd) ||
            !memory.TryReadUInt32(
                checked(
                    targetAuxDataAddress +
                    ObjectAuxDataPropertyVectorCapacity),
                out var vectorCapacity))
        {
            error = $"Could not read AuxData property vector at 0x{vectorBeginPointerAddress:X8}";

            return false;
        }

        if (vectorBegin == 0 &&
            vectorEnd == 0 &&
            vectorCapacity == 0 &&
            !requireAllRequestedProperties)
        {
            return true;
        }

        if (vectorBegin == 0 ||
            vectorEnd < vectorBegin ||
            vectorCapacity < vectorEnd)
        {
            error = $"AuxData property vector is inconsistent: begin=0x{vectorBegin:X8}, end=0x{vectorEnd:X8}, capacity=0x{vectorCapacity:X8}";

            return false;
        }

        var activeLength =
            vectorEnd - vectorBegin;

        if (activeLength % sizeof(uint) != 0)
        {
            error = $"AuxData property vector active length {activeLength} is not DWORD-aligned";

            return false;
        }

        var propertyCount = checked(
            (int)(activeLength / sizeof(uint)));

        if (propertyCount == 0 &&
            !requireAllRequestedProperties)
        {
            return true;
        }

        if (propertyCount is 0 or > MaximumAuxDataPropertyVectorCount)
        {
            error = string.Create(
                CultureInfo.InvariantCulture,
$"AuxData property vector contains unexpected property count {propertyCount}");

            return false;
        }

        propertyVectorAddress = vectorBegin;

        if (!memory.TryReadBytes(
                vectorBegin,
                checked(
                    propertyCount *
                    sizeof(uint)),
                out var vectorBytes))
        {
            error = string.Create(
                CultureInfo.InvariantCulture,
$"Could not read {propertyCount}-entry AuxData property vector at 0x{vectorBegin:X8}");

            return false;
        }

        for (var index = 0;
             index < propertyCount;
             index++)
        {
            var propertyAddress = BitConverter.ToUInt32(
                vectorBytes,
                checked(
                    index *
                    sizeof(uint)));

            if (propertyAddress == 0)
            {
                continue;
            }

            var qualifiedName = "";
            var shortName = "";

            if (memory.TryReadUInt32(
                    checked(
                        propertyAddress +
                        AuxDataPropertyQualifiedName),
                    out var qualifiedNameAddress) &&
                qualifiedNameAddress != 0)
            {
                _ = memory.TryReadNullTerminatedLatin1String(
                    qualifiedNameAddress,
                    MaximumAuxDataNameLength,
                    out qualifiedName);
            }

            if (memory.TryReadUInt32(
                    checked(
                        propertyAddress +
                        AuxDataPropertyShortName),
                    out var shortNameAddress) &&
                shortNameAddress != 0)
            {
                _ = memory.TryReadNullTerminatedLatin1String(
                    shortNameAddress,
                    MaximumAuxDataNameLength,
                    out shortName);
            }

            if (!string.IsNullOrWhiteSpace(
                    qualifiedName) &&
                wantedSet.Contains(qualifiedName))
            {
                found[qualifiedName] =
                    propertyAddress;

                if (found.Count == wanted.Length)
                {
                    return true;
                }

                continue;
            }

            if (string.IsNullOrWhiteSpace(shortName) ||
                !wantedByShortName.ContainsKey(shortName))
            {
                continue;
            }

            if (!shortNameCandidates.TryGetValue(
                    shortName,
                    out var candidates))
            {
                candidates = [];
                shortNameCandidates[shortName] =
                    candidates;
            }

            candidates.Add(propertyAddress);
        }

        foreach (var requestedName in wanted)
        {
            if (found.ContainsKey(requestedName))
            {
                continue;
            }

            var shortName =
                GetShortPropertyName(requestedName);

            if (wantedByShortName[shortName].Length != 1 ||
                !shortNameCandidates.TryGetValue(
                    shortName,
                    out var candidates))
            {
                continue;
            }

            var distinctCandidates = candidates
                .Distinct()
                .ToArray();

            if (distinctCandidates.Length == 1)
            {
                found[requestedName] =
                    distinctCandidates[0];
            }
        }

        if (found.Count != wanted.Length &&
            requireAllRequestedProperties)
        {
            var missing = wanted
                .Where(
                    name =>
                        !found.ContainsKey(name));

            error = string.Create(
                CultureInfo.InvariantCulture,
$"AuxData property vector exposed {propertyCount} properties but resolved {found.Count}/{wanted.Length} requested name(s); missing: {string.Join(", ", missing)}");

            return false;
        }

        return true;

        static string GetShortPropertyName(
            string qualifiedName)
        {
            var separatorIndex =
                qualifiedName.LastIndexOf('.');

            return separatorIndex >= 0 &&
                   separatorIndex + 1 <
                       qualifiedName.Length
                ? qualifiedName[(separatorIndex + 1)..]
                : qualifiedName;
        }
    }

    private bool TryFindPropertiesByCanonicalNamePointer(
        ProcessMemoryReader memory,
        uint lookupAddress,
        uint nilSentinelAddress,
        uint internBucketTableAddress,
        uint internBucketCount,
        IReadOnlyCollection<string> requestedNames,
        out IReadOnlyDictionary<string, uint> properties,
        out int treeNodeVisitCount,
        out string error)
    {
        Dictionary<string, uint> found =
            new(StringComparer.Ordinal);

        properties = found;
        treeNodeVisitCount = 0;
        error = "";

        if (internBucketTableAddress == 0 ||
            internBucketCount == 0 ||
            internBucketCount >
                MaximumAuxDataInternBucketCount)
        {
            error = $"AuxData intern table is inconsistent: table=0x{internBucketTableAddress:X8}, buckets={internBucketCount}";

            return false;
        }

        var requested = requestedNames
            .Where(
                name =>
                    !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (requested.Length == 0)
        {
            return true;
        }

        var headerPointerAddress = checked(
            lookupAddress +
            AuxDataLookupTreeHeader);

        if (!memory.TryReadUInt32(
                headerPointerAddress,
                out var headerAddress) ||
            headerAddress == 0)
        {
            error = $"Could not resolve AuxData tree header at 0x{headerPointerAddress:X8}";

            return false;
        }

        var rootPointerAddress = checked(
            headerAddress +
            AuxDataTreeHeaderRoot);

        if (!memory.TryReadUInt32(
                rootPointerAddress,
                out var rootAddress))
        {
            error = $"Could not read AuxData tree root at 0x{rootPointerAddress:X8}";

            return false;
        }

        if (rootAddress == 0 ||
            rootAddress == nilSentinelAddress)
        {
            return true;
        }

        foreach (var requestedName in requested)
        {
            if (!this.TryResolveInternedNamePointer(
                    memory,
                    internBucketTableAddress,
                    internBucketCount,
                    requestedName,
                    out var canonicalNameAddress,
                    out var nameWasInterned,
                    out error))
            {
                return false;
            }

            if (!nameWasInterned)
            {
                continue;
            }

            if (!this.TryFindPropertyByCanonicalNamePointer(
                    memory,
                    rootAddress,
                    headerAddress,
                    nilSentinelAddress,
                    canonicalNameAddress,
                    out var propertyAddress,
                    out var propertyWasFound,
                    out var visitedNodeCount,
                    out error))
            {
                return false;
            }

            treeNodeVisitCount = checked(
                treeNodeVisitCount +
                visitedNodeCount);

            if (propertyWasFound)
            {
                found[requestedName] =
                    propertyAddress;
            }
        }

        return true;
    }

    private bool TryResolveInternedNamePointer(
        ProcessMemoryReader memory,
        uint internBucketTableAddress,
        uint internBucketCount,
        string requestedName,
        out uint canonicalNameAddress,
        out bool wasFound,
        out string error)
    {
        canonicalNameAddress = 0;
        wasFound = false;
        error = "";

        var hash = ComputeAuxDataNameHash(
            requestedName);

        var bucketIndex =
            hash % internBucketCount;

        var bucketPointerAddress = checked(
            internBucketTableAddress +
            checked(
                bucketIndex *
                sizeof(uint)));

        if (!memory.TryReadUInt32(
                bucketPointerAddress,
                out var nodeAddress))
        {
            error = $"Could not read AuxData intern bucket {bucketIndex} at 0x{bucketPointerAddress:X8}";

            return false;
        }

        HashSet<uint> visited = [];

        for (var chainIndex = 0;
             chainIndex <
                MaximumAuxDataInternChainLength;
             chainIndex++)
        {
            if (nodeAddress == 0)
            {
                return true;
            }

            if (!visited.Add(nodeAddress))
            {
                error = $"AuxData intern bucket {bucketIndex} contains a cycle at 0x{nodeAddress:X8}";

                return false;
            }

            if (!memory.TryReadBytes(
                    nodeAddress,
                    AuxDataInternNodeSize,
                    out var nodeBytes))
            {
                error = $"Could not read AuxData intern node at 0x{nodeAddress:X8}";

                return false;
            }

            var stringAddress =
                BitConverter.ToUInt32(
                    nodeBytes,
                    checked(
                        (int)AuxDataInternNodeString));

            var storedHash =
                BitConverter.ToUInt32(
                    nodeBytes,
                    checked(
                        (int)AuxDataInternNodeHash));

            var nextAddress =
                BitConverter.ToUInt32(
                    nodeBytes,
                    checked(
                        (int)AuxDataInternNodeNext));

            if (storedHash == hash)
            {
                if (stringAddress == 0)
                {
                    error = $"AuxData intern node at 0x{nodeAddress:X8} contains a null string pointer";

                    return false;
                }

                if (!memory.TryReadNullTerminatedLatin1String(
                        stringAddress,
                        MaximumAuxDataNameLength,
                        out var candidateName))
                {
                    error = $"Could not read AuxData interned name at 0x{stringAddress:X8}";

                    return false;
                }

                if (string.Equals(
                        candidateName,
                        requestedName,
                        StringComparison.Ordinal))
                {
                    canonicalNameAddress =
                        stringAddress;

                    wasFound = true;
                    return true;
                }
            }

            nodeAddress = nextAddress;
        }

        error = string.Create(
            CultureInfo.InvariantCulture,
        $"AuxData intern bucket {bucketIndex} exceeded {MaximumAuxDataInternChainLength} nodes while resolving {requestedName}");

        return false;
    }

    private bool TryFindPropertyByCanonicalNamePointer(
        ProcessMemoryReader memory,
        uint rootAddress,
        uint headerAddress,
        uint nilSentinelAddress,
        uint canonicalNameAddress,
        out uint propertyAddress,
        out bool wasFound,
        out int visitedNodeCount,
        out string error)
    {
        propertyAddress = 0;
        wasFound = false;
        visitedNodeCount = 0;
        error = "";

        var nodeAddress = rootAddress;
        HashSet<uint> visited = [];

        for (var depth = 0;
             depth < MaximumAuxDataOrderedLookupDepth;
             depth++)
        {
            if (nodeAddress == 0 ||
                nodeAddress == nilSentinelAddress ||
                nodeAddress == headerAddress)
            {
                return true;
            }

            if (!visited.Add(nodeAddress))
            {
                error = $"AuxData pointer-key tree contains a cycle at 0x{nodeAddress:X8}";

                return false;
            }

            visitedNodeCount =
                visited.Count;

            if (!memory.TryReadBytes(
                    nodeAddress,
                    AuxDataTreeNodeSnapshotSize,
                    out var nodeBytes))
            {
                error = $"Could not read AuxData pointer-key tree node at 0x{nodeAddress:X8}";

                return false;
            }

            var leftAddress =
                BitConverter.ToUInt32(
                    nodeBytes,
                    checked(
                        (int)AuxDataTreeNodeLeft));

            var rightAddress =
                BitConverter.ToUInt32(
                    nodeBytes,
                    checked(
                        (int)AuxDataTreeNodeRight));

            var nodeCanonicalNameAddress =
                BitConverter.ToUInt32(
                    nodeBytes,
                    checked(
                        (int)AuxDataTreeNodeName));

            if (canonicalNameAddress ==
                nodeCanonicalNameAddress)
            {
                propertyAddress =
                    BitConverter.ToUInt32(
                        nodeBytes,
                        checked(
                            (int)AuxDataTreeNodeProperty));

                wasFound =
                    propertyAddress != 0;

                return true;
            }

            nodeAddress =
                canonicalNameAddress <
                    nodeCanonicalNameAddress
                    ? leftAddress
                    : rightAddress;
        }

        error = string.Create(
            CultureInfo.InvariantCulture,
        $"AuxData pointer-key lookup exceeded {MaximumAuxDataOrderedLookupDepth} levels for canonical name 0x{canonicalNameAddress:X8}");

        return false;
    }

    private static uint ComputeAuxDataNameHash(
        string value)
    {
        var bytes = Encoding.Latin1.GetBytes(
            value);

        var hash = 0u;

        foreach (var valueByte in bytes)
        {
            // InternCString adds a signed char to the UInt32 accumulator,
            // multiplies by 0x401, then folds the upper bits with >> 6.
            var signedValue =
                unchecked((uint)(sbyte)valueByte);

            hash = unchecked(
                (hash + signedValue) *
                0x401u);

            hash ^= hash >> 6;
        }

        return hash;
    }

    private bool TryFindProperties(
        ProcessMemoryReader memory,
        uint lookupAddress,
        uint nilSentinelAddress,
        IReadOnlyCollection<string> requestedNames,
        out IReadOnlyDictionary<string, uint> properties,
        out string error)
    {
        return this.TryFindPropertiesCore(
            memory,
            lookupAddress,
            nilSentinelAddress,
            requestedNames,
            MaximumAuxDataTreeNodeCount,
            out properties,
            out error);
    }

    private bool TryFindPropertiesCore(
        ProcessMemoryReader memory,
        uint lookupAddress,
        uint nilSentinelAddress,
        IReadOnlyCollection<string> requestedNames,
        int maximumNodeCount,
        out IReadOnlyDictionary<string, uint> properties,
        out string error)
    {
        Dictionary<string, uint> found =
            new(StringComparer.Ordinal);

        properties = found;
        error = "";

        var wanted = requestedNames
            .Where(
                name =>
                    !string.IsNullOrWhiteSpace(name))
            .ToHashSet(
                StringComparer.Ordinal);

        if (wanted.Count == 0)
        {
            return true;
        }

        var headerPointerAddress = checked(
            lookupAddress +
            AuxDataLookupTreeHeader);

        if (!memory.TryReadUInt32(
                headerPointerAddress,
                out var headerAddress) ||
            headerAddress == 0)
        {
            error = $"Could not resolve AuxData tree header at 0x{headerPointerAddress:X8}";

            return false;
        }

        var rootPointerAddress = checked(
            headerAddress +
            AuxDataTreeHeaderRoot);

        if (!memory.TryReadUInt32(
                rootPointerAddress,
                out var rootAddress))
        {
            error = $"Could not read AuxData tree root at 0x{rootPointerAddress:X8}";

            return false;
        }

        if (rootAddress == 0 ||
            rootAddress == nilSentinelAddress)
        {
            return true;
        }

        foreach (var requestedName in wanted)
        {
            if (TryFindPropertyByOrderedTree(
                    memory,
                    rootAddress,
                    headerAddress,
                    nilSentinelAddress,
                    requestedName,
                    StringComparer.Ordinal,
                    ascending: true,
                    out var propertyAddress) ||
                TryFindPropertyByOrderedTree(
                    memory,
                    rootAddress,
                    headerAddress,
                    nilSentinelAddress,
                    requestedName,
                    StringComparer.Ordinal,
                    ascending: false,
                    out propertyAddress) ||
                TryFindPropertyByOrderedTree(
                    memory,
                    rootAddress,
                    headerAddress,
                    nilSentinelAddress,
                    requestedName,
                    StringComparer.OrdinalIgnoreCase,
                    ascending: true,
                    out propertyAddress) ||
                TryFindPropertyByOrderedTree(
                    memory,
                    rootAddress,
                    headerAddress,
                    nilSentinelAddress,
                    requestedName,
                    StringComparer.OrdinalIgnoreCase,
                    ascending: false,
                    out propertyAddress))
            {
                found[requestedName] = propertyAddress;
            }
        }

        if (found.Count == wanted.Count)
        {
            return true;
        }

        Stack<uint> pending = [];
        HashSet<uint> visited = [];

        pending.Push(rootAddress);

        while (pending.Count > 0)
        {
            if (visited.Count >=
                maximumNodeCount)
            {
                error = string.Create(
                    CultureInfo.InvariantCulture,
$"AuxData tree exceeded targeted traversal limit of {maximumNodeCount} nodes after resolving {found.Count}/{wanted.Count} requested properties");

                return false;
            }

            var nodeAddress = pending.Pop();

            if (nodeAddress == 0 ||
                nodeAddress == nilSentinelAddress ||
                nodeAddress == headerAddress)
            {
                continue;
            }

            if (!visited.Add(nodeAddress))
            {
                continue;
            }

            if (!memory.TryReadUInt32(
                    nodeAddress +
                    AuxDataTreeNodeLeft,
                    out var leftAddress) ||
                !memory.TryReadUInt32(
                    nodeAddress +
                    AuxDataTreeNodeRight,
                    out var rightAddress) ||
                !memory.TryReadUInt32(
                    nodeAddress +
                    AuxDataTreeNodeName,
                    out var nameAddress) ||
                !memory.TryReadUInt32(
                    nodeAddress +
                    AuxDataTreeNodeProperty,
                    out var propertyAddress))
            {
                error = $"Could not read AuxData tree node at 0x{nodeAddress:X8}";

                return false;
            }

            if (nameAddress != 0)
            {
                if (!memory.TryReadNullTerminatedLatin1String(
                        nameAddress,
                        MaximumAuxDataNameLength,
                        out var name))
                {
                    error = $"Could not read AuxData name at 0x{nameAddress:X8}";

                    return false;
                }

                if (wanted.Contains(name))
                {
                    found[name] = propertyAddress;

                    if (found.Count == wanted.Count)
                    {
                        return true;
                    }
                }
            }

            if (rightAddress != 0 &&
                rightAddress != nilSentinelAddress &&
                rightAddress != headerAddress)
            {
                pending.Push(rightAddress);
            }

            if (leftAddress != 0 &&
                leftAddress != nilSentinelAddress &&
                leftAddress != headerAddress)
            {
                pending.Push(leftAddress);
            }
        }

        return true;
    }


    private static bool TryFindPropertyByOrderedTree(
        ProcessMemoryReader memory,
        uint rootAddress,
        uint headerAddress,
        uint nilSentinelAddress,
        string requestedName,
        StringComparer comparer,
        bool ascending,
        out uint propertyAddress)
    {
        propertyAddress = 0;

        var nodeAddress = rootAddress;
        HashSet<uint> visited = [];

        for (var depth = 0;
             depth < MaximumAuxDataOrderedLookupDepth;
             depth++)
        {
            if (nodeAddress == 0 ||
                nodeAddress == nilSentinelAddress ||
                nodeAddress == headerAddress ||
                !visited.Add(nodeAddress))
            {
                return false;
            }

            if (!memory.TryReadUInt32(
                    nodeAddress +
                    AuxDataTreeNodeLeft,
                    out var leftAddress) ||
                !memory.TryReadUInt32(
                    nodeAddress +
                    AuxDataTreeNodeRight,
                    out var rightAddress) ||
                !memory.TryReadUInt32(
                    nodeAddress +
                    AuxDataTreeNodeName,
                    out var nameAddress) ||
                nameAddress == 0 ||
                !memory.TryReadNullTerminatedLatin1String(
                    nameAddress,
                    MaximumAuxDataNameLength,
                    out var nodeName))
            {
                return false;
            }

            var comparison = comparer.Compare(
                requestedName,
                nodeName);

            if (comparison == 0)
            {
                return memory.TryReadUInt32(
                    nodeAddress +
                    AuxDataTreeNodeProperty,
                    out propertyAddress);
            }

            var moveLeft = ascending
                ? comparison < 0
                : comparison > 0;

            nodeAddress = moveLeft
                ? leftAddress
                : rightAddress;
        }

        return false;
    }


    private bool TryValidatePropertyType(
        ProcessMemoryReader memory,
        uint propertyAddress,
        uint expectedTypeDescriptor,
        string propertyName,
        out string error)
    {
        error = "";

        var descriptorAddress = checked(
            propertyAddress +
            AuxDataPropertyTypeDescriptor);

        if (!memory.TryReadUInt32(
                descriptorAddress,
                out var actualTypeDescriptor))
        {
            error = $"Could not read {propertyName} type descriptor at 0x{descriptorAddress:X8}";

            return false;
        }

        if (actualTypeDescriptor !=
            expectedTypeDescriptor)
        {
            error = $"Unexpected {propertyName} type descriptor 0x{actualTypeDescriptor:X8} at 0x{descriptorAddress:X8}; expected 0x{expectedTypeDescriptor:X8}";

            return false;
        }

        return true;
    }

    private bool TryReadDeltaFloatState(
        ProcessMemoryReader memory,
        uint propertyAddress,
        string propertyName,
        out DeltaFloatState state,
        out string error)
    {
        state = default;
        error = "";

        if (!memory.TryReadUInt32(
                propertyAddress +
                DeltaFloatTransitionEndTimeLow,
                out var transitionEndTimeLow) ||
            !memory.TryReadUInt32(
                propertyAddress +
                DeltaFloatTransitionEndTimeHigh,
                out var transitionEndTimeHigh) ||
            !TryReadSingle(
                memory,
                propertyAddress +
                DeltaFloatInterpolationRate,
                out var interpolationRate) ||
            !TryReadSingle(
                memory,
                propertyAddress +
                DeltaFloatTransitionBaseValue,
                out var transitionBaseValue) ||
            !memory.TryReadUInt32(
                propertyAddress +
                DeltaFloatLastEvaluatedTimeLow,
                out var lastEvaluatedTimeLow) ||
            !memory.TryReadUInt32(
                propertyAddress +
                DeltaFloatLastEvaluatedTimeHigh,
                out var lastEvaluatedTimeHigh) ||
            !TryReadSingle(
                memory,
                propertyAddress +
                DeltaFloatCachedValue,
                out var cachedValue) ||
            !TryReadByte(
                memory,
                propertyAddress +
                DeltaFloatInterpolationComplete,
                out var interpolationComplete))
        {
            error = $"Could not read {propertyName} transition state at 0x{propertyAddress:X8}";

            return false;
        }

        if (!float.IsFinite(interpolationRate) ||
            !float.IsFinite(transitionBaseValue) ||
            !float.IsFinite(cachedValue))
        {
            error = $"{propertyName} transition state contains non-finite values at 0x{propertyAddress:X8}";

            return false;
        }

        state = new DeltaFloatState(
            transitionEndTimeLow,
            unchecked((int)transitionEndTimeHigh),
            interpolationRate,
            transitionBaseValue,
            lastEvaluatedTimeLow,
            unchecked((int)lastEvaluatedTimeHigh),
            cachedValue,
            interpolationComplete != 0);

        return true;
    }

    private static float EvaluateDeltaFloat(
        DeltaFloatState state,
        uint clientTime)
    {
        var currentTime =
            (long)clientTime;

        var lastEvaluatedTime =
            ((long)state.LastEvaluatedTimeHigh << 32) |
            state.LastEvaluatedTimeLow;

        if (state.InterpolationComplete ||
            currentTime == lastEvaluatedTime)
        {
            return state.CachedValue;
        }

        if (state.InterpolationRate == 0.0f)
        {
            return state.TransitionBaseValue;
        }

        var transitionEndTime =
            ((long)state.TransitionEndTimeHigh << 32) |
            state.TransitionEndTimeLow;

        if (currentTime >= transitionEndTime)
        {
            return state.InterpolationRate < 0.0f
                ? 0.0f
                : 1.0f;
        }

        var remainingTime =
            transitionEndTime - currentTime;

        var remainingDuration =
            remainingTime > int.MaxValue
                ? int.MaxValue
                : remainingTime < int.MinValue
                    ? int.MinValue
                    : (int)remainingTime;

        if (state.InterpolationRate < 0.0f)
        {
            var value =
                -(remainingDuration *
                  state.InterpolationRate);

            if (value > 1.0f)
            {
                value = 1.0f;
            }

            if (state.TransitionBaseValue < value)
            {
                value = state.TransitionBaseValue;
            }

            return value;
        }

        var risingValue =
            1.0f -
            remainingDuration *
            state.InterpolationRate;

        if (risingValue < 0.0f)
        {
            risingValue = 0.0f;
        }

        if (risingValue <
            state.TransitionBaseValue)
        {
            risingValue =
                state.TransitionBaseValue;
        }

        return risingValue;
    }

    private static bool TryReadSingle(
        ProcessMemoryReader memory,
        uint address,
        out float value)
    {
        value = 0.0f;

        if (!memory.TryReadBytes(
                address,
                sizeof(float),
                out var bytes))
        {
            return false;
        }

        value = BitConverter.ToSingle(
            bytes,
            0);

        return true;
    }

    private static bool TryReadByte(
        ProcessMemoryReader memory,
        uint address,
        out byte value)
    {
        value = 0;

        if (!memory.TryReadBytes(
                address,
                1,
                out var bytes))
        {
            return false;
        }

        value = bytes[0];
        return true;
    }

    private readonly record struct DeltaFloatState(
        uint TransitionEndTimeLow,
        int TransitionEndTimeHigh,
        float InterpolationRate,
        float TransitionBaseValue,
        uint LastEvaluatedTimeLow,
        int LastEvaluatedTimeHigh,
        float CachedValue,
        bool InterpolationComplete);
}
