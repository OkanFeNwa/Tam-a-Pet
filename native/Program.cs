using System.Windows.Forms;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // Single instance
        using var mutex = new Mutex(true, "TamAPet.SingleInstance", out bool first);
        if (!first) return;

        ApplicationConfiguration.Initialize();
        Cfg.Load();
        int dump = Array.IndexOf(args, "--dump-stats");
        if (dump >= 0 && dump + 1 < args.Length)
        {
            using var sw = new StatsWindow(1.25f);
            using var img = sw.Preview(100, 70, 35);
            img.Save(args[dump + 1]);
            return;
        }
        using var app = new PetApp(args.Contains("--settings"));
        Application.Run();
    }
}
