namespace Net7ClientManager.Observations.Models;

public sealed record ClientMissionObservation
{
    public int Slot { get; init; }

    public uint Address { get; init; }

    public uint ValidState { get; init; }

    public int? RawId { get; init; }

    public string Name { get; init; } = "";

    public string Summary { get; init; } = "";

    public string Reward { get; init; } = "";

    public string FailureConsequence { get; init; } = "";

    public string IssuingFaction { get; init; } = "";

    public int? Stage { get; init; }

    public int? StageCount { get; init; }

    public bool? IsTimed { get; init; }

    public bool? IsForfeitable { get; init; }

    public bool? IsComplete { get; init; }

    public bool? IsFailed { get; init; }

    public bool? IsExpired { get; init; }

    public bool? IsFullyVisible { get; init; }

    public int? StartTime { get; init; }

    public long? ExpirationTime { get; init; }

    public long? StageExpirationTime { get; init; }

    public bool? HasGivenNewMissionMessage { get; init; }

    public int StageCapacity { get; init; }

    public IReadOnlyList<ClientMissionStageObservation>
        Stages
    { get; init; } = [];

    public ClientMissionStageObservation? CurrentStage =>
        this.Stage is >= 1
            ? this.Stages.FirstOrDefault(
                stage =>
                    stage.Index == this.Stage.Value - 1)
            : null;

    public string CurrentStageText =>
        this.CurrentStage?.Text ?? "";

    public long? EffectiveExpirationTime =>
        this.CurrentStage?.IsTimed == true
            ? this.StageExpirationTime
            : this.IsTimed == true
                ? this.ExpirationTime
                : null;

    public long? RemainingMilliseconds { get; init; }

    public bool IsTerminal =>
        this.IsComplete == true ||
        this.IsFailed == true ||
        this.IsExpired == true;
}
