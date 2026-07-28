namespace Net7ClientManager.Services;

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

internal sealed class CommandPaletteKeyboardHook : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const uint LlkhfInjected = 0x00000010;

    private readonly Func<bool> tryBeginHotKey;
    private readonly Action hotKeyReleased;
    private readonly LowLevelKeyboardProc hookProc;

    private IntPtr hookHandle;
    private Keys hotKey;
    private bool observedMainKeyDown;
    private bool suppressedMainKeyDown;
    private bool disposed;

    public CommandPaletteKeyboardHook(
        Keys hotKey,
        Func<bool> tryBeginHotKey,
        Action hotKeyReleased)
    {
        this.hotKey = Normalize(hotKey);
        this.tryBeginHotKey = tryBeginHotKey;
        this.hotKeyReleased = hotKeyReleased;
        this.hookProc = this.HookCallback;
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);

        if (this.hookHandle != IntPtr.Zero)
        {
            return;
        }

        using var process = Process.GetCurrentProcess();
        var moduleHandle = GetModuleHandle(
            process.MainModule?.ModuleName);

        this.hookHandle = SetWindowsHookEx(
            WhKeyboardLl,
            this.hookProc,
            moduleHandle,
            threadId: 0);

        if (this.hookHandle == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    public void Stop()
    {
        if (this.hookHandle != IntPtr.Zero)
        {
            _ = UnhookWindowsHookEx(this.hookHandle);
            this.hookHandle = IntPtr.Zero;
        }

        this.observedMainKeyDown = false;
        this.suppressedMainKeyDown = false;
    }

    public void UpdateHotKey(Keys hotKey)
    {
        this.hotKey = Normalize(hotKey);
        this.observedMainKeyDown = false;
        this.suppressedMainKeyDown = false;
    }

    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;
        this.Stop();
    }

    private IntPtr HookCallback(
        int code,
        IntPtr message,
        IntPtr parameter)
    {
        if (code < 0 || parameter == IntPtr.Zero)
        {
            return this.PassThrough(code, message, parameter);
        }

        var data = Marshal.PtrToStructure<LowLevelKeyboardInput>(parameter);

        if ((data.Flags & LlkhfInjected) != 0)
        {
            return this.PassThrough(code, message, parameter);
        }

        var messageId = message.ToInt32();
        var isKeyDown = messageId is WmKeyDown or WmSysKeyDown;
        var isKeyUp = messageId is WmKeyUp or WmSysKeyUp;
        var configuredKey = this.hotKey & Keys.KeyCode;
        var eventKey = (Keys)data.VirtualKeyCode & Keys.KeyCode;

        if (eventKey != configuredKey)
        {
            return this.PassThrough(code, message, parameter);
        }

        if (isKeyUp)
        {
            var suppressKeyUp = this.suppressedMainKeyDown;
            this.observedMainKeyDown = false;
            this.suppressedMainKeyDown = false;

            if (!suppressKeyUp)
            {
                return this.PassThrough(code, message, parameter);
            }

            try
            {
                this.hotKeyReleased();
            }
            catch (Exception exception)
            {
                Debug.WriteLine(
                    $"Command Palette hotkey release callback failed: {exception}");
            }

            return new IntPtr(1);
        }

        if (!isKeyDown)
        {
            return this.PassThrough(code, message, parameter);
        }

        if (this.observedMainKeyDown)
        {
            return this.suppressedMainKeyDown
                ? new IntPtr(1)
                : this.PassThrough(code, message, parameter);
        }

        this.observedMainKeyDown = true;

        if (!this.IsConfiguredChordDown())
        {
            return this.PassThrough(code, message, parameter);
        }

        try
        {
            this.suppressedMainKeyDown = this.tryBeginHotKey();
        }
        catch (Exception exception)
        {
            Debug.WriteLine(
                $"Command Palette hotkey callback failed: {exception}");
            this.suppressedMainKeyDown = false;
        }

        return this.suppressedMainKeyDown
            ? new IntPtr(1)
            : this.PassThrough(code, message, parameter);
    }

    private IntPtr PassThrough(
        int code,
        IntPtr message,
        IntPtr parameter)
    {
        return CallNextHookEx(
            this.hookHandle,
            code,
            message,
            parameter);
    }

    private bool IsConfiguredChordDown()
    {
        var modifiers = this.hotKey & Keys.Modifiers;

        return IsModifierStateExact(
                   Keys.Control,
                   (modifiers & Keys.Control) == Keys.Control) &&
               IsModifierStateExact(
                   Keys.Shift,
                   (modifiers & Keys.Shift) == Keys.Shift) &&
               IsModifierStateExact(
                   Keys.Alt,
                   (modifiers & Keys.Alt) == Keys.Alt) &&
               !IsKeyDown(Keys.LWin) &&
               !IsKeyDown(Keys.RWin);
    }

    private static bool IsModifierStateExact(Keys modifier, bool required)
    {
        var down = modifier switch
        {
            Keys.Control =>
                IsKeyDown(Keys.ControlKey) ||
                IsKeyDown(Keys.LControlKey) ||
                IsKeyDown(Keys.RControlKey),
            Keys.Shift =>
                IsKeyDown(Keys.ShiftKey) ||
                IsKeyDown(Keys.LShiftKey) ||
                IsKeyDown(Keys.RShiftKey),
            Keys.Alt =>
                IsKeyDown(Keys.Menu) ||
                IsKeyDown(Keys.LMenu) ||
                IsKeyDown(Keys.RMenu),
            _ => false,
        };

        return down == required;
    }

    private static bool IsKeyDown(Keys key)
    {
        return (GetAsyncKeyState((int)key) & 0x8000) != 0;
    }

    private static Keys Normalize(Keys keys)
    {
        return (keys & Keys.KeyCode) |
               (keys & (Keys.Control | Keys.Shift | Keys.Alt));
    }

    private delegate IntPtr LowLevelKeyboardProc(
        int code,
        IntPtr message,
        IntPtr parameter);

    [StructLayout(LayoutKind.Sequential)]
    private struct LowLevelKeyboardInput
    {
        public uint VirtualKeyCode;

        public uint ScanCode;

        public uint Flags;

        public uint Time;

        public UIntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(
        int hookId,
        LowLevelKeyboardProc callback,
        IntPtr moduleHandle,
        uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(
        IntPtr hookHandle);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(
        IntPtr hookHandle,
        int code,
        IntPtr message,
        IntPtr parameter);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(
        string? moduleName);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(
        int virtualKey);
}
