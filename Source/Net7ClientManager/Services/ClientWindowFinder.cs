namespace Net7ClientManager.Services;

using Net7ClientManager.Win32;

public sealed class ClientWindowFinder
{
    private const string GameWindowClassName = "G";

    public IntPtr? FindGameWindow(int processId)
    {
        var windows = NativeMethods.GetVisibleProcessWindows(processId);

        foreach (var windowHandle in windows)
        {
            var className = NativeMethods.GetWindowClassName(windowHandle);

            if (string.Equals(className, GameWindowClassName, StringComparison.Ordinal))
            {
                return windowHandle;
            }
        }

        return null;
    }
}
