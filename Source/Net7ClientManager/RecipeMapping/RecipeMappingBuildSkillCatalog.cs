namespace Net7ClientManager.RecipeMapping;

using Net7ClientManager.Observations.Models;

internal static class RecipeMappingBuildSkillCatalog
{
    public const string BuildComponents = "Build Components";
    public const string BuildDevices = "Build Devices";
    public const string BuildEngines = "Build Engines";
    public const string BuildReactors = "Build Reactors";
    public const string BuildShields = "Build Shields";
    public const string BuildWeapons = "Build Weapons";
    public const string BuildAmmunition = "Build Ammunition";

    private static readonly string[] skillNames =
    [
        BuildWeapons,
        BuildAmmunition,
        BuildDevices,
        BuildEngines,
        BuildReactors,
        BuildShields,
        BuildComponents,
    ];

    public static IReadOnlyList<string> SkillNames => skillNames;

    public static IReadOnlyList<RecipeMappingObservedBuildSkill> ObserveBuildSkills(
        ClientCharacterSkillsObservation? skills)
    {
        if (skills is not { IsAvailable: true })
        {
            return [];
        }

        var byName = skills.Skills
            .Where(skill => !string.IsNullOrWhiteSpace(skill.Name))
            .GroupBy(skill => skill.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Last(),
                StringComparer.OrdinalIgnoreCase);

        return skillNames
            .Where(byName.ContainsKey)
            .Select(name =>
            {
                var skill = byName[name];

                return new RecipeMappingObservedBuildSkill
                {
                    Name = name,
                    CurrentRank = skill.CurrentRank,
                    IsLearned = skill.IsLearned,
                };
            })
            .ToArray();
    }

    public static bool IsCategoryRequired(
        string? path,
        IReadOnlySet<string> requiredBuildSkills)
    {
        if (requiredBuildSkills.Count == 0 ||
            string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        return GetApplicableSkillNames(path)
            .Any(requiredBuildSkills.Contains);
    }

    public static IReadOnlyList<string> GetApplicableSkillNames(
        int categoryId)
    {
        return categoryId switch
        {
            100 or 101 or 102 => [BuildWeapons],
            103 => [BuildWeapons, BuildAmmunition],
            110 or 111 => [BuildDevices],
            120 => [BuildReactors],
            121 => [BuildEngines],
            122 => [BuildShields],
            140 or 141 or 142 or
            150 or 151 or 152 or 153 or
            160 or 161 or 162 or 163 or
            170 or 171 or 172 or 173 or
            180 or 181 or 182 or 183 => [BuildComponents],
            _ => [],
        };
    }

    public static IReadOnlyList<string> GetApplicableSkillNames(
        string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return [];
        }

        var value = path.Trim();

        if (value.StartsWith(
                "Components / ",
                StringComparison.OrdinalIgnoreCase))
        {
            return [BuildComponents];
        }

        if (value.StartsWith(
                "Items / Systems / ",
                StringComparison.OrdinalIgnoreCase))
        {
            return [BuildDevices];
        }

        if (value.Equals(
                "Items / Core / Engine",
                StringComparison.OrdinalIgnoreCase))
        {
            return [BuildEngines];
        }

        if (value.Equals(
                "Items / Core / Reactor",
                StringComparison.OrdinalIgnoreCase))
        {
            return [BuildReactors];
        }

        if (value.Equals(
                "Items / Core / Shield",
                StringComparison.OrdinalIgnoreCase))
        {
            return [BuildShields];
        }

        if (value.Equals(
                "Items / Weapons / Ammo",
                StringComparison.OrdinalIgnoreCase))
        {
            return [BuildWeapons, BuildAmmunition];
        }

        if (value.StartsWith(
                "Items / Weapons / ",
                StringComparison.OrdinalIgnoreCase))
        {
            return [BuildWeapons];
        }

        return [];
    }

    public static string ResolveDisplaySkillName(
        string? path,
        IReadOnlySet<string> requiredBuildSkills)
    {
        var applicable = GetApplicableSkillNames(path)
            .Where(requiredBuildSkills.Contains)
            .ToArray();

        if (applicable.Length == 0)
        {
            return "";
        }

        if (applicable.Contains(BuildWeapons, StringComparer.Ordinal) &&
            applicable.Contains(BuildAmmunition, StringComparer.Ordinal))
        {
            return BuildWeapons;
        }

        return applicable[0];
    }

    public static string GetShortCategoryName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "Manufacturing category";
        }

        var value = path.Trim();
        var prefixes = new[]
        {
            "Items / Weapons / ",
            "Items / Systems / ",
            "Items / Core / ",
            "Components / ",
        };

        foreach (var prefix in prefixes)
        {
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return value[prefix.Length..];
            }
        }

        return value;
    }
}

internal sealed record RecipeMappingObservedBuildSkill
{
    public string Name { get; init; } = "";

    public int CurrentRank { get; init; }

    public bool IsLearned { get; init; }
}
