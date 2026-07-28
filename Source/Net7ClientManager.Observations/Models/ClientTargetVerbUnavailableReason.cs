namespace Net7ClientManager.Observations.Models;

public enum ClientTargetVerbUnavailableReason : ushort
{
    None = 0x0000,
    PlayerAlreadyInGroup = 0x0001,
    TooFar = 0x0002,
}
