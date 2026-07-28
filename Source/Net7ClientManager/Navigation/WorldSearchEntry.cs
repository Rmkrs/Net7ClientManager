namespace Net7ClientManager.Navigation;

public sealed record WorldSearchEntry
{
    public required string Identity { get; init; }

    public required string Name { get; init; }

    public required WorldSearchKind Kind { get; init; }

    public required string SystemName { get; init; }

    public required string SectorKey { get; init; }

    public required string SectorName { get; init; }

    public required string Location { get; init; }

    public string ScopeType { get; init; } = "";

    internal string MobFactionIdentifier { get; init; } = "";

    internal ForgeNavigationEncounterFactionBindingKind
        MobFactionBindingKind
    { get; init; } = ForgeNavigationEncounterFactionBindingKind.Unknown;

    internal int? MobIntrinsicRelationshipRaw { get; init; }

    public int? Level { get; init; }

    public string NearNavigationName { get; init; } = "";

    public required IReadOnlyList<string> SearchTerms { get; init; }

    public NavigationDestination? Destination { get; init; }

    public string KindDisplayName => this.Kind switch
    {
        WorldSearchKind.Sector => "Sector",
        WorldSearchKind.NavigationPoint => "Navigation point",
        WorldSearchKind.Station => "Station",
        WorldSearchKind.Gate => "Gate",
        WorldSearchKind.Planet => "Planet",
        WorldSearchKind.Npc => "NPC",
        WorldSearchKind.StationService => "Station service",
        WorldSearchKind.VendorItem => "Vendor item",
        WorldSearchKind.Mob => "Mob",
        WorldSearchKind.MobLoot => "Mob loot",
        WorldSearchKind.Harvestable => "Harvestable",
        WorldSearchKind.HarvestableResource => "Harvestable resource",
        WorldSearchKind.Social => "Social",
        WorldSearchKind.SocialPresence => "Pilot presence",
        WorldSearchKind.LookingForGuild => "Looking for guild",
        WorldSearchKind.GuildRecruitment => "Guild recruitment",
        _ => this.Kind.ToString(),
    };
}
