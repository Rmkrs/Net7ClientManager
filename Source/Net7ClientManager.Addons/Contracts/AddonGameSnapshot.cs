// ReSharper disable GrammarMistakeInComment
namespace Net7ClientManager.Addons.Contracts;

public sealed record AddonGameSnapshot
{
    public required long Sequence { get; init; }

    public required DateTimeOffset ObservedAt { get; init; }

    public required AddonLifecycleSnapshot Lifecycle { get; init; }

    public required AddonWorldSnapshot World { get; init; }

    public required AddonCharacterSnapshot Character { get; init; }

    /// <summary>
    /// Internal target identity used only to distinguish target lifecycle
    /// changes. It is never copied into Lua tables or event payloads.
    /// </summary>
    public uint? InternalTargetObjectId { get; init; }

    /// <summary>
    /// Curated public game domains beyond the small strongly typed bootstrap
    /// surface. Values contain only addon-safe primitives, dictionaries and
    /// ordered collections. Observation addresses, pointers and probe
    /// diagnostics never enter this graph.
    /// </summary>
    public IReadOnlyDictionary<string, object?> PublicData { get; init; } =
        new Dictionary<string, object?>(StringComparer.Ordinal);

    /// <summary>
    /// Deterministic signatures for the complete public domain tables. The
    /// runtime uses these to refresh Lua tables only when their readable state
    /// changed.
    /// </summary>
    public IReadOnlyDictionary<string, string> DomainFingerprints { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// Per-domain signatures used only for semantic change events. These can
    /// intentionally exclude high-frequency values such as ordinary owner
    /// movement while DomainFingerprints still tracks the complete public
    /// table so Lua always receives the latest readable state.
    /// </summary>
    public IReadOnlyDictionary<string, string> EventDomainFingerprints
    { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// The same curated combat-event records exposed through
    /// game.combat.recent_events. Kept separately so the coordinator can emit
    /// one combat.event callback for each newly observed event.
    /// </summary>
    public IReadOnlyList<IReadOnlyDictionary<string, object?>>
        RecentCombatEvents
    { get; init; } = [];
}
