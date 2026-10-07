using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// Settings window. Controls are custom-painted (rounded cards, slider, pills) to look modern;
// it is created when opened and disposed when closed, so it costs no memory while closed.
sealed class SettingsForm : Form
{
    static readonly (string code, string name)[] Languages = { ("it", "Italiano"), ("en", "English") };

    // key, group, min, max, step, value formatter
    static readonly (string key, string group, double min, double max, double step, Func<double, string> fmt)[] Fields =
    {
        ("nearRange", "mouse", 50, 400, 10, v => v.ToString("0")),
        ("jumpRange", "mouse", 100, 1500, 50, v => v.ToString("0")),
        ("jumpSpeed", "mouse", 500, 6000, 100, v => v.ToString("0")),
        ("jumpCooldown", "mouse", 0, 30, 1, v => v.ToString("0")),
        ("wander", "movement", 0, 0.3, 0.01, v => Math.Round(v * 100).ToString("0")),
        ("walkSpeed", "movement", 1, 6, 0.5, v => v.ToString("0.#")),
        ("cycle", "movement", 600, 3000, 50, v => v.ToString("0")),
    };

    // sound credits: translation key of what it is used for, and who made it (the cat meows need none)
    static readonly (string key, string who)[] Credits =
    {
        ("credits.purr", "Kerzoven · CC0 · OpenGameArt.org"),
        ("credits.kitten", "Technopeasant (Baby Animals - Sounds Pack) · CC0 · OpenGameArt.org"),
        ("credits.munch", "StarNinjas (7 Eating Crunches) · CC0 · OpenGameArt.org"),
    };

    static readonly (string key, string group, double min, double max, double step, Func<double, string> fmt) VolumeField =
        ("volume", "audio", 0, 100, 5, v => v.ToString("0"));

    readonly float k;
    readonly Color bg, sideBg, card, text, muted, line, accent, accentSoft, track;
    readonly Font fBase, fBold, fTitle, fSmall, fBrand;
    readonly Icon appIcon;
    readonly Panel side = new BufferedPanel { Dock = DockStyle.Left };
    readonly Panel host = new BufferedPanel { Dock = DockStyle.Fill };
    readonly Panel view = new BufferedPanel();                 // scrolled content (moved up/down by the custom scrollbar)
    readonly Bar bar;
    int devClicks;
    bool focusName = true;
    System.Windows.Forms.Timer? statsT;
    string page = Environment.GetCommandLineArgs().Contains("--cat") ? "cat" : Environment.GetCommandLineArgs().Contains("--credits") ? "credits" : "general";

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);

    public SettingsForm(string startPage = "")
    {
        if (startPage != "") page = startPage;
        using (var gr = CreateGraphics()) k = gr.DpiX / 96f;
        bg = Theme.Bg; sideBg = Theme.SideBg; card = Theme.Card; text = Theme.Text; muted = Theme.Muted;
        line = Theme.Line; accent = Theme.Accent; accentSoft = Theme.AccentSoft; track = Theme.Track;
        fBase = new Font("Segoe UI", 9.5f); fBold = new Font("Segoe UI Semibold", 9.5f); fTitle = new Font("Segoe UI Semibold", 18f);
        fSmall = new Font("Segoe UI", 8.5f); fBrand = new Font("Segoe UI Semibold", 11f);
        using (var s = typeof(SettingsForm).Assembly.GetManifestResourceStream("icon.ico")!) appIcon = new Icon(s);

        Icon = appIcon;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Font = fBase;
        BackColor = bg;
        ClientSize = new Size(P(700), P(540));

        side.Width = P(182); side.BackColor = sideBg;
        side.Paint += (_, e) =>
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = RoundRect(new Rectangle(P(16), P(18), P(40), P(40)), P(12))) using (var br = new SolidBrush(accentSoft)) g.FillPath(br, path);
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.DrawIcon(appIcon, new Rectangle(P(22), P(24), P(28), P(28)));
            TextRenderer.DrawText(g, "Desktop Pet", fBrand, new Point(P(64), P(28)), text);
        };
        host.BackColor = bg; view.BackColor = bg;
        bar = new Bar(this) { Dock = DockStyle.Right, Width = P(14) };
        bar.Scrolled += () => view.Top = -bar.Value;
        host.Controls.Add(view);
        host.Controls.Add(bar);
        Controls.Add(host);
        Controls.Add(side);
        Cfg.LanguageChanged += Rebuild;
    }

    public void GoTo(string p)
    {
        if (p == "") return;
        page = p; bar.Value = 0;
        if (IsHandleCreated) Build();
    }

    // built once the real layout exists (host.ClientSize is only meaningful now)
    protected override void OnShown(EventArgs e) { base.OnShown(e); Build(); }

    int P(int v) => (int)Math.Round(v * k);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (bg.GetBrightness() < 0.5f) { int on = 1; DwmSetWindowAttribute(Handle, 20, ref on, 4); }   // dark title bar
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Cfg.LanguageChanged -= Rebuild;
            statsT?.Dispose();
            fBase.Dispose(); fBold.Dispose(); fTitle.Dispose(); fSmall.Dispose(); fBrand.Dispose(); appIcon.Dispose();
        }
        base.Dispose(disposing);
    }

    protected override void OnMouseWheel(MouseEventArgs e) { bar.Value -= (int)Math.Round(e.Delta * P(70) / 120.0); base.OnMouseWheel(e); }

    void Rebuild() { if (IsHandleCreated) BeginInvoke(Build); }

    // ---- layout ---------------------------------------------------------------------------------

    [DllImport("user32.dll")] static extern int SendMessage(IntPtr hwnd, int msg, int wparam, int lparam);

    // Rebuilding destroys and recreates every control: stop painting meanwhile, so the window doesn't flicker
    void Build()
    {
        const int WM_SETREDRAW = 0x000B;
        bool live = IsHandleCreated && view.IsHandleCreated && side.IsHandleCreated;
        if (live) { SendMessage(view.Handle, WM_SETREDRAW, 0, 0); SendMessage(side.Handle, WM_SETREDRAW, 0, 0); }
        try { BuildCore(); }
        finally
        {
            if (live)
            {
                SendMessage(view.Handle, WM_SETREDRAW, 1, 0); SendMessage(side.Handle, WM_SETREDRAW, 1, 0);
                view.Invalidate(true); side.Invalidate(true);
            }
        }
    }

    void BuildCore()
    {
        Text = Str.T("title");
        foreach (Control c in side.Controls.Cast<Control>().ToList()) c.Dispose();
        foreach (Control c in view.Controls.Cast<Control>().ToList()) c.Dispose();
        statsT?.Dispose(); statsT = null;
        int keepScroll = bar.Value;

        int ny = P(84);
        var nav = new List<(string, string)> { ("general", "nav.general"), ("cat", "nav.cat") };
        if (Cfg.Dev) nav.Add(("dev", "nav.dev"));
        foreach (var (id, key) in nav)
        {
            var pill = new Pill(this) { Text = Str.T(key), Active = page == id, Bounds = new Rectangle(P(12), ny, P(158), P(40)), Kind = PillKind.Nav };
            pill.Click += (_, _) => { page = id; bar.Value = 0; Build(); };
            side.Controls.Add(pill);
            ny += P(46);
        }

        int pad = P(28), w = host.ClientSize.Width - bar.Width - pad * 2;
        int y = pad;

        if (page == "general")
        {
            y = Title(Str.T("nav.general"), pad, y, w);

            // language
            var c = AddCard(pad, y, w);
            int cy = RowHeader(c, Str.T("language"), Str.T("language.hint"), P(16), P(12), w - P(32));
            int bx = P(16);
            foreach (var (code, name) in Languages)
            {
                var b = new Pill(this) { Text = name, Active = Cfg.Lang == code, Bounds = new Rectangle(bx, cy + P(4), P(110), P(32)), Kind = PillKind.Seg };
                b.Click += (_, _) => Cfg.SetLang(code);
                c.Controls.Add(b);
                bx += P(118);
            }
            c.Height = cy + P(52);
            y += c.Height + P(14);

            // preferences: always on top, start with Windows, volume
            var pc = AddCard(pad, y, w);
            int py = P(4);
            py = ToggleRow(pc, "onTop", py + P(12), w - P(32), Cfg.OnTop, v => Cfg.SetOnTop(v));
            pc.Controls.Add(new Panel { Bounds = new Rectangle(P(16), py, w - P(32), 1), BackColor = line });
            py = ToggleRow(pc, "startup", py + P(12), w - P(32), Startup.Enabled, v => { try { Startup.Set(v); } catch { } });
            pc.Controls.Add(new Panel { Bounds = new Rectangle(P(16), py, w - P(32), 1), BackColor = line });
            py = SliderRow(pc, VolumeField, py + P(12), w - P(32));
            pc.Height = py + P(4);
            y += pc.Height + P(18);

            view.Controls.Add(Lbl(Str.T("about"), fBold, muted, pad + P(2), y, w));
            y += P(28);
            var about = AddCard(pad, y, w);
            var ver = typeof(SettingsForm).Assembly.GetName().Version!;
            var verLbl = Lbl(Str.T("version"), fBold, text, P(16), P(14), w - P(200)); verLbl.BackColor = card; about.Controls.Add(verLbl);
            var val = Lbl($"{ver.Major}.{ver.Minor}.{ver.Build}", fBold, accent, P(16) + w - P(32) - P(180), P(14), P(180), ContentAlignment.TopRight);
            val.BackColor = card; about.Controls.Add(val);
            var note = Lbl("", fSmall, muted, P(16), P(40), w - P(32)); note.BackColor = card; about.Controls.Add(note);
            if (Cfg.Dev) note.Text = Str.T("dev.active");
            var creditsBtn = new Pill(this) { Text = Str.T("about.creditsBtn"), Kind = PillKind.Seg, Bounds = new Rectangle(P(16), P(68), P(130), P(32)) };
            creditsBtn.Click += (_, _) => { page = "credits"; bar.Value = 0; Build(); };
            about.Controls.Add(creditsBtn);
            about.Height = P(68) + P(32) + P(16);
            // Tap the version 9 times (like a cat's lives) to unlock developer mode
            val.Click += (_, _) =>
            {
                if (Cfg.Dev) return;
                devClicks++;
                if (devClicks >= 9) { Cfg.Dev = true; Cfg.Save(); devClicks = 0; page = "dev"; Build(); }
                else if (devClicks >= 4) note.Text = string.Format(Str.T("dev.left"), 9 - devClicks);
            };
        }
        else if (page == "credits")
        {
            y = Title(Str.T("credits.title"), pad, y, w);
            view.Controls.Add(Lbl(Str.T("credits.sounds"), fBold, muted, pad + P(2), y - P(6), w));
            y += P(22);
            var cc = AddCard(pad, y, w);
            int cy2 = P(4);
            bool first = true;
            foreach (var (key, who) in Credits)
            {
                if (!first) cc.Controls.Add(new Panel { Bounds = new Rectangle(P(16), cy2, w - P(32), 1), BackColor = line });
                first = false;
                cy2 = RowHeader(cc, Str.T(key), who, P(16), cy2 + P(12), w - P(32)) + P(8);
            }
            cc.Height = cy2 + P(4);
            y += cc.Height + P(14);
            var back = new Pill(this) { Text = Str.T("credits.back"), Bounds = new Rectangle(pad, y + P(6), P(150), P(34)), Kind = PillKind.Outline };
            back.Click += (_, _) => { page = "general"; bar.Value = 0; Build(); };
            view.Controls.Add(back);
        }
        else if (page == "dev")
        {
            y = Title(Str.T("nav.dev"), pad, y, w);
            view.Controls.Add(Lbl(Str.T("dev.intro"), fBase, muted, pad, y - P(6), w));
            y += P(26);
            view.Controls.Add(Lbl(Str.T("dev.actions"), fBold, muted, pad + P(2), y, w));
            y += P(28);
            var c = AddCard(pad, y, w);
            string[] acts = { "idle", "lick", "walk", "run", "sleep", "play", "pounce", "frenzy", "meow", "bowl", "bed", "ball", "hungry", "sad", "tired" };
            int gap = P(8), cols = 3, bw = (w - P(32) - gap * (cols - 1)) / cols, bh = P(38);
            for (int i = 0; i < acts.Length; i++)
            {
                string a2 = acts[i];
                var b = new Pill(this) { Text = Str.T("act." + a2), Kind = PillKind.Seg, Bounds = new Rectangle(P(16) + i % cols * (bw + gap), P(16) + i / cols * (bh + gap), bw, bh) };
                b.Click += (_, _) => PetWindow.Instance?.Trigger(a2);
                c.Controls.Add(b);
            }
            c.Height = P(16) * 2 + (acts.Length + cols - 1) / cols * (bh + gap) - gap;
            y += c.Height + P(14);

            view.Controls.Add(Lbl(Str.T("dev.state"), fBold, muted, pad + P(2), y, w));
            y += P(28);
            var sc = AddCard(pad, y, w);
            var stats = Lbl("", fBase, text, P(16), P(16), w - P(32)); stats.BackColor = card; sc.Controls.Add(stats);
            sc.Height = P(52);
            void Refresh()
            {
                if (PetWindow.Instance is { } pw)
                {
                    var st = pw.Stats;
                    stats.Text = string.Format(Str.T("dev.stats.fmt"), Math.Round(st.Hunger), Math.Round(st.Happiness), Math.Round(st.Energy), st.State);
                }
            }
            Refresh();
            statsT = new System.Windows.Forms.Timer { Interval = 500 };
            statsT.Tick += (_, _) => Refresh();
            statsT.Start();
            y += sc.Height + P(14);

            // behaviour tuning (kept here for development, not part of the normal settings)
            foreach (var group in new[] { "mouse", "movement" })
            {
                view.Controls.Add(Lbl(Str.T("group." + group), fBold, muted, pad + P(2), y, w));
                y += P(28);
                var tc = AddCard(pad, y, w);
                int ty = P(4);
                bool firstRow = true;
                foreach (var f in Fields.Where(f => f.group == group))
                {
                    if (!firstRow) { tc.Controls.Add(new Panel { Bounds = new Rectangle(P(16), ty, w - P(32), 1), BackColor = line }); }
                    firstRow = false;
                    ty = SliderRow(tc, f, ty + P(12), w - P(32));
                }
                tc.Height = ty + P(4);
                y += tc.Height + P(14);
            }
            var reset = new Pill(this) { Text = Str.T("reset"), Bounds = new Rectangle(pad, y + P(6), P(190), P(34)), Kind = PillKind.Outline };
            reset.Click += (_, _) => { Cfg.ResetCat(); Build(); };
            view.Controls.Add(reset);
            var off = new Pill(this) { Text = Str.T("dev.disable"), Bounds = new Rectangle(pad + P(202), y + P(6), P(240), P(34)), Kind = PillKind.Outline };
            off.Click += (_, _) => { Cfg.Dev = false; Cfg.Save(); page = "general"; Build(); };
            view.Controls.Add(off);
        }
        else
        {
            // the cat: its name
            y = Title(Str.T("nav.cat"), pad, y, w);
            var nc = AddCard(pad, y, w);
            int ny2 = RowHeader(nc, Str.T("name.label"), Str.T("name.hint"), P(16), P(12), w - P(32));
            var field = new Card(track, line, card, P(8)) { Bounds = new Rectangle(P(16), ny2 + P(4), w - P(32), P(34)) };
            var tb = new TextBox { Text = Cfg.Name, MaxLength = 20, BorderStyle = BorderStyle.None, BackColor = track, ForeColor = text, Font = fBase, Bounds = new Rectangle(P(10), P(8), w - P(32) - P(20), P(20)) };
            tb.TextChanged += (_, _) => { Cfg.Name = tb.Text.Trim(); Cfg.Save(); };
            field.Controls.Add(tb);
            nc.Controls.Add(field);
            nc.Height = ny2 + P(4) + P(34) + P(16);
            if (focusName) { ActiveControl = tb; focusName = false; }
        }

        int bottom = view.Controls.Cast<Control>().Max(c => c.Bottom) + pad;
        view.SetBounds(0, 0, host.ClientSize.Width - bar.Width, Math.Max(bottom, host.ClientSize.Height));
        bar.Value = keepScroll;
        bar.Setup(view.Height, host.ClientSize.Height);
    }

    int Title(string t, int x, int y, int w)
    {
        view.Controls.Add(Lbl(t, fTitle, text, x, y, w));
        return y + P(46);
    }

    Card AddCard(int x, int y, int w)
    {
        var c = new Card(card, line, bg, P(14)) { Bounds = new Rectangle(x, y, w, P(60)) };
        view.Controls.Add(c);
        return c;
    }

    Label Lbl(string t, Font f, Color col, int x, int y, int w, ContentAlignment align = ContentAlignment.TopLeft)
    {
        int h = TextRenderer.MeasureText(t, f, new Size(w, 0), TextFormatFlags.WordBreak).Height;
        return new Label { Text = t, Font = f, ForeColor = col, BackColor = Color.Transparent, Bounds = new Rectangle(x, y, w, Math.Max(h, P(16))), TextAlign = align, UseCompatibleTextRendering = false };
    }

    // label + hint inside a card; returns the y below them
    int RowHeader(Control parent, string label, string hint, int x, int y, int w)
    {
        var l = Lbl(label, fBold, text, x, y, w); l.BackColor = card; parent.Controls.Add(l);
        y += P(22);
        if (hint != "") { var h = Lbl(hint, fSmall, muted, x, y, w); h.BackColor = card; parent.Controls.Add(h); y += h.Height + P(4); }
        return y;
    }

    int SliderRow(Control parent, (string key, string group, double min, double max, double step, Func<double, string> fmt) f, int y, int w)
    {
        string unit = Str.T(f.key + ".unit");
        var val = Lbl("", fBold, accent, P(16) + w - P(150), y, P(150), ContentAlignment.TopRight);
        val.BackColor = card; parent.Controls.Add(val);
        int cy = RowHeader(parent, Str.T(f.key + ".label"), Str.T(f.key + ".hint"), P(16), y, w - P(160));
        void Show() => val.Text = $"{f.fmt(Cfg.V[f.key])} {unit}";
        var s = new Slider(k) { Min = f.min, Max = f.max, Step = f.step, Value = Cfg.V[f.key], Track = track, Accent = accent, Back = card, Bounds = new Rectangle(P(16), cy, w, P(26)) };
        s.Changed += () => { Cfg.V[f.key] = s.Value; Show(); };
        s.Committed += () => { Cfg.Save(); if (f.key == "volume") { Audio.Refresh(); Audio.Play("mew"); } };   // volume: let the user hear it
        Show();
        parent.Controls.Add(s);
        return cy + P(26) + P(10);
    }

    // label + hint on the left, an on/off switch on the right
    int ToggleRow(Control parent, string key, int y, int w, bool value, Action<bool> set)
    {
        int cy = RowHeader(parent, Str.T(key + ".label"), Str.T(key + ".hint"), P(16), y, w - P(70));
        var t = new Toggle(this) { On = value, Bounds = new Rectangle(P(16) + w - P(44), y + P(2), P(44), P(24)) };
        t.Changed += () => set(t.On);
        parent.Controls.Add(t);
        return Math.Max(cy, y + P(30)) + P(10);
    }

    sealed class BufferedPanel : Panel
    {
        public BufferedPanel() { DoubleBuffered = true; }
    }

    // ---- custom controls ------------------------------------------------------------------------

    static GraphicsPath RoundRect(Rectangle r, int rad)
    {
        var p = new GraphicsPath(); int d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    enum PillKind { Nav, Seg, Outline }

    sealed class Pill : Control
    {
        readonly SettingsForm f;
        bool hover;
        public bool Active;
        public PillKind Kind;
        public Pill(SettingsForm owner)
        {
            f = owner;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            Cursor = Cursors.Hand;
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            Color parentBg = Kind == PillKind.Nav ? f.sideBg : Kind == PillKind.Outline ? f.bg : f.card;
            g.Clear(parentBg);
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color fill = parentBg, fore = f.text;
            switch (Kind)
            {
                case PillKind.Nav: if (Active) fill = f.accentSoft; else if (hover) fill = f.bg; fore = Active ? f.accent : f.muted; break;
                case PillKind.Seg: fill = Active ? f.accent : hover ? f.line : f.track; fore = Active ? Color.White : f.text; break;
                case PillKind.Outline: fill = f.card; fore = hover ? f.accent : f.text; break;
            }
            using (var path = RoundRect(r, f.P(10)))
            {
                using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                if (Kind == PillKind.Outline) using (var pen = new Pen(hover ? f.accent : f.line)) g.DrawPath(pen, path);
            }
            if (Kind == PillKind.Nav && Active)
                using (var b = new SolidBrush(f.accent)) g.FillPath(b, RoundRect(new Rectangle(f.P(4), Height / 2 - f.P(9), f.P(3), f.P(18)), f.P(1)));
            var flags = TextFormatFlags.VerticalCenter | (Kind == PillKind.Nav ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter);
            var tr = Kind == PillKind.Nav ? new Rectangle(f.P(18), 0, Width - f.P(18), Height) : ClientRectangle;
            TextRenderer.DrawText(g, Text, f.fBold, tr, fore, flags);
        }
    }

    sealed class Card : Panel
    {
        readonly Color fill, border, back; readonly int rad;
        public Card(Color fill, Color border, Color back, int rad)
        {
            this.fill = fill; this.border = border; this.back = back; this.rad = rad;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(back);
            using var path = RoundRect(new Rectangle(0, 0, Width - 1, Height - 1), rad);
            using (var b = new SolidBrush(fill)) g.FillPath(b, path);
            using (var pen = new Pen(border)) g.DrawPath(pen, path);
        }
    }

    sealed class Slider : Control
    {
        readonly float k;
        bool dragging;
        public double Min, Max, Step, Value;
        public Color Track, Accent, Back;
        public event Action? Changed, Committed;
        public Slider(float scale)
        {
            k = scale;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            Cursor = Cursors.Hand;
        }
        int Pad => (int)(10 * k);
        void SetFromX(int mx)
        {
            double t = Math.Clamp((mx - Pad) / (double)(Width - 2 * Pad), 0, 1);
            double v = Math.Round((Min + t * (Max - Min) - Min) / Step) * Step + Min;
            v = Math.Round(Math.Clamp(v, Min, Max), 4);
            if (v == Value) return;
            Value = v; Invalidate(); Changed?.Invoke();
        }
        protected override void OnMouseDown(MouseEventArgs e) { dragging = true; Capture = true; SetFromX(e.X); base.OnMouseDown(e); }
        protected override void OnMouseMove(MouseEventArgs e) { if (dragging) SetFromX(e.X); base.OnMouseMove(e); }
        protected override void OnMouseUp(MouseEventArgs e) { if (dragging) { dragging = false; Capture = false; Committed?.Invoke(); } base.OnMouseUp(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Back);
            int th = (int)(6 * k), cy = Height / 2, x0 = Pad, x1 = Width - Pad;
            double t = (Value - Min) / (Max - Min);
            int tx = x0 + (int)(t * (x1 - x0));
            using (var b = new SolidBrush(Track)) g.FillPath(b, RoundRect(new Rectangle(x0, cy - th / 2, x1 - x0, th), th / 2));
            if (tx > x0) using (var b = new SolidBrush(Accent)) g.FillPath(b, RoundRect(new Rectangle(x0, cy - th / 2, tx - x0, th), th / 2));
            int r = (int)(9 * k), ri = (int)(4 * k);
            using (var b = new SolidBrush(Accent)) g.FillEllipse(b, tx - r, cy - r, r * 2, r * 2);
            using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, tx - ri, cy - ri, ri * 2, ri * 2);
        }
    }

    sealed class Toggle : Control
    {
        readonly SettingsForm f;
        public bool On;
        public event Action? Changed;
        public Toggle(SettingsForm owner)
        {
            f = owner;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            Cursor = Cursors.Hand;
        }
        protected override void OnClick(EventArgs e) { On = !On; Invalidate(); Changed?.Invoke(); base.OnClick(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(f.card);
            using (var path = RoundRect(new Rectangle(0, 0, Width - 1, Height - 1), Height / 2))
            using (var b = new SolidBrush(On ? f.accent : f.track)) g.FillPath(b, path);
            int d = Height - f.P(8), x = On ? Width - d - f.P(4) : f.P(4);
            using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, x, f.P(4), d, d);
        }
    }

    // Slim custom scrollbar (the system one clashes with the UI): rounded thumb, no arrows, grows on hover
    sealed class Bar : Control
    {
        readonly SettingsForm f;
        int value, content, viewport, dragOffset;
        bool hover, drag;
        public event Action? Scrolled;
        public Bar(SettingsForm owner)
        {
            f = owner;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
        }
        int Max => Math.Max(0, content - viewport);
        public int Value
        {
            get => value;
            set { int v = Math.Clamp(value, 0, Max); if (v == this.value) return; this.value = v; Invalidate(); Scrolled?.Invoke(); }
        }
        public void Setup(int contentHeight, int viewportHeight)
        {
            content = contentHeight; viewport = viewportHeight;
            Visible = content > viewport;
            value = Math.Clamp(value, 0, Max);
            Invalidate(); Scrolled?.Invoke();
        }
        Rectangle Thumb()
        {
            int h = Math.Max(f.P(40), (int)((double)viewport / content * Height));
            int y = Max == 0 ? 0 : (int)((double)value / Max * (Height - h));
            int w = hover || drag ? f.P(8) : f.P(5);
            return new Rectangle(Width - w - f.P(3), y, w, h);
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            var t = Thumb();
            if (e.Y >= t.Top && e.Y <= t.Bottom) { drag = true; Capture = true; dragOffset = e.Y - t.Top; }
            else Value += e.Y < t.Top ? -viewport : viewport;   // click on the track: page up/down
            Invalidate();
            base.OnMouseDown(e);
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (drag)
            {
                int h = Thumb().Height;
                Value = (int)((double)(e.Y - dragOffset) / Math.Max(1, Height - h) * Max);
            }
            base.OnMouseMove(e);
        }
        protected override void OnMouseUp(MouseEventArgs e) { drag = false; Capture = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(f.bg);
            if (Max == 0) return;
            var t = Thumb();
            using var path = RoundRect(t, t.Width / 2);
            using var b = new SolidBrush(hover || drag ? f.accent : f.track);
            g.FillPath(b, path);
        }
    }
}
