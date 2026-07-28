namespace Net7ClientManager.Addons.Contracts;

public static class AddonActivationContexts
{
    public const string ApplicationStarted = "application_started";
    public const string Intro = "intro";
    public const string Login = "login";
    public const string CharacterSelection = "character_selection";
    public const string InGame = "in_game";

    private static readonly string[] defaultContexts =
    [
        InGame,
    ];

    private static readonly HashSet<string> supportedContexts =
        new(StringComparer.Ordinal)
        {
            ApplicationStarted,
            Intro,
            Login,
            CharacterSelection,
            InGame,
        };

    public static IReadOnlyList<string> GetEffectiveContexts(
        AddonManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var configured = manifest.Activation?.Contexts;

        if (configured == null || configured.Count == 0)
        {
            return [.. defaultContexts];
        }

        return
        [
            .. configured
                .Where(context => !string.IsNullOrWhiteSpace(context))
                .Select(context => context.Trim())
                .Distinct(StringComparer.Ordinal),
        ];
    }

    public static bool IsActive(
        AddonManifest manifest,
        string lifecycleContext)
    {
        return GetEffectiveContexts(manifest).Contains(
            lifecycleContext,
            StringComparer.Ordinal);
    }

    public static bool IsSupported(string context)
    {
        return supportedContexts.Contains(context);
    }
}
