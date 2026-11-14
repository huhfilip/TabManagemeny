using Microsoft.Win32;
using TabMan.Core;

namespace TabMan.Platform.Windows;

public static class HotkeyNative
{
    public const int Id = 0x544D01;
    public const int Message = 0x0312;

    public static bool TryRegister(IntPtr hwnd, HotkeyChord chord)
    {
        if (hwnd == IntPtr.Zero || !chord.IsValid)
        {
            return false;
        }

        NativeMethods.UnregisterHotKey(hwnd, Id);
        return NativeMethods.RegisterHotKey(hwnd, Id, (uint)chord.Win32Modifiers, (uint)chord.VirtualKey);
    }

    public static void Unregister(IntPtr hwnd)
    {
        if (hwnd != IntPtr.Zero)
        {
            NativeMethods.UnregisterHotKey(hwnd, Id);
        }
    }
}

public readonly record struct MonitorWorkArea(PixelRect WorkArea, uint DpiX, uint DpiY);

public static class MonitorLocator
{
    public static MonitorWorkArea FromCursor()
    {
        if (!NativeMethods.GetCursorPos(out var point))
        {
            return new MonitorWorkArea(new PixelRect(0, 0, 1280, 800), 96, 96);
        }

        var monitor = NativeMethods.MonitorFromPoint(point, NativeMethods.MonitorDefaultToNearest);
        var info = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        if (monitor == IntPtr.Zero || !NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            return new MonitorWorkArea(new PixelRect(0, 0, 1280, 800), 96, 96);
        }

        uint dpiX = 96;
        uint dpiY = 96;
        try
        {
            if (NativeMethods.GetDpiForMonitor(monitor, 0, out var x, out var y) == 0 && x != 0 && y != 0)
            {
                dpiX = x;
                dpiY = y;
            }
        }
        catch (DllNotFoundException)
        {
            dpiX = 96;
            dpiY = 96;
        }

        var work = info.rcWork;
        return new MonitorWorkArea(
            new PixelRect(work.Left, work.Top, Math.Max(0, work.Right - work.Left), Math.Max(0, work.Bottom - work.Top)),
            dpiX,
            dpiY);
    }
}

public static class WindowChromeNative
{
    public static void SetToolWindow(IntPtr hwnd)
    {
        var ex = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlExStyle).ToInt64();
        ex |= NativeMethods.WsExToolWindow;
        ex &= ~NativeMethods.WsExAppWindow;
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GwlExStyle, new IntPtr(ex));
    }

    public static void ApplyRounded(IntPtr hwnd, bool dark)
    {
        var corner = 2;
        _ = NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DwmwaWindowCornerPreference, ref corner, sizeof(int));
        var backdrop = 3;
        _ = NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DwmwaSystemBackdropType, ref backdrop, sizeof(int));
        var immersive = dark ? 1 : 0;
        _ = NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DwmwaUseImmersiveDarkMode, ref immersive, sizeof(int));
    }

    public static void Place(IntPtr hwnd, PixelRect rect)
    {
        NativeMethods.SetWindowPos(
            hwnd,
            new IntPtr(NativeMethods.HWND_TOPMOST),
            rect.X,
            rect.Y,
            Math.Max(1, rect.Width),
            Math.Max(1, rect.Height),
            NativeMethods.SwpShowWindow);
    }
}

public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static void Apply(bool enabled, string? exePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (key is null)
        {
            return;
        }

        if (!enabled)
        {
            key.DeleteValue(StartupCommand.ValueName, throwOnMissingValue: false);
            return;
        }

        var value = StartupCommand.FormatRunValue(exePath);
        if (value is null)
        {
            return;
        }

        key.SetValue(StartupCommand.ValueName, value);
    }
}

public static class SystemTheme
{
    public static bool? PreferLight()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value ? value != 0 : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException or System.Security.SecurityException)
        {
            return null;
        }
    }
}

public sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    public bool IsPrimary { get; }

    public SingleInstance()
    {
        _mutex = new Mutex(true, @"Local\TabMan.SingleInstance", out var created);
        IsPrimary = created;
    }

    public void Dispose()
    {
        if (IsPrimary)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // The mutex was already released.
            }
        }

        _mutex.Dispose();
    }
}

public readonly record struct AltTabAction(bool CycleForward, bool CycleBackward, bool Activate);

/// <summary>
/// Optional Alt+Tab integration. This is an in-process low-level keyboard hook.
/// It does not inject a DLL into other processes and does not edit system files.
/// Off unless the user turns it on. Unhooked on dispose.
/// </summary>
public sealed class AltTabHook : IDisposable
{
    private readonly NativeMethods.HookProc _callback;
    private IntPtr _hook;
    private AltTabPhase _phase = AltTabPhase.Idle;

    public AltTabHook()
    {
        _callback = Callback;
    }

    public bool IsInstalled => _hook != IntPtr.Zero;

    public event Action<AltTabAction>? ActionRequested;

    public bool TryInstall()
    {
        if (_hook != IntPtr.Zero)
        {
            return true;
        }

        _hook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WhKeyboardLl,
            _callback,
            NativeMethods.GetModuleHandle(null),
            0);
        if (_hook == IntPtr.Zero)
        {
            _phase = AltTabPhase.Idle;
            return false;
        }

        return true;
    }

    public void Remove()
    {
        if (_hook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }

        _phase = AltTabPhase.Idle;
    }

    public void Disengage() => _phase = AltTabPhase.Idle;

    public void Dispose()
    {
        Remove();
        ActionRequested = null;
    }

    private IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0 || _hook == IntPtr.Zero)
        {
            return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        try
        {
            var message = wParam.ToInt32();
            var keyDown = message is NativeMethods.WmKeyDown or NativeMethods.WmSysKeyDown;
            var keyUp = message is NativeMethods.WmKeyUp or NativeMethods.WmSysKeyUp;
            if (!keyDown && !keyUp)
            {
                return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
            }

            var data = System.Runtime.InteropServices.Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            var alt = (data.flags & NativeMethods.LlkhfAltDown) != 0 || AltTabPolicy.IsAlt((int)data.vkCode);
            var stroke = new KeyStroke(
                (int)data.vkCode,
                keyDown,
                alt,
                (NativeMethods.GetKeyState(0x11) & 0x8000) != 0,
                (NativeMethods.GetKeyState(0x10) & 0x8000) != 0);
            var decision = AltTabPolicy.Evaluate(_phase, stroke, featureEnabled: true);
            _phase = decision.NextPhase;
            if (decision.CycleForward || decision.CycleBackward || decision.Activate)
            {
                ActionRequested?.Invoke(new AltTabAction(decision.CycleForward, decision.CycleBackward, decision.Activate));
            }

            if (decision.Swallow)
            {
                return new IntPtr(1);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            // Let the original Alt+Tab run if our handler fails.
        }

        return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }
}

public sealed class TrayIcon : IDisposable
{
    private readonly NOTIFYICONDATA _data;
    private IntPtr _hwnd;
    private IntPtr _icon;
    private bool _added;

    public TrayIcon()
    {
        _data = new NOTIFYICONDATA
        {
            cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NOTIFYICONDATA>(),
            uID = 1,
            uFlags = NativeMethods.NifMessage | NativeMethods.NifIcon | NativeMethods.NifTip,
            uCallbackMessage = NativeMethods.WmContextCallback,
            szTip = "TabMan"
        };
    }

    public event Action? OpenSwitcher;
    public event Action? OpenSettings;
    public event Action? ExitRequested;

    public void Attach(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _icon = LoadIcon();
        var data = _data;
        data.hWnd = hwnd;
        data.hIcon = _icon;
        _added = NativeMethods.Shell_NotifyIcon(NativeMethods.NimAdd, ref data);
    }

    public void Handle(int message, IntPtr lParam)
    {
        if (message != NativeMethods.WmContextCallback)
        {
            return;
        }

        var mouse = lParam.ToInt32();
        if (mouse is 0x0202 or 0x0203)
        {
            OpenSwitcher?.Invoke();
        }
        else if (mouse is 0x0205 or 0x007B)
        {
            ShowMenu();
        }
    }

    public void Dispose()
    {
        if (_added && _hwnd != IntPtr.Zero)
        {
            var data = _data;
            data.hWnd = _hwnd;
            NativeMethods.Shell_NotifyIcon(NativeMethods.NimDelete, ref data);
            _added = false;
        }

        if (_icon != IntPtr.Zero)
        {
            NativeMethods.DestroyIcon(_icon);
            _icon = IntPtr.Zero;
        }
    }

    private void ShowMenu()
    {
        var menu = NativeMethods.CreatePopupMenu();
        if (menu == IntPtr.Zero)
        {
            return;
        }

        try
        {
            NativeMethods.AppendMenu(menu, NativeMethods.MfString, 1, "Open switcher");
            NativeMethods.AppendMenu(menu, NativeMethods.MfString, 2, "Settings");
            NativeMethods.AppendMenu(menu, NativeMethods.MfSeparator, 0, null);
            NativeMethods.AppendMenu(menu, NativeMethods.MfString, 3, "Exit");
            if (!NativeMethods.GetCursorPos(out var point))
            {
                return;
            }

            NativeMethods.SetForegroundWindow(_hwnd);
            var command = NativeMethods.TrackPopupMenu(
                menu,
                NativeMethods.TpmReturnCmd | NativeMethods.TpmRightButton,
                point.X,
                point.Y,
                0,
                _hwnd,
                IntPtr.Zero);
            switch (command)
            {
                case 1:
                    OpenSwitcher?.Invoke();
                    break;
                case 2:
                    OpenSettings?.Invoke();
                    break;
                case 3:
                    ExitRequested?.Invoke();
                    break;
            }
        }
        finally
        {
            NativeMethods.DestroyMenu(menu);
        }
    }

    private static IntPtr LoadIcon()
    {
        var file = Path.Combine(AppContext.BaseDirectory, "tabman.ico");
        if (File.Exists(file))
        {
            var loaded = NativeMethods.LoadImage(IntPtr.Zero, file, NativeMethods.ImageIcon, 32, 32, NativeMethods.LrLoadFromFile);
            if (loaded != IntPtr.Zero)
            {
                return loaded;
            }
        }

        var exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe))
        {
            return IntPtr.Zero;
        }

        NativeMethods.ExtractIconEx(exe, 0, out var large, out var small, 1);
        if (small != IntPtr.Zero)
        {
            if (large != IntPtr.Zero)
            {
                NativeMethods.DestroyIcon(large);
            }

            return small;
        }

        return large;
    }
}
