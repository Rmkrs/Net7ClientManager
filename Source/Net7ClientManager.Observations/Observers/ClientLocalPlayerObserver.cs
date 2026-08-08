namespace Net7ClientManager.Observations.Observers;

using Net7ClientManager.Observations.Models;

internal sealed class ClientLocalPlayerObserver
{
    private const uint ClientObjectAuxData = 0x88;

    private static readonly string[] propertyNames =
    [
        .. ClientShipAuxDataObserver.PropertyNames,
        .. ClientInventoryObserver.PropertyNames,
        .. ClientBuffObserver.PropertyNames,
    ];

    private readonly ClientObjectResolver objectResolver =
        new();

    private readonly ClientAuxDataLookupReader auxDataReader =
        new();

    private readonly ClientShipAuxDataObserver shipAuxDataObserver =
        new();

    private readonly ClientSpatialObserver spatialObserver = new();

    private readonly ClientInventoryObserver inventoryObserver =
        new();

    private readonly ClientCharacterProgressionObserver characterProgressionObserver =
        new();

    private readonly ClientCharacterDetailsObserver characterDetailsObserver =
        new();

    private readonly ClientVendorInventoryObserver vendorInventoryObserver =
        new();

    private readonly ClientRewardOverflowInventoryObserver rewardOverflowInventoryObserver =
        new();

    private readonly ClientBuffObserver buffObserver =
        new();

    private readonly System.Threading.Lock lookupCacheLock = new();

    private readonly Dictionary<int, CachedLocalPlayerLookup>
        cachedLookups = [];

    public void Refresh(
        ProcessMemoryReader memory,
        ObservedClientState state)
    {
        if (!state.HasDirectClientState ||
            state.ClientContextAddress == 0)
        {
            this.Forget(state.ProcessId);
            state.LocalPlayer =
                ClientLocalPlayerObservation.Unavailable(
                    "Direct SClient state is unavailable");

            return;
        }

        if (!this.objectResolver
                .TryResolveLocalPlayerClientObject(
                    memory,
                    state.ClientContextAddress,
                    out var clientObjectAddress,
                    out var error,
                    out _))
        {
            this.Forget(state.ProcessId);
            state.LocalPlayer =
                ClientLocalPlayerObservation.Unavailable(
                    error);

            return;
        }

        uint auxDataPointerAddress;

        try
        {
            auxDataPointerAddress = checked(
                clientObjectAddress +
                ClientObjectAuxData);
        }
        catch (OverflowException)
        {
            this.Forget(state.ProcessId);
            state.LocalPlayer =
                ClientLocalPlayerObservation.Unavailable(
                    "Local-player ObjectAuxData pointer address overflow");

            return;
        }

        if (!memory.TryReadUInt32(
                auxDataPointerAddress,
                out var auxDataAddress) ||
            auxDataAddress == 0)
        {
            this.Forget(state.ProcessId);
            state.LocalPlayer =
                ClientLocalPlayerObservation.Unavailable(
                        $"Could not resolve local-player ObjectAuxData at 0x{auxDataPointerAddress:X8}");

            return;
        }

        if (!this.auxDataReader.TryOpen(
                memory,
                state.ModuleBaseAddress,
                auxDataAddress,
                propertyNames,
                out var lookup,
                out var lookupError))
        {
            this.Forget(state.ProcessId);
            state.LocalPlayer =
                ClientLocalPlayerObservation.Unavailable(
                    lookupError);

            return;
        }

        this.CacheLookup(
            state,
            auxDataAddress,
            lookup);

        var shipAuxData = this.shipAuxDataObserver.Observe(
            memory,
            state.CurrentClientTime,
            lookup);

        var spatial = this.spatialObserver.Observe(
            memory,
            state.ModuleBaseAddress,
            state.CurrentClientTime,
            clientObjectAddress);

        var inventory = this.inventoryObserver.Observe(
            memory,
            state.ProcessId,
            state.ModuleBaseAddress,
            state.ClientContextAddress,
            state.CurrentClientTime,
            lookup);

        var characterProgression =
            this.characterProgressionObserver.Observe(
                memory,
                state.ModuleBaseAddress,
                state.ProcessId,
                state.LocalPlayerAuxDataAddress);

        var characterDetails =
            this.characterDetailsObserver.Observe(
                memory,
                state.ModuleBaseAddress,
                state.LocalPlayerAuxDataAddress);

        var vendorInventory =
            this.vendorInventoryObserver.Observe(
                memory,
                state.ModuleBaseAddress,
                state.ProcessId,
                state.LocalPlayerAuxDataAddress,
                characterDetails.Credits);

        var rewardOverflowInventory =
            this.rewardOverflowInventoryObserver.Observe(
                memory,
                state.ModuleBaseAddress,
                state.ProcessId,
                state.LocalPlayerAuxDataAddress);

        var buffs = this.buffObserver.Observe(
            memory,
            state.ModuleBaseAddress,
            state.CurrentClientTime,
            lookup);

        var status = shipAuxData.Operational.IsAvailable
            ? "Available"
            : shipAuxData.Operational.Status;

        state.LocalPlayer =
            new ClientLocalPlayerObservation
            {
                IsAvailable = true,
                Status = status,
                ObjectId =
                    state.LocalPlayerObjectId,
                ClientObjectAddress =
                    clientObjectAddress,
                AuxDataAddress =
                    auxDataAddress,
                Shield = shipAuxData.Shield,
                Hull = shipAuxData.Hull,
                Energy = shipAuxData.Energy,
                Operational = shipAuxData.Operational,
                Spatial = spatial,
                Inventory = inventory,
                CharacterProgression =
                    characterProgression,
                CharacterDetails =
                    characterDetails,
                VendorInventory =
                    vendorInventory,
                RewardOverflowInventory =
                    rewardOverflowInventory,
                Buffs = buffs,
                Missions =
                    state.LocalPlayer.Missions,
                Reputation =
                    state.LocalPlayer.Reputation,
                SecureInventory =
                    state.LocalPlayer.SecureInventory,
            };
    }

    public bool RefreshVendorTransactionState(
        ProcessMemoryReader memory,
        ObservedClientState state)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(state);

        if (!state.HasDirectClientState ||
            state.ClientContextAddress == 0 ||
            !state.LocalPlayer.IsAvailable ||
            !this.TryGetCachedLookup(state, out var cachedLookup))
        {
            return false;
        }

        if (!this.characterDetailsObserver.TryRefreshCreditsFast(
                memory,
                state.LocalPlayer.CharacterDetails,
                out var characterDetails) ||
            !this.inventoryObserver.TryRefreshCargoTransactionState(
                memory,
                cachedLookup.Lookup,
                state.LocalPlayer.Inventory,
                out var inventory))
        {
            return false;
        }

        var creditsChanged =
            characterDetails.MoneyValidState !=
                state.LocalPlayer.CharacterDetails.MoneyValidState ||
            characterDetails.Credits !=
                state.LocalPlayer.CharacterDetails.Credits;
        var cargoChanged =
            !ReferenceEquals(inventory, state.LocalPlayer.Inventory);

        if (!creditsChanged && !cargoChanged)
        {
            return false;
        }

        state.LocalPlayer = state.LocalPlayer with
        {
            CharacterDetails = characterDetails,
            Inventory = inventory,
        };
        return true;
    }

    public void RefreshVendorShoppingState(
        ProcessMemoryReader memory,
        ObservedClientState state)
    {
        if (!state.HasDirectClientState ||
            state.ClientContextAddress == 0 ||
            !state.LocalPlayer.IsAvailable ||
            !this.TryGetCachedLookup(
                state,
                out var cachedLookup))
        {
            return;
        }

        var inventory = this.inventoryObserver.ObserveCargo(
            memory,
            state.ProcessId,
            state.ModuleBaseAddress,
            state.ClientContextAddress,
            state.CurrentClientTime,
            cachedLookup.Lookup,
            state.LocalPlayer.Inventory);

        var vendorInventory =
            this.vendorInventoryObserver.Observe(
                memory,
                state.ModuleBaseAddress,
                state.ProcessId,
                state.LocalPlayerAuxDataAddress,
                state.LocalPlayer.CharacterDetails.Credits);

        state.LocalPlayer = state.LocalPlayer with
        {
            Inventory = inventory,
            VendorInventory = vendorInventory,
        };
    }

    public bool TryGetCachedAuxDataLookup(
        ObservedClientState state,
        out ClientAuxDataLookupSnapshot lookup)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (this.TryGetCachedLookup(state, out var cachedLookup))
        {
            lookup = cachedLookup.Lookup;
            return true;
        }

        lookup = default;
        return false;
    }

    public void Forget(int processId)
    {
        lock (this.lookupCacheLock)
        {
            this.cachedLookups.Remove(processId);
        }
    }

    private void CacheLookup(
        ObservedClientState state,
        uint auxDataAddress,
        ClientAuxDataLookupSnapshot lookup)
    {
        lock (this.lookupCacheLock)
        {
            this.cachedLookups[state.ProcessId] =
                new CachedLocalPlayerLookup(
                    state.ProcessStartedAt,
                    state.ModuleBaseAddress,
                    state.ClientContextAddress,
                    auxDataAddress,
                    lookup);
        }
    }

    private bool TryGetCachedLookup(
        ObservedClientState state,
        out CachedLocalPlayerLookup cachedLookup)
    {
        lock (this.lookupCacheLock)
        {
            if (this.cachedLookups.TryGetValue(
                    state.ProcessId,
                    out var candidate) &&
                candidate.ProcessStartedAt ==
                    state.ProcessStartedAt &&
                candidate.ModuleBaseAddress ==
                    state.ModuleBaseAddress &&
                candidate.ClientContextAddress ==
                    state.ClientContextAddress &&
                candidate.AuxDataAddress ==
                    state.LocalPlayer.AuxDataAddress)
            {
                cachedLookup = candidate;
                return true;
            }
        }

        cachedLookup = default!;
        return false;
    }

    private sealed record CachedLocalPlayerLookup(
        DateTimeOffset ProcessStartedAt,
        uint ModuleBaseAddress,
        uint ClientContextAddress,
        uint AuxDataAddress,
        ClientAuxDataLookupSnapshot Lookup);
}
