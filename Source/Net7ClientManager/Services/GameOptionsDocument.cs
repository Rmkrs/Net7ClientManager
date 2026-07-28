namespace Net7ClientManager.Services;

using System.Globalization;
using System.Text;

internal sealed class GameOptionsDocument
{
    private readonly List<string> lines;
    private readonly string newline;
    private readonly bool hasTrailingNewline;

    private GameOptionsDocument(
        List<string> lines,
        string newline,
        bool hasTrailingNewline)
    {
        this.lines = lines;
        this.newline = newline;
        this.hasTrailingNewline = hasTrailingNewline;
    }

    public static bool TryRead(
        string path,
        out GameOptionsDocument document,
        out string error)
    {
        document = null!;
        error = "";

        try
        {
            var text = File.ReadAllText(path, Encoding.Latin1);
            var newline = text.Contains("\r\n", StringComparison.Ordinal)
                ? "\r\n"
                : "\n";
            var hasTrailingNewline = text.EndsWith(newline, StringComparison.Ordinal);
            var lines = text.Split(
                    ["\r\n", "\n"],
                    StringSplitOptions.None)
                .ToList();

            if (hasTrailingNewline && lines.Count > 0 && lines[^1].Length == 0)
            {
                lines.RemoveAt(lines.Count - 1);
            }

            document = new GameOptionsDocument(
                lines,
                newline,
                hasTrailingNewline);
            return true;
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            ArgumentException or
            NotSupportedException)
        {
            error = ex.Message;
            return false;
        }
    }

    public string? GetValue(string optionName)
    {
        var location = this.FindOption(optionName);

        if (location == null || location.Value.ValueLineIndex < 0)
        {
            return null;
        }

        const string prefix = "OptionValue=";
        var line = this.lines[location.Value.ValueLineIndex];
        return line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? line[prefix.Length..]
            : null;
    }

    public bool? GetBoolean(string optionName)
    {
        return this.GetValue(optionName)?.Trim().ToLowerInvariant() switch
        {
            "yes" => true,
            "no" => false,
            _ => null,
        };
    }

    public decimal? GetDecimal(string optionName)
    {
        return decimal.TryParse(
            this.GetValue(optionName),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var value)
                ? value
                : null;
    }

    public bool SetValue(string optionName, string value)
    {
        var location = this.FindOption(optionName);

        if (location == null)
        {
            return false;
        }

        var replacement = string.Concat("OptionValue=", value);

        if (location.Value.ValueLineIndex >= 0)
        {
            this.lines[location.Value.ValueLineIndex] = replacement;
        }
        else
        {
            this.lines.Insert(
                location.Value.NameLineIndex + 1,
                replacement);
        }

        return true;
    }

    public bool SetBoolean(string optionName, bool value)
    {
        return this.SetValue(optionName, value ? "yes" : "no");
    }

    public bool SetFloat(string optionName, decimal value)
    {
        return this.SetValue(
            optionName,
            value.ToString("0.000000", CultureInfo.InvariantCulture));
    }

    public void SaveAtomic(
        string path,
        string backupDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupDirectory);
        var directory = Path.GetDirectoryName(path) ??
                        throw new InvalidOperationException(
                            "The options file has no parent directory.");
        var fileName = Path.GetFileName(path);
        var temporaryPath = Path.Combine(
            directory,
            string.Concat(fileName, ".n7cm.tmp"));
        Directory.CreateDirectory(backupDirectory);
        var backupPath = Path.Combine(
            backupDirectory,
            fileName);
        var text = string.Join(this.newline, this.lines);

        if (this.hasTrailingNewline)
        {
            text = string.Concat(text, this.newline);
        }

        File.WriteAllText(
            temporaryPath,
            text,
            Encoding.Latin1);

        try
        {
            File.Copy(path, backupPath, overwrite: true);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private OptionLocation? FindOption(string optionName)
    {
        for (var index = 0; index < this.lines.Count; index++)
        {
            const string prefix = "OptionName=";
            var line = this.lines[index];

            if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    line[prefix.Length..].Trim(),
                    optionName,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var valueLineIndex = -1;

            for (var candidate = index + 1;
                 candidate < this.lines.Count;
                 candidate++)
            {
                var candidateLine = this.lines[candidate];

                if (candidateLine.StartsWith("[", StringComparison.Ordinal))
                {
                    break;
                }

                if (candidateLine.StartsWith(
                        "OptionValue=",
                        StringComparison.OrdinalIgnoreCase))
                {
                    valueLineIndex = candidate;
                    break;
                }
            }

            return new OptionLocation(index, valueLineIndex);
        }

        return null;
    }

    private readonly record struct OptionLocation(
        int NameLineIndex,
        int ValueLineIndex);
}
