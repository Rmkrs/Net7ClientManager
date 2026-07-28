namespace Net7ClientManager.SkillPlanning;

using System.Collections.ObjectModel;
using System.Globalization;

internal sealed class SkillPlannerCatalog
{
    private const int SupportedSchemaVersion = 1;
    private const int ExpectedProfessionCount = 9;

    private readonly IReadOnlyDictionary<int, SkillPlannerProfessionDefinition>
        professionsByIndex;

    private readonly IReadOnlyDictionary<string, SkillPlannerProfessionDefinition>
        professionsByTag;

    private readonly IReadOnlyDictionary<int, SkillPlannerSkillDefinition>
        skillsById;

    private readonly IReadOnlyDictionary<string, SkillPlannerSkillDefinition>
        skillsByName;

    public SkillPlannerCatalog(
        SkillPlannerCatalogDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.SchemaVersion != SupportedSchemaVersion)
        {
            throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Unsupported skill-planner catalog schema {document.SchemaVersion}."));
        }

        this.Revision = RequireText(
            document.Revision,
            "catalog revision");

        var professionDefinitions = BuildProfessions(document.Professions);
        this.professionsByIndex =
            new ReadOnlyDictionary<int, SkillPlannerProfessionDefinition>(
                professionDefinitions.ToDictionary(
                    profession => profession.Index));
        this.professionsByTag =
            new ReadOnlyDictionary<string, SkillPlannerProfessionDefinition>(
                professionDefinitions.ToDictionary(
                    profession => profession.Tag,
                    StringComparer.OrdinalIgnoreCase));

        var groupOrder = BuildGroupOrder(document.Groups);
        var skillDefinitions = BuildSkills(
            document.Skills,
            groupOrder,
            this.professionsByIndex);

        this.skillsById =
            new ReadOnlyDictionary<int, SkillPlannerSkillDefinition>(
                skillDefinitions.ToDictionary(skill => skill.Id));
        this.skillsByName =
            new ReadOnlyDictionary<string, SkillPlannerSkillDefinition>(
                skillDefinitions.ToDictionary(
                    skill => skill.Name,
                    StringComparer.OrdinalIgnoreCase));

        ValidateGroups(
            document.Groups,
            this.skillsById);

        this.Professions = professionDefinitions;
        this.Groups = document.Groups
            .Select(
                (group, index) =>
                    new SkillPlannerGroupDefinition(
                        index,
                        RequireText(group.Name, "skill group name"),
                        group.SkillIds.ToArray()))
            .ToArray();
        this.Skills = skillDefinitions;
    }

    public string Revision { get; }

    public IReadOnlyList<SkillPlannerProfessionDefinition> Professions { get; }

    public IReadOnlyList<SkillPlannerGroupDefinition> Groups { get; }

    public IReadOnlyList<SkillPlannerSkillDefinition> Skills { get; }

    public bool TryGetProfession(
        int professionIndex,
        out SkillPlannerProfessionDefinition definition)
    {
        return this.professionsByIndex.TryGetValue(
            professionIndex,
            out definition!);
    }

    public bool TryGetProfession(
        string tag,
        out SkillPlannerProfessionDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            definition = default!;
            return false;
        }

        return this.professionsByTag.TryGetValue(
            tag.Trim(),
            out definition!);
    }

    public bool TryGetSkill(
        int skillId,
        out SkillPlannerSkillDefinition definition)
    {
        return this.skillsById.TryGetValue(skillId, out definition!);
    }

    public bool TryGetSkill(
        string skillName,
        out SkillPlannerSkillDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(skillName))
        {
            definition = default!;
            return false;
        }

        return this.skillsByName.TryGetValue(
            skillName.Trim(),
            out definition!);
    }

    public bool TryResolveProfessionIndex(
        int raceIndex,
        int professionIndex,
        out int plannerProfessionIndex)
    {
        foreach (var profession in this.Professions)
        {
            if (profession.RaceIndex == raceIndex &&
                profession.ProfessionIndex == professionIndex)
            {
                plannerProfessionIndex = profession.Index;
                return true;
            }
        }

        plannerProfessionIndex = -1;
        return false;
    }

    private static IReadOnlyList<SkillPlannerProfessionDefinition>
        BuildProfessions(
            IReadOnlyList<SkillPlannerProfessionDocument> documents)
    {
        if (documents.Count != ExpectedProfessionCount)
        {
            throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Skill-planner catalog contains {documents.Count} professions; expected {ExpectedProfessionCount}."));
        }

        List<SkillPlannerProfessionDefinition> definitions = [];
        HashSet<int> indexes = [];
        HashSet<string> tags = new(StringComparer.OrdinalIgnoreCase);
        HashSet<(int Race, int Profession)> coordinates = [];

        foreach (var document in documents.OrderBy(value => value.Index))
        {
            if (document.Index is < 0 or >= ExpectedProfessionCount ||
                !indexes.Add(document.Index))
            {
                throw new InvalidOperationException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Invalid or duplicate planner profession index {document.Index}."));
            }

            var tag = RequireText(document.Tag, "profession tag");
            if (!tags.Add(tag))
            {
                throw new InvalidOperationException(
                    $"Duplicate planner profession tag '{tag}'.");
            }

            if (document.RaceIndex is < 0 or > 2 ||
                document.ProfessionIndex is < 0 or > 2 ||
                !coordinates.Add((document.RaceIndex, document.ProfessionIndex)))
            {
                throw new InvalidOperationException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Invalid or duplicate profession coordinate {document.RaceIndex}:{document.ProfessionIndex}."));
            }

            definitions.Add(
                new SkillPlannerProfessionDefinition(
                    document.Index,
                    document.RaceIndex,
                    document.ProfessionIndex,
                    tag,
                    RequireText(document.RaceName, "race name"),
                    RequireText(document.ProfessionName, "profession name")));
        }

        return definitions;
    }

    private static IReadOnlyDictionary<string, int> BuildGroupOrder(
        IReadOnlyList<SkillPlannerGroupDocument> documents)
    {
        Dictionary<string, int> result =
            new(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < documents.Count; index++)
        {
            var name = RequireText(
                documents[index].Name,
                "skill group name");

            if (!result.TryAdd(name, index))
            {
                throw new InvalidOperationException(
                    $"Duplicate skill-planner group '{name}'.");
            }
        }

        return result;
    }

    private static IReadOnlyList<SkillPlannerSkillDefinition> BuildSkills(
        IReadOnlyList<SkillPlannerSkillDocument> documents,
        IReadOnlyDictionary<string, int> groupOrder,
        IReadOnlyDictionary<int, SkillPlannerProfessionDefinition>
            professionsByIndex)
    {
        if (documents.Count == 0)
        {
            throw new InvalidOperationException(
                "Skill-planner catalog contains no skills.");
        }

        List<SkillPlannerSkillDefinition> definitions = [];
        HashSet<int> ids = [];
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);

        foreach (var document in documents)
        {
            if (document.Id < 0 || !ids.Add(document.Id))
            {
                throw new InvalidOperationException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Invalid or duplicate planner skill id {document.Id}."));
            }

            var name = RequireText(document.Name, "skill name");
            if (!names.Add(name))
            {
                throw new InvalidOperationException(
                    $"Duplicate planner skill name '{name}'.");
            }

            var category = ParseCategory(document.Category, name);
            var groupName = RequireText(document.Group, "skill group");
            if (!groupOrder.TryGetValue(groupName, out var groupIndex))
            {
                throw new InvalidOperationException(
                    $"Skill '{name}' references unknown group '{groupName}'.");
            }

            if (document.MinimumRank is < 0 or > 1)
            {
                throw new InvalidOperationException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Skill '{name}' has unsupported minimum rank {document.MinimumRank}."));
            }

            Dictionary<int, SkillPlannerProfessionSkillDefinition>
                professionRules = [];

            foreach (var rule in document.ProfessionRules)
            {
                if (!professionsByIndex.ContainsKey(rule.ProfessionIndex) ||
                    !professionRules.TryAdd(
                        rule.ProfessionIndex,
                        new SkillPlannerProfessionSkillDefinition(
                            rule.ProfessionIndex,
                            ValidateRank(rule.MaximumRank, name),
                            ParseRequirementCurve(
                                rule.RequirementCurve,
                                name),
                            Math.Max(0, rule.LearnLevel))))
                {
                    throw new InvalidOperationException(
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"Skill '{name}' has an invalid or duplicate profession rule for index {rule.ProfessionIndex}."));
                }
            }

            if (professionRules.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Skill '{name}' is not available to any profession.");
            }

            Dictionary<int, SkillPlannerRankDefinition> ranks = [];
            foreach (var rank in document.Ranks)
            {
                if (rank.Rank is < 1 or > 9 ||
                    !ranks.TryAdd(
                        rank.Rank,
                        new SkillPlannerRankDefinition(
                            rank.Rank,
                            rank.Description.Trim())))
                {
                    throw new InvalidOperationException(
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"Skill '{name}' has an invalid or duplicate rank description for rank {rank.Rank}."));
                }
            }

            definitions.Add(
                new SkillPlannerSkillDefinition(
                    document.Id,
                    name,
                    document.Description.Trim(),
                    category,
                    document.MinimumRank,
                    groupName,
                    groupIndex,
                    new ReadOnlyDictionary<
                        int,
                        SkillPlannerProfessionSkillDefinition>(professionRules),
                    new ReadOnlyDictionary<int, SkillPlannerRankDefinition>(ranks)));
        }

        return definitions
            .OrderBy(definition => definition.GroupIndex)
            .ThenBy(definition => definition.Id)
            .ToArray();
    }

    private static void ValidateGroups(
        IReadOnlyList<SkillPlannerGroupDocument> groups,
        IReadOnlyDictionary<int, SkillPlannerSkillDefinition> skillsById)
    {
        HashSet<int> groupedSkillIds = [];

        foreach (var group in groups)
        {
            foreach (var skillId in group.SkillIds)
            {
                if (!skillsById.ContainsKey(skillId))
                {
                    throw new InvalidOperationException(
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"Skill group '{group.Name}' references unknown skill id {skillId}."));
                }

                if (!groupedSkillIds.Add(skillId))
                {
                    throw new InvalidOperationException(
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"Skill id {skillId} appears in more than one planner group."));
                }
            }
        }

        if (groupedSkillIds.Count != skillsById.Count)
        {
            var missing = skillsById.Keys
                .Where(skillId => !groupedSkillIds.Contains(skillId))
                .OrderBy(skillId => skillId);

            throw new InvalidOperationException(
                $"Skill-planner groups do not cover skill ids {string.Join(", ", missing)}.");
        }
    }

    private static int ValidateRank(int rank, string skillName)
    {
        if (rank is < 1 or > 9)
        {
            throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Skill '{skillName}' has unsupported maximum rank {rank}."));
        }

        return rank;
    }

    private static SkillPlannerCategory ParseCategory(
        string value,
        string skillName)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "combat" => SkillPlannerCategory.Combat,
            "explore" => SkillPlannerCategory.Explore,
            "trade" => SkillPlannerCategory.Trade,
            "total" => SkillPlannerCategory.Overall,
            _ => throw new InvalidOperationException(
                $"Skill '{skillName}' has unknown category '{value}'."),
        };
    }

    private static SkillPlannerRequirementCurve ParseRequirementCurve(
        string value,
        string skillName)
    {
        return value.Trim() switch
        {
            "pw" => SkillPlannerRequirementCurve.PrimaryWeapon,
            "sw" => SkillPlannerRequirementCurve.SecondaryWeapon,
            "pt" => SkillPlannerRequirementCurve.PrimaryTech,
            "st" => SkillPlannerRequirementCurve.SecondaryTech,
            "non" => SkillPlannerRequirementCurve.Standard,
            "EX" => SkillPlannerRequirementCurve.Extended,
            _ => throw new InvalidOperationException(
                $"Skill '{skillName}' has unknown requirement curve '{value}'."),
        };
    }

    private static string RequireText(string value, string fieldName)
    {
        var normalized = value.Trim();
        if (normalized.Length == 0)
        {
            throw new InvalidOperationException(
                $"Skill-planner {fieldName} is empty.");
        }

        return normalized;
    }
}

internal sealed record SkillPlannerProfessionDefinition(
    int Index,
    int RaceIndex,
    int ProfessionIndex,
    string Tag,
    string RaceName,
    string ProfessionName)
{
    public string DisplayName =>
        $"{this.RaceName} {this.ProfessionName}";
}

internal sealed record SkillPlannerGroupDefinition(
    int Order,
    string Name,
    IReadOnlyList<int> SkillIds);

internal sealed record SkillPlannerSkillDefinition(
    int Id,
    string Name,
    string Description,
    SkillPlannerCategory Category,
    int MinimumRank,
    string GroupName,
    int GroupIndex,
    IReadOnlyDictionary<int, SkillPlannerProfessionSkillDefinition>
        ProfessionRules,
    IReadOnlyDictionary<int, SkillPlannerRankDefinition> Ranks)
{
    public bool TryGetProfessionRule(
        int professionIndex,
        out SkillPlannerProfessionSkillDefinition definition)
    {
        return this.ProfessionRules.TryGetValue(
            professionIndex,
            out definition!);
    }

    public string GetRankDescription(int rank)
    {
        return this.Ranks.TryGetValue(rank, out var definition)
            ? definition.Description
            : "";
    }
}

internal sealed record SkillPlannerProfessionSkillDefinition(
    int ProfessionIndex,
    int MaximumRank,
    SkillPlannerRequirementCurve RequirementCurve,
    int LearnLevel);

internal sealed record SkillPlannerRankDefinition(
    int Rank,
    string Description);

internal enum SkillPlannerCategory
{
    Combat,
    Explore,
    Trade,
    Overall,
}

internal enum SkillPlannerRequirementCurve
{
    PrimaryWeapon,
    SecondaryWeapon,
    PrimaryTech,
    SecondaryTech,
    Standard,
    Extended,
}
