namespace Net7ClientManager.Shopping;

using System.Globalization;
using Net7ClientManager.Observations;
using Net7ClientManager.Observations.Models;

internal sealed class VendorShoppingCompanionCoordinator
{
    private readonly Lock stateLock = new();
    private readonly Dictionary<int, VendorProcessState> processStates = [];

    public VendorShoppingCompanionPresentation Build(
        ClientObservationSnapshot snapshot,
        Func<ShoppingPlanSnapshot?> resolveShoppingPlan)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(resolveShoppingPlan);

        if (!TryResolveVendorContext(
                snapshot,
                out var vendorContext,
                out var inventory))
        {
            lock (this.stateLock)
            {
                if (this.processStates.TryGetValue(
                        snapshot.ProcessId,
                        out var existingState))
                {
                    existingState.EndGeneration();
                }
            }

            return VendorShoppingCompanionPresentation.Hidden;
        }

        // VendorInventory is a persistent last-loaded cache. Confirm that
        // the observed catalogue belongs to the active NPC before presenting
        // it, so a vendor switch never flashes purchases from the last NPC.
        var catalogFingerprint = BuildCatalogFingerprint(inventory);
        VendorProcessState state;
        bool catalogConfirmed;

        lock (this.stateLock)
        {
            if (!this.processStates.TryGetValue(
                    snapshot.ProcessId,
                    out var resolvedState) ||
                resolvedState.ProcessStartedAt !=
                    snapshot.ProcessStartedAt)
            {
                resolvedState = new VendorProcessState(
                    snapshot.ProcessStartedAt);
                this.processStates[snapshot.ProcessId] = resolvedState;
            }

            state = resolvedState;
            catalogConfirmed = state.ObserveVendorCatalog(
                vendorContext.ContextKey,
                catalogFingerprint,
                snapshot.ObservedAt,
                vendorContext.InteractionKind ==
                    ClientStarbaseInteractionKind.VendorTrade);
        }

        var shoppingPlan = resolveShoppingPlan();

        if (shoppingPlan == null ||
            shoppingPlan.ShoppingList.RequestedOutputs.Count == 0)
        {
            return VendorShoppingCompanionPresentation.Hidden;
        }

        // Requested outputs mean additional quantities, so root ownership
        // deliberately does not complete them in ShoppingPlanBuilder. Keep a
        // live ownership baseline while this plan is active so purchases can
        // still count down immediately without changing that list contract.
        var shoppingPlanFingerprint =
            BuildShoppingPlanFingerprint(
                shoppingPlan.ShoppingList,
                shoppingPlan.Ownership,
                shoppingPlan.GalaxyKnowledgeIdentity);
        Dictionary<int, ShoppingPlanLine> usefulLines;
        Dictionary<int, long> remainingByItemTemplateId;

        lock (this.stateLock)
        {
            state.SeedShoppingProgress(
                shoppingPlanFingerprint,
                shoppingPlan.Lines);
            usefulLines = shoppingPlan.Lines
                .Where(line => line.ActiveAcquireQuantity > 0)
                .GroupBy(line => line.ItemTemplateId)
                .ToDictionary(
                    group => group.Key,
                    group => group.First());
            remainingByItemTemplateId = usefulLines.ToDictionary(
                pair => pair.Key,
                pair => state.ResolveRemainingQuantity(
                    shoppingPlanFingerprint,
                    pair.Value));
        }

        if (!catalogConfirmed ||
            vendorContext.InteractionKind !=
                ClientStarbaseInteractionKind.VendorTrade ||
            usefulLines.Count == 0)
        {
            return VendorShoppingCompanionPresentation.Hidden;
        }

        var purchases = inventory.Items
            .Where(item =>
                item.ItemTemplateId is > 0 &&
                usefulLines.ContainsKey(item.ItemTemplateId.Value) &&
                remainingByItemTemplateId[item.ItemTemplateId.Value] > 0)
            .OrderBy(item => item.Slot)
            .Select(item =>
            {
                var itemTemplateId = item.ItemTemplateId!.Value;
                var planLine = usefulLines[itemTemplateId];
                var remainingQuantity =
                    remainingByItemTemplateId[itemTemplateId];
                var packageQuantity = Math.Max(
                    1,
                    item.StackCount.GetValueOrDefault(1));
                var purchaseCount = remainingQuantity / packageQuantity;

                if (remainingQuantity % packageQuantity != 0)
                {
                    purchaseCount++;
                }

                return new VendorShoppingCompanionLine
                {
                    ItemTemplateId = itemTemplateId,
                    ItemName = string.IsNullOrWhiteSpace(planLine.ItemName)
                        ? item.ItemName?.Trim() ??
                          string.Create(
                              CultureInfo.InvariantCulture,
                              $"Item {itemTemplateId}")
                        : planLine.ItemName.Trim(),
                    RemainingQuantity = remainingQuantity,
                    PackageQuantity = packageQuantity,
                    PurchaseCount = purchaseCount,
                    VendorSlot = item.Slot,
                };
            })
            .GroupBy(line => line.ItemTemplateId)
            .Select(group => group.First())
            .ToArray();

        if (purchases.Length == 0)
        {
            return VendorShoppingCompanionPresentation.Hidden;
        }

        return VendorShoppingCompanionPresentation.Create(
            vendorContext.VendorName,
            shoppingPlan.ShoppingList.Name,
            purchases);
    }

    private static bool TryResolveVendorContext(
        ClientObservationSnapshot snapshot,
        out ActiveVendorContext vendorContext,
        out ClientVendorInventoryObservation inventory)
    {
        vendorContext = null!;
        inventory = ClientVendorInventoryObservation.Unavailable(
            "No active vendor catalogue");
        var interaction = snapshot.StarbaseContext.Interaction;

        if (snapshot.LifecycleState != ClientLifecycleState.InGame ||
            snapshot.LoadingOrTransitionFlag != 0 ||
            !snapshot.World.IsAvailable ||
            snapshot.World.Environment != ClientWorldEnvironment.Starbase ||
            !snapshot.StarbaseContext.IsAvailable ||
            !snapshot.LocalPlayer.IsAvailable ||
            interaction.Kind is not (
                ClientStarbaseInteractionKind.TalkTree or
                ClientStarbaseInteractionKind.VendorTrade) ||
            interaction.RoomClass < 0 ||
            interaction.NpcSlot < 0)
        {
            return false;
        }

        var room = snapshot.StarbaseContext.Rooms.FirstOrDefault(candidate =>
            candidate.RoomClass == interaction.RoomClass);
        var npc = room?.Npcs.FirstOrDefault(candidate =>
            candidate.Slot == interaction.NpcSlot);

        if (room == null ||
            npc == null ||
            !npc.IsVendor ||
            string.IsNullOrWhiteSpace(npc.Name))
        {
            return false;
        }

        inventory = snapshot.LocalPlayer.VendorInventory;

        if (!inventory.IsAvailable ||
            inventory.ReadErrorCount != 0 ||
            inventory.Slots.Count != 128 ||
            inventory.Items.Count == 0 ||
            inventory.Items.Any(item =>
                item.ItemTemplateId is not > 0))
        {
            return false;
        }

        var starbaseId = snapshot.World.CurrentStarbaseId != 0
            ? snapshot.World.CurrentStarbaseId
            : snapshot.StarbaseContext.StarbaseId;
        var vendorName = npc.Name.Trim();
        var contextKey = string.Create(
            CultureInfo.InvariantCulture,
            $"{starbaseId}|{room.RoomClass}|{room.DefinitionKey}|{npc.Slot}|{npc.DefinitionKey}|{npc.DefinitionSecondaryId}|{vendorName}");
        vendorContext = new ActiveVendorContext(
            contextKey,
            vendorName,
            interaction.Kind);
        return true;
    }

    private static string BuildCatalogFingerprint(
        ClientVendorInventoryObservation inventory)
    {
        return string.Join(
            "|",
            inventory.Items
                .OrderBy(item => item.Slot)
                .Select(item => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{item.Slot}:{item.ItemTemplateId.GetValueOrDefault()}:{Math.Max(1, item.StackCount.GetValueOrDefault(1))}")));
    }

    private static string BuildShoppingPlanFingerprint(
        ShoppingListDocument shoppingList,
        ShoppingOwnershipSnapshot ownership,
        string galaxyKnowledgeIdentity)
    {
        return string.Join(
            "|",
            new[]
            {
                shoppingList.ListId,
                galaxyKnowledgeIdentity,
                ownership.ActiveCharacterId?.ToString(
                    CultureInfo.InvariantCulture) ??
                    ownership.ActivePilotName.Trim(),
                string.Join(
                    ",",
                    shoppingList.RequestedOutputs
                        .OrderBy(output => output.ItemTemplateId)
                        .Select(output => string.Create(
                            CultureInfo.InvariantCulture,
                            $"{output.ItemTemplateId}:{output.Quantity}"))),
                string.Join(
                    ",",
                    shoppingList.RecipeSelections
                        .OrderBy(selection =>
                            selection.OutputItemTemplateId)
                        .ThenBy(selection =>
                            selection.RecipeIdentity,
                            StringComparer.Ordinal)
                        .Select(selection => string.Create(
                            CultureInfo.InvariantCulture,
                            $"{selection.OutputItemTemplateId}:{selection.RecipeIdentity}"))),
            });
    }

    private sealed class VendorProcessState
    {
        private static readonly TimeSpan StableConfirmationDelay =
            TimeSpan.FromMilliseconds(250);

        public VendorProcessState(DateTimeOffset processStartedAt)
        {
            this.ProcessStartedAt = processStartedAt;
        }

        public DateTimeOffset ProcessStartedAt { get; }

        private readonly HashSet<string> confirmedCatalogOwners =
            new(StringComparer.Ordinal);

        private string activeVendorContextKey = "";
        private string currentCatalogFingerprint = "";
        private string lastObservedCatalogFingerprint = "";
        private DateTimeOffset stableSince;
        private int observationCount;
        private bool catalogConfirmed;
        private bool allowStableConfirmation;
        private string shoppingPlanFingerprint = "";
        private readonly Dictionary<int, ShoppingProgressBaseline>
            shoppingProgressByItemTemplateId = [];

        public bool ObserveVendorCatalog(
            string vendorContextKey,
            string catalogFingerprint,
            DateTimeOffset observedAt,
            bool vendorTradeActive)
        {
            if (!string.Equals(
                    this.activeVendorContextKey,
                    vendorContextKey,
                    StringComparison.Ordinal))
            {
                var isKnownOwner = this.confirmedCatalogOwners.Contains(
                    CreateOwnerKey(vendorContextKey, catalogFingerprint));
                var isFirstCatalogInProcess = string.IsNullOrEmpty(
                    this.lastObservedCatalogFingerprint);
                var changedSincePreviousVendor =
                    !isFirstCatalogInProcess &&
                    !string.Equals(
                        this.lastObservedCatalogFingerprint,
                        catalogFingerprint,
                        StringComparison.Ordinal);

                this.activeVendorContextKey = vendorContextKey;
                this.currentCatalogFingerprint = catalogFingerprint;
                this.lastObservedCatalogFingerprint = catalogFingerprint;
                this.stableSince = observedAt;
                this.observationCount = 1;
                this.catalogConfirmed =
                    isKnownOwner || changedSincePreviousVendor;
                this.allowStableConfirmation = isFirstCatalogInProcess;

                if (changedSincePreviousVendor)
                {
                    this.confirmedCatalogOwners.Add(
                        CreateOwnerKey(
                            vendorContextKey,
                            catalogFingerprint));
                }

                return this.catalogConfirmed && vendorTradeActive;
            }

            if (!string.Equals(
                    this.currentCatalogFingerprint,
                    catalogFingerprint,
                    StringComparison.Ordinal))
            {
                this.currentCatalogFingerprint = catalogFingerprint;
                this.lastObservedCatalogFingerprint = catalogFingerprint;
                this.stableSince = observedAt;
                this.observationCount = 1;
                this.catalogConfirmed = true;
                this.allowStableConfirmation = false;
                this.confirmedCatalogOwners.Add(
                    CreateOwnerKey(vendorContextKey, catalogFingerprint));
                return false;
            }

            this.observationCount++;

            if (!this.catalogConfirmed &&
                this.allowStableConfirmation &&
                vendorTradeActive &&
                this.observationCount >= 2 &&
                observedAt - this.stableSince >= StableConfirmationDelay)
            {
                this.catalogConfirmed = true;
                this.allowStableConfirmation = false;
                this.confirmedCatalogOwners.Add(
                    CreateOwnerKey(vendorContextKey, catalogFingerprint));
            }

            return this.catalogConfirmed && vendorTradeActive;
        }

        public void SeedShoppingProgress(
            string planFingerprint,
            IReadOnlyList<ShoppingPlanLine> lines)
        {
            if (!string.Equals(
                    this.shoppingPlanFingerprint,
                    planFingerprint,
                    StringComparison.Ordinal))
            {
                this.shoppingPlanFingerprint = planFingerprint;
                this.shoppingProgressByItemTemplateId.Clear();
            }

            foreach (var line in lines.Where(line =>
                         line.ActiveAcquireQuantity > 0))
            {
                this.shoppingProgressByItemTemplateId.TryAdd(
                    line.ItemTemplateId,
                    new ShoppingProgressBaseline(
                        line.ActiveAcquireQuantity,
                        line.OwnedAvailableNow));
            }
        }

        public long ResolveRemainingQuantity(
            string planFingerprint,
            ShoppingPlanLine line)
        {
            this.SeedShoppingProgress(planFingerprint, [line]);
            var baseline =
                this.shoppingProgressByItemTemplateId[line.ItemTemplateId];
            var acquiredSinceBaseline = line.OwnedAvailableNow >
                                        baseline.OwnedAvailableNow
                ? line.OwnedAvailableNow - baseline.OwnedAvailableNow
                : 0;
            var baselineRemaining = acquiredSinceBaseline >=
                                    baseline.AcquireQuantity
                ? 0
                : baseline.AcquireQuantity - acquiredSinceBaseline;
            return Math.Min(
                line.ActiveAcquireQuantity,
                baselineRemaining);
        }

        public void EndGeneration()
        {
            this.activeVendorContextKey = "";
            this.currentCatalogFingerprint = "";
            this.stableSince = default;
            this.observationCount = 0;
            this.catalogConfirmed = false;
            this.allowStableConfirmation = false;
        }

        private sealed record ShoppingProgressBaseline(
            long AcquireQuantity,
            long OwnedAvailableNow);

        private static string CreateOwnerKey(
            string vendorContextKey,
            string catalogFingerprint) =>
            string.Concat(
                vendorContextKey,
                "|",
                catalogFingerprint);
    }

    private sealed record ActiveVendorContext(
        string ContextKey,
        string VendorName,
        ClientStarbaseInteractionKind InteractionKind);
}

internal sealed record VendorShoppingCompanionPresentation
{
    public static VendorShoppingCompanionPresentation Hidden { get; } =
        new();

    public bool IsVisible { get; init; }

    public string VendorName { get; init; } = "";

    public string ShoppingListName { get; init; } = "";

    public IReadOnlyList<VendorShoppingCompanionLine> Lines
    { get; init; } = [];

    public string Fingerprint { get; init; } = "hidden";

    public static VendorShoppingCompanionPresentation Create(
        string vendorName,
        string shoppingListName,
        IReadOnlyList<VendorShoppingCompanionLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var normalizedVendorName = string.IsNullOrWhiteSpace(vendorName)
            ? "Vendor"
            : vendorName.Trim();
        var normalizedShoppingListName =
            string.IsNullOrWhiteSpace(shoppingListName)
                ? "Active shopping list"
                : shoppingListName.Trim();
        var stableLines = lines.ToArray();
        var fingerprint = string.Join(
            "|",
            new[]
            {
                normalizedVendorName,
                normalizedShoppingListName,
            }.Concat(stableLines.Select(line => string.Create(
                CultureInfo.InvariantCulture,
                $"{line.ItemTemplateId}:{line.RemainingQuantity}:{line.PackageQuantity}:{line.PurchaseCount}:{line.VendorSlot}:{line.ItemName}"))));

        return new VendorShoppingCompanionPresentation
        {
            IsVisible = stableLines.Length > 0,
            VendorName = normalizedVendorName,
            ShoppingListName = normalizedShoppingListName,
            Lines = stableLines,
            Fingerprint = fingerprint,
        };
    }
}

internal sealed record VendorShoppingCompanionLine
{
    public int ItemTemplateId { get; init; }

    public string ItemName { get; init; } = "";

    public long RemainingQuantity { get; init; }

    public int PackageQuantity { get; init; }

    public long PurchaseCount { get; init; }

    public int VendorSlot { get; init; }
}
