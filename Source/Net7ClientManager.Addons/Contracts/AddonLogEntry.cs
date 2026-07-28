namespace Net7ClientManager.Addons.Contracts;

public sealed record AddonLogEntry(
    DateTimeOffset ObservedAt,
    int OwnerProcessId,
    string AddonId,
    AddonLogLevel Level,
    string Message);
