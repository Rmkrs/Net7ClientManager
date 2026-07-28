namespace Net7ClientManager.Services;

using System.Globalization;

/// <summary>
/// Tolerant reader for client-authored data INIs. Repeated sections are
/// merged in file order so extension files can add fields in a later block.
/// </summary>
internal sealed class GameDataIniDocument(
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> sections)
{
    public static bool TryRead(
        string path,
        out GameDataIniDocument document)
    {
        document = Empty;

        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            using var reader = new StreamReader(stream);

            var mutableSections =
                new Dictionary<string, Dictionary<string, string>>(
                    StringComparer.OrdinalIgnoreCase);

            string? currentSection = null;

            while (reader.ReadLine() is { } line)
            {
                var trimmed = line.Trim();

                if (trimmed.Length == 0 ||
                    trimmed.StartsWith(';') ||
                    trimmed.StartsWith('#'))
                {
                    continue;
                }

                if (trimmed.StartsWith('[') &&
                    trimmed.EndsWith(']') &&
                    trimmed.Length > 2)
                {
                    currentSection = trimmed[1..^1].Trim();

                    if (!mutableSections.ContainsKey(currentSection))
                    {
                        mutableSections[currentSection] =
                            new Dictionary<string, string>(
                                StringComparer.OrdinalIgnoreCase);
                    }

                    continue;
                }

                if (currentSection == null)
                {
                    continue;
                }

                var separatorIndex = line.IndexOf('=');

                if (separatorIndex < 0)
                {
                    continue;
                }

                var key = line[..separatorIndex].Trim();
                var value = line[(separatorIndex + 1)..].Trim();

                if (key.Length == 0)
                {
                    continue;
                }

                mutableSections[currentSection][key] = value;
            }

            document = new GameDataIniDocument(
                mutableSections.ToDictionary(
                    section => section.Key,
                    section =>
                        (IReadOnlyDictionary<string, string>)section.Value,
                    StringComparer.OrdinalIgnoreCase));

            return true;
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            ArgumentException or
            NotSupportedException)
        {
            return false;
        }
    }

    public bool TryGetSection(
        string sectionName,
        out IReadOnlyDictionary<string, string> section)
    {
        return sections.TryGetValue(sectionName, out section!);
    }

    public IReadOnlyDictionary<int, string> GetIntegerKeyMap(
        string sectionName)
    {
        if (!sections.TryGetValue(sectionName, out var section))
        {
            return new Dictionary<int, string>();
        }

        var entries = new Dictionary<int, string>();

        foreach (var pair in section)
        {
            if (!int.TryParse(
                    pair.Key,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var key) ||
                string.IsNullOrWhiteSpace(pair.Value))
            {
                continue;
            }

            entries[key] = pair.Value.Trim();
        }

        return entries;
    }

    private static GameDataIniDocument Empty { get; } =
        new(
            new Dictionary<string, IReadOnlyDictionary<string, string>>(
                StringComparer.OrdinalIgnoreCase));
}
