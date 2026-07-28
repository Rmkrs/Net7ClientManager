namespace Net7ClientManager.Services;

using System.Globalization;
using System.Text.RegularExpressions;
using Net7ClientManager.Models;

internal sealed partial class SkillIniCatalogService(
    GameKeyMapLocator keyMapLocator)
{
    private const string SkillFileName = "cskill_t.ini";
    private const string SkillExtensionFileName = "cskill_ex_t.ini";
    private const string AbilityFileName = "cskill_ability_t.ini";
    private const string AbilityExtensionFileName = "cskill_ability_ex_t.ini";
    private const string GenericSkillIconResourceName = "skill_.tga";

    private static readonly TimeSpan ValidationInterval =
        TimeSpan.FromSeconds(2);

    private readonly object gate = new();
    private readonly Dictionary<string, CachedCatalog> cachedCatalogs =
        new(StringComparer.OrdinalIgnoreCase);

    public SkillIniCatalog GetCatalog(ClientInstance client)
    {
        return this.GetCatalogFromIniDirectory(
            keyMapLocator.LocateIniDirectory(client));
    }

    public SkillIniCatalog GetCatalog(string outputDirectory)
    {
        return this.GetCatalogFromIniDirectory(
            keyMapLocator.LocateIniDirectory(outputDirectory));
    }

    private SkillIniCatalog GetCatalogFromIniDirectory(string? iniDirectory)
    {
        if (string.IsNullOrWhiteSpace(iniDirectory))
        {
            return SkillIniCatalog.Unavailable(
                "The game INI directory could not be located");
        }

        var directoryKey = NormalizePath(iniDirectory);
        var now = DateTimeOffset.UtcNow;

        lock (this.gate)
        {
            if (this.cachedCatalogs.TryGetValue(
                    directoryKey,
                    out var cached) &&
                cached.NextValidationAt > now)
            {
                return cached.Catalog;
            }

            var stamp = CreateStamp(directoryKey);

            if (cached?.Catalog.IsAvailable == true &&
                cached.Stamp == stamp)
            {
                cached.NextValidationAt = now + ValidationInterval;
                return cached.Catalog;
            }

            var catalog = LoadCatalog(
                directoryKey,
                out var status);

            if (!catalog.IsAvailable &&
                cached?.Catalog.IsAvailable == true)
            {
                cached.NextValidationAt = now + ValidationInterval;
                return cached.Catalog;
            }

            if (!catalog.IsAvailable &&
                !string.IsNullOrWhiteSpace(status))
            {
                catalog = SkillIniCatalog.Unavailable(status);
            }

            this.cachedCatalogs[directoryKey] = new CachedCatalog
            {
                Stamp = stamp,
                Catalog = catalog,
                NextValidationAt = now + ValidationInterval,
            };

            return catalog;
        }
    }

    private static SkillIniCatalog LoadCatalog(
        string iniDirectory,
        out string status)
    {
        var skillPath = Path.Combine(
            iniDirectory,
            SkillFileName);
        var abilityPath = Path.Combine(
            iniDirectory,
            AbilityFileName);

        if (!GameDataIniDocument.TryRead(
                skillPath,
                out var skillDocument))
        {
            status = string.Concat(
                "Could not read ",
                SkillFileName);
            return SkillIniCatalog.Unavailable(status);
        }

        if (!GameDataIniDocument.TryRead(
                abilityPath,
                out var abilityDocument))
        {
            status = string.Concat(
                "Could not read ",
                AbilityFileName);
            return SkillIniCatalog.Unavailable(status);
        }

        GameDataIniDocument.TryRead(
            Path.Combine(
                iniDirectory,
                SkillExtensionFileName),
            out var skillExtensionDocument);

        GameDataIniDocument.TryRead(
            Path.Combine(
                iniDirectory,
                AbilityExtensionFileName),
            out var abilityExtensionDocument);

        var skillNamesById = skillDocument.GetIntegerKeyMap(
            "All Skills");
        var skillIdsByName = skillNamesById
            .GroupBy(
                entry => entry.Value,
                StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() == 1)
            .ToDictionary(
                group => group.Key,
                group => group.Single().Key,
                StringComparer.OrdinalIgnoreCase);
        var skillFamilyIconsById = BuildSkillFamilyIconDefinitions(
            skillNamesById,
            skillExtensionDocument);
        var skillFamilyIconsByName = skillFamilyIconsById.Values
            .ToDictionary(
                definition => definition.SkillFamilyName,
                StringComparer.OrdinalIgnoreCase);
        var activatedNamesById = abilityDocument.GetIntegerKeyMap(
            "Activated Abilities");
        var passiveNamesById = abilityDocument.GetIntegerKeyMap(
            "Passive Abilities");

        if (skillNamesById.Count == 0 ||
            activatedNamesById.Count == 0)
        {
            status = "The game skill or activated-ability map was empty";
            return SkillIniCatalog.Unavailable(status);
        }

        abilityExtensionDocument.TryGetSection(
            "AbilityColors",
            out var abilityColors);

        var byId = new Dictionary<int, SkillAbilityDefinition>();
        var byName = new Dictionary<string, List<SkillAbilityDefinition>>(
            StringComparer.OrdinalIgnoreCase);

        AddDefinitions(
            activatedNamesById,
            isActivatable: true,
            skillDocument,
            skillExtensionDocument,
            abilityDocument,
            abilityExtensionDocument,
            abilityColors,
            skillIdsByName,
            byId,
            byName);

        AddDefinitions(
            passiveNamesById,
            isActivatable: false,
            skillDocument,
            skillExtensionDocument,
            abilityDocument,
            abilityExtensionDocument,
            abilityColors,
            skillIdsByName,
            byId,
            byName);

        var activatedLoaded = byId.Values.Count(definition =>
            definition.IsIntrinsicallyActivatable);
        var passiveLoaded = byId.Count - activatedLoaded;

        status = string.Create(
            CultureInfo.InvariantCulture,
            $"Loaded {activatedLoaded} activated and {passiveLoaded} passive abilities from {iniDirectory}");

        return new SkillIniCatalog(
            byId,
            byName,
            skillFamilyIconsById,
            skillFamilyIconsByName,
            status);
    }

    private static Dictionary<int, SkillFamilyIconDefinition>
        BuildSkillFamilyIconDefinitions(
            IReadOnlyDictionary<int, string> skillNamesById,
            GameDataIniDocument skillExtensionDocument)
    {
        var definitions =
            new Dictionary<int, SkillFamilyIconDefinition>();

        foreach (var pair in skillNamesById)
        {
            skillExtensionDocument.TryGetSection(
                pair.Value,
                out var familyExtensionSection);

            var iconResourceNames = new List<string>();

            AddSkillIconResourceName(
                iconResourceNames,
                GetValue(
                    familyExtensionSection,
                    "Icon"));

            AddSkillIconResourceName(
                iconResourceNames,
                GenericSkillIconResourceName);

            definitions[pair.Key] = new SkillFamilyIconDefinition
            {
                SkillFamilyId = pair.Key,
                SkillFamilyName = pair.Value,
                IconResourceNames = iconResourceNames,
            };
        }

        return definitions;
    }

    private static void AddSkillIconResourceName(
        ICollection<string> resourceNames,
        string resourceName)
    {
        var normalizedName = resourceName.Trim();

        if (normalizedName.Length == 0 ||
            resourceNames.Contains(
                normalizedName,
                StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        resourceNames.Add(normalizedName);
    }

    private static void AddDefinitions(
        IReadOnlyDictionary<int, string> namesById,
        bool isActivatable,
        GameDataIniDocument skillDocument,
        GameDataIniDocument skillExtensionDocument,
        GameDataIniDocument abilityDocument,
        GameDataIniDocument abilityExtensionDocument,
        IReadOnlyDictionary<string, string>? abilityColors,
        IReadOnlyDictionary<string, int> skillIdsByName,
        IDictionary<int, SkillAbilityDefinition> byId,
        IDictionary<string, List<SkillAbilityDefinition>> byName)
    {
        foreach (var pair in namesById)
        {
            if (!abilityDocument.TryGetSection(
                    pair.Value,
                    out var abilitySection))
            {
                continue;
            }

            var familyName = GetValue(
                abilitySection,
                "Skill");
            var minimumSkillLevel = GetInt(
                abilitySection,
                "MinLevel");
            var baseCost = GetNullableInt(
                abilitySection,
                "Cost");
            var description = GetValue(
                abilitySection,
                "Desc");

            skillDocument.TryGetSection(
                familyName,
                out var familySection);
            skillExtensionDocument.TryGetSection(
                familyName,
                out var familyExtensionSection);
            abilityExtensionDocument.TryGetSection(
                pair.Value,
                out var abilityExtensionSection);

            var listedCost = GetNullableInt(
                                 abilityExtensionSection,
                                 "Cost") ??
                             baseCost;
            var familyDescription = GetValue(
                familySection,
                "Description");
            var rankDescription = minimumSkillLevel > 0
                ? GetValue(
                    familySection,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Desc_{minimumSkillLevel}"))
                : "";
            var rangeByRank = ParseFloatList(
                GetValue(
                    familySection,
                    "Range"));
            var iconResourceName = FirstNonEmpty(
                GetValue(
                    abilityExtensionSection,
                    "Icon"),
                GetValue(
                    familyExtensionSection,
                    "Icon"));
            var colorText = FirstNonEmpty(
                GetValue(
                    abilityExtensionSection,
                    "Color"),
                minimumSkillLevel > 0
                    ? GetValue(
                        abilityColors,
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"Level_{minimumSkillLevel}"))
                    : "");
            var color = ParseColor(colorText);
            var percentageCost = ResolveMaximumReactorCostPercent(
                description,
                rankDescription,
                familyDescription);

            var definition = new SkillAbilityDefinition
            {
                AbilityId = pair.Key,
                IsIntrinsicallyActivatable = isActivatable,
                Name = pair.Value,
                SkillFamilyId = skillIdsByName.TryGetValue(
                    familyName,
                    out var familyId)
                    ? familyId
                    : null,
                SkillFamilyName = familyName,
                MinimumSkillLevel = minimumSkillLevel,
                ListedEnergyCost = listedCost,
                MaximumReactorCostPercent = percentageCost,
                Description = description,
                SkillFamilyDescription = familyDescription,
                RankDescription = rankDescription,
                RangeByRank = rangeByRank,
                IconResourceName = iconResourceName,
                TintRed = color?.Red,
                TintGreen = color?.Green,
                TintBlue = color?.Blue,
            };

            definition = definition with
            {
                Semantics = SkillAbilitySemanticClassifier.Classify(
                    definition),
            };

            byId[pair.Key] = definition;

            if (!byName.TryGetValue(
                    pair.Value,
                    out var candidates))
            {
                candidates = [];
                byName[pair.Value] = candidates;
            }

            candidates.Add(definition);
        }
    }

    private static float? ResolveMaximumReactorCostPercent(
        params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            var match = MaximumReactorCostRegex().Match(candidate);

            if (match.Success &&
                float.TryParse(
                    match.Groups["percent"].Value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var percentage) &&
                float.IsFinite(percentage) &&
                percentage >= 0)
            {
                return percentage;
            }
        }

        return null;
    }

    private static IReadOnlyList<float> ParseFloatList(
        string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var values = new List<float>();

        foreach (var token in value.Split(','))
        {
            if (!float.TryParse(
                    token.Trim(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var parsed) ||
                !float.IsFinite(parsed))
            {
                return [];
            }

            values.Add(parsed);
        }

        return values;
    }

    private static SkillColor? ParseColor(
        string value)
    {
        var values = ParseFloatList(value);

        return values.Count >= 3
            ? new SkillColor(
                values[0],
                values[1],
                values[2])
            : null;
    }

    private static string GetValue(
        IReadOnlyDictionary<string, string>? section,
        string key)
    {
        return section != null &&
               section.TryGetValue(key, out var value)
            ? value.Trim()
            : "";
    }

    private static int GetInt(
        IReadOnlyDictionary<string, string>? section,
        string key)
    {
        return GetNullableInt(section, key) ?? 0;
    }

    private static int? GetNullableInt(
        IReadOnlyDictionary<string, string>? section,
        string key)
    {
        return int.TryParse(
            GetValue(section, key),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : null;
    }

    private static string FirstNonEmpty(
        params string[] values)
    {
        return values.FirstOrDefault(value =>
            !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";
    }

    private static SkillIniCatalogStamp CreateStamp(
        string iniDirectory)
    {
        return new SkillIniCatalogStamp(
            CreateFileStamp(
                Path.Combine(
                    iniDirectory,
                    SkillFileName)),
            CreateFileStamp(
                Path.Combine(
                    iniDirectory,
                    SkillExtensionFileName)),
            CreateFileStamp(
                Path.Combine(
                    iniDirectory,
                    AbilityFileName)),
            CreateFileStamp(
                Path.Combine(
                    iniDirectory,
                    AbilityExtensionFileName)));
    }

    private static SkillIniFileStamp CreateFileStamp(
        string path)
    {
        try
        {
            var info = new FileInfo(path);

            return info.Exists
                ? new SkillIniFileStamp(
                    true,
                    info.Length,
                    info.LastWriteTimeUtc.Ticks)
                : default;
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            ArgumentException or
            NotSupportedException)
        {
            return default;
        }
    }

    private static string NormalizePath(string path)
    {
        try
        {
            return Path
                .GetFullPath(path)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);
        }
        catch (Exception ex) when (
            ex is ArgumentException or
            NotSupportedException or
            PathTooLongException or
            System.Security.SecurityException)
        {
            return path.Trim();
        }
    }

    [GeneratedRegex(
        @"(?<percent>\d+(?:\.\d+)?)\s*%\s*" +
        @"(?:of\s+(?:the\s+)?maximum\s+reactor\s+capacity|" +
        @"reactor\s+capacity\s+energy\s+cost)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MaximumReactorCostRegex();

    private sealed class CachedCatalog
    {
        public SkillIniCatalogStamp Stamp { get; init; }

        public SkillIniCatalog Catalog { get; init; } =
            SkillIniCatalog.Unavailable("Not loaded");

        public DateTimeOffset NextValidationAt { get; set; }
    }

    private readonly record struct SkillIniCatalogStamp(
        SkillIniFileStamp Skill,
        SkillIniFileStamp SkillExtension,
        SkillIniFileStamp Ability,
        SkillIniFileStamp AbilityExtension);

    private readonly record struct SkillIniFileStamp(
        bool Exists,
        long Length,
        long LastWriteTimeUtcTicks);

    private sealed record SkillColor(
        float Red,
        float Green,
        float Blue);
}
