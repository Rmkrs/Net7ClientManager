namespace Net7ClientManager.Net7Site;

using Net7ClientManager.Models;

internal sealed record Net7SiteSyncResult
{
    // The full account list after the sync, including accounts that were left unchanged.
    public required IReadOnlyList<GameAccount> Accounts { get; init; }

    public required IReadOnlyList<string> Changes { get; init; }

    public required IReadOnlyList<string> UntouchedAccounts { get; init; }

    public required int RemovedCharacterCount { get; init; }
}
