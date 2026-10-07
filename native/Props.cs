using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

// Pixel art helper: art strings with a generated 1px outline (palette key '#').
static class PixelArt
{
    public static Bitmap Render(string[] art, Dictionary<char, Color> pal, int u, bool outline = true)
    {
        int h = art.Length, w = art.Max(r => r.Length);
        int o = outline ? 1 : 0;   // art that already contains its own outline gets none added
        bool Solid(int x, int y) => x >= 0 && y >= 0 && y < h && x < art[y].Length && art[y][x] != '.';
        var bmp = new Bitmap((w + 2 * o) * u, (h + 2 * o) * u, PixelFormat.Format32bppPArgb);
        using var g = System.Drawing.Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        for (int y = -o; y < h + o; y++)
            for (int x = -o; x < w + o; x++)
            {
                Color c;
                if (Solid(x, y)) c = pal[art[y][x]];
                else if (outline && (Solid(x - 1, y) || Solid(x + 1, y) || Solid(x, y - 1) || Solid(x, y + 1))) c = pal['#'];
                else continue;
                using var b = new SolidBrush(c);
                g.FillRectangle(b, (x + o) * u, (y + o) * u, u, u);
            }
        return bmp;
    }

    // A little heart (floats up while the cat is petted, like the z's of sleep)
    public static readonly string[] HeartArt =
    {
        ".oo.oo.",
        "ohooooo",
        "ooooooo",
        ".ooooo.",
        "..ooo..",
        "...o...",
    };
    public static readonly Dictionary<char, Color> HeartPal = new()
    {
        ['o'] = Color.FromArgb(0xFF, 0x5C, 0x8A), ['h'] = Color.FromArgb(0xFF, 0xB0, 0xC8), ['#'] = Color.FromArgb(0x7A, 0x14, 0x34),
    };

    public static readonly string[] Bowl =
    {
        "..fFfffFff..",
        ".fffFffffff.",
        "RRRRRRRRRRRR",
        "rrrrrrrrrrrr",
        ".rrrrrrrrrr.",
        "..bbbbbbbb..",
    };
    // Palettes are derived from the owner's colours: lighter/darker variants give the pixel-art shading
    public static Color Mix(Color a, Color b, double t) =>
        Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
    public static Color Lighter(Color c, double t) => Mix(c, Color.White, t);
    public static Color Darker(Color c, double t) => Mix(c, Color.Black, t);

    static readonly Color Food = Color.FromArgb(0x8A, 0x5A, 0x32);   // the food never changes colour

    public static Dictionary<char, Color> BowlPal(Color body) => new()
    {
        ['f'] = Food, ['F'] = Lighter(Food, 0.3),
        ['R'] = Lighter(body, 0.3), ['r'] = body,
        ['b'] = Darker(body, 0.3), ['#'] = Darker(body, 0.75),
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
    public static Dictionary<char, Color> BedPal(Color outer) => new()
    {
        ['l'] = Lighter(outer, 0.4), ['m'] = outer, ['c'] = Lighter(outer, 0.45),
        ['d'] = Darker(outer, 0.4), ['#'] = Darker(outer, 0.75),
    };

    // The ball: a 6x6 pixel circle (the ring comes from the user's SVG, '#' = ring), filled in and shaded here:
    // highlight at the top left, shadow at the bottom right. 'k' is the ring colour.
    static readonly string[] BallRing =
    {
        ".####.",
        "##..##",
        "#....#",
        "#....#",
        "##..##",
        ".####.",
    };

    public static readonly string[] BallArt = MakeBall();

    static string[] MakeBall()
    {
        int n = BallRing.Length;
        double c = n / 2.0, r = n / 2.0;
        var rows = new string[n];
        for (int y = 0; y < n; y++)
        {
            string ring = BallRing[y];
            int first = ring.IndexOf('#'), last = ring.LastIndexOf('#');
            var row = new char[n];
            for (int x = 0; x < n; x++)
            {
                if (ring[x] == '#') { row[x] = 'k'; continue; }
                if (x < first || x > last) { row[x] = '.'; continue; }   // outside the ring
                double dx = x + 0.5 - c, dy = y + 0.5 - c;
                double light = -(dx + dy) * 0.7071 / r;                    // 1 = facing the light (top left), -1 = away
                row[x] = light > 0.3 ? 'h' : light < -0.3 ? 'S' : light < -0.05 ? 's' : 'o';
            }
            rows[y] = new string(row);
        }
        return rows;
    }

    public static Dictionary<char, Color> BallPal(Color body) => new()
    {
        ['o'] = body, ['h'] = Lighter(body, 0.45), ['s'] = Darker(body, 0.15), ['S'] = Darker(body, 0.3), ['k'] = Darker(body, 0.55),
    };

    // The three objects in one colour (every other shade is derived from it)
    public static Bitmap BowlImage(int u, Color c) => Render(Bowl, BowlPal(c), u);
    public static Bitmap BedImage(int u, Color c) => Render(Bed, BedPal(c), u);
    public static Bitmap BallImage(int u, Color c) => Render(BallArt, BallPal(c), u, outline: false);
}

// A little object living in its own click-through-where-transparent window (bowl, bed, ball).
// Left-drag moves it, right-click offers "remove".
abstract class PropWindow : Form
{
    protected Bitmap img;
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

    public abstract void Recolor();   // redraw in the colours currently chosen

    protected void SetImage(Bitmap b)
    {
        var old = img;
        img = b;
        if (IsHandleCreated) Native.PushBitmap(Handle, img, Left, Top);
        old.Dispose();
    }

    public Point Center => new(Left + Width / 2, Top + Height / 2);

    // Where the window may go while dragged (override to restrict)
    protected virtual Point Constrain(Point p) => p;
    protected virtual void OnGrab() { }
    protected virtual void OnDrop() { }
    protected virtual void OnDragged(Point p) { }

    public event Action? Placed;   // it was put down (or came to rest): its position is worth saving
    protected void RaisePlaced() => Placed?.Invoke();

    public void Reassert() { if (TopMost) Native.SetWindowPos(Handle, (IntPtr)(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); }   // HWND_TOPMOST, only while "always on top" is on

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

    protected override void OnDrop() => RaisePlaced();

    readonly int u;

    readonly CatProfile cat;   // the cat this object belongs to (its colour)

    public override void Recolor() => SetImage(IsBed ? PixelArt.BedImage(u, cat.BedColor) : PixelArt.BowlImage(u, cat.BowlColor));

    public TaskbarProp(bool bed, int u, CatProfile owner) : base(bed ? PixelArt.BedImage(u, owner.BedColor) : PixelArt.BowlImage(u, owner.BowlColor))
    {
        IsBed = bed;
        this.u = u;
        cat = owner;
    }

    static Rectangle Area(Point near) => Screens.WorkAreaAt(near.X, near.Y);

    protected override Point Constrain(Point p)
    {
        var wa = Area(Cursor.Position);
        return new Point(Math.Clamp(p.X, wa.Left, wa.Right - Width), wa.Bottom - Height);
    }

    // Put it on the taskbar of the screen holding centerX
    public void PlaceNear(int centerX, Rectangle wa)
    {
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
    double BounceMin => 130 * S;   // below this impact speed on the floor the ball just rolls (a late timer tick can add ~100 px/s of gravity)

    public event Action<double>? Bounced;   // hit a wall or the floor: argument = loudness 0.35..1 from the impact speed
    long lastBounceAt, waAt;
    Rectangle wa;

    void Bounce(double speed)
    {
        long now = Environment.TickCount64;
        if (speed < 40 * S) return;   // too soft to hear
        Bounced?.Invoke(Math.Clamp(speed / (900 * S), 0.65, 1));
    }

    readonly int u;

    readonly CatProfile cat;

    public override void Recolor() => SetImage(PixelArt.BallImage(u, cat.BallColor));

    public BallWindow(int u, float scale, CatProfile owner) : base(PixelArt.BallImage(u, owner.BallColor))
    {
        S = scale;
        this.u = u;
        cat = owner;
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

    // Back where it was left (it falls if that was in the air)
    public void Restore(int px, int py)
    {
        x = px; y = py; vx = vy = 0;
        Location = new Point(px, py);
        Start();
    }

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
        if (now - waAt > 250) { wa = Screens.WorkAreaAt((int)x + Width / 2, (int)y + Height / 2); waAt = now; }
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
            double inSpeed = Math.Abs(vy);
            if (inSpeed >= BounceMin)
            {
                Bounce(inSpeed);                  // a real bounce
                vy = -vy * Restitution;
                if (Math.Abs(vy) < BounceMin) vy = 0;
            }
            else vy = 0;                          // resting or rolling: gravity jitter, no sound
        }
        if (floor && vy == 0) vx *= Math.Max(0, 1 - 2.5 * dt);   // rolling friction
        Location = new Point((int)Math.Round(x), (int)Math.Round(y));
        if (floor && vy == 0 && Math.Abs(vx) < 8) { vx = 0; physT.Stop(); RaisePlaced(); }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) physT.Dispose();
        base.Dispose(disposing);
    }
}
