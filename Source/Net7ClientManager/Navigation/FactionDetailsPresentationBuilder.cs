namespace Net7ClientManager.Navigation;

using Net7ClientManager.ActivityJournal;
using Net7ClientManager.Observations.Models;

internal static class FactionDetailsPresentationBuilder
{
    private const int MaximumRecentChanges = 4;

    public static FactionDetailsPresentation Build(
        ClientPanelPresentationObservation panelPresentation,
        ClientLocalPlayerObservation localPlayer,
        Func<uint, string, int, IReadOnlyList<ReputationJournalEntry>>
            getReputationHistory)
    {
        ArgumentNullException.ThrowIfNull(panelPresentation);
        ArgumentNullException.ThrowIfNull(localPlayer);
        ArgumentNullException.ThrowIfNull(getReputationHistory);

        var details = panelPresentation.FactionDetails;

        if (!details.IsDisplayed ||
            string.IsNullOrWhiteSpace(details.SelectedFactionKey))
        {
            return FactionDetailsPresentation.Hidden;
        }

        var factionKey = details.SelectedFactionKey.Trim();
        var reputation = FindFaction(
            localPlayer.Reputation,
            factionKey);
        var displayName = FirstNonEmpty(
            reputation?.DisplayName,
            FactionDisplayNameResolver.GetDisplayName(factionKey),
            factionKey);
        var description = reputation?.Description?.Trim() ?? "";
        var rules = FactionReactionCatalog.GetRules(factionKey);
        var helpful = rules
            .Where(rule => rule.Multiplier > 0)
            .Select(ToPresentation)
            .OrderByDescending(rule => rule.Multiplier)
            .ThenBy(
                rule => rule.DefeatedFactionName,
                StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        var harmful = rules
            .Where(rule => rule.Multiplier < 0)
            .Select(ToPresentation)
            .OrderBy(rule => rule.Multiplier)
            .ThenBy(
                rule => rule.DefeatedFactionName,
                StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        IReadOnlyList<ReputationJournalEntry> history = [];

        if (localPlayer.ObjectId is not 0 and not uint.MaxValue)
        {
            history = getReputationHistory(
                localPlayer.ObjectId,
                factionKey,
                MaximumRecentChanges);
        }

        return new FactionDetailsPresentation
        {
            IsVisible = true,
            FactionKey = factionKey,
            DisplayName = displayName,
            Description = description,
            Reputation = reputation?.Reaction,
            IsAffiliated = IsAffiliated(
                localPlayer.Reputation.Affiliation,
                factionKey,
                displayName),
            HelpfulKills = helpful,
            HarmfulKills = harmful,
            RecentChanges = history,
        };
    }

    private static ClientFactionReputationObservation? FindFaction(
        ClientReputationObservation reputation,
        string factionKey)
    {
        if (!reputation.IsAvailable)
        {
            return null;
        }

        return reputation.Factions.FirstOrDefault(faction =>
            string.Equals(
                faction.FactionKey,
                factionKey,
                StringComparison.OrdinalIgnoreCase));
    }

    private static FactionReactionPresentation ToPresentation(
        FactionReactionRule rule)
    {
        return new FactionReactionPresentation
        {
            DefeatedFactionKey = rule.DefeatedFactionKey,
            DefeatedFactionName =
                FactionDisplayNameResolver.GetDisplayNameOrDefault(
                    rule.DefeatedFactionKey,
                    rule.DefeatedFactionKey),
            Multiplier = rule.Multiplier,
            IsEstimated = rule.IsEstimated,
        };
    }

    private static bool IsAffiliated(
        string? affiliation,
        string factionKey,
        string displayName)
    {
        if (string.IsNullOrWhiteSpace(affiliation))
        {
            return false;
        }

        var normalizedAffiliation = Normalize(affiliation);
        return normalizedAffiliation == Normalize(factionKey) ||
               normalizedAffiliation == Normalize(displayName) ||
               normalizedAffiliation == Normalize(
                   FactionDisplayNameResolver.GetDisplayName(factionKey));
    }

    private static string Normalize(string value) =>
        string.Concat(value.Where(char.IsLetterOrDigit))
            .ToUpperInvariant();

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value =>
            !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";
}
