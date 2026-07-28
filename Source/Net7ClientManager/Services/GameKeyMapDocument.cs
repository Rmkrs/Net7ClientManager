namespace Net7ClientManager.Services;

internal sealed class GameKeyMapDocument
{
    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> sections;

    private GameKeyMapDocument(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> sections,
        string activeUserProfile)
    {
        this.sections = sections;
        this.ActiveUserProfile = activeUserProfile;
    }

    public string ActiveUserProfile { get; }

    public static bool TryParse(
        string text,
        out GameKeyMapDocument? document,
        out string error)
    {
        document = null;
        error = "";

        var mutableSections =
            new Dictionary<string, Dictionary<string, string>>(
                StringComparer.OrdinalIgnoreCase);

        string? currentSection = null;

        using var reader = new StringReader(text);

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

        if (!mutableSections.TryGetValue(
                "Default",
                out var defaultSection) ||
            !defaultSection.TryGetValue(
                "User",
                out var activeUserProfile) ||
            string.IsNullOrWhiteSpace(activeUserProfile))
        {
            var userSections = mutableSections.Keys
                .Where(sectionName =>
                    !string.Equals(
                        sectionName,
                        "Default",
                        StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (userSections.Count != 1)
            {
                error = "keymap.ini does not identify an active user profile.";
                return false;
            }

            activeUserProfile = userSections[0];
        }

        activeUserProfile = activeUserProfile.Trim();

        if (!mutableSections.ContainsKey(activeUserProfile))
        {
            error = $"keymap.ini does not contain active profile section [{activeUserProfile}].";
            return false;
        }

        var readOnlySections = mutableSections.ToDictionary(
            section => section.Key,
            section =>
                (IReadOnlyDictionary<string, string>)section.Value,
            StringComparer.OrdinalIgnoreCase);

        document = new GameKeyMapDocument(
            readOnlySections,
            activeUserProfile);

        return true;
    }

    public IReadOnlyList<GameKeyMapBinding> GetBindings()
    {
        if (!this.sections.TryGetValue(
                this.ActiveUserProfile,
                out var activeSection))
        {
            return [];
        }

        List<GameKeyMapBinding> bindings = [];

        foreach (var entry in activeSection)
        {
            if (string.IsNullOrWhiteSpace(entry.Value) ||
                entry.Key.Length < 3 ||
                entry.Key[1] != '_' ||
                (entry.Key[0] != '1' && entry.Key[0] != '2'))
            {
                continue;
            }

            bindings.Add(
                new GameKeyMapBinding(
                    DefinitionName: entry.Key[2..],
                    Slot: entry.Key[0] - '0',
                    SourceText: entry.Value.Trim()));
        }

        return bindings;
    }

    public string? GetBinding(
        string definitionName,
        int slot)
    {
        if (!this.sections.TryGetValue(
                this.ActiveUserProfile,
                out var activeSection))
        {
            return null;
        }

        var key = string.Concat(
            slot.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "_",
            definitionName);

        return activeSection.TryGetValue(key, out var value)
            ? value
            : null;
    }
}

internal sealed record GameKeyMapBinding(
    string DefinitionName,
    int Slot,
    string SourceText);
