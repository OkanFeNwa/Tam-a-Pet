using System.Globalization;

// User settings: a flat key -> number table (sliders bind to the keys), plus the language.
static class Cfg
{
    public static readonly Dictionary<string, double> Defaults = new()
    {
        ["volume"] = 40, ["nearRange"] = 150, ["jumpRange"] = 500, ["jumpSpeed"] = 2000, ["jumpCooldown"] = 5,
        ["wander"] = 0.08, ["walkSpeed"] = 2, ["cycle"] = 1328
    };
    public static Dictionary<string, double> V = new(Defaults);
    public static string Lang = "it";
    public static bool OnTop = true;   // keep the cat above other windows
    public static string Name = "";   // the cat's name
    public static bool Dev;   // developer mode, unlocked from the version label in settings

    static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Tam-a-Pet", "settings.ini");

    public static double NearRange => V["nearRange"];
    public static double JumpRange => V["jumpRange"];
    public static double JumpSpeed => V["jumpSpeed"];
    public static double JumpCooldown => V["jumpCooldown"];
    public static double Wander => V["wander"];
    public static double WalkSpeed => V["walkSpeed"];
    public static double Cycle => V["cycle"];

    public static event Action? LanguageChanged;
    public static event Action? OnTopChanged;

    public static void SetOnTop(bool on) { OnTop = on; Save(); OnTopChanged?.Invoke(); }

    public static void Load()
    {
        try
        {
            foreach (var line in File.ReadAllLines(FilePath))
            {
                var kv = line.Split('=', 2);
                if (kv.Length != 2) continue;
                if (kv[0] == "lang") Lang = kv[1];
                else if (kv[0] == "dev") Dev = kv[1] == "1";
                else if (kv[0] == "onTop") OnTop = kv[1] != "0";
                else if (kv[0] == "name") Name = kv[1];
                else if (V.ContainsKey(kv[0]) && double.TryParse(kv[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) V[kv[0]] = d;
            }
        }
        catch { }
        if (V["jumpSpeed"] == 800) V["jumpSpeed"] = Defaults["jumpSpeed"];   // old default was too sensitive
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllLines(FilePath, V.Select(p => $"{p.Key}={p.Value.ToString(CultureInfo.InvariantCulture)}").Append($"lang={Lang}").Append($"dev={(Dev ? 1 : 0)}").Append($"onTop={(OnTop ? 1 : 0)}").Append($"name={Name.Replace('\n', ' ').Replace('\r', ' ')}"));
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
            ["about"] = "Informazioni", ["version"] = "Versione", ["dev.left"] = "Ancora {0} click per la modalità sviluppatore", ["dev.on"] = "Modalità sviluppatore attivata", ["dev.active"] = "Modalità sviluppatore attiva", ["nav.dev"] = "Sviluppatore", ["dev.intro"] = "Funzioni in anteprima e strumenti di debug.", ["dev.empty"] = "Per ora non c'è niente qui.", ["dev.disable"] = "Disattiva modalità sviluppatore",
            ["dev.actions"] = "Interazioni del gatto", ["dev.state"] = "Stato", ["dev.stats.fmt"] = "Fame {0} · Felicità {1} · Energia {2} · stato: {3}", ["act.idle"] = "Idle", ["act.lick"] = "Leccata", ["act.walk"] = "Cammina", ["act.run"] = "Corri", ["act.sleep"] = "Dormi / sveglia", ["act.play"] = "Gioca (click)", ["act.pounce"] = "Salto", ["act.frenzy"] = "Frenesia",
            ["menu.objects"] = "Oggetti", ["menu.bowl"] = "Ciotola", ["menu.bed"] = "Cuccia", ["menu.ball"] = "Pallina", ["menu.remove"] = "Rimuovi", ["name.label"] = "Nome del gatto", ["name.hint"] = "Compare sopra le barre delle statistiche",
            ["act.bowl"] = "Ciotola on/off", ["act.bed"] = "Cuccia on/off", ["act.ball"] = "Pallina on/off",
            ["act.hungry"] = "Ha fame", ["act.sad"] = "È triste", ["act.tired"] = "Ha sonno",
            ["onTop.label"] = "Sempre in primo piano", ["onTop.hint"] = "Il gatto resta sopra le altre finestre", ["startup.label"] = "Avvia con Windows", ["startup.hint"] = "Apre il gatto quando accedi al PC", ["volume.label"] = "Volume", ["volume.hint"] = "Miagolii e suoni del gatto (0 = muto)", ["volume.unit"] = "%", ["group.tuning"] = "Parametri del comportamento", ["act.meow"] = "Miagola",
            ["about.creditsBtn"] = "Crediti", ["credits.title"] = "Crediti", ["credits.sounds"] = "Suoni", ["credits.back"] = "Indietro", ["credits.purr"] = "Fusa", ["credits.kitten"] = "Miagolio da gattino (salto)", ["credits.munch"] = "Masticare",
            ["stat.hunger"] = "Fame", ["stat.happiness"] = "Felicità", ["stat.energy"] = "Energia",
            ["tray.settings"] = "Impostazioni...", 
            ["tray.exit"] = "Esci",
            ["title"] = "Impostazioni", ["nav.general"] = "Generale", ["nav.cat"] = "Gatto",
            ["language"] = "Lingua", ["language.hint"] = "Lingua dell'interfaccia", ["reset"] = "Ripristina predefiniti",
            
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
            ["about"] = "About", ["version"] = "Version", ["dev.left"] = "{0} more clicks to enable developer mode", ["dev.on"] = "Developer mode enabled", ["dev.active"] = "Developer mode is on", ["nav.dev"] = "Developer", ["dev.intro"] = "Preview features and debug tools.", ["dev.empty"] = "Nothing here yet.", ["dev.disable"] = "Turn off developer mode",
            ["dev.actions"] = "Cat interactions", ["dev.state"] = "State", ["dev.stats.fmt"] = "Hunger {0} · Happiness {1} · Energy {2} · state: {3}", ["act.idle"] = "Idle", ["act.lick"] = "Lick", ["act.walk"] = "Walk", ["act.run"] = "Run", ["act.sleep"] = "Sleep / wake", ["act.play"] = "Play (click)", ["act.pounce"] = "Jump", ["act.frenzy"] = "Frenzy",
            ["menu.objects"] = "Objects", ["menu.bowl"] = "Bowl", ["menu.bed"] = "Bed", ["menu.ball"] = "Ball", ["menu.remove"] = "Remove", ["name.label"] = "Cat name", ["name.hint"] = "Shown above the stat bars",
            ["act.bowl"] = "Bowl on/off", ["act.bed"] = "Bed on/off", ["act.ball"] = "Ball on/off",
            ["act.hungry"] = "Make hungry", ["act.sad"] = "Make sad", ["act.tired"] = "Make tired",
            ["onTop.label"] = "Always on top", ["onTop.hint"] = "The cat stays above other windows", ["startup.label"] = "Start with Windows", ["startup.hint"] = "Opens the cat when you sign in", ["volume.label"] = "Volume", ["volume.hint"] = "Meows and other cat sounds (0 = mute)", ["volume.unit"] = "%", ["group.tuning"] = "Behaviour tuning", ["act.meow"] = "Meow",
            ["about.creditsBtn"] = "Credits", ["credits.title"] = "Credits", ["credits.sounds"] = "Sounds", ["credits.back"] = "Back", ["credits.purr"] = "Purring", ["credits.kitten"] = "Kitten meow (jump)", ["credits.munch"] = "Chewing",
            ["stat.hunger"] = "Hunger", ["stat.happiness"] = "Happiness", ["stat.energy"] = "Energy",
            ["tray.settings"] = "Settings...", 
            ["tray.exit"] = "Exit",
            ["title"] = "Settings", ["nav.general"] = "General", ["nav.cat"] = "Cat",
            ["language"] = "Language", ["language.hint"] = "Interface language", ["reset"] = "Reset to defaults",
            
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

    public static string T(string key) => All.GetValueOrDefault(Cfg.Lang, All["it"]).GetValueOrDefault(key, key);
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
