namespace Net7ClientManager.SkillPlanning;

using System.Collections.ObjectModel;
using System.Globalization;

internal sealed class SkillBuildHullCatalog
{
    private const int SupportedSchemaVersion = 1;
    private const int ExpectedProfessionCount = 9;

    private static readonly IReadOnlyList<int> ExpectedUpgradeLevels =
        [0, 10, 30, 50, 75, 100, 135];

    private readonly IReadOnlyDictionary<int, IReadOnlyList<SkillBuildHullDefinition>>
        hullsByProfession;

    public SkillBuildHullCatalog(
        SkillBuildHullCatalogDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.SchemaVersion != SupportedSchemaVersion)
        {
            throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Unsupported skill-build hull catalog schema {document.SchemaVersion}."));
        }

        this.Revision = document.Revision.Trim();
        if (this.Revision.Length == 0)
        {
            throw new InvalidOperationException(
                "Skill-build hull catalog revision is empty.");
        }

        if (document.Professions.Count != ExpectedProfessionCount)
        {
            throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Skill-build hull catalog contains {document.Professions.Count} professions; expected {ExpectedProfessionCount}."));
        }

        Dictionary<int, IReadOnlyList<SkillBuildHullDefinition>> definitions = [];

        foreach (var profession in document.Professions)
        {
            if (profession.ProfessionIndex is < 0 or >= ExpectedProfessionCount ||
                definitions.ContainsKey(profession.ProfessionIndex))
            {
                throw new InvalidOperationException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Invalid or duplicate hull profession index {profession.ProfessionIndex}."));
            }

            var ordered = profession.Hulls
                .OrderBy(hull => hull.OverallLevel)
                .ToArray();

            if (ordered.Length != ExpectedUpgradeLevels.Count ||
                !ordered.Select(hull => hull.OverallLevel)
                    .SequenceEqual(ExpectedUpgradeLevels))
            {
                throw new InvalidOperationException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Hull progression for profession {profession.ProfessionIndex} does not contain the expected upgrade levels."));
            }

            var previousWeapons = 0;
            var previousDevices = 0;
            var previousCargo = 0;
            var previousHull = 0;
            List<SkillBuildHullDefinition> professionDefinitions = [];

            for (var tierIndex = 0; tierIndex < ordered.Length; tierIndex++)
            {
                var hull = ordered[tierIndex];
                if (hull.HullPoints <= 0 ||
                    hull.WeaponSlots <= 0 ||
                    hull.DeviceSlots <= 0 ||
                    hull.CargoSlots <= 0 ||
                    hull.HullPoints < previousHull ||
                    hull.WeaponSlots < previousWeapons ||
                    hull.DeviceSlots < previousDevices ||
                    hull.CargoSlots < previousCargo)
                {
                    throw new InvalidOperationException(
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"Hull progression for profession {profession.ProfessionIndex} is invalid at overall level {hull.OverallLevel}."));
                }

                professionDefinitions.Add(
                    new SkillBuildHullDefinition(
                        profession.ProfessionIndex,
                        tierIndex + 1,
                        hull.OverallLevel,
                        hull.HullPoints,
                        hull.WeaponSlots,
                        hull.DeviceSlots,
                        hull.CargoSlots));

                previousHull = hull.HullPoints;
                previousWeapons = hull.WeaponSlots;
                previousDevices = hull.DeviceSlots;
                previousCargo = hull.CargoSlots;
            }

            definitions.Add(
                profession.ProfessionIndex,
                professionDefinitions);
        }

        this.hullsByProfession =
            new ReadOnlyDictionary<int, IReadOnlyList<SkillBuildHullDefinition>>(
                definitions);
    }

    public string Revision { get; }


    public IReadOnlyList<SkillBuildHullDefinition> GetProgression(
        int professionIndex)
    {
        if (!this.hullsByProfession.TryGetValue(
                professionIndex,
                out var hulls))
        {
            throw new ArgumentOutOfRangeException(
                nameof(professionIndex),
                professionIndex,
                "Unknown skill-build profession index.");
        }

        return hulls;
    }

    public SkillBuildHullDefinition GetMaximum(int professionIndex) =>
        this.GetProgression(professionIndex)[^1];

    public bool TryResolveForSlots(
        int professionIndex,
        int weaponCount,
        int deviceCount,
        out SkillBuildHullDefinition hull)
    {
        var progression = this.GetProgression(professionIndex);
        hull = progression[^1];

        foreach (var candidate in progression)
        {
            if (candidate.WeaponSlots >= Math.Max(0, weaponCount) &&
                candidate.DeviceSlots >= Math.Max(0, deviceCount))
            {
                hull = candidate;
                return true;
            }
        }

        return false;
    }

    public SkillBuildHullDefinition Resolve(
        int professionIndex,
        int overallLevel)
    {
        if (!this.hullsByProfession.TryGetValue(
                professionIndex,
                out var hulls))
        {
            throw new ArgumentOutOfRangeException(
                nameof(professionIndex),
                professionIndex,
                "Unknown skill-build profession index.");
        }

        var effectiveLevel = Math.Max(0, overallLevel);
        var result = hulls[0];

        foreach (var hull in hulls)
        {
            if (hull.RequiredOverallLevel > effectiveLevel)
            {
                break;
            }

            result = hull;
        }

        return result;
    }
}

internal sealed record SkillBuildHullDefinition(
    int ProfessionIndex,
    int Tier,
    int RequiredOverallLevel,
    int HullPoints,
    int WeaponSlots,
    int DeviceSlots,
    int CargoSlots);
