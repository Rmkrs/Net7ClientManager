namespace Net7ClientManager.Addons.Loading;

using System.Globalization;

internal readonly record struct AddonSemanticVersion(
    int Major,
    int Minor,
    int Patch,
    string PreRelease) : IComparable<AddonSemanticVersion>
{
    public static bool TryParse(
        string value,
        out AddonSemanticVersion version)
    {
        version = default;

        if (string.IsNullOrWhiteSpace(value) ||
            !string.Equals(
                value,
                value.Trim(),
                StringComparison.Ordinal))
        {
            return false;
        }

        var buildParts = value.Split(
            '+',
            count: 2,
            StringSplitOptions.None);

        if (buildParts.Length == 2 &&
            !AreValidIdentifiers(
                buildParts[1],
                allowNumericLeadingZero: true))
        {
            return false;
        }

        var versionParts = buildParts[0].Split(
            '-',
            count: 2,
            StringSplitOptions.None);
        var coreParts = versionParts[0].Split('.');

        if (coreParts.Length != 3 ||
            !TryParseCoreNumber(coreParts[0], out var major) ||
            !TryParseCoreNumber(coreParts[1], out var minor) ||
            !TryParseCoreNumber(coreParts[2], out var patch))
        {
            return false;
        }

        var preRelease = versionParts.Length == 2
            ? versionParts[1]
            : "";

        if (preRelease.Length > 0 &&
            !AreValidIdentifiers(
                preRelease,
                allowNumericLeadingZero: false))
        {
            return false;
        }

        if (versionParts.Length == 2 &&
            preRelease.Length == 0)
        {
            return false;
        }

        version = new AddonSemanticVersion(
            major,
            minor,
            patch,
            preRelease);

        return true;
    }

    public int CompareTo(AddonSemanticVersion other)
    {
        var result = this.Major.CompareTo(other.Major);

        if (result != 0)
        {
            return result;
        }

        result = this.Minor.CompareTo(other.Minor);

        if (result != 0)
        {
            return result;
        }

        result = this.Patch.CompareTo(other.Patch);

        if (result != 0)
        {
            return result;
        }

        if (this.PreRelease.Length == 0)
        {
            return other.PreRelease.Length == 0
                ? 0
                : 1;
        }

        if (other.PreRelease.Length == 0)
        {
            return -1;
        }

        var leftIdentifiers = this.PreRelease.Split('.');
        var rightIdentifiers = other.PreRelease.Split('.');
        var sharedCount = Math.Min(
            leftIdentifiers.Length,
            rightIdentifiers.Length);

        for (var index = 0; index < sharedCount; index++)
        {
            result = CompareIdentifier(
                leftIdentifiers[index],
                rightIdentifiers[index]);

            if (result != 0)
            {
                return result;
            }
        }

        return leftIdentifiers.Length.CompareTo(
            rightIdentifiers.Length);
    }

    private static bool TryParseCoreNumber(
        string value,
        out int number)
    {
        number = 0;

        return value.Length > 0 &&
               (value.Length == 1 || value[0] != '0') &&
               int.TryParse(
                   value,
                   NumberStyles.None,
                   CultureInfo.InvariantCulture,
                   out number);
    }

    private static bool AreValidIdentifiers(
        string value,
        bool allowNumericLeadingZero)
    {
        if (value.Length == 0)
        {
            return false;
        }

        foreach (var identifier in value.Split('.'))
        {
            if (identifier.Length == 0 ||
                identifier.Any(character =>
                    !char.IsAsciiLetterOrDigit(character) &&
                    character != '-'))
            {
                return false;
            }

            if (!allowNumericLeadingZero &&
                identifier.Length > 1 &&
                identifier[0] == '0' &&
                identifier.All(char.IsAsciiDigit))
            {
                return false;
            }
        }

        return true;
    }

    private static int CompareIdentifier(
        string left,
        string right)
    {
        var leftIsNumeric = left.All(char.IsAsciiDigit);
        var rightIsNumeric = right.All(char.IsAsciiDigit);

        if (leftIsNumeric && rightIsNumeric)
        {
            var lengthResult = left.Length.CompareTo(right.Length);

            return lengthResult != 0
                ? lengthResult
                : string.Compare(
                    left,
                    right,
                    StringComparison.Ordinal);
        }

        if (leftIsNumeric)
        {
            return -1;
        }

        if (rightIsNumeric)
        {
            return 1;
        }

        return string.Compare(
            left,
            right,
            StringComparison.Ordinal);
    }
}
