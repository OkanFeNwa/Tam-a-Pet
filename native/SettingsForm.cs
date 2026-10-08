using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// Settings window. Controls are custom-painted (rounded cards, slider, pills) to look modern;
// it is created when opened and disposed when closed, so it costs no memory while closed.
sealed partial class SettingsForm : Form
{
    static readonly (string code, string name)[] Languages = { ("en", "English"), ("it", "Italiano"), ("fr", "Français"), ("es", "Español") };

    // sound credits: translation key of what it is used for, and who made it (the cat meows need none)
    static readonly (string key, string who)[] Credits =
    {
        ("credits.purr", "Kerzoven · CC0 · OpenGameArt.org"),
        ("credits.kitten", "Technopeasant (Baby Animals - Sounds Pack) · CC0 · OpenGameArt.org"),
        ("credits.munch", "StarNinjas (7 Eating Crunches) · CC0 · OpenGameArt.org"),
    };

    static readonly Color[] FurPresets =
    {
        Color.FromArgb(0xE0, 0xE0, 0xE0), Color.FromArgb(0xF0, 0xE2, 0xC2), Color.FromArgb(0xB5, 0xB5, 0xB5), Color.FromArgb(0x55, 0x56, 0x5C),
        Color.FromArgb(0x2A, 0x2A, 0x2E), Color.FromArgb(0xE8, 0x91, 0x3C), Color.FromArgb(0x8B, 0x5A, 0x3C),
    };
    static readonly Color[] NosePresets =
    {
        Color.FromArgb(202, 113, 159), Color.FromArgb(0xFF, 0x96, 0xB4), Color.FromArgb(0xE0, 0x52, 0x5A), Color.FromArgb(0xE8, 0x91, 0x3C),
        Color.FromArgb(0x8B, 0x5A, 0x3C), Color.FromArgb(0x2A, 0x2A, 0x2E), Color.FromArgb(0xF0, 0xE2, 0xC2),
    };
    static readonly Color[] EarPresets =
    {
        Color.FromArgb(154, 135, 126), Color.FromArgb(202, 113, 159), Color.FromArgb(0xFF, 0x96, 0xB4), Color.FromArgb(0xE8, 0x91, 0x3C),
        Color.FromArgb(0x8B, 0x5A, 0x3C), Color.FromArgb(0x2A, 0x2A, 0x2E), Color.FromArgb(0xF0, 0xE2, 0xC2),
    };
    static readonly Color[] EyePresets =
    {
        Color.Black, Color.FromArgb(0x4C, 0xC2, 0x4A), Color.FromArgb(0x3A, 0x8D, 0xFF), Color.FromArgb(0xF0, 0xA0, 0x20),
        Color.FromArgb(0xF2, 0xD8, 0x3C), Color.FromArgb(0xA0, 0x5C, 0xFF), Color.FromArgb(0xFF, 0x6F, 0xA8),
    };

    static readonly Color[] ObjectPresets =
    {
        Color.FromArgb(0xE0, 0x52, 0x5A), Color.FromArgb(0xFF, 0x7A, 0x45), Color.FromArgb(0xF2, 0xD8, 0x3C), Color.FromArgb(0x4C, 0xC2, 0x4A),
        Color.FromArgb(0x3C, 0xC9, 0xC0), Color.FromArgb(0x3A, 0x8D, 0xFF), Color.FromArgb(0xA0, 0x5C, 0xFF), Color.FromArgb(0xFF, 0x6F, 0xA8),
        Color.FromArgb(0xE8, 0xE8, 0xE8),
    };

    readonly List<Bitmap> objPreviews = new();   // previews of the objects on a cat's page
    Bitmap? previewSheet;
    readonly float k;
    readonly Color bg, sideBg, card, text, muted, line, accent, accentSoft, track;
    readonly Font fBase, fBold, fTitle, fSmall, fBrand, fIcon;   // fIcon: the system's icon font
    readonly Icon appIcon;
    readonly Panel side = new BufferedPanel { Dock = DockStyle.Left };
    readonly Panel host = new BufferedPanel { Dock = DockStyle.Fill };
    readonly Panel view = new BufferedPanel();                 // scrolled content (moved up/down by the custom scrollbar)
    readonly Bar bar;
    int devClicks;
    string updateNote = "";
    bool updating;
    bool mixOpen;   // the mixer starts collapsed
    readonly HashSet<int> mixCats = new();   // cats whose sounds are expanded in the mixer
    bool focusName = true;
    System.Windows.Forms.Timer? statsT;
    string page = Environment.GetCommandLineArgs().Contains("--cat") ? "cat0" : Environment.GetCommandLineArgs().Contains("--cat2") ? "cat1" : Environment.GetCommandLineArgs().Contains("--cat3") ? "cat2" : Environment.GetCommandLineArgs().Contains("--paint") ? "paint0" : Environment.GetCommandLineArgs().Contains("--cats") ? "cats" : Environment.GetCommandLineArgs().Contains("--pets") ? "pets"
        : Environment.GetCommandLineArgs().Contains("--credits") ? "credits" : Environment.GetCommandLineArgs().Contains("--volumes") ? "volumes" : Environment.GetCommandLineArgs().Contains("--info") ? "info" : "general";
    int devTarget;   // the cat the developer buttons act on

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);

    public SettingsForm(string startPage = "")
    {
        if (startPage != "") page = startPage;
        using (var gr = CreateGraphics()) k = gr.DpiX / 96f;
        bg = Theme.Bg; sideBg = Theme.SideBg; card = Theme.Card; text = Theme.Text; muted = Theme.Muted;
        line = Theme.Line; accent = Theme.Accent; accentSoft = Theme.AccentSoft; track = Theme.Track;
        fBase = new Font("Segoe UI", 9.5f); fBold = new Font("Segoe UI Semibold", 9.5f); fTitle = new Font("Segoe UI Semibold", 18f);
        fSmall = new Font("Segoe UI", 8.5f); fBrand = new Font("Segoe UI Semibold", 11f);
        fIcon = new Font(FontFamily.Families.Any(x => x.Name == "Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets", 12f);
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
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Build();
        if (Environment.GetCommandLineArgs().Contains("--picker"))   // testing: open the colour picker right away
            BeginInvoke(new Action(() => { using var d = new ColorPicker(this, Cfg.Cats[0].Fur1); d.ShowDialog(this); }));
    }

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
            previewSheet?.Dispose();
            foreach (var pv in objPreviews) pv.Dispose();
            statsT?.Dispose(); scrollT?.Dispose(); paintT?.Dispose();
            FlushPaint(); paintSaveT?.Dispose(); paintTip?.Dispose();
            fBase.Dispose(); fBold.Dispose(); fTitle.Dispose(); fSmall.Dispose(); fBrand.Dispose(); fIcon.Dispose(); appIcon.Dispose();
        }
        base.Dispose(disposing);
    }

    // The wheel moves a target; the view follows it on a spring, so scrolling glides, can be reversed at any moment
    // and keeps its speed between wheel notches (a precision trackpad just adds many small steps)
    readonly Spring scroll = new() { Response = 0.22 };
    System.Windows.Forms.Timer? scrollT;
    long scrollLast;
    int scrollSet;

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        double step = -e.Delta * P(70) / 120.0;
        if (!Spring.Enabled) { bar.Value += (int)Math.Round(step); base.OnMouseWheel(e); return; }
        if (scrollT == null || !scrollT.Enabled) { scroll.Value = scroll.Target = scrollSet = bar.Value; scroll.Velocity = 0; }
        scroll.Target = Math.Clamp(scroll.Target + step, 0, bar.MaxValue);
        scrollT ??= NewScrollTimer();
        if (!scrollT.Enabled) { scrollLast = Environment.TickCount64; scrollT.Start(); }
        base.OnMouseWheel(e);
    }

    System.Windows.Forms.Timer NewScrollTimer()
    {
        var t = new System.Windows.Forms.Timer { Interval = 8 };
        t.Tick += (_, _) =>
        {
            if (Math.Abs(bar.Value - scrollSet) > 1) { t.Stop(); return; }   // the scrollbar was dragged or the page changed: let go
            long now = Environment.TickCount64; scroll.Step((now - scrollLast) / 1000.0); scrollLast = now;
            scroll.Target = Math.Clamp(scroll.Target, 0, bar.MaxValue);
            scrollSet = (int)Math.Round(scroll.Value);
            bar.Value = scrollSet;
            if (scroll.Done) t.Stop();
        };
        return t;
    }

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
        foreach (var pv in objPreviews) pv.Dispose();
        objPreviews.Clear();
        statsT?.Dispose(); statsT = null;
        paintT?.Dispose(); paintT = null;
        FlushPaint();
        SyncPaintWindow();
        int keepScroll = bar.Value;

        int ny = P(84);
        var nav = new List<(string id, string label)> { ("general", Str.T("nav.general")), ("volumes", Str.T("nav.volumes")), ("pets", Str.T("nav.pets")), ("info", Str.T("about")) };
        if (Cfg.Dev) nav.Add(("dev", Str.T("nav.dev")));
        foreach (var (id, label) in nav)
        {
            var pill = new Pill(this) { Text = label, Active = NavActive(id), Bounds = new Rectangle(P(12), ny, P(158), P(40)), Kind = PillKind.Nav };
            pill.Click += (_, _) => { page = id; bar.Value = 0; Build(); };
            side.Controls.Add(pill);
            ny += P(46);
        }

        int pad = P(28), w = host.ClientSize.Width - bar.Width - pad * 2;
        int y = pad;

        if (page == "general")
        {
            y = Title(Str.T("nav.general"), pad, y, w);

            // preferences: always on top, start with Windows, classic sprites, language (a drop-down)
            var pc = AddCard(pad, y, w);
            int py = P(4);
            py = ToggleRow(pc, "onTop", py + P(12), w - P(32), Cfg.OnTop, v => Cfg.SetOnTop(v));
            pc.Controls.Add(new Panel { Bounds = new Rectangle(P(16), py, w - P(32), 1), BackColor = line });
            py = ToggleRow(pc, "startup", py + P(12), w - P(32), Startup.Enabled, v => { try { Startup.Set(v); } catch { } });
            pc.Controls.Add(new Panel { Bounds = new Rectangle(P(16), py, w - P(32), 1), BackColor = line });
            py = ToggleRow(pc, "classic", py + P(12), w - P(32), Cfg.ClassicSprites, v => { Cfg.SetClassic(v); Build(); });
            pc.Controls.Add(new Panel { Bounds = new Rectangle(P(16), py, w - P(32), 1), BackColor = line });
            int ly = py + P(12);
            int lh = RowHeader(pc, Str.T("language"), Str.T("language.hint"), P(16), ly, w - P(32) - P(160));
            var drop = new Pill(this) { Text = Languages.First(l => l.code == Cfg.Lang).name + "  ▾", Kind = PillKind.Seg, Bounds = new Rectangle(P(16) + w - P(32) - P(150), ly, P(150), P(32)) };
            drop.Click += (_, _) =>
            {
                var m = new ContextMenuStrip();
                foreach (var (code, name) in Languages) { var code2 = code; var it = new ToolStripMenuItem(name) { Checked = Cfg.Lang == code }; it.Click += (_, _) => Cfg.SetLang(code2); m.Items.Add(it); }
                MenuRenderer.Apply(m);
                m.Show(drop, new Point(0, drop.Height + P(4)));
            };
            pc.Controls.Add(drop);
            py = Math.Max(lh, ly + P(32)) + P(10);
            pc.Height = py + P(4);
            y += pc.Height + P(18);
        }
        else if (page == "volumes")
        {
            y = Title(Str.T("nav.volumes"), pad, y, w);
            // mixer: the master volume, expandable into each cat and each of its sounds
            var mc = AddCard(pad, y, w);
            int my = P(12), first = Cfg.Cats[0].Index, mx = P(16), mw = w - P(32);
            my = MixRow(mc, Str.T("mixer.master"), mx, my, mw, () => (int)Cfg.V["volume"], v => Cfg.V["volume"] = v, () => { mixOpen = !mixOpen; Build(); }, mixOpen, first, "mew");
            if (mixOpen)
                foreach (var cp in Cfg.Cats)
                {
                    var cat = cp;
                    bool open = mixCats.Contains(cat.Index);
                    my = MixRow(mc, cat.Display, mx + P(30), my, mw - P(30), () => cat.Volume, v => cat.Volume = v, () => { if (!mixCats.Remove(cat.Index)) mixCats.Add(cat.Index); Build(); }, open, cat.Index, "mew");
                    if (!open) continue;
                    my = MixRow(mc, Str.T("mix.voice"), mx + P(60), my, mw - P(60), () => cat.VoiceVol, v => cat.VoiceVol = v, null, false, cat.Index, "mew");
                    my = MixRow(mc, Str.T("mix.purr"), mx + P(60), my, mw - P(60), () => cat.PurrVol, v => cat.PurrVol = v, null, false, cat.Index, "mew");
                    my = MixRow(mc, Str.T("mix.fx"), mx + P(60), my, mw - P(60), () => cat.FxVol, v => cat.FxVol = v, null, false, cat.Index, "munch");
                }
            mc.Height = my;
            y += mc.Height + P(18);
        }
        else if (page == "info")
        {
            y = Title(Str.T("about"), pad, y, w);
            var about = AddCard(pad, y, w);
            int ay = P(4);

            // automatic check at start (installing is always manual)
            ay = ToggleRow(about, "autoupdate", ay + P(12), w - P(32), Cfg.AutoUpdate, v => { Cfg.AutoUpdate = v; Cfg.Save(); });
            about.Controls.Add(new Panel { Bounds = new Rectangle(P(16), ay, w - P(32), 1), BackColor = line });

            // version (tap it 9 times, like a cat's lives, to unlock developer mode)
            var verLbl = Lbl(Str.T("version"), fBold, text, P(16), ay + P(14), w - P(200)); verLbl.BackColor = card; about.Controls.Add(verLbl);
            var val = Lbl(Updater.Current.ToString(), fBold, accent, P(16) + w - P(32) - P(180), ay + P(14), P(180), ContentAlignment.TopRight);
            val.BackColor = card; about.Controls.Add(val);
            var note = Lbl(Cfg.Dev ? Str.T("dev.active") : "", fSmall, muted, P(16), ay + P(40), w - P(32)); note.BackColor = card; about.Controls.Add(note);
            val.Click += (_, _) =>
            {
                if (Cfg.Dev) return;
                devClicks++;
                if (devClicks >= 9) { Cfg.Dev = true; Cfg.Save(); devClicks = 0; page = "dev"; Build(); }
                else if (devClicks >= 4) note.Text = string.Format(Str.T("dev.left"), 9 - devClicks);
            };
            ay += P(66);   // below the note (it is empty unless developer mode is on or being unlocked)
            about.Controls.Add(new Panel { Bounds = new Rectangle(P(16), ay, w - P(32), 1), BackColor = line });

            // look for a newer release on GitHub, and install it on request
            var updBtn = new Pill(this) { Text = Updater.Found != null ? Str.T("upd.install") : Str.T("upd.check"), Kind = PillKind.Seg, Bounds = new Rectangle(P(16), ay + P(14), P(230), P(34)) };
            var updNote = Lbl(updateNote != "" ? updateNote : Updater.Found is { } fu ? string.Format(Str.T("upd.avail"), fu.Tag) : "", fSmall, muted, P(16), ay + P(54), w - P(32)); updNote.BackColor = card; about.Controls.Add(updNote);
            updBtn.Click += async (_, _) =>
            {
                if (updating) return;
                updating = true;
                try
                {
                    if (Updater.Found != null)
                    {
                        updNote.Text = Str.T("upd.installing");
                        await Updater.Install(Updater.Found);
                        foreach (var c in PetWindow.All.ToList()) c.PrepareExit();   // save, then quit: the script swaps the exe and restarts
                        Application.Exit();
                        return;
                    }
                    updNote.Text = Str.T("upd.checking");
                    var r = await Updater.Latest();   // in this process: starting a second copy of the exe just to look took several seconds
                    if (r != null && Updater.IsNewer(r)) { Updater.Found = r; updateNote = string.Format(Str.T("upd.avail"), r.Tag); updBtn.Text = Str.T("upd.install"); }
                    else updateNote = string.Format(Str.T("upd.latest"), "v" + Updater.Current);
                }
                catch { updateNote = Str.T("upd.error"); }
                finally { updating = false; }
                if (!IsDisposed) updNote.Text = updateNote;
            };
            about.Controls.Add(updBtn);
            ay += P(54) + updNote.Height + P(14);
            about.Controls.Add(new Panel { Bounds = new Rectangle(P(16), ay, w - P(32), 1), BackColor = line });

            // changelog (the release notes on GitHub) and credits
            var logBtn = new Pill(this) { Text = Str.T("about.changelog"), Kind = PillKind.Seg, Bounds = new Rectangle(P(16), ay + P(14), P(150), P(34)) };
            logBtn.Click += (_, _) => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Updater.ReleasesUrl) { UseShellExecute = true }); } catch { } };
            about.Controls.Add(logBtn);
            var creditsBtn = new Pill(this) { Text = Str.T("about.creditsBtn"), Kind = PillKind.Seg, Bounds = new Rectangle(P(16) + P(160), ay + P(14), P(150), P(34)) };
            creditsBtn.Click += (_, _) => { page = "credits"; bar.Value = 0; Build(); };
            about.Controls.Add(creditsBtn);
            about.Height = ay + P(14) + P(34) + P(16);
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
            back.Click += (_, _) => { page = "info"; bar.Value = 0; Build(); };
            view.Controls.Add(back);
        }
        else if (page == "dev")
        {
            y = Title(Str.T("nav.dev"), pad, y, w);
            view.Controls.Add(Lbl(Str.T("dev.intro"), fBase, muted, pad, y - P(6), w));
            y += P(26);
            if (devTarget >= Cfg.Cats.Count) devTarget = 0;
            if (Cfg.Cats.Count > 1)   // several cats: choose which one the buttons act on
            {
                view.Controls.Add(Lbl(Str.T("dev.target"), fBold, muted, pad + P(2), y, w));
                y += P(28);
                for (int i = 0; i < Cfg.Cats.Count; i++)
                {
                    int ci = i;
                    var tp = new Pill(this) { Text = Cfg.Cats[i].Display, Kind = PillKind.Seg, Active = devTarget == i, Bounds = new Rectangle(pad + i % 3 * P(150), y + i / 3 * P(42), P(140), P(34)) };
                    tp.Click += (_, _) => { devTarget = ci; Build(); };
                    view.Controls.Add(tp);
                }
                y += (Cfg.Cats.Count + 2) / 3 * P(42) + P(6);
            }
            view.Controls.Add(Lbl(Str.T("dev.actions"), fBold, muted, pad + P(2), y, w));
            y += P(28);
            var c = AddCard(pad, y, w);
            string[] acts = { "idle", "lick", "walk", "run", "sleep", "play", "pounce", "frenzy", "pet", "meow", "bowl", "bed", "ball", "hungry", "sad", "tired", "groom", "fight" };
            int gap = P(8), cols = 3, bw = (w - P(32) - gap * (cols - 1)) / cols, bh = P(38);
            for (int i = 0; i < acts.Length; i++)
            {
                string a2 = acts[i];
                var b = new Pill(this) { Text = Str.T("act." + a2), Kind = PillKind.Seg, Bounds = new Rectangle(P(16) + i % cols * (bw + gap), P(16) + i / cols * (bh + gap), bw, bh) };
                b.Click += (_, _) => PetWindow.Find(Cfg.Cats[devTarget].Index)?.Trigger(a2);
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
                if (PetWindow.Find(Cfg.Cats[devTarget].Index) is { } pw)
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

            var off = new Pill(this) { Text = Str.T("dev.disable"), Bounds = new Rectangle(pad, y + P(6), P(240), P(34)), Kind = PillKind.Outline };
            off.Click += (_, _) => { Cfg.Dev = false; Cfg.Save(); page = "general"; Build(); };
            view.Controls.Add(off);
        }
        else if (page == "pets")
        {
            y = Title(Str.T("nav.pets"), pad, y, w);
            view.Controls.Add(Lbl(Str.T("pets.intro"), fBase, muted, pad, y - P(6), w));
            y += P(30);
            var pcard = AddCard(pad, y, w);
            pcard.Controls.Add(Thumb(Cfg.Cats[0], new Rectangle(P(14), P(14), P(90), P(72))));
            var pn = Lbl(Str.T("pets.cats"), fBold, text, P(120), P(28), w - P(180)); pn.BackColor = card; pcard.Controls.Add(pn);
            var pcnt = Lbl(string.Format(Str.T("pets.count"), Cfg.Cats.Count), fSmall, muted, P(120), P(52), w - P(180)); pcnt.BackColor = card; pcard.Controls.Add(pcnt);
            var pch = Lbl("\u203A", fTitle, muted, w - P(48), P(24), P(30), ContentAlignment.TopRight); pch.BackColor = card; pcard.Controls.Add(pch);
            pcard.Height = P(100);
            Clickable(pcard, () => { page = "cats"; bar.Value = 0; Build(); });
        }
        else if (page == "cats")
        {
            var back = new Pill(this) { Text = Str.T("back.pets"), Bounds = new Rectangle(pad, y - P(8), P(110), P(32)), Kind = PillKind.Outline };
            back.Click += (_, _) => { page = "pets"; bar.Value = 0; Build(); };
            view.Controls.Add(back);
            y += P(36);
            y = Title(Str.T("pets.cats"), pad, y, w);
            var shc = AddCard(pad, y, w);
            shc.Height = ToggleRow(shc, "share", P(12), w - P(32), Cfg.ShareObjects, v => { Cfg.SetShare(v); Build(); }) + P(2);
            y += shc.Height + P(14);
            foreach (var cp in Cfg.Cats)
            {
                var cat = cp;
                var row = AddCard(pad, y, w);
                row.Controls.Add(Thumb(cat, new Rectangle(P(14), P(12), P(90), P(72))));
                var nm = Lbl(cat.Display, fBold, text, P(120), P(26), w - P(180)); nm.BackColor = card; row.Controls.Add(nm);
                var chl = Lbl(Str.T("char." + cat.Character), fSmall, muted, P(120), P(50), w - P(180)); chl.BackColor = card; row.Controls.Add(chl);
                var arrow = Lbl("\u203A", fTitle, muted, w - P(48), P(22), P(30), ContentAlignment.TopRight); arrow.BackColor = card; row.Controls.Add(arrow);
                row.Height = P(96);
                Clickable(row, () => { page = "cat" + cat.Index; bar.Value = 0; Build(); });
                y += row.Height + P(12);
            }
            if (Cfg.Cats.Count < Cfg.MaxCats)
            {
                var add = new Pill(this) { Text = "+  " + Str.T("cats.add"), Bounds = new Rectangle(pad, y + P(2), P(220), P(36)), Kind = PillKind.Outline };
                add.Click += (_, _) => { Cfg.AddCat(); Build(); };
                view.Controls.Add(add);
            }
            else view.Controls.Add(Lbl(string.Format(Str.T("cats.max"), Cfg.MaxCats), fSmall, muted, pad, y + P(6), w));
        }
        else if (page.StartsWith("paint"))
        {
            var pcat = int.TryParse(page.AsSpan(5), out int pci) && Cfg.ById(pci) is { } pf ? pf : Cfg.Cats[0];
            y = BuildPaint(pcat, pad, y, w);
        }
        else
        {
            // one cat: its name, volume, character, look and objects (each cat is independent from the others)
            var cat = int.TryParse(page.AsSpan(3), out int pi) && Cfg.ById(pi) is { } found ? found : Cfg.Cats[0];
            var win = PetWindow.Find(cat.Index);
            var backCats = new Pill(this) { Text = Str.T("back.cats"), Bounds = new Rectangle(pad, y - P(8), P(110), P(32)), Kind = PillKind.Outline };
            backCats.Click += (_, _) => { page = "cats"; bar.Value = 0; Build(); };
            view.Controls.Add(backCats);
            y += P(36);
            y = Title(cat.Display, pad, y, w);

            // name
            var nc = AddCard(pad, y, w);
            int ny2 = RowHeader(nc, Str.T("name.label"), Str.T("name.hint"), P(16), P(12), w - P(32));
            var field = new Card(track, line, card, P(8)) { Bounds = new Rectangle(P(16), ny2 + P(4), w - P(32), P(34)) };
            var tb = new TextBox { Text = cat.Name, MaxLength = 20, BorderStyle = BorderStyle.None, BackColor = track, ForeColor = text, Font = fBase, Bounds = new Rectangle(P(10), P(8), w - P(32) - P(20), P(20)) };
            tb.TextChanged += (_, _) => { cat.Name = tb.Text.Trim(); Cfg.Changed(cat); };
            field.Controls.Add(tb);
            nc.Controls.Add(field);
            nc.Height = ny2 + P(4) + P(34) + P(16);
            if (focusName) { ActiveControl = tb; focusName = false; }
            y += nc.Height + P(14);

            var vc = AddCard(pad, y, w);
            vc.Height = MixRow(vc, Str.T("mixer.pet"), P(16), P(14), w - P(32), () => cat.Volume, v => cat.Volume = v, null, false, cat.Index, "mew", false) + P(2);
            y += vc.Height + P(14);

            // monitor (only with more than one)
            if (Screens.Monitors.Length > 1)
            {
                var mon = AddCard(pad, y, w);
                int mh = RowHeader(mon, Str.T("monitor.label"), Str.T("monitor.hint"), P(16), P(12), w - P(32) - P(200));
                string MonLabel(string value)
                {
                    if (value == "free") return Str.T("monitor.free");
                    var ms = Screens.Monitors;
                    var m = ms.FirstOrDefault(q => q.Name == value); if (m.Name == null) m = ms.FirstOrDefault(q => q.Primary);
                    var digits = new string(m.Name.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
                    return string.Format(Str.T("monitor.n"), digits == "" ? "?" : digits) + (m.Primary ? " " + Str.T("monitor.main") : "");
                }
                var catP = cat;
                var mdrop = new Pill(this) { Text = MonLabel(cat.Monitor) + "  ▾", Kind = PillKind.Seg, Bounds = new Rectangle(P(16) + w - P(32) - P(190), P(12), P(190), P(32)) };
                mdrop.Click += (_, _) =>
                {
                    var m = new ContextMenuStrip();
                    string curName = Screens.Monitors.FirstOrDefault(q => q.Name == catP.Monitor).Name ?? Screens.Monitors.FirstOrDefault(q => q.Primary).Name;
                    var freeItem = new ToolStripMenuItem(Str.T("monitor.free")) { Checked = catP.Monitor == "free" };
                    freeItem.Click += (_, _) => { Cfg.SetMonitor(catP, "free"); Build(); };
                    m.Items.Add(freeItem);
                    foreach (var mm in Screens.Monitors)
                    {
                        var name = mm.Name;
                        var it = new ToolStripMenuItem(MonLabel(name)) { Checked = catP.Monitor != "free" && name == curName };
                        it.Click += (_, _) => { Cfg.SetMonitor(catP, name); Build(); };
                        m.Items.Add(it);
                    }
                    MenuRenderer.Apply(m);
                    m.Show(mdrop, new Point(0, mdrop.Height + P(4)));
                };
                mon.Controls.Add(mdrop);
                mon.Height = Math.Max(mh, P(12) + P(32)) + P(14);
                y += mon.Height + P(14);
            }

            // character
            view.Controls.Add(Lbl(Str.T("char.title"), fBold, muted, pad + P(2), y, w));
            y += P(28);
            var chc = AddCard(pad, y, w);
            int pw = (w - P(32) - P(8) * 2) / 3, px = P(16), py = P(14), col = 0;
            foreach (var t in Characters.All)
            {
                var key = t.Key;
                var cp = new Pill(this) { Text = Str.T("char." + key), Kind = PillKind.Seg, Active = cat.Character == key, Bounds = new Rectangle(px, py, pw, P(34)) };
                cp.Click += (_, _) => { cat.Character = key; Cfg.Changed(cat); Build(); };
                chc.Controls.Add(cp);
                if (++col % 3 == 0) { px = P(16); py += P(42); } else px += pw + P(8);
            }
            if (col % 3 != 0) py += P(42);
            var chint = Lbl(Str.T("char." + cat.Character + ".hint"), fSmall, muted, P(16), py + P(2), w - P(32)); chint.BackColor = card; chc.Controls.Add(chint);
            chc.Height = py + P(2) + chint.Height + P(14);
            y += chc.Height + P(14);

            // look: three colours with a live preview of the cat
            view.Controls.Add(Lbl(Str.T("look.title"), fBold, muted, pad + P(2), y, w));
            y += P(28);
            var lc = AddCard(pad, y, w);
            previewSheet?.Dispose();
            previewSheet = CatSprite.BuildSheet(cat);
            lc.Controls.Add(new CatPreview(this, previewSheet) { Bounds = new Rectangle(P(16), P(16), P(170), P(136)) });
            int rx = P(16) + P(170) + P(18), ry = P(14);
            int rw = w - rx - P(16);
            ry = ColorRow(lc, Str.T(Cfg.ClassicSprites ? "look.fur1" : "look.fur"), cat.Fur1, FurPresets, c => { cat.Fur1 = c; Cfg.Changed(cat); }, rx, ry, rw);
            if (Cfg.ClassicSprites) ry = ColorRow(lc, Str.T("look.fur2"), cat.Fur2, FurPresets, c => { cat.Fur2 = c; Cfg.Changed(cat); }, rx, ry, rw);   // the new cat has one fur colour (its shades are derived)
            if (cat.Pattern != "none") ry = ColorRow(lc, Str.T("look.fur3"), cat.Fur3, FurPresets, c => { cat.Fur3 = c; Cfg.Changed(cat); }, rx, ry, rw);
            ry = ColorRow(lc, Str.T("look.eyes"), cat.Eyes, EyePresets, c => { cat.Eyes = c; Cfg.Changed(cat); }, rx, ry, rw);
            if (!Cfg.ClassicSprites)
            {
                ry = ColorRow(lc, Str.T("look.nose"), cat.Nose, NosePresets, c => { cat.Nose = c; Cfg.Changed(cat); }, rx, ry, rw);
                ry = ColorRow(lc, Str.T("look.ears"), cat.Ears, EarPresets, c => { cat.Ears = c; Cfg.Changed(cat); }, rx, ry, rw);
            }
            lc.Height = Math.Max(P(16) * 2 + P(136), ry + P(6));
            y += lc.Height + P(14);
            y = PatternCard(cat, pad, y, w);

            y = AccessoryCard(cat, pad, y, w);
            var resetLook = new Pill(this) { Text = Str.T("look.reset"), Bounds = new Rectangle(pad, y + P(2), P(190), P(34)), Kind = PillKind.Outline };
            resetLook.Click += (_, _) => { cat.ResetLook(); Cfg.Changed(cat); Build(); };
            view.Controls.Add(resetLook);
            if (!Cfg.ClassicSprites)
            {
                var studio = new Pill(this) { Text = Str.T("paint.open"), Bounds = new Rectangle(pad + P(200), y + P(2), w - P(200), P(34)), Kind = PillKind.Seg, Active = true };
                studio.Click += (_, _) => { page = "paint" + cat.Index; bar.Value = 0; Build(); };
                view.Controls.Add(studio);
            }
            y += P(34) + P(22);

            // objects: which ones this cat has, and the colour of each (the shades are derived from it)
            if (Cfg.ShareObjects && cat != Cfg.Cats[0])
            {
                view.Controls.Add(Lbl(string.Format(Str.T("objs.shared"), Cfg.Cats[0].Display), fBase, muted, pad + P(2), y, w));
                y += P(40);
            }
            else if (win != null)
            {
                view.Controls.Add(Lbl(Str.T("objs.title"), fBold, muted, pad + P(2), y, w));
                y += P(28);
                int pu = P(4);
                var defs = new (string label, Func<Bitmap> image, Func<bool> has, Action toggle, Func<Color> get, Action<Color> set)[]
                {
                    (Str.T("menu.bowl"), () => PixelArt.BowlImage(pu, cat.BowlColor), () => win.HasBowl, () => win.ToggleProp(false), () => cat.BowlColor, c => { cat.BowlColor = c; Cfg.Changed(cat); }),
                    (Str.T("menu.bed"), () => PixelArt.BedImage(pu, cat.BedColor), () => win.HasBed, () => win.ToggleProp(true), () => cat.BedColor, c => { cat.BedColor = c; Cfg.Changed(cat); }),
                    (Str.T("menu.ball"), () => PixelArt.BallImage(pu, cat.BallColor), () => win.HasBall, () => win.ToggleBall(), () => cat.BallColor, c => { cat.BallColor = c; Cfg.Changed(cat); }),
                };
                foreach (var (label, image, has, toggle, get, set) in defs)
                {
                    var oc = AddCard(pad, y, w);
                    var bmp = image();
                    objPreviews.Add(bmp);
                    oc.Controls.Add(new ObjPreview(this, bmp) { Bounds = new Rectangle(P(16), P(16), P(130), P(104)) });
                    int ox = P(16) + P(130) + P(18), orw = w - ox - P(16);
                    var ol = Lbl(label, fBold, text, ox, P(18), orw - P(60)); ol.BackColor = card; oc.Controls.Add(ol);
                    var tg = new Toggle(this) { On = has(), Bounds = new Rectangle(w - P(16) - P(44), P(16), P(44), P(24)) };
                    tg.Changed += () => { if (has() != tg.On) toggle(); };
                    oc.Controls.Add(tg);
                    int oy = ColorRow(oc, Str.T("obj.color"), get(), ObjectPresets, set, ox, P(54), orw);
                    oc.Height = Math.Max(P(16) * 2 + P(104), oy + P(6));
                    y += oc.Height + P(14);
                }
                var resetObj = new Pill(this) { Text = Str.T("look.reset"), Bounds = new Rectangle(pad, y + P(2), P(190), P(34)), Kind = PillKind.Outline };
                resetObj.Click += (_, _) => { cat.ResetObjectColors(); Cfg.Changed(cat); Build(); };
                view.Controls.Add(resetObj);
                y += P(34) + P(22);
            }
            if (Cfg.Cats.Count > 1)
            {
                var rm = new Pill(this) { Text = Str.T("cat.remove"), Bounds = new Rectangle(pad, y + P(2), P(240), P(34)), Kind = PillKind.Outline };
                rm.Click += (_, _) =>
                {
                    if (MessageBox.Show(this, string.Format(Str.T("cat.remove.ask"), cat.Display), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                    Cfg.RemoveCat(cat); page = "cats"; bar.Value = 0; Build();
                };
                view.Controls.Add(rm);
            }
        }

        int bottom = view.Controls.Cast<Control>().Max(c => c.Bottom) + pad;
        view.SetBounds(0, 0, host.ClientSize.Width - bar.Width, Math.Max(bottom, host.ClientSize.Height));
        bar.Value = keepScroll;
        bar.Setup(view.Height, host.ClientSize.Height);
    }

    bool NavActive(string id) => id == "pets" ? page.StartsWith("cat") || page.StartsWith("paint") : id == "info" ? page is "info" or "credits" : page == id;

    // makes a card (or any control with children) react to a click anywhere on it
    void Clickable(Card c, Action a)
    {
        c.SetHover(Mix(card, accent, 0.07), accent);
        // hover/press are tracked for the card and all its children, so moving over a label does not flicker it off
        void Hook(Control x)
        {
            x.Cursor = Cursors.Hand;
            x.Click += (_, _) => a();
            x.MouseEnter += (_, _) => c.SetState(true, false);
            x.MouseDown += (_, _) => c.SetState(true, true);
            x.MouseUp += (_, _) => c.SetState(true, false);
            x.MouseLeave += (_, _) => { if (!c.ClientRectangle.Contains(c.PointToClient(Cursor.Position))) c.SetState(false, false); };
            foreach (Control ch in x.Controls) Hook(ch);
        }
        Hook(c);
    }

    // a small picture of a cat in its colours, for the lists
    CatPreview Thumb(CatProfile p, Rectangle r)
    {
        var sheet = CatSprite.BuildSheet(p);
        objPreviews.Add(sheet);
        return new CatPreview(this, sheet) { Bounds = r };
    }

    // One row of the mixer: [chevron] label ... value, and a slider below. toggle != null makes it expandable.
    int MixRow(Control parent, string label, int x, int y, int w, Func<int> get, Action<int> set, Action? toggle, bool open, int owner, string sound, bool indent = true)
    {
        int ind = indent ? P(30) : 0;
        if (toggle != null)
        {
            var ch = new Pill(this) { Text = open ? "\u25BE" : "\u25B8", Kind = PillKind.Seg, Bounds = new Rectangle(x, y, P(26), P(24)) };
            ch.Click += (_, _) => toggle();
            parent.Controls.Add(ch);
        }
        var val = Lbl("", fBold, accent, x + w - P(60), y + P(2), P(60), ContentAlignment.TopRight); val.BackColor = card; parent.Controls.Add(val);
        var l = Lbl(label, fBold, text, x + ind, y + P(2), w - ind - P(64)); l.BackColor = card; parent.Controls.Add(l);
        var sl = new Slider(k) { Min = 0, Max = 100, Step = 5, Value = get(), Track = track, Accent = accent, Back = card, Bounds = new Rectangle(x + ind, y + P(28), w - ind, P(26)) };
        sl.Changed += () => { set((int)sl.Value); val.Text = get() + "%"; };
        sl.Committed += () => { Cfg.Save(); Audio.Refresh(); Audio.Play(sound, 1, owner); };   // let the user hear it
        val.Text = get() + "%";
        parent.Controls.Add(sl);
        return y + P(28) + P(26) + P(12);
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

    // One colour layer: its name, a row of preset chips and a "+" chip for any colour; returns the y of the next layer
    int ColorRow(Control parent, string label, Color current, Color[] presets, Action<Color> set, int rx, int ry, int rw)
    {
        var l = Lbl(label, fBold, text, rx, ry, rw); l.BackColor = card; parent.Controls.Add(l);
        int sx = rx, sy = ry + P(24), size = P(22), gap = P(6);
        bool matched = false;
        foreach (var pc in presets)
        {
            bool sel = pc.ToArgb() == current.ToArgb();
            matched |= sel;
            var sw = new Swatch(this) { Fill = pc, Selected = sel, Bounds = new Rectangle(sx, sy, size, size) };
            var chosen = pc;
            sw.Click += (_, _) => { set(chosen); Build(); };
            parent.Controls.Add(sw);
            sx += size + gap;
        }
        var custom = new Swatch(this) { Fill = matched ? track : current, Selected = !matched, Plus = true, Bounds = new Rectangle(sx, sy, size, size) };
        custom.Click += (_, _) =>
        {
            using var dlg = new ColorPicker(this, current);
            if (dlg.ShowDialog(this) == DialogResult.OK) { Cfg.AddRecent(dlg.Result); set(dlg.Result); Build(); }
        };
        parent.Controls.Add(custom);
        return ry + P(54);
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

    static Color Mix(Color a, Color b, double t) =>
        Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

    // Motion is a spring, not a fixed-duration tween: it can be re-targeted at any moment and keeps its velocity.
    // damping 1 = no overshoot; response = seconds to get there. Off when Windows has animations turned off.
    sealed class Spring
    {
        public double Value, Target, Velocity;
        public double Response = 0.28, Damping = 1;
        public bool Done => Math.Abs(Target - Value) < 0.002 && Math.Abs(Velocity) < 0.01;
        public static bool Enabled => SystemInformation.UIEffectsEnabled;
        public void Step(double dt)
        {
            if (!Enabled) { Value = Target; Velocity = 0; return; }
            double w = 2 * Math.PI / Response;
            dt = Math.Min(dt, 0.033);
            Velocity += (w * w * (Target - Value) - 2 * Damping * w * Velocity) * dt;
            Value += Velocity * dt;
            if (Done) { Value = Target; Velocity = 0; }
        }
    }

    enum PillKind { Nav, Seg, Outline }

    sealed class Pill : Control
    {
        readonly SettingsForm f;
        bool hover, pressed;
        public bool Active;
        public string Glyph = "";   // an icon of the system's icon font, drawn before the text (alone when there is no text)
        public PillKind Kind;
        public Pill(SettingsForm owner)
        {
            f = owner;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            Cursor = Cursors.Hand;
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }   // feedback on the press, not on the release
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            Color parentBg = Kind == PillKind.Nav ? f.sideBg : Kind == PillKind.Outline ? f.bg : f.card;
            g.Clear(parentBg);
            var r = pressed ? new Rectangle(f.P(1), f.P(1), Width - 1 - f.P(2), Height - 1 - f.P(2)) : new Rectangle(0, 0, Width - 1, Height - 1);   // pressed: it sinks a little
            Color fill = parentBg, fore = f.text;
            switch (Kind)
            {
                case PillKind.Nav: if (Active) fill = f.accentSoft; else if (hover) fill = f.bg; fore = Active ? f.accent : f.muted; break;
                case PillKind.Seg: fill = Active ? f.accent : hover ? f.line : f.track; fore = Active ? Color.White : f.text; break;
                case PillKind.Outline: fill = f.card; fore = hover ? f.accent : f.text; break;
            }
            if (pressed) fill = Mix(fill, f.text, 0.10);
            using (var path = RoundRect(r, f.P(10)))
            {
                using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                if (Kind == PillKind.Outline) using (var pen = new Pen(hover ? f.accent : f.line)) g.DrawPath(pen, path);
            }
            if (Kind == PillKind.Nav && Active)
                using (var b = new SolidBrush(f.accent)) g.FillPath(b, RoundRect(new Rectangle(f.P(4), Height / 2 - f.P(9), f.P(3), f.P(18)), f.P(1)));
            if (Glyph != "")
            {
                const TextFormatFlags nf = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;
                int gw = TextRenderer.MeasureText(Glyph, f.fIcon, Size.Empty, nf).Width;
                int tw = Text == "" ? 0 : TextRenderer.MeasureText(Text, f.fBold, Size.Empty, nf).Width, gap = tw == 0 ? 0 : f.P(8);
                int x0 = Math.Max(f.P(8), (Width - gw - gap - tw) / 2);
                TextRenderer.DrawText(g, Glyph, f.fIcon, new Rectangle(x0, 0, gw + f.P(2), Height), fore, nf);
                if (tw > 0) TextRenderer.DrawText(g, Text, f.fBold, new Rectangle(x0 + gw + gap, 0, Width - x0 - gw - gap, Height), fore, nf | TextFormatFlags.EndEllipsis);
                return;
            }
            var flags = TextFormatFlags.VerticalCenter | (Kind == PillKind.Nav ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter);
            var tr = Kind == PillKind.Nav ? new Rectangle(f.P(18), 0, Width - f.P(18), Height) : ClientRectangle;
            TextRenderer.DrawText(g, Text, f.fBold, tr, fore, flags);
        }
    }

    sealed class Card : Panel
    {
        readonly Color fill, border, back; readonly int rad;
        Color hoverFill, hoverBorder;
        bool hover, pressed;
        public bool Interactive;
        public void SetHover(Color f, Color b) { hoverFill = f; hoverBorder = b; Interactive = true; }
        public void SetState(bool h, bool p)
        {
            if (hover == h && pressed == p) return;
            hover = h; pressed = p;
            Color cur = Interactive && hover ? (pressed ? Mix(hoverFill, Color.Black, 0.12) : hoverFill) : fill;
            foreach (Control c in Controls) if (c is Label or CatPreview) c.BackColor = cur;   // the children are painted on the card colour: follow it
            Invalidate(true);
        }
        public Card(Color fill, Color border, Color back, int rad)
        {
            this.fill = fill; this.border = border; this.back = back; this.rad = rad;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(back);
            using var path = RoundRect(new Rectangle(0, 0, Width - 1, Height - 1), rad);
            using (var b = new SolidBrush(Interactive && hover ? (pressed ? Mix(hoverFill, Color.Black, 0.12) : hoverFill) : fill)) g.FillPath(b, path);
            using (var pen = new Pen(Interactive && hover ? hoverBorder : border)) g.DrawPath(pen, path);
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
        readonly Spring pos = new() { Response = 0.30, Damping = 0.85 };   // a little overshoot: the switch is flicked
        readonly System.Windows.Forms.Timer t = new() { Interval = 15 };
        long last;
        bool on, pressed;
        public bool On
        {
            get => on;
            set { on = value; pos.Target = value ? 1 : 0; if (!IsHandleCreated || !Spring.Enabled) pos.Value = pos.Target; else Run(); }
        }
        public event Action? Changed;
        public Toggle(SettingsForm owner)
        {
            f = owner;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            Cursor = Cursors.Hand;
            t.Tick += (_, _) =>
            {
                long now = Environment.TickCount64; pos.Step((now - last) / 1000.0); last = now;
                Invalidate();
                if (pos.Done) t.Stop();
            };
        }
        void Run() { if (!t.Enabled) { last = Environment.TickCount64; t.Start(); } }
        protected override void Dispose(bool disposing) { if (disposing) t.Dispose(); base.Dispose(disposing); }
        protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnMouseLeave(EventArgs e) { pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnClick(EventArgs e) { On = !On; Changed?.Invoke(); base.OnClick(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(f.card);
            double p = pos.Value, pc = Math.Clamp(p, 0, 1);
            using (var path = RoundRect(new Rectangle(0, 0, Width - 1, Height - 1), Height / 2))
            using (var b = new SolidBrush(Mix(f.track, f.accent, pc))) g.FillPath(b, path);
            int d = Height - f.P(8), w = d + (pressed ? f.P(4) : 0);   // the knob stretches under the finger
            int x0 = f.P(4), x1 = Width - d - f.P(4);
            int x = (int)Math.Round(x0 + (x1 - x0) * p) - (on && pressed ? f.P(4) : 0);
            using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, x, f.P(4), w, d);
        }
    }

    // A colour chip: filled square, accent ring when it is the current colour; the "+" one opens the colour picker
    sealed class Swatch : Control
    {
        readonly SettingsForm f;
        bool hover, pressed;
        public Color Fill;
        public bool Selected, Plus;
        public Swatch(SettingsForm owner)
        {
            f = owner;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            Cursor = Cursors.Hand;
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(f.card);
            int pad = pressed ? f.P(5) : f.P(3);
            var inner = new Rectangle(pad, pad, Width - pad * 2 - 1, Height - pad * 2 - 1);
            using (var path = RoundRect(inner, f.P(5))) using (var b = new SolidBrush(Fill))
            {
                g.FillPath(b, path);
                using var pen = new Pen(f.line); g.DrawPath(pen, path);
            }
            if (Plus)
            {
                using var pen = new Pen(Fill.GetBrightness() > 0.55f ? Color.FromArgb(60, 60, 70) : Color.White, 1.6f * f.k);
                int cx = Width / 2, cy = Height / 2, r = f.P(4);
                g.DrawLine(pen, cx - r, cy, cx + r, cy); g.DrawLine(pen, cx, cy - r, cx, cy + r);
            }
            if (Selected || hover)
                using (var path = RoundRect(new Rectangle(0, 0, Width - 1, Height - 1), f.P(7))) using (var pen = new Pen(Selected ? f.accent : f.muted, 2f))
                    g.DrawPath(pen, path);
        }
    }

    // An object (bowl, bed, ball) in the chosen colours
    sealed class ObjPreview : Control
    {
        readonly SettingsForm f;
        readonly Bitmap bmp;
        public ObjPreview(SettingsForm owner, Bitmap bitmap)
        {
            f = owner; bmp = bitmap;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(f.card);
            using (var path = RoundRect(new Rectangle(0, 0, Width - 1, Height - 1), f.P(12))) using (var b = new SolidBrush(f.track))
                g.FillPath(b, path);
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(bmp, (Width - bmp.Width) / 2, (Height - bmp.Height) / 2, bmp.Width, bmp.Height);
        }
    }

    // The sitting cat in the chosen colours
    sealed class CatPreview : Control
    {
        readonly SettingsForm f;
        readonly Bitmap sheet;
        public CatPreview(SettingsForm owner, Bitmap sheetBitmap)
        {
            f = owner; sheet = sheetBitmap; BackColor = f.card;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(BackColor);
            using (var path = RoundRect(new Rectangle(0, 0, Width - 1, Height - 1), f.P(12))) using (var b = new SolidBrush(f.track))
                g.FillPath(b, path);
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            var src = CatSprite.PreviewRect;   // the sitting cat inside its 32x32 frame
            int scale = Math.Max(1, Math.Min((Width - f.P(16)) / src.Width, (Height - f.P(12)) / src.Height));
            var dst = new Rectangle((Width - src.Width * scale) / 2, (Height - src.Height * scale) / 2, src.Width * scale, src.Height * scale);
            g.DrawImage(sheet, dst, src.X, src.Y, src.Width, src.Height, GraphicsUnit.Pixel);
        }
    }

    // ---- colour picker -----------------------------------------------------------------------------

    static void ToHsv(Color c, out double h, out double s, out double v)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        v = max; s = max == 0 ? 0 : d / max;
        if (d == 0) h = 0;
        else if (max == r) h = 60 * (((g - b) / d) % 6);
        else if (max == g) h = 60 * ((b - r) / d + 2);
        else h = 60 * ((r - g) / d + 4);
        if (h < 0) h += 360;
    }

    static Color FromHsv(double h, double s, double v)
    {
        double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c, r, g, b;
        int sector = (int)(h / 60) % 6;
        (r, g, b) = sector switch { 0 => (c, x, 0.0), 1 => (x, c, 0.0), 2 => (0.0, c, x), 3 => (0.0, x, c), 4 => (x, 0.0, c), _ => (c, 0.0, x) };
        return Color.FromArgb((int)Math.Round((r + m) * 255), (int)Math.Round((g + m) * 255), (int)Math.Round((b + m) * 255));
    }

    // A modern colour picker in the app's style: saturation/brightness square, hue bar, hex field, recent colours
    sealed class ColorPicker : Form
    {
        readonly SettingsForm f;
        readonly SvBox sv;
        readonly HueBar hueBar;
        readonly ColorChip newChip, oldChip;
        readonly TextBox hex;
        double h, s, v;
        bool syncing;
        public Color Result;

        public ColorPicker(SettingsForm owner, Color start)
        {
            f = owner; Result = start;
            ToHsv(start, out h, out s, out v);
            Text = Str.T("picker.title");
            Icon = owner.Icon;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = f.card; ForeColor = f.text; Font = f.fBase;
            int pad = f.P(18), cw = f.P(304);
            ClientSize = new Size(cw + pad * 2, f.P(486));

            int y = pad;
            sv = new SvBox(f) { Bounds = new Rectangle(pad, y, cw, f.P(180)), Hue = h, S = s, V = v };
            sv.Changed += () => { s = sv.S; v = sv.V; Apply(false); };
            Controls.Add(sv);
            y += f.P(180) + f.P(14);

            hueBar = new HueBar(f) { Bounds = new Rectangle(pad, y, cw, f.P(22)), Hue = h };
            hueBar.Changed += () => { h = hueBar.Hue; sv.Hue = h; Apply(false); };
            Controls.Add(hueBar);
            y += f.P(22) + f.P(16);

            int half = (cw - f.P(10)) / 2;
            oldChip = new ColorChip(f) { Caption = Str.T("picker.current"), Fill = start, Bounds = new Rectangle(pad, y, half, f.P(40)) };
            newChip = new ColorChip(f) { Caption = Str.T("picker.new"), Fill = start, Bounds = new Rectangle(pad + half + f.P(10), y, half, f.P(40)) };
            Controls.Add(oldChip); Controls.Add(newChip);
            y += f.P(40) + f.P(16);

            var hl = f.Lbl("Hex", f.fBold, f.muted, pad, y + f.P(8), f.P(40)); hl.BackColor = f.card; Controls.Add(hl);
            var field = new Card(f.track, f.line, f.card, f.P(8)) { Bounds = new Rectangle(pad + f.P(44), y, f.P(150), f.P(34)) };
            hex = new TextBox { MaxLength = 7, BorderStyle = BorderStyle.None, BackColor = f.track, ForeColor = f.text, Font = f.fBase, Bounds = new Rectangle(f.P(10), f.P(8), f.P(130), f.P(20)) };
            hex.TextChanged += (_, _) =>
            {
                if (syncing) return;
                if (CatSprite.TryParse(hex.Text.TrimStart('#'), out var c)) { ToHsv(c, out h, out s, out v); sv.Hue = hueBar.Hue = h; sv.S = s; sv.V = v; Apply(true); }
            };
            field.Controls.Add(hex);
            Controls.Add(field);
            y += f.P(34) + f.P(18);

            if (Cfg.Recent.Count > 0)
            {
                var rl = f.Lbl(Str.T("picker.recent"), f.fBold, f.muted, pad, y, cw); rl.BackColor = f.card; Controls.Add(rl);
                y += f.P(24);
                int sx = pad;
                foreach (var rc in Cfg.Recent)
                {
                    var sw = new Swatch(f) { Fill = rc, Bounds = new Rectangle(sx, y, f.P(26), f.P(26)) };
                    var picked = rc;
                    sw.Click += (_, _) => { ToHsv(picked, out h, out s, out v); sv.Hue = hueBar.Hue = h; sv.S = s; sv.V = v; Apply(false); };
                    Controls.Add(sw);
                    sx += f.P(26) + f.P(6);
                }
            }

            int by = ClientSize.Height - pad - f.P(36);
            var ok = new Pill(f) { Text = Str.T("picker.ok"), Kind = PillKind.Seg, Active = true, Bounds = new Rectangle(pad + cw - f.P(130), by, f.P(130), f.P(36)) };
            ok.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
            var cancel = new Pill(f) { Text = Str.T("picker.cancel"), Kind = PillKind.Seg, Bounds = new Rectangle(pad + cw - f.P(130) - f.P(10) - f.P(110), by, f.P(110), f.P(36)) };
            cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(ok); Controls.Add(cancel);
            AcceptButton = null;
            Apply(false);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (f.bg.GetBrightness() < 0.5f) { int on = 1; DwmSetWindowAttribute(Handle, 20, ref on, 4); }
        }

        void Apply(bool fromHex)
        {
            Result = FromHsv(h, s, v);
            newChip.Fill = Result; newChip.Invalidate();
            if (!fromHex) { syncing = true; hex.Text = "#" + CatSprite.Hex(Result); syncing = false; }
        }
    }

    // Saturation (left to right) and brightness (top to bottom) for the current hue
    sealed class SvBox : Control
    {
        readonly SettingsForm f;
        Bitmap? cache;
        double hue;
        public double S, V;
        public event Action? Changed;
        public double Hue { get => hue; set { hue = value; cache?.Dispose(); cache = null; Invalidate(); } }

        public SvBox(SettingsForm owner)
        {
            f = owner;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            Cursor = Cursors.Cross;
        }

        void Pick(MouseEventArgs e)
        {
            S = Math.Clamp(e.X / (double)(Width - 1), 0, 1);
            V = 1 - Math.Clamp(e.Y / (double)(Height - 1), 0, 1);
            Invalidate(); Changed?.Invoke();
        }
        protected override void OnMouseDown(MouseEventArgs e) { Capture = true; Pick(e); base.OnMouseDown(e); }
        protected override void OnMouseMove(MouseEventArgs e) { if (Capture) Pick(e); base.OnMouseMove(e); }
        protected override void OnMouseUp(MouseEventArgs e) { Capture = false; base.OnMouseUp(e); }

        Bitmap Build()
        {
            var bmp = new Bitmap(Width, Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var data = bmp.LockBits(new Rectangle(0, 0, Width, Height), System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var px = new int[Width * Height];
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                    px[y * Width + x] = FromHsv(hue, x / (double)(Width - 1), 1 - y / (double)(Height - 1)).ToArgb();
            System.Runtime.InteropServices.Marshal.Copy(px, 0, data.Scan0, px.Length);
            bmp.UnlockBits(data);
            return bmp;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(f.card);
            cache ??= Build();
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = RoundRect(r, f.P(10)))
            {
                var state = g.Save();
                g.SetClip(path);
                g.DrawImageUnscaled(cache, 0, 0);
                g.Restore(state);
                using var pen = new Pen(f.line); g.DrawPath(pen, path);
            }
            int cx = (int)(S * (Width - 1)), cy = (int)((1 - V) * (Height - 1)), rr = f.P(7);
            using (var pen = new Pen(Color.White, 2.5f)) g.DrawEllipse(pen, cx - rr, cy - rr, rr * 2, rr * 2);
            using (var pen = new Pen(Color.FromArgb(120, 0, 0, 0), 1f)) g.DrawEllipse(pen, cx - rr - 1, cy - rr - 1, rr * 2 + 2, rr * 2 + 2);
        }

        protected override void Dispose(bool disposing) { if (disposing) cache?.Dispose(); base.Dispose(disposing); }
    }

    // The rainbow bar
    sealed class HueBar : Control
    {
        readonly SettingsForm f;
        public double Hue;
        public event Action? Changed;

        public HueBar(SettingsForm owner)
        {
            f = owner;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            Cursor = Cursors.Hand;
        }

        void Pick(MouseEventArgs e) { Hue = Math.Clamp(e.X / (double)(Width - 1), 0, 1) * 359.99; Invalidate(); Changed?.Invoke(); }
        protected override void OnMouseDown(MouseEventArgs e) { Capture = true; Pick(e); base.OnMouseDown(e); }
        protected override void OnMouseMove(MouseEventArgs e) { if (Capture) Pick(e); base.OnMouseMove(e); }
        protected override void OnMouseUp(MouseEventArgs e) { Capture = false; base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(f.card);
            int th = f.P(12), cy = Height / 2, pad = f.P(9);
            var bar = new Rectangle(pad, cy - th / 2, Width - pad * 2, th);
            var blend = new ColorBlend(7)
            {
                Colors = new[] { Color.Red, Color.Yellow, Color.Lime, Color.Cyan, Color.Blue, Color.Magenta, Color.Red },
                Positions = new[] { 0f, 1f / 6, 2f / 6, 3f / 6, 4f / 6, 5f / 6, 1f },
            };
            using (var br = new LinearGradientBrush(bar, Color.Red, Color.Red, 0f) { InterpolationColors = blend })
            using (var path = RoundRect(bar, th / 2))
                g.FillPath(br, path);
            int tx = pad + (int)(Hue / 359.99 * (Width - pad * 2)), r = f.P(9);
            using (var b = new SolidBrush(FromHsv(Hue, 1, 1))) g.FillEllipse(b, tx - r, cy - r, r * 2, r * 2);
            using (var pen = new Pen(Color.White, 2.5f)) g.DrawEllipse(pen, tx - r, cy - r, r * 2, r * 2);
        }
    }

    // A big colour chip with a caption (the current and the new colour)
    sealed class ColorChip : Control
    {
        readonly SettingsForm f;
        public Color Fill;
        public string Caption = "";

        public ColorChip(SettingsForm owner)
        {
            f = owner;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(f.card);
            using (var path = RoundRect(new Rectangle(0, 0, Width - 1, Height - 1), f.P(10))) using (var b = new SolidBrush(Fill))
            {
                g.FillPath(b, path);
                using var pen = new Pen(f.line); g.DrawPath(pen, path);
            }
            var fore = Fill.GetBrightness() > 0.6f ? Color.FromArgb(40, 40, 50) : Color.White;
            TextRenderer.DrawText(g, Caption, f.fSmall, ClientRectangle, fore, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
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
        public int MaxValue => Max;
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
