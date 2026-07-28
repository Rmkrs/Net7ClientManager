namespace Net7ClientManager.Services;

using System.Globalization;
using System.Text;

/// <summary>
/// Read-only view of the inventory-icon entries in the client's basset.ini.
/// Item templates carry the BASE identifier; the matching section supplies
/// the logical Icon resource name stored in the art MIX archives.
/// </summary>
internal sealed class EnbBassetIconCatalog
{
    private const string BaseSectionPrefix = "BASE-";

    private readonly IReadOnlyDictionary<int, string> iconResourceNames;

    private EnbBassetIconCatalog(
        IReadOnlyDictionary<int, string> iconResourceNames)
    {
        this.iconResourceNames = iconResourceNames;
    }

    public static EnbBassetIconCatalog? Load(string bassetIniPath)
    {
        if (string.IsNullOrWhiteSpace(bassetIniPath) ||
            !File.Exists(bassetIniPath))
        {
            return null;
        }

        try
        {
            Dictionary<int, string> icons = [];
            int? currentBaseId = null;

            foreach (var sourceLine in File.ReadLines(
                         bassetIniPath,
                         Encoding.Latin1))
            {
                var line = sourceLine.Trim();

                if (line.Length == 0 ||
                    line.StartsWith(';') ||
                    line.StartsWith('#'))
                {
                    continue;
                }

                if (line[0] == '[')
                {
                    currentBaseId = TryParseBaseSection(line);
                    continue;
                }

                if (!currentBaseId.HasValue)
                {
                    continue;
                }

                var equalsIndex = line.IndexOf('=');

                if (equalsIndex <= 0 ||
                    !string.Equals(
                        line[..equalsIndex].Trim(),
                        "Icon",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var resourceName = line[(equalsIndex + 1)..]
                    .Trim()
                    .Trim('"');

                if (resourceName.Length > 0)
                {
                    icons[currentBaseId.Value] = resourceName;
                }
            }

            return icons.Count == 0
                ? null
                : new EnbBassetIconCatalog(icons);
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            System.Security.SecurityException or
            DecoderFallbackException)
        {
            return null;
        }
    }

    public bool TryGetIconResourceName(
        int baseId,
        out string resourceName)
    {
        if (this.iconResourceNames.TryGetValue(
                baseId,
                out var observedResourceName))
        {
            resourceName = observedResourceName;
            return true;
        }

        resourceName = "";
        return false;
    }

    private static int? TryParseBaseSection(string line)
    {
        var closeBracketIndex = line.IndexOf(']');

        if (closeBracketIndex <= 1)
        {
            return null;
        }

        var sectionName = line[1..closeBracketIndex].Trim();

        if (!sectionName.StartsWith(
                BaseSectionPrefix,
                StringComparison.OrdinalIgnoreCase) ||
            !int.TryParse(
                sectionName[BaseSectionPrefix.Length..],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var baseId) ||
            baseId < 0)
        {
            return null;
        }

        return baseId;
    }
}
