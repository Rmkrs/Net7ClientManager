namespace Net7ClientManager.Navigation;

public sealed record NavigationWormholeCasterAvailability
{
    public required int ProcessId { get; init; }

    public required string PilotName { get; init; }

    public required string SkillFamilyName { get; init; }

    public required int SkillRank { get; init; }

    /// <summary>
    /// True only while this pilot is in stable space or on a planet, where
    /// the native shortcut bars can be inspected reliably. Stations
    /// deliberately expose no shortcut entries, so absence there is unknown
    /// rather than missing.
    /// </summary>
    public bool CanInspectShortcuts { get; init; }

    public bool HasShortcut { get; init; }
}

public sealed record NavigationWormholeDestinationAvailability
{
    public required NavigationWormholeDestination Destination { get; init; }

    public IReadOnlyList<NavigationWormholeCasterAvailability> Casters
    {
        get;
        init;
    } = [];

    public bool HasReadyCaster => this.Casters.Any(caster => caster.HasShortcut);

    /// <summary>
    /// A missing shortcut is conclusive only when every eligible managed
    /// caster is currently in an inspectable space or planet context.
    /// </summary>
    public bool IsShortcutReadinessKnown =>
        this.Casters.All(caster => caster.CanInspectShortcuts);

    public IReadOnlyList<string> CasterNames =>
    [
        .. this.Casters
            .Select(caster => caster.PilotName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase),
    ];
}

public sealed class NavigationWormholeAvailability
{
    private readonly IReadOnlyDictionary<string, NavigationWormholeDestinationAvailability>
        bySectorKey;

    public NavigationWormholeAvailability(
        IEnumerable<NavigationWormholeCasterAvailability> casters)
    {
        ArgumentNullException.ThrowIfNull(casters);

        var casterList = casters
            .Where(caster => caster.SkillRank > 0)
            .ToArray();

        var destinations = new Dictionary<string, NavigationWormholeDestinationAvailability>(
            StringComparer.Ordinal);

        foreach (var destination in NavigationWormholeCatalog.Destinations)
        {
            var eligible = casterList
                .Where(caster =>
                    NavigationWormholeCatalog.FamilyMatches(
                        destination.SkillFamilyName,
                        caster.SkillFamilyName) &&
                    caster.SkillRank >= destination.RequiredRank)
                .OrderByDescending(caster => caster.HasShortcut)
                .ThenByDescending(caster => caster.SkillRank)
                .ThenBy(caster => caster.PilotName, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (eligible.Length == 0)
            {
                continue;
            }

            destinations[destination.SectorKey] =
                new NavigationWormholeDestinationAvailability
                {
                    Destination = destination,
                    Casters = eligible,
                };
        }

        this.bySectorKey = destinations;
        this.Destinations =
        [
            .. destinations.Values
                .OrderBy(item => item.Destination.SkillFamilyName, StringComparer.Ordinal)
                .ThenBy(item => item.Destination.RequiredRank),
        ];
    }

    public static NavigationWormholeAvailability None { get; } = new([]);

    public IReadOnlyList<NavigationWormholeDestinationAvailability> Destinations
    {
        get;
    }

    public bool IsAvailable => this.Destinations.Count > 0;

    public bool TryGetDestination(
        string? sectorKey,
        out NavigationWormholeDestinationAvailability availability)
    {
        if (string.IsNullOrWhiteSpace(sectorKey))
        {
            availability = null!;
            return false;
        }

        return this.bySectorKey.TryGetValue(sectorKey, out availability!);
    }
}
