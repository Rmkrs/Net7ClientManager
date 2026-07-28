namespace Net7ClientManager.Observations.Observers;

using System.Buffers.Binary;
using System.Globalization;
using Net7ClientManager.Observations.Models;

internal sealed class ClientMissionObserver
{
    private const uint ImageBase = 0x00400000;

    private const uint MissionsArrayVTableStatic =
        0x00b03418;

    private const uint MissionsArrayVTableRva =
        MissionsArrayVTableStatic - ImageBase;

    private const uint MissionVTableStatic =
        0x00b03748;

    private const uint MissionVTableRva =
        MissionVTableStatic - ImageBase;

    private const uint MissionStagesVTableStatic =
        0x00b03800;

    private const uint MissionStagesVTableRva =
        MissionStagesVTableStatic - ImageBase;

    private const uint MissionStageVTableStatic =
        0x00b037a4;

    private const uint MissionStageVTableRva =
        MissionStageVTableStatic - ImageBase;

    private const uint LocalPlayerAuxDataMissions =
        0x0ea0;

    private const int MissionCapacity = 12;
    private const int MissionStageCapacity = 20;

    private const int AuxDataPropertyValid = 0x70;
    private const int AuxDataPropertyValue = 0x84;
    private const int AuxDataTwoWordValueLow = 0x88;
    private const int AuxDataTwoWordValueHigh = 0x8c;

    private const int AggregateVectorBegin = 0x88;
    private const int AggregateVectorEnd = 0x8c;
    private const int AggregateVectorCapacity = 0x90;
    private const int AggregateHeaderLength = 0x94;

    private const int MissionRecordSize = 0x0ae0;
    private const int MissionStageRecordSize = 0x01ac;

    private const int MissionIdProperty = 0x009c;
    private const int MissionNameProperty = 0x0124;
    private const int MissionSummaryProperty = 0x01ac;
    private const int MissionRewardProperty = 0x0234;
    private const int MissionFailureConsequenceProperty = 0x02bc;
    private const int MissionIssuingFactionProperty = 0x0344;
    private const int MissionIsTimedProperty = 0x03cc;
    private const int MissionExpirationTimeProperty = 0x0458;
    private const int MissionStartTimeProperty = 0x04e8;
    private const int MissionIsForfeitableProperty = 0x0570;
    private const int MissionIsCompleteProperty = 0x05f8;
    private const int MissionIsFailedProperty = 0x0680;
    private const int MissionIsExpiredProperty = 0x0708;
    private const int MissionIsFullyVisibleProperty = 0x0790;
    private const int MissionStageCountProperty = 0x0818;
    private const int MissionStageProperty = 0x08a0;
    private const int MissionStagesAggregate = 0x0928;
    private const int MissionStagesVectorBegin = 0x09b0;
    private const int MissionStagesVectorEnd = 0x09b4;
    private const int MissionStageExpirationTimeProperty = 0x09c8;
    private const int MissionHasGivenNewMissionMessageProperty = 0x0a58;

    private const int MissionStageTextProperty = 0x009c;
    private const int MissionStageIsTimedProperty = 0x0124;

    private const int MaximumMissionNameLength = 512;
    private const int MaximumMissionTextLength = 4096;

    private readonly Dictionary<int, ProcessCache>
        processCaches = [];

    public void Refresh(
        ProcessMemoryReader memory,
        ObservedClientState state)
    {
        if (!state.HasDirectClientState ||
            state.LocalPlayerAuxDataAddress == 0)
        {
            this.Forget(
                state.ProcessId);

            SetObservation(
                state,
                ClientMissionLogObservation.Unavailable(
                    "SClient Hull AuxData (+0x12C0) is unavailable"));

            return;
        }

        var lastError = "Mission state is unavailable";

        for (var attempt = 0;
             attempt < 2;
             attempt++)
        {
            if (!this.TryGetOrCreateCache(
                    memory,
                    state,
                    out var cache,
                    out lastError))
            {
                this.processCaches.Remove(
                    state.ProcessId);

                continue;
            }

            if (this.TryObserve(
                    memory,
                    state,
                    cache,
                    out var observation,
                    out lastError))
            {
                SetObservation(
                    state,
                    observation);

                return;
            }

            // A mission or stage vector may have been rebuilt while it was
            // sampled. Drop all stable references and resolve the structure
            // once more before reporting the tick as unavailable.
            this.processCaches.Remove(
                state.ProcessId);
        }

        SetObservation(
            state,
            ClientMissionLogObservation.Unavailable(
                lastError));
    }

    public void Forget(
        int processId)
    {
        this.processCaches.Remove(
            processId);
    }

    private bool TryGetOrCreateCache(
        ProcessMemoryReader memory,
        ObservedClientState state,
        out ProcessCache cache,
        out string error)
    {
        if (this.processCaches.TryGetValue(
                state.ProcessId,
                out var existingCache) &&
            existingCache.ModuleBaseAddress ==
                state.ModuleBaseAddress &&
            existingCache.HullAuxDataAddress ==
                state.LocalPlayerAuxDataAddress)
        {
            cache = existingCache;
            error = "";
            return true;
        }

        if (!this.TryCreateCache(
                memory,
                state.ModuleBaseAddress,
                state.LocalPlayerAuxDataAddress,
                out cache,
                out error))
        {
            return false;
        }

        this.processCaches[state.ProcessId] =
            cache;

        return true;
    }

    private bool TryCreateCache(
        ProcessMemoryReader memory,
        uint moduleBaseAddress,
        uint hullAuxDataAddress,
        out ProcessCache cache,
        out string error)
    {
        cache = null!;
        error = "";

        uint missionsAddress;

        try
        {
            missionsAddress = checked(
                hullAuxDataAddress +
                LocalPlayerAuxDataMissions);
        }
        catch (OverflowException)
        {
            error =
                "Hull.Missions address calculation overflow";

            return false;
        }

        var expectedMissionsVTable = checked(
            moduleBaseAddress +
            MissionsArrayVTableRva);

        var expectedMissionVTable = checked(
            moduleBaseAddress +
            MissionVTableRva);

        var expectedStagesVTable = checked(
            moduleBaseAddress +
            MissionStagesVTableRva);

        var expectedStageVTable = checked(
            moduleBaseAddress +
            MissionStageVTableRva);

        if (!memory.TryReadBytes(
                missionsAddress,
                AggregateHeaderLength,
                out var missionsHeader))
        {
            error = $"Could not read Hull.Missions header at 0x{missionsAddress:X8}";

            return false;
        }

        if (!TryValidateVTable(
                missionsHeader,
                expectedMissionsVTable,
                "Hull.Missions",
                missionsAddress,
                out error) ||
            !TryReadVectorState(
                missionsHeader,
                missionsAddress,
                MissionCapacity,
                "Hull.Missions",
                out var missionVectorBegin,
                out var missionVectorEnd,
                out error) ||
            !TryReadPointerVector(
                memory,
                missionVectorBegin,
                MissionCapacity,
                "Hull.Missions",
                out var missionAddresses,
                out error))
        {
            return false;
        }

        var missions = new MissionCache[MissionCapacity];

        for (var slot = 0;
             slot < MissionCapacity;
             slot++)
        {
            var missionAddress =
                missionAddresses[slot];

            if (missionAddress == 0)
            {
                error = string.Create(
                    CultureInfo.InvariantCulture,
                    $"Hull.Missions.{slot} address is zero");

                return false;
            }

            if (!memory.TryReadBytes(
                    missionAddress,
                    MissionRecordSize,
                    out var missionSnapshot))
            {
                error = string.Create(
                    CultureInfo.InvariantCulture,
                    $"Could not read Hull.Missions.{slot} at 0x{missionAddress:X8}");

                return false;
            }

            if (!TryValidateVTable(
                    missionSnapshot,
                    expectedMissionVTable,
                    string.Create(CultureInfo.InvariantCulture, $"Hull.Missions.{slot}"),
                    missionAddress,
                    out error) ||
                !TryValidateEmbeddedVTable(
                    missionSnapshot,
                    MissionStagesAggregate,
                    expectedStagesVTable,
                    string.Create(CultureInfo.InvariantCulture, $"Hull.Missions.{slot}.Stages"),
                    missionAddress,
                    out error))
            {
                return false;
            }

            var stageVectorBegin =
                ReadUInt32(
                    missionSnapshot,
                    MissionStagesVectorBegin);

            var stageVectorEnd =
                ReadUInt32(
                    missionSnapshot,
                    MissionStagesVectorEnd);

            if (!TryValidateVectorRange(
                    stageVectorBegin,
                    stageVectorEnd,
                    MissionStageCapacity,
                    string.Create(CultureInfo.InvariantCulture, $"Hull.Missions.{slot}.Stages"),
                    out error) ||
                !TryReadPointerVector(
                    memory,
                    stageVectorBegin,
                    MissionStageCapacity,
                    string.Create(CultureInfo.InvariantCulture, $"Hull.Missions.{slot}.Stages"),
                    out var stageAddresses,
                    out error))
            {
                return false;
            }

            missions[slot] =
                new MissionCache(
                    missionAddress,
                    stageVectorBegin,
                    stageVectorEnd,
                    stageAddresses);
        }

        cache = new ProcessCache(
            moduleBaseAddress,
            hullAuxDataAddress,
            missionsAddress,
            missionVectorBegin,
            missionVectorEnd,
            expectedMissionsVTable,
            expectedMissionVTable,
            expectedStagesVTable,
            expectedStageVTable,
            missions);

        return true;
    }

    private bool TryObserve(
        ProcessMemoryReader memory,
        ObservedClientState state,
        ProcessCache cache,
        out ClientMissionLogObservation observation,
        out string error)
    {
        observation = null!;
        error = "";

        if (!memory.TryReadBytes(
                cache.MissionsAddress,
                AggregateHeaderLength,
                out var missionsHeader))
        {
            error = $"Could not reread Hull.Missions header at 0x{cache.MissionsAddress:X8}";

            return false;
        }

        if (!TryValidateVTable(
                missionsHeader,
                cache.ExpectedMissionsVTable,
                "Hull.Missions",
                cache.MissionsAddress,
                out error))
        {
            return false;
        }

        var missionVectorBegin =
            ReadUInt32(
                missionsHeader,
                AggregateVectorBegin);

        var missionVectorEnd =
            ReadUInt32(
                missionsHeader,
                AggregateVectorEnd);

        if (missionVectorBegin !=
                cache.MissionVectorBegin ||
            missionVectorEnd !=
                cache.MissionVectorEnd)
        {
            error =
                "Hull.Missions vector changed while cached";

            return false;
        }

        var collectionValidState =
            ReadUInt32(
                missionsHeader,
                AuxDataPropertyValid);

        List<ClientMissionObservation> missions = [];

        for (var slot = 0;
             slot < cache.Missions.Length;
             slot++)
        {
            var missionCache =
                cache.Missions[slot];

            if (!memory.TryReadBytes(
                    missionCache.Address,
                    MissionRecordSize,
                    out var missionSnapshot))
            {
                error = string.Create(
                    CultureInfo.InvariantCulture,
                    $"Could not read Hull.Missions.{slot} at 0x{missionCache.Address:X8}");

                return false;
            }

            if (!TryValidateVTable(
                    missionSnapshot,
                    cache.ExpectedMissionVTable,
                    string.Create(CultureInfo.InvariantCulture, $"Hull.Missions.{slot}"),
                    missionCache.Address,
                    out error) ||
                !TryValidateEmbeddedVTable(
                    missionSnapshot,
                    MissionStagesAggregate,
                    cache.ExpectedStagesVTable,
                    string.Create(CultureInfo.InvariantCulture, $"Hull.Missions.{slot}.Stages"),
                    missionCache.Address,
                    out error))
            {
                return false;
            }

            var stageVectorBegin =
                ReadUInt32(
                    missionSnapshot,
                    MissionStagesVectorBegin);

            var stageVectorEnd =
                ReadUInt32(
                    missionSnapshot,
                    MissionStagesVectorEnd);

            if (stageVectorBegin !=
                    missionCache.StageVectorBegin ||
                stageVectorEnd !=
                    missionCache.StageVectorEnd)
            {
                error = string.Create(
                    CultureInfo.InvariantCulture,
                    $"Hull.Missions.{slot}.Stages vector changed while cached");

                return false;
            }

            if (!TryReadStringProperty(
                    memory,
                    missionSnapshot,
                    MissionNameProperty,
                    MaximumMissionNameLength,
                    string.Create(CultureInfo.InvariantCulture, $"Hull.Missions.{slot}.Name"),
                    out var name,
                    out error))
            {
                return false;
            }

            // Native mission UI and notification code use non-empty Name as
            // the occupied-slot test. Constructor defaults in an empty slot
            // are therefore never surfaced as real mission state.
            if (string.IsNullOrEmpty(name))
            {
                missionCache.ClearValueCache();
                continue;
            }

            if (!TryReadStringProperty(
                    memory,
                    missionSnapshot,
                    MissionSummaryProperty,
                    MaximumMissionTextLength,
                    string.Create(CultureInfo.InvariantCulture, $"Hull.Missions.{slot}.Summary"),
                    out var summary,
                    out error) ||
                !TryReadStringProperty(
                    memory,
                    missionSnapshot,
                    MissionRewardProperty,
                    MaximumMissionTextLength,
                    string.Create(CultureInfo.InvariantCulture, $"Hull.Missions.{slot}.Reward"),
                    out var reward,
                    out error) ||
                !TryReadStringProperty(
                    memory,
                    missionSnapshot,
                    MissionFailureConsequenceProperty,
                    MaximumMissionTextLength,
                    string.Create(CultureInfo.InvariantCulture, $"Hull.Missions.{slot}.FailureConsequence"),
                    out var failureConsequence,
                    out error) ||
                !TryReadStringProperty(
                    memory,
                    missionSnapshot,
                    MissionIssuingFactionProperty,
                    MaximumMissionNameLength,
                    string.Create(CultureInfo.InvariantCulture, $"Hull.Missions.{slot}.IssuingFaction"),
                    out var issuingFaction,
                    out error))
            {
                return false;
            }

            var rawId = ReadNullableInt32Property(
                missionSnapshot,
                MissionIdProperty);

            var stage = ReadNullableInt32Property(
                missionSnapshot,
                MissionStageProperty);

            var stageCount = ReadNullableInt32Property(
                missionSnapshot,
                MissionStageCountProperty);

            var stagesValidState = ReadUInt32(
                missionSnapshot,
                MissionStagesAggregate +
                AuxDataPropertyValid);

            if (!this.TryReadStages(
                    memory,
                    cache.ExpectedStageVTable,
                    slot,
                    missionCache,
                    name,
                    rawId,
                    stage,
                    stageCount,
                    stagesValidState,
                    out var stages,
                    out error))
            {
                return false;
            }

            var isTimed = ReadNullableBooleanProperty(
                missionSnapshot,
                MissionIsTimedProperty);

            var expirationTime = ReadNullableInt64Property(
                missionSnapshot,
                MissionExpirationTimeProperty);

            var stageExpirationTime = ReadNullableInt64Property(
                missionSnapshot,
                MissionStageExpirationTimeProperty);

            var currentStage = stage is >= 1
                ? stages.FirstOrDefault(
                    item =>
                        item.Index == stage.Value - 1)
                : null;

            var effectiveExpirationTime =
                currentStage?.IsTimed == true
                    ? stageExpirationTime
                    : isTimed == true
                        ? expirationTime
                        : null;

            var remainingMilliseconds =
                effectiveExpirationTime.HasValue
                    ? effectiveExpirationTime.Value -
                      state.CurrentClientTime
                    : (long?)null;

            missions.Add(
                new ClientMissionObservation
                {
                    Slot = slot,
                    Address =
                        missionCache.Address,
                    ValidState = ReadUInt32(
                        missionSnapshot,
                        AuxDataPropertyValid),
                    RawId = rawId,
                    Name = name,
                    Summary = summary,
                    Reward = reward,
                    FailureConsequence =
                        failureConsequence,
                    IssuingFaction = issuingFaction,
                    Stage = stage,
                    StageCount = stageCount,
                    IsTimed = isTimed,
                    IsForfeitable =
                        ReadNullableBooleanProperty(
                            missionSnapshot,
                            MissionIsForfeitableProperty),
                    IsComplete =
                        ReadNullableBooleanProperty(
                            missionSnapshot,
                            MissionIsCompleteProperty),
                    IsFailed =
                        ReadNullableBooleanProperty(
                            missionSnapshot,
                            MissionIsFailedProperty),
                    IsExpired =
                        ReadNullableBooleanProperty(
                            missionSnapshot,
                            MissionIsExpiredProperty),
                    IsFullyVisible =
                        ReadNullableBooleanProperty(
                            missionSnapshot,
                            MissionIsFullyVisibleProperty),
                    StartTime =
                        ReadNullableInt32Property(
                            missionSnapshot,
                            MissionStartTimeProperty),
                    ExpirationTime =
                        expirationTime,
                    StageExpirationTime =
                        stageExpirationTime,
                    HasGivenNewMissionMessage =
                        ReadNullableBooleanProperty(
                            missionSnapshot,
                            MissionHasGivenNewMissionMessageProperty),
                    StageCapacity =
                        MissionStageCapacity,
                    Stages = stages,
                    RemainingMilliseconds =
                        remainingMilliseconds,
                });
        }

        observation =
            new ClientMissionLogObservation
            {
                IsAvailable = true,
                Status = string.Create(
                    CultureInfo.InvariantCulture,
                    $"Available; {missions.Count}/{MissionCapacity} mission slots occupied"),
                Address =
                    cache.MissionsAddress,
                ValidState =
                    collectionValidState,
                Capacity =
                    MissionCapacity,
                Missions = missions,
            };

        return true;
    }

    private bool TryReadStages(
        ProcessMemoryReader memory,
        uint expectedStageVTable,
        int missionSlot,
        MissionCache missionCache,
        string missionName,
        int? rawId,
        int? stage,
        int? stageCount,
        uint stagesValidState,
        out IReadOnlyList<ClientMissionStageObservation> stages,
        out string error)
    {
        stages = [];
        error = "";

        var logicalStageCount = GetLogicalStageCount(
            stage,
            stageCount);

        var refreshAll =
            missionCache.CachedStages.Count !=
                logicalStageCount ||
            !string.Equals(
                missionCache.LastName,
                missionName,
                StringComparison.Ordinal) ||
            missionCache.LastRawId != rawId ||
            missionCache.LastStage != stage ||
            missionCache.LastStageCount != stageCount ||
            missionCache.LastStagesValidState !=
                stagesValidState;

        ClientMissionStageObservation[] result;

        if (refreshAll)
        {
            result = new ClientMissionStageObservation[
                logicalStageCount];

            for (var index = 0;
                 index < logicalStageCount;
                 index++)
            {
                if (!TryReadStage(
                        memory,
                        expectedStageVTable,
                        missionSlot,
                        index,
                        missionCache.StageAddresses[index],
                        out result[index],
                        out error))
                {
                    return false;
                }
            }
        }
        else
        {
            result =
                [.. missionCache.CachedStages];

            if (stage is >= 1 and <= MissionStageCapacity)
            {
                var currentIndex =
                    stage.Value - 1;

                if (currentIndex < result.Length &&
                    !TryReadStage(
                        memory,
                        expectedStageVTable,
                        missionSlot,
                        currentIndex,
                        missionCache.StageAddresses[currentIndex],
                        out result[currentIndex],
                        out error))
                {
                    return false;
                }
            }
        }

        missionCache.LastName = missionName;
        missionCache.LastRawId = rawId;
        missionCache.LastStage = stage;
        missionCache.LastStageCount = stageCount;
        missionCache.LastStagesValidState =
            stagesValidState;
        missionCache.CachedStages = result;

        stages = result;
        return true;
    }

    private static bool TryReadStage(
        ProcessMemoryReader memory,
        uint expectedStageVTable,
        int missionSlot,
        int stageIndex,
        uint stageAddress,
        out ClientMissionStageObservation stage,
        out string error)
    {
        stage = null!;
        error = "";

        if (stageAddress == 0)
        {
            error = string.Create(
                CultureInfo.InvariantCulture,
                $"Hull.Missions.{missionSlot}.Stages.{stageIndex} address is zero");

            return false;
        }

        if (!memory.TryReadBytes(
                stageAddress,
                MissionStageRecordSize,
                out var snapshot))
        {
            error = string.Create(
                CultureInfo.InvariantCulture,
                $"Could not read Hull.Missions.{missionSlot}.Stages.{stageIndex} at 0x{stageAddress:X8}");

            return false;
        }

        if (!TryValidateVTable(
                snapshot,
                expectedStageVTable,
                string.Create(CultureInfo.InvariantCulture, $"Hull.Missions.{missionSlot}.Stages.{stageIndex}"),
                stageAddress,
                out error) ||
            !TryReadStringProperty(
                memory,
                snapshot,
                MissionStageTextProperty,
                MaximumMissionTextLength,
                string.Create(CultureInfo.InvariantCulture, $"Hull.Missions.{missionSlot}.Stages.{stageIndex}.Text"),
                out var text,
                out error))
        {
            return false;
        }

        var validState = ReadUInt32(
            snapshot,
            AuxDataPropertyValid);

        stage = new ClientMissionStageObservation
        {
            Index = stageIndex,
            Address = stageAddress,
            ValidState = validState,
            IsAvailable = validState != 0,
            Text = text,
            IsTimed = ReadNullableBooleanProperty(
                snapshot,
                MissionStageIsTimedProperty),
        };

        return true;
    }

    private static int GetLogicalStageCount(
        int? stage,
        int? stageCount)
    {
        if (stageCount is > 0 and <= MissionStageCapacity)
        {
            return stageCount.Value;
        }

        if (stage is > 0 and <= MissionStageCapacity)
        {
            return stage.Value;
        }

        return 0;
    }

    private static bool TryReadStringProperty(
        ProcessMemoryReader memory,
        byte[] snapshot,
        int propertyOffset,
        int maximumLength,
        string propertyName,
        out string value,
        out string error)
    {
        value = "";
        error = "";

        var validState = ReadUInt32(
            snapshot,
            propertyOffset +
            AuxDataPropertyValid);

        if (validState == 0)
        {
            return true;
        }

        var stringAddress = ReadUInt32(
            snapshot,
            propertyOffset +
            AuxDataPropertyValue);

        if (stringAddress == 0)
        {
            return true;
        }

        if (!memory.TryReadNullTerminatedLatin1String(
                stringAddress,
                maximumLength,
                out value))
        {
            error = $"Could not read {propertyName} string at 0x{stringAddress:X8}";

            return false;
        }

        return true;
    }

    private static int? ReadNullableInt32Property(
        byte[] snapshot,
        int propertyOffset)
    {
        return ReadUInt32(
                   snapshot,
                   propertyOffset +
                   AuxDataPropertyValid) != 0
            ? unchecked((int)ReadUInt32(
                snapshot,
                propertyOffset +
                AuxDataPropertyValue))
            : null;
    }

    private static bool? ReadNullableBooleanProperty(
        byte[] snapshot,
        int propertyOffset)
    {
        return ReadUInt32(
                   snapshot,
                   propertyOffset +
                   AuxDataPropertyValid) != 0
            ? snapshot[
                  propertyOffset +
                  AuxDataPropertyValue] != 0
            : null;
    }

    private static long? ReadNullableInt64Property(
        byte[] snapshot,
        int propertyOffset)
    {
        if (ReadUInt32(
                snapshot,
                propertyOffset +
                AuxDataPropertyValid) == 0)
        {
            return null;
        }

        var low = ReadUInt32(
            snapshot,
            propertyOffset +
            AuxDataTwoWordValueLow);

        var high = ReadUInt32(
            snapshot,
            propertyOffset +
            AuxDataTwoWordValueHigh);

        return unchecked(
            (long)(
                ((ulong)high << 32) |
                low));
    }

    private static bool TryReadVectorState(
        byte[] snapshot,
        uint aggregateAddress,
        int expectedCount,
        string name,
        out uint begin,
        out uint end,
        out string error)
    {
        begin = ReadUInt32(
            snapshot,
            AggregateVectorBegin);

        end = ReadUInt32(
            snapshot,
            AggregateVectorEnd);

        var capacityEnd = ReadUInt32(
            snapshot,
            AggregateVectorCapacity);

        if (!TryValidateVectorRange(
                begin,
                end,
                expectedCount,
                name,
                out error))
        {
            return false;
        }

        if (capacityEnd < end)
        {
            error = $"{name} capacity end 0x{capacityEnd:X8} precedes vector end 0x{end:X8} at 0x{aggregateAddress:X8}";

            return false;
        }

        return true;
    }

    private static bool TryValidateVectorRange(
        uint begin,
        uint end,
        int expectedCount,
        string name,
        out string error)
    {
        error = "";

        if (begin == 0 ||
            end < begin ||
            (end - begin) % sizeof(uint) != 0)
        {
            error = $"{name} contained invalid vector range 0x{begin:X8}..0x{end:X8}";

            return false;
        }

        var count = checked(
            (int)((end - begin) /
                  sizeof(uint)));

        if (count != expectedCount)
        {
            error = string.Create(
                CultureInfo.InvariantCulture,
                $"{name} contained {count} entries; expected {expectedCount}");

            return false;
        }

        return true;
    }

    private static bool TryReadPointerVector(
        ProcessMemoryReader memory,
        uint begin,
        int count,
        string name,
        out uint[] pointers,
        out string error)
    {
        pointers = [];
        error = "";

        if (!memory.TryReadBytes(
                begin,
                checked(
                    count *
                    sizeof(uint)),
                out var bytes))
        {
            error = $"Could not read {name} pointer vector at 0x{begin:X8}";

            return false;
        }

        pointers = new uint[count];

        for (var index = 0;
             index < count;
             index++)
        {
            pointers[index] =
                BinaryPrimitives.ReadUInt32LittleEndian(
                    bytes.AsSpan(
                        index * sizeof(uint),
                        sizeof(uint)));
        }

        return true;
    }

    private static bool TryValidateVTable(
        byte[] snapshot,
        uint expectedVTable,
        string name,
        uint address,
        out string error)
    {
        error = "";

        var actualVTable = ReadUInt32(
            snapshot,
            0);

        if (actualVTable == expectedVTable)
        {
            return true;
        }

        error = $"Unexpected {name} vtable 0x{actualVTable:X8} at 0x{address:X8}; expected 0x{expectedVTable:X8}";

        return false;
    }

    private static bool TryValidateEmbeddedVTable(
        byte[] snapshot,
        int offset,
        uint expectedVTable,
        string name,
        uint parentAddress,
        out string error)
    {
        error = "";

        var actualVTable = ReadUInt32(
            snapshot,
            offset);

        if (actualVTable == expectedVTable)
        {
            return true;
        }

        error = $"Unexpected {name} vtable 0x{actualVTable:X8} at 0x{parentAddress + (uint)offset:X8}; expected 0x{expectedVTable:X8}";

        return false;
    }

    private static uint ReadUInt32(
        byte[] snapshot,
        int offset)
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(
            snapshot.AsSpan(
                offset,
                sizeof(uint)));
    }

    private static void SetObservation(
        ObservedClientState state,
        ClientMissionLogObservation observation)
    {
        state.LocalPlayer = state.LocalPlayer with
        {
            Missions = observation,
        };
    }

    private sealed class ProcessCache(
        uint moduleBaseAddress,
        uint hullAuxDataAddress,
        uint missionsAddress,
        uint missionVectorBegin,
        uint missionVectorEnd,
        uint expectedMissionsVTable,
        uint expectedMissionVTable,
        uint expectedStagesVTable,
        uint expectedStageVTable,
        MissionCache[] missions)
    {
        public uint ModuleBaseAddress { get; } =
            moduleBaseAddress;

        public uint HullAuxDataAddress { get; } =
            hullAuxDataAddress;

        public uint MissionsAddress { get; } =
            missionsAddress;

        public uint MissionVectorBegin { get; } =
            missionVectorBegin;

        public uint MissionVectorEnd { get; } =
            missionVectorEnd;

        public uint ExpectedMissionsVTable { get; } =
            expectedMissionsVTable;

        public uint ExpectedMissionVTable { get; } =
            expectedMissionVTable;

        public uint ExpectedStagesVTable { get; } =
            expectedStagesVTable;

        public uint ExpectedStageVTable { get; } =
            expectedStageVTable;

        public MissionCache[] Missions { get; } =
            missions;
    }

    private sealed class MissionCache(
        uint address,
        uint stageVectorBegin,
        uint stageVectorEnd,
        uint[] stageAddresses)
    {
        public uint Address { get; } =
            address;

        public uint StageVectorBegin { get; } =
            stageVectorBegin;

        public uint StageVectorEnd { get; } =
            stageVectorEnd;

        public uint[] StageAddresses { get; } =
            stageAddresses;

        public string LastName { get; set; } = "";

        public int? LastRawId { get; set; }

        public int? LastStage { get; set; }

        public int? LastStageCount { get; set; }

        public uint LastStagesValidState { get; set; }

        public IReadOnlyList<ClientMissionStageObservation>
            CachedStages
        { get; set; } = [];

        public void ClearValueCache()
        {
            this.LastName = "";
            this.LastRawId = null;
            this.LastStage = null;
            this.LastStageCount = null;
            this.LastStagesValidState = 0;
            this.CachedStages = [];
        }
    }
}
