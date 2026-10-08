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
        int dc = Array.IndexOf(args, "--dump-cat");   // file fur1 fur2 eyes [pattern] [accessory|-] [classic]: the whole sheet in those colours
        if (dc >= 0 && dc + 4 < args.Length)
        {
            var prof = new CatProfile(0);
            CatSprite.TryParse(args[dc + 2], out prof.Fur1); CatSprite.TryParse(args[dc + 3], out prof.Fur2); CatSprite.TryParse(args[dc + 4], out prof.Eyes);
            prof.Pattern = args.Length > dc + 5 ? args[dc + 5] : "none";
            prof.Accessory = args.Length > dc + 6 && args[dc + 6] != "-" ? args[dc + 6] : "";
            Cfg.ClassicSprites = args.Length > dc + 7 && args[dc + 7] == "classic";
            using var sheet = CatSprite.BuildSheet(prof);
            using var outImg = new Bitmap(sheet.Width * 3, sheet.Height * 3);
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

        int dsh = Array.IndexOf(args, "--dump-sheet");   // file fur eyes pattern variantColour accessory|- nose ears: one cat's sprite sheet, as the app draws it (for videos and docs)
        if (dsh >= 0 && dsh + 7 < args.Length)
        {
            var prof = new CatProfile(0);
            CatSprite.TryParse(args[dsh + 2], out prof.Fur1); CatSprite.TryParse(args[dsh + 3], out prof.Eyes);
            prof.Pattern = args[dsh + 4]; CatSprite.TryParse(args[dsh + 5], out prof.Fur3);
            prof.Accessory = args[dsh + 6] == "-" ? "" : args[dsh + 6];
            CatSprite.TryParse(args[dsh + 7], out prof.Nose);
            if (args.Length > dsh + 8) CatSprite.TryParse(args[dsh + 8], out prof.Ears);
            Cfg.ClassicSprites = false;
            using var sheet = CatSprite.BuildSheet(prof);
            sheet.Save(args[dsh + 1]);
            return;
        }
        int dpr = Array.IndexOf(args, "--dump-props");   // folder bowlColour bedColour ballColour: the three objects (bowl in its four levels), one pixel per art pixel
        if (dpr >= 0 && dpr + 4 < args.Length)
        {
            Directory.CreateDirectory(args[dpr + 1]);
            CatSprite.TryParse(args[dpr + 2], out var bc); CatSprite.TryParse(args[dpr + 3], out var dc2); CatSprite.TryParse(args[dpr + 4], out var lc);
            for (int lv = 0; lv < 4; lv++) { using var b = PixelArt.BowlImage(1, bc, lv); b.Save(Path.Combine(args[dpr + 1], $"bowl-{lv}.png")); }
            using (var b = PixelArt.BedImage(1, dc2)) b.Save(Path.Combine(args[dpr + 1], "bed.png"));
            using (var b = PixelArt.BallImage(1, lc)) b.Save(Path.Combine(args[dpr + 1], "ball.png"));
            return;
        }

        if (args.Contains("--check-share")) { Environment.Exit(LookShare.SelfCheck() ? 0 : 1); }   // dev check of sharing a look
        if (args.Contains("--check-paint")) { Environment.Exit(HandPaint.SelfCheck() ? 0 : 1); }   // dev check of the hand painting layer
        if (args.Contains("--check-presets")) { Environment.Exit(LookPresets.SelfCheck() ? 0 : 1); }   // dev check: presets survive save/load, names stay unique

        int dr = Array.IndexOf(args, "--dump-reminder");
        if (dr >= 0 && dr + 1 < args.Length)
        {
            using var rw = new ReminderWindow(args.Length > dr + 3 ? float.Parse(args[dr + 3], System.Globalization.CultureInfo.InvariantCulture) : 1.25f, Cfg.Cats[0]);
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
            int Arg(int i, int d) => args.Length > dump + i ? int.Parse(args[dump + i]) : d;   // file [name [hunger happiness energy [scale x100]]]
            using var sw = new StatsWindow(Arg(6, 125) / 100f, Cfg.Cats[0]);
            using var img = sw.Preview(Arg(3, 100), Arg(4, 70), Arg(5, 35));
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
