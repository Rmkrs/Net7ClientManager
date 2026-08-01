namespace Net7ClientManager.Addons.Contracts;

public sealed record AddonNavigationWormholeSnapshot
{
    public required string SkillFamilyName { get; init; }

    public required string AbilityName { get; init; }

    public required int RequiredRank { get; init; }

    public bool HasReadyCaster { get; init; }

    public IReadOnlyList<string> CasterNames { get; init; } = [];
}
