namespace Net7ClientManager.PilotArchive;

public sealed class PilotArchiveChangedEventArgs(
    uint characterId) : EventArgs
{
    public uint CharacterId { get; } = characterId;
}
