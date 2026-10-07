using System.Windows.Forms;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // Single instance (tests with their own TAMAPET_DIR folder can run next to the real app)
        using var mutex = new Mutex(true, "TamAPet.SingleInstance" + (Environment.GetEnvironmentVariable("TAMAPET_DIR") is { } d ? "." + d.Replace('\\', '_').Replace(':', '_') : ""), out bool first);
        if (!first) return;

        ApplicationConfiguration.Initialize();
        Cfg.Load();
        int ds = Array.IndexOf(args, "--dump-sound");
        if (ds >= 0 && ds + 2 < args.Length) { Audio.Dump(args[ds + 1], args[ds + 2]); return; }
        int dump = Array.IndexOf(args, "--dump-stats");
        if (dump >= 0 && dump + 1 < args.Length)
        {
            Cfg.Name = args.Length > dump + 2 ? args[dump + 2] : Cfg.Name;
            using var sw = new StatsWindow(1.25f);
            using var img = sw.Preview(100, 70, 35);
            img.Save(args[dump + 1]);
            return;
        }
        int sp = Array.IndexOf(args, "--spawn");
        using var app = new PetApp(args.Contains("--settings"), sp >= 0 && sp + 1 < args.Length ? args[sp + 1] : "");
        Application.Run();
    }
}
