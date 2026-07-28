namespace Net7ClientManager.Observations.Models;

public sealed record ClientCharacterSkillsObservation
{
    public bool IsAvailable { get; init; }

    public string Status { get; init; } = "";

    public uint StaticCatalogBeginAddress { get; init; }

    public uint StaticCatalogEndAddress { get; init; }

    public uint LiveVectorBeginAddress { get; init; }

    public uint LiveVectorEndAddress { get; init; }

    public int StaticDefinitionCount { get; init; }

    public int LiveSlotCount { get; init; }

    public int ReservedSlotCount { get; init; }

    public IReadOnlyList<ClientCharacterSkillObservation> Skills { get; init; } =
        [];

    public int LearnedSkillCount =>
        this.Skills.Count(
            skill =>
                skill.IsLearned);

    public int SpentSkillPoints =>
        this.Skills.Sum(
            skill =>
                skill.SpentSkillPoints);

    public static ClientCharacterSkillsObservation Unavailable(
        string status)
    {
        return new ClientCharacterSkillsObservation
        {
            Status = status,
        };
    }
}
