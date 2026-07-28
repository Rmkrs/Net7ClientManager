namespace Net7ClientManager.Contributions;

using Net7ClientManager.Addons.Registry;

internal sealed record ForgeAddonPublicationRequest
{
    public int ProtocolVersion { get; init; } = 1;

    public required string ContributorId { get; init; }

    public required string RequestId { get; init; }

    public DateTimeOffset SubmittedAtUtc { get; init; }

    public required string ClientVersion { get; init; }

    public required string LivePilotName { get; init; }

    public required string AddonId { get; init; }

    public required string Version { get; init; }

    public string? Summary { get; init; }

    public required string SourcePackageSha256 { get; init; }

    public required string SourcePackageBase64 { get; init; }

    public string Signature { get; init; } = "";
}

internal sealed record ForgeAddonPublicationResponse(
    string RequestId,
    AddonRegistryRelease Release,
    bool AlreadyPublished);
