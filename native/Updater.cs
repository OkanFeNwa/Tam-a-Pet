using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;

// Looks for a newer release on GitHub and, if the user agrees, replaces the running exe with it.
// Releases are published with the exe attached as "TamAPet-Portable.exe" and a tag like "v0.1.0" or "v0.1.0b".
//
// Version numbers: MAJOR.MINOR.PATCH + an optional letter.
//   MAJOR  the complete release (0 = still being built)
//   MINOR  a major feature was added
//   PATCH  minor features and bug fixes
//   letter (a-z) tiny bug fixes only, no new feature: 0.1.0 < 0.1.0a < 0.1.0b < 0.1.1
readonly record struct AppVersion(int Major, int Minor, int Patch, char Letter) : IComparable<AppVersion>
{
    public static bool TryParse(string text, out AppVersion v)
    {
        v = default;
        string t = text.Trim().TrimStart('v', 'V');
        char letter = '\0';
        if (t.Length > 0 && char.IsAsciiLetter(t[^1])) { letter = char.ToLowerInvariant(t[^1]); t = t[..^1]; }
        var parts = t.Split('.');
        if (parts.Length != 3 || !int.TryParse(parts[0], out int ma) || !int.TryParse(parts[1], out int mi) || !int.TryParse(parts[2], out int pa) || ma < 0 || mi < 0 || pa < 0) return false;
        v = new AppVersion(ma, mi, pa, letter);
        return true;
    }
    public int CompareTo(AppVersion o)
    {
        int c = Major.CompareTo(o.Major); if (c != 0) return c;
        c = Minor.CompareTo(o.Minor); if (c != 0) return c;
        c = Patch.CompareTo(o.Patch); if (c != 0) return c;
        return Letter.CompareTo(o.Letter);
    }
    public static bool operator >(AppVersion a, AppVersion b) => a.CompareTo(b) > 0;
    public static bool operator <(AppVersion a, AppVersion b) => a.CompareTo(b) < 0;
    public override string ToString() => $"{Major}.{Minor}.{Patch}{(Letter == '\0' ? "" : Letter.ToString())}";
}

static class Updater
{
    const string Repo = "OkanFeNwa/Tam-a-Pet";
    const string Asset = "TamAPet-Portable.exe";   // the release carries TamAPet-Setup.exe (installer) and this (portable, and the update payload)

    public sealed record Release(AppVersion Version, string Tag, string Url);

    public static Release? Found;   // a newer release seen by the automatic check (nothing is downloaded until the user asks)

    // The running version, from the build's InformationalVersion (which carries the letter, e.g. "0.1.0b")
    public static AppVersion Current { get; } = ReadCurrent();
    static AppVersion ReadCurrent()
    {
        var asm = typeof(Updater).Assembly;
        string s = (System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(asm)?.InformationalVersion ?? "").Split('+')[0];
        if (AppVersion.TryParse(s, out var v)) return v;
        var n = asm.GetName().Version!;
        return new AppVersion(n.Major, n.Minor, Math.Max(0, n.Build), '\0');
    }

    static HttpClient NewClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Tam-a-Pet-updater");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    }

    // null = no release (or none with the exe); throws when GitHub can't be reached or the repo is not public
    public static async Task<Release?> Latest()
    {
        using var http = NewClient();
        using var doc = JsonDocument.Parse(await http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest"));
        string tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
        if (!AppVersion.TryParse(tag, out var v)) return null;
        foreach (var a in doc.RootElement.GetProperty("assets").EnumerateArray())
            if (string.Equals(a.GetProperty("name").GetString(), Asset, StringComparison.OrdinalIgnoreCase))
                return new Release(v, tag, a.GetProperty("browser_download_url").GetString()!);
        return null;
    }

    // The look at GitHub runs in a short-lived copy of the exe (--check-update file), so the HTTP/TLS machinery
    // never gets loaded in the long-running pet process (it would cost several MB of memory for good).
    public static async Task<Release?> CheckInHelper()
    {
        string tmp = Path.Combine(Path.GetTempPath(), $"tamapet-check-{Environment.ProcessId}.txt");
        try
        {
            using var p = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, $"--check-update \"{tmp}\"") { CreateNoWindow = true, UseShellExecute = false })!;
            await p.WaitForExitAsync();
            var lines = File.Exists(tmp) ? File.ReadAllLines(tmp) : Array.Empty<string>();
            if (lines.Length == 0 || lines[0] == "error") throw new IOException("update check failed");
            return lines[0] == "none" || !AppVersion.TryParse(lines[0], out var lv) ? null : new Release(lv, lines[0], lines[1]);
        }
        finally { try { File.Delete(tmp); } catch { } }
    }

    // what the helper process runs
    public static void RunHelper(string outFile)
    {
        try
        {
            var r = Latest().GetAwaiter().GetResult();
            File.WriteAllLines(outFile, r == null ? new[] { "none" } : new[] { r.Tag, r.Url });
        }
        catch { File.WriteAllText(outFile, "error"); }
    }

    public static bool IsNewer(Release r) => r.Version > Current;

    // Downloads the new exe next to the running one, then a tiny script waits for this process to end,
    // swaps the files and starts the new version. The caller exits the app right after.
    public static async Task Install(Release r)
    {
        string exe = Environment.ProcessPath!;
        string dir = Path.GetDirectoryName(exe)!;
        string fresh = Path.Combine(dir, "TamAPet.update.exe");
        using (var http = NewClient())
        using (var src = await http.GetStreamAsync(r.Url))
        using (var dst = File.Create(fresh))
            await src.CopyToAsync(dst);

        string bat = Path.Combine(Path.GetTempPath(), "tamapet-update.cmd");
        File.WriteAllText(bat,
            "@echo off\r\n" +
            $"for /l %%i in (1,1,40) do (move /y \"{fresh}\" \"{exe}\" >nul 2>&1 && goto done || timeout /t 1 /nobreak >nul)\r\n" +
            "exit /b 1\r\n" +
            ":done\r\n" +
            $"start \"\" \"{exe}\"\r\n" +
            "del \"%~f0\"\r\n");
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{bat}\"") { CreateNoWindow = true, UseShellExecute = false });
    }
}
