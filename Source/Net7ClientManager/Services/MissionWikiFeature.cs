// ReSharper disable StringLiteralTypo
namespace Net7ClientManager.Services;

using System.Text.RegularExpressions;

internal static partial class MissionWikiFeature
{
    public const string AddonId =
        "net7.example.mission-wiki";

    public const string Name =
        "Mission Wiki";

    public const string Version =
        "2.3.0";

    public const string Description =
        "Shows the selected mission's Net-7 Wiki page or a pre-filled wiki search, "
        + "adds navigation helpers, and steps aside for game confirmations.";

    public static IReadOnlyList<string>
        CreatePageTitleCandidates(string missionName)
    {
        var normalized = NormalizeMissionName(missionName);

        if (normalized.Length == 0)
        {
            return [];
        }

        var tags = BracketTagRegex()
            .Matches(normalized)
            .Select(match =>
                match.Groups["tag"].Value.Trim())
            .Where(tag => tag.Length > 0)
            .ToArray();

        var baseTitle = BracketTagRegex()
            .Replace(normalized, " ");
        baseTitle = WhitespaceRegex()
            .Replace(baseTitle, " ")
            .Trim();

        if (tags.Length == 0)
        {
            return [baseTitle];
        }

        var versionTags = tags
            .Where(LooksLikeVersionTag)
            .ToArray();
        var descriptorTags = tags
            .Where(tag => !LooksLikeVersionTag(tag))
            .ToArray();

        List<string> candidates = [];

        AddDecoratedCandidate(
            candidates,
            baseTitle,
            descriptorTags,
            versionTags);
        AddDecoratedCandidate(
            candidates,
            baseTitle,
            [],
            versionTags);
        AddDecoratedCandidate(
            candidates,
            baseTitle,
            descriptorTags,
            []);
        AddCandidate(candidates, baseTitle);

        return candidates;
    }

    public static Uri BuildPageUri(string pageTitle)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pageTitle);

        var encodedTitle = Uri.EscapeDataString(
            pageTitle.Replace(' ', '_'));

        return new Uri(
            string.Concat(
                "https://net7wiki.bmsite.net/index.php?title=",
                encodedTitle),
            UriKind.Absolute);
    }

    public static Uri BuildSearchUri(string missionName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(missionName);

        var searchTerms = CreateSearchTerms(missionName);
        var encodedSearch = Uri.EscapeDataString(searchTerms);

        return new Uri(
            string.Concat(
                "https://net7wiki.bmsite.net/index.php?title=Special:Search&profile=default&search=",
                encodedSearch),
            UriKind.Absolute);
    }

    public static bool IsSearchUri(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (!IsAllowedArticleUri(uri))
        {
            return false;
        }

        if (uri.AbsolutePath.StartsWith(
                "/wiki/Special:Search",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (var component in uri.Query
                     .TrimStart('?')
                     .Split(
                         '&',
                         StringSplitOptions.RemoveEmptyEntries |
                         StringSplitOptions.TrimEntries))
        {
            var separatorIndex = component.IndexOf('=');
            var rawName = separatorIndex >= 0
                ? component[..separatorIndex]
                : component;
            var rawValue = separatorIndex >= 0
                ? component[(separatorIndex + 1)..]
                : "";

            var name = Uri.UnescapeDataString(
                rawName.Replace('+', ' '));

            if (!name.Equals(
                    "title",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = Uri.UnescapeDataString(
                rawValue.Replace('+', ' '));

            return value.Equals(
                "Special:Search",
                StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    public static bool IsAllowedTopLevelUri(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (uri.Scheme.Equals(
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase))
        {
            return IsWikiHost(uri) ||
                   uri.Host.Equals(
                       "challenges.cloudflare.com",
                       StringComparison.OrdinalIgnoreCase);
        }

        return uri.Scheme.Equals(
            "about",
            StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsAllowedArticleUri(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        return uri.Scheme.Equals(
                   Uri.UriSchemeHttps,
                   StringComparison.OrdinalIgnoreCase) &&
               IsWikiHost(uri);
    }

    private static string CreateSearchTerms(string missionName)
    {
        var normalized = NormalizeMissionName(missionName);
        var baseTitle = BracketTagRegex()
            .Replace(normalized, " ")
            .Replace("'", "", StringComparison.Ordinal);

        baseTitle = SearchNoiseRegex()
            .Replace(baseTitle, " ");

        return WhitespaceRegex()
            .Replace(baseTitle, " ")
            .Trim();
    }

    private static bool IsWikiHost(Uri uri)
    {
        return uri.Host.Equals(
            "net7wiki.bmsite.net",
            StringComparison.OrdinalIgnoreCase);
    }

    private static void AddDecoratedCandidate(
        ICollection<string> candidates,
        string baseTitle,
        IEnumerable<string> descriptorTags,
        IEnumerable<string> versionTags)
    {
        var decorations = descriptorTags
            .Concat(versionTags)
            .Select(tag => string.Concat("(", tag, ")"))
            .ToArray();

        if (decorations.Length == 0)
        {
            return;
        }

        var parts = new[] { baseTitle }
            .Concat(decorations)
            .ToArray();

        AddCandidate(
            candidates,
            string.Join(' ', parts));
    }

    private static void AddCandidate(
        ICollection<string> candidates,
        string candidate)
    {
        if (candidate.Length > 0 &&
            !candidate.Contains('[', StringComparison.Ordinal) &&
            !candidate.Contains(']', StringComparison.Ordinal) &&
            !candidates.Contains(
                candidate,
                StringComparer.OrdinalIgnoreCase))
        {
            candidates.Add(candidate);
        }
    }

    private static string NormalizeMissionName(string value)
    {
        return WhitespaceRegex()
            .Replace(
                value
                    .Replace('’', '\'')
                    .Replace('‘', '\''),
                " ")
            .Trim();
    }

    private static bool LooksLikeVersionTag(string tag)
    {
        return tag.StartsWith(
                   "Version",
                   StringComparison.OrdinalIgnoreCase) ||
               tag.StartsWith(
                   "Ver ",
                   StringComparison.OrdinalIgnoreCase) ||
               tag.StartsWith(
                   "v.",
                   StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(
        "[^\\p{L}\\p{Nd}]+",
        RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 100)]
    private static partial Regex SearchNoiseRegex();

    [GeneratedRegex(
        "\\s+",
        RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 100)]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(
        "\\s*\\[(?<tag>[^\\[\\]]+)\\]",
        RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 100)]
    private static partial Regex BracketTagRegex();
}
