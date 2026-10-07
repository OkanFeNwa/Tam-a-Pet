using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

// The installer is the app itself: an exe named "TamAPet-Setup*.exe" shows this small wizard, which copies itself to
// %LocalAppData%\Programs\Tam-a-Pet\TamAPet.exe, adds the Start menu entry (and optionally desktop / start with Windows)
// and registers an uninstaller. The installed exe run with --uninstall removes all of it. No admin rights needed.
static class Installer
{
    const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\TamAPet";
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool IsSetup => Path.GetFileName(Environment.ProcessPath ?? "").StartsWith("TamAPet-Setup", StringComparison.OrdinalIgnoreCase);

    // TAMAPET_INSTALL_DIR only exists so the installer can be tested without touching the real installation
    static string InstallDir => Environment.GetEnvironmentVariable("TAMAPET_INSTALL_DIR")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Tam-a-Pet");
    static string StartMenuLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Tam-a-Pet.lnk");
    static string DesktopLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Tam-a-Pet.lnk");

    static readonly Dictionary<string, (string it, string en)> Text = new()
    {
        ["title"] = ("Installa Tam-a-Pet", "Install Tam-a-Pet"),
        ["intro"] = ("Uno o più gatti che girano per il desktop. Viene installato solo per il tuo utente, senza permessi di amministratore.", "One or more cats roaming your desktop. It is installed for your user only, without administrator rights."),
        ["where"] = ("Cartella di installazione", "Install folder"),
        ["desktop"] = ("Crea un collegamento sul desktop", "Create a desktop shortcut"),
        ["startup"] = ("Avvia con Windows", "Start with Windows"),
        ["install"] = ("Installa", "Install"),
        ["cancel"] = ("Annulla", "Cancel"),
        ["done"] = ("Installazione completata", "Installation complete"),
        ["launch"] = ("Avvia Tam-a-Pet", "Launch Tam-a-Pet"),
        ["close"] = ("Chiudi", "Close"),
        ["failed"] = ("Installazione non riuscita: ", "Installation failed: "),
        ["unask"] = ("Rimuovere Tam-a-Pet da questo computer?", "Remove Tam-a-Pet from this computer?"),
        ["unsettings"] = ("Eliminare anche le impostazioni e i dati dei gatti?", "Also delete the settings and the cats' data?"),
        ["undone"] = ("Tam-a-Pet è stato rimosso.", "Tam-a-Pet has been removed."),
    };
    static string T(string key) => Cfg.Lang == "it" ? Text[key].it : Text[key].en;

    // ---- install ---------------------------------------------------------------------------------

    public static void RunSetup(string[] args)
    {
        if (args.Contains("--silent")) { Install(args.Contains("--desktop"), args.Contains("--startup")); return; }
        Application.Run(new SetupForm());
    }

    static void KillRunning()
    {
        foreach (var p in Process.GetProcessesByName("TamAPet"))
            if (p.Id != Environment.ProcessId) { try { p.Kill(); p.WaitForExit(4000); } catch { } }
    }

    public static string Install(bool desktop, bool startup)
    {
        string target = Path.Combine(InstallDir, "TamAPet.exe");
        Directory.CreateDirectory(InstallDir);
        KillRunning();
        File.Copy(Environment.ProcessPath!, target, true);
        MakeLink(StartMenuLink, target);
        if (desktop) MakeLink(DesktopLink, target);
        using (var k = Registry.CurrentUser.CreateSubKey(UninstallKey))
        {
            var v = typeof(Installer).Assembly.GetName().Version!;
            k.SetValue("DisplayName", "Tam-a-Pet");
            k.SetValue("DisplayVersion", $"{v.Major}.{v.Minor}.{v.Build}");
            k.SetValue("Publisher", "OkanFeNwa");
            k.SetValue("InstallLocation", InstallDir);
            k.SetValue("DisplayIcon", target);
            k.SetValue("UninstallString", $"\"{target}\" --uninstall");
            k.SetValue("NoModify", 1, RegistryValueKind.DWord);
            k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            k.SetValue("EstimatedSize", (int)(new FileInfo(target).Length / 1024), RegistryValueKind.DWord);
        }
        using (var run = Registry.CurrentUser.OpenSubKey(RunKey, true))
        {
            if (startup) run?.SetValue("TamAPet", $"\"{target}\"");
            else if (run?.GetValue("TamAPet") != null) run.SetValue("TamAPet", $"\"{target}\"");   // already on: keep it pointing at the installed copy
        }
        return target;
    }

    static void MakeLink(string path, string target)
    {
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        dynamic link = shell.CreateShortcut(path);
        link.TargetPath = target;
        link.WorkingDirectory = Path.GetDirectoryName(target);
        link.IconLocation = target + ",0";
        link.Description = "Tam-a-Pet";
        link.Save();
    }

    // ---- uninstall -------------------------------------------------------------------------------

    public static void Uninstall(bool silent)
    {
        if (!silent && MessageBox.Show(T("unask"), "Tam-a-Pet", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        bool wipe = !silent && MessageBox.Show(T("unsettings"), "Tam-a-Pet", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        KillRunning();
        foreach (var l in new[] { StartMenuLink, DesktopLink }) try { File.Delete(l); } catch { }
        using (var run = Registry.CurrentUser.OpenSubKey(RunKey, true)) run?.DeleteValue("TamAPet", false);
        Registry.CurrentUser.DeleteSubKey(UninstallKey, false);
        string dir = Path.GetDirectoryName(Environment.ProcessPath)!;
        string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Tam-a-Pet");
        // this exe is running from the folder: a script removes it once we have exited
        string cmd = $"/c ping -n 3 127.0.0.1 >nul & rmdir /s /q \"{dir}\"" + (wipe ? $" & rmdir /s /q \"{data}\"" : "");
        Process.Start(new ProcessStartInfo("cmd.exe", cmd) { CreateNoWindow = true, UseShellExecute = false });
        if (!silent) MessageBox.Show(T("undone"), "Tam-a-Pet", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // ---- the wizard ------------------------------------------------------------------------------

    sealed class SetupForm : Form
    {
        readonly float k;
        readonly CheckBox desktop = new(), startup = new();
        readonly Button go = new(), cancel = new();
        readonly Label status = new();
        readonly Icon appIcon;
        string? installedPath;   // set once the install is done: the main button then launches the app

        int P(int v) => (int)Math.Round(v * k);

        Label Lbl(string t, Font f, Color c, int x, int y, int w, int h) => new() { Text = t, Font = f, ForeColor = c, BackColor = Color.Transparent, Bounds = new Rectangle(P(x), P(y), P(w), P(h)) };

        void Flat(Button b, bool primary, int x, int y, int w)
        {
            b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderSize = primary ? 0 : 1; b.FlatAppearance.BorderColor = Theme.Line;
            b.BackColor = primary ? Theme.Accent : Theme.Card; b.ForeColor = primary ? Color.White : Theme.Text;
            b.Font = new Font("Segoe UI Semibold", 9.5f); b.Cursor = Cursors.Hand;
            b.Bounds = new Rectangle(P(x), P(y), P(w), P(36));
        }

        public SetupForm()
        {
            using (var gr = CreateGraphics()) k = gr.DpiX / 96f;
            using (var s = typeof(Installer).Assembly.GetManifestResourceStream("icon.ico")!) appIcon = new Icon(s);
            Icon = appIcon;
            Text = T("title");
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Bg; ForeColor = Theme.Text; Font = new Font("Segoe UI", 9.5f);
            ClientSize = new Size(P(500), P(360));

            var v = typeof(Installer).Assembly.GetName().Version!;
            Controls.Add(Lbl("Tam-a-Pet", new Font("Segoe UI Semibold", 20f), Theme.Text, 28, 24, 440, 40));
            Controls.Add(Lbl($"v{v.Major}.{v.Minor}.{v.Build}", new Font("Segoe UI", 9.5f), Theme.Accent, 30, 66, 200, 22));
            Controls.Add(Lbl(T("intro"), Font, Theme.Muted, 30, 96, 440, 48));
            Controls.Add(Lbl(T("where"), new Font("Segoe UI Semibold", 9.5f), Theme.Text, 30, 152, 440, 22));
            Controls.Add(Lbl(InstallDir, new Font("Segoe UI", 9f), Theme.Muted, 30, 174, 440, 22));

            foreach (var (box, key, y) in new[] { (desktop, "desktop", 208), (startup, "startup", 236) })
            {
                box.Text = T(key); box.BackColor = Theme.Bg; box.ForeColor = Theme.Text; box.Cursor = Cursors.Hand;
                box.Bounds = new Rectangle(P(30), P(y), P(440), P(24)); box.FlatStyle = FlatStyle.Standard; box.UseVisualStyleBackColor = false;   // System style ignores ForeColor: black text on the dark background
                Controls.Add(box);
            }
            desktop.Checked = true;

            status.Bounds = new Rectangle(P(30), P(276), P(440), P(24)); status.ForeColor = Theme.Muted; status.BackColor = Color.Transparent;
            Controls.Add(status);

            Flat(go, true, 250, 306, 120); go.Text = T("install"); go.Click += (_, _) =>
            {
                if (installedPath == null) DoInstall();
                else { Process.Start(new ProcessStartInfo(installedPath) { UseShellExecute = true }); Close(); }
            };
            Flat(cancel, false, 378, 306, 100); cancel.Text = T("cancel"); cancel.Click += (_, _) => Close();
            Controls.Add(go); Controls.Add(cancel);
            AcceptButton = go;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (Theme.Bg.GetBrightness() < 0.5f) { int on = 1; DwmSetWindowAttribute(Handle, 20, ref on, 4); }   // dark title bar
        }

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);

        void DoInstall()
        {
            go.Enabled = cancel.Enabled = desktop.Enabled = startup.Enabled = false;
            Cursor = Cursors.WaitCursor;
            string target;
            try { target = Install(desktop.Checked, startup.Checked); }
            catch (Exception ex)
            {
                Cursor = Cursors.Default;
                status.ForeColor = Color.FromArgb(0xE0, 0x52, 0x5A); status.Text = T("failed") + ex.Message;
                cancel.Enabled = true; cancel.Text = T("close");
                return;
            }
            Cursor = Cursors.Default;
            desktop.Visible = startup.Visible = false;
            status.ForeColor = Theme.Accent; status.Text = T("done");
            go.Text = T("launch"); go.Enabled = true; go.Width = P(150); go.Left = P(220);
            cancel.Text = T("close"); cancel.Enabled = true; cancel.Left = P(378);
            installedPath = target;
        }

        protected override void Dispose(bool disposing) { if (disposing) appIcon.Dispose(); base.Dispose(disposing); }
    }
}
