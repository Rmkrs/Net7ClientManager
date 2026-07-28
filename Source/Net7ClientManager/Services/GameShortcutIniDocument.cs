namespace Net7ClientManager.Services;

using System.Globalization;

internal sealed class GameShortcutIniDocument(
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> sections)
{
    public static bool TryRead(
        string path,
        out GameShortcutIniDocument document)
    {
        document = new GameShortcutIniDocument(
            new Dictionary<string, IReadOnlyDictionary<string, string>>(
                StringComparer.OrdinalIgnoreCase));

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

            document = new GameShortcutIniDocument(
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

    public IReadOnlyList<GameShortcutIniEntry> GetEntries(
        string sectionName)
    {
        if (!sections.TryGetValue(sectionName, out var section) ||
            !section.TryGetValue("Num", out var numText) ||
            !int.TryParse(
                numText,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var count) ||
            count <= 0)
        {
            return [];
        }

        var entries = new List<GameShortcutIniEntry>(count);

        for (var index = 0; index < count; index++)
        {
            if (!TryGetInt(section, index, "Bar", out var bar) ||
                !TryGetInt(section, index, "Group", out var group) ||
                !TryGetInt(section, index, "Button", out var button) ||
                !section.TryGetValue(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{index}_Key"),
                    out var key) ||
                string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            entries.Add(
                new GameShortcutIniEntry(
                    bar,
                    group,
                    button,
                    key));
        }

        return entries;
    }

    private static bool TryGetInt(
        IReadOnlyDictionary<string, string> section,
        int index,
        string name,
        out int value)
    {
        value = 0;

        return section.TryGetValue(
                   string.Create(
                       CultureInfo.InvariantCulture,
                       $"{index}_{name}"),
                   out var text) &&
               int.TryParse(
                   text,
                   NumberStyles.Integer,
                   CultureInfo.InvariantCulture,
                   out value);
    }
}

internal sealed record GameShortcutIniEntry(
    int Bar,
    int Group,
    int Button,
    string Key);
