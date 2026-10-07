using System.Drawing;
using System.Windows.Forms;

// Owns the cats' windows and the tray icon (no main form: the app lives in the notification area).
sealed class PetApp : IDisposable
{
    readonly NotifyIcon tray = new();
    readonly ContextMenuStrip menu = new();
    readonly ToolStripMenuItem settingsItem = new(), exitItem = new();
    readonly System.Windows.Forms.Timer trimT = new() { Interval = 60_000 };
    SettingsForm? settings;
    readonly System.Windows.Forms.Timer updateT = new() { Interval = 15_000 };   // first check 15 s after start, then every 12 h
    string? announced;

    public PetApp(bool openSettings = false, string spawn = "")
    {
        using var s = typeof(PetApp).Assembly.GetManifestResourceStream("icon.ico")!;
        tray.Icon = new Icon(s);
        tray.Text = "Desktop Pet";

        settingsItem.Click += (_, _) => OpenSettings();
        exitItem.Click += (_, _) => { SaveAll(); tray.Visible = false; Application.Exit(); };
        Microsoft.Win32.SystemEvents.SessionEnding += (_, _) => SaveAll();   // Windows is shutting down / signing out
        menu.Items.AddRange(new ToolStripItem[] { settingsItem, new ToolStripSeparator(), exitItem });
        MenuRenderer.Apply(menu);
        tray.ContextMenuStrip = menu;
        // left click opens the same menu (right click already does)
        tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                typeof(NotifyIcon).GetMethod("ShowContextMenu", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.Invoke(tray, null);
        };
        Cfg.LanguageChanged += Translate;
        Translate();
        tray.Visible = true;

        Cfg.OnTopChanged += () => { foreach (var c in PetWindow.All) c.ApplyOnTop(Cfg.OnTop); };
        Cfg.MonitorChanged += () => { foreach (var c in PetWindow.All.ToList()) c.ApplyMonitor(); };
        Cfg.CatAdded += StartCat;
        Cfg.ShareChanged += PetWindow.ApplyShare;
        Cfg.CatRemoved += p => PetWindow.Find(p.Index)?.Shutdown();
        foreach (var p in Cfg.Cats.ToList()) StartCat(p);
        foreach (var what in spawn.Split(',', StringSplitOptions.RemoveEmptyEntries)) PetWindow.Find(0)?.Trigger(what);   // --spawn bowl,bed,ball (testing)
        trimT.Tick += (_, _) => Native.Trim();
        trimT.Start();
        if (openSettings) OpenSettings();
        if (Environment.GetCommandLineArgs().Contains("--show-menu"))   // testing: show the tray menu without clicking the icon
        {
            var once = new System.Windows.Forms.Timer { Interval = 1500 };
            once.Tick += (_, _) => { once.Stop(); menu.Show(new Point(600, 300)); };
            once.Start();
        }
        tray.BalloonTipClicked += (_, _) => OpenSettings("general");
        updateT.Tick += async (_, _) => { updateT.Interval = 12 * 3600 * 1000; await CheckUpdate(); };
        updateT.Start();
        Native.Trim();
    }

    // Quiet look at GitHub: if a newer release exists, say so once. Nothing is downloaded here.
    async Task CheckUpdate()
    {
        if (!Cfg.AutoUpdate) return;
        try
        {
            var r = await Updater.CheckInHelper();
            if (r == null || !Updater.IsNewer(r)) return;
            Updater.Found = r;
            if (announced == r.Tag) return;
            announced = r.Tag;
            tray.ShowBalloonTip(10000, "Tam-a-Pet", string.Format(Str.T("upd.avail"), r.Tag) + " " + Str.T("upd.balloon"), ToolTipIcon.Info);
        }
        catch { }   // offline, rate limited, no release yet: stay silent
        finally { GC.Collect(); Native.Trim(); }   // the network stack is not needed again for 12 h: give its memory back
    }

    // A cat's window; its bowl, bed and ball come back where they were
    static void StartCat(CatProfile p)
    {
        var cat = new PetWindow(p);
        cat.Show();
        cat.RestoreObjects();
    }

    static void SaveAll() { foreach (var c in PetWindow.All.ToList()) c.PrepareExit(); }

    void Translate()
    {
        settingsItem.Text = Str.T("tray.settings");
        exitItem.Text = Str.T("tray.exit");
    }

    void OpenSettings(string page = "")
    {
        if (settings != null) { settings.GoTo(page); settings.Activate(); return; }
        settings = new SettingsForm(page);
        settings.FormClosed += (_, _) => { settings.Dispose(); settings = null; Native.Trim(); };
        settings.Show();
    }

    public void Dispose()
    {
        tray.Visible = false;
        tray.Dispose();
        foreach (var c in PetWindow.All.ToList()) c.Dispose();
    }
}
