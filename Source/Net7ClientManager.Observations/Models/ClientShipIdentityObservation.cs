namespace Net7ClientManager.Observations.Models;

public sealed record ClientShipIdentityObservation
{
    public string? Name { get; init; }

    public string? Owner { get; init; }

    public string? Title { get; init; }

    public string? Rank { get; init; }

    public string? FactionIdentifier { get; init; }

    public string? ProfessionName { get; init; }

    public string? GuildName { get; init; }

    public string? GuildRankName { get; init; }

    public int? CombatLevel { get; init; }

    public int? GuildRankRaw { get; init; }
}
