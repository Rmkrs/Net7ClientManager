namespace Net7ClientManager.Observations.Observers;

internal readonly record struct ClientMapIdentityReadResult(
    string Name,
    string Owner,
    string Title,
    string Rank,
    string MapDisplayName,
    string MapDisplayNameSource,
    string Status);
