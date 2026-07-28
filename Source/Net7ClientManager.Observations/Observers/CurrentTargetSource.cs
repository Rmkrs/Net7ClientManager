namespace Net7ClientManager.Observations.Observers;

using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Auto)]
internal readonly record struct CurrentTargetSource(
    uint LocalClientObjectAddress,
    uint LocalShipAuxDataAddress,
    uint TargetObjectIdAddress,
    uint TargetObjectId);
