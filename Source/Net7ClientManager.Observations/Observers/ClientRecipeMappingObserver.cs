namespace Net7ClientManager.Observations.Observers;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Net7ClientManager.Observations.Models;

// Read-only observer for the game's own Manufacturing/Analyze state.
// It never invokes client functions, writes process memory or manufactures
// network requests; any request still originates from normal game UI input.
internal sealed class ClientRecipeMappingObserver
{
    private const uint ClientContextManufacturingObjectId = 0x1328;
    private const uint ClientObjectAuxData = 0x88;
    private const uint StarbaseViewCurrentInterfaceCommand = 0x5c;

    private const uint ModeValue = 0x01ac;
    private const uint ValidityValue = 0x0234;
    private const uint ManufactureTargetProperty = 0x0348;
    private const uint ManufactureComponentsProperty = 0x03e4;
    private const uint PrimaryCategoriesProperty = 0x0640;
    private const uint KnownFormulaProperty = 0x06dc;
    private const uint CurrentItemCategoryValue = 0x0898;
    private const uint TechLevelFilterBitfieldValue = 0x0c50;

    private const uint PropertyVectorBegin = 0x88;
    private const uint PropertyVectorEnd = 0x8c;

    private const uint InventoryItemTemplateIdValue = 0x120;
    private const uint CategoryNameValue = 0x120;
    private const uint PrimaryCategoriesBegin = 0x1ac;
    private const uint PrimaryCategoriesEnd = 0x1b0;
    private const uint SecondarySubcategoriesBegin = 0x234;
    private const uint SecondarySubcategoriesEnd = 0x238;
    private const uint LeafCategoryIdValue = 0x1a8;
    private const uint LeafVisibleValue = 0x230;
    private const uint FormulaItemNameValue = 0x120;
    private const uint FormulaItemIdValue = 0x1a8;
    private const uint FormulaTechLevelValue = 0x230;

    private const uint PanelActiveFlagValue = 0x007c;
    private const uint PanelPrimaryIndexValue = 0x0b80;
    private const uint PanelSecondaryIndexValue = 0x0b84;
    private const uint PanelLeafIndexValue = 0x0b88;
    private const uint PanelBrowserStageValue = 0x0b98;
    private const uint PanelSelectedOutputItemIdValue = 0x0bb0;
    private const uint PanelPreviousAttemptsValue = 0x0bad;
    private const uint PanelPendingTechFilterRequestCountValue = 0x0bc0;
    // Proven from ui_analyze.cpp. ExecuteCurrentAnalyzeOrDismantle sets
    // +0x110=1 and +0x114=2.0f when the attempt starts. When the result is
    // processed, +0x110 returns to 0 and the same native 2.0-second timer is
    // restarted as the post-result lockout.
    private const uint AnalyzePanelAttemptPhaseValue = 0x0110;
    private const uint AnalyzePanelDelayRemainingValue = 0x0114;

    // Proven from ui_analyze.cpp. These live in ManufacturingLab AuxData.
    private const string NegotiatedCostPropertyName = "NegotiatedCost";
    private const string SuccessProbabilityPropertyName = "SuccessProbability";
    private const string CriticalSuccessProbabilityPropertyName =
        "CriticalSuccessProbability";
    private const uint AuxDataPropertyValid = 0x70;
    private const uint AuxDataPropertyPrimaryValue = 0x84;
    private const uint UInt64AuxDataPropertyLowWord = 0x88;
    private const uint UInt64AuxDataPropertyHighWord = 0x8c;

    private const int TargetSlotCount = 2;
    private const int ComponentSlotCount = 6;
    private const int MaximumPrimaryCategories = 2;
    private const int MaximumSecondaryCategories = 5;
    private const int MaximumLeafCategories = 5;
    private const int MaximumKnownFormulaEntries = 500;
    private const int MaximumNameLength = 160;
    private const int MaximumFastCraftingCargoSlots = 40;
    private static readonly TimeSpan LookupRetryDelay = TimeSpan.FromSeconds(2);

    private static readonly string[] manufacturingMetricPropertyNames =
    [
        NegotiatedCostPropertyName,
        SuccessProbabilityPropertyName,
        CriticalSuccessProbabilityPropertyName,
    ];

    private readonly ClientObjectResolver objectResolver = new();
    private readonly ClientAuxDataLookupReader auxDataReader = new();
    private readonly Dictionary<int, ManufacturingCategoryCache>
        categoryCaches = [];
    private readonly Dictionary<int, ManufacturingMetricLookupCache>
        manufacturingMetricLookupCaches = [];
    private readonly Dictionary<int, FastCargoLookupCache>
        fastCargoLookupCaches = [];
    private readonly Dictionary<int, FastManufactureOutputState>
        fastManufactureOutputStates = [];

    public void ForgetCatalog(int processId)
    {
        this.categoryCaches.Remove(processId);
    }

    public void Forget(int processId)
    {
        this.ForgetCatalog(processId);
        this.manufacturingMetricLookupCaches.Remove(processId);
        this.fastCargoLookupCaches.Remove(processId);
        this.fastManufactureOutputStates.Remove(processId);
    }

    public void RefreshActivity(
        ProcessMemoryReader memory,
        ObservedClientState state,
        uint moduleBaseAddress,
        ClientAuxDataLookupSnapshot? localPlayerLookup,
        DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(state);

        var analyzePanel = state.StarbaseContext.Diagnostics.Panels
            .FirstOrDefault(item =>
                item.Kind == ClientStarbasePanelKind.Analyze);
        var manufacturingPanel = state.StarbaseContext.Diagnostics.Panels
            .FirstOrDefault(item =>
                item.Kind == ClientStarbasePanelKind.Manufacturing);
        int? currentInterfaceCommand = null;
        if (state.StarbaseContext.StarbaseViewAddress != 0 &&
            TryReadInt32(
                memory,
                state.StarbaseContext.StarbaseViewAddress,
                StarbaseViewCurrentInterfaceCommand,
                out var directCurrentInterfaceCommand))
        {
            currentInterfaceCommand = directCurrentInterfaceCommand;
        }

        var analyzePanelActive = IsPanelActive(
            memory,
            analyzePanel,
            currentInterfaceCommand);
        var manufacturingPanelActive = IsPanelActive(
            memory,
            manufacturingPanel,
            currentInterfaceCommand);

        if (!analyzePanelActive && !manufacturingPanelActive)
        {
            this.ResetFastManufactureBaseline(state.ProcessId);

            PublishActivityState(
                state,
                new ClientManufacturingActivityObservation
                {
                    IsAvailable = true,
                    Status = "Crafting panels inactive",
                    ObservedAt = observedAt,
                });
            return;
        }

        if (!this.TryResolveManufacturingLab(
                memory,
                state.ClientContextAddress,
                out var resolved,
                out var error))
        {
            PublishActivityState(
                state,
                ClientManufacturingActivityObservation.Unavailable(
                    error,
                    observedAt));
            return;
        }

        if (!TryReadInt32(
                memory,
                resolved.AuxDataAddress,
                ModeValue,
                out var mode) ||
            !TryReadInt32(
                memory,
                resolved.AuxDataAddress,
                ValidityValue,
                out var validity))
        {
            PublishActivityState(
                state,
                ClientManufacturingActivityObservation.Unavailable(
                    "Could not read ManufacturingLab activity state",
                    observedAt,
                    resolved.ManufacturingObjectId,
                    resolved.ClientObjectAddress,
                    resolved.AuxDataAddress));
            return;
        }

        if (!TryReadVectorEntries(
                memory,
                resolved.AuxDataAddress,
                ManufactureTargetProperty,
                TargetSlotCount,
                allowEmpty: false,
                out var targets,
                out error))
        {
            PublishActivityState(
                state,
                ClientManufacturingActivityObservation.Unavailable(
                    error,
                    observedAt,
                    resolved.ManufacturingObjectId,
                    resolved.ClientObjectAddress,
                    resolved.AuxDataAddress));
            return;
        }

        var labTargetItemTemplateId = targets.Count == 0
            ? 0
            : ReadPopulatedItemTemplateId(memory, targets[0]);

        // Raw ManufacturingLab mode is not a globally unique operation ID.
        // The live Build panel uses mode 3, which Dismantle also uses in the
        // Analyze panel. The active native panel is therefore authoritative
        // for the operation, and ManufacturingPanel+0xBB0 is the selected
        // recipe output.
        var manufacturingSelectedOutputItemTemplateId = 0;
        if (manufacturingPanelActive &&
            manufacturingPanel is { Address: > 0 } &&
            TryReadInt32(
                memory,
                manufacturingPanel.Address,
                PanelSelectedOutputItemIdValue,
                out var selectedOutputItemTemplateId) &&
            selectedOutputItemTemplateId > 0)
        {
            manufacturingSelectedOutputItemTemplateId =
                selectedOutputItemTemplateId;
        }

        var targetItemTemplateId = manufacturingPanelActive &&
                                   manufacturingSelectedOutputItemTemplateId > 0
            ? manufacturingSelectedOutputItemTemplateId
            : labTargetItemTemplateId;

        var analyzeUiPacingAvailable = false;
        var analyzeUiAttemptInProgress = false;
        var analyzeUiDelayRemainingDeciseconds = 0;
        if (analyzePanelActive &&
            analyzePanel is { Address: > 0 } &&
            TryReadAnalyzeUiPacing(
                memory,
                analyzePanel.Address,
                out analyzeUiAttemptInProgress,
                out analyzeUiDelayRemainingDeciseconds))
        {
            analyzeUiPacingAvailable = true;
        }

        IReadOnlyList<int> resultComponentItemTemplateIds = [];
        if (analyzePanelActive &&
            mode is 2 or 3 &&
            TryReadVectorEntries(
                memory,
                resolved.AuxDataAddress,
                ManufactureComponentsProperty,
                ComponentSlotCount,
                allowEmpty: true,
                out var componentSlots,
                out _))
        {
            resultComponentItemTemplateIds = componentSlots
                .Select(address => ReadPopulatedItemTemplateId(memory, address))
                .Where(itemTemplateId => itemTemplateId > 0)
                .ToArray();
        }

        this.TryReadManufacturingMetrics(
            memory,
            state.ProcessId,
            moduleBaseAddress,
            resolved.AuxDataAddress,
            observedAt,
            out var negotiatedCostCredits,
            out var successProbabilityPercent,
            out var criticalSuccessProbabilityPercent);

        // Reuse the complete local-player AuxData lookup already owned by
        // the normal inventory observer. Binding direct cargo addresses from
        // that cache is O(40) dictionary lookup work once, and the fast lane
        // itself remains only direct process-memory reads.
        if (manufacturingPanelActive &&
            state.LocalPlayerAuxDataAddress != 0 &&
            localPlayerLookup.HasValue)
        {
            this.TryGetFastCargoLookup(
                state.ProcessId,
                state.LocalPlayerAuxDataAddress,
                ResolveFastCargoSlotCount(state),
                localPlayerLookup.Value,
                out _);
        }

        var fastManufactureTargetItemTemplateId =
            manufacturingPanelActive
                ? this.ResolveFastManufactureTargetItemTemplateId(
                    state.ProcessId,
                    targetItemTemplateId,
                    validity)
                : 0;
        var outputObservation = this.ObserveFastManufactureOutput(
            memory,
            state,
            fastManufactureTargetItemTemplateId,
            observedAt,
            localPlayerLookup);

        var nextActivity = new ClientManufacturingActivityObservation
        {
            IsAvailable = true,
            Status = string.Create(
                CultureInfo.InvariantCulture,
                $"Crafting panel active; ManufacturingLab mode {mode}, validity {validity}, target {targetItemTemplateId}"),
            ObservedAt = observedAt,
            ManufacturingObjectId = resolved.ManufacturingObjectId,
            ClientObjectAddress = resolved.ClientObjectAddress,
            AuxDataAddress = resolved.AuxDataAddress,
            IsAnalyzePanelActive = analyzePanelActive,
            IsManufacturingPanelActive = manufacturingPanelActive,
            Mode = mode,
            Validity = validity,
            TargetItemTemplateId = targetItemTemplateId,
            NegotiatedCostCredits = negotiatedCostCredits,
            SuccessProbabilityPercent = successProbabilityPercent,
            CriticalSuccessProbabilityPercent = criticalSuccessProbabilityPercent,
            IsAnalyzeUiPacingAvailable = analyzeUiPacingAvailable,
            IsAnalyzeUiAttemptInProgress = analyzeUiAttemptInProgress,
            AnalyzeUiDelayRemainingDeciseconds =
                analyzeUiDelayRemainingDeciseconds,
            IsManufactureOutputObservationAvailable = outputObservation.IsAvailable,
            ManufactureOutputSequence = outputObservation.Sequence,
            ManufactureOutputObservedAt = outputObservation.ObservedAt,
            ManufactureOutputItemTemplateId = outputObservation.ItemTemplateId,
            ManufactureOutputQuantity = outputObservation.Quantity,
            ManufactureOutputQualityPercent = outputObservation.QualityPercent,
            ResultComponentItemTemplateIds = resultComponentItemTemplateIds,
        };

        PublishActivityState(state, nextActivity);
    }

    public ClientManufacturingActivityObservation ObserveAnalyzeUiPacing(
        ProcessMemoryReader memory,
        ObservedClientState state,
        DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(state);

        var analyzePanel = state.StarbaseContext.Diagnostics.Panels
            .FirstOrDefault(item =>
                item.Kind == ClientStarbasePanelKind.Analyze);
        if (analyzePanel is not { Address: > 0 })
        {
            return ClientManufacturingActivityObservation.Unavailable(
                "Analyze panel is unavailable",
                observedAt);
        }

        // This presentation lane is intentionally self-contained. The panel
        // address comes from the stable starbase topology, but activity is
        // decided only from the native panel's own +0x7C byte. Do not consult
        // the slower cached interface-command observation here.
        if (!TryReadByte(
                memory,
                analyzePanel.Address,
                PanelActiveFlagValue,
                out var activeFlag) ||
            activeFlag == 0)
        {
            return new ClientManufacturingActivityObservation
            {
                IsAvailable = true,
                Status = "Analyze panel inactive",
                ObservedAt = observedAt,
            };
        }

        var auxDataAddress =
            state.RealtimeManufacturingActivity.AuxDataAddress != 0
                ? state.RealtimeManufacturingActivity.AuxDataAddress
                : state.ManufacturingActivity.AuxDataAddress;
        uint manufacturingObjectId = 0;
        uint clientObjectAddress = 0;

        if (auxDataAddress == 0)
        {
            if (!this.TryResolveManufacturingLab(
                    memory,
                    state.ClientContextAddress,
                    out var resolved,
                    out var error))
            {
                return ClientManufacturingActivityObservation.Unavailable(
                    error,
                    observedAt);
            }

            manufacturingObjectId = resolved.ManufacturingObjectId;
            clientObjectAddress = resolved.ClientObjectAddress;
            auxDataAddress = resolved.AuxDataAddress;
        }

        if (!TryReadInt32(
                memory,
                auxDataAddress,
                ModeValue,
                out var mode) ||
            !TryReadAnalyzeUiPacing(
                memory,
                analyzePanel.Address,
                out var attemptInProgress,
                out var delayRemainingDeciseconds))
        {
            return ClientManufacturingActivityObservation.Unavailable(
                "Could not read native Analyze pacing state",
                observedAt,
                manufacturingObjectId,
                clientObjectAddress,
                auxDataAddress);
        }

        return new ClientManufacturingActivityObservation
        {
            IsAvailable = true,
            Status = "Native Analyze pacing state",
            ObservedAt = observedAt,
            ManufacturingObjectId = manufacturingObjectId,
            ClientObjectAddress = clientObjectAddress,
            AuxDataAddress = auxDataAddress,
            IsAnalyzePanelActive = true,
            Mode = mode,
            IsAnalyzeUiPacingAvailable = true,
            IsAnalyzeUiAttemptInProgress = attemptInProgress,
            AnalyzeUiDelayRemainingDeciseconds =
                delayRemainingDeciseconds,
        };
    }

    private static void PublishActivityState(
        ObservedClientState state,
        ClientManufacturingActivityObservation next)
    {
        var previous = state.ManufacturingActivity;
        if (AreEquivalentForSnapshot(previous, next))
        {
            return;
        }

        state.ManufacturingActivity = next;
    }

    private static bool AreEquivalentForSnapshot(
        ClientManufacturingActivityObservation previous,
        ClientManufacturingActivityObservation next)
    {
        return
            previous.IsAvailable == next.IsAvailable &&
            string.Equals(previous.Status, next.Status, StringComparison.Ordinal) &&
            previous.ManufacturingObjectId == next.ManufacturingObjectId &&
            previous.ClientObjectAddress == next.ClientObjectAddress &&
            previous.AuxDataAddress == next.AuxDataAddress &&
            previous.IsAnalyzePanelActive == next.IsAnalyzePanelActive &&
            previous.IsManufacturingPanelActive == next.IsManufacturingPanelActive &&
            previous.Mode == next.Mode &&
            previous.Validity == next.Validity &&
            previous.TargetItemTemplateId == next.TargetItemTemplateId &&
            previous.NegotiatedCostCredits == next.NegotiatedCostCredits &&
            previous.SuccessProbabilityPercent == next.SuccessProbabilityPercent &&
            previous.CriticalSuccessProbabilityPercent ==
                next.CriticalSuccessProbabilityPercent &&
            previous.IsManufactureOutputObservationAvailable ==
                next.IsManufactureOutputObservationAvailable &&
            previous.ManufactureOutputSequence == next.ManufactureOutputSequence &&
            previous.ManufactureOutputItemTemplateId ==
                next.ManufactureOutputItemTemplateId &&
            previous.ManufactureOutputQuantity == next.ManufactureOutputQuantity &&
            previous.ManufactureOutputQualityPercent ==
                next.ManufactureOutputQualityPercent &&
            previous.ResultComponentItemTemplateIds.SequenceEqual(
                next.ResultComponentItemTemplateIds);
    }

    private static bool TryReadAnalyzeUiPacing(
        ProcessMemoryReader memory,
        uint analyzePanelAddress,
        out bool attemptInProgress,
        out int delayRemainingDeciseconds)
    {
        attemptInProgress = false;
        delayRemainingDeciseconds = 0;

        try
        {
            if (!TryReadByte(
                    memory,
                    analyzePanelAddress,
                    AnalyzePanelAttemptPhaseValue,
                    out var attemptPhase) ||
                !TryReadUInt32(
                    memory,
                    analyzePanelAddress,
                    AnalyzePanelDelayRemainingValue,
                    out var rawDelay))
            {
                return false;
            }

            var delaySeconds = BitConverter.Int32BitsToSingle(
                unchecked((int)rawDelay));
            if (!float.IsFinite(delaySeconds))
            {
                return false;
            }

            attemptInProgress = attemptPhase != 0;
            var clampedDelaySeconds = Math.Clamp(delaySeconds, 0.0f, 10.0f);
            delayRemainingDeciseconds = (int)Math.Ceiling(
                clampedDelaySeconds * 10.0f);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private void TryReadManufacturingMetrics(
        ProcessMemoryReader memory,
        int processId,
        uint moduleBaseAddress,
        uint auxDataAddress,
        DateTimeOffset observedAt,
        out ulong? negotiatedCostCredits,
        out float? successProbabilityPercent,
        out float? criticalSuccessProbabilityPercent)
    {
        negotiatedCostCredits = null;
        successProbabilityPercent = null;
        criticalSuccessProbabilityPercent = null;

        if (!this.TryGetManufacturingMetricLookup(
                memory,
                processId,
                moduleBaseAddress,
                auxDataAddress,
                observedAt,
                out var lookup))
        {
            return;
        }

        if (lookup.Properties.TryGetValue(
                NegotiatedCostPropertyName,
                out var costPropertyAddress) &&
            TryReadUInt64AuxDataValue(
                memory,
                costPropertyAddress,
                out var cost) &&
            cost > 0)
        {
            negotiatedCostCredits = cost;
        }

        if (this.auxDataReader.TryReadFloatProperty(
                memory,
                lookup,
                SuccessProbabilityPropertyName,
                out var successSample,
                out _) &&
            successSample.IsValid &&
            float.IsFinite(successSample.Value))
        {
            successProbabilityPercent = successSample.Value * 100.0f;
        }

        if (this.auxDataReader.TryReadFloatProperty(
                memory,
                lookup,
                CriticalSuccessProbabilityPropertyName,
                out var criticalSample,
                out _) &&
            criticalSample.IsValid &&
            float.IsFinite(criticalSample.Value))
        {
            criticalSuccessProbabilityPercent = criticalSample.Value * 100.0f;
        }
    }

    private bool TryGetManufacturingMetricLookup(
        ProcessMemoryReader memory,
        int processId,
        uint moduleBaseAddress,
        uint auxDataAddress,
        DateTimeOffset observedAt,
        out ClientAuxDataLookupSnapshot lookup)
    {
        if (this.manufacturingMetricLookupCaches.TryGetValue(
                processId,
                out var cached) &&
            cached.AuxDataAddress == auxDataAddress)
        {
            if (cached.Lookup.HasValue &&
                (HasAllManufacturingMetricProperties(cached.Lookup.Value) ||
                 observedAt < cached.RetryAt))
            {
                lookup = cached.Lookup.Value;
                return true;
            }

            if (!cached.Lookup.HasValue && observedAt < cached.RetryAt)
            {
                lookup = default;
                return false;
            }
        }

        if (this.auxDataReader.TryOpenTargeted(
                memory,
                moduleBaseAddress,
                auxDataAddress,
                manufacturingMetricPropertyNames,
                out lookup,
                out _,
                out _))
        {
            this.manufacturingMetricLookupCaches[processId] =
                new ManufacturingMetricLookupCache(
                    auxDataAddress,
                    lookup,
                    HasAllManufacturingMetricProperties(lookup)
                        ? DateTimeOffset.MaxValue
                        : observedAt + LookupRetryDelay);
            return true;
        }

        this.manufacturingMetricLookupCaches[processId] =
            new ManufacturingMetricLookupCache(
                auxDataAddress,
                null,
                observedAt + LookupRetryDelay);
        lookup = default;
        return false;
    }


    private int ResolveFastManufactureTargetItemTemplateId(
        int processId,
        int currentTargetItemTemplateId,
        int validity)
    {
        if (currentTargetItemTemplateId > 0)
        {
            return currentTargetItemTemplateId;
        }

        // The native result transition can clear ManufactureTarget before the
        // newly built item is visible in cargo. Retain only the already
        // baselined target during the proven terminal-validity window. Once the
        // terminal clears, target 0 resets the baseline normally.
        return validity is 14 or 15 or 16 or 17 &&
               this.fastManufactureOutputStates.TryGetValue(
                   processId,
                   out var outputState)
            ? outputState.TargetItemTemplateId
            : 0;
    }

    private static bool HasAllManufacturingMetricProperties(
        ClientAuxDataLookupSnapshot lookup)
    {
        return manufacturingMetricPropertyNames.All(name =>
            lookup.Properties.TryGetValue(name, out var address) &&
            address != 0);
    }

    private FastManufactureOutputObservation ObserveFastManufactureOutput(
        ProcessMemoryReader memory,
        ObservedClientState state,
        int targetItemTemplateId,
        DateTimeOffset observedAt,
        ClientAuxDataLookupSnapshot? localPlayerLookup)
    {
        if (targetItemTemplateId <= 0 || state.LocalPlayerAuxDataAddress == 0)
        {
            this.ResetFastManufactureBaseline(state.ProcessId);
            return this.GetLatestFastManufactureOutput(
                state.ProcessId,
                isAvailable: false);
        }

        if (localPlayerLookup.HasValue)
        {
            this.TryGetFastCargoLookup(
                state.ProcessId,
                state.LocalPlayerAuxDataAddress,
                ResolveFastCargoSlotCount(state),
                localPlayerLookup.Value,
                out _);
        }

        if (!this.fastCargoLookupCaches.TryGetValue(
                state.ProcessId,
                out var cache) ||
            !cache.IsAvailable ||
            cache.LocalPlayerAuxDataAddress !=
                state.LocalPlayerAuxDataAddress)
        {
            return this.GetLatestFastManufactureOutput(
                state.ProcessId,
                isAvailable: false);
        }

        var quantity = 0;
        var currentSlotQuantities =
            new int[MaximumFastCraftingCargoSlots];
        var currentSlotQualityPercent =
            new float?[MaximumFastCraftingCargoSlots];
        var readSucceeded = true;

        for (var slot = 0; slot < cache.Slots.Count; slot++)
        {
            var addresses = cache.Slots[slot];
            if (!TryReadInt32AuxDataProperty(
                    memory,
                    addresses.ItemTemplateIdPropertyAddress,
                    out var itemTemplateIdValid,
                    out var itemTemplateId))
            {
                readSucceeded = false;
                break;
            }

            if (!itemTemplateIdValid || itemTemplateId != targetItemTemplateId)
            {
                continue;
            }

            if (!TryReadInt32AuxDataProperty(
                    memory,
                    addresses.StackCountPropertyAddress,
                    out var stackCountValid,
                    out var stackCount))
            {
                readSucceeded = false;
                break;
            }

            var slotQuantity = stackCountValid
                ? Math.Max(1, stackCount)
                : 1;
            quantity += slotQuantity;
            currentSlotQuantities[slot] = slotQuantity;

            if (TryReadFloatAuxDataProperty(
                    memory,
                    addresses.QualityPropertyAddress,
                    out var qualityValid,
                    out var quality) &&
                qualityValid &&
                float.IsFinite(quality))
            {
                currentSlotQualityPercent[slot] = quality * 100.0f;
            }
        }

        if (!readSucceeded)
        {
            return this.GetLatestFastManufactureOutput(
                state.ProcessId,
                isAvailable: false);
        }

        if (!this.fastManufactureOutputStates.TryGetValue(
                state.ProcessId,
                out var outputState))
        {
            outputState = new FastManufactureOutputState();
            this.fastManufactureOutputStates[state.ProcessId] = outputState;
        }

        if (outputState.TargetItemTemplateId != targetItemTemplateId)
        {
            outputState.TargetItemTemplateId = targetItemTemplateId;
            outputState.LastQuantity = quantity;
            outputState.LastSlotQuantities = currentSlotQuantities;
            return outputState.ToObservation(isAvailable: true);
        }

        var delta = quantity - outputState.LastQuantity;
        float? outputQualityPercent = null;
        if (delta > 0)
        {
            for (var slot = 0;
                 slot < currentSlotQuantities.Length;
                 slot++)
            {
                var previousSlotQuantity = slot <
                    outputState.LastSlotQuantities.Length
                        ? outputState.LastSlotQuantities[slot]
                        : 0;
                if (currentSlotQuantities[slot] > previousSlotQuantity &&
                    currentSlotQualityPercent[slot].HasValue)
                {
                    outputQualityPercent = currentSlotQualityPercent[slot];
                    break;
                }
            }

            outputState.Sequence++;
            outputState.LastOutputObservedAt = observedAt;
            outputState.LastOutputItemTemplateId = targetItemTemplateId;
            outputState.LastOutputQuantity = delta;
            outputState.LastOutputQualityPercent = outputQualityPercent;
        }

        outputState.LastQuantity = quantity;
        outputState.LastSlotQuantities = currentSlotQuantities;

        return outputState.ToObservation(isAvailable: true);
    }

    private static int ResolveFastCargoSlotCount(
        ObservedClientState state)
    {
        var reportedCapacity = state.LocalPlayer.Inventory.CargoCapacity;
        return reportedCapacity.HasValue
            ? Math.Clamp(
                reportedCapacity.Value,
                1,
                MaximumFastCraftingCargoSlots)
            : MaximumFastCraftingCargoSlots;
    }

    private bool TryGetFastCargoLookup(
        int processId,
        uint localPlayerAuxDataAddress,
        int cargoSlotCount,
        ClientAuxDataLookupSnapshot lookup,
        out FastCargoLookupCache cache)
    {
        if (this.fastCargoLookupCaches.TryGetValue(
                processId,
                out var cached) &&
            cached.LocalPlayerAuxDataAddress == localPlayerAuxDataAddress &&
            cached.Slots.Count == cargoSlotCount &&
            cached.IsAvailable)
        {
            cache = cached;
            return true;
        }

        List<FastCargoSlotAddresses> slots = [];
        for (var slot = 0; slot < cargoSlotCount; slot++)
        {
            var prefix = string.Create(
                CultureInfo.InvariantCulture,
                $"Inventory.Cargo.{slot}");
            if (!lookup.Properties.TryGetValue(
                    $"{prefix}.ItemTemplateID",
                    out var itemTemplateIdAddress) ||
                !lookup.Properties.TryGetValue(
                    $"{prefix}.StackCount",
                    out var stackCountAddress) ||
                !lookup.Properties.TryGetValue(
                    $"{prefix}.Quality",
                    out var qualityAddress) ||
                itemTemplateIdAddress == 0 ||
                stackCountAddress == 0 ||
                qualityAddress == 0)
            {
                cache = new FastCargoLookupCache(
                    localPlayerAuxDataAddress,
                    [],
                    DateTimeOffset.MaxValue,
                    IsAvailable: false);
                this.fastCargoLookupCaches[processId] = cache;
                return false;
            }

            slots.Add(new FastCargoSlotAddresses(
                itemTemplateIdAddress,
                stackCountAddress,
                qualityAddress));
        }

        cache = new FastCargoLookupCache(
            localPlayerAuxDataAddress,
            slots,
            DateTimeOffset.MaxValue,
            IsAvailable: true);
        this.fastCargoLookupCaches[processId] = cache;
        return true;
    }

    private FastManufactureOutputObservation GetLatestFastManufactureOutput(
        int processId,
        bool isAvailable)
    {
        return this.fastManufactureOutputStates.TryGetValue(
                processId,
                out var state)
            ? state.ToObservation(isAvailable)
            : new FastManufactureOutputObservation(
                isAvailable,
                0,
                null,
                0,
                0,
                null);
    }

    private void ResetFastManufactureBaseline(int processId)
    {
        if (this.fastManufactureOutputStates.TryGetValue(
                processId,
                out var state))
        {
            state.TargetItemTemplateId = 0;
            state.LastQuantity = 0;
            state.LastSlotQuantities = [];
        }
    }

    private static bool TryReadUInt64AuxDataValue(
        ProcessMemoryReader memory,
        uint propertyAddress,
        out ulong value)
    {
        value = 0;
        if (propertyAddress == 0 ||
            !memory.TryReadUInt32(
                propertyAddress + AuxDataPropertyValid,
                out var valid) ||
            valid == 0 ||
            !memory.TryReadUInt32(
                propertyAddress + UInt64AuxDataPropertyLowWord,
                out var low) ||
            !memory.TryReadUInt32(
                propertyAddress + UInt64AuxDataPropertyHighWord,
                out var high))
        {
            return false;
        }

        value = ((ulong)high << 32) | low;
        return true;
    }

    private static bool TryReadInt32AuxDataProperty(
        ProcessMemoryReader memory,
        uint propertyAddress,
        out bool isValid,
        out int value)
    {
        isValid = false;
        value = 0;
        if (propertyAddress == 0 ||
            !memory.TryReadUInt32(
                propertyAddress + AuxDataPropertyValid,
                out var valid) ||
            !memory.TryReadUInt32(
                propertyAddress + AuxDataPropertyPrimaryValue,
                out var raw))
        {
            return false;
        }

        isValid = valid != 0;
        value = unchecked((int)raw);
        return true;
    }

    private static bool TryReadFloatAuxDataProperty(
        ProcessMemoryReader memory,
        uint propertyAddress,
        out bool isValid,
        out float value)
    {
        isValid = false;
        value = 0;
        if (propertyAddress == 0 ||
            !memory.TryReadUInt32(
                propertyAddress + AuxDataPropertyValid,
                out var valid) ||
            !memory.TryReadUInt32(
                propertyAddress + AuxDataPropertyPrimaryValue,
                out var raw))
        {
            return false;
        }

        isValid = valid != 0;
        value = BitConverter.Int32BitsToSingle(unchecked((int)raw));
        return !isValid || float.IsFinite(value);
    }

    private sealed record ManufacturingMetricLookupCache(
        uint AuxDataAddress,
        ClientAuxDataLookupSnapshot? Lookup,
        DateTimeOffset RetryAt);

    private sealed record FastCargoSlotAddresses(
        uint ItemTemplateIdPropertyAddress,
        uint StackCountPropertyAddress,
        uint QualityPropertyAddress);

    private sealed record FastCargoLookupCache(
        uint LocalPlayerAuxDataAddress,
        IReadOnlyList<FastCargoSlotAddresses> Slots,
        DateTimeOffset RetryAt,
        bool IsAvailable);

    private sealed record FastManufactureOutputObservation(
        bool IsAvailable,
        long Sequence,
        DateTimeOffset? ObservedAt,
        int ItemTemplateId,
        int Quantity,
        float? QualityPercent);

    private sealed class FastManufactureOutputState
    {
        public int TargetItemTemplateId { get; set; }
        public int LastQuantity { get; set; }
        public int[] LastSlotQuantities { get; set; } = [];
        public long Sequence { get; set; }
        public DateTimeOffset? LastOutputObservedAt { get; set; }
        public int LastOutputItemTemplateId { get; set; }
        public int LastOutputQuantity { get; set; }
        public float? LastOutputQualityPercent { get; set; }

        public FastManufactureOutputObservation ToObservation(bool isAvailable) =>
            new(
                isAvailable,
                this.Sequence,
                this.LastOutputObservedAt,
                this.LastOutputItemTemplateId,
                this.LastOutputQuantity,
                this.LastOutputQualityPercent);
    }

    private static bool IsPanelActive(
        ProcessMemoryReader memory,
        ClientStarbasePanelObservation? panel,
        int? currentInterfaceCommand)
    {
        if (panel is not { Address: > 0 } ||
            !TryReadByte(
                memory,
                panel.Address,
                PanelActiveFlagValue,
                out var activeFlag) ||
            activeFlag == 0)
        {
            return false;
        }

        // The fast crafting lane must not depend on the slower cached
        // StarbaseContext command. Read the command directly alongside the
        // panel flag. If that one diagnostic read fails, the native +0x7C
        // active byte remains the authoritative positive signal rather than
        // manufacturing a false panel-close edge.
        return !panel.CurrentInterfaceCommand.HasValue ||
               !currentInterfaceCommand.HasValue ||
               panel.CurrentInterfaceCommand.Value ==
                   currentInterfaceCommand.Value;
    }

    public void RefreshCatalog(
        ProcessMemoryReader memory,
        ObservedClientState state,
        DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(state);

        var panel = state.StarbaseContext.Diagnostics.Panels
            .FirstOrDefault(item =>
                item.Kind == ClientStarbasePanelKind.Manufacturing);

        if (panel == null || panel.Address == 0)
        {
            this.categoryCaches.Remove(state.ProcessId);
            SetStableCatalogUnavailable(
                state,
                "Manufacturing panel is not available",
                observedAt,
                panel?.Address ?? 0);
            return;
        }

        int? currentInterfaceCommand = null;
        if (state.StarbaseContext.StarbaseViewAddress != 0 &&
            TryReadInt32(
                memory,
                state.StarbaseContext.StarbaseViewAddress,
                StarbaseViewCurrentInterfaceCommand,
                out var directCurrentInterfaceCommand))
        {
            currentInterfaceCommand = directCurrentInterfaceCommand;
        }

        if (!IsPanelActive(memory, panel, currentInterfaceCommand))
        {
            this.categoryCaches.Remove(state.ProcessId);
            SetStableCatalogUnavailable(
                state,
                "Manufacturing panel is not active",
                observedAt,
                panel.Address);
            return;
        }

        if (!this.TryResolveManufacturingLab(
                memory,
                state.ClientContextAddress,
                out var resolved,
                out var error))
        {
            state.ManufacturingCatalog =
                ClientManufacturingCatalogObservation.Unavailable(
                    error,
                    observedAt,
                    panelAddress: panel.Address,
                    isManufacturingPanelActive: true);
            return;
        }

        if (!this.TryGetCachedCategories(
                memory,
                state.ProcessId,
                resolved.AuxDataAddress,
                out var categories,
                out error))
        {
            state.ManufacturingCatalog =
                ClientManufacturingCatalogObservation.Unavailable(
                    error,
                    observedAt,
                    resolved.ManufacturingObjectId,
                    resolved.ClientObjectAddress,
                    resolved.AuxDataAddress,
                    panel.Address,
                    isManufacturingPanelActive: true);
            return;
        }

        if (!TryReadCatalogFrame(
                memory,
                resolved.AuxDataAddress,
                panel.Address,
                categories,
                out var first,
                out error) ||
            !TryReadCatalogFrame(
                memory,
                resolved.AuxDataAddress,
                panel.Address,
                categories,
                out var second,
                out error))
        {
            state.ManufacturingCatalog =
                ClientManufacturingCatalogObservation.Unavailable(
                    error,
                    observedAt,
                    resolved.ManufacturingObjectId,
                    resolved.ClientObjectAddress,
                    resolved.AuxDataAddress,
                    panel.Address,
                    isManufacturingPanelActive: true);
            return;
        }

        if (!string.Equals(
                first.Fingerprint,
                second.Fingerprint,
                StringComparison.Ordinal))
        {
            state.ManufacturingCatalog =
                ClientManufacturingCatalogObservation.Unavailable(
                    "Manufacturing catalogue changed while it was being read",
                    observedAt,
                    resolved.ManufacturingObjectId,
                    resolved.ClientObjectAddress,
                    resolved.AuxDataAddress,
                    panel.Address,
                    isManufacturingPanelActive: true);
            return;
        }

        state.ManufacturingCatalog = new ClientManufacturingCatalogObservation
        {
            IsAvailable = true,
            Status = second.CanRecord
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"Manufacturing category {second.CurrentItemCategoryId} exposes {second.KnownFormulas.Count} known formula(s)")
                : second.Status,
            ObservedAt = observedAt,
            ManufacturingObjectId = resolved.ManufacturingObjectId,
            ClientObjectAddress = resolved.ClientObjectAddress,
            AuxDataAddress = resolved.AuxDataAddress,
            PanelAddress = panel.Address,
            IsManufacturingPanelActive = true,
            BrowserStage = second.BrowserStage,
            PrimaryIndex = second.PrimaryIndex,
            SecondaryIndex = second.SecondaryIndex,
            LeafIndex = second.LeafIndex,
            ShowingPreviousAttempts = second.ShowingPreviousAttempts,
            CurrentItemCategoryId = second.CurrentItemCategoryId,
            TechLevelFilterBitfield = second.TechLevelFilterBitfield,
            PendingTechFilterRequestCount =
                second.PendingTechFilterRequestCount,
            Categories = second.Categories,
            KnownFormulas = second.KnownFormulas,
            ResultFingerprint = second.Fingerprint,
        };
    }

    private bool TryResolveManufacturingLab(
        ProcessMemoryReader memory,
        uint clientContextAddress,
        out ResolvedManufacturingLab resolved,
        out string error)
    {
        resolved = default;
        error = "";

        if (clientContextAddress == 0)
        {
            error = "SClient is unavailable";
            return false;
        }

        if (!TryAdd(
                clientContextAddress,
                ClientContextManufacturingObjectId,
                out var manufacturingObjectIdAddress) ||
            !memory.TryReadUInt32(
                manufacturingObjectIdAddress,
                out var manufacturingObjectId))
        {
            error = "Could not read the active ManufacturingLab ObjectId";
            return false;
        }

        if (manufacturingObjectId is 0 or uint.MaxValue)
        {
            error = "No ManufacturingLab is active";
            return false;
        }

        if (!this.objectResolver.TryLookupClientObject(
                memory,
                clientContextAddress,
                manufacturingObjectId,
                out var clientObjectAddress,
                out var resolveError,
                out _))
        {
            error = resolveError;
            return false;
        }

        if (!TryAdd(
                clientObjectAddress,
                ClientObjectAuxData,
                out var auxDataPointerAddress) ||
            !memory.TryReadUInt32(
                auxDataPointerAddress,
                out var auxDataAddress) ||
            auxDataAddress == 0)
        {
            error = "Could not resolve the ManufacturingLab AuxData pointer";
            return false;
        }

        resolved = new ResolvedManufacturingLab(
            manufacturingObjectId,
            clientObjectAddress,
            auxDataAddress);
        return true;
    }

    private static void SetStableCatalogUnavailable(
        ObservedClientState state,
        string status,
        DateTimeOffset observedAt,
        uint panelAddress)
    {
        if (!state.ManufacturingCatalog.IsAvailable &&
            !state.ManufacturingCatalog.IsManufacturingPanelActive &&
            state.ManufacturingCatalog.PanelAddress == panelAddress &&
            string.Equals(
                state.ManufacturingCatalog.Status,
                status,
                StringComparison.Ordinal))
        {
            return;
        }

        state.ManufacturingCatalog =
            ClientManufacturingCatalogObservation.Unavailable(
                status,
                observedAt,
                panelAddress: panelAddress);
    }

    private bool TryGetCachedCategories(
        ProcessMemoryReader memory,
        int processId,
        uint auxDataAddress,
        out IReadOnlyList<ClientManufacturingCategoryObservation> categories,
        out string error)
    {
        if (this.categoryCaches.TryGetValue(processId, out var cache) &&
            cache.AuxDataAddress == auxDataAddress &&
            cache.Categories.Any(category => category.IsVisible))
        {
            categories = cache.Categories;
            error = "";
            return true;
        }

        if (!TryReadCategories(
                memory,
                auxDataAddress,
                out categories,
                out error))
        {
            return false;
        }

        // The terminal briefly exposes names/IDs before its visibility flags
        // are initialized. Never freeze that transient zero-visible tree into
        // the session cache. Re-read until at least one real leaf is visible,
        // then the category geometry is stable for the terminal session.
        if (categories.Any(category => category.IsVisible))
        {
            this.categoryCaches[processId] =
                new ManufacturingCategoryCache(auxDataAddress, categories);
        }
        else
        {
            this.categoryCaches.Remove(processId);
        }

        return true;
    }

    private static bool TryReadCatalogFrame(
        ProcessMemoryReader memory,
        uint auxDataAddress,
        uint panelAddress,
        IReadOnlyList<ClientManufacturingCategoryObservation> categories,
        out CatalogFrame frame,
        out string error)
    {
        frame = null!;
        error = "";

        if (!TryReadInt32(memory, panelAddress, PanelBrowserStageValue, out var browserStage) ||
            !TryReadInt32(memory, panelAddress, PanelPrimaryIndexValue, out var primaryIndex) ||
            !TryReadInt32(memory, panelAddress, PanelSecondaryIndexValue, out var secondaryIndex) ||
            !TryReadInt32(memory, panelAddress, PanelLeafIndexValue, out var leafIndex) ||
            !TryReadByte(memory, panelAddress, PanelPreviousAttemptsValue, out var previousAttempts) ||
            !TryReadInt32(
                memory,
                panelAddress,
                PanelPendingTechFilterRequestCountValue,
                out var pendingTechFilterRequestCount) ||
            !TryReadInt32(memory, auxDataAddress, CurrentItemCategoryValue, out var currentCategoryId) ||
            !TryReadUInt32(memory, auxDataAddress, TechLevelFilterBitfieldValue, out var techLevelFilter))
        {
            error = "Could not read the Manufacturing panel catalogue state";
            return false;
        }

        if (!TryReadKnownFormulas(
                memory,
                auxDataAddress,
                out var knownFormulas,
                out error))
        {
            return false;
        }

        var selectedCategory = categories.FirstOrDefault(category =>
            category.IsVisible &&
            category.CategoryId == currentCategoryId);
        var allTechLevelsEnabled = (techLevelFilter & 0x01ff) == 0x01ff;
        var selectionMatchesCurrentCategory =
            selectedCategory != null &&
            primaryIndex == selectedCategory.PrimaryIndex &&
            secondaryIndex == selectedCategory.SecondaryIndex &&
            leafIndex == selectedCategory.LeafIndex;
        var canRecord =
            browserStage == 3 &&
            previousAttempts == 0 &&
            selectionMatchesCurrentCategory &&
            allTechLevelsEnabled &&
            pendingTechFilterRequestCount == 0;

        var status = pendingTechFilterRequestCount != 0
            ? "Waiting for the Manufacturing tech-level filter request"
            : !allTechLevelsEnabled
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"Enable all manufacturing tech levels before recording this category (filter 0x{techLevelFilter:X})")
                : browserStage != 3
                    ? "Select a manufacturing leaf category to record it"
                    : previousAttempts != 0
                        ? "Previous Attempts is active; switch back to Known Formula"
                        : !selectionMatchesCurrentCategory
                            ? "Waiting for the selected manufacturing category result"
                            : "Manufacturing category result is ready to stabilize";

        var fingerprint = ComputeCatalogFingerprint(
            browserStage,
            primaryIndex,
            secondaryIndex,
            leafIndex,
            previousAttempts != 0,
            currentCategoryId,
            techLevelFilter,
            pendingTechFilterRequestCount,
            knownFormulas);

        frame = new CatalogFrame(
            browserStage,
            primaryIndex,
            secondaryIndex,
            leafIndex,
            previousAttempts != 0,
            currentCategoryId,
            techLevelFilter,
            pendingTechFilterRequestCount,
            categories,
            knownFormulas,
            fingerprint,
            canRecord,
            status);
        return true;
    }

    private static bool TryReadCategories(
        ProcessMemoryReader memory,
        uint auxDataAddress,
        out IReadOnlyList<ClientManufacturingCategoryObservation> categories,
        out string error)
    {
        categories = [];
        error = "";

        if (!TryReadVectorEntries(
                memory,
                auxDataAddress,
                PrimaryCategoriesProperty,
                MaximumPrimaryCategories,
                allowEmpty: false,
                out var primaryEntries,
                out error))
        {
            return false;
        }

        List<ClientManufacturingCategoryObservation> result = [];

        for (var primaryIndex = 0;
             primaryIndex < primaryEntries.Count;
             primaryIndex++)
        {
            var primaryAddress = primaryEntries[primaryIndex];

            if (!TryReadPointerString(
                    memory,
                    primaryAddress,
                    CategoryNameValue,
                    out var primaryName))
            {
                error = string.Create(
                    CultureInfo.InvariantCulture,
                    $"Could not read manufacturing primary category {primaryIndex} name");
                return false;
            }

            if (!TryReadDirectPointerVector(
                    memory,
                    primaryAddress,
                    PrimaryCategoriesBegin,
                    PrimaryCategoriesEnd,
                    MaximumSecondaryCategories,
                    allowEmpty: true,
                    out var secondaryEntries,
                    out error))
            {
                return false;
            }

            for (var secondaryIndex = 0;
                 secondaryIndex < secondaryEntries.Count;
                 secondaryIndex++)
            {
                var secondaryAddress = secondaryEntries[secondaryIndex];

                if (!TryReadPointerString(
                        memory,
                        secondaryAddress,
                        CategoryNameValue,
                        out var secondaryName))
                {
                    error = string.Create(
                        CultureInfo.InvariantCulture,
                        $"Could not read manufacturing secondary category {primaryIndex}/{secondaryIndex} name");
                    return false;
                }

                if (!TryReadDirectPointerVector(
                        memory,
                        secondaryAddress,
                        SecondarySubcategoriesBegin,
                        SecondarySubcategoriesEnd,
                        MaximumLeafCategories,
                        allowEmpty: true,
                        out var leafEntries,
                        out error))
                {
                    return false;
                }

                for (var leafIndex = 0;
                     leafIndex < leafEntries.Count;
                     leafIndex++)
                {
                    var leafAddress = leafEntries[leafIndex];

                    if (!TryReadPointerString(
                            memory,
                            leafAddress,
                            CategoryNameValue,
                            out var leafName))
                    {
                        error = string.Create(
                            CultureInfo.InvariantCulture,
                            $"Could not read manufacturing leaf category {primaryIndex}/{secondaryIndex}/{leafIndex} name");
                        return false;
                    }

                    if (!TryReadInt32(
                            memory,
                            leafAddress,
                            LeafCategoryIdValue,
                            out var categoryId) ||
                        !TryReadByte(
                            memory,
                            leafAddress,
                            LeafVisibleValue,
                            out var visible))
                    {
                        error = string.Create(
                            CultureInfo.InvariantCulture,
                            $"Could not read manufacturing category {primaryIndex}/{secondaryIndex}/{leafIndex}");
                        return false;
                    }

                    result.Add(
                        new ClientManufacturingCategoryObservation
                        {
                            PrimaryIndex = primaryIndex,
                            SecondaryIndex = secondaryIndex,
                            LeafIndex = leafIndex,
                            PrimaryName = primaryName,
                            SecondaryName = secondaryName,
                            LeafName = leafName,
                            CategoryId = categoryId,
                            IsVisible = visible != 0 && categoryId > 0,
                        });
                }
            }
        }

        categories = result;
        return true;
    }

    private static bool TryReadKnownFormulas(
        ProcessMemoryReader memory,
        uint auxDataAddress,
        out IReadOnlyList<ClientKnownManufacturingFormulaObservation> formulas,
        out string error)
    {
        formulas = [];
        error = "";

        if (!TryReadVectorEntries(
                memory,
                auxDataAddress,
                KnownFormulaProperty,
                MaximumKnownFormulaEntries,
                allowEmpty: false,
                out var entries,
                out error))
        {
            return false;
        }

        List<ClientKnownManufacturingFormulaObservation> result = [];

        for (var index = 0; index < entries.Count; index++)
        {
            var entryAddress = entries[index];

            if (!TryReadPointerString(
                    memory,
                    entryAddress,
                    FormulaItemNameValue,
                    out var itemName))
            {
                error = string.Create(
                    CultureInfo.InvariantCulture,
                    $"Could not read KnownFormula entry {index} name");
                return false;
            }

            if (string.IsNullOrWhiteSpace(itemName))
            {
                break;
            }

            if (!TryReadInt32(
                    memory,
                    entryAddress,
                    FormulaItemIdValue,
                    out var itemTemplateId) ||
                itemTemplateId <= 0 ||
                !TryReadInt32(
                    memory,
                    entryAddress,
                    FormulaTechLevelValue,
                    out var techLevel))
            {
                error = string.Create(
                    CultureInfo.InvariantCulture,
                    $"Could not read KnownFormula entry {index}");
                return false;
            }

            result.Add(
                new ClientKnownManufacturingFormulaObservation
                {
                    Index = index,
                    ItemTemplateId = itemTemplateId,
                    ItemName = itemName,
                    TechLevel = techLevel,
                });
        }

        formulas = result;
        return true;
    }

    private static bool TryReadVectorEntries(
        ProcessMemoryReader memory,
        uint baseAddress,
        uint propertyOffset,
        int maximumCount,
        bool allowEmpty,
        out IReadOnlyList<uint> entries,
        out string error)
    {
        entries = [];
        error = "";

        if (!TryAdd(baseAddress, propertyOffset, out var propertyAddress) ||
            !TryAdd(propertyAddress, PropertyVectorBegin, out var beginOffset) ||
            !TryAdd(propertyAddress, PropertyVectorEnd, out var endOffset) ||
            !memory.TryReadUInt32(beginOffset, out var begin) ||
            !memory.TryReadUInt32(endOffset, out var end))
        {
            error = string.Create(
                CultureInfo.InvariantCulture,
                $"Could not read ManufacturingLab vector at +0x{propertyOffset:X}");
            return false;
        }

        return TryReadPointerVector(
            memory,
            begin,
            end,
            maximumCount,
            allowEmpty,
            out entries,
            out error);
    }

    private static bool TryReadDirectPointerVector(
        ProcessMemoryReader memory,
        uint baseAddress,
        uint beginOffset,
        uint endOffset,
        int maximumCount,
        bool allowEmpty,
        out IReadOnlyList<uint> entries,
        out string error)
    {
        entries = [];
        error = "";

        if (!TryAdd(baseAddress, beginOffset, out var beginAddress) ||
            !TryAdd(baseAddress, endOffset, out var endAddress) ||
            !memory.TryReadUInt32(beginAddress, out var begin) ||
            !memory.TryReadUInt32(endAddress, out var end))
        {
            error = "Could not read manufacturing category vector";
            return false;
        }

        return TryReadPointerVector(
            memory,
            begin,
            end,
            maximumCount,
            allowEmpty,
            out entries,
            out error);
    }

    private static bool TryReadPointerVector(
        ProcessMemoryReader memory,
        uint begin,
        uint end,
        int maximumCount,
        bool allowEmpty,
        out IReadOnlyList<uint> entries,
        out string error)
    {
        entries = [];
        error = "";

        if (begin == 0)
        {
            if (allowEmpty && end == 0)
            {
                return true;
            }

            error = "Manufacturing vector begin pointer is null";
            return false;
        }

        if (end < begin ||
            (end - begin) % sizeof(uint) != 0)
        {
            error = "Manufacturing vector bounds are invalid";
            return false;
        }

        var count = checked((int)((end - begin) / sizeof(uint)));

        if ((!allowEmpty && count == 0) ||
            count > maximumCount)
        {
            error = string.Create(
                CultureInfo.InvariantCulture,
                $"Manufacturing vector has unexpected count {count}");
            return false;
        }

        if (count == 0)
        {
            return true;
        }

        var buffer = new byte[checked(count * sizeof(uint))];

        if (!memory.TryReadBytes(begin, buffer))
        {
            error = "Could not read manufacturing vector entries";
            return false;
        }

        List<uint> result = new(count);

        for (var index = 0; index < count; index++)
        {
            var entryAddress = BitConverter.ToUInt32(
                buffer,
                index * sizeof(uint));

            if (entryAddress == 0)
            {
                error = string.Create(
                    CultureInfo.InvariantCulture,
                    $"Manufacturing vector entry {index} is null");
                return false;
            }

            result.Add(entryAddress);
        }

        entries = result;
        return true;
    }

    private static int ReadPopulatedItemTemplateId(
        ProcessMemoryReader memory,
        uint entryAddress)
    {
        return TryReadInt32(
                   memory,
                   entryAddress,
                   InventoryItemTemplateIdValue,
                   out var itemTemplateId) &&
               itemTemplateId > 0
            ? itemTemplateId
            : 0;
    }

    private static bool TryReadPointerString(
        ProcessMemoryReader memory,
        uint baseAddress,
        uint pointerOffset,
        out string value)
    {
        value = "";

        if (!TryAdd(baseAddress, pointerOffset, out var pointerAddress) ||
            !memory.TryReadUInt32(pointerAddress, out var valueAddress) ||
            valueAddress == 0 ||
            !memory.TryReadNullTerminatedLatin1String(
                valueAddress,
                MaximumNameLength,
                out var rawValue))
        {
            return false;
        }

        value = rawValue.Trim();
        return true;
    }

    private static bool TryReadInt32(
        ProcessMemoryReader memory,
        uint baseAddress,
        uint offset,
        out int value)
    {
        value = 0;

        if (!TryReadUInt32(
                memory,
                baseAddress,
                offset,
                out var raw))
        {
            return false;
        }

        value = unchecked((int)raw);
        return true;
    }

    private static bool TryReadUInt32(
        ProcessMemoryReader memory,
        uint baseAddress,
        uint offset,
        out uint value)
    {
        value = 0;

        return TryAdd(baseAddress, offset, out var address) &&
            memory.TryReadUInt32(address, out value);
    }

    private static bool TryReadByte(
        ProcessMemoryReader memory,
        uint baseAddress,
        uint offset,
        out byte value)
    {
        value = 0;

        if (!TryAdd(baseAddress, offset, out var address))
        {
            return false;
        }

        var buffer = new byte[1];

        if (!memory.TryReadBytes(address, buffer))
        {
            return false;
        }

        value = buffer[0];
        return true;
    }

    private static string ComputeCatalogFingerprint(
        int browserStage,
        int primaryIndex,
        int secondaryIndex,
        int leafIndex,
        bool showingPreviousAttempts,
        int currentCategoryId,
        uint techLevelFilter,
        int pendingTechFilterRequestCount,
        IReadOnlyList<ClientKnownManufacturingFormulaObservation> formulas)
    {
        var source = string.Join(
            "|",
            browserStage.ToString(CultureInfo.InvariantCulture),
            primaryIndex.ToString(CultureInfo.InvariantCulture),
            secondaryIndex.ToString(CultureInfo.InvariantCulture),
            leafIndex.ToString(CultureInfo.InvariantCulture),
            showingPreviousAttempts ? "1" : "0",
            currentCategoryId.ToString(CultureInfo.InvariantCulture),
            techLevelFilter.ToString("X", CultureInfo.InvariantCulture),
            pendingTechFilterRequestCount.ToString(CultureInfo.InvariantCulture),
            string.Join(
                ",",
                formulas.Select(formula => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{formula.ItemTemplateId}:{formula.TechLevel}:{formula.ItemName}"))));

        return Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(source)))
            .ToLowerInvariant();
    }

    private static bool TryAdd(
        uint left,
        uint right,
        out uint result)
    {
        try
        {
            result = checked(left + right);
            return true;
        }
        catch (OverflowException)
        {
            result = 0;
            return false;
        }
    }

    private sealed record ManufacturingCategoryCache(
        uint AuxDataAddress,
        IReadOnlyList<ClientManufacturingCategoryObservation> Categories);

    private readonly record struct ResolvedManufacturingLab(
        uint ManufacturingObjectId,
        uint ClientObjectAddress,
        uint AuxDataAddress);

    private sealed record CatalogFrame(
        int BrowserStage,
        int PrimaryIndex,
        int SecondaryIndex,
        int LeafIndex,
        bool ShowingPreviousAttempts,
        int CurrentItemCategoryId,
        uint TechLevelFilterBitfield,
        int PendingTechFilterRequestCount,
        IReadOnlyList<ClientManufacturingCategoryObservation> Categories,
        IReadOnlyList<ClientKnownManufacturingFormulaObservation> KnownFormulas,
        string Fingerprint,
        bool CanRecord,
        string Status);
}
