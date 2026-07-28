namespace Net7ClientManager.Shopping;

using System.Collections.ObjectModel;
using Net7ClientManager.Addons.Contracts;
using Net7ClientManager.Observations;
using Net7ClientManager.Observations.Models;
using Net7ClientManager.PilotArchive;

internal static class ShoppingOwnershipBuilder
{
    private static readonly IReadOnlySet<string> emptyCollections =
        new HashSet<string>(StringComparer.Ordinal);

    public static ShoppingOwnershipSnapshot Build(
        PilotArchiveStore archive,
        IReadOnlyList<ClientObservationSnapshot> snapshots,
        uint? activeCharacterId,
        int? activeProcessId)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(snapshots);

        var activeSnapshot = ResolveActiveSnapshot(
            snapshots,
            activeCharacterId,
            activeProcessId,
            out var resolvedCharacterId,
            out var activePilotName);
        activeCharacterId = resolvedCharacterId ?? activeCharacterId;

        Dictionary<int, MutableOwnership> ownership = [];
        var pilots = archive.GetPilots();

        foreach (var pilot in pilots)
        {
            var details = archive.GetPilot(pilot.CharacterId);
            if (details == null)
            {
                continue;
            }

            var isActive = activeCharacterId == pilot.CharacterId;
            if (isActive && string.IsNullOrWhiteSpace(activePilotName))
            {
                activePilotName = pilot.Name;
            }

            if (!isActive || activeSnapshot == null)
            {
                AddArchivedPilot(
                    ownership,
                    details,
                    isActive,
                    isLive: false,
                    excludedCollections: emptyCollections);
                continue;
            }

            var excluded = AddLivePilot(
                ownership,
                activeSnapshot,
                pilot.CharacterId,
                pilot.Name);
            AddArchivedPilot(
                ownership,
                details,
                isActive: true,
                isLive: false,
                excludedCollections: excluded);
        }

        if (activeSnapshot != null &&
            activeCharacterId.HasValue &&
            pilots.All(pilot => pilot.CharacterId != activeCharacterId.Value))
        {
            AddLivePilot(
                ownership,
                activeSnapshot,
                activeCharacterId.Value,
                activePilotName);
        }

        var frozen = ownership
            .OrderBy(pair => pair.Key)
            .ToDictionary(
                pair => pair.Key,
                pair => pair.Value.ToSnapshot(),
                EqualityComparer<int>.Default);

        return new ShoppingOwnershipSnapshot
        {
            ActiveCharacterId = activeCharacterId,
            ActivePilotName = activePilotName,
            ActivePilotIsLive = activeSnapshot != null,
            BuiltAtUtc = DateTimeOffset.UtcNow,
            ItemsByTemplateId =
                new ReadOnlyDictionary<int, ShoppingItemOwnership>(frozen),
        };
    }

    private static ClientObservationSnapshot? ResolveActiveSnapshot(
        IReadOnlyList<ClientObservationSnapshot> snapshots,
        uint? activeCharacterId,
        int? activeProcessId,
        out uint? resolvedCharacterId,
        out string activePilotName)
    {
        resolvedCharacterId = activeCharacterId;
        activePilotName = "";

        var candidates = snapshots
            .Where(snapshot =>
                snapshot.LifecycleState == ClientLifecycleState.InGame)
            .OrderByDescending(snapshot => snapshot.ObservedAt)
            .ToArray();

        if (activeProcessId.HasValue)
        {
            candidates = candidates
                .OrderByDescending(snapshot =>
                    snapshot.ProcessId == activeProcessId.Value)
                .ThenByDescending(snapshot => snapshot.ObservedAt)
                .ToArray();
        }

        foreach (var snapshot in candidates)
        {
            var identity = ClientLiveCharacterIdentityResolver.Resolve(snapshot);
            if (identity.CharacterObjectId is not { } characterId)
            {
                continue;
            }

            if (activeCharacterId.HasValue &&
                characterId != activeCharacterId.Value &&
                (!activeProcessId.HasValue ||
                 snapshot.ProcessId != activeProcessId.Value))
            {
                continue;
            }

            if (activeProcessId.HasValue &&
                snapshot.ProcessId != activeProcessId.Value &&
                !activeCharacterId.HasValue)
            {
                continue;
            }

            resolvedCharacterId = characterId;
            activePilotName = identity.Name?.Trim() ?? "";
            return snapshot;
        }

        return null;
    }

    private static HashSet<string> AddLivePilot(
        Dictionary<int, MutableOwnership> ownership,
        ClientObservationSnapshot snapshot,
        uint characterId,
        string pilotName)
    {
        HashSet<string> included = new(StringComparer.Ordinal);
        var player = snapshot.LocalPlayer;
        var inventory = player.Inventory;

        if (inventory.IsAvailable &&
            IsCompleteSlots(inventory.CargoSlots, expectedCount: 40))
        {
            AddLiveSlots(
                ownership,
                inventory.CargoSlots,
                characterId,
                pilotName,
                "Cargo",
                snapshot.ObservedAt);
            included.Add(PilotArchiveSections.Cargo);
        }

        if (inventory.IsAvailable &&
            IsCompleteSlots(inventory.EquippedSlots, expectedCount: 20) &&
            IsCompleteSlots(inventory.AmmoSlots, expectedCount: 20))
        {
            AddLiveSlots(
                ownership,
                inventory.EquippedSlots,
                characterId,
                pilotName,
                "Equipped",
                snapshot.ObservedAt);
            AddLiveSlots(
                ownership,
                inventory.AmmoSlots,
                characterId,
                pilotName,
                "Loaded ammo",
                snapshot.ObservedAt);
            included.Add(PilotArchiveSections.Equipment);
        }

        if (player.SecureInventory.IsAvailable &&
            player.SecureInventory.ReadErrorCount == 0 &&
            IsCompleteSlots(
                player.SecureInventory.Slots,
                ClientSecureInventoryObservation.ExpectedSlotCount))
        {
            AddLiveSlots(
                ownership,
                player.SecureInventory.Slots,
                characterId,
                pilotName,
                "Vault",
                snapshot.ObservedAt);
            included.Add(PilotArchiveSections.Vault);
        }

        return included;
    }

    private static void AddArchivedPilot(
        Dictionary<int, MutableOwnership> ownership,
        PilotArchivePilotDetails details,
        bool isActive,
        bool isLive,
        IReadOnlySet<string> excludedCollections)
    {
        if (!excludedCollections.Contains(PilotArchiveSections.Cargo))
        {
            AddArchivedSlots(
                ownership,
                details.CargoSlots,
                details.Pilot,
                isActive,
                isLive,
                "Cargo",
                GetObservedAt(details, PilotArchiveSections.Cargo));
        }

        if (!excludedCollections.Contains(PilotArchiveSections.Equipment))
        {
            AddArchivedSlots(
                ownership,
                details.EquipmentSlots,
                details.Pilot,
                isActive,
                isLive,
                "Equipped",
                GetObservedAt(details, PilotArchiveSections.Equipment));
            AddArchivedSlots(
                ownership,
                details.AmmoSlots,
                details.Pilot,
                isActive,
                isLive,
                "Loaded ammo",
                GetObservedAt(details, PilotArchiveSections.Equipment));
        }

        if (!excludedCollections.Contains(PilotArchiveSections.Vault))
        {
            AddArchivedSlots(
                ownership,
                details.VaultSlots,
                details.Pilot,
                isActive,
                isLive,
                "Vault",
                GetObservedAt(details, PilotArchiveSections.Vault));
        }
    }

    private static DateTimeOffset? GetObservedAt(
        PilotArchivePilotDetails details,
        string section)
    {
        return details.SectionObservedAt.TryGetValue(section, out var observedAt)
            ? observedAt
            : null;
    }

    private static void AddLiveSlots(
        Dictionary<int, MutableOwnership> ownership,
        IEnumerable<ClientInventoryItemObservation> slots,
        uint characterId,
        string pilotName,
        string collection,
        DateTimeOffset observedAt)
    {
        foreach (var slot in slots.Where(slot => slot.IsOccupied))
        {
            Add(
                ownership,
                slot.ItemTemplateId!.Value,
                ResolveQuantity(slot.StackCount),
                characterId,
                pilotName,
                isActive: true,
                isLive: true,
                collection: collection,
                observedAt: observedAt);
        }
    }

    private static void AddArchivedSlots(
        Dictionary<int, MutableOwnership> ownership,
        IEnumerable<AddonInventorySlotSnapshot> slots,
        PilotArchivePilotSnapshot pilot,
        bool isActive,
        bool isLive,
        string collection,
        DateTimeOffset? observedAt)
    {
        foreach (var slot in slots.Where(slot =>
                     slot.IsOccupied &&
                     slot.TemplateId is > 0))
        {
            Add(
                ownership,
                slot.TemplateId!.Value,
                ResolveQuantity(slot.StackCount),
                pilot.CharacterId,
                pilot.Name,
                isActive,
                isLive,
                collection,
                observedAt);
        }
    }

    private static void Add(
        Dictionary<int, MutableOwnership> ownership,
        int itemTemplateId,
        long quantity,
        uint characterId,
        string pilotName,
        bool isActive,
        bool isLive,
        string collection,
        DateTimeOffset? observedAt)
    {
        if (itemTemplateId <= 0 || quantity <= 0)
        {
            return;
        }

        if (!ownership.TryGetValue(itemTemplateId, out var item))
        {
            item = new MutableOwnership(itemTemplateId);
            ownership.Add(itemTemplateId, item);
        }

        item.Add(new ShoppingOwnershipLocation
        {
            CharacterId = characterId,
            PilotName = pilotName,
            IsActivePilot = isActive,
            IsLive = isLive,
            Collection = collection,
            Quantity = quantity,
            ObservedAtUtc = observedAt,
        });
    }

    private static bool IsCompleteSlots(
        IReadOnlyList<ClientInventoryItemObservation> slots,
        int expectedCount)
    {
        return slots.Count == expectedCount &&
               slots.All(slot =>
                   slot.IsOccupied ||
                   slot.IsUsableEmpty ||
                   slot.IsUnavailable);
    }

    private static long ResolveQuantity(int? stackCount) =>
        stackCount is > 0
            ? stackCount.Value
            : 1;

    private sealed class MutableOwnership(int itemTemplateId)
    {
        private readonly List<ShoppingOwnershipLocation> locations = [];

        public void Add(ShoppingOwnershipLocation location)
        {
            this.locations.Add(location);
        }

        public ShoppingItemOwnership ToSnapshot()
        {
            return new ShoppingItemOwnership
            {
                ItemTemplateId = itemTemplateId,
                AvailableNow = this.locations
                    .Where(location => location.IsActivePilot)
                    .Sum(location => location.Quantity),
                OwnedElsewhere = this.locations
                    .Where(location => !location.IsActivePilot)
                    .Sum(location => location.Quantity),
                Locations = this.locations
                    .OrderByDescending(location => location.IsActivePilot)
                    .ThenBy(location => location.PilotName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(location => location.Collection, StringComparer.Ordinal)
                    .ToArray(),
            };
        }
    }
}
