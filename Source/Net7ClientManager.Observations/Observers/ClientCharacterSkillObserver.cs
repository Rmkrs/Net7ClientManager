// ReSharper disable GrammarMistakeInComment
namespace Net7ClientManager.Observations.Observers;

using System.Globalization;
using System.Runtime.InteropServices;
using Net7ClientManager.Observations.Models;

internal sealed class ClientCharacterSkillObserver
{
    /*
     * The static client catalog at DAT_00BE8E2C..DAT_00BE8E30 is the
     * canonical index-to-definition map. The live Hull.RPGInfo.Skills
     * vector uses the same indices.
     *
     * Do not use Hull.RPGInfo.Skills +0x84 as the collection count. It
     * remains 48 for legacy reasons while the active vector exposes 64
     * wrappers and the current static catalog contains 61 definitions.
     */
    private const uint ImageBase = 0x00400000;

    private const uint StaticSkillCatalogBeginStatic =
        0x00be8e2c;

    private const uint StaticSkillCatalogEndStatic =
        0x00be8e30;

    private const uint StaticSkillCatalogBeginRva =
        StaticSkillCatalogBeginStatic - ImageBase;

    private const uint StaticSkillCatalogEndRva =
        StaticSkillCatalogEndStatic - ImageBase;

    private const uint SkillDefinitionName = 0x00;
    private const uint SkillDefinitionCategory = 0x08;
    private const uint SkillDefinitionSelectable = 0x10;

    private const uint AuxDataPropertyTypeDescriptor = 0x04;
    private const uint AuxDataPropertyQualifiedName = 0x3c;
    private const uint AuxDataPropertyValid = 0x70;
    private const uint AuxDataPropertyValue = 0x84;

    private const uint AuxDataPropertyVectorBegin = 0x88;
    private const uint AuxDataPropertyVectorEnd = 0x8c;
    private const uint AuxDataPropertyVectorCapacity = 0x90;

    private const int MaximumSkillDefinitionCount = 256;
    private const int MaximumLiveSkillSlotCount = 256;
    private const int MaximumSkillChildPropertyCount = 32;
    private const int MaximumSkillNameLength = 160;
    private const int MaximumCategoryLength = 80;

    private static readonly TimeSpan fallbackRefreshInterval =
        TimeSpan.FromSeconds(30);

    private readonly Dictionary<int, CachedSkillState>
        cachedStates = [];

    public ClientCharacterSkillsObservation Observe(
        ProcessMemoryReader memory,
        uint moduleBaseAddress,
        int processId,
        uint skillsPropertyAddress,
        uint int32PropertyTypeDescriptor,
        int? availableSkillPoints)
    {
        if (skillsPropertyAddress == 0)
        {
            return ClientCharacterSkillsObservation.Unavailable(
                "Hull.RPGInfo.Skills property is unavailable");
        }

        if (!TryReadStaticCatalogBounds(
                memory,
                moduleBaseAddress,
                out var staticCatalogBegin,
                out var staticCatalogEnd,
                out var staticDefinitionCount,
                out var staticBoundsError))
        {
            this.cachedStates.Remove(
                processId);

            return ClientCharacterSkillsObservation.Unavailable(
                staticBoundsError);
        }

        if (!TryReadVectorBounds(
                memory,
                skillsPropertyAddress,
                MaximumLiveSkillSlotCount,
                "live RPG skill wrapper vector",
                out var liveVectorBegin,
                out var liveVectorEnd,
                out _,
                out var liveSlotCount,
                out var liveBoundsError))
        {
            this.cachedStates.Remove(
                processId);

            return ClientCharacterSkillsObservation.Unavailable(
                liveBoundsError);
        }

        if (staticDefinitionCount > liveSlotCount)
        {
            this.cachedStates.Remove(
                processId);

            return ClientCharacterSkillsObservation.Unavailable(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Static skill catalog contains {staticDefinitionCount} definitions but the live vector exposes only {liveSlotCount} slots"));
        }

        if (!this.cachedStates.TryGetValue(
                processId,
                out var cachedState) ||
            !cachedState.Matches(
                moduleBaseAddress,
                skillsPropertyAddress,
                staticCatalogBegin,
                staticCatalogEnd,
                liveVectorBegin,
                liveVectorEnd,
                int32PropertyTypeDescriptor))
        {
            if (!TryBuildCachedState(
                    memory,
                    moduleBaseAddress,
                    skillsPropertyAddress,
                    staticCatalogBegin,
                    staticCatalogEnd,
                    staticDefinitionCount,
                    liveVectorBegin,
                    liveVectorEnd,
                    liveSlotCount,
                    int32PropertyTypeDescriptor,
                    out cachedState,
                    out var buildError))
            {
                this.cachedStates.Remove(
                    processId);

                return ClientCharacterSkillsObservation.Unavailable(
                    buildError);
            }

            this.cachedStates[processId] =
                cachedState;
        }

        var now = DateTimeOffset.UtcNow;

        if (cachedState.LastObservation.IsAvailable &&
            cachedState.LastAvailableSkillPoints ==
                availableSkillPoints &&
            now - cachedState.LastObservedAt <
                fallbackRefreshInterval)
        {
            return cachedState.LastObservation;
        }

        List<ClientCharacterSkillObservation> skills = [];

        foreach (var entry in cachedState.Entries)
        {
            if (!TryReadInt32Value(
                    memory,
                    entry.LevelPropertyAddress,
                    entry.Name + ".Level",
                    out var level,
                    out var readError))
            {
                this.cachedStates.Remove(
                    processId);

                return ClientCharacterSkillsObservation.Unavailable(
                    readError ??
                    string.Create(CultureInfo.InvariantCulture, $"Could not read live skill {entry.Index} level"));
            }

            if (!TryReadInt32Value(
                    memory,
                    entry.MaximumRankPropertyAddress,
                    entry.Name + ".MaxSkillLevels",
                    out var maximumRank,
                    out readError))
            {
                this.cachedStates.Remove(
                    processId);

                return ClientCharacterSkillsObservation.Unavailable(
                    readError ??
                    string.Create(CultureInfo.InvariantCulture, $"Could not read live skill {entry.Index} maximum rank"));
            }

            if (!TryReadInt32Value(
                    memory,
                    entry.AvailabilityPropertyAddress,
                    entry.Name + ".Availability",
                    out var availabilityCode,
                    out readError))
            {
                this.cachedStates.Remove(
                    processId);

                return ClientCharacterSkillsObservation.Unavailable(
                    readError ??
                    string.Create(CultureInfo.InvariantCulture, $"Could not read live skill {entry.Index} availability"));
            }

            if (!TryReadInt32Value(
                    memory,
                    entry.QuestOnlyLevelsPropertyAddress,
                    entry.Name + ".QuestOnlyLevels",
                    out var questOnlyLevels,
                    out readError))
            {
                this.cachedStates.Remove(
                    processId);

                return ClientCharacterSkillsObservation.Unavailable(
                    readError ??
                    string.Create(CultureInfo.InvariantCulture, $"Could not read live skill {entry.Index} quest-only levels"));
            }

            skills.Add(
                new ClientCharacterSkillObservation
                {
                    Index = entry.Index,
                    Name = entry.Name,
                    Category = entry.Category,
                    IsActiveAbility =
                        entry.IsActiveAbility,
                    CurrentRank = level,
                    MaximumRank = maximumRank,
                    AvailabilityCode =
                        availabilityCode,
                    QuestOnlyLevels =
                        questOnlyLevels,
                    StaticDefinitionAddress =
                        entry.StaticDefinitionAddress,
                    LiveWrapperAddress =
                        entry.LiveWrapperAddress,
                });
        }

        var observation =
            new ClientCharacterSkillsObservation
            {
                IsAvailable = true,
                Status = string.Create(
                    CultureInfo.InvariantCulture,
                    $"Available; {skills.Count} named definitions joined to {liveSlotCount} live slots"),
                StaticCatalogBeginAddress =
                    staticCatalogBegin,
                StaticCatalogEndAddress =
                    staticCatalogEnd,
                LiveVectorBeginAddress =
                    liveVectorBegin,
                LiveVectorEndAddress =
                    liveVectorEnd,
                StaticDefinitionCount =
                    staticDefinitionCount,
                LiveSlotCount = liveSlotCount,
                ReservedSlotCount =
                    Math.Max(
                        0,
                        liveSlotCount -
                        staticDefinitionCount),
                Skills = skills,
            };

        cachedState.LastObservation =
            observation;

        cachedState.LastObservedAt = now;
        cachedState.LastAvailableSkillPoints =
            availableSkillPoints;

        return observation;
    }

    private static bool TryBuildCachedState(
        ProcessMemoryReader memory,
        uint moduleBaseAddress,
        uint skillsPropertyAddress,
        uint staticCatalogBegin,
        uint staticCatalogEnd,
        int staticDefinitionCount,
        uint liveVectorBegin,
        uint liveVectorEnd,
        int liveSlotCount,
        uint int32PropertyTypeDescriptor,
        out CachedSkillState cachedState,
        out string error)
    {
        cachedState = new CachedSkillState();
        error = "";

        if (!memory.TryReadBytes(
                staticCatalogBegin,
                checked(
                    staticDefinitionCount *
                    sizeof(uint)),
                out var staticVectorBytes))
        {
            error = string.Create(
                CultureInfo.InvariantCulture,
                $"Could not read {staticDefinitionCount}-entry static skill catalog at 0x{staticCatalogBegin:X8}");

            return false;
        }

        if (!memory.TryReadBytes(
                liveVectorBegin,
                checked(
                    liveSlotCount *
                    sizeof(uint)),
                out var liveVectorBytes))
        {
            error = string.Create(
                CultureInfo.InvariantCulture,
                $"Could not read {liveSlotCount}-entry live skill vector at 0x{liveVectorBegin:X8}");

            return false;
        }

        List<CachedSkillEntry> entries = [];
        uint? availabilityPropertyTypeDescriptor = null;

        for (var index = 0;
             index < staticDefinitionCount;
             index++)
        {
            var staticDefinitionAddress =
                BitConverter.ToUInt32(
                    staticVectorBytes,
                    checked(
                        index *
                        sizeof(uint)));

            if (staticDefinitionAddress == 0)
            {
                error = string.Create(
                    CultureInfo.InvariantCulture,
                    $"Static skill definition {index} is null");

                return false;
            }

            if (!TryReadPointerString(
                    memory,
                    checked(
                        staticDefinitionAddress +
                        SkillDefinitionName),
                    MaximumSkillNameLength,
                    string.Create(CultureInfo.InvariantCulture, $"static skill {index} name"),
                    out var name,
                    out error) ||
                !TryReadPointerString(
                    memory,
                    checked(
                        staticDefinitionAddress +
                        SkillDefinitionCategory),
                    MaximumCategoryLength,
                    string.Create(CultureInfo.InvariantCulture, $"static skill {index} category"),
                    out var category,
                    out error) ||
                !TryReadByte(
                    memory,
                    checked(
                        staticDefinitionAddress +
                        SkillDefinitionSelectable),
                    out var selectable))
            {
                if (string.IsNullOrWhiteSpace(error))
                {
                    error = string.Create(
                        CultureInfo.InvariantCulture,
                        $"Could not read static skill definition {index} at 0x{staticDefinitionAddress:X8}");
                }

                return false;
            }

            var liveWrapperAddress =
                BitConverter.ToUInt32(
                    liveVectorBytes,
                    checked(
                        index *
                        sizeof(uint)));

            if (liveWrapperAddress == 0)
            {
                error = string.Create(
                    CultureInfo.InvariantCulture,
                    $"Live skill wrapper {index} is null");

                return false;
            }

            if (!TryResolveLiveSkillProperties(
                    memory,
                    index,
                    liveWrapperAddress,
                    int32PropertyTypeDescriptor,
                    out var propertyAddresses,
                    out var currentAvailabilityTypeDescriptor,
                    out error))
            {
                return false;
            }

            if (!availabilityPropertyTypeDescriptor.HasValue)
            {
                availabilityPropertyTypeDescriptor =
                    currentAvailabilityTypeDescriptor;
            }
            else if (availabilityPropertyTypeDescriptor.Value !=
                     currentAvailabilityTypeDescriptor)
            {
                error = string.Create(
                    CultureInfo.InvariantCulture,
                    $"Live skill {index} Availability had type descriptor 0x{currentAvailabilityTypeDescriptor:X8}; expected the previously observed 0x{availabilityPropertyTypeDescriptor.Value:X8}");

                return false;
            }

            entries.Add(
                new CachedSkillEntry(
                    index,
                    name,
                    category,
                    selectable != 0,
                    staticDefinitionAddress,
                    liveWrapperAddress,
                    propertyAddresses.Level,
                    propertyAddresses.Availability,
                    propertyAddresses.QuestOnlyLevels,
                    propertyAddresses.MaximumRank));
        }

        cachedState = new CachedSkillState
        {
            ModuleBaseAddress =
                moduleBaseAddress,
            SkillsPropertyAddress =
                skillsPropertyAddress,
            StaticCatalogBeginAddress =
                staticCatalogBegin,
            StaticCatalogEndAddress =
                staticCatalogEnd,
            LiveVectorBeginAddress =
                liveVectorBegin,
            LiveVectorEndAddress =
                liveVectorEnd,
            Int32PropertyTypeDescriptor =
                int32PropertyTypeDescriptor,
            AvailabilityPropertyTypeDescriptor =
                availabilityPropertyTypeDescriptor ?? 0,
            Entries = entries,
        };

        return true;
    }

    private static bool TryResolveLiveSkillProperties(
        ProcessMemoryReader memory,
        int index,
        uint liveWrapperAddress,
        uint int32PropertyTypeDescriptor,
        out LiveSkillPropertyAddresses propertyAddresses,
        out uint availabilityPropertyTypeDescriptor,
        out string error)
    {
        propertyAddresses = default;
        availabilityPropertyTypeDescriptor = 0;
        error = "";

        if (!TryReadPointerString(
                memory,
                checked(
                    liveWrapperAddress +
                    AuxDataPropertyQualifiedName),
                MaximumSkillNameLength,
                string.Create(CultureInfo.InvariantCulture, $"live skill wrapper {index} name"),
                out var wrapperName,
                out error))
        {
            return false;
        }

        var expectedWrapperName = string.Create(
            CultureInfo.InvariantCulture,
            $"Hull.RPGInfo.Skills.{index}");

        if (!string.Equals(
                wrapperName,
                expectedWrapperName,
                StringComparison.Ordinal))
        {
            error = string.Create(
                CultureInfo.InvariantCulture,
                $"Live skill wrapper {index} had unexpected name '{wrapperName}' at 0x{liveWrapperAddress:X8}; expected '{expectedWrapperName}'");

            return false;
        }

        if (!TryReadVectorBounds(
                memory,
                liveWrapperAddress,
                MaximumSkillChildPropertyCount,
                string.Create(CultureInfo.InvariantCulture, $"live skill {index} child-property vector"),
                out var childVectorBegin,
                out _,
                out _,
                out var childPropertyCount,
                out error))
        {
            return false;
        }

        if (!memory.TryReadBytes(
                childVectorBegin,
                checked(
                    childPropertyCount *
                    sizeof(uint)),
                out var childVectorBytes))
        {
            error = string.Create(
                CultureInfo.InvariantCulture,
                $"Could not read {childPropertyCount}-entry child-property vector for live skill {index} at 0x{childVectorBegin:X8}");

            return false;
        }

        var level = 0u;
        var availability = 0u;
        var questOnlyLevels = 0u;
        var maximumRank = 0u;

        for (var childIndex = 0;
             childIndex < childPropertyCount;
             childIndex++)
        {
            var propertyAddress =
                BitConverter.ToUInt32(
                    childVectorBytes,
                    checked(
                        childIndex *
                        sizeof(uint)));

            if (propertyAddress == 0)
            {
                continue;
            }

            if (!TryReadPointerString(
                    memory,
                    checked(
                        propertyAddress +
                        AuxDataPropertyQualifiedName),
                    MaximumSkillNameLength,
                    string.Create(CultureInfo.InvariantCulture, $"live skill {index} child {childIndex} name"),
                    out var propertyName,
                    out error))
            {
                return false;
            }

            var suffix =
                propertyName[(propertyName.LastIndexOf('.') + 1)..];

            if (string.Equals(suffix, "Level", StringComparison.Ordinal))
            {
                level = propertyAddress;
            }
            else if (string.Equals(suffix, "Availability", StringComparison.Ordinal))
            {
                availability = propertyAddress;
            }
            else if (string.Equals(suffix, "QuestOnlyLevels", StringComparison.Ordinal))
            {
                questOnlyLevels = propertyAddress;
            }
            else if (string.Equals(suffix, "MaxSkillLevels", StringComparison.Ordinal))
            {
                maximumRank = propertyAddress;
            }
        }

        if (level == 0 ||
            availability == 0 ||
            questOnlyLevels == 0 ||
            maximumRank == 0)
        {
            error = string.Create(
                CultureInfo.InvariantCulture,
                $"Live skill {index} did not expose the required Level, Availability, QuestOnlyLevels, and MaxSkillLevels properties");

            return false;
        }

        if (!TryValidatePropertyType(
                memory,
                level,
                int32PropertyTypeDescriptor,
                string.Create(CultureInfo.InvariantCulture, $"live skill {index} Level"),
                out error) ||
            !TryValidatePropertyType(
                memory,
                questOnlyLevels,
                int32PropertyTypeDescriptor,
                string.Create(CultureInfo.InvariantCulture, $"live skill {index} QuestOnlyLevels"),
                out error) ||
            !TryValidatePropertyType(
                memory,
                maximumRank,
                int32PropertyTypeDescriptor,
                string.Create(CultureInfo.InvariantCulture, $"live skill {index} MaxSkillLevels"),
                out error) ||
            !TryReadPropertyTypeDescriptor(
                memory,
                availability,
                string.Create(CultureInfo.InvariantCulture, $"live skill {index} Availability"),
                out availabilityPropertyTypeDescriptor,
                out error))
        {
            return false;
        }

        propertyAddresses =
            new LiveSkillPropertyAddresses(
                level,
                availability,
                questOnlyLevels,
                maximumRank);

        return true;
    }

    private static bool TryReadStaticCatalogBounds(
        ProcessMemoryReader memory,
        uint moduleBaseAddress,
        out uint beginAddress,
        out uint endAddress,
        out int definitionCount,
        out string error)
    {
        beginAddress = 0;
        endAddress = 0;
        definitionCount = 0;
        error = "";

        try
        {
            if (!memory.TryReadUInt32(
                    checked(
                        moduleBaseAddress +
                        StaticSkillCatalogBeginRva),
                    out beginAddress) ||
                !memory.TryReadUInt32(
                    checked(
                        moduleBaseAddress +
                        StaticSkillCatalogEndRva),
                    out endAddress))
            {
                error =
                    "Could not read the static skill-catalog vector bounds";

                return false;
            }
        }
        catch (OverflowException)
        {
            error =
                "Static skill-catalog vector address calculation overflow";

            return false;
        }

        if (beginAddress == 0 ||
            endAddress < beginAddress)
        {
            error = $"Static skill catalog is inconsistent: begin=0x{beginAddress:X8}, end=0x{endAddress:X8}";

            return false;
        }

        var activeLength =
            endAddress - beginAddress;

        if (activeLength % sizeof(uint) != 0)
        {
            error = $"Static skill catalog length {activeLength} is not DWORD-aligned";

            return false;
        }

        definitionCount = checked(
            (int)(activeLength /
                  sizeof(uint)));

        if (definitionCount is <= 0 or > MaximumSkillDefinitionCount)
        {
            error = string.Create(
                CultureInfo.InvariantCulture,
                $"Static skill catalog contains unexpected definition count {definitionCount}");

            return false;
        }

        return true;
    }

    private static bool TryReadVectorBounds(
        ProcessMemoryReader memory,
        uint ownerAddress,
        int maximumCount,
        string description,
        out uint beginAddress,
        out uint endAddress,
        out uint capacityAddress,
        out int count,
        out string error)
    {
        beginAddress = 0;
        endAddress = 0;
        capacityAddress = 0;
        count = 0;
        error = "";

        if (!memory.TryReadUInt32(
                checked(
                    ownerAddress +
                    AuxDataPropertyVectorBegin),
                out beginAddress) ||
            !memory.TryReadUInt32(
                checked(
                    ownerAddress +
                    AuxDataPropertyVectorEnd),
                out endAddress) ||
            !memory.TryReadUInt32(
                checked(
                    ownerAddress +
                    AuxDataPropertyVectorCapacity),
                out capacityAddress))
        {
            error = $"Could not read {description} bounds at 0x{ownerAddress:X8}";

            return false;
        }

        if (beginAddress == 0 ||
            endAddress < beginAddress ||
            capacityAddress < endAddress)
        {
            error = $"{description} is inconsistent: begin=0x{beginAddress:X8}, end=0x{endAddress:X8}, capacity=0x{capacityAddress:X8}";

            return false;
        }

        var activeLength =
            endAddress - beginAddress;

        if (activeLength % sizeof(uint) != 0)
        {
            error = $"{description} length {activeLength} is not DWORD-aligned";

            return false;
        }

        count = checked(
            (int)(activeLength /
                  sizeof(uint)));

        if (count <= 0 ||
            count > maximumCount)
        {
            error = string.Create(
                CultureInfo.InvariantCulture,
                $"{description} contains unexpected entry count {count}");

            return false;
        }

        return true;
    }

    private static bool TryReadPointerString(
        ProcessMemoryReader memory,
        uint pointerAddress,
        int maximumLength,
        string description,
        out string value,
        out string error)
    {
        value = "";
        error = "";

        if (!memory.TryReadUInt32(
                pointerAddress,
                out var stringAddress) ||
            stringAddress == 0 ||
            !memory.TryReadNullTerminatedLatin1String(
                stringAddress,
                maximumLength,
                out value))
        {
            error = $"Could not read {description} through 0x{pointerAddress:X8}";

            return false;
        }

        return true;
    }

    private static bool TryReadPropertyTypeDescriptor(
        ProcessMemoryReader memory,
        uint propertyAddress,
        string description,
        out uint typeDescriptor,
        out string error)
    {
        typeDescriptor = 0;
        error = "";

        var descriptorAddress = checked(
            propertyAddress +
            AuxDataPropertyTypeDescriptor);

        if (!memory.TryReadUInt32(
                descriptorAddress,
                out typeDescriptor) ||
            typeDescriptor == 0)
        {
            error = $"Could not read {description} type descriptor at 0x{descriptorAddress:X8}";

            return false;
        }

        return true;
    }

    private static bool TryValidatePropertyType(
        ProcessMemoryReader memory,
        uint propertyAddress,
        uint expectedTypeDescriptor,
        string description,
        out string error)
    {
        error = "";

        if (!memory.TryReadUInt32(
                checked(
                    propertyAddress +
                    AuxDataPropertyTypeDescriptor),
                out var actualTypeDescriptor))
        {
            error = $"Could not read {description} type descriptor at 0x{propertyAddress + AuxDataPropertyTypeDescriptor:X8}";

            return false;
        }

        if (actualTypeDescriptor !=
            expectedTypeDescriptor)
        {
            error = $"{description} had type descriptor 0x{actualTypeDescriptor:X8}; expected 0x{expectedTypeDescriptor:X8}";

            return false;
        }

        return true;
    }

    private static bool TryReadInt32Value(
        ProcessMemoryReader memory,
        uint propertyAddress,
        string description,
        out int value,
        out string? error)
    {
        value = 0;
        error = null;

        if (!memory.TryReadBytes(
                checked(
                    propertyAddress +
                    AuxDataPropertyValid),
                checked(
                    (int)(AuxDataPropertyValue -
                          AuxDataPropertyValid +
                          sizeof(uint))),
                out var bytes))
        {
            error = $"Could not read {description} at 0x{propertyAddress:X8}";

            return false;
        }

        var validValue =
            BitConverter.ToUInt32(
                bytes,
                0);

        if (validValue == 0)
        {
            error =
                $"{description} is present but not valid";

            return false;
        }

        value = BitConverter.ToInt32(
            bytes,
            checked(
                (int)(AuxDataPropertyValue -
                      AuxDataPropertyValid)));

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

    private sealed class CachedSkillState
    {
        public uint ModuleBaseAddress { get; init; }

        public uint SkillsPropertyAddress { get; init; }

        public uint StaticCatalogBeginAddress { get; init; }

        public uint StaticCatalogEndAddress { get; init; }

        public uint LiveVectorBeginAddress { get; init; }

        public uint LiveVectorEndAddress { get; init; }

        public uint Int32PropertyTypeDescriptor { get; init; }

        public uint AvailabilityPropertyTypeDescriptor { get; init; }

        public IReadOnlyList<CachedSkillEntry> Entries { get; init; } = [];

        public DateTimeOffset LastObservedAt { get; set; }

        public int? LastAvailableSkillPoints { get; set; }

        public ClientCharacterSkillsObservation LastObservation { get; set; } =
            ClientCharacterSkillsObservation.Unavailable(
                "Character skills have not been read yet");

        public bool Matches(
            uint moduleBaseAddress,
            uint skillsPropertyAddress,
            uint staticCatalogBeginAddress,
            uint staticCatalogEndAddress,
            uint liveVectorBeginAddress,
            uint liveVectorEndAddress,
            uint int32PropertyTypeDescriptor)
        {
            return this.ModuleBaseAddress ==
                       moduleBaseAddress &&
                   this.SkillsPropertyAddress ==
                       skillsPropertyAddress &&
                   this.StaticCatalogBeginAddress ==
                       staticCatalogBeginAddress &&
                   this.StaticCatalogEndAddress ==
                       staticCatalogEndAddress &&
                   this.LiveVectorBeginAddress ==
                       liveVectorBeginAddress &&
                   this.LiveVectorEndAddress ==
                       liveVectorEndAddress &&
                   this.Int32PropertyTypeDescriptor ==
                       int32PropertyTypeDescriptor;
        }
    }

    private readonly record struct CachedSkillEntry(
        int Index,
        string Name,
        string Category,
        bool IsActiveAbility,
        uint StaticDefinitionAddress,
        uint LiveWrapperAddress,
        uint LevelPropertyAddress,
        uint AvailabilityPropertyAddress,
        uint QuestOnlyLevelsPropertyAddress,
        uint MaximumRankPropertyAddress);

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct LiveSkillPropertyAddresses(
        uint Level,
        uint Availability,
        uint QuestOnlyLevels,
        uint MaximumRank);
}
