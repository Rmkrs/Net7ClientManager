namespace Net7ClientManager.Contributions;

internal sealed record ForgeBuildContentDocument
{
    public int SchemaVersion { get; init; } = 1;

    public int ProfessionIndex { get; init; }

    public string Title { get; init; } = "";

    public string Summary { get; init; } = "";

    public string Notes { get; init; } = "";

    public IReadOnlyList<ForgeBuildEquipmentRequirementDocument> Equipment { get; init; } = [];

    public IReadOnlyList<ForgeBuildSkillRecommendationDocument> RecommendedSkills { get; init; } = [];
}

internal sealed record ForgeBuildEquipmentRequirementDocument
{
    public string Kind { get; init; } = "";

    public int Order { get; init; }

    public IReadOnlyList<ForgeBuildEquipmentAlternativeDocument> Alternatives { get; init; } = [];
}

internal sealed record ForgeBuildEquipmentAlternativeDocument
{
    public int ItemTemplateId { get; init; }

    public string ItemName { get; init; } = "";

    public string Notes { get; init; } = "";
}

internal sealed record ForgeBuildSkillRecommendationDocument(
    int SkillId,
    int TargetRank);

internal sealed record ForgeBuildPublicationRequest
{
    public int ProtocolVersion { get; init; } = 1;

    public string ContributorId { get; init; } = "";

    public string RequestId { get; init; } = "";

    public DateTimeOffset SubmittedAtUtc { get; init; }

    public string ClientVersion { get; init; } = "";

    public string LivePilotName { get; init; } = "";

    public string? BuildId { get; init; }

    public string DocumentSha256 { get; init; } = "";

    public string DocumentJson { get; init; } = "";

    public string Signature { get; init; } = "";
}

internal sealed record ForgeBuildPublicationResponse(
    string RequestId,
    string BuildId,
    int Version,
    string ContentSha256,
    string PublisherPilotName,
    DateTimeOffset PublishedAtUtc,
    int StarCount);

internal sealed record ForgeBuildSearchRequest
{
    public int ProtocolVersion { get; init; } = 1;

    public string ContributorId { get; init; } = "";

    public string RequestId { get; init; } = "";

    public DateTimeOffset SubmittedAtUtc { get; init; }

    public string ClientVersion { get; init; } = "";

    public string LivePilotName { get; init; } = "";

    public string Query { get; init; } = "";

    public int? ProfessionIndex { get; init; }

    public string PublisherPilotName { get; init; } = "";

    public bool StarredOnly { get; init; }

    public bool OwnedOnly { get; init; }

    public string Sort { get; init; } = "relevance";

    public int Offset { get; init; }

    public int Limit { get; init; } = 50;

    public string Signature { get; init; } = "";
}

internal sealed record ForgeBuildSearchResponse(
    int TotalCount,
    IReadOnlyList<ForgeBuildSummaryResponse> Builds);

internal sealed record ForgeBuildSummaryResponse(
    string BuildId,
    string Title,
    string Summary,
    string PublisherPilotName,
    int ProfessionIndex,
    int LatestVersion,
    DateTimeOffset LatestPublishedAtUtc,
    int StarCount,
    bool IsStarredByMe,
    bool IsOwnedByMe);

internal sealed record ForgeBuildAccessRequest
{
    public int ProtocolVersion { get; init; } = 1;

    public string ContributorId { get; init; } = "";

    public string RequestId { get; init; } = "";

    public DateTimeOffset SubmittedAtUtc { get; init; }

    public string ClientVersion { get; init; } = "";

    public string LivePilotName { get; init; } = "";

    public string BuildId { get; init; } = "";

    public int? Version { get; init; }

    public string Signature { get; init; } = "";
}

internal sealed record ForgeBuildDetailsResponse(
    string BuildId,
    string Title,
    string Summary,
    string Notes,
    string PublisherPilotName,
    int ProfessionIndex,
    DateTimeOffset CreatedAtUtc,
    int LatestVersion,
    int StarCount,
    bool IsStarredByMe,
    bool IsOwnedByMe,
    IReadOnlyList<ForgeBuildVersionSummaryResponse> Versions);

internal sealed record ForgeBuildVersionSummaryResponse(
    int Version,
    string Title,
    string Summary,
    DateTimeOffset PublishedAtUtc,
    string ContentSha256);

internal sealed record ForgeBuildVersionResponse(
    string BuildId,
    int Version,
    string PublisherPilotName,
    DateTimeOffset PublishedAtUtc,
    string ContentSha256,
    int LatestVersion,
    int StarCount,
    bool IsStarredByMe,
    bool IsOwnedByMe,
    ForgeBuildContentDocument Content);

internal sealed record ForgeBuildStarRequest
{
    public int ProtocolVersion { get; init; } = 1;

    public string ContributorId { get; init; } = "";

    public string RequestId { get; init; } = "";

    public DateTimeOffset SubmittedAtUtc { get; init; }

    public string ClientVersion { get; init; } = "";

    public string LivePilotName { get; init; } = "";

    public string BuildId { get; init; } = "";

    public bool Starred { get; init; }

    public string Signature { get; init; } = "";
}

internal sealed record ForgeBuildStarResponse(
    string BuildId,
    bool Starred,
    int StarCount);

internal sealed class ForgeBuildApiException : Exception
{
    public ForgeBuildApiException(
        string code,
        string message,
        int statusCode)
        : base(message)
    {
        this.Code = code;
        this.StatusCode = statusCode;
    }

    public string Code { get; }

    public int StatusCode { get; }
}
