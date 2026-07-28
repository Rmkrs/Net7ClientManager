namespace Net7ClientManager.SkillPlanning;

internal sealed record SkillBuildHullCatalogDocument
{
    public int SchemaVersion { get; init; }

    public string Revision { get; init; } = "";

    public IReadOnlyList<SkillBuildHullProfessionDocument> Professions { get; init; } = [];
}

internal sealed record SkillBuildHullProfessionDocument
{
    public int ProfessionIndex { get; init; }

    public IReadOnlyList<SkillBuildHullRankDocument> Hulls { get; init; } = [];
}

internal sealed record SkillBuildHullRankDocument
{
    public int OverallLevel { get; init; }

    public int HullPoints { get; init; }

    public int WeaponSlots { get; init; }

    public int DeviceSlots { get; init; }

    public int CargoSlots { get; init; }
}
