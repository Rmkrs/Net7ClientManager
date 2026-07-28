namespace Net7ClientManager.Observations.Models;

public sealed record ClientNavigationRadarObservation
{
    public bool? AppearsInRadar { get; init; }

    public float? RadarRange { get; init; }

    public bool HasData =>
        this.AppearsInRadar.HasValue ||
        this.RadarRange.HasValue;
}
