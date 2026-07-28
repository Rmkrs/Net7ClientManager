namespace Net7ClientManager.Navigation;

using System.Globalization;
using System.Text;

public sealed class GalaxyTopology
{
    private readonly IReadOnlyDictionary<string, GalaxySectorDefinition> sectorsByKey;
    private readonly IReadOnlyDictionary<string, GalaxySectorDefinition> sectorsByName;

    public GalaxyTopology(IReadOnlyCollection<GalaxySectorDefinition> sectors)
    {
        ArgumentNullException.ThrowIfNull(sectors);

        var byKey = new Dictionary<string, GalaxySectorDefinition>(StringComparer.Ordinal);
        var byName = new Dictionary<string, GalaxySectorDefinition>(StringComparer.Ordinal);

        foreach (var sector in sectors)
        {
            if (string.IsNullOrWhiteSpace(sector.Key) ||
                string.IsNullOrWhiteSpace(sector.Name) ||
                string.IsNullOrWhiteSpace(sector.SystemName))
            {
                throw new InvalidOperationException(
                    "Galaxy sectors require a key, name and system name.");
            }

            if (!byKey.TryAdd(sector.Key, sector))
            {
                throw new InvalidOperationException(
                        $"Duplicate galaxy sector key '{sector.Key}'.");
            }

            AddName(byName, sector.Name, sector);

            foreach (var alias in sector.Aliases)
            {
                AddName(byName, alias, sector);
            }
        }

        foreach (var sector in byKey.Values)
        {
            foreach (var connection in sector.Connections)
            {
                if (!byKey.ContainsKey(connection))
                {
                    throw new InvalidOperationException(
                            $"Galaxy sector '{sector.Key}' references unknown sector '{connection}'.");
                }
            }
        }

        this.sectorsByKey = byKey;
        this.sectorsByName = byName;
        this.Sectors =
        [
            .. byKey.Values
                .OrderBy(sector => sector.SystemName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(sector => sector.Name, StringComparer.OrdinalIgnoreCase),
        ];
    }

    public IReadOnlyList<GalaxySectorDefinition> Sectors { get; }

    public bool TryGetByKey(
        string? key,
        out GalaxySectorDefinition sector)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            sector = null!;
            return false;
        }

        return this.sectorsByKey.TryGetValue(key, out sector!);
    }

    public bool TryResolve(
        string? nameOrKey,
        out GalaxySectorDefinition sector)
    {
        if (string.IsNullOrWhiteSpace(nameOrKey))
        {
            sector = null!;
            return false;
        }

        if (this.sectorsByKey.TryGetValue(nameOrKey, out sector!))
        {
            return true;
        }

        return this.sectorsByName.TryGetValue(
            NormalizeName(nameOrKey),
            out sector!);
    }

    public GalaxySectorDefinition GetByKey(string key)
    {
        return this.sectorsByKey.TryGetValue(key, out var sector)
            ? sector
            : throw new KeyNotFoundException(
                    $"Unknown galaxy sector '{key}'.");
    }

    public static string NormalizeName(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var decomposed = value
            .Trim()
            .Normalize(NormalizationForm.FormD);

        var result = new StringBuilder(decomposed.Length);
        var pendingSeparator = false;

        foreach (var character in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);

            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                if (pendingSeparator && result.Length > 0)
                {
                    result.Append(' ');
                }

                result.Append(char.ToLowerInvariant(character));
                pendingSeparator = false;
                continue;
            }

            if (character is '\'' or '’' or '`' or '´')
            {
                continue;
            }

            pendingSeparator = true;
        }

        return result.ToString();
    }

    private static void AddName(
        IDictionary<string, GalaxySectorDefinition> sectorsByName,
        string name,
        GalaxySectorDefinition sector)
    {
        var normalizedName = NormalizeName(name);

        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            throw new InvalidOperationException(
                    $"Galaxy sector '{sector.Key}' has an empty normalized name.");
        }

        if (sectorsByName.TryGetValue(normalizedName, out var existing) &&
            !ReferenceEquals(existing, sector))
        {
            throw new InvalidOperationException(
                    $"Galaxy sector name or alias '{name}' is ambiguous.");
        }

        sectorsByName[normalizedName] = sector;
    }
}
