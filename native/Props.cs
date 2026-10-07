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

    // The bowl, seen from above, in four fill levels (the model was drawn by the project owner). a = outline, b/c/e = the
    // bowl's tones (derived from the owner's colour), d = the dark inside, f = a shine; g/h/i/j = the food (never changes colour)
    public static readonly string[][] BowlLevels =
    {
        new[]   // empty
        {
            ".....aaaaa.....",
            "...aabbbbbaa...",
            "..abbcccccbba..",
            ".abccdddddccba.",
            ".abadddddddaba.",
            "acceadddddabcca",
            "accbeaaaaaebcca",
            "acbbbeeeeebbcca",
            "acbbbefeebbbcca",
            "acbbefeeebbbcca",
            ".accbeeebbccca.",
            "..aaabbbccaaa..",
            ".....aaaaa.....",
        },
        new[]   // almost empty
        {
            ".....aaaaa.....",
            "...aabbbbbaa...",
            "..abbcccccbba..",
            ".abccdddddccba.",
            ".abadddddddaba.",
            "acceadhhgdabcca",
            "accbeaaaaaebcca",
            "acbbbeeeeebbcca",
            "acbbbefeebbbcca",
            "acbbefeeebbbcca",
            ".accbeeebbccca.",
            "..aaabbbccaaa..",
            ".....aaaaa.....",
        },
        new[]   // half full
        {
            ".....aaaaa.....",
            "...aabbbbbaa...",
            "..abbcccccbba..",
            ".acccddggdccba.",
            ".acagjghjgdaba.",
            "acceahjgggabcca",
            "accbeaaaaaebcca",
            "acbbbeeeeebbcca",
            "acbbbefeebbbcca",
            "acbbefeeebbbcca",
            ".accbeeebbccca.",
            "..aaabbbccaaa..",
            ".....aaaaa.....",
        },
        new[]   // full
        {
            ".....aaaaa.....",
            "...aabgbgbaa...",
            "..abgggghggba..",
            ".abgiiigjgigba.",
            ".abaihhggihaba.",
            "acceahhhjgabcca",
            "accbeaaaaaebcca",
            "acbbbeeeeebbcca",
            "acbbbefeebbbcca",
            "acbbefeeebbbcca",
            ".accbeeebbccca.",
            "..aaabbbccaaa..",
            ".....aaaaa.....",
        },
    };

    // 0 empty .. 3 full, from how much food is left
    public static int FoodLevel(double food) => food <= 0 ? 0 : food <= CatProfile.BowlCapacity / 3 ? 1 : food <= CatProfile.BowlCapacity * 2 / 3 ? 2 : 3;

    // Palettes are derived from the owner's colours: lighter/darker variants give the pixel-art shading
    public static Color Mix(Color a, Color b, double t) =>
        Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
    public static Color Lighter(Color c, double t) => Mix(c, Color.White, t);
    public static Color Darker(Color c, double t) => Mix(c, Color.Black, t);

    public static Dictionary<char, Color> BowlPal(Color body) => new()
    {
        ['a'] = HslShift(body, -0.3, -0.05), ['b'] = body, ['c'] = HslShift(body, -0.13, 0), ['e'] = HslShift(body, 0.1, 0.05),
        ['d'] = Color.FromArgb(44, 30, 49), ['f'] = Color.FromArgb(211, 238, 211),
        ['g'] = Color.FromArgb(222, 93, 58), ['h'] = Color.FromArgb(243, 168, 51), ['i'] = Color.FromArgb(247, 243, 183), ['j'] = Color.FromArgb(233, 133, 55),
    };

    // The bed: a round "doughnut" cushion seen from slightly above, lit from the top left. m = the owner's colour, h/l = lit,
    // d/D = in shade; c/C = the hollow in the middle (the inner wall at the back, then the floor)
    public static readonly string[] Bed =
    {
        ".......hhhhhhhlll.......",
        "....hhhhhhhhhhhllllm....",
        "..hhhhhhhhhhhhhllllmmm..",
        ".hhhhhhccccccccccllmmmm.",
        "llhhhhccccccccccccmmmmmd",
        "llllhhCCCCCCCCCCCCmmmmdd",
        "lllllllCCCCCCCCCCmmmmddd",
        "mmlllllllllllmmmmmmmdddd",
        ".mmmmmmmmmmmmmmmmmddddd.",
        "..mmmmmmmmmmmmmmdddddd..",
        "....mmmmmmmddddddddd....",
        ".......dddddddddd.......",
    };

    // Same hue, new lightness/saturation (a plain mix with white or black turns a vivid colour into a washed-out grey)
    public static Color HslShift(Color c, double dl, double ds)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), l = (max + min) / 2, d = max - min, h = 0, sat = 0;
        if (d > 0)
        {
            sat = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            h = max == r ? (g - b) / d + (g < b ? 6 : 0) : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
            h /= 6;
        }
        l = Math.Clamp(l + dl, 0.04, 0.94); sat = Math.Clamp(sat + ds, 0, 1);
        double q = l < 0.5 ? l * (1 + sat) : l + sat - l * sat, p = 2 * l - q;
        double Ch(double t) { t = (t % 1 + 1) % 1; return t < 1 / 6.0 ? p + (q - p) * 6 * t : t < 0.5 ? q : t < 2 / 3.0 ? p + (q - p) * (2 / 3.0 - t) * 6 : p; }
        return d == 0 && sat == 0 ? Color.FromArgb((int)Math.Round(l * 255), (int)Math.Round(l * 255), (int)Math.Round(l * 255))
            : Color.FromArgb((int)Math.Round(Ch(h + 1 / 3.0) * 255), (int)Math.Round(Ch(h) * 255), (int)Math.Round(Ch(h - 1 / 3.0) * 255));
    }

    public static Dictionary<char, Color> BedPal(Color outer)
    {
        return new()
        {
            ['h'] = HslShift(outer, 0.2, -0.05), ['l'] = HslShift(outer, 0.1, 0), ['m'] = outer, ['d'] = HslShift(outer, -0.12, 0), ['D'] = HslShift(outer, -0.24, 0),
            ['c'] = HslShift(outer, -0.2, 0.05), ['C'] = HslShift(outer, -0.34, 0.05), ['#'] = Darker(outer, 0.75),
        };
    }

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
    public static Bitmap BowlImage(int u, Color c, int level = 3) => Render(BowlLevels[Math.Clamp(level, 0, 3)], BowlPal(c), u, outline: false);
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
    Point grab, grabAt;
    public event Action? Clicked;   // pressed and released without moving it
    protected void AddMenu(string text, Action a) => menu.Items.Insert(0, new ToolStripMenuItem(text, null, (_, _) => a()));

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
            grabAt = Cursor.Position;
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
        if (dragging)
        {
            dragging = false; Capture = false; OnDrop();
            if (Math.Abs(Cursor.Position.X - grabAt.X) + Math.Abs(Cursor.Position.Y - grabAt.Y) < 4) Clicked?.Invoke();
        }
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

    int shownLevel;
    public override void Recolor() => SetImage(IsBed ? PixelArt.BedImage(u, cat.BedColor) : PixelArt.BowlImage(u, cat.BowlColor, shownLevel = PixelArt.FoodLevel(cat.BowlFood)));

    public TaskbarProp(bool bed, int u, CatProfile owner) : base(bed ? PixelArt.BedImage(u, owner.BedColor) : PixelArt.BowlImage(u, owner.BowlColor, PixelArt.FoodLevel(owner.BowlFood)))
    {
        IsBed = bed;
        this.u = u;
        cat = owner;
        shownLevel = PixelArt.FoodLevel(owner.BowlFood);
        if (!bed)
        {
            Clicked += Refill;   // click the bowl to fill it up again
            AddMenu(Str.T("bowl.refill"), Refill);
        }
    }

    // The bowl holds BowlCapacity points of food (a point = a point of hunger the cat gets back); the picture follows
    public bool HasFood => cat.BowlFood > 0;
    public double Take(double amount)
    {
        double got = Math.Min(amount, cat.BowlFood);
        cat.BowlFood -= got;
        ShowFood();
        return got;
    }
    public void Refill() { cat.BowlFood = CatProfile.BowlCapacity; ShowFood(); }
    void ShowFood() { if (!IsBed && PixelArt.FoodLevel(cat.BowlFood) != shownLevel) Recolor(); }

    static Rectangle Area(Point near) => Screens.WorkAreaAt(near.X, near.Y);

    protected override Point Constrain(Point p)
    {
        var wa = Screens.HomeOf(cat.Monitor) ?? Area(Cursor.Position);   // a pet tied to a monitor keeps its objects there
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
        bool free = cat.Monitor == "free";   // its pet roams every monitor: the ball may roll or fly across to the next one
        if (free || now - waAt > 250) { wa = Screens.HomeOf(cat.Monitor) ?? Screens.WorkAreaAt((int)x + Width / 2, (int)y + Height / 2); waAt = now; }
        double left = wa.Left, right = wa.Right - Width, top = wa.Top, bottom = wa.Bottom - Height;
        int midY = (int)y + Height / 2;
        bool openLeft = free && Screens.OtherMonitorAt(wa, wa.Left - 1, midY), openRight = free && Screens.OtherMonitorAt(wa, wa.Right, midY);   // another monitor right next to this edge: no wall

        vy += Gravity * dt;
        x += vx * dt; y += vy * dt;
        if (x < left && !openLeft) { x = left; Bounce(Math.Abs(vx)); vx = -vx * Restitution; }
        else if (x > right && !openRight) { x = right; Bounce(Math.Abs(vx)); vx = -vx * Restitution; }
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
