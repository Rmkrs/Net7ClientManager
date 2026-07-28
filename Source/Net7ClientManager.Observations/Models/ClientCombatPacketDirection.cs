namespace Net7ClientManager.Observations.Models;

public enum ClientCombatPacketDirection
{
    Unknown = 0,
    Outgoing = 1,
    Incoming = 2,
    Self = 3,
    ObservedThirdParty = 4,
}
