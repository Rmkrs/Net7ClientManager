namespace Net7ClientManager.Navigation;

public sealed record NavigationWormholeDestination
{
    public required string SkillFamilyName { get; init; }

    public required string AbilityName { get; init; }

    public required string SectorKey { get; init; }

    public required int RequiredRank { get; init; }

    /// <summary>
    /// Zero-based position in the native right-click destination menu for the
    /// skill family. The UI automation still verifies the selected ability
    /// after clicking, so this is a preference rather than blind trust.
    /// </summary>
    public required int MenuIndex { get; init; }
}
