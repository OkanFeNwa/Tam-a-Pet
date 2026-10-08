using System.Drawing;
using System.Drawing.Imaging;

// The cat's pictures, recoloured with the owner's choices: "fur 1" (the grey patches: back, tail, ears, some paws),
// "fur 2" (the light belly/chest/face/legs) and the eyes. The sprite sheet only has four flat colours, so recolouring
// is exact: light/grey pixels are swapped, and the eye pixels (a hand-checked list per frame) get the eye colour.
static class CatSprite
{
    public static readonly Color DefaultNose = Color.FromArgb(202, 113, 159), DefaultEars = Color.FromArgb(154, 135, 126);
    public static readonly Color DefaultFur1 = Color.FromArgb(181, 181, 181), DefaultFur2 = Color.FromArgb(224, 224, 224), DefaultEyes = Color.Black;

    // "row,col:x.y x.y;..." eye pixels inside each 32x32 frame (closed eyes of the sleeping frames are left dark)
    const string EyeData = "0,0:16.23 19.23;0,1:16.23 19.23;0,2:16.23 19.23;0,3:16.23 19.23;1,0:17.23;1,1:17.23;1,2:17.23;1,3:17.23;2,0:16.23 17.23 19.23;2,1:16.24 17.24 19.24;2,2:16.23 17.23;2,3:16.22 17.22;3,0:16.23 17.23 19.23;3,1:16.25 17.25 19.25;3,2:16.25 17.25 19.25;3,3:16.24 17.24 19.24;4,0:19.24;4,1:19.23;4,2:19.22;4,3:19.23;4,4:19.24;4,5:19.23;4,6:19.22;4,7:19.23;5,0:19.24;5,1:19.23;5,2:19.22;5,3:19.23;5,4:19.25;5,5:19.24;5,6:19.23;5,7:19.24;7,0:19.25;7,1:18.25;7,2:18.25;7,3:20.26;7,4:18.25;7,5:18.25;8,0:16.23 19.23;8,1:16.19 19.19;8,2:17.18 20.18;8,3:18.21;8,4:17.26 20.26;8,5:17.25 20.25;8,6:17.24 20.24;9,0:19.24;9,1:19.24 19.25 20.25;9,2:19.24 19.25 20.25;9,3:19.24 19.25 20.25;9,4:19.24 19.25 20.25;9,5:19.24 19.25 20.25;9,6:19.24 19.25 20.25;9,7:19.24";

    static readonly Dictionary<(int r, int c), HashSet<(int x, int y)>> eyes = ParseEyes();

    static Dictionary<(int, int), HashSet<(int, int)>> ParseEyes()
    {
        var d = new Dictionary<(int, int), HashSet<(int, int)>>();
        foreach (var part in EyeData.Split(';'))
        {
            var kv = part.Split(':');
            var rc = kv[0].Split(',');
            var set = new HashSet<(int, int)>();
            foreach (var p in kv[1].Split(' ')) { var xy = p.Split('.'); set.Add((int.Parse(xy[0]), int.Parse(xy[1]))); }
            d[(int.Parse(rc[0]), int.Parse(rc[1]))] = set;
        }
        return d;
    }

    // hand: draw what the owner painted by hand over the cat (the colour studio asks for the cat without it)
    public static Bitmap BuildSheet(CatProfile p, bool hand = true) =>
        Cfg.ClassicSprites ? BuildSheet(p.Fur1, p.Fur2, p.Eyes, p.Pattern, p.Fur3) : BuildNew(p, hand);

    // the accessories of the new sprites (each is a sheet with the same layout, holding only the accessory)
    public static readonly string[] Accessories = { "bow-red", "bow-pink", "bow-gold", "bow2-blue", "bow2-green", "bow2-pink", "glasses-red", "glasses-gold", "halo", "wings", "cupid", "santa-1", "santa-2", "antlers-red", "antlers-green" };

    // the part of the first frame shown in the settings previews (a sitting cat)
    public static Rectangle PreviewRect => Cfg.ClassicSprites ? new Rectangle(7, 17, 17, 15) : new Rectangle(4, 6, 24, 22);

    static Bitmap LoadRes(string name)
    {
        using var s = typeof(CatSprite).Assembly.GetManifestResourceStream(name)!;
        using var png = new Bitmap(s);
        var bmp = new Bitmap(png.Width, png.Height, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bmp);
        g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
        g.DrawImage(png, 0, 0, png.Width, png.Height);
        return bmp;
    }

    // New sprites: the art does not reach the bottom of its 32x32 cell (and each row stops at its own height), so every
    // row is drawn lower by this many sheet pixels: the paws then stand on the same line as the old sprites'.
    static int[]? foot;
    public static int FootDy(int row)
    {
        if (foot == null)
        {
            using var b = LoadRes("spr.cat");
            var d = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            var buf = new byte[d.Stride * d.Height];
            System.Runtime.InteropServices.Marshal.Copy(d.Scan0, buf, 0, buf.Length);
            b.UnlockBits(d);
            var f = new int[b.Height / 32];
            for (int r = 0; r < f.Length; r++)
            {
                int bottom = 0;
                for (int y = r * 32; y < r * 32 + 32; y++)
                    for (int x = 0; x < b.Width; x++)
                        if (buf[y * d.Stride + x * 4 + 3] != 0) bottom = Math.Max(bottom, y - r * 32 + 1);
                f[r] = bottom == 0 ? 0 : 32 - bottom;
            }
            foot = f;
        }
        return row >= 0 && row < foot.Length ? foot[row] : 0;
    }

    // the untouched art, for the colour studio: which part of the cat is at this pixel of the sheet, and how many pictures each row has
    static byte[]? rawBuf; static int rawW, rawH, rawStride;
    static void EnsureRaw()
    {
        if (rawBuf != null) return;
        using var b = LoadRes("spr.cat");
        var d = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        var buf = new byte[d.Stride * d.Height];
        System.Runtime.InteropServices.Marshal.Copy(d.Scan0, buf, 0, buf.Length);
        b.UnlockBits(d);
        rawW = b.Width; rawH = b.Height; rawStride = d.Stride; rawBuf = buf;
    }

    public static HashSet<(int, int)>? LastEyes;   // the eye pixels found by the last BuildNew: they are dark like the outline but are not part of it

    // where the owner may paint by hand: the cat's own pixels, except its outline (the eyes are dark too, but they are free)
    public static bool Paintable(int px, int py)
    {
        if (!Solid(px, py)) return false;
        int i = py * rawStride + px * 4;
        bool outline = rawBuf![i + 2] == 18 && rawBuf[i + 1] == 14 && rawBuf[i] == 20;
        return !outline || LastEyes?.Contains((px, py)) == true;
    }

    public static bool Solid(int px, int py)
    {
        EnsureRaw();
        return px >= 0 && py >= 0 && px < rawW && py < rawH && rawBuf![py * rawStride + px * 4 + 3] != 0;
    }

    public static int[] RowFrames()
    {
        EnsureRaw();
        var f = new int[rawH / 32];
        for (int y = 0; y < rawH; y++)
            for (int x = 0; x < rawW; x++)
                if (rawBuf![y * rawStride + x * 4 + 3] != 0) f[y / 32] = Math.Max(f[y / 32], x / 32 + 1);
        return f;
    }

    public static Color Shade(Color c) => Color.FromArgb(c.R * 66 / 100, c.G * 66 / 100, c.B * 66 / 100);
    public static Color Light(Color c) => Color.FromArgb(c.R + (255 - c.R) * 3 / 10, c.G + (255 - c.G) * 3 / 10, c.B + (255 - c.B) * 3 / 10);

    // The new cat has three fur tones (main, shade, highlight) and a pink nose: the owner's "fur" colour gives all three,
    // the eyes are the single dark dots inside the face, and the pattern colour is turned into the same three tones.
    static Bitmap BuildNew(CatProfile p, bool hand)
    {
        var sheet = LoadRes("spr.cat");
        int W = sheet.Width, H = sheet.Height;
        var data = sheet.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.ReadWrite, PixelFormat.Format32bppPArgb);
        int stride = data.Stride;
        var buf = new byte[stride * H];
        System.Runtime.InteropServices.Marshal.Copy(data.Scan0, buf, 0, buf.Length);
        var orig = (byte[])buf.Clone();

        bool Op(int x, int y) => x >= 0 && y >= 0 && x < W && y < H && orig[y * stride + x * 4 + 3] != 0;
        bool IsOut(int x, int y) { if (!Op(x, y)) return false; int i = y * stride + x * 4; return orig[i + 2] == 18 && orig[i + 1] == 14 && orig[i] == 20; }
        bool Fill(int x, int y) => Op(x, y) && !IsOut(x, y);
        int Depth(int x, int y, int dx, int dy) { int d = 0; while (d < 3 && Fill(x + dx * (d + 1), y + dy * (d + 1))) d++; return d; }

        // only pictures that show the face (they have the pink nose) have eyes: seen from behind, the dark dot is the tail
        var faces = new HashSet<(int, int)>();
        var nose = new Dictionary<(int, int), (double x, double y, int n)>();   // where the nose is in each picture: the eyes are just above it
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int i = y * stride + x * 4;
                if (orig[i + 3] != 0 && orig[i + 2] == 202 && orig[i + 1] == 113 && orig[i] == 159)
                {
                    var key = (y / 32, x / 32);
                    faces.Add(key);
                    nose.TryGetValue(key, out var nv);
                    nose[key] = (nv.x + x % 32, nv.y + y % 32, nv.n + 1);
                }
            }

        // eyes: the small groups (1-4 pixels) of dark pixels with fur all around them, in the pictures that show the face
        var eyePx = new HashSet<(int, int)>();
        var eyeByCell = new Dictionary<(int, int), List<(int x, int y)>>();
        var seen = new bool[W * H];
        var cands = new Dictionary<(int, int), List<(double d, List<(int x, int y)> comp)>>();
        bool Inside(int x, int y) { for (int oy = -1; oy <= 1; oy++) for (int ox = -1; ox <= 1; ox++) if (!Op(x + ox, y + oy)) return false; return true; }
        for (int y0 = 1; y0 < H - 1; y0++)
            for (int x0 = 1; x0 < W - 1; x0++)
            {
                if (seen[y0 * W + x0] || !faces.Contains((y0 / 32, x0 / 32)) || !IsOut(x0, y0)) continue;
                var comp = new List<(int x, int y)>();
                var todo = new Stack<(int x, int y)>();
                todo.Push((x0, y0)); seen[y0 * W + x0] = true;
                bool inside = true;
                while (todo.Count > 0)
                {
                    var (cx, cy) = todo.Pop();
                    comp.Add((cx, cy));
                    if (!Inside(cx, cy)) inside = false;
                    for (int oy = -1; oy <= 1; oy++)
                        for (int ox = -1; ox <= 1; ox++)
                        {
                            int nx = cx + ox, ny = cy + oy;
                            if (nx < 1 || ny < 1 || nx >= W - 1 || ny >= H - 1 || seen[ny * W + nx] || !IsOut(nx, ny)) continue;
                            seen[ny * W + nx] = true; todo.Push((nx, ny));
                        }
                    if (comp.Count > 4) break;
                }
                if (!inside || comp.Count > 4) continue;
                if (comp.Max(c => c.x) - comp.Min(c => c.x) > comp.Max(c => c.y) - comp.Min(c => c.y)) continue;   // a flat line is a closed eye: it stays dark
                var nz = nose[(y0 / 32, x0 / 32)];
                double mx = comp.Average(c => c.x % 32), my = comp.Average(c => c.y % 32);
                double ddx = mx - nz.x / nz.n, ddy = nz.y / nz.n - my;
                if (Math.Abs(ddx) > 4.6 || ddy < 0.5 || ddy > 4.5) continue;   // not on the face
                var cell = (y0 / 32, x0 / 32);
                if (!cands.TryGetValue(cell, out var cl)) cands[cell] = cl = new();
                cl.Add((ddx * ddx + ddy * ddy, comp));
            }
        foreach (var (cell, cl) in cands)   // a cat has two eyes: of the groups near the nose, the two closest
            foreach (var (_, comp) in cl.OrderBy(t => t.d).Take(2))
                foreach (var (ex, ey) in comp)
                {
                    eyePx.Add((ex, ey));
                    if (!eyeByCell.TryGetValue(cell, out var l)) eyeByCell[cell] = l = new();
                    l.Add((ex % 32, ey % 32));
                }
        bool NearHead(int row, int col, int fx, int fy)
        {
            if (!eyeByCell.TryGetValue((row, col), out var l)) return false;
            foreach (var (ex, ey) in l) if (Math.Abs(fx - ex) <= 5 && fy >= ey - 6 && fy <= ey + 4) return true;
            return false;
        }

        LastEyes = eyePx;
        Color fur = p.Fur1, third = p.Fur3, white = Color.FromArgb(240, 240, 240);
        void Put(int i, Color c) { buf[i] = c.B; buf[i + 1] = c.G; buf[i + 2] = c.R; }
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int i = y * stride + x * 4;
                if (orig[i + 3] == 0) continue;
                if (eyePx.Contains((x, y))) { Put(i, p.Eyes); continue; }
                byte b = orig[i], g = orig[i + 1], r = orig[i + 2];
                if (r == 202 && g == 113 && b == 159) { Put(i, p.Nose); continue; }   // the nose
                if (r == 154 && g == 135 && b == 126) { Put(i, p.Ears); continue; }   // inside the ears
                if (r == 18 && g == 14 && b == 20) { if (p.Outline is Color oc) Put(i, oc); continue; }   // outline: as drawn unless the painter overrides it
                int cls = r == 98 && g == 103 && b == 115 ? 0 : r == 65 && g == 71 && b == 82 ? 1 : r == 134 && g == 141 && b == 155 ? 2 : -1;   // main, shade, highlight
                if (cls < 0) continue;   // outline, nose, whiskers, ...: as drawn
                int fx = x % 32, fy = y % 32;
                Color src = fur;
                switch (p.Pattern)
                {
                    case "tabby":   // short stripes hanging from the line of the back, 3 px apart, not on the head
                        if (!NearHead(y / 32, x / 32, fx, fy) && Depth(x, y, 0, -1) < 2 && fx % 3 == 0) src = third;
                        break;
                    case "patches": if (Blob(fx, fy) > 0.7) src = third; break;
                    case "calico":
                        double n = Blob(fx, fy);
                        if (n > 0.4) src = third; else if (n < -1.2) src = white;
                        break;
                }
                Put(i, cls == 0 || p.Pattern == "none" ? src : cls == 1 ? p.Shade ?? Shade(src) : p.Light ?? Light(src));   // no pattern: one flat colour
            }
        if (hand && p.Hand is { } hb && hb.Width == W && hb.Height == H)   // painted by hand: only on the cat's own pixels
        {
            var hd = hb.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var hbuf = new byte[hd.Stride * H];
            System.Runtime.InteropServices.Marshal.Copy(hd.Scan0, hbuf, 0, hbuf.Length);
            hb.UnlockBits(hd);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int i = y * stride + x * 4, j = y * hd.Stride + x * 4;
                    if (hbuf[j + 3] == 0 || orig[i + 3] == 0 || IsOut(x, y) && !eyePx.Contains((x, y))) continue;   // never over the outline
                    buf[i] = hbuf[j]; buf[i + 1] = hbuf[j + 1]; buf[i + 2] = hbuf[j + 2];
                }
        }
        System.Runtime.InteropServices.Marshal.Copy(buf, 0, data.Scan0, buf.Length);
        sheet.UnlockBits(data);

        if (p.Accessory != "" && Accessories.Contains(p.Accessory))
        {
            using var layer = LoadRes("spr.acc-" + p.Accessory);
            using var g = Graphics.FromImage(sheet);
            g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceOver;
            g.DrawImage(layer, 0, 0, W, H);
        }
        return sheet;
    }

    // soft blobs, the same in every frame (so the markings do not flicker): > 0 is "inside a patch"
    static double Blob(int fx, int fy) => Math.Sin(fx * 0.5 + 1.3) + Math.Sin(fy * 0.55 + 0.4) + Math.Sin((fx + fy) * 0.33 + 2) + Math.Sin((fx - fy) * 0.29);

    public static Color DefaultVariantColor(string pattern, Color keep) => pattern switch
    {
        "tabby" => Color.FromArgb(0x4A, 0x4A, 0x52), "patches" => Color.FromArgb(0x8B, 0x5A, 0x3C), "calico" => Color.FromArgb(0xE8, 0x91, 0x3C), _ => keep
    };

    // is this pixel on the head (around the eyes, ears included)? the markings of the back skip it
    static bool NearHead(int row, int col, int fx, int fy)
    {
        if (!eyes.TryGetValue((row, col), out var set)) return false;
        foreach (var (ex, ey) in set) if (Math.Abs(fx - ex) <= 3 && fy >= ey - 7 && fy <= ey + 3) return true;
        return false;
    }

    // A fresh 256x320 sheet (10 rows of 32x32 frames) in the given colours
    public static Bitmap BuildSheet(Color fur1, Color fur2, Color eyeColor, string pattern = "none", Color? fur3 = null)
    {
        using var s = typeof(CatSprite).Assembly.GetManifestResourceStream("cat-sprite.png")!;
        using var png = new Bitmap(s);
        var sheet = new Bitmap(png.Width, png.Height, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(sheet))
        {
            g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
            g.DrawImage(png, 0, 0, png.Width, png.Height);
        }

        var rect = new Rectangle(0, 0, sheet.Width, sheet.Height);
        var data = sheet.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppPArgb);
        var buf = new byte[data.Stride * data.Height];
        System.Runtime.InteropServices.Marshal.Copy(data.Scan0, buf, 0, buf.Length);
        var third = fur3 ?? Color.FromArgb(0xE8, 0x91, 0x3C);
        var dark = Color.FromArgb(fur1.R * 11 / 20, fur1.G * 11 / 20, fur1.B * 11 / 20);   // tabby stripes: a darker fur 1
        var orig = (byte[])buf.Clone();   // the untouched art: to find the outline and how deep a pixel lies below the back
        bool Fill(int px, int py) => px >= 0 && py >= 0 && px < sheet.Width && py < sheet.Height && orig[py * data.Stride + px * 4 + 3] != 0 && !(orig[py * data.Stride + px * 4 + 2] == 47 && orig[py * data.Stride + px * 4 + 1] == 47 && orig[py * data.Stride + px * 4] == 46);
        int Depth(int px, int py, int dx, int dy) { int d = 0; while (d < 3 && Fill(px + dx * (d + 1), py + dy * (d + 1))) d++; return d; }   // 3 = at least 3 deep
        void Put(int i, Color c) { buf[i] = c.B; buf[i + 1] = c.G; buf[i + 2] = c.R; }   // art is fully opaque, so premultiplied == plain
        for (int y = 0; y < sheet.Height; y++)
            for (int x = 0; x < sheet.Width; x++)
            {
                int i = y * data.Stride + x * 4;
                if (buf[i + 3] == 0) continue;
                if (eyes.TryGetValue((y / 32, x / 32), out var set) && set.Contains((x % 32, y % 32))) { Put(i, eyeColor); continue; }
                byte b = buf[i], gg = buf[i + 1], r = buf[i + 2];
                if (r == 47 && gg == 47 && b == 46) Put(i, Color.Black);   // outline and stripes: pure black
                else if (r == 224 && gg == 224 && b == 224 || r == 181 && gg == 181 && b == 181)
                {
                    bool grey = r == 181;
                    int fx = x % 32, fy = y % 32;
                    Color baseC = grey ? fur1 : fur2, c = baseC;
                    switch (pattern)
                    {
                        case "tabby":   // stripes across the back, tail and haunches (not on the head)
                            // short stripes hanging from the top line of the back and from the rear edge, 3 px apart
                            if (!NearHead(y / 32, x / 32, fx, fy) && (Depth(x, y, 0, -1) < 2 && fx % 3 == 0 || (y / 32 is <= 3 or 8) && Depth(x, y, -1, 0) < 2 && fy % 3 == 0)) c = third;
                            break;
                        case "patches": if (Blob(fx, fy) > 0.7) c = third; break;
                        case "calico":
                            double n = Blob(fx, fy);
                            if (grey) { if (n > 0.3) c = third; }
                            else if (n > 1.1) c = third; else if (n < -1.3) c = fur1;
                            break;
                    }
                    Put(i, c);
                }
            }
        System.Runtime.InteropServices.Marshal.Copy(buf, 0, data.Scan0, buf.Length);
        sheet.UnlockBits(data);
        return sheet;
    }

    public static string Hex(Color c) => $"{c.R:X2}{c.G:X2}{c.B:X2}";

    public static bool TryParse(string hex, out Color c)
    {
        c = default;
        if (hex.Length != 6 || !int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out int v)) return false;
        c = Color.FromArgb((v >> 16) & 255, (v >> 8) & 255, v & 255);
        return true;
    }
}
