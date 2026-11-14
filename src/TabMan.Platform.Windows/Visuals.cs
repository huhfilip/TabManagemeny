using System.Runtime.InteropServices;

namespace TabMan.Platform.Windows;

public sealed class IconPixels
{
    public int Width { get; init; }
    public int Height { get; init; }
    public int Stride { get; init; }
    public required byte[] Bgra { get; init; }
}

public sealed class IconCache
{
    private readonly Dictionary<string, IconPixels?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public IconPixels? Get(string processName, string? exePath, long hwnd)
    {
        var key = string.IsNullOrWhiteSpace(processName) ? "pid" : processName;
        lock (_gate)
        {
            if (_cache.TryGetValue(key, out var cached))
            {
                return cached;
            }
        }

        IconPixels? pixels = null;
        if (!string.IsNullOrWhiteSpace(exePath))
        {
            pixels = FromFile(exePath);
        }

        if (pixels is null && hwnd != 0)
        {
            pixels = FromWindow(new IntPtr(hwnd));
        }

        lock (_gate)
        {
            _cache[key] = pixels;
        }

        return pixels;
    }

    private static IconPixels? FromFile(string path)
    {
        var info = new SHFILEINFO();
        var result = NativeMethods.SHGetFileInfo(
            path,
            0,
            ref info,
            (uint)Marshal.SizeOf<SHFILEINFO>(),
            NativeMethods.ShgfiIcon | NativeMethods.ShgfiLargeIcon);
        if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return FromHicon(info.hIcon);
        }
        finally
        {
            NativeMethods.DestroyIcon(info.hIcon);
        }
    }

    private static IconPixels? FromWindow(IntPtr hwnd)
    {
        IntPtr icon = IntPtr.Zero;
        try
        {
            NativeMethods.SendMessageTimeout(hwnd, NativeMethods.WmGetIcon, new IntPtr(NativeMethods.IconBig), IntPtr.Zero, NativeMethods.SmtoAbortIfHung, 80, out icon);
            if (icon == IntPtr.Zero)
            {
                icon = NativeMethods.GetClassLongPtr(hwnd, NativeMethods.GclpHicon);
            }

            return icon == IntPtr.Zero ? null : FromHicon(icon);
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static IconPixels? FromHicon(IntPtr icon)
    {
        if (!NativeMethods.GetIconInfo(icon, out var info))
        {
            return null;
        }

        try
        {
            var bitmap = info.hbmColor != IntPtr.Zero ? info.hbmColor : info.hbmMask;
            var header = new BITMAP();
            if (bitmap == IntPtr.Zero || NativeMethods.GetObject(bitmap, Marshal.SizeOf<BITMAP>(), ref header) == 0)
            {
                return null;
            }

            var width = header.bmWidth;
            var height = info.hbmColor != IntPtr.Zero ? header.bmHeight : Math.Max(1, header.bmHeight / 2);
            if (width <= 0 || height <= 0 || width > 256 || height > 256)
            {
                return null;
            }

            var bmi = new BITMAPINFO
            {
                bmiHeader = new BITMAPINFOHEADER
                {
                    biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
                    biWidth = width,
                    biHeight = -height,
                    biPlanes = 1,
                    biBitCount = 32
                }
            };
            var pixels = new byte[width * height * 4];
            var dc = NativeMethods.GetDC(IntPtr.Zero);
            try
            {
                if (NativeMethods.GetDIBits(dc, bitmap, 0, (uint)height, pixels, ref bmi, 0) == 0)
                {
                    return null;
                }
            }
            finally
            {
                NativeMethods.ReleaseDC(IntPtr.Zero, dc);
            }

            var opaque = false;
            for (var i = 3; i < pixels.Length; i += 4)
            {
                if (pixels[i] != 0)
                {
                    opaque = true;
                    break;
                }
            }

            if (!opaque)
            {
                for (var i = 3; i < pixels.Length; i += 4)
                {
                    pixels[i] = 255;
                }
            }

            return new IconPixels { Width = width, Height = height, Stride = width * 4, Bgra = pixels };
        }
        finally
        {
            if (info.hbmColor != IntPtr.Zero)
            {
                NativeMethods.DeleteObject(info.hbmColor);
            }

            if (info.hbmMask != IntPtr.Zero)
            {
                NativeMethods.DeleteObject(info.hbmMask);
            }
        }
    }
}

public sealed class ThumbnailMap : IDisposable
{
    private readonly Dictionary<long, IntPtr> _thumbs = [];

    public void Update(IntPtr destination, long hwnd, int left, int top, int right, int bottom)
    {
        if (destination == IntPtr.Zero || hwnd == 0 || right <= left || bottom <= top)
        {
            return;
        }

        if (!_thumbs.TryGetValue(hwnd, out var thumb) || thumb == IntPtr.Zero)
        {
            if (NativeMethods.DwmRegisterThumbnail(destination, new IntPtr(hwnd), out thumb) != 0 || thumb == IntPtr.Zero)
            {
                return;
            }

            _thumbs[hwnd] = thumb;
        }

        var props = new DwmThumbnailProperties
        {
            dwFlags = 0x1 | 0x4 | 0x8,
            rcDestination = new RECT { Left = left, Top = top, Right = right, Bottom = bottom },
            opacity = 255,
            fVisible = true,
            fSourceClientAreaOnly = false
        };
        _ = NativeMethods.DwmUpdateThumbnailProperties(thumb, ref props);
    }

    public void RetainOnly(IReadOnlySet<long> hwnds)
    {
        foreach (var key in _thumbs.Keys.ToList())
        {
            if (!hwnds.Contains(key))
            {
                NativeMethods.DwmUnregisterThumbnail(_thumbs[key]);
                _thumbs.Remove(key);
            }
        }
    }

    public void Clear()
    {
        foreach (var thumb in _thumbs.Values)
        {
            NativeMethods.DwmUnregisterThumbnail(thumb);
        }

        _thumbs.Clear();
    }

    public void Dispose() => Clear();
}
