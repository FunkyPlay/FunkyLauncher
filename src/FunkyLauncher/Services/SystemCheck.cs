// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Text.RegularExpressions;
using FunkyLauncher.Core;

namespace FunkyLauncher.Services;

public enum CheckState { Ok, Warning, Error }

/// <summary>A line of the check list. Tool: a missing required tool (REDmod) — its row gets "How to install" / "Check again".</summary>
/// PickGame: the game was not found and the player can show its folder — red row with "Show the game folder" (0.1.19).
/// Copies: several copies of the game were found — "Pick a copy" (0.1.19).
public sealed record CheckItem(CheckState State, string Title, string? Hint = null, RequiredTool? Tool = null, AdminFlag.Found? Admin = null, bool PickGame = false,
    List<FoundGame>? Copies = null);

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
    /// <summary>Only Latin letters, digits and simple signs, no spaces (the drive part "C:\\" aside).</summary>
    public static bool IsPlainPath(string dir) =>
        dir.All(c => c < 128 && c != ' ');

    /// <summary>The same drive, the modlist's default folder name: "G:\NeonNights".</summary>
    public static string PlainSuggestion(string dir, Modlist ml)
    {
        var root = System.IO.Path.GetPathRoot(dir) is { Length: > 0 } r ? r : "C:\\";
        var name = System.IO.Path.GetFileName(ml.DefaultInstallDir.TrimEnd(System.IO.Path.DirectorySeparatorChar));
        return System.IO.Path.Combine(root, name.Length > 0 ? name : "FunkyPlay");
    }

    public static async Task<GameInfo?> FindGameAsync(Modlist ml, CancellationToken token)
    {
        // the folder the player picked comes first (a second copy elsewhere must not win over the player's choice)
        if (!string.IsNullOrEmpty(ml.GameExe) && GameFolders.Picked(ml) is { } picked && File.Exists(System.IO.Path.Combine(picked, ml.GameExePath)))
            return new GameInfo { Version = GameSearch.VersionOf(ml, System.IO.Path.Combine(picked, ml.GameExePath)), Path = System.IO.Path.GetFullPath(picked).TrimEnd('\\') };
        GameInfo? found = null;
        try { found = await FindGameAsync(ml.Game, token); } catch (OperationCanceledException) { throw; } catch { }
        if (found != null || string.IsNullOrEmpty(ml.GameExe)) return found;
        // 0.1.19: the known places and the usual folders on every drive (by the exe and its version, the right version first)
        var copies = await Task.Run(() => GameSearch.All(ml), token);
        return copies.FirstOrDefault() is { } g ? new GameInfo { Version = g.Version, Path = g.Dir } : null;
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

        // the built-in browser of the manual downloads (free Nexus accounts) and the Nexus login page (0.1.17)
        var web = WebProfile.RuntimeVersion();
        items.Add(web != null
            ? new CheckItem(CheckState.Ok, Loc.F("check.webview.ok", web))
            : new CheckItem(CheckState.Warning, Loc.T("check.webview.no"), Loc.T("check.webview.hint")));

        GameInfo? game = null;
        if (wjVer != null || nexusList)
        {
            try { game = await FindGameAsync(ml, token); } catch (OperationCanceledException) { throw; } catch { }
        }

        if (game == null && nexusList)
        {
            // the launcher looks for the game itself; if it can't, the player shows the folder. 0.1.19: a red row with its own
            // button, first in the list (players didn't see the small "Game folder…" button, 21.11)
            items.Insert(0, new CheckItem(CheckState.Error, Loc.F("check.game.no", ml.Game), Loc.F("check.game.pick", ml.GameExe), PickGame: true));
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
            // 0.1.19: several copies on this PC and the player has not picked one — say which is used, offer to pick
            if (GameFolders.Picked(ml) == null && await Task.Run(() => GameSearch.All(ml), token) is { Count: > 1 } copies)
                items.Add(new CheckItem(CheckState.Warning, Loc.F("check.game.copies", copies.Count, ml.Game), Loc.F("check.game.copies.hint", game.Path, game.Version),
                    Copies: copies));
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
            // 0.1.19: key game files (only on this PC). Missing — a row; all matched — a green row; a differing build — only the log
            if (GameFiles.Has(ml))
            {
                var gf = await Task.Run(() => GameFiles.Check(ml, game.Path), token);
                if (gf.Missing.Count > 0)
                    items.Add(new CheckItem(CheckState.Warning, Loc.F("check.files.missing", GameFiles.Names(gf.Missing)), Loc.F("check.files.missing.hint", GameFiles.Names(gf.Missing))));
                else if (gf.AllVerified)
                    items.Add(new CheckItem(CheckState.Ok, Loc.F("check.files.ok", ml.GameVersion)));
            }
            var missing = MissingTools(ml, game.Path);
            foreach (var t in ml.RequiredTools ?? new())
                items.Add(missing.Contains(t)
                    ? new CheckItem(CheckState.Error, Loc.F("check.tool.no", t.Name), ToolHint(t), t)
                    : new CheckItem(CheckState.Ok, Loc.F("check.tool.ok", t.Name)));
            // 0.1.18: "Run as administrator" on the game exe → MO2 can't start it (error 740). A warning, not an error:
            // installing works, only PLAY doesn't — and PLAY asks again
            if (AdminFlag.Check(ml, game.Path) is { } flag)
                items.Add(new CheckItem(CheckState.Warning, Loc.F("admin.flag.title", ml.GameExe),
                    Loc.T("admin.flag.hint") + "\n" + AdminFlag.Steps(flag), Admin: flag));
        }

        items.Add(IsGameFolder(installDir, ml, game?.Path)
            ? new CheckItem(CheckState.Error, Loc.T("check.folder.game"), Loc.F("check.folder.game.hint", ml.Game, ml.DefaultInstallDir))
            : IsBadFolder(installDir, game?.Path)
            ? new CheckItem(CheckState.Error, Loc.T("check.folder.bad"), Loc.T("check.folder.hint"))
            : new CheckItem(CheckState.Ok, Loc.T("check.folder.ok"), installDir));
        // 0.1.23 (support #249, chat Cyberpunk, Funky yes): MO2 and some mod tools trip over non-Latin letters and spaces
        // in the modlist path — a warning with a plain path to use instead (install is not blocked)
        if (!IsGameFolder(installDir, ml, game?.Path) && !IsPlainPath(installDir))
            items.Add(new CheckItem(CheckState.Warning, Loc.T("check.folder.chars"),
                Loc.F("check.folder.chars.hint", PlainSuggestion(installDir, ml))));

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
