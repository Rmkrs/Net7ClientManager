namespace Net7ClientManager.Observations.Models;

using System.Globalization;

public sealed record ClientTargetDistanceObservation
{
    public bool IsAvailable { get; init; }

    public string Status { get; init; } = "";

    public float SurfaceDistance { get; init; }

    public ClientSpatialObservation Local { get; init; } =
        ClientSpatialObservation.Unavailable(
            "Local spatial state was not observed");

    public ClientSpatialObservation Target { get; init; } =
        ClientSpatialObservation.Unavailable(
            "Target spatial state was not observed");

    public string NativeReadoutText =>
        this.IsAvailable
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{this.SurfaceDistance / 1000.0f:0.00}k")
            : "";

    public static ClientTargetDistanceObservation Unavailable(
        string status)
    {
        return new ClientTargetDistanceObservation
        {
            Status = status,
        };
    }
}
