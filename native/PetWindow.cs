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
    const int WM_LBUTTONDOWN = 0x201, WM_LBUTTONUP = 0x202, WM_MOUSEACTIVATE = 0x21;

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
        ["pet"] = (6, 4, false),     // being stroked: lies down like when asleep, with hearts
        ["play"] = (7, 6, false),    // click: plays with the mouse
        ["pounce"] = (8, 7, true),   // fast mouse nearby: one jump
        ["frenzy"] = (9, 8, false),  // too many clicks
    };

    // Every cat is one of these windows (up to two)
    public static readonly List<PetWindow> All = new();
    public static PetWindow? Find(int index) => All.FirstOrDefault(c => c.cat.Index == index);

    readonly CatProfile cat;     // this cat's name, look, character, needs and objects
    Trait Traits => cat.Trait;   // its character: how lively, hungry, lazy, moody and talkative it is

    enum Goal { None, Bowl, Bed, Ball, Cat }

    Bitmap sheet;
    readonly Bitmap buf;
    readonly LayeredSurface surface;
    readonly Font?[] zFonts = new Font?[8];   // the floating z letters of the sleep animation
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
    long hoverOffAt, hoverSince, lastMouseMoveAt;   // lastMouseMoveAt: when the mouse last really moved (the stats panel wants it still)
    Native.POINT stillRef;
    long lockUntil, nextJumpAt, lastCursorAt, lastMoveAt, fastSince;
    readonly Queue<long> clicks = new();
    // The objects: this cat's own, or (with "shared objects") the first cat's, which every cat then uses
    TaskbarProp? ownBowl, ownBed;
    BallWindow? ownBall;
    PetWindow Owner => Cfg.ShareObjects ? All.FirstOrDefault(c => c.cat == Cfg.Cats[0]) ?? this : this;
    TaskbarProp? bowl { get => Owner.ownBowl; set => Owner.ownBowl = value; }
    TaskbarProp? bed { get => Owner.ownBed; set => Owner.ownBed = value; }
    BallWindow? ball { get => Owner.ownBall; set => Owner.ownBall = value; }

    // Every object first, then every cat: whatever the number of cats and objects, the cats are in front
    public static void RaiseAll()
    {
        foreach (var c in All) { c.ownBowl?.Reassert(); c.ownBed?.Reassert(); c.ownBall?.Reassert(); }
        foreach (var c in All)
            if (c.TopMost && c.Visible && c.IsHandleCreated)
                SetWindowPos(c.Handle, (IntPtr)(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);   // HWND_TOPMOST, NOSIZE|NOMOVE|NOACTIVATE
    }

    // Sharing was turned on: the other cats' own objects go away
    public static void ApplyShare()
    {
        if (!Cfg.ShareObjects) return;
        foreach (var c in All.ToList())
            if (c.Owner != c) { c.ownBowl?.Close(); c.ownBed?.Close(); c.ownBall?.Close(); }
    }

    // Cats meeting: one walks to the other, then they groom each other (one licks, the other lies purring) or fight
    PetWindow? partner;
    string social = "", socialKind = "";   // social: "" | approach (walking to the partner) | wait (being approached) | act
    bool giver;
    long socialUntil, socialDeadline;
    double socialSide;
    (double X, double Y)? fleeFrom;
    Goal goal;                    // what the cat is walking to (cleared whenever it is sent elsewhere)
    bool keepGoal;
    long chaseUntil, kickCooldown, lastKickAt;
    int saveTick;
    bool pressed, petting;          // mouse button held on the cat / being stroked
    long pressAt, lastSlowMoveAt;
    Native.POINT pressPt;
    Bitmap[]? hearts;
    ReminderWindow? reminder;
    long remindHideAt, nextRemindAt;   // the reminder is shown for 5 s, then again every 30 s while a need is low
    int remindMask;
    bool shuttingDown;   // the app is quitting: closing the objects must not erase their saved state
    long noInteractUntil, fleeAt, angryCooldownUntil;   // anger: the cat hisses, then runs away and can't be touched for a bit
    Native.POINT cursor;

    (double X, double Y)? Target
    {
        get => target;
        set { target = value; if (!keepGoal) goal = Goal.None; if (value != null && !moveT.Enabled) { lastMoveAt = Environment.TickCount64; moveT.Start(); } else if (value == null) moveT.Stop(); }
    }

    public PetWindow(CatProfile profile)
    {
        cat = profile;
        All.Add(this);
        hunger = cat.Hunger; happiness = cat.Happiness; energy = cat.Energy;   // where it was left
        using (var gr = Graphics.FromHwnd(IntPtr.Zero)) S = gr.DpiX / 96f;
        size = Cell * Math.Max(1, (int)Math.Round(4 * S));

        sheet = CatSprite.BuildSheet(cat);
        Cfg.ProfileChanged += OnProfileChanged;
        surface = new LayeredSurface(size, size);
        buf = surface.Bitmap;
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
        x = wa.Left + wa.Width / 2 - size / 2 + (cat.Index % 2 == 0 ? -1 : 1) * (0.7 + 0.5 * (cat.Index / 2)) * size; y = wa.Bottom - size;   // the two cats start side by side
        Location = new Point((int)x, (int)y);

        animT.Tick += (_, _) => Animate();
        moveT.Tick += (_, _) => MoveStep();
        logicT.Tick += (_, _) => Logic();
        cursorT.Tick += (_, _) => PollCursor();
        animT.Interval = 100;
        GetCursorPos(out cursor);   // start from the real position (not 0,0, which would look like a huge jump)
        lastCursorAt = Now;
    }

    // The owner changed this cat's colours (or its objects'): redraw
    void OnProfileChanged(CatProfile p)
    {
        if (p != cat) return;
        var old = sheet;
        sheet = CatSprite.BuildSheet(cat);
        old.Dispose();
        bowl?.Recolor(); bed?.Recolor(); ball?.Recolor();
        if (IsHandleCreated) Redraw();
    }

    // A sound with this cat's voice
    void Say(string name) => Audio.Play(name, 1, cat.Index, Traits.Pitch);

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
        if (m.Msg == WM_LBUTTONDOWN)
        {
            pressed = true; pressAt = Now; lastSlowMoveAt = 0;
            statsWin?.HidePanel(); hoverSince = 0;   // pressing/stroking: no stats panel
            GetCursorPos(out pressPt);
            Capture = true;   // we also get the release when the mouse has left the cat
        }
        else if (m.Msg == WM_LBUTTONUP)
        {
            Capture = false;
            bool wasPetting = petting;
            EndPetting();
            if (pressed && !wasPetting && Now - pressAt < 500) OnClickPet();   // a short press is a click; holding and stroking is not
            pressed = false;
        }
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
    public void ToggleProp(bool isBed, int? left = null)
    {
        var existing = isBed ? bed : bowl;
        if (existing != null) { existing.Close(); return; }
        var o = Owner;
        var p = new TaskbarProp(isBed, Unit, o.cat);
        var scr = Screens.WorkAreaAt((int)(x + size / 2.0), (int)(y + size / 2.0));
        if (left is int l) p.PlaceNear(l + p.Width / 2, Screens.WorkAreaAt(l + p.Width / 2, Screen.PrimaryScreen!.Bounds.Bottom - 5));   // restored position
        else p.PlaceNear((int)(x + size / 2.0 + (isBed ? -1.4 : 1.2) * size), scr);
        p.FormClosed += (_, _) =>
        {
            if (isBed) { if (o.ownBed == p) o.ownBed = null; } else if (o.ownBowl == p) o.ownBowl = null;
            if (goal == (isBed ? Goal.Bed : Goal.Bowl)) { Target = null; state = "idle"; }
            if (!shuttingDown) SaveObjects();
        };
        if (isBed) bed = p; else bowl = p;
        p.Show();
        RaiseAll();
        p.Placed += SaveObjects;
        p.Placed += RaiseAll;
        SaveObjects();
    }

    public void ToggleBall(int? px = null, int? py = null)
    {
        if (ball != null) { ball.Close(); return; }
        var o = Owner;
        var b = new BallWindow(Unit, S, o.cat);
        b.Bounced += loudness => Audio.Play("boing", loudness, cat.Index);
        b.FormClosed += (_, _) =>
        {
            if (o.ownBall == b) o.ownBall = null;
            if (goal == Goal.Ball) { Target = null; state = "idle"; }
            chaseUntil = 0;
            if (!shuttingDown) SaveObjects();
        };
        ball = b;
        if (px is int bx && py is int by) b.Restore(bx, by);   // where it was last time
        else b.Drop((int)(x + size / 2.0), (int)y);              // falls from the cat
        b.Show();
        RaiseAll();
        b.Placed += SaveObjects;
        b.Placed += RaiseAll;
        SaveObjects();
    }

    // The summoned objects (and where they are) are remembered between runs; the ball keeps the place where it
    // last came to rest, even if the cat moved it.
    // The needs are written every 10 s (and on exit), so a kill loses at most a few seconds
    void SaveStats()
    {
        cat.Hunger = hunger; cat.Happiness = happiness; cat.Energy = energy;
        Cfg.Save();
    }

    public void SaveObjects()
    {
        cat.Hunger = hunger; cat.Happiness = happiness; cat.Energy = energy;
        if (Owner != this) { Cfg.Save(); return; }   // the shared objects are saved with the first cat
        cat.Bowl = bowl == null ? "" : bowl.Left.ToString();
        cat.Bed = bed == null ? "" : bed.Left.ToString();
        cat.Ball = ball == null ? "" : $"{ball.Left},{ball.Top}";
        Cfg.Save();
    }

    public void PrepareExit() { shuttingDown = true; SaveObjects(); }

    // The cat is turned off (the second one can be): remember everything, then close it and its objects
    public void Shutdown()
    {
        SaveObjects();
        shuttingDown = true;
        Cfg.ProfileChanged -= OnProfileChanged;
        animT.Stop(); moveT.Stop(); logicT.Stop(); cursorT.Stop();
        Audio.Purr(cat.Index, false);
        EndSocial();
        ownBowl?.Close(); ownBed?.Close(); ownBall?.Close(); statsWin?.Close(); reminder?.Close();
        All.Remove(this);
        Close();
    }

    public void RestoreObjects()
    {
        if (Owner != this) return;   // shared: the first cat restores them
        // read everything first: spawning an object saves the state, which would erase the ones not restored yet
        bool hasBowl = int.TryParse(cat.Bowl, out int bx), hasBed = int.TryParse(cat.Bed, out int dx);
        var xy = cat.Ball.Split(',');
        bool hasBall = xy.Length == 2 && int.TryParse(xy[0], out int px0) && int.TryParse(xy[1], out int py0);
        int px = hasBall ? int.Parse(xy[0]) : 0, py = hasBall ? int.Parse(xy[1]) : 0;
        if (hasBowl) ToggleProp(false, bx);
        if (hasBed) ToggleProp(true, dx);
        if (hasBall) ToggleBall(px, py);
    }

    (double, double)? GoalTarget(Goal gl)
    {
        switch (gl)
        {
            case Goal.Bowl when bowl != null:   // stand left of the bowl, on the taskbar
                return Clamp(bowl.Center.X - size, Screens.WorkAreaAt(bowl.Center.X, bowl.Center.Y).Bottom - size);
            case Goal.Bed when bed != null:     // lie "inside" the bed: feet overlap its back half
                return Clamp(bed.Center.X - size / 2.0, bed.Top + bed.Height * 0.55 - size);
            case Goal.Ball when ball != null:
                return Clamp(ball.Center.X - size / 2.0, ball.Center.Y - size / 2.0);
            case Goal.Cat when partner != null:   // right next to the other cat
                return Clamp(partner.x + socialSide * size * 0.55, partner.y);
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
            case Goal.Cat:
                BeginInteraction();
                break;
        }
    }

    // Angry for 2.5 s, then it runs away from the cursor and ignores the mouse for 2 s more; can't be angered again for 10 s
    void Anger(int ms = 2500)
    {
        long now = Now;
        happiness = Math.Max(0, happiness - 20);   // being angered makes it unhappy...
        energy = Math.Max(0, energy - 5);          // ...and wears it out
        SetState("frenzy", ms);
        fleeAt = now + ms;
        noInteractUntil = now + ms + 2000;
        angryCooldownUntil = now + 10000;
    }

    // After being angry: run to the spot of the screen that is farthest from the cursor (best of a few random ones)
    void Flee()
    {
        GetCursorPos(out var c);
        if (fleeFrom is { } ff) { c = new Native.POINT((int)ff.X, (int)ff.Y); fleeFrom = null; }   // after a fight: away from the other cat
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

    // ---- cats together ----------------------------------------------------------------------------

    bool CanSocialize => social == "" && !shuttingDown && (state is "idle" or "walk" or "run") && Now >= lockUntil && Now >= noInteractUntil && !pressed && !petting && energy >= 20;

    PetWindow? FindPartner()
    {
        var here = Screens.WorkAreaAt((int)(x + size / 2.0), (int)(y + size / 2.0));
        var near = All.Where(c => c != this && c.CanSocialize && Screens.WorkAreaAt((int)(c.x + size / 2.0), (int)(c.y + size / 2.0)) == here).ToList();
        return near.Count == 0 ? null : near[rnd.Next(near.Count)];
    }

    void StartSocial(PetWindow other, string kind)
    {
        social = "approach"; socialKind = kind; partner = other; giver = true;
        socialDeadline = Now + 40000;
        socialSide = x < other.x ? -1 : 1;   // it stops on its own side of the other cat
        other.social = "wait"; other.socialKind = kind; other.partner = this; other.giver = false; other.socialDeadline = socialDeadline;
        other.Target = null; other.state = "idle";
        GoTo(Goal.Cat);
        state = kind == "fight" ? "run" : "walk";
    }

    // The walker has reached the other cat
    void BeginInteraction()
    {
        var p = partner;
        if (p == null) { EndSocial(); return; }
        facingRight = p.x > x; p.facingRight = x > p.x;
        if (socialKind == "fight")
        {
            fleeFrom = (p.x + size / 2.0, p.y + size / 2.0); p.fleeFrom = (x + size / 2.0, y + size / 2.0);
            social = ""; p.social = ""; partner = null; p.partner = null;
            Anger(FightMs); p.Anger(FightMs);   // both hiss and scuffle for a while, then run away from each other
            fightUntil = p.fightUntil = Now + FightMs; nextHissAt = p.nextHissAt = Now + 1600;
            return;
        }
        social = "act"; p.social = "act";
        socialUntil = p.socialUntil = Now + 5000;
        SetState("lick", 5000);   // this one licks...
        p.Target = null; p.sleepManual = false;
        p.SetState("pet", 5000);  // ...the other lies down and purrs, with hearts
    }

    const int FightMs = 8000;   // how long two cats fight
    long fightUntil, nextHissAt;

    void SocialTick()
    {
        var p = partner;
        if (p == null || p.shuttingDown || !All.Contains(p) || p.partner != this || Now > socialDeadline + 8000) { EndSocial(); return; }
        idleTimer = 0;
        switch (social)
        {
            case "approach": if (target == null && goal != Goal.Cat) { GoTo(Goal.Cat); state = socialKind == "fight" ? "run" : "walk"; } break;
            case "wait": Target = null; if (state != "idle") state = "idle"; break;
            case "act":
                happiness = Math.Min(100, happiness + (giver ? 2 : 4));
                Target = null;
                if (giver && Now >= socialUntil) EndSocial();
                break;
        }
    }

    void EndSocial()
    {
        var p = partner;
        partner = null;
        if (social != "")
        {
            if (goal == Goal.Cat) Target = null;
            if (state is "pet" or "lick" or "walk" or "run") { state = "idle"; lockUntil = 0; }
        }
        social = "";
        if (p != null && p.partner == this) p.EndSocial();
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
        energy = Math.Max(0, energy - 2);   // every kick of the ball costs a bit of energy
        hunger = Math.Max(0, hunger - 0.5);
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
            case "meow": Say("meow"); break;
            case "pet": Target = null; SetState("pet", 4000); break;   // debug: the stroking pose for 4 s
            case "hungry": hunger = 25; break;      // debug: set a need so the cat reacts to it
            case "sad": happiness = 25; break;
            case "tired": energy = 30; break;
            case "play": Target = null; SetState("play", (int)Cfg.Cycle); break;
            case "frenzy": Target = null; Anger(); break;
            case "groom": case "fight": if (FindPartner() is { } other) StartSocial(other, what); break;
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
            case "play": if (!kickSound) Say("mew"); kickSound = false; break;   // kicking the ball is silent: the ball boings when it bounces
            case "frenzy": Say("hiss"); break;
            case "pounce": Say("chirp"); break;
            case "lick": Say("lick"); break;
            case "eat": Say("munch"); break;
        }
    }

    public void ApplyOnTop(bool on)
    {
        TopMost = on;
        foreach (PropWindow? p in new PropWindow?[] { ownBowl, ownBed, ownBall }) if (p != null) p.TopMost = on;
        if (reminder != null) reminder.TopMost = on;
    }

    // ---- geometry -------------------------------------------------------------------------------

    Rectangle AreaFor(double px, double py)
    {
        var wa = Screens.WorkAreaAt((int)px, (int)py);
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

    // Thought bubble with the bowl / bed / ball when the cat is hungry / tired / sad (it hides while the stats panel is up or the cat sleeps)
    void UpdateReminder()
    {
        long now = Now;
        int mask = (hunger < 40 ? ReminderWindow.Hungry : 0) | (energy < 40 ? ReminderWindow.Tired : 0) | (happiness < 50 ? ReminderWindow.Sad : 0);
        bool showing = remindHideAt != 0;
        var rect = new Rectangle(Left, Top, size, size);
        if (mask == 0 || state == "sleep" || statsWin is { Visible: true })
        {
            if (showing) { reminder?.HideBubble(); remindHideAt = 0; }
            if (mask == 0) nextRemindAt = 0;   // the next time a need gets low it shows right away
            return;
        }
        if (showing)
        {
            if (now >= remindHideAt) { reminder?.HideBubble(); remindHideAt = 0; nextRemindAt = now + 30000; }
            else if (mask != remindMask) { remindMask = mask; reminder?.ShowAbove(rect, mask); }
            return;
        }
        if (now < nextRemindAt) return;
        reminder ??= new ReminderWindow(S, cat);
        remindMask = mask;
        reminder.ShowAbove(rect, mask);
        remindHideAt = now + 5000;
    }

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
            // the panel only shows up if the mouse rests on the cat (not while stroking or pressing)
            if (!pressed && !petting && now - Math.Max(hoverSince, lastMouseMoveAt) >= HoverDelay)
            {
                statsWin ??= new StatsWindow(S, cat);
                if (remindHideAt != 0) { reminder?.HideBubble(); remindHideAt = 0; nextRemindAt = now + 30000; }   // the panel takes the bubble's place
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
        if (mouseNear && target == null && state != "sleep" && state != "pet" && state != "lick" && state != "eat") facingRight = cursor.X > x + size / 2.0;
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
        if (Math.Abs(p.X - stillRef.X) + Math.Abs(p.Y - stillRef.Y) > 3 * S) { lastMouseMoveAt = now; stillRef = p; }
        UpdateNear();

        FaceMouse();
        UpdateHover();

        if (pressed) { TrackPetting(p, speed, now); return; }   // button held: stroking, not a mouse "flick"

        // Sustained fast mouse movement near the cat (a quick flick, not a normal move): jump to the mouse,
        // then wait for the cooldown
        if (speed < Cfg.JumpSpeed * S) fastSince = 0;
        else if (fastSince == 0) fastSince = now;
        if (DistTo(p) < Cfg.JumpRange * S && fastSince != 0 && now - fastSince >= 120 && now >= nextJumpAt && now >= lockUntil && now >= noInteractUntil && state != "sleep")
        {
            Target = Clamp(p.X - size / 2.0, p.Y - size / 2.0);
            frame = 0;
            SetState("pounce", (int)Cfg.Cycle + 100);
            energy = Math.Max(0, energy - 3);        // chasing the mouse is tiring, but fun
            hunger = Math.Max(0, hunger - 1);
            happiness = Math.Min(100, happiness + 4);
            nextJumpAt = now + (long)(Cfg.JumpCooldown * 1000);
            fastSince = 0;
        }
    }

    // Hold the button on the cat and move the mouse slowly: it lies down, purrs and floats hearts, and gets happier
    void TrackPetting(Native.POINT p, double speed, long now)
    {
        double fromPress = Math.Sqrt(Math.Pow(p.X - pressPt.X, 2) + Math.Pow(p.Y - pressPt.Y, 2));
        if (fromPress > 6 * S && speed >= 15 * S && speed <= 700 * S) lastSlowMoveAt = now;
        UpdatePetting(now);
    }

    void UpdatePetting(long now)
    {
        if (!pressed) return;
        if (!petting)
        {
            if (now - pressAt >= 250 && now - lastSlowMoveAt < 400 && now >= noInteractUntil) StartPetting();
        }
        else if (now - lastSlowMoveAt > 1500) EndPetting();   // the hand stopped: it settles down
    }

    void StartPetting()
    {
        if (social != "") EndSocial();
        petting = true;
        Target = null;
        sleepManual = false;
        SetState("pet");
    }

    void EndPetting()
    {
        if (!petting) return;
        petting = false;
        if (state == "pet") SetState("idle");
    }

    void OnClickPet()
    {
        if (Now < noInteractUntil) return;   // angry / running away: ignores the mouse
        if (social != "") EndSocial();
        // every click tires the cat out, whether it plays or not (a little hungrier, a little happier)
        energy = Math.Max(0, energy - 4);
        hunger = Math.Max(0, hunger - 1.5);
        happiness = Math.Min(100, happiness + 6);
        long now = Now;
        while (clicks.Count > 0 && now - clicks.Peek() >= 2000) clicks.Dequeue();
        clicks.Enqueue(now);
        Target = null;
        if (clicks.Count >= 3 && now >= angryCooldownUntil) { Anger(); clicks.Clear(); }
        else SetState("play", (int)Cfg.Cycle);
    }

    // ---- loops ----------------------------------------------------------------------------------

    // Every animation takes the same time per cycle (frame duration = cycle / frames)
    // assigning Interval restarts the timer: only do it when the value really changes
    void SetAnimInterval(int ms) { if (animT.Interval != ms) animT.Interval = ms; }

    void Animate()
    {
        Audio.Purr(cat.Index, state == "sleep" || state == "pet", Traits.Pitch);   // purring while asleep or being stroked
        UpdatePetting(Now);
        if (fleeAt != 0 && Now >= fleeAt) { fleeAt = 0; Flee(); }
        UpdateNear();   // the cat may have walked toward/away from a still mouse
        FaceMouse();
        UpdateHover();
        string name = state == "idle" && mouseNear ? "near" : state;
        if (currentAnim != name) { frame = 0; currentAnim = name; }
        var a = Anims[name];
        Redraw();
        if (name is "sleep" or "pet")
        {
            // redraw 2x per frame so the floating z letters move smoothly
            SetAnimInterval(Math.Max(15, (int)(Cfg.Cycle / a.frames / 2)));
            if (++sleepTick % 2 != 0) return;
        }
        else SetAnimInterval(Math.Max(15, (int)(Cfg.Cycle / a.frames)));
        frame = a.once ? Math.Min(frame + 1, a.frames - 1) : (frame + 1) % a.frames;
    }

    void MoveStep()
    {
        if (goal == Goal.Cat)
        {
            if (partner == null) { Target = null; state = "idle"; return; }
            target = GoalTarget(Goal.Cat);
        }
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
        double step = Cfg.WalkSpeed * Math.Sqrt(Traits.Activity) * mult / 16.0 * dt * S;    // speed is "px per 16ms" at 96 dpi
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
        if (remindHideAt != 0) reminder?.ShowAbove(new Rectangle(Left, Top, size, size), remindMask);   // ...and the bubble
    }

    void Logic()
    {
        // Other windows (taskbar, Start menu, other topmost apps) can push us down: re-assert every second
        if (All.Count > 0 && All[0] == this) RaiseAll();
        if (++saveTick % 10 == 0) SaveStats();
        UpdateReminder();
        // each need goes from 100 to 0 in its own time (this runs once per second)
        hunger = Math.Max(0, hunger - 100.0 / HungerSeconds * Traits.Appetite);
        happiness = Math.Max(0, happiness - 100.0 / HappinessSeconds * Traits.Mood);
        energy = Math.Max(0, energy - 100.0 / EnergySeconds * Traits.Laziness);

        // Moving around costs extra, per second: running (also chasing, fleeing, jumping) tires it and makes it hungry faster
        (double e, double h) cost = state switch { "run" => (0.45, 0.3), "pounce" => (0.6, 0.35), "walk" => (0.12, 0.08), _ => (0, 0) };
        energy = Math.Max(0, energy - cost.e);
        hunger = Math.Max(0, hunger - cost.h);

        // Needs are restored gradually, per second: sleeping -> energy, eating -> hunger, playing with the ball -> happiness
        if (ball != null && (goal == Goal.Ball || Now - lastKickAt < 2500)) happiness = Math.Min(100, happiness + 2.5);

        if (social != "") SocialTick();
        if (Now < fightUntil && state == "frenzy" && Now >= nextHissAt) { Say("hiss"); nextHissAt = Now + 1400 + rnd.Next(900); }
        else if (state == "pet")
        {
            if (!petting && Now >= lockUntil) state = "idle";   // (the debug pose ends by itself)
            else { happiness = Math.Min(100, happiness + 4); Target = null; idleTimer = 0; }   // petting cheers it up
        }
        else if (state == "sleep")
        {
            energy = Math.Min(100, energy + 1.5);     // recover while asleep, wake up when rested
            Target = null; idleTimer = 0;
            if (energy >= 100 && !sleepManual) state = "idle";   // manual sleep lasts until woken up
        }
        else if (state == "eat")
        {
            hunger = Math.Min(100, hunger + 8);       // keeps eating until full
            Say("munch");
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
            if (rnd.NextDouble() < (hunger < 30 ? 1 / 15.0 : 1 / 50.0) * Traits.Chatty) Say(hunger < 30 ? "meowfood" : "meow");   // an occasional meow, more when hungry
            // Use the objects according to needs: tired -> bed, hungry -> bowl, sad -> ball
            bool playing = now < chaseUntil && happiness < 90;   // a play session goes on until it is happy again
            if (bed != null && energy < 40) GoTo(Goal.Bed);
            else if (bowl != null && hunger < 40) GoTo(Goal.Bowl);
            else if (ball != null && (happiness < 50 || playing))
            {
                if (!playing) chaseUntil = now + 60000;   // at most a minute per session
                GoTo(Goal.Ball);
            }
            else if (Traits.Needy > 0 && !mouseNear && now - lastMouseMoveAt < 30000 && rnd.NextDouble() < 0.03)
            {
                Target = (cursor.X - size / 2.0, y);   // comes to the mouse, calling
                state = "walk"; idleTimer = 0; Say("meow");
            }
            else if (rnd.NextDouble() < 0.012 * Traits.Activity && FindPartner() is { } mate)
                StartSocial(mate, rnd.NextDouble() < (happiness < 40 || mate.happiness < 40 ? 0.6 : 0.25) ? "fight" : "groom");
            else if (r < Cfg.Wander * Traits.Activity)
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

    // Three hearts rising above the head and fading out (same motion as the z's)
    void DrawHearts()
    {
        hearts ??= new[] { 2, 3, 4 }.Select(k => PixelArt.Render(PixelArt.HeartArt, PixelArt.HeartPal, Math.Max(1, (int)Math.Round(k * S / 1.25)))).ToArray();
        long t = Environment.TickCount64 % 2400;
        g.CompositingMode = CompositingMode.SourceOver;
        for (int i = 0; i < 3; i++)
        {
            double p = (t / 2400.0 + i / 3.0) % 1.0;
            var bmp = hearts[Math.Clamp((int)(p * 3), 0, 2)];
            float px = (float)(size * (0.60 + 0.10 * Math.Sin(p * 6 + i)) - bmp.Width / 2.0);
            if (!facingRight) px = size - px - bmp.Width;
            float py = (float)(size * (0.62 - 0.34 * p));
            var cm = new ColorMatrix { Matrix33 = (float)Math.Clamp(Math.Sin(Math.PI * p), 0, 1) };
            using var ia = new ImageAttributes();
            ia.SetColorMatrix(cm);
            g.DrawImage(bmp, new Rectangle((int)px, (int)py, bmp.Width, bmp.Height), 0, 0, bmp.Width, bmp.Height, GraphicsUnit.Pixel, ia);
        }
        g.CompositingMode = CompositingMode.SourceCopy;
    }

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
            int fi = Math.Clamp((int)(p * 7), 0, 7);
            var font = zFonts[fi] ??= new Font("Segoe UI", (float)((7 + 6 * fi / 7.0) * S), FontStyle.Bold, GraphicsUnit.Point);
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
        else if (currentAnim == "pet") DrawHearts();

        surface.Push(Handle, Left, Top);
    }
}
