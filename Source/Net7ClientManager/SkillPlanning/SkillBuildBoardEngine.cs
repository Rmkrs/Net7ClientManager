namespace Net7ClientManager.SkillPlanning;

using System.Globalization;

internal sealed class SkillBuildBoardEngine
{
    private readonly SkillPlannerCatalog skills;
    private readonly SkillBuildHullCatalog hulls;
    private readonly SkillBuildEquipmentCatalog equipment;

    public SkillBuildBoardEngine(
        SkillPlannerCatalog skills,
        SkillBuildHullCatalog hulls,
        SkillBuildEquipmentCatalog equipment)
    {
        this.skills = skills ?? throw new ArgumentNullException(nameof(skills));
        this.hulls = hulls ?? throw new ArgumentNullException(nameof(hulls));
        this.equipment = equipment ?? throw new ArgumentNullException(nameof(equipment));
    }

    public SkillBuildAnalysis Analyze(
        SkillBuildDocument build,
        SkillPlannerCharacterBaseline? baseline,
        SkillBuildEquipmentBaseline? equipped)
    {
        ArgumentNullException.ThrowIfNull(build);

        if (!this.skills.TryGetProfession(
                build.ProfessionIndex,
                out var profession))
        {
            throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Unknown build profession index {build.ProfessionIndex}."));
        }

        List<string> issues = [];
        if (baseline != null && baseline.ProfessionIndex != build.ProfessionIndex)
        {
            issues.Add("This build belongs to a different profession.");
            baseline = null;
            equipped = null;
        }

        var weaponCount = build.Equipment.Count(value =>
            value.Kind == SkillBuildEquipmentKind.Weapon);
        var deviceCount = build.Equipment.Count(value =>
            value.Kind == SkillBuildEquipmentKind.Device);
        var hasSupportedEquipmentSlots = this.hulls.TryResolveForSlots(
            build.ProfessionIndex,
            weaponCount,
            deviceCount,
            out var requiredHull);
        if (!hasSupportedEquipmentSlots)
        {
            issues.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"This profession supports at most {requiredHull.WeaponSlots} weapons and {requiredHull.DeviceSlots} devices."));
        }

        var equipmentAnalyses = this.MatchEquipment(
            build.Equipment,
            equipped);
        var genericChoiceByRequirement = build.Equipment.ToDictionary(
            value => value.RequirementId,
            value => value.Alternatives.FirstOrDefault(),
            StringComparer.Ordinal);
        var effectiveChoiceByRequirement = equipmentAnalyses.ToDictionary(
            value => value.Requirement.RequirementId,
            value => value.EffectiveChoice,
            StringComparer.Ordinal);

        var genericState = this.BuildTargetState(
            build,
            genericChoiceByRequirement,
            issues);
        var effectiveState = baseline == null
            ? genericState
            : this.BuildTargetState(
                build,
                effectiveChoiceByRequirement,
                issues);

        var genericLevelCalculation = this.CalculateGenericLevels(
            build.ProfessionIndex,
            genericState,
            baseline,
            requiredHull,
            weaponCount,
            deviceCount);
        var effectiveLevelCalculation = baseline == null
            ? genericLevelCalculation
            : this.CalculateLiveLevels(
                baseline,
                effectiveState,
                requiredHull,
                weaponCount,
                deviceCount);

        var recommendations = build.RecommendedSkills
            .GroupBy(value => value.SkillId)
            .ToDictionary(
                group => group.Key,
                group => group.Max(value => Math.Max(0, value.TargetRank)));
        List<SkillBuildSkillAnalysis> skillAnalyses = [];

        foreach (var skill in this.skills.Skills
                     .Where(value => value.TryGetProfessionRule(
                         build.ProfessionIndex,
                         out _))
                     .OrderBy(value => value.Name,
                         StringComparer.CurrentCultureIgnoreCase)
                     .ThenBy(value => value.Id))
        {
            _ = skill.TryGetProfessionRule(
                build.ProfessionIndex,
                out var rule);
            var startingRank = Math.Max(0, skill.MinimumRank);
            var equipmentRank = genericState.EquipmentSkillRanks
                .GetValueOrDefault(skill.Id, startingRank);
            var recommendedRank = recommendations.GetValueOrDefault(
                skill.Id,
                startingRank);
            var effectiveEquipmentRank = effectiveState.EquipmentSkillRanks
                .GetValueOrDefault(skill.Id, startingRank);
            var targetRank = Math.Clamp(
                Math.Max(effectiveEquipmentRank, recommendedRank),
                startingRank,
                rule.MaximumRank);
            int? currentRank = null;
            if (baseline != null)
            {
                currentRank = baseline.Skills.TryGetValue(
                        skill.Id,
                        out var owned)
                    ? Math.Max(startingRank, owned.CurrentRank)
                    : startingRank;
            }

            skillAnalyses.Add(
                new SkillBuildSkillAnalysis
                {
                    Skill = skill,
                    StartingRank = startingRank,
                    EquipmentMinimumRank = equipmentRank,
                    RecommendedRank = Math.Clamp(
                        recommendedRank,
                        startingRank,
                        rule.MaximumRank),
                    TargetRank = targetRank,
                    CurrentRank = currentRank,
                    MaximumRank = rule.MaximumRank,
                    SkillPointCost = this.CalculatePaidCost(
                        build.ProfessionIndex,
                        skill.Id,
                        targetRank,
                        baseline),
                });
        }

        var genericPoints = this.CalculateTotalPaidCost(
            build.ProfessionIndex,
            genericState.TargetSkillRanks,
            baseline);
        var effectivePoints = this.CalculateTotalPaidCost(
            build.ProfessionIndex,
            effectiveState.TargetSkillRanks,
            baseline);
        var remainingCost = baseline == null
            ? effectivePoints
            : this.CalculateRemainingPaidCost(
                baseline,
                effectiveState.TargetSkillRanks);

        return new SkillBuildAnalysis
        {
            Build = build,
            Profession = profession,
            GenericLevels = genericLevelCalculation.Levels,
            EffectiveLevels = effectiveLevelCalculation.Levels,
            RequiredHull = requiredHull,
            HasSupportedEquipmentSlots = hasSupportedEquipmentSlots,
            GenericRequiredSkillPoints = genericPoints,
            EffectiveRequiredSkillPoints = effectivePoints,
            RemainingSkillPointCost = remainingCost,
            AvailableSkillPoints = baseline?.AvailableSkillPoints ?? 0,
            GenericReasons = genericLevelCalculation.Reasons,
            EffectiveReasons = effectiveLevelCalculation.Reasons,
            Skills = skillAnalyses,
            Equipment = equipmentAnalyses,
            Issues = issues.Distinct(StringComparer.Ordinal).ToArray(),
        };
    }

    private TargetState BuildTargetState(
        SkillBuildDocument build,
        IReadOnlyDictionary<string, SkillBuildEquipmentAlternative?> choiceByRequirement,
        ICollection<string> issues)
    {
        Dictionary<int, int> equipmentRanks = [];
        List<RequirementCandidate> requirements = [];
        var combat = 0;
        var explore = 0;
        var trade = 0;
        var overall = 0;

        foreach (var requirement in build.Equipment)
        {
            if (!choiceByRequirement.TryGetValue(
                    requirement.RequirementId,
                    out var alternative) ||
                alternative == null)
            {
                issues.Add("One equipment position has no accepted equipment.");
                continue;
            }

            var choice = this.equipment.Find(alternative.ItemTemplateId);
            if (choice == null)
            {
                issues.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{FormatItem(alternative)} is not present in the local equipment catalogue."));
                continue;
            }

            if (!this.equipment.IsCompatible(choice, build.ProfessionIndex))
            {
                issues.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{FormatItem(alternative)} cannot be equipped by this profession."));
                continue;
            }

            combat = Math.Max(combat, choice.RequiredCombatLevel);
            explore = Math.Max(explore, choice.RequiredExploreLevel);
            trade = Math.Max(trade, choice.RequiredTradeLevel);
            overall = Math.Max(overall, choice.RequiredOverallLevel);
            AddRequirementCandidate(
                requirements,
                SkillPlannerLevelTrack.Combat,
                choice.RequiredCombatLevel,
                string.Create(
                    CultureInfo.CurrentCulture,
                    $"{choice.Name} requires Combat {choice.RequiredCombatLevel}."));
            AddRequirementCandidate(
                requirements,
                SkillPlannerLevelTrack.Explore,
                choice.RequiredExploreLevel,
                string.Create(
                    CultureInfo.CurrentCulture,
                    $"{choice.Name} requires Explore {choice.RequiredExploreLevel}."));
            AddRequirementCandidate(
                requirements,
                SkillPlannerLevelTrack.Trade,
                choice.RequiredTradeLevel,
                string.Create(
                    CultureInfo.CurrentCulture,
                    $"{choice.Name} requires Trade {choice.RequiredTradeLevel}."));
            AddRequirementCandidate(
                requirements,
                SkillPlannerLevelTrack.Overall,
                choice.RequiredOverallLevel,
                string.Create(
                    CultureInfo.CurrentCulture,
                    $"{choice.Name} requires Overall {choice.RequiredOverallLevel}."));

            if (choice.RequiredSkillId is not { } skillId)
            {
                continue;
            }

            if (!this.skills.TryGetSkill(skillId, out var skill) ||
                !skill.TryGetProfessionRule(
                    build.ProfessionIndex,
                    out var rule))
            {
                issues.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{FormatItem(alternative)} requires a skill unavailable to this profession."));
                continue;
            }

            var requiredRank = Math.Max(
                skill.MinimumRank,
                choice.RequiredSkillRank);
            if (requiredRank > rule.MaximumRank)
            {
                issues.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{FormatItem(alternative)} requires {skill.Name} {requiredRank}, which this profession cannot train."));
                continue;
            }

            equipmentRanks[skillId] = Math.Max(
                equipmentRanks.GetValueOrDefault(skillId, skill.MinimumRank),
                requiredRank);
        }

        Dictionary<int, int> targets = [];
        foreach (var skill in this.skills.Skills)
        {
            if (!skill.TryGetProfessionRule(
                    build.ProfessionIndex,
                    out var rule))
            {
                continue;
            }

            var equipmentRank = equipmentRanks.GetValueOrDefault(
                skill.Id,
                skill.MinimumRank);
            var recommendation = build.RecommendedSkills
                .Where(value => value.SkillId == skill.Id)
                .Select(value => value.TargetRank)
                .DefaultIfEmpty(skill.MinimumRank)
                .Max();
            var target = Math.Clamp(
                Math.Max(equipmentRank, recommendation),
                skill.MinimumRank,
                rule.MaximumRank);
            targets[skill.Id] = target;

            var requirement = ResolveSkillLevelRequirement(
                skill,
                rule,
                target);
            AddRequirementCandidate(
                requirements,
                requirement.Track,
                requirement.Level,
                string.Create(
                    CultureInfo.CurrentCulture,
                    $"{skill.Name} rank {target} requires {FormatLevelTrack(requirement.Track)} {requirement.Level}."));
            switch (requirement.Track)
            {
                case SkillPlannerLevelTrack.Combat:
                    combat = Math.Max(combat, requirement.Level);
                    break;
                case SkillPlannerLevelTrack.Explore:
                    explore = Math.Max(explore, requirement.Level);
                    break;
                case SkillPlannerLevelTrack.Trade:
                    trade = Math.Max(trade, requirement.Level);
                    break;
                case SkillPlannerLevelTrack.Overall:
                    overall = Math.Max(overall, requirement.Level);
                    break;
            }
        }

        return new TargetState(
            equipmentRanks,
            targets,
            combat,
            explore,
            trade,
            overall,
            requirements);
    }

    private LevelCalculation CalculateGenericLevels(
        int professionIndex,
        TargetState state,
        SkillPlannerCharacterBaseline? skillMetadata,
        SkillBuildHullDefinition requiredHull,
        int weaponCount,
        int deviceCount)
    {
        var hullOverall = requiredHull.RequiredOverallLevel;
        var minimumTrackTotal = checked(
            state.CombatLevel +
            state.ExploreLevel +
            state.TradeLevel);
        var requiredSkillPoints = this.CalculateTotalPaidCost(
            professionIndex,
            state.TargetSkillRanks,
            skillMetadata);
        var skillPointOverall = FindMinimumOverallForSkillPoints(
            state.CombatLevel,
            state.ExploreLevel,
            state.TradeLevel,
            requiredSkillPoints,
            currentCombat: 0,
            currentExplore: 0,
            currentTrade: 0,
            availableSkillPoints: 0);

        var levels = new SkillBuildLevelTarget
        {
            Combat = state.CombatLevel,
            Explore = state.ExploreLevel,
            Trade = state.TradeLevel,
            Overall = new[]
            {
                state.OverallLevel,
                hullOverall,
                minimumTrackTotal,
                skillPointOverall,
            }.Max(),
        };

        return new LevelCalculation(
            levels,
            BuildRequirementReasons(
                state,
                requiredHull,
                weaponCount,
                deviceCount,
                levels,
                skillPointOverall,
                requiredSkillPoints,
                availableSkillPoints: null,
                remainingSkillPointCost: requiredSkillPoints));
    }

    private LevelCalculation CalculateLiveLevels(
        SkillPlannerCharacterBaseline baseline,
        TargetState state,
        SkillBuildHullDefinition requiredHull,
        int weaponCount,
        int deviceCount)
    {
        var hullOverall = requiredHull.RequiredOverallLevel;
        var remainingCost = this.CalculateRemainingPaidCost(
            baseline,
            state.TargetSkillRanks);
        var skillPointOverall = FindMinimumOverallForSkillPoints(
            Math.Max(baseline.CombatLevel, state.CombatLevel),
            Math.Max(baseline.ExploreLevel, state.ExploreLevel),
            Math.Max(baseline.TradeLevel, state.TradeLevel),
            remainingCost,
            baseline.CombatLevel,
            baseline.ExploreLevel,
            baseline.TradeLevel,
            baseline.AvailableSkillPoints);
        var minimumTrackTotal = checked(
            state.CombatLevel +
            state.ExploreLevel +
            state.TradeLevel);

        var levels = new SkillBuildLevelTarget
        {
            Combat = state.CombatLevel,
            Explore = state.ExploreLevel,
            Trade = state.TradeLevel,
            Overall = new[]
            {
                state.OverallLevel,
                hullOverall,
                minimumTrackTotal,
                skillPointOverall,
            }.Max(),
            CurrentCombat = baseline.CombatLevel,
            CurrentExplore = baseline.ExploreLevel,
            CurrentTrade = baseline.TradeLevel,
            CurrentOverall = baseline.OverallLevel,
        };

        return new LevelCalculation(
            levels,
            BuildRequirementReasons(
                state,
                requiredHull,
                weaponCount,
                deviceCount,
                levels,
                skillPointOverall,
                requiredTotalSkillPoints: this.CalculateTotalPaidCost(
                    baseline.ProfessionIndex,
                    state.TargetSkillRanks,
                    baseline),
                availableSkillPoints: baseline.AvailableSkillPoints,
                remainingSkillPointCost: remainingCost));
    }

    private SkillBuildRequirementReasons BuildRequirementReasons(
        TargetState state,
        SkillBuildHullDefinition requiredHull,
        int weaponCount,
        int deviceCount,
        SkillBuildLevelTarget levels,
        int skillPointOverall,
        int requiredTotalSkillPoints,
        int? availableSkillPoints,
        int remainingSkillPointCost)
    {
        var combat = GetTrackReasons(
            state,
            SkillPlannerLevelTrack.Combat,
            levels.Combat);
        var explore = GetTrackReasons(
            state,
            SkillPlannerLevelTrack.Explore,
            levels.Explore);
        var trade = GetTrackReasons(
            state,
            SkillPlannerLevelTrack.Trade,
            levels.Trade);

        List<string> hull = [];
        if (weaponCount > 0 && this.hulls.TryResolveForSlots(
                requiredHull.ProfessionIndex,
                weaponCount,
                deviceCount: 0,
                out var weaponHull) &&
            weaponHull.Tier == requiredHull.Tier)
        {
            hull.Add(string.Create(
                CultureInfo.CurrentCulture,
                $"{weaponCount} {(weaponCount == 1 ? "weapon" : "weapons")} require Hull {weaponHull.Tier}."));
        }
        if (deviceCount > 0 && this.hulls.TryResolveForSlots(
                requiredHull.ProfessionIndex,
                weaponCount: 0,
                deviceCount,
                out var deviceHull) &&
            deviceHull.Tier == requiredHull.Tier)
        {
            hull.Add(string.Create(
                CultureInfo.CurrentCulture,
                $"{deviceCount} {(deviceCount == 1 ? "device" : "devices")} require Hull {deviceHull.Tier}."));
        }
        if (requiredHull.RequiredOverallLevel > 0)
        {
            hull.Add(string.Create(
                CultureInfo.CurrentCulture,
                $"Hull {requiredHull.Tier} unlocks at Overall {requiredHull.RequiredOverallLevel}."));
        }

        var minimumTrackTotal = checked(
            levels.Combat +
            levels.Explore +
            levels.Trade);
        List<string> overall = [.. GetTrackReasons(
            state,
            SkillPlannerLevelTrack.Overall,
            levels.Overall)];
        if (requiredHull.RequiredOverallLevel == levels.Overall &&
            requiredHull.RequiredOverallLevel > 0)
        {
            overall.Add(string.Create(
                CultureInfo.CurrentCulture,
                $"Hull {requiredHull.Tier} requires Overall {requiredHull.RequiredOverallLevel}."));
        }
        if (minimumTrackTotal == levels.Overall && minimumTrackTotal > 0)
        {
            overall.Add(string.Create(
                CultureInfo.CurrentCulture,
                $"Combat {levels.Combat} + Explore {levels.Explore} + Trade {levels.Trade} total Overall {minimumTrackTotal}."));
        }

        var deficit = Math.Max(
            0,
            remainingSkillPointCost - Math.Max(0, availableSkillPoints ?? 0));
        if (skillPointOverall == levels.Overall && deficit > 0)
        {
            overall.Add(string.Create(
                CultureInfo.CurrentCulture,
                $"The {deficit}-point skill deficit raises the Overall target to {FormatOverallTarget(skillPointOverall)}."));
        }

        List<string> skillPoints = [];
        if (!availableSkillPoints.HasValue)
        {
            skillPoints.Add(string.Create(
                CultureInfo.CurrentCulture,
                $"The target skill ranks cost {requiredTotalSkillPoints} skill points."));
        }
        else
        {
            skillPoints.Add(string.Create(
                CultureInfo.CurrentCulture,
                $"The remaining skill ranks cost {remainingSkillPointCost} skill points."));
            skillPoints.Add(string.Create(
                CultureInfo.CurrentCulture,
                $"This character has {availableSkillPoints.Value} {(availableSkillPoints.Value == 1 ? "skill point" : "skill points")} available now."));
            if (deficit > 0)
            {
                skillPoints.Add(string.Create(
                    CultureInfo.CurrentCulture,
                    $"The remaining {deficit} points are included in the Overall target of {FormatOverallTarget(levels.Overall)}."));
            }
        }

        return new SkillBuildRequirementReasons
        {
            Overall = DistinctReasons(overall),
            Hull = DistinctReasons(hull),
            Combat = DistinctReasons(combat),
            Explore = DistinctReasons(explore),
            Trade = DistinctReasons(trade),
            SkillPoints = DistinctReasons(skillPoints),
        };
    }

    private static string FormatOverallTarget(int overall) =>
        overall <= 150
            ? overall.ToString(CultureInfo.CurrentCulture)
            : string.Create(
                CultureInfo.CurrentCulture,
                $"150 + {overall - 150}");

    private static IReadOnlyList<string> GetTrackReasons(
        TargetState state,
        SkillPlannerLevelTrack track,
        int target)
    {
        if (target <= 0)
        {
            return [];
        }

        return DistinctReasons(
            state.Requirements
                .Where(value =>
                    value.Track == track &&
                    value.Level == target)
                .Select(value => value.Reason));
    }

    private static IReadOnlyList<string> DistinctReasons(
        IEnumerable<string> reasons) =>
        reasons
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.CurrentCulture)
            .ToArray();

    private static void AddRequirementCandidate(
        ICollection<RequirementCandidate> requirements,
        SkillPlannerLevelTrack track,
        int level,
        string reason)
    {
        if (level <= 0)
        {
            return;
        }

        requirements.Add(new RequirementCandidate(track, level, reason));
    }

    private static string FormatLevelTrack(
        SkillPlannerLevelTrack track) =>
        track switch
        {
            SkillPlannerLevelTrack.Combat => "Combat",
            SkillPlannerLevelTrack.Explore => "Explore",
            SkillPlannerLevelTrack.Trade => "Trade",
            SkillPlannerLevelTrack.Overall => "Overall",
            _ => "Level",
        };

    private IReadOnlyList<SkillBuildEquipmentRequirementAnalysis> MatchEquipment(
        IReadOnlyList<SkillBuildEquipmentRequirement> requirements,
        SkillBuildEquipmentBaseline? baseline)
    {
        Dictionary<string, MatchedEquipment> matched =
            new(StringComparer.Ordinal);

        if (baseline?.IsAvailable == true)
        {
            foreach (var kind in new[]
                     {
                         SkillBuildEquipmentKind.Weapon,
                         SkillBuildEquipmentKind.Device,
                     })
            {
                var kindRequirements = requirements
                    .Where(value => value.Kind == kind)
                    .OrderBy(value => value.Order)
                    .ToArray();
                var actual = baseline.Items
                    .Where(value => value.Key.Kind == kind)
                    .OrderBy(value => value.Key.Ordinal)
                    .Select(value => value.Value)
                    .ToArray();
                MatchUnordered(
                    kindRequirements,
                    actual,
                    matched,
                    SkillBuildEquipmentAvailability.Equipped);
            }

            foreach (var kind in new[]
                     {
                         SkillBuildEquipmentKind.Shield,
                         SkillBuildEquipmentKind.Reactor,
                         SkillBuildEquipmentKind.Engine,
                     })
            {
                var requirement = requirements.FirstOrDefault(
                    value => value.Kind == kind);
                if (requirement == null)
                {
                    continue;
                }

                var slot = kind switch
                {
                    SkillBuildEquipmentKind.Shield => SkillBuildEquipmentSlot.Shield,
                    SkillBuildEquipmentKind.Reactor => SkillBuildEquipmentSlot.Reactor,
                    _ => SkillBuildEquipmentSlot.Engine,
                };
                if (baseline.Items.TryGetValue(slot, out var actual) &&
                    TryResolveAlternative(requirement, actual.ItemTemplateId, out var alternative))
                {
                    matched[requirement.RequirementId] =
                        new MatchedEquipment(
                            actual,
                            alternative,
                            SkillBuildEquipmentAvailability.Equipped);
                }
            }

            MatchOwned(
                requirements,
                baseline.InventoryItemCounts,
                matched,
                SkillBuildEquipmentAvailability.Inventory);
            MatchOwned(
                requirements,
                baseline.VaultItemCounts,
                matched,
                SkillBuildEquipmentAvailability.Vault);
        }

        return requirements
            .OrderBy(value => GetEquipmentKindOrder(value.Kind))
            .ThenBy(value => value.Order)
            .Select(requirement =>
            {
                matched.TryGetValue(
                    requirement.RequirementId,
                    out var value);
                var preferred = requirement.Alternatives.FirstOrDefault();
                return new SkillBuildEquipmentRequirementAnalysis
                {
                    Requirement = requirement,
                    EffectiveChoice = value?.Alternative ?? preferred,
                    EquippedChoice = value?.Availability ==
                        SkillBuildEquipmentAvailability.Equipped
                            ? value.Alternative
                            : null,
                    EquippedItemTemplateId = value?.Availability ==
                        SkillBuildEquipmentAvailability.Equipped
                            ? value.Item?.ItemTemplateId
                            : null,
                    EquippedItemName = value?.Availability ==
                        SkillBuildEquipmentAvailability.Equipped
                            ? value.Item?.ItemName ?? ""
                            : "",
                    IsEquipped = value?.Availability ==
                        SkillBuildEquipmentAvailability.Equipped,
                    Availability = value?.Availability ??
                        SkillBuildEquipmentAvailability.Missing,
                };
            })
            .ToArray();
    }

    private static void MatchOwned(
        IReadOnlyList<SkillBuildEquipmentRequirement> requirements,
        IReadOnlyDictionary<int, int> counts,
        IDictionary<string, MatchedEquipment> result,
        SkillBuildEquipmentAvailability availability)
    {
        var unmatched = requirements
            .Where(value => !result.ContainsKey(value.RequirementId))
            .OrderBy(value => GetEquipmentKindOrder(value.Kind))
            .ThenBy(value => value.Order)
            .ToArray();
        if (unmatched.Length == 0 || counts.Count == 0)
        {
            return;
        }

        var actual = counts
            .Where(value => value.Key > 0 && value.Value > 0)
            .OrderBy(value => value.Key)
            .SelectMany(value => Enumerable.Range(0, value.Value)
                .Select(_ => new SkillBuildEquippedItem(value.Key, "")))
            .ToArray();
        MatchUnordered(unmatched, actual, result, availability);
    }

    private static void MatchUnordered(
        IReadOnlyList<SkillBuildEquipmentRequirement> requirements,
        IReadOnlyList<SkillBuildEquippedItem> actual,
        IDictionary<string, MatchedEquipment> result,
        SkillBuildEquipmentAvailability availability)
    {
        var itemToRequirement = Enumerable.Repeat(-1, actual.Count).ToArray();

        for (var requirementIndex = 0;
             requirementIndex < requirements.Count;
             requirementIndex++)
        {
            var visited = new bool[actual.Count];
            _ = TryAssign(
                requirementIndex,
                requirements,
                actual,
                itemToRequirement,
                visited);
        }

        for (var itemIndex = 0; itemIndex < actual.Count; itemIndex++)
        {
            var requirementIndex = itemToRequirement[itemIndex];
            if (requirementIndex < 0)
            {
                continue;
            }

            var requirement = requirements[requirementIndex];
            if (TryResolveAlternative(
                    requirement,
                    actual[itemIndex].ItemTemplateId,
                    out var alternative))
            {
                result[requirement.RequirementId] =
                    new MatchedEquipment(
                        actual[itemIndex],
                        alternative,
                        availability);
            }
        }
    }

    private static bool TryAssign(
        int requirementIndex,
        IReadOnlyList<SkillBuildEquipmentRequirement> requirements,
        IReadOnlyList<SkillBuildEquippedItem> actual,
        int[] itemToRequirement,
        bool[] visited)
    {
        for (var itemIndex = 0; itemIndex < actual.Count; itemIndex++)
        {
            if (visited[itemIndex] ||
                !requirements[requirementIndex].Alternatives.Any(
                    value => value.ItemTemplateId == actual[itemIndex].ItemTemplateId))
            {
                continue;
            }

            visited[itemIndex] = true;
            if (itemToRequirement[itemIndex] < 0 ||
                TryAssign(
                    itemToRequirement[itemIndex],
                    requirements,
                    actual,
                    itemToRequirement,
                    visited))
            {
                itemToRequirement[itemIndex] = requirementIndex;
                return true;
            }
        }

        return false;
    }

    private static SkillPlannerLevelRequirement ResolveSkillLevelRequirement(
        SkillPlannerSkillDefinition skill,
        SkillPlannerProfessionSkillDefinition rule,
        int targetRank)
    {
        var track = SkillPlannerRules.ResolveRequirementTrack(skill.Category);
        var level = SkillPlannerRules.ResolveRankRequirementLevel(
            rule.RequirementCurve,
            targetRank);

        if (skill.MinimumRank == 0 &&
            targetRank > 0 &&
            rule.LearnLevel > level)
        {
            return new SkillPlannerLevelRequirement(
                SkillPlannerLevelTrack.Overall,
                rule.LearnLevel);
        }

        return new SkillPlannerLevelRequirement(track, Math.Max(0, level));
    }

    private static int FindMinimumOverallForSkillPoints(
        int minimumCombat,
        int minimumExplore,
        int minimumTrade,
        int requiredFutureSkillPoints,
        int currentCombat,
        int currentExplore,
        int currentTrade,
        int availableSkillPoints)
    {
        var requiredFromLevels = Math.Max(
            0,
            requiredFutureSkillPoints - Math.Max(0, availableSkillPoints));
        var currentEarned = SkillPlannerRules.CalculateEarnedSkillPoints(
            currentCombat,
            currentExplore,
            currentTrade);
        var best = int.MaxValue;

        for (var combat = Math.Clamp(minimumCombat, 0, 50);
             combat <= 50;
             combat++)
        {
            for (var explore = Math.Clamp(minimumExplore, 0, 50);
                 explore <= 50;
                 explore++)
            {
                var partial = combat + explore;
                if (partial >= best)
                {
                    break;
                }

                for (var trade = Math.Clamp(minimumTrade, 0, 50);
                     trade <= 50;
                     trade++)
                {
                    var overall = partial + trade;
                    if (overall >= best)
                    {
                        break;
                    }

                    var futureEarned = Math.Max(
                        0,
                        SkillPlannerRules.CalculateEarnedSkillPoints(
                            combat,
                            explore,
                            trade) - currentEarned);
                    if (futureEarned >= requiredFromLevels)
                    {
                        best = overall;
                        break;
                    }
                }
            }
        }

        if (best != int.MaxValue)
        {
            return best;
        }

        var earnedAtCap = Math.Max(
            0,
            SkillPlannerRules.CalculateEarnedSkillPoints(50, 50, 50) -
            currentEarned);
        var postCapLevels = Math.Max(
            0,
            requiredFromLevels - earnedAtCap);
        return checked(150 + postCapLevels);
    }

    private int CalculateTotalPaidCost(
        int professionIndex,
        IReadOnlyDictionary<int, int> targetRanks,
        SkillPlannerCharacterBaseline? skillMetadata)
    {
        var total = 0;
        foreach (var pair in targetRanks)
        {
            total = checked(
                total +
                this.CalculatePaidCost(
                    professionIndex,
                    pair.Key,
                    pair.Value,
                    skillMetadata));
        }

        return total;
    }

    private int CalculateRemainingPaidCost(
        SkillPlannerCharacterBaseline baseline,
        IReadOnlyDictionary<int, int> targetRanks)
    {
        var total = 0;
        foreach (var pair in targetRanks)
        {
            var current = baseline.Skills.TryGetValue(pair.Key, out var owned)
                ? Math.Max(0, owned.CurrentRank)
                : 0;
            total = checked(
                total +
                Math.Max(
                    0,
                    this.CalculatePaidCost(
                        baseline.ProfessionIndex,
                        pair.Key,
                        pair.Value,
                        baseline) -
                    this.CalculatePaidCost(
                        baseline.ProfessionIndex,
                        pair.Key,
                        current,
                        baseline)));
        }

        return total;
    }

    private int CalculatePaidCost(
        int professionIndex,
        int skillId,
        int rank,
        SkillPlannerCharacterBaseline? skillMetadata)
    {
        _ = professionIndex;
        _ = skillId;
        _ = skillMetadata;
        return SkillPlannerRules.CalculatePaidSkillPointsToRank(
            Math.Max(0, rank));
    }

    private static bool TryResolveAlternative(
        SkillBuildEquipmentRequirement requirement,
        int itemTemplateId,
        out SkillBuildEquipmentAlternative alternative)
    {
        alternative = requirement.Alternatives.FirstOrDefault(
            value => value.ItemTemplateId == itemTemplateId)!;
        return alternative != null;
    }

    private static string FormatItem(
        SkillBuildEquipmentAlternative alternative) =>
        string.IsNullOrWhiteSpace(alternative.ItemName)
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"Item #{alternative.ItemTemplateId}")
            : alternative.ItemName;

    private static int GetEquipmentKindOrder(
        SkillBuildEquipmentKind kind) =>
        kind switch
        {
            SkillBuildEquipmentKind.Weapon => 0,
            SkillBuildEquipmentKind.Shield => 1,
            SkillBuildEquipmentKind.Reactor => 2,
            SkillBuildEquipmentKind.Engine => 3,
            SkillBuildEquipmentKind.Device => 4,
            _ => int.MaxValue,
        };

    private sealed record TargetState(
        IReadOnlyDictionary<int, int> EquipmentSkillRanks,
        IReadOnlyDictionary<int, int> TargetSkillRanks,
        int CombatLevel,
        int ExploreLevel,
        int TradeLevel,
        int OverallLevel,
        IReadOnlyList<RequirementCandidate> Requirements);

    private sealed record RequirementCandidate(
        SkillPlannerLevelTrack Track,
        int Level,
        string Reason);

    private sealed record LevelCalculation(
        SkillBuildLevelTarget Levels,
        SkillBuildRequirementReasons Reasons);

    private sealed record MatchedEquipment(
        SkillBuildEquippedItem? Item,
        SkillBuildEquipmentAlternative Alternative,
        SkillBuildEquipmentAvailability Availability);
}
