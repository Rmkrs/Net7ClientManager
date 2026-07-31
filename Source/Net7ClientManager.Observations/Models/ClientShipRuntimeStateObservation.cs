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

    /// <summary>
    /// Starting or active Warp travel and the native gate-transition lock
    /// block a new Auto Pilot run. Recovery value 3 intentionally does not:
    /// the global state can retain 3 after travel has ended, and an already
    /// available Gate/Dock/Land verb remains safe to activate during recovery.
    /// </summary>
    public bool BlocksAutoPilotStart =>
        this.PrivateWarpState is 1 or 2 or 4 ||
        this.GlobalWarpState is 1 or 2 or 4;

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
