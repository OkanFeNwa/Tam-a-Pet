using System.Drawing;
using System.Windows.Forms;

// Owns the pet window and the tray icon (no main form: the app lives in the notification area).
sealed class PetApp : IDisposable
{
    readonly PetWindow pet = new();
    readonly NotifyIcon tray = new();
    readonly ContextMenuStrip menu = new();
    readonly ToolStripMenuItem settingsItem = new(), exitItem = new(), objectsItem = new();
    readonly ToolStripMenuItem bowlItem = new(), bedItem = new(), ballItem = new();
    readonly System.Windows.Forms.Timer trimT = new() { Interval = 60_000 };
    SettingsForm? settings;

    public PetApp(bool openSettings = false, string spawn = "")
    {
        using var s = typeof(PetApp).Assembly.GetManifestResourceStream("icon.ico")!;
        tray.Icon = new Icon(s);
        tray.Text = "Desktop Pet";

        settingsItem.Click += (_, _) => OpenSettings();
        exitItem.Click += (_, _) => { tray.Visible = false; Application.Exit(); };
        bowlItem.Click += (_, _) => pet.ToggleProp(false);
        bedItem.Click += (_, _) => pet.ToggleProp(true);
        ballItem.Click += (_, _) => pet.ToggleBall();
        objectsItem.DropDownItems.AddRange(new ToolStripItem[] { bowlItem, bedItem, ballItem });
        objectsItem.DropDownOpening += (_, _) => { bowlItem.Checked = pet.HasBowl; bedItem.Checked = pet.HasBed; ballItem.Checked = pet.HasBall; };
        menu.Items.AddRange(new ToolStripItem[] { objectsItem, settingsItem, new ToolStripSeparator(), exitItem });
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

        Cfg.OnTopChanged += () => pet.ApplyOnTop(Cfg.OnTop);
        pet.Show();
        foreach (var what in spawn.Split(',', StringSplitOptions.RemoveEmptyEntries)) pet.Trigger(what);   // --spawn bowl,bed,ball (testing)
        trimT.Tick += (_, _) => Native.Trim();
        trimT.Start();
        if (openSettings) OpenSettings();
        if (Environment.GetCommandLineArgs().Contains("--show-menu"))   // testing: show the tray menu without clicking the icon
        {
            var once = new System.Windows.Forms.Timer { Interval = 1500 };
            once.Tick += (_, _) => { once.Stop(); menu.Show(new Point(600, 300)); objectsItem.ShowDropDown(); };
            once.Start();
        }
        Native.Trim();
    }

    void Translate()
    {
        objectsItem.Text = Str.T("menu.objects");
        bowlItem.Text = Str.T("menu.bowl");
        bedItem.Text = Str.T("menu.bed");
        ballItem.Text = Str.T("menu.ball");
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
        pet.Dispose();
    }
}
