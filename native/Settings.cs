using System.Drawing;
using System.Globalization;

// A cat's character: multipliers on how it behaves. Each cat has its own.
sealed record Trait(string Key, double Activity, double Appetite, double Laziness, double Mood, double Pitch, double Chatty, double Needy = 0);

static class Characters
{
    // Multipliers on how fast a need drops (above 1 = faster), on top of the base times: hunger 10 min, happiness 5 min,
    // energy 15 min (see PetWindow). "appetite" = hunger, "laziness" = energy, "mood" = happiness.
    //                                   activity appetite laziness mood pitch chatty
    public static readonly Trait[] All =
    {
        new("balanced", 1.00, 1.00, 1.00, 1.00, 1.00, 1.0),
        new("playful",  1.35, 1.15, 1.20, 1.30, 1.10, 1.2),   // livelier: burns food and energy faster, gets bored (sad) sooner
        new("lazy",     0.55, 0.90, 1.35, 0.80, 0.92, 0.7),   // moves little but tires sooner; eats less and is easily content
        new("greedy",   0.90, 1.60, 1.00, 1.00, 0.96, 1.0),   // always hungry
        new("chatty",   1.00, 1.00, 1.00, 1.15, 1.05, 2.5),   // meows a lot and likes company: a bit less patient
        new("needy",    1.00, 1.00, 1.00, 1.60, 1.08, 1.8, 1),   // wants attention: loses happiness faster, calls you and comes to the mouse
    };

    public static Trait Get(string key) => All.FirstOrDefault(t => t.Key == key) ?? All[0];
}

// Everything that belongs to one cat: name, look, character, needs, its objects (colour + saved position).
sealed class CatProfile
{
    public readonly int Index;
    public string Name = "";
    public Color Fur1, Fur2, Eyes;
    public Color Fur3 = Color.FromArgb(0xE8, 0x91, 0x3C);   // the colour of the variant (stripes, patches)
    public string Pattern = "none";   // none | tabby | patches | calico
    public int Volume = 100, VoiceVol = 100, PurrVol = 100, FxVol = 100;   // this pet in the mixer: all of it, then its voice, purring and effects
    public Color BowlColor = Color.FromArgb(0xE0, 0x52, 0x5A), BedColor = Color.FromArgb(0x7F, 0x95, 0xF0), BallColor = Color.FromArgb(0xFF, 0x7A, 0x45);
    public string Character;
    public double Hunger = 100, Happiness = 100, Energy = 100;
    public string Monitor = "";   // where the pet (and its objects) live: "" = the main monitor, "free" = every monitor, or a monitor's device name
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
        Pattern = "none";
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
    public static bool AutoUpdate = true;   // look for a newer release at start (installing it is always manual)
    public const int MaxCats = 8;
    public static readonly List<CatProfile> Cats = new() { new(0) };   // every cat the user has (each with its own window); the first one has Index 0
    public static CatProfile? ById(int index) => Cats.FirstOrDefault(c => c.Index == index);
    public static readonly List<Color> Recent = new();   // colours picked with the colour picker, newest first (kept between runs)
    public static bool ShareObjects;   // all cats use the same bowl, bed and ball (those of the first cat)
    public static bool OnTop = true;   // keep the cats above other windows
    public static bool Dev;   // developer mode, unlocked from the version label in settings

    // TAMAPET_DIR overrides the folder (used by tests so they never touch the real settings)
    static string FilePath => Path.Combine(Environment.GetEnvironmentVariable("TAMAPET_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Tam-a-Pet"), "settings.ini");

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
            case "fur3": if (Hex(val, out var c4)) p.Fur3 = c4; break;
            case "vol": case "volvoice": case "volpurr": case "volfx":
                if (int.TryParse(val, out var vv)) { vv = Math.Clamp(vv, 0, 100); if (key == "vol") p.Volume = vv; else if (key == "volvoice") p.VoiceVol = vv; else if (key == "volpurr") p.PurrVol = vv; else p.FxVol = vv; }
                break;
            case "char": if (Characters.All.Any(t => t.Key == val)) p.Character = val; break;
            case "fur1": if (Hex(val, out var c1)) p.Fur1 = c1; break;
            case "fur2": if (Hex(val, out var c2)) p.Fur2 = c2; break;
            case "eyes": if (Hex(val, out var c3)) p.Eyes = c3; break;
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
                lines.Add($"{k}vol={p.Volume}");
                lines.Add($"{k}volvoice={p.VoiceVol}");
                lines.Add($"{k}volpurr={p.PurrVol}");
                lines.Add($"{k}volfx={p.FxVol}");
                lines.Add($"{k}eyes={CatSprite.Hex(p.Eyes)}");
                lines.Add($"{k}bowlc={CatSprite.Hex(p.BowlColor)}");
                lines.Add($"{k}bedc={CatSprite.Hex(p.BedColor)}");
                lines.Add($"{k}ballc={CatSprite.Hex(p.BallColor)}");
                lines.Add($"{k}hunger={D(p.Hunger)}");
                lines.Add($"{k}happiness={D(p.Happiness)}");
                lines.Add($"{k}energy={D(p.Energy)}");
                if (p.Monitor != "") lines.Add($"{k}monitor={p.Monitor}");
                if (p.Bowl != "") lines.Add($"{k}bowl={p.Bowl}");
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
            ["nav.pets"] = "Pet", ["pets.intro"] = "Scegli il tipo di pet da gestire.", ["pets.cats"] = "Gatti", ["pets.count"] = "{0} nel desktop", ["cats.add"] = "Aggiungi gatto", ["cats.max"] = "Hai raggiunto il massimo di {0} gatti.", ["back.pets"] = "‹ Pet", ["back.cats"] = "‹ Gatti", ["cat.remove"] = "Rimuovi questo gatto", ["cat.remove.ask"] = "Rimuovere {0}? Le sue statistiche e i suoi oggetti andranno persi.", ["pattern.title"] = "Variante", ["pattern.none"] = "Nessuna", ["pattern.tabby"] = "Tigrato", ["pattern.patches"] = "A chiazze", ["pattern.calico"] = "Calico", ["look.fur3"] = "Colore della variante", ["mixer.title"] = "Mixer", ["mixer.master"] = "Volume generale", ["mixer.pet"] = "Volume del gatto", ["mix.voice"] = "Voce", ["mix.purr"] = "Fusa", ["mix.fx"] = "Effetti", ["cat.sound"] = "Volume", ["about"] = "Informazioni", ["upd.check"] = "Cerca aggiornamenti", ["autoupdate.label"] = "Cerca aggiornamenti all'avvio", ["autoupdate.hint"] = "Controlla su GitHub; scaricare e installare richiede sempre un tuo clic", ["upd.balloon"] = "Apri le Impostazioni per installarla.", ["upd.checking"] = "Controllo in corso...", ["upd.latest"] = "Hai l'ultima versione ({0}).", ["upd.avail"] = "Disponibile la versione {0}.", ["upd.install"] = "Scarica e installa", ["upd.installing"] = "Scarico l'aggiornamento...", ["upd.error"] = "Impossibile controllare gli aggiornamenti (nessuna connessione, o nessuna versione pubblicata).", ["version"] = "Versione", ["dev.left"] = "Ancora {0} click per la modalità sviluppatore", ["dev.on"] = "Modalità sviluppatore attivata", ["dev.active"] = "Modalità sviluppatore attiva", ["nav.dev"] = "Sviluppatore", ["dev.intro"] = "Funzioni in anteprima e strumenti di debug.", ["dev.empty"] = "Per ora non c'è niente qui.", ["dev.disable"] = "Disattiva modalità sviluppatore",
            ["dev.actions"] = "Interazioni del gatto", ["dev.state"] = "Stato", ["dev.stats.fmt"] = "Fame {0} · Felicità {1} · Energia {2} · stato: {3}", ["act.idle"] = "Idle", ["act.lick"] = "Leccata", ["act.walk"] = "Cammina", ["act.run"] = "Corri", ["act.sleep"] = "Dormi / sveglia", ["act.play"] = "Gioca (click)", ["act.pounce"] = "Salto", ["act.frenzy"] = "Frenesia", ["act.groom"] = "Si leccano", ["act.fight"] = "Litigano", ["share.label"] = "Oggetti condivisi", ["share.hint"] = "Tutti i gatti usano la stessa ciotola, cuccia e pallina (quelli del primo gatto). Gli oggetti degli altri gatti vengono rimossi.", ["objs.shared"] = "Gli oggetti sono condivisi: si gestiscono dalla scheda di {0}.",
            ["menu.objects"] = "Oggetti", ["menu.bowl"] = "Ciotola", ["menu.bed"] = "Cuccia", ["menu.ball"] = "Pallina", ["menu.remove"] = "Rimuovi", ["name.label"] = "Nome del gatto", ["name.hint"] = "Compare sopra le barre delle statistiche",
            ["act.bowl"] = "Ciotola on/off", ["act.bed"] = "Cuccia on/off", ["act.ball"] = "Pallina on/off",
            ["act.hungry"] = "Ha fame", ["act.sad"] = "È triste", ["act.tired"] = "Ha sonno",
            ["onTop.label"] = "Sempre in primo piano", ["onTop.hint"] = "Il gatto resta sopra le altre finestre", ["startup.label"] = "Avvia con Windows", ["startup.hint"] = "Apre il gatto quando accedi al PC", ["volume.label"] = "Volume", ["volume.hint"] = "Miagolii e suoni del gatto (0 = muto)", ["volume.unit"] = "%", ["group.tuning"] = "Parametri del comportamento", ["act.meow"] = "Miagola",
            ["about.creditsBtn"] = "Crediti", ["credits.title"] = "Crediti", ["credits.sounds"] = "Suoni", ["credits.back"] = "Indietro", ["credits.purr"] = "Fusa", ["credits.kitten"] = "Miagolio da gattino (salto)", ["credits.munch"] = "Masticare",
            ["look.title"] = "Aspetto", ["look.fur1"] = "Pelo 1 (schiena e zampe)", ["look.fur2"] = "Pelo 2 (pancia)", ["look.eyes"] = "Occhi", ["look.custom"] = "Scegli un colore", ["look.reset"] = "Ripristina colori",
            ["nav.objects"] = "Oggetti", ["obj.bowlbody"] = "Ciotola", ["obj.bowlfood"] = "Cibo", ["obj.bedouter"] = "Cuccia", ["obj.bedinner"] = "Cuscino", ["obj.ballbody"] = "Pallina", ["obj.ballring"] = "Contorno",
            ["picker.title"] = "Scegli un colore", ["picker.new"] = "Nuovo", ["picker.current"] = "Attuale", ["picker.recent"] = "Usati di recente", ["picker.ok"] = "Conferma", ["picker.cancel"] = "Annulla",
            ["act.pet"] = "Coccole",
            ["cat.default"] = "Gatto", ["cat.on.label"] = "Secondo gatto", ["cat.on.hint"] = "Un secondo gatto, con le sue statistiche, il suo carattere e i suoi oggetti", ["char.title"] = "Carattere", ["char.balanced"] = "Equilibrato", ["char.playful"] = "Giocherellone", ["char.lazy"] = "Pigro", ["char.greedy"] = "Goloso", ["char.chatty"] = "Chiacchierone", ["char.needy"] = "Cerca attenzioni", ["char.needy.hint"] = "La felicità cala molto in fretta: ti chiama e ti raggiunge col mouse.", ["char.balanced.hint"] = "Né troppo vivace né troppo pigro.", ["char.playful.hint"] = "Corre di più, consuma cibo ed energia più in fretta e si annoia presto.", ["char.lazy.hint"] = "Si muove poco, ma si stanca prima; mangia meno ed è facile da accontentare.", ["char.greedy.hint"] = "Ha sempre fame: svuota la ciotola in fretta.", ["char.chatty.hint"] = "Miagola molto più spesso e ha un po' meno pazienza.", ["objs.title"] = "Oggetti", ["obj.color"] = "Colore", ["dev.target"] = "Gatto da controllare",
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
            ["nav.pets"] = "Pets", ["pets.intro"] = "Choose the kind of pet to manage.", ["pets.cats"] = "Cats", ["pets.count"] = "{0} on the desktop", ["cats.add"] = "Add a cat", ["cats.max"] = "You have reached the limit of {0} cats.", ["back.pets"] = "‹ Pets", ["back.cats"] = "‹ Cats", ["cat.remove"] = "Remove this cat", ["cat.remove.ask"] = "Remove {0}? Its stats and objects will be lost.", ["pattern.title"] = "Variant", ["pattern.none"] = "None", ["pattern.tabby"] = "Tabby", ["pattern.patches"] = "Patched", ["pattern.calico"] = "Calico", ["look.fur3"] = "Variant colour", ["mixer.title"] = "Mixer", ["mixer.master"] = "Master volume", ["mixer.pet"] = "Cat volume", ["mix.voice"] = "Voice", ["mix.purr"] = "Purring", ["mix.fx"] = "Effects", ["cat.sound"] = "Volume", ["about"] = "About", ["upd.check"] = "Check for updates", ["autoupdate.label"] = "Check for updates at start", ["autoupdate.hint"] = "Looks on GitHub; downloading and installing always needs your click", ["upd.balloon"] = "Open Settings to install it.", ["upd.checking"] = "Checking...", ["upd.latest"] = "You have the latest version ({0}).", ["upd.avail"] = "Version {0} is available.", ["upd.install"] = "Download and install", ["upd.installing"] = "Downloading the update...", ["upd.error"] = "Could not check for updates (no connection, or no published release).", ["version"] = "Version", ["dev.left"] = "{0} more clicks to enable developer mode", ["dev.on"] = "Developer mode enabled", ["dev.active"] = "Developer mode is on", ["nav.dev"] = "Developer", ["dev.intro"] = "Preview features and debug tools.", ["dev.empty"] = "Nothing here yet.", ["dev.disable"] = "Turn off developer mode",
            ["dev.actions"] = "Cat interactions", ["dev.state"] = "State", ["dev.stats.fmt"] = "Hunger {0} · Happiness {1} · Energy {2} · state: {3}", ["act.idle"] = "Idle", ["act.lick"] = "Lick", ["act.walk"] = "Walk", ["act.run"] = "Run", ["act.sleep"] = "Sleep / wake", ["act.play"] = "Play (click)", ["act.pounce"] = "Jump", ["act.frenzy"] = "Frenzy", ["act.groom"] = "Groom each other", ["act.fight"] = "Fight", ["share.label"] = "Shared objects", ["share.hint"] = "All cats use the same bowl, bed and ball (those of the first cat). The objects of the other cats are removed.", ["objs.shared"] = "The objects are shared: manage them from the page of {0}.",
            ["menu.objects"] = "Objects", ["menu.bowl"] = "Bowl", ["menu.bed"] = "Bed", ["menu.ball"] = "Ball", ["menu.remove"] = "Remove", ["name.label"] = "Cat name", ["name.hint"] = "Shown above the stat bars",
            ["act.bowl"] = "Bowl on/off", ["act.bed"] = "Bed on/off", ["act.ball"] = "Ball on/off",
            ["act.hungry"] = "Make hungry", ["act.sad"] = "Make sad", ["act.tired"] = "Make tired",
            ["onTop.label"] = "Always on top", ["onTop.hint"] = "The cat stays above other windows", ["startup.label"] = "Start with Windows", ["startup.hint"] = "Opens the cat when you sign in", ["volume.label"] = "Volume", ["volume.hint"] = "Meows and other cat sounds (0 = mute)", ["volume.unit"] = "%", ["group.tuning"] = "Behaviour tuning", ["act.meow"] = "Meow",
            ["about.creditsBtn"] = "Credits", ["credits.title"] = "Credits", ["credits.sounds"] = "Sounds", ["credits.back"] = "Back", ["credits.purr"] = "Purring", ["credits.kitten"] = "Kitten meow (jump)", ["credits.munch"] = "Chewing",
            ["look.title"] = "Look", ["look.fur1"] = "Fur 1 (back and paws)", ["look.fur2"] = "Fur 2 (belly)", ["look.eyes"] = "Eyes", ["look.custom"] = "Pick a colour", ["look.reset"] = "Reset colours",
            ["nav.objects"] = "Objects", ["obj.bowlbody"] = "Bowl", ["obj.bowlfood"] = "Food", ["obj.bedouter"] = "Bed", ["obj.bedinner"] = "Cushion", ["obj.ballbody"] = "Ball", ["obj.ballring"] = "Outline",
            ["picker.title"] = "Pick a colour", ["picker.new"] = "New", ["picker.current"] = "Current", ["picker.recent"] = "Recently used", ["picker.ok"] = "Done", ["picker.cancel"] = "Cancel",
            ["act.pet"] = "Petting",
            ["cat.default"] = "Cat", ["cat.on.label"] = "Second cat", ["cat.on.hint"] = "A second cat, with its own stats, character and objects", ["char.title"] = "Character", ["char.balanced"] = "Balanced", ["char.playful"] = "Playful", ["char.lazy"] = "Lazy", ["char.greedy"] = "Greedy", ["char.chatty"] = "Chatty", ["char.needy"] = "Attention seeker", ["char.needy.hint"] = "Happiness drops much faster: calls you and follows the mouse.", ["char.balanced.hint"] = "Neither too lively nor too lazy.", ["char.playful.hint"] = "Runs more, burns food and energy faster and gets bored soon.", ["char.lazy.hint"] = "Hardly moves, but tires sooner; eats less and is easy to please.", ["char.greedy.hint"] = "Always hungry: empties the bowl in no time.", ["char.chatty.hint"] = "Meows much more often and is a bit less patient.", ["objs.title"] = "Objects", ["obj.color"] = "Colour", ["dev.target"] = "Cat to control",
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
        }
    };

    public static string T(string key) => All.GetValueOrDefault(Cfg.Lang, All["en"]).GetValueOrDefault(key, key);
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
