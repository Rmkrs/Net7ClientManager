namespace Net7ClientManager.RecipeMapping;

using System.Collections.Frozen;
using System.Diagnostics;
using Net7ClientManager.Observations;
using Net7ClientManager.Observations.Models;

internal sealed class RecipeMappingCoordinator
{
    // ManufacturingLab result semantics proven from ui_analyze.cpp and
    // runtime control flights. Mode 2 is Analyze; mode 3 is Dismantle.
    // Validities 16 and 17 are normal/critical success for either operation,
    // so recipe learning must always be gated by both the real Analyze panel
    // and Analyze mode. Manufacture is mode 1; its 14-17 terminal ladder is
    // intentionally isolated here so the first live manufacturing flight can
    // confirm that the client uses the same result values before release.
    private const int ManufactureMode = 1;
    private const int AnalyzeMode = 2;
    private const int DismantleMode = 3;
    private const int FailedValidity = 14;
    private const int FailedDamagedValidity = 15;
    private const int SucceededValidity = 16;
    private const int CriticalSucceededValidity = 17;
    private const int InactiveCraftingSamplesBeforeReset = 2;
    private const int StableCategoryObservationCount = 2;

    private static readonly RecipeMappingItemPresentation mappedPresentation =
        new()
        {
            IsManufacturable = true,
            Knowledge = RecipeMappingItemKnowledge.Mapped,
            Text = "Mapped",
        };

    private static readonly RecipeMappingItemPresentation
        missingPresentation = new()
        {
            IsManufacturable = true,
            Knowledge = RecipeMappingItemKnowledge.Missing,
            Text = "Missing",
        };

    private static readonly RecipeMappingItemPresentation
        notScannedPresentation = new()
        {
            IsManufacturable = true,
            Knowledge = RecipeMappingItemKnowledge.NotScanned,
            Text = "Not scanned",
        };

    private static readonly RecipeMappingItemPresentation
        skillNotLearnedPresentation = new()
        {
            IsManufacturable = true,
            Knowledge = RecipeMappingItemKnowledge.SkillNotLearned,
            Text = "Skill not learned",
        };

    private static readonly RecipeMappingItemPresentation
        skillNotAvailablePresentation = new()
        {
            IsManufacturable = true,
            Knowledge = RecipeMappingItemKnowledge.SkillNotAvailable,
            Text = "Skill not available",
        };

    private static readonly RecipeMappingItemPresentation
        notManufacturablePresentation = new()
        {
            IsManufacturable = false,
            Knowledge = RecipeMappingItemKnowledge.NotManufacturable,
            Text = "Not Manufacturable",
        };

    private static readonly RecipeMappingItemPresentation
        unknownManufacturabilityPresentation = new()
        {
            IsManufacturable = null,
            Knowledge = RecipeMappingItemKnowledge.NotApplicable,
        };

    private readonly System.Threading.Lock lockObject = new();
    private readonly RecipeMappingStore store;
    private readonly RecipeMappingDocument document;
    private readonly Dictionary<int, ProcessRecipeMappingState>
        processStates = [];
    private IReadOnlyDictionary<uint, RecipeMappingFastCharacterState>
        fastCharacterStates =
            new Dictionary<uint, RecipeMappingFastCharacterState>();
    private IReadOnlyDictionary<int, IReadOnlyList<RecipeMappingMappedPilot>>
        mappedPilotsByItemTemplateId =
            new Dictionary<int, IReadOnlyList<RecipeMappingMappedPilot>>();

    public RecipeMappingCoordinator(RecipeMappingStore store)
    {
        this.store = store ??
            throw new ArgumentNullException(nameof(store));
        this.store.Initialize();
        this.document = this.store.Load();
        NormalizeDocument(this.document);
        this.PublishAllFastCharacterStates();
    }

    public RecipeMappingObservationResult? Observe(
        ClientObservationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var identity = ClientLiveCharacterIdentityResolver.Resolve(snapshot);

        if (identity.CharacterObjectId is not { } characterId ||
            characterId is 0 or uint.MaxValue)
        {
            lock (this.lockObject)
            {
                this.processStates.Remove(snapshot.ProcessId);
            }

            return new RecipeMappingObservationResult
            {
                Presentation = RecipeMappingPresentation.Unavailable(
                    "Crafting is waiting for the live character identity."),
            };
        }

        var skills = snapshot.LocalPlayer.CharacterProgression.Skills;
        RecipeMappingPresentation presentation;
        RecipeMappingCharacterRecord? recordToSave = null;
        List<RecipeMappingHistoryEventRecord> recordedEvents = [];
        ClientProductionRecipeObservation? recipeToSave = null;

        lock (this.lockObject)
        {
            var record = this.GetOrCreateCharacter(
                characterId,
                identity.Name);
            var processState = this.GetOrCreateProcessState(
                snapshot.ProcessId,
                characterId);
            var normalizedPilotName = identity.Name?.Trim() ?? "";
            var pilotNameChanged =
                normalizedPilotName.Length > 0 &&
                !string.Equals(
                    record.PilotName,
                    normalizedPilotName,
                    StringComparison.Ordinal);
            var needsBuildSkillInitialization =
                record.BaselineInitializedBuildSkillNames.Count <
                    RecipeMappingBuildSkillCatalog.SkillNames.Count &&
                skills.IsAvailable;
            var hasNewBuildSkillObservation =
                skills.IsAvailable &&
                !ReferenceEquals(
                    skills,
                    processState.LastBuildSkillObservation);
            var hasNewCatalogObservation =
                snapshot.ManufacturingCatalog.ObservedAt !=
                    DateTimeOffset.MinValue &&
                snapshot.ManufacturingCatalog.ObservedAt !=
                    processState.LastCatalogObservedAt;
            var hasNewActivityObservation =
                snapshot.ManufacturingActivity.ObservedAt !=
                    DateTimeOffset.MinValue &&
                snapshot.ManufacturingActivity.ObservedAt !=
                    processState.LastActivityObservedAt;
            var hasNewProductionRecipe =
                snapshot.ProductionRecipe.IsAvailable &&
                !string.Equals(
                    snapshot.ProductionRecipe.RecipeFingerprint,
                    processState.LastProductionRecipeFingerprint,
                    StringComparison.Ordinal);

            if (!pilotNameChanged &&
                !needsBuildSkillInitialization &&
                !hasNewBuildSkillObservation &&
                !hasNewCatalogObservation &&
                !hasNewActivityObservation &&
                !hasNewProductionRecipe)
            {
                return null;
            }

            var changed = false;

            if (pilotNameChanged)
            {
                record.PilotName = normalizedPilotName;
                changed = true;
            }

            if (needsBuildSkillInitialization ||
                hasNewBuildSkillObservation)
            {
                changed |= this.ObserveBuildSkillBaseline(
                    record,
                    skills,
                    snapshot.ObservedAt);
                processState.LastBuildSkillObservation = skills;
            }

            changed |= this.ObserveManufacturingCatalog(
                record,
                processState,
                snapshot.ManufacturingCatalog,
                recordedEvents);
            changed |= this.ObserveCraftingActivity(
                record,
                processState,
                snapshot.ManufacturingActivity,
                snapshot.LocalPlayer.Inventory,
                recordedEvents);

            if (hasNewProductionRecipe)
            {
                processState.LastProductionRecipeFingerprint =
                    snapshot.ProductionRecipe.RecipeFingerprint;
                if (snapshot.ProductionRecipe.Kind ==
                        ClientProductionRecipeKind.Manufacture &&
                    snapshot.ProductionRecipe.OutputItemTemplateId > 0 &&
                    snapshot.ProductionRecipe.Ingredients.Count > 0)
                {
                    recipeToSave = snapshot.ProductionRecipe;
                }
            }

            if (changed)
            {
                record.UpdatedAtUtc = snapshot.ObservedAt;
                NormalizeCharacter(record);
                this.PublishFastCharacterState(record);
            }

            if (changed || recordedEvents.Count != 0 || recipeToSave != null)
            {
                // Recipe-component rows have a foreign key to the character.
                // Persist the character before saving newly observed recipe
                // details, even when no mapping state changed in this sample.
                recordToSave = CloneCharacter(record);
            }

            presentation = BuildPresentation(
                record,
                snapshot.ManufacturingCatalog,
                skills,
                includeRecipes: false);
        }

        if (recordToSave != null)
        {
            this.TrySaveCharacter(recordToSave, recordedEvents);
        }

        if (recipeToSave != null)
        {
            this.TrySaveRecipeComponents(
                characterId,
                recipeToSave,
                snapshot.ObservedAt);
        }

        return new RecipeMappingObservationResult
        {
            Presentation = presentation,
            RecordedEvents = recordedEvents,
            RecipeDetailsChanged = recipeToSave != null,
        };
    }

    public void ForgetProcess(int processId)
    {
        lock (this.lockObject)
        {
            this.processStates.Remove(processId);
        }
    }

    public void SetBaselineCaptureTarget(
        int processId,
        uint characterId,
        int? categoryId)
    {
        lock (this.lockObject)
        {
            var normalizedCategoryId = categoryId is > 0
                ? categoryId.Value
                : 0;

            if (!this.processStates.TryGetValue(processId, out var state) ||
                state.CharacterId != characterId)
            {
                if (normalizedCategoryId == 0)
                {
                    return;
                }

                state = this.GetOrCreateProcessState(
                    processId,
                    characterId);
            }

            if (state.BaselineCaptureCategoryId == normalizedCategoryId)
            {
                return;
            }

            state.BaselineCaptureCategoryId = normalizedCategoryId;
            state.PendingCategoryId = 0;
            state.PendingCategoryFingerprint = "";
            state.PendingCategoryStableCount = 0;
        }
    }

    public RecipeMappingPresentation GetPresentation(
        uint characterId,
        ClientManufacturingCatalogObservation? currentCatalog = null,
        ClientCharacterSkillsObservation? currentSkills = null,
        bool includeRecipes = false)
    {
        lock (this.lockObject)
        {
            var record = this.document.Characters.FirstOrDefault(item =>
                item.CharacterId == characterId);

            return record == null
                ? RecipeMappingPresentation.Unavailable(
                    "No crafting data has been recorded for this character yet.")
                : BuildPresentation(
                    record,
                    currentCatalog,
                    currentSkills,
                    includeRecipes);
        }
    }

    public RecipeMappingItemPresentation? ResolveItemPresentation(
        uint characterId,
        int itemTemplateId,
        bool? isManufacturable,
        int categoryId,
        bool requiredBuildSkillAvailable,
        bool requiredBuildSkillLearned)
    {
        if (itemTemplateId <= 0)
        {
            return null;
        }

        var states = Volatile.Read(ref this.fastCharacterStates);
        states.TryGetValue(characterId, out var state);
        var mappedOn = this.GetMappedPilotNames(
            itemTemplateId,
            excludeCharacterId: characterId);
        // The Forge production catalogue and another pilot's mapped recipe are
        // positive evidence only. Absence from either source is not proof that
        // the game refuses to manufacture the item. Keep that case neutral
        // until an authoritative native negative signal is available.
        var isKnownManufacturable =
            isManufacturable == true || mappedOn.Count != 0;

        RecipeMappingItemPresentation presentation;

        if (state?.KnownRecipeItemTemplateIds.Contains(itemTemplateId) == true)
        {
            presentation = mappedPresentation;
        }
        else if (isManufacturable == false)
        {
            presentation = notManufacturablePresentation;
        }
        else if (!isKnownManufacturable)
        {
            presentation = unknownManufacturabilityPresentation;
        }
        else if (!requiredBuildSkillAvailable)
        {
            presentation = skillNotAvailablePresentation;
        }
        else if (!requiredBuildSkillLearned)
        {
            presentation = skillNotLearnedPresentation;
        }
        else if (categoryId <= 0 ||
                 state == null ||
                 !state.ScannedCategoryIds.Contains(categoryId))
        {
            presentation = notScannedPresentation;
        }
        else
        {
            presentation = missingPresentation;
        }

        return mappedOn.Count == 0
            ? presentation
            : presentation with
            {
                MappedOnPilotNames = mappedOn,
            };
    }

    public IReadOnlyList<string> GetMappedPilotNames(
        int itemTemplateId,
        uint? excludeCharacterId = null)
    {
        if (itemTemplateId <= 0)
        {
            return [];
        }

        var index = Volatile.Read(ref this.mappedPilotsByItemTemplateId);
        if (!index.TryGetValue(itemTemplateId, out var pilots) ||
            pilots.Count == 0)
        {
            return [];
        }

        return pilots
            .Where(pilot => pilot.CharacterId != excludeCharacterId)
            .Select(pilot => pilot.PilotName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private bool ObserveBuildSkillBaseline(
        RecipeMappingCharacterRecord record,
        ClientCharacterSkillsObservation skills,
        DateTimeOffset observedAt)
    {
        if (!skills.IsAvailable)
        {
            return false;
        }

        var observedBuildSkills =
            RecipeMappingBuildSkillCatalog.ObserveBuildSkills(skills);

        if (observedBuildSkills.Count !=
            RecipeMappingBuildSkillCatalog.SkillNames.Count)
        {
            return false;
        }

        var changed = false;

        foreach (var skill in observedBuildSkills)
        {
            if (record.BaselineInitializedBuildSkillNames.Contains(
                    skill.Name,
                    StringComparer.Ordinal))
            {
                if (skill.IsLearned &&
                    record.KnownZeroBuildSkillNames.Remove(skill.Name))
                {
                    // A newly learned Build skill makes its recipe categories
                    // uncertain again. Keep every recipe we already know, but
                    // require those categories to be scanned once under the new
                    // skill state. This also matters for shared categories such
                    // as Ammo (Build Weapons / Build Ammunition).
                    foreach (var category in record.Categories.Where(category =>
                                 RecipeMappingBuildSkillCatalog
                                     .GetApplicableSkillNames(category.CategoryId)
                                     .Contains(
                                         skill.Name,
                                         StringComparer.OrdinalIgnoreCase)))
                    {
                        category.IsVisited = false;
                    }

                    record.BaselineObservedComplete = false;
                    record.BaselineCompletedAtUtc = null;
                    changed = true;
                }

                continue;
            }

            record.BaselineInitializedBuildSkillNames.Add(skill.Name);

            if (!skill.IsLearned)
            {
                record.KnownZeroBuildSkillNames.Add(skill.Name);
            }

            changed = true;
        }

        // A Build skill first observed as unlearned has a trustworthy empty
        // recipe state until the game later reports it as learned. Once a
        // skill becomes learned, every category applicable to it is explicitly
        // invalidated above, including shared categories such as Ammo.
        if (!record.BaselineObservedComplete)
        {
            var requiredBuildSkills = GetRequiredBuildSkillNames(record);
            if (requiredBuildSkills.Count == 0 ||
                AreRequiredStoredCategoriesComplete(
                    record,
                    requiredBuildSkills))
            {
                record.BaselineObservedComplete = true;
                record.BaselineCompletedAtUtc ??= observedAt;
                changed = true;
            }
        }

        return changed;
    }

    private bool ObserveManufacturingCatalog(
        RecipeMappingCharacterRecord record,
        ProcessRecipeMappingState processState,
        ClientManufacturingCatalogObservation catalog,
        List<RecipeMappingHistoryEventRecord> recordedEvents)
    {
        var changed = false;

        if (catalog.IsAvailable)
        {
            foreach (var observedCategory in catalog.Categories
                         .Where(category =>
                             category.IsVisible &&
                             category.CategoryId > 0))
            {
                var category = record.Categories.FirstOrDefault(item =>
                    item.CategoryId == observedCategory.CategoryId);

                if (category == null)
                {
                    record.Categories.Add(
                        new RecipeMappingCategoryRecord
                        {
                            CategoryId = observedCategory.CategoryId,
                            PrimaryIndex = observedCategory.PrimaryIndex,
                            SecondaryIndex = observedCategory.SecondaryIndex,
                            LeafIndex = observedCategory.LeafIndex,
                            Path = observedCategory.DisplayPath,
                        });
                    changed = true;
                    continue;
                }

                if (!string.Equals(
                        category.Path,
                        observedCategory.DisplayPath,
                        StringComparison.Ordinal) ||
                    category.PrimaryIndex != observedCategory.PrimaryIndex ||
                    category.SecondaryIndex != observedCategory.SecondaryIndex ||
                    category.LeafIndex != observedCategory.LeafIndex)
                {
                    category.Path = observedCategory.DisplayPath;
                    category.PrimaryIndex = observedCategory.PrimaryIndex;
                    category.SecondaryIndex = observedCategory.SecondaryIndex;
                    category.LeafIndex = observedCategory.LeafIndex;
                    changed = true;
                }
            }
        }

        if (catalog.ObservedAt == DateTimeOffset.MinValue ||
            catalog.ObservedAt == processState.LastCatalogObservedAt)
        {
            return changed;
        }

        processState.LastCatalogObservedAt = catalog.ObservedAt;

        if (processState.BaselineCaptureCategoryId <= 0 ||
            !catalog.CanRecordCurrentCategory ||
            catalog.CurrentCategory == null ||
            catalog.CurrentCategory.CategoryId !=
                processState.BaselineCaptureCategoryId)
        {
            processState.PendingCategoryId = 0;
            processState.PendingCategoryFingerprint = "";
            processState.PendingCategoryStableCount = 0;
            return changed;
        }

        var categoryId = catalog.CurrentCategory.CategoryId;
        var fingerprint = catalog.ResultFingerprint;

        if (processState.PendingCategoryId == categoryId &&
            string.Equals(
                processState.PendingCategoryFingerprint,
                fingerprint,
                StringComparison.Ordinal))
        {
            processState.PendingCategoryStableCount++;
        }
        else
        {
            processState.PendingCategoryId = categoryId;
            processState.PendingCategoryFingerprint = fingerprint;
            processState.PendingCategoryStableCount = 1;
        }

        if (processState.PendingCategoryStableCount <
            StableCategoryObservationCount)
        {
            return changed;
        }

        var targetCategory = record.Categories.FirstOrDefault(item =>
            item.CategoryId == categoryId);

        if (targetCategory == null)
        {
            targetCategory = new RecipeMappingCategoryRecord
            {
                CategoryId = categoryId,
                PrimaryIndex = catalog.CurrentCategory.PrimaryIndex,
                SecondaryIndex = catalog.CurrentCategory.SecondaryIndex,
                LeafIndex = catalog.CurrentCategory.LeafIndex,
                Path = catalog.CurrentCategory.DisplayPath,
            };
            record.Categories.Add(targetCategory);
            changed = true;
        }

        var formulaIds = catalog.KnownFormulas
            .Select(formula => formula.ItemTemplateId)
            .Where(itemTemplateId => itemTemplateId > 0)
            .Distinct()
            .OrderBy(itemTemplateId => itemTemplateId)
            .ToArray();

        if (!targetCategory.IsVisited ||
            !targetCategory.FormulaItemTemplateIds.SequenceEqual(formulaIds))
        {
            targetCategory.IsVisited = true;
            targetCategory.FormulaItemTemplateIds = [.. formulaIds];
            changed = true;
        }

        if (targetCategory.LastObservedAtUtc != catalog.ObservedAt)
        {
            targetCategory.LastObservedAtUtc = catalog.ObservedAt;
            changed = true;
        }

        foreach (var itemTemplateId in formulaIds)
        {
            if (!record.KnownRecipeItemTemplateIds.Contains(itemTemplateId))
            {
                record.KnownRecipeItemTemplateIds.Add(itemTemplateId);
                recordedEvents.Add(new RecipeMappingHistoryEventRecord
                {
                    CharacterId = record.CharacterId,
                    ItemTemplateId = itemTemplateId,
                    Kind = RecipeMappingHistoryEventKind.RecipeLearnedByScan,
                    ObservedAtUtc = catalog.ObservedAt,
                });
                changed = true;
            }
        }

        if (!record.BaselineObservedComplete)
        {
            var requiredBuildSkills = GetRequiredBuildSkillNames(record);
            var observedComplete = requiredBuildSkills.Count == 0 ||
                AreRequiredCategoriesComplete(
                    record,
                    catalog,
                    requiredBuildSkills);

            if (observedComplete)
            {
                record.BaselineObservedComplete = true;
                record.BaselineCompletedAtUtc = catalog.ObservedAt;
                changed = true;
            }
        }

        return changed;
    }

    private static bool AreRequiredStoredCategoriesComplete(
        RecipeMappingCharacterRecord record,
        IReadOnlySet<string> requiredBuildSkills)
    {
        var requiredCategories = record.Categories
            .Where(category =>
                RecipeMappingBuildSkillCatalog.IsCategoryRequired(
                    category.Path,
                    requiredBuildSkills))
            .ToArray();

        if (requiredCategories.Length == 0)
        {
            return false;
        }

        foreach (var skillName in requiredBuildSkills)
        {
            var skillHasCategory = requiredCategories.Any(category =>
                RecipeMappingBuildSkillCatalog
                    .GetApplicableSkillNames(category.Path)
                    .Contains(skillName, StringComparer.Ordinal));

            if (!skillHasCategory)
            {
                return false;
            }
        }

        return requiredCategories.All(category => category.IsVisited);
    }

    private static bool AreRequiredCategoriesComplete(
        RecipeMappingCharacterRecord record,
        ClientManufacturingCatalogObservation catalog,
        IReadOnlySet<string> requiredBuildSkills)
    {
        var visibleRequiredCategories = catalog.Categories
            .Where(category =>
                category.IsVisible &&
                category.CategoryId > 0 &&
                RecipeMappingBuildSkillCatalog.IsCategoryRequired(
                    category.DisplayPath,
                    requiredBuildSkills))
            .ToArray();

        if (visibleRequiredCategories.Length == 0)
        {
            return false;
        }

        // Do not silently declare a build skill complete if a future client
        // changes the Manufacturing taxonomy and our path mapping no longer
        // recognizes a category family for that skill.
        foreach (var skillName in requiredBuildSkills)
        {
            var skillHasCategory = visibleRequiredCategories.Any(category =>
                RecipeMappingBuildSkillCatalog
                    .GetApplicableSkillNames(category.DisplayPath)
                    .Contains(skillName, StringComparer.Ordinal));

            if (!skillHasCategory)
            {
                return false;
            }
        }

        return visibleRequiredCategories.All(observedCategory =>
            record.Categories.Any(category =>
                category.CategoryId == observedCategory.CategoryId &&
                category.IsVisited));
    }

    private bool ObserveCraftingActivity(
        RecipeMappingCharacterRecord record,
        ProcessRecipeMappingState processState,
        ClientManufacturingActivityObservation activity,
        ClientInventoryObservation inventory,
        List<RecipeMappingHistoryEventRecord> recordedEvents)
    {
        if (!activity.IsAvailable ||
            activity.ObservedAt == DateTimeOffset.MinValue ||
            activity.ObservedAt == processState.LastActivityObservedAt)
        {
            return false;
        }

        processState.LastActivityObservedAt = activity.ObservedAt;

        // Raw ManufacturingLab mode is shared by different native panels.
        // Manufacturing has been observed at raw mode 3, the same value used
        // by Dismantle. Resolve the semantic operation from the active panel
        // first and use raw mode only inside Analyze.
        var operationMode = ResolveOperationMode(activity);

        // Observer caches can be rebuilt across a client lifecycle transition.
        // A fresh fast-cargo baseline starts its local output sequence at zero;
        // acknowledge that reset before comparing future manufactured outputs.
        if (activity.IsManufactureOutputObservationAvailable &&
            activity.ManufactureOutputSequence <
                processState.LastManufactureOutputSequence)
        {
            processState.LastManufactureOutputSequence =
                activity.ManufactureOutputSequence;
        }

        // A manufactured cargo delta is an independent, direct result signal.
        // Consume it before changing the selected-target arm so a very fast
        // recipe switch cannot attach the new recipe's metrics to the old
        // output.
        if (activity.ManufactureOutputSequence >
                processState.LastManufactureOutputSequence &&
            activity.ManufactureOutputItemTemplateId > 0)
        {
            var outputItemTemplateId =
                activity.ManufactureOutputItemTemplateId;
            var armedMatchesOutput =
                processState.ArmedCraftingMode == ManufactureMode &&
                processState.ArmedCraftingItemTemplateId == outputItemTemplateId;
            var currentMatchesOutput =
                operationMode == ManufactureMode &&
                activity.TargetItemTemplateId == outputItemTemplateId;
            var resultValidity =
                currentMatchesOutput && IsTerminalResult(activity.Validity)
                    ? activity.Validity
                    : armedMatchesOutput &&
                      (processState.LastRecordedTerminalValidity is
                          SucceededValidity or CriticalSucceededValidity)
                        ? processState.LastRecordedTerminalValidity
                        : 0;
            var outputKind = resultValidity == CriticalSucceededValidity
                ? RecipeMappingHistoryEventKind.ManufactureCriticalSucceeded
                : RecipeMappingHistoryEventKind.ManufactureSucceeded;

            recordedEvents.Add(new RecipeMappingHistoryEventRecord
            {
                CharacterId = record.CharacterId,
                ItemTemplateId = outputItemTemplateId,
                Kind = outputKind,
                ResultValidity = resultValidity,
                ObservedAtUtc = activity.ManufactureOutputObservedAt ??
                    activity.ObservedAt,
                OutcomeQualityPercent =
                    activity.ManufactureOutputQualityPercent,
                CreditsSpent = armedMatchesOutput
                    ? processState.ArmedNegotiatedCostCredits
                    : currentMatchesOutput
                        ? ToSignedCredits(activity.NegotiatedCostCredits)
                        : null,
                SuccessProbabilityPercent = armedMatchesOutput
                    ? processState.ArmedSuccessProbabilityPercent
                    : currentMatchesOutput
                        ? activity.SuccessProbabilityPercent
                        : null,
                CriticalSuccessProbabilityPercent = armedMatchesOutput
                    ? processState.ArmedCriticalSuccessProbabilityPercent
                    : currentMatchesOutput
                        ? activity.CriticalSuccessProbabilityPercent
                        : null,
                OutputQuantity = Math.Max(
                    1,
                    activity.ManufactureOutputQuantity),
            });

            processState.LastManufactureOutputSequence =
                activity.ManufactureOutputSequence;
            if (resultValidity != 0)
            {
                processState.LastRecordedTerminalValidity = resultValidity;
            }
        }

        if (operationMode == 0)
        {
            // Do not erase a latched Analyze/Dismantle attempt because of one
            // ambiguous panel sample. The target is already gone by the time
            // the terminal result arrives, so an eager reset here makes that
            // result impossible to recover on the next sample. Two positive
            // inactive samples are enough to close a genuinely exited panel
            // without making one read hiccup destructive.
            if (processState.CraftingAttemptArmed &&
                ++processState.ConsecutiveInactiveCraftingSamples <
                    InactiveCraftingSamplesBeforeReset)
            {
                return false;
            }

            ResetCraftingAttempt(processState);
            return false;
        }

        processState.ConsecutiveInactiveCraftingSamples = 0;
        var isTerminalResult = IsTerminalResult(activity.Validity);
        if (activity.TargetItemTemplateId > 0)
        {
            var isNewAttempt =
                !processState.CraftingAttemptArmed ||
                processState.ArmedCraftingMode != operationMode ||
                processState.ArmedCraftingItemTemplateId !=
                    activity.TargetItemTemplateId;

            if (isNewAttempt)
            {
                processState.ArmedCraftingMode = operationMode;
                processState.ArmedCraftingItemTemplateId =
                    activity.TargetItemTemplateId;
                processState.CraftingAttemptArmed = true;
                processState.LastRecordedTerminalValidity = 0;
                processState.CraftingCargoBefore =
                    CaptureCraftingCargo(inventory);
                processState.ArmedNegotiatedCostCredits = null;
                processState.ArmedSuccessProbabilityPercent = null;
                processState.ArmedCriticalSuccessProbabilityPercent = null;
            }
            else if (!isTerminalResult &&
                     processState.LastRecordedTerminalValidity != 0)
            {
                // Retry of the same resident item/recipe. Refresh the
                // before-image after the terminal state has cleared.
                processState.LastRecordedTerminalValidity = 0;
                processState.CraftingCargoBefore =
                    CaptureCraftingCargo(inventory);
            }

            if (activity.NegotiatedCostCredits.HasValue)
            {
                processState.ArmedNegotiatedCostCredits =
                    ToSignedCredits(activity.NegotiatedCostCredits);
            }

            if (activity.SuccessProbabilityPercent.HasValue)
            {
                processState.ArmedSuccessProbabilityPercent =
                    activity.SuccessProbabilityPercent;
            }

            if (activity.CriticalSuccessProbabilityPercent.HasValue)
            {
                processState.ArmedCriticalSuccessProbabilityPercent =
                    activity.CriticalSuccessProbabilityPercent;
            }
        }

        if (!isTerminalResult ||
            !processState.CraftingAttemptArmed ||
            processState.ArmedCraftingMode != operationMode ||
            processState.ArmedCraftingItemTemplateId <= 0 ||
            processState.LastRecordedTerminalValidity == activity.Validity)
        {
            return false;
        }

        var itemTemplateId = processState.ArmedCraftingItemTemplateId;
        var kind = ResolveHistoryKind(operationMode, activity.Validity);
        if (!kind.HasValue)
        {
            return false;
        }

        // A successful manufacture is committed from the targeted 50 ms cargo
        // output signal, which carries the actual output quality. If that lane
        // is available, do not race it by writing a lower-fidelity terminal
        // event here. Failures still have no output and therefore come from the
        // ManufacturingLab terminal state.
        if (operationMode == ManufactureMode &&
            (activity.Validity is SucceededValidity or CriticalSucceededValidity) &&
            activity.IsManufactureOutputObservationAvailable)
        {
            processState.LastRecordedTerminalValidity = activity.Validity;
            return false;
        }

        var currentCargo = CaptureCraftingCargo(inventory);
        var cargoGains = operationMode is AnalyzeMode or DismantleMode
            ? CalculateCraftingCargoGains(
                processState.CraftingCargoBefore,
                currentCargo)
            : [];
        var resultItems = operationMode is AnalyzeMode or DismantleMode &&
                          activity.ResultComponentItemTemplateIds.Count > 0
            ? EnrichObservedResultComponents(
                BuildObservedResultComponents(
                    activity.ResultComponentItemTemplateIds),
                cargoGains)
            : cargoGains;

        var changed = false;
        if (operationMode == AnalyzeMode &&
            (activity.Validity is SucceededValidity or CriticalSucceededValidity) &&
            !record.KnownRecipeItemTemplateIds.Contains(itemTemplateId))
        {
            record.KnownRecipeItemTemplateIds.Add(itemTemplateId);
            changed = true;
        }

        recordedEvents.Add(new RecipeMappingHistoryEventRecord
        {
            CharacterId = record.CharacterId,
            ItemTemplateId = itemTemplateId,
            Kind = kind.Value,
            ResultValidity = activity.Validity,
            ObservedAtUtc = activity.ObservedAt,
            CreditsSpent = processState.ArmedNegotiatedCostCredits,
            SuccessProbabilityPercent =
                processState.ArmedSuccessProbabilityPercent,
            CriticalSuccessProbabilityPercent =
                processState.ArmedCriticalSuccessProbabilityPercent,
            ResultItems = resultItems,
        });

        processState.LastRecordedTerminalValidity = activity.Validity;
        processState.CraftingCargoBefore = currentCargo;

        if ((operationMode is AnalyzeMode or DismantleMode) &&
            (activity.Validity is SucceededValidity or CriticalSucceededValidity))
        {
            // Analyze/Dismantle consumes or clears the target. Manufacture can
            // retain the selected recipe and is keyed by output sequence, so
            // it deliberately remains armed for repeated Build clicks.
            processState.CraftingAttemptArmed = false;
        }

        return changed;
    }

    private static int ResolveOperationMode(
        ClientManufacturingActivityObservation activity)
    {
        if (activity.IsManufacturingPanelActive)
        {
            return ManufactureMode;
        }

        if (!activity.IsAnalyzePanelActive)
        {
            return 0;
        }

        return activity.Mode switch
        {
            AnalyzeMode => AnalyzeMode,
            DismantleMode => DismantleMode,
            _ => 0,
        };
    }

    private static long? ToSignedCredits(ulong? credits)
    {
        return credits is { } value && value <= (ulong)long.MaxValue
            ? checked((long)value)
            : null;
    }

    private static RecipeMappingHistoryEventKind? ResolveHistoryKind(
        int mode,
        int validity)
    {
        return (mode, validity) switch
        {
            (AnalyzeMode, FailedValidity) =>
                RecipeMappingHistoryEventKind.AnalyzeFailed,
            (AnalyzeMode, FailedDamagedValidity) =>
                RecipeMappingHistoryEventKind.AnalyzeFailedDamaged,
            (AnalyzeMode, SucceededValidity) =>
                RecipeMappingHistoryEventKind.AnalyzeSucceeded,
            (AnalyzeMode, CriticalSucceededValidity) =>
                RecipeMappingHistoryEventKind.AnalyzeCriticalSucceeded,
            (DismantleMode, FailedValidity) =>
                RecipeMappingHistoryEventKind.DismantleFailed,
            (DismantleMode, FailedDamagedValidity) =>
                RecipeMappingHistoryEventKind.DismantleFailedDamaged,
            (DismantleMode, SucceededValidity) =>
                RecipeMappingHistoryEventKind.DismantleSucceeded,
            (DismantleMode, CriticalSucceededValidity) =>
                RecipeMappingHistoryEventKind.DismantleCriticalSucceeded,
            (ManufactureMode, FailedValidity) =>
                RecipeMappingHistoryEventKind.ManufactureFailed,
            (ManufactureMode, FailedDamagedValidity) =>
                RecipeMappingHistoryEventKind.ManufactureFailedDamaged,
            (ManufactureMode, SucceededValidity) =>
                RecipeMappingHistoryEventKind.ManufactureSucceeded,
            (ManufactureMode, CriticalSucceededValidity) =>
                RecipeMappingHistoryEventKind.ManufactureCriticalSucceeded,
            _ => null,
        };
    }

    private static bool IsTerminalResult(int validity)
    {
        return validity is
            FailedValidity or
            FailedDamagedValidity or
            SucceededValidity or
            CriticalSucceededValidity;
    }

    private static void ResetCraftingAttempt(ProcessRecipeMappingState state)
    {
        state.ArmedCraftingMode = 0;
        state.ArmedCraftingItemTemplateId = 0;
        state.CraftingAttemptArmed = false;
        state.LastRecordedTerminalValidity = 0;
        state.CraftingCargoBefore = null;
        state.ConsecutiveInactiveCraftingSamples = 0;
        state.ArmedNegotiatedCostCredits = null;
        state.ArmedSuccessProbabilityPercent = null;
        state.ArmedCriticalSuccessProbabilityPercent = null;
    }

    private static CraftingCargoSnapshot? CaptureCraftingCargo(
        ClientInventoryObservation inventory)
    {
        if (!inventory.IsAvailable)
        {
            return null;
        }

        var slots = inventory.CargoSlots
            .Where(slot => slot.IsOccupied && slot.ItemTemplateId is > 0)
            .Select(slot => new CraftingCargoSlot(
                slot.Slot,
                slot.ItemTemplateId!.Value,
                Math.Max(1, slot.StackCount ?? 1),
                slot.QualityPercent))
            .ToArray();
        return new CraftingCargoSnapshot(slots);
    }

    private static IReadOnlyList<RecipeMappingHistoryItem>
        BuildObservedResultComponents(
            IReadOnlyList<int> itemTemplateIds)
    {
        return itemTemplateIds
            .Where(itemTemplateId => itemTemplateId > 0)
            .GroupBy(itemTemplateId => itemTemplateId)
            .Select(group => new RecipeMappingHistoryItem
            {
                ItemTemplateId = group.Key,
                Quantity = group.Count(),
            })
            .OrderBy(item => item.ItemTemplateId)
            .ToArray();
    }

    private static IReadOnlyList<RecipeMappingHistoryItem>
        EnrichObservedResultComponents(
            IReadOnlyList<RecipeMappingHistoryItem> observedItems,
            IReadOnlyList<RecipeMappingHistoryItem> cargoGains)
    {
        if (observedItems.Count == 0 || cargoGains.Count == 0)
        {
            return observedItems;
        }

        var cargoByItemTemplateId = cargoGains
            .ToDictionary(item => item.ItemTemplateId);
        return observedItems
            .Select(item => cargoByItemTemplateId.TryGetValue(
                    item.ItemTemplateId,
                    out var cargoGain)
                ? item with
                {
                    QualityPercent = cargoGain.QualityPercent,
                }
                : item)
            .ToArray();
    }

    private static IReadOnlyList<RecipeMappingHistoryItem>
        CalculateCraftingCargoGains(
            CraftingCargoSnapshot? before,
            CraftingCargoSnapshot? after)
    {
        if (before == null || after == null)
        {
            return [];
        }

        var beforeCounts = before.Slots
            .GroupBy(item => item.ItemTemplateId)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Quantity));
        var afterCounts = after.Slots
            .GroupBy(item => item.ItemTemplateId)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Quantity));

        return afterCounts
            .Select(pair => new
            {
                ItemTemplateId = pair.Key,
                Quantity = pair.Value - beforeCounts.GetValueOrDefault(pair.Key),
            })
            .Where(item => item.Quantity > 0)
            .Select(item => new RecipeMappingHistoryItem
            {
                ItemTemplateId = item.ItemTemplateId,
                Quantity = item.Quantity,
                QualityPercent = after.Slots
                    .Where(slot => slot.ItemTemplateId == item.ItemTemplateId)
                    .Select(slot => slot.QualityPercent)
                    .FirstOrDefault(value => value.HasValue),
            })
            .OrderBy(item => item.ItemTemplateId)
            .ToArray();
    }

    private void TrySaveCharacter(
        RecipeMappingCharacterRecord record,
        IReadOnlyList<RecipeMappingHistoryEventRecord> historyEvents)
    {
        try
        {
            this.store.SaveCharacter(record, historyEvents);
        }
        catch (Exception exception)
        {
            Debug.WriteLine(
                string.Concat(
                    "Crafting persistence failed: ",
                    exception),
                "Net7.Crafting");
        }
    }

    private void TrySaveRecipeComponents(
        uint characterId,
        ClientProductionRecipeObservation recipe,
        DateTimeOffset observedAt)
    {
        try
        {
            this.store.SaveRecipeComponents(
                characterId,
                recipe.OutputItemTemplateId,
                recipe.Ingredients,
                observedAt);
        }
        catch (Exception exception)
        {
            Debug.WriteLine(
                string.Concat(
                    "Crafting recipe-component persistence failed: ",
                    exception),
                "Net7.Crafting");
        }
    }

    public RecipeMappingRecipeDetailsPresentation GetRecipeDetails(
        uint characterId,
        int itemTemplateId)
    {
        var details = this.store.GetRecipeDetails(characterId, itemTemplateId);
        return details with
        {
            Components = details.Components
                .Select(component => component with
                {
                    Name = ClientItemTemplateNameResolver.GetKnownName(
                        component.ItemTemplateId) ??
                        string.Concat("Item ", component.ItemTemplateId),
                })
                .ToArray(),
        };
    }

    public RecipeMappingHistoryEventRecord RecordScanCompleted(
        uint characterId,
        DateTimeOffset observedAt,
        int recipeCount,
        int newRecipeCount)
    {
        var historyEvent = new RecipeMappingHistoryEventRecord
        {
            CharacterId = characterId,
            Kind = RecipeMappingHistoryEventKind.RecipeScanCompleted,
            ObservedAtUtc = observedAt,
            RecipeCount = Math.Max(0, recipeCount),
            NewRecipeCount = Math.Max(0, newRecipeCount),
        };

        try
        {
            return this.store.AppendHistoryEvent(historyEvent);
        }
        catch (Exception exception)
        {
            Debug.WriteLine(
                string.Concat("Crafting scan history persistence failed: ", exception),
                "Net7.Crafting");
            return historyEvent;
        }
    }

    private void PublishAllFastCharacterStates()
    {
        var states = this.document.Characters.ToDictionary(
            record => record.CharacterId,
            BuildFastCharacterState);
        Volatile.Write(ref this.fastCharacterStates, states);
        this.PublishMappedPilotIndex();
    }

    private void PublishFastCharacterState(
        RecipeMappingCharacterRecord record)
    {
        var current = Volatile.Read(ref this.fastCharacterStates);
        Dictionary<uint, RecipeMappingFastCharacterState> next =
            new(current)
            {
                [record.CharacterId] = BuildFastCharacterState(record),
            };
        Volatile.Write(ref this.fastCharacterStates, next);
        this.PublishMappedPilotIndex();
    }

    private void PublishMappedPilotIndex()
    {
        var index = this.document.Characters
            .Where(record => !string.IsNullOrWhiteSpace(record.PilotName))
            .SelectMany(record => record.KnownRecipeItemTemplateIds
                .Where(itemTemplateId => itemTemplateId > 0)
                .Distinct()
                .Select(itemTemplateId => new
                {
                    ItemTemplateId = itemTemplateId,
                    Pilot = new RecipeMappingMappedPilot(
                        record.CharacterId,
                        record.PilotName.Trim()),
                }))
            .GroupBy(item => item.ItemTemplateId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<RecipeMappingMappedPilot>)group
                    .Select(item => item.Pilot)
                    .GroupBy(pilot => pilot.CharacterId)
                    .Select(pilots => pilots.Last())
                    .OrderBy(pilot => pilot.PilotName, StringComparer.OrdinalIgnoreCase)
                    .ToArray());
        Volatile.Write(ref this.mappedPilotsByItemTemplateId, index);
    }

    private static RecipeMappingFastCharacterState BuildFastCharacterState(
        RecipeMappingCharacterRecord record)
    {
        return new RecipeMappingFastCharacterState(
            record.KnownRecipeItemTemplateIds
                .Where(itemTemplateId => itemTemplateId > 0)
                .ToFrozenSet(),
            record.Categories
                .Where(category => category.CategoryId > 0 && category.IsVisited)
                .Select(category => category.CategoryId)
                .ToFrozenSet());
    }

    private static RecipeMappingCharacterRecord CloneCharacter(
        RecipeMappingCharacterRecord source)
    {
        return new RecipeMappingCharacterRecord
        {
            CharacterId = source.CharacterId,
            PilotName = source.PilotName,
            BaselineObservedComplete = source.BaselineObservedComplete,
            BaselineCompletedAtUtc = source.BaselineCompletedAtUtc,
            UpdatedAtUtc = source.UpdatedAtUtc,
            BaselineInitializedBuildSkillNames =
                [.. source.BaselineInitializedBuildSkillNames],
            KnownZeroBuildSkillNames =
                [.. source.KnownZeroBuildSkillNames],
            KnownRecipeItemTemplateIds =
                [.. source.KnownRecipeItemTemplateIds],
            Categories = source.Categories
                .Select(category =>
                    new RecipeMappingCategoryRecord
                    {
                        CategoryId = category.CategoryId,
                        PrimaryIndex = category.PrimaryIndex,
                        SecondaryIndex = category.SecondaryIndex,
                        LeafIndex = category.LeafIndex,
                        Path = category.Path,
                        IsVisited = category.IsVisited,
                        LastObservedAtUtc = category.LastObservedAtUtc,
                        FormulaItemTemplateIds =
                            [.. category.FormulaItemTemplateIds],
                    })
                .ToList(),
        };
    }

    private RecipeMappingCharacterRecord GetOrCreateCharacter(
        uint characterId,
        string? pilotName)
    {
        var record = this.document.Characters.FirstOrDefault(item =>
            item.CharacterId == characterId);

        if (record != null)
        {
            return record;
        }

        record = new RecipeMappingCharacterRecord
        {
            CharacterId = characterId,
            PilotName = pilotName?.Trim() ?? "",
        };
        this.document.Characters.Add(record);
        return record;
    }

    private ProcessRecipeMappingState GetOrCreateProcessState(
        int processId,
        uint characterId)
    {
        if (this.processStates.TryGetValue(
                processId,
                out var state) &&
            state.CharacterId == characterId)
        {
            return state;
        }

        state = new ProcessRecipeMappingState(characterId);
        this.processStates[processId] = state;
        return state;
    }

    private static RecipeMappingPresentation BuildPresentation(
        RecipeMappingCharacterRecord record,
        ClientManufacturingCatalogObservation? currentCatalog,
        ClientCharacterSkillsObservation? currentSkills,
        bool includeRecipes)
    {
        var requiredBuildSkills = GetRequiredBuildSkillNames(record);
        var observedBuildSkills =
            RecipeMappingBuildSkillCatalog.ObserveBuildSkills(currentSkills);
        var ranks = observedBuildSkills.ToDictionary(
            skill => skill.Name,
            skill => skill.CurrentRank,
            StringComparer.Ordinal);

        var categories = record.Categories
            .Where(category =>
                RecipeMappingBuildSkillCatalog.IsCategoryRequired(
                    category.Path,
                    requiredBuildSkills))
            .OrderBy(category => category.PrimaryIndex)
            .ThenBy(category => category.SecondaryIndex)
            .ThenBy(category => category.LeafIndex)
            .ThenBy(category => category.CategoryId)
            .Select(category =>
                new RecipeMappingCategoryProgress
                {
                    CategoryId = category.CategoryId,
                    PrimaryIndex = category.PrimaryIndex,
                    SecondaryIndex = category.SecondaryIndex,
                    LeafIndex = category.LeafIndex,
                    Path = category.Path,
                    DisplayName = RecipeMappingBuildSkillCatalog
                        .GetShortCategoryName(category.Path),
                    BuildSkillName = RecipeMappingBuildSkillCatalog
                        .ResolveDisplaySkillName(
                            category.Path,
                            requiredBuildSkills),
                    IsVisited = category.IsVisited,
                    FormulaCount = category.FormulaItemTemplateIds.Count,
                    LastObservedAtUtc = category.LastObservedAtUtc,
                })
            .ToArray();
        var completed = categories.Count(category => category.IsVisited);

        var buildSkills = RecipeMappingBuildSkillCatalog.SkillNames
            .Where(requiredBuildSkills.Contains)
            .Select(skillName =>
            {
                var skillCategories = categories
                    .Where(category =>
                        RecipeMappingBuildSkillCatalog
                            .GetApplicableSkillNames(category.Path)
                            .Contains(skillName, StringComparer.Ordinal))
                    .ToArray();
                var skillCompleted = skillCategories.Count(category =>
                    category.IsVisited);

                return new RecipeMappingBuildSkillProgress
                {
                    Name = skillName,
                    CurrentRank = ranks.GetValueOrDefault(skillName),
                    RequiresBaseline = true,
                    IsComplete = skillCategories.Length > 0 &&
                        skillCompleted == skillCategories.Length,
                    CompletedCategoryCount = skillCompleted,
                    TotalCategoryCount = skillCategories.Length,
                };
            })
            .ToArray();

        var baselineStatus = record.BaselineObservedComplete
            ? RecipeMappingBaselineStatus.Complete
            : completed == 0
                ? RecipeMappingBaselineStatus.NotStarted
                : RecipeMappingBaselineStatus.InProgress;
        var currentCategory = currentCatalog?.CurrentCategory;
        var currentCategoryIsRequired = currentCategory != null &&
            RecipeMappingBuildSkillCatalog.IsCategoryRequired(
                currentCategory.DisplayPath,
                requiredBuildSkills);
        var nextCategory = categories.FirstOrDefault(category =>
            !category.IsVisited);
        var buildSkillDataInitialized =
            record.BaselineInitializedBuildSkillNames.Count >=
            RecipeMappingBuildSkillCatalog.SkillNames.Count;
        var status = ResolveStatus(
            baselineStatus,
            requiredBuildSkills,
            completed,
            categories.Length,
            buildSkillDataInitialized);
        var manufacturingReadyForAutomatedScan =
            currentCatalog is
            {
                IsAvailable: true,
                IsManufacturingPanelActive: true,
            } &&
            !currentCatalog.ShowingPreviousAttempts &&
            currentCatalog.PrimaryIndex != -2 &&
            currentCatalog.Categories.Any(category =>
                category.IsVisible && category.CategoryId > 0);
        var recipes = includeRecipes
            ? BuildRecipeRows(record)
            : [];

        return new RecipeMappingPresentation
        {
            IsAvailable = true,
            Status = status,
            CharacterId = record.CharacterId,
            PilotName = record.PilotName,
            BaselineStatus = baselineStatus,
            BuildSkillsAvailable = buildSkillDataInitialized,
            BuildSkillObservationStatus = currentSkills?.Status ?? "",
            RequiredBuildSkillCount = requiredBuildSkills.Count,
            KnownZeroBuildSkillCount = record.KnownZeroBuildSkillNames.Count,
            CompletedCategoryCount = completed,
            TotalCategoryCount = categories.Length,
            KnownRecipeCount = record.KnownRecipeItemTemplateIds.Count,
            CurrentCategoryId = currentCategory?.CategoryId,
            CurrentCategoryIsRequired = currentCategoryIsRequired,
            CurrentCategoryPath = currentCategory?.DisplayPath ?? "",
            CurrentCategoryDisplayName = currentCategoryIsRequired
                ? RecipeMappingBuildSkillCatalog.GetShortCategoryName(
                    currentCategory?.DisplayPath)
                : "",
            NextUnvisitedCategoryPath = nextCategory?.Path ?? "",
            NextUnvisitedCategoryDisplayName = nextCategory?.DisplayName ?? "",
            ManufacturingPanelActive =
                currentCatalog?.IsManufacturingPanelActive ?? false,
            ManufacturingCatalogAvailable =
                currentCatalog?.IsAvailable ?? false,
            ManufacturingReadyForAutomatedScan =
                manufacturingReadyForAutomatedScan,
            ManufacturingObservationStatus =
                currentCatalog?.Status ?? "",
            AllTechLevelsEnabled =
                currentCatalog?.AllTechLevelsEnabled ?? false,
            BuildSkills = buildSkills,
            Categories = categories,
            Recipes = recipes,
        };
    }

    private static IReadOnlyList<RecipeMappingRecipeRow> BuildRecipeRows(
        RecipeMappingCharacterRecord record)
    {
        var categoryByItemId = record.Categories
            .SelectMany(category => category.FormulaItemTemplateIds
                .Select(itemTemplateId => new
                {
                    ItemTemplateId = itemTemplateId,
                    Category = category,
                }))
            .Where(item => item.ItemTemplateId > 0)
            .GroupBy(item => item.ItemTemplateId)
            .ToDictionary(
                group => group.Key,
                group => group.First().Category);

        return record.KnownRecipeItemTemplateIds
            .Where(itemTemplateId => itemTemplateId > 0)
            .Distinct()
            .Select(itemTemplateId =>
            {
                categoryByItemId.TryGetValue(
                    itemTemplateId,
                    out var category);
                var template = ClientItemTemplateNameResolver
                    .GetKnownTemplate(itemTemplateId);

                // Manufacturing leaf category IDs are also exposed as the
                // item's cdata subcategory (Beam 100, Projectile 101, etc.).
                // This gives newly learned Analyze recipes their proper
                // category immediately, even though the completed baseline
                // catalogue is intentionally no longer being polled.
                var templateSubcategory = template?.Subcategory ?? 0;
                if (category == null && templateSubcategory > 0)
                {
                    category = record.Categories.FirstOrDefault(candidate =>
                        candidate.CategoryId == templateSubcategory);
                }

                var pathParts = (category?.Path ?? "")
                    .Split(
                        " / ",
                        StringSplitOptions.RemoveEmptyEntries |
                        StringSplitOptions.TrimEntries);
                var name = template?.Name;

                if (string.IsNullOrWhiteSpace(name))
                {
                    name = ClientItemTemplateNameResolver.GetKnownName(
                        itemTemplateId);
                }

                return new RecipeMappingRecipeRow
                {
                    ItemTemplateId = itemTemplateId,
                    Name = string.IsNullOrWhiteSpace(name)
                        ? string.Concat("Item ", itemTemplateId)
                        : name,
                    TechLevel = template is { TechLevel: > 0 }
                        ? checked((int)template.TechLevel)
                        : null,
                    CategoryId = category?.CategoryId,
                    PrimaryCategory = pathParts.Length > 0
                        ? pathParts[0]
                        : "",
                    SecondaryCategory = pathParts.Length > 1
                        ? pathParts[1]
                        : "",
                    Category = pathParts.Length > 2
                        ? pathParts[2]
                        : "",
                    ApplicableBuildSkillNames =
                        RecipeMappingBuildSkillCatalog.GetApplicableSkillNames(
                            category?.CategoryId ?? templateSubcategory),
                };
            })
            .OrderBy(row => row.PrimaryCategory, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.SecondaryCategory, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.TechLevel ?? int.MaxValue)
            .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string ResolveStatus(
        RecipeMappingBaselineStatus baselineStatus,
        IReadOnlySet<string> requiredBuildSkills,
        int completedCategoryCount,
        int totalCategoryCount,
        bool buildSkillDataInitialized)
    {
        if (!buildSkillDataInitialized)
        {
            return "Reading this pilot's build skills...";
        }

        return baselineStatus switch
        {
            RecipeMappingBaselineStatus.Complete =>
                "Recipe scan complete.",
            _ when requiredBuildSkills.Count == 0 =>
                "No recipe scan is needed for the currently trained build skills.",
            RecipeMappingBaselineStatus.InProgress =>
                string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"Recipe scan: {completedCategoryCount}/{totalCategoryCount} categories complete."),
            _ when requiredBuildSkills.Count == 1 =>
                string.Concat(
                    requiredBuildSkills.First(),
                    " has recipes ready to scan."),
            _ => string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{requiredBuildSkills.Count} trained build skills have recipes ready to scan."),
        };
    }

    private static HashSet<string> GetRequiredBuildSkillNames(
        RecipeMappingCharacterRecord record)
    {
        var zeroBaseline = record.KnownZeroBuildSkillNames.ToHashSet(
            StringComparer.Ordinal);

        return record.BaselineInitializedBuildSkillNames
            .Where(skillName => !zeroBaseline.Contains(skillName))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static bool IsBaselineComplete(
        RecipeMappingCharacterRecord record)
    {
        return record.BaselineObservedComplete;
    }

    private static void NormalizeDocument(
        RecipeMappingDocument document)
    {
        document.Characters ??= [];

        foreach (var record in document.Characters)
        {
            NormalizeCharacter(record);
        }

        document.Characters = document.Characters
            .Where(record => record.CharacterId is not 0 and not uint.MaxValue)
            .GroupBy(record => record.CharacterId)
            .Select(group => group.Last())
            .OrderBy(record => record.CharacterId)
            .ToList();
    }

    private static void NormalizeCharacter(
        RecipeMappingCharacterRecord record)
    {
        record.PilotName = record.PilotName?.Trim() ?? "";
        record.BaselineInitializedBuildSkillNames =
            (record.BaselineInitializedBuildSkillNames ?? [])
            .Where(skillName =>
                RecipeMappingBuildSkillCatalog.SkillNames.Contains(
                    skillName,
                    StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        record.KnownZeroBuildSkillNames =
            (record.KnownZeroBuildSkillNames ?? [])
            .Where(skillName =>
                RecipeMappingBuildSkillCatalog.SkillNames.Contains(
                    skillName,
                    StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        record.KnownRecipeItemTemplateIds = record.KnownRecipeItemTemplateIds
            .Where(itemTemplateId => itemTemplateId > 0)
            .Distinct()
            .OrderBy(itemTemplateId => itemTemplateId)
            .ToList();
        record.Categories = record.Categories
            .Where(category => category.CategoryId > 0)
            .GroupBy(category => category.CategoryId)
            .Select(group => group.Last())
            .OrderBy(category => category.CategoryId)
            .ToList();

        foreach (var category in record.Categories)
        {
            category.Path = category.Path?.Trim() ?? "";
            category.FormulaItemTemplateIds = category.FormulaItemTemplateIds
                .Where(itemTemplateId => itemTemplateId > 0)
                .Distinct()
                .OrderBy(itemTemplateId => itemTemplateId)
                .ToList();
        }
    }

    private sealed record RecipeMappingFastCharacterState(
        FrozenSet<int> KnownRecipeItemTemplateIds,
        FrozenSet<int> ScannedCategoryIds);

    private sealed record RecipeMappingMappedPilot(
        uint CharacterId,
        string PilotName);

    private sealed record CraftingCargoSlot(
        int Slot,
        int ItemTemplateId,
        int Quantity,
        float? QualityPercent);

    private sealed record CraftingCargoSnapshot(
        IReadOnlyList<CraftingCargoSlot> Slots);

    private sealed class ProcessRecipeMappingState(uint characterId)
    {
        public uint CharacterId { get; } = characterId;

        public ClientCharacterSkillsObservation? LastBuildSkillObservation
        { get; set; }

        public DateTimeOffset LastCatalogObservedAt { get; set; }

        public int BaselineCaptureCategoryId { get; set; }

        public int PendingCategoryId { get; set; }

        public string PendingCategoryFingerprint { get; set; } = "";

        public int PendingCategoryStableCount { get; set; }

        public DateTimeOffset LastActivityObservedAt { get; set; }

        public string LastProductionRecipeFingerprint { get; set; } = "";

        public int ArmedCraftingMode { get; set; }

        public int ArmedCraftingItemTemplateId { get; set; }

        public bool CraftingAttemptArmed { get; set; }

        public int LastRecordedTerminalValidity { get; set; }

        public CraftingCargoSnapshot? CraftingCargoBefore { get; set; }

        public int ConsecutiveInactiveCraftingSamples { get; set; }

        public long? ArmedNegotiatedCostCredits { get; set; }

        public float? ArmedSuccessProbabilityPercent { get; set; }

        public float? ArmedCriticalSuccessProbabilityPercent { get; set; }

        public long LastManufactureOutputSequence { get; set; }
    }
}
