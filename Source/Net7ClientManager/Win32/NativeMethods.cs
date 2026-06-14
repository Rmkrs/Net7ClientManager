// ReSharper disable IdentifierTypo
// ReSharper disable StringLiteralTypo
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

    private const int BmClick = 0x00F5;

    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    public const int WmHotKey = 0x0312;

    private const int VkEscape = 0x1B;

    private const uint GaRoot = 2;
    private const int SwRestore = 9;

    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;

    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;

    private const uint KeyEventKeyUp = 0x0002;

    private const int WM_NCLBUTTONDOWN = 0x00A1;

    private const int HTCAPTION = 0x0002;

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
            ThrowIfLastPInvokeError();
        }

        return windows;
    }

    public static string GetWindowClassName(IntPtr windowHandle)
    {
        Span<char> className = stackalloc char[256];

        var length = GetClassName(windowHandle, className, className.Length);

        if (length == 0)
        {
            ThrowIfLastPInvokeError();
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
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
    }

    public static WindowBounds? GetWindowBounds(IntPtr windowHandle)
    {
        var succeeded = GetWindowRect(windowHandle, out var rect);

        if (!succeeded)
        {
            ThrowIfLastPInvokeError();
            return null;
        }

        return new WindowBounds(rect.Left, rect.Top, rect.Width, rect.Height);
    }

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

    public static void SendMoveWindowMessage(IntPtr windowHandle)
    {
        _ = SendMessage(
            windowHandle,
            WM_NCLBUTTONDOWN,
            new IntPtr(HTCAPTION),
            IntPtr.Zero);
    }

    public static void FocusWindow(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return;
        }

        var rootWindowHandle = GetAncestor(windowHandle, GaRoot);

        if (rootWindowHandle == IntPtr.Zero)
        {
            rootWindowHandle = windowHandle;
        }

        if (IsIconic(rootWindowHandle))
        {
            _ = ShowWindow(rootWindowHandle, SwRestore);
        }

        _ = BringWindowToTop(rootWindowHandle);
        _ = SetForegroundWindow(rootWindowHandle);

        _ = BringWindowToTop(windowHandle);
        _ = SetFocus(windowHandle);
    }

    public static void SendEscape(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return;
        }

        _ = PostMessage(windowHandle, WmKeyDown, new IntPtr(VkEscape), IntPtr.Zero);
        _ = PostMessage(windowHandle, WmKeyUp, new IntPtr(VkEscape), IntPtr.Zero);
    }

    public static bool IsLauncherPlayButtonDisplayed(IntPtr windowHandle)
    {
        return FindLauncherPlayButton(windowHandle) != IntPtr.Zero;
    }

    public static bool ClickLauncherPlayButton(IntPtr windowHandle)
    {
        var buttonHandle = FindLauncherPlayButton(windowHandle);

        if (buttonHandle == IntPtr.Zero)
        {
            return false;
        }

        FocusWindow(windowHandle);
        _ = SendMessage(buttonHandle, BmClick, IntPtr.Zero, IntPtr.Zero);

        return true;
    }

    public static bool IsTosWindowDisplayed(int processId)
    {
        var windowHandle = FindProcessWindowWithTitleContains(processId, "Earth & Beyond");

        if (windowHandle == IntPtr.Zero)
        {
            return false;
        }

        return FindTosAgreeButton(windowHandle) != IntPtr.Zero;
    }

    public static bool AcceptTos(int processId)
    {
        var windowHandle = FindProcessWindowWithTitleContains(processId, "Earth & Beyond");

        if (windowHandle == IntPtr.Zero)
        {
            return false;
        }

        var buttonHandle = FindTosAgreeButton(windowHandle);

        if (buttonHandle == IntPtr.Zero)
        {
            return false;
        }

        FocusWindow(windowHandle);
        _ = SendMessage(buttonHandle, BmClick, IntPtr.Zero, IntPtr.Zero);

        return true;
    }

    public static bool ForegroundLeftClick(IntPtr windowHandle, int clientX, int clientY)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return false;
        }

        var point = new NativePoint
        {
            X = clientX,
            Y = clientY,
        };

        if (!ClientToScreen(windowHandle, ref point))
        {
            return false;
        }

        FocusWindow(windowHandle);
        Thread.Sleep(50);

        if (!SetCursorPos(point.X, point.Y))
        {
            return false;
        }

        mouse_event(MouseEventLeftDown, 0, 0, 0, UIntPtr.Zero);
        mouse_event(MouseEventLeftUp, 0, 0, 0, UIntPtr.Zero);

        return true;
    }

    public static async Task ForegroundHoldKeyAsync(
        IntPtr windowHandle,
        Keys key,
        TimeSpan duration)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return;
        }

        FocusWindow(windowHandle);

        keybd_event((byte)key, 0, 0, UIntPtr.Zero);
        await Task.Delay(duration).ConfigureAwait(true);
        keybd_event((byte)key, 0, KeyEventKeyUp, UIntPtr.Zero);
    }

    public static bool RegisterCaptureHotKey(IntPtr windowHandle, int id)
    {
        return RegisterHotKey(
            windowHandle,
            id,
            ModControl | ModShift,
            (uint)Keys.C);
    }

    public static bool UnregisterCaptureHotKey(IntPtr windowHandle, int id)
    {
        return UnregisterHotKey(windowHandle, id);
    }

    public static bool TryGetCursorPositionRelativeToClient(
        IntPtr clientWindowHandle,
        out Point point)
    {
        point = Point.Empty;

        if (clientWindowHandle == IntPtr.Zero)
        {
            return false;
        }

        if (!GetCursorPos(out var nativePoint))
        {
            return false;
        }

        if (!ScreenToClient(clientWindowHandle, ref nativePoint))
        {
            return false;
        }

        point = new Point(nativePoint.X, nativePoint.Y);
        return true;
    }

    public static bool TryGetClientSize(IntPtr windowHandle, out Size size)
    {
        size = Size.Empty;

        if (windowHandle == IntPtr.Zero)
        {
            return false;
        }

        if (!GetClientRect(windowHandle, out var rect))
        {
            return false;
        }

        size = new Size(rect.Width, rect.Height);
        return true;
    }

    private static IntPtr FindLauncherPlayButton(IntPtr windowHandle)
    {
        return FindWindowEx(
            windowHandle,
            IntPtr.Zero,
            "WindowsForms10.BUTTON.app.0.2004eee",
            "&Play");
    }

    private static IntPtr FindTosAgreeButton(IntPtr windowHandle)
    {
        return FindWindowEx(
            windowHandle,
            IntPtr.Zero,
            "Button",
            "I Agree");
    }

    private static IntPtr FindProcessWindowWithTitleContains(int processId, string titleText)
    {
        var result = IntPtr.Zero;

        _ = EnumWindows((windowHandle, _) =>
        {
            var threadId = GetWindowThreadProcessId(windowHandle, out var windowProcessId);

            if (threadId == 0 || windowProcessId != processId || !IsWindowVisible(windowHandle))
            {
                return true;
            }

            var title = GetWindowTextValue(windowHandle);

            if (!title.Contains(titleText, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            result = windowHandle;
            return false;
        }, IntPtr.Zero);

        return result;
    }

    private static string GetWindowTextValue(IntPtr windowHandle)
    {
        var length = GetWindowTextLength(windowHandle);

        if (length <= 0)
        {
            return string.Empty;
        }

        Span<char> buffer = stackalloc char[length + 1];
        var copied = GetWindowText(windowHandle, buffer, buffer.Length);

        return copied <= 0
            ? string.Empty
            : new string(buffer[..copied]);
    }

    private static void ThrowIfLastPInvokeError()
    {
        var errorCode = Marshal.GetLastPInvokeError();

        if (errorCode != 0)
        {
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

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
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

    [LibraryImport("user32.dll", EntryPoint = "GetWindowRect", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(IntPtr windowHandle, out Rect rect);

    [LibraryImport("user32.dll", EntryPoint = "GetClientRect", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetClientRect(IntPtr windowHandle, out Rect rect);

    [LibraryImport("user32.dll", EntryPoint = "EnumDisplaySettingsW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumDisplaySettings(
        string deviceName,
        int modeNumber,
        ref DisplayDeviceMode displayDeviceMode);

    [LibraryImport("user32.dll", EntryPoint = "IsWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindow(IntPtr windowHandle);

    [LibraryImport("user32.dll", EntryPoint = "ReleaseCapture")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ReleaseCapture();

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    private static partial IntPtr SendMessage(
        IntPtr windowHandle,
        int message,
        IntPtr wParam,
        IntPtr lParam);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessage(
        IntPtr windowHandle,
        int message,
        IntPtr wParam,
        IntPtr lParam);

    [LibraryImport("user32.dll", EntryPoint = "SetForegroundWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(IntPtr windowHandle);

    [LibraryImport("user32.dll", EntryPoint = "SetFocus")]
    private static partial IntPtr SetFocus(IntPtr windowHandle);

    [LibraryImport("user32.dll", EntryPoint = "GetAncestor")]
    private static partial IntPtr GetAncestor(IntPtr windowHandle, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "BringWindowToTop")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool BringWindowToTop(IntPtr windowHandle);

    [LibraryImport("user32.dll", EntryPoint = "IsIconic")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(IntPtr windowHandle);

    [LibraryImport("user32.dll", EntryPoint = "ShowWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(IntPtr windowHandle, int command);

    [LibraryImport("user32.dll", EntryPoint = "FindWindowExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr FindWindowEx(
        IntPtr parentWindowHandle,
        IntPtr childAfterWindowHandle,
        string? className,
        string? windowTitle);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextLengthW", SetLastError = true)]
    private static partial int GetWindowTextLength(IntPtr windowHandle);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial int GetWindowText(IntPtr windowHandle, Span<char> text, int maxCount);

    [LibraryImport("user32.dll", EntryPoint = "ClientToScreen", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ClientToScreen(
        IntPtr windowHandle,
        ref NativePoint point);

    [LibraryImport("user32.dll", EntryPoint = "SetCursorPos", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetCursorPos(int x, int y);

    [LibraryImport("user32.dll", EntryPoint = "mouse_event")]
    private static partial void mouse_event(
        uint flags,
        uint dx,
        uint dy,
        uint data,
        UIntPtr extraInfo);

    [LibraryImport("user32.dll", EntryPoint = "keybd_event")]
    private static partial void keybd_event(
        byte virtualKey,
        byte scanCode,
        uint flags,
        UIntPtr extraInfo);

    [LibraryImport("user32.dll", EntryPoint = "RegisterHotKey", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(
        IntPtr windowHandle,
        int id,
        uint modifiers,
        uint virtualKey);

    [LibraryImport("user32.dll", EntryPoint = "UnregisterHotKey", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(
        IntPtr windowHandle,
        int id);

    [LibraryImport("user32.dll", EntryPoint = "GetCursorPos", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(
        out NativePoint point);

    [LibraryImport("user32.dll", EntryPoint = "ScreenToClient", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ScreenToClient(
        IntPtr windowHandle,
        ref NativePoint point);
}
