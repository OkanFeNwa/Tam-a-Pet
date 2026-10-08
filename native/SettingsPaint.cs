using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// The colour studio page of the settings window: browse every animation of the cat, click a part of it (or pick it from the list)
// to recolour it, and keep looks as presets (create, apply, overwrite, rename, duplicate, delete).
sealed partial class SettingsForm
{
    int paintRow, paintFrame;
    bool paintPlay = true;
    int[]? paintFrames;   // pictures in each row of the sheet
    System.Windows.Forms.Timer? paintT;
    PaintView? paintView;
    Pill? paintRowPill;
    Label? paintInfo;

    // ---- the page ---------------------------------------------------------------------------------

    int BuildPaint(CatProfile cat, int pad, int y, int w)
    {
        LookPresets.EnsureLoaded();
        paintFrames ??= CatSprite.RowFrames();
        if (paintRow >= paintFrames.Length || paintFrames[paintRow] == 0) paintRow = 0;

        var back = new Pill(this) { Text = "‹  " + cat.Display, Bounds = new Rectangle(pad, y - P(8), P(170), P(32)), Kind = PillKind.Outline };
        back.Click += (_, _) => { page = "cat" + cat.Index; bar.Value = 0; Build(); };
        view.Controls.Add(back);
        y += P(36);
        y = Title(Str.T("paint.title"), pad, y, w);

        // the cat, big, with the animation controls beside it
        var vc = AddCard(pad, y, w);
        previewSheet?.Dispose();
        previewSheet = CatSprite.BuildSheet(cat);
        int vs = P(208);
        paintView = new PaintView(this, previewSheet) { Bounds = new Rectangle(P(16), P(16), vs, vs), Row = paintRow, Frame = paintFrame };
        paintView.Pick += part => EditPart(cat, part);
        vc.Controls.Add(paintView);
        int rx = P(16) + vs + P(18), rw = w - rx - P(16);

        var al = Lbl(Str.T("paint.anim"), fBold, text, rx, P(16), rw); al.BackColor = card; vc.Controls.Add(al);
        paintRowPill = new Pill(this) { Kind = PillKind.Seg, Bounds = new Rectangle(rx, P(42), rw, P(34)) };
        paintRowPill.Click += (_, _) =>
        {
            var m = new ContextMenuStrip();
            for (int r = 0; r < paintFrames.Length; r++)
            {
                if (paintFrames[r] == 0) continue;
                int row = r;
                var it = new ToolStripMenuItem(RowLabel(row)) { Checked = row == paintRow };
                it.Click += (_, _) => { paintRow = row; paintFrame = 0; ShowFrame(); };
                m.Items.Add(it);
            }
            MenuRenderer.Apply(m);
            m.Show(paintRowPill, new Point(0, paintRowPill!.Height + P(4)));
        };
        vc.Controls.Add(paintRowPill);
        int half = (rw - P(8)) / 2;
        var prev = new Pill(this) { Text = "‹", Kind = PillKind.Seg, Bounds = new Rectangle(rx, P(84), half, P(34)) };
        var next = new Pill(this) { Text = "›", Kind = PillKind.Seg, Bounds = new Rectangle(rx + half + P(8), P(84), half, P(34)) };
        prev.Click += (_, _) => StepRow(-1);
        next.Click += (_, _) => StepRow(1);
        vc.Controls.Add(prev); vc.Controls.Add(next);
        var play = new Pill(this) { Kind = PillKind.Seg, Active = paintPlay, Bounds = new Rectangle(rx, P(126), rw, P(34)) };
        string PlayText() => (paintPlay ? "‖  " + Str.T("paint.pause") : "▶  " + Str.T("paint.play"));
        play.Text = PlayText();
        play.Click += (_, _) => { paintPlay = !paintPlay; play.Active = paintPlay; play.Text = PlayText(); play.Invalidate(); };
        vc.Controls.Add(play);
        paintInfo = Lbl("", fSmall, muted, rx, P(170), rw); paintInfo.BackColor = card; vc.Controls.Add(paintInfo);
        var hint = Lbl(Str.T("paint.hint"), fSmall, muted, P(16), P(16) + vs + P(10), w - P(32)); hint.BackColor = card; vc.Controls.Add(hint);
        vc.Height = hint.Bottom + P(14);
        y += vc.Height + P(14);
        ShowFrame();
        paintT = new System.Windows.Forms.Timer { Interval = 110 };
        paintT.Tick += (_, _) =>
        {
            if (!paintPlay || paintFrames[paintRow] <= 1) return;
            paintFrame = (paintFrame + 1) % paintFrames[paintRow];
            ShowFrame();
        };
        paintT.Start();

        // the parts, one colour each
        view.Controls.Add(Lbl(Str.T("paint.parts"), fBold, muted, pad + P(2), y, w));
        y += P(28);
        var pc = AddCard(pad, y, w);
        int py = P(14);
        foreach (var d in PartDefs(cat))
        {
            int top = py;
            py = ColorRow(pc, d.Label, d.Get(), d.Presets, d.Set, P(16), py, w - P(32));
            if (d.Auto != null)   // derived from the fur unless the owner picks one: "Auto" goes back to the derived colour
            {
                var auto = new Pill(this) { Text = Str.T("paint.auto"), Kind = PillKind.Seg, Active = d.IsAuto!(), Bounds = new Rectangle(P(16) + w - P(32) - P(60), top - P(6), P(60), P(30)) };
                auto.Click += (_, _) => { d.Auto(); Cfg.Changed(cat); Build(); };
                pc.Controls.Add(auto); auto.BringToFront();   // the label above spans the whole row
            }
        }
        pc.Height = py + P(4);
        y += pc.Height + P(14);

        y = PatternCard(cat, pad, y, w);
        y = AccessoryCard(cat, pad, y, w);
        var resetLook = new Pill(this) { Text = Str.T("look.reset"), Bounds = new Rectangle(pad, y + P(2), P(190), P(34)), Kind = PillKind.Outline };
        resetLook.Click += (_, _) => { cat.ResetLook(); Cfg.Changed(cat); Build(); };
        view.Controls.Add(resetLook);
        y += P(34) + P(22);

        return PresetsSection(cat, pad, y, w);
    }

    void ShowFrame()
    {
        if (paintView == null || paintFrames == null) return;
        paintView.Row = paintRow; paintView.Frame = paintFrame;
        paintView.Invalidate();
        if (paintRowPill != null) { paintRowPill.Text = RowLabel(paintRow) + "  ▾"; paintRowPill.Invalidate(); }
        if (paintInfo != null) paintInfo.Text = string.Format(Str.T("paint.frame"), paintFrame + 1, paintFrames[paintRow]);
    }

    void StepRow(int d)
    {
        int n = paintFrames!.Length, r = paintRow;
        do r = (r + d + n) % n; while (paintFrames[r] == 0);
        paintRow = r; paintFrame = 0; ShowFrame();
    }

    // the rows of the sheet, named from how the pet uses them (the order of the directions is PetWindow's)
    static string RowLabel(int r)
    {
        string A(string k) => Str.T("an." + k);
        string D(string k) => Str.T("dir." + k);
        string[] dirs = { "right", "dr", "down", "dl", "left", "ul", "up", "ur" };
        int[] walk = { 6, 9, 4, 8, 7, 11, 5, 10 }, eat = { 23, 24, 20, 25, 22, 27, 21, 26 }, paw = { 47, 48, 44, 49, 46, 51, 45, 50 };
        int i;
        if ((i = Array.IndexOf(walk, r)) >= 0) return $"{A("walk")} · {D(dirs[i])}";
        if ((i = Array.IndexOf(eat, r)) >= 0) return $"{A("eat")} · {D(dirs[i])}";
        if ((i = Array.IndexOf(paw, r)) >= 0) return $"{A("paw")} · {D(dirs[i])}";
        if (r is >= 12 and <= 19)
        {
            string pose = D(r < 14 ? "front" : r < 16 ? "back" : r < 18 ? "curled" : "stretched");
            return $"{A("sleep")} · {pose} · {D(r % 2 == 0 ? "left" : "right")}";
        }
        return r switch
        {
            0 => A("sit"), 1 => A("stand"), 2 => A("lie"), 3 => A("loaf"),
            >= 28 and <= 31 => $"{A("meow")} {r - 27}", >= 32 and <= 35 => $"{A("yawn")} {r - 31}", >= 36 and <= 38 => $"{A("wash")} {r - 35}",
            39 => $"{A("scratch")} · {D("left")}", 40 => $"{A("scratch")} · {D("right")}",
            41 => $"{A("hiss")} · {D("left")}", 42 => $"{A("hiss")} · {D("right")}",
            52 => A("jump"),
            _ => $"{A("pose")} {r}"
        };
    }

    // ---- the parts --------------------------------------------------------------------------------

    sealed record PartDef(string Key, string Label, Func<Color> Get, Action<Color> Set, Color[] Presets, Action? Auto = null, Func<bool>? IsAuto = null);

    List<PartDef> PartDefs(CatProfile cat)
    {
        var l = new List<PartDef>
        {
            new("fur", Str.T("look.fur"), () => cat.Fur1, c => { cat.Fur1 = c; Cfg.Changed(cat); }, FurPresets),
            new("shade", Str.T("part.shade"), () => cat.Shade ?? CatSprite.Shade(cat.Fur1), c => { cat.Shade = c; Cfg.Changed(cat); }, FurPresets, () => cat.Shade = null, () => cat.Shade == null),
            new("light", Str.T("part.light"), () => cat.Light ?? CatSprite.Light(cat.Fur1), c => { cat.Light = c; Cfg.Changed(cat); }, FurPresets, () => cat.Light = null, () => cat.Light == null),
            new("outline", Str.T("part.outline"), () => cat.Outline ?? Color.FromArgb(18, 14, 20), c => { cat.Outline = c; Cfg.Changed(cat); }, FurPresets, () => cat.Outline = null, () => cat.Outline == null),
            new("eyes", Str.T("look.eyes"), () => cat.Eyes, c => { cat.Eyes = c; Cfg.Changed(cat); }, EyePresets),
            new("nose", Str.T("look.nose"), () => cat.Nose, c => { cat.Nose = c; Cfg.Changed(cat); }, NosePresets),
            new("ears", Str.T("look.ears"), () => cat.Ears, c => { cat.Ears = c; Cfg.Changed(cat); }, EarPresets),
        };
        if (cat.Pattern != "none") l.Add(new("var", Str.T("look.fur3"), () => cat.Fur3, c => { cat.Fur3 = c; Cfg.Changed(cat); }, FurPresets));
        return l;
    }

    // a click on the cat: the picker for the part under the mouse
    void EditPart(CatProfile cat, string part)
    {
        var d = PartDefs(cat).FirstOrDefault(p => p.Key == part);
        if (d == null) return;
        using var dlg = new ColorPicker(this, d.Get());
        if (dlg.ShowDialog(this) == DialogResult.OK) { Cfg.AddRecent(dlg.Result); d.Set(dlg.Result); Build(); }
    }

    // ---- pattern and accessory (also used by the cat's own page) ----------------------------------------

    int PatternCard(CatProfile cat, int pad, int y, int w)
    {
        view.Controls.Add(Lbl(Str.T("pattern.title"), fBold, muted, pad + P(2), y, w));
        y += P(28);
        var ptc = AddCard(pad, y, w);
        string[] pats = { "none", "tabby", "patches", "calico" };
        int ptw = (w - P(32) - P(8) * 3) / 4;
        for (int i = 0; i < pats.Length; i++)
        {
            string pn2 = pats[i];
            var pp = new Pill(this) { Text = Str.T("pattern." + pn2), Kind = PillKind.Seg, Active = cat.Pattern == pn2, Bounds = new Rectangle(P(16) + i * (ptw + P(8)), P(14), ptw, P(34)) };
            pp.Click += (_, _) =>
            {
                if (cat.Pattern != pn2) cat.Fur3 = CatSprite.DefaultVariantColor(pn2, cat.Fur3);   // each variant starts with its own colour
                cat.Pattern = pn2; Cfg.Changed(cat); Build();
            };
            ptc.Controls.Add(pp);
        }
        ptc.Height = P(62);
        return y + ptc.Height + P(14);
    }

    int AccessoryCard(CatProfile cat, int pad, int y, int w)
    {
        if (Cfg.ClassicSprites) return y;   // accessories: only the new sprites have them
        view.Controls.Add(Lbl(Str.T("acc.title"), fBold, muted, pad + P(2), y, w));
        y += P(28);
        var acc = AddCard(pad, y, w);
        var ids = new List<string> { "" };
        ids.AddRange(CatSprite.Accessories);
        string AccName(string id) => id == "" ? Str.T("acc.none") : Str.T("acc." + id);
        var accDrop = new Pill(this) { Text = AccName(cat.Accessory) + "  ▾", Kind = PillKind.Seg, Bounds = new Rectangle(P(16), P(14), w - P(32), P(36)) };
        accDrop.Click += (_, _) =>
        {
            var m = new ContextMenuStrip();
            foreach (var id in ids)
            {
                var aid = id;
                var it = new ToolStripMenuItem(AccName(aid)) { Checked = cat.Accessory == aid };
                it.Click += (_, _) => { cat.Accessory = aid; Cfg.Changed(cat); Build(); };
                m.Items.Add(it);
            }
            MenuRenderer.Apply(m);
            m.Show(accDrop, new Point(0, accDrop.Height + P(4)));
        };
        acc.Controls.Add(accDrop);
        acc.Height = P(14) + P(36) + P(14);
        return y + acc.Height + P(14);
    }

    // ---- presets ---------------------------------------------------------------------------------

    static bool Matches(LookPreset p, CatProfile c) =>
        p.Fur1.ToArgb() == c.Fur1.ToArgb() && p.Shade?.ToArgb() == c.Shade?.ToArgb() && p.Light?.ToArgb() == c.Light?.ToArgb() && p.Outline?.ToArgb() == c.Outline?.ToArgb()
        && p.Ears.ToArgb() == c.Ears.ToArgb() && p.Nose.ToArgb() == c.Nose.ToArgb() && p.Eyes.ToArgb() == c.Eyes.ToArgb()
        && p.Pattern == c.Pattern && p.Accessory == c.Accessory && (p.Pattern == "none" || p.Fur3.ToArgb() == c.Fur3.ToArgb());

    string? AskName(string title, string initial)
    {
        using var d = new NameDialog(this, title, initial);
        return d.ShowDialog(this) == DialogResult.OK ? d.Result : null;
    }

    int PresetsSection(CatProfile cat, int pad, int y, int w)
    {
        view.Controls.Add(Lbl(Str.T("presets.title"), fBold, muted, pad + P(2), y, w));
        y += P(28);

        var save = new Pill(this) { Text = Str.T("preset.save"), Kind = PillKind.Seg, Active = true, Bounds = new Rectangle(pad, y, w, P(38)) };
        save.Click += (_, _) =>
        {
            if (LookPresets.User.Count >= LookPresets.MaxUser) return;
            var name = AskName(Str.T("preset.name"), LookPresets.Unique(Str.T("preset.new")));
            if (name == null) return;
            LookPresets.User.Add(LookPreset.From(cat, LookPresets.Unique(name)));
            LookPresets.Save(); Build();
        };
        view.Controls.Add(save);
        y += P(38) + P(12);

        var all = LookPresets.User.AsEnumerable().Reverse().Concat(LookPresets.Builtin).ToList();   // newest of the user's first, then the templates
        if (LookPresets.User.Count == 0)
        {
            view.Controls.Add(Lbl(Str.T("preset.empty"), fSmall, muted, pad + P(2), y, w));
            y += P(30);
        }
        foreach (var pr in all)
        {
            var preset = pr;
            var rc = AddCard(pad, y, w);
            rc.Height = P(66);
            rc.Controls.Add(Thumb(preset.Sample(), new Rectangle(P(10), P(9), P(66), P(48))));
            int more = w - P(16) - P(36), apply = more - P(8) - P(84), nx = P(10) + P(66) + P(14);
            var nm = Lbl(preset.Display, fBold, text, nx, P(13), apply - nx - P(8)); nm.BackColor = card; nm.AutoEllipsis = true; nm.Height = P(20); rc.Controls.Add(nm);
            var sub = Lbl(preset.Builtin ? Str.T("preset.builtin") : (preset.Pattern == "none" ? "" : Str.T("pattern." + preset.Pattern)), fSmall, muted, nx, P(35), apply - nx - P(8)); sub.BackColor = card; rc.Controls.Add(sub);

            bool on = Matches(preset, cat);
            var ap = new Pill(this) { Text = on ? "✓" : Str.T("preset.apply"), Kind = PillKind.Seg, Active = on, Bounds = new Rectangle(apply, P(16), P(84), P(34)) };
            ap.Click += (_, _) => { preset.Apply(cat); Cfg.Changed(cat); Build(); };
            rc.Controls.Add(ap);

            var mb = new Pill(this) { Text = "⋯", Kind = PillKind.Seg, Bounds = new Rectangle(more, P(16), P(36), P(34)) };
            mb.Click += (_, _) =>
            {
                var m = new ContextMenuStrip();
                m.Items.Add(new ToolStripMenuItem(Str.T("preset.apply"), null, (_, _) => { preset.Apply(cat); Cfg.Changed(cat); Build(); }));
                if (!preset.Builtin)
                {
                    m.Items.Add(new ToolStripMenuItem(Str.T("preset.update"), null, (_, _) =>
                    {
                        var fresh = LookPreset.From(cat, preset.Name);
                        int i = LookPresets.User.IndexOf(preset);
                        if (i >= 0) { LookPresets.User[i] = fresh; LookPresets.Save(); Build(); }
                    }));
                    m.Items.Add(new ToolStripMenuItem(Str.T("preset.rename"), null, (_, _) =>
                    {
                        var n = AskName(Str.T("preset.name"), preset.Name);
                        if (n == null) return;
                        preset.Name = LookPresets.Unique(n, preset); LookPresets.Save(); Build();
                    }));
                }
                m.Items.Add(new ToolStripMenuItem(Str.T("preset.dup"), null, (_, _) =>
                {
                    if (LookPresets.User.Count >= LookPresets.MaxUser) return;
                    var copy = preset.Copy(LookPresets.Unique(preset.Display));
                    int i = LookPresets.User.IndexOf(preset);
                    LookPresets.User.Insert(i >= 0 ? i + 1 : LookPresets.User.Count, copy);
                    LookPresets.Save(); Build();
                }));
                if (!preset.Builtin)
                    m.Items.Add(new ToolStripMenuItem(Str.T("preset.del"), null, (_, _) =>
                    {
                        if (MessageBox.Show(this, string.Format(Str.T("preset.del.ask"), preset.Name), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                        LookPresets.User.Remove(preset); LookPresets.Save(); Build();
                    }));
                MenuRenderer.Apply(m);
                m.Show(mb, new Point(0, mb.Height + P(4)));
            };
            rc.Controls.Add(mb);
            y += rc.Height + P(10);
        }
        return y + P(4);
    }

    // ---- controls ---------------------------------------------------------------------------------

    // The cat, big, on its card: shows one picture of one row of the sheet; a click says which part of the cat was hit
    sealed class PaintView : Control
    {
        readonly SettingsForm f;
        readonly Bitmap sheet;
        public int Row, Frame;
        public event Action<string>? Pick;

        public PaintView(SettingsForm owner, Bitmap sheetBitmap)
        {
            f = owner; sheet = sheetBitmap; BackColor = f.card; Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
        }

        int Scale => Math.Max(1, Math.Min((Width - f.P(16)) / 32, (Height - f.P(16)) / 32));

        Rectangle Dest()
        {
            int s = Scale;   // the paws stand on the same line in every row
            return new Rectangle((Width - 32 * s) / 2, (Height - 32 * s) / 2 + CatSprite.FootDy(Row) * s, 32 * s, 32 * s);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(BackColor);
            using (var path = RoundRect(new Rectangle(0, 0, Width - 1, Height - 1), f.P(12))) using (var b = new SolidBrush(f.track))
                g.FillPath(b, path);
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(sheet, Dest(), Frame * 32, Row * 32, 32, 32, GraphicsUnit.Pixel);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            var d = Dest(); int s = Scale;
            int cx = (e.X - d.X) / s, cy = (e.Y - d.Y) / s;
            if (e.X < d.X || e.Y < d.Y || cx >= 32 || cy >= 32) return;
            if (CatSprite.PartAt(Frame * 32 + cx, Row * 32 + cy) is { } part) Pick?.Invoke(part);
        }
    }

    // asks for a name (a preset's): a small window in the same style as the colour picker
    sealed class NameDialog : Form
    {
        public string Result = "";
        readonly SettingsForm f;
        readonly TextBox tb;

        public NameDialog(SettingsForm owner, string title, string initial)
        {
            f = owner;
            Text = title; Font = f.fBase; BackColor = f.bg; Icon = f.appIcon;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(f.P(360), f.P(158));
            var l = f.Lbl(title, f.fBold, f.text, f.P(20), f.P(18), f.P(320)); l.BackColor = f.bg; Controls.Add(l);
            var field = new Card(f.track, f.line, f.bg, f.P(8)) { Bounds = new Rectangle(f.P(20), f.P(48), f.P(320), f.P(34)) };
            tb = new TextBox { Text = initial, MaxLength = 24, BorderStyle = BorderStyle.None, BackColor = f.track, ForeColor = f.text, Font = f.fBase, Bounds = new Rectangle(f.P(10), f.P(8), f.P(300), f.P(20)) };
            field.Controls.Add(tb);
            Controls.Add(field);
            var ok = new Pill(f) { Text = Str.T("picker.ok"), Kind = PillKind.Seg, Active = true, Bounds = new Rectangle(f.P(20) + f.P(156), f.P(104), f.P(164), f.P(36)) };
            var cancel = new Pill(f) { Text = Str.T("picker.cancel"), Kind = PillKind.Outline, Bounds = new Rectangle(f.P(20), f.P(104), f.P(148), f.P(36)) };
            ok.Click += (_, _) => Accept();
            cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;
            Controls.Add(ok); Controls.Add(cancel);
            ActiveControl = tb;
            Shown += (_, _) => tb.SelectAll();
        }

        void Accept() { Result = tb.Text.Trim(); if (Result != "") DialogResult = DialogResult.OK; }

        protected override bool ProcessCmdKey(ref Message msg, Keys key)
        {
            if (key == Keys.Enter) { Accept(); return true; }
            if (key == Keys.Escape) { DialogResult = DialogResult.Cancel; return true; }
            return base.ProcessCmdKey(ref msg, key);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (f.bg.GetBrightness() < 0.5f) { int on = 1; DwmSetWindowAttribute(Handle, 20, ref on, 4); }
        }
    }
}
