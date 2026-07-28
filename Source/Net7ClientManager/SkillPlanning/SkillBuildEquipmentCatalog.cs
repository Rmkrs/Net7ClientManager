namespace Net7ClientManager.SkillPlanning;

using Net7ClientManager.Observations.Models;

internal sealed class SkillBuildEquipmentCatalog
{
    private readonly SkillPlannerCatalog skills;
    private readonly Lazy<IReadOnlyList<SkillBuildEquipmentChoice>> choices;

    public SkillBuildEquipmentCatalog(SkillPlannerCatalog skills)
    {
        this.skills = skills ?? throw new ArgumentNullException(nameof(skills));
        this.choices = new Lazy<IReadOnlyList<SkillBuildEquipmentChoice>>(
            this.LoadChoices,
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public IReadOnlyList<SkillBuildEquipmentChoice> GetChoices(
        SkillBuildEquipmentKind kind) =>
        this.choices.Value
            .Where(value => value.Kind == kind)
            .ToArray();

    public SkillBuildEquipmentChoice? Find(int itemTemplateId) =>
        this.choices.Value.FirstOrDefault(
            value => value.ItemTemplateId == itemTemplateId);

    public bool IsCompatible(
        SkillBuildEquipmentChoice choice,
        int professionIndex)
    {
        ArgumentNullException.ThrowIfNull(choice);

        if (!this.skills.TryGetProfession(professionIndex, out var profession))
        {
            return false;
        }

        // Native item attributes use three-bit exclusion masks:
        //   race bit 0 = Terran, bit 1 = Jenquai, bit 2 = Progen
        //   profession bit 0 = Warrior, bit 1 = Trader, bit 2 = Explorer
        // A mask containing the other two bits therefore represents an
        // "Only" restriction. Attribute 0x11 adds the lore restriction used
        // by the native tooltip path: 1 excludes Progen, 2 excludes Jenquai.
        var professionMask = choice.ProfessionRestriction & 0x07;
        var raceMask = choice.RaceRestriction & 0x07;
        raceMask |= choice.LoreRestriction switch
        {
            1 => 1 << 2,
            2 => 1 << 1,
            _ => 0,
        };

        if ((professionMask & (1 << profession.ProfessionIndex)) != 0 ||
            (raceMask & (1 << profession.RaceIndex)) != 0)
        {
            return false;
        }

        if (choice.RequiredSkillId is not { } skillId)
        {
            return true;
        }

        return this.skills.TryGetSkill(skillId, out var skill) &&
               skill.TryGetProfessionRule(professionIndex, out var rule) &&
               choice.RequiredSkillRank <= rule.MaximumRank;
    }

    private IReadOnlyList<SkillBuildEquipmentChoice> LoadChoices()
    {
        List<SkillBuildEquipmentChoice> result = [];

        foreach (var value in ClientItemTemplateNameResolver
                     .GetKnownEquipmentTemplates())
        {
            if (!TryClassify(value.Subcategory, out var kind))
            {
                continue;
            }

            var requiredSkillName = ResolveRequiredSkillName(
                kind,
                value.TypeDisplayName);
            int? requiredSkillId = null;
            if (requiredSkillName != null &&
                this.skills.TryGetSkill(requiredSkillName, out var skill))
            {
                requiredSkillId = skill.Id;
            }

            result.Add(
                new SkillBuildEquipmentChoice
                {
                    ItemTemplateId = value.ItemTemplateId,
                    Name = value.Name,
                    TypeDisplayName = value.TypeDisplayName,
                    Category = value.Category,
                    Subcategory = value.Subcategory,
                    ItemType = value.ItemType,
                    Kind = kind,
                    TechLevel = value.TechLevel,
                    ProfessionRestriction = value.ProfessionRestriction,
                    RaceRestriction = value.RaceRestriction,
                    LoreRestriction = value.LoreRestriction,
                    RequiredCombatLevel = value.RequiredCombatLevel,
                    RequiredExploreLevel = value.RequiredExploreLevel,
                    RequiredTradeLevel = value.RequiredTradeLevel,
                    RequiredOverallLevel = value.RequiredOverallLevel,
                    RequiredSkillId = requiredSkillId,
                    RequiredSkillName = requiredSkillName ?? "",
                    RequiredSkillRank = Math.Max(0, value.TechLevel),
                });
        }

        return result
            .OrderBy(value => value.Name,
                StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(value => value.ItemTemplateId)
            .ToArray();
    }

    internal static bool TryClassify(
        int subcategory,
        out SkillBuildEquipmentKind kind)
    {
        // Native equip semantics use exact subcategories. In particular,
        // ammunition is 103 and must never be offered as a weapon.
        kind = subcategory switch
        {
            100 or 101 or 102 => SkillBuildEquipmentKind.Weapon,
            110 => SkillBuildEquipmentKind.Device,
            120 => SkillBuildEquipmentKind.Reactor,
            121 => SkillBuildEquipmentKind.Engine,
            122 => SkillBuildEquipmentKind.Shield,
            _ => default,
        };
        return subcategory is 100 or 101 or 102 or 110 or 120 or 121 or 122;
    }

    private static string? ResolveRequiredSkillName(
        SkillBuildEquipmentKind kind,
        string typeDisplayName)
    {
        var exactType = typeDisplayName.Trim();
        return kind switch
        {
            SkillBuildEquipmentKind.Shield => "Shield Tech",
            SkillBuildEquipmentKind.Reactor => "Reactor Tech",
            SkillBuildEquipmentKind.Engine => "Engine Tech",
            SkillBuildEquipmentKind.Device => "Device Tech",
            SkillBuildEquipmentKind.Weapon when
                exactType.Equals("Beam Weapon", StringComparison.OrdinalIgnoreCase) =>
                "Beam Weapon",
            SkillBuildEquipmentKind.Weapon when
                exactType.Equals("Projectile Weapon", StringComparison.OrdinalIgnoreCase) =>
                "Projectile Weapon",
            SkillBuildEquipmentKind.Weapon when
                exactType.Equals("Missile Weapon", StringComparison.OrdinalIgnoreCase) =>
                "Missile Weapon",
            _ => null,
        };
    }

}

internal sealed record SkillBuildEquipmentChoice
{
    public int ItemTemplateId { get; init; }

    public string Name { get; init; } = "";

    public string TypeDisplayName { get; init; } = "";

    public int Category { get; init; }

    public int Subcategory { get; init; }

    public int ItemType { get; init; }

    public SkillBuildEquipmentKind Kind { get; init; }

    public int TechLevel { get; init; }

    public int ProfessionRestriction { get; init; }

    public int RaceRestriction { get; init; }

    public int LoreRestriction { get; init; }

    public int RequiredCombatLevel { get; init; }

    public int RequiredExploreLevel { get; init; }

    public int RequiredTradeLevel { get; init; }

    public int RequiredOverallLevel { get; init; }

    public int? RequiredSkillId { get; init; }

    public string RequiredSkillName { get; init; } = "";

    public int RequiredSkillRank { get; init; }
}
