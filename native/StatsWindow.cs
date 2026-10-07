using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

// Small click-through panel shown above the cat while the mouse hovers on it:
// three rounded bars (fullness, happiness, energy), each split in 5 segments of 20%, with the name above.
sealed class StatsWindow : Form
{
    static readonly Color Panel = Color.FromArgb(225, 0x14, 0x15, 0x1b);
    static readonly Color Track = Color.FromArgb(0x2e, 0x30, 0x40);
    static readonly Color Border = Color.FromArgb(0x3a, 0x3d, 0x52);
    static readonly Color LabelCol = Color.FromArgb(0xc4, 0xc8, 0xd8);
    static readonly Color[] Colors =
    {
        Color.FromArgb(0xff, 0x6b, 0x9d),   // fullness
        Color.FromArgb(0xff, 0xd9, 0x3d),   // happiness
        Color.FromArgb(0x6b, 0xcf, 0x7f),   // energy
    };
    static readonly string[] Keys = { "stat.hunger", "stat.happiness", "stat.energy" };

    readonly float S;
    readonly CatProfile profile;   // whose needs (and name) it shows
    Size panel;
    readonly System.Windows.Forms.Timer fadeT = new() { Interval = 16 };
    Bitmap? cur;                      // what the window currently shows (re-pushed with a new alpha while fading)
    int alpha, target;                // 0..255
    long lastKey = -1;
    string lastLang = "", lastName = "";
    Point lastLoc = new(int.MinValue, 0);

    const int FadeMs = 200;

    int Px(double v) => (int)Math.Round(v * S);

    // taller when the cat has a name (shown as a title)
    Size PanelSize => new(Px(200), Px(128 + (profile.Name.Length > 0 ? 28 : 0)));

    public StatsWindow(float scale, CatProfile owner)
    {
        S = scale;
        profile = owner;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = Cfg.OnTop;
        StartPosition = FormStartPosition.Manual;
        panel = PanelSize;
        ClientSize = panel;
        fadeT.Tick += (_, _) => FadeStep();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { fadeT.Dispose(); cur?.Dispose(); }
        base.Dispose(disposing);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x80000 | 0x80 | 0x08000000 | 0x20;   // LAYERED | TOOLWINDOW | NOACTIVATE | TRANSPARENT (click-through)
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => true;
    public IntPtr Anchor;   // the cat's window: when "always on top" is off this popup is placed right behind it


    // Show (fade in) or move/refresh the panel above the cat window; values are 0..100
    public void ShowAbove(Rectangle cat, double hunger, double happiness, double energy)
    {
        var wa = Screens.WorkAreaAt(cat.Left + cat.Width / 2, cat.Top + cat.Height / 2);
        int x = Math.Clamp(cat.Left + cat.Width / 2 - panel.Width / 2, wa.Left, Math.Max(wa.Left, wa.Right - panel.Width));
        int y = Math.Max(wa.Top, cat.Top + cat.Height / 2 - panel.Height - Px(6));   // the cat only fills the lower half of its box
        var loc = new Point(x, y);

        int h = (int)Math.Round(hunger), hp = (int)Math.Round(happiness), e = (int)Math.Round(energy);
        long key = h | (long)hp << 8 | (long)e << 16;
        if (panel != PanelSize) { panel = PanelSize; ClientSize = panel; lastKey = -1; }
        y = Math.Max(wa.Top, cat.Top + cat.Height / 2 - panel.Height - Px(6));
        loc = new Point(x, y);
        bool changed = key != lastKey || loc != lastLoc || lastLang != Cfg.Lang || lastName != profile.Name;
        if (!Visible) { alpha = 0; Show(); changed = true; }
        target = 255;
        if (changed)
        {
            lastKey = key; lastLoc = loc; lastLang = Cfg.Lang; lastName = profile.Name;
            Location = loc;
            cur?.Dispose();
            cur = Render(h, hp, e);
            Push();
            TopMost = Cfg.OnTop;
            if (TopMost) Native.SetWindowPos(Handle, (IntPtr)(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
            else if (Anchor != IntPtr.Zero) Native.SetWindowPos(Handle, Anchor, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);   // not on top: sit right behind the cat in the z-order, not over every other window
        }
        if (alpha != target && !fadeT.Enabled) fadeT.Start();
    }

    // Fade out, then hide
    public void HidePanel()
    {
        if (!Visible) return;
        target = 0;
        if (!fadeT.Enabled) fadeT.Start();
    }

    void Push()
    {
        if (cur != null) Native.PushBitmap(Handle, cur, lastLoc.X, lastLoc.Y, (byte)alpha);
    }

    void FadeStep()
    {
        int step = Math.Max(1, 255 * fadeT.Interval / FadeMs);
        alpha = target > alpha ? Math.Min(target, alpha + step) : Math.Max(target, alpha - step);
        Push();
        if (alpha != target) return;
        fadeT.Stop();
        if (alpha == 0) { Hide(); lastKey = -1; cur?.Dispose(); cur = null; }
    }

    // For development: render the panel to an image without showing it (TamAPet.exe --dump-stats file.png)
    public Bitmap Preview(int hunger, int happiness, int energy) => Render(hunger, happiness, energy);

    Bitmap Render(params int[] values)
    {
        var bmp = new Bitmap(panel.Width, panel.Height, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        using (var path = Round(new Rectangle(0, 0, panel.Width - 1, panel.Height - 1), Px(12)))
        {
            using var b = new SolidBrush(Panel);
            g.FillPath(b, path);
            using var pen = new Pen(Border);
            g.DrawPath(pen, path);
        }

        int pad = Px(14), w = panel.Width - pad * 2, barH = Px(7), y = pad;
        if (profile.Name.Length > 0)
        {
            using var title = new Font("Segoe UI Semibold", 14f * S, FontStyle.Regular, GraphicsUnit.Pixel);
            using var titleBrush = new SolidBrush(Color.White);
            g.DrawString(profile.Name, title, titleBrush, new RectangleF(pad - 1, y - Px(1), w + 2, Px(24)), new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap });
            y += Px(28);
        }
        using var font = new Font("Segoe UI Semibold", 11.5f * S, FontStyle.Regular, GraphicsUnit.Pixel);
        using var labelBrush = new SolidBrush(LabelCol);
        using var right = new StringFormat { Alignment = StringAlignment.Far };
        for (int i = 0; i < 3; i++)
        {
            g.DrawString(Str.T(Keys[i]), font, labelBrush, pad - 1, y - Px(1));
            g.DrawString(values[i].ToString(), font, labelBrush, new RectangleF(pad, y - Px(1), w + 1, Px(20)), right);   // value, right-aligned on the same row
            DrawBar(g, new Rectangle(pad, y + Px(21), w, barH), values[i] / 100.0, Colors[i]);
            y += Px(36);
        }
        return bmp;
    }

    // Rounded bar split in 5 segments (20% each): the filled part grows left to right through the segments
    void DrawBar(Graphics g, Rectangle r, double fraction, Color color)
    {
        const int Segments = 5;
        int gap = Math.Max(2, Px(3));
        var state = g.Save();
        using (var clip = Round(r, r.Height / 2)) g.SetClip(clip);

        // the 5 segments (the gaps between them stay panel-coloured); the filled part grows through them
        double sw = (r.Width - gap * (Segments - 1)) / (double)Segments;
        using var track = new SolidBrush(Track);
        using var fill = new SolidBrush(color);
        for (int i = 0; i < Segments; i++)
        {
            float x = (float)(r.X + i * (sw + gap));
            g.FillRectangle(track, x, r.Y, (float)sw, r.Height);
            double part = Math.Clamp(fraction * Segments - i, 0, 1);
            if (part > 0) g.FillRectangle(fill, x, r.Y, (float)(sw * part), r.Height);
        }
        g.Restore(state);
    }

    static GraphicsPath Round(Rectangle r, int rad)
    {
        var p = new GraphicsPath(); int d = Math.Max(1, rad * 2);
        p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}
