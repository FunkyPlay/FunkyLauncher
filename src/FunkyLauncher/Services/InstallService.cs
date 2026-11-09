// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Text.RegularExpressions;
using FunkyLauncher.Core;

namespace FunkyLauncher.Services;

public enum InstallOutcome { Success, NeedsManualDownloads, Failed, Cancelled }

public sealed record InstallProgress(string Status, int Downloaded, int Total, string? LogLine);

/// <summary>Installs the modlist with the official wabbajack-cli and reports progress.</summary>
public static partial class InstallService
{
    // wabbajack-cli preflight logs "[Downloaded] <archive>: <message>" and "[ManualRequired] <archive>: ..."
    [GeneratedRegex(@"\[(?<state>Downloaded|ManualRequired|Failed)\]\s+(?<name>[^:]+):")]
    private static partial Regex ArchiveLine();

    public static async Task<(InstallOutcome Outcome, int ExitCode, int Manual)> RunAsync(
        Settings settings, IProgress<InstallProgress> progress, CancellationToken token)
    {
        progress.Report(new InstallProgress(Loc.T("inst.preparing"), 0, 0, null));

        if (WabbajackService.FindCli() == null)
        {
            var dl = new Progress<int>(p => progress.Report(new InstallProgress(Loc.F("inst.wj.download", p), 0, 0, null)));
            await WabbajackService.DownloadAsync(dl, token);
            progress.Report(new InstallProgress(Loc.T("inst.wj.extract"), 0, 0, null));
        }

        var modlist = settings.ResolveModlist() ?? throw new InvalidOperationException(Loc.T("check.modlist.no"));
        var total = WabbajackService.CountArchives(modlist);
        Directory.CreateDirectory(settings.InstallDir);
        Directory.CreateDirectory(settings.DownloadsDir);

        var downloaded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var manual = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var args = $"install -w \"{modlist}\" -o \"{settings.InstallDir}\" -d \"{settings.DownloadsDir}\"";
        int code;
        try
        {
            code = await WabbajackService.RunAsync(args, line =>
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
}
