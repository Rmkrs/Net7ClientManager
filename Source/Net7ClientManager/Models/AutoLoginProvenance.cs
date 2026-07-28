namespace Net7ClientManager.Models;

/// <summary>
/// Records the account login that Net7 Client Manager actually submitted for
/// this process. It is automation provenance, not live character identity.
/// </summary>
public sealed record AutoLoginProvenance
{
    public required Guid AccountId { get; init; }

    public required string LoginName { get; init; }

    public required DateTimeOffset SubmittedAt { get; init; }

    public DateTimeOffset? ConfirmedAt { get; init; }

    public bool IsConfirmed =>
        this.ConfirmedAt.HasValue;
}
