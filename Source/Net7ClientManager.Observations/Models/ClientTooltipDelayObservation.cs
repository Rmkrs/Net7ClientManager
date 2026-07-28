namespace Net7ClientManager.Observations.Models;

public sealed record ClientTooltipDelayObservation
{
    public const int DefaultDelayMilliseconds = 600;

    public bool IsAvailable { get; init; }

    public string Status { get; init; } = "";

    public float CurrentPercent { get; init; }

    public float DefaultPercent { get; init; }

    public int DelayMilliseconds { get; init; } =
        DefaultDelayMilliseconds;

    public uint FloatOptionAddress { get; init; }

    public bool UsedCachedResolution { get; init; }

    public static ClientTooltipDelayObservation Unavailable(
        string status)
    {
        return new ClientTooltipDelayObservation
        {
            Status = status,
        };
    }
}
