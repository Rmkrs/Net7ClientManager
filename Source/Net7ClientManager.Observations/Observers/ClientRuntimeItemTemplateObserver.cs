namespace Net7ClientManager.Observations.Observers;

using System.Globalization;
using System.Text;
using Net7ClientManager.Observations.Models;

internal sealed class ClientRuntimeItemTemplateObserver
{
    private const uint ItemRegistryBucketArrayOffset = 0x0cb0;
    private const int ItemRegistryBucketCount = 0x101;

    private const uint RegistryNodeNext = 0x04;
    private const uint RegistryNodeDefinition = 0x10;
    private const uint RegistryNodeTemplateId = 0x14;
    private const uint RegistryNodeFlags = 0x18;
    private const uint RegistryNodeBucketTerminalFlag = 0x00000002;
    private const int RegistryNodeSize = 0x1c;
    private const int MaximumBucketNodeCount = 512;

    private const int ClientItemDefinitionSize = 0x60;
    private const uint DefinitionItemTemplateId = 0x00;
    private const uint DefinitionCategory = 0x04;
    private const uint DefinitionSubcategory = 0x08;
    private const uint DefinitionItemType = 0x0c;
    private const uint DefinitionName = 0x14;
    private const uint DefinitionSecondString = 0x24;
    private const uint DefinitionThirdString = 0x34;
    private const uint DefinitionGameBasset = 0x44;
    private const uint DefinitionTechLevel = 0x48;
    private const uint DefinitionCost = 0x4c;
    private const uint DefinitionMaxStack = 0x50;
    private const uint DefinitionFlags = 0x58;
    private const uint DefinitionItemTemplateInfo = 0x5c;

    private const uint ItemTemplateAttributesBegin = 0x0c;
    private const uint ItemTemplateAttributesEnd = 0x10;
    private const uint ItemTemplateActivatedEffects = 0x40;
    private const uint ItemTemplateEquippedEffects = 0x68;

    private const int AttributeEntrySize = 0x08;
    private const uint ItemFieldTypeTableRva = 0x007f34e0;
    private const uint ItemTypeNameAttributeId = 11;

    private const int EffectRecordSize = 0x58;
    private const uint EffectSourceOrIdentity = 0x00;
    private const uint EffectNameFormat = 0x10;
    private const uint EffectDescriptionFormat = 0x20;
    private const uint EffectNameValues = 0x30;
    private const uint EffectDescriptionValues = 0x40;
    private const uint EffectField50 = 0x50;
    private const uint EffectField54 = 0x54;

    private const uint VectorBegin = 0x04;
    private const uint VectorEnd = 0x08;

    private const uint SharedStringBuffer = 0x04;
    private const uint SharedStringLength = 0x08;

    private const int MaximumAttributeCount = 512;
    private const int MaximumEffectCount = 256;
    private const int MaximumEffectValueCount = 128;
    private const int MaximumSharedStringLength = 8_192;
    private const int MaximumAttributeStringLength = 8_192;

    private readonly object cacheLock = new();

    private readonly Dictionary<RuntimeTemplateCacheKey, ClientRuntimeItemTemplateObservation>
        cache = [];

    public IReadOnlyDictionary<int, ClientRuntimeItemTemplateObservation> ResolveDefinitions(
        ProcessMemoryReader memory,
        int processId,
        uint moduleBaseAddress,
        uint clientContextAddress,
        IEnumerable<int> itemTemplateIds)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(itemTemplateIds);

        var requestedIds = itemTemplateIds
            .Where(itemTemplateId => itemTemplateId > 0)
            .Distinct()
            .ToArray();

        if (clientContextAddress == 0 ||
            moduleBaseAddress == 0 ||
            requestedIds.Length == 0)
        {
            return new Dictionary<int, ClientRuntimeItemTemplateObservation>();
        }

        Dictionary<int, ClientRuntimeItemTemplateObservation> resolved = [];
        List<int> missing = [];

        lock (this.cacheLock)
        {
            foreach (var itemTemplateId in requestedIds)
            {
                var key = new RuntimeTemplateCacheKey(
                    processId,
                    moduleBaseAddress,
                    clientContextAddress,
                    itemTemplateId);

                if (this.cache.TryGetValue(key, out var cached))
                {
                    resolved[itemTemplateId] = cached;
                }
                else
                {
                    missing.Add(itemTemplateId);
                }
            }
        }

        foreach (var itemTemplateId in missing)
        {
            if (!this.TryResolveDefinition(
                    memory,
                    moduleBaseAddress,
                    clientContextAddress,
                    itemTemplateId,
                    out var definition))
            {
                continue;
            }

            var key = new RuntimeTemplateCacheKey(
                processId,
                moduleBaseAddress,
                clientContextAddress,
                itemTemplateId);

            lock (this.cacheLock)
            {
                this.cache[key] = definition;
            }

            resolved[itemTemplateId] = definition;
        }

        return resolved;
    }

    private bool TryResolveDefinition(
        ProcessMemoryReader memory,
        uint moduleBaseAddress,
        uint clientContextAddress,
        int itemTemplateId,
        out ClientRuntimeItemTemplateObservation definition)
    {
        definition = null!;

        var bucketIndex =
            unchecked((uint)itemTemplateId) %
            (uint)ItemRegistryBucketCount;

        if (!TryAdd(
                clientContextAddress,
                ItemRegistryBucketArrayOffset,
                out var bucketArrayAddress) ||
            !TryAdd(
                bucketArrayAddress,
                bucketIndex * sizeof(uint),
                out var bucketAddress) ||
            !memory.TryReadUInt32(
                bucketAddress,
                out var nodeAddress))
        {
            return false;
        }

        for (var nodeIndex = 0;
             nodeAddress != 0 &&
             nodeIndex < MaximumBucketNodeCount;
             nodeIndex++)
        {
            if (!memory.TryReadBytes(
                    nodeAddress,
                    RegistryNodeSize,
                    out var nodeBytes))
            {
                return false;
            }

            var nextNodeAddress = ReadUInt32(
                nodeBytes,
                RegistryNodeNext);
            var definitionAddress = ReadUInt32(
                nodeBytes,
                RegistryNodeDefinition);
            var nodeTemplateId = unchecked((int)ReadUInt32(
                nodeBytes,
                RegistryNodeTemplateId));
            var nodeFlags = ReadUInt32(
                nodeBytes,
                RegistryNodeFlags);

            if (nodeTemplateId == itemTemplateId &&
                definitionAddress != 0 &&
                this.TryReadDefinition(
                    memory,
                    moduleBaseAddress,
                    definitionAddress,
                    itemTemplateId,
                    out definition))
            {
                return true;
            }

            if ((nodeFlags & RegistryNodeBucketTerminalFlag) != 0)
            {
                break;
            }

            nodeAddress = nextNodeAddress;
        }

        return false;
    }

    private bool TryReadDefinition(
        ProcessMemoryReader memory,
        uint moduleBaseAddress,
        uint definitionAddress,
        int expectedItemTemplateId,
        out ClientRuntimeItemTemplateObservation definition)
    {
        definition = null!;

        if (!memory.TryReadBytes(
                definitionAddress,
                ClientItemDefinitionSize,
                out var bytes))
        {
            return false;
        }

        var itemTemplateId = unchecked((int)ReadUInt32(
            bytes,
            DefinitionItemTemplateId));

        if (itemTemplateId != expectedItemTemplateId)
        {
            return false;
        }

        var itemTemplateInfoAddress = ReadUInt32(
            bytes,
            DefinitionItemTemplateInfo);

        var name = this.ReadSharedString(
            memory,
            checked(definitionAddress + DefinitionName));
        var description = this.ReadSharedString(
            memory,
            checked(definitionAddress + DefinitionSecondString));
        var manufacturer = this.ReadSharedString(
            memory,
            checked(definitionAddress + DefinitionThirdString));

        if (ClientItemTemplateCatalog.TryGetDefinition(
                itemTemplateId,
                out var fallback))
        {
            name = FirstNonEmpty(name, fallback.Name);
            description = FirstNonEmpty(description, fallback.Description);
            manufacturer = FirstNonEmpty(manufacturer, fallback.Manufacturer);
        }

        IReadOnlyList<ClientRuntimeItemAttributeObservation> attributes = [];
        IReadOnlyList<ClientRuntimeItemEffectObservation> activatedEffects = [];
        IReadOnlyList<ClientRuntimeItemEffectObservation> equippedEffects = [];

        if (itemTemplateInfoAddress != 0)
        {
            attributes = ReadAttributes(
                memory,
                moduleBaseAddress,
                itemTemplateInfoAddress);
            activatedEffects = this.ReadEffectVector(
                memory,
                checked(itemTemplateInfoAddress + ItemTemplateActivatedEffects));
            equippedEffects = this.ReadEffectVector(
                memory,
                checked(itemTemplateInfoAddress + ItemTemplateEquippedEffects));
        }

        var itemType = unchecked((int)ReadUInt32(
            bytes,
            DefinitionItemType));
        var typeDisplayName = ResolveTypeDisplayName(
            attributes,
            itemType);

        definition = new ClientRuntimeItemTemplateObservation
        {
            IsAvailable = true,
            Status = "Runtime item template resolved",
            ItemTemplateId = itemTemplateId,
            Category = unchecked((int)ReadUInt32(bytes, DefinitionCategory)),
            Subcategory = unchecked((int)ReadUInt32(bytes, DefinitionSubcategory)),
            ItemType = itemType,
            Name = NormalizeText(name),
            Description = NormalizeText(description),
            Manufacturer = NormalizeText(manufacturer),
            TypeDisplayName = typeDisplayName,
            GameBasset = ReadUInt32(bytes, DefinitionGameBasset),
            TechLevel = ReadUInt32(bytes, DefinitionTechLevel),
            Cost = ReadUInt32(bytes, DefinitionCost),
            MaxStack = ReadUInt32(bytes, DefinitionMaxStack),
            Flags = ReadUInt32(bytes, DefinitionFlags),
            Attributes = attributes,
            ActivatedEffects = activatedEffects,
            EquippedEffects = equippedEffects,
        };

        return true;
    }

    private static IReadOnlyList<ClientRuntimeItemAttributeObservation> ReadAttributes(
        ProcessMemoryReader memory,
        uint moduleBaseAddress,
        uint itemTemplateInfoAddress)
    {
        if (!TryReadFlatVector(
                memory,
                itemTemplateInfoAddress,
                ItemTemplateAttributesBegin,
                ItemTemplateAttributesEnd,
                AttributeEntrySize,
                MaximumAttributeCount,
                out var begin,
                out var count))
        {
            return [];
        }

        List<ClientRuntimeItemAttributeObservation> attributes = [];

        for (var index = 0; index < count; index++)
        {
            var entryAddress = checked(
                begin +
                (uint)(index * AttributeEntrySize));

            if (!memory.TryReadBytes(
                    entryAddress,
                    AttributeEntrySize,
                    out var bytes))
            {
                continue;
            }

            var itemInfoId = ReadUInt32(bytes, 0);
            var rawValue = ReadUInt32(bytes, 4);
            var typeCode = 0u;

            if (TryAdd(
                    moduleBaseAddress,
                    ItemFieldTypeTableRva,
                    out var typeTableAddress) &&
                TryMultiply(
                    itemInfoId,
                    sizeof(uint),
                    out var typeOffset) &&
                TryAdd(
                    typeTableAddress,
                    typeOffset,
                    out var typeAddress))
            {
                _ = memory.TryReadUInt32(
                    typeAddress,
                    out typeCode);
            }

            int? intValue = null;
            float? floatValue = null;
            var stringValue = "";

            switch (typeCode)
            {
                case 1:
                    intValue = unchecked((int)rawValue);
                    break;

                case 2:
                    var observedFloat = BitConverter.Int32BitsToSingle(
                        unchecked((int)rawValue));

                    if (float.IsFinite(observedFloat))
                    {
                        floatValue = observedFloat;
                    }

                    break;

                case 4 when rawValue != 0:
                    _ = memory.TryReadNullTerminatedLatin1String(
                        rawValue,
                        MaximumAttributeStringLength,
                        out stringValue);
                    break;
            }

            attributes.Add(
                new ClientRuntimeItemAttributeObservation
                {
                    ItemInfoId = itemInfoId,
                    TypeCode = typeCode,
                    RawValue = rawValue,
                    Int32Value = intValue,
                    FloatValue = floatValue,
                    StringValue = NormalizeText(stringValue),
                });
        }

        return attributes;
    }

    private IReadOnlyList<ClientRuntimeItemEffectObservation> ReadEffectVector(
        ProcessMemoryReader memory,
        uint vectorAddress)
    {
        if (!TryReadObjectVector(
                memory,
                vectorAddress,
                EffectRecordSize,
                MaximumEffectCount,
                out var begin,
                out var count))
        {
            return [];
        }

        List<ClientRuntimeItemEffectObservation> effects = [];

        for (var index = 0; index < count; index++)
        {
            var address = checked(
                begin +
                (uint)(index * EffectRecordSize));

            _ = memory.TryReadUInt32(
                checked(address + EffectField50),
                out var field50);
            _ = memory.TryReadUInt32(
                checked(address + EffectField54),
                out var field54);

            effects.Add(
                new ClientRuntimeItemEffectObservation
                {
                    Index = index,
                    SourceOrIdentity = NormalizeText(
                        this.ReadSharedString(
                            memory,
                            checked(address + EffectSourceOrIdentity))),
                    NameFormat = NormalizeText(
                        this.ReadSharedString(
                            memory,
                            checked(address + EffectNameFormat))),
                    DescriptionFormat = NormalizeText(
                        this.ReadSharedString(
                            memory,
                            checked(address + EffectDescriptionFormat))),
                    NameValues = ReadFloatVector(
                        memory,
                        checked(address + EffectNameValues)),
                    DescriptionValues = ReadFloatVector(
                        memory,
                        checked(address + EffectDescriptionValues)),
                    Field50 = field50,
                    Field54 = field54,
                });
        }

        return effects;
    }

    private string ReadSharedString(
        ProcessMemoryReader memory,
        uint address)
    {
        if (!memory.TryReadUInt32(
                checked(address + SharedStringBuffer),
                out var bufferAddress) ||
            !memory.TryReadUInt32(
                checked(address + SharedStringLength),
                out var length) ||
            length == 0)
        {
            return "";
        }

        if (bufferAddress == 0 ||
            length > MaximumSharedStringLength ||
            !memory.TryReadBytes(
                bufferAddress,
                checked((int)length),
                out var bytes))
        {
            return "";
        }

        return Encoding.Latin1.GetString(bytes);
    }

    private static IReadOnlyList<float> ReadFloatVector(
        ProcessMemoryReader memory,
        uint vectorAddress)
    {
        if (!TryReadObjectVector(
                memory,
                vectorAddress,
                sizeof(float),
                MaximumEffectValueCount,
                out var begin,
                out var count) ||
            count == 0 ||
            !memory.TryReadBytes(
                begin,
                checked(count * sizeof(float)),
                out var bytes))
        {
            return [];
        }

        var values = new float[count];

        for (var index = 0; index < count; index++)
        {
            var value = BitConverter.ToSingle(
                bytes,
                index * sizeof(float));

            values[index] = float.IsFinite(value)
                ? value
                : 0.0f;
        }

        return values;
    }

    private static bool TryReadObjectVector(
        ProcessMemoryReader memory,
        uint vectorAddress,
        int elementSize,
        int maximumCount,
        out uint begin,
        out int count)
    {
        return TryReadFlatVector(
            memory,
            vectorAddress,
            VectorBegin,
            VectorEnd,
            elementSize,
            maximumCount,
            out begin,
            out count);
    }

    private static bool TryReadFlatVector(
        ProcessMemoryReader memory,
        uint ownerAddress,
        uint beginOffset,
        uint endOffset,
        int elementSize,
        int maximumCount,
        out uint begin,
        out int count)
    {
        begin = 0;
        count = 0;

        if (!memory.TryReadUInt32(
                checked(ownerAddress + beginOffset),
                out begin) ||
            !memory.TryReadUInt32(
                checked(ownerAddress + endOffset),
                out var end))
        {
            return false;
        }

        if (begin == 0 && end == 0)
        {
            return true;
        }

        if (begin == 0 ||
            end < begin)
        {
            return false;
        }

        var byteLength = end - begin;

        if (byteLength % (uint)elementSize != 0)
        {
            return false;
        }

        var unsignedCount = byteLength / (uint)elementSize;

        if (unsignedCount > (uint)maximumCount)
        {
            return false;
        }

        count = checked((int)unsignedCount);
        return true;
    }

    private static string ResolveTypeDisplayName(
        IReadOnlyList<ClientRuntimeItemAttributeObservation> attributes,
        int itemType)
    {
        var observed = attributes
            .FirstOrDefault(attribute =>
                attribute.ItemInfoId == ItemTypeNameAttributeId &&
                !string.IsNullOrWhiteSpace(attribute.StringValue))
            ?.StringValue;

        if (!string.IsNullOrWhiteSpace(observed))
        {
            const string techSuffix = " Tech";

            return observed.EndsWith(
                    techSuffix,
                    StringComparison.OrdinalIgnoreCase)
                ? observed[..^techSuffix.Length]
                : observed;
        }

        return itemType switch
        {
            2 => "Shield",
            6 => "Engine",
            7 => "Reactor",
            11 => "Device",
            14 => "Beam Weapon",
            16 => "Projectile Weapon",
            _ => "",
        };
    }

    private static string FirstNonEmpty(
        params string[] values)
    {
        return values.FirstOrDefault(value =>
            !string.IsNullOrWhiteSpace(value)) ?? "";
    }

    private static string NormalizeText(string value)
    {
        return value
            .Replace("\\n", "\n", StringComparison.Ordinal)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Trim();
    }

    private static uint ReadUInt32(
        byte[] bytes,
        uint offset)
    {
        return BitConverter.ToUInt32(
            bytes,
            checked((int)offset));
    }

    private static bool TryMultiply(
        uint value,
        uint multiplier,
        out uint result)
    {
        try
        {
            result = checked(value * multiplier);
            return true;
        }
        catch (OverflowException)
        {
            result = 0;
            return false;
        }
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

    private readonly record struct RuntimeTemplateCacheKey(
        int ProcessId,
        uint ModuleBaseAddress,
        uint ClientContextAddress,
        int ItemTemplateId);
}
