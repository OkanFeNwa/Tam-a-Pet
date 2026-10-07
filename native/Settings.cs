using System.Globalization;

// User settings: a flat key -> number table (sliders bind to the keys), plus the language.
static class Cfg
{
    public static readonly Dictionary<string, double> Defaults = new()
    {
        ["nearRange"] = 150, ["jumpRange"] = 500, ["jumpSpeed"] = 2000, ["jumpCooldown"] = 5,
        ["wander"] = 0.08, ["walkSpeed"] = 2, ["cycle"] = 1328
    };
    public static Dictionary<string, double> V = new(Defaults);
    public static string Lang = "it";
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
            File.WriteAllLines(FilePath, V.Select(p => $"{p.Key}={p.Value.ToString(CultureInfo.InvariantCulture)}").Append($"lang={Lang}").Append($"dev={(Dev ? 1 : 0)}"));
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
        V = new(Defaults);
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
            ["dev.actions"] = "Interazioni del gatto", ["dev.state"] = "Stato", ["dev.stats.fmt"] = "Sazietà {0} · Felicità {1} · Energia {2} · stato: {3}", ["act.idle"] = "Idle", ["act.lick"] = "Leccata", ["act.walk"] = "Cammina", ["act.run"] = "Corri", ["act.sleep"] = "Dormi / sveglia", ["act.play"] = "Gioca (click)", ["act.pounce"] = "Salto", ["act.frenzy"] = "Frenesia",
            ["stat.hunger"] = "Sazietà", ["stat.happiness"] = "Felicità", ["stat.energy"] = "Energia",
            ["tray.alwaysOnTop"] = "Sempre in primo piano", ["tray.settings"] = "Impostazioni...", ["tray.show"] = "Mostra gatto",
            ["tray.hide"] = "Nascondi gatto", ["tray.exit"] = "Esci",
            ["title"] = "Impostazioni", ["nav.general"] = "Generale", ["nav.cat"] = "Gatto",
            ["language"] = "Lingua", ["language.hint"] = "Lingua dell'interfaccia", ["reset"] = "Ripristina predefiniti",
            ["cat.intro"] = "Regola il comportamento del gatto. Le modifiche si applicano subito.",
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
            ["dev.actions"] = "Cat interactions", ["dev.state"] = "State", ["dev.stats.fmt"] = "Fullness {0} · Happiness {1} · Energy {2} · state: {3}", ["act.idle"] = "Idle", ["act.lick"] = "Lick", ["act.walk"] = "Walk", ["act.run"] = "Run", ["act.sleep"] = "Sleep / wake", ["act.play"] = "Play (click)", ["act.pounce"] = "Jump", ["act.frenzy"] = "Frenzy",
            ["stat.hunger"] = "Fullness", ["stat.happiness"] = "Happiness", ["stat.energy"] = "Energy",
            ["tray.alwaysOnTop"] = "Always on top", ["tray.settings"] = "Settings...", ["tray.show"] = "Show pet",
            ["tray.hide"] = "Hide pet", ["tray.exit"] = "Exit",
            ["title"] = "Settings", ["nav.general"] = "General", ["nav.cat"] = "Cat",
            ["language"] = "Language", ["language.hint"] = "Interface language", ["reset"] = "Reset to defaults",
            ["cat.intro"] = "Tune how the cat behaves. Changes apply immediately.",
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
