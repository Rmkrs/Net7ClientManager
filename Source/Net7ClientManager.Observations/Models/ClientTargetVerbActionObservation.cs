namespace Net7ClientManager.Observations.Models;

public sealed record ClientTargetVerbActionObservation
{
    public uint ButtonAddress { get; init; }

    public uint ButtonVTableAddress { get; init; }

    public uint PackedState { get; init; }

    public uint RawVerbType { get; init; }

    public ClientTargetVerb Verb { get; init; }

    public ushort UnavailableReasonCode { get; init; }

    public ClientTargetVerbUnavailableReason? KnownUnavailableReason { get; init; }

    public bool IsExecutable =>
        this.UnavailableReasonCode == 0;
}
