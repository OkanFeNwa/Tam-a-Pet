using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

// LAB: the cat painter. Browse every row of the sprite sheet (animation / direction), play it, and recolour each part of the
// cat (fur, shade, highlight, outline, ears, nose, eyes, pattern, accessory). Click the cat to pick the part under the mouse.
// Standalone window (--paint): it never touches the settings or the running pet.
sealed class PainterForm : Form
{
    const int Cell = 32, Cols = 11, Zoom = 14;
    static readonly (string name, string label)[] Parts =
    {
        ("fur", "Pelo"), ("shade", "Ombra"), ("light", "Luce"), ("outline", "Contorno"),
        ("ears", "Orecchie"), ("nose", "Naso"), ("eyes", "Occhi"), ("var", "Variante (disegno)")
    };
    static readonly Dictionary<int, string> RowNames = new()
    {
        [0] = "Seduto", [1] = "In piedi", [2] = "Sdraiato", [4] = "Cammina giù", [5] = "Cammina su", [6] = "Cammina destra", [7] = "Cammina sinistra",
        [8] = "Cammina giù-sx", [9] = "Cammina giù-dx", [10] = "Cammina su-dx", [11] = "Cammina su-sx",
        [12] = "Dorme (sx)", [13] = "Dorme (dx)", [20] = "Mangia giù", [22] = "Mangia sx", [23] = "Mangia dx", [32] = "Sbadiglio", [36] = "Si lava",
        [39] = "Si gratta (sx)", [40] = "Si gratta (dx)", [41] = "Soffia (sx)", [42] = "Soffia (dx)", [46] = "Zampata sx", [47] = "Zampata dx", [52] = "Salto"
    };

    readonly CatProfile prof = new(0);
    Bitmap orig, sheet;
    int[] frames = new int[0];
    int row, frame;
    string sel = "fur";
    readonly Dictionary<string, Panel> swatches = new();
    readonly Panel view = new() { Left = 230, Top = 12, Width = Cell * Zoom, Height = Cell * Zoom, Cursor = Cursors.Cross };
    readonly ListBox list = new() { Left = 12, Top = 12, Width = 205, Height = 560, IntegralHeight = false };
    readonly System.Windows.Forms.Timer timer = new() { Interval = 110 };
    readonly CheckBox play = new() { Text = "Animazione", Checked = true, Left = 230, Top = Cell * Zoom + 24, AutoSize = true };
    readonly Label info = new() { Left = 340, Top = Cell * Zoom + 26, AutoSize = true };

    public PainterForm()
    {
        Text = "Tam-a-Pet · Laboratorio colori (prova)";
        ClientSize = new Size(230 + Cell * Zoom + 300, 590);
        FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Cfg.ClassicSprites = false;
        Reset();
        orig = CatSprite.BuildSheet(new CatProfile(0));
        sheet = orig;
        typeof(Panel).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(view, true);

        // frames per row = filled cells in the untouched art
        frames = new int[orig.Height / Cell];
        for (int r = 0; r < frames.Length; r++)
            for (int c = 0; c < Cols; c++) if (CellFilled(r, c)) frames[r] = c + 1;
        for (int r = 0; r < frames.Length; r++)
            if (frames[r] > 0) list.Items.Add(new RowItem(r, RowNames.TryGetValue(r, out var n) ? n : $"Riga {r}", frames[r]));
        list.SelectedIndexChanged += (_, _) => { row = ((RowItem)list.SelectedItem!).Row; frame = 0; view.Invalidate(); };
        list.SelectedIndex = 0;

        view.Paint += OnPaint;
        view.MouseDown += OnPick;
        timer.Tick += (_, _) => { if (play.Checked) { frame = (frame + 1) % Math.Max(1, frames[row]); view.Invalidate(); } };
        timer.Start();

        int x = 230 + Cell * Zoom + 20, y = 14;
        Controls.Add(new Label { Text = "Parti (o clicca sul gatto)", Left = x, Top = y, AutoSize = true, Font = new Font(Font, FontStyle.Bold) }); y += 28;
        foreach (var (name, label) in Parts)
        {
            var sw = new Panel { Left = x, Top = y, Width = 44, Height = 26, BorderStyle = BorderStyle.FixedSingle, Cursor = Cursors.Hand };
            var b = new Button { Text = label, Left = x + 54, Top = y - 1, Width = 190, Height = 28, TextAlign = ContentAlignment.MiddleLeft };
            sw.Click += (_, _) => Choose(name); b.Click += (_, _) => Choose(name);
            swatches[name] = sw; Controls.Add(sw); Controls.Add(b); y += 34;
        }
        y += 6;
        Controls.Add(new Label { Text = "Disegno", Left = x, Top = y + 4, AutoSize = true });
        var pat = new ComboBox { Left = x + 70, Top = y, Width = 170, DropDownStyle = ComboBoxStyle.DropDownList };
        pat.Items.AddRange(new object[] { "none", "tabby", "patches", "calico" }); pat.SelectedItem = "none";
        pat.SelectedIndexChanged += (_, _) => { prof.Pattern = (string)pat.SelectedItem!; Rebuild(); };
        Controls.Add(pat); y += 34;
        Controls.Add(new Label { Text = "Accessorio", Left = x, Top = y + 4, AutoSize = true });
        var acc = new ComboBox { Left = x + 70, Top = y, Width = 170, DropDownStyle = ComboBoxStyle.DropDownList };
        acc.Items.Add("(nessuno)"); acc.Items.AddRange(CatSprite.Accessories); acc.SelectedIndex = 0;
        acc.SelectedIndexChanged += (_, _) => { prof.Accessory = acc.SelectedIndex == 0 ? "" : (string)acc.SelectedItem!; Rebuild(); };
        Controls.Add(acc); y += 44;

        var reset = new Button { Text = "Ripristina colori", Left = x, Top = y, Width = 120, Height = 30 };
        reset.Click += (_, _) => { Reset(); pat.SelectedItem = "none"; acc.SelectedIndex = 0; Rebuild(); };
        var save = new Button { Text = "Salva PNG…", Left = x + 128, Top = y, Width = 115, Height = 30 };
        save.Click += (_, _) =>
        {
            using var d = new SaveFileDialog { Filter = "PNG|*.png", FileName = "gatto-colorato.png" };
            if (d.ShowDialog() == DialogResult.OK) sheet.Save(d.FileName, ImageFormat.Png);
        };
        Controls.AddRange(new Control[] { list, view, play, info, reset, save });
        Rebuild();
    }

    sealed record RowItem(int Row, string Name, int Frames) { public override string ToString() => $"{Row,2}  {Name} ({Frames})"; }

    bool CellFilled(int r, int c)
    {
        for (int y = 0; y < Cell; y++) for (int x = 0; x < Cell; x++) if (orig.GetPixel(c * Cell + x, r * Cell + y).A != 0) return true;
        return false;
    }

    void Reset()
    {
        prof.ResetLook();
        prof.Fur1 = Color.FromArgb(98, 103, 115); prof.Eyes = Color.FromArgb(76, 194, 74);
        sel = "fur";
        if (swatches.Count > 0) UpdateSwatches();
    }

    Color Current(string part) => part switch
    {
        "fur" => prof.Fur1, "shade" => prof.Shade ?? Color.FromArgb(prof.Fur1.R * 66 / 100, prof.Fur1.G * 66 / 100, prof.Fur1.B * 66 / 100),
        "light" => prof.Light ?? Color.FromArgb(prof.Fur1.R + (255 - prof.Fur1.R) * 3 / 10, prof.Fur1.G + (255 - prof.Fur1.G) * 3 / 10, prof.Fur1.B + (255 - prof.Fur1.B) * 3 / 10),
        "outline" => prof.Outline ?? Color.FromArgb(18, 14, 20), "ears" => prof.Ears, "nose" => prof.Nose, "eyes" => prof.Eyes, _ => prof.Fur3
    };

    void Choose(string part)
    {
        sel = part; UpdateSwatches();
        using var d = new ColorDialog { Color = Current(part), FullOpen = true };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        var c = d.Color;
        switch (part)
        {
            case "fur": prof.Fur1 = c; break;
            case "shade": prof.Shade = c; break;
            case "light": prof.Light = c; break;
            case "outline": prof.Outline = c; break;
            case "ears": prof.Ears = c; break;
            case "nose": prof.Nose = c; break;
            case "eyes": prof.Eyes = c; break;
            default: prof.Fur3 = c; if (prof.Pattern == "none") { prof.Pattern = "patches"; } break;
        }
        Rebuild();
    }

    void UpdateSwatches()
    {
        foreach (var (name, sw) in swatches) { sw.BackColor = Current(name); sw.BorderStyle = name == sel ? BorderStyle.Fixed3D : BorderStyle.FixedSingle; }
    }

    void Rebuild()
    {
        var old = sheet;
        sheet = CatSprite.BuildSheet(prof);
        if (!ReferenceEquals(old, orig)) old.Dispose();
        UpdateSwatches(); view.Invalidate();
    }

    void OnPaint(object? s, PaintEventArgs e)
    {
        var g = e.Graphics;
        for (int y = 0; y < Cell; y++)   // checkerboard: shows what is transparent
            for (int x = 0; x < Cell; x++)
                using (var b = new SolidBrush((x + y) % 2 == 0 ? Color.FromArgb(70, 72, 80) : Color.FromArgb(82, 84, 92))) g.FillRectangle(b, x * Zoom, y * Zoom, Zoom, Zoom);
        g.InterpolationMode = InterpolationMode.NearestNeighbor; g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(sheet, new Rectangle(0, 0, Cell * Zoom, Cell * Zoom), frame * Cell, row * Cell, Cell, Cell, GraphicsUnit.Pixel);
        info.Text = $"riga {row} · immagine {frame + 1}/{frames[row]}";
    }

    // what is under the mouse? read the untouched art: the colour tells the part (the eyes are the dark dots found by BuildNew)
    void OnPick(object? s, MouseEventArgs e)
    {
        int px = frame * Cell + e.X / Zoom, py = row * Cell + e.Y / Zoom;
        if (px >= orig.Width || py >= orig.Height) return;
        var c = RawPixel(px, py);
        if (c.A == 0) return;
        string part;
        if (CatSprite.LastEyes?.Contains((px, py)) == true) part = "eyes";
        else if (c.R == 202 && c.G == 113 && c.B == 159) part = "nose";
        else if (c.R == 154 && c.G == 135 && c.B == 126) part = "ears";
        else if (c.R == 18 && c.G == 14 && c.B == 20) part = "outline";
        else if (c.R == 98 && c.G == 103 && c.B == 115) part = "fur";
        else if (c.R == 65 && c.G == 71 && c.B == 82) part = "shade";
        else if (c.R == 134 && c.G == 141 && c.B == 155) part = "light";
        else return;
        Choose(part);
    }

    Bitmap? raw;
    Color RawPixel(int x, int y)
    {
        if (raw == null) { using var st = typeof(CatSprite).Assembly.GetManifestResourceStream("spr.cat")!; raw = new Bitmap(st); }
        return raw.GetPixel(x, y);
    }

    protected override void Dispose(bool disposing) { if (disposing) { timer.Dispose(); raw?.Dispose(); sheet?.Dispose(); } base.Dispose(disposing); }
}
