namespace Net7ClientManager.Observations.Models;

public sealed record ClientMissionStageObservation
{
    public int Index { get; init; }

    public uint Address { get; init; }

    public uint ValidState { get; init; }

    public bool IsAvailable { get; init; }

    public string Text { get; init; } = "";

    public bool? IsTimed { get; init; }
}
