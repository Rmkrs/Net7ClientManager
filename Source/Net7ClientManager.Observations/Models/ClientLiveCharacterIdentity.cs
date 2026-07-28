namespace Net7ClientManager.Observations.Models;

/// <summary>
/// One authoritative, live-only character identity for runtime consumers.
/// Every value is derived from the current read-only observation snapshot.
/// </summary>
public sealed record ClientLiveCharacterIdentity
{
    public required int ProcessId { get; init; }

    public required DateTimeOffset ObservedAt { get; init; }

    public bool IsAvailable { get; init; }

    public ClientLiveCharacterIdentityStatus Status { get; init; }

    public string StatusText { get; init; } = "";

    public uint? CharacterObjectId { get; init; }

    public string? Name { get; init; }

    public string? Race { get; init; }

    public string? Profession { get; init; }

    public string? ProfessionCode { get; init; }

    public string? FactionAffiliation { get; init; }

    public int? CombatLevel { get; init; }

    public int? ExploreLevel { get; init; }

    public int? TradeLevel { get; init; }

    public int? OverallLevel { get; init; }

    public string? GuildName { get; init; }

    public required ClientProfessionResolution ProfessionResolution
    { get; init; }

    public required ClientLiveCharacterIdentityDiagnostics Diagnostics
    { get; init; }

    public static ClientLiveCharacterIdentity Unavailable(
        int processId,
        string statusText,
        DateTimeOffset? observedAt = null)
    {
        return new ClientLiveCharacterIdentity
        {
            ProcessId = processId,
            ObservedAt = observedAt ?? DateTimeOffset.MinValue,
            Status = ClientLiveCharacterIdentityStatus.Unavailable,
            StatusText = statusText,
            ProfessionResolution =
                ClientProfessionResolver.ResolveDetailed(
                    identity: null,
                    progression: null,
                    reputation: null),
            Diagnostics = new ClientLiveCharacterIdentityDiagnostics(),
        };
    }
}
