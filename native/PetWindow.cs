using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;
using static Native;

// The cat: a per-pixel-alpha layered window exactly as big as the sprite (transparent pixels let clicks through).
// Moving the cat = moving the window.
sealed class PetWindow : Form
{
    const int Cell = 32;                       // sprite cell size in the sheet
    const int WM_LBUTTONDOWN = 0x201, WM_RBUTTONDOWN = 0x204, WM_MOUSEACTIVATE = 0x21;

    // Sprite rows (0-based) and frame counts
    static readonly Dictionary<string, (int row, int frames, bool once)> Anims = new()
    {
        ["idle"] = (0, 4, false),
        ["near"] = (1, 4, false),    // idle while the mouse is close
        ["lick"] = (2, 4, false),
        ["walk"] = (4, 8, false),
        ["run"] = (5, 8, false),
        ["sleep"] = (6, 4, false),
        ["play"] = (7, 6, false),    // click: plays with the mouse
        ["pounce"] = (8, 7, true),   // fast mouse nearby: one jump
        ["frenzy"] = (9, 8, false),  // too many clicks
    };

    public static PetWindow? Instance;

    readonly Bitmap sheet;
    readonly Bitmap buf;
    readonly Graphics g;
    readonly ImageAttributes attrs = new();
    readonly int size;                          // on-screen sprite size (integer multiple of 32)
    readonly float S;                           // DPI scale

    readonly System.Windows.Forms.Timer animT = new(), moveT = new() { Interval = 16 }, logicT = new() { Interval = 1000 }, cursorT = new() { Interval = 50 };
    readonly Random rnd = new();

    double x, y;                                // window top-left, screen px
    (double X, double Y)? target;
    double hunger = 100, happiness = 100, energy = 100;
    string state = "idle", currentAnim = "idle";
    bool facingRight = true, mouseNear;
    int frame, idleTimer, sleepTick;
    bool sleepManual;
    StatsWindow? statsWin;
    long hoverOffAt;   // sleep asked by the user: no automatic wake-up
    long lockUntil, nextJumpAt, lastCursorAt, lastMoveAt, fastSince;
    readonly Queue<long> clicks = new();
    Native.POINT cursor;

    (double X, double Y)? Target
    {
        get => target;
        set { target = value; if (value != null && !moveT.Enabled) { lastMoveAt = Environment.TickCount64; moveT.Start(); } else if (value == null) moveT.Stop(); }
    }

    public PetWindow()
    {
        Instance = this;
        using (var gr = Graphics.FromHwnd(IntPtr.Zero)) S = gr.DpiX / 96f;
        size = Cell * Math.Max(1, (int)Math.Round(4 * S));

        using (var s = typeof(PetWindow).Assembly.GetManifestResourceStream("cat-sprite.png")!)
        using (var png = new Bitmap(s))
        {
            sheet = new Bitmap(png.Width, png.Height, PixelFormat.Format32bppPArgb);
            using var sg = Graphics.FromImage(sheet);
            sg.CompositingMode = CompositingMode.SourceCopy;
            sg.DrawImage(png, 0, 0, png.Width, png.Height);
        }
        buf = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
        g = Graphics.FromImage(buf);
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.CompositingMode = CompositingMode.SourceCopy;
        attrs.SetWrapMode(WrapMode.TileFlipXY);

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(size, size);
        var wa = Screen.PrimaryScreen!.WorkingArea;
        x = wa.Left + wa.Width / 2 - size / 2; y = wa.Bottom - size;
        Location = new Point((int)x, (int)y);

        animT.Tick += (_, _) => Animate();
        moveT.Tick += (_, _) => MoveStep();
        logicT.Tick += (_, _) => Logic();
        cursorT.Tick += (_, _) => PollCursor();
        animT.Interval = 100;
        GetCursorPos(out cursor);   // start from the real position (not 0,0, which would look like a huge jump)
        lastCursorAt = Now;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x80000 | 0x80 | 0x08000000;   // LAYERED | TOOLWINDOW | NOACTIVATE
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => true;

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Redraw();
        animT.Start(); logicT.Start(); cursorT.Start();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_MOUSEACTIVATE) { m.Result = (IntPtr)3; return; }   // MA_NOACTIVATE
        if (m.Msg == WM_LBUTTONDOWN) OnClickPet();
        else if (m.Msg == WM_RBUTTONDOWN) { Target = null; if (state == "sleep") SetState("idle"); else { sleepManual = true; SetState("sleep"); } }
        base.WndProc(ref m);
    }

    // Debug: current stats, and a way to start any interaction by hand (developer page in settings)
    public (double Hunger, double Happiness, double Energy, string State) Stats => (hunger, happiness, energy, state);

    public void Trigger(string what)
    {
        if (what != "sleep" && state == "sleep") state = "idle";   // wake up first
        switch (what)
        {
            case "idle": Target = null; SetState("idle"); break;
            case "lick": Target = null; SetState("lick", 2500); break;
            case "walk": case "run": Target = RandomTarget(); SetState(what); break;
            case "sleep": Target = null; sleepManual = true; SetState(state == "sleep" ? "idle" : "sleep"); break;
            case "play": Target = null; SetState("play", (int)Cfg.Cycle); break;
            case "frenzy": Target = null; SetState("frenzy", 2500); break;
            case "pounce":
                GetCursorPos(out var p);
                Target = Clamp(p.X - size / 2.0, p.Y - size / 2.0);
                frame = 0;
                SetState("pounce", (int)Cfg.Cycle + 100);
                break;
        }
    }

    static long Now => Environment.TickCount64;

    void SetState(string s, int ms = 0) { state = s; lockUntil = Now + ms; }

    // ---- geometry -------------------------------------------------------------------------------

    Rectangle AreaFor(double px, double py)
    {
        var wa = Screen.FromPoint(new Point((int)px, (int)py)).WorkingArea;
        return new Rectangle(wa.Left, wa.Top, Math.Max(0, wa.Width - size), Math.Max(0, wa.Height - size));
    }

    (double, double) Clamp(double px, double py)
    {
        var b = AreaFor(px + size / 2, py + size / 2);
        return (Math.Max(b.Left, Math.Min(b.Right, px)), Math.Max(b.Top, Math.Min(b.Bottom, py)));
    }

    (double, double) RandomTarget()
    {
        var b = AreaFor(x + size / 2, y + size / 2);
        double tx = x, ty = y;
        for (int i = 0; i < 10; i++)
        {
            tx = b.Left + rnd.NextDouble() * b.Width; ty = b.Top + rnd.NextDouble() * b.Height;
            if (Math.Sqrt((tx - x) * (tx - x) + (ty - y) * (ty - y)) > 150 * S) break;   // go somewhere actually far
        }
        return (tx, ty);
    }

    double DistTo(Native.POINT p) => Math.Sqrt(Math.Pow(p.X - x - size / 2.0, 2) + Math.Pow(p.Y - y - size / 2.0, 2));

    // Stats panel while the mouse is over the cat (an opaque pixel, not just its box); hides shortly after leaving
    void UpdateHover()
    {
        bool over = false;
        int lx = cursor.X - Left, ly = cursor.Y - Top;
        if (Visible && lx >= 0 && ly >= 0 && lx < size && ly < size) over = buf.GetPixel(lx, ly).A > 0;
        if (over)
        {
            hoverOffAt = 0;
            statsWin ??= new StatsWindow(S);
            statsWin.ShowAbove(new Rectangle(Left, Top, size, size), hunger, happiness, energy);
        }
        else if (statsWin is { Visible: true })
        {
            if (hoverOffAt == 0) hoverOffAt = Now + 350;
            else if (Now >= hoverOffAt) { statsWin.HidePanel(); hoverOffAt = 0; }
        }
    }

    // Sprite faces right; flip it toward the mouse when standing still, but never in the middle of
    // sleeping or licking (it turns when the action is over: Animate() calls this too)
    void FaceMouse()
    {
        if (mouseNear && target == null && state != "sleep" && state != "lick") facingRight = cursor.X > x + size / 2.0;
    }

    void UpdateNear() => mouseNear = DistTo(cursor) < Cfg.NearRange * S;

    // ---- input ----------------------------------------------------------------------------------

    void PollCursor()
    {
        GetCursorPos(out var p);
        if (p.X == cursor.X && p.Y == cursor.Y) return;
        long now = Now;
        double dist = Math.Sqrt(Math.Pow(p.X - cursor.X, 2) + Math.Pow(p.Y - cursor.Y, 2));
        double speed = dist / Math.Max(1, now - lastCursorAt) * 1000;   // px/s
        lastCursorAt = now;
        cursor = p;
        UpdateNear();

        FaceMouse();
        UpdateHover();

        // Sustained fast mouse movement near the cat (a quick flick, not a normal move): jump to the mouse,
        // then wait for the cooldown
        if (speed < Cfg.JumpSpeed * S) fastSince = 0;
        else if (fastSince == 0) fastSince = now;
        if (DistTo(p) < Cfg.JumpRange * S && fastSince != 0 && now - fastSince >= 120 && now >= nextJumpAt && now >= lockUntil && state != "sleep")
        {
            Target = Clamp(p.X - size / 2.0, p.Y - size / 2.0);
            frame = 0;
            SetState("pounce", (int)Cfg.Cycle + 100);
            nextJumpAt = now + (long)(Cfg.JumpCooldown * 1000);
            fastSince = 0;
        }
    }

    void OnClickPet()
    {
        hunger = Math.Min(100, hunger + 10);
        happiness = Math.Min(100, happiness + 15);
        long now = Now;
        while (clicks.Count > 0 && now - clicks.Peek() >= 2000) clicks.Dequeue();
        clicks.Enqueue(now);
        Target = null;
        if (clicks.Count >= 5) SetState("frenzy", 2500);
        else SetState("play", (int)Cfg.Cycle);
    }

    // ---- loops ----------------------------------------------------------------------------------

    // Every animation takes the same time per cycle (frame duration = cycle / frames)
    void Animate()
    {
        UpdateNear();   // the cat may have walked toward/away from a still mouse
        FaceMouse();
        UpdateHover();
        string name = state == "idle" && mouseNear ? "near" : state;
        if (currentAnim != name) { frame = 0; currentAnim = name; }
        var a = Anims[name];
        Redraw();
        if (name == "sleep")
        {
            // redraw 3x per frame so the floating z letters move smoothly
            animT.Interval = Math.Max(15, (int)(Cfg.Cycle / a.frames / 3));
            if (++sleepTick % 3 != 0) return;
        }
        else animT.Interval = Math.Max(15, (int)(Cfg.Cycle / a.frames));
        frame = a.once ? Math.Min(frame + 1, a.frames - 1) : (frame + 1) % a.frames;
    }

    void MoveStep()
    {
        if (target == null) return;
        long now = Now;
        double dt = now - lastMoveAt; lastMoveAt = now;
        double mult = state == "walk" ? 1 : state == "pounce" ? 3 : 2;
        double step = Cfg.WalkSpeed * mult / 16.0 * dt * S;    // speed is "px per 16ms" at 96 dpi
        double dx = target.Value.X - x, dy = target.Value.Y - y, dist = Math.Sqrt(dx * dx + dy * dy);
        if (dist <= step)
        {
            x = target.Value.X; y = target.Value.Y;
            Target = null;
            if (state == "walk" || state == "run") state = "idle";
        }
        else
        {
            x += dx / dist * step; y += dy / dist * step;
            if (Math.Abs(dx) > 1) facingRight = dx > 0;
        }
        Location = new Point((int)Math.Round(x), (int)Math.Round(y));
        if (statsWin is { Visible: true }) UpdateHover();   // keep the panel attached while the cat moves
    }

    void Logic()
    {
        // Other windows (taskbar, Start menu, other topmost apps) can push us down: re-assert every second
        if (TopMost && Visible && IsHandleCreated) SetWindowPos(Handle, (IntPtr)(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);   // HWND_TOPMOST, NOSIZE|NOMOVE|NOACTIVATE
        hunger = Math.Max(0, hunger - 0.5);
        happiness = Math.Max(0, happiness - 0.3);
        energy = Math.Max(0, energy - 0.2);

        if (state == "sleep")
        {
            energy = Math.Min(100, energy + 1.5);     // recover while asleep, wake up when rested
            Target = null; idleTimer = 0;
            if (energy >= 100 && !sleepManual) state = "idle";   // manual sleep lasts until woken up
        }
        else if (Now < lockUntil) { }                 // click / pounce / lick animation playing
        else if (energy < 20) { sleepManual = false; SetState("sleep"); Target = null; }
        else if (target != null) { }                  // walking/running - controlled by Move()
        else
        {
            if (state is "pounce" or "lick") state = "idle";
            idleTimer++;
            double r = rnd.NextDouble();
            if (r < Cfg.Wander)
            {
                Target = RandomTarget();
                state = rnd.NextDouble() < 0.3 && energy > 50 ? "run" : "walk";
                idleTimer = 0;
            }
            else if (idleTimer > 5 && r < 0.1) { SetState("lick", 2500); idleTimer = 0; }
            else state = "idle";
        }
    }

    // ---- drawing --------------------------------------------------------------------------------

    // Three "z" rising above the head and fading out, looping every 2.4s
    void DrawZzz()
    {
        long t = Environment.TickCount64 % 2400;
        g.CompositingMode = CompositingMode.SourceOver;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        for (int i = 0; i < 3; i++)
        {
            double p = (t / 2400.0 + i / 3.0) % 1.0;                        // 0..1 progress of this letter
            float px = (float)(size * (0.62 + 0.14 * p));                    // drifts away from the head
            if (!facingRight) px = size - px;
            float py = (float)(size * (0.62 - 0.32 * p));
            int alpha = (int)(255 * Math.Sin(Math.PI * p));
            using var font = new Font("Segoe UI", (float)((7 + 6 * p) * S), FontStyle.Bold, GraphicsUnit.Point);
            using var brush = new SolidBrush(Color.FromArgb(Math.Clamp(alpha, 0, 255), 0x9a, 0xb4, 0xff));
            g.DrawString("z", font, brush, px, py);
        }
        g.CompositingMode = CompositingMode.SourceCopy;
    }

    void Redraw()
    {
        if (!IsHandleCreated) return;
        var a = Anims[currentAnim];
        g.Clear(Color.Transparent);
        var dest = facingRight ? new Rectangle(0, 0, size, size) : new Rectangle(size, 0, -size, size);   // negative width = mirror
        g.DrawImage(sheet, dest, frame % a.frames * Cell, a.row * Cell, Cell, Cell, GraphicsUnit.Pixel, attrs);

        if (currentAnim == "sleep") DrawZzz();

        PushBitmap(Handle, buf, Left, Top);
    }
}
