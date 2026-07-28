namespace Net7ClientManager.Observations;

using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct LoginTaskObservation(
    uint TaskAddress,
    uint? State,
    uint CharacterViewAddress,
    uint? CharacterViewMode);
