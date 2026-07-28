namespace Net7ClientManager.Observations.Models;

public sealed record ClientLootTractorObservation
{
    public bool IsAvailable { get; init; }

    public string Status { get; init; } = "";

    public DateTimeOffset ObservedAt { get; init; }

    public uint ClientTime { get; init; }

    public bool IsTractoring { get; init; }

    public bool IsByLocalPlayer { get; init; }

    public bool IsCameraTargetResolved { get; init; }

    public bool IsDirectSpatialState { get; init; }

    public bool IsStateBoundToClientContext { get; init; }

    public bool MatchesCurrentTarget { get; init; }

    public bool WasRecentlyCompleted { get; init; }

    public bool WasRecentlyInterrupted { get; init; }

    public ClientLootTractorTransitionKind TransitionKind { get; init; }

    public DateTimeOffset? StartedAt { get; init; }

    public DateTimeOffset? LastActiveAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public TimeSpan ActiveDuration { get; init; }

    public uint CameraTargetObjectId { get; init; }

    public uint TargetClientObjectAddress { get; init; }

    public uint ObjectIdReadFromClientObject { get; init; }

    public uint ClientObjectVTableAddress { get; init; }

    public uint AuxDataAddress { get; init; }

    public uint NamePropertyAddress { get; init; }

    public uint NameAddress { get; init; }

    public bool NamePropertyValid { get; init; }

    public string Name { get; init; } = "";

    public byte ObjectType { get; init; }

    public ClientTargetKind TargetKind { get; init; }

    public uint HandlerAddress { get; init; }

    public uint HandlerVTableAddress { get; init; }

    public uint ProviderAddress { get; init; }

    public uint ProviderVTableAddress { get; init; }

    public uint StateAddress { get; init; }

    public uint StateVTableAddress { get; init; }

    public uint StateTime { get; init; }

    public ClientSpatialPosition Position { get; init; }

    public ClientSpatialPosition Motion { get; init; }

    public float Transform44 { get; init; }

    public float StateSpeed { get; init; }

    public float TractorSpeed { get; init; }

    public uint HandlerTractorObjectId { get; init; }

    public uint HandlerTractorEffectId { get; init; }

    public uint StateTractorOwnerObjectId { get; init; }

    public uint StateClientContextAddress { get; init; }

    public string IdentityStatus { get; init; } = "";

    public string CurrentTargetName { get; init; } = "";

    public string ItemName =>
        string.IsNullOrWhiteSpace(this.Name)
            ? this.CurrentTargetName
            : this.Name;

    public static ClientLootTractorObservation Unavailable(
        string status)
    {
        return new ClientLootTractorObservation
        {
            Status = status,
            ObservedAt = DateTimeOffset.UtcNow,
        };
    }
}
