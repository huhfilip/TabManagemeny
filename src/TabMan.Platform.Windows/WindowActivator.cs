namespace TabMan.Platform.Windows;

public static class WindowActivator
{
    public static bool IsAlive(long hwnd) =>
        hwnd != 0 && NativeMethods.IsWindow(new IntPtr(hwnd));

    /// <summary>
    /// Restores a minimized window and brings it forward. Uses AttachThreadInput
    /// only so the foreground lock accepts a switch the user just requested.
    /// </summary>
    public static bool TryActivate(long hwndValue)
    {
        var hwnd = new IntPtr(hwndValue);
        if (!NativeMethods.IsWindow(hwnd))
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid != 0)
        {
            NativeMethods.AllowSetForegroundWindow((int)pid);
        }

        if (NativeMethods.IsIconic(hwnd))
        {
            NativeMethods.ShowWindow(hwnd, NativeMethods.SwRestore);
        }
        else
        {
            NativeMethods.ShowWindow(hwnd, NativeMethods.SwShow);
        }

        var foreground = NativeMethods.GetForegroundWindow();
        var current = NativeMethods.GetCurrentThreadId();
        var foregroundThread = foreground == IntPtr.Zero ? 0 : NativeMethods.GetWindowThreadProcessId(foreground, out _);
        var targetThread = NativeMethods.GetWindowThreadProcessId(hwnd, out _);
        var attachedForeground = false;
        var attachedTarget = false;
        try
        {
            if (foregroundThread != 0 && foregroundThread != current)
            {
                attachedForeground = NativeMethods.AttachThreadInput(current, foregroundThread, true);
            }

            if (targetThread != 0 && targetThread != current && targetThread != foregroundThread)
            {
                attachedTarget = NativeMethods.AttachThreadInput(current, targetThread, true);
            }

            NativeMethods.BringWindowToTop(hwnd);
            NativeMethods.SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attachedTarget)
            {
                NativeMethods.AttachThreadInput(current, targetThread, false);
            }

            if (attachedForeground)
            {
                NativeMethods.AttachThreadInput(current, foregroundThread, false);
            }
        }

        return NativeMethods.GetForegroundWindow() == hwnd;
    }
}
