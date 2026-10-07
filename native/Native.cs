using System.Drawing;
using System.Runtime.InteropServices;

static class Native
{
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }
    [StructLayout(LayoutKind.Sequential)] public struct SIZE { public int cx, cy; public SIZE(int x, int y) { cx = x; cy = y; } }
    [StructLayout(LayoutKind.Sequential)] public struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
        IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
    [DllImport("kernel32.dll")] public static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] public static extern bool SetProcessWorkingSetSize(IntPtr proc, IntPtr min, IntPtr max);

    // Show a bitmap with per-pixel alpha as the content of a layered window, at screen position (x, y)
    public static void PushBitmap(IntPtr hwnd, Bitmap bmp, int x, int y, byte alpha = 255)
    {
        IntPtr hBmp = bmp.GetHbitmap(Color.FromArgb(0));
        IntPtr screenDc = GetDC(IntPtr.Zero), memDc = CreateCompatibleDC(screenDc);
        IntPtr old = SelectObject(memDc, hBmp);
        var sz = new SIZE(bmp.Width, bmp.Height);
        var src = new POINT(0, 0);
        var dst = new POINT(x, y);
        var bf = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = alpha, AlphaFormat = 1 };   // AC_SRC_OVER, AC_SRC_ALPHA
        UpdateLayeredWindow(hwnd, screenDc, ref dst, ref sz, memDc, ref src, 0, ref bf, 2);   // ULW_ALPHA
        SelectObject(memDc, old);
        DeleteObject(hBmp);
        DeleteDC(memDc);
        ReleaseDC(IntPtr.Zero, screenDc);
    }

    // Give back memory we don't need (called now and then: the pet is idle most of the time)
    public static void Trim()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        SetProcessWorkingSetSize(GetCurrentProcess(), (IntPtr)(-1), (IntPtr)(-1));
    }
}

// Work areas of the monitors, cached: Screen.FromPoint enumerates the monitors and allocates on every call
static class Screens
{
    static (string Name, Rectangle Area, bool Primary)[] named = Array.Empty<(string, Rectangle, bool)>();
    static Rectangle[] areas = Load();
    static long loadedAt = Environment.TickCount64;

    static Rectangle[] Load()
    {
        var all = System.Windows.Forms.Screen.AllScreens;
        named = all.Select(s => (s.DeviceName, s.WorkingArea, s.Primary)).ToArray();
        return all.Select(s => s.WorkingArea).ToArray();
    }

    static void Refresh()
    {
        long now = Environment.TickCount64;
        if (now - loadedAt > 5000) { areas = Load(); loadedAt = now; }   // picks up taskbar / monitor changes
    }

    // The monitors (device name, work area, is it the main one), in the system's order
    public static (string Name, Rectangle Area, bool Primary)[] Monitors { get { Refresh(); return named; } }

    // The work area a pet is tied to: "free" = none (it may roam every monitor); a device name = that monitor;
    // "" or a monitor that is no longer connected = the main one
    public static Rectangle? HomeOf(string monitor)
    {
        if (monitor == "free") return null;
        Refresh();
        var n = named;
        foreach (var m in n) if (m.Name == monitor) return m.Area;
        foreach (var m in n) if (m.Primary) return m.Area;
        return n[0].Area;
    }

    public static Rectangle RandomArea(Random r) { Refresh(); var a = areas; return a[r.Next(a.Length)]; }

    // Work area of the monitor containing (x, y), or the nearest one
    public static Rectangle WorkAreaAt(int x, int y)
    {
        Refresh();
        var a = areas;
        Rectangle best = a[0];
        double bestD = double.MaxValue;
        foreach (var r in a)
        {
            if (r.Contains(x, y)) return r;
            int dx = Math.Max(Math.Max(r.Left - x, 0), x - r.Right), dy = Math.Max(Math.Max(r.Top - y, 0), y - r.Bottom);
            double d = (double)dx * dx + (double)dy * dy;
            if (d < bestD) { bestD = d; best = r; }
        }
        return best;
    }
}

// The cat's picture lives in a DIB section selected into a memory DC once: GDI+ draws straight into it, and showing
// it is a single UpdateLayeredWindow call (no bitmap/DC created and destroyed on every frame).
sealed class LayeredSurface : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFOHEADER { public int Size, Width, Height; public short Planes, BitCount; public int Compression, SizeImage, XPels, YPels, ClrUsed, ClrImportant; }

    [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);

    public readonly Bitmap Bitmap;
    readonly IntPtr memDc, hBmp, oldObj;
    readonly Native.SIZE size;

    public LayeredSurface(int w, int h)
    {
        var bi = new BITMAPINFOHEADER { Size = 40, Width = w, Height = -h, Planes = 1, BitCount = 32 };   // negative height = top-down
        IntPtr screenDc = Native.GetDC(IntPtr.Zero);
        memDc = Native.CreateCompatibleDC(screenDc);
        hBmp = CreateDIBSection(screenDc, ref bi, 0, out var bits, IntPtr.Zero, 0);
        Native.ReleaseDC(IntPtr.Zero, screenDc);
        oldObj = Native.SelectObject(memDc, hBmp);
        Bitmap = new Bitmap(w, h, w * 4, System.Drawing.Imaging.PixelFormat.Format32bppPArgb, bits);
        size = new Native.SIZE(w, h);
    }

    public void Push(IntPtr hwnd, int x, int y, byte alpha = 255)
    {
        var dst = new Native.POINT(x, y);
        var src = new Native.POINT(0, 0);
        var bf = new Native.BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = alpha, AlphaFormat = 1 };   // AC_SRC_OVER, AC_SRC_ALPHA
        var sz = size;
        Native.UpdateLayeredWindow(hwnd, IntPtr.Zero, ref dst, ref sz, memDc, ref src, 0, ref bf, 2);   // ULW_ALPHA
    }

    public void Dispose()
    {
        Bitmap.Dispose();
        Native.SelectObject(memDc, oldObj);
        Native.DeleteObject(hBmp);
        Native.DeleteDC(memDc);
    }
}
