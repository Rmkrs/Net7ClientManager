namespace Net7ClientManager.Observations.Models;

public enum ClientTargetVerb : uint
{
    Unknown = 0xffffffff,
    NotApplicable = 0x00000000,
    Scan = 0x00010000,
    Land = 0x00020000,
    Unknown03 = 0x00030000,
    Unknown04 = 0x00040000,
    Unknown05 = 0x00050000,
    Trade = 0x00060000,
    Tractor = 0x00070000,
    Dock = 0x00080000,
    Unknown09 = 0x00090000,
    Gate = 0x000a0000,
    Register = 0x000b0000,
    Jumpstart = 0x000c0000,
    Follow = 0x000d0000,
}
