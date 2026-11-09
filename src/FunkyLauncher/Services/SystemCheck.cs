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
}

/// <summary>Everything the launcher checks before installing.</summary>
public static class SystemCheck
{
    /// <summary>Asks wabbajack-cli which games are installed (Steam, GOG, Epic).</summary>
    public static async Task<GameInfo?> FindGameAsync(string game, CancellationToken token)
    {
        if (WabbajackService.FindCli() == null) return null;
        // line: "[X] Cyberpunk 2077 2.5 -> Path: C:\..." (X = installed)
        var rx = new Regex(@"\[X\]\s+" + Regex.Escape(game) + @"\s+(?<ver>\S+)\s+->\s+Path:\s+(?<path>.+)$");
        GameInfo? found = null;
        await WabbajackService.RunAsync("list-games", line =>
        {
            var m = rx.Match(line.Trim());
            if (m.Success && found == null)
                found = new GameInfo { Version = m.Groups["ver"].Value, Path = m.Groups["path"].Value.Trim() };
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

    public static async Task<List<CheckItem>> RunAllAsync(Settings settings, Modlist ml, CancellationToken token)
    {
        var items = new List<CheckItem>();
        var installDir = settings.InstallDir(ml);
        var installed = settings.IsInstalled(ml);

        var file = settings.ResolveWabbajack(ml);
        if (file != null) items.Add(new CheckItem(CheckState.Ok, Loc.T("check.modlist.ok"), file));
        else if (!string.IsNullOrEmpty(ml.WabbajackUrl)) items.Add(new CheckItem(CheckState.Warning, Loc.T("check.modlist.download")));
        else items.Add(new CheckItem(CheckState.Error, Loc.T("check.modlist.no"), Loc.T("check.modlist.hint")));

        var wjVer = WabbajackService.CliVersion();
        items.Add(wjVer != null
            ? new CheckItem(CheckState.Ok, Loc.F("check.wj.ok", wjVer))
            : new CheckItem(CheckState.Warning, Loc.T("check.wj.no")));

        GameInfo? game = null;
        if (wjVer != null)
        {
            try { game = await FindGameAsync(ml.Game, token); } catch (OperationCanceledException) { throw; } catch { }
        }

        if (game == null && wjVer == null)
        {
            // the game is found through wabbajack-cli; a new player gets it with the first step of INSTALL
            items.Add(new CheckItem(CheckState.Warning, Loc.F("check.game.later", ml.Game), Loc.T("check.game.later.hint")));
        }
        else if (game == null)
        {
            items.Add(new CheckItem(CheckState.Error, Loc.F("check.game.no", ml.Game), Loc.T("check.game.hint")));
        }
        else
        {
            items.Add(new CheckItem(CheckState.Ok, Loc.F("check.game.ok", ml.Game), game.Path));
            if (!string.IsNullOrEmpty(ml.GameVersion))
                items.Add(LaunchWatcher.SameGameVersion(game.Version, ml.GameVersion)
                    ? new CheckItem(CheckState.Ok, Loc.F("check.version.ok", game.Version))
                    : new CheckItem(CheckState.Error, Loc.F("check.version.no", game.Version, ml.GameVersion), Loc.T("check.version.hint")));
            if (!string.IsNullOrEmpty(ml.DlcCheckPath))
            {
                var dlc = ml.DlcName?.ToString() ?? "DLC";
                items.Add(Directory.Exists(System.IO.Path.Combine(game.Path, ml.DlcCheckPath))
                    ? new CheckItem(CheckState.Ok, Loc.F("check.dlc.ok", dlc))
                    : new CheckItem(CheckState.Error, Loc.F("check.dlc.no", dlc), Loc.T("check.dlc.hint")));
            }
        }

        items.Add(IsBadFolder(installDir, game?.Path)
            ? new CheckItem(CheckState.Error, Loc.T("check.folder.bad"), Loc.T("check.folder.hint"))
            : new CheckItem(CheckState.Ok, Loc.T("check.folder.ok"), installDir));

        var (free, drive) = FreeSpace(installDir);
        var gb = free / (1024 * 1024 * 1024);
        items.Add(free >= ml.RequiredFreeBytes || installed
            ? new CheckItem(CheckState.Ok, Loc.F("check.disk.ok", gb, drive))
            : new CheckItem(CheckState.Error, Loc.F("check.disk.no", gb, drive, ml.SizeGb + 5), Loc.T("check.disk.hint")));

        items.Add(WabbajackService.IsNexusLoggedIn
            ? new CheckItem(CheckState.Ok, Loc.T("check.nexus.ok"))
            : new CheckItem(CheckState.Error, Loc.T("check.nexus.no"), Loc.T("check.nexus.hint")));

        return items;
    }

    /// <summary>
    /// Before installing (Wabbajack is on disk by now): the game, its version and the DLC must be there.
    /// Throws GameCheckException with a message for the player.
    /// </summary>
    public static async Task EnsureGameReadyAsync(Modlist ml, CancellationToken token)
    {
        var game = await FindGameAsync(ml.Game, token);
        if (game == null)
            throw new GameCheckException(Loc.F("check.game.no", ml.Game) + ". " + Loc.T("check.game.hint"));
        if (!string.IsNullOrEmpty(ml.GameVersion) && !LaunchWatcher.SameGameVersion(game.Version, ml.GameVersion))
            throw new GameCheckException(Loc.F("check.version.no", game.Version, ml.GameVersion) + ". " + Loc.T("check.version.hint"));
        if (!string.IsNullOrEmpty(ml.DlcCheckPath) && !Directory.Exists(System.IO.Path.Combine(game.Path, ml.DlcCheckPath)))
        {
            var dlc = ml.DlcName?.ToString() ?? "DLC";
            throw new GameCheckException(Loc.F("check.dlc.no", dlc) + ". " + Loc.T("check.dlc.hint"));
        }
    }
}

/// <summary>The game is missing / wrong version / no DLC — shown to the player as is.</summary>
public sealed class GameCheckException(string message) : Exception(message);
