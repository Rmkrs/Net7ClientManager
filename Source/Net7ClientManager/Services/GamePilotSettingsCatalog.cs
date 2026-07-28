namespace Net7ClientManager.Services;

using System.Buffers.Binary;
using System.Globalization;
using System.Text.RegularExpressions;
using Net7ClientManager.Core;
using Net7ClientManager.Observations;
using Net7ClientManager.Observations.Models;

internal static partial class GamePilotSettingsCatalog
{
    public static IReadOnlyList<GamePilotSettingsFile> Load(
        string outputDirectory,
        ClientManager clientManager)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentNullException.ThrowIfNull(clientManager);

        var archiveNames = clientManager.PilotArchive
            .GetPilots()
            .GroupBy(
                pilot => BinaryPrimitives.ReverseEndianness(
                    pilot.CharacterId))
            .ToDictionary(
                group => group.Key,
                group => group.First().Name);
        var shortcutNames = ReadShortcutNames(
            Path.Combine(outputDirectory, "shortcut.ini"));
        var onlineIdentities = ResolveOnlineIdentities(clientManager);
        List<GamePilotSettingsFile> files = [];

        foreach (var path in Directory.EnumerateFiles(
                     outputDirectory,
                     "player*_options.ini",
                     SearchOption.TopDirectoryOnly))
        {
            var match = OptionsFileNameRegex().Match(
                Path.GetFileName(path));

            if (!match.Success ||
                !uint.TryParse(
                    match.Groups["identity"].Value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var identity))
            {
                continue;
            }

            string displayName;
            string? sourceName = null;

            if (archiveNames.TryGetValue(identity, out var archiveName))
            {
                displayName = archiveName;
                sourceName = archiveName;
            }
            else if (shortcutNames.TryGetValue(identity, out var shortcutName))
            {
                displayName = ExtractPilotName(shortcutName);
                sourceName = shortcutName;
            }
            else
            {
                displayName = string.Concat(
                    "Unknown pilot · ",
                    identity.ToString(CultureInfo.InvariantCulture));
            }

            files.Add(new GamePilotSettingsFile
            {
                Identity = identity,
                FilePath = path,
                DisplayName = displayName,
                SourceName = sourceName,
                IsOnline = onlineIdentities.Contains(identity),
            });
        }

        return
        [
            .. files
                .OrderBy(file => file.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(file => file.Identity),
        ];
    }

    public static IReadOnlySet<uint> ResolveOnlineIdentities(
        ClientManager clientManager)
    {
        HashSet<uint> result = [];

        foreach (var snapshot in clientManager.GetClientObservationSnapshots())
        {
            if (snapshot.LifecycleState != ClientLifecycleState.InGame)
            {
                continue;
            }

            if (snapshot.Shortcuts.ShortcutIdentity is > 0 and <= uint.MaxValue)
            {
                result.Add((uint)snapshot.Shortcuts.ShortcutIdentity.Value);
                continue;
            }

            var objectId = snapshot.LocalPlayer.ObjectId != 0
                ? snapshot.LocalPlayer.ObjectId
                : snapshot.LocalPlayerObjectId;

            if (objectId is not 0 and not uint.MaxValue)
            {
                result.Add(BinaryPrimitives.ReverseEndianness(objectId));
            }
        }

        return result;
    }

    private static Dictionary<uint, string> ReadShortcutNames(string path)
    {
        var result = new Dictionary<uint, string>();

        if (!File.Exists(path))
        {
            return result;
        }

        uint? currentIdentity = null;

        try
        {
            foreach (var rawLine in File.ReadLines(path))
            {
                var line = rawLine.Trim();
                var section = ShortcutSectionRegex().Match(line);

                if (section.Success &&
                    uint.TryParse(
                        section.Groups["identity"].Value,
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out var identity))
                {
                    currentIdentity = identity;
                    continue;
                }

                if (line.StartsWith("[", StringComparison.Ordinal))
                {
                    currentIdentity = null;
                    continue;
                }

                if (!currentIdentity.HasValue ||
                    !line.StartsWith("Name=", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var name = line["Name=".Length..].Trim();

                if (!string.IsNullOrWhiteSpace(name) &&
                    !result.ContainsKey(currentIdentity.Value))
                {
                    result[currentIdentity.Value] = name;
                }
            }
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            ArgumentException or
            NotSupportedException)
        {
            return result;
        }

        return result;
    }

    private static string ExtractPilotName(string sourceName)
    {
        var parts = sourceName.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries |
            StringSplitOptions.TrimEntries);

        return parts.Length == 0
            ? sourceName
            : parts[^1];
    }

    [GeneratedRegex(
        "^player(?<identity>[0-9]+)_options\\.ini$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OptionsFileNameRegex();

    [GeneratedRegex(
        "^\\[(?<identity>[0-9]+)_(?:PDA_EQUIP|PDA_CARGO|Skills)\\]$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ShortcutSectionRegex();
}

internal sealed record GamePilotSettingsFile
{
    public required uint Identity { get; init; }

    public required string FilePath { get; init; }

    public required string DisplayName { get; init; }

    public string? SourceName { get; init; }

    public bool IsOnline { get; set; }
}

internal sealed class GamePilotSettingsRow
{
    public required GamePilotSettingsFile File { get; init; }

    public required GameOptionsDocument Document { get; set; }

    public Dictionary<string, object?> Values { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> DirtyOptions { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, ChatFontRecord> PlayerChatFonts { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, ChatFontRecord> MessageChatFonts { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> DirtyPlayerChatFontResolutions { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> DirtyMessageChatFontResolutions { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public bool HasDirtyChanges =>
        this.DirtyOptions.Count > 0 ||
        this.DirtyPlayerChatFontResolutions.Count > 0 ||
        this.DirtyMessageChatFontResolutions.Count > 0;
}

internal sealed record ChatFontRecord
{
    public required string Resolution { get; init; }

    public required string Font { get; set; }

    public required decimal Scale { get; set; }

    public required int LetterSpacing { get; set; }

    public required int LineSpacing { get; set; }
}

internal static class ChatFontValueCodec
{
    public static Dictionary<string, ChatFontRecord> Parse(string? value)
    {
        var result = new Dictionary<string, ChatFontRecord>(
            StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(value))
        {
            return result;
        }

        foreach (var rawRecord in value.Split(
                     "\\n",
                     StringSplitOptions.RemoveEmptyEntries |
                     StringSplitOptions.TrimEntries))
        {
            var colon = rawRecord.IndexOf(':');

            if (colon <= 0 || colon >= rawRecord.Length - 1)
            {
                continue;
            }

            var resolution = rawRecord[..colon].Trim();
            var payload = rawRecord[(colon + 1)..];
            var payloadParts = payload.Split(':', 2);

            if (payloadParts.Length != 2)
            {
                continue;
            }

            var fontFile = payloadParts[0].Trim();
            var values = payloadParts[1].Split(',');

            if (values.Length != 3 ||
                !decimal.TryParse(
                    values[0],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var scale) ||
                !int.TryParse(
                    values[1],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var letterSpacing) ||
                !int.TryParse(
                    values[2],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var lineSpacing))
            {
                continue;
            }

            var font = fontFile;

            if (font.StartsWith("font_", StringComparison.OrdinalIgnoreCase))
            {
                font = font["font_".Length..];
            }

            if (font.EndsWith(".tga", StringComparison.OrdinalIgnoreCase))
            {
                font = font[..^".tga".Length];
            }

            result[resolution] = new ChatFontRecord
            {
                Resolution = resolution,
                Font = font,
                Scale = scale,
                LetterSpacing = letterSpacing,
                LineSpacing = lineSpacing,
            };
        }

        return result;
    }

    public static string ReplaceOrAdd(
        string? value,
        ChatFontRecord replacement)
    {
        var records = string.IsNullOrWhiteSpace(value)
            ? new List<string>()
            : value.Split(
                    "\\n",
                    StringSplitOptions.RemoveEmptyEntries)
                .ToList();
        var replaced = false;

        for (var index = 0; index < records.Count; index++)
        {
            var colon = records[index].IndexOf(':');

            if (colon <= 0 ||
                !string.Equals(
                    records[index][..colon].Trim(),
                    replacement.Resolution,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            records[index] = FormatRecord(replacement);
            replaced = true;
            break;
        }

        if (!replaced)
        {
            records.Add(FormatRecord(replacement));
        }

        return string.Concat(
            records.Select(record => string.Concat(record, "\\n")));
    }

    public static string Format(
        IReadOnlyDictionary<string, ChatFontRecord> records)
    {
        return string.Concat(
            records.Values
                .OrderBy(
                    record => ResolutionSortKey(record.Resolution))
                .ThenBy(record => record.Resolution, StringComparer.OrdinalIgnoreCase)
                .Select(record => string.Concat(
                    FormatRecord(record),
                    "\\n")));
    }

    private static string FormatRecord(ChatFontRecord record)
    {
        return string.Concat(
            record.Resolution,
            ":font_",
            record.Font,
            ".tga:",
            record.Scale.ToString("0.0", CultureInfo.InvariantCulture),
            ",",
            record.LetterSpacing.ToString(CultureInfo.InvariantCulture),
            ",",
            record.LineSpacing.ToString(CultureInfo.InvariantCulture));
    }

    public static int MinimumLineSpacing(string font)
    {
        return font.ToLowerInvariant() switch
        {
            "tiny5" => -4,
            "small7" => -6,
            "small9" => -7,
            "small11" => -9,
            "small12" => -10,
            _ => -12,
        };
    }

    private static long ResolutionSortKey(string resolution)
    {
        var parts = resolution.Split('x', '×');

        if (parts.Length == 2 &&
            int.TryParse(parts[0], out var width) &&
            int.TryParse(parts[1], out var height))
        {
            return ((long)width << 32) | (uint)height;
        }

        return long.MaxValue;
    }
}
