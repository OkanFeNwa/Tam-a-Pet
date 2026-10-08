using System.Drawing;
using System.Globalization;

// A cat's character: multipliers on how it behaves. Each cat has its own.
sealed record Trait(string Key, double Activity, double Appetite, double Laziness, double Mood, double Pitch, double Chatty, double Needy = 0);

static class Characters
{
    // How long each need lasts from full to empty, in units of the balanced cat's time (10, 15 and 5 "minutes", which the
    // cat doubles to 20, 30 and 10: see PetWindow), for every character. The three always add up to 30:
    // a character that is weak in one need makes up for it in another. The balanced cat is the reference (hunger 10,
    // energy 15, happiness 5); a Trait stores the speed relative to it (10 / minutes, ...).
    //                         activity  hunger energy happiness   pitch chatty
    static Trait T(string key, double activity, double hunger, double energy, double happiness, double pitch, double chatty, double needy = 0) =>
        new(key, activity, 10 / hunger, 15 / energy, 5 / happiness, pitch, chatty, needy);

    public static readonly Trait[] All =
    {
        T("balanced", 1.00, 10, 15, 5, 1.00, 1.0),                 // 10 + 15 + 5 = 30
        T("playful",  1.35, 13, 13, 4, 1.10, 1.2),                 // livelier: gets bored (sad) fast, tires a bit sooner, but is not a big eater
        T("lazy",     0.55, 12, 11, 7, 0.92, 0.7),                 // moves little but tires sooner; eats less and is easily content
        T("greedy",   0.90, 6, 18, 6, 0.96, 1.0),                  // always hungry, but plenty of energy
        T("chatty",   1.00, 11, 15, 4, 1.05, 2.5),                 // meows a lot and likes company: less patient
        T("needy",    1.00, 12, 15, 3, 1.08, 1.8, 1),              // wants attention: happiness drops very fast, calls you and comes to the mouse
    };

    public static Trait Get(string key) => All.FirstOrDefault(t => t.Key == key) ?? All[0];
}

// Everything that belongs to one cat: name, look, character, needs, its objects (colour + saved position).
sealed class CatProfile
{
    public readonly int Index;
    public string Name = "";
    public Color Fur1, Fur2, Eyes;
    public Color? Eyes2;   // heterochromia: the right eye (of the picture) in another colour; null = both the same
    public Color Nose = CatSprite.DefaultNose, Ears = CatSprite.DefaultEars;   // new sprites: the pink nose and the inside of the ears
    public Color Fur3 = Color.FromArgb(0xE8, 0x91, 0x3C);   // the colour of the variant (stripes, patches)
    public Bitmap? Hand;   // what was painted by hand over the cat (see HandPaint); null = nothing
    public Color? Shade, Light, Outline;   // painter overrides for the three fur tones' shade/highlight and the outline (null = derived / as drawn)
    public string Accessory = "";   // "" or one of CatSprite.Accessories (new sprites only)
    public string Pattern = "none";   // none | tabby | patches | calico
    public int Volume = 100, VoiceVol = 100, PurrVol = 100, FxVol = 100;   // this pet in the mixer: all of it, then its voice, purring and effects
    public Color BowlColor = Color.FromArgb(0xE0, 0x52, 0x5A), BedColor = Color.FromArgb(0x7F, 0x95, 0xF0), BallColor = Color.FromArgb(0xFF, 0x7A, 0x45);
    public string Character;
    public double Hunger = 100, Happiness = 100, Energy = 100;
    public string Monitor = "";   // where the pet (and its objects) live: "" = the main monitor, "free" = every monitor, or a monitor's device name
    public const double BowlCapacity = 400;   // how much the bowl holds, in points of hunger
    public double BowlFood = BowlCapacity;    // and how much is left
    public string Bowl = "", Bed = "", Ball = "";   // saved objects: "x" (bowl, bed) or "x,y" (ball); empty = not summoned

    public CatProfile(int index)
    {
        Index = index;
        Character = Characters.All[index == 0 ? 0 : (index % (Characters.All.Length - 1)) + 1].Key;
        ResetLook();
    }

    static readonly (int fur1, int fur2, int eyes)[] Looks = { (0xE8913C, 0xF0E2C2, 0x4CC24A), (0x55565C, 0xE0E0E0, 0xF0A020), (0x8B5A3C, 0xF0E2C2, 0x3A8DFF), (0x2A2A2E, 0xE0E0E0, 0xF2D83C), (0xF0E2C2, 0xE0E0E0, 0xA05CFF), (0xB5B5B5, 0xE0E0E0, 0x4CC24A), (0xE8913C, 0xE0E0E0, 0x3A8DFF) };

    public Trait Trait => Characters.Get(Character);
    public string Display => Name != "" ? Name : $"{Str.T("cat.default")} {Index + 1}";

    public void ResetLook()
    {
        Pattern = "none"; Accessory = ""; Shade = Light = Outline = null; Eyes2 = null; Nose = CatSprite.DefaultNose; Ears = CatSprite.DefaultEars;
        if (Index == 0) { Fur1 = CatSprite.DefaultFur1; Fur2 = CatSprite.DefaultFur2; Eyes = CatSprite.DefaultEyes; }
        else
        {
            var (a, b, e) = Looks[(Index - 1) % Looks.Length];
            Fur1 = Color.FromArgb(a); Fur2 = Color.FromArgb(b); Eyes = Color.FromArgb(e);
        }
    }

    public void ResetObjectColors()
    {
        BowlColor = Color.FromArgb(0xE0, 0x52, 0x5A); BedColor = Color.FromArgb(0x7F, 0x95, 0xF0); BallColor = Color.FromArgb(0xFF, 0x7A, 0x45);
    }
}

// User settings: a flat key -> number table (sliders bind to the keys), the language, and the two cats' profiles.
static class Cfg
{
    public static readonly Dictionary<string, double> Defaults = new()
    {
        ["volume"] = 40, ["nearRange"] = 150, ["jumpRange"] = 500, ["jumpSpeed"] = 2000, ["jumpCooldown"] = 5,
        ["wander"] = 0.08, ["walkSpeed"] = 2, ["cycle"] = 1328
    };
    public static Dictionary<string, double> V = new(Defaults);
    public static string Lang = "en";   // English is the main language; the user can switch to Italian in the settings
    public static bool ClassicSprites;   // the original sprite sheet instead of the new one (default)
    public static void SetClassic(bool on) { ClassicSprites = on; Save(); foreach (var c in Cats.ToList()) ProfileChanged?.Invoke(c); }
    public static bool AutoUpdate = true;   // look for a newer release at start (installing it is always manual)
    public const int MaxCats = 8;
    public static readonly List<CatProfile> Cats = new() { new(0) };   // every cat the user has (each with its own window); the first one has Index 0
    public static CatProfile? ById(int index) => Cats.FirstOrDefault(c => c.Index == index);
    public static readonly List<Color> Recent = new();   // colours picked with the colour picker, newest first (kept between runs)
    public static bool ShareObjects;   // all cats use the same bowl, bed and ball (those of the first cat)
    public static bool OnTop = true;   // keep the cats above other windows
    public static bool Dev;   // developer mode, unlocked from the version label in settings

    // TAMAPET_DIR overrides the folder (used by tests so they never touch the real settings)
    public static string Dir => Environment.GetEnvironmentVariable("TAMAPET_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Tam-a-Pet");
    static string FilePath => Path.Combine(Dir, "settings.ini");

    public static double NearRange => V["nearRange"];
    public static double JumpRange => V["jumpRange"];
    public static double JumpSpeed => V["jumpSpeed"];
    public static double JumpCooldown => V["jumpCooldown"];
    public static double Wander => V["wander"];
    public static double WalkSpeed => V["walkSpeed"];
    public static double Cycle => V["cycle"];

    public static event Action? LanguageChanged;
    public static event Action? OnTopChanged;
    public static event Action<CatProfile>? ProfileChanged;      // name, colours, character, objects' colours of one cat
    public static event Action<CatProfile>? CatAdded, CatRemoved;
    public static event Action? ShareChanged;
    public static void SetShare(bool on) { ShareObjects = on; Save(); ShareChanged?.Invoke(); }

    // after changing something in a cat's profile: save and tell the cat
    public static void Changed(CatProfile p) { Save(); ProfileChanged?.Invoke(p); }

    public static event Action? MonitorChanged;
    public static void SetMonitor(CatProfile p, string monitor) { p.Monitor = monitor; Save(); MonitorChanged?.Invoke(); }

    // A new cat takes the lowest free slot (its window, sounds and settings keys are tied to it)
    public static CatProfile? AddCat()
    {
        if (Cats.Count >= MaxCats) return null;
        int idx = Enumerable.Range(0, MaxCats).First(i => ById(i) == null);
        var p = new CatProfile(idx);
        Cats.Add(p);
        Cats.Sort((x, y) => x.Index.CompareTo(y.Index));
        Save();
        CatAdded?.Invoke(p);
        return p;
    }

    public static void RemoveCat(CatProfile p)
    {
        if (Cats.Count <= 1) return;
        Cats.Remove(p);
        HandPaint.Clear(p);
        CatRemoved?.Invoke(p);   // its window goes away, then the file no longer has it
        Save();
    }

    public static void AddRecent(Color c)
    {
        Recent.RemoveAll(x => x.ToArgb() == c.ToArgb());
        Recent.Insert(0, c);
        if (Recent.Count > 10) Recent.RemoveRange(10, Recent.Count - 10);
        Save();
    }

    public static void SetOnTop(bool on) { OnTop = on; Save(); OnTopChanged?.Invoke(); }

    static bool Hex(string v, out Color c) => CatSprite.TryParse(v, out c);

    // One "key=value" of a cat; the old single-cat file used the same keys without the "c1." prefix
    static void ReadCat(CatProfile p, string key, string val)
    {
        switch (key)
        {
            case "name": p.Name = val; break;
            case "stripes": if (val == "1" && p.Pattern == "none") p.Pattern = "tabby"; break;   // the first version of the patterns
            case "pattern": if (val is "none" or "tabby" or "patches" or "calico") p.Pattern = val; break;
            case "acc": p.Accessory = CatSprite.Accessories.Contains(val) ? val : ""; break;
            case "fur3": if (Hex(val, out var c4)) p.Fur3 = c4; break;
            case "vol": case "volvoice": case "volpurr": case "volfx":
                if (int.TryParse(val, out var vv)) { vv = Math.Clamp(vv, 0, 100); if (key == "vol") p.Volume = vv; else if (key == "volvoice") p.VoiceVol = vv; else if (key == "volpurr") p.PurrVol = vv; else p.FxVol = vv; }
                break;
            case "char": if (Characters.All.Any(t => t.Key == val)) p.Character = val; break;
            case "fur1": if (Hex(val, out var c1)) p.Fur1 = c1; break;
            case "fur2": if (Hex(val, out var c2)) p.Fur2 = c2; break;
            case "eyes": if (Hex(val, out var c3)) p.Eyes = c3; break;
            case "nose": if (Hex(val, out var c5)) p.Nose = c5; break;
            case "ears": if (Hex(val, out var c6)) p.Ears = c6; break;
            case "eyes2": if (Hex(val, out var s0)) p.Eyes2 = s0; break;
            case "shade": if (Hex(val, out var s1)) p.Shade = s1; break;
            case "light": if (Hex(val, out var s2)) p.Light = s2; break;
            case "outline": if (Hex(val, out var s3)) p.Outline = s3; break;
            case "bowlc": case "bowlbody": if (Hex(val, out var o1)) p.BowlColor = o1; break;
            case "bedc": case "bedouter": if (Hex(val, out var o2)) p.BedColor = o2; break;
            case "ballc": case "ballbody": if (Hex(val, out var o3)) p.BallColor = o3; break;
            case "hunger": case "happiness": case "energy":
                if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var st))
                {
                    st = Math.Clamp(st, 0, 100);
                    if (key == "hunger") p.Hunger = st; else if (key == "happiness") p.Happiness = st; else p.Energy = st;
                }
                break;
            case "monitor": p.Monitor = val; break;
            case "bowl": p.Bowl = val; break;
            case "bowlfood": if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var bf)) p.BowlFood = Math.Clamp(bf, 0, CatProfile.BowlCapacity); break;
            case "bed": p.Bed = val; break;
            case "ball": p.Ball = val; break;
        }
    }

    public static void Load()
    {
        var off = new HashSet<int>();
        try
        {
            foreach (var line in File.ReadAllLines(FilePath))
            {
                var kv = line.Split('=', 2);
                if (kv.Length != 2) continue;
                string key = kv[0], val = kv[1];
                if (key == "lang") Lang = val;
                else if (key == "dev") Dev = val == "1";
                else if (key == "onTop") OnTop = val != "0";
                else if (key == "share") ShareObjects = val == "1";
                else if (key == "autoupdate") AutoUpdate = val != "0";
                else if (key == "classic") ClassicSprites = val == "1";
                else if (key == "custom")
                {
                    foreach (var hex in val.Split(',', StringSplitOptions.RemoveEmptyEntries))
                        if (Hex(hex, out var rc) && Recent.Count < 10) Recent.Add(rc);
                }
                else if (key.Length > 3 && key[0] == 'c' && key[2] == '.' && char.IsDigit(key[1]) && key[1] - '1' is >= 0 and < MaxCats)
                {
                    int ci = key[1] - '1';
                    if (key == $"c{ci + 1}.on") { if (ci > 0 && val == "0") off.Add(ci); continue; }   // the old file could switch the second cat off
                    var cp = ById(ci);
                    if (cp == null) { cp = new CatProfile(ci); Cats.Add(cp); }
                    ReadCat(cp, key.Substring(3), val);
                }
                else if (V.ContainsKey(key) && double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) V[key] = d;
                else ReadCat(Cats[0], key, val);   // the old single-cat file
            }
        }
        catch { }
        Cats.RemoveAll(c => off.Contains(c.Index));
        Cats.Sort((x, y) => x.Index.CompareTo(y.Index));
        foreach (var p in Cats) HandPaint.Load(p);
        foreach (var p in Cats)
            if (p.Eyes.ToArgb() == Color.FromArgb(47, 47, 46).ToArgb()) p.Eyes = CatSprite.DefaultEyes;   // the old default eye colour: now pure black like the outline
        if (V["jumpSpeed"] == 800) V["jumpSpeed"] = Defaults["jumpSpeed"];   // old default was too sensitive
    }

    static string D(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var lines = new List<string>();
            lines.AddRange(V.Select(p => $"{p.Key}={p.Value.ToString(CultureInfo.InvariantCulture)}"));
            lines.Add($"lang={Lang}");
            lines.Add($"dev={(Dev ? 1 : 0)}");
            lines.Add($"onTop={(OnTop ? 1 : 0)}");
            lines.Add($"share={(ShareObjects ? 1 : 0)}");
            lines.Add($"autoupdate={(AutoUpdate ? 1 : 0)}");
            lines.Add($"classic={(ClassicSprites ? 1 : 0)}");
            if (Recent.Count > 0) lines.Add("custom=" + string.Join(",", Recent.Select(CatSprite.Hex)));
            foreach (var p in Cats)
            {
                string k = $"c{p.Index + 1}.";
                lines.Add($"{k}name={p.Name.Replace('\n', ' ').Replace('\r', ' ')}");
                lines.Add($"{k}char={p.Character}");
                lines.Add($"{k}fur1={CatSprite.Hex(p.Fur1)}");
                lines.Add($"{k}fur2={CatSprite.Hex(p.Fur2)}");
                lines.Add($"{k}pattern={p.Pattern}");
                lines.Add($"{k}fur3={CatSprite.Hex(p.Fur3)}");
                lines.Add($"{k}acc={p.Accessory}");
                lines.Add($"{k}vol={p.Volume}");
                lines.Add($"{k}volvoice={p.VoiceVol}");
                lines.Add($"{k}volpurr={p.PurrVol}");
                lines.Add($"{k}volfx={p.FxVol}");
                lines.Add($"{k}eyes={CatSprite.Hex(p.Eyes)}");
                lines.Add($"{k}nose={CatSprite.Hex(p.Nose)}");
                lines.Add($"{k}ears={CatSprite.Hex(p.Ears)}");
                if (p.Eyes2 is Color e2) lines.Add($"{k}eyes2={CatSprite.Hex(e2)}");
                if (p.Shade is Color sh) lines.Add($"{k}shade={CatSprite.Hex(sh)}");
                if (p.Light is Color li) lines.Add($"{k}light={CatSprite.Hex(li)}");
                if (p.Outline is Color ou) lines.Add($"{k}outline={CatSprite.Hex(ou)}");
                lines.Add($"{k}bowlc={CatSprite.Hex(p.BowlColor)}");
                lines.Add($"{k}bedc={CatSprite.Hex(p.BedColor)}");
                lines.Add($"{k}ballc={CatSprite.Hex(p.BallColor)}");
                lines.Add($"{k}hunger={D(p.Hunger)}");
                lines.Add($"{k}happiness={D(p.Happiness)}");
                lines.Add($"{k}energy={D(p.Energy)}");
                if (p.Monitor != "") lines.Add($"{k}monitor={p.Monitor}");
                if (p.Bowl != "") lines.Add($"{k}bowl={p.Bowl}");
                lines.Add($"{k}bowlfood={D(p.BowlFood)}");
                if (p.Bed != "") lines.Add($"{k}bed={p.Bed}");
                if (p.Ball != "") lines.Add($"{k}ball={p.Ball}");
            }
            File.WriteAllLines(FilePath, lines);
        }
        catch { }
    }

    public static void SetLang(string lang)
    {
        Lang = lang;
        Save();
        LanguageChanged?.Invoke();
    }

    public static void ResetCat()
    {
        double volume = V["volume"];
        V = new(Defaults);
        V["volume"] = volume;   // sound level is a user preference, not tuning
        Save();
    }
}

// UI strings. Add a language = add a block here and an entry in SettingsForm.Languages.
static class Str
{
    static readonly Dictionary<string, Dictionary<string, string>> All = new()
    {
        ["it"] = new()
        {
            ["paint.open"] = "Studio colori…", ["paint.title"] = "Studio colori", ["paint.anim"] = "Animazione", ["paint.play"] = "Riproduci", ["paint.pause"] = "Pausa", ["paint.frame"] = "Immagine {0} di {1}", ["paint.parts"] = "Parti", ["paint.auto"] = "Auto", ["part.shade"] = "Ombra", ["part.light"] = "Luce", ["part.outline"] = "Contorno", ["presets.title"] = "Preset", ["preset.save"] = "+  Salva l'aspetto attuale come preset", ["preset.apply"] = "Applica", ["preset.update"] = "Sovrascrivi con l'aspetto attuale", ["preset.rename"] = "Rinomina…", ["preset.dup"] = "Duplica", ["preset.del"] = "Elimina", ["preset.del.ask"] = "Eliminare il preset \"{0}\"?", ["preset.name"] = "Nome del preset", ["preset.new"] = "Il mio preset", ["preset.empty"] = "Nessun preset salvato: colora il gatto e salva l'aspetto.", ["preset.builtin"] = "Modello", ["bp.grey"] = "Grigio", ["bp.orange"] = "Soriano arancione", ["bp.siamese"] = "Siamese", ["bp.black"] = "Gatto nero", ["bp.calico"] = "Calico", ["bp.candy"] = "Caramella", ["an.sit"] = "Seduto", ["an.stand"] = "In piedi", ["an.lie"] = "Sdraiato", ["an.loaf"] = "A pagnotta", ["an.walk"] = "Cammina", ["an.sleep"] = "Dorme", ["an.eat"] = "Mangia", ["an.meow"] = "Miagola", ["an.yawn"] = "Sbadiglia", ["an.wash"] = "Si lava", ["an.scratch"] = "Si gratta", ["an.hiss"] = "Soffia", ["an.paw"] = "Zampata", ["an.jump"] = "Salto", ["an.pose"] = "Posa", ["dir.right"] = "destra", ["dir.left"] = "sinistra", ["dir.up"] = "su", ["dir.down"] = "giù", ["dir.dr"] = "giù-destra", ["dir.dl"] = "giù-sinistra", ["dir.ur"] = "su-destra", ["dir.ul"] = "su-sinistra", ["dir.front"] = "di fronte", ["dir.back"] = "di schiena", ["dir.curled"] = "raggomitolato", ["dir.stretched"] = "allungato", ["tool.brush"] = "Pennello", ["tool.eraser"] = "Gomma", ["tool.pick"] = "Contagocce", ["tool.part"] = "Parte", ["paint.size"] = "Dimensione", ["paint.undo"] = "Annulla", ["paint.brushcolor"] = "Colore del pennello", ["paint.clearframe"] = "Cancella questa immagine", ["paint.clearall"] = "Cancella tutto il disegno", ["paint.clearall.ask"] = "Cancellare tutto quello che hai dipinto a mano su {0}?", ["paint.hint"] = "Dipingi il gatto pixel per pixel. Ogni immagine di ogni animazione si dipinge a parte.", ["paint.static"] = "Pose statiche", ["paint.animated"] = "Animazioni", ["paint.view"] = "Vista {0}", ["paint.redo"] = "Ripeti", ["paint.save"] = "Salva", ["paint.saved"] = "Salvato", ["paint.share"] = "Condividi", ["paint.leave.title"] = "Disegno non salvato", ["paint.leave.msg"] = "Il tuo disegno a mano non è stato salvato. Se esci ora andrà perso.", ["paint.leave.save"] = "Salva ed esci", ["paint.leave.discard"] = "Scarta il disegno", ["paint.leave.stay"] = "Continua a modificare", ["share.copy"] = "Copia il codice da condividere", ["share.image"] = "Salva come immagine…", ["share.paste"] = "Importa dal codice copiato", ["share.file"] = "Importa da un'immagine…", ["share.copied"] = "Codice copiato: incollalo su Discord o dove vuoi.", ["share.copied.nodraw"] = "Codice copiato senza il disegno (troppo grande per un messaggio): condividi l'immagine per includerlo.", ["share.saved"] = "Immagine salvata: mandala a chi vuoi, contiene tutto l'aspetto.", ["share.imported"] = "Aspetto importato. Salva per tenere il disegno.", ["share.invalid"] = "Questo non è un aspetto di Tam-a-Pet.", ["share.failed"] = "Qualcosa è andato storto.", ["share.card.painted"] = "Dipinto a mano", ["share.card.how"] = "Importalo in Tam-a-Pet: Studio colori › Condividi › Importa (o trascina questa immagine sulla finestra)", ["hetero.label"] = "Eterocromia", ["look.eyeL"] = "Occhio sinistro", ["look.eyeR"] = "Occhio destro",
            ["nav.pets"] = "Pet", ["pets.intro"] = "Scegli il tipo di pet da gestire.", ["pets.cats"] = "Gatti", ["pets.count"] = "{0} nel desktop", ["cats.add"] = "Aggiungi gatto", ["cats.max"] = "Hai raggiunto il massimo di {0} gatti.", ["back.pets"] = "‹ Pet", ["back.cats"] = "‹ Gatti", ["cat.remove"] = "Rimuovi questo gatto", ["cat.remove.ask"] = "Rimuovere {0}? Le sue statistiche e i suoi oggetti andranno persi.", ["pattern.title"] = "Variante", ["pattern.none"] = "Nessuna", ["pattern.tabby"] = "Tigrato", ["pattern.patches"] = "A chiazze", ["pattern.calico"] = "Calico", ["look.fur3"] = "Colore della variante", ["mixer.title"] = "Mixer", ["mixer.master"] = "Volume generale", ["mixer.pet"] = "Volume del gatto", ["mix.voice"] = "Voce", ["mix.purr"] = "Fusa", ["mix.fx"] = "Effetti", ["cat.sound"] = "Volume", ["about"] = "Informazioni", ["upd.check"] = "Cerca aggiornamenti", ["autoupdate.label"] = "Cerca aggiornamenti in automatico", ["autoupdate.hint"] = "Controlla su GitHub all'avvio e ogni ora; scaricare e installare richiede sempre un tuo clic", ["upd.balloon"] = "Apri le Impostazioni per installarla.", ["upd.checking"] = "Controllo in corso...", ["upd.latest"] = "Hai l'ultima versione ({0}).", ["upd.avail"] = "Disponibile la versione {0}.", ["upd.install"] = "Scarica e installa", ["upd.installing"] = "Scarico l'aggiornamento...", ["upd.error"] = "Impossibile controllare gli aggiornamenti (nessuna connessione, o nessuna versione pubblicata).", ["version"] = "Versione", ["dev.left"] = "Ancora {0} click per la modalità sviluppatore", ["dev.on"] = "Modalità sviluppatore attivata", ["dev.active"] = "Modalità sviluppatore attiva", ["nav.dev"] = "Sviluppatore", ["dev.intro"] = "Funzioni in anteprima e strumenti di debug.", ["dev.empty"] = "Per ora non c'è niente qui.", ["dev.disable"] = "Disattiva modalità sviluppatore",
            ["dev.actions"] = "Interazioni del gatto", ["dev.state"] = "Stato", ["dev.stats.fmt"] = "Fame {0} · Felicità {1} · Energia {2} · stato: {3}", ["act.idle"] = "Idle", ["act.lick"] = "Leccata", ["act.walk"] = "Cammina", ["act.run"] = "Corri", ["act.sleep"] = "Dormi / sveglia", ["act.play"] = "Gioca (click)", ["act.pounce"] = "Salto", ["act.frenzy"] = "Frenesia", ["act.groom"] = "Si leccano", ["act.fight"] = "Litigano", ["nav.volumes"] = "Volumi", ["about.changelog"] = "Changelog", ["bowl.refill"] = "Riempi la ciotola", ["look.nose"] = "Naso", ["look.ears"] = "Orecchie", ["acc.title"] = "Accessori", ["acc.none"] = "Nessuno", ["look.fur"] = "Pelo", ["classic.label"] = "Sprite classici", ["classic.hint"] = "Usa i vecchi sprite dei gatti al posto dei nuovi (gli accessori funzionano solo con i nuovi)", ["acc.bow-red"] = "Fiocco rosso", ["acc.bow-pink"] = "Fiocco rosa", ["acc.bow-gold"] = "Fiocco oro", ["acc.bow2-blue"] = "Fiocco blu", ["acc.bow2-green"] = "Fiocco verde", ["acc.bow2-pink"] = "Fiocco rosa 2", ["acc.glasses-red"] = "Occhiali rossi", ["acc.glasses-gold"] = "Occhiali oro", ["acc.halo"] = "Aureola", ["acc.wings"] = "Ali", ["acc.cupid"] = "Cupido", ["acc.santa-1"] = "Natale 1", ["acc.santa-2"] = "Natale 2", ["acc.antlers-red"] = "Renna rossa", ["acc.antlers-green"] = "Renna verde", ["share.label"] = "Oggetti condivisi", ["share.hint"] = "Tutti i gatti usano la stessa ciotola, cuccia e pallina (quelli del primo gatto). Gli oggetti degli altri gatti vengono rimossi.", ["objs.shared"] = "Gli oggetti sono condivisi: si gestiscono dalla scheda di {0}.",
            ["menu.objects"] = "Oggetti", ["menu.bowl"] = "Ciotola", ["menu.bed"] = "Cuccia", ["menu.ball"] = "Pallina", ["menu.remove"] = "Rimuovi", ["name.label"] = "Nome del gatto", ["name.hint"] = "Compare sopra le barre delle statistiche",
            ["act.bowl"] = "Ciotola on/off", ["act.bed"] = "Cuccia on/off", ["act.ball"] = "Pallina on/off",
            ["act.hungry"] = "Ha fame", ["act.sad"] = "È triste", ["act.tired"] = "Ha sonno",
            ["onTop.label"] = "Sempre in primo piano", ["onTop.hint"] = "Il gatto resta sopra le altre finestre", ["startup.label"] = "Avvia con Windows", ["startup.hint"] = "Apre il gatto quando accedi al PC", ["volume.label"] = "Volume", ["volume.hint"] = "Miagolii e suoni del gatto (0 = muto)", ["volume.unit"] = "%", ["group.tuning"] = "Parametri del comportamento", ["act.meow"] = "Miagola",
            ["about.creditsBtn"] = "Crediti", ["credits.title"] = "Crediti", ["credits.sounds"] = "Suoni", ["credits.back"] = "Indietro", ["credits.purr"] = "Fusa", ["credits.kitten"] = "Miagolio da gattino (salto)", ["credits.munch"] = "Masticare",
            ["look.title"] = "Aspetto", ["look.fur1"] = "Pelo 1 (schiena e zampe)", ["look.fur2"] = "Pelo 2 (pancia)", ["look.eyes"] = "Occhi", ["look.custom"] = "Scegli un colore", ["look.reset"] = "Ripristina colori",
            ["nav.objects"] = "Oggetti", ["obj.bowlbody"] = "Ciotola", ["obj.bowlfood"] = "Cibo", ["obj.bedouter"] = "Cuccia", ["obj.bedinner"] = "Cuscino", ["obj.ballbody"] = "Pallina", ["obj.ballring"] = "Contorno",
            ["picker.title"] = "Scegli un colore", ["picker.new"] = "Nuovo", ["picker.current"] = "Attuale", ["picker.recent"] = "Usati di recente", ["picker.ok"] = "Conferma", ["picker.cancel"] = "Annulla",
            ["act.pet"] = "Coccole",
            ["cat.default"] = "Gatto", ["cat.on.label"] = "Secondo gatto", ["cat.on.hint"] = "Un secondo gatto, con le sue statistiche, il suo carattere e i suoi oggetti", ["char.title"] = "Carattere", ["char.balanced"] = "Equilibrato", ["char.playful"] = "Giocherellone", ["char.lazy"] = "Pigro", ["char.greedy"] = "Goloso", ["char.chatty"] = "Chiacchierone", ["char.needy"] = "Cerca attenzioni", ["char.needy.hint"] = "La felicità cala molto in fretta: ti chiama e ti raggiunge col mouse.", ["char.balanced.hint"] = "Né troppo vivace né troppo pigro.", ["char.playful.hint"] = "Corre di più e si annoia presto, ma mangia poco.", ["char.lazy.hint"] = "Si muove poco, ma si stanca prima; mangia meno ed è facile da accontentare.", ["char.greedy.hint"] = "Ha sempre fame: svuota la ciotola in fretta, ma ha molta energia.", ["char.chatty.hint"] = "Miagola molto più spesso e ha un po' meno pazienza.", ["objs.title"] = "Oggetti", ["obj.color"] = "Colore", ["dev.target"] = "Gatto da controllare",
            ["stat.hunger"] = "Fame", ["stat.happiness"] = "Felicità", ["stat.energy"] = "Energia",
            ["tray.settings"] = "Impostazioni...", 
            ["tray.exit"] = "Esci",
            ["title"] = "Impostazioni", ["nav.general"] = "Generale", ["nav.cat"] = "Gatto",
            ["language"] = "Lingua", ["language.hint"] = "Lingua dell'interfaccia", ["monitor.label"] = "Monitor", ["monitor.hint"] = "Dove vive questo pet con i suoi oggetti", ["monitor.free"] = "Libero (tutti)", ["monitor.n"] = "Monitor {0}", ["monitor.main"] = "(principale)", ["reset"] = "Ripristina predefiniti",
            
            ["group.mouse"] = "Mouse", ["group.movement"] = "Movimento",
            ["nearRange.label"] = "Distanza \"mouse vicino\"", ["nearRange.hint"] = "Entro questa distanza il gatto si accorge del mouse", ["nearRange.unit"] = "px",
            ["jumpRange.label"] = "Raggio d'azione del salto", ["jumpRange.hint"] = "Entro questa distanza dal gatto il mouse può farlo saltare", ["jumpRange.unit"] = "px",
            ["jumpSpeed.label"] = "Velocità del mouse per il salto", ["jumpSpeed.hint"] = "Più basso = salta più facilmente", ["jumpSpeed.unit"] = "px/s",
            ["jumpCooldown.label"] = "Pausa tra un salto e l'altro", ["jumpCooldown.hint"] = "", ["jumpCooldown.unit"] = "s",
            ["wander.label"] = "Voglia di girare", ["wander.hint"] = "Probabilità al secondo di partire per una passeggiata (0 = sta fermo)", ["wander.unit"] = "% al secondo",
            ["walkSpeed.label"] = "Velocità di camminata", ["walkSpeed.hint"] = "La corsa è il doppio", ["walkSpeed.unit"] = "px",
            ["cycle.label"] = "Durata del ciclo delle animazioni", ["cycle.hint"] = "Più alto = animazioni più lente", ["cycle.unit"] = "ms",
        },
        ["en"] = new()
        {
            ["paint.open"] = "Colour studio…", ["paint.title"] = "Colour studio", ["paint.anim"] = "Animation", ["paint.play"] = "Play", ["paint.pause"] = "Pause", ["paint.frame"] = "Picture {0} of {1}", ["paint.parts"] = "Parts", ["paint.auto"] = "Auto", ["part.shade"] = "Shade", ["part.light"] = "Highlight", ["part.outline"] = "Outline", ["presets.title"] = "Presets", ["preset.save"] = "+  Save current look as a preset", ["preset.apply"] = "Apply", ["preset.update"] = "Overwrite with the current look", ["preset.rename"] = "Rename…", ["preset.dup"] = "Duplicate", ["preset.del"] = "Delete", ["preset.del.ask"] = "Delete the preset \"{0}\"?", ["preset.name"] = "Preset name", ["preset.new"] = "My preset", ["preset.empty"] = "No saved presets yet: colour your cat and save the look.", ["preset.builtin"] = "Template", ["bp.grey"] = "Grey", ["bp.orange"] = "Orange tabby", ["bp.siamese"] = "Siamese", ["bp.black"] = "Black cat", ["bp.calico"] = "Calico", ["bp.candy"] = "Candy", ["an.sit"] = "Sitting", ["an.stand"] = "Standing", ["an.lie"] = "Lying", ["an.loaf"] = "Loaf", ["an.walk"] = "Walking", ["an.sleep"] = "Sleeping", ["an.eat"] = "Eating", ["an.meow"] = "Meowing", ["an.yawn"] = "Yawning", ["an.wash"] = "Washing", ["an.scratch"] = "Scratching", ["an.hiss"] = "Hissing", ["an.paw"] = "Paw attack", ["an.jump"] = "Jump", ["an.pose"] = "Pose", ["dir.right"] = "right", ["dir.left"] = "left", ["dir.up"] = "up", ["dir.down"] = "down", ["dir.dr"] = "down-right", ["dir.dl"] = "down-left", ["dir.ur"] = "up-right", ["dir.ul"] = "up-left", ["dir.front"] = "facing us", ["dir.back"] = "back to us", ["dir.curled"] = "curled up", ["dir.stretched"] = "stretched out", ["tool.brush"] = "Brush", ["tool.eraser"] = "Eraser", ["tool.pick"] = "Eyedropper", ["tool.part"] = "Part", ["paint.size"] = "Size", ["paint.undo"] = "Undo", ["paint.brushcolor"] = "Brush colour", ["paint.clearframe"] = "Clear this picture", ["paint.clearall"] = "Clear all painting", ["paint.clearall.ask"] = "Remove everything you painted by hand on {0}?", ["paint.hint"] = "Paint the cat pixel by pixel. Each picture of each animation is painted on its own.", ["paint.static"] = "Static poses", ["paint.animated"] = "Animations", ["paint.view"] = "View {0}", ["paint.redo"] = "Redo", ["paint.save"] = "Save", ["paint.saved"] = "Saved", ["paint.share"] = "Share", ["paint.leave.title"] = "Unsaved drawing", ["paint.leave.msg"] = "Your hand-painted drawing is not saved. If you leave now, it will be lost.", ["paint.leave.save"] = "Save and leave", ["paint.leave.discard"] = "Discard the drawing", ["paint.leave.stay"] = "Keep editing", ["share.copy"] = "Copy the share code", ["share.image"] = "Save as an image…", ["share.paste"] = "Import from the copied code", ["share.file"] = "Import from an image…", ["share.copied"] = "Code copied: paste it in Discord or anywhere you like.", ["share.copied.nodraw"] = "Code copied without the painting (too big for a message): share the image to include it.", ["share.saved"] = "Image saved: send it to anyone, it holds the whole look.", ["share.imported"] = "Look imported. Save to keep the drawing.", ["share.invalid"] = "That is not a Tam-a-Pet look.", ["share.failed"] = "Something went wrong.", ["share.card.painted"] = "Hand-painted", ["share.card.how"] = "Import it in Tam-a-Pet: Colour studio › Share › Import (or drop this picture on the window)", ["hetero.label"] = "Heterochromia", ["look.eyeL"] = "Left eye", ["look.eyeR"] = "Right eye",
            ["nav.pets"] = "Pets", ["pets.intro"] = "Choose the kind of pet to manage.", ["pets.cats"] = "Cats", ["pets.count"] = "{0} on the desktop", ["cats.add"] = "Add a cat", ["cats.max"] = "You have reached the limit of {0} cats.", ["back.pets"] = "‹ Pets", ["back.cats"] = "‹ Cats", ["cat.remove"] = "Remove this cat", ["cat.remove.ask"] = "Remove {0}? Its stats and objects will be lost.", ["pattern.title"] = "Variant", ["pattern.none"] = "None", ["pattern.tabby"] = "Tabby", ["pattern.patches"] = "Patched", ["pattern.calico"] = "Calico", ["look.fur3"] = "Variant colour", ["mixer.title"] = "Mixer", ["mixer.master"] = "Master volume", ["mixer.pet"] = "Cat volume", ["mix.voice"] = "Voice", ["mix.purr"] = "Purring", ["mix.fx"] = "Effects", ["cat.sound"] = "Volume", ["about"] = "About", ["upd.check"] = "Check for updates", ["autoupdate.label"] = "Check for updates automatically", ["autoupdate.hint"] = "Looks on GitHub at start and every hour; downloading and installing always needs your click", ["upd.balloon"] = "Open Settings to install it.", ["upd.checking"] = "Checking...", ["upd.latest"] = "You have the latest version ({0}).", ["upd.avail"] = "Version {0} is available.", ["upd.install"] = "Download and install", ["upd.installing"] = "Downloading the update...", ["upd.error"] = "Could not check for updates (no connection, or no published release).", ["version"] = "Version", ["dev.left"] = "{0} more clicks to enable developer mode", ["dev.on"] = "Developer mode enabled", ["dev.active"] = "Developer mode is on", ["nav.dev"] = "Developer", ["dev.intro"] = "Preview features and debug tools.", ["dev.empty"] = "Nothing here yet.", ["dev.disable"] = "Turn off developer mode",
            ["dev.actions"] = "Cat interactions", ["dev.state"] = "State", ["dev.stats.fmt"] = "Hunger {0} · Happiness {1} · Energy {2} · state: {3}", ["act.idle"] = "Idle", ["act.lick"] = "Lick", ["act.walk"] = "Walk", ["act.run"] = "Run", ["act.sleep"] = "Sleep / wake", ["act.play"] = "Play (click)", ["act.pounce"] = "Jump", ["act.frenzy"] = "Frenzy", ["act.groom"] = "Groom each other", ["act.fight"] = "Fight", ["nav.volumes"] = "Volumes", ["about.changelog"] = "Changelog", ["bowl.refill"] = "Refill the bowl", ["look.nose"] = "Nose", ["look.ears"] = "Ears", ["acc.title"] = "Accessories", ["acc.none"] = "None", ["look.fur"] = "Fur", ["classic.label"] = "Classic sprites", ["classic.hint"] = "Use the original cat sprites instead of the new ones (accessories only work with the new ones)", ["acc.bow-red"] = "Red bow", ["acc.bow-pink"] = "Pink bow", ["acc.bow-gold"] = "Gold bow", ["acc.bow2-blue"] = "Blue bow", ["acc.bow2-green"] = "Green bow", ["acc.bow2-pink"] = "Pink bow 2", ["acc.glasses-red"] = "Red glasses", ["acc.glasses-gold"] = "Gold glasses", ["acc.halo"] = "Halo", ["acc.wings"] = "Wings", ["acc.cupid"] = "Cupid", ["acc.santa-1"] = "Santa hat 1", ["acc.santa-2"] = "Santa hat 2", ["acc.antlers-red"] = "Red antlers", ["acc.antlers-green"] = "Green antlers", ["share.label"] = "Shared objects", ["share.hint"] = "All cats use the same bowl, bed and ball (those of the first cat). The objects of the other cats are removed.", ["objs.shared"] = "The objects are shared: manage them from the page of {0}.",
            ["menu.objects"] = "Objects", ["menu.bowl"] = "Bowl", ["menu.bed"] = "Bed", ["menu.ball"] = "Ball", ["menu.remove"] = "Remove", ["name.label"] = "Cat name", ["name.hint"] = "Shown above the stat bars",
            ["act.bowl"] = "Bowl on/off", ["act.bed"] = "Bed on/off", ["act.ball"] = "Ball on/off",
            ["act.hungry"] = "Make hungry", ["act.sad"] = "Make sad", ["act.tired"] = "Make tired",
            ["onTop.label"] = "Always on top", ["onTop.hint"] = "The cat stays above other windows", ["startup.label"] = "Start with Windows", ["startup.hint"] = "Opens the cat when you sign in", ["volume.label"] = "Volume", ["volume.hint"] = "Meows and other cat sounds (0 = mute)", ["volume.unit"] = "%", ["group.tuning"] = "Behaviour tuning", ["act.meow"] = "Meow",
            ["about.creditsBtn"] = "Credits", ["credits.title"] = "Credits", ["credits.sounds"] = "Sounds", ["credits.back"] = "Back", ["credits.purr"] = "Purring", ["credits.kitten"] = "Kitten meow (jump)", ["credits.munch"] = "Chewing",
            ["look.title"] = "Look", ["look.fur1"] = "Fur 1 (back and paws)", ["look.fur2"] = "Fur 2 (belly)", ["look.eyes"] = "Eyes", ["look.custom"] = "Pick a colour", ["look.reset"] = "Reset colours",
            ["nav.objects"] = "Objects", ["obj.bowlbody"] = "Bowl", ["obj.bowlfood"] = "Food", ["obj.bedouter"] = "Bed", ["obj.bedinner"] = "Cushion", ["obj.ballbody"] = "Ball", ["obj.ballring"] = "Outline",
            ["picker.title"] = "Pick a colour", ["picker.new"] = "New", ["picker.current"] = "Current", ["picker.recent"] = "Recently used", ["picker.ok"] = "Done", ["picker.cancel"] = "Cancel",
            ["act.pet"] = "Petting",
            ["cat.default"] = "Cat", ["cat.on.label"] = "Second cat", ["cat.on.hint"] = "A second cat, with its own stats, character and objects", ["char.title"] = "Character", ["char.balanced"] = "Balanced", ["char.playful"] = "Playful", ["char.lazy"] = "Lazy", ["char.greedy"] = "Greedy", ["char.chatty"] = "Chatty", ["char.needy"] = "Attention seeker", ["char.needy.hint"] = "Happiness drops much faster: calls you and follows the mouse.", ["char.balanced.hint"] = "Neither too lively nor too lazy.", ["char.playful.hint"] = "Runs more and gets bored soon, but eats little.", ["char.lazy.hint"] = "Hardly moves, but tires sooner; eats less and is easy to please.", ["char.greedy.hint"] = "Always hungry: empties the bowl in no time, but has plenty of energy.", ["char.chatty.hint"] = "Meows much more often and is a bit less patient.", ["objs.title"] = "Objects", ["obj.color"] = "Colour", ["dev.target"] = "Cat to control",
            ["stat.hunger"] = "Hunger", ["stat.happiness"] = "Happiness", ["stat.energy"] = "Energy",
            ["tray.settings"] = "Settings...", 
            ["tray.exit"] = "Exit",
            ["title"] = "Settings", ["nav.general"] = "General", ["nav.cat"] = "Cat",
            ["language"] = "Language", ["language.hint"] = "Interface language", ["monitor.label"] = "Monitor", ["monitor.hint"] = "Where this pet lives, with its objects", ["monitor.free"] = "Free (all)", ["monitor.n"] = "Monitor {0}", ["monitor.main"] = "(main)", ["reset"] = "Reset to defaults",
            
            ["group.mouse"] = "Mouse", ["group.movement"] = "Movement",
            ["nearRange.label"] = "\"Mouse nearby\" distance", ["nearRange.hint"] = "Within this distance the cat notices the mouse", ["nearRange.unit"] = "px",
            ["jumpRange.label"] = "Jump trigger range", ["jumpRange.hint"] = "Within this distance from the cat the mouse can make it jump", ["jumpRange.unit"] = "px",
            ["jumpSpeed.label"] = "Mouse speed to trigger a jump", ["jumpSpeed.hint"] = "Lower = jumps more easily", ["jumpSpeed.unit"] = "px/s",
            ["jumpCooldown.label"] = "Pause between jumps", ["jumpCooldown.hint"] = "", ["jumpCooldown.unit"] = "s",
            ["wander.label"] = "Urge to wander", ["wander.hint"] = "Chance per second of going for a walk (0 = stays still)", ["wander.unit"] = "% per second",
            ["walkSpeed.label"] = "Walking speed", ["walkSpeed.hint"] = "Running is twice as fast", ["walkSpeed.unit"] = "px",
            ["cycle.label"] = "Animation cycle length", ["cycle.hint"] = "Higher = slower animations", ["cycle.unit"] = "ms",
        },
        ["fr"] = new()
        {
            ["paint.open"] = "Studio des couleurs…", ["paint.title"] = "Studio des couleurs", ["paint.anim"] = "Animation", ["paint.play"] = "Lecture", ["paint.pause"] = "Pause", ["paint.frame"] = "Image {0} sur {1}", ["paint.parts"] = "Parties", ["paint.auto"] = "Auto", ["part.shade"] = "Ombre", ["part.light"] = "Lumière", ["part.outline"] = "Contour", ["presets.title"] = "Préréglages", ["preset.save"] = "+  Enregistrer l'apparence actuelle", ["preset.apply"] = "Appliquer", ["preset.update"] = "Remplacer par l'apparence actuelle", ["preset.rename"] = "Renommer…", ["preset.dup"] = "Dupliquer", ["preset.del"] = "Supprimer", ["preset.del.ask"] = "Supprimer le préréglage « {0} » ?", ["preset.name"] = "Nom du préréglage", ["preset.new"] = "Mon préréglage", ["preset.empty"] = "Aucun préréglage enregistré : colorez votre chat puis enregistrez.", ["preset.builtin"] = "Modèle", ["bp.grey"] = "Gris", ["bp.orange"] = "Tigré roux", ["bp.siamese"] = "Siamois", ["bp.black"] = "Chat noir", ["bp.calico"] = "Calico", ["bp.candy"] = "Bonbon", ["an.sit"] = "Assis", ["an.stand"] = "Debout", ["an.lie"] = "Couché", ["an.loaf"] = "En boule", ["an.walk"] = "Marche", ["an.sleep"] = "Dort", ["an.eat"] = "Mange", ["an.meow"] = "Miaule", ["an.yawn"] = "Bâille", ["an.wash"] = "Se lave", ["an.scratch"] = "Se gratte", ["an.hiss"] = "Siffle", ["an.paw"] = "Coup de patte", ["an.jump"] = "Saut", ["an.pose"] = "Pose", ["dir.right"] = "droite", ["dir.left"] = "gauche", ["dir.up"] = "haut", ["dir.down"] = "bas", ["dir.dr"] = "bas-droite", ["dir.dl"] = "bas-gauche", ["dir.ur"] = "haut-droite", ["dir.ul"] = "haut-gauche", ["dir.front"] = "de face", ["dir.back"] = "de dos", ["dir.curled"] = "roulé en boule", ["dir.stretched"] = "étendu", ["tool.brush"] = "Pinceau", ["tool.eraser"] = "Gomme", ["tool.pick"] = "Pipette", ["tool.part"] = "Partie", ["paint.size"] = "Taille", ["paint.undo"] = "Annuler", ["paint.brushcolor"] = "Couleur du pinceau", ["paint.clearframe"] = "Effacer cette image", ["paint.clearall"] = "Effacer tout le dessin", ["paint.clearall.ask"] = "Effacer tout ce que vous avez peint à la main sur {0} ?", ["paint.hint"] = "Peignez le chat pixel par pixel. Chaque image de chaque animation se peint séparément.", ["paint.static"] = "Poses statiques", ["paint.animated"] = "Animations", ["paint.view"] = "Vue {0}", ["paint.redo"] = "Rétablir", ["paint.save"] = "Enregistrer", ["paint.saved"] = "Enregistré", ["paint.share"] = "Partager", ["paint.leave.title"] = "Dessin non enregistré", ["paint.leave.msg"] = "Votre dessin à la main n'est pas enregistré. Si vous quittez maintenant, il sera perdu.", ["paint.leave.save"] = "Enregistrer et quitter", ["paint.leave.discard"] = "Abandonner le dessin", ["paint.leave.stay"] = "Continuer à modifier", ["share.copy"] = "Copier le code de partage", ["share.image"] = "Enregistrer comme image…", ["share.paste"] = "Importer depuis le code copié", ["share.file"] = "Importer depuis une image…", ["share.copied"] = "Code copié : collez-le sur Discord ou ailleurs.", ["share.copied.nodraw"] = "Code copié sans le dessin (trop gros pour un message) : partagez l'image pour l'inclure.", ["share.saved"] = "Image enregistrée : envoyez-la à qui vous voulez, elle contient toute l'apparence.", ["share.imported"] = "Apparence importée. Enregistrez pour garder le dessin.", ["share.invalid"] = "Ceci n'est pas une apparence Tam-a-Pet.", ["share.failed"] = "Une erreur est survenue.", ["share.card.painted"] = "Peint à la main", ["share.card.how"] = "Importez-la dans Tam-a-Pet : Studio des couleurs › Partager › Importer (ou déposez cette image sur la fenêtre)", ["hetero.label"] = "Hétérochromie", ["look.eyeL"] = "Œil gauche", ["look.eyeR"] = "Œil droit",
            ["nav.pets"] = "Animaux", ["pets.intro"] = "Choisissez le type d'animal à gérer.", ["pets.cats"] = "Chats", ["pets.count"] = "{0} sur le bureau", ["cats.add"] = "Ajouter un chat", ["cats.max"] = "Vous avez atteint la limite de {0} chats.",
            ["back.pets"] = "‹ Animaux", ["back.cats"] = "‹ Chats", ["cat.remove"] = "Supprimer ce chat", ["cat.remove.ask"] = "Supprimer {0} ? Ses statistiques et ses objets seront perdus.", ["pattern.title"] = "Variante", ["pattern.none"] = "Aucune",
            ["pattern.tabby"] = "Tigré", ["pattern.patches"] = "Tacheté", ["pattern.calico"] = "Calico", ["look.fur3"] = "Couleur de la variante", ["mixer.title"] = "Mixeur", ["mixer.master"] = "Volume général",
            ["mixer.pet"] = "Volume du chat", ["mix.voice"] = "Voix", ["mix.purr"] = "Ronronnement", ["mix.fx"] = "Effets", ["cat.sound"] = "Volume", ["about"] = "À propos",
            ["upd.check"] = "Rechercher des mises à jour", ["autoupdate.label"] = "Rechercher les mises à jour automatiquement", ["autoupdate.hint"] = "Vérifie sur GitHub au démarrage puis toutes les heures ; le téléchargement et l'installation demandent toujours votre clic", ["upd.balloon"] = "Ouvrez les Paramètres pour l'installer.", ["upd.checking"] = "Vérification...", ["upd.latest"] = "Vous avez la dernière version ({0}).",
            ["upd.avail"] = "La version {0} est disponible.", ["upd.install"] = "Télécharger et installer", ["upd.installing"] = "Téléchargement de la mise à jour...", ["upd.error"] = "Impossible de vérifier les mises à jour (pas de connexion, ou aucune version publiée).", ["version"] = "Version", ["dev.left"] = "Encore {0} clics pour activer le mode développeur",
            ["dev.on"] = "Mode développeur activé", ["dev.active"] = "Le mode développeur est activé", ["nav.dev"] = "Développeur", ["dev.intro"] = "Fonctions en avant-première et outils de débogage.", ["dev.empty"] = "Rien ici pour le moment.", ["dev.disable"] = "Désactiver le mode développeur",
            ["dev.actions"] = "Interactions du chat", ["dev.state"] = "État", ["dev.stats.fmt"] = "Faim {0} · Bonheur {1} · Énergie {2} · état : {3}", ["act.idle"] = "Repos", ["act.lick"] = "Léchage", ["act.walk"] = "Marche",
            ["act.run"] = "Course", ["act.sleep"] = "Dormir / réveiller", ["act.play"] = "Jouer (clic)", ["act.pounce"] = "Saut", ["act.frenzy"] = "Colère", ["act.groom"] = "Se toiletter mutuellement",
            ["act.fight"] = "Se battre", ["nav.volumes"] = "Volumes", ["about.changelog"] = "Journal des modifications", ["bowl.refill"] = "Remplir la gamelle", ["look.nose"] = "Nez", ["look.ears"] = "Oreilles", ["acc.title"] = "Accessoires", ["acc.none"] = "Aucun", ["look.fur"] = "Pelage", ["classic.label"] = "Sprites classiques", ["classic.hint"] = "Utilise les sprites d'origine des chats au lieu des nouveaux (les accessoires ne fonctionnent qu'avec les nouveaux)", ["acc.bow-red"] = "Nœud rouge", ["acc.bow-pink"] = "Nœud rose", ["acc.bow-gold"] = "Nœud doré", ["acc.bow2-blue"] = "Nœud bleu", ["acc.bow2-green"] = "Nœud vert", ["acc.bow2-pink"] = "Nœud rose 2", ["acc.glasses-red"] = "Lunettes rouges", ["acc.glasses-gold"] = "Lunettes dorées", ["acc.halo"] = "Auréole", ["acc.wings"] = "Ailes", ["acc.cupid"] = "Cupidon", ["acc.santa-1"] = "Bonnet 1", ["acc.santa-2"] = "Bonnet 2", ["acc.antlers-red"] = "Renne rouge", ["acc.antlers-green"] = "Renne verte", ["share.label"] = "Objets partagés", ["share.hint"] = "Tous les chats utilisent la même gamelle, le même lit et la même balle (ceux du premier chat). Les objets des autres chats sont supprimés.", ["objs.shared"] = "Les objets sont partagés : gérez-les depuis la page de {0}.", ["menu.objects"] = "Objets", ["menu.bowl"] = "Gamelle",
            ["menu.bed"] = "Lit", ["menu.ball"] = "Balle", ["menu.remove"] = "Retirer", ["name.label"] = "Nom du chat", ["name.hint"] = "Affiché au-dessus des barres de statistiques", ["act.bowl"] = "Gamelle oui/non",
            ["act.bed"] = "Lit oui/non", ["act.ball"] = "Balle oui/non", ["act.hungry"] = "Donner faim", ["act.sad"] = "Rendre triste", ["act.tired"] = "Fatiguer", ["onTop.label"] = "Toujours au premier plan",
            ["onTop.hint"] = "Le chat reste au-dessus des autres fenêtres", ["startup.label"] = "Lancer avec Windows", ["startup.hint"] = "Ouvre le chat à l'ouverture de session", ["volume.label"] = "Volume", ["volume.hint"] = "Miaulements et autres sons du chat (0 = muet)", ["volume.unit"] = "%",
            ["group.tuning"] = "Réglage du comportement", ["act.meow"] = "Miauler", ["about.creditsBtn"] = "Crédits", ["credits.title"] = "Crédits", ["credits.sounds"] = "Sons", ["credits.back"] = "Retour",
            ["credits.purr"] = "Ronronnement", ["credits.kitten"] = "Miaulement de chaton (saut)", ["credits.munch"] = "Mastication", ["look.title"] = "Apparence", ["look.fur1"] = "Pelage 1 (dos et pattes)", ["look.fur2"] = "Pelage 2 (ventre)",
            ["look.eyes"] = "Yeux", ["look.custom"] = "Choisir une couleur", ["look.reset"] = "Réinitialiser les couleurs", ["nav.objects"] = "Objets", ["obj.bowlbody"] = "Gamelle", ["obj.bowlfood"] = "Nourriture",
            ["obj.bedouter"] = "Lit", ["obj.bedinner"] = "Coussin", ["obj.ballbody"] = "Balle", ["obj.ballring"] = "Contour", ["picker.title"] = "Choisir une couleur", ["picker.new"] = "Nouvelle",
            ["picker.current"] = "Actuelle", ["picker.recent"] = "Utilisées récemment", ["picker.ok"] = "Terminé", ["picker.cancel"] = "Annuler", ["act.pet"] = "Caresser", ["cat.default"] = "Chat",
            ["cat.on.label"] = "Deuxième chat", ["cat.on.hint"] = "Un deuxième chat, avec ses propres statistiques, son caractère et ses objets", ["char.title"] = "Caractère", ["char.balanced"] = "Équilibré", ["char.playful"] = "Joueur", ["char.lazy"] = "Paresseux",
            ["char.greedy"] = "Gourmand", ["char.chatty"] = "Bavard", ["char.needy"] = "En manque d'attention", ["char.needy.hint"] = "Le bonheur baisse beaucoup plus vite : il vous appelle et suit la souris.", ["char.balanced.hint"] = "Ni trop vif, ni trop paresseux.", ["char.playful.hint"] = "Court davantage et s'ennuie vite, mais mange peu.",
            ["char.lazy.hint"] = "Bouge peu, mais se fatigue plus vite ; mange moins et se contente de peu.", ["char.greedy.hint"] = "Toujours affamé : vide la gamelle en un rien de temps, mais plein d'énergie.", ["char.chatty.hint"] = "Miaule beaucoup plus souvent et a un peu moins de patience.", ["objs.title"] = "Objets", ["obj.color"] = "Couleur", ["dev.target"] = "Chat à contrôler",
            ["stat.hunger"] = "Faim", ["stat.happiness"] = "Bonheur", ["stat.energy"] = "Énergie", ["tray.settings"] = "Paramètres...", ["tray.exit"] = "Quitter", ["title"] = "Paramètres",
            ["nav.general"] = "Général", ["nav.cat"] = "Chat", ["language"] = "Langue", ["language.hint"] = "Langue de l'interface", ["monitor.label"] = "Écran", ["monitor.hint"] = "Où vit cet animal, avec ses objets",
            ["monitor.free"] = "Libre (tous)", ["monitor.n"] = "Écran {0}", ["monitor.main"] = "(principal)", ["reset"] = "Rétablir les valeurs par défaut", ["group.mouse"] = "Souris", ["group.movement"] = "Mouvement",
            ["nearRange.label"] = "Distance « souris proche »", ["nearRange.hint"] = "À cette distance, le chat remarque la souris", ["nearRange.unit"] = "px", ["jumpRange.label"] = "Portée du déclenchement du saut", ["jumpRange.hint"] = "À cette distance du chat, la souris peut le faire sauter", ["jumpRange.unit"] = "px",
            ["jumpSpeed.label"] = "Vitesse de la souris pour déclencher un saut", ["jumpSpeed.hint"] = "Plus bas = saute plus facilement", ["jumpSpeed.unit"] = "px/s", ["jumpCooldown.label"] = "Pause entre les sauts", ["jumpCooldown.hint"] = "", ["jumpCooldown.unit"] = "s",
            ["wander.label"] = "Envie de se promener", ["wander.hint"] = "Probabilité par seconde de partir se promener (0 = reste immobile)", ["wander.unit"] = "% par seconde", ["walkSpeed.label"] = "Vitesse de marche", ["walkSpeed.hint"] = "La course est deux fois plus rapide", ["walkSpeed.unit"] = "px",
            ["cycle.label"] = "Durée du cycle des animations", ["cycle.hint"] = "Plus élevé = animations plus lentes", ["cycle.unit"] = "ms",
        },
        ["es"] = new()
        {
            ["paint.open"] = "Estudio de color…", ["paint.title"] = "Estudio de color", ["paint.anim"] = "Animación", ["paint.play"] = "Reproducir", ["paint.pause"] = "Pausa", ["paint.frame"] = "Imagen {0} de {1}", ["paint.parts"] = "Partes", ["paint.auto"] = "Auto", ["part.shade"] = "Sombra", ["part.light"] = "Luz", ["part.outline"] = "Contorno", ["presets.title"] = "Preajustes", ["preset.save"] = "+  Guardar el aspecto actual", ["preset.apply"] = "Aplicar", ["preset.update"] = "Sobrescribir con el aspecto actual", ["preset.rename"] = "Renombrar…", ["preset.dup"] = "Duplicar", ["preset.del"] = "Eliminar", ["preset.del.ask"] = "¿Eliminar el preajuste \"{0}\"?", ["preset.name"] = "Nombre del preajuste", ["preset.new"] = "Mi preajuste", ["preset.empty"] = "Aún no hay preajustes: colorea el gato y guarda el aspecto.", ["preset.builtin"] = "Plantilla", ["bp.grey"] = "Gris", ["bp.orange"] = "Atigrado naranja", ["bp.siamese"] = "Siamés", ["bp.black"] = "Gato negro", ["bp.calico"] = "Calicó", ["bp.candy"] = "Caramelo", ["an.sit"] = "Sentado", ["an.stand"] = "De pie", ["an.lie"] = "Tumbado", ["an.loaf"] = "En bolita", ["an.walk"] = "Camina", ["an.sleep"] = "Duerme", ["an.eat"] = "Come", ["an.meow"] = "Maúlla", ["an.yawn"] = "Bosteza", ["an.wash"] = "Se lava", ["an.scratch"] = "Se rasca", ["an.hiss"] = "Bufa", ["an.paw"] = "Zarpazo", ["an.jump"] = "Salto", ["an.pose"] = "Postura", ["dir.right"] = "derecha", ["dir.left"] = "izquierda", ["dir.up"] = "arriba", ["dir.down"] = "abajo", ["dir.dr"] = "abajo-derecha", ["dir.dl"] = "abajo-izquierda", ["dir.ur"] = "arriba-derecha", ["dir.ul"] = "arriba-izquierda", ["dir.front"] = "de frente", ["dir.back"] = "de espaldas", ["dir.curled"] = "hecho un ovillo", ["dir.stretched"] = "estirado", ["tool.brush"] = "Pincel", ["tool.eraser"] = "Goma", ["tool.pick"] = "Cuentagotas", ["tool.part"] = "Parte", ["paint.size"] = "Tamaño", ["paint.undo"] = "Deshacer", ["paint.brushcolor"] = "Color del pincel", ["paint.clearframe"] = "Borrar esta imagen", ["paint.clearall"] = "Borrar todo el dibujo", ["paint.clearall.ask"] = "¿Borrar todo lo que has pintado a mano en {0}?", ["paint.hint"] = "Pinta el gato píxel a píxel. Cada imagen de cada animación se pinta por separado.", ["paint.static"] = "Poses estáticas", ["paint.animated"] = "Animaciones", ["paint.view"] = "Vista {0}", ["paint.redo"] = "Rehacer", ["paint.save"] = "Guardar", ["paint.saved"] = "Guardado", ["paint.share"] = "Compartir", ["paint.leave.title"] = "Dibujo sin guardar", ["paint.leave.msg"] = "Tu dibujo a mano no está guardado. Si sales ahora, se perderá.", ["paint.leave.save"] = "Guardar y salir", ["paint.leave.discard"] = "Descartar el dibujo", ["paint.leave.stay"] = "Seguir editando", ["share.copy"] = "Copiar el código para compartir", ["share.image"] = "Guardar como imagen…", ["share.paste"] = "Importar desde el código copiado", ["share.file"] = "Importar desde una imagen…", ["share.copied"] = "Código copiado: pégalo en Discord o donde quieras.", ["share.copied.nodraw"] = "Código copiado sin el dibujo (demasiado grande para un mensaje): comparte la imagen para incluirlo.", ["share.saved"] = "Imagen guardada: envíala a quien quieras, contiene todo el aspecto.", ["share.imported"] = "Aspecto importado. Guarda para conservar el dibujo.", ["share.invalid"] = "Eso no es un aspecto de Tam-a-Pet.", ["share.failed"] = "Algo ha salido mal.", ["share.card.painted"] = "Pintado a mano", ["share.card.how"] = "Impórtalo en Tam-a-Pet: Estudio de color › Compartir › Importar (o suelta esta imagen en la ventana)", ["hetero.label"] = "Heterocromía", ["look.eyeL"] = "Ojo izquierdo", ["look.eyeR"] = "Ojo derecho",
            ["nav.pets"] = "Mascotas", ["pets.intro"] = "Elige el tipo de mascota que quieres gestionar.", ["pets.cats"] = "Gatos", ["pets.count"] = "{0} en el escritorio", ["cats.add"] = "Añadir un gato", ["cats.max"] = "Has alcanzado el límite de {0} gatos.",
            ["back.pets"] = "‹ Mascotas", ["back.cats"] = "‹ Gatos", ["cat.remove"] = "Eliminar este gato", ["cat.remove.ask"] = "¿Eliminar a {0}? Se perderán sus estadísticas y sus objetos.", ["pattern.title"] = "Variante", ["pattern.none"] = "Ninguna",
            ["pattern.tabby"] = "Atigrado", ["pattern.patches"] = "Con manchas", ["pattern.calico"] = "Calicó", ["look.fur3"] = "Color de la variante", ["mixer.title"] = "Mezclador", ["mixer.master"] = "Volumen general",
            ["mixer.pet"] = "Volumen del gato", ["mix.voice"] = "Voz", ["mix.purr"] = "Ronroneo", ["mix.fx"] = "Efectos", ["cat.sound"] = "Volumen", ["about"] = "Acerca de",
            ["upd.check"] = "Buscar actualizaciones", ["autoupdate.label"] = "Buscar actualizaciones automáticamente", ["autoupdate.hint"] = "Consulta GitHub al iniciar y cada hora; descargar e instalar siempre requiere tu clic", ["upd.balloon"] = "Abre Ajustes para instalarla.", ["upd.checking"] = "Comprobando...", ["upd.latest"] = "Tienes la última versión ({0}).",
            ["upd.avail"] = "Hay una nueva versión disponible: {0}.", ["upd.install"] = "Descargar e instalar", ["upd.installing"] = "Descargando la actualización...", ["upd.error"] = "No se pudieron buscar actualizaciones (sin conexión, o sin versión publicada).", ["version"] = "Versión", ["dev.left"] = "Faltan {0} clics para activar el modo desarrollador",
            ["dev.on"] = "Modo desarrollador activado", ["dev.active"] = "El modo desarrollador está activado", ["nav.dev"] = "Desarrollador", ["dev.intro"] = "Funciones en vista previa y herramientas de depuración.", ["dev.empty"] = "Aún no hay nada aquí.", ["dev.disable"] = "Desactivar el modo desarrollador",
            ["dev.actions"] = "Interacciones del gato", ["dev.state"] = "Estado", ["dev.stats.fmt"] = "Hambre {0} · Felicidad {1} · Energía {2} · estado: {3}", ["act.idle"] = "Reposo", ["act.lick"] = "Lamer", ["act.walk"] = "Caminar",
            ["act.run"] = "Correr", ["act.sleep"] = "Dormir / despertar", ["act.play"] = "Jugar (clic)", ["act.pounce"] = "Salto", ["act.frenzy"] = "Furia", ["act.groom"] = "Acicalarse mutuamente",
            ["act.fight"] = "Pelear", ["nav.volumes"] = "Volúmenes", ["about.changelog"] = "Registro de cambios", ["bowl.refill"] = "Rellenar el cuenco", ["look.nose"] = "Nariz", ["look.ears"] = "Orejas", ["acc.title"] = "Accesorios", ["acc.none"] = "Ninguno", ["look.fur"] = "Pelaje", ["classic.label"] = "Sprites clásicos", ["classic.hint"] = "Usa los sprites originales de los gatos en lugar de los nuevos (los accesorios solo funcionan con los nuevos)", ["acc.bow-red"] = "Lazo rojo", ["acc.bow-pink"] = "Lazo rosa", ["acc.bow-gold"] = "Lazo dorado", ["acc.bow2-blue"] = "Lazo azul", ["acc.bow2-green"] = "Lazo verde", ["acc.bow2-pink"] = "Lazo rosa 2", ["acc.glasses-red"] = "Gafas rojas", ["acc.glasses-gold"] = "Gafas doradas", ["acc.halo"] = "Aureola", ["acc.wings"] = "Alas", ["acc.cupid"] = "Cupido", ["acc.santa-1"] = "Gorro 1", ["acc.santa-2"] = "Gorro 2", ["acc.antlers-red"] = "Reno rojo", ["acc.antlers-green"] = "Reno verde", ["share.label"] = "Objetos compartidos", ["share.hint"] = "Todos los gatos usan el mismo cuenco, la misma cama y la misma pelota (los del primer gato). Los objetos de los demás gatos se eliminan.", ["objs.shared"] = "Los objetos son compartidos: gestiónalos desde la página de {0}.", ["menu.objects"] = "Objetos", ["menu.bowl"] = "Cuenco",
            ["menu.bed"] = "Cama", ["menu.ball"] = "Pelota", ["menu.remove"] = "Quitar", ["name.label"] = "Nombre del gato", ["name.hint"] = "Se muestra sobre las barras de estadísticas", ["act.bowl"] = "Cuenco sí/no",
            ["act.bed"] = "Cama sí/no", ["act.ball"] = "Pelota sí/no", ["act.hungry"] = "Dar hambre", ["act.sad"] = "Poner triste", ["act.tired"] = "Cansar", ["onTop.label"] = "Siempre en primer plano",
            ["onTop.hint"] = "El gato se queda por encima de las demás ventanas", ["startup.label"] = "Iniciar con Windows", ["startup.hint"] = "Abre el gato al iniciar sesión", ["volume.label"] = "Volumen", ["volume.hint"] = "Maullidos y otros sonidos del gato (0 = silencio)", ["volume.unit"] = "%",
            ["group.tuning"] = "Ajuste del comportamiento", ["act.meow"] = "Maullar", ["about.creditsBtn"] = "Créditos", ["credits.title"] = "Créditos", ["credits.sounds"] = "Sonidos", ["credits.back"] = "Volver",
            ["credits.purr"] = "Ronroneo", ["credits.kitten"] = "Maullido de gatito (salto)", ["credits.munch"] = "Masticar", ["look.title"] = "Aspecto", ["look.fur1"] = "Pelaje 1 (lomo y patas)", ["look.fur2"] = "Pelaje 2 (barriga)",
            ["look.eyes"] = "Ojos", ["look.custom"] = "Elegir un color", ["look.reset"] = "Restablecer colores", ["nav.objects"] = "Objetos", ["obj.bowlbody"] = "Cuenco", ["obj.bowlfood"] = "Comida",
            ["obj.bedouter"] = "Cama", ["obj.bedinner"] = "Cojín", ["obj.ballbody"] = "Pelota", ["obj.ballring"] = "Contorno", ["picker.title"] = "Elegir un color", ["picker.new"] = "Nuevo",
            ["picker.current"] = "Actual", ["picker.recent"] = "Usados recientemente", ["picker.ok"] = "Hecho", ["picker.cancel"] = "Cancelar", ["act.pet"] = "Acariciar", ["cat.default"] = "Gato",
            ["cat.on.label"] = "Segundo gato", ["cat.on.hint"] = "Un segundo gato, con sus propias estadísticas, carácter y objetos", ["char.title"] = "Carácter", ["char.balanced"] = "Equilibrado", ["char.playful"] = "Juguetón", ["char.lazy"] = "Perezoso",
            ["char.greedy"] = "Glotón", ["char.chatty"] = "Hablador", ["char.needy"] = "Busca atención", ["char.needy.hint"] = "La felicidad baja mucho más rápido: te llama y sigue el ratón.", ["char.balanced.hint"] = "Ni demasiado vivaz ni demasiado perezoso.", ["char.playful.hint"] = "Corre más y se aburre pronto, pero come poco.",
            ["char.lazy.hint"] = "Apenas se mueve, pero se cansa antes; come menos y se conforma con poco.", ["char.greedy.hint"] = "Siempre tiene hambre: vacía el cuenco enseguida, pero tiene mucha energía.", ["char.chatty.hint"] = "Maúlla mucho más a menudo y tiene algo menos de paciencia.", ["objs.title"] = "Objetos", ["obj.color"] = "Color", ["dev.target"] = "Gato a controlar",
            ["stat.hunger"] = "Hambre", ["stat.happiness"] = "Felicidad", ["stat.energy"] = "Energía", ["tray.settings"] = "Ajustes...", ["tray.exit"] = "Salir", ["title"] = "Ajustes",
            ["nav.general"] = "General", ["nav.cat"] = "Gato", ["language"] = "Idioma", ["language.hint"] = "Idioma de la interfaz", ["monitor.label"] = "Monitor", ["monitor.hint"] = "Dónde vive esta mascota, con sus objetos",
            ["monitor.free"] = "Libre (todos)", ["monitor.n"] = "Monitor {0}", ["monitor.main"] = "(principal)", ["reset"] = "Restablecer valores predeterminados", ["group.mouse"] = "Ratón", ["group.movement"] = "Movimiento",
            ["nearRange.label"] = "Distancia «ratón cerca»", ["nearRange.hint"] = "Dentro de esta distancia el gato nota el ratón", ["nearRange.unit"] = "px", ["jumpRange.label"] = "Alcance para activar el salto", ["jumpRange.hint"] = "Dentro de esta distancia del gato, el ratón puede hacerlo saltar", ["jumpRange.unit"] = "px",
            ["jumpSpeed.label"] = "Velocidad del ratón para activar un salto", ["jumpSpeed.hint"] = "Más bajo = salta con más facilidad", ["jumpSpeed.unit"] = "px/s", ["jumpCooldown.label"] = "Pausa entre saltos", ["jumpCooldown.hint"] = "", ["jumpCooldown.unit"] = "s",
            ["wander.label"] = "Ganas de pasear", ["wander.hint"] = "Probabilidad por segundo de ir a dar un paseo (0 = se queda quieto)", ["wander.unit"] = "% por segundo", ["walkSpeed.label"] = "Velocidad al caminar", ["walkSpeed.hint"] = "Correr es el doble de rápido", ["walkSpeed.unit"] = "px",
            ["cycle.label"] = "Duración del ciclo de animaciones", ["cycle.hint"] = "Más alto = animaciones más lentas", ["cycle.unit"] = "ms",
        }
    };

    public static string T(string key) => All.GetValueOrDefault(Cfg.Lang, All["en"]).GetValueOrDefault(key) ?? All["en"].GetValueOrDefault(key, key);   // a missing translation falls back to English
}

// "Start with Windows": a value in the per-user Run key (no admin rights needed)
static class Startup
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool Enabled
    {
        get { using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey); return k?.GetValue("TamAPet") != null; }
    }

    public static void Set(bool on)
    {
        using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, true);
        if (k == null) return;
        if (on) k.SetValue("TamAPet", "\"" + System.Windows.Forms.Application.ExecutablePath + "\"");
        else k.DeleteValue("TamAPet", false);
    }
}
