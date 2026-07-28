namespace Net7ClientManager.Observations.Models;

public sealed record ClientCharacterProgressionObservation
{
    public bool IsAvailable { get; init; }

    public string Status { get; init; } = "";

    public uint AuxDataAddress { get; init; }

    public uint AuxDataLookupAddress { get; init; }

    public uint RpgInfoAddress { get; init; }

    public int LookupTraversalNodeCount { get; init; }

    public int? Race { get; init; }

    public int? Profession { get; init; }

    public ClientCharacterExperienceTrackObservation Combat { get; init; } =
        new();

    public ClientCharacterExperienceTrackObservation Explore { get; init; } =
        new();

    public ClientCharacterExperienceTrackObservation Trade { get; init; } =
        new();

    public int? CombatLevel =>
        this.Combat.Level;

    public int? ExploreLevel =>
        this.Explore.Level;

    public int? TradeLevel =>
        this.Trade.Level;

    public int? SkillPoints { get; init; }

    public int? HullUpgradeLevel { get; init; }

    public int? HullTier { get; init; }

    public int? CurrentHullUpgradeOverallLevel { get; init; }

    public int? NextHullUpgradeOverallLevel { get; init; }

    public int? OverallLevel =>
        this.CombatLevel.HasValue &&
        this.ExploreLevel.HasValue &&
        this.TradeLevel.HasValue
            ? checked(
                this.CombatLevel.Value +
                this.ExploreLevel.Value +
                this.TradeLevel.Value)
            : null;

    public int? OverallLevelsUntilNextHullUpgrade =>
        this.OverallLevel.HasValue &&
        this.NextHullUpgradeOverallLevel.HasValue
            ? Math.Max(
                0,
                this.NextHullUpgradeOverallLevel.Value -
                this.OverallLevel.Value)
            : null;

    public bool IsMaximumHullTier =>
        this.HullTier == 7 &&
        !this.NextHullUpgradeOverallLevel.HasValue;

    public ClientCharacterSkillsObservation Skills { get; init; } =
        ClientCharacterSkillsObservation.Unavailable(
            "Character skills were not observed");

    public IReadOnlyDictionary<string, uint> PropertyAddresses { get; init; } =
        new Dictionary<string, uint>(
            StringComparer.Ordinal);

    public static ClientCharacterProgressionObservation Unavailable(
        string status,
        uint auxDataAddress = 0,
        uint auxDataLookupAddress = 0)
    {
        return new ClientCharacterProgressionObservation
        {
            Status = status,
            AuxDataAddress = auxDataAddress,
            AuxDataLookupAddress =
                auxDataLookupAddress,
        };
    }
}
