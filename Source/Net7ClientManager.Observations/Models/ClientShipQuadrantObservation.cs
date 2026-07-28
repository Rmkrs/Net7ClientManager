namespace Net7ClientManager.Observations.Models;

public sealed record ClientShipQuadrantObservation
{
    public int Index { get; init; }

    public uint PropertyAddress { get; init; }

    public float HealthFraction { get; init; }

    public float HealthPercent =>
        this.HealthFraction *
        100.0f;

    public float DamageFraction =>
        1.0f -
        this.HealthFraction;

    public float DamagePercent =>
        this.DamageFraction *
        100.0f;
}
