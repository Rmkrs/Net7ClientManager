namespace Net7ClientManager.Navigation;

using Net7ClientManager.Observations.Models;

internal static class EncounterDispositionResolver
{
    public static GalaxyAtlasSafetyBand Resolve(
        ForgeNavigationEncounterFactionBindingKind factionBindingKind,
        string factionIdentifier,
        int? intrinsicRelationshipRaw,
        ClientReputationObservation reputation)
    {
        if (factionBindingKind ==
                ForgeNavigationEncounterFactionBindingKind.Unaffiliated &&
            intrinsicRelationshipRaw is { } raw)
        {
            return raw switch
            {
                0 => GalaxyAtlasSafetyBand.Danger,
                1 => GalaxyAtlasSafetyBand.Neutral,
                2 or 3 => GalaxyAtlasSafetyBand.Safe,
                _ => GalaxyAtlasSafetyBand.Unknown,
            };
        }

        var hasFaction = !string.IsNullOrWhiteSpace(
            factionIdentifier);
        var bindingKind = factionBindingKind;

        if (bindingKind ==
                ForgeNavigationEncounterFactionBindingKind.Unknown &&
            hasFaction)
        {
            bindingKind =
                ForgeNavigationEncounterFactionBindingKind.FactionLinked;
        }

        if (bindingKind !=
                ForgeNavigationEncounterFactionBindingKind.FactionLinked ||
            !hasFaction ||
            !reputation.IsAvailable ||
            !TryFindFactionStanding(
                reputation,
                factionIdentifier,
                out var reaction))
        {
            return GalaxyAtlasSafetyBand.Unknown;
        }

        return ResolveFactionSafety(
            factionIdentifier,
            reaction);
    }

    private static GalaxyAtlasSafetyBand ResolveFactionSafety(
        string factionIdentifier,
        float reaction)
    {
        if (UsesOutlawSafetyProfile(factionIdentifier))
        {
            return reaction < 2000.0f
                ? GalaxyAtlasSafetyBand.Danger
                : GalaxyAtlasSafetyBand.Safe;
        }

        return FactionStandingBandResolver.ResolveStandard(reaction);
    }

    private static bool UsesOutlawSafetyProfile(
        string factionIdentifier)
    {
        var normalizedIdentifier = NormalizeFactionIdentifier(
            factionIdentifier);

        return normalizedIdentifier is "PIRATE" or "CHVZ";
    }

    private static bool TryFindFactionStanding(
        ClientReputationObservation reputation,
        string factionIdentifier,
        out float reaction)
    {
        reaction = 0.0f;
        var normalizedIdentifier = NormalizeFactionIdentifier(
            factionIdentifier);

        foreach (var faction in reputation.Factions)
        {
            if (!faction.Reaction.HasValue)
            {
                continue;
            }

            if (string.Equals(
                    faction.FactionKey,
                    factionIdentifier,
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    faction.DisplayName,
                    factionIdentifier,
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    NormalizeFactionIdentifier(faction.FactionKey),
                    normalizedIdentifier,
                    StringComparison.Ordinal) ||
                string.Equals(
                    NormalizeFactionIdentifier(faction.DisplayName),
                    normalizedIdentifier,
                    StringComparison.Ordinal))
            {
                reaction = faction.Reaction.Value;
                return true;
            }
        }

        return false;
    }

    private static string NormalizeFactionIdentifier(string value)
    {
        return string.Concat(
            value.Where(char.IsLetterOrDigit))
            .ToUpperInvariant();
    }
}
