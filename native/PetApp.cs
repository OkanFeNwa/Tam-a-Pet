using System.Drawing;
using System.Windows.Forms;

// Owns the pet window and the tray icon (no main form: the app lives in the notification area).
sealed class PetApp : IDisposable
{
    readonly PetWindow pet = new();
    readonly NotifyIcon tray = new();
    readonly ContextMenuStrip menu = new();
    readonly ToolStripMenuItem onTop = new() { CheckOnClick = true, Checked = true };
    readonly ToolStripMenuItem settingsItem = new(), showItem = new(), hideItem = new(), exitItem = new();
    readonly System.Windows.Forms.Timer trimT = new() { Interval = 60_000 };
    SettingsForm? settings;

    public PetApp(bool openSettings = false)
    {
        using var s = typeof(PetApp).Assembly.GetManifestResourceStream("icon.ico")!;
        tray.Icon = new Icon(s);
        tray.Text = "Desktop Pet";

        onTop.CheckedChanged += (_, _) => pet.TopMost = onTop.Checked;
        settingsItem.Click += (_, _) => OpenSettings();
        showItem.Click += (_, _) => pet.Show();
        hideItem.Click += (_, _) => pet.Hide();
        exitItem.Click += (_, _) => { tray.Visible = false; Application.Exit(); };
        menu.Items.AddRange(new ToolStripItem[] { onTop, settingsItem, new ToolStripSeparator(), showItem, hideItem, new ToolStripSeparator(), exitItem });
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => OpenSettings();
        Cfg.LanguageChanged += Translate;
        Translate();
        tray.Visible = true;

        pet.Show();
        trimT.Tick += (_, _) => Native.Trim();
        trimT.Start();
        if (openSettings) OpenSettings();
        Native.Trim();
    }

    void Translate()
    {
        onTop.Text = Str.T("tray.alwaysOnTop");
        settingsItem.Text = Str.T("tray.settings");
        showItem.Text = Str.T("tray.show");
        hideItem.Text = Str.T("tray.hide");
        exitItem.Text = Str.T("tray.exit");
    }

    void OpenSettings()
    {
        if (settings != null) { settings.Activate(); return; }
        settings = new SettingsForm();
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
