namespace Net7ClientManager.Net7Site;

using System.Globalization;
using Net7ClientManager.Models;
using Net7ClientManager.Services;

// Brings Client Manager's accounts in line with the game accounts on one Net-7.org login.
//
// - Game accounts on that login are matched by login name; new ones are added.
// - Their characters are made to match the site exactly: added, moved to the right slot,
//   moved between accounts, and removed when the site no longer has them.
// - Accounts that are not on that login are left exactly as they are.
// - A character keeps its Id when its name is found again, so launches that refer to it
//   keep working; its race and profession are kept when the site does not give them.
// - Stored game passwords are never touched.
internal static class Net7SiteAccountSync
{
    public static Net7SiteSyncResult Merge(
        IReadOnlyList<GameAccount> accounts,
        IReadOnlyList<Net7SiteGameAccount> siteAccounts)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(siteAccounts);

        var result = accounts.Select(CopyAccount).ToList();
        var changes = new List<string>();
        var removedCount = 0;

        var accountsByLogin = new Dictionary<string, GameAccount>(StringComparer.OrdinalIgnoreCase);

        foreach (var account in result.Where(account => !string.IsNullOrWhiteSpace(account.LoginName)))
        {
            _ = accountsByLogin.TryAdd(account.LoginName.Trim(), account);
        }

        var syncedAccounts = siteAccounts
            .Select(site => accountsByLogin.GetValueOrDefault(site.LoginName))
            .OfType<GameAccount>()
            .ToList();

        // Every character currently on a synced account, by name, with the account it is on.
        var knownCharacters = new Dictionary<string, (GameAccount Account, GameCharacter Character)>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var account in syncedAccounts)
        {
            foreach (var character in account.Characters)
            {
                _ = knownCharacters.TryAdd(character.Name.Trim(), (account, character));
            }
        }

        var siteNames = new HashSet<string>(
            siteAccounts.SelectMany(site => site.Avatars).Select(avatar => avatar.Name.Trim()),
            StringComparer.OrdinalIgnoreCase);

        var nextSortOrder = result.Count == 0
            ? 0
            : result.Max(account => account.SortOrder) + 1;

        var synced = new HashSet<GameAccount>();

        foreach (var site in siteAccounts)
        {
            if (!accountsByLogin.TryGetValue(site.LoginName, out var account))
            {
                account = new GameAccount
                {
                    DisplayName = site.LoginName,
                    LoginName = site.LoginName,
                    SortOrder = nextSortOrder++,
                };

                result.Add(account);
                accountsByLogin[site.LoginName] = account;
                changes.Add($"Add account {AccountName(account)}");
            }

            _ = synced.Add(account);
            var characters = new List<GameCharacter>();

            foreach (var avatar in site.Avatars)
            {
                var slotNumber = avatar.Slot + 1;
                var name = avatar.Name.Trim();

                if (!knownCharacters.TryGetValue(name, out var known))
                {
                    characters.Add(new GameCharacter
                    {
                        CharacterSlotNumber = slotNumber,
                        Name = name,
                        Race = avatar.Race,
                        Profession = avatar.Profession,
                    });

                    changes.Add(Describe(account, slotNumber, $"add {name}{ClassText(avatar.Race, avatar.Profession)}"));
                    continue;
                }

                var existing = known.Character;
                var character = new GameCharacter
                {
                    Id = existing.Id,
                    CharacterSlotNumber = slotNumber,
                    Name = name,
                    Race = avatar.Race ?? existing.Race,
                    Profession = avatar.Profession ?? existing.Profession,
                    CharacterSelectClickActionName = existing.CharacterSelectClickActionName,
                };

                characters.Add(character);

                if (!ReferenceEquals(known.Account, account))
                {
                    changes.Add(Describe(account, slotNumber, $"move {name} here from {AccountName(known.Account)}"));
                }
                else if (existing.CharacterSlotNumber != slotNumber)
                {
                    changes.Add(Describe(account, slotNumber, $"move {name} here from slot {existing.CharacterSlotNumber.ToString(CultureInfo.InvariantCulture)}"));
                }
                else if (!string.Equals(existing.Name, name, StringComparison.Ordinal))
                {
                    changes.Add(Describe(account, slotNumber, $"rename {existing.Name} to {name}"));
                }

                if (!string.Equals(existing.Race, character.Race, StringComparison.Ordinal) ||
                    !string.Equals(existing.Profession, character.Profession, StringComparison.Ordinal))
                {
                    changes.Add(Describe(account, slotNumber, $"set {name} to{ClassText(character.Race, character.Profession)}"));
                }
            }

            foreach (var removed in account.Characters.Where(character => !siteNames.Contains(character.Name.Trim())))
            {
                removedCount++;
                changes.Add(Describe(account, removed.CharacterSlotNumber, $"remove {removed.Name} (not on Net-7.org)"));
            }

            account.Characters = [.. characters.OrderBy(character => character.CharacterSlotNumber)];
        }

        var untouched = result
            .Where(account => !synced.Contains(account))
            .Select(AccountName)
            .ToList();

        return new Net7SiteSyncResult
        {
            Accounts = result,
            Changes = changes,
            UntouchedAccounts = untouched,
            RemovedCharacterCount = removedCount,
        };
    }

    private static string Describe(GameAccount account, int slotNumber, string change)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{AccountName(account)}, slot {slotNumber}: {change}");
    }

    private static string AccountName(GameAccount account)
    {
        return UiObfuscationMode.AccountName(account.ToString(), fallback: "(unnamed)");
    }

    private static string ClassText(string? race, string? profession)
    {
        return race == null || profession == null
            ? ""
            : $" ({race} {profession})";
    }

    private static GameAccount CopyAccount(GameAccount account)
    {
        return new GameAccount
        {
            Id = account.Id,
            SortOrder = account.SortOrder,
            DisplayName = account.DisplayName,
            LoginName = account.LoginName,
            ProtectedPassword = account.ProtectedPassword,
            Characters = [.. account.Characters.Select(character => new GameCharacter
            {
                Id = character.Id,
                CharacterSlotNumber = character.CharacterSlotNumber,
                Name = character.Name,
                Race = character.Race,
                Profession = character.Profession,
                CharacterSelectClickActionName = character.CharacterSelectClickActionName,
            })],
        };
    }
}
