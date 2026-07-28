namespace Net7ClientManager.Addons.Runtime;

using Net7ClientManager.Addons.Contracts;

public sealed class AddonLogEntryEventArgs(
    AddonLogEntry entry)
    : EventArgs
{
    public AddonLogEntry Entry { get; } =
        entry ?? throw new ArgumentNullException(nameof(entry));
}
