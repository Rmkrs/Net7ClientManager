namespace Net7ClientManager.Observations.Models;

public sealed record ClientShipRuntimeStateObservation
{
    public int? PrivateWarpState { get; init; }

    public int? GlobalWarpState { get; init; }

    public int? WarpAvailable { get; init; }

    public int? EngineThrustState { get; init; }

    public int? EngineTrailType { get; init; }

    public string? TargetThreat { get; init; }

    public string? TargetThreatSound { get; init; }

    public int? TargetThreatLevel { get; init; }

    public string? InterruptibleAbilityName { get; init; }

    public float? InterruptProgress { get; init; }

    public int? InterruptStateRaw { get; init; }

    public ulong? InterruptibleActivationTimeRaw { get; init; }

    public ulong? WarpTriggerTimeRaw { get; init; }

    public bool HasActiveWarpState =>
        this.PrivateWarpState is > 0 ||
        this.GlobalWarpState is > 0;

    public bool HasEngineThrust =>
        this.EngineThrustState is > 0;

    public bool HasInterruptibleAbility =>
        !string.IsNullOrWhiteSpace(
            this.InterruptibleAbilityName);

    public float? InterruptProgressPercent =>
        this.InterruptProgress.HasValue
            ? Math.Clamp(
                  this.InterruptProgress.Value,
                  0.0f,
                  1.0f) *
              100.0f
            : null;
}
