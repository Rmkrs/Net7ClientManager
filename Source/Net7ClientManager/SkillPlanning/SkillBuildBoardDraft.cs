namespace Net7ClientManager.SkillPlanning;

internal sealed class SkillBuildBoardDraft
{
    private SkillBuildBoardDraft(
        string buildId,
        int professionIndex,
        string title,
        string summary,
        string notes)
    {
        this.BuildId = buildId;
        this.ProfessionIndex = professionIndex;
        this.Title = title;
        this.Summary = summary;
        this.Notes = notes;
    }

    public string BuildId { get; }

    public int ProfessionIndex { get; }

    public string Title { get; set; }

    public string Summary { get; set; }

    public string Notes { get; set; }

    public List<SkillBuildEquipmentRequirementDraft> Equipment { get; } = [];

    public Dictionary<int, int> RecommendedSkillRanks { get; } = [];

    public static SkillBuildBoardDraft CreateEmpty(
        int professionIndex,
        string title) =>
        new(
            Guid.NewGuid().ToString("N"),
            professionIndex,
            title,
            "",
            "");

    public static SkillBuildBoardDraft CreateFromCurrent(
        int professionIndex,
        string title,
        SkillBuildEquipmentBaseline equipment)
    {
        var result = CreateEmpty(professionIndex, title);
        if (!equipment.IsAvailable)
        {
            return result;
        }

        foreach (var pair in equipment.Items
                     .OrderBy(value => GetSortKey(value.Key)))
        {
            result.Equipment.Add(
                new SkillBuildEquipmentRequirementDraft
                {
                    RequirementId = CreateRequirementId(pair.Key.Kind),
                    Kind = pair.Key.Kind,
                    Order = pair.Key.Ordinal,
                    Alternatives =
                    [
                        new SkillBuildEquipmentAlternative
                        {
                            ItemTemplateId = pair.Value.ItemTemplateId,
                            ItemName = pair.Value.ItemName,
                        },
                    ],
                });
        }

        result.NormalizeOrders();
        return result;
    }

    public static SkillBuildBoardDraft FromDocument(
        SkillBuildDocument build)
    {
        ArgumentNullException.ThrowIfNull(build);

        var result = new SkillBuildBoardDraft(
            build.BuildId,
            build.ProfessionIndex,
            build.Title,
            build.Summary,
            build.Notes);

        result.Equipment.AddRange(
            build.Equipment.Select(value =>
                new SkillBuildEquipmentRequirementDraft
                {
                    RequirementId = value.RequirementId,
                    Kind = value.Kind,
                    Order = value.Order,
                    Alternatives = value.Alternatives
                        .Select(CloneAlternative)
                        .ToList(),
                }));

        foreach (var recommendation in build.RecommendedSkills)
        {
            result.RecommendedSkillRanks[recommendation.SkillId] =
                recommendation.TargetRank;
        }

        result.NormalizeOrders();
        return result;
    }

    public static SkillBuildBoardDraft CreateCopy(
        SkillBuildDocument build)
    {
        ArgumentNullException.ThrowIfNull(build);

        var result = new SkillBuildBoardDraft(
            Guid.NewGuid().ToString("N"),
            build.ProfessionIndex,
            string.Concat(build.Title, " copy"),
            build.Summary,
            build.Notes);
        result.Equipment.AddRange(
            build.Equipment.Select(value =>
                new SkillBuildEquipmentRequirementDraft
                {
                    RequirementId = CreateRequirementId(value.Kind),
                    Kind = value.Kind,
                    Order = value.Order,
                    Alternatives = value.Alternatives
                        .Select(CloneAlternative)
                        .ToList(),
                }));
        foreach (var recommendation in build.RecommendedSkills)
        {
            result.RecommendedSkillRanks[recommendation.SkillId] =
                recommendation.TargetRank;
        }
        result.NormalizeOrders();
        return result;
    }

    public SkillBuildDocument ToDocument() =>
        new()
        {
            BuildId = this.BuildId,
            ProfessionIndex = this.ProfessionIndex,
            Title = this.Title.Trim(),
            Summary = this.Summary.Trim(),
            Notes = this.Notes.Trim(),
            Equipment = this.Equipment
                .Where(value => value.Alternatives.Count > 0)
                .OrderBy(value => GetKindSortKey(value.Kind))
                .ThenBy(value => value.Order)
                .Select(value => value.ToDocument())
                .ToArray(),
            RecommendedSkills = this.RecommendedSkillRanks
                .Where(value => value.Value > 0)
                .OrderBy(value => value.Key)
                .Select(value =>
                    new SkillBuildSkillRecommendation(
                        value.Key,
                        value.Value))
                .ToArray(),
        };

    public SkillBuildEquipmentRequirementDraft AddRequirement(
        SkillBuildEquipmentKind kind,
        SkillBuildEquipmentAlternative alternative)
    {
        ArgumentNullException.ThrowIfNull(alternative);

        if (kind is SkillBuildEquipmentKind.Shield or
            SkillBuildEquipmentKind.Reactor or
            SkillBuildEquipmentKind.Engine)
        {
            var existing = this.Equipment.FirstOrDefault(value => value.Kind == kind);
            if (existing != null)
            {
                existing.Alternatives.Clear();
                existing.Alternatives.Add(CloneAlternative(alternative));
                return existing;
            }
        }

        var result = new SkillBuildEquipmentRequirementDraft
        {
            RequirementId = CreateRequirementId(kind),
            Kind = kind,
            Order = this.Equipment.Count(value => value.Kind == kind) + 1,
            Alternatives = [CloneAlternative(alternative)],
        };
        this.Equipment.Add(result);
        this.NormalizeOrders();
        return result;
    }

    public void RemoveRequirement(string requirementId)
    {
        this.Equipment.RemoveAll(value => string.Equals(
            value.RequirementId,
            requirementId,
            StringComparison.Ordinal));
        this.NormalizeOrders();
    }

    public bool ReplacePrimaryAlternative(
        string requirementId,
        SkillBuildEquipmentAlternative replacement)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requirementId);
        ArgumentNullException.ThrowIfNull(replacement);

        var requirement = this.Equipment.FirstOrDefault(value => string.Equals(
            value.RequirementId,
            requirementId,
            StringComparison.Ordinal));
        if (requirement == null)
        {
            return false;
        }

        var previousPrimary = requirement.Alternatives.FirstOrDefault();
        requirement.Alternatives.RemoveAll(value =>
            value.ItemTemplateId == replacement.ItemTemplateId ||
            previousPrimary != null &&
            value.ItemTemplateId == previousPrimary.ItemTemplateId);
        requirement.Alternatives.Insert(0, CloneAlternative(replacement));
        return true;
    }

    public void SetRecommendedRank(
        int skillId,
        int rank,
        int equipmentMinimumRank)
    {
        if (rank <= equipmentMinimumRank)
        {
            this.RecommendedSkillRanks.Remove(skillId);
            return;
        }

        this.RecommendedSkillRanks[skillId] = rank;
    }

    private void NormalizeOrders()
    {
        foreach (var kind in Enum.GetValues<SkillBuildEquipmentKind>())
        {
            var values = this.Equipment
                .Where(value => value.Kind == kind)
                .OrderBy(value => value.Order)
                .ThenBy(value => value.RequirementId, StringComparer.Ordinal)
                .ToArray();

            for (var index = 0; index < values.Length; index++)
            {
                values[index].Order = index + 1;
            }
        }
    }

    private static SkillBuildEquipmentAlternative CloneAlternative(
        SkillBuildEquipmentAlternative value) =>
        new()
        {
            ItemTemplateId = value.ItemTemplateId,
            ItemName = value.ItemName,
            Notes = value.Notes,
        };

    private static string CreateRequirementId(SkillBuildEquipmentKind kind) =>
        string.Concat(
            kind.ToString().ToLowerInvariant(),
            "-",
            Guid.NewGuid().ToString("N"));

    private static int GetKindSortKey(SkillBuildEquipmentKind kind) =>
        kind switch
        {
            SkillBuildEquipmentKind.Weapon => 0,
            SkillBuildEquipmentKind.Shield => 1,
            SkillBuildEquipmentKind.Reactor => 2,
            SkillBuildEquipmentKind.Engine => 3,
            SkillBuildEquipmentKind.Device => 4,
            _ => int.MaxValue,
        };

    private static int GetSortKey(SkillBuildEquipmentSlot slot) =>
        checked((GetKindSortKey(slot.Kind) * 100) + slot.Ordinal);
}

internal sealed class SkillBuildEquipmentRequirementDraft
{
    public string RequirementId { get; set; } = "";

    public SkillBuildEquipmentKind Kind { get; set; }

    public int Order { get; set; }

    public List<SkillBuildEquipmentAlternative> Alternatives { get; set; } = [];

    public SkillBuildEquipmentRequirement ToDocument() =>
        new()
        {
            RequirementId = this.RequirementId,
            Kind = this.Kind,
            Order = this.Order,
            Alternatives = this.Alternatives
                .Select(value =>
                    new SkillBuildEquipmentAlternative
                    {
                        ItemTemplateId = value.ItemTemplateId,
                        ItemName = value.ItemName,
                        Notes = value.Notes,
                    })
                .ToArray(),
        };
}
