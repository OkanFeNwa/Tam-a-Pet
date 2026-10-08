using System.Drawing;

// A saved cat look (every colour, the pattern and the accessory) that can be applied to any cat.
// The built-in ones are templates (read-only: duplicate one to change it); the user's own live in presets.ini next to settings.ini.
sealed class LookPreset
{
    public string Name = "", Key = "";   // Key != "": a built-in template (its name is translated)
    public Color Fur1, Ears = CatSprite.DefaultEars, Nose = CatSprite.DefaultNose, Eyes, Fur3 = Color.FromArgb(0xE8, 0x91, 0x3C);
    public Color? Shade, Light, Outline, Eyes2;
    public string Pattern = "none", Accessory = "";

    public bool Builtin => Key != "";
    public string Display => Builtin ? Str.T("bp." + Key) : Name;

    public static LookPreset From(CatProfile p, string name) => new()
    {
        Name = name, Fur1 = p.Fur1, Shade = p.Shade, Light = p.Light, Outline = p.Outline, Ears = p.Ears, Nose = p.Nose, Eyes = p.Eyes, Eyes2 = p.Eyes2,
        Fur3 = p.Fur3, Pattern = p.Pattern, Accessory = p.Accessory
    };

    public void Apply(CatProfile p)
    {
        p.Fur1 = Fur1; p.Shade = Shade; p.Light = Light; p.Outline = Outline; p.Ears = Ears; p.Nose = Nose; p.Eyes = Eyes; p.Eyes2 = Eyes2;
        p.Fur3 = Fur3; p.Pattern = Pattern; p.Accessory = Accessory;
    }

    public CatProfile Sample() { var p = new CatProfile(0); Apply(p); return p; }   // a throw-away cat wearing it (for thumbnails)

    public LookPreset Copy(string name) { var c = (LookPreset)MemberwiseClone(); c.Name = name; c.Key = ""; return c; }

    // name, fur, shade, light, outline, ears, nose, eyes, variant, pattern, accessory: tab separated, "-" = not set
    public string ToLine()
    {
        string H(Color? c) => c is Color v ? CatSprite.Hex(v) : "-";
        return string.Join("\t", Name.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' '), H(Fur1), H(Shade), H(Light), H(Outline), H(Ears), H(Nose), H(Eyes), H(Fur3), Pattern, Accessory, H(Eyes2));
    }

    public static LookPreset? Parse(string line)
    {
        var f = line.Split('\t');
        if (f.Length < 11 || f[0].Trim() == "") return null;
        Color? C(string s) => CatSprite.TryParse(s, out var c) ? c : null;
        var p = new LookPreset { Name = f[0] };
        if (C(f[1]) is not Color fur) return null;
        p.Fur1 = fur; p.Shade = C(f[2]); p.Light = C(f[3]); p.Outline = C(f[4]);
        p.Ears = C(f[5]) ?? p.Ears; p.Nose = C(f[6]) ?? p.Nose; p.Eyes = C(f[7]) ?? CatSprite.DefaultEyes; p.Fur3 = C(f[8]) ?? p.Fur3; if (f.Length > 11) p.Eyes2 = C(f[11]);
        p.Pattern = f[9] is "tabby" or "patches" or "calico" ? f[9] : "none";
        p.Accessory = CatSprite.Accessories.Contains(f[10]) ? f[10] : "";
        return p;
    }

}

static class LookPresets
{
    public const int MaxUser = 30;
    public static readonly List<LookPreset> User = new();
    static bool loaded;

    static Color H(int rgb) => Color.FromArgb(unchecked((int)0xFF000000) | rgb);   // opaque (FromArgb(int) alone has alpha 0)

    public static readonly LookPreset[] Builtin =
    {
        new() { Key = "grey", Fur1 = Color.FromArgb(98, 103, 115), Eyes = H(0x4CC24A) },
        new() { Key = "orange", Fur1 = H(0xE8913C), Eyes = H(0x4CC24A), Pattern = "tabby", Fur3 = H(0xB5651D), Nose = H(0xFF96B4), Ears = H(0xFF96B4) },
        new() { Key = "siamese", Fur1 = H(0xF0E2C2), Eyes = H(0x3A8DFF), Pattern = "patches", Fur3 = H(0x6B4A3A), Nose = H(0x8B5A3C), Ears = H(0x8B5A3C) },
        new() { Key = "black", Fur1 = H(0x2A2A2E), Eyes = H(0xF2D83C), Nose = H(0x55565C), Ears = H(0x55565C), Outline = H(0x0A0A0C) },
        new() { Key = "calico", Fur1 = H(0x55565C), Eyes = H(0xF0A020), Pattern = "calico", Fur3 = H(0xE8913C) },
        new() { Key = "candy", Fur1 = H(0xFFB6D5), Eyes = H(0xA05CFF), Nose = H(0xE0525A), Ears = H(0xFF96B4), Outline = H(0x5A2A44) },
    };

    static string FilePath => Path.Combine(Cfg.Dir, "presets.ini");

    public static void EnsureLoaded()
    {
        if (loaded) return;
        loaded = true;
        try { foreach (var l in File.ReadAllLines(FilePath)) if (LookPreset.Parse(l) is { } p && User.Count < MaxUser) User.Add(p); }
        catch { }
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Cfg.Dir);
            File.WriteAllLines(FilePath, User.Select(p => p.ToLine()));
        }
        catch { }
    }

    // a name nobody else has: "Name", "Name (2)", ...
    public static string Unique(string name, LookPreset? except = null)
    {
        name = name.Trim(); if (name == "") name = Str.T("preset.new");
        bool Taken(string n) => User.Any(p => p != except && string.Equals(p.Name, n, StringComparison.CurrentCultureIgnoreCase)) || Builtin.Any(b => string.Equals(b.Display, n, StringComparison.CurrentCultureIgnoreCase));
        if (!Taken(name)) return name;
        string stem = System.Text.RegularExpressions.Regex.Replace(name, @" \(\d+\)$", "");
        for (int i = 2; ; i++) if (!Taken($"{stem} ({i})")) return $"{stem} ({i})";
    }

    // runnable check (--check-presets): every template survives a text round trip, names never collide
    public static bool SelfCheck()
    {
        foreach (var b in Builtin)
        {
            var back = LookPreset.Parse(b.Copy("x").ToLine());
            if (back == null || back.Fur1 != b.Fur1 || back.Shade != b.Shade || back.Outline != b.Outline || back.Eyes != b.Eyes || back.Pattern != b.Pattern || back.Fur3 != b.Fur3) { Console.Error.WriteLine("round trip failed: " + b.Key); return false; }
        }
        User.Clear(); User.Add(new LookPreset { Name = "Mia", Fur1 = Color.Red });
        if (Unique("mia") != "mia (2)" || Unique("Mia", User[0]) != "Mia" || Unique("") != Str.T("preset.new")) { Console.Error.WriteLine("unique names failed"); return false; }
        return true;
    }
}
