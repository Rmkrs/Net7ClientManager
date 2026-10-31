namespace Net7ClientManager.Net7Site;

using System.Text.Json;
using System.Text.Json.Serialization;

// Reads the reply of https://www.net-7.org/api/account for the signed-in login:
// its game accounts, and every avatar on them with slot, race and profession.
internal static class Net7SiteApiParser
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
        PropertyNameCaseInsensitive = true,
    };

    public static IReadOnlyList<Net7SiteGameAccount> ParseAccounts(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        AccountReply? reply;

        try
        {
            reply = JsonSerializer.Deserialize<AccountReply>(json, jsonOptions);
        }
        catch (JsonException exception)
        {
            throw new Net7SiteException(
                "The account list from Net-7.org could not be read. The website may have changed.",
                exception);
        }

        if (reply?.Result?.Accounts == null)
        {
            throw new Net7SiteException(
                "The account list from Net-7.org was empty. The website may have changed.");
        }

        var avatarsByAccount = (reply.Result.Avatars ?? [])
            .Where(avatar => avatar.Id > 0 &&
                             avatar.Slot is >= 0 and <= 4 &&
                             !string.IsNullOrWhiteSpace(avatar.Avatar?.Name))
            .ToLookup(avatar => avatar.AccountId);

        var accounts = new List<Net7SiteGameAccount>(reply.Result.Accounts.Count);

        foreach (var account in reply.Result.Accounts)
        {
            if (string.IsNullOrWhiteSpace(account.Username))
            {
                throw new Net7SiteException(
                    "A game account from Net-7.org has no name. The website may have changed.");
            }

            accounts.Add(new Net7SiteGameAccount
            {
                AccountId = account.Id,
                LoginName = account.Username.Trim(),
                Avatars = [.. avatarsByAccount[account.Id]
                    .Select(avatar => new Net7SiteAvatar
                    {
                        AvatarId = avatar.Id,
                        Name = avatar.Avatar!.Name!.Trim(),
                        Slot = avatar.Slot,
                        Race = KnownValue(races, avatar.Avatar.Race),
                        Profession = KnownValue(professions, ProfessionName(avatar.Avatar.Profession)),
                    })
                    .OrderBy(avatar => avatar.Slot)],
            });
        }

        return accounts;
    }

    // The site calls the Terran trade class "Tradesman"; the game and Client Manager say "Trader".
    private static string? ProfessionName(string? profession)
    {
        return string.Equals(profession?.Trim(), "Tradesman", StringComparison.OrdinalIgnoreCase)
            ? "Trader"
            : profession;
    }

    private static string? KnownValue(string[] values, string? value)
    {
        return values.FirstOrDefault(known =>
            string.Equals(known, value?.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private sealed class AccountReply
    {
        [JsonPropertyName("result")]
        public AccountResult? Result { get; set; }
    }

    private sealed class AccountResult
    {
        [JsonPropertyName("accounts")]
        public List<ApiAccount>? Accounts { get; set; }

        [JsonPropertyName("avatars")]
        public List<ApiAvatar>? Avatars { get; set; }
    }

    private sealed class ApiAccount
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("username")]
        public string? Username { get; set; }
    }

    private sealed class ApiAvatar
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("slot")]
        public int Slot { get; set; }

        [JsonPropertyName("account_id")]
        public int AccountId { get; set; }

        [JsonPropertyName("avatar")]
        public ApiAvatarDetails? Avatar { get; set; }
    }

    private sealed class ApiAvatarDetails
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("race")]
        public string? Race { get; set; }

        [JsonPropertyName("prof")]
        public string? Profession { get; set; }
    }
}
