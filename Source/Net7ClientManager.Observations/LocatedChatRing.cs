namespace Net7ClientManager.Observations;

using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct LocatedChatRing(
    uint ChatPanel,
    uint Ring,
    uint Capacity,
    uint WriteCounter,
    uint Entries);
