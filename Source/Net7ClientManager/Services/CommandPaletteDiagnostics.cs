namespace Net7ClientManager.Services;

using Net7ClientManager.Models;

internal sealed record CommandPaletteDiagnosticEntry(
    DateTimeOffset TimestampUtc,
    string EventName,
    int? ProcessId,
    string? CharacterName,
    string? Details);

internal sealed class CommandPaletteDiagnostics
{
    private const int MaximumEntries = 10;
    private readonly object sync = new();
    private readonly Queue<CommandPaletteDiagnosticEntry> entries = new();

    public void Record(
        string eventName,
        ClientInstance? client = null,
        string? details = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);

        var characterName = client?.LiveCharacterIdentity.Name;
        var entry = new CommandPaletteDiagnosticEntry(
            DateTimeOffset.UtcNow,
            eventName.Trim(),
            client?.ProcessId,
            string.IsNullOrWhiteSpace(characterName)
                ? null
                : characterName.Trim(),
            NormalizeDetails(details));

        lock (this.sync)
        {
            this.entries.Enqueue(entry);

            while (this.entries.Count > MaximumEntries)
            {
                _ = this.entries.Dequeue();
            }
        }
    }

    public IReadOnlyList<CommandPaletteDiagnosticEntry> Snapshot()
    {
        lock (this.sync)
        {
            return [.. this.entries];
        }
    }

    private static string? NormalizeDetails(string? details)
    {
        if (string.IsNullOrWhiteSpace(details))
        {
            return null;
        }

        return details
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
    }
}
