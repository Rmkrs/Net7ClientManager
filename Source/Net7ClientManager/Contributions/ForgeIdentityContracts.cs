namespace Net7ClientManager.Contributions;

internal sealed record ForgeContributorRecoveryStartRequest
{
    public int ProtocolVersion { get; init; } = 1;

    public required string PublicKey { get; init; }

    public required string LivePilotName { get; init; }

    public string? DeviceLabel { get; init; }

    public string Signature { get; init; } = "";
}

internal sealed record ForgeContributorRecoveryStartResponse(
    string RecoveryRequestId,
    string RecoveryCode,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc);

internal sealed record ForgeContributorRecoveryStatusResponse(
    string RecoveryRequestId,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? ResolvedAtUtc,
    string? ContributorId);

internal sealed record ForgeIdentityStatusSnapshot(
    bool HasIdentity,
    string? ContributorId,
    string? RecoveryRequestId,
    string? RecoveryCode,
    string? RecoveryStatus,
    string? RecoveryPilotName,
    DateTimeOffset? RecoveryCreatedAtUtc,
    DateTimeOffset? RecoveryExpiresAtUtc,
    DateTimeOffset? RecoveryResolvedAtUtc)
{
    public bool HasPendingRecovery =>
        !string.IsNullOrWhiteSpace(this.RecoveryRequestId) &&
        string.Equals(this.RecoveryStatus, "pending", StringComparison.OrdinalIgnoreCase);
}

internal class ForgeIdentityException : InvalidOperationException
{
    public ForgeIdentityException(string message)
        : base(message)
    {
    }
}

internal sealed class ForgeIdentityRequiredException : ForgeIdentityException
{
    public ForgeIdentityRequiredException()
        : base(
            "Log into a character once before using Forge.")
    {
    }
}

internal sealed class ForgeIdentityRecoveryPendingException : ForgeIdentityException
{
    public ForgeIdentityRecoveryPendingException(
        ForgeIdentityStatusSnapshot status)
        : base(CreateMessage(status))
    {
        this.Status = status;
    }

    public ForgeIdentityStatusSnapshot Status { get; }

    private static string CreateMessage(ForgeIdentityStatusSnapshot status)
    {
        return string.IsNullOrWhiteSpace(status.RecoveryCode)
            ? "Forge is waiting for Huron to approve this installation."
            : string.Concat(
                "Forge is waiting for Huron to approve code ",
                status.RecoveryCode,
                ". Open Forge Contributions to copy the forum message.");
    }
}

internal sealed class ForgeIdentityApiException : InvalidOperationException
{
    public ForgeIdentityApiException(
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
