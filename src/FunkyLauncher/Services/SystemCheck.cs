// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Text.RegularExpressions;
using FunkyLauncher.Core;

namespace FunkyLauncher.Services;

public enum CheckState { Ok, Warning, Error }

/// <summary>A line of the check list. Tool: a missing required tool (REDmod) — its row gets "How to install" / "Check again".</summary>
public sealed record CheckItem(CheckState State, string Title, string? Hint = null, RequiredTool? Tool = null);

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

    /// <summary>
    /// The game of a modlist: Wabbajack's store detection first; then (games Wabbajack does not see, e.g. a
    /// Starfield copy outside Steam) the folder the player picked, the Steam libraries and the catalog's folder hints.
    /// The version is the game exe's file version.
    /// </summary>
    public static async Task<GameInfo?> FindGameAsync(Modlist ml, CancellationToken token)
    {
        GameInfo? found = null;
        try { found = await FindGameAsync(ml.Game, token); } catch (OperationCanceledException) { throw; } catch { }
        if (found != null || string.IsNullOrEmpty(ml.GameExe)) return found;
        foreach (var dir in GameFolders.Candidates(ml))
        {
            var exe = System.IO.Path.Combine(dir, ml.GameExePath);
            if (!File.Exists(exe)) continue;
            var v = System.Diagnostics.FileVersionInfo.GetVersionInfo(exe);
            var ver = $"{v.FileMajorPart}.{v.FileMinorPart}.{v.FileBuildPart}";
            // Cyberpunk: the file version is 3.0.x, the game version ("2.5") is the product version
            if (!LaunchWatcher.SameGameVersion(ver, ml.GameVersion) && v.ProductVersion is { Length: > 0 } pv
                && LaunchWatcher.SameGameVersion(pv.Trim(), ml.GameVersion))
                ver = pv.Trim();
            return new GameInfo { Version = ver, Path = System.IO.Path.GetFullPath(dir).TrimEnd('\\') };
        }
        return null;
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

    /// <summary>
    /// The folder is the game itself, or the game sits inside it (e.g. "F:\Games" with the game in "F:\Games\Cyberpunk 2077").
    /// Installing there can overwrite or delete the game's files — the launcher never allows it, even if the game was not found.
    /// </summary>
    public static bool IsGameFolder(string dir, Modlist ml, string? gamePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(dir) || !System.IO.Path.IsPathRooted(dir)) return false;
            var full = System.IO.Path.GetFullPath(dir).TrimEnd('\\');
            if (!string.IsNullOrEmpty(gamePath))
            {
                var game = System.IO.Path.GetFullPath(gamePath).TrimEnd('\\');
                if ((game + "\\").StartsWith(full + "\\", StringComparison.OrdinalIgnoreCase)) return true;
            }
            if (!Directory.Exists(full) || string.IsNullOrEmpty(ml.GameExe)) return false;
            // the game's own files: the exe in the root or in bin\x64 (Cyberpunk), Cyberpunk's archives
            return File.Exists(System.IO.Path.Combine(full, ml.GameExe))
                || File.Exists(System.IO.Path.Combine(full, "bin", "x64", ml.GameExe))
                || Directory.Exists(System.IO.Path.Combine(full, "archive", "pc", "content"));
        }
        catch { return false; }
    }

    /// <summary>
    /// The DLC proof is a folder (Cyberpunk: archive/pc/ep1) or a file (Starfield: Data/ShatteredSpace.esm);
    /// several separated by ";" must all be there (Fallout 4: all six DLC).
    /// </summary>
    private static bool DlcPresent(string gamePath, string rel) =>
        rel.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).All(r =>
        {
            var p = System.IO.Path.Combine(gamePath, r);
            return Directory.Exists(p) || File.Exists(p);
        });

    /// <summary>Required game tools (REDmod…) missing from the game folder.</summary>
    public static List<RequiredTool> MissingTools(Modlist ml, string gamePath) =>
        (ml.RequiredTools ?? new()).Where(t => t.Check.Length > 0 && !DlcPresent(gamePath, t.Check)).ToList();

    public static string ToolHint(RequiredTool t) => t.Hint?.ToString() is { Length: > 0 } h ? h : Loc.F("check.tool.hint", t.Name);

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

        var nexusList = Mo2Profile.IsNexusList(ml);
        if (nexusList)
        {
            var main = Addons.Base(ml.Id);
            items.Add(main != null
                ? new CheckItem(CheckState.Ok, Loc.F("check.list.ok", main.Mods.Count))
                : new CheckItem(CheckState.Error, Loc.T("check.list.no"), Loc.T("check.modlist.hint")));
        }
        else
        {
            var file = settings.ResolveWabbajack(ml);
            if (file != null) items.Add(new CheckItem(CheckState.Ok, Loc.T("check.modlist.ok"), file));
            else if (!string.IsNullOrEmpty(ml.WabbajackUrl)) items.Add(new CheckItem(CheckState.Warning, Loc.T("check.modlist.download")));
            else items.Add(new CheckItem(CheckState.Error, Loc.T("check.modlist.no"), Loc.T("check.modlist.hint")));
        }

        var wjVer = WabbajackService.CliVersion();
        items.Add(wjVer != null
            ? new CheckItem(CheckState.Ok, Loc.F("check.wj.ok", wjVer))
            : new CheckItem(CheckState.Warning, Loc.T("check.wj.no")));

        GameInfo? game = null;
        if (wjVer != null || nexusList)
        {
            try { game = await FindGameAsync(ml, token); } catch (OperationCanceledException) { throw; } catch { }
        }

        if (game == null && nexusList)
        {
            // the launcher looks for the game itself; if it can't, the player shows the folder ("Game folder…")
            items.Add(new CheckItem(CheckState.Error, Loc.F("check.game.no", ml.Game), Loc.T("check.game.pick")));
        }
        else if (game == null && wjVer == null)
        {
            // the game is found through wabbajack-cli; a new player gets it with the first step of INSTALL
            items.Add(new CheckItem(CheckState.Warning, Loc.F("check.game.later", ml.Game), Loc.T("check.game.later.hint")));
        }
        else if (game == null)
        {
            items.Add(new CheckItem(CheckState.Error, Loc.F("check.game.no", ml.Game), Loc.T("check.game.hint.wj")));
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
                items.Add(DlcPresent(game.Path, ml.DlcCheckPath)
                    ? new CheckItem(CheckState.Ok, Loc.F("check.dlc.ok", dlc))
                    : new CheckItem(CheckState.Error, Loc.F("check.dlc.no", dlc), Loc.T("check.dlc.hint")));
            }
            var missing = MissingTools(ml, game.Path);
            foreach (var t in ml.RequiredTools ?? new())
                items.Add(missing.Contains(t)
                    ? new CheckItem(CheckState.Error, Loc.F("check.tool.no", t.Name), ToolHint(t), t)
                    : new CheckItem(CheckState.Ok, Loc.F("check.tool.ok", t.Name)));
        }

        items.Add(IsGameFolder(installDir, ml, game?.Path)
            ? new CheckItem(CheckState.Error, Loc.T("check.folder.game"), Loc.F("check.folder.game.hint", ml.Game, ml.DefaultInstallDir))
            : IsBadFolder(installDir, game?.Path)
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

        // honest note on how downloading works (wording checked by the legal chat): both ways, same weight, no sales pitch
        if (!installed)
        {
            var files = nexusList ? Addons.Base(ml.Id)?.Mods.Count ?? ml.ModCount : ml.ModCount;
            items.Add(new CheckItem(CheckState.Warning, Loc.T("check.download.how"), Loc.F("check.download.hint", files)));
        }

        return items;
    }

    /// <summary>
    /// Before installing (Wabbajack is on disk by now): the game, its version and the DLC must be there.
    /// Throws GameCheckException with a message for the player.
    /// </summary>
    public static async Task EnsureGameReadyAsync(Modlist ml, CancellationToken token)
    {
        await EnsureGameReadyAndGetAsync(ml, token);
    }

    /// <summary>Same as <see cref="EnsureGameReadyAsync"/>, returns the game found.</summary>
    public static async Task<GameInfo> EnsureGameReadyAndGetAsync(Modlist ml, CancellationToken token)
    {
        var game = await FindGameAsync(ml, token);
        // never install into the game itself (or a folder that holds it): it can overwrite or delete the game's files
        if (Modlist.InstallDirOf?.Invoke(ml) is { Length: > 0 } dir && IsGameFolder(dir, ml, game?.Path))
            throw new GameCheckException(Loc.T("check.folder.game") + ". " + Loc.F("check.folder.game.hint", ml.Game, ml.DefaultInstallDir));
        if (game == null)
            throw new GameCheckException(Loc.F("check.game.no", ml.Game) + ". " + Loc.T("check.game.hint"));
        if (!string.IsNullOrEmpty(ml.GameVersion) && !LaunchWatcher.SameGameVersion(game.Version, ml.GameVersion))
            throw new GameCheckException(Loc.F("check.version.no", game.Version, ml.GameVersion) + ". " + Loc.T("check.version.hint"));
        if (!string.IsNullOrEmpty(ml.DlcCheckPath) && !DlcPresent(game.Path, ml.DlcCheckPath))
        {
            var dlc = ml.DlcName?.ToString() ?? "DLC";
            throw new GameCheckException(Loc.F("check.dlc.no", dlc) + ". " + Loc.T("check.dlc.hint"));
        }
        if (MissingTools(ml, game.Path).FirstOrDefault() is { } tool)
            throw new GameCheckException(Loc.F("check.tool.no", tool.Name) + ". " + ToolHint(tool));
        return game;
    }
}

/// <summary>The game is missing / wrong version / no DLC — shown to the player as is.</summary>
public sealed class GameCheckException(string message) : Exception(message);
