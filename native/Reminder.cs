using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

// A small thought bubble above the cat with the thing it needs: the bowl when it is hungry, the bed when it is
// tired, the ball when it is sad (several icons side by side if it needs more than one). Click-through, fades in/out.
sealed class ReminderWindow : Form
{
    public const int Hungry = 1, Tired = 2, Sad = 4;

    static readonly Color Fill = Color.FromArgb(235, 0x14, 0x15, 0x1b);
    static readonly Color Border = Color.FromArgb(0x3a, 0x3d, 0x52);
    const int FadeMs = 200;

    readonly float S;
    readonly CatProfile cat;   // whose objects (colours) it shows
    readonly int u;                         // size of one art pixel on screen
    readonly System.Windows.Forms.Timer fadeT = new() { Interval = 16 };
    LayeredSurface? surface;
    Size bubble;
    int lastMask = -1, alpha, target;
    Point lastLoc = new(int.MinValue, 0);

    public ReminderWindow(float scale, CatProfile owner)
    {
        S = scale;
        cat = owner;
        u = Math.Max(2, (int)Math.Round(2.4 * scale));
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = Cfg.OnTop;
        StartPosition = FormStartPosition.Manual;
        fadeT.Tick += (_, _) => FadeStep();
    }

    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ExStyle |= 0x80000 | 0x80 | 0x08000000 | 0x20; return cp; }   // LAYERED | TOOLWINDOW | NOACTIVATE | TRANSPARENT (click-through)
    }

    protected override bool ShowWithoutActivation => true;

    protected override void Dispose(bool disposing)
    {
        if (disposing) { fadeT.Dispose(); surface?.Dispose(); }
        base.Dispose(disposing);
    }

    int Px(double v) => (int)Math.Round(v * S);

    // For development: the bubble as an image, without showing it
    public Bitmap Preview(int mask)
    {
        Render(mask);
        return (Bitmap)surface!.Bitmap.Clone();
    }

    // The icons for the needs in the mask, in a rounded bubble with a tail pointing down to the cat
    void Render(int mask)
    {
        var icons = new List<Bitmap>();
        if ((mask & Hungry) != 0) icons.Add(PixelArt.BowlImage(u, cat.BowlColor));
        if ((mask & Tired) != 0) icons.Add(PixelArt.BedImage(u, cat.BedColor));
        if ((mask & Sad) != 0) icons.Add(PixelArt.BallImage(u, cat.BallColor));

        int pad = Px(10), gap = Px(12), tail = Px(8);
        int bodyW = pad * 2 + icons.Sum(i => i.Width) + gap * (icons.Count - 1);
        int bodyH = pad * 2 + icons.Max(i => i.Height);
        bubble = new Size(bodyW + 2, bodyH + tail + 2);

        surface?.Dispose();
        surface = new LayeredSurface(bubble.Width, bubble.Height);
        using var g = Graphics.FromImage(surface.Bitmap);
        g.Clear(Color.Transparent);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var body = new Rectangle(0, 0, bodyW, bodyH);
        using (var path = Round(body, Px(10))) using (var b = new SolidBrush(Fill)) using (var pen = new Pen(Border))
        {
            g.FillPath(b, path);
            g.DrawPath(pen, path);
        }
        // the tail (the junction with the body is covered with the fill colour)
        int cx = bodyW / 2, tw = Px(7);
        using (var b = new SolidBrush(Fill)) using (var pen = new Pen(Border))
        {
            var tri = new[] { new Point(cx - tw, bodyH - 1), new Point(cx + tw, bodyH - 1), new Point(cx, bodyH + tail - 1) };
            g.FillPolygon(b, tri);
            g.DrawLine(pen, tri[0], tri[2]);
            g.DrawLine(pen, tri[1], tri[2]);
            using var cover = new Pen(Fill, 2);
            g.DrawLine(cover, cx - tw + 1, bodyH - 1, cx + tw - 1, bodyH - 1);
        }

        g.SmoothingMode = SmoothingMode.None;
        int x = pad;
        foreach (var icon in icons)
        {
            g.DrawImageUnscaled(icon, x, pad + (bodyH - pad * 2 - icon.Height) / 2);
            x += icon.Width + gap;
            icon.Dispose();
        }
        lastMask = mask;
    }

    // Show (fade in) or move/refresh the bubble above the cat window
    public void ShowAbove(Rectangle cat, int mask)
    {
        if (mask != lastMask || surface == null) { Render(mask); lastLoc = new Point(int.MinValue, 0); }
        var wa = Screens.WorkAreaAt(cat.Left + cat.Width / 2, cat.Top + cat.Height / 2);
        int x = Math.Clamp(cat.Left + cat.Width / 2 - bubble.Width / 2, wa.Left, Math.Max(wa.Left, wa.Right - bubble.Width));
        int y = Math.Max(wa.Top, cat.Top + (int)(cat.Height * 0.42) - bubble.Height);   // just above the cat's head
        var loc = new Point(x, y);

        bool first = !Visible;
        if (first) { alpha = 0; ClientSize = bubble; Show(); }
        else if (ClientSize != bubble) ClientSize = bubble;
        target = 255;
        if (first || loc != lastLoc)
        {
            lastLoc = loc;
            Location = loc;
            surface!.Push(Handle, loc.X, loc.Y, (byte)alpha);
            TopMost = Cfg.OnTop;
            if (TopMost) Native.SetWindowPos(Handle, (IntPtr)(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
        }
        if (alpha != target && !fadeT.Enabled) fadeT.Start();
    }

    // Fade out, then hide
    public void HideBubble()
    {
        if (!Visible) return;
        target = 0;
        if (!fadeT.Enabled) fadeT.Start();
    }

    void FadeStep()
    {
        int step = Math.Max(1, 255 * fadeT.Interval / FadeMs);
        alpha = target > alpha ? Math.Min(target, alpha + step) : Math.Max(target, alpha - step);
        surface?.Push(Handle, lastLoc.X, lastLoc.Y, (byte)alpha);
        if (alpha != target) return;
        fadeT.Stop();
        if (alpha == 0) { Hide(); lastMask = -1; }
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
