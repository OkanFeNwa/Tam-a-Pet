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
    const int WM_LBUTTONDOWN = 0x201, WM_MOUSEACTIVATE = 0x21;

    // Sprite rows (0-based) and frame counts
    static readonly Dictionary<string, (int row, int frames, bool once)> Anims = new()
    {
        ["idle"] = (0, 4, false),
        ["near"] = (1, 4, false),    // idle while the mouse is close
        ["lick"] = (2, 4, false),
        ["eat"] = (2, 4, false),     // at the bowl (same frames as licking)
        ["walk"] = (4, 8, false),
        ["run"] = (5, 8, false),
        ["sleep"] = (6, 4, false),
        ["play"] = (7, 6, false),    // click: plays with the mouse
        ["pounce"] = (8, 7, true),   // fast mouse nearby: one jump
        ["frenzy"] = (9, 8, false),  // too many clicks
    };

    public static PetWindow? Instance;

    enum Goal { None, Bowl, Bed, Ball }

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
    long hoverOffAt, hoverSince;   // sleep asked by the user: no automatic wake-up
    long lockUntil, nextJumpAt, lastCursorAt, lastMoveAt, fastSince;
    readonly Queue<long> clicks = new();
    TaskbarProp? bowl, bed;
    BallWindow? ball;
    Goal goal;                    // what the cat is walking to (cleared whenever it is sent elsewhere)
    bool keepGoal;
    long chaseUntil, kickCooldown, lastKickAt;
    long noInteractUntil, fleeAt, angryCooldownUntil;   // anger: the cat hisses, then runs away and can't be touched for a bit
    Native.POINT cursor;

    (double X, double Y)? Target
    {
        get => target;
        set { target = value; if (!keepGoal) goal = Goal.None; if (value != null && !moveT.Enabled) { lastMoveAt = Environment.TickCount64; moveT.Start(); } else if (value == null) moveT.Stop(); }
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
        TopMost = Cfg.OnTop;
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
        base.WndProc(ref m);
    }

    // ---- menu, objects, goals ---------------------------------------------------------------------

    void ToggleSleep()
    {
        Target = null;
        if (state == "sleep") SetState("idle"); else { sleepManual = true; SetState("sleep"); }
    }

    // Objects are summoned from the tray menu (PetApp)
    public bool HasBowl => bowl != null;
    public bool HasBed => bed != null;
    public bool HasBall => ball != null;

    int Unit => Math.Max(1, (int)Math.Round(4 * S));   // art pixel size: same as the cat's

    // Bowl / bed: spawn on the taskbar next to the cat, or remove if already there
    public void ToggleProp(bool isBed)
    {
        var existing = isBed ? bed : bowl;
        if (existing != null) { existing.Close(); return; }
        var p = new TaskbarProp(isBed, Unit);
        var scr = Screen.FromPoint(new Point((int)(x + size / 2.0), (int)(y + size / 2.0)));
        p.PlaceNear((int)(x + size / 2.0 + (isBed ? -1.4 : 1.2) * size), scr);
        p.FormClosed += (_, _) =>
        {
            if (isBed) { if (bed == p) bed = null; } else if (bowl == p) bowl = null;
            if (goal == (isBed ? Goal.Bed : Goal.Bowl)) { Target = null; state = "idle"; }
        };
        if (isBed) bed = p; else bowl = p;
        p.Show();
    }

    public void ToggleBall()
    {
        if (ball != null) { ball.Close(); return; }
        var b = new BallWindow(Unit, S);
        b.Bounced += () => Audio.Play("boing");
        b.FormClosed += (_, _) =>
        {
            if (ball == b) ball = null;
            if (goal == Goal.Ball) { Target = null; state = "idle"; }
            chaseUntil = 0;
        };
        ball = b;
        b.Drop((int)(x + size / 2.0), (int)y);   // falls from the cat
        b.Show();
    }

    (double, double)? GoalTarget(Goal gl)
    {
        switch (gl)
        {
            case Goal.Bowl when bowl != null:   // stand left of the bowl, on the taskbar
                return Clamp(bowl.Center.X - size, Screen.FromPoint(bowl.Center).WorkingArea.Bottom - size);
            case Goal.Bed when bed != null:     // lie "inside" the bed: feet overlap its back half
                return Clamp(bed.Center.X - size / 2.0, bed.Top + bed.Height * 0.55 - size);
            case Goal.Ball when ball != null:
                return Clamp(ball.Center.X - size / 2.0, ball.Center.Y - size / 2.0);
        }
        return null;
    }

    void GoTo(Goal gl)
    {
        var t = GoalTarget(gl);
        if (t == null) return;
        keepGoal = true; Target = t; keepGoal = false;
        goal = gl;
        state = gl == Goal.Bed ? "walk" : "run";
    }

    void Arrived(Goal gl)
    {
        switch (gl)
        {
            case Goal.Bowl when bowl != null:
                facingRight = bowl.Center.X > x + size / 2.0;
                SetState("eat");   // hunger is restored gradually in Logic() while eating
                break;
            case Goal.Bed:
                sleepManual = false;   // wakes by itself when rested
                SetState("sleep");
                break;
        }
    }

    // Angry for 2.5 s, then it runs away from the cursor and ignores the mouse for 2 s more; can't be angered again for 10 s
    void Anger()
    {
        long now = Now;
        SetState("frenzy", 2500);
        fleeAt = now + 2500;
        noInteractUntil = now + 2500 + 2000;
        angryCooldownUntil = now + 10000;
    }

    // After being angry: run to the spot of the screen that is farthest from the cursor (best of a few random ones)
    void Flee()
    {
        GetCursorPos(out var c);
        var b = AreaFor(x + size / 2.0, y + size / 2.0);
        (double, double) best = (x, y);
        double bestD = -1;
        for (int i = 0; i < 12; i++)
        {
            double tx = b.Left + rnd.NextDouble() * b.Width, ty = b.Top + rnd.NextDouble() * b.Height;
            double d = Math.Sqrt(Math.Pow(tx + size / 2.0 - c.X, 2) + Math.Pow(ty + size / 2.0 - c.Y, 2));
            if (d > bestD) { bestD = d; best = (tx, ty); }
        }
        Target = best;
        state = "run";
    }

    // The cat bats the ball away from itself in a random direction, a bit upward
    void Kick()
    {
        if (ball == null) return;
        double ang = Math.Atan2(ball.Center.Y - (y + size / 2.0), ball.Center.X - (x + size / 2.0)) + (rnd.NextDouble() - 0.5) * 1.2;
        double sp = (700 + rnd.NextDouble() * 600) * S;
        double vx = Math.Cos(ang) * sp, vy = Math.Sin(ang) * sp - 500 * S;
        ball.Kick(vx, vy);
        lastKickAt = Now;
        kickSound = true;   // happiness grows gradually while playing (see Logic)
        Target = null;
        facingRight = vx > 0;
        SetState("play", (int)Cfg.Cycle);
        kickCooldown = Now + (long)Cfg.Cycle + 300;
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
            case "sleep": ToggleSleep(); break;
            case "bowl": ToggleProp(false); break;
            case "bed": ToggleProp(true); break;
            case "ball": ToggleBall(); break;
            case "meow": Audio.Play("meow"); break;
            case "hungry": hunger = 25; break;      // debug: set a need so the cat reacts to it
            case "sad": happiness = 25; break;
            case "tired": energy = 30; break;
            case "play": Target = null; SetState("play", (int)Cfg.Cycle); break;
            case "frenzy": Target = null; Anger(); break;
            case "pounce":
                GetCursorPos(out var p);
                Target = Clamp(p.X - size / 2.0, p.Y - size / 2.0);
                frame = 0;
                SetState("pounce", (int)Cfg.Cycle + 100);
                break;
        }
    }

    static long Now => Environment.TickCount64;

    bool kickSound;   // the next "play" is the ball being kicked, not a click

    void SetState(string s, int ms = 0)
    {
        state = s; lockUntil = Now + ms;
        switch (s)   // a sound for each interaction
        {
            case "play": if (!kickSound) Audio.Play("mew"); kickSound = false; break;   // kicking the ball is silent: the ball boings when it bounces
            case "frenzy": Audio.Play("hiss"); break;
            case "pounce": Audio.Play("chirp"); break;
            case "lick": Audio.Play("lick"); break;
            case "eat": Audio.Play("munch"); break;
        }
    }

    public void ApplyOnTop(bool on)
    {
        TopMost = on;
        foreach (PropWindow? p in new PropWindow?[] { bowl, bed, ball }) if (p != null) p.TopMost = on;
    }

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

    // Stats panel: the mouse must stay on the cat (an opaque pixel, not just its box) for HoverDelay;
    // it fades out shortly after the mouse leaves
    const int HoverDelay = 1500;
    // seconds from full to empty
    const double HungerSeconds = 450, HappinessSeconds = 300, EnergySeconds = 600;   // 7.5, 5 and 10 minutes

    void UpdateHover()
    {
        bool over = false;
        int lx = cursor.X - Left, ly = cursor.Y - Top;
        if (Visible && lx >= 0 && ly >= 0 && lx < size && ly < size) over = buf.GetPixel(lx, ly).A > 0;
        long now = Now;
        if (over)
        {
            hoverOffAt = 0;
            if (hoverSince == 0) hoverSince = now;
            if (now - hoverSince >= HoverDelay)
            {
                statsWin ??= new StatsWindow(S);
                statsWin.ShowAbove(new Rectangle(Left, Top, size, size), hunger, happiness, energy);
            }
        }
        else
        {
            if (hoverOffAt == 0) hoverOffAt = now;
            else if (now - hoverOffAt >= 350) { hoverSince = 0; statsWin?.HidePanel(); }   // short wobbles off the sprite don't reset the wait
        }
    }

    // Sprite faces right; flip it toward the mouse when standing still, but never in the middle of
    // sleeping or licking (it turns when the action is over: Animate() calls this too)
    void FaceMouse()
    {
        if (mouseNear && target == null && state != "sleep" && state != "lick" && state != "eat") facingRight = cursor.X > x + size / 2.0;
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
        if (DistTo(p) < Cfg.JumpRange * S && fastSince != 0 && now - fastSince >= 120 && now >= nextJumpAt && now >= lockUntil && now >= noInteractUntil && state != "sleep")
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
        if (Now < noInteractUntil) return;   // angry / running away: ignores the mouse
        hunger = Math.Min(100, hunger + 10);
        happiness = Math.Min(100, happiness + 15);
        long now = Now;
        while (clicks.Count > 0 && now - clicks.Peek() >= 2000) clicks.Dequeue();
        clicks.Enqueue(now);
        Target = null;
        if (clicks.Count >= 3 && now >= angryCooldownUntil) { Anger(); clicks.Clear(); }
        else SetState("play", (int)Cfg.Cycle);
    }

    // ---- loops ----------------------------------------------------------------------------------

    // Every animation takes the same time per cycle (frame duration = cycle / frames)
    void Animate()
    {
        Audio.Purr(state == "sleep");   // purring while asleep
        if (fleeAt != 0 && Now >= fleeAt) { fleeAt = 0; Flee(); }
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
        if (goal == Goal.Ball)
        {
            if (ball == null) { Target = null; state = "idle"; return; }
            target = GoalTarget(Goal.Ball);   // keep following the ball
            double bd = Math.Sqrt(Math.Pow(ball.Center.X - (x + size / 2.0), 2) + Math.Pow(ball.Center.Y - (y + size / 2.0), 2));
            if (bd < size * 0.6 && Now >= kickCooldown) { Kick(); return; }
        }
        if (target == null) return;
        long now = Now;
        double dt = now - lastMoveAt; lastMoveAt = now;
        double mult = state == "walk" ? 1 : state == "pounce" ? 3 : 2;
        double step = Cfg.WalkSpeed * mult / 16.0 * dt * S;    // speed is "px per 16ms" at 96 dpi
        double dx = target.Value.X - x, dy = target.Value.Y - y, dist = Math.Sqrt(dx * dx + dy * dy);
        if (dist <= step)
        {
            x = target.Value.X; y = target.Value.Y;
            var reached = goal;
            Target = null;
            if (state == "walk" || state == "run") state = "idle";
            Arrived(reached);
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
        if (TopMost && Visible && IsHandleCreated)
        {
            bowl?.Reassert(); bed?.Reassert(); ball?.Reassert();   // objects first, so the cat stays above them
            SetWindowPos(Handle, (IntPtr)(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);   // HWND_TOPMOST, NOSIZE|NOMOVE|NOACTIVATE
        }
        // each need goes from 100 to 0 in its own time (this runs once per second)
        hunger = Math.Max(0, hunger - 100.0 / HungerSeconds);
        happiness = Math.Max(0, happiness - 100.0 / HappinessSeconds);
        energy = Math.Max(0, energy - 100.0 / EnergySeconds);

        // Needs are restored gradually, per second: sleeping -> energy, eating -> hunger, playing with the ball -> happiness
        if (ball != null && (goal == Goal.Ball || Now - lastKickAt < 2500)) happiness = Math.Min(100, happiness + 2.5);

        if (state == "sleep")
        {
            energy = Math.Min(100, energy + 1.5);     // recover while asleep, wake up when rested
            Target = null; idleTimer = 0;
            if (energy >= 100 && !sleepManual) state = "idle";   // manual sleep lasts until woken up
        }
        else if (state == "eat")
        {
            hunger = Math.Min(100, hunger + 8);       // keeps eating until full
            Audio.Play("munch");
            Target = null; idleTimer = 0;
            if (hunger >= 100 || bowl == null) state = "idle";
        }
        else if (Now < lockUntil) { }                 // click / pounce / lick animation playing
        else if (energy < 20)
        {
            if (bed != null) { if (goal != Goal.Bed) GoTo(Goal.Bed); }   // too tired: go to the bed
            else { sleepManual = false; SetState("sleep"); Target = null; }
        }
        else if (target != null) { }                  // walking/running - controlled by Move()
        else
        {
            if (state is "pounce" or "lick" or "eat") state = "idle";
            idleTimer++;
            double r = rnd.NextDouble();
            long now = Now;
            if (rnd.NextDouble() < (hunger < 30 ? 1 / 15.0 : 1 / 50.0)) Audio.Play(hunger < 30 ? "meowfood" : "meow");   // an occasional meow, more when hungry
            // Use the objects according to needs: tired -> bed, hungry -> bowl, sad -> ball
            bool playing = now < chaseUntil && happiness < 90;   // a play session goes on until it is happy again
            if (bed != null && energy < 40) GoTo(Goal.Bed);
            else if (bowl != null && hunger < 40) GoTo(Goal.Bowl);
            else if (ball != null && (happiness < 50 || playing))
            {
                if (!playing) chaseUntil = now + 60000;   // at most a minute per session
                GoTo(Goal.Ball);
            }
            else if (r < Cfg.Wander)
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
