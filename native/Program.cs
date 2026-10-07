using System.Drawing;
using System.Windows.Forms;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Cfg.Load();
        int cu = Array.IndexOf(args, "--check-update");   // helper process of the update check: see Updater.CheckInHelper
        if (cu >= 0 && cu + 1 < args.Length) { Updater.RunHelper(args[cu + 1]); return; }
        if (Installer.IsSetup) { Installer.RunSetup(args); return; }   // this exe is the installer
        if (args.Contains("--uninstall")) { Installer.Uninstall(args.Contains("--silent")); return; }

        // Development tools that only write a file (no window): they don't need the single-instance lock
        int at = Array.IndexOf(args, "--audio-test");
        if (at >= 0 && at + 1 < args.Length) { Audio.SelfTest(args[at + 1]); return; }
        int dm = Array.IndexOf(args, "--dump-mix");
        if (dm >= 0 && dm + 1 < args.Length) { Audio.DumpMix(args[dm + 1]); return; }
        int dc = Array.IndexOf(args, "--dump-cat");   // file fur1 fur2 eyes (hex): a few frames in those colours
        if (dc >= 0 && dc + 4 < args.Length)
        {
            CatSprite.TryParse(args[dc + 2], out var f1); CatSprite.TryParse(args[dc + 3], out var f2); CatSprite.TryParse(args[dc + 4], out var ec);
            using var sheet = CatSprite.BuildSheet(f1, f2, ec, args.Length > dc + 5 ? args[dc + 5] : "none");
            using var outImg = new Bitmap(8 * 32 * 3, 10 * 32 * 3);
            using (var g = System.Drawing.Graphics.FromImage(outImg))
            {
                g.Clear(Color.FromArgb(70, 70, 80));
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                g.DrawImage(sheet, new Rectangle(0, 0, outImg.Width, outImg.Height), 0, 0, sheet.Width, sheet.Height, GraphicsUnit.Pixel);
            }
            outImg.Save(args[dc + 1]);
            return;
        }
        int dr = Array.IndexOf(args, "--dump-reminder");
        if (dr >= 0 && dr + 1 < args.Length)
        {
            using var rw = new ReminderWindow(1.25f, Cfg.Cats[0]);
            using var rimg = rw.Preview(args.Length > dr + 2 ? int.Parse(args[dr + 2]) : 7);
            rimg.Save(args[dr + 1]);
            return;
        }
        int ds = Array.IndexOf(args, "--dump-sound");
        if (ds >= 0 && ds + 2 < args.Length) { Audio.Dump(args[ds + 1], args[ds + 2]); return; }
        int dump = Array.IndexOf(args, "--dump-stats");
        if (dump >= 0 && dump + 1 < args.Length)
        {
            if (args.Length > dump + 2) Cfg.Cats[0].Name = args[dump + 2];
            using var sw = new StatsWindow(1.25f, Cfg.Cats[0]);
            using var img = sw.Preview(100, 70, 35);
            img.Save(args[dump + 1]);
            return;
        }

        // Only one copy of the app can run: a second launch just exits. Always the same lock, whatever the settings folder.
        using var mutex = new Mutex(true, "TamAPet.SingleInstance", out bool first);
        if (!first) return;

        int sp = Array.IndexOf(args, "--spawn");
        using var app = new PetApp(args.Contains("--settings"), sp >= 0 && sp + 1 < args.Length ? args[sp + 1] : "");
        Application.Run();
    }
}
