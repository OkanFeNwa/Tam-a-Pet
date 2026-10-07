using System.Drawing;
using System.Drawing.Imaging;

// The cat's pictures, recoloured with the owner's choices: "fur 1" (the grey patches: back, tail, ears, some paws),
// "fur 2" (the light belly/chest/face/legs) and the eyes. The sprite sheet only has four flat colours, so recolouring
// is exact: light/grey pixels are swapped, and the eye pixels (a hand-checked list per frame) get the eye colour.
static class CatSprite
{
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

    public static Bitmap BuildSheet(CatProfile p) => BuildSheet(p.Fur1, p.Fur2, p.Eyes, p.Pattern, p.Fur3);

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
