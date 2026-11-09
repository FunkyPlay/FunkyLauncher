// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Text.RegularExpressions;
using FunkyLauncher.Core;

namespace FunkyLauncher.Services;

public enum CheckState { Ok, Warning, Error }

public sealed record CheckItem(CheckState State, string Title, string? Hint = null);

public sealed class GameInfo
{
    public string Version { get; init; } = "";
    public string Path { get; init; } = "";
    public bool HasPhantomLiberty { get; init; }
}

/// <summary>Everything the launcher checks before installing.</summary>
public static partial class SystemCheck
{
    [GeneratedRegex(@"\[X\]\s+Cyberpunk 2077\s+(?<ver>\S+)\s+->\s+Path:\s+(?<path>.+)$")]
    private static partial Regex GameLine();

    /// <summary>Asks wabbajack-cli which games are installed (Steam, GOG, Epic).</summary>
    public static async Task<GameInfo?> FindGameAsync(CancellationToken token)
    {
        if (WabbajackService.FindCli() == null) return null;
        GameInfo? found = null;
        await WabbajackService.RunAsync("list-games", line =>
        {
            var m = GameLine().Match(line.Trim());
            if (m.Success && found == null)
            {
                var path = m.Groups["path"].Value.Trim();
                found = new GameInfo
                {
                    Version = m.Groups["ver"].Value,
                    Path = path,
                    HasPhantomLiberty = Directory.Exists(System.IO.Path.Combine(path, "archive", "pc", "ep1")),
                };
            }
        }, token);
        return found;
    }

    /// <summary>Folders where installs often break.</summary>
    public static bool IsBadFolder(string dir, string? gamePath)
    {
        if (string.IsNullOrWhiteSpace(dir) || !System.IO.Path.IsPathRooted(dir)) return true;
        var full = System.IO.Path.GetFullPath(dir).TrimEnd('\\') + "\\";
        string[] bad =
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
            Environment.GetEnvironmentVariable("OneDrive") ?? "",
        };
        if (bad.Any(b => !string.IsNullOrEmpty(b) && full.StartsWith(b.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)))
            return true;
        if (!string.IsNullOrEmpty(gamePath) && full.StartsWith(System.IO.Path.GetFullPath(gamePath).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
            return true;
        return full.Length > 80; // long paths break some mods
    }

    public static (long FreeBytes, string Drive) FreeSpace(string dir)
    {
        try
        {
            var root = System.IO.Path.GetPathRoot(System.IO.Path.GetFullPath(dir)) ?? "C:\\";
            var info = new DriveInfo(root);
            return (info.AvailableFreeSpace, root.TrimEnd('\\'));
        }
        catch
        {
            return (0, "?");
        }
    }

    public static async Task<List<CheckItem>> RunAllAsync(Settings settings, CancellationToken token)
    {
        var items = new List<CheckItem>();

        var modlist = settings.ResolveModlist();
        items.Add(modlist != null
            ? new CheckItem(CheckState.Ok, Loc.T("check.modlist.ok"), modlist)
            : new CheckItem(CheckState.Error, Loc.T("check.modlist.no"), Loc.T("check.modlist.hint")));

        var wjVer = WabbajackService.CliVersion();
        items.Add(wjVer != null
            ? new CheckItem(CheckState.Ok, Loc.F("check.wj.ok", wjVer))
            : new CheckItem(CheckState.Warning, Loc.T("check.wj.no")));

        GameInfo? game = null;
        if (wjVer != null)
        {
            try { game = await FindGameAsync(token); } catch (OperationCanceledException) { throw; } catch { }
        }

        if (game == null)
        {
            items.Add(new CheckItem(wjVer == null ? CheckState.Warning : CheckState.Error,
                Loc.T("check.game.no"), Loc.T("check.game.hint")));
        }
        else
        {
            items.Add(new CheckItem(CheckState.Ok, Loc.T("check.game.ok"), game.Path));
            items.Add(game.Version == AppConfig.RequiredGameVersion
                ? new CheckItem(CheckState.Ok, Loc.F("check.version.ok", game.Version))
                : new CheckItem(CheckState.Error, Loc.F("check.version.no", game.Version, AppConfig.RequiredGameVersion), Loc.T("check.version.hint")));
            items.Add(game.HasPhantomLiberty
                ? new CheckItem(CheckState.Ok, Loc.T("check.pl.ok"))
                : new CheckItem(CheckState.Error, Loc.T("check.pl.no"), Loc.T("check.pl.hint")));
        }

        items.Add(IsBadFolder(settings.InstallDir, game?.Path)
            ? new CheckItem(CheckState.Error, Loc.T("check.folder.bad"), Loc.T("check.folder.hint"))
            : new CheckItem(CheckState.Ok, Loc.T("check.folder.ok"), settings.InstallDir));

        var (free, drive) = FreeSpace(settings.InstallDir);
        var gb = free / (1024 * 1024 * 1024);
        items.Add(free >= AppConfig.RequiredFreeBytes || settings.IsInstalled
            ? new CheckItem(CheckState.Ok, Loc.F("check.disk.ok", gb, drive))
            : new CheckItem(CheckState.Error, Loc.F("check.disk.no", gb, drive), Loc.T("check.disk.hint")));

        items.Add(WabbajackService.IsNexusLoggedIn
            ? new CheckItem(CheckState.Ok, Loc.T("check.nexus.ok"))
            : new CheckItem(CheckState.Error, Loc.T("check.nexus.no"), Loc.T("check.nexus.hint")));

        return items;
    }
}
