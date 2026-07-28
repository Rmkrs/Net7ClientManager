namespace Net7ClientManager.Observations.Models;

public sealed record ClientCharacterSkillObservation
{
    public int Index { get; init; }

    public string Name { get; init; } = "";

    public string Category { get; init; } = "";

    public bool IsActiveAbility { get; init; }

    public int CurrentRank { get; init; }

    public int MaximumRank { get; init; }

    public int AvailabilityCode { get; init; }

    public int QuestOnlyLevels { get; init; }

    public uint StaticDefinitionAddress { get; init; }

    public uint LiveWrapperAddress { get; init; }

    public bool IsLearned =>
        this.CurrentRank > 0;

    public bool IsMaxed =>
        this.MaximumRank > 0 &&
        this.CurrentRank >= this.MaximumRank;

    public int SpentSkillPoints =>
        checked(
            this.CurrentRank *
            (this.CurrentRank - 1) /
            2);
}
