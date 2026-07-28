namespace Net7ClientManager.SkillPlanning;

using System.Globalization;
using System.Text;

internal sealed record SkillBuildSkillsCompanionPresentation
{
    public bool HasLiveContext { get; init; }

    public bool HasActiveBuild { get; init; }

    public string Fingerprint { get; init; } = "hidden";

    public string StructureFingerprint { get; init; } = "hidden";

    public string BuildTitle { get; init; } = "Build";

    public string StatusText { get; init; } = "Waiting for the live character.";

    public IReadOnlyList<SkillBuildSkillsCompanionRow> Skills { get; init; } = [];

    public static SkillBuildSkillsCompanionPresentation Hidden { get; } = new();

    public static SkillBuildSkillsCompanionPresentation Create(
        SkillBuildBoardPresentation live,
        SkillBuildLocalWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(live);
        ArgumentNullException.ThrowIfNull(workspace);

        if (!live.HasBuildContext ||
            live.Baseline == null ||
            live.CharacterId == 0)
        {
            return new SkillBuildSkillsCompanionPresentation
            {
                HasLiveContext = false,
                Fingerprint = string.Concat("unavailable:", live.Fingerprint),
                StructureFingerprint = "unavailable",
                StatusText = string.IsNullOrWhiteSpace(live.StatusText)
                    ? "Waiting for the live character and skills."
                    : live.StatusText,
            };
        }

        var library = workspace.GetLibrary(
            live.CharacterId,
            live.Baseline.ProfessionIndex);
        var build = library.ActiveBuild;
        if (build == null)
        {
            return new SkillBuildSkillsCompanionPresentation
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
        var rows = analysis.Skills
            .Select(skill => new SkillBuildSkillsCompanionRow(
                skill.Skill.Id,
                skill.Skill.Name,
                skill.Skill.Description,
                skill.CurrentRank.GetValueOrDefault(skill.StartingRank),
                skill.TargetRank,
                skill.MaximumRank))
            .ToArray();

        var structure = new StringBuilder(256)
            .Append(build.BuildId)
            .Append('|')
            .Append(live.Baseline.ProfessionIndex);
        var fingerprint = new StringBuilder(512)
            .Append(build.BuildId)
            .Append('|')
            .Append(build.Title)
            .Append('|')
            .Append(live.Fingerprint);

        foreach (var row in rows)
        {
            structure.Append('|')
                .Append(row.SkillId)
                .Append(':')
                .Append(row.MaximumRank);
            fingerprint.Append('|')
                .Append(row.SkillId)
                .Append(':')
                .Append(row.CurrentRank)
                .Append('/')
                .Append(row.TargetRank)
                .Append('/')
                .Append(row.MaximumRank);
        }

        return new SkillBuildSkillsCompanionPresentation
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
            Skills = rows,
        };
    }
}

internal sealed record SkillBuildSkillsCompanionRow(
    int SkillId,
    string SkillName,
    string Description,
    int CurrentRank,
    int TargetRank,
    int MaximumRank);
