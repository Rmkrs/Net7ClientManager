namespace Net7ClientManager.Services;

using System.Globalization;
using Net7ClientManager.Models;

public sealed class HostedClientTitleService
{
    public string BuildBaseTitle(
        ClientSlot? slot,
        GameAccount? account,
        GameCharacter? character,
        int processId)
    {
        if (slot == null)
        {
            return string.Create(CultureInfo.InvariantCulture, $"Earth & Beyond - PID {processId}");
        }

        var parts = new List<string>
        {
            string.IsNullOrWhiteSpace(slot.Name)
                ? "Unnamed Slot"
                : slot.Name.Trim(),
        };

        var accountTitle = this.BuildAccountTitle(account);

        if (!string.IsNullOrWhiteSpace(accountTitle))
        {
            parts.Add(accountTitle);
        }

        if (!string.IsNullOrWhiteSpace(character?.Name))
        {
            parts.Add(character.Name.Trim());
        }

        return string.Join(" - ", parts);
    }

    public string BuildTitle(
        string baseTitle,
        string? statusText,
        bool showStatus)
    {
        if (!showStatus || string.IsNullOrWhiteSpace(statusText))
        {
            return baseTitle;
        }

        return string.Concat(
            baseTitle,
            " - ",
            statusText.Trim());
    }

    private string? BuildAccountTitle(GameAccount? account)
    {
        if (account == null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(account.LoginName))
        {
            return account.LoginName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(account.DisplayName))
        {
            return account.DisplayName.Trim();
        }

        return null;
    }
}
