namespace Net7ClientManager.Social;

using System.Globalization;
using System.Text;
using Net7ClientManager.Navigation;

internal static class SocialWorldSearch
{
    public static IReadOnlyList<WorldSearchMatch> Search(
        SocialDataSnapshot snapshot,
        GalaxyDataSet dataSet,
        string? query,
        WorldSearchKind? kind,
        Func<DateTimeOffset, SocialPresenceFreshness> getFreshness,
        int maximumResults)
    {
        if (maximumResults <= 0)
        {
            return [];
        }

        var normalizedQuery = Normalize(query);
        var browseWithoutText = normalizedQuery.Length == 0;
        var presenceByPilot = snapshot.Presence.ToDictionary(
            record => record.PilotName,
            StringComparer.OrdinalIgnoreCase);
        List<WorldSearchEntry> entries = [];

        if (!kind.HasValue ||
            kind == WorldSearchKind.Social ||
            kind == WorldSearchKind.SocialPresence)
        {
            foreach (var presence in snapshot.Presence)
            {
                var freshness = getFreshness(presence.UpdatedAtUtc);
                var location = JoinNonEmpty(
                    freshness switch
                    {
                        SocialPresenceFreshness.Online => "Online",
                        SocialPresenceFreshness.RecentlySeen => "Recently seen",
                        _ => "Offline",
                    },
                    DescribePresenceLocation(presence),
                    presence.SectorName);
                entries.Add(CreateEntry(
                    $"social-presence:{Normalize(presence.PilotName)}",
                    presence.PilotName,
                    WorldSearchKind.SocialPresence,
                    presence,
                    location,
                    [
                        presence.PilotName,
                        presence.SystemName ?? "",
                        presence.SectorName ?? "",
                        presence.StationName ?? "",
                        presence.NearestNavName ?? "",
                        location,
                        "pilot presence social",
                    ],
                    dataSet,
                    allowDestination:
                        freshness == SocialPresenceFreshness.Online));
            }
        }

        if (!kind.HasValue ||
            kind == WorldSearchKind.Social ||
            kind == WorldSearchKind.LookingForGuild)
        {
            foreach (var profile in snapshot.LookingForGuild)
            {
                presenceByPilot.TryGetValue(profile.PilotName, out var presence);
                var presenceFreshness = presence == null
                    ? (SocialPresenceFreshness?)null
                    : getFreshness(presence.UpdatedAtUtc);
                var location = JoinNonEmpty(
                    profile.ProfessionName,
                    profile.OverallLevel.HasValue
                        ? string.Create(
                            CultureInfo.InvariantCulture,
                            $"level {profile.OverallLevel.Value}")
                        : null,
                    string.Join(", ", profile.InterestTags),
                    presenceFreshness switch
                    {
                        SocialPresenceFreshness.Online => "Online",
                        SocialPresenceFreshness.RecentlySeen => "Recently seen",
                        SocialPresenceFreshness.Offline => "Offline",
                        _ => null,
                    },
                    DescribePresenceLocation(presence),
                    presence?.SectorName);
                entries.Add(CreateEntry(
                    $"looking-for-guild:{Normalize(profile.PilotName)}",
                    profile.PilotName,
                    WorldSearchKind.LookingForGuild,
                    presence,
                    location,
                    [
                        profile.PilotName,
                        profile.ProfessionName ?? "",
                        profile.OverallLevel?.ToString(CultureInfo.InvariantCulture) ?? "",
                        string.Join(" ", profile.InterestTags),
                        string.Join(" ", profile.Languages),
                        profile.OtherLanguage ?? "",
                        profile.Region ?? "",
                        profile.OtherRegion ?? "",
                        profile.Availability ?? "",
                        profile.Message ?? "",
                        presence?.StationName ?? "",
                        presence?.NearestNavName ?? "",
                        presence?.SectorName ?? "",
                        location,
                        "looking for guild lfg pilot social",
                    ],
                    dataSet,
                    allowDestination:
                        presenceFreshness == SocialPresenceFreshness.Online));
            }
        }

        if (!kind.HasValue ||
            kind == WorldSearchKind.Social ||
            kind == WorldSearchKind.GuildRecruitment)
        {
            foreach (var guild in snapshot.GuildRecruitment)
            {
                entries.Add(new WorldSearchEntry
                {
                    Identity = $"guild-recruitment:{Normalize(guild.GuildName)}",
                    Name = guild.GuildName,
                    Kind = WorldSearchKind.GuildRecruitment,
                    SystemName = "",
                    SectorKey = "",
                    SectorName = "",
                    Location = JoinNonEmpty(
                        $"Contact: {guild.PublishingPilotName}",
                        string.Join(", ", guild.FocusTags)),
                    SearchTerms =
                    [
                        guild.GuildName,
                        guild.PublishingPilotName,
                        guild.OtherContacts ?? "",
                        string.Join(" ", guild.FocusTags),
                        string.Join(" ", guild.WantedProfessions),
                        string.Join(" ", guild.Languages),
                        guild.OtherLanguage ?? "",
                        guild.Region ?? "",
                        guild.OtherRegion ?? "",
                        guild.ActiveTimes ?? "",
                        guild.Requirements ?? "",
                        guild.Message ?? "",
                        "guild recruitment recruiting social",
                    ],
                });
            }
        }

        return [.. entries
            .Select(entry => new
            {
                Entry = entry,
                Rank = browseWithoutText
                    ? 500
                    : Score(entry, normalizedQuery),
            })
            .Where(candidate => candidate.Rank != int.MaxValue)
            .OrderBy(candidate => candidate.Rank)
            .ThenBy(candidate => candidate.Entry.Name, StringComparer.OrdinalIgnoreCase)
            .Take(maximumResults)
            .Select(candidate => new WorldSearchMatch
            {
                Entry = candidate.Entry,
                Rank = candidate.Rank,
            })];
    }

    private static WorldSearchEntry CreateEntry(
        string identity,
        string name,
        WorldSearchKind kind,
        SocialPresenceRecord? presence,
        string location,
        IReadOnlyList<string> searchTerms,
        GalaxyDataSet dataSet,
        bool allowDestination)
    {
        GalaxySectorDefinition? sector = null;
        NavigationDestination? destination = null;

        if (allowDestination &&
            presence != null &&
            dataSet.Topology.TryResolve(
                presence.SectorKey ?? presence.SectorName,
                out sector!))
        {
            destination = ResolveDestination(dataSet, sector, presence);
        }

        return new WorldSearchEntry
        {
            Identity = identity,
            Name = name,
            Kind = kind,
            SystemName = presence?.SystemName ?? sector?.SystemName ?? "",
            SectorKey = presence?.SectorKey ?? sector?.Key ?? "",
            SectorName = presence?.SectorName ?? sector?.Name ?? "",
            Location = location,
            SearchTerms = searchTerms,
            Destination = destination,
        };
    }

    private static NavigationDestination? ResolveDestination(
        GalaxyDataSet dataSet,
        GalaxySectorDefinition sector,
        SocialPresenceRecord presence)
    {
        var locationName = !string.IsNullOrWhiteSpace(presence.StationName)
            ? presence.StationName
            : presence.NearestNavName;

        if (!string.IsNullOrWhiteSpace(locationName) &&
            dataSet.Catalog.TryGetSector(sector.Key, out var catalogSector))
        {
            var normalizedName = GalaxyTopology.NormalizeName(
                locationName);
            var target = catalogSector.Targets.FirstOrDefault(candidate =>
                string.Equals(
                    GalaxyTopology.NormalizeName(candidate.Name),
                    normalizedName,
                    StringComparison.Ordinal) ||
                string.Equals(
                    GalaxyTopology.NormalizeName(candidate.MapDisplayName),
                    normalizedName,
                    StringComparison.Ordinal));

            if (target != null)
            {
                return NavigationDestination.ForTarget(sector, target);
            }
        }

        return NavigationDestination.ForSector(sector);
    }

    private static string? DescribePresenceLocation(
        SocialPresenceRecord? presence)
    {
        if (!string.IsNullOrWhiteSpace(presence?.StationName))
        {
            return string.Concat("Docked at ", presence.StationName);
        }

        if (!string.IsNullOrWhiteSpace(presence?.NearestNavName))
        {
            return string.Concat("Near ", presence.NearestNavName);
        }

        return null;
    }

    private static int Score(
        WorldSearchEntry entry,
        string normalizedQuery)
    {
        var normalizedName = Normalize(entry.Name);
        if (normalizedName == normalizedQuery)
        {
            return 0;
        }

        if (normalizedName.StartsWith(normalizedQuery, StringComparison.Ordinal))
        {
            return 10;
        }

        if (normalizedName.Contains(normalizedQuery, StringComparison.Ordinal))
        {
            return 20;
        }

        var terms = Normalize(string.Join(" ", entry.SearchTerms));
        if (terms.Contains(normalizedQuery, StringComparison.Ordinal))
        {
            return 40;
        }

        var tokens = normalizedQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return tokens.All(token => terms.Contains(token, StringComparison.Ordinal))
            ? 60
            : int.MaxValue;
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var builder = new StringBuilder(value.Length);
        var pendingSeparator = false;

        foreach (var character in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                if (pendingSeparator && builder.Length > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(character);
                pendingSeparator = false;
            }
            else
            {
                pendingSeparator = true;
            }
        }

        return builder.ToString();
    }

    private static string JoinNonEmpty(params string?[] values)
    {
        return string.Join(
            " · ",
            values.Where(value => !string.IsNullOrWhiteSpace(value)));
    }
}
