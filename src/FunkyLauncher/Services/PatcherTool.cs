// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FunkyLauncher.Core;

namespace FunkyLauncher.Services;

/// <summary>
/// 0.1.23 (Legal 25.11): FunkyPatcher (Mutagen, GPL-3.0) is its own program, run as a separate process and talked to only
/// through command-line arguments and its JSON report — no Mutagen code inside the launcher's process.
/// The exe comes with each launcher release on GitHub (FunkyPatcher.exe + .sha256) and is kept in DataDir\tools.
/// </summary>
public static class PatcherTool
{
    public sealed record Run(bool Ok, string? Error, int Made, int Skipped, int Patches);

    private static string ReleaseBase => $"{AppConfig.GitHubUrl}/releases/download/v{AppConfig.Version.Split('+')[0]}/";

    /// <summary>The exe to run: FUNKYLAUNCHER_PATCHER (tests), the one next to the launcher (a build from source), else the downloaded one.</summary>
    public static async Task<string> EnsureAsync(Action<string> log, CancellationToken token)
    {
        var env = Environment.GetEnvironmentVariable("FUNKYLAUNCHER_PATCHER");
        if (!string.IsNullOrEmpty(env) && File.Exists(env)) return env;
        var near = Path.Combine(AppContext.BaseDirectory, "FunkyPatcher.exe");
        if (File.Exists(near)) return near;
        var dir = Path.Combine(AppConfig.DataDir, "tools", "FunkyPatcher-" + AppConfig.Version.Split('+')[0]);
        var exe = Path.Combine(dir, "FunkyPatcher.exe");
        if (File.Exists(exe)) return exe;

        Directory.CreateDirectory(dir);
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("FunkyLauncher/" + AppConfig.Version);
        log("download " + ReleaseBase + "FunkyPatcher.exe");
        var expected = (await http.GetStringAsync(ReleaseBase + "FunkyPatcher.exe.sha256", token)).Trim().Split(' ', '\t', '\r', '\n')[0];
        if (expected.Length != 64) throw new InvalidOperationException("FunkyPatcher: bad .sha256");
        var part = exe + ".part";
        using (var resp = await http.GetAsync(ReleaseBase + "FunkyPatcher.exe", HttpCompletionOption.ResponseHeadersRead, token))
        {
            resp.EnsureSuccessStatusCode();
            await using var src = await resp.Content.ReadAsStreamAsync(token);
            await using var dst = File.Create(part);
            await src.CopyToAsync(dst, token);
        }
        string got;
        await using (var f = File.OpenRead(part)) got = Convert.ToHexString(await SHA256.HashDataAsync(f, token));
        if (!got.Equals(expected, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(part);
            throw new InvalidOperationException("FunkyPatcher: sha256 does not match");
        }
        File.Move(part, exe, true);
        return exe;
    }

    /// <summary>Runs FunkyPatcher and reads its report. Its console lines go to <paramref name="log"/>.</summary>
    public static async Task<Run> BuildAsync(string mo2, string profile, string game, IReadOnlyList<string> dicts, string outDir,
        string gameType, string prefix, string report, Action<string> log, CancellationToken token)
    {
        var exe = await EnsureAsync(log, token);
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        void Add(string k, string v) { psi.ArgumentList.Add(k); psi.ArgumentList.Add(v); }
        Add("--mo2", mo2); Add("--profile", profile); Add("--game", game);
        foreach (var d in dicts) Add("--dict", d);
        Add("--out", outDir); Add("--game-type", gameType); Add("--name", prefix); Add("--report", report);
        if (File.Exists(report)) File.Delete(report);

        using var p = new Process { StartInfo = psi };
        var err = new StringBuilder();
        p.OutputDataReceived += (_, e) => { if (e.Data != null) log(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (err) err.AppendLine(e.Data); };
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        try { await p.WaitForExitAsync(token); }
        catch (OperationCanceledException) { try { p.Kill(true); } catch { } throw; }
        p.WaitForExit();                                               // the last output lines

        if (!File.Exists(report)) return new Run(false, $"FunkyPatcher exit {p.ExitCode}: {err.ToString().Trim()}", 0, 0, 0);
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(report, token));
        var r = doc.RootElement;
        int Num(string n) => r.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;
        var skipped = r.TryGetProperty("Skipped", out var sk) && sk.ValueKind == JsonValueKind.Object ? sk.EnumerateObject().Sum(x => x.Value.GetInt32()) : 0;
        var patches = r.TryGetProperty("Patches", out var pa) && pa.ValueKind == JsonValueKind.Array ? pa.GetArrayLength() : 0;
        var ok = r.TryGetProperty("Ok", out var o) && o.ValueKind == JsonValueKind.True;
        var error = r.TryGetProperty("Error", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
        return new Run(ok && p.ExitCode == 0, error ?? (p.ExitCode != 0 ? err.ToString().Trim() : null), Num("Made"), skipped, patches);
    }
}
