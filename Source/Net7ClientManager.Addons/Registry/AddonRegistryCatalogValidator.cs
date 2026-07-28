namespace Net7ClientManager.Addons.Registry;

using Net7ClientManager.Addons.Loading;

internal static class AddonRegistryCatalogValidator
{
    private const long MaximumPackageSize = 16 * 1024 * 1024;
    private const int PackageHashLength = 64;

    public static IReadOnlyDictionary<string, AddonRegistrySummary> CreateIndex(
        IReadOnlyList<AddonRegistrySummary> addons)
    {
        ArgumentNullException.ThrowIfNull(addons);

        if (addons.Count == 0)
        {
            throw new InvalidDataException(
                "Net7 Forge returned an empty addon catalog.");
        }

        Dictionary<string, AddonRegistrySummary> index = new(
            StringComparer.Ordinal);

        foreach (var addon in addons)
        {
            Validate(addon);
            var normalized = Normalize(addon!);

            if (!index.TryAdd(normalized.Id, normalized))
            {
                throw new InvalidDataException(
                    string.Concat(
                        "Net7 Forge returned duplicate addon id '",
                        normalized.Id,
                        "'."));
            }
        }

        return index;
    }

    private static void Validate(AddonRegistrySummary? addon)
    {
        if (addon == null ||
            addon.LatestRelease == null ||
            addon.Releases == null ||
            addon.Releases.Count == 0)
        {
            throw new InvalidDataException(
                "Net7 Forge returned an incomplete addon entry.");
        }

        if (string.IsNullOrWhiteSpace(addon.Id) ||
            string.IsNullOrWhiteSpace(addon.Name) ||
            string.IsNullOrWhiteSpace(addon.PublisherId) ||
            string.IsNullOrWhiteSpace(addon.PublisherName))
        {
            throw new InvalidDataException(
                "Net7 Forge returned invalid addon metadata.");
        }

        HashSet<string> versions = new(StringComparer.Ordinal);

        foreach (var release in addon.Releases)
        {
            ValidateRelease(addon, release);

            if (!versions.Add(release.Version))
            {
                throw new InvalidDataException(
                    string.Concat(
                        "Net7 Forge returned duplicate version '",
                        release.Version,
                        "' for addon '",
                        addon.Id,
                        "'."));
            }
        }

        ValidateRelease(addon, addon.LatestRelease);

        if (!versions.Contains(addon.LatestRelease.Version))
        {
            throw new InvalidDataException(
                string.Concat(
                    "Net7 Forge returned a latest release that is not part of addon '",
                    addon.Id,
                    "'."));
        }
    }

    private static void ValidateRelease(
        AddonRegistrySummary addon,
        AddonRegistryRelease? release)
    {
        if (release == null ||
            !string.Equals(
                addon.Id,
                release.AddonId,
                StringComparison.Ordinal) ||
            !string.Equals(
                addon.PublisherId,
                release.PublisherId,
                StringComparison.Ordinal) ||
            !string.Equals(
                addon.PublisherName,
                release.PublisherName,
                StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(release.Name) ||
            release.ApiVersion <= 0 ||
            release.PublishedAt == default ||
            !AddonSemanticVersion.TryParse(
                release.Version,
                out _) ||
            release.PackageSize <= 0 ||
            release.PackageSize > MaximumPackageSize ||
            !IsSha256(release.PackageSha256) ||
            !string.Equals(
                release.DownloadUrl,
                string.Concat(
                    "/api/v1/packages/",
                    release.PackageSha256),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                string.Concat(
                    "Net7 Forge returned invalid metadata for addon '",
                    addon.Id,
                    "' version '",
                    release?.Version ?? "<unknown>",
                    "'."));
        }
    }

    private static AddonRegistrySummary Normalize(
        AddonRegistrySummary addon)
    {
        return addon with
        {
            Categories = NormalizeTerms(addon.Categories),
            Tags = NormalizeTerms(addon.Tags),
            Releases = [.. addon.Releases],
        };
    }

    private static IReadOnlyList<string> NormalizeTerms(
        IReadOnlyList<string>? values)
    {
        return
        [
            .. (values ?? [])
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase),
        ];
    }

    private static bool IsSha256(string? value)
    {
        return value?.Length == PackageHashLength &&
               value.All(IsHexCharacter);
    }

    private static bool IsHexCharacter(char value)
    {
        return value is >= '0' and <= '9' or
            >= 'a' and <= 'f' or
            >= 'A' and <= 'F';
    }
}
