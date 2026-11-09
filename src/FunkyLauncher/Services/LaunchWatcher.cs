// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Diagnostics;
using FunkyLauncher.Core;

namespace FunkyLauncher.Services;

public enum LaunchResult { Ok, NotStarted, ClosedEarly, Crashed }

public sealed record LaunchReport(LaunchResult Result, TimeSpan RunTime, string? CrashFile);

/// <summary>
/// After PLAY: waits for the game process, then for it to close, and decides whether it was a normal
/// exit or a problem (never started, closed right away, or left a crash report).
/// </summary>
public static class LaunchWatcher
{
    private static readonly TimeSpan StartTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan EarlyExit = TimeSpan.FromSeconds(60);

    public static async Task<LaunchReport> WatchAsync(Settings settings, Modlist ml, CancellationToken token)
    {
        var launched = DateTime.Now;
        var name = Path.GetFileNameWithoutExtension(ml.GameExe);
        if (string.IsNullOrEmpty(name)) return new LaunchReport(LaunchResult.Ok, TimeSpan.Zero, null);

        // 1) wait for the game to appear
        Process? game = null;
        while (game == null)
        {
            game = Process.GetProcessesByName(name).FirstOrDefault();
            if (game != null) break;
            if (DateTime.Now - launched > StartTimeout)
                return new LaunchReport(LaunchResult.NotStarted, TimeSpan.Zero, FindCrash(settings, ml, launched));
            await Task.Delay(2000, token);
        }

        // 2) wait for it to close
        var started = DateTime.Now;
        while (Process.GetProcessesByName(name).Length > 0)
            await Task.Delay(3000, token);
        var runTime = DateTime.Now - started;

        // 3) normal exit or a problem?
        await Task.Delay(3000, token); // crash reporters write their files right after the exit
        var crash = FindCrash(settings, ml, launched);
        if (crash != null) return new LaunchReport(LaunchResult.Crashed, runTime, crash);
        if (runTime < EarlyExit) return new LaunchReport(LaunchResult.ClosedEarly, runTime, null);
        return new LaunchReport(LaunchResult.Ok, runTime, null);
    }

    /// <summary>A crash report written after the launch: mod crash handlers in overwrite, or the game's own crash reporter.</summary>
    private static string? FindCrash(Settings settings, Modlist ml, DateTime since)
    {
        var places = new[]
        {
            (Path.Combine(settings.InstallDir(ml), "overwrite"), "crash-*.txt", true),
            (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "REDEngine", "ReportQueue"), "*", true),
        };
        foreach (var (dir, pattern, recursive) in places)
        {
            if (!Directory.Exists(dir)) continue;
            try
            {
                var f = new DirectoryInfo(dir)
                    .EnumerateFiles(pattern, recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                    .Where(x => x.LastWriteTime > since)
                    .OrderByDescending(x => x.LastWriteTime)
                    .FirstOrDefault();
                if (f != null) return f.FullName;
            }
            catch { /* folder busy — skip */ }
        }
        return null;
    }

    /// <summary>"2.5" vs "2.5.0" are the same; "2.6" is not.</summary>
    public static bool SameGameVersion(string installed, string required)
    {
        if (string.IsNullOrWhiteSpace(required)) return true;
        if (Version.TryParse(Pad(installed), out var a) && Version.TryParse(Pad(required), out var b))
            return a.Major == b.Major && a.Minor == b.Minor && (b.Build <= 0 || a.Build == b.Build);
        return installed.Trim() == required.Trim();
    }

    private static string Pad(string v) => v.Count(c => c == '.') == 0 ? v + ".0" : v;
}
