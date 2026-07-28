namespace Net7ClientManager.Services;

using System.Globalization;
using Net7ClientManager.Models;

internal sealed class SkillIniCatalog(
    IReadOnlyDictionary<int, SkillAbilityDefinition> abilitiesById,
    IReadOnlyDictionary<string, List<SkillAbilityDefinition>> abilitiesByName,
    IReadOnlyDictionary<int, SkillFamilyIconDefinition> skillFamilyIconsById,
    IReadOnlyDictionary<string, SkillFamilyIconDefinition> skillFamilyIconsByName,
    string status)
{
    public static SkillIniCatalog Unavailable(string status) =>
        new(
            new Dictionary<int, SkillAbilityDefinition>(),
            new Dictionary<string, List<SkillAbilityDefinition>>(
                StringComparer.OrdinalIgnoreCase),
            new Dictionary<int, SkillFamilyIconDefinition>(),
            new Dictionary<string, SkillFamilyIconDefinition>(
                StringComparer.OrdinalIgnoreCase),
            status);

    public bool IsAvailable => abilitiesById.Count > 0;

    public string Status { get; } = status;

    public bool TryResolveAbility(
        int? abilityId,
        string abilityName,
        string skillFamilyName,
        out SkillAbilityDefinition definition)
    {
        if (abilityId.HasValue &&
            abilitiesById.TryGetValue(abilityId.Value, out definition!))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(abilityName) &&
            abilitiesByName.TryGetValue(
                abilityName.Trim(),
                out var candidates))
        {
            if (!string.IsNullOrWhiteSpace(skillFamilyName))
            {
                var matchingFamily = candidates
                    .Where(candidate => string.Equals(
                        candidate.SkillFamilyName,
                        skillFamilyName.Trim(),
                        StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (matchingFamily.Count == 1)
                {
                    definition = matchingFamily[0];
                    return true;
                }
            }

            if (candidates.Count == 1)
            {
                definition = candidates[0];
                return true;
            }
        }

        definition = default!;
        return false;
    }

    public bool TryResolveSkillFamilyIcon(
        int? skillFamilyId,
        string skillFamilyName,
        int currentRank,
        out SkillFamilyIconDefinition definition)
    {
        SkillFamilyIconDefinition? family = null;

        if (skillFamilyId.HasValue &&
            skillFamilyIconsById.TryGetValue(
                skillFamilyId.Value,
                out var familyById))
        {
            family = familyById;
        }

        if (family == null &&
            !string.IsNullOrWhiteSpace(skillFamilyName) &&
            skillFamilyIconsByName.TryGetValue(
                skillFamilyName.Trim(),
                out var familyByName))
        {
            family = familyByName;
        }

        if (family == null)
        {
            definition = default!;
            return false;
        }

        var candidates = abilitiesById.Values
            .Where(candidate =>
                candidate.SkillFamilyId == family.SkillFamilyId ||
                string.Equals(
                    candidate.SkillFamilyName,
                    family.SkillFamilyName,
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (candidates.Length > 0 &&
            TrySelectSkillFamilyIcon(
                candidates,
                currentRank,
                out var tintSource))
        {
            family = family with
            {
                TintRed = tintSource.TintRed,
                TintGreen = tintSource.TintGreen,
                TintBlue = tintSource.TintBlue,
            };
        }

        definition = family;
        return true;
    }

    private static bool TrySelectSkillFamilyIcon(
        IReadOnlyList<SkillAbilityDefinition> candidates,
        int currentRank,
        out SkillAbilityDefinition definition)
    {
        var effectiveRank = Math.Max(0, currentRank);
        var eligible = effectiveRank > 0
            ? candidates
                .Where(candidate =>
                    candidate.MinimumSkillLevel <= effectiveRank)
                .ToArray()
            : [];
        var ordered = eligible.Length > 0
            ? eligible.OrderByDescending(candidate =>
                candidate.MinimumSkillLevel)
            : candidates.OrderBy(candidate =>
                candidate.MinimumSkillLevel);

        definition = ordered
            .ThenByDescending(candidate =>
                candidate.IsIntrinsicallyActivatable)
            .ThenBy(candidate => candidate.AbilityId)
            .First();
        return true;
    }
}

internal sealed record SkillAbilityDefinition
{
    public int AbilityId { get; init; }

    public bool IsIntrinsicallyActivatable { get; init; }

    public string Name { get; init; } = "";

    public int? SkillFamilyId { get; init; }

    public string SkillFamilyName { get; init; } = "";

    public int MinimumSkillLevel { get; init; }

    public int? ListedEnergyCost { get; init; }

    public float? MaximumReactorCostPercent { get; init; }

    public string Description { get; init; } = "";

    public string SkillFamilyDescription { get; init; } = "";

    public string RankDescription { get; init; } = "";

    public IReadOnlyList<float> RangeByRank { get; init; } = [];

    public string IconResourceName { get; init; } = "";

    public float? TintRed { get; init; }

    public float? TintGreen { get; init; }

    public float? TintBlue { get; init; }

    public SkillAbilitySemantics Semantics { get; init; } =
        SkillAbilitySemantics.Unknown;

    public float? ResolveRange(int? currentSkillRank)
    {
        if (this.RangeByRank.Count == 0)
        {
            return null;
        }

        var effectiveRank = Math.Max(
            this.MinimumSkillLevel,
            currentSkillRank.GetValueOrDefault(this.MinimumSkillLevel));

        if (effectiveRank <= 0)
        {
            return null;
        }

        var index = Math.Clamp(
            effectiveRank - 1,
            0,
            this.RangeByRank.Count - 1);

        return this.RangeByRank[index];
    }
}

internal sealed record SkillFamilyIconDefinition
{
    public int SkillFamilyId { get; init; }

    public string SkillFamilyName { get; init; } = "";

    public IReadOnlyList<string> IconResourceNames { get; init; } = [];

    public float? TintRed { get; init; }

    public float? TintGreen { get; init; }

    public float? TintBlue { get; init; }
}

internal sealed record SkillShortcutDetails
{
    public int AbilityId { get; init; }

    public bool IsIntrinsicallyActivatable { get; init; }

    public string SkillFamilyName { get; init; } = "";

    public int? SkillFamilyId { get; init; }

    public int Rank { get; init; }

    public int? ListedEnergyCost { get; init; }

    public float? MaximumReactorCostPercent { get; init; }

    public float? Range { get; init; }

    public string Description { get; init; } = "";

    public SkillAbilitySemantics Semantics { get; init; } =
        SkillAbilitySemantics.Unknown;

    public string LayoutSignature =>
        string.Join(
            ":",
            new[]
            {
                this.AbilityId.ToString(CultureInfo.InvariantCulture),
                this.IsIntrinsicallyActivatable ? "1" : "0",
                this.SkillFamilyId?.ToString(CultureInfo.InvariantCulture) ?? "",
                this.Rank.ToString(CultureInfo.InvariantCulture),
                this.ListedEnergyCost?.ToString(CultureInfo.InvariantCulture) ?? "",
                this.MaximumReactorCostPercent?.ToString(
                    "R",
                    CultureInfo.InvariantCulture) ?? "",
                this.Range?.ToString("R", CultureInfo.InvariantCulture) ?? "",
                this.Semantics.Category.ToString(),
                this.Semantics.TargetDisposition.ToString(),
                this.Semantics.EffectScope.ToString(),
                this.Semantics.Confidence.ToString(),
                this.Description,
            });
}

internal sealed record SkillAbilitySemantics(
    GameShortcutActionCategory Category,
    SkillTargetDisposition TargetDisposition,
    SkillEffectScope EffectScope,
    SkillSemanticConfidence Confidence)
{
    public static SkillAbilitySemantics Unknown { get; } =
        new(
            GameShortcutActionCategory.Utility,
            SkillTargetDisposition.Unknown,
            SkillEffectScope.Unknown,
            SkillSemanticConfidence.Unknown);
}

[Flags]
internal enum SkillTargetDisposition
{
    Unknown = 0,
    Self = 1 << 0,
    Friendly = 1 << 1,
    Hostile = 1 << 2,
}

[Flags]
internal enum SkillEffectScope
{
    Unknown = 0,
    Self = 1 << 0,
    Target = 1 << 1,
    Group = 1 << 2,
    Area = 1 << 3,
}

internal enum SkillSemanticConfidence
{
    Unknown,
    Inferred,
    Explicit,
    Curated,
}
