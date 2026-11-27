// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Diagnostics;
using System.Security.Cryptography;
using FunkyLauncher.Core;

namespace FunkyLauncher.Services;

/// <summary>
/// The player engine (mpv for playback, FFmpeg for cutting) is downloaded on first use, not shipped with the
/// launcher (~66 MB download, ~230 MB on disk). Every file is checked against its SHA-256 from the catalog.
/// Sources: 7-Zip's own site (7zr.exe), the mpv Windows builds linked from mpv.io, the FFmpeg builds linked from ffmpeg.org.
/// </summary>
public static class PlayerEngine
{
    public static string Dir => Path.Combine(AppConfig.DataDir, "player");
    public static string Mpv => Path.Combine(Dir, "libmpv-2.dll");
    public static string Ffmpeg => Path.Combine(Dir, "ffmpeg.exe");
    private static string Marker => Path.Combine(Dir, "engine.txt");

    public static bool Ready => File.Exists(Mpv) && File.Exists(Ffmpeg) && File.Exists(Marker);

    public static string? InstalledVersion => File.Exists(Marker) ? File.ReadAllText(Marker).Trim() : null;

    /// <summary>Downloads, checks and unpacks the engine. Progress: (step text, percent of the current file).</summary>
    public static async Task EnsureAsync(IProgress<(string Step, int Percent)> progress, CancellationToken ct)
    {
        if (Ready) return;
        var src = Catalog.PlayerEngine ?? throw new InvalidOperationException(Loc.T("player.err.nosource"));
        Directory.CreateDirectory(Dir);
        var tmp = Path.Combine(Dir, "download");
        Directory.CreateDirectory(tmp);

        var seven = Path.Combine(tmp, "7zr.exe");
        await GetAsync(src.SevenZip, seven, "7-Zip", progress, ct);
        var mpv7z = Path.Combine(tmp, "mpv.7z");
        await GetAsync(src.Mpv, mpv7z, "mpv", progress, ct);
        var ff7z = Path.Combine(tmp, "ffmpeg.7z");
        await GetAsync(src.Ffmpeg, ff7z, "FFmpeg", progress, ct);

        progress.Report((Loc.T("player.unpacking"), 100));
        await ExtractAsync(seven, mpv7z, src.Mpv.Extract, ct);
        await ExtractAsync(seven, ff7z, src.Ffmpeg.Extract, ct);
        if (!File.Exists(Mpv) || !File.Exists(Ffmpeg)) throw new InvalidOperationException(Loc.T("player.err.unpack"));

        File.WriteAllText(Marker, src.Version);
        try { Directory.Delete(tmp, true); } catch { /* only our own downloads; next time is fine */ }
    }

    private static async Task GetAsync(EngineFile f, string target, string name, IProgress<(string, int)> progress, CancellationToken ct)
    {
        if (File.Exists(target) && Hash(target) == f.Sha256.ToLowerInvariant()) return; // already downloaded earlier
        var p = new Progress<int>(pct => progress.Report((Loc.F("player.downloading", name), pct)));
        await WabbajackService.DownloadFileAsync(f.Url, target, p, ct);
        if (Hash(target) != f.Sha256.ToLowerInvariant())
        {
            File.Delete(target);
            throw new InvalidOperationException(Loc.F("player.err.hash", name));
        }
    }

    private static string Hash(string file)
    {
        using var s = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(s)).ToLowerInvariant();
    }

    private static async Task ExtractAsync(string seven, string archive, string pattern, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(seven) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in new[] { "e", archive, "-o" + Dir, pattern, "-y" }) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        _ = p.StandardOutput.ReadToEndAsync(ct);
        var err = await p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        if (p.ExitCode != 0) throw new InvalidOperationException("7-Zip: " + err.Trim());
    }
}
