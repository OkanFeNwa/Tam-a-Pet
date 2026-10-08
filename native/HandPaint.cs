using System.Drawing;
using System.Drawing.Imaging;

// What the owner painted on a cat by hand: a transparent picture the size of the sprite sheet, kept next to settings.ini
// (paint-c1.png for the first cat, ...) and drawn over the recoloured cat. It only exists for cats that have something painted.
static class HandPaint
{
    public const int W = 352, H = 1696;   // the new sprite sheet

    static string PathOf(CatProfile p) => Path.Combine(Cfg.Dir, $"paint-c{p.Index + 1}.png");

    public static void Load(CatProfile p)
    {
        try
        {
            if (!File.Exists(PathOf(p))) return;
            using var src = new Bitmap(PathOf(p));
            if (src.Width != W || src.Height != H) return;
            var b = new Bitmap(W, H, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(b)) { g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy; g.DrawImage(src, 0, 0, W, H); }
            p.Hand = b;
        }
        catch { }
    }

    public static Bitmap Ensure(CatProfile p) => p.Hand ??= new Bitmap(W, H, PixelFormat.Format32bppArgb);

    static bool IsEmpty(Bitmap b)
    {
        var d = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var row = new byte[d.Stride];
            for (int y = 0; y < b.Height; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(d.Scan0 + y * d.Stride, row, 0, d.Stride);
                for (int x = 3; x < d.Stride; x += 4) if (row[x] != 0) return false;
            }
            return true;
        }
        finally { b.UnlockBits(d); }
    }

    // after a stroke: keep the file in step; with nothing painted left there is no layer and no file
    public static void Save(CatProfile p)
    {
        try
        {
            if (p.Hand == null || IsEmpty(p.Hand)) { Clear(p); return; }
            Directory.CreateDirectory(Cfg.Dir);
            p.Hand.Save(PathOf(p), ImageFormat.Png);
        }
        catch { }
    }

    // the draft becomes the cat's painting (nothing painted: no layer, no file)
    public static void Commit(CatProfile p, Bitmap draft)
    {
        p.Hand?.Dispose(); p.Hand = null;
        if (!IsEmpty(draft))
        {
            var b = new Bitmap(W, H, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(b)) { g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy; g.DrawImage(draft, 0, 0, W, H); }
            p.Hand = b;
        }
        Save(p);
    }

    public static void Clear(CatProfile p)
    {
        p.Hand?.Dispose(); p.Hand = null;
        try { File.Delete(PathOf(p)); } catch { }
    }

    // runnable check (--check-paint): a painted pixel shows in the built sheet only on the cat, survives save/load, and an empty layer leaves no file
    public static bool SelfCheck()
    {
        Cfg.ClassicSprites = false;
        var p = new CatProfile(0);
        CatSprite.BuildSheet(p).Dispose();   // finds the eyes, which Paintable needs
        int sx = -1, sy = -1, ox = -1, oy = -1;
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                if (sx < 0 && CatSprite.Paintable(x, y)) { sx = x; sy = y; }
                if (ox < 0 && CatSprite.Solid(x, y) && !CatSprite.Paintable(x, y)) { ox = x; oy = y; }
            }
        if (sx < 0 || ox < 0) { Console.Error.WriteLine("no paintable / outline pixel found"); return false; }
        var red = Color.FromArgb(255, 250, 10, 20);
        Ensure(p).SetPixel(sx, sy, red);
        p.Hand!.SetPixel(0, 0, red);   // on a transparent pixel: must not show
        p.Hand.SetPixel(ox, oy, red);   // on the outline: must not show either
        using (var s = CatSprite.BuildSheet(p)) if (s.GetPixel(sx, sy).ToArgb() != red.ToArgb() || s.GetPixel(0, 0).A != 0 || s.GetPixel(ox, oy).ToArgb() == red.ToArgb()) { Console.Error.WriteLine("paint not applied right"); return false; }
        using (var s = CatSprite.BuildSheet(p, false)) if (s.GetPixel(sx, sy).ToArgb() == red.ToArgb()) { Console.Error.WriteLine("hand=false still painted"); return false; }
        Save(p); p.Hand!.Dispose(); p.Hand = null; Load(p);
        if (p.Hand == null || p.Hand.GetPixel(sx, sy).ToArgb() != red.ToArgb()) { Console.Error.WriteLine("save/load failed"); return false; }
        p.Hand.SetPixel(sx, sy, Color.Transparent); p.Hand.SetPixel(0, 0, Color.Transparent); p.Hand.SetPixel(ox, oy, Color.Transparent);
        Save(p);
        if (p.Hand != null || File.Exists(PathOf(p))) { Console.Error.WriteLine("empty layer kept"); return false; }
        return true;
    }
}
