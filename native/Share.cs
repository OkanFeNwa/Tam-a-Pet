using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

// Sharing a cat's look (colours, pattern, accessory and, if it fits, the hand painting) with other people.
// Two forms, both made from the same text code "tamapet:...":
//   - the code itself, to paste in a Discord message (looks only when the painting is too big for a message);
//   - a picture (a card with the cat on it): the code is hidden in the PNG file, so the picture is also the file to send.
// Nothing is run from what is imported: it is read as data and every value is checked first.
static class LookShare
{
    const string Prefix = "tamapet:", ChunkKey = "TamAPet";
    public const int MaxMessage = 1900;   // a Discord message holds 2000 characters

    public sealed class Shared
    {
        public string Name = "";
        public LookPreset Look = new();
        public Bitmap? Hand;   // null: no painting in it
    }

    static string Clean(string s) { s = Regex.Replace(s ?? "", @"[\p{C}]", " ").Trim(); return s.Length > 24 ? s[..24] : s; }
    static string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    static byte[] UnB64(string s) { s = s.Replace('-', '+').Replace('_', '/'); return Convert.FromBase64String(s + new string('=', (4 - s.Length % 4) % 4)); }

    static byte[] Png(Bitmap b) { using var ms = new MemoryStream(); b.Save(ms, ImageFormat.Png); return ms.ToArray(); }

    // look: the colours, pattern and accessory of the cat; hand: the painting (null = none)
    public static string Encode(string name, CatProfile look, Bitmap? hand)
    {
        var d = new Dictionary<string, string>
        {
            ["n"] = Clean(name), ["f"] = CatSprite.Hex(look.Fur1), ["ea"] = CatSprite.Hex(look.Ears), ["no"] = CatSprite.Hex(look.Nose),
            ["ey"] = CatSprite.Hex(look.Eyes), ["f3"] = CatSprite.Hex(look.Fur3), ["p"] = look.Pattern, ["a"] = look.Accessory,
        };
        if (look.Eyes2 is Color e2) d["e2"] = CatSprite.Hex(e2);
        if (look.Shade is Color sh) d["sh"] = CatSprite.Hex(sh);
        if (look.Light is Color li) d["li"] = CatSprite.Hex(li);
        if (look.Outline is Color ou) d["ou"] = CatSprite.Hex(ou);
        if (hand != null) d["h"] = Convert.ToBase64String(Png(hand));
        using var ms = new MemoryStream();
        using (var z = new DeflateStream(ms, CompressionLevel.Optimal, true)) z.Write(JsonSerializer.SerializeToUtf8Bytes(d));
        return Prefix + B64(ms.ToArray());
    }

    // finds a code in any text (a pasted message may have words around it) and checks everything in it
    public static Shared? Decode(string text)
    {
        try
        {
            var m = Regex.Match(text ?? "", Prefix + @"([A-Za-z0-9_-]{8,})");
            if (!m.Success) return null;
            using var inflate = new DeflateStream(new MemoryStream(UnB64(m.Groups[1].Value)), CompressionMode.Decompress);
            using var capped = new MemoryStream();
            var buf = new byte[8192]; int n, total = 0;
            while ((n = inflate.Read(buf, 0, buf.Length)) > 0) { total += n; if (total > 6_000_000) return null; capped.Write(buf, 0, n); }   // no bombs
            var d = JsonSerializer.Deserialize<Dictionary<string, string>>(capped.ToArray());
            if (d == null || !d.TryGetValue("f", out var f) || !CatSprite.TryParse(f, out var fur)) return null;

            Color? C(string k) => d.TryGetValue(k, out var v) && CatSprite.TryParse(v, out var c) ? c : null;
            var look = new LookPreset
            {
                Fur1 = fur, Eyes2 = C("e2"), Shade = C("sh"), Light = C("li"), Outline = C("ou"),
                Ears = C("ea") ?? CatSprite.DefaultEars, Nose = C("no") ?? CatSprite.DefaultNose, Eyes = C("ey") ?? CatSprite.DefaultEyes, Fur3 = C("f3") ?? Color.FromArgb(0xE8, 0x91, 0x3C),
                Pattern = d.GetValueOrDefault("p", "none") is "tabby" or "patches" or "calico" ? d["p"] : "none",
                Accessory = CatSprite.Accessories.Contains(d.GetValueOrDefault("a", "")) ? d["a"] : "",
            };
            var shared = new Shared { Name = Clean(d.GetValueOrDefault("n", "")), Look = look };
            if (d.TryGetValue("h", out var h) && h.Length < 2_000_000)
            {
                var png = Convert.FromBase64String(h);
                // the size is in the header: read it before decoding, so a huge picture is never opened
                if (png.Length > 24 && ReadInt(png, 16) == HandPaint.W && ReadInt(png, 20) == HandPaint.H)
                {
                    using var src = new Bitmap(new MemoryStream(png));
                    var bmp = new Bitmap(HandPaint.W, HandPaint.H, PixelFormat.Format32bppArgb);
                    using (var g = Graphics.FromImage(bmp)) { g.CompositingMode = CompositingMode.SourceCopy; g.DrawImage(src, 0, 0, HandPaint.W, HandPaint.H); }
                    shared.Hand = bmp;
                }
            }
            return shared;
        }
        catch { return null; }
    }

    // a file the owner picked: a picture made by Export, or a text file with the code
    public static Shared? DecodeFile(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length > 8_000_000) return null;
            return bytes.Length > 8 && bytes[0] == 0x89 && bytes[1] == 'P' ? Decode(ReadChunk(bytes) ?? "") : Decode(Encoding.UTF8.GetString(bytes));
        }
        catch { return null; }
    }

    // ---- the picture ------------------------------------------------------------------------------

    // a card (the cat on it, its colours, its name) with the full code hidden in the file
    public static byte[] MakeImage(string name, CatProfile look, Bitmap? hand)
    {
        var tmp = new CatProfile(0) { Hand = hand };
        LookPreset.From(look, "").Apply(tmp);
        using var sheet = CatSprite.BuildSheet(tmp);
        const int W = 640, H = 320;
        using var card = new Bitmap(W, H, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(card))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);
            using (var path = Round(new Rectangle(0, 0, W - 1, H - 1), 28))
            {
                using var bg = new LinearGradientBrush(new Rectangle(0, 0, W, H), Color.FromArgb(0x23, 0x24, 0x34), Color.FromArgb(0x14, 0x15, 0x1b), 60f);
                g.FillPath(bg, path);
                using var pen = new Pen(Color.FromArgb(0x3a, 0x3c, 0x52)); g.DrawPath(pen, path);
            }
            var box = new Rectangle(28, 28, 264, 264);
            using (var path = Round(box, 20)) using (var b = new SolidBrush(Color.FromArgb(0x2f, 0x31, 0x42))) g.FillPath(b, path);
            g.InterpolationMode = InterpolationMode.NearestNeighbor; g.PixelOffsetMode = PixelOffsetMode.Half;
            var src = CatSprite.PreviewRect;
            int s = Math.Min((box.Width - 24) / src.Width, (box.Height - 24) / src.Height);
            g.DrawImage(sheet, new Rectangle(box.X + (box.Width - src.Width * s) / 2, box.Y + (box.Height - src.Height * s) / 2, src.Width * s, src.Height * s), src.X, src.Y, src.Width, src.Height, GraphicsUnit.Pixel);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using var title = new Font("Segoe UI Semibold", 26f, FontStyle.Regular, GraphicsUnit.Pixel);
            using var small = new Font("Segoe UI", 13f, FontStyle.Regular, GraphicsUnit.Pixel);
            using var white = new SolidBrush(Color.FromArgb(0xee, 0xf0, 0xf6));
            using var muted = new SolidBrush(Color.FromArgb(0x8d, 0x92, 0xa6));
            string label = Clean(name) == "" ? "Tam-a-Pet" : Clean(name);
            g.DrawString(label, title, white, 324, 34);
            string extra = (look.Pattern == "none" ? "" : Str.T("pattern." + look.Pattern)) + (look.Accessory == "" ? "" : (look.Pattern == "none" ? "" : " · ") + Str.T("acc." + look.Accessory));
            g.DrawString(extra == "" ? "Tam-a-Pet" : extra, small, muted, 326, 76);

            // the colours, as little squares
            var chips = new List<Color> { look.Fur1, look.Shade ?? CatSprite.Shade(look.Fur1), look.Light ?? CatSprite.Light(look.Fur1), look.Eyes, look.Nose, look.Ears };
            if (look.Eyes2 is Color eye2) chips.Insert(4, eye2);
            if (look.Pattern != "none") chips.Add(look.Fur3);
            for (int i = 0; i < chips.Count; i++)
            {
                var r = new Rectangle(326 + i * 44, 120, 36, 36);
                using var path = Round(r, 10); using var b = new SolidBrush(Color.FromArgb(255, chips[i]));
                g.FillPath(b, path);
                using var pen = new Pen(Color.FromArgb(0x3a, 0x3c, 0x52)); g.DrawPath(pen, path);
            }
            if (hand != null) g.DrawString(Str.T("share.card.painted"), small, white, 326, 176);
            using var how = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Far };
            g.DrawString(Str.T("share.card.how"), small, muted, new RectangleF(326, 190, W - 326 - 28, H - 190 - 28), how);
        }
        return AddChunk(Png(card), ChunkKey, Encode(name, look, hand));
    }

    static GraphicsPath Round(Rectangle r, int rad)
    {
        var p = new GraphicsPath(); int d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure(); return p;
    }

    // ---- PNG chunks (a text chunk before the end of the file) ----------------------------------

    static int ReadInt(byte[] b, int i) => b[i] << 24 | b[i + 1] << 16 | b[i + 2] << 8 | b[i + 3];

    static uint[]? crcTable;
    static uint Crc(byte[] data, int start, int len)
    {
        if (crcTable == null)
        {
            crcTable = new uint[256];
            for (uint n = 0; n < 256; n++) { uint c = n; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1; crcTable[n] = c; }
        }
        uint r = 0xFFFFFFFF;
        for (int i = start; i < start + len; i++) r = crcTable[(r ^ data[i]) & 0xFF] ^ (r >> 8);
        return r ^ 0xFFFFFFFF;
    }

    static byte[] AddChunk(byte[] png, string key, string text)
    {
        int iend = png.Length - 12;   // the last chunk of a PNG is IEND: 12 bytes
        var data = Encoding.Latin1.GetBytes(key + "\0" + text);
        var chunk = new byte[12 + data.Length];
        chunk[0] = (byte)(data.Length >> 24); chunk[1] = (byte)(data.Length >> 16); chunk[2] = (byte)(data.Length >> 8); chunk[3] = (byte)data.Length;
        Encoding.ASCII.GetBytes("tEXt").CopyTo(chunk, 4);
        data.CopyTo(chunk, 8);
        uint crc = Crc(chunk, 4, 4 + data.Length);
        chunk[8 + data.Length] = (byte)(crc >> 24); chunk[9 + data.Length] = (byte)(crc >> 16); chunk[10 + data.Length] = (byte)(crc >> 8); chunk[11 + data.Length] = (byte)crc;
        var outp = new byte[png.Length + chunk.Length];
        Array.Copy(png, 0, outp, 0, iend); chunk.CopyTo(outp, iend); Array.Copy(png, iend, outp, iend + chunk.Length, png.Length - iend);
        return outp;
    }

    static string? ReadChunk(byte[] png)
    {
        int i = 8;
        while (i + 12 <= png.Length)
        {
            int len = ReadInt(png, i);
            if (len < 0 || i + 12 + len > png.Length) return null;
            if (png[i + 4] == 't' && png[i + 5] == 'E' && png[i + 6] == 'X' && png[i + 7] == 't')
            {
                var s = Encoding.Latin1.GetString(png, i + 8, len);
                if (s.StartsWith(ChunkKey + "\0")) return s[(ChunkKey.Length + 1)..];
            }
            i += 12 + len;
        }
        return null;
    }

    // runnable check (--check-share): a look and a painting survive a code and a picture; garbage and wrong sizes are refused
    public static bool SelfCheck()
    {
        Cfg.ClassicSprites = false;
        var cat = new CatProfile(0) { Fur1 = Color.FromArgb(255, 10, 20, 30), Pattern = "tabby", Accessory = "halo", Eyes2 = Color.FromArgb(255, 9, 8, 7), Outline = Color.FromArgb(255, 1, 2, 3) };
        using var hand = new Bitmap(HandPaint.W, HandPaint.H, PixelFormat.Format32bppArgb);
        hand.SetPixel(5, 6, Color.FromArgb(255, 250, 10, 20));
        var back = Decode("hello " + Encode("Mia", cat, hand) + " bye");
        if (back == null || back.Name != "Mia" || back.Look.Pattern != "tabby" || back.Look.Accessory != "halo" || back.Look.Eyes2 != Color.FromArgb(255, 9, 8, 7) || back.Look.Outline != Color.FromArgb(255, 1, 2, 3) || back.Hand == null || back.Hand.GetPixel(5, 6).ToArgb() != Color.FromArgb(255, 250, 10, 20).ToArgb()) { Console.Error.WriteLine("code round trip failed"); return false; }
        if (Encode("x", cat, null).Length > 400) { Console.Error.WriteLine("code of a plain look too long"); return false; }
        var file = Path.Combine(Path.GetTempPath(), "tamapet-check.png");
        File.WriteAllBytes(file, MakeImage("Mia", cat, hand));
        var fromPic = DecodeFile(file);
        if (Environment.GetEnvironmentVariable("TAMAPET_SHARE_PNG") is { } keep) File.Copy(file, keep, true);   // to look at the card
        File.Delete(file);
        if (fromPic == null || fromPic.Look.Fur1 != cat.Fur1 || fromPic.Hand == null) { Console.Error.WriteLine("picture round trip failed"); return false; }
        if (Decode("tamapet:AAAAAAAAAAAA") != null || Decode("nothing here") != null) { Console.Error.WriteLine("garbage accepted"); return false; }
        using var tiny = new Bitmap(10, 10);
        if (Decode(Encode("x", cat, tiny))?.Hand != null) { Console.Error.WriteLine("wrong size accepted"); return false; }
        return true;
    }
}
