namespace Net7ClientManager.Services;

using System.Globalization;
using System.Text.Json;
using Net7ClientManager.Models;

public sealed class GameAccountStore
{
    private static readonly JsonSerializerOptions jsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string filePath;

    public GameAccountStore()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Net7ClientManager");

        Directory.CreateDirectory(directory);

        this.filePath = Path.Combine(directory, "accounts.json");
    }

    public List<GameAccount> Load()
    {
        if (!File.Exists(this.filePath))
        {
            return [];
        }

        try
        {
            var json = File.ReadAllText(this.filePath);
            var config = JsonSerializer.Deserialize<GameAccountConfig>(json, jsonOptions);

            var accounts = config?.Accounts ?? [];

            foreach (var account in accounts)
            {
                foreach (var character in account.Characters)
                {
                    character.CharacterSlotNumber = NormalizeCharacterSlotNumber(character);
                }

                account.Characters = [.. account.Characters
                    .Where(character => character.CharacterSlotNumber is >= 1 and <= 5)
                    .GroupBy(character => character.CharacterSlotNumber)
                    .Select(group => group.Last())
                    .OrderBy(character => character.CharacterSlotNumber)];
            }

            return [.. accounts
                .OrderBy(account => account.SortOrder)
                .ThenBy(account => account.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(account => account.LoginName, StringComparer.OrdinalIgnoreCase)];
        }
        catch
        {
            return [];
        }
    }

    public void Save(IReadOnlyCollection<GameAccount> accounts)
    {
        var config = new GameAccountConfig
        {
            Accounts = [.. accounts
                .OrderBy(account => account.SortOrder)
                .ThenBy(account => account.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(account => account.LoginName, StringComparer.OrdinalIgnoreCase)],
        };

        var json = JsonSerializer.Serialize(config, jsonOptions);
        File.WriteAllText(this.filePath, json);
    }

    private static int NormalizeCharacterSlotNumber(GameCharacter character)
    {
        var legacyName = character.CharacterSelectClickActionName;

        if (!string.IsNullOrWhiteSpace(legacyName))
        {
            const string prefix = "Character Screen Slot ";

            if (legacyName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var numberText = legacyName[prefix.Length..];

                if (int.TryParse(numberText, CultureInfo.InvariantCulture, out var legacySlotNumber)
                    && legacySlotNumber is >= 1 and <= 5)
                {
                    return legacySlotNumber;
                }
            }
        }

        return character.CharacterSlotNumber is >= 1 and <= 5
            ? character.CharacterSlotNumber
            : 1;
    }

    private sealed class GameAccountConfig
    {
        public List<GameAccount> Accounts { get; set; } = [];
    }
}
