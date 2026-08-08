namespace Net7ClientManager.SkillPlanning;

#if DEBUG
internal static class SkillBuildBoardScenarios
{
    public static void Validate(
        SkillPlannerCatalog skills,
        SkillBuildHullCatalog hulls)
    {
        var equipmentCatalog = new SkillBuildEquipmentCatalog(skills);
        var engine = new SkillBuildBoardEngine(
            skills,
            hulls,
            equipmentCatalog);

        ValidateLevelZeroSkillPoints();
        ValidateCanonicalStartingSkills(skills);
        ValidateEquipmentClassification();
        ValidateEquipmentRestrictions(equipmentCatalog);
        ValidateHullSlotLimits(hulls);
        ValidateSkillPointAwareOverall(engine, equipmentCatalog);
        ValidateAvailablePointsLowerLiveOverall(engine);
        ValidateRemainingRankCosts(engine, skills);
        ValidatePostCapSkillPointOverall(engine, skills);
        ValidateRequirementReasons(
            skills,
            engine,
            equipmentCatalog,
            hulls);
        ValidateUnorderedEquipment(engine, equipmentCatalog);
        ValidateOwnedEquipmentLocations(engine, equipmentCatalog);
        ValidateDuplicateRequirementsNeedDuplicateItems(engine, equipmentCatalog);
        ValidateDraftPrimaryReplacement();
    }

    private static void ValidateLevelZeroSkillPoints()
    {
        AssertEqual(
            0,
            SkillPlannerRules.CalculateEarnedSkillPoints(0, 0, 0),
            "level-zero skill points");
    }


    private static void ValidateCanonicalStartingSkills(
        SkillPlannerCatalog skills)
    {
        var expected = new[]
        {
            "Beam Weapon",
            "Device Tech",
            "Engine Tech",
            "Reactor Tech",
            "Shield Tech",
        };

        foreach (var profession in skills.Professions)
        {
            var starting = skills.Skills
                .Where(value =>
                    value.MinimumRank > 0 &&
                    value.TryGetProfessionRule(profession.Index, out _))
                .Select(value => value.Name)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();

            AssertEqual(
                string.Join("|", expected),
                string.Join("|", starting),
                $"canonical free starting skills for {profession.Tag}");
        }
    }


    private static void ValidateEquipmentClassification()
    {
        AssertTrue(
            SkillBuildEquipmentCatalog.TryClassify(
                100,
                out var beamKind) &&
            beamKind == SkillBuildEquipmentKind.Weapon,
            "beam equipment classification");
        AssertTrue(
            !SkillBuildEquipmentCatalog.TryClassify(
                103,
                out _),
            "ammunition excluded from equipment classification");
        AssertTrue(
            SkillBuildEquipmentCatalog.TryClassify(
                110,
                out var deviceKind) &&
            deviceKind == SkillBuildEquipmentKind.Device,
            "device equipment classification");
    }

    private static void ValidateEquipmentRestrictions(
        SkillBuildEquipmentCatalog equipmentCatalog)
    {
        const int progenWarrior = 6;
        var unrestricted = new SkillBuildEquipmentChoice
        {
            ItemTemplateId = 1,
            Name = "Unrestricted",
            Kind = SkillBuildEquipmentKind.Weapon,
        };
        AssertTrue(
            equipmentCatalog.IsCompatible(unrestricted, progenWarrior),
            "unrestricted equipment accepted");

        AssertTrue(
            !equipmentCatalog.IsCompatible(
                unrestricted with { RaceRestriction = 1 << 2 },
                progenWarrior),
            "Progen restriction mask");
        AssertTrue(
            equipmentCatalog.IsCompatible(
                unrestricted with { RaceRestriction = (1 << 0) | (1 << 1) },
                progenWarrior),
            "Progen-only restriction mask");
        AssertTrue(
            !equipmentCatalog.IsCompatible(
                unrestricted with { ProfessionRestriction = 1 << 0 },
                progenWarrior),
            "Warrior restriction mask");
        AssertTrue(
            equipmentCatalog.IsCompatible(
                unrestricted with { ProfessionRestriction = (1 << 1) | (1 << 2) },
                progenWarrior),
            "Warrior-only restriction mask");
        AssertTrue(
            !equipmentCatalog.IsCompatible(
                unrestricted with { LoreRestriction = 1 },
                progenWarrior),
            "lore restriction excludes Progen");
        AssertEqual(
            "Jenquai Only",
            ItemTemplateRestrictionEvaluator.FormatRestrictionText(
                professionRestriction: 0,
                raceRestriction: (1 << 0) | (1 << 2),
                loreRestriction: 0),
            "shared restriction text for Jenquai-only equipment");
        AssertEqual(
            "Progen Restricted",
            ItemTemplateRestrictionEvaluator.FormatRestrictionText(
                professionRestriction: 0,
                raceRestriction: 1 << 2,
                loreRestriction: 0),
            "shared native-style restriction text for Progen-restricted equipment");
        AssertEqual(
            "Warrior Only · Progen Only",
            ItemTemplateRestrictionEvaluator.FormatRestrictionText(
                professionRestriction: (1 << 1) | (1 << 2),
                raceRestriction: (1 << 0) | (1 << 1),
                loreRestriction: 0),
            "shared profession and race restriction text");
    }

    private static void ValidateHullSlotLimits(
        SkillBuildHullCatalog hulls)
    {
        const int progenWarrior = 6;
        var maximum = hulls.GetMaximum(progenWarrior);
        AssertTrue(
            hulls.TryResolveForSlots(
                progenWarrior,
                maximum.WeaponSlots,
                maximum.DeviceSlots,
                out var resolved) &&
            resolved.Tier == maximum.Tier,
            "maximum hull slot resolution");
        AssertTrue(
            !hulls.TryResolveForSlots(
                progenWarrior,
                maximum.WeaponSlots + 1,
                maximum.DeviceSlots,
                out _),
            "impossible weapon count rejected");
    }

    private static void ValidateDraftPrimaryReplacement()
    {
        var build = new SkillBuildDocument
        {
            BuildId = "replace-primary",
            ProfessionIndex = 6,
            Title = "Replace primary",
            Equipment =
            [
                new SkillBuildEquipmentRequirement
                {
                    RequirementId = "weapon-a",
                    Kind = SkillBuildEquipmentKind.Weapon,
                    Order = 1,
                    Alternatives =
                    [
                        Alternative(101, "Primary"),
                        Alternative(102, "Alternative A"),
                        Alternative(103, "Alternative B"),
                    ],
                },
            ],
        };
        var draft = SkillBuildBoardDraft.FromDocument(build);

        AssertTrue(
            draft.ReplacePrimaryAlternative(
                "weapon-a",
                Alternative(102, "Alternative A")),
            "replace primary accepted");
        var requirement = draft.Equipment.Single();
        AssertEqual(
            "102|103",
            string.Join(
                "|",
                requirement.Alternatives.Select(value => value.ItemTemplateId)),
            "replace removes the previous primary without duplicating an existing alternative");

        AssertTrue(
            draft.ReplacePrimaryAlternative(
                "weapon-a",
                Alternative(104, "New primary")),
            "replace primary with new equipment accepted");
        AssertEqual(
            "104|103",
            string.Join(
                "|",
                requirement.Alternatives.Select(value => value.ItemTemplateId)),
            "replace preserves other alternatives and removes the replaced primary");
    }

    private static void ValidateSkillPointAwareOverall(
        SkillBuildBoardEngine engine,
        SkillBuildEquipmentCatalog equipmentCatalog)
    {
        var weapon = equipmentCatalog.GetChoices(SkillBuildEquipmentKind.Weapon)
            .FirstOrDefault(value => value.RequiredSkillId.HasValue);
        if (weapon == null)
        {
            return;
        }

        var build = new SkillBuildDocument
        {
            BuildId = "points",
            ProfessionIndex = 6,
            Title = "Points",
            Equipment =
            [
                Requirement("weapon", SkillBuildEquipmentKind.Weapon, weapon),
            ],
        };
        var analysis = engine.Analyze(build, baseline: null, equipped: null);

        AssertTrue(
            analysis.GenericLevels.Overall >=
            analysis.GenericLevels.Combat +
            analysis.GenericLevels.Explore +
            analysis.GenericLevels.Trade,
            "overall covers track minima");
        AssertTrue(
            analysis.GenericRequiredSkillPoints >= 0,
            "generic skill points calculated");
    }


    private static void ValidateAvailablePointsLowerLiveOverall(
        SkillBuildBoardEngine engine)
    {
        var build = new SkillBuildDocument
        {
            BuildId = "available-points",
            ProfessionIndex = 6,
            Title = "Available points",
            RecommendedSkills =
            [
                new SkillBuildSkillRecommendation(18, 2),
                new SkillBuildSkillRecommendation(20, 2),
                new SkillBuildSkillRecommendation(45, 2),
                new SkillBuildSkillRecommendation(55, 2),
            ],
        };
        var noBonus = new SkillPlannerCharacterBaseline
        {
            ProfessionIndex = 6,
            Skills = new Dictionary<int, SkillPlannerOwnedSkill>(),
            AvailableSkillPoints = 0,
        };
        var withBonus = noBonus with { AvailableSkillPoints = 3 };

        var withoutBonusAnalysis = engine.Analyze(
            build,
            noBonus,
            equipped: null);
        var withBonusAnalysis = engine.Analyze(
            build,
            withBonus,
            equipped: null);

        AssertTrue(
            withBonusAnalysis.EffectiveLevels.Overall <
            withoutBonusAnalysis.EffectiveLevels.Overall,
            "available points lower live overall requirement");
    }


    private static void ValidateRemainingRankCosts(
        SkillBuildBoardEngine engine,
        SkillPlannerCatalog skills)
    {
        const int progenWarrior = 6;
        var projectile = skills.Skills.FirstOrDefault(value =>
            string.Equals(value.Name, "Projectile Weapon", StringComparison.Ordinal) &&
            value.TryGetProfessionRule(progenWarrior, out var projectileRule) &&
            projectileRule.MaximumRank >= 9);
        var combatTrance = skills.Skills.FirstOrDefault(value =>
            string.Equals(value.Name, "Combat Trance", StringComparison.Ordinal) &&
            value.TryGetProfessionRule(progenWarrior, out var combatRule) &&
            combatRule.MaximumRank >= 7);
        if (projectile == null || combatTrance == null)
        {
            return;
        }

        var build = new SkillBuildDocument
        {
            BuildId = "remaining-cost",
            ProfessionIndex = progenWarrior,
            Title = "Remaining cost",
            RecommendedSkills =
            [
                new SkillBuildSkillRecommendation(projectile.Id, 9),
                new SkillBuildSkillRecommendation(combatTrance.Id, 7),
            ],
        };
        var baseline = new SkillPlannerCharacterBaseline
        {
            ProfessionIndex = progenWarrior,
            CombatLevel = 42,
            ExploreLevel = 45,
            TradeLevel = 39,
            AvailableSkillPoints = 1,
            Skills = new Dictionary<int, SkillPlannerOwnedSkill>
            {
                [projectile.Id] = new SkillPlannerOwnedSkill
                {
                    SkillId = projectile.Id,
                    CurrentRank = 8,
                    MaximumRank = 9,
                },
                [combatTrance.Id] = new SkillPlannerOwnedSkill
                {
                    SkillId = combatTrance.Id,
                    CurrentRank = 6,
                    MaximumRank = 7,
                },
            },
        };

        var analysis = engine.Analyze(build, baseline, equipped: null);
        AssertEqual(
            14,
            analysis.RemainingSkillPointCost,
            "remaining rank costs use the live ranks");
        AssertTrue(
            analysis.EffectiveLevels.Combat >= 50,
            "combat rank requirement retained");
    }

    private static void ValidatePostCapSkillPointOverall(
        SkillBuildBoardEngine engine,
        SkillPlannerCatalog skills)
    {
        const int progenWarrior = 6;
        var recommendations = skills.Skills
            .Select(skill =>
                skill.TryGetProfessionRule(progenWarrior, out var rule)
                    ? new SkillBuildSkillRecommendation(
                        skill.Id,
                        rule.MaximumRank)
                    : null)
            .Where(value => value != null)
            .Cast<SkillBuildSkillRecommendation>()
            .ToArray();
        var build = new SkillBuildDocument
        {
            BuildId = "post-cap-points",
            ProfessionIndex = progenWarrior,
            Title = "Post-cap points",
            RecommendedSkills = recommendations,
        };

        var analysis = engine.Analyze(build, baseline: null, equipped: null);
        var earnedAtCap = SkillPlannerRules.CalculateEarnedSkillPoints(50, 50, 50);
        var expected = checked(
            150 + Math.Max(
                0,
                analysis.GenericRequiredSkillPoints - earnedAtCap));
        AssertEqual(
            expected,
            analysis.GenericLevels.Overall,
            "post-cap skill points extend the Overall target");
    }

    private static void ValidateRequirementReasons(
        SkillPlannerCatalog skills,
        SkillBuildBoardEngine engine,
        SkillBuildEquipmentCatalog equipmentCatalog,
        SkillBuildHullCatalog hulls)
    {
        const int progenWarrior = 6;
        var combatTrance = skills.Skills.FirstOrDefault(value =>
            string.Equals(
                value.Name,
                "Combat Trance",
                StringComparison.Ordinal) &&
            value.TryGetProfessionRule(progenWarrior, out _));
        if (combatTrance != null &&
            combatTrance.TryGetProfessionRule(progenWarrior, out var rule))
        {
            var build = new SkillBuildDocument
            {
                BuildId = "requirement-reasons",
                ProfessionIndex = progenWarrior,
                Title = "Requirement reasons",
                RecommendedSkills =
                [
                    new SkillBuildSkillRecommendation(
                        combatTrance.Id,
                        rule.MaximumRank),
                ],
            };
            var baseline = new SkillPlannerCharacterBaseline
            {
                ProfessionIndex = progenWarrior,
                CombatLevel = 1,
                Skills = new Dictionary<int, SkillPlannerOwnedSkill>(),
                AvailableSkillPoints = 1,
            };
            var analysis = engine.Analyze(build, baseline, equipped: null);

            AssertTrue(
                analysis.EffectiveReasons.Combat.Any(value =>
                    value.Contains(
                        "Combat Trance rank",
                        StringComparison.Ordinal)),
                "combat requirement reason");
            AssertTrue(
                analysis.EffectiveReasons.SkillPoints.Any(value =>
                    value.Contains(
                        "1 skill point available",
                        StringComparison.Ordinal)),
                "live skill-point reason");
        }

        var weapon = equipmentCatalog
            .GetChoices(SkillBuildEquipmentKind.Weapon)
            .FirstOrDefault(value => equipmentCatalog.IsCompatible(
                value,
                progenWarrior));
        if (weapon == null)
        {
            return;
        }

        var maximumHull = hulls.GetMaximum(progenWarrior);
        var device = equipmentCatalog
            .GetChoices(SkillBuildEquipmentKind.Device)
            .FirstOrDefault(value => equipmentCatalog.IsCompatible(
                value,
                progenWarrior));
        var hullEquipment = Enumerable
            .Range(1, maximumHull.WeaponSlots)
            .Select(index => Requirement(
                $"weapon-{index}",
                SkillBuildEquipmentKind.Weapon,
                weapon))
            .ToList();
        if (device != null)
        {
            hullEquipment.AddRange(
                Enumerable.Range(1, maximumHull.DeviceSlots)
                    .Select(index => Requirement(
                        $"device-{index}",
                        SkillBuildEquipmentKind.Device,
                        device)));
        }
        var hullBuild = new SkillBuildDocument
        {
            BuildId = "hull-reasons",
            ProfessionIndex = progenWarrior,
            Title = "Hull reasons",
            Equipment = hullEquipment,
        };
        var hullAnalysis = engine.Analyze(
            hullBuild,
            baseline: null,
            equipped: null);
        AssertTrue(
            hullAnalysis.GenericReasons.Hull.Any(value => value.Contains(
                string.Create(
                    System.Globalization.CultureInfo.CurrentCulture,
                    $"{maximumHull.WeaponSlots} weapons require Hull {maximumHull.Tier}"),
                StringComparison.CurrentCulture)),
            "hull slot requirement reason");
        if (device != null &&
            hulls.TryResolveForSlots(
                progenWarrior,
                weaponCount: 0,
                deviceCount: maximumHull.DeviceSlots,
                out var deviceHull) &&
            deviceHull.Tier < maximumHull.Tier)
        {
            AssertTrue(
                hullAnalysis.GenericReasons.Hull.All(value => !value.Contains(
                    string.Create(
                        System.Globalization.CultureInfo.CurrentCulture,
                        $"{maximumHull.DeviceSlots} devices require Hull {deviceHull.Tier}"),
                    StringComparison.CurrentCulture)),
                "already-satisfied lower hull reason omitted");
        }
    }

    private static void ValidateUnorderedEquipment(
        SkillBuildBoardEngine engine,
        SkillBuildEquipmentCatalog equipmentCatalog)
    {
        var weapons = equipmentCatalog.GetChoices(SkillBuildEquipmentKind.Weapon)
            .Take(2)
            .ToArray();
        if (weapons.Length < 2)
        {
            return;
        }

        var build = new SkillBuildDocument
        {
            BuildId = "unordered",
            ProfessionIndex = 6,
            Title = "Unordered",
            Equipment =
            [
                Requirement("a", SkillBuildEquipmentKind.Weapon, weapons[0]),
                Requirement("b", SkillBuildEquipmentKind.Weapon, weapons[1]),
            ],
        };
        var baseline = new SkillPlannerCharacterBaseline
        {
            ProfessionIndex = 6,
            Skills = new Dictionary<int, SkillPlannerOwnedSkill>(),
        };
        var equipped = new SkillBuildEquipmentBaseline
        {
            IsAvailable = true,
            WeaponSlotCount = 2,
            DeviceSlotCount = 1,
            Items = new Dictionary<SkillBuildEquipmentSlot, SkillBuildEquippedItem>
            {
                [SkillBuildEquipmentSlot.Weapon(1)] =
                    new(weapons[1].ItemTemplateId, weapons[1].Name),
                [SkillBuildEquipmentSlot.Weapon(2)] =
                    new(weapons[0].ItemTemplateId, weapons[0].Name),
            },
        };

        var analysis = engine.Analyze(build, baseline, equipped);
        AssertTrue(
            analysis.Equipment.All(value => value.IsEquipped),
            "unordered equipment matching");
    }


    private static void ValidateOwnedEquipmentLocations(
        SkillBuildBoardEngine engine,
        SkillBuildEquipmentCatalog equipmentCatalog)
    {
        var choices = equipmentCatalog
            .GetChoices(SkillBuildEquipmentKind.Device)
            .Take(2)
            .ToArray();
        if (choices.Length < 2)
        {
            return;
        }

        var build = new SkillBuildDocument
        {
            BuildId = "owned-locations",
            ProfessionIndex = 6,
            Title = "Owned locations",
            Equipment =
            [
                Requirement("inventory", SkillBuildEquipmentKind.Device, choices[0]),
                Requirement("vault", SkillBuildEquipmentKind.Device, choices[1]),
            ],
        };
        var baseline = new SkillPlannerCharacterBaseline
        {
            ProfessionIndex = 6,
            Skills = new Dictionary<int, SkillPlannerOwnedSkill>(),
        };
        var equipment = new SkillBuildEquipmentBaseline
        {
            IsAvailable = true,
            InventoryItemCounts = new Dictionary<int, int>
            {
                [choices[0].ItemTemplateId] = 1,
            },
            VaultItemCounts = new Dictionary<int, int>
            {
                [choices[1].ItemTemplateId] = 1,
            },
        };

        var analysis = engine.Analyze(build, baseline, equipment);
        AssertEqual(
            SkillBuildEquipmentAvailability.Inventory,
            analysis.Equipment.Single(value =>
                value.Requirement.RequirementId == "inventory").Availability,
            "inventory equipment recognized by template id");
        AssertEqual(
            SkillBuildEquipmentAvailability.Vault,
            analysis.Equipment.Single(value =>
                value.Requirement.RequirementId == "vault").Availability,
            "vault equipment recognized by template id");
    }

    private static void ValidateDuplicateRequirementsNeedDuplicateItems(
        SkillBuildBoardEngine engine,
        SkillBuildEquipmentCatalog equipmentCatalog)
    {
        var weapon = equipmentCatalog.GetChoices(SkillBuildEquipmentKind.Weapon)
            .FirstOrDefault();
        if (weapon == null)
        {
            return;
        }

        var build = new SkillBuildDocument
        {
            BuildId = "duplicates",
            ProfessionIndex = 6,
            Title = "Duplicates",
            Equipment =
            [
                Requirement("copy-a", SkillBuildEquipmentKind.Weapon, weapon),
                Requirement("copy-b", SkillBuildEquipmentKind.Weapon, weapon),
            ],
        };
        var baseline = new SkillPlannerCharacterBaseline
        {
            ProfessionIndex = 6,
            Skills = new Dictionary<int, SkillPlannerOwnedSkill>(),
        };
        var equipped = new SkillBuildEquipmentBaseline
        {
            IsAvailable = true,
            WeaponSlotCount = 1,
            Items = new Dictionary<SkillBuildEquipmentSlot, SkillBuildEquippedItem>
            {
                [SkillBuildEquipmentSlot.Weapon(1)] =
                    new(weapon.ItemTemplateId, weapon.Name),
            },
        };

        var analysis = engine.Analyze(build, baseline, equipped);
        AssertEqual(
            1,
            analysis.Equipment.Count(value => value.IsEquipped),
            "one item cannot satisfy two requirements");
    }

    private static SkillBuildEquipmentRequirement Requirement(
        string id,
        SkillBuildEquipmentKind kind,
        SkillBuildEquipmentChoice choice) =>
        new()
        {
            RequirementId = id,
            Kind = kind,
            Order = 1,
            Alternatives =
            [
                new SkillBuildEquipmentAlternative
                {
                    ItemTemplateId = choice.ItemTemplateId,
                    ItemName = choice.Name,
                },
            ],
        };

    private static SkillBuildEquipmentAlternative Alternative(
        int itemTemplateId,
        string itemName) =>
        new()
        {
            ItemTemplateId = itemTemplateId,
            ItemName = itemName,
        };

    private static void AssertTrue(
        bool condition,
        string scenario)
    {
        if (!condition)
        {
            throw new InvalidOperationException(
                $"Build-board scenario failed: {scenario}.");
        }
    }

    private static void AssertEqual<T>(
        T expected,
        T actual,
        string scenario)
        where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"Build-board scenario failed: {scenario}; expected '{expected}', got '{actual}'.");
        }
    }
}
#endif
