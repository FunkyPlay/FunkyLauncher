// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using FunkyLauncher.Core;

namespace FunkyLauncher.Services;

/// <summary>
/// Is a newer Funky Launcher out? Reads the latest GitHub release of FunkyPlay/FunkyLauncher (one small request at start,
/// nothing is sent about the player). With the player's "Update" the new exe is downloaded from that release,
/// checked against the SHA-256 published next to it, swapped in place of this one, and started.
/// </summary>
public static class LauncherUpdate
{
    public const string LatestApi = "https://api.github.com/repos/FunkyPlay/FunkyLauncher/releases/latest";
    public const string DownloadUrl = "https://funkylauncher.com/download";

    /// <summary>A newer release: version, its notes (as written on GitHub), the exe and its .sha256.</summary>
    public sealed record Release(string Version, string Notes, string? ExeUrl, string? ShaUrl);

    /// <summary>Test hook: another release endpoint (a local test server), like FUNKYLAUNCHER_DATA for the data folder.</summary>
    private static string Api => Environment.GetEnvironmentVariable("FUNKYLAUNCHER_UPDATE_API") is { Length: > 0 } t ? t : LatestApi;

    private static HttpClient Http(int seconds)
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(seconds) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("FunkyLauncher/" + AppConfig.Version);
        return http;
    }

    /// <summary>The newer release, or null if this one is the latest or GitHub can't be reached.</summary>
    public static async Task<Release?> NewerAsync(CancellationToken token)
    {
        try
        {
            using var http = Http(10);
            using var doc = JsonDocument.Parse(await http.GetStringAsync(Api, token));
            var root = doc.RootElement;
            var tag = root.GetProperty("tag_name").GetString()?.TrimStart('v', 'V') ?? "";
            if (!(Version.TryParse(tag, out var latest) && Version.TryParse(AppConfig.Version, out var mine) && latest > mine)) return null;
            string? exe = null, sha = null;
            if (root.TryGetProperty("assets", out var assets))
                foreach (var a in assets.EnumerateArray())
                {
                    var name = a.GetProperty("name").GetString() ?? "";
                    var url = a.GetProperty("browser_download_url").GetString();
                    if (name.Equals("FunkyLauncher.exe", StringComparison.OrdinalIgnoreCase)) exe = url;
                    else if (name.Equals("FunkyLauncher.exe.sha256", StringComparison.OrdinalIgnoreCase)) sha = url;
                }
            var notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
            return new Release(tag, CleanNotes(notes), exe, sha);
        }
        catch { return null; }
    }

    /// <summary>Release notes are Markdown on GitHub: keep the words, drop the markup.</summary>
    private static string CleanNotes(string md)
    {
        var lines = md.Replace("\r", "").Split('\n')
            .Select(l => l.TrimEnd())
            .Where(l => !l.StartsWith("<!--"))
            .Select(l => l.TrimStart('#', ' ').Replace("**", "").Replace("`", ""))
            .Select(l => l.StartsWith("- ") || l.StartsWith("* ") ? "• " + l[2..] : l);
        return string.Join("\n", lines).Trim();
    }

    /// <summary>
    /// The exe that runs now, if it can replace itself: a single-file release (not a developer build with
    /// FunkyLauncher.dll next to it) in a folder the player can write to.
    /// </summary>
    public static string? SelfUpdatableExe()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (exe == null || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return null;
            var dir = Path.GetDirectoryName(exe)!;
            if (File.Exists(Path.Combine(dir, "FunkyLauncher.dll"))) return null;
            var probe = Path.Combine(dir, ".funky-write-test-" + Environment.ProcessId);
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return exe;
        }
        catch { return null; }
    }

    /// <summary>
    /// Downloads the new exe next to this one, checks its SHA-256, puts it in place of this exe (this one becomes
    /// "*.old" and is deleted by the next start) and starts it. The caller then closes this launcher.
    /// Any failure leaves the current launcher untouched.
    /// </summary>
    public static async Task InstallAsync(Release r, string exe, IProgress<double> progress, CancellationToken token)
    {
        if (r.ExeUrl == null || r.ShaUrl == null) throw new InvalidOperationException("no exe in the release");
        var fresh = exe + ".new";
        var old = exe + ".old";
        using var http = Http(600);

        var shaText = await http.GetStringAsync(r.ShaUrl, token);
        var expected = new string(shaText.Trim().TakeWhile(Uri.IsHexDigit).ToArray());
        if (expected.Length != 64) throw new InvalidOperationException("bad .sha256");

        try
        {
            using (var resp = await http.GetAsync(r.ExeUrl, HttpCompletionOption.ResponseHeadersRead, token))
            {
                resp.EnsureSuccessStatusCode();
                var total = resp.Content.Headers.ContentLength ?? 0;
                await using var src = await resp.Content.ReadAsStreamAsync(token);
                await using var dst = File.Create(fresh);
                var buf = new byte[1 << 16];
                long done = 0;
                int n;
                while ((n = await src.ReadAsync(buf, token)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n), token);
                    done += n;
                    if (total > 0) progress.Report((double)done / total);
                }
            }
            string actual;
            await using (var f = File.OpenRead(fresh)) actual = Convert.ToHexString(await SHA256.HashDataAsync(f, token));
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("SHA-256 mismatch");

            if (File.Exists(old)) File.Delete(old);
            File.Move(exe, old);                       // a running exe can be renamed, not overwritten
            try { File.Move(fresh, exe); }
            catch { File.Move(old, exe); throw; }      // put the current one back
        }
        catch
        {
            try { if (File.Exists(fresh)) File.Delete(fresh); } catch { }
            throw;
        }
        Process.Start(new ProcessStartInfo(exe, $"--after-update {Environment.ProcessId}") { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! });
    }

    /// <summary>Started by the previous version: wait until it has closed (it holds the single-launcher lock).</summary>
    public static void WaitForPrevious(string[] args)
    {
        var i = Array.IndexOf(args, "--after-update");
        if (i < 0 || i + 1 >= args.Length || !int.TryParse(args[i + 1], out var pid)) return;
        try { Process.GetProcessById(pid).WaitForExit(30000); } catch { /* already gone */ }
    }

    /// <summary>The previous exe left by an update ("*.old").</summary>
    public static void CleanupOld()
    {
        var exe = Environment.ProcessPath;
        if (exe == null) return;
        var old = exe + ".old";
        Task.Run(async () =>
        {
            for (var i = 0; i < 10 && File.Exists(old); i++)
            {
                try { File.Delete(old); } catch { await Task.Delay(1000); }
            }
        });
    }
}
