using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;

// Looks for a newer release on GitHub and, if the user agrees, replaces the running exe with it.
// Releases are published with the single exe attached as "TamAPet.exe" and a tag like "v0.2.0".
static class Updater
{
    const string Repo = "OkanFeNwa/Tam-a-Pet";
    const string Asset = "TamAPet-Portable.exe";   // the release carries TamAPet-Setup.exe (installer) and this (portable, and the update payload)

    public sealed record Release(Version Version, string Tag, string Url);

    public static Release? Found;   // a newer release seen by the automatic check (nothing is downloaded until the user asks)

    public static Version Current => typeof(Updater).Assembly.GetName().Version!;

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
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var v)) return null;
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
            return lines[0] == "none" ? null : new Release(Version.Parse(lines[0].TrimStart('v', 'V')), lines[0], lines[1]);
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

    public static bool IsNewer(Release r)
    {
        var cur = Current;
        return r.Version > new Version(cur.Major, cur.Minor, Math.Max(0, cur.Build));
    }

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
