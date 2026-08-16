namespace Net7ClientManager.Observations.Observers;

using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Net7ClientManager.Observations.Models;

internal static class ClientItemTemplateCatalog
{
    private const int MaximumRecordCount = 100_000;
    private const int MaximumCatalogStringLength = 16_384;
    private const int MaximumCatalogStringFieldCount = 32;
    private const int ModelBassetIdOffsetBeforeMetadata = 22;
    private const int IconBassetIdOffsetBeforeMetadata = 20;
    private const int CoreMetadataLength = 22;
    private const int EffectVectorMetadataLength = 16;
    private const int MaximumEffectCount = 256;
    private const int MaximumEffectValueCount = 128;

    private static readonly object stateLock = new();
    private static readonly object refreshLock = new();

    private static ClientItemTemplateCatalogState? currentState;
    private static string? preferredCatalogPath;

    internal static void ConfigurePreferredCatalogPath(string? path)
    {
        var normalizedPath = string.IsNullOrWhiteSpace(path)
            ? null
            : path.Trim().Trim('"');

        Volatile.Write(
            ref preferredCatalogPath,
            normalizedPath);
    }

    internal static ClientItemTemplateCatalogSnapshot GetSnapshot()
    {
        return GetState().Snapshot;
    }

    internal static ClientItemTemplateCatalogRefreshResult RefreshIfChanged()
    {
        lock (refreshLock)
        {
            var previous = GetState();
            var sourcePath = FindCatalogPath();

            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                return new ClientItemTemplateCatalogRefreshResult
                {
                    Status = previous.Status,
                    Snapshot = previous.Snapshot,
                };
            }

            FileInfo sourceInfo;
            string normalizedPath;

            try
            {
                normalizedPath = Path.GetFullPath(sourcePath);
                sourceInfo = new FileInfo(normalizedPath);
            }
            catch (Exception exception)
            {
                return new ClientItemTemplateCatalogRefreshResult
                {
                    InputChanged = true,
                    Status =
                        $"Could not inspect cdata.dat at '{sourcePath}': {exception.Message}",
                    Snapshot = previous.Snapshot,
                };
            }

            var unchanged =
                string.Equals(
                    normalizedPath,
                    previous.SourcePath,
                    StringComparison.OrdinalIgnoreCase) &&
                sourceInfo.Exists &&
                sourceInfo.Length == previous.FileLength &&
                sourceInfo.LastWriteTimeUtc == previous.LastWriteTimeUtc;

            if (unchanged)
            {
                return new ClientItemTemplateCatalogRefreshResult
                {
                    Status = previous.Status,
                    Snapshot = previous.Snapshot,
                };
            }

            // Parse outside stateLock so ordinary item lookups continue using
            // the previous complete catalog while the replacement is built.
            var candidate = LoadCatalog(normalizedPath);

            if (!candidate.Snapshot.IsAvailable)
            {
                return new ClientItemTemplateCatalogRefreshResult
                {
                    InputChanged = true,
                    Status = candidate.Status,
                    Snapshot = previous.Snapshot,
                };
            }

            lock (stateLock)
            {
                currentState = candidate;
            }

            return new ClientItemTemplateCatalogRefreshResult
            {
                InputChanged = true,
                Applied = true,
                Status = candidate.Status,
                Snapshot = candidate.Snapshot,
            };
        }
    }

    private static ClientItemTemplateCatalogState GetState()
    {
        lock (stateLock)
        {
            return currentState ??= LoadCatalog();
        }
    }

    internal static bool TryGetDefinition(
        int itemTemplateId,
        out ClientItemTemplateDefinition definition)
    {
        if (GetState().Definitions.TryGetValue(
                itemTemplateId,
                out var observedDefinition))
        {
            definition = observedDefinition;
            return true;
        }

        definition = null!;
        return false;
    }

    internal static bool TryGetRuntimeObservation(
        int itemTemplateId,
        out ClientRuntimeItemTemplateObservation observation)
    {
        if (TryGetDefinition(
                itemTemplateId,
                out var definition) &&
            definition.RuntimeObservation.IsAvailable)
        {
            observation = definition.RuntimeObservation;
            return true;
        }

        observation = null!;
        return false;
    }

    public static string? GetKnownName(
        int itemTemplateId)
    {
        if (TryGetDefinition(
                itemTemplateId,
                out var definition) &&
            !string.IsNullOrWhiteSpace(
                definition.Name))
        {
            return definition.Name;
        }

        return null;
    }

    internal static IReadOnlyList<ClientItemTemplateDefinition>
        GetKnownDefinitions()
    {
        return GetState().Definitions.Values
            .Where(definition =>
                definition.RuntimeObservation.IsAvailable &&
                !string.IsNullOrWhiteSpace(definition.Name))
            .OrderBy(
                definition => definition.Name,
                StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static ClientItemTemplateCatalogState LoadCatalog(
        string? requestedPath = null)
    {
        var path = requestedPath ?? FindCatalogPath();

        if (path == null)
        {
            return ClientItemTemplateCatalogState.Unavailable(
                "cdata.dat was not found; using the evidence-backed fallback names");
        }

        try
        {
            var normalizedPath = Path.GetFullPath(path);
            var sourceInfoBeforeRead = new FileInfo(normalizedPath);
            var expectedLength = sourceInfoBeforeRead.Length;
            var expectedLastWriteTimeUtc =
                sourceInfoBeforeRead.LastWriteTimeUtc;
            var data = File.ReadAllBytes(normalizedPath);
            var sourceInfo = new FileInfo(normalizedPath);

            if (data.LongLength != expectedLength ||
                sourceInfo.Length != expectedLength ||
                sourceInfo.LastWriteTimeUtc != expectedLastWriteTimeUtc)
            {
                return ClientItemTemplateCatalogState.Unavailable(
                    $"cdata.dat at '{normalizedPath}' changed while it was being read",
                    normalizedPath);
            }

            var sha256 = Convert
                .ToHexString(SHA256.HashData(data))
                .ToLowerInvariant();

            if (data.Length < 12)
            {
                return ClientItemTemplateCatalogState.Unavailable(
                    $"cdata.dat at '{path}' is too small",
                    path);
            }

            var dataStart = DecodeInt32(
                data.AsSpan(0, sizeof(int)));

            if (dataStart <= 0 ||
                dataStart >= data.Length - 8)
            {
                return ClientItemTemplateCatalogState.Unavailable(
                    string.Create(CultureInfo.InvariantCulture, $"cdata.dat at '{path}' has invalid data start {dataStart}"),
                    path);
            }

            var entryCount = DecodeInt32(
                data.AsSpan(
                    checked(dataStart + 4),
                    sizeof(int)));

            if (entryCount is <= 0 or > MaximumRecordCount)
            {
                return ClientItemTemplateCatalogState.Unavailable(
                    string.Create(CultureInfo.InvariantCulture, $"cdata.dat at '{path}' has invalid entry count {entryCount}"),
                    path);
            }

            var tableOffset = checked(
                dataStart + 8);

            var tableLength = checked(
                entryCount * 8);

            if (tableOffset + tableLength > data.Length)
            {
                return ClientItemTemplateCatalogState.Unavailable(
                    $"cdata.dat index table runs past the end of '{path}'",
                    path);
            }

            List<ClientCDataIndexEntry> entries =
                new(entryCount);
            List<int> failedItemTemplateIds = [];

            for (var index = 0;
                 index < entryCount;
                 index++)
            {
                var encodedPair = data.AsSpan(
                    checked(tableOffset + index * 8),
                    8);

                var decodedPair = DecodeBytes(
                    encodedPair);

                var id = BinaryPrimitives.ReadInt32LittleEndian(
                    decodedPair.AsSpan(0, 4));

                var offset = BinaryPrimitives.ReadInt32LittleEndian(
                    decodedPair.AsSpan(4, 4));

                if (offset < 0)
                {
                    failedItemTemplateIds.Add(id);
                    continue;
                }

                entries.Add(
                    new ClientCDataIndexEntry(
                        index,
                        id,
                        offset));
            }

            var sortedByOffset = entries
                .OrderBy(
                    entry => entry.Offset)
                .ToArray();

            Dictionary<int, ClientItemTemplateDefinition>
                definitions = [];

            for (var sortedIndex = 0;
                 sortedIndex < sortedByOffset.Length;
                 sortedIndex++)
            {
                var entry = sortedByOffset[sortedIndex];

                var nextOffset = sortedIndex + 1 <
                    sortedByOffset.Length
                        ? sortedByOffset[sortedIndex + 1].Offset
                        : dataStart - 4;

                var recordLength =
                    nextOffset - entry.Offset;

                if (recordLength <= 0)
                {
                    failedItemTemplateIds.Add(entry.Id);
                    continue;
                }

                var absoluteOffset = checked(
                    4 + entry.Offset);

                if (absoluteOffset < 0 ||
                    absoluteOffset + recordLength > data.Length)
                {
                    failedItemTemplateIds.Add(entry.Id);
                    continue;
                }

                var decodedRecord = DecodeBytes(
                    data.AsSpan(
                        absoluteOffset,
                        recordLength));

                if (!TryParseTemplateDefinition(
                        entry,
                        decodedRecord,
                        out var definition))
                {
                    failedItemTemplateIds.Add(entry.Id);
                    continue;
                }

                definitions[entry.Id] = definition;
            }

            var loadedAtUtc = DateTimeOffset.UtcNow;
            var status = string.Create(
                CultureInfo.InvariantCulture,
                $"Loaded {definitions.Count} item templates from {normalizedPath}");

            var snapshot = new ClientItemTemplateCatalogSnapshot
            {
                IsAvailable = definitions.Count != 0,
                Status = status,
                SourcePath = normalizedPath,
                FileLength = sourceInfo.Length,
                LastWriteTimeUtc = new DateTimeOffset(sourceInfo.LastWriteTimeUtc),
                Sha256 = sha256,
                LoadedAtUtc = loadedAtUtc,
                IndexEntryCount = entryCount,
                FailedItemTemplateIds =
                    Array.AsReadOnly(
                        failedItemTemplateIds
                            .Distinct()
                            .Order()
                            .ToArray()),
                Templates =
                    Array.AsReadOnly(
                        definitions.Values
                            .OrderBy(definition => definition.Id)
                            .Select(definition =>
                                new ClientItemTemplateCatalogEntry
                                {
                                    ItemTemplateId = definition.Id,
                                    ModelBassetId = definition.ModelBassetId,
                                    IconBassetId = definition.IconBassetId,
                                    AdditionalText =
                                        Array.AsReadOnly(
                                            definition.AdditionalText
                                                .ToArray()),
                                    Template =
                                        definition.RuntimeObservation,
                                })
                            .ToArray()),
            };

            return new ClientItemTemplateCatalogState(
                normalizedPath,
                sourceInfo.Length,
                sourceInfo.LastWriteTimeUtc,
                definitions,
                status,
                snapshot);
        }
        catch (Exception exception)
        {
            return ClientItemTemplateCatalogState.Unavailable(
                $"Could not load cdata.dat at '{path}': {exception.Message}",
                path);
        }
    }

    private static bool TryParseTemplateDefinition(
        ClientCDataIndexEntry entry,
        byte[] decodedRecord,
        out ClientItemTemplateDefinition definition)
    {
        definition = null!;

        if (decodedRecord.Length < 16 + CoreMetadataLength ||
            !TryFindTailStringChain(
                decodedRecord,
                out var chain))
        {
            return false;
        }

        var values = chain.Values
            .SkipWhile(
                string.IsNullOrEmpty)
            .Reverse()
            .SkipWhile(
                string.IsNullOrEmpty)
            .Reverse()
            .ToArray();

        if (values.Length == 0 ||
            string.IsNullOrWhiteSpace(values[0]) ||
            !TryParseRuntimeObservation(
                entry,
                decodedRecord,
                chain.StartOffset,
                values,
                out var runtimeObservation))
        {
            return false;
        }

        var embeddedId = BinaryPrimitives.ReadInt32BigEndian(
            decodedRecord.AsSpan(0, sizeof(int)));
        var modelBassetId = TryReadBassetId(
            decodedRecord,
            chain.StartOffset - ModelBassetIdOffsetBeforeMetadata);
        var iconBassetId = TryReadBassetId(
            decodedRecord,
            chain.StartOffset - IconBassetIdOffsetBeforeMetadata);

        definition = new ClientItemTemplateDefinition
        {
            Id = entry.Id,
            EmbeddedId = embeddedId,
            RecordIndex = entry.Index,
            RecordOffset = entry.Offset,
            MetadataOffset = chain.StartOffset,
            ModelBassetId = modelBassetId,
            IconBassetId = iconBassetId,
            Name = runtimeObservation.Name,
            Description = runtimeObservation.Description,
            Manufacturer = runtimeObservation.Manufacturer,
            AdditionalText = values.Length > 3
                ? [
                    .. values
                        .Skip(3)
                        .Select(NormalizeCatalogText),
                ]
                : [],
            RuntimeObservation = runtimeObservation,
        };

        return true;
    }

    private static bool TryParseRuntimeObservation(
        ClientCDataIndexEntry entry,
        byte[] record,
        int metadataOffset,
        IReadOnlyList<string> strings,
        out ClientRuntimeItemTemplateObservation observation)
    {
        observation = null!;
        var offset = 0;

        if (!TryReadUInt32BigEndian(record, ref offset, out var embeddedId) ||
            unchecked((int)embeddedId) != entry.Id ||
            !TryReadInt32LittleEndian(record, ref offset, out var category) ||
            !TryReadInt32LittleEndian(record, ref offset, out var subcategory) ||
            !TryReadInt32LittleEndian(record, ref offset, out var itemType) ||
            !TryReadAttributes(record, ref offset, out var attributes) ||
            !TryReadEffectVector(record, ref offset, out var activatedEffects) ||
            !TryReadEffectVector(record, ref offset, out var equippedEffects) ||
            offset + CoreMetadataLength != metadataOffset ||
            !TryReadUInt16BigEndian(record, ref offset, out var gameBasset) ||
            !TryReadUInt16BigEndian(record, ref offset, out _) ||
            !TryReadUInt16BigEndian(record, ref offset, out var techLevel) ||
            !TryReadUInt32BigEndian(record, ref offset, out var cost) ||
            !TryReadUInt32BigEndian(record, ref offset, out var maxStack) ||
            !TryReadUInt32BigEndian(record, ref offset, out _) ||
            !TryReadUInt32LittleEndian(record, ref offset, out var flags) ||
            offset != metadataOffset)
        {
            return false;
        }

        var name = strings.Count > 0
            ? NormalizeCatalogText(strings[0])
            : "";
        var description = strings.Count > 1
            ? NormalizeCatalogText(strings[1])
            : "";
        var manufacturer = strings.Count > 2
            ? NormalizeCatalogText(strings[2])
            : "";

        observation = new ClientRuntimeItemTemplateObservation
        {
            IsAvailable = true,
            Status = "Static item template resolved from cdata.dat",
            ItemTemplateId = entry.Id,
            Category = category,
            Subcategory = subcategory,
            ItemType = itemType,
            Name = name,
            Description = description,
            Manufacturer = manufacturer,
            TypeDisplayName = ResolveTypeDisplayName(attributes, itemType),
            GameBasset = gameBasset,
            TechLevel = techLevel,
            Cost = cost,
            MaxStack = maxStack,
            Flags = flags,
            Attributes = attributes,
            ActivatedEffects = activatedEffects,
            EquippedEffects = equippedEffects,
        };

        return true;
    }

    private static bool TryReadAttributes(
        byte[] record,
        ref int offset,
        out IReadOnlyList<ClientRuntimeItemAttributeObservation> attributes)
    {
        attributes = [];

        if (!TryReadByte(record, ref offset, out var count))
        {
            return false;
        }

        List<ClientRuntimeItemAttributeObservation> result = new(count);

        for (var index = 0; index < count; index++)
        {
            if (!TryReadUInt32LittleEndian(
                    record,
                    ref offset,
                    out var itemInfoId))
            {
                return false;
            }

            var typeCode = ResolveItemFieldType(itemInfoId);

            switch (typeCode)
            {
                case 1:
                    if (!TryReadUInt32LittleEndian(
                            record,
                            ref offset,
                            out var intRaw))
                    {
                        return false;
                    }

                    result.Add(
                        new ClientRuntimeItemAttributeObservation
                        {
                            ItemInfoId = itemInfoId,
                            TypeCode = typeCode,
                            RawValue = intRaw,
                            Int32Value = unchecked((int)intRaw),
                        });
                    break;

                case 2:
                    if (!TryReadUInt32LittleEndian(
                            record,
                            ref offset,
                            out var floatRaw))
                    {
                        return false;
                    }

                    var floatValue = BitConverter.Int32BitsToSingle(
                        unchecked((int)floatRaw));

                    result.Add(
                        new ClientRuntimeItemAttributeObservation
                        {
                            ItemInfoId = itemInfoId,
                            TypeCode = typeCode,
                            RawValue = floatRaw,
                            FloatValue = float.IsFinite(floatValue)
                                ? floatValue
                                : null,
                        });
                    break;

                case 4:
                    if (!TryReadCatalogString(
                            record,
                            ref offset,
                            out var stringValue))
                    {
                        return false;
                    }

                    result.Add(
                        new ClientRuntimeItemAttributeObservation
                        {
                            ItemInfoId = itemInfoId,
                            TypeCode = typeCode,
                            StringValue = NormalizeCatalogText(stringValue),
                        });
                    break;

                default:
                    return false;
            }
        }

        attributes = result;
        return true;
    }

    private static bool TryReadEffectVector(
        byte[] record,
        ref int offset,
        out IReadOnlyList<ClientRuntimeItemEffectObservation> effects)
    {
        effects = [];

        if (!TryReadUInt32BigEndian(
                record,
                ref offset,
                out var unsignedCount) ||
            unsignedCount > MaximumEffectCount)
        {
            return false;
        }

        var count = checked((int)unsignedCount);
        List<ClientRuntimeItemEffectObservation> result = new(count);

        for (var index = 0; index < count; index++)
        {
            if (!TryReadEffect(
                    record,
                    ref offset,
                    index,
                    out var effect))
            {
                return false;
            }

            result.Add(effect);
        }

        if (count != 0 &&
            !TrySkip(record, ref offset, EffectVectorMetadataLength))
        {
            return false;
        }

        effects = result;
        return true;
    }

    private static bool TryReadEffect(
        byte[] record,
        ref int offset,
        int index,
        out ClientRuntimeItemEffectObservation effect)
    {
        effect = null!;

        if (!TryReadCatalogString(
                record,
                ref offset,
                out var sourceOrIdentity) ||
            !TryReadCatalogString(
                record,
                ref offset,
                out var nameFormat) ||
            !TryReadCatalogString(
                record,
                ref offset,
                out var descriptionFormat) ||
            !TryReadFloatVector(
                record,
                ref offset,
                out var nameValues) ||
            !TryReadFloatVector(
                record,
                ref offset,
                out var descriptionValues) ||
            !TryReadUInt32LittleEndian(
                record,
                ref offset,
                out var field50) ||
            !TryReadUInt32LittleEndian(
                record,
                ref offset,
                out var field54))
        {
            return false;
        }

        effect = new ClientRuntimeItemEffectObservation
        {
            Index = index,
            SourceOrIdentity = NormalizeCatalogText(sourceOrIdentity),
            NameFormat = NormalizeCatalogText(nameFormat),
            DescriptionFormat = NormalizeCatalogText(descriptionFormat),
            NameValues = nameValues,
            DescriptionValues = descriptionValues,
            Field50 = field50,
            Field54 = field54,
        };

        return true;
    }

    private static bool TryReadFloatVector(
        byte[] record,
        ref int offset,
        out IReadOnlyList<float> values)
    {
        values = [];

        if (!TryReadUInt32BigEndian(
                record,
                ref offset,
                out var unsignedCount) ||
            unsignedCount > MaximumEffectValueCount)
        {
            return false;
        }

        var count = checked((int)unsignedCount);
        var result = new float[count];

        for (var index = 0; index < count; index++)
        {
            if (!TryReadUInt32BigEndian(
                    record,
                    ref offset,
                    out var rawValue))
            {
                return false;
            }

            var value = BitConverter.Int32BitsToSingle(
                unchecked((int)rawValue));
            result[index] = float.IsFinite(value)
                ? value
                : 0.0f;
        }

        values = result;
        return true;
    }

    private static uint ResolveItemFieldType(uint itemInfoId)
    {
        return itemInfoId switch
        {
            0x01 or 0x0b or 0x0d or 0x1b => 4,
            0x09 or 0x14 or 0x15 or 0x1a or 0x22 or 0x25 => 2,
            <= 0x25 => 1,
            _ => 0,
        };
    }

    private static string ResolveTypeDisplayName(
        IReadOnlyList<ClientRuntimeItemAttributeObservation> attributes,
        int itemType)
    {
        var observed = attributes
            .FirstOrDefault(attribute =>
                attribute.ItemInfoId == 0x0b &&
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

    private static bool TryReadByte(
        byte[] record,
        ref int offset,
        out byte value)
    {
        if (offset < 0 || offset >= record.Length)
        {
            value = 0;
            return false;
        }

        value = record[offset++];
        return true;
    }

    private static bool TryReadUInt16BigEndian(
        byte[] record,
        ref int offset,
        out ushort value)
    {
        if (!TryTake(record, ref offset, sizeof(ushort), out var bytes))
        {
            value = 0;
            return false;
        }

        value = BinaryPrimitives.ReadUInt16BigEndian(bytes);
        return true;
    }

    private static bool TryReadUInt32BigEndian(
        byte[] record,
        ref int offset,
        out uint value)
    {
        if (!TryTake(record, ref offset, sizeof(uint), out var bytes))
        {
            value = 0;
            return false;
        }

        value = BinaryPrimitives.ReadUInt32BigEndian(bytes);
        return true;
    }

    private static bool TryReadInt32LittleEndian(
        byte[] record,
        ref int offset,
        out int value)
    {
        if (!TryTake(record, ref offset, sizeof(int), out var bytes))
        {
            value = 0;
            return false;
        }

        value = BinaryPrimitives.ReadInt32LittleEndian(bytes);
        return true;
    }

    private static bool TryReadUInt32LittleEndian(
        byte[] record,
        ref int offset,
        out uint value)
    {
        if (!TryTake(record, ref offset, sizeof(uint), out var bytes))
        {
            value = 0;
            return false;
        }

        value = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        return true;
    }

    private static bool TryReadCatalogString(
        byte[] record,
        ref int offset,
        out string value)
    {
        value = "";

        if (!TryTake(record, ref offset, sizeof(ushort), out var lengthBytes))
        {
            return false;
        }

        var length = BinaryPrimitives.ReadUInt16LittleEndian(lengthBytes);

        if (length > MaximumCatalogStringLength ||
            !TryTake(record, ref offset, length, out var bytes) ||
            !IsCatalogTextOrTerminator(bytes))
        {
            return false;
        }

        value = Encoding.Latin1
            .GetString(bytes)
            .TrimEnd('\0');
        return true;
    }

    private static bool TryTake(
        byte[] record,
        ref int offset,
        int count,
        out ReadOnlySpan<byte> bytes)
    {
        if (count < 0 ||
            offset < 0 ||
            offset > record.Length - count)
        {
            bytes = default;
            return false;
        }

        bytes = record.AsSpan(offset, count);
        offset += count;
        return true;
    }

    private static bool TrySkip(
        byte[] record,
        ref int offset,
        int count)
    {
        return TryTake(record, ref offset, count, out _);
    }

    private static bool IsCatalogTextOrTerminator(
        ReadOnlySpan<byte> bytes)
    {
        foreach (var value in bytes)
        {
            if (value is 0 or 9 or 10 or 13)
            {
                continue;
            }

            if (value < 32)
            {
                return false;
            }
        }

        return true;
    }

    private static int? TryReadBassetId(
        byte[] record,
        int offset)
    {
        if (offset < 0 ||
            offset > record.Length - sizeof(ushort))
        {
            return null;
        }

        var value = BinaryPrimitives.ReadUInt16BigEndian(
            record.AsSpan(offset, sizeof(ushort)));

        return value == ushort.MaxValue
            ? null
            : value;
    }

    private static bool TryFindTailStringChain(
        byte[] record,
        out ClientCDataStringChain best)
    {
        best = default;
        var found = false;

        for (var startOffset = 0;
             startOffset <= record.Length - sizeof(ushort);
             startOffset++)
        {
            var position = startOffset;
            List<string> values = [];
            var nonEmptyCount = 0;
            var textLength = 0;

            while (position + sizeof(ushort) <=
                   record.Length &&
                   values.Count < MaximumCatalogStringFieldCount)
            {
                var length =
                    BinaryPrimitives.ReadUInt16LittleEndian(
                        record.AsSpan(
                            position,
                            sizeof(ushort)));

                position += sizeof(ushort);

                if (length > MaximumCatalogStringLength ||
                    position + length > record.Length)
                {
                    break;
                }

                var bytes = record.AsSpan(
                    position,
                    length);

                if (!IsCatalogText(bytes))
                {
                    break;
                }

                var value = Encoding.Latin1.GetString(
                    bytes);

                values.Add(value);

                if (length > 0)
                {
                    nonEmptyCount++;
                    textLength += length;
                }

                position += length;

                if (position != record.Length)
                {
                    continue;
                }

                if (nonEmptyCount == 0)
                {
                    break;
                }

                var candidate = new ClientCDataStringChain(
                    startOffset,
                    values,
                    nonEmptyCount,
                    textLength);

                if (!found ||
                    IsBetterTailStringChain(
                        candidate,
                        best))
                {
                    best = candidate;
                    found = true;
                }

                break;
            }
        }

        return found;
    }

    private static bool IsBetterTailStringChain(
        ClientCDataStringChain candidate,
        ClientCDataStringChain current)
    {
        if (candidate.NonEmptyCount !=
            current.NonEmptyCount)
        {
            return candidate.NonEmptyCount >
                   current.NonEmptyCount;
        }

        if (candidate.TextLength !=
            current.TextLength)
        {
            return candidate.TextLength >
                   current.TextLength;
        }

        // Zero-length fields can make the same valid chain appear to start a
        // few bytes earlier. Prefer the latest equivalent start so Name is
        // the first actual field rather than a leading empty placeholder.
        return candidate.StartOffset >
               current.StartOffset;
    }

    private static bool IsCatalogText(
        ReadOnlySpan<byte> bytes)
    {
        foreach (var value in bytes)
        {
            if (value is 9 or 10 or 13)
            {
                continue;
            }

            if (value < 32)
            {
                return false;
            }
        }

        return true;
    }

    private static string NormalizeCatalogText(
        string value)
    {
        return value
            .Replace(
                "\\n",
                Environment.NewLine,
                StringComparison.Ordinal)
            .Trim();
    }

    private static string? FindCatalogPath()
    {
        List<string> candidates = [];

        var environmentPath =
            Environment.GetEnvironmentVariable(
                "NET7_CDATA_PATH");

        if (!string.IsNullOrWhiteSpace(
                environmentPath))
        {
            candidates.Add(
                environmentPath.Trim().Trim('"'));
        }

        var pathFile = Path.Combine(
            AppContext.BaseDirectory,
            "cdata-path.txt");

        if (File.Exists(pathFile))
        {
            var configuredPath = File
                .ReadAllText(pathFile)
                .Trim()
                .Trim('"');

            if (!string.IsNullOrWhiteSpace(
                    configuredPath))
            {
                candidates.Add(configuredPath);
            }
        }

        var configuredLauncherCatalogPath =
            Volatile.Read(ref preferredCatalogPath);

        if (!string.IsNullOrWhiteSpace(
                configuredLauncherCatalogPath))
        {
            candidates.Add(
                configuredLauncherCatalogPath);
        }

        candidates.Add(
            Path.Combine(
                AppContext.BaseDirectory,
                "cdata.dat"));

        var programFilesX86 = Environment.GetFolderPath(
            Environment.SpecialFolder.ProgramFilesX86);

        if (!string.IsNullOrWhiteSpace(programFilesX86))
        {
            candidates.Add(
                Path.Combine(
                    programFilesX86,
                    "Net-7",
                    "bin",
                    "cdata.dat"));
        }

        var programFiles = Environment.GetFolderPath(
            Environment.SpecialFolder.ProgramFiles);

        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            candidates.Add(
                Path.Combine(
                    programFiles,
                    "Net-7",
                    "bin",
                    "cdata.dat"));
        }

        candidates.Add(
            @"C:\Games\Net-7\bin\cdata.dat");

        candidates.Add(
            @"C:\Net-7\bin\cdata.dat");

        return candidates
            .Distinct(
                StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(
                File.Exists);
    }

    private static int DecodeInt32(
        ReadOnlySpan<byte> encoded)
    {
        var decoded = DecodeBytes(
            encoded);

        return BinaryPrimitives.ReadInt32LittleEndian(
            decoded);
    }

    private static byte[] DecodeBytes(
        ReadOnlySpan<byte> source)
    {
        var result = new byte[source.Length];

        for (var index = 0;
             index < source.Length;
             index++)
        {
            result[index] = unchecked(
                (byte)(
                    source[source.Length - 1 - index] +
                    index +
                    'j'));
        }

        return result;
    }

    private sealed record ClientItemTemplateCatalogState(
        string? SourcePath,
        long FileLength,
        DateTime LastWriteTimeUtc,
        IReadOnlyDictionary<int, ClientItemTemplateDefinition>
            Definitions,
        string Status,
        ClientItemTemplateCatalogSnapshot Snapshot)
    {
        public static ClientItemTemplateCatalogState Unavailable(
            string status,
            string? sourcePath = null)
        {
            return new ClientItemTemplateCatalogState(
                sourcePath,
                0,
                default,
                new Dictionary<int, ClientItemTemplateDefinition>(),
                status,
                new ClientItemTemplateCatalogSnapshot
                {
                    Status = status,
                    SourcePath = sourcePath,
                    LoadedAtUtc = DateTimeOffset.UtcNow,
                });
        }
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct ClientCDataIndexEntry(
        int Index,
        int Id,
        int Offset);

    private readonly record struct ClientCDataStringChain(
        int StartOffset,
        IReadOnlyList<string> Values,
        int NonEmptyCount,
        int TextLength);
}
