// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.IO.Compression;
using System.Text;
using FunkyLauncher.Core;

namespace FunkyLauncher.Services;

/// <summary>
/// "Make an error report": collects launcher, Wabbajack, MO2 and game mod logs into one zip on the
/// Desktop. Nothing is sent anywhere — the player sends the file to support themselves.
/// </summary>
public static class ReportService
{
    private const long MaxFile = 5L * 1024 * 1024;

    public static async Task<string> CreateAsync(Settings settings, Modlist ml, CancellationToken token)
    {
        var name = $"FunkyLauncher-report-{DateTime.Now:yyyyMMdd-HHmm}.zip";
        var target = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), name);
        var game = await SystemCheck.FindGameAsync(ml.Game, token);

        var info = new StringBuilder()
            .AppendLine($"Funky Launcher {AppConfig.Version}")
            .AppendLine($"Windows: {Environment.OSVersion.VersionString}")
            .AppendLine($"Date: {DateTime.Now:yyyy-MM-dd HH:mm}")
            .AppendLine($"Modlist: {ml.Title} {ml.Version} (needs game {ml.GameVersion})")
            .AppendLine($"Install dir: {settings.InstallDir(ml)} (installed: {settings.IsInstalled(ml)})")
            .AppendLine($"Game: {game?.Version ?? "not found"} {game?.Path}")
            .AppendLine($"Wabbajack: {WabbajackService.CliVersion() ?? "not found"}")
            .AppendLine($"Nexus login: {WabbajackService.IsNexusLoggedIn}");

        await Task.Run(() =>
        {
            using var zip = ZipFile.Open(target, ZipArchiveMode.Create);
            var e = zip.CreateEntry("info.txt");
            using (var w = new StreamWriter(e.Open())) w.Write(info.ToString());

            Add(zip, "launcher", Path.Combine(AppConfig.DataDir, "settings.json"));
            var cli = WabbajackService.FindCli();
            if (cli != null) AddNewest(zip, "wabbajack", Path.Combine(Path.GetDirectoryName(cli)!, "logs"), "*.log", 3, false);

            var dir = settings.InstallDir(ml);
            AddNewest(zip, "mo2", Path.Combine(dir, "logs"), "*.log", 5, false);
            AddNewest(zip, "mods-logs", Path.Combine(dir, "overwrite"), "*.log", 30, true);
            AddNewest(zip, "crashes", Path.Combine(dir, "overwrite"), "crash-*.txt", 3, true);

            if (game != null)
            {
                AddNewest(zip, "game-cet", Path.Combine(game.Path, "bin", "x64", "plugins", "cyber_engine_tweaks"), "*.log", 10, false);
                AddNewest(zip, "game-red4ext", Path.Combine(game.Path, "red4ext", "logs"), "*.log", 5, false);
            }
        }, token);
        return target;
    }

    private static void AddNewest(ZipArchive zip, string folder, string dir, string pattern, int count, bool recursive)
    {
        if (!Directory.Exists(dir)) return;
        var files = new DirectoryInfo(dir)
            .EnumerateFiles(pattern, recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
            .OrderByDescending(f => f.LastWriteTime).Take(count);
        foreach (var f in files)
            Add(zip, folder + "/" + Path.GetRelativePath(dir, f.DirectoryName!).Replace('\\', '/').Replace("./", "").TrimEnd('.'), f.FullName);
    }

    private static void Add(ZipArchive zip, string folder, string file)
    {
        try
        {
            var fi = new FileInfo(file);
            if (!fi.Exists || fi.Length > MaxFile) return;
            var entry = zip.CreateEntry((folder.TrimEnd('/') + "/" + fi.Name).Replace("//", "/"));
            // FileShare.ReadWrite: logs may still be open by the game or MO2
            using var src = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var dst = entry.Open();
            src.CopyTo(dst);
        }
        catch { /* a locked or vanished log is not worth failing the report */ }
    }
}
