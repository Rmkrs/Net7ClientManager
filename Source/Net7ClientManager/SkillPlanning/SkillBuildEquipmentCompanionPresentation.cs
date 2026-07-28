namespace Net7ClientManager.SkillPlanning;

using System.Globalization;
using System.Text;

internal sealed record SkillBuildEquipmentCompanionPresentation
{
    public bool HasLiveContext { get; init; }

    public bool HasActiveBuild { get; init; }

    public string Fingerprint { get; init; } = "hidden";

    public string StructureFingerprint { get; init; } = "hidden";

    public string BuildTitle { get; init; } = "Build";

    public string StatusText { get; init; } = "Waiting for the live character.";

    public IReadOnlyList<SkillBuildEquipmentCompanionRow> Equipment { get; init; } = [];

    public static SkillBuildEquipmentCompanionPresentation Hidden { get; } = new();

    public static SkillBuildEquipmentCompanionPresentation Create(
        SkillBuildBoardPresentation live,
        SkillBuildLocalWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(live);
        ArgumentNullException.ThrowIfNull(workspace);

        if (!live.HasBuildContext ||
            live.Baseline == null ||
            live.CharacterId == 0)
        {
            return new SkillBuildEquipmentCompanionPresentation
            {
                HasLiveContext = false,
                Fingerprint = string.Concat("unavailable:", live.Fingerprint),
                StructureFingerprint = "unavailable",
                StatusText = string.IsNullOrWhiteSpace(live.StatusText)
                    ? "Waiting for the live character and equipment."
                    : live.StatusText,
            };
        }

        var library = workspace.GetLibrary(
            live.CharacterId,
            live.Baseline.ProfessionIndex);
        var build = library.ActiveBuild;
        if (build == null)
        {
            return new SkillBuildEquipmentCompanionPresentation
            {
                HasLiveContext = true,
                HasActiveBuild = false,
                Fingerprint = string.Create(
                    CultureInfo.InvariantCulture,
                    $"empty:{live.CharacterId}:{live.Baseline.ProfessionIndex}:{live.Fingerprint}"),
                StructureFingerprint = string.Create(
                    CultureInfo.InvariantCulture,
                    $"empty:{live.Baseline.ProfessionIndex}"),
                StatusText = "No active build is selected for this pilot.",
            };
        }

        var analysis = workspace.Board.Analyze(
            build,
            live.Baseline,
            live.EquipmentBaseline);
        var rows = analysis.Equipment
            .Select(CreateRow)
            .ToArray();

        var structure = new StringBuilder(512)
            .Append(build.BuildId)
            .Append('|')
            .Append(live.Baseline.ProfessionIndex);
        var fingerprint = new StringBuilder(768)
            .Append(build.BuildId)
            .Append('|')
            .Append(build.Title)
            .Append('|')
            .Append(live.Fingerprint);

        foreach (var row in rows)
        {
            structure.Append('|')
                .Append(row.RequirementId)
                .Append(':')
                .Append(row.Kind)
                .Append(':')
                .Append(row.Order)
                .Append(':')
                .Append(row.PreferredItemTemplateId)
                .Append(':')
                .Append(row.PreferredItemName);

            foreach (var alternative in row.Alternatives)
            {
                structure.Append(',')
                    .Append(alternative.ItemTemplateId)
                    .Append(':')
                    .Append(alternative.ItemName);
            }

            fingerprint.Append('|')
                .Append(row.RequirementId)
                .Append(':')
                .Append(row.Availability)
                .Append(':')
                .Append(row.EffectiveItemTemplateId)
                .Append(':')
                .Append(row.EffectiveItemName);
        }

        return new SkillBuildEquipmentCompanionPresentation
        {
            HasLiveContext = true,
            HasActiveBuild = true,
            Fingerprint = fingerprint.ToString(),
            StructureFingerprint = structure.ToString(),
            BuildTitle = string.IsNullOrWhiteSpace(build.Title)
                ? "Untitled build"
                : build.Title.Trim(),
            StatusText = analysis.Issues.Count == 0
                ? "Comparing the active build with this pilot."
                : analysis.Issues[0],
            Equipment = rows,
        };
    }

    private static SkillBuildEquipmentCompanionRow CreateRow(
        SkillBuildEquipmentRequirementAnalysis analysis)
    {
        var preferred = analysis.Requirement.Alternatives.FirstOrDefault();
        var effective = analysis.EffectiveChoice ?? preferred;
        var alternatives = analysis.Requirement.Alternatives
            .Select((alternative, index) =>
                new SkillBuildEquipmentCompanionAlternative(
                    alternative.ItemTemplateId,
                    FormatItemName(
                        alternative.ItemTemplateId,
                        alternative.ItemName),
                    index == 0))
            .ToArray();

        return new SkillBuildEquipmentCompanionRow(
            analysis.Requirement.RequirementId,
            analysis.Requirement.Kind,
            analysis.Requirement.Order,
            preferred?.ItemTemplateId ?? 0,
            FormatItemName(
                preferred?.ItemTemplateId ?? 0,
                preferred?.ItemName),
            effective?.ItemTemplateId ?? 0,
            FormatItemName(
                effective?.ItemTemplateId ?? 0,
                effective?.ItemName),
            analysis.Availability,
            alternatives);
    }

    private static string FormatItemName(
        int itemTemplateId,
        string? itemName)
    {
        if (!string.IsNullOrWhiteSpace(itemName))
        {
            return itemName.Trim();
        }

        return itemTemplateId > 0
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"Item #{itemTemplateId}")
            : "Choose equipment";
    }
}

internal sealed record SkillBuildEquipmentCompanionRow(
    string RequirementId,
    SkillBuildEquipmentKind Kind,
    int Order,
    int PreferredItemTemplateId,
    string PreferredItemName,
    int EffectiveItemTemplateId,
    string EffectiveItemName,
    SkillBuildEquipmentAvailability Availability,
    IReadOnlyList<SkillBuildEquipmentCompanionAlternative> Alternatives)
{
    public bool UsesAcceptedAlternative =>
        this.Availability != SkillBuildEquipmentAvailability.Missing &&
        this.EffectiveItemTemplateId > 0 &&
        this.EffectiveItemTemplateId != this.PreferredItemTemplateId;
}

internal sealed record SkillBuildEquipmentCompanionAlternative(
    int ItemTemplateId,
    string ItemName,
    bool IsPreferred);
