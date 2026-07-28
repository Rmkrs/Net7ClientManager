namespace Net7ClientManager.CombatJournal;

public enum CombatJournalOutcome
{
    Active = 0,
    Killed = 1,
    Died = 2,
    Disengaged = 3,
    Interrupted = 4,
}

public enum CombatJournalDirection
{
    Outgoing = 1,
    Incoming = 2,
}

public sealed record CombatJournalEvent
{
    public long EventId { get; init; }

    public required string EncounterId { get; init; }

    public required long Sequence { get; init; }

    public required DateTimeOffset OccurredAt { get; init; }

    public required CombatJournalDirection Direction { get; init; }

    public required float Damage { get; init; }

    public required float UnmodifiedDamage { get; init; }

    public required float Modifier { get; init; }

    public string DamageType { get; init; } = "Unknown";

    public bool IsCritical { get; init; }

    public uint SourceObjectId { get; init; }

    public string SourceName { get; init; } = "";

    public uint VictimObjectId { get; init; }

    public string VictimName { get; init; } = "";
}

public sealed record CombatJournalEncounter
{
    public required string EncounterId { get; init; }

    public required uint CharacterId { get; init; }

    public required string PilotName { get; init; }

    public required uint TargetObjectId { get; init; }

    public required string TargetName { get; init; }

    public int? TargetCombatLevel { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public required DateTimeOffset LastEventAt { get; init; }

    public DateTimeOffset? EndedAt { get; init; }

    public required CombatJournalOutcome Outcome { get; init; }

    public string SystemName { get; init; } = "";

    public string SectorName { get; init; } = "";

    public string StarbaseName { get; init; } = "";

    public string NearestNavName { get; init; } = "";

    public double OutgoingDamage { get; init; }

    public double IncomingDamage { get; init; }

    public int OutgoingHitCount { get; init; }

    public int IncomingHitCount { get; init; }

    public int OutgoingCriticalCount { get; init; }

    public int IncomingCriticalCount { get; init; }

    public float LargestOutgoingHit { get; init; }

    public float LargestIncomingHit { get; init; }

    public IReadOnlyList<CombatJournalEvent> Events { get; init; } = [];

    public TimeSpan Duration =>
        (this.EndedAt ?? this.LastEventAt) - this.StartedAt;

    public string OutcomeDisplay => this.Outcome switch
    {
        CombatJournalOutcome.Active => "Active",
        CombatJournalOutcome.Killed => "Killed",
        CombatJournalOutcome.Died => "Died",
        CombatJournalOutcome.Disengaged => "Disengaged",
        CombatJournalOutcome.Interrupted => "Interrupted",
        _ => this.Outcome.ToString(),
    };
}

public sealed class CombatJournalChangedEventArgs(uint characterId) : EventArgs
{
    public uint CharacterId { get; } = characterId;
}

public sealed class CombatEncounterEndedEventArgs(
    CombatJournalEncounter encounter) : EventArgs
{
    public CombatJournalEncounter Encounter { get; } = encounter;
}
