namespace Net7ClientManager.Addons.Contracts;

public sealed record AddonCharacterIdentitySnapshot
{
    public uint? Id { get; init; }

    public string? Name { get; init; }

    public string? OwnerName { get; init; }

    public string? Title { get; init; }

    public string? Rank { get; init; }

    public string? Race { get; init; }

    public string? Profession { get; init; }

    public string? ProfessionCode { get; init; }

    public string? Affiliation { get; init; }

    public string? ResolutionStatus { get; init; }

    public string? ResolutionMessage { get; init; }

    public string? GuildName { get; init; }

    public string? GuildRank { get; init; }

    public int? CombatLevel { get; init; }
}
