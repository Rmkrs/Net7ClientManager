namespace Net7ClientManager.Services;

using System.Globalization;
using System.Text;
using Net7ClientManager.Models;

internal sealed class GameBuffDefinitionCatalogService(
    GameKeyMapLocator keyMapLocator)
{
    private const string BuffDefinitionFileName = "buffdef.ini";

    private static readonly TimeSpan ValidationInterval =
        TimeSpan.FromSeconds(2);

    private readonly object gate = new();
    private readonly Dictionary<string, CachedCatalog> cachedCatalogs =
        new(StringComparer.OrdinalIgnoreCase);

    public GameBuffDefinitionCatalog GetCatalog(ClientInstance client)
    {
        return this.GetCatalogFromIniDirectory(
            keyMapLocator.LocateIniDirectory(client));
    }

    private GameBuffDefinitionCatalog GetCatalogFromIniDirectory(
        string? iniDirectory)
    {
        if (string.IsNullOrWhiteSpace(iniDirectory))
        {
            return GameBuffDefinitionCatalog.Unavailable(
                "The game INI directory could not be located");
        }

        string directoryKey;

        try
        {
            directoryKey = Path.GetFullPath(iniDirectory);
        }
        catch (Exception ex) when (
            ex is ArgumentException or
            NotSupportedException or
            PathTooLongException or
            System.Security.SecurityException)
        {
            return GameBuffDefinitionCatalog.Unavailable(
                "The game INI directory path was invalid");
        }

        var now = DateTimeOffset.UtcNow;
        var filePath = Path.Combine(
            directoryKey,
            BuffDefinitionFileName);
        var stamp = CreateStamp(filePath);

        lock (this.gate)
        {
            if (this.cachedCatalogs.TryGetValue(
                    directoryKey,
                    out var cached) &&
                cached.NextValidationAt > now)
            {
                return cached.Catalog;
            }

            if (cached?.Catalog.IsAvailable == true &&
                cached.Stamp == stamp)
            {
                cached.NextValidationAt = now + ValidationInterval;
                return cached.Catalog;
            }

            var catalog = LoadCatalog(filePath);

            if (!catalog.IsAvailable &&
                cached?.Catalog.IsAvailable == true)
            {
                cached.NextValidationAt = now + ValidationInterval;
                return cached.Catalog;
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

    private static GameBuffDefinitionCatalog LoadCatalog(
        string filePath)
    {
        if (!GameDataIniDocument.TryRead(
                filePath,
                out var document))
        {
            return GameBuffDefinitionCatalog.Unavailable(
                $"Could not read {BuffDefinitionFileName}");
        }

        List<GameBuffDefinition> definitions = [];

        foreach (var section in document.GetSections("BUFF-"))
        {
            var values = section.Value;

            if (!values.TryGetValue("BuffType", out var buffType) ||
                string.IsNullOrWhiteSpace(buffType))
            {
                continue;
            }

            values.TryGetValue("BuffAsset", out var asset);
            values.TryGetValue("BuffToolTip", out var toolTip);
            values.TryGetValue("BuffAltToolTip", out var alternateToolTip);
            values.TryGetValue("IsGoodBuff", out var goodBuffValue);

            definitions.Add(
                new GameBuffDefinition(
                    section.Key,
                    buffType.Trim(),
                    asset?.Trim() ?? "",
                    FirstNonEmpty(
                        DecodeValue(toolTip),
                        Humanize(buffType)),
                    DecodeValue(alternateToolTip),
                    ParseBoolean(goodBuffValue)));
        }

        if (definitions.Count == 0)
        {
            return GameBuffDefinitionCatalog.Unavailable(
                $"{BuffDefinitionFileName} contained no buff definitions");
        }

        return GameBuffDefinitionCatalog.Create(
            definitions,
            string.Create(
                CultureInfo.InvariantCulture,
                $"Loaded {definitions.Count} buff definitions from {filePath}"));
    }

    private static string DecodeValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        return value.Trim()
            .Replace("\\r\\n", "\n", StringComparison.Ordinal)
            .Replace("\\n", "\n", StringComparison.Ordinal)
            .Replace("\\r", "\n", StringComparison.Ordinal)
            .Replace("\\t", "\t", StringComparison.Ordinal)
            .Trim();
    }

    private static bool? ParseBoolean(string? value)
    {
        return value?.Trim().ToUpperInvariant() switch
        {
            "TRUE" => true,
            "FALSE" => false,
            _ => null,
        };
    }

    private static string Humanize(string value)
    {
        return string.Join(
            " ",
            value.Replace('_', ' ')
                .Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries));
    }

    private static string FirstNonEmpty(params string[] values)
    {
        return values.FirstOrDefault(value =>
            !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";
    }

    private static BuffDefinitionCatalogStamp CreateStamp(
        string filePath)
    {
        try
        {
            var information = new FileInfo(filePath);

            return information.Exists
                ? new BuffDefinitionCatalogStamp(
                    information.Length,
                    information.LastWriteTimeUtc.Ticks)
                : default;
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            System.Security.SecurityException)
        {
            return default;
        }
    }

    private sealed class CachedCatalog
    {
        public BuffDefinitionCatalogStamp Stamp { get; init; }

        public GameBuffDefinitionCatalog Catalog { get; init; } =
            GameBuffDefinitionCatalog.Unavailable("Not loaded");

        public DateTimeOffset NextValidationAt { get; set; }
    }

    private readonly record struct BuffDefinitionCatalogStamp(
        long Length,
        long LastWriteTimeUtcTicks);
}

internal sealed class GameBuffDefinitionCatalog
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<GameBuffDefinition>>
        definitionsByExactType;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<GameBuffDefinition>>
        definitionsByNormalizedType;

    private GameBuffDefinitionCatalog(
        bool isAvailable,
        string status,
        IReadOnlyDictionary<string, IReadOnlyList<GameBuffDefinition>>
            definitionsByExactType,
        IReadOnlyDictionary<string, IReadOnlyList<GameBuffDefinition>>
            definitionsByNormalizedType)
    {
        this.IsAvailable = isAvailable;
        this.Status = status;
        this.definitionsByExactType = definitionsByExactType;
        this.definitionsByNormalizedType = definitionsByNormalizedType;
    }

    public bool IsAvailable { get; }

    public string Status { get; }

    public static GameBuffDefinitionCatalog Unavailable(string status) =>
        new(
            false,
            status,
            new Dictionary<string, IReadOnlyList<GameBuffDefinition>>(
                StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, IReadOnlyList<GameBuffDefinition>>(
                StringComparer.Ordinal));

    public static GameBuffDefinitionCatalog Create(
        IReadOnlyList<GameBuffDefinition> definitions,
        string status)
    {
        var exact = definitions
            .GroupBy(
                definition => definition.BuffType,
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<GameBuffDefinition>)group.ToArray(),
                StringComparer.OrdinalIgnoreCase);
        var normalized = definitions
            .GroupBy(
                definition => NormalizeKey(definition.BuffType),
                StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<GameBuffDefinition>)group.ToArray(),
                StringComparer.Ordinal);

        return new GameBuffDefinitionCatalog(
            true,
            status,
            exact,
            normalized);
    }

    public bool TryResolve(
        IEnumerable<string?> candidates,
        out GameBuffDefinition definition)
    {
        definition = default!;
        var prepared = candidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Select(candidate => candidate!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var candidate in prepared)
        {
            if (this.definitionsByExactType.TryGetValue(
                    candidate,
                    out var exact) &&
                TryChooseDefinition(exact, out definition))
            {
                return true;
            }
        }

        foreach (var candidate in prepared)
        {
            var key = NormalizeKey(candidate);

            if (key.Length != 0 &&
                this.definitionsByNormalizedType.TryGetValue(
                    key,
                    out var normalized) &&
                TryChooseDefinition(normalized, out definition))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryChooseDefinition(
        IReadOnlyList<GameBuffDefinition> candidates,
        out GameBuffDefinition definition)
    {
        definition = default!;

        if (candidates.Count == 0)
        {
            return false;
        }

        var first = candidates[0];
        var hasConflictingText = candidates.Any(candidate =>
            !string.Equals(
                candidate.ToolTip,
                first.ToolTip,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                candidate.AlternateToolTip,
                first.AlternateToolTip,
                StringComparison.OrdinalIgnoreCase));

        if (hasConflictingText)
        {
            return false;
        }

        definition = first;
        return true;
    }

    private static string NormalizeKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var builder = new StringBuilder(value.Length);

        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToUpperInvariant(character));
            }
        }

        return builder.ToString();
    }
}

internal sealed record GameBuffDefinition(
    string Section,
    string BuffType,
    string Asset,
    string ToolTip,
    string AlternateToolTip,
    bool? IsGoodBuff);
