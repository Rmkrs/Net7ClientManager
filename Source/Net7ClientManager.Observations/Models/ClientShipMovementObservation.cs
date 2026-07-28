namespace Net7ClientManager.Observations.Models;

public sealed record ClientShipMovementObservation
{
    public float? MaximumTiltRate { get; init; }

    public float? MaximumTurnRate { get; init; }

    public float? MaximumTiltAngle { get; init; }

    public float? MaximumSpeed { get; init; }

    public float? MinimumSpeed { get; init; }

    public float? Acceleration { get; init; }
}
