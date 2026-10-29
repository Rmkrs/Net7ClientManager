namespace Net7ClientManager.Net7Site;

using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

// Net-7.org has no API for this; these read the same pages the website shows.
// The Avatar Transfer table carries each slot as JSON in a data-avatar attribute,
// which is the stable part. The race and profession come from the avatar page's heading.
internal static partial class Net7SitePageParser
{
    private static readonly string[] races = ["Terran", "Jenquai", "Progen"];

    private static readonly string[] professions =
    [
        "Enforcer", "Trader", "Scout",
        "Defender", "Explorer", "Seeker",
        "Sentinel", "Warrior", "Privateer",
    ];

    private static readonly JsonSerializerOptions jsonOptions = new()
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public static bool IsSignedIn(string html)
    {
        ArgumentNullException.ThrowIfNull(html);

        return html.Contains("href=\"#logout\"", StringComparison.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<Net7SiteGameAccount> ParseTransferRoster(string html)
    {
        ArgumentNullException.ThrowIfNull(html);

        var start = html.IndexOf("<div id=\"transfer\"", StringComparison.OrdinalIgnoreCase);

        if (start < 0)
        {
            throw new Net7SiteException(
                "The Avatar Transfer section was not found on Net-7.org. The website may have changed.");
        }

        var end = html.IndexOf("<div id=\"acc_vault\"", start, StringComparison.OrdinalIgnoreCase);
        var section = end < 0
            ? html[start..]
            : html[start..end];

        var accounts = new List<Net7SiteGameAccount>();

        foreach (Match row in RowRegex().Matches(section))
        {
            var rowHtml = row.Groups["row"].Value;
            var slots = SlotAttributeRegex().Matches(rowHtml)
                .Select(match => ParseSlot(match.Groups["json"].Value))
                .ToList();

            if (slots.Count == 0)
            {
                continue;
            }

            var cells = CellRegex().Matches(rowHtml);
            var loginName = cells.Count > 1
                ? CleanText(cells[1].Groups["cell"].Value)
                : "";

            if (string.IsNullOrWhiteSpace(loginName))
            {
                loginName = slots
                    .Select(slot => slot.AccountName)
                    .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? "";
            }

            if (string.IsNullOrWhiteSpace(loginName))
            {
                throw new Net7SiteException(
                    "A game account on the Avatar Transfer page has no name. The website may have changed.");
            }

            accounts.Add(new Net7SiteGameAccount
            {
                AccountId = slots[0].AccountId,
                LoginName = loginName,
                Avatars = [.. slots
                    .Where(slot => slot.AvatarId is > 0 && slot.Slot is >= 0 and <= 4)
                    .Select(slot => new Net7SiteAvatar
                    {
                        AvatarId = slot.AvatarId!.Value,
                        Name = slot.Name ?? "",
                        Slot = slot.Slot,
                    })
                    .Where(avatar => !string.IsNullOrWhiteSpace(avatar.Name))
                    .OrderBy(avatar => avatar.Slot)],
            });
        }

        return accounts;
    }

    // "Jenquai Explorer : Overall Level: 150" -> ("Jenquai", "Explorer").
    public static (string? Race, string? Profession) ParseAvatarClass(string html)
    {
        ArgumentNullException.ThrowIfNull(html);

        var match = CharacterClassRegex().Match(html);

        if (!match.Success)
        {
            return (null, null);
        }

        var text = CleanText(match.Groups["text"].Value);
        var colon = text.IndexOf(':', StringComparison.Ordinal);
        var words = (colon < 0 ? text : text[..colon])
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (words.Length != 2)
        {
            return (null, null);
        }

        var race = races.FirstOrDefault(value =>
            string.Equals(value, words[0], StringComparison.OrdinalIgnoreCase));
        var profession = professions.FirstOrDefault(value =>
            string.Equals(value, words[1], StringComparison.OrdinalIgnoreCase));

        return race == null || profession == null
            ? (null, null)
            : (race, profession);
    }

    private static TransferSlot ParseSlot(string attributeValue)
    {
        try
        {
            return JsonSerializer.Deserialize<TransferSlot>(
                       WebUtility.HtmlDecode(attributeValue),
                       jsonOptions) ??
                   throw new Net7SiteException("An avatar slot on Net-7.org was empty.");
        }
        catch (JsonException exception)
        {
            throw new Net7SiteException(
                "An avatar slot on Net-7.org could not be read. The website may have changed.",
                exception);
        }
    }

    private static string CleanText(string html)
    {
        return WebUtility.HtmlDecode(TagRegex().Replace(html, " "))
            .Replace(' ', ' ')
            .Trim();
    }

    [GeneratedRegex(
        @"<tr\b[^>]*>(?<row>.*?)</tr>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex RowRegex();

    [GeneratedRegex(
        @"<td\b[^>]*>(?<cell>.*?)</td>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex CellRegex();

    [GeneratedRegex(
        "data-avatar=\"(?<json>[^\"]*)\"",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex SlotAttributeRegex();

    [GeneratedRegex(
        "class=\"characterclass\"[^>]*>(?<text>.*?)</h2>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex CharacterClassRegex();

    [GeneratedRegex(
        "<[^>]*>",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex TagRegex();

    private sealed class TransferSlot
    {
        [JsonPropertyName("avatar_id")]
        public int? AvatarId { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("slot")]
        public int Slot { get; set; }

        [JsonPropertyName("account_id")]
        public int AccountId { get; set; }

        [JsonPropertyName("acc_name")]
        public string? AccountName { get; set; }
    }
}
