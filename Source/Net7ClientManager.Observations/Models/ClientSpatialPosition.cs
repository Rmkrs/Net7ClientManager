namespace Net7ClientManager.Observations.Models;

using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Auto)]
public readonly record struct ClientSpatialPosition(
    float X,
    float Y,
    float Z);
