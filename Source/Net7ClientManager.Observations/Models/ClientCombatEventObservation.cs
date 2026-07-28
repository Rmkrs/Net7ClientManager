namespace Net7ClientManager.Observations.Models;

public sealed record ClientCombatEventObservation
{
    public long Sequence { get; init; }

    public DateTimeOffset ObservedAt { get; init; }

    public uint ClientTime { get; init; }

    public uint MessageInstanceId { get; init; }

    public float Damage { get; init; }

    public float Modifier { get; init; }

    public float UnmodifiedDamage =>
        this.Damage - this.Modifier;

    public int DamageType { get; init; }

    public ClientCombatDamageType? KnownDamageType =>
        this.DamageType switch
        {
            0 => ClientCombatDamageType.Impact,
            1 => ClientCombatDamageType.Explosive,
            2 => ClientCombatDamageType.Plasma,
            3 => ClientCombatDamageType.Energy,
            4 => ClientCombatDamageType.Emp,
            5 => ClientCombatDamageType.Chemical,
            _ => null,
        };

    public int Inflicted { get; init; }

    public bool IsCritical =>
        this.Inflicted == 3;

    public uint SourceObjectId { get; init; }

    public uint VictimObjectId { get; init; }

    public uint LocalPlayerObjectId { get; init; }

    public ClientCombatActorIdentityObservation SourceIdentity { get; init; } =
        ClientCombatActorIdentityObservation.Pending(0);

    public ClientCombatActorIdentityObservation VictimIdentity { get; init; } =
        ClientCombatActorIdentityObservation.Pending(0);

    /// <summary>
    /// Direction inferred directly from the packet's source, victim and local
    /// player ObjectIds before actor identity enrichment is available.
    /// </summary>
    public ClientCombatPacketDirection CapturedDirection { get; init; }

    /// <summary>
    /// Effective direction after conservative identity-aware attribution.
    /// This normally equals CapturedDirection. A missile whose resolved Owner
    /// exactly matches the local pilot may be promoted from observed third-party
    /// to outgoing damage.
    /// </summary>
    public ClientCombatPacketDirection Direction { get; init; }

    public ClientCombatDirectionAttribution DirectionAttribution { get; init; }
}
