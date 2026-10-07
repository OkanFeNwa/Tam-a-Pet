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
    readonly Size panel;
    long lastKey = -1;
    string lastLang = "";
    Point lastLoc = new(int.MinValue, 0);

    int Px(double v) => (int)Math.Round(v * S);

    public StatsWindow(float scale)
    {
        S = scale;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        panel = new Size(Px(190), Px(14 + 3 * 32 + 2 * 6 - 6 + 14));
        ClientSize = panel;
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

    // Show (or move/refresh) the panel above the cat window; values are 0..100
    public void ShowAbove(Rectangle cat, double hunger, double happiness, double energy)
    {
        var wa = Screen.FromPoint(new Point(cat.Left + cat.Width / 2, cat.Top + cat.Height / 2)).WorkingArea;
        int x = Math.Clamp(cat.Left + cat.Width / 2 - panel.Width / 2, wa.Left, Math.Max(wa.Left, wa.Right - panel.Width));
        int y = Math.Max(wa.Top, cat.Top + cat.Height / 2 - panel.Height - Px(6));   // the cat only fills the lower half of its box
        var loc = new Point(x, y);

        int h = (int)Math.Round(hunger), hp = (int)Math.Round(happiness), e = (int)Math.Round(energy);
        long key = h | (long)hp << 8 | (long)e << 16;
        bool first = !Visible;
        if (first) Show();
        if (!first && key == lastKey && loc == lastLoc && lastLang == Cfg.Lang) return;
        lastKey = key; lastLoc = loc; lastLang = Cfg.Lang;
        Location = loc;
        using var bmp = Render(h, hp, e);
        Native.PushBitmap(Handle, bmp, loc.X, loc.Y);
        Native.SetWindowPos(Handle, (IntPtr)(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);   // stay above the cat
    }

    public void HidePanel() { if (Visible) { Hide(); lastKey = -1; } }

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

        int pad = Px(14), w = panel.Width - pad * 2, barH = Px(10), y = pad;
        using var font = new Font("Segoe UI Semibold", 8.5f * S, FontStyle.Regular, GraphicsUnit.Pixel);
        using var labelBrush = new SolidBrush(LabelCol);
        for (int i = 0; i < 3; i++)
        {
            g.DrawString(Str.T(Keys[i]), font, labelBrush, pad - 1, y - Px(1));
            DrawBar(g, new Rectangle(pad, y + Px(16), w, barH), values[i] / 100.0, Colors[i]);
            y += Px(32) + Px(6);
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
