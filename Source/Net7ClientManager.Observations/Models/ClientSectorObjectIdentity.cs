namespace Net7ClientManager.Observations.Models;

using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Auto)]
public readonly record struct ClientSectorObjectIdentity(
    uint ActiveSectorNumber,
    uint ObjectId);
