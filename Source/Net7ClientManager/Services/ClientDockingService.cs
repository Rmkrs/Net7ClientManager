namespace Net7ClientManager.Services;

using System.ComponentModel;
using Net7ClientManager.Win32;

public sealed class ClientDockingService
{
    public bool Dock(IntPtr gameWindowHandle, IntPtr hostWindowHandle, Size hostClientSize)
    {
        if (!this.IsValidWindow(gameWindowHandle) || !this.IsValidWindow(hostWindowHandle))
        {
            return false;
        }

        _ = NativeMethods.TryRemoveTitleBar(gameWindowHandle);

        if (!this.IsValidWindow(gameWindowHandle) || !this.IsValidWindow(hostWindowHandle))
        {
            return false;
        }

        NativeMethods.SetParentWindow(gameWindowHandle, hostWindowHandle);

        return this.TryResizeDockedWindow(gameWindowHandle, hostClientSize);
    }

    public bool TryResizeDockedWindow(IntPtr gameWindowHandle, Size hostClientSize)
    {
        if (!this.IsValidWindow(gameWindowHandle))
        {
            return false;
        }

        try
        {
            NativeMethods.SetWindowBounds(
                gameWindowHandle,
                x: 0,
                y: 0,
                width: hostClientSize.Width,
                height: hostClientSize.Height);

            return true;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    private bool IsValidWindow(IntPtr windowHandle)
    {
        return windowHandle != IntPtr.Zero
               && NativeMethods.IsWindow(windowHandle);
    }
}
