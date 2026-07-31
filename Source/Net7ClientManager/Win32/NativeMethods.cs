// ReSharper disable IdentifierTypo
// ReSharper disable StringLiteralTypo
namespace Net7ClientManager.Win32;

using System.ComponentModel;
using System.Runtime.InteropServices;
using Net7ClientManager.Models;

public static partial class NativeMethods
{
    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;

    private const nint WsCaption = 0x00C00000;
    private const nint WsThickFrame = 0x00040000;
    private const nint WsSysMenu = 0x00080000;
    private const nint WsExTransparent = 0x00000020;

    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;

    private const int BmClick = 0x00F5;
    private const int PbmGetRange = 0x0407;
    private const int PbmGetPosition = 0x0408;

    private const uint SmtoAbortIfHung = 0x0002;
    private const uint SmtoErrorOnExit = 0x0020;
    private const uint LauncherControlMessageTimeoutMilliseconds = 250;

    private const int WmSetRedraw = 0x000B;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    public const int WmHotKey = 0x0312;
    private const int WmThemeChanged = 0x031A;

    private const uint RdwInvalidate = 0x0001;
    private const uint RdwErase = 0x0004;
    private const uint RdwAllChildren = 0x0080;
    private const uint RdwUpdateNow = 0x0100;
    private const uint RdwFrame = 0x0400;

    private const int VkEscape = 0x1B;

    private const uint GaRoot = 2;
    private const int SwRestore = 9;

    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModAlt = 0x0001;
    private const uint ModWin = 0x0008;

    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;
    private const uint MouseEventRightDown = 0x0008;
    private const uint MouseEventRightUp = 0x0010;

    private const uint KeyEventExtendedKey = 0x0001;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint KeyEventScanCode = 0x0008;
    private const int KeyToggleMask = 0x0001;
    private const uint InputKeyboard = 1;
    private const uint MapVirtualKeyToScanCode = 0;

    private const int WM_NCLBUTTONDOWN = 0x00A1;

    private const int HTCAPTION = 0x0002;

    private delegate bool EnumWindowsProc(IntPtr windowHandle, IntPtr parameter);

    public static void SetWindowRedraw(
        IntPtr windowHandle,
        bool enabled)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return;
        }

        _ = SendMessage(
            windowHandle,
            WmSetRedraw,
            enabled ? new IntPtr(1) : IntPtr.Zero,
            IntPtr.Zero);

        if (!enabled)
        {
            return;
        }

        _ = RedrawWindow(
            windowHandle,
            IntPtr.Zero,
            IntPtr.Zero,
            RdwInvalidate |
            RdwErase |
            RdwAllChildren |
            RdwUpdateNow |
            RdwFrame);
    }

    public static bool TryApplyDarkControlTheme(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return false;
        }

        var result = SetWindowTheme(
            windowHandle,
            "DarkMode_Explorer",
            null);

        if (result < 0)
        {
            return false;
        }

        _ = SendMessage(
            windowHandle,
            WmThemeChanged,
            IntPtr.Zero,
            IntPtr.Zero);
        _ = RedrawWindow(
            windowHandle,
            IntPtr.Zero,
            IntPtr.Zero,
            RdwInvalidate |
            RdwErase |
            RdwAllChildren |
            RdwUpdateNow |
            RdwFrame);
        return true;
    }

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

    public static bool TryPrepareHostedGameWindow(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return false;
        }

        var style = GetWindowLongPtr(windowHandle, GwlStyle);

        // The hosted client has no visible system menu. Leaving WS_SYSMENU
        // enabled makes DefWindowProc chime for Alt+character shortcuts.
        var hostedStyle = style & ~(WsCaption | WsThickFrame | WsSysMenu);

        if (hostedStyle != style)
        {
            Marshal.SetLastPInvokeError(0);

            var previousStyle = SetWindowLongPtr(
                windowHandle,
                GwlStyle,
                hostedStyle);

            if (previousStyle == 0 && Marshal.GetLastPInvokeError() != 0)
            {
                return false;
            }
        }

        return SetWindowPos(
            windowHandle,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            SwpNoMove |
            SwpNoSize |
            SwpNoZOrder |
            SwpNoActivate |
            SwpFrameChanged);
    }

    public static bool TrySetWindowClickThrough(
        IntPtr windowHandle,
        bool clickThrough)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return false;
        }

        var style = GetWindowLongPtr(
            windowHandle,
            GwlExStyle);

        var updatedStyle = clickThrough
            ? style | WsExTransparent
            : style & ~WsExTransparent;

        if (updatedStyle == style)
        {
            return true;
        }

        Marshal.SetLastPInvokeError(0);

        var previousStyle = SetWindowLongPtr(
            windowHandle,
            GwlExStyle,
            updatedStyle);

        if (previousStyle == 0 &&
            Marshal.GetLastPInvokeError() != 0)
        {
            return false;
        }

        return SetWindowPos(
            windowHandle,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            SwpNoZOrder |
            SwpNoActivate |
            SwpFrameChanged);
    }

    public static bool TryBringWindowToTopWithoutActivation(
        IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return false;
        }

        return SetWindowPos(
            windowHandle,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            SwpNoMove |
            SwpNoSize |
            SwpNoActivate);
    }

    public static bool TryPlaceWindowBehindWithoutActivation(
        IntPtr windowHandle,
        IntPtr windowInFrontHandle)
    {
        if (windowHandle == IntPtr.Zero ||
            windowInFrontHandle == IntPtr.Zero)
        {
            return false;
        }

        // Use a concrete sibling as the insertion point. Unlike HWND_TOP,
        // this only adjusts the relative order inside the existing owner
        // group and cannot promote the hosted game above other applications.
        return SetWindowPos(
            windowHandle,
            windowInFrontHandle,
            0,
            0,
            0,
            0,
            SwpNoMove |
            SwpNoSize |
            SwpNoActivate);
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

    /// <summary>
    /// Gives a hosted cross-process child window real keyboard focus. A plain
    /// SetFocus call is thread-affine and can silently leave focus on a manager
    /// form after the game window has been reparented into it.
    /// </summary>
    public static bool TryFocusWindowForKeyboardInput(
        IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero ||
            !IsWindow(windowHandle))
        {
            return false;
        }

        var rootWindowHandle = GetAncestor(
            windowHandle,
            GaRoot);

        if (rootWindowHandle == IntPtr.Zero)
        {
            rootWindowHandle = windowHandle;
        }

        var currentThreadId = GetCurrentThreadId();
        var targetThreadId = GetWindowThreadProcessId(
            windowHandle,
            out _);

        var foregroundWindowHandle = GetForegroundWindow();
        var foregroundThreadId = foregroundWindowHandle == IntPtr.Zero
            ? 0
            : GetWindowThreadProcessId(
                foregroundWindowHandle,
                out _);

        var attachedToForeground = false;
        var attachedToTarget = false;

        try
        {
            if (foregroundThreadId != 0 &&
                foregroundThreadId != currentThreadId)
            {
                attachedToForeground = AttachThreadInput(
                    currentThreadId,
                    foregroundThreadId,
                    attach: true);

                if (!attachedToForeground)
                {
                    return false;
                }
            }

            if (targetThreadId != 0 &&
                targetThreadId != currentThreadId &&
                targetThreadId != foregroundThreadId)
            {
                attachedToTarget = AttachThreadInput(
                    currentThreadId,
                    targetThreadId,
                    attach: true);

                if (!attachedToTarget)
                {
                    return false;
                }
            }

            if (IsIconic(rootWindowHandle))
            {
                _ = ShowWindow(
                    rootWindowHandle,
                    SwRestore);
            }

            _ = BringWindowToTop(rootWindowHandle);
            _ = SetForegroundWindow(rootWindowHandle);
            _ = BringWindowToTop(windowHandle);
            _ = SetFocus(windowHandle);

            var focusedWindowHandle = GetFocus();

            return IsChildOrSameWindow(
                windowHandle,
                focusedWindowHandle);
        }
        finally
        {
            if (attachedToTarget)
            {
                _ = AttachThreadInput(
                    currentThreadId,
                    targetThreadId,
                    attach: false);
            }

            if (attachedToForeground)
            {
                _ = AttachThreadInput(
                    currentThreadId,
                    foregroundThreadId,
                    attach: false);
            }
        }
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

    public static bool TryObserveLauncherWindow(
        IntPtr windowHandle,
        out LauncherWindowObservation observation)
    {
        observation = default;

        if (windowHandle == IntPtr.Zero)
        {
            return false;
        }

        var playButtonHandle = FindLauncherPlayButton(windowHandle);
        var progressBarHandle = FindLauncherProgressBar(windowHandle);

        var progressReadable =
            TryReadProgressBar(
                progressBarHandle,
                out var progressMinimum,
                out var progressMaximum,
                out var progressPosition);

        observation = new LauncherWindowObservation(
            PlayButtonHandle: playButtonHandle,
            IsPlayButtonVisible:
                playButtonHandle != IntPtr.Zero &&
                IsWindowVisible(playButtonHandle),
            IsPlayButtonEnabled:
                playButtonHandle != IntPtr.Zero &&
                IsWindowEnabled(playButtonHandle),
            ProgressBarHandle: progressBarHandle,
            IsProgressReadable: progressReadable,
            ProgressMinimum: progressMinimum,
            ProgressMaximum: progressMaximum,
            ProgressPosition: progressPosition);

        return playButtonHandle != IntPtr.Zero ||
               progressBarHandle != IntPtr.Zero;
    }

    public static bool ClickLauncherPlayButton(
        IntPtr windowHandle)
    {
        var buttonHandle = FindLauncherPlayButton(windowHandle);

        if (buttonHandle == IntPtr.Zero ||
            !IsWindowVisible(buttonHandle) ||
            !IsWindowEnabled(buttonHandle))
        {
            return false;
        }

        FocusWindow(windowHandle);

        // Queue the click and observe its effect from the launcher state
        // machine. A synchronous BM_CLICK can block N7CM inside launcher code.
        return PostMessage(
            buttonHandle,
            BmClick,
            IntPtr.Zero,
            IntPtr.Zero);
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

    internal static bool IsCapsLockEnabled()
    {
        return (GetKeyState((int)Keys.CapsLock) & KeyToggleMask) != 0;
    }

    internal static async Task<bool> TrySetCapsLockEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken)
    {
        if (IsCapsLockEnabled() == enabled)
        {
            return true;
        }

        var chord = new GameKeyChord(
            KeyData: Keys.CapsLock,
            SourceText: nameof(Keys.CapsLock),
            DisplayText: "Caps Lock",
            IsExtendedKey: false);

        if (!await TapKeyboardKeyAsync(
                chord,
                cancellationToken)
            .ConfigureAwait(false))
        {
            return false;
        }

        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (IsCapsLockEnabled() == enabled)
            {
                return true;
            }

            await Task.Delay(
                    TimeSpan.FromMilliseconds(20),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return IsCapsLockEnabled() == enabled;
    }

    public static IntPtr GetWindowAtScreenPoint(
        Point point)
    {
        return WindowFromPoint(new NativePoint
        {
            X = point.X,
            Y = point.Y,
        });
    }

    public static bool TryGetCursorScreenPosition(
        out Point point)
    {
        point = Point.Empty;

        if (!GetCursorPos(out var nativePoint))
        {
            return false;
        }

        point = new Point(nativePoint.X, nativePoint.Y);
        return true;
    }

    public static bool TryConvertClientPointToScreen(
        IntPtr clientWindowHandle,
        Point clientPoint,
        out Point screenPoint)
    {
        screenPoint = Point.Empty;

        if (clientWindowHandle == IntPtr.Zero)
        {
            return false;
        }

        var nativePoint = new NativePoint
        {
            X = clientPoint.X,
            Y = clientPoint.Y,
        };

        if (!ClientToScreen(
                clientWindowHandle,
                ref nativePoint))
        {
            return false;
        }

        screenPoint = new Point(
            nativePoint.X,
            nativePoint.Y);

        return true;
    }

    public static bool MoveCursorToScreenPoint(Point point)
    {
        return SetCursorPos(point.X, point.Y);
    }

    public static void LeftButtonDownAtCurrentCursor()
    {
        mouse_event(
            MouseEventLeftDown,
            0,
            0,
            0,
            UIntPtr.Zero);
    }

    public static void LeftButtonUpAtCurrentCursor()
    {
        mouse_event(
            MouseEventLeftUp,
            0,
            0,
            0,
            UIntPtr.Zero);
    }

    public static void LeftClickAtCurrentCursor()
    {
        LeftButtonDownAtCurrentCursor();
        LeftButtonUpAtCurrentCursor();
    }

    /// <summary>
    /// Emits a deliberate foreground-style mouse click. Older game clients
    /// can miss an instantaneous down/up pair, and restoring the cursor
    /// immediately after mouse-up can move it before the client consumes the
    /// queued input. Hold the button across at least one frame and leave the
    /// cursor in place briefly after release.
    /// </summary>
    public static async Task StableLeftClickAtCurrentCursorAsync(
        CancellationToken cancellationToken)
    {
        var buttonDown = false;

        try
        {
            LeftButtonDownAtCurrentCursor();
            buttonDown = true;

            await Task.Delay(
                    TimeSpan.FromMilliseconds(50),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            if (buttonDown)
            {
                LeftButtonUpAtCurrentCursor();
            }
        }

        await Task.Delay(
                TimeSpan.FromMilliseconds(75),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public static void RightButtonDownAtCurrentCursor()
    {
        mouse_event(
            MouseEventRightDown,
            0,
            0,
            0,
            UIntPtr.Zero);
    }

    public static void RightButtonUpAtCurrentCursor()
    {
        mouse_event(
            MouseEventRightUp,
            0,
            0,
            0,
            UIntPtr.Zero);
    }

    public static void RightClickAtCurrentCursor()
    {
        RightButtonDownAtCurrentCursor();
        RightButtonUpAtCurrentCursor();
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

    public static bool RegisterGlobalHotKey(IntPtr windowHandle, int id, Keys keys)
    {
        var modifiers = 0u;

        if ((keys & Keys.Control) == Keys.Control)
        {
            modifiers |= ModControl;
        }

        if ((keys & Keys.Shift) == Keys.Shift)
        {
            modifiers |= ModShift;
        }

        if ((keys & Keys.Alt) == Keys.Alt)
        {
            modifiers |= ModAlt;
        }

        if ((keys & Keys.LWin) == Keys.LWin || (keys & Keys.RWin) == Keys.RWin)
        {
            modifiers |= ModWin;
        }

        var key = keys & Keys.KeyCode;

        if (key == Keys.None)
        {
            return false;
        }

        return RegisterHotKey(
            windowHandle,
            id,
            modifiers,
            (uint)key);
    }

    public static bool UnregisterGlobalHotKey(IntPtr windowHandle, int id)
    {
        return UnregisterHotKey(windowHandle, id);
    }

    public static Task ForegroundTapKeyAsync(IntPtr windowHandle, Keys key)
    {
        return ForegroundHoldKeyAsync(
            windowHandle,
            key,
            TimeSpan.FromMilliseconds(80));
    }

    public static Task<bool> TryForegroundTapKeyAsync(
        IntPtr windowHandle,
        Keys key)
    {
        return TryForegroundTapKeyAsync(
            windowHandle,
            key,
            CancellationToken.None);
    }

    public static Task<bool> TryForegroundTapKeyAsync(
        IntPtr windowHandle,
        Keys key,
        CancellationToken cancellationToken)
    {
        var chord = new GameKeyChord(
            KeyData: key,
            SourceText: key.ToString(),
            DisplayText: key.ToString(),
            IsExtendedKey: false);

        return TryForegroundTapChordAsync(
            windowHandle,
            chord,
            cancellationToken);
    }

    internal static async Task<bool> TryForegroundTapChordAsync(
        IntPtr windowHandle,
        GameKeyChord chord,
        CancellationToken cancellationToken)
    {
        if (chord.KeyCode == Keys.None ||
            !TryFocusWindowForKeyboardInput(windowHandle))
        {
            return false;
        }

        await Task.Delay(
                TimeSpan.FromMilliseconds(50),
                cancellationToken)
            .ConfigureAwait(false);

        if (!TryFocusWindowForKeyboardInput(windowHandle))
        {
            return false;
        }

        var pressedModifiers = new List<HeldKeyboardKey>(3);

        try
        {
            if (!PressRequiredModifier(
                    chord.Modifiers,
                    Keys.Control,
                    Keys.ControlKey,
                    pressedModifiers) ||
                !PressRequiredModifier(
                    chord.Modifiers,
                    Keys.Shift,
                    Keys.ShiftKey,
                    pressedModifiers) ||
                !PressRequiredModifier(
                    chord.Modifiers,
                    Keys.Alt,
                    Keys.Menu,
                    pressedModifiers))
            {
                return false;
            }

            return await TapKeyboardKeyAsync(
                    chord,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            ReleaseHeldKeys(pressedModifiers);
        }
    }

    internal static async Task<bool> TryForegroundStableLeftClickWithHeldChordAsync(
        IntPtr windowHandle,
        Point screenPoint,
        GameKeyChord? heldChord,
        Func<CancellationToken, Task<bool>>? waitAfterHeldChordAsync,
        CancellationToken cancellationToken)
    {
        if (windowHandle == IntPtr.Zero ||
            !TryFocusWindowForKeyboardInput(windowHandle))
        {
            return false;
        }

        await Task.Delay(
                TimeSpan.FromMilliseconds(50),
                cancellationToken)
            .ConfigureAwait(false);

        if (!TryFocusWindowForKeyboardInput(windowHandle))
        {
            return false;
        }

        var heldKeys = new List<HeldKeyboardKey>(4);

        try
        {
            if (heldChord != null &&
                !PressChordDown(
                    heldChord,
                    heldKeys))
            {
                return false;
            }

            if (waitAfterHeldChordAsync != null &&
                !await waitAfterHeldChordAsync(cancellationToken)
                    .ConfigureAwait(false))
            {
                return false;
            }

            if (!MoveCursorToScreenPoint(screenPoint))
            {
                return false;
            }

            await StableLeftClickAtCurrentCursorAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            ReleaseHeldKeys(heldKeys);
        }

        return true;
    }

    internal static async Task<bool> TryForegroundTapChordWithHeldChordAsync(
        IntPtr windowHandle,
        GameKeyChord tapChord,
        GameKeyChord? heldChord,
        Func<CancellationToken, Task<bool>>? waitAfterHeldChordAsync,
        CancellationToken cancellationToken)
    {
        if (tapChord.KeyCode == Keys.None ||
            !TryFocusWindowForKeyboardInput(windowHandle))
        {
            return false;
        }

        await Task.Delay(
                TimeSpan.FromMilliseconds(50),
                cancellationToken)
            .ConfigureAwait(false);

        if (!TryFocusWindowForKeyboardInput(windowHandle))
        {
            return false;
        }

        var heldKeys = new List<HeldKeyboardKey>(4);

        try
        {
            if (heldChord != null &&
                !PressChordDown(
                    heldChord,
                    heldKeys))
            {
                return false;
            }

            if (waitAfterHeldChordAsync != null &&
                !await waitAfterHeldChordAsync(cancellationToken)
                    .ConfigureAwait(false))
            {
                return false;
            }

            if (!await TapChordAsync(
                    tapChord,
                    cancellationToken)
                .ConfigureAwait(false))
            {
                return false;
            }
        }
        finally
        {
            ReleaseHeldKeys(heldKeys);
        }

        return true;
    }

    private static async Task<bool> TapChordAsync(
        GameKeyChord chord,
        CancellationToken cancellationToken)
    {
        var pressedModifiers = new List<HeldKeyboardKey>(3);

        try
        {
            if (!PressRequiredModifier(
                    chord.Modifiers,
                    Keys.Control,
                    Keys.ControlKey,
                    pressedModifiers) ||
                !PressRequiredModifier(
                    chord.Modifiers,
                    Keys.Shift,
                    Keys.ShiftKey,
                    pressedModifiers) ||
                !PressRequiredModifier(
                    chord.Modifiers,
                    Keys.Alt,
                    Keys.Menu,
                    pressedModifiers))
            {
                return false;
            }

            return await TapKeyboardKeyAsync(
                    chord,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            ReleaseHeldKeys(pressedModifiers);
        }
    }

    private static bool PressChordDown(
        GameKeyChord chord,
        ICollection<HeldKeyboardKey> pressedKeys)
    {
        return PressHeldModifier(
                   chord.Modifiers,
                   Keys.Control,
                   Keys.ControlKey,
                   pressedKeys) &&
               PressHeldModifier(
                   chord.Modifiers,
                   Keys.Shift,
                   Keys.ShiftKey,
                   pressedKeys) &&
               PressHeldModifier(
                   chord.Modifiers,
                   Keys.Alt,
                   Keys.Menu,
                   pressedKeys) &&
               PressHeldKey(
                   chord.KeyCode,
                   chord.IsExtendedKey,
                   pressedKeys);
    }

    private static bool PressHeldModifier(
        Keys requiredModifiers,
        Keys modifierFlag,
        Keys modifierKey,
        ICollection<HeldKeyboardKey> pressedKeys)
    {
        return (requiredModifiers & modifierFlag) != modifierFlag ||
               PressHeldKey(
                   modifierKey,
                   isExtendedKey: false,
                   pressedKeys);
    }

    private static bool PressHeldKey(
        Keys key,
        bool isExtendedKey,
        ICollection<HeldKeyboardKey> pressedKeys)
    {
        var keyCode = NormalizePhysicalKey(key & Keys.KeyCode);

        if (keyCode == Keys.None ||
            IsKeyDown(keyCode))
        {
            return true;
        }

        if (!TryCreateKeyboardInput(
                keyCode,
                isExtendedKey,
                keyUp: false,
                out var pressedKey))
        {
            return false;
        }

        if (!SendKeyboardInput(pressedKey))
        {
            return false;
        }

        pressedKeys.Add(pressedKey);
        return true;
    }

    private static void ReleaseHeldKeys(
        IReadOnlyList<HeldKeyboardKey> heldKeys)
    {
        for (var index = heldKeys.Count - 1;
             index >= 0;
             index--)
        {
            var key = heldKeys[index];
            _ = SendKeyboardInput(
                key with
                {
                    Flags = key.Flags | KeyEventKeyUp,
                });
        }
    }

    private static async Task<bool> TapKeyboardKeyAsync(
        GameKeyChord chord,
        CancellationToken cancellationToken)
    {
        var keyCode = NormalizePhysicalKey(chord.KeyCode);

        if (!TryCreateKeyboardInput(
                keyCode,
                chord.IsExtendedKey,
                keyUp: false,
                out var keyDown) ||
            !SendKeyboardInput(keyDown))
        {
            return false;
        }

        try
        {
            await Task.Delay(
                    TimeSpan.FromMilliseconds(80),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _ = SendKeyboardInput(
                keyDown with
                {
                    Flags = keyDown.Flags | KeyEventKeyUp,
                });
        }

        return true;
    }

    private static bool PressRequiredModifier(
        Keys requiredModifiers,
        Keys modifierFlag,
        Keys modifierKey,
        ICollection<HeldKeyboardKey> pressedModifiers)
    {
        if ((requiredModifiers & modifierFlag) != modifierFlag)
        {
            return true;
        }

        return PressHeldKey(
            modifierKey,
            isExtendedKey: false,
            pressedModifiers);
    }

    private static bool TryCreateKeyboardInput(
        Keys key,
        bool isExtendedKey,
        bool keyUp,
        out HeldKeyboardKey keyboardKey)
    {
        keyboardKey = default;
        var keyCode = NormalizePhysicalKey(key & Keys.KeyCode);

        if (keyCode == Keys.None)
        {
            return false;
        }

        var scanCode = MapVirtualKey(
            (uint)keyCode,
            MapVirtualKeyToScanCode);

        if (scanCode == 0 ||
            scanCode > ushort.MaxValue)
        {
            return false;
        }

        var flags = KeyEventScanCode;

        if (isExtendedKey || IsExtendedPhysicalKey(keyCode))
        {
            flags |= KeyEventExtendedKey;
        }

        if (keyUp)
        {
            flags |= KeyEventKeyUp;
        }

        keyboardKey = new HeldKeyboardKey(
            (ushort)scanCode,
            flags);
        return true;
    }

    private static bool SendKeyboardInput(HeldKeyboardKey key)
    {
        var inputs = new[]
        {
            new NativeInput
            {
                Type = InputKeyboard,
                Data = new NativeInputUnion
                {
                    Keyboard = new NativeKeyboardInput
                    {
                        VirtualKey = 0,
                        ScanCode = key.ScanCode,
                        Flags = key.Flags,
                        Time = 0,
                        ExtraInfo = UIntPtr.Zero,
                    },
                },
            },
        };

        return SendInput(
                   (uint)inputs.Length,
                   inputs,
                   Marshal.SizeOf<NativeInput>()) == (uint)inputs.Length;
    }

    private static Keys NormalizePhysicalKey(Keys key)
    {
        return key switch
        {
            Keys.ControlKey => Keys.LControlKey,
            Keys.ShiftKey => Keys.LShiftKey,
            Keys.Menu => Keys.LMenu,
            _ => key,
        };
    }

    private static bool IsExtendedPhysicalKey(Keys key)
    {
        return key is
            Keys.RControlKey or
            Keys.RMenu or
            Keys.Insert or
            Keys.Delete or
            Keys.Home or
            Keys.End or
            Keys.PageUp or
            Keys.PageDown or
            Keys.Left or
            Keys.Right or
            Keys.Up or
            Keys.Down or
            Keys.NumLock or
            Keys.Divide;
    }

    private readonly record struct HeldKeyboardKey(
        ushort ScanCode,
        uint Flags);

    public static IntPtr GetForegroundWindowHandle()
    {
        return GetForegroundWindow();
    }

    public static bool IsChildOrSameWindow(IntPtr possibleParentWindowHandle, IntPtr possibleChildWindowHandle)
    {
        if (possibleParentWindowHandle == IntPtr.Zero || possibleChildWindowHandle == IntPtr.Zero)
        {
            return false;
        }

        if (possibleParentWindowHandle == possibleChildWindowHandle)
        {
            return true;
        }

        var currentWindowHandle = possibleChildWindowHandle;

        while (currentWindowHandle != IntPtr.Zero)
        {
            currentWindowHandle = GetParent(currentWindowHandle);

            if (currentWindowHandle == possibleParentWindowHandle)
            {
                return true;
            }
        }

        return false;
    }

    public static bool MoveCursorToClientPoint(
        IntPtr clientWindowHandle,
        int clientX,
        int clientY)
    {
        if (clientWindowHandle == IntPtr.Zero)
        {
            return false;
        }

        var point = new NativePoint
        {
            X = clientX,
            Y = clientY,
        };

        if (!ClientToScreen(clientWindowHandle, ref point))
        {
            return false;
        }

        return SetCursorPos(point.X, point.Y);
    }

    public static bool IsKeyDown(Keys key)
    {
        return (GetAsyncKeyState((int)(key & Keys.KeyCode)) & 0x8000) != 0;
    }

    private static IntPtr FindLauncherPlayButton(
        IntPtr windowHandle)
    {
        var buttonHandle = FindWindowEx(
            windowHandle,
            IntPtr.Zero,
            className: null,
            windowTitle: "&Play");

        return buttonHandle != IntPtr.Zero
            ? buttonHandle
            : FindWindowEx(
                windowHandle,
                IntPtr.Zero,
                className: null,
                windowTitle: "Play");
    }

    private static IntPtr FindLauncherProgressBar(
        IntPtr windowHandle)
    {
        // Spy++ confirms LaunchNet7 v2.2.0 exposes a direct WinForms wrapper
        // around the standard msctls_progress32 control. Match the stable
        // native class fragment rather than the runtime-specific suffix.
        var childHandle = IntPtr.Zero;

        while (true)
        {
            childHandle = FindWindowEx(
                windowHandle,
                childHandle,
                className: null,
                windowTitle: null);

            if (childHandle == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            string className;

            try
            {
                className = GetWindowClassName(childHandle);
            }
            catch (Win32Exception)
            {
                continue;
            }

            if (className.Contains(
                    "msctls_progress32",
                    StringComparison.OrdinalIgnoreCase))
            {
                return childHandle;
            }
        }
    }

    private static bool TryReadProgressBar(
        IntPtr progressBarHandle,
        out int minimum,
        out int maximum,
        out int position)
    {
        minimum = 0;
        maximum = 0;
        position = 0;

        if (progressBarHandle == IntPtr.Zero ||
            !TrySendMessageWithTimeout(
                progressBarHandle,
                PbmGetRange,
                new IntPtr(1),
                IntPtr.Zero,
                out var minimumResult) ||
            !TrySendMessageWithTimeout(
                progressBarHandle,
                PbmGetRange,
                IntPtr.Zero,
                IntPtr.Zero,
                out var maximumResult) ||
            !TrySendMessageWithTimeout(
                progressBarHandle,
                PbmGetPosition,
                IntPtr.Zero,
                IntPtr.Zero,
                out var positionResult))
        {
            return false;
        }

        minimum = minimumResult.ToInt32();
        maximum = maximumResult.ToInt32();
        position = positionResult.ToInt32();
        return true;
    }

    private static bool TrySendMessageWithTimeout(
        IntPtr windowHandle,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        out IntPtr result)
    {
        result = IntPtr.Zero;

        return SendMessageTimeout(
                   windowHandle,
                   message,
                   wParam,
                   lParam,
                   SmtoAbortIfHung | SmtoErrorOnExit,
                   LauncherControlMessageTimeoutMilliseconds,
                   out result) != IntPtr.Zero;
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

    public readonly record struct LauncherWindowObservation(
        IntPtr PlayButtonHandle,
        bool IsPlayButtonVisible,
        bool IsPlayButtonEnabled,
        IntPtr ProgressBarHandle,
        bool IsProgressReadable,
        int ProgressMinimum,
        int ProgressMaximum,
        int ProgressPosition)
    {
        public bool HasPlayButton =>
            this.PlayButtonHandle != IntPtr.Zero;

        public bool HasProgressBar =>
            this.ProgressBarHandle != IntPtr.Zero;

        public bool IsProgressComplete =>
            this.IsProgressReadable &&
            this.ProgressMaximum > this.ProgressMinimum &&
            this.ProgressPosition >= this.ProgressMaximum;

        public bool IsReady =>
            this.HasPlayButton &&
            this.IsPlayButtonVisible &&
            this.IsPlayButtonEnabled &&
            this.HasProgressBar &&
            this.IsProgressComplete;
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

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct NativeInput
    {
        [FieldOffset(0)]
        public uint Type;

        [FieldOffset(8)]
        public NativeInputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    private struct NativeInputUnion
    {
        [FieldOffset(0)]
        public NativeKeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeKeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
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

    [LibraryImport(
        "uxtheme.dll",
        EntryPoint = "SetWindowTheme",
        StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SetWindowTheme(
        IntPtr windowHandle,
        string? subApplicationName,
        string? subIdentifierList);

    [LibraryImport("user32.dll", EntryPoint = "EnumWindows", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumWindows(EnumWindowsProc enumProc, IntPtr parameter);

    [LibraryImport("user32.dll", EntryPoint = "IsWindowVisible", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(IntPtr windowHandle);

    [LibraryImport("user32.dll", EntryPoint = "IsWindowEnabled")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowEnabled(IntPtr windowHandle);

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

    [LibraryImport("user32.dll", EntryPoint = "SendMessageTimeoutW", SetLastError = true)]
    private static partial IntPtr SendMessageTimeout(
        IntPtr windowHandle,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        uint flags,
        uint timeoutMilliseconds,
        out IntPtr result);

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

    [LibraryImport("user32.dll", EntryPoint = "RedrawWindow", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RedrawWindow(
        IntPtr windowHandle,
        IntPtr updateRectangle,
        IntPtr updateRegion,
        uint flags);

    [LibraryImport("user32.dll", EntryPoint = "SetFocus")]
    private static partial IntPtr SetFocus(IntPtr windowHandle);

    [LibraryImport("user32.dll", EntryPoint = "GetFocus")]
    private static partial IntPtr GetFocus();

    [LibraryImport("user32.dll", EntryPoint = "GetKeyState")]
    private static partial short GetKeyState(int virtualKey);

    [LibraryImport("user32.dll", EntryPoint = "AttachThreadInput", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachThreadInput(
        uint attachThreadId,
        uint attachToThreadId,
        [MarshalAs(UnmanagedType.Bool)] bool attach);

    [LibraryImport("kernel32.dll", EntryPoint = "GetCurrentThreadId")]
    private static partial uint GetCurrentThreadId();

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

    [LibraryImport("user32.dll", EntryPoint = "MapVirtualKeyW")]
    private static partial uint MapVirtualKey(
        uint code,
        uint mapType);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(
        uint inputCount,
        [In] NativeInput[] inputs,
        int inputSize);

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

    [LibraryImport("user32.dll", EntryPoint = "WindowFromPoint")]
    private static partial IntPtr WindowFromPoint(
        NativePoint point);

    [LibraryImport("user32.dll", EntryPoint = "GetCursorPos", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(
        out NativePoint point);

    [LibraryImport("user32.dll", EntryPoint = "ScreenToClient", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ScreenToClient(
        IntPtr windowHandle,
        ref NativePoint point);

    [LibraryImport("user32.dll", EntryPoint = "GetForegroundWindow")]
    private static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll", EntryPoint = "GetParent")]
    private static partial IntPtr GetParent(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}


