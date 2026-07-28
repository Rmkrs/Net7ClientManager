namespace Net7ClientManager.Observations.Models;

using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Auto)]
public readonly record struct ObjectRegistryNode(
    uint NextAddress,
    uint ClientObjectAddress,
    uint ObjectId,
    uint Flags);
