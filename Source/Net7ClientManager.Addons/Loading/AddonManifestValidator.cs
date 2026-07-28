namespace Net7ClientManager.Addons.Loading;

using System.Text.RegularExpressions;
using Net7ClientManager.Addons.Contracts;

internal static partial class AddonManifestValidator
{
    private const int RegexTimeoutMilliseconds = 1_000;

    public static string? Validate(AddonManifest? manifest)
    {
        if (manifest == null)
        {
            return "addon.json did not contain a manifest object.";
        }

        if (string.IsNullOrWhiteSpace(manifest.Id) ||
            manifest.Id.Length > 128 ||
            !AddonIdPattern().IsMatch(manifest.Id))
        {
            return "id must contain only lowercase ASCII letters, digits, dots, underscores or hyphens.";
        }

        if (string.IsNullOrWhiteSpace(manifest.Name) ||
            manifest.Name.Length > 128)
        {
            return "name is required and may not exceed 128 characters.";
        }

        if (string.IsNullOrWhiteSpace(manifest.Version) ||
            manifest.Version.Length > 64 ||
            !AddonSemanticVersion.TryParse(
                manifest.Version,
                out _))
        {
            return "version must use semantic version syntax such as 1.0.0.";
        }

        if (manifest.ApiVersion != AddonApiVersion.Current)
        {
            return string.Concat(
                "Unsupported apiVersion ",
                manifest.ApiVersion,
                "; this build supports apiVersion ",
                AddonApiVersion.Current,
                ".");
        }

        if (string.IsNullOrWhiteSpace(manifest.EntryPoint))
        {
            return "entryPoint is required.";
        }

        if (manifest.Activation == null)
        {
            return null;
        }

        if (manifest.Activation.Contexts.Count == 0)
        {
            return "activation.contexts must contain at least one lifecycle context.";
        }

        foreach (var context in manifest.Activation.Contexts)
        {
            if (string.IsNullOrWhiteSpace(context) ||
                !AddonActivationContexts.IsSupported(context.Trim()))
            {
                return string.Concat(
                    "Unsupported activation context '",
                    context,
                    "'. Supported values: ",
                    string.Join(
                        ", ",
                        [
                            AddonActivationContexts.ApplicationStarted,
                            AddonActivationContexts.Intro,
                            AddonActivationContexts.Login,
                            AddonActivationContexts.CharacterSelection,
                            AddonActivationContexts.InGame,
                        ]),
                    ".");
            }
        }

        return manifest.Activation.Contexts
            .Select(context => context.Trim())
            .Distinct(StringComparer.Ordinal)
            .Count() != manifest.Activation.Contexts.Count
                ? "activation.contexts may not contain duplicate values."
                : null;
    }

    [GeneratedRegex(
        "^[a-z0-9](?:[a-z0-9._-]{0,126}[a-z0-9])?$",
        RegexOptions.CultureInvariant,
        RegexTimeoutMilliseconds)]
    private static partial Regex AddonIdPattern();
}
