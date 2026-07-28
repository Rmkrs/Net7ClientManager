namespace Net7ClientManager.Observations.Observers;

using System.Buffers.Binary;
using System.Globalization;
using Net7ClientManager.Observations.Models;

internal sealed class ClientReputationObserver(ClientFactionCatalog factionCatalog)
{
    private const uint ImageBase = 0x00400000;

    private const uint ReputationVTableRva =
        0x00b033bc - ImageBase;

    private const uint FactionArrayVTableRva =
        0x00b0385c - ImageBase;

    private const uint FactionEntryVTableRva =
        0x00b03b14 - ImageBase;

    private const uint LocalPlayerAuxDataReputation =
        0x0f3c;

    private const int FactionCapacity = 32;

    private const int AuxDataPropertyValid = 0x70;
    private const int AuxDataPropertyValue = 0x84;

    private const int AggregateVectorBegin = 0x88;
    private const int AggregateVectorEnd = 0x8c;
    private const int AggregateVectorCapacity = 0x90;

    private const int ReputationFactionAggregate = 0x009c;
    private const int ReputationAffiliationProperty = 0x0138;
    private const int ReputationSnapshotLength = 0x01c0;

    private const int FactionEntrySize = 0x0234;
    private const int FactionNameProperty = 0x009c;
    private const int FactionReactionProperty = 0x0124;
    private const int FactionOrderProperty = 0x01ac;

    private const int MaximumFactionKeyLength = 128;
    private const int MaximumAffiliationLength = 512;

    private readonly Dictionary<int, ProcessCache>
        processCaches = [];

    private readonly ClientFactionCatalog factionCatalog = factionCatalog ??
                                                           throw new ArgumentNullException(
                                                               nameof(factionCatalog));

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
                this.CreateUnavailableObservation(
                    "SClient Hull AuxData (+0x12C0) is unavailable"));

            return;
        }

        var lastError =
            "Reputation state is unavailable";

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
                    cache,
                    out var observation,
                    out lastError))
            {
                SetObservation(
                    state,
                    observation);

                return;
            }

            // A fixed child vector may have been rebuilt while sampled.
            // Drop the cached addresses and resolve once more before the
            // current tick is reported unavailable.
            this.processCaches.Remove(
                state.ProcessId);
        }

        SetObservation(
            state,
            this.CreateUnavailableObservation(
                lastError));
    }

    private ClientReputationObservation
        CreateUnavailableObservation(
            string status)
    {
        var catalog =
            this.factionCatalog.Snapshot;

        return ClientReputationObservation.Unavailable(
            status) with
        {
            FactionCatalogStatus =
                catalog.Status,
            FactionCatalogSourcePath =
                catalog.SourcePath,
            FactionCatalogDefinitionCount =
                catalog.Definitions.Count,
        };
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

        if (!TryCreateCache(
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

    private static bool TryCreateCache(
        ProcessMemoryReader memory,
        uint moduleBaseAddress,
        uint hullAuxDataAddress,
        out ProcessCache cache,
        out string error)
    {
        cache = null!;
        error = "";

        uint reputationAddress;

        try
        {
            reputationAddress = checked(
                hullAuxDataAddress +
                LocalPlayerAuxDataReputation);
        }
        catch (OverflowException)
        {
            error =
                "Hull.Reputation address calculation overflow";

            return false;
        }

        var expectedReputationVTable = checked(
            moduleBaseAddress +
            ReputationVTableRva);

        var expectedFactionArrayVTable = checked(
            moduleBaseAddress +
            FactionArrayVTableRva);

        var expectedFactionEntryVTable = checked(
            moduleBaseAddress +
            FactionEntryVTableRva);

        if (!memory.TryReadBytes(
                reputationAddress,
                ReputationSnapshotLength,
                out var reputationSnapshot))
        {
            error = $"Could not read Hull.Reputation at 0x{reputationAddress:X8}";

            return false;
        }

        if (!TryValidateVTable(
                reputationSnapshot,
                0,
                expectedReputationVTable,
                "Hull.Reputation",
                reputationAddress,
                out error) ||
            !TryValidateVTable(
                reputationSnapshot,
                ReputationFactionAggregate,
                expectedFactionArrayVTable,
                "Hull.Reputation.Faction",
                checked(
                    reputationAddress +
                    ReputationFactionAggregate),
                out error) ||
            !TryReadFactionVectorState(
                reputationSnapshot,
                reputationAddress,
                out var vectorBegin,
                out var vectorEnd,
                out error) ||
            !TryReadPointerVector(
                memory,
                vectorBegin,
                out var factionAddresses,
                out error))
        {
            return false;
        }

        var factions =
            new FactionCache[FactionCapacity];

        for (var slot = 0;
             slot < FactionCapacity;
             slot++)
        {
            var factionAddress =
                factionAddresses[slot];

            if (factionAddress == 0)
            {
                error = string.Create(
                    CultureInfo.InvariantCulture,
                    $"Hull.Reputation.Faction.{slot} address is zero");

                return false;
            }

            if (!memory.TryReadUInt32(
                    factionAddress,
                    out var factionVTable) ||
                factionVTable !=
                    expectedFactionEntryVTable)
            {
                error = string.Create(
                    CultureInfo.InvariantCulture,
                    $"Unexpected Hull.Reputation.Faction.{slot} vtable 0x{factionVTable:X8} at 0x{factionAddress:X8}; expected 0x{expectedFactionEntryVTable:X8}");

                return false;
            }

            factions[slot] =
                new FactionCache(
                    factionAddress);
        }

        cache = new ProcessCache(
            moduleBaseAddress,
            hullAuxDataAddress,
            reputationAddress,
            checked(
                reputationAddress +
                ReputationFactionAggregate),
            vectorBegin,
            vectorEnd,
            expectedReputationVTable,
            expectedFactionArrayVTable,
            expectedFactionEntryVTable,
            factions);

        return true;
    }

    private bool TryObserve(
        ProcessMemoryReader memory,
        ProcessCache cache,
        out ClientReputationObservation observation,
        out string error)
    {
        observation = null!;
        error = "";

        var catalog =
            this.factionCatalog.Snapshot;

        if (!memory.TryReadBytes(
                cache.ReputationAddress,
                ReputationSnapshotLength,
                out var reputationSnapshot))
        {
            error = $"Could not reread Hull.Reputation at 0x{cache.ReputationAddress:X8}";

            return false;
        }

        if (!TryValidateVTable(
                reputationSnapshot,
                0,
                cache.ExpectedReputationVTable,
                "Hull.Reputation",
                cache.ReputationAddress,
                out error) ||
            !TryValidateVTable(
                reputationSnapshot,
                ReputationFactionAggregate,
                cache.ExpectedFactionArrayVTable,
                "Hull.Reputation.Faction",
                cache.FactionCollectionAddress,
                out error) ||
            !TryReadFactionVectorState(
                reputationSnapshot,
                cache.ReputationAddress,
                out var vectorBegin,
                out var vectorEnd,
                out error))
        {
            return false;
        }

        if (vectorBegin != cache.VectorBegin ||
            vectorEnd != cache.VectorEnd)
        {
            error =
                "Hull.Reputation.Faction vector changed while cached";

            return false;
        }

        if (!TryReadPointerVector(
                memory,
                vectorBegin,
                out var currentAddresses,
                out error))
        {
            return false;
        }

        for (var slot = 0;
             slot < FactionCapacity;
             slot++)
        {
            if (currentAddresses[slot] !=
                cache.Factions[slot].Address)
            {
                error = string.Create(
                    CultureInfo.InvariantCulture,
                    $"Hull.Reputation.Faction.{slot} pointer changed while cached");

                return false;
            }
        }

        var reputationValidState =
            ReadUInt32(
                reputationSnapshot,
                AuxDataPropertyValid);

        var factionValidState =
            ReadUInt32(
                reputationSnapshot,
                ReputationFactionAggregate +
                AuxDataPropertyValid);

        if (!TryReadCachedString(
                memory,
                reputationSnapshot,
                ReputationAffiliationProperty,
                MaximumAffiliationLength,
                "Hull.Reputation.Affiliation",
                cache.Affiliation,
                out var affiliation,
                out error))
        {
            return false;
        }

        if (reputationValidState == 0 ||
            factionValidState == 0)
        {
            observation =
                new ClientReputationObservation
                {
                    Status = $"Hull.Reputation is present but not valid; reputation valid={reputationValidState}, faction valid={factionValidState}",
                    Address =
                        cache.ReputationAddress,
                    ValidState =
                        reputationValidState,
                    FactionCollectionAddress =
                        cache.FactionCollectionAddress,
                    FactionCollectionValidState =
                        factionValidState,
                    Capacity =
                        FactionCapacity,
                    Affiliation =
                        affiliation,
                    FactionCatalogStatus =
                        catalog.Status,
                    FactionCatalogSourcePath =
                        catalog.SourcePath,
                    FactionCatalogDefinitionCount =
                        catalog.Definitions.Count,
                };

            return true;
        }

        List<ClientFactionReputationObservation> factions = [];

        for (var slot = 0;
             slot < FactionCapacity;
             slot++)
        {
            var factionCache =
                cache.Factions[slot];

            if (!memory.TryReadBytes(
                    factionCache.Address,
                    FactionEntrySize,
                    out var factionSnapshot))
            {
                error = string.Create(
                    CultureInfo.InvariantCulture,
                    $"Could not read Hull.Reputation.Faction.{slot} at 0x{factionCache.Address:X8}");

                return false;
            }

            var factionVTable =
                ReadUInt32(
                    factionSnapshot,
                    0);

            if (factionVTable !=
                cache.ExpectedFactionEntryVTable)
            {
                error = string.Create(
                    CultureInfo.InvariantCulture,
                    $"Unexpected Hull.Reputation.Faction.{slot} vtable 0x{factionVTable:X8} at 0x{factionCache.Address:X8}; expected 0x{cache.ExpectedFactionEntryVTable:X8}");

                return false;
            }

            if (!TryReadCachedString(
                    memory,
                    factionSnapshot,
                    FactionNameProperty,
                    MaximumFactionKeyLength,
                    string.Create(CultureInfo.InvariantCulture, $"Hull.Reputation.Faction.{slot}.Name"),
                    factionCache.FactionKey,
                    out var factionKey,
                    out error))
            {
                return false;
            }

            // Native faction UI uses non-empty Name/FactionKey as the
            // occupied-slot test. A zero Reaction is still a real standing.
            if (string.IsNullOrEmpty(
                    factionKey))
            {
                continue;
            }

            var isCatalogResolved =
                catalog.Definitions.TryGetValue(
                    factionKey,
                    out var factionDefinition);

            factions.Add(
                new ClientFactionReputationObservation
                {
                    Slot = slot,
                    Address =
                        factionCache.Address,
                    ValidState = ReadUInt32(
                        factionSnapshot,
                        AuxDataPropertyValid),
                    FactionKey =
                        factionKey,
                    DisplayName =
                        isCatalogResolved
                            ? factionDefinition!.DisplayName
                            : factionKey,
                    Description =
                        isCatalogResolved
                            ? factionDefinition!.Description
                            : "",
                    IsCatalogResolved =
                        isCatalogResolved,
                    Reaction =
                        ReadNullableSingle(
                            factionSnapshot,
                            FactionReactionProperty),
                    Order =
                        ReadNullableInt32(
                            factionSnapshot,
                            FactionOrderProperty),
                });
        }

        observation =
            new ClientReputationObservation
            {
                IsAvailable = true,
                Status = string.Create(
                    CultureInfo.InvariantCulture,
                    $"Available; {factions.Count}/{FactionCapacity} faction slots occupied; affiliation={FormatAffiliation(affiliation)}"),
                Address =
                    cache.ReputationAddress,
                ValidState =
                    reputationValidState,
                FactionCollectionAddress =
                    cache.FactionCollectionAddress,
                FactionCollectionValidState =
                    factionValidState,
                Capacity =
                    FactionCapacity,
                Affiliation =
                    affiliation,
                FactionCatalogStatus =
                    catalog.Status,
                FactionCatalogSourcePath =
                    catalog.SourcePath,
                FactionCatalogDefinitionCount =
                    catalog.Definitions.Count,
                Factions =
                    factions,
            };

        return true;
    }

    private static bool TryReadFactionVectorState(
        byte[] snapshot,
        uint reputationAddress,
        out uint begin,
        out uint end,
        out string error)
    {
        begin = ReadUInt32(
            snapshot,
            ReputationFactionAggregate +
            AggregateVectorBegin);

        end = ReadUInt32(
            snapshot,
            ReputationFactionAggregate +
            AggregateVectorEnd);

        var capacityEnd = ReadUInt32(
            snapshot,
            ReputationFactionAggregate +
            AggregateVectorCapacity);

        if (begin == 0 ||
            end < begin ||
            (end - begin) !=
                FactionCapacity * sizeof(uint))
        {
            error = string.Create(
                CultureInfo.InvariantCulture,
                $"Hull.Reputation.Faction contained invalid vector range 0x{begin:X8}..0x{end:X8}; expected {FactionCapacity} entries");

            return false;
        }

        if (capacityEnd < end)
        {
            error = $"Hull.Reputation.Faction capacity end 0x{capacityEnd:X8} precedes vector end 0x{end:X8} at 0x{checked(reputationAddress + ReputationFactionAggregate):X8}";

            return false;
        }

        error = "";
        return true;
    }

    private static bool TryReadPointerVector(
        ProcessMemoryReader memory,
        uint begin,
        out uint[] pointers,
        out string error)
    {
        pointers = [];
        error = "";

        if (!memory.TryReadBytes(
                begin,
                FactionCapacity * sizeof(uint),
                out var bytes))
        {
            error = $"Could not read Hull.Reputation.Faction pointer vector at 0x{begin:X8}";

            return false;
        }

        pointers = new uint[FactionCapacity];

        for (var index = 0;
             index < FactionCapacity;
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

    private static bool TryReadCachedString(
        ProcessMemoryReader memory,
        byte[] snapshot,
        int propertyOffset,
        int maximumLength,
        string propertyName,
        CachedString cache,
        out string value,
        out string error)
    {
        value = "";
        error = "";

        if (ReadUInt32(
                snapshot,
                propertyOffset +
                AuxDataPropertyValid) == 0)
        {
            cache.Clear();
            return true;
        }

        var stringAddress = ReadUInt32(
            snapshot,
            propertyOffset +
            AuxDataPropertyValue);

        if (stringAddress == 0)
        {
            cache.Clear();
            return true;
        }

        if (cache.Address == stringAddress)
        {
            value = cache.Value;
            return true;
        }

        if (!memory.TryReadNullTerminatedLatin1String(
                stringAddress,
                maximumLength,
                out value))
        {
            error = $"Could not read {propertyName} at 0x{stringAddress:X8}";

            return false;
        }

        cache.Address = stringAddress;
        cache.Value = value;

        return true;
    }

    private static float? ReadNullableSingle(
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

        var bits = BinaryPrimitives.ReadInt32LittleEndian(
            snapshot.AsSpan(
                propertyOffset +
                AuxDataPropertyValue,
                sizeof(int)));

        var value = BitConverter.Int32BitsToSingle(
            bits);

        return float.IsFinite(value)
            ? value
            : null;
    }

    private static int? ReadNullableInt32(
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

    private static bool TryValidateVTable(
        byte[] snapshot,
        int offset,
        uint expectedVTable,
        string name,
        uint address,
        out string error)
    {
        var actualVTable =
            ReadUInt32(
                snapshot,
                offset);

        if (actualVTable == expectedVTable)
        {
            error = "";
            return true;
        }

        error = $"Unexpected {name} vtable 0x{actualVTable:X8} at 0x{address:X8}; expected 0x{expectedVTable:X8}";

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

    private static string FormatAffiliation(
        string affiliation)
    {
        return string.IsNullOrEmpty(
                affiliation)
            ? "None"
            : affiliation;
    }

    private static void SetObservation(
        ObservedClientState state,
        ClientReputationObservation observation)
    {
        state.LocalPlayer = state.LocalPlayer with
        {
            Reputation = observation,
        };
    }

    private sealed class ProcessCache(
        uint moduleBaseAddress,
        uint hullAuxDataAddress,
        uint reputationAddress,
        uint factionCollectionAddress,
        uint vectorBegin,
        uint vectorEnd,
        uint expectedReputationVTable,
        uint expectedFactionArrayVTable,
        uint expectedFactionEntryVTable,
        FactionCache[] factions)
    {
        public uint ModuleBaseAddress { get; } =
            moduleBaseAddress;

        public uint HullAuxDataAddress { get; } =
            hullAuxDataAddress;

        public uint ReputationAddress { get; } =
            reputationAddress;

        public uint FactionCollectionAddress { get; } =
            factionCollectionAddress;

        public uint VectorBegin { get; } =
            vectorBegin;

        public uint VectorEnd { get; } =
            vectorEnd;

        public uint ExpectedReputationVTable { get; } =
            expectedReputationVTable;

        public uint ExpectedFactionArrayVTable { get; } =
            expectedFactionArrayVTable;

        public uint ExpectedFactionEntryVTable { get; } =
            expectedFactionEntryVTable;

        public FactionCache[] Factions { get; } =
            factions;

        public CachedString Affiliation { get; } =
            new();
    }

    private sealed class FactionCache(
        uint address)
    {
        public uint Address { get; } =
            address;

        public CachedString FactionKey { get; } =
            new();
    }

    private sealed class CachedString
    {
        public uint Address { get; set; }

        public string Value { get; set; } = "";

        public void Clear()
        {
            this.Address = 0;
            this.Value = "";
        }
    }
}
