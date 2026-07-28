// ReSharper disable StringLiteralTypo
namespace Net7ClientManager.Observations.Models;

/// <summary>
/// Resolves player profession exclusively from live observations. The
/// character's reputation affiliation is preferred because it is a stable
/// profession organisation and covers emulator-added classes. Operational
/// FactionIdentifier and RPGInfo remain independent corroborating fallbacks.
/// </summary>
public static class ClientProfessionResolver
{
    private static readonly IReadOnlyDictionary<string, string>
        professionByAffiliation =
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase)
            {
                // Field-verified across all nine professions in the
                // Net-7 emulator on 2026-06-27.
                ["Sha'ha'dem Explorers"] = "Jenquai Explorer",
                ["Sharim Traders"] = "Jenquai Seeker",
                ["Shinwa Warriors"] = "Jenquai Defender",
                ["Sabine Explorers"] = "Progen Sentinel",
                ["Collegia Traders"] = "Progen Privateer",
                ["Centuriata Warriors"] = "Progen Warrior",
                ["Hyperia Explorers"] = "Terran Scout",
                ["InfinitiCorp Traders"] = "Terran Trader",
                ["EarthCorps Warriors"] = "Terran Enforcer",
            };

    public static ClientProfessionResolution ResolveDetailed(
        ClientShipIdentityObservation? identity,
        ClientCharacterProgressionObservation? progression,
        ClientReputationObservation? reputation)
    {
        var affiliation = Normalize(reputation?.Affiliation);
        var factionIdentifier = Normalize(identity?.FactionIdentifier);

        var affiliationCandidate = FromAffiliation(affiliation);
        var factionIdentifierCandidate = FromFactionIdentifier(
            factionIdentifier);
        var raceProfessionCandidate = FromRaceAndProfession(
            progression?.Race,
            progression?.Profession);

        var candidates = new[]
            {
                affiliationCandidate,
                factionIdentifierCandidate,
                raceProfessionCandidate,
            }
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Select(candidate => candidate!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var hasRawSource =
            !string.IsNullOrWhiteSpace(affiliation) ||
            !string.IsNullOrWhiteSpace(factionIdentifier) ||
            progression?.Race.HasValue == true ||
            progression?.Profession.HasValue == true;

        if (candidates.Length == 0)
        {
            return CreateResult(
                profession: null,
                hasRawSource
                    ? ClientProfessionResolutionStatus.Unknown
                    : ClientProfessionResolutionStatus.Unavailable,
                ClientProfessionResolutionSource.None,
                affiliation,
                factionIdentifier,
                progression,
                affiliationCandidate,
                factionIdentifierCandidate,
                raceProfessionCandidate,
                []);
        }

        // Reputation affiliation is the authoritative profession source.
        // This is especially important for the three emulator-added
        // professions, where older operational/raw fields can still expose a
        // legacy or transitional value. Preserve any disagreement in the
        // diagnostic candidate list, but do not hide a profession that the
        // known affiliation resolves unambiguously.
        if (!string.IsNullOrWhiteSpace(affiliationCandidate))
        {
            var corroboratingSourceCount = new[]
                {
                    affiliationCandidate,
                    factionIdentifierCandidate,
                    raceProfessionCandidate,
                }
                .Count(candidate =>
                    string.Equals(
                        candidate,
                        affiliationCandidate,
                        StringComparison.Ordinal));

            return CreateResult(
                affiliationCandidate,
                corroboratingSourceCount > 1
                    ? ClientProfessionResolutionStatus.ResolvedAndCorroborated
                    : ClientProfessionResolutionStatus.Resolved,
                ClientProfessionResolutionSource.ReputationAffiliation,
                affiliation,
                factionIdentifier,
                progression,
                affiliationCandidate,
                factionIdentifierCandidate,
                raceProfessionCandidate,
                candidates.Length > 1
                    ? candidates
                    : []);
        }

        if (candidates.Length > 1)
        {
            return CreateResult(
                profession: null,
                ClientProfessionResolutionStatus.Conflicting,
                ClientProfessionResolutionSource.None,
                affiliation,
                factionIdentifier,
                progression,
                affiliationCandidate,
                factionIdentifierCandidate,
                raceProfessionCandidate,
                candidates);
        }

        var profession = candidates[0];
        var corroboratingSourceCount2 = new[]
            {
                affiliationCandidate,
                factionIdentifierCandidate,
                raceProfessionCandidate,
            }
            .Count(candidate =>
                string.Equals(
                    candidate,
                    profession,
                    StringComparison.Ordinal));

        return CreateResult(
            profession,
            corroboratingSourceCount2 > 1
                ? ClientProfessionResolutionStatus.ResolvedAndCorroborated
                : ClientProfessionResolutionStatus.Resolved,
            GetPreferredSource(
                profession,
                affiliationCandidate,
                factionIdentifierCandidate),
            affiliation,
            factionIdentifier,
            progression,
            affiliationCandidate,
            factionIdentifierCandidate,
            raceProfessionCandidate,
            []);
    }

    private static string? FromAffiliation(string? affiliation)
    {
        var normalized = Normalize(affiliation);

        return normalized != null &&
               professionByAffiliation.TryGetValue(
                   normalized,
                   out var profession)
            ? profession
            : null;
    }

    public static string? FromFactionIdentifier(
        string? factionIdentifier)
    {
        return Normalize(factionIdentifier)?.ToUpperInvariant() switch
        {
            "JD" => "Jenquai Defender",
            "JE" => "Jenquai Explorer",
            "JS" => "Jenquai Seeker",
            "PS" => "Progen Sentinel",
            "PW" => "Progen Warrior",
            "PP" => "Progen Privateer",
            "TE" => "Terran Enforcer",
            "TT" => "Terran Trader",
            "TS" => "Terran Scout",
            _ => null,
        };
    }

    private static string? FromRaceAndProfession(
        int? race,
        int? profession)
    {
        if (!race.HasValue ||
            !profession.HasValue ||
            race.Value < 0 ||
            race.Value > 2 ||
            profession.Value < 0 ||
            profession.Value > 2)
        {
            return null;
        }

        return (race.Value, profession.Value) switch
        {
            (0, 0) => "Jenquai Explorer",
            (0, 1) => "Jenquai Seeker",
            (0, 2) => "Jenquai Defender",
            (1, 0) => "Progen Sentinel",
            (1, 1) => "Progen Privateer",
            (1, 2) => "Progen Warrior",
            (2, 0) => "Terran Scout",
            (2, 1) => "Terran Trader",
            (2, 2) => "Terran Enforcer",
            _ => null,
        };
    }

    public static string? RaceNameFromRaw(int? race)
    {
        return race switch
        {
            0 => "Jenquai",
            1 => "Progen",
            2 => "Terran",
            _ => null,
        };
    }

    private static ClientProfessionResolution CreateResult(
        string? profession,
        ClientProfessionResolutionStatus status,
        ClientProfessionResolutionSource source,
        string? affiliation,
        string? factionIdentifier,
        ClientCharacterProgressionObservation? progression,
        string? affiliationCandidate,
        string? factionIdentifierCandidate,
        string? raceProfessionCandidate,
        IReadOnlyList<string> conflictingCandidates)
    {
        return new ClientProfessionResolution
        {
            Profession = profession,
            Status = status,
            Source = source,
            ReputationAffiliation = affiliation,
            FactionIdentifier = factionIdentifier,
            RaceRaw = progression?.Race,
            ProfessionRaw = progression?.Profession,
            AffiliationCandidate = affiliationCandidate,
            FactionIdentifierCandidate = factionIdentifierCandidate,
            RaceProfessionCandidate = raceProfessionCandidate,
            ConflictingCandidates = conflictingCandidates,
        };
    }

    private static ClientProfessionResolutionSource GetPreferredSource(
        string profession,
        string? affiliationCandidate,
        string? factionIdentifierCandidate)
    {
        if (string.Equals(
                affiliationCandidate,
                profession,
                StringComparison.Ordinal))
        {
            return ClientProfessionResolutionSource.ReputationAffiliation;
        }

        return string.Equals(
                factionIdentifierCandidate,
                profession,
                StringComparison.Ordinal)
            ? ClientProfessionResolutionSource.FactionIdentifier
            : ClientProfessionResolutionSource.RaceAndProfession;
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return string.Join(
            ' ',
            value.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries));
    }
}

