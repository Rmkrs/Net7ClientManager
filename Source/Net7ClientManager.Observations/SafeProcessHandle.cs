namespace Net7ClientManager.Observations;

internal sealed class SafeProcessHandle
    : Microsoft.Win32.SafeHandles
        .SafeHandleZeroOrMinusOneIsInvalid
{
    internal SafeProcessHandle()
        : base(ownsHandle: true)
    {
    }

    protected override bool ReleaseHandle()
    {
        return ProcessMemoryNativeMethods.CloseHandle(this.handle);
    }
}
