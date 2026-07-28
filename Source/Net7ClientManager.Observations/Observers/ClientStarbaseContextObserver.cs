// ReSharper disable IdentifierTypo
namespace Net7ClientManager.Observations.Observers;

using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Net7ClientManager.Observations.Models;

internal sealed class ClientStarbaseContextObserver
{
    private const uint ImageBase = 0x00400000;

    private const uint ClientContextStarbaseView = 0x135c;
    private const uint ClientContextCurrentStarbaseId = 0x1324;

    /*
     * StarbaseView owns the operational room state directly.
     * +0x50 is committed/current, +0x54 is requested/pending, and
     * +0x58 remembers the previous room during a transition.
     */
    private const uint StarbaseViewCurrentRoomClass = 0x50;
    private const uint StarbaseViewPendingRoomClass = 0x54;
    private const uint StarbaseViewPreviousRoomClass = 0x58;
    private const uint StarbaseViewCurrentInterfaceCommand = 0x5c;
    private const uint StarbaseViewCurrentRoomDisplayController = 0x60;
    private const uint StarbaseViewCurrentInterface = 0x64;
    private const uint StarbaseViewDefinition = 0x1b4;

    /*
     * StarbaseDefinition begins with the room hash map. The map header
     * follows the client's standard chained UInt32 -> pointer map layout.
     */
    private const uint StarbaseDefinitionRoomBucketArrayBegin = 0x08;
    private const uint StarbaseDefinitionRoomBucketArrayEnd = 0x0c;
    private const uint StarbaseDefinitionRoomEntryCount = 0x14;
    private const uint StarbaseDefinitionId = 0x18;

    /*
     * Native room-class consumers translate the presentation room class
     * through DAT_00B90F3C before looking up StarbaseDefinition.RoomMap.
     * The room map key is therefore not itself the operational room class.
     */
    private const uint RoomDefinitionKeyByRoomClassStatic =
        0x00b90f3c;

    private const uint RoomDefinitionKeyByRoomClassRva =
        RoomDefinitionKeyByRoomClassStatic - ImageBase;

    private const int SupportedRoomClassCount = 4;

    private const uint ControllerRoomClass = 0xac;
    private const uint ControllerStarbaseView = 0xb0;
    private const uint ControllerRoomDefinition = 0xb4;
    private const uint ControllerSelectedFacilitySlot = 0xc0;
    private const uint ControllerSelectedNpcSlot = 0xc4;
    private const uint ControllerSelectedPlayerInteractionKey = 0xc8;
    private const uint ControllerNpcWrapperArray = 0x13c;
    private const uint ControllerPlayerInteractionMenu = 0x1f8;
    private const uint ControllerInteractionTransitionFlags = 0x1fc;
    private const int ControllerInteractionTransitionFlagCount = 6;
    private const uint ControllerInputStateFlags = 0x214;
    private const int ControllerInputStateFlagCount = 7;

    private const uint RoomDefinitionKey = 0x00;
    private const uint RoomFacilityBucketArrayBegin = 0x2c;
    private const uint RoomFacilityBucketArrayEnd = 0x30;
    private const uint RoomFacilityEntryCount = 0x38;
    private const uint RoomNpcBucketArrayBegin = 0x44;
    private const uint RoomNpcBucketArrayEnd = 0x48;
    private const uint RoomNpcEntryCount = 0x50;

    private const uint HashNodeNext = 0x00;
    private const uint HashNodeKey = 0x04;
    private const uint HashNodeValue = 0x08;

    private const uint NpcWrapperPrimaryAvatar = 0x04;
    private const uint NpcWrapperSecondaryAvatar = 0x08;
    private const uint NpcDefinitionVendorType = 0x18;
    private const uint NpcDefinitionAmbientType = 0x1c;

    /*
     * LoungeNPC packet processing allocates a 0x12C-byte NPC definition.
     * The first 0x20 bytes contain slot/id, an optional shared display-name
     * string, vendor type, and ambient type. FUN_004BED80 then copies the
     * packet's complete 0x10C-byte internal AvatarData into definition +0x20.
     * AvatarData stores fixed-width Latin-1 first/last names at +0xD4/+0xE8.
     */
    private const uint NpcDefinitionAvatarData = 0x20;
    private const uint AvatarDataFirstName = 0xd4;
    private const uint AvatarDataLastName = 0xe8;
    private const int AvatarDataNameFieldLength = 20;

    private const uint TalkTreeNpcNameWidget = 0x110;
    private const uint TextWidgetTextPointer = 0xfc;

    private const int ControllerSnapshotLength =
        checked((int)ControllerInputStateFlags +
                ControllerInputStateFlagCount);

    private const int RoomDefinitionSnapshotLength = 0x54;
    private const int MaximumNpcNameLength = 256;
    private const int MaximumRoomCount = 64;
    private const int MaximumRoomFacilityCount = 64;
    private const int MaximumRoomNpcCount = 64;
    private const int MaximumHashBucketCount = 65536;
    private const int MaximumHashChainLength = 4096;

    private static readonly TimeSpan discoveryRetryInterval =
        TimeSpan.FromSeconds(5);

    private readonly System.Threading.Lock cacheLock = new();

    private readonly Dictionary<int, ProcessCache> processCaches = [];

    public void Refresh(
        ProcessMemoryReader memory,
        ObservedClientState state)
    {
        if (!state.HasDirectClientState ||
            state.ClientContextAddress == 0)
        {
            this.Forget(state.ProcessId);

            state.StarbaseContext =
                ClientStarbaseContextObservation.Unavailable(
                    "Direct SClient state is unavailable");

            return;
        }

        if (!TryReadUInt32(
                memory,
                state.ClientContextAddress,
                ClientContextStarbaseView,
                "SClient.StarbaseView",
                out var starbaseViewAddress,
                out var viewError))
        {
            this.Forget(state.ProcessId);

            state.StarbaseContext =
                ClientStarbaseContextObservation.Unavailable(
                    viewError);

            return;
        }

        if (starbaseViewAddress == 0)
        {
            this.Forget(state.ProcessId);

            state.StarbaseContext =
                ClientStarbaseContextObservation.Unavailable(
                    "Starbase view is not present in the current SClient");

            return;
        }

        List<string> errors = [];

        var starbaseId = ReadUInt32(
            memory,
            state.ClientContextAddress,
            ClientContextCurrentStarbaseId,
            "SClient.CurrentStarbaseId",
            errors);

        var currentRoomClass = ReadInt32(
            memory,
            starbaseViewAddress,
            StarbaseViewCurrentRoomClass,
            "StarbaseView.CurrentRoomClass",
            errors,
            defaultValue: -1);

        var pendingRoomClass = ReadInt32(
            memory,
            starbaseViewAddress,
            StarbaseViewPendingRoomClass,
            "StarbaseView.PendingRoomClass",
            errors,
            defaultValue: -1);

        var previousRoomClass = ReadInt32(
            memory,
            starbaseViewAddress,
            StarbaseViewPreviousRoomClass,
            "StarbaseView.PreviousRoomClass",
            errors,
            defaultValue: -1);

        var currentRoomDisplayControllerAddress = ReadUInt32(
            memory,
            starbaseViewAddress,
            StarbaseViewCurrentRoomDisplayController,
            "StarbaseView.CurrentRoomDisplayController",
            errors);

        var currentInterfaceCommand = ReadInt32(
            memory,
            starbaseViewAddress,
            StarbaseViewCurrentInterfaceCommand,
            "StarbaseView.CurrentInterfaceCommand",
            errors,
            defaultValue: -1);

        var currentInterfaceAddress = ReadUInt32(
            memory,
            starbaseViewAddress,
            StarbaseViewCurrentInterface,
            "StarbaseView.CurrentInterface",
            errors);

        var starbaseDefinitionAddress = ReadUInt32(
            memory,
            starbaseViewAddress,
            StarbaseViewDefinition,
            "StarbaseView.StarbaseDefinition",
            errors);

        var starbaseDefinitionId = starbaseDefinitionAddress == 0
            ? 0
            : ReadUInt32(
                memory,
                starbaseDefinitionAddress,
                StarbaseDefinitionId,
                "StarbaseDefinition.Id",
                errors);

        var roomDefinitions =
            EnumerateStarbaseRoomDefinitions(
                memory,
                state.ModuleBaseAddress,
                starbaseDefinitionAddress);

        if (!string.IsNullOrWhiteSpace(
                roomDefinitions.Error))
        {
            errors.Add(roomDefinitions.Error);
        }

        var cache = this.GetOrCreateCache(
            state,
            starbaseViewAddress);

        var controllerDiscovery =
            this.ResolveRoomControllers(
                memory,
                cache,
                roomDefinitions.Rooms);

        var rooms = ObserveRooms(
            memory,
            roomDefinitions.Rooms,
            controllerDiscovery.Controllers,
            currentRoomClass,
            errors);

        var currentRoom = rooms.FirstOrDefault(
            room => room.RoomClass == currentRoomClass);

        var currentController =
            ResolveCurrentRoomController(
                controllerDiscovery.Controllers,
                currentRoom);

        var playerInteractionMenuAddress = currentController == null
            ? 0
            : TryReadUInt32Value(
                memory,
                currentController.Address,
                ControllerPlayerInteractionMenu);

        byte playerInteractionMenuActiveFlag = 0;

        if (playerInteractionMenuAddress != 0 &&
            !TryReadByte(
                memory,
                playerInteractionMenuAddress,
                ClientStarbaseInteractionCatalog.PanelActiveFlagOffset,
                "StarbaseController.PlayerInteractionMenu.Active",
                out playerInteractionMenuActiveFlag,
                out var playerInteractionMenuError))
        {
            errors.Add(playerInteractionMenuError);
        }

        var panels = ObservePanels(
            memory,
            starbaseViewAddress,
            errors);

        var selectedNpcSlot = currentController == null
            ? -1
            : TryReadNullableInt32Value(
                memory,
                currentController.Address,
                ControllerSelectedNpcSlot) ?? -1;

        var interactionRead = ObserveInteractionContext(
            memory,
            panels,
            playerInteractionMenuAddress,
            playerInteractionMenuActiveFlag,
            currentRoomClass,
            selectedNpcSlot,
            currentRoom);

        var currentInterfaceKind =
            ClientStarbaseInteractionCatalog.GetCurrentInterfaceKind(
                currentInterfaceCommand,
                interactionRead.VendorTradeControllerAddress);

        var currentInterfacePanel = panels.FirstOrDefault(
            panel =>
                panel.Address != 0 &&
                panel.Address == currentInterfaceAddress);

        if (currentInterfaceKind ==
                ClientStarbaseInteractionKind.None &&
            currentInterfacePanel != null)
        {
            currentInterfaceKind =
                currentInterfacePanel.ActiveInteractionKind;
        }

        var currentInterfaceIsActive =
            currentInterfaceAddress != 0 &&
            currentInterfacePanel?.IsActive == true;

        var status = BuildStatus(
            rooms,
            currentRoomClass,
            pendingRoomClass,
            interactionRead.Observation,
            errors);

        state.StarbaseContext =
            new ClientStarbaseContextObservation
            {
                IsAvailable = true,
                Status = status,
                OwnerClientContextAddress =
                    state.ClientContextAddress,
                StarbaseViewAddress =
                    starbaseViewAddress,
                StarbaseId = starbaseId,
                StarbaseDefinitionAddress =
                    starbaseDefinitionAddress,
                StarbaseDefinitionId =
                    starbaseDefinitionId,
                CurrentRoomClass =
                    currentRoomClass,
                PendingRoomClass =
                    pendingRoomClass,
                PreviousRoomClass =
                    previousRoomClass,
                CurrentRoomDisplayControllerAddress =
                    currentRoomDisplayControllerAddress,
                ControllerAddress =
                    currentController?.Address ?? 0,
                ControllerResolutionStatus =
                    controllerDiscovery.Status,
                ReadErrorCount =
                    errors.Count,
                Rooms = rooms,
                Interaction =
                    interactionRead.Observation,
                Diagnostics =
                    new ClientStarbaseDiagnosticsObservation
                    {
                        CurrentInterfaceCommand =
                            currentInterfaceCommand,
                        CurrentInterfaceAddress =
                            currentInterfaceAddress,
                        CurrentInterfaceKind =
                            currentInterfaceKind,
                        CurrentInterfaceIsActive =
                            currentInterfaceIsActive,
                        ActiveInteractionKinds =
                            interactionRead.ActiveKinds,
                        TalkTreePanelAddress =
                            interactionRead.TalkTreePanelAddress,
                        TalkTreePanelActiveFlag =
                            interactionRead.TalkTreePanelActiveFlag,
                        VendorTradeControllerAddress =
                            interactionRead.VendorTradeControllerAddress,
                        TalkTreeNpcNameWidgetAddress =
                            interactionRead.NpcNameWidgetAddress,
                        PlayerTradeInterfaceAddress =
                            interactionRead.PlayerTradeInterfaceAddress,
                        PlayerTradeInterfaceActiveFlag =
                            interactionRead.PlayerTradeInterfaceActiveFlag,
                        PlayerInteractionMenuAddress =
                            playerInteractionMenuAddress,
                        PlayerInteractionMenuActiveFlag =
                            playerInteractionMenuActiveFlag,
                        ReverseReferenceHitCount =
                            controllerDiscovery.ReferenceHitCount,
                        RoomControllers =
                            [.. controllerDiscovery.Controllers
                                .Select(
                                    controller =>
                                        new ClientStarbaseRoomControllerObservation
                                        {
                                            Address = controller.Address,
                                            RoomClass = controller.RoomClass,
                                            RoomDefinitionAddress =
                                                controller.RoomDefinitionAddress,
                                            IsCurrent =
                                                controller.Address ==
                                                currentController?.Address,
                                            Status = controller.Status,
                                        })],
                        Panels = panels,
                        ReadErrors = [.. errors],
                    },
            };
    }

    public void Forget(
        int processId)
    {
        lock (this.cacheLock)
        {
            this.processCaches.Remove(
                processId);
        }
    }

    private ProcessCache GetOrCreateCache(
        ObservedClientState state,
        uint starbaseViewAddress)
    {
        lock (this.cacheLock)
        {
            if (this.processCaches.TryGetValue(
                    state.ProcessId,
                    out var existing) &&
                existing.ModuleBaseAddress ==
                    state.ModuleBaseAddress &&
                existing.ClientContextAddress ==
                    state.ClientContextAddress &&
                existing.StarbaseViewAddress ==
                    starbaseViewAddress)
            {
                return existing;
            }

            var created = new ProcessCache(
                state.ModuleBaseAddress,
                state.ClientContextAddress,
                starbaseViewAddress);

            this.processCaches[state.ProcessId] = created;

            return created;
        }
    }

    private RoomControllerDiscovery ResolveRoomControllers(
        ProcessMemoryReader memory,
        ProcessCache cache,
        IReadOnlyList<RoomDefinitionReference> rooms)
    {
        var validatedCached = cache.Controllers
            .Select(
                controller =>
                    TryReadRoomController(
                        memory,
                        controller.Address,
                        cache.StarbaseViewAddress,
                        rooms,
                        out var validated)
                            ? validated
                            : null)
            .Where(controller => controller != null)
            .Select(controller => controller!)
            .DistinctBy(controller => controller.Address)
            .OrderBy(controller => controller.RoomClass)
            .ThenBy(controller => controller.Address)
            .ToArray();

        cache.Controllers = validatedCached;

        var hasCompleteCoverage = rooms.Count > 0 &&
            rooms.All(
                room => validatedCached.Any(
                    controller =>
                        controller.RoomClass == room.RoomClass &&
                        controller.RoomDefinitionAddress ==
                            room.DefinitionAddress));

        var now = DateTimeOffset.UtcNow;
        var retryAllowed =
            !cache.LastDiscoveryAttemptAt.HasValue ||
            now - cache.LastDiscoveryAttemptAt.Value >=
                discoveryRetryInterval;

        if ((!hasCompleteCoverage ||
             validatedCached.Length == 0) &&
            retryAllowed)
        {
            cache.LastDiscoveryAttemptAt = now;

            HashSet<uint> inspected = [];
            List<RoomControllerCandidate> discovered = [];
            var referenceHitCount = 0;

            foreach (var referenceAddress
                     in memory.ScanForUInt32(
                         cache.StarbaseViewAddress))
            {
                referenceHitCount++;

                if (referenceAddress < ControllerStarbaseView)
                {
                    continue;
                }

                var candidateAddress =
                    referenceAddress -
                    ControllerStarbaseView;

                if ((candidateAddress & 3) != 0 ||
                    !inspected.Add(candidateAddress))
                {
                    continue;
                }

                if (!TryReadRoomController(
                        memory,
                        candidateAddress,
                        cache.StarbaseViewAddress,
                        rooms,
                        out var candidate))
                {
                    continue;
                }

                if (candidate != null)
                {
                    discovered.Add(candidate);
                }
            }

            cache.LastReferenceHitCount =
                referenceHitCount;

            cache.Controllers = [.. discovered
                .DistinctBy(controller => controller.Address)
                .OrderBy(controller => controller.RoomClass)
                .ThenBy(controller => controller.Address)];
        }

        var status = cache.Controllers.Count switch
        {
            0 when rooms.Count == 0 =>
                "Room controllers were not resolved because the starbase room map is unavailable",
            0 =>
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"No room controller matched the {rooms.Count} StarbaseDefinition room(s); scanned {cache.LastReferenceHitCount} StarbaseView reference hit(s)"),
            _ =>
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Resolved {cache.Controllers.Count} room controller(s) by exact StarbaseView backlink and RoomDefinition match; scanned {cache.LastReferenceHitCount} reference hit(s)"),
        };

        return new RoomControllerDiscovery(
            cache.Controllers,
            cache.LastReferenceHitCount,
            status);
    }

    private static bool TryReadRoomController(
        ProcessMemoryReader memory,
        uint candidateAddress,
        uint expectedStarbaseViewAddress,
        IReadOnlyList<RoomDefinitionReference> rooms,
        out RoomControllerCandidate? candidate)
    {
        candidate = null;

        if (!memory.TryReadBytes(
                candidateAddress,
                ControllerSnapshotLength,
                out var snapshot))
        {
            return false;
        }

        if (ReadUInt32(
                snapshot,
                ControllerStarbaseView) !=
            expectedStarbaseViewAddress)
        {
            return false;
        }

        var roomClass = ReadInt32(
            snapshot,
            ControllerRoomClass);

        if (roomClass is < 0 or >= SupportedRoomClassCount)
        {
            return false;
        }

        var roomDefinitionAddress = ReadUInt32(
            snapshot,
            ControllerRoomDefinition);

        var expectedRoom = rooms.FirstOrDefault(
            room =>
                room.RoomClass == roomClass &&
                room.DefinitionAddress ==
                    roomDefinitionAddress);

        if (roomDefinitionAddress == 0 ||
            expectedRoom.DefinitionAddress == 0 ||
            !memory.TryReadBytes(
                roomDefinitionAddress,
                RoomDefinitionSnapshotLength,
                out var roomSnapshot) ||
            ReadInt32(
                roomSnapshot,
                RoomDefinitionKey) != expectedRoom.DefinitionKey)
        {
            return false;
        }

        var selectedFacilitySlot = ReadInt32(
            snapshot,
            ControllerSelectedFacilitySlot);

        var selectedNpcSlot = ReadInt32(
            snapshot,
            ControllerSelectedNpcSlot);

        var selectedPlayerInteractionKey = ReadInt32(
            snapshot,
            ControllerSelectedPlayerInteractionKey);

        if (selectedFacilitySlot is < -1 or > 3 ||
            selectedNpcSlot is < -1 or > 13 ||
            selectedPlayerInteractionKey is < -1 or > 255 ||
            !ContainsOnlyBooleanBytes(
                snapshot,
                ControllerInteractionTransitionFlags,
                ControllerInteractionTransitionFlagCount) ||
            !ContainsOnlyBooleanBytes(
                snapshot,
                ControllerInputStateFlags,
                ControllerInputStateFlagCount))
        {
            return false;
        }

        candidate = new RoomControllerCandidate(
            candidateAddress,
            roomClass,
            roomDefinitionAddress,
            "Validated by StarbaseView backlink, exact room definition, selectors, and Boolean transition/input fields");

        return true;
    }

    private static IReadOnlyList<ClientStarbaseRoomObservation> ObserveRooms(
        ProcessMemoryReader memory,
        IReadOnlyList<RoomDefinitionReference> roomDefinitions,
        IReadOnlyList<RoomControllerCandidate> roomControllers,
        int currentRoomClass,
        List<string> errors)
    {
        List<ClientStarbaseRoomObservation> rooms = [];

        foreach (var roomDefinition in roomDefinitions
                     .OrderBy(room => room.RoomClass))
        {
            var controller = roomControllers.FirstOrDefault(
                candidate =>
                    candidate.RoomClass == roomDefinition.RoomClass &&
                    candidate.RoomDefinitionAddress ==
                        roomDefinition.DefinitionAddress);

            var facilities = EnumerateRoomFacilities(
                memory,
                roomDefinition.DefinitionAddress);

            var npcs = EnumerateRoomNpcs(
                memory,
                controller?.Address ?? 0,
                roomDefinition.DefinitionAddress);

            List<string> roomErrors = [];

            if (!string.IsNullOrWhiteSpace(facilities.Error))
            {
                roomErrors.Add(facilities.Error);
                errors.Add(facilities.Error);
            }

            if (!string.IsNullOrWhiteSpace(npcs.Error))
            {
                roomErrors.Add(npcs.Error);
                errors.Add(npcs.Error);
            }

            var status = roomErrors.Count == 0
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"Available; {facilities.Facilities.Count} facility definition(s), {npcs.Npcs.Count} NPC definition(s)")
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"Available with {roomErrors.Count} read error(s): {roomErrors[0]}");

            rooms.Add(
                new ClientStarbaseRoomObservation
                {
                    RoomClass = roomDefinition.RoomClass,
                    DefinitionKey =
                        roomDefinition.DefinitionKey,
                    DefinitionAddress =
                        roomDefinition.DefinitionAddress,
                    ControllerAddress =
                        controller?.Address ?? 0,
                    IsCurrent =
                        roomDefinition.RoomClass == currentRoomClass,
                    Status = status,
                    Facilities = facilities.Facilities,
                    Npcs = npcs.Npcs,
                });
        }

        return rooms;
    }

    private static RoomControllerCandidate? ResolveCurrentRoomController(
        IReadOnlyList<RoomControllerCandidate> controllers,
        ClientStarbaseRoomObservation? currentRoom)
    {
        if (currentRoom == null)
        {
            return null;
        }

        return controllers.FirstOrDefault(
            controller =>
                controller.RoomClass == currentRoom.RoomClass &&
                controller.RoomDefinitionAddress ==
                    currentRoom.DefinitionAddress);
    }

    private static RoomDefinitionCatalog EnumerateStarbaseRoomDefinitions(
        ProcessMemoryReader memory,
        uint moduleBaseAddress,
        uint starbaseDefinitionAddress)
    {
        if (starbaseDefinitionAddress == 0)
        {
            return new RoomDefinitionCatalog(
                [],
                "StarbaseDefinition is not loaded yet",
                "");
        }

        var map = EnumeratePointerMap(
            memory,
            starbaseDefinitionAddress,
            StarbaseDefinitionRoomBucketArrayBegin,
            StarbaseDefinitionRoomBucketArrayEnd,
            StarbaseDefinitionRoomEntryCount,
            "StarbaseDefinition.RoomMap",
            MaximumRoomCount);

        uint translationTableAddress;

        try
        {
            translationTableAddress = checked(
                moduleBaseAddress +
                RoomDefinitionKeyByRoomClassRva);
        }
        catch (OverflowException)
        {
            return new RoomDefinitionCatalog(
                [],
                $"{map.Status}; room-class translation address overflowed",
                "Room-class translation table address calculation overflowed");
        }

        if (!memory.TryReadBytes(
                translationTableAddress,
                checked(
                    SupportedRoomClassCount *
                    sizeof(uint)),
                out var translationBytes))
        {
            return new RoomDefinitionCatalog(
                [],
                $"{map.Status}; room-class translation table could not be read",
                    $"Could not read room-class translation table at 0x{translationTableAddress:X8}");
        }

        Dictionary<int, int> roomClassByDefinitionKey = [];
        List<string> errors = [];

        for (var roomClass = 0;
             roomClass < SupportedRoomClassCount;
             roomClass++)
        {
            var rawDefinitionKey = BitConverter.ToUInt32(
                translationBytes,
                checked(
                    roomClass *
                    sizeof(uint)));

            var definitionKey = unchecked(
                (int)rawDefinitionKey);

            if (!roomClassByDefinitionKey.TryAdd(
                    definitionKey,
                    roomClass))
            {
                errors.Add(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Room-class translation table maps multiple classes to definition key {definitionKey}"));
            }
        }

        List<RoomDefinitionReference> rooms = [];

        foreach (var entry in map.Entries)
        {
            if (!roomClassByDefinitionKey.TryGetValue(
                    entry.Key,
                    out var roomClass))
            {
                errors.Add(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Room map key {entry.Key} at 0x{entry.ValueAddress:X8} is not referenced by the native room-class translation table"));

                continue;
            }

            var storedDefinitionKey = TryReadInt32Value(
                memory,
                entry.ValueAddress,
                RoomDefinitionKey);

            if (storedDefinitionKey != entry.Key)
            {
                errors.Add(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Room map key {entry.Key} points to definition key {storedDefinitionKey} at 0x{entry.ValueAddress:X8}"));
            }

            rooms.Add(
                new RoomDefinitionReference(
                    roomClass,
                    entry.Key,
                    entry.ValueAddress));
        }

        if (!string.IsNullOrWhiteSpace(map.Error))
        {
            errors.Insert(0, map.Error);
        }

        var mappingText = string.Join(
            ", ",
            roomClassByDefinitionKey
                .OrderBy(pair => pair.Value)
                .Select(
                    pair =>
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"{pair.Value}->{pair.Key}")));

        var status = errors.Count == 0
            ? $"{map.Status}; room-class mapping {mappingText}" : string.Create(
                CultureInfo.InvariantCulture,
                $"{map.Status}; room-class mapping {mappingText}; {errors.Count} room mapping error(s)");

        return new RoomDefinitionCatalog(
            [.. rooms.OrderBy(room => room.RoomClass)],
            status,
            errors.Count == 0
                ? ""
                : errors[0]);
    }

    private static RoomFacilityCatalog EnumerateRoomFacilities(
        ProcessMemoryReader memory,
        uint roomDefinitionAddress)
    {
        var map = EnumeratePointerMap(
            memory,
            roomDefinitionAddress,
            RoomFacilityBucketArrayBegin,
            RoomFacilityBucketArrayEnd,
            RoomFacilityEntryCount,
            "RoomDefinition.FacilityMap",
            MaximumRoomFacilityCount);

        var facilities = map.Entries
            .Select(
                entry => ObserveRoomFacility(
                    memory,
                    entry.Key,
                    entry.ValueAddress))
            .OrderBy(facility => facility.Slot)
            .ToArray();

        return new RoomFacilityCatalog(
            facilities,
            map.Status,
            map.Error);
    }

    private static ClientStarbaseFacilityObservation ObserveRoomFacility(
        ProcessMemoryReader memory,
        int slot,
        uint definitionAddress)
    {
        var definitionSlot = TryReadInt32Value(
            memory,
            definitionAddress,
            0x00);

        var facilityType = TryReadInt32Value(
            memory,
            definitionAddress,
            0x04);

        var panelDefinition =
            ClientStarbaseInteractionCatalog
                .GetPanelByFacilityType(
                    facilityType);

        return new ClientStarbaseFacilityObservation
        {
            Slot = slot,
            DefinitionAddress =
                definitionAddress,
            DefinitionSlot =
                definitionSlot,
            FacilityType =
                facilityType,
            FacilityTypeName =
                ClientStarbaseInteractionCatalog
                    .GetFacilityTypeName(
                        facilityType),
            InteractionKind =
                ClientStarbaseInteractionCatalog
                    .GetFacilityInteractionKind(
                        facilityType),
            PanelKind =
                panelDefinition?.Kind,
            CurrentInterfaceCommand =
                panelDefinition?.CurrentInterfaceCommand,
            ReservedValue =
                TryReadUInt32Value(
                    memory,
                    definitionAddress,
                    0x08),
        };
    }

    private static RoomNpcCatalog EnumerateRoomNpcs(
        ProcessMemoryReader memory,
        uint controllerAddress,
        uint roomDefinitionAddress)
    {
        var map = EnumeratePointerMap(
            memory,
            roomDefinitionAddress,
            RoomNpcBucketArrayBegin,
            RoomNpcBucketArrayEnd,
            RoomNpcEntryCount,
            "RoomDefinition.NpcMap",
            MaximumRoomNpcCount);

        var npcs = map.Entries
            .Select(
                entry => ObserveRoomNpc(
                    memory,
                    controllerAddress,
                    entry.Key,
                    entry.ValueAddress))
            .OrderBy(npc => npc.Slot)
            .ToArray();

        return new RoomNpcCatalog(
            npcs,
            map.Status,
            map.Error);
    }

    private static ClientStarbaseNpcObservation ObserveRoomNpc(
        ProcessMemoryReader memory,
        uint controllerAddress,
        int slot,
        uint definitionAddress)
    {
        var wrapperAddress =
            controllerAddress != 0 &&
            slot is >= 0 and < 14
                ? TryReadPointerArrayEntry(
                    memory,
                    controllerAddress,
                    ControllerNpcWrapperArray,
                    slot)
                : 0;

        var firstName = ReadFixedWidthLatin1Field(
            memory,
            definitionAddress,
            checked(
                NpcDefinitionAvatarData +
                AvatarDataFirstName),
            AvatarDataNameFieldLength);

        var lastName = ReadFixedWidthLatin1Field(
            memory,
            definitionAddress,
            checked(
                NpcDefinitionAvatarData +
                AvatarDataLastName),
            AvatarDataNameFieldLength);

        var name = string.Join(
            " ",
            new[] { firstName, lastName }
                .Where(part =>
                    !string.IsNullOrWhiteSpace(part)));

        return new ClientStarbaseNpcObservation
        {
            Slot = slot,
            DefinitionAddress = definitionAddress,
            DefinitionKey = TryReadInt32Value(
                memory,
                definitionAddress,
                0x00),
            DefinitionSecondaryId = TryReadInt32Value(
                memory,
                definitionAddress,
                0x04),
            Name = name,
            VendorType = NormalizeNpcVendorType(
                TryReadInt32Value(
                    memory,
                    definitionAddress,
                    NpcDefinitionVendorType)),
            AmbientType = NormalizeNpcAmbientType(
                TryReadInt32Value(
                    memory,
                    definitionAddress,
                    NpcDefinitionAmbientType)),
            WrapperAddress = wrapperAddress,
            PrimaryAvatarAddress = wrapperAddress == 0
                ? 0
                : TryReadUInt32Value(
                    memory,
                    wrapperAddress,
                    NpcWrapperPrimaryAvatar),
            SecondaryAvatarAddress = wrapperAddress == 0
                ? 0
                : TryReadUInt32Value(
                    memory,
                    wrapperAddress,
                    NpcWrapperSecondaryAvatar),
        };
    }

    private static string ReadFixedWidthLatin1Field(
        ProcessMemoryReader memory,
        uint ownerAddress,
        uint fieldOffset,
        int fieldLength)
    {
        if (ownerAddress == 0 ||
            fieldLength <= 0)
        {
            return "";
        }

        uint fieldAddress;

        try
        {
            fieldAddress = checked(
                ownerAddress +
                fieldOffset);
        }
        catch (OverflowException)
        {
            return "";
        }

        if (!memory.TryReadBytes(
                fieldAddress,
                fieldLength,
                out var bytes))
        {
            return "";
        }

        var textLength = Array.IndexOf(
            bytes,
            (byte)0);

        if (textLength < 0)
        {
            textLength = bytes.Length;
        }

        if (textLength == 0)
        {
            return "";
        }

        var value = Encoding.Latin1
            .GetString(
                bytes,
                0,
                textLength)
            .Trim();

        return value.Any(char.IsControl)
            ? ""
            : value;
    }

    private static ClientStarbaseNpcVendorType NormalizeNpcVendorType(
        int rawValue)
    {
        return rawValue is >=
                (int)ClientStarbaseNpcVendorType.Invalid and <=
                (int)ClientStarbaseNpcVendorType.BlackMarket
            ? (ClientStarbaseNpcVendorType)rawValue
            : ClientStarbaseNpcVendorType.Invalid;
    }

    private static ClientStarbaseNpcAmbientType NormalizeNpcAmbientType(
        int rawValue)
    {
        return rawValue is >=
                (int)ClientStarbaseNpcAmbientType.None and <=
                (int)ClientStarbaseNpcAmbientType.Bartender
            ? (ClientStarbaseNpcAmbientType)rawValue
            : ClientStarbaseNpcAmbientType.None;
    }

    private static PointerMapCatalog EnumeratePointerMap(
        ProcessMemoryReader memory,
        uint ownerAddress,
        uint bucketBeginOffset,
        uint bucketEndOffset,
        uint entryCountOffset,
        string mapName,
        int maximumEntryCount)
    {
        if (ownerAddress == 0)
        {
            return new PointerMapCatalog(
                [],
                $"{mapName} owner is null",
                "");
        }

        if (!TryReadUInt32(
                memory,
                ownerAddress,
                bucketBeginOffset,
                $"{mapName}.BucketBegin",
                out var bucketArrayAddress,
                out var error) ||
            !TryReadUInt32(
                memory,
                ownerAddress,
                bucketEndOffset,
                $"{mapName}.BucketEnd",
                out var bucketArrayEnd,
                out error) ||
            !TryReadUInt32(
                memory,
                ownerAddress,
                entryCountOffset,
                $"{mapName}.EntryCount",
                out var entryCount,
                out error))
        {
            return new PointerMapCatalog(
                [],
                $"{mapName} header could not be read",
                error);
        }

        if (entryCount > (uint)maximumEntryCount)
        {
            return new PointerMapCatalog(
                [],
                    $"{mapName} reports unexpected entry count {entryCount}",
                "");
        }

        if (bucketArrayAddress == 0 &&
            bucketArrayEnd == 0 &&
            entryCount == 0)
        {
            return new PointerMapCatalog(
                [],
                $"{mapName} is empty",
                "");
        }

        if (bucketArrayAddress == 0 ||
            bucketArrayEnd < bucketArrayAddress)
        {
            return new PointerMapCatalog(
                [],
                $"{mapName} bucket array is inconsistent",
                "");
        }

        var bucketBytes =
            bucketArrayEnd - bucketArrayAddress;

        if (bucketBytes % sizeof(uint) != 0)
        {
            return new PointerMapCatalog(
                [],
                $"{mapName} bucket array is not DWORD-aligned",
                "");
        }

        var bucketCount = checked(
            (int)(bucketBytes / sizeof(uint)));

        if (bucketCount is <= 0 or > MaximumHashBucketCount)
        {
            return new PointerMapCatalog(
                [],
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{mapName} has unexpected bucket count {bucketCount}"),
                "");
        }

        if (!memory.TryReadBytes(
                bucketArrayAddress,
                checked(bucketCount * sizeof(uint)),
                out var bucketPointers))
        {
            return new PointerMapCatalog(
                [],
                $"{mapName} bucket array could not be read",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Could not read {bucketCount} buckets at 0x{bucketArrayAddress:X8}"));
        }

        HashSet<uint> visitedNodes = [];
        Dictionary<int, uint> entries = [];

        for (var bucketIndex = 0;
             bucketIndex < bucketCount;
             bucketIndex++)
        {
            var nodeAddress = BitConverter.ToUInt32(
                bucketPointers,
                checked(bucketIndex * sizeof(uint)));

            for (var chainIndex = 0;
                 nodeAddress != 0 &&
                 chainIndex < MaximumHashChainLength;
                 chainIndex++)
            {
                if (!visitedNodes.Add(nodeAddress))
                {
                    break;
                }

                if (!memory.TryReadBytes(
                        nodeAddress,
                        0x0c,
                        out var nodeBytes))
                {
                    return new PointerMapCatalog(
                        [.. entries
                            .Select(
                                pair =>
                                    new PointerMapEntry(
                                        pair.Key,
                                        pair.Value))
                            .OrderBy(entry => entry.Key)],
                        $"{mapName} node could not be read",
                            $"Could not read map node at 0x{nodeAddress:X8}");
                }

                var nextAddress = BitConverter.ToUInt32(
                    nodeBytes,
                    checked((int)HashNodeNext));

                var key = BitConverter.ToInt32(
                    nodeBytes,
                    checked((int)HashNodeKey));

                var valueAddress = BitConverter.ToUInt32(
                    nodeBytes,
                    checked((int)HashNodeValue));

                if (key >= 0 &&
                    valueAddress != 0)
                {
                    entries.TryAdd(key, valueAddress);
                }

                nodeAddress = nextAddress;
            }
        }

        var orderedEntries = entries
            .Select(
                pair =>
                    new PointerMapEntry(
                        pair.Key,
                        pair.Value))
            .OrderBy(entry => entry.Key)
            .ToArray();

        var status = (uint)orderedEntries.Length == entryCount
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"Enumerated {orderedEntries.Length} {mapName} entry/entries")
            : string.Create(
                CultureInfo.InvariantCulture,
                $"Enumerated {orderedEntries.Length} {mapName} entry/entries; map reports {entryCount}");

        return new PointerMapCatalog(
            orderedEntries,
            status,
            "");
    }

    private static IReadOnlyList<ClientStarbasePanelObservation> ObservePanels(
        ProcessMemoryReader memory,
        uint starbaseViewAddress,
        List<string> errors)
    {
        List<ClientStarbasePanelObservation> panels = [];

        foreach (var definition
                 in ClientStarbaseInteractionCatalog.Panels)
        {
            var panelAddress = ReadUInt32(
                memory,
                starbaseViewAddress,
                definition.ViewFieldOffset,
                    $"StarbaseView.{definition.Kind}",
                errors);

            if (panelAddress == 0)
            {
                panels.Add(
                    new ClientStarbasePanelObservation
                    {
                        Slot = definition.Slot,
                        Kind = definition.Kind,
                        Name = definition.DisplayName,
                        ViewFieldOffset =
                            definition.ViewFieldOffset,
                        CurrentInterfaceCommand =
                            definition.CurrentInterfaceCommand,
                        IsPersistent =
                            definition.IsPersistent,
                        Status = "Interface pointer is null",
                    });

                continue;
            }

            var panelErrorsBefore = errors.Count;

            if (!TryReadByte(
                    memory,
                    panelAddress,
                    ClientStarbaseInteractionCatalog
                        .PanelActiveFlagOffset,
                        $"{definition.DisplayName}.Active",
                    out var activeFlag,
                    out var activeError))
            {
                errors.Add(activeError);
            }

            var childAddress =
                definition.Kind ==
                    ClientStarbasePanelKind.TalkTree
                    ? ReadUInt32(
                        memory,
                        panelAddress,
                        ClientStarbaseInteractionCatalog
                            .TalkTreeVendorTradeControllerOffset,
                        "TalkTree.VendorTradeController",
                        errors)
                    : 0;

            panels.Add(
                new ClientStarbasePanelObservation
                {
                    Slot = definition.Slot,
                    Kind = definition.Kind,
                    Name = definition.DisplayName,
                    ViewFieldOffset =
                        definition.ViewFieldOffset,
                    CurrentInterfaceCommand =
                        definition.CurrentInterfaceCommand,
                    IsPersistent =
                        definition.IsPersistent,
                    Address = panelAddress,
                    ActiveFlag = activeFlag,
                    ChildAddress = childAddress,
                    ChildKind = childAddress != 0
                        ? ClientStarbaseInteractionKind.VendorTrade
                        : ClientStarbaseInteractionKind.None,
                    Status = errors.Count == panelErrorsBefore
                        ? activeFlag != 0 || childAddress != 0
                            ? "Attached and active"
                            : definition.IsPersistent
                                ? "Persistent interface attached but inactive"
                                : "Interface attached but inactive"
                        : "One or more interface fields could not be read",
                });
        }

        return panels;
    }

    private static InteractionReadResult ObserveInteractionContext(
        ProcessMemoryReader memory,
        IReadOnlyList<ClientStarbasePanelObservation> panels,
        uint playerInteractionMenuAddress,
        byte playerInteractionMenuActiveFlag,
        int currentRoomClass,
        int selectedNpcSlot,
        ClientStarbaseRoomObservation? currentRoom)
    {
        var talkTreePanel = panels.FirstOrDefault(
            panel =>
                panel.Kind ==
                ClientStarbasePanelKind.TalkTree);

        var playerTradePanel = panels.FirstOrDefault(
            panel =>
                panel.Kind ==
                ClientStarbasePanelKind.PlayerTrade);

        var activeKinds = panels
            .Where(
                panel =>
                    panel.IsActive &&
                    panel.ActiveInteractionKind !=
                        ClientStarbaseInteractionKind.None)
            .Select(panel => panel.ActiveInteractionKind)
            .ToList();

        if (playerInteractionMenuAddress != 0 &&
            playerInteractionMenuActiveFlag != 0)
        {
            activeKinds.Add(
                ClientStarbaseInteractionKind.PlayerInteractionMenu);
        }

        var distinctActiveKinds = activeKinds
            .Distinct()
            .ToArray();

        var kind = distinctActiveKinds.Length switch
        {
            0 => ClientStarbaseInteractionKind.None,
            1 => distinctActiveKinds[0],
            _ => ClientStarbaseInteractionKind.Ambiguous,
        };

        var talkTreePanelAddress =
            talkTreePanel?.Address ?? 0;

        var talkTreePanelActiveFlag =
            talkTreePanel?.ActiveFlag ?? 0;

        var vendorTradeControllerAddress =
            talkTreePanel?.ChildAddress ?? 0;

        var playerTradeInterfaceAddress =
            playerTradePanel?.Address ?? 0;

        var playerTradeInterfaceActiveFlag =
            playerTradePanel?.ActiveFlag ?? 0;

        var npcNameWidgetAddress =
            talkTreePanelAddress == 0
                ? 0
                : TryReadUInt32Value(
                    memory,
                    talkTreePanelAddress,
                    TalkTreeNpcNameWidget);

        _ = TryReadDisplayedNpcName(
            memory,
            npcNameWidgetAddress,
            out var npcNameAddress,
            out var npcName);

        var observation = BuildInteractionObservation(
            kind,
            distinctActiveKinds,
            currentRoomClass,
            selectedNpcSlot,
            currentRoom,
            npcName,
            npcNameAddress);

        return new InteractionReadResult
        {
            Observation = observation,
            ActiveKinds = distinctActiveKinds,
            TalkTreePanelAddress =
                talkTreePanelAddress,
            TalkTreePanelActiveFlag =
                talkTreePanelActiveFlag,
            VendorTradeControllerAddress =
                vendorTradeControllerAddress,
            PlayerTradeInterfaceAddress =
                playerTradeInterfaceAddress,
            PlayerTradeInterfaceActiveFlag =
                playerTradeInterfaceActiveFlag,
            NpcNameWidgetAddress =
                npcNameWidgetAddress,
        };
    }

    private static ClientStarbaseInteractionObservation BuildInteractionObservation(
        ClientStarbaseInteractionKind kind,
        IReadOnlyList<ClientStarbaseInteractionKind> activeKinds,
        int currentRoomClass,
        int selectedNpcSlot,
        ClientStarbaseRoomObservation? currentRoom,
        string npcName,
        uint npcNameAddress)
    {
        if (kind == ClientStarbaseInteractionKind.None)
        {
            return ClientStarbaseInteractionObservation.None with
            {
                RoomClass = currentRoomClass,
            };
        }

        if (kind == ClientStarbaseInteractionKind.Ambiguous)
        {
            return new ClientStarbaseInteractionObservation
            {
                Kind = kind,
                RoomClass = currentRoomClass,
                Status = $"Multiple starbase interactions are active: {string.Join(", ", activeKinds)}",
                NpcSlot = selectedNpcSlot,
                NpcName = npcName,
                NpcNameAddress = npcNameAddress,
            };
        }

        if (IsFacilityInteraction(kind))
        {
            var matches = currentRoom?.Facilities
                .Where(
                    facility =>
                        facility.InteractionKind == kind)
                .ToArray() ?? [];

            var facility = matches.FirstOrDefault();
            var facilityName = facility?.FacilityTypeName ??
                ClientStarbaseInteractionCatalog.GetInteractionDisplayName(
                    kind);

            var facilitySlot = matches.Length == 1
                ? matches[0].Slot
                : -1;

            var location = currentRoomClass >= 0
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"room {currentRoomClass}")
                : "an unresolved room";

            var slotSuffix = matches.Length switch
            {
                1 => string.Create(
                    CultureInfo.InvariantCulture,
                    $", slot {facilitySlot}"),
                > 1 => string.Create(
                    CultureInfo.InvariantCulture,
                    $"; {matches.Length} matching facilities are present in that room, so the exact slot is unresolved"),
                _ => "; no matching FacilityDefinition was found in the current room topology",
            };

            return new ClientStarbaseInteractionObservation
            {
                Kind = kind,
                RoomClass = currentRoomClass,
                FacilitySlot = facilitySlot,
                FacilityType = facility?.FacilityType,
                FacilityName = facilityName,
                Status = $"{facilityName} is active in {location}{slotSuffix}",
            };
        }

        var interactionName =
            ClientStarbaseInteractionCatalog.GetInteractionDisplayName(
                kind);

        var npcSuffix =
            kind is ClientStarbaseInteractionKind.TalkTree or
                    ClientStarbaseInteractionKind.VendorTrade
                ? string.IsNullOrWhiteSpace(npcName)
                    ? " with an unresolved NPC"
                    : $" with {npcName}"
                : "";

        var roomSuffix = currentRoomClass >= 0
            ? string.Create(
                CultureInfo.InvariantCulture,
                $" in room {currentRoomClass}")
            : " in an unresolved room";

        return new ClientStarbaseInteractionObservation
        {
            Kind = kind,
            RoomClass = currentRoomClass,
            Status = $"{interactionName}{npcSuffix}{roomSuffix}",
            NpcSlot = selectedNpcSlot,
            NpcName = npcName,
            NpcNameAddress = npcNameAddress,
        };
    }

    private static bool IsFacilityInteraction(
        ClientStarbaseInteractionKind kind)
    {
        return kind is
            ClientStarbaseInteractionKind.Refining or
            ClientStarbaseInteractionKind.Analyze or
            ClientStarbaseInteractionKind.Manufacturing or
            ClientStarbaseInteractionKind.JobsTerminal or
            ClientStarbaseInteractionKind.IntergalacticNet or
            ClientStarbaseInteractionKind.CustomizeAvatar or
            ClientStarbaseInteractionKind.CustomizeShip;
    }

    private static bool TryReadDisplayedNpcName(
        ProcessMemoryReader memory,
        uint npcNameWidgetAddress,
        out uint nameAddress,
        out string name)
    {
        nameAddress = 0;
        name = "";

        if (npcNameWidgetAddress == 0)
        {
            return false;
        }

        nameAddress = TryReadUInt32Value(
            memory,
            npcNameWidgetAddress,
            TextWidgetTextPointer);

        if (nameAddress == 0 ||
            !memory.TryReadNullTerminatedLatin1String(
                nameAddress,
                MaximumNpcNameLength,
                out name))
        {
            nameAddress = 0;
            name = "";
            return false;
        }

        name = name.Trim();

        if (name.Length == 0 ||
            !name.Any(char.IsLetter) ||
            name.Any(char.IsControl))
        {
            nameAddress = 0;
            name = "";
            return false;
        }

        return true;
    }

    private static string BuildStatus(
        IReadOnlyList<ClientStarbaseRoomObservation> rooms,
        int currentRoomClass,
        int pendingRoomClass,
        ClientStarbaseInteractionObservation interaction,
        IReadOnlyList<string> errors)
    {
        var roomText = currentRoomClass >= 0
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"current room {currentRoomClass}")
            : "current room unavailable";

        var transitionText = pendingRoomClass != currentRoomClass
            ? string.Create(
                CultureInfo.InvariantCulture,
                $", transitioning to room {pendingRoomClass}")
            : "";

        var interactionText = interaction.IsActive
            ? interaction.Status
            : "no active interaction";

        return errors.Count == 0
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"Available; {rooms.Count} room(s), {roomText}{transitionText}, {interactionText}")
            : string.Create(
                CultureInfo.InvariantCulture,
                $"Available with {errors.Count} read error(s); {rooms.Count} room(s), {roomText}{transitionText}, {interactionText}: {errors[0]}");
    }

    private static bool ContainsOnlyBooleanBytes(
        byte[] snapshot,
        uint offset,
        int count)
    {
        var start = checked((int)offset);

        for (var index = 0;
             index < count;
             index++)
        {
            if (snapshot[start + index] > 1)
            {
                return false;
            }
        }

        return true;
    }

    private static uint TryReadUInt32Value(
        ProcessMemoryReader memory,
        uint baseAddress,
        uint offset)
    {
        return TryReadUInt32(
            memory,
            baseAddress,
            offset,
            "best-effort field",
            out var value,
            out _)
                ? value
                : 0;
    }

    private static int TryReadInt32Value(
        ProcessMemoryReader memory,
        uint baseAddress,
        uint offset)
    {
        return TryReadUInt32(
            memory,
            baseAddress,
            offset,
            "best-effort field",
            out var value,
            out _)
                ? unchecked((int)value)
                : 0;
    }

    private static int? TryReadNullableInt32Value(
        ProcessMemoryReader memory,
        uint baseAddress,
        uint offset)
    {
        return TryReadUInt32(
            memory,
            baseAddress,
            offset,
            "best-effort Int32 field",
            out var value,
            out _)
                ? unchecked((int)value)
                : null;
    }

    private static uint TryReadPointerArrayEntry(
        ProcessMemoryReader memory,
        uint baseAddress,
        uint arrayOffset,
        int index)
    {
        try
        {
            return memory.TryReadUInt32(
                checked(
                    baseAddress +
                    arrayOffset +
                    checked((uint)index * sizeof(uint))),
                out var value)
                    ? value
                    : 0;
        }
        catch (OverflowException)
        {
            return 0;
        }
    }

    private static int ReadInt32(
        ProcessMemoryReader memory,
        uint baseAddress,
        uint offset,
        string fieldName,
        List<string> errors,
        int defaultValue)
    {
        if (TryReadUInt32(
                memory,
                baseAddress,
                offset,
                fieldName,
                out var rawValue,
                out var error))
        {
            return unchecked((int)rawValue);
        }

        errors.Add(error);
        return defaultValue;
    }

    private static uint ReadUInt32(
        ProcessMemoryReader memory,
        uint baseAddress,
        uint offset,
        string fieldName,
        List<string> errors)
    {
        if (TryReadUInt32(
                memory,
                baseAddress,
                offset,
                fieldName,
                out var value,
                out var error))
        {
            return value;
        }

        errors.Add(error);
        return 0;
    }

    private static bool TryReadUInt32(
        ProcessMemoryReader memory,
        uint baseAddress,
        uint offset,
        string fieldName,
        out uint value,
        out string error)
    {
        uint address;

        try
        {
            address = checked(
                baseAddress + offset);
        }
        catch (OverflowException)
        {
            value = 0;
            error =
                $"Address overflow while resolving {fieldName}";

            return false;
        }

        if (memory.TryReadUInt32(
                address,
                out value))
        {
            error = "";
            return true;
        }

        error = $"Could not read {fieldName} at 0x{address:X8}";

        return false;
    }

    private static bool TryReadByte(
        ProcessMemoryReader memory,
        uint baseAddress,
        uint offset,
        string fieldName,
        out byte value,
        out string error)
    {
        uint address;

        try
        {
            address = checked(
                baseAddress + offset);
        }
        catch (OverflowException)
        {
            value = 0;
            error =
                $"Address overflow while resolving {fieldName}";

            return false;
        }

        if (memory.TryReadBytes(
                address,
                sizeof(byte),
                out var bytes))
        {
            value = bytes[0];
            error = "";
            return true;
        }

        value = 0;
        error = $"Could not read {fieldName} at 0x{address:X8}";

        return false;
    }

    private static uint ReadUInt32(
        byte[] bytes,
        uint offset)
    {
        return BitConverter.ToUInt32(
            bytes,
            checked((int)offset));
    }

    private static int ReadInt32(
        byte[] bytes,
        uint offset)
    {
        return BitConverter.ToInt32(
            bytes,
            checked((int)offset));
    }

    private sealed class ProcessCache(
        uint moduleBaseAddress,
        uint clientContextAddress,
        uint starbaseViewAddress)
    {
        public uint ModuleBaseAddress { get; } =
            moduleBaseAddress;

        public uint ClientContextAddress { get; } =
            clientContextAddress;

        public uint StarbaseViewAddress { get; } =
            starbaseViewAddress;

        public IReadOnlyList<RoomControllerCandidate> Controllers
        { get; set; } = [];

        public DateTimeOffset? LastDiscoveryAttemptAt { get; set; }

        public int LastReferenceHitCount { get; set; }
    }

    private sealed record RoomControllerCandidate(
        uint Address,
        int RoomClass,
        uint RoomDefinitionAddress,
        string Status);

    private readonly record struct RoomControllerDiscovery(
        IReadOnlyList<RoomControllerCandidate> Controllers,
        int ReferenceHitCount,
        string Status);

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct RoomDefinitionReference(
        int RoomClass,
        int DefinitionKey,
        uint DefinitionAddress);

    private readonly record struct RoomDefinitionCatalog(
        IReadOnlyList<RoomDefinitionReference> Rooms,
        string Status,
        string Error);

    private readonly record struct RoomFacilityCatalog(
        IReadOnlyList<ClientStarbaseFacilityObservation> Facilities,
        string Status,
        string Error);

    private readonly record struct RoomNpcCatalog(
        IReadOnlyList<ClientStarbaseNpcObservation> Npcs,
        string Status,
        string Error);

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct PointerMapEntry(
        int Key,
        uint ValueAddress);

    private readonly record struct PointerMapCatalog(
        IReadOnlyList<PointerMapEntry> Entries,
        string Status,
        string Error);

    private sealed record InteractionReadResult
    {
        public ClientStarbaseInteractionObservation Observation
        { get; init; } = ClientStarbaseInteractionObservation.None;

        public IReadOnlyList<ClientStarbaseInteractionKind> ActiveKinds
        { get; init; } = [];

        public uint TalkTreePanelAddress { get; init; }

        public byte TalkTreePanelActiveFlag { get; init; }

        public uint VendorTradeControllerAddress { get; init; }

        public uint PlayerTradeInterfaceAddress { get; init; }

        public byte PlayerTradeInterfaceActiveFlag { get; init; }

        public uint NpcNameWidgetAddress { get; init; }
    }
}
