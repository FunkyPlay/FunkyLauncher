// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Text.RegularExpressions;
using FunkyLauncher.Core;

namespace FunkyLauncher.Services;

public enum InstallOutcome { Success, NeedsManualDownloads, Failed, Cancelled }

public sealed record InstallProgress(string Status, int Downloaded, int Total, string? LogLine);

/// <summary>Installs / verifies a modlist with the official wabbajack-cli and reports progress.</summary>
public static partial class InstallService
{
    // wabbajack-cli preflight logs "[Downloaded] <archive>: <message>" and "[ManualRequired] <archive>: ..."
    [GeneratedRegex(@"\[(?<state>Downloaded|ManualRequired|Failed)\]\s+(?<name>[^:]+):")]
    private static partial Regex ArchiveLine();

    /// <summary>Makes sure Wabbajack and the .wabbajack file are on disk; returns the file path.</summary>
    private static async Task<string> PrepareAsync(Settings settings, Modlist ml, IProgress<InstallProgress> progress, CancellationToken token)
    {
        progress.Report(new InstallProgress(Loc.T("inst.preparing"), 0, 0, null));

        if (WabbajackService.FindCli() == null)
        {
            var dl = new Progress<int>(p => progress.Report(new InstallProgress(Loc.F("inst.wj.download", p), 0, 0, null)));
            await WabbajackService.DownloadAsync(dl, token);
            progress.Report(new InstallProgress(Loc.T("inst.wj.extract"), 0, 0, null));
        }

        var file = settings.ResolveWabbajack(ml);
        if (file == null && !string.IsNullOrEmpty(ml.WabbajackUrl))
        {
            file = Settings.CachedWabbajack(ml);
            var dl = new Progress<int>(p => progress.Report(new InstallProgress(Loc.F("inst.list.download", p), 0, 0, null)));
            await WabbajackService.DownloadFileAsync(ml.WabbajackUrl, file, dl, token);
        }
        return file ?? throw new InvalidOperationException(Loc.T("check.modlist.no"));
    }

    /// <param name="targetDir">Install folder; null = the current one. Updates go into a new folder.</param>
    public static async Task<(InstallOutcome Outcome, int ExitCode, int Manual)> RunAsync(
        Settings settings, Modlist ml, IProgress<InstallProgress> progress, CancellationToken token, string? targetDir = null)
    {
        string file;
        try { file = await PrepareAsync(settings, ml, progress, token); }
        catch (OperationCanceledException) { return (InstallOutcome.Cancelled, -1, 0); }

        // a new player had no Wabbajack during the checks, so the game was not checked yet — do it now
        progress.Report(new InstallProgress(Loc.T("inst.checkgame"), 0, 0, null));
        await SystemCheck.EnsureGameReadyAsync(ml, token);

        var total = WabbajackService.CountArchives(file);
        var installDir = targetDir ?? settings.InstallDir(ml);
        var downloads = settings.DownloadsDir(ml);
        Directory.CreateDirectory(installDir);
        Directory.CreateDirectory(downloads);
        // pin the archives folder so a future update (new install folder) reuses these archives
        if (!settings.DownloadDirs.ContainsKey(ml.Id)) { settings.DownloadDirs[ml.Id] = downloads; settings.Save(); }

        var downloaded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var manual = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        int code;
        try
        {
            code = await WabbajackService.RunAsync($"install -w \"{file}\" -o \"{installDir}\" -d \"{downloads}\"", line =>
            {
                var m = ArchiveLine().Match(line);
                if (m.Success)
                {
                    var name = m.Groups["name"].Value.Trim();
                    if (m.Groups["state"].Value == "Downloaded") downloaded.Add(name);
                    else if (m.Groups["state"].Value == "ManualRequired") manual.Add(name);
                }
                progress.Report(new InstallProgress(Loc.T("inst.running"), downloaded.Count, total, line));
            }, token);
        }
        catch (OperationCanceledException)
        {
            return (InstallOutcome.Cancelled, -1, 0);
        }

        // exit codes from Wabbajack.CLI PreflightInstall: 0 ok, 2 install failed, 3 preflight stopped
        return code switch
        {
            0 => (InstallOutcome.Success, 0, 0),
            3 when manual.Count > 0 => (InstallOutcome.NeedsManualDownloads, 3, manual.Count),
            _ => (InstallOutcome.Failed, code, manual.Count),
        };
    }

    // "... mods\X\file.ext | Hashes do not match ..." (log and console formats differ before the path)
    [GeneratedRegex(@"(?<path>[^\s|\]\)][^|]*?)\s*\|\s*(Hashes do not match|File does not exist|Size does not match)", RegexOptions.IgnoreCase)]
    private static partial Regex VerifyError();

    /// <summary>Files mods rewrite while you play (CET databases, logs) — a changed hash there is normal.</summary>
    private static bool ChangesDuringPlay(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".sqlite3" or ".log" or ".lock";
    }

    /// <summary>
    /// Checks an installed modlist against its .wabbajack file (wabbajack-cli verify-modlist-install).
    /// Returns the number of real problems (files that normally change during play are not counted).
    /// </summary>
    public static async Task<(bool Ok, int Problems, int Ignored)> VerifyAsync(
        Settings settings, Modlist ml, IProgress<InstallProgress> progress, CancellationToken token)
    {
        var file = await PrepareAsync(settings, ml, progress, token);
        progress.Report(new InstallProgress(Loc.T("inst.verifying"), 0, 0, null));
        var problems = 0;
        var ignored = 0;
        var code = await WabbajackService.RunAsync($"verify-modlist-install -m \"{file}\" -i \"{settings.InstallDir(ml)}\"", line =>
        {
            var m = VerifyError().Match(line);
            if (m.Success)
            {
                if (ChangesDuringPlay(m.Groups["path"].Value)) ignored++; else problems++;
            }
            progress.Report(new InstallProgress(Loc.T("inst.verifying"), 0, 0, line));
        }, token);
        // exit code is not 0 when there are errors; if every error is a "changes during play" file, it is fine
        var ok = code == 0 || (problems == 0 && ignored > 0);
        return (ok, problems == 0 && code != 0 && ignored == 0 ? code : problems, ignored);
    }
}
