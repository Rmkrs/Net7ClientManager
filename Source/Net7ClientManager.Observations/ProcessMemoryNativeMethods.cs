using System.Runtime.InteropServices;

namespace Net7ClientManager.Observations;

internal static class ProcessMemoryNativeMethods
{
    [DllImport(
        "kernel32.dll",
        SetLastError = true)]
    public static extern SafeProcessHandle OpenProcess(
        uint desiredAccess,
        bool inheritHandle,
        int processId);

    [DllImport(
        "kernel32.dll",
        SetLastError = true)]
    public static extern bool ReadProcessMemory(
        SafeProcessHandle processHandle,
        IntPtr baseAddress,
        byte[] buffer,
        UIntPtr size,
        out UIntPtr bytesRead);

    [DllImport(
        "kernel32.dll",
        SetLastError = true)]
    public static extern int VirtualQueryEx(
        SafeProcessHandle processHandle,
        IntPtr address,
        out MemoryBasicInformation buffer,
        uint length);

    [StructLayout(LayoutKind.Sequential)]
    public struct MemoryBasicInformation
    {
        public IntPtr BaseAddress;
        public IntPtr AllocationBase;
        public uint AllocationProtect;
        public UIntPtr RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
    }

    [DllImport(
        "kernel32.dll",
        SetLastError = true)]
    public static extern bool CloseHandle(
        IntPtr handle);
}
