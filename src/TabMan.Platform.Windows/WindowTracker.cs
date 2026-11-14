using TabMan.Core;

namespace TabMan.Platform.Windows;

public sealed class WindowSnapshot
{
    public required IReadOnlyList<WindowInfo> Windows { get; init; }

    /// <summary>In-memory only. Never written to settings.</summary>
    public required IReadOnlyDictionary<int, string> ExePathsByProcessId { get; init; }
}

/// <summary>
/// Keeps a live window list with WinEvent hooks. Enumeration runs when the
/// switcher opens or while it is visible, not on a timer.
/// </summary>
public sealed class WindowTracker : IDisposable
{
    private static readonly uint[] Events =
    [
        0x0003, 0x0016, 0x0017,
        0x8000, 0x8001, 0x8002, 0x8003, 0x800C, 0x8017, 0x8018
    ];

    private readonly int _ownProcessId;
    private readonly List<IntPtr> _hooks = [];
    private readonly Dictionary<long, (int ProcessId, long Ticks)> _activity = [];
    private readonly object _gate = new();
    private NativeMethods.WinEventProc? _callback;
    private bool _aumidUnavailable;

    public WindowTracker(int ownProcessId)
    {
        _ownProcessId = ownProcessId;
    }

    public event EventHandler? Changed;

    public void Start()
    {
        if (_hooks.Count > 0)
        {
            return;
        }

        _callback = OnWinEvent;
        foreach (var evt in Events)
        {
            var hook = NativeMethods.SetWinEventHook(
                evt,
                evt,
                IntPtr.Zero,
                _callback,
                0,
                0,
                NativeMethods.WineventOutOfContext | NativeMethods.WineventSkipOwnProcess);
            if (hook != IntPtr.Zero)
            {
                _hooks.Add(hook);
            }
        }
    }

    public WindowSnapshot Capture()
    {
        var raw = new List<(IntPtr Hwnd, WindowTraits Traits, int ProcessId, string ProcessName, string? Aumid, string Title, string ClassName)>();
        var paths = new Dictionary<int, string>();
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            try
            {
                if (TryRead(hwnd, paths, out var row))
                {
                    raw.Add(row);
                }
            }
            catch (Exception)
            {
                // Skip a window we cannot inspect. Do not record its title.
            }

            return true;
        }, IntPtr.Zero);

        var windows = new List<WindowInfo>(raw.Count);
        var z = 0;
        foreach (var row in raw)
        {
            if (!WindowEligibility.IsCandidate(row.Traits))
            {
                continue;
            }

            long ticks;
            lock (_gate)
            {
                ticks = _activity.TryGetValue(row.Hwnd.ToInt64(), out var known) && known.ProcessId == row.ProcessId
                    ? known.Ticks
                    : 0;
            }

            windows.Add(new WindowInfo
            {
                Hwnd = row.Hwnd.ToInt64(),
                ProcessId = row.ProcessId,
                ProcessName = row.ProcessName,
                AppUserModelId = row.Aumid,
                Title = row.Title,
                ClassName = row.ClassName,
                IsMinimized = row.Traits.IsIconic,
                ZOrder = z,
                LastActiveUtcTicks = ticks
            });
            z++;
        }

        lock (_gate)
        {
            if (_activity.Count > 800)
            {
                var live = windows.Select(window => window.Hwnd).ToHashSet();
                foreach (var key in _activity.Keys.Where(key => !live.Contains(key)).Take(400).ToList())
                {
                    _activity.Remove(key);
                }
            }
        }

        return new WindowSnapshot { Windows = windows, ExePathsByProcessId = paths };
    }

    public void Dispose()
    {
        foreach (var hook in _hooks)
        {
            NativeMethods.UnhookWinEvent(hook);
        }

        _hooks.Clear();
        _callback = null;
        Changed = null;
    }

    private void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (hwnd == IntPtr.Zero || idObject != NativeMethods.ObjidWindow)
        {
            return;
        }

        if (eventType == 0x0003)
        {
            NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid != 0 && pid != (uint)_ownProcessId)
            {
                lock (_gate)
                {
                    _activity[hwnd.ToInt64()] = ((int)pid, DateTime.UtcNow.Ticks);
                }
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private bool TryRead(
        IntPtr hwnd,
        Dictionary<int, string> paths,
        out (IntPtr Hwnd, WindowTraits Traits, int ProcessId, string ProcessName, string? Aumid, string Title, string ClassName) row)
    {
        row = default;
        var title = ReadText(hwnd);
        var className = ReadClass(hwnd);
        NativeMethods.GetWindowThreadProcessId(hwnd, out var pidValue);
        var pid = (int)pidValue;
        var exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlExStyle).ToInt64();
        var cloaked = 0;
        _ = NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DwmwaCloaked, out cloaked, sizeof(int));
        var owner = NativeMethods.GetWindow(hwnd, NativeMethods.GwOwner);
        var traits = new WindowTraits(
            IsVisible: NativeMethods.IsWindowVisible(hwnd),
            IsIconic: NativeMethods.IsIconic(hwnd),
            IsCloaked: cloaked != 0,
            HasOwner: owner != IntPtr.Zero,
            IsToolWindow: (exStyle & NativeMethods.WsExToolWindow) != 0,
            IsAppWindow: (exStyle & NativeMethods.WsExAppWindow) != 0,
            IsNoActivate: (exStyle & NativeMethods.WsExNoActivate) != 0,
            HasTitle: !string.IsNullOrWhiteSpace(title),
            IsOwnProcess: pid == _ownProcessId,
            ClassName: className);

        if (!WindowEligibility.IsCandidate(traits))
        {
            return false;
        }

        var (processName, path) = ReadProcess(pid);
        if (!string.IsNullOrEmpty(path))
        {
            paths[pid] = path;
        }

        row = (hwnd, traits, pid, processName, ReadAumid(hwnd), title.Trim(), className);
        return true;
    }

    private static string ReadText(IntPtr hwnd)
    {
        var buffer = new char[512];
        var length = NativeMethods.GetWindowText(hwnd, buffer, buffer.Length);
        return length <= 0 ? "" : new string(buffer, 0, Math.Min(length, buffer.Length));
    }

    private static string ReadClass(IntPtr hwnd)
    {
        var buffer = new char[256];
        var length = NativeMethods.GetClassName(hwnd, buffer, buffer.Length);
        return length <= 0 ? "" : new string(buffer, 0, Math.Min(length, buffer.Length));
    }

    private static (string Name, string? Path) ReadProcess(int pid)
    {
        if (pid <= 0)
        {
            return ("unknown", null);
        }

        var handle = NativeMethods.OpenProcess(NativeMethods.ProcessQueryLimitedInformation, false, (uint)pid);
        if (handle == IntPtr.Zero)
        {
            return ("pid" + pid, null);
        }

        try
        {
            var buffer = new char[1024];
            var size = buffer.Length;
            if (!NativeMethods.QueryFullProcessImageName(handle, 0, buffer, ref size) || size <= 0)
            {
                return ("pid" + pid, null);
            }

            var path = new string(buffer, 0, size);
            var name = Path.GetFileNameWithoutExtension(path);
            return (string.IsNullOrWhiteSpace(name) ? "pid" + pid : name, path);
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }

    private string? ReadAumid(IntPtr hwnd)
    {
        if (_aumidUnavailable)
        {
            return null;
        }

        try
        {
            var iid = new Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
            var hr = NativeMethods.SHGetPropertyStoreForWindow(hwnd, ref iid, out var punk);
            if (hr != 0 || punk == IntPtr.Zero)
            {
                return null;
            }

            var store = (IPropertyStore)MarshalPropertyStore(punk);
            var key = new PropertyKey
            {
                FormatId = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
                PropertyId = 5
            };
            if (store.GetValue(ref key, out var value) != 0)
            {
                return null;
            }

            try
            {
                if (value.VariantType is 31 or 8 && value.Pointer != IntPtr.Zero)
                {
                    return System.Runtime.InteropServices.Marshal.PtrToStringUni(value.Pointer);
                }

                return null;
            }
            finally
            {
                NativeMethods.PropVariantClear(ref value);
                System.Runtime.InteropServices.Marshal.Release(punk);
            }
        }
        catch (Exception ex) when (ex is InvalidCastException or System.Runtime.InteropServices.COMException or EntryPointNotFoundException)
        {
            _aumidUnavailable = true;
            return null;
        }
    }

    private static object MarshalPropertyStore(IntPtr punk) =>
        System.Runtime.InteropServices.Marshal.GetObjectForIUnknown(punk);
}
