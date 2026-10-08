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
    System.Windows.Forms.Timer? paintT, paintStatusT;
    Bitmap? paintDraft;   // the drawing being made: it only reaches the cat on "Save"
    bool paintChanged;
    Pill? paintSavePill;
    Label? paintStatusLbl;
    string paintStatusText = "", paintPageName = "paint0";
    PaintView? paintView;
    Pill? paintRowPill, paintPlayPill;
    Label? paintInfo;
    ToolTip? paintTip;
    readonly Dictionary<PaintTool, Pill> toolPills = new();
    readonly Dictionary<int, Pill> sizePills = new();
    SvBox? pSv; HueBar? pHue; ColorChip? pChip; TextBox? pHex; Panel? pRecent;
    double pH, pS, pV;
    bool pSync;

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

    Panel? paintFramePanel;
    FrameStrip? paintStrip;

    // rows 0-3 hold one pose seen from different sides (pictures to choose from, not frames to play); the others are animations
    bool IsAnimated(int row) => row > 3 && paintFrames![row] > 1;

    int BuildPaint(CatProfile cat, int pad, int y, int w)
    {
        LookPresets.EnsureLoaded();
        paintCatObj = cat;
        paintFrames ??= CatSprite.RowFrames();
        if (paintRow >= paintFrames.Length || paintFrames[paintRow] == 0) paintRow = 0;
        paintPageName = page;
        if (paintDraft == null || paintCat != cat.Index)   // opening the studio: the draft starts as the saved drawing
        {
            DropDraft(); paintCat = cat.Index;
            paintDraft = new Bitmap(HandPaint.W, HandPaint.H, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            if (cat.Hand != null) using (var g = Graphics.FromImage(paintDraft)) { g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy; g.DrawImage(cat.Hand, 0, 0, HandPaint.W, HandPaint.H); }
        }
        EnableDrop(cat);
        toolPills.Clear(); sizePills.Clear();
        paintTip?.Dispose(); paintTip = new ToolTip();
        paintBuiltW = host.ClientSize.Width;
        ToHsv(brushColor, out pH, out pS, out pV);

        // one row on top: back, title
        var back = new Pill(this) { Text = cat.Display, Glyph = "", Bounds = new Rectangle(pad, y - P(4), P(170), P(36)), Kind = PillKind.Outline };
        back.Click += (_, _) => { page = "cat" + cat.Index; bar.Value = 0; Build(); };
        view.Controls.Add(back);
        view.Controls.Add(Lbl(Str.T("paint.title"), fTitle, text, pad + P(186), y - P(1), P(320)));   // narrow: it must not cover the buttons

        // Share and Save, on the right (under the title in the small window); a line of status next to them
        int hw = P(124), hg = P(8), hy = y - P(4), hx = pad + w - hw * 2 - hg;
        if (w < P(900)) { y += P(48); hy = y - P(4); hx = pad; }
        var sharePill = new Pill(this) { Text = Str.T("paint.share"), Glyph = "\uE72D", Kind = PillKind.Seg, Bounds = new Rectangle(hx, hy, hw, P(36)) };
        sharePill.Click += (_, _) => ShareMenu(cat, sharePill);
        paintSavePill = new Pill(this) { Text = Str.T("paint.save"), Glyph = "\uE74E", Kind = PillKind.Seg, Active = paintChanged, Bounds = new Rectangle(hx + hw + hg, hy, hw, P(36)) };
        paintSavePill.Click += (_, _) => { if (paintChanged) { CommitDraft(cat); ShowStatus(Str.T("paint.saved")); } };
        view.Controls.Add(sharePill); view.Controls.Add(paintSavePill);
        Tip(sharePill, Str.T("paint.share")); Tip(paintSavePill, Str.T("paint.save"));
        int sx0 = w < P(900) ? hx + hw * 2 + hg * 2 : pad + P(520), sx1 = w < P(900) ? pad + w : hx - P(12);
        paintStatusLbl = Lbl(paintStatusText, fSmall, muted, sx0, hy + P(9), Math.Max(P(60), sx1 - sx0), w < P(900) ? ContentAlignment.TopLeft : ContentAlignment.TopRight);
        view.Controls.Add(paintStatusLbl);
        y += P(48);

        int gap = P(14), limit = host.ClientSize.Height - pad;   // limit: the lowest the content may reach without scrolling
        if (w < P(900))   // the small window: one column, it scrolls
        {
            y = CanvasCard(cat, pad, y, w, w - P(32)) + gap;
            y = AnimCard(cat, pad, y, w, 0) + gap;
            y = VariantsCard(cat, pad, y, w, 0) + gap;
            y = ToolsCard(cat, pad, y, w, 0) + gap;
            y = BrushColorCard(pad, y, w, 0) + gap;
            return PresetsCard(cat, pad, y, w, 0);
        }

        // Full screen, everything in view. Above: the cat, then the animations with the variants under them, then the presets.
        // Below: the tools and the colour, half of the window each.
        int hb = P(176), midW = P(300), varH = P(168);
        int top = y, topH = limit - top - hb - gap;
        int vs = Math.Max(P(240), Math.Min(topH - P(32), w - midW - P(300) - gap * 2 - P(32)));
        int canvasW = vs + P(32);
        int cbottom = CanvasCard(cat, pad, top, canvasW, vs), ch = cbottom - top;
        int mx = pad + canvasW + gap;
        AnimCard(cat, mx, top, midW, ch - varH - gap);
        VariantsCard(cat, mx, cbottom - varH, midW, varH);
        PresetsCard(cat, mx + midW + gap, top, w - canvasW - midW - gap * 2, ch);

        int y2 = cbottom + gap, half = (w - gap) / 2;
        ToolsCard(cat, pad, y2, half, hb);
        BrushColorCard(pad + half + gap, y2, w - half - gap, hb);
        return y2 + hb;
    }

    // the cat, big, to paint on pixel by pixel; returns the y under the card
    int CanvasCard(CatProfile cat, int x, int y, int w, int vs)
    {
        var vc = AddCard(x, y, w);
        previewSheet?.Dispose();
        previewSheet = CatSprite.BuildSheet(cat, false);   // the recoloured cat without the hand painting: PaintView draws that on top itself
        paintView = new PaintView(this, previewSheet, () => paintDraft) { Bounds = new Rectangle((w - vs) / 2, P(16), vs, vs), Row = paintRow, Frame = paintFrame, Brush = paintSize, Outline = paintTool != PaintTool.Pick };
        paintView.Down += (px, py) => PaintDown(cat, px, py);
        paintView.Move += (px, py) => PaintMove(cat, px, py);
        paintView.Up += () => PaintUp(cat);
        vc.Controls.Add(paintView);
        vc.Height = vs + P(32);
        return y + vc.Height;
    }

    // which animation (or pose), its pictures side by side to pick from, and the playback controls
    int AnimCard(CatProfile cat, int x, int y, int w, int h)
    {
        int iw = w - P(32);
        var ac = AddCard(x, y, w);
        paintRowPill = new Pill(this) { Kind = PillKind.Seg, Bounds = new Rectangle(P(16), P(16), iw, P(40)) };
        paintRowPill.Click += (_, _) =>
        {
            var cm = new ContextMenuStrip();
            void Header(string t) { cm.Items.Add(new ToolStripMenuItem(t) { Enabled = false }); }
            void Rows(bool animated)
            {
                for (int r = 0; r < paintFrames!.Length; r++)
                {
                    if (paintFrames[r] == 0 || (r > 3 && paintFrames[r] > 1) != animated) continue;
                    int row = r;
                    var it = new ToolStripMenuItem(RowLabel(row)) { Checked = row == paintRow };
                    it.Click += (_, _) => { paintRow = row; paintFrame = 0; FillFramePanel(); SetPlay(IsAnimated(row)); };
                    cm.Items.Add(it);
                }
            }
            Header(Str.T("paint.static")); Rows(false);
            cm.Items.Add(new ToolStripSeparator());
            Header(Str.T("paint.animated")); Rows(true);
            MenuRenderer.Apply(cm);
            cm.Show(paintRowPill, new Point(0, paintRowPill!.Height + P(4)));
        };
        ac.Controls.Add(paintRowPill);
        var hint = Lbl(Str.T("paint.hint"), fSmall, muted, P(16), 0, iw); hint.BackColor = card;
        int panelTop = P(16) + P(40) + P(14);
        int panelH = h > 0 ? Math.Max(P(120), h - panelTop - hint.Height - P(28)) : P(340);
        paintFramePanel = new Panel { BackColor = card, Bounds = new Rectangle(P(16), panelTop, iw, panelH) };
        ac.Controls.Add(paintFramePanel);
        FillFramePanel();
        hint.Top = h > 0 ? h - hint.Height - P(14) : paintFramePanel.Bottom + P(12);
        ac.Controls.Add(hint);
        ac.Height = h > 0 ? h : hint.Bottom + P(16);
        paintT?.Dispose();
        paintT = new System.Windows.Forms.Timer { Interval = 110 };
        paintT.Tick += (_, _) =>
        {
            if (!paintPlay || painting || !IsAnimated(paintRow)) return;
            paintFrame = (paintFrame + 1) % paintFrames![paintRow];
            ShowFrame();
        };
        paintT.Start();
        return y + ac.Height;
    }

    // previous, play, next and "picture 3 of 6" for an animation; then every picture of the row, to pick one
    void FillFramePanel()
    {
        var pn = paintFramePanel!;
        foreach (Control c in pn.Controls.Cast<Control>().ToList()) c.Dispose();
        paintInfo = null; paintPlayPill = null; paintStrip = null;
        int bg = P(6), sy = 0;
        if (IsAnimated(paintRow))
        {
            int fw = P(52);
            var prev = new Pill(this) { Glyph = "", Kind = PillKind.Seg, Bounds = new Rectangle(0, 0, fw, P(40)) };
            paintPlayPill = new Pill(this) { Kind = PillKind.Seg, Bounds = new Rectangle(fw + bg, 0, fw, P(40)) };
            var next = new Pill(this) { Glyph = "", Kind = PillKind.Seg, Bounds = new Rectangle((fw + bg) * 2, 0, fw, P(40)) };
            prev.Click += (_, _) => StepFrame(-1);
            next.Click += (_, _) => StepFrame(1);
            paintPlayPill.Click += (_, _) => SetPlay(!paintPlay);
            int ix = (fw + bg) * 3 + P(4);
            paintInfo = Lbl("", fBold, muted, ix, P(11), Math.Max(P(40), pn.Width - ix)); paintInfo.BackColor = card; paintInfo.TextAlign = ContentAlignment.TopRight;
            pn.Controls.Add(prev); pn.Controls.Add(paintPlayPill); pn.Controls.Add(next); pn.Controls.Add(paintInfo);
            sy = P(40) + P(12);
            SetPlay(paintPlay);
        }
        paintStrip = new FrameStrip(this, previewSheet!, () => paintDraft) { Bounds = new Rectangle(0, sy, pn.Width, pn.Height - sy), Row = paintRow, Frame = paintFrame, Count = paintFrames![paintRow] };
        paintStrip.Pick += i => { if (IsAnimated(paintRow)) SetPlay(false); paintFrame = i; ShowFrame(); };
        pn.Controls.Add(paintStrip);
        ShowFrame();
    }

    void Tip(Control c, string text) => paintTip?.SetToolTip(c, text);

    // ---- tools -------------------------------------------------------------------------------------

    int ToolsCard(CatProfile cat, int x, int y, int w, int h)
    {
        int iw = w - P(32), th = P(56), ah = P(48), gp = P(8);
        var tc = AddCard(x, y, w);
        int cy = P(16);

        // tool: brush, eraser, eyedropper; on the right the size of the brush
        int toolsW = iw * 58 / 100, sizeW = iw - toolsW - P(20);
        int tw = (toolsW - gp * 2) / 3, sw = (sizeW - gp * 2) / 3;
        var tools = new (PaintTool t, string glyph, string key)[] { (PaintTool.Brush, "", "tool.brush"), (PaintTool.Eraser, "", "tool.eraser"), (PaintTool.Pick, "", "tool.pick") };
        for (int i = 0; i < tools.Length; i++)
        {
            var (t, glyph, key) = tools[i];
            var tp = new Pill(this) { Glyph = glyph, Kind = PillKind.Seg, Active = paintTool == t, Bounds = new Rectangle(P(16) + i * (tw + gp), cy, tw, th) };
            tp.Click += (_, _) => SetTool(t);
            Tip(tp, Str.T(key));
            toolPills[t] = tp; tc.Controls.Add(tp);
        }
        int[] dots = { 5, 9, 14 };   // the size of the brush is shown by the size of a square
        for (int s = 1; s <= 3; s++)
        {
            int size = s;
            var sp = new Pill(this) { Dot = dots[s - 1], Kind = PillKind.Seg, Active = paintSize == s, Bounds = new Rectangle(P(16) + toolsW + P(20) + (s - 1) * (sw + gp), cy, sw, th) };
            sp.Click += (_, _) => SetSize(size);
            Tip(sp, Str.T("paint.size") + " " + s + "×" + s);
            sizePills[s] = sp; tc.Controls.Add(sp);
        }
        cy += th + (h > 0 ? Math.Max(P(10), h - P(32) - th - ah) : P(14));

        // undo, redo, clear this picture, clear everything
        var actions = new (string glyph, string key, Action act)[]
        {
            ("", "paint.undo", () => Undo(cat)),
            ("", "paint.redo", () => Redo(cat)),
            ("", "paint.clearframe", () => ClearFrame(cat)),
            ("", "paint.clearall", () => ClearAll(cat)),
        };
        int aw = (iw - gp * 3) / 4;
        for (int i = 0; i < actions.Length; i++)
        {
            var (glyph, key, act) = actions[i];
            var ap = new Pill(this) { Glyph = glyph, Kind = PillKind.Seg, Bounds = new Rectangle(P(16) + i * (aw + gp), cy, aw, ah) };
            ap.Click += (_, _) => act();
            Tip(ap, Str.T(key));
            tc.Controls.Add(ap);
        }
        cy += ah + P(16);
        tc.Height = h > 0 ? h : cy;
        return y + tc.Height;
    }

    // the pattern of the fur and "reset colours"
    int VariantsCard(CatProfile cat, int x, int y, int w, int h)
    {
        int iw = w - P(32);
        var vc = AddCard(x, y, w);
        string[] pats = { "none", "tabby", "patches", "calico" };
        int pw = (iw - P(8)) / 2, cy = P(16);
        for (int i = 0; i < pats.Length; i++)
        {
            string pn = pats[i];
            var pp = new Pill(this) { Text = Str.T("pattern." + pn), Kind = PillKind.Seg, Active = cat.Pattern == pn, Bounds = new Rectangle(P(16) + (i % 2) * (pw + P(8)), cy + (i / 2) * P(46), pw, P(38)) };
            pp.Click += (_, _) =>
            {
                if (cat.Pattern != pn) cat.Fur3 = CatSprite.DefaultVariantColor(pn, cat.Fur3);   // each variant starts with its own colour
                cat.Pattern = pn; Cfg.Changed(cat); Build();
            };
            vc.Controls.Add(pp);
        }
        cy += P(46) * 2 + P(6);
        var reset = new Pill(this) { Text = Str.T("look.reset"), Kind = PillKind.Seg, Bounds = new Rectangle(P(16), cy, iw, P(38)) };
        reset.Click += (_, _) => { cat.ResetLook(); Cfg.Changed(cat); Build(); };
        vc.Controls.Add(reset);
        cy += P(38) + P(16);
        vc.Height = h > 0 ? h : cy;
        return y + vc.Height;
    }

    // ---- brush colour ------------------------------------------------------------------------------

    // the colour square on the left; on its right the hue, the colour in hex and the colours used lately
    int BrushColorCard(int x, int y, int w, int h)
    {
        var cc = AddCard(x, y, w);
        int iw = w - P(32), svW = Math.Min(P(260), iw * 55 / 100), svH = h > 0 ? h - P(32) : P(150);
        int rx = P(16) + svW + P(16), rw = iw - svW - P(16);
        pSv = new SvBox(this) { Bounds = new Rectangle(P(16), P(16), svW, svH), Hue = pH, S = pS, V = pV };
        pHue = new HueBar(this) { Bounds = new Rectangle(rx, P(16), rw, P(24)), Hue = pH };
        int cy = P(16) + P(24) + P(12);
        pChip = new ColorChip(this) { Fill = brushColor, Bounds = new Rectangle(rx, cy, P(56), P(36)) };
        var field = new Card(track, line, card, P(8)) { Bounds = new Rectangle(rx + P(64), cy, rw - P(64), P(36)) };
        pHex = new TextBox { MaxLength = 7, BorderStyle = BorderStyle.None, BackColor = track, ForeColor = text, Font = fBase, Bounds = new Rectangle(P(10), P(9), rw - P(64) - P(20), P(20)), Text = "#" + CatSprite.Hex(brushColor) };
        field.Controls.Add(pHex);
        pSv.Changed += () => { pS = pSv.S; pV = pSv.V; ApplyHsv(); };
        pHue.Changed += () => { pH = pHue.Hue; pSv.Hue = pH; ApplyHsv(); };
        pHex.TextChanged += (_, _) => { if (!pSync && CatSprite.TryParse(pHex.Text.TrimStart('#'), out var c)) SetBrush(c, false); };
        cc.Controls.Add(pSv); cc.Controls.Add(pHue); cc.Controls.Add(pChip); cc.Controls.Add(field);
        cy += P(36) + P(14);
        var rl = Lbl(Str.T("picker.recent"), fSmall, muted, rx, cy, rw); rl.BackColor = card; cc.Controls.Add(rl);
        cy += P(20);
        pRecent = new Panel { Bounds = new Rectangle(rx, cy, rw, Math.Max(P(60), P(16) + svH - cy)), BackColor = card };
        cc.Controls.Add(pRecent);
        FillRecent();
        cc.Height = h > 0 ? h : P(16) + Math.Max(svH, pRecent.Bottom - P(16)) + P(16);
        return y + cc.Height;
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
        int sz = P(26), gp = P(6), perRow = Math.Max(1, (pRecent.Width + gp) / (sz + gp)), i = 0;
        foreach (var rc in Cfg.Recent)
        {
            var picked = rc;
            var sw = new Swatch(this) { Fill = rc, Selected = rc.ToArgb() == brushColor.ToArgb(), Bounds = new Rectangle(i % perRow * (sz + gp), i / perRow * (sz + gp), sz, sz) };
            sw.Click += (_, _) => { SetBrush(picked, true); FillRecent(); };
            pRecent.Controls.Add(sw);
            i++;
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
        if (paintStrip != null) { paintStrip.Frame = paintFrame; paintStrip.Invalidate(); }
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
    List<(int x, int y, int old, int now)>? stroke;   // the pixels the current stroke changed, with their previous colour (0 = nothing)
    readonly List<List<(int x, int y, int old, int now)>> undo = new(), redo = new();
    CatProfile? paintCatObj;

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
        paintView!.Invalidate(); paintStrip?.Invalidate();
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
        paintView!.Invalidate(); paintStrip?.Invalidate();
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
                int old = paintDraft!.GetPixel(sx, sy).ToArgb(), now = paintTool == PaintTool.Eraser ? 0 : Color.FromArgb(255, brushColor).ToArgb();
                if (old == now) continue;
                stroke!.Add((sx, sy, old, now));
                paintDraft.SetPixel(sx, sy, Color.FromArgb(now));
            }
    }

    void PaintUp(CatProfile cat)
    {
        if (!painting) return;
        painting = false;
        if (stroke is { Count: > 0 })
        {
            undo.Add(stroke); redo.Clear(); if (undo.Count > 60) undo.RemoveAt(0);
            paintStrip?.Invalidate();
            if (paintTool == PaintTool.Brush) RememberColor();
            SetChanged(true);
        }
        stroke = null;
    }

    // ---- the drawing is a draft until it is saved --------------------------------------------------
    // What is painted lives in paintDraft, not in the cat: the pet on the desktop keeps its saved look until "Save".
    // Leaving the studio with changes asks first, and what was not saved is lost.

    void SetChanged(bool on)
    {
        paintChanged = on;
        if (paintSavePill != null) paintSavePill.Active = on;
    }

    void CommitDraft(CatProfile cat)
    {
        if (paintDraft == null) return;
        HandPaint.Commit(cat, paintDraft);
        Cfg.Changed(cat);
        SetChanged(false);
    }

    void DropDraft()
    {
        paintDraft?.Dispose(); paintDraft = null; paintChanged = false;
        undo.Clear(); redo.Clear();
    }

    // Is it all right to leave the studio? (unsaved drawing: save it, drop it, or stay)
    bool LeaveStudioOk()
    {
        if (paintDraft == null || !paintChanged || paintCatObj == null) { DropDraft(); return true; }
        using var d = new ConfirmDialog(this);
        var r = d.ShowDialog(this);
        if (r == DialogResult.Cancel) return false;
        if (r == DialogResult.Yes) CommitDraft(paintCatObj);
        DropDraft();
        return true;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (!e.Cancel && inPaintPage && !LeaveStudioOk()) e.Cancel = true;
    }

    static int[] PixelsOf(Bitmap b)
    {
        var d = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var px = new int[b.Width * b.Height];
        Marshal.Copy(d.Scan0, px, 0, px.Length);
        b.UnlockBits(d);
        return px;
    }

    // the whole drawing is replaced by another (an import): one step, which can be undone
    void ReplaceDraft(Bitmap? other)
    {
        var mine = PixelsOf(paintDraft!);
        var theirs = other != null ? PixelsOf(other) : new int[mine.Length];
        var s = new List<(int x, int y, int old, int now)>();
        for (int i = 0; i < mine.Length; i++)
        {
            int a = (mine[i] >> 24) == 0 ? 0 : mine[i], b = (theirs[i] >> 24) == 0 ? 0 : theirs[i];
            if (a == b) continue;
            s.Add((i % paintDraft!.Width, i / paintDraft.Width, a, b)); paintDraft.SetPixel(i % paintDraft.Width, i / paintDraft.Width, Color.FromArgb(b));
        }
        if (s.Count == 0) return;
        undo.Add(s); redo.Clear();
        SetChanged(true);
        paintView?.Invalidate(); paintStrip?.Invalidate();
    }

    void Undo(CatProfile cat)
    {
        if (undo.Count == 0) return;
        var s = undo[^1]; undo.RemoveAt(undo.Count - 1);
        foreach (var (x, y, old, _) in s) paintDraft!.SetPixel(x, y, Color.FromArgb(old));
        redo.Add(s);
        SetChanged(true);
        paintView?.Invalidate(); paintStrip?.Invalidate();
    }

    void Redo(CatProfile cat)
    {
        if (redo.Count == 0) return;
        var s = redo[^1]; redo.RemoveAt(redo.Count - 1);
        foreach (var (x, y, _, now) in s) paintDraft!.SetPixel(x, y, Color.FromArgb(now));
        undo.Add(s);
        SetChanged(true);
        paintView?.Invalidate(); paintStrip?.Invalidate();
    }

    // everything painted goes, in one step that can be undone (no "are you sure": undo is the safety net)
    void ClearAll(CatProfile cat) => ReplaceDraft(null);

    void ClearFrame(CatProfile cat)
    {
        var s = new List<(int x, int y, int old, int now)>();
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                int sx = paintFrame * 32 + x, sy = paintRow * 32 + y, old = paintDraft!.GetPixel(sx, sy).ToArgb();
                if (old == 0) continue;
                s.Add((sx, sy, old, 0)); paintDraft.SetPixel(sx, sy, Color.Transparent);
            }
        if (s.Count == 0) return;
        undo.Add(s); redo.Clear();
        SetChanged(true);
        paintView?.Invalidate(); paintStrip?.Invalidate();
    }

    // ---- sharing -----------------------------------------------------------------------------------

    void ShowStatus(string msg)
    {
        paintStatusText = msg;
        if (paintStatusLbl != null) paintStatusLbl.Text = msg;
        paintStatusT ??= new System.Windows.Forms.Timer { Interval = 5000 };
        paintStatusT.Tick -= ClearStatus; paintStatusT.Tick += ClearStatus;
        paintStatusT.Stop(); paintStatusT.Start();
    }

    void ClearStatus(object? s, EventArgs e)
    {
        paintStatusT?.Stop(); paintStatusText = "";
        if (paintStatusLbl != null) paintStatusLbl.Text = "";
    }

    void ShareMenu(CatProfile cat, Control anchor)
    {
        var m = new ContextMenuStrip();
        m.Items.Add(new ToolStripMenuItem(Str.T("share.copy"), null, (_, _) => CopyCode(cat)));
        m.Items.Add(new ToolStripMenuItem(Str.T("share.image"), null, (_, _) => SaveImage(cat)));
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add(new ToolStripMenuItem(Str.T("share.paste"), null, (_, _) => ImportShared(cat, LookShare.Decode(SafeClipboardText()))));
        m.Items.Add(new ToolStripMenuItem(Str.T("share.file"), null, (_, _) =>
        {
            using var d = new OpenFileDialog { Filter = "Tam-a-Pet|*.png;*.txt|*.*|*.*" };
            if (d.ShowDialog(this) == DialogResult.OK) ImportShared(cat, LookShare.DecodeFile(d.FileName));
        }));
        MenuRenderer.Apply(m);
        m.Show(anchor, new Point(0, anchor.Height + P(4)));
    }

    static string SafeClipboardText() { try { return Clipboard.ContainsText() ? Clipboard.GetText() : ""; } catch { return ""; } }

    string ShareName(CatProfile cat) => cat.Name != "" ? cat.Name : "";

    // the code for a message: with the drawing if it fits in one, otherwise the look alone
    void CopyCode(CatProfile cat)
    {
        bool hasArt = paintDraft != null && PixelsOf(paintDraft).Any(p => (p >> 24) != 0);
        var code = LookShare.Encode(ShareName(cat), cat, hasArt ? paintDraft : null);
        bool dropped = false;
        if (code.Length > LookShare.MaxMessage) { code = LookShare.Encode(ShareName(cat), cat, null); dropped = true; }
        try { Clipboard.SetText(code); ShowStatus(Str.T(dropped ? "share.copied.nodraw" : "share.copied")); }
        catch { ShowStatus(Str.T("share.failed")); }
    }

    void SaveImage(CatProfile cat)
    {
        using var d = new SaveFileDialog { Filter = "PNG|*.png", FileName = "tamapet-" + (cat.Name != "" ? string.Concat(cat.Name.Split(Path.GetInvalidFileNameChars())) : "cat") + ".png" };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        bool hasArt = paintDraft != null && PixelsOf(paintDraft).Any(p => (p >> 24) != 0);
        try { File.WriteAllBytes(d.FileName, LookShare.MakeImage(ShareName(cat), cat, hasArt ? paintDraft : null)); ShowStatus(Str.T("share.saved")); }
        catch { ShowStatus(Str.T("share.failed")); }
    }

    // what someone sent: its colours are the cat's now; its drawing goes into the draft (Save keeps it, Undo takes it back)
    void ImportShared(CatProfile cat, LookShare.Shared? s)
    {
        if (s == null) { ShowStatus(Str.T("share.invalid")); return; }
        s.Look.Apply(cat);
        Cfg.Changed(cat);
        ReplaceDraft(s.Hand);
        s.Hand?.Dispose();
        ShowStatus(Str.T("share.imported"));
        Build();
    }

    bool dropSet;
    void EnableDrop(CatProfile cat)
    {
        if (dropSet) return;
        dropSet = true;
        AllowDrop = true;
        DragEnter += (_, e) => { if (inPaintPage && e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy; };
        DragDrop += (_, e) =>
        {
            if (!inPaintPage || paintCatObj == null || e.Data?.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;
            ImportShared(paintCatObj, LookShare.DecodeFile(files[0]));
        };
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
        && p.Ears.ToArgb() == c.Ears.ToArgb() && p.Nose.ToArgb() == c.Nose.ToArgb() && p.Eyes.ToArgb() == c.Eyes.ToArgb() && p.Eyes2?.ToArgb() == c.Eyes2?.ToArgb()
        && p.Pattern == c.Pattern && p.Accessory == c.Accessory && (p.Pattern == "none" || p.Fur3.ToArgb() == c.Fur3.ToArgb());

    string? AskName(string title, string initial)
    {
        using var d = new NameDialog(this, title, initial);
        return d.ShowDialog(this) == DialogResult.OK ? d.Result : null;
    }

    // One card: title, "save the look", and a grid of presets (the cat in the preset's colours and its name); a click applies one, "⋯" has the rest.
    // h > 0: the card has that height (full screen, no scrolling): the cells shrink and the columns grow until all of them fit.
    int PresetsCard(CatProfile cat, int x, int y, int w, int h)
    {
        var pc = AddCard(x, y, w);
        int iw = w - P(32), cy = P(16);
        var title = Lbl(Str.T("presets.title"), fBold, text, P(16), cy + P(2), iw); title.BackColor = card; pc.Controls.Add(title);
        cy += P(28);

        var save = new Pill(this) { Text = Str.T("preset.save").TrimStart('+', ' '), Glyph = "", Kind = PillKind.Seg, Active = true, Bounds = new Rectangle(P(16), cy, iw, P(40)) };
        save.Click += (_, _) =>
        {
            if (LookPresets.User.Count >= LookPresets.MaxUser) return;
            var name = AskName(Str.T("preset.name"), LookPresets.Unique(Str.T("preset.new")));
            if (name == null) return;
            LookPresets.User.Add(LookPreset.From(cat, LookPresets.Unique(name)));
            LookPresets.Save(); Build();
        };
        pc.Controls.Add(save);
        cy += P(40) + P(12);

        if (LookPresets.User.Count == 0 && h == 0)
        {
            var empty = Lbl(Str.T("preset.empty"), fSmall, muted, P(16), cy, iw); empty.BackColor = card; pc.Controls.Add(empty);
            cy += empty.Height + P(10);
        }
        var all = LookPresets.User.AsEnumerable().Reverse().Concat(LookPresets.Builtin).ToList();   // newest of the user's first, then the templates
        int gap = P(8), cols;
        int CellH(int cw) => P(10) + Math.Min(P(72), cw * 6 / 10) + P(6) + P(20) + (cw >= P(105) ? P(18) : 0) + P(8);
        if (h == 0) cols = Math.Max(2, (iw + gap) / (P(140) + gap));
        else
        {
            int room = h - P(16) - cy;
            for (cols = 2; cols < 6; cols++)
            {
                int cw0 = (iw - gap * (cols - 1)) / cols;
                if ((all.Count + cols - 1) / cols * (CellH(cw0) + gap) <= room) break;
            }
        }
        int cw = (iw - gap * (cols - 1)) / cols, ch = CellH(cw), n = 0;
        foreach (var pr in all)
        {
            var preset = pr;
            int cx = P(16) + (n % cols) * (cw + gap), cyy = cy + (n / cols) * (ch + gap);
            n++;
            bool on = Matches(preset, cat);
            var cell = new Card(bg, on ? accent : line, card, P(12)) { Bounds = new Rectangle(cx, cyy, cw, ch) };
            pc.Controls.Add(cell);
            int tw = Math.Min(cw - 2 * (P(6) + P(30) + P(6)), P(112)), th = Math.Min(P(72), cw * 6 / 10);   // the picture stays clear of the "⋯" button
            var thumb = Thumb(preset.Sample(), new Rectangle((cw - tw) / 2, P(10), tw, th)); thumb.BackColor = bg;
            cell.Controls.Add(thumb);
            var nm = Lbl(preset.Display, cw >= P(90) ? fBold : fSmall, text, P(4), P(10) + th + P(6), cw - P(8), ContentAlignment.TopCenter); nm.BackColor = bg; nm.AutoEllipsis = true; nm.Height = P(20); cell.Controls.Add(nm);
            if (cw >= P(105))
            {
                var sub = Lbl(preset.Builtin ? Str.T("preset.builtin") : (preset.Pattern == "none" ? " " : Str.T("pattern." + preset.Pattern)), fSmall, muted, P(4), P(10) + th + P(26), cw - P(8), ContentAlignment.TopCenter); sub.BackColor = bg; cell.Controls.Add(sub);
            }
            Clickable(cell, () => { preset.Apply(cat); Cfg.Changed(cat); Build(); });

            var mb = new Pill(this) { Glyph = "", Kind = PillKind.Seg, Bounds = new Rectangle(cw - P(6) - P(30), P(6), P(30), P(26)) };
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
        pc.Height = h > 0 ? h : cy + rows * (ch + gap) + P(8);
        return y + pc.Height;
    }

    // ---- controls ---------------------------------------------------------------------------------

    // The cat, big, on its card: shows one picture of one row of the sheet, with what was painted by hand on top.
    // The mouse reports positions in pixels of that picture (0-31, or outside while dragging out of it).
    sealed class PaintView : Control
    {
        readonly SettingsForm f;
        readonly Bitmap sheet;
        readonly Func<Bitmap?> hand;
        public int Row, Frame;
        public bool Outline = true;   // show where the brush would paint
        int brush = 1, fromBrush = 1;
        readonly Spring grow = new() { Response = 0.18, Value = 1, Target = 1 };   // 0 = the size the brush had, 1 = the new one
        System.Windows.Forms.Timer? anim;
        long animLast;
        public int Brush
        {
            get => brush;
            set
            {
                if (value == brush) return;
                double cur = fromBrush + (brush - fromBrush) * Math.Clamp(grow.Value, 0, 1.2);   // start from the size shown now, not from the old target
                fromBrush = (int)Math.Round(cur); brush = value;
                grow.Value = 0; grow.Velocity = 0; grow.Target = 1;
                if (!IsHandleCreated || !Spring.Enabled) { grow.Value = 1; Invalidate(); return; }
                anim ??= NewTimer();
                if (!anim.Enabled) { animLast = Environment.TickCount64; anim.Start(); }
            }
        }

        System.Windows.Forms.Timer NewTimer()
        {
            var t = new System.Windows.Forms.Timer { Interval = 10 };
            t.Tick += (_, _) =>
            {
                long now = Environment.TickCount64; double dt = (now - animLast) / 1000.0; animLast = now;
                grow.Step(dt); Invalidate();
                if (grow.Done) t.Stop();
            };
            return t;
        }

        protected override void Dispose(bool disposing) { if (disposing) anim?.Dispose(); base.Dispose(disposing); }
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
                double t = grow.Value, size = fromBrush + (brush - fromBrush) * t;
                double off = (fromBrush - 1) / 2 + ((brush - 1) / 2 - (fromBrush - 1) / 2) * t;   // integer halves, as the brush paints
                using var pen = new Pen(f.accent, 2f);
                g.DrawRectangle(pen, (float)(d.X + (hover.X - off) * s), (float)(d.Y + (hover.Y - off) * s), (float)(size * s), (float)(size * s));
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

    // Every picture of one row of the sheet side by side (with the hand painting on them): click one to work on it;
    // while an animation plays, the one being shown is outlined.
    sealed class FrameStrip : Control
    {
        readonly SettingsForm f;
        readonly Bitmap sheet;
        readonly Func<Bitmap?> hand;
        public int Row, Count;
        public event Action<int>? Pick;
        int frame;
        readonly Spring sx = new() { Response = 0.16 }, sy = new() { Response = 0.16 };   // where the outline is, one spring for each axis
        System.Windows.Forms.Timer? anim;
        long animLast;
        bool placed;
        public int Frame
        {
            get => frame;
            set
            {
                frame = value;
                var (cell, cols, gap) = Geometry();
                sx.Target = frame % cols * (cell + gap); sy.Target = frame / cols * (cell + gap);
                if (!placed || !IsHandleCreated || !Spring.Enabled) { sx.Value = sx.Target; sy.Value = sy.Target; sx.Velocity = sy.Velocity = 0; placed = Width > 0; Invalidate(); return; }
                anim ??= NewTimer();
                if (!anim.Enabled) { animLast = Environment.TickCount64; anim.Start(); }
            }
        }

        System.Windows.Forms.Timer NewTimer()
        {
            var t = new System.Windows.Forms.Timer { Interval = 10 };
            t.Tick += (_, _) =>
            {
                long now = Environment.TickCount64; double dt = (now - animLast) / 1000.0; animLast = now;
                sx.Step(dt); sy.Step(dt);
                Invalidate();
                if (sx.Done && sy.Done) t.Stop();
            };
            return t;
        }

        protected override void Dispose(bool disposing) { if (disposing) anim?.Dispose(); base.Dispose(disposing); }

        public FrameStrip(SettingsForm owner, Bitmap sheetBitmap, Func<Bitmap?> handLayer)
        {
            f = owner; sheet = sheetBitmap; hand = handLayer; BackColor = f.card; Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
        }

        (int cell, int cols, int gap) Geometry()
        {
            // the biggest pictures that all fit in the strip
            int gap = f.P(6), cols = 2;
            for (; cols < 8; cols++)
            {
                int c = (Width - gap * (cols - 1)) / cols;
                if ((Count + cols - 1) / cols * (c + gap) <= Height + gap) break;
            }
            return ((Width - gap * (cols - 1)) / cols, cols, gap);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(BackColor);
            var (cell, cols, gap) = Geometry();
            int s = Math.Max(1, (cell - f.P(8)) / 32);
            for (int i = 0; i < Count; i++)
            {
                var r = new Rectangle(i % cols * (cell + gap), i / cols * (cell + gap), cell - 1, cell - 1);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var path = RoundRect(r, f.P(8)))
                {
                    using (var b = new SolidBrush(f.track)) g.FillPath(b, path);
                    using var pen = new Pen(f.line); g.DrawPath(pen, path);
                }
                g.InterpolationMode = InterpolationMode.NearestNeighbor; g.PixelOffsetMode = PixelOffsetMode.Half;
                var d = new Rectangle(r.X + (cell - 32 * s) / 2, r.Y + (cell - 32 * s) / 2, 32 * s, 32 * s);
                g.DrawImage(sheet, d, i * 32, Row * 32, 32, 32, GraphicsUnit.Pixel);
                if (hand() is { } h) g.DrawImage(h, d, i * 32, Row * 32, 32, 32, GraphicsUnit.Pixel);
            }
            g.SmoothingMode = SmoothingMode.AntiAlias;   // the outline of the current picture: it travels from one to the next
            if (Count > 0)
            {
                using var path = RoundRect(new Rectangle((int)Math.Round(sx.Value), (int)Math.Round(sy.Value), cell - 1, cell - 1), f.P(8));
                using var pen = new Pen(f.accent, 2f); g.DrawPath(pen, path);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            var (cell, cols, gap) = Geometry();
            int cx = e.X / (cell + gap), cy = e.Y / (cell + gap), i = cy * cols + cx;
            if (cx < cols && i < Count && e.X % (cell + gap) < cell && e.Y % (cell + gap) < cell) Pick?.Invoke(i);
        }
    }

    // the unsaved drawing is about to be lost: save it, throw it away, or stay (Yes / No / Cancel)
    sealed class ConfirmDialog : Form
    {
        readonly SettingsForm f;

        public ConfirmDialog(SettingsForm owner)
        {
            f = owner;
            Text = Str.T("paint.leave.title"); Font = f.fBase; BackColor = f.bg; Icon = f.appIcon;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(f.P(400), f.P(214));
            var t = f.Lbl(Str.T("paint.leave.title"), f.fBold, f.text, f.P(20), f.P(18), f.P(360)); t.BackColor = f.bg; Controls.Add(t);
            var m = f.Lbl(Str.T("paint.leave.msg"), f.fBase, f.muted, f.P(20), f.P(46), f.P(360)); m.BackColor = f.bg; Controls.Add(m);
            var save = new Pill(f) { Text = Str.T("paint.leave.save"), Kind = PillKind.Seg, Active = true, Bounds = new Rectangle(f.P(20), f.P(114), f.P(360), f.P(40)) };
            var drop = new Pill(f) { Text = Str.T("paint.leave.discard"), Kind = PillKind.Outline, Bounds = new Rectangle(f.P(20), f.P(162), f.P(176), f.P(40)) };
            var stay = new Pill(f) { Text = Str.T("paint.leave.stay"), Kind = PillKind.Outline, Bounds = new Rectangle(f.P(204), f.P(162), f.P(176), f.P(40)) };
            save.Click += (_, _) => DialogResult = DialogResult.Yes;
            drop.Click += (_, _) => DialogResult = DialogResult.No;
            stay.Click += (_, _) => DialogResult = DialogResult.Cancel;
            Controls.Add(save); Controls.Add(drop); Controls.Add(stay);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys key)
        {
            if (key == Keys.Escape) { DialogResult = DialogResult.Cancel; return true; }
            return base.ProcessCmdKey(ref msg, key);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (f.bg.GetBrightness() < 0.5f) { int on = 1; DwmSetWindowAttribute(Handle, 20, ref on, 4); }
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
