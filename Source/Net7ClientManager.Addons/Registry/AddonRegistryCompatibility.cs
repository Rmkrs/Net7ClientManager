namespace Net7ClientManager.Addons.Registry;

using Net7ClientManager.Addons.Contracts;
using Net7ClientManager.Addons.Loading;

public static class AddonRegistryCompatibility
{
    public static AddonRegistryRelease? GetLatestCompatibleRelease(
        AddonRegistrySummary addon)
    {
        ArgumentNullException.ThrowIfNull(addon);

        return addon.Releases
            .Where(release =>
                release.ApiVersion == AddonApiVersion.Current)
            .Select(release => new
            {
                Release = release,
                Version = AddonSemanticVersion.TryParse(
                    release.Version,
                    out var version)
                        ? (AddonSemanticVersion?)version
                        : null,
            })
            .Where(item => item.Version != null)
            .OrderByDescending(item => item.Version)
            .Select(item => item.Release)
            .FirstOrDefault();
    }

    public static bool IsNewer(
        string candidateVersion,
        string installedVersion)
    {
        return AddonSemanticVersion.TryParse(
                   candidateVersion,
                   out var candidate) &&
               AddonSemanticVersion.TryParse(
                   installedVersion,
                   out var installed) &&
               candidate.CompareTo(installed) > 0;
    }
}
