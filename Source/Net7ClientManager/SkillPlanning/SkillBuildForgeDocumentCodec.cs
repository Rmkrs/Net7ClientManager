namespace Net7ClientManager.SkillPlanning;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Net7ClientManager.Contributions;

internal sealed record PreparedSkillBuildForgeDocument(
    ForgeBuildContentDocument Content,
    string CanonicalJson,
    string ContentSha256);

internal static class SkillBuildForgeDocumentCodec
{
    private static readonly JsonSerializerOptions options = CreateOptions();

    public static PreparedSkillBuildForgeDocument Prepare(
        SkillBuildDocument build)
    {
        ArgumentNullException.ThrowIfNull(build);
        var content = Normalize(
            new ForgeBuildContentDocument
            {
                SchemaVersion = 1,
                ProfessionIndex = build.ProfessionIndex,
                Title = build.Title,
                Summary = build.Summary,
                Notes = build.Notes,
                Equipment = build.Equipment
                    .Select(requirement =>
                        new ForgeBuildEquipmentRequirementDocument
                        {
                            Kind = ToForgeKind(requirement.Kind),
                            Order = requirement.Order,
                            Alternatives = requirement.Alternatives
                                .Select(alternative =>
                                    new ForgeBuildEquipmentAlternativeDocument
                                    {
                                        ItemTemplateId = alternative.ItemTemplateId,
                                        ItemName = alternative.ItemName,
                                        Notes = alternative.Notes,
                                    })
                                .ToArray(),
                        })
                    .ToArray(),
                RecommendedSkills = build.RecommendedSkills
                    .Select(recommendation =>
                        new ForgeBuildSkillRecommendationDocument(
                            recommendation.SkillId,
                            recommendation.TargetRank))
                    .ToArray(),
            });
        var json = JsonSerializer.Serialize(content, options);
        return new PreparedSkillBuildForgeDocument(
            content,
            json,
            ComputeSha256(Encoding.UTF8.GetBytes(json)));
    }

    public static SkillBuildDocument ToLocalBuild(
        ForgeBuildContentDocument content,
        string localBuildId)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(localBuildId);
        var normalized = Normalize(content);
        return new SkillBuildDocument
        {
            SchemaVersion = SkillBuildDocument.CurrentSchemaVersion,
            BuildId = localBuildId.Trim(),
            ProfessionIndex = normalized.ProfessionIndex,
            Title = normalized.Title,
            Summary = normalized.Summary,
            Notes = normalized.Notes,
            Equipment = normalized.Equipment
                .Select(requirement =>
                    new SkillBuildEquipmentRequirement
                    {
                        RequirementId = Guid.NewGuid().ToString("N"),
                        Kind = FromForgeKind(requirement.Kind),
                        Order = requirement.Order,
                        Alternatives = requirement.Alternatives
                            .Select(alternative =>
                                new SkillBuildEquipmentAlternative
                                {
                                    ItemTemplateId = alternative.ItemTemplateId,
                                    ItemName = alternative.ItemName,
                                    Notes = alternative.Notes,
                                })
                            .ToArray(),
                    })
                .ToArray(),
            RecommendedSkills = normalized.RecommendedSkills
                .Select(recommendation =>
                    new SkillBuildSkillRecommendation(
                        recommendation.SkillId,
                        recommendation.TargetRank))
                .ToArray(),
        };
    }

    private static ForgeBuildContentDocument Normalize(
        ForgeBuildContentDocument content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var kindCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var equipment = (content.Equipment ?? [])
            .Select((requirement, index) => new
            {
                Requirement = requirement,
                InputIndex = index,
                Kind = NormalizeKind(requirement.Kind),
            })
            .OrderBy(value => GetKindSortKey(value.Kind))
            .ThenBy(value => value.Requirement.Order > 0
                ? value.Requirement.Order
                : value.InputIndex + 1)
            .ThenBy(value => value.InputIndex)
            .Select(value =>
            {
                var count = kindCounts.TryGetValue(value.Kind, out var current)
                    ? current + 1
                    : 1;
                kindCounts[value.Kind] = count;
                return new ForgeBuildEquipmentRequirementDocument
                {
                    Kind = value.Kind,
                    Order = count,
                    Alternatives = (value.Requirement.Alternatives ?? [])
                        .Select(alternative =>
                            new ForgeBuildEquipmentAlternativeDocument
                            {
                                ItemTemplateId = alternative.ItemTemplateId,
                                ItemName = NormalizeSingleLine(alternative.ItemName),
                                Notes = NormalizeMultiLine(alternative.Notes),
                            })
                        .ToArray(),
                };
            })
            .ToArray();

        var skills = (content.RecommendedSkills ?? [])
            .OrderBy(value => value.SkillId)
            .Select(value => new ForgeBuildSkillRecommendationDocument(
                value.SkillId,
                value.TargetRank))
            .ToArray();

        return new ForgeBuildContentDocument
        {
            SchemaVersion = 1,
            ProfessionIndex = content.ProfessionIndex,
            Title = NormalizeSingleLine(content.Title),
            Summary = NormalizeSingleLine(content.Summary),
            Notes = NormalizeMultiLine(content.Notes),
            Equipment = equipment,
            RecommendedSkills = skills,
        };
    }

    private static string ToForgeKind(SkillBuildEquipmentKind kind) =>
        kind switch
        {
            SkillBuildEquipmentKind.Weapon => "weapon",
            SkillBuildEquipmentKind.Shield => "shield",
            SkillBuildEquipmentKind.Reactor => "reactor",
            SkillBuildEquipmentKind.Engine => "engine",
            SkillBuildEquipmentKind.Device => "device",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    private static SkillBuildEquipmentKind FromForgeKind(string value) =>
        NormalizeKind(value) switch
        {
            "weapon" => SkillBuildEquipmentKind.Weapon,
            "shield" => SkillBuildEquipmentKind.Shield,
            "reactor" => SkillBuildEquipmentKind.Reactor,
            "engine" => SkillBuildEquipmentKind.Engine,
            "device" => SkillBuildEquipmentKind.Device,
            _ => throw new InvalidOperationException("Unknown Forge build equipment kind."),
        };

    private static string NormalizeKind(string? value) =>
        value?.Trim().ToLowerInvariant() ?? "";

    private static int GetKindSortKey(string kind) =>
        kind switch
        {
            "weapon" => 0,
            "shield" => 1,
            "reactor" => 2,
            "engine" => 3,
            "device" => 4,
            _ => int.MaxValue,
        };

    private static string NormalizeSingleLine(string? value) =>
        (value ?? "")
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();

    private static string NormalizeMultiLine(string? value) =>
        (value ?? "")
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();

    private static string ComputeSha256(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static JsonSerializerOptions CreateOptions() =>
        new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            WriteIndented = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
}
