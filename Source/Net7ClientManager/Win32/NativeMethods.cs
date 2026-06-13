// ReSharper disable IdentifierTypo
// ReSharper disable InconsistentNaming
namespace Net7ClientManager.Win32;

using System.ComponentModel;
using System.Runtime.InteropServices;

public static partial class NativeMethods
{
    private const int GwlStyle = -16;

    private const nint WsCaption = 0x00C00000;
    private const nint WsThickFrame = 0x00040000;

    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;

    private delegate bool EnumWindowsProc(IntPtr windowHandle, IntPtr parameter);

    public static IReadOnlyList<IntPtr> GetVisibleProcessWindows(int processId)
    {
        var windows = new List<IntPtr>();

        var succeeded = EnumWindows((windowHandle, _) =>
        {
            var threadId = GetWindowThreadProcessId(windowHandle, out var windowProcessId);

            if (threadId != 0
                && windowProcessId == processId
                && IsWindowVisible(windowHandle))
            {
                windows.Add(windowHandle);
            }

            return true;
        }, IntPtr.Zero);

        if (!succeeded)
        {
            var errorCode = Marshal.GetLastPInvokeError();

            if (errorCode != 0)
            {
                throw new Win32Exception(errorCode);
            }
        }

        return windows;
    }

    public static string GetWindowClassName(IntPtr windowHandle)
    {
        Span<char> className = stackalloc char[256];

        var length = GetClassName(windowHandle, className, className.Length);

        if (length == 0)
        {
            var errorCode = Marshal.GetLastPInvokeError();

            if (errorCode != 0)
            {
                throw new Win32Exception(errorCode);
            }

            return string.Empty;
        }

        return new string(className[..length]);
    }

    public static void SetParentWindow(IntPtr childWindowHandle, IntPtr newParentWindowHandle)
    {
        Marshal.SetLastPInvokeError(error: 0);

        var previousParent = SetParent(childWindowHandle, newParentWindowHandle);
        var errorCode = Marshal.GetLastPInvokeError();

        if (previousParent == IntPtr.Zero && errorCode != 0)
        {
            throw new Win32Exception(errorCode);
        }
    }

    public static bool TryRemoveTitleBar(IntPtr windowHandle)
    {
        var style = GetWindowLongPtr(windowHandle, GwlStyle);
        style &= ~(WsCaption | WsThickFrame);

        _ = SetWindowLongPtr(windowHandle, GwlStyle, style);

        return SetWindowPos(
            windowHandle,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
    }

    public static void SetWindowBounds(IntPtr windowHandle, int x, int y, int width, int height)
    {
        var succeeded = SetWindowPos(
            windowHandle,
            IntPtr.Zero,
            x,
            y,
            width,
            height,
            SwpNoZOrder | SwpNoActivate);

        if (!succeeded)
        {
            var errorCode = Marshal.GetLastPInvokeError();
            throw new Win32Exception(errorCode);
        }
    }

    [StructLayout(LayoutKind.Auto)]
    public readonly record struct WindowBounds(int Left, int Top, int Width, int Height);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly int Width => this.Right - this.Left;
        public readonly int Height => this.Bottom - this.Top;
    }

    public static WindowBounds? GetWindowBounds(IntPtr windowHandle)
    {
        var succeeded = GetWindowRect(windowHandle, out var rect);

        if (!succeeded)
        {
            var errorCode = Marshal.GetLastPInvokeError();

            if (errorCode != 0)
            {
                throw new Win32Exception(errorCode);
            }

            return null;
        }

        return new WindowBounds(rect.Left, rect.Top, rect.Width, rect.Height);
    }

    [LibraryImport("user32.dll", EntryPoint = "GetWindowRect", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(IntPtr windowHandle, out Rect rect);

    [LibraryImport("user32.dll", EntryPoint = "EnumWindows", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumWindows(EnumWindowsProc enumProc, IntPtr parameter);

    [LibraryImport("user32.dll", EntryPoint = "IsWindowVisible", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(IntPtr windowHandle);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowThreadProcessId", SetLastError = true)]
    private static partial uint GetWindowThreadProcessId(IntPtr windowHandle, out int processId);

    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial int GetClassName(IntPtr windowHandle, Span<char> className, int maxCount);

    [LibraryImport("user32.dll", EntryPoint = "SetParent", SetLastError = true)]
    private static partial IntPtr SetParent(IntPtr childWindowHandle, IntPtr newParentWindowHandle);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static partial nint GetWindowLongPtr(IntPtr windowHandle, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static partial nint SetWindowLongPtr(IntPtr windowHandle, int index, nint value);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowPos", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(
        IntPtr windowHandle,
        IntPtr insertAfterWindowHandle,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    public static Size? GetPhysicalDisplaySize(string deviceName)
    {
        var displaySettings = new DisplayDeviceMode
        {
            Size = (ushort)Marshal.SizeOf<DisplayDeviceMode>(),
        };

        var succeeded = EnumDisplaySettings(
            deviceName,
            modeNumber: -1,
            ref displaySettings);

        if (!succeeded)
        {
            return null;
        }

        return new Size(
            width: displaySettings.PelsWidth,
            height: displaySettings.PelsHeight);
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct DisplayDeviceMode
    {
        private fixed char deviceName[32];

        public ushort SpecVersion;
        public ushort DriverVersion;
        public ushort Size;
        public ushort DriverExtra;
        public uint Fields;

        public int PositionX;
        public int PositionY;
        public uint DisplayOrientation;
        public uint DisplayFixedOutput;

        public short Color;
        public short Duplex;
        public short YResolution;
        public short TTOption;
        public short Collate;

        private fixed char formName[32];

        public ushort LogPixels;
        public uint BitsPerPel;
        public int PelsWidth;
        public int PelsHeight;
        public uint DisplayFlags;
        public uint DisplayFrequency;
        public uint IcmMethod;
        public uint IcmIntent;
        public uint MediaType;
        public uint DitherType;
        public uint Reserved1;
        public uint Reserved2;
        public uint PanningWidth;
        public uint PanningHeight;
    }

    [LibraryImport("user32.dll", EntryPoint = "EnumDisplaySettingsW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumDisplaySettings(
        string deviceName,
        int modeNumber,
        ref DisplayDeviceMode displayDeviceMode);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindow(IntPtr hWnd);

#pragma warning disable CA1707
    public const int WM_NCLBUTTONDOWN = 0x00A1;
#pragma warning restore CA1707

    public const int HTCAPTION = 0x0002;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ReleaseCapture();

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    private static partial IntPtr SendMessage(
        IntPtr hWnd,
        int msg,
        IntPtr wParam,
        IntPtr lParam);

    public static void SendMoveWindowMessage(IntPtr windowHandle)
    {
        _ = SendMessage(
            windowHandle,
            WM_NCLBUTTONDOWN,
            new IntPtr(HTCAPTION),
            IntPtr.Zero);
    }
}
