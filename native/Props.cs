using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

// Pixel art helper: art strings with a generated 1px outline (palette key '#').
static class PixelArt
{
    public static Bitmap Render(string[] art, Dictionary<char, Color> pal, int u)
    {
        int h = art.Length, w = art.Max(r => r.Length);
        bool Solid(int x, int y) => x >= 0 && y >= 0 && y < h && x < art[y].Length && art[y][x] != '.';
        var bmp = new Bitmap((w + 2) * u, (h + 2) * u, PixelFormat.Format32bppPArgb);
        using var g = System.Drawing.Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        for (int y = -1; y <= h; y++)
            for (int x = -1; x <= w; x++)
            {
                Color c;
                if (Solid(x, y)) c = pal[art[y][x]];
                else if (Solid(x - 1, y) || Solid(x + 1, y) || Solid(x, y - 1) || Solid(x, y + 1)) c = pal['#'];
                else continue;
                using var b = new SolidBrush(c);
                g.FillRectangle(b, (x + 1) * u, (y + 1) * u, u, u);
            }
        return bmp;
    }

    public static readonly string[] Bowl =
    {
        "..fFfffFff..",
        ".fffFffffff.",
        "RRRRRRRRRRRR",
        "rrrrrrrrrrrr",
        ".rrrrrrrrrr.",
        "..bbbbbbbb..",
    };
    public static readonly Dictionary<char, Color> BowlPal = new()
    {
        ['f'] = Color.FromArgb(0x8a, 0x5a, 0x32), ['F'] = Color.FromArgb(0xb3, 0x7a, 0x45),
        ['R'] = Color.FromArgb(0xf0, 0x7c, 0x82), ['r'] = Color.FromArgb(0xe0, 0x52, 0x5a),
        ['b'] = Color.FromArgb(0x9a, 0x34, 0x3c), ['#'] = Color.FromArgb(0x3a, 0x12, 0x16),
    };

    public static readonly string[] Bed =
    {
        "...lmmmmmmmmmmmmmm...",
        "..mmmmmmmmmmmmmmmmm..",
        ".mmcccccccccccccccmm.",
        "mmccccccccccccccccmmm",
        "mmmmmmmmmmmmmmmmmmmmm",
        ".mmmmmmmmmmmmmmmmmmm.",
        "..ddddddddddddddddd..",
    };
    public static readonly Dictionary<char, Color> BedPal = new()
    {
        ['l'] = Color.FromArgb(0xc4, 0xd0, 0xff), ['m'] = Color.FromArgb(0x7f, 0x95, 0xf0), ['c'] = Color.FromArgb(0xb9, 0xc6, 0xff),
        ['d'] = Color.FromArgb(0x4a, 0x58, 0xa8), ['#'] = Color.FromArgb(0x1c, 0x22, 0x4a),
    };

    public static readonly string[] BallArt =
    {
        "..oooo..",
        ".hhooooo",
        "hhooooos",
        "hooooosS",
        "ooooossS",
        "oooosssS",
        ".ooSSSS.",
        "..SSSS..",
    };
    public static readonly Dictionary<char, Color> BallPal = new()
    {
        ['o'] = Color.FromArgb(0xff, 0x7a, 0x45), ['h'] = Color.FromArgb(0xff, 0xc2, 0xa0), ['s'] = Color.FromArgb(0xe0, 0x5a, 0x2a),
        ['S'] = Color.FromArgb(0xb8, 0x3f, 0x18), ['#'] = Color.FromArgb(0x5a, 0x24, 0x10),
    };
}

// A little object living in its own click-through-where-transparent window (bowl, bed, ball).
// Left-drag moves it, right-click offers "remove".
abstract class PropWindow : Form
{
    protected readonly Bitmap img;
    readonly ContextMenuStrip menu = new();
    bool dragging;
    Point grab;

    protected PropWindow(Bitmap image)
    {
        img = image;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = Cfg.OnTop;
        StartPosition = FormStartPosition.Manual;
        ClientSize = image.Size;
        menu.Items.Add(new ToolStripMenuItem(Str.T("menu.remove"), null, (_, _) => Close()));
    }

    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ExStyle |= 0x80000 | 0x80 | 0x08000000; return cp; }   // LAYERED | TOOLWINDOW | NOACTIVATE
    }

    protected override bool ShowWithoutActivation => true;

    protected override void OnShown(EventArgs e) { base.OnShown(e); Native.PushBitmap(Handle, img, Left, Top); }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x21) { m.Result = (IntPtr)3; return; }   // WM_MOUSEACTIVATE -> MA_NOACTIVATE
        base.WndProc(ref m);
    }

    public Point Center => new(Left + Width / 2, Top + Height / 2);

    // Where the window may go while dragged (override to restrict)
    protected virtual Point Constrain(Point p) => p;
    protected virtual void OnGrab() { }
    protected virtual void OnDrop() { }
    protected virtual void OnDragged(Point p) { }

    public void Reassert() => Native.SetWindowPos(Handle, (IntPtr)(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);   // HWND_TOPMOST

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            dragging = true; Capture = true;
            grab = new Point(Cursor.Position.X - Left, Cursor.Position.Y - Top);
            OnGrab();
        }
        else if (e.Button == MouseButtons.Right) menu.Show(Cursor.Position);
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (dragging)
        {
            var p = Constrain(new Point(Cursor.Position.X - grab.X, Cursor.Position.Y - grab.Y));
            Location = p;
            OnDragged(p);
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (dragging) { dragging = false; Capture = false; OnDrop(); }
        base.OnMouseUp(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { img.Dispose(); menu.Dispose(); }
        base.Dispose(disposing);
    }
}

// Bowl and bed: they sit on the taskbar (bottom of the work area) and can only slide along it.
sealed class TaskbarProp : PropWindow
{
    public readonly bool IsBed;

    public TaskbarProp(bool bed, int u) : base(bed ? PixelArt.Render(PixelArt.Bed, PixelArt.BedPal, u) : PixelArt.Render(PixelArt.Bowl, PixelArt.BowlPal, u))
    {
        IsBed = bed;
    }

    static Rectangle Area(Point near) => Screen.FromPoint(near).WorkingArea;

    protected override Point Constrain(Point p)
    {
        var wa = Area(Cursor.Position);
        return new Point(Math.Clamp(p.X, wa.Left, wa.Right - Width), wa.Bottom - Height);
    }

    // Put it on the taskbar of the screen holding centerX
    public void PlaceNear(int centerX, Screen screen)
    {
        var wa = screen.WorkingArea;
        Location = new Point(Math.Clamp(centerX - Width / 2, wa.Left, wa.Right - Width), wa.Bottom - Height);
    }
}

// The ball: thrown by the cat (Kick) or by the user (drag and release), bounces on the screen edges.
sealed class BallWindow : PropWindow
{
    readonly float S;
    readonly System.Windows.Forms.Timer physT = new() { Interval = 16 };
    readonly List<(long t, Point p)> trail = new();
    double x, y, vx, vy;
    long last;
    bool held;

    const double Restitution = 0.78;

    public event Action? Bounced;   // hit a wall or the floor hard enough to be heard
    long lastBounceAt, waAt;
    Rectangle wa;

    void Bounce(double speed)
    {
        long now = Environment.TickCount64;
        if (speed < 120 * S || now - lastBounceAt < 90) return;
        lastBounceAt = now;
        Bounced?.Invoke();
    }

    public BallWindow(int u, float scale) : base(PixelArt.Render(PixelArt.BallArt, PixelArt.BallPal, u))
    {
        S = scale;
        physT.Tick += (_, _) => Step();
    }

    double Gravity => 1800 * S;

    public void Drop(int centerX, int topY)
    {
        x = centerX - Width / 2.0; y = topY; vx = vy = 0;
        Location = new Point((int)x, (int)y);
        Start();
    }

    public void Kick(double kx, double ky) { vx = kx; vy = ky; Start(); }

    void Start() { last = Environment.TickCount64; if (!physT.Enabled) physT.Start(); }

    protected override void OnGrab() { held = true; physT.Stop(); trail.Clear(); vx = vy = 0; }
    protected override void OnDragged(Point p)
    {
        x = p.X; y = p.Y;
        long now = Environment.TickCount64;
        trail.Add((now, p));
        trail.RemoveAll(t => now - t.t > 120);
    }
    protected override void OnDrop()
    {
        held = false;
        // throw with the speed the mouse had in the last ~100ms
        if (trail.Count >= 2)
        {
            var a = trail[0]; var b = trail[^1];
            double dt = Math.Max(16, b.t - a.t) / 1000.0;
            vx = Math.Clamp((b.p.X - a.p.X) / dt, -3500 * S, 3500 * S);
            vy = Math.Clamp((b.p.Y - a.p.Y) / dt, -3500 * S, 3500 * S);
        }
        Start();
    }

    void Step()
    {
        if (held) return;
        long now = Environment.TickCount64;
        double dt = Math.Clamp((now - last) / 1000.0, 0, 0.05); last = now;
        if (now - waAt > 250) { wa = Screen.FromPoint(new Point((int)x + Width / 2, (int)y + Height / 2)).WorkingArea; waAt = now; }   // looking up the screen every frame is slow
        double left = wa.Left, right = wa.Right - Width, top = wa.Top, bottom = wa.Bottom - Height;

        vy += Gravity * dt;
        x += vx * dt; y += vy * dt;
        if (x < left) { x = left; Bounce(Math.Abs(vx)); vx = -vx * Restitution; }
        else if (x > right) { x = right; Bounce(Math.Abs(vx)); vx = -vx * Restitution; }
        if (y < top) { y = top; Bounce(Math.Abs(vy)); vy = -vy * Restitution; }
        bool floor = false;
        if (y >= bottom)
        {
            y = bottom; floor = true;
            Bounce(Math.Abs(vy));
            vy = -vy * Restitution;
            if (Math.Abs(vy) < 120 * S) vy = 0;
        }
        if (floor && vy == 0) vx *= Math.Max(0, 1 - 2.5 * dt);   // rolling friction
        Location = new Point((int)Math.Round(x), (int)Math.Round(y));
        if (floor && vy == 0 && Math.Abs(vx) < 8) { vx = 0; physT.Stop(); }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) physT.Dispose();
        base.Dispose(disposing);
    }
}
