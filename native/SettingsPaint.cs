using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// The colour studio page of the settings window: paint the cat by hand, pixel by pixel, one picture of one animation at a time,
// and keep looks as presets (create, apply, overwrite, rename, duplicate, delete).
// It opens the window full screen: the cat on the left, the tools on the right (one column in the small window).
sealed partial class SettingsForm
{
    enum PaintTool { Brush, Eraser, Pick }

    int paintRow, paintFrame, paintSize = 1, paintCat = -1, paintBuiltW;
    bool paintPlay = true, inPaintPage;
    PaintTool paintTool = PaintTool.Brush;
    Color brushColor = Color.FromArgb(0xE0, 0x52, 0x5A);
    int[]? paintFrames;   // pictures in each row of the sheet
    System.Windows.Forms.Timer? paintT, paintSaveT;
    PaintView? paintView;
    Pill? paintRowPill, paintPlayPill;
    Label? paintInfo;
    ToolTip? paintTip;
    readonly Dictionary<PaintTool, Pill> toolPills = new();
    readonly Dictionary<int, Pill> sizePills = new();
    SvBox? pSv; HueBar? pHue; ColorChip? pChip; TextBox? pHex; Panel? pRecent;
    double pH, pS, pV;
    bool pSync;
    CatProfile? paintDirty;   // painted but not saved yet

    // the window goes full screen when the studio opens and back to its size when it closes
    void SyncPaintWindow()
    {
        bool isPaint = page.StartsWith("paint");
        if (isPaint == inPaintPage) return;
        inPaintPage = isPaint;
        MaximizeBox = isPaint;
        WindowState = isPaint ? FormWindowState.Maximized : FormWindowState.Normal;
    }

    protected override void OnClientSizeChanged(EventArgs e)
    {
        base.OnClientSizeChanged(e);
        if (IsHandleCreated && page.StartsWith("paint") && host.ClientSize.Width != paintBuiltW) BeginInvoke(Build);   // maximised or restored by hand: lay the page out again
    }

    // ---- the page ---------------------------------------------------------------------------------

    int BuildPaint(CatProfile cat, int pad, int y, int w)
    {
        LookPresets.EnsureLoaded();
        paintFrames ??= CatSprite.RowFrames();
        if (paintRow >= paintFrames.Length || paintFrames[paintRow] == 0) paintRow = 0;
        if (paintCat != cat.Index) { paintCat = cat.Index; undo.Clear(); }
        toolPills.Clear(); sizePills.Clear();
        paintTip?.Dispose(); paintTip = new ToolTip();
        paintBuiltW = host.ClientSize.Width;
        ToHsv(brushColor, out pH, out pS, out pV);

        var back = new Pill(this) { Text = cat.Display, Glyph = "", Bounds = new Rectangle(pad, y - P(8), P(170), P(32)), Kind = PillKind.Outline };
        back.Click += (_, _) => { page = "cat" + cat.Index; bar.Value = 0; Build(); };
        view.Controls.Add(back);
        y += P(36);
        y = Title(Str.T("paint.title"), pad, y, w);

        bool wide = w >= P(860);   // full screen: the cat on the left, the tools on the right
        int rw = wide ? P(440) : w, lw = wide ? w - rw - P(16) : w;
        int rx = wide ? pad + lw + P(16) : pad;

        // the cat, big, to paint on pixel by pixel
        var vc = AddCard(pad, y, lw);
        previewSheet?.Dispose();
        previewSheet = CatSprite.BuildSheet(cat, false);   // the recoloured cat without the hand painting: PaintView draws that on top itself
        int vs = wide ? Math.Min(lw - P(32), Math.Max(P(320), host.ClientSize.Height - P(190))) : lw - P(32);
        paintView = new PaintView(this, previewSheet, () => cat.Hand) { Bounds = new Rectangle((lw - vs) / 2, P(16), vs, vs), Row = paintRow, Frame = paintFrame, Brush = paintSize, Outline = paintTool != PaintTool.Pick };
        paintView.Down += (px, py) => PaintDown(cat, px, py);
        paintView.Move += (px, py) => PaintMove(cat, px, py);
        paintView.Up += () => PaintUp(cat);
        vc.Controls.Add(paintView);
        vc.Height = vs + P(32);
        int bottom = y + vc.Height + P(14);
        if (!wide) y = bottom;

        // tools
        int iw = rw - P(32);
        var tc = AddCard(rx, y, rw);
        int cy = P(16);
        var tools = new (PaintTool t, string glyph, string key)[] { (PaintTool.Brush, "", "tool.brush"), (PaintTool.Eraser, "", "tool.eraser"), (PaintTool.Pick, "", "tool.pick") };
        int tw = (iw - P(8) * 2) / 3;
        for (int i = 0; i < tools.Length; i++)
        {
            var (t, glyph, key) = tools[i];
            var tp = new Pill(this) { Glyph = glyph, Kind = PillKind.Seg, Active = paintTool == t, Bounds = new Rectangle(P(16) + i * (tw + P(8)), cy, tw, P(42)) };
            tp.Click += (_, _) => SetTool(t);
            Tip(tp, Str.T(key));
            toolPills[t] = tp; tc.Controls.Add(tp);
        }
        cy += P(42) + P(12);

        var sl = Lbl(Str.T("paint.size"), fBold, text, P(16), cy + P(8), P(110)); sl.BackColor = card; tc.Controls.Add(sl);
        int sw = P(52), sx = P(16) + P(110);
        for (int s = 1; s <= 3; s++)
        {
            int size = s;
            var sp = new Pill(this) { Text = s.ToString(), Kind = PillKind.Seg, Active = paintSize == s, Bounds = new Rectangle(sx + (s - 1) * (sw + P(6)), cy, sw, P(36)) };
            sp.Click += (_, _) => SetSize(size);
            sizePills[s] = sp; tc.Controls.Add(sp);
        }
        cy += P(36) + P(12);

        var actions = new (string glyph, string key, Action act)[]
        {
            ("", "paint.undo", () => Undo(cat)),
            ("", "paint.clearframe", () => ClearFrame(cat)),
            ("", "paint.clearall", () =>
            {
                if (cat.Hand == null) return;
                if (MessageBox.Show(this, string.Format(Str.T("paint.clearall.ask"), cat.Display), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                undo.Clear(); HandPaint.Clear(cat); Cfg.Changed(cat); paintView?.Invalidate();
            }),
        };
        for (int i = 0; i < actions.Length; i++)
        {
            var (glyph, key, act) = actions[i];
            var ap = new Pill(this) { Glyph = glyph, Kind = PillKind.Seg, Bounds = new Rectangle(P(16) + i * (tw + P(8)), cy, tw, P(42)) };
            ap.Click += (_, _) => act();
            Tip(ap, Str.T(key));
            tc.Controls.Add(ap);
        }
        cy += P(42) + P(18);

        // which animation, which picture of it
        var lblAnim = Lbl(Str.T("paint.anim"), fBold, text, P(16), cy, iw); lblAnim.BackColor = card; tc.Controls.Add(lblAnim);
        cy += P(24);
        paintRowPill = new Pill(this) { Kind = PillKind.Seg, Bounds = new Rectangle(P(16), cy, iw, P(36)) };
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
        tc.Controls.Add(paintRowPill);
        cy += P(36) + P(10);

        paintInfo = Lbl("", fBold, muted, P(16), cy, iw, ContentAlignment.TopCenter); paintInfo.BackColor = card; tc.Controls.Add(paintInfo);
        cy += P(24);
        var prevFr = new Pill(this) { Glyph = "", Kind = PillKind.Seg, Bounds = new Rectangle(P(16), cy, tw, P(42)) };
        paintPlayPill = new Pill(this) { Kind = PillKind.Seg, Bounds = new Rectangle(P(16) + tw + P(8), cy, tw, P(42)) };
        var nextFr = new Pill(this) { Glyph = "", Kind = PillKind.Seg, Bounds = new Rectangle(P(16) + (tw + P(8)) * 2, cy, tw, P(42)) };
        prevFr.Click += (_, _) => StepFrame(-1);
        nextFr.Click += (_, _) => StepFrame(1);
        paintPlayPill.Click += (_, _) => SetPlay(!paintPlay);
        tc.Controls.Add(prevFr); tc.Controls.Add(paintPlayPill); tc.Controls.Add(nextFr);
        SetPlay(paintPlay);
        cy += P(42) + P(12);
        var hint = Lbl(Str.T("paint.hint"), fSmall, muted, P(16), cy, iw); hint.BackColor = card; tc.Controls.Add(hint);
        tc.Height = hint.Bottom + P(14);
        y += tc.Height + P(14);
        ShowFrame();
        paintT?.Dispose();
        paintT = new System.Windows.Forms.Timer { Interval = 110 };
        paintT.Tick += (_, _) =>
        {
            if (!paintPlay || painting || paintFrames[paintRow] <= 1) return;
            paintFrame = (paintFrame + 1) % paintFrames[paintRow];
            ShowFrame();
        };
        paintT.Start();

        // the brush colour: a picker, and the colours used lately
        y = BrushColorCard(rx, y, rw);

        y = PatternCard(cat, rx, y, rw);
        var resetLook = new Pill(this) { Text = Str.T("look.reset"), Bounds = new Rectangle(rx, y + P(2), P(190), P(34)), Kind = PillKind.Outline };
        resetLook.Click += (_, _) => { cat.ResetLook(); Cfg.Changed(cat); Build(); };
        view.Controls.Add(resetLook);
        y += P(34) + P(22);

        y = PresetsSection(cat, rx, y, rw);
        return Math.Max(y, bottom);
    }

    void Tip(Control c, string text) => paintTip?.SetToolTip(c, text);

    // ---- brush colour ------------------------------------------------------------------------------

    int BrushColorCard(int x, int y, int w)
    {
        view.Controls.Add(Lbl(Str.T("paint.brushcolor"), fBold, muted, x + P(2), y, w));
        y += P(28);
        var cc = AddCard(x, y, w);
        int iw = w - P(32), cy = P(16);
        pSv = new SvBox(this) { Bounds = new Rectangle(P(16), cy, iw, P(150)), Hue = pH, S = pS, V = pV };
        cy += P(150) + P(8);
        pHue = new HueBar(this) { Bounds = new Rectangle(P(16), cy, iw, P(24)), Hue = pH };
        cy += P(24) + P(10);
        pChip = new ColorChip(this) { Fill = brushColor, Bounds = new Rectangle(P(16), cy, P(64), P(34)) };
        var hl = Lbl("Hex", fBold, muted, P(16) + P(76), cy + P(8), P(36)); hl.BackColor = card;
        var field = new Card(track, line, card, P(8)) { Bounds = new Rectangle(P(16) + P(76) + P(40), cy, P(130), P(34)) };
        pHex = new TextBox { MaxLength = 7, BorderStyle = BorderStyle.None, BackColor = track, ForeColor = text, Font = fBase, Bounds = new Rectangle(P(10), P(8), P(110), P(20)), Text = "#" + CatSprite.Hex(brushColor) };
        field.Controls.Add(pHex);
        pSv.Changed += () => { pS = pSv.S; pV = pSv.V; ApplyHsv(); };
        pHue.Changed += () => { pH = pHue.Hue; pSv.Hue = pH; ApplyHsv(); };
        pHex.TextChanged += (_, _) => { if (!pSync && CatSprite.TryParse(pHex.Text.TrimStart('#'), out var c)) SetBrush(c, false); };
        cc.Controls.Add(pSv); cc.Controls.Add(pHue); cc.Controls.Add(pChip); cc.Controls.Add(hl); cc.Controls.Add(field);
        cy += P(34) + P(16);

        var rl = Lbl(Str.T("picker.recent"), fBold, muted, P(16), cy, iw); rl.BackColor = card; cc.Controls.Add(rl);
        cy += P(24);
        pRecent = new Panel { Bounds = new Rectangle(P(16), cy, iw, P(30)), BackColor = card };
        cc.Controls.Add(pRecent);
        FillRecent();
        cc.Height = cy + P(30) + P(14);
        return y + cc.Height + P(14);
    }

    void ApplyHsv()
    {
        brushColor = FromHsv(pH, pS, pV);
        if (pChip != null) { pChip.Fill = brushColor; pChip.Invalidate(); }
        if (pHex != null) { pSync = true; pHex.Text = "#" + CatSprite.Hex(brushColor); pSync = false; }
    }

    // a colour chosen from outside the picker (recent colour, eyedropper, hex): the picker follows
    void SetBrush(Color c, bool moveHex)
    {
        brushColor = Color.FromArgb(255, c);
        ToHsv(brushColor, out pH, out pS, out pV);
        if (pSv != null) { pSv.Hue = pH; pSv.S = pS; pSv.V = pV; pSv.Invalidate(); }
        if (pHue != null) { pHue.Hue = pH; pHue.Invalidate(); }
        if (pChip != null) { pChip.Fill = brushColor; pChip.Invalidate(); }
        if (moveHex && pHex != null) { pSync = true; pHex.Text = "#" + CatSprite.Hex(brushColor); pSync = false; }
    }

    void FillRecent()
    {
        if (pRecent == null) return;
        foreach (Control c in pRecent.Controls.Cast<Control>().ToList()) c.Dispose();
        int sx = 0, sz = P(26);
        foreach (var rc in Cfg.Recent)
        {
            var picked = rc;
            var sw = new Swatch(this) { Fill = rc, Selected = rc.ToArgb() == brushColor.ToArgb(), Bounds = new Rectangle(sx, 0, sz, sz) };
            sw.Click += (_, _) => { SetBrush(picked, true); FillRecent(); };
            pRecent.Controls.Add(sw);
            sx += sz + P(6);
        }
    }

    // after a stroke: the colour just used goes first among the recent ones
    void RememberColor()
    {
        if (Cfg.Recent.Count > 0 && (Cfg.Recent[0].ToArgb() & 0xFFFFFF) == (brushColor.ToArgb() & 0xFFFFFF)) return;
        Cfg.AddRecent(Color.FromArgb(255, brushColor));
        FillRecent();
    }

    // ---- tool, size, animation, picture ------------------------------------------------------------

    // changing tool or size only repaints the buttons: no page rebuild
    void SetTool(PaintTool t)
    {
        paintTool = t;
        foreach (var (k, p) in toolPills) { p.Active = k == t; p.Invalidate(); }
        if (paintView != null) { paintView.Outline = t != PaintTool.Pick; paintView.Invalidate(); }
    }

    void SetSize(int s)
    {
        paintSize = s;
        foreach (var (k, p) in sizePills) { p.Active = k == s; p.Invalidate(); }
        if (paintView != null) { paintView.Brush = s; paintView.Invalidate(); }
    }

    void SetPlay(bool on)
    {
        paintPlay = on;
        if (paintPlayPill == null) return;
        paintPlayPill.Active = on;
        paintPlayPill.Glyph = on ? "" : "";
        Tip(paintPlayPill, Str.T(on ? "paint.pause" : "paint.play"));
        paintPlayPill.Invalidate();
    }

    void ShowFrame()
    {
        if (paintView == null || paintFrames == null) return;
        paintView.Row = paintRow; paintView.Frame = paintFrame;
        paintView.Invalidate();
        if (paintRowPill != null) { paintRowPill.Text = RowLabel(paintRow) + "  ▾"; paintRowPill.Invalidate(); }
        if (paintInfo != null) paintInfo.Text = string.Format(Str.T("paint.frame"), paintFrame + 1, paintFrames[paintRow]);
    }

    // one picture at a time (this stops the animation: you are working on a picture)
    void StepFrame(int d)
    {
        SetPlay(false);
        int n = Math.Max(1, paintFrames![paintRow]);
        paintFrame = (paintFrame + d + n) % n;
        ShowFrame();
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

    // ---- painting ----------------------------------------------------------------------------------

    bool painting;
    Point lastDab;
    List<(int x, int y, int old)>? stroke;   // the pixels the current stroke changed, with their previous colour (0 = nothing)
    readonly List<List<(int x, int y, int old)>> undo = new();

    void PaintDown(CatProfile cat, int px, int py)
    {
        if (paintTool == PaintTool.Pick)
        {
            if (paintView!.ColorAt(paintFrame * 32 + px, paintRow * 32 + py) is Color c) { SetBrush(c, true); FillRecent(); SetTool(PaintTool.Brush); }
            return;
        }
        SetPlay(false);   // you cannot paint on a moving picture
        painting = true; stroke = new(); lastDab = new Point(px, py);
        Dab(cat, px, py);
        paintView!.Invalidate();
    }

    void PaintMove(CatProfile cat, int px, int py)
    {
        if (!painting) return;
        // every pixel on the line from the last position: a fast mouse does not leave gaps
        int x0 = lastDab.X, y0 = lastDab.Y, dx = Math.Abs(px - x0), dy = -Math.Abs(py - y0), sx = x0 < px ? 1 : -1, sy = y0 < py ? 1 : -1, err = dx + dy;
        while (true)
        {
            Dab(cat, x0, y0);
            if (x0 == px && y0 == py) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
        lastDab = new Point(px, py);
        paintView!.Invalidate();
    }

    void Dab(CatProfile cat, int cx, int cy)
    {
        int off = (paintSize - 1) / 2;
        for (int j = 0; j < paintSize; j++)
            for (int i = 0; i < paintSize; i++)
            {
                int x = cx - off + i, y = cy - off + j;
                if (x < 0 || y < 0 || x >= 32 || y >= 32) continue;
                int sx = paintFrame * 32 + x, sy = paintRow * 32 + y;
                if (!CatSprite.Paintable(sx, sy)) continue;   // only on the cat itself, never on its outline
                var hand = paintTool == PaintTool.Eraser ? cat.Hand : HandPaint.Ensure(cat);
                if (hand == null) continue;
                int old = hand.GetPixel(sx, sy).ToArgb(), now = paintTool == PaintTool.Eraser ? 0 : Color.FromArgb(255, brushColor).ToArgb();
                if (old == now) continue;
                stroke!.Add((sx, sy, old));
                hand.SetPixel(sx, sy, Color.FromArgb(now));
            }
    }

    // the stroke is done: saving it and telling the pet is deferred a little, so strokes in quick succession never wait for the disk
    void PaintUp(CatProfile cat)
    {
        if (!painting) return;
        painting = false;
        if (stroke is { Count: > 0 })
        {
            undo.Add(stroke); if (undo.Count > 60) undo.RemoveAt(0);
            if (paintTool == PaintTool.Brush) RememberColor();
            MarkDirty(cat);
        }
        stroke = null;
    }

    void MarkDirty(CatProfile cat)
    {
        paintDirty = cat;
        if (paintSaveT == null) { paintSaveT = new System.Windows.Forms.Timer { Interval = 600 }; paintSaveT.Tick += (_, _) => FlushPaint(); }
        paintSaveT.Stop(); paintSaveT.Start();
    }

    void FlushPaint()
    {
        paintSaveT?.Stop();
        if (paintDirty is not { } c) return;
        paintDirty = null;
        HandPaint.Save(c); Cfg.Changed(c);
    }

    void Undo(CatProfile cat)
    {
        if (undo.Count == 0) return;
        var s = undo[^1]; undo.RemoveAt(undo.Count - 1);
        var hand = HandPaint.Ensure(cat);
        foreach (var (x, y, old) in s) hand.SetPixel(x, y, Color.FromArgb(old));
        MarkDirty(cat);
        paintView?.Invalidate();
    }

    void ClearFrame(CatProfile cat)
    {
        if (cat.Hand == null) return;
        var s = new List<(int x, int y, int old)>();
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                int sx = paintFrame * 32 + x, sy = paintRow * 32 + y, old = cat.Hand.GetPixel(sx, sy).ToArgb();
                if (old == 0) continue;
                s.Add((sx, sy, old)); cat.Hand.SetPixel(sx, sy, Color.Transparent);
            }
        if (s.Count == 0) return;
        undo.Add(s);
        MarkDirty(cat);
        paintView?.Invalidate();
    }

    // ---- pattern (also used by the cat's own page) ------------------------------------------------------

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

    // a grid: the cat in the preset's colours and its name; a click applies it, "⋯" has the rest
    int PresetsSection(CatProfile cat, int x, int y, int w)
    {
        view.Controls.Add(Lbl(Str.T("presets.title"), fBold, muted, x + P(2), y, w));
        y += P(28);

        var save = new Pill(this) { Text = Str.T("preset.save").TrimStart('+', ' '), Glyph = "", Kind = PillKind.Seg, Active = true, Bounds = new Rectangle(x, y, w, P(38)) };
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

        if (LookPresets.User.Count == 0)
        {
            view.Controls.Add(Lbl(Str.T("preset.empty"), fSmall, muted, x + P(2), y, w));
            y += P(30);
        }
        var all = LookPresets.User.AsEnumerable().Reverse().Concat(LookPresets.Builtin).ToList();   // newest of the user's first, then the templates
        int gap = P(10), cols = Math.Max(2, (w + gap) / (P(140) + gap)), cw = (w - gap * (cols - 1)) / cols, ch = P(132), n = 0;
        foreach (var pr in all)
        {
            var preset = pr;
            int cx = x + (n % cols) * (cw + gap), cyy = y + (n / cols) * (ch + gap);
            n++;
            bool on = Matches(preset, cat);
            var cell = new Card(card, on ? accent : line, bg, P(14)) { Bounds = new Rectangle(cx, cyy, cw, ch) };
            view.Controls.Add(cell);
            int tw = Math.Min(cw - P(24), P(112)), th = P(72);
            cell.Controls.Add(Thumb(preset.Sample(), new Rectangle((cw - tw) / 2, P(10), tw, th)));
            var nm = Lbl(preset.Display, fBold, text, P(8), P(10) + th + P(8), cw - P(16), ContentAlignment.TopCenter); nm.BackColor = card; nm.AutoEllipsis = true; nm.Height = P(20); cell.Controls.Add(nm);
            var sub = Lbl(preset.Builtin ? Str.T("preset.builtin") : (preset.Pattern == "none" ? " " : Str.T("pattern." + preset.Pattern)), fSmall, muted, P(8), P(10) + th + P(28), cw - P(16), ContentAlignment.TopCenter); sub.BackColor = card; cell.Controls.Add(sub);
            Clickable(cell, () => { preset.Apply(cat); Cfg.Changed(cat); Build(); });

            var mb = new Pill(this) { Glyph = "", Kind = PillKind.Seg, Bounds = new Rectangle(cw - P(8) - P(34), P(8), P(34), P(30)) };
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
                        var nn = AskName(Str.T("preset.name"), preset.Name);
                        if (nn == null) return;
                        preset.Name = LookPresets.Unique(nn, preset); LookPresets.Save(); Build();
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
            cell.Controls.Add(mb);   // added after Clickable: a click on "⋯" opens its menu instead of applying the preset
            mb.BringToFront();
        }
        int rows = (all.Count + cols - 1) / cols;
        return y + rows * (ch + gap) + P(4);
    }

    // ---- controls ---------------------------------------------------------------------------------

    // The cat, big, on its card: shows one picture of one row of the sheet, with what was painted by hand on top.
    // The mouse reports positions in pixels of that picture (0-31, or outside while dragging out of it).
    sealed class PaintView : Control
    {
        readonly SettingsForm f;
        readonly Bitmap sheet;
        readonly Func<Bitmap?> hand;
        public int Row, Frame, Brush = 1;
        public bool Outline = true;   // show where the brush would paint
        public event Action<int, int>? Down, Move;
        public event Action? Up;
        Point hover = new(-100, -100);
        bool drag;

        public PaintView(SettingsForm owner, Bitmap sheetBitmap, Func<Bitmap?> handLayer)
        {
            f = owner; sheet = sheetBitmap; hand = handLayer; BackColor = f.card; Cursor = Cursors.Cross;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
        }

        int Scale => Math.Max(1, Math.Min((Width - f.P(8)) / 32, (Height - f.P(8)) / 32));

        Rectangle Dest()
        {
            int s = Scale;   // the whole 32x32 picture, as it is in the sheet
            return new Rectangle((Width - 32 * s) / 2, (Height - 32 * s) / 2, 32 * s, 32 * s);
        }

        // what the owner sees at a pixel of the sheet: the hand painting if any, else the recoloured cat (null: transparent)
        public Color? ColorAt(int sx, int sy)
        {
            if (hand() is { } h) { var c = h.GetPixel(sx, sy); if (c.A != 0) return Color.FromArgb(255, c); }
            var b = sheet.GetPixel(sx, sy);
            return b.A == 0 ? null : Color.FromArgb(255, b);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(BackColor);
            using (var path = RoundRect(new Rectangle(0, 0, Width - 1, Height - 1), f.P(12))) using (var b = new SolidBrush(f.track))
                g.FillPath(b, path);
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            var d = Dest(); int s = Scale;
            g.DrawImage(sheet, d, Frame * 32, Row * 32, 32, 32, GraphicsUnit.Pixel);
            if (hand() is { } h) g.DrawImage(h, d, Frame * 32, Row * 32, 32, 32, GraphicsUnit.Pixel);
            g.SmoothingMode = SmoothingMode.None;
            if (s >= 8)   // the pixel grid, faint
                using (var pen = new Pen(Color.FromArgb(26, 255, 255, 255)))
                    for (int i = 0; i <= 32; i++) { g.DrawLine(pen, d.X + i * s, d.Y, d.X + i * s, d.Bottom); g.DrawLine(pen, d.X, d.Y + i * s, d.Right, d.Y + i * s); }
            if (Outline && hover.X >= 0 && hover.Y >= 0 && hover.X < 32 && hover.Y < 32)
            {
                int off = (Brush - 1) / 2;
                using var pen = new Pen(f.accent, 2f);
                g.DrawRectangle(pen, d.X + (hover.X - off) * s, d.Y + (hover.Y - off) * s, Brush * s, Brush * s);
            }
        }

        Point Cell(MouseEventArgs e)
        {
            var d = Dest(); int s = Scale;
            return new Point((int)Math.Floor((e.X - d.X) / (double)s), (int)Math.Floor((e.Y - d.Y) / (double)s));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            drag = true; Capture = true;
            var c = Cell(e); hover = c;
            Down?.Invoke(c.X, c.Y);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var c = Cell(e);
            if (c != hover) { hover = c; Invalidate(); }
            if (drag) Move?.Invoke(c.X, c.Y);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!drag) return;
            drag = false; Capture = false;
            Up?.Invoke();
        }

        protected override void OnMouseLeave(EventArgs e) { hover = new Point(-100, -100); Invalidate(); base.OnMouseLeave(e); }
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
