namespace Net7ClientManager.Services;

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

internal static class ItemEffectTextFormatter
{
    public static ItemEffectDisplayText Resolve(
        string? nameFormat,
        IReadOnlyList<float> nameValues,
        string? descriptionFormat,
        IReadOnlyList<float> descriptionValues)
    {
        ArgumentNullException.ThrowIfNull(nameValues);
        ArgumentNullException.ThrowIfNull(descriptionValues);

        return new ItemEffectDisplayText(
            Normalize(Reconstruct(nameFormat, nameValues)),
            Normalize(Reconstruct(descriptionFormat, descriptionValues)));
    }

    public static string Reconstruct(
        string? format,
        IReadOnlyList<float> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        if (string.IsNullOrWhiteSpace(format))
        {
            return "";
        }

        var builder = new StringBuilder(format.Length + 32);
        var cursor = 0;
        var valueIndex = 0;

        while (cursor < format.Length)
        {
            var tokenStart = format.IndexOf(
                "%value",
                cursor,
                StringComparison.Ordinal);

            if (tokenStart < 0)
            {
                builder.Append(
                    format,
                    cursor,
                    format.Length - cursor);
                break;
            }

            builder.Append(
                format,
                cursor,
                tokenStart - cursor);
            var tokenEnd = format.IndexOf(
                '%',
                tokenStart + 6);

            if (tokenEnd < 0)
            {
                builder.Append(
                    format,
                    tokenStart,
                    format.Length - tokenStart);
                break;
            }

            var numericFormat = format.Substring(
                tokenStart + 6,
                tokenEnd - (tokenStart + 6));

            if (valueIndex < values.Count &&
                TryFormatNativeFloat(
                    values[valueIndex],
                    numericFormat,
                    out var formatted))
            {
                builder.Append(formatted);
            }
            else
            {
                builder.Append(
                    format,
                    tokenStart,
                    tokenEnd - tokenStart + 1);
            }

            valueIndex++;
            cursor = tokenEnd + 1;
        }

        return builder.ToString().Trim();
    }

    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        var normalized = text
            .Replace("\\n", "\n", StringComparison.Ordinal)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Trim();

        while (normalized.StartsWith(
                   "Effect:",
                   StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[7..].TrimStart();
        }

        normalized = Regex.Replace(
            normalized,
            @"\s+",
            " ");
        normalized = normalized.Replace(
            " per seconds",
            " per second",
            StringComparison.OrdinalIgnoreCase);

        return normalized.Trim();
    }

    private static bool TryFormatNativeFloat(
        float value,
        string numericFormat,
        out string formatted)
    {
        formatted = "";

        if (numericFormat.Length < 3 ||
            numericFormat[^1] != 'f')
        {
            return false;
        }

        var decimalPoint = numericFormat.LastIndexOf('.');

        if (decimalPoint < 0 ||
            decimalPoint == numericFormat.Length - 2 ||
            !int.TryParse(
                numericFormat.AsSpan(
                    decimalPoint + 1,
                    numericFormat.Length - decimalPoint - 2),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var precision) ||
            precision is < 0 or > 9)
        {
            return false;
        }

        formatted = value.ToString(
            string.Create(
                CultureInfo.InvariantCulture,
                $"F{precision}"),
            CultureInfo.InvariantCulture);
        return true;
    }
}

internal sealed record ItemEffectDisplayText(
    string Name,
    string Description);
