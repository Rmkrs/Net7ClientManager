namespace Net7ClientManager.Observations.Models;

/// <summary>
/// Diagnostic result for resolving one of the nine player professions from
/// independent live observation sources. A known reputation affiliation is
/// authoritative and may retain lower-priority disagreements for diagnostics;
/// otherwise conflicting candidates deliberately produce no resolved
/// profession.
/// </summary>
public sealed record ClientProfessionResolution
{
    public string? Profession { get; init; }

    public ClientProfessionResolutionStatus Status { get; init; }

    public ClientProfessionResolutionSource Source { get; init; }

    public string? ReputationAffiliation { get; init; }

    public string? FactionIdentifier { get; init; }

    public int? RaceRaw { get; init; }

    public int? ProfessionRaw { get; init; }

    public string? AffiliationCandidate { get; init; }

    public string? FactionIdentifierCandidate { get; init; }

    public string? RaceProfessionCandidate { get; init; }

    public IReadOnlyList<string> ConflictingCandidates { get; init; } = [];

    public bool IsResolved =>
        this.Status is ClientProfessionResolutionStatus.Resolved or
            ClientProfessionResolutionStatus.ResolvedAndCorroborated;
}
