// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Diagnostics;
using System.Text.Json;
using FunkyLauncher.Core;
using Microsoft.Win32;

namespace FunkyLauncher.Services;

/// <summary>A copy of the game found on this PC: its folder and the version of its exe.</summary>
public sealed record FoundGame(string Dir, string Version);

/// <summary>
/// 0.1.19 automatic game search (Funky 21.11: "variant 2 is needed too"). The game is recognised by its exe and the
/// exe's version — never by where it came from. Places: the folders other game apps record (uninstall entries, their
/// install manifests and registry keys), then the usual folders on every fixed drive ("Games", "Игры", Program Files…)
/// up to three levels deep — with a time limit, never the whole drive. Several copies — the player picks one.
/// </summary>
public static class GameSearch
{
    private static readonly Dictionary<string, List<FoundGame>> Cache = new();
    private static readonly string[] UsualRoots =
    {
        "Games", "Игры", "Program Files", "Program Files (x86)", "SteamLibrary", "GOG Games", "Epic Games", "XboxGames",
        @"Program Files\Epic Games", @"Program Files (x86)\Steam\steamapps\common", @"Program Files (x86)\GOG Galaxy\Games",
    };
    private static readonly HashSet<string> Skip = new(StringComparer.OrdinalIgnoreCase)
    { "Windows", "$Recycle.Bin", "System Volume Information", "ProgramData", "Users", "Recovery", "PerfLogs", "WindowsApps", "Common Files" };

    /// <summary>The version of the game exe: the file version; for Cyberpunk the product version ("2.5") when that is the one the modlist names.</summary>
    public static string VersionOf(Modlist ml, string exe)
    {
        var v = FileVersionInfo.GetVersionInfo(exe);
        var ver = $"{v.FileMajorPart}.{v.FileMinorPart}.{v.FileBuildPart}";
        if (!LaunchWatcher.SameGameVersion(ver, ml.GameVersion) && v.ProductVersion is { Length: > 0 } pv
            && LaunchWatcher.SameGameVersion(pv.Trim(), ml.GameVersion))
            ver = pv.Trim();
        return ver;
    }

    private static FoundGame? At(Modlist ml, string dir)
    {
        try
        {
            var exe = Path.Combine(dir, ml.GameExePath);
            return File.Exists(exe) ? new FoundGame(Path.GetFullPath(dir).TrimEnd('\\'), VersionOf(ml, exe)) : null;
        }
        catch { return null; }
    }

    /// <summary>Every copy of the game found (the right version first). Searched once per launcher run; <paramref name="again"/> searches anew.</summary>
    public static List<FoundGame> All(Modlist ml, bool again = false, TimeSpan? budget = null)
    {
        if (string.IsNullOrEmpty(ml.GameExe)) return new();
        lock (Cache)
            if (!again && Cache.TryGetValue(ml.Id, out var hit)) return hit;
        var sw = Stopwatch.StartNew();
        var limit = budget ?? TimeSpan.FromSeconds(8);
        var found = new Dictionary<string, FoundGame>(StringComparer.OrdinalIgnoreCase);
        void Add(string? dir)
        {
            if (string.IsNullOrWhiteSpace(dir)) return;
            try { dir = Path.GetFullPath(dir.Trim().Trim('"')).TrimEnd('\\'); } catch { return; }
            if (!found.ContainsKey(dir) && At(ml, dir) is { } g) found[dir] = g;
        }
        // 1) quick: the folders the launcher already knows and the ones other game apps record
        foreach (var d in GameFolders.Candidates(ml)) Add(d);
        foreach (var d in RecordedFolders()) { Add(d); if (FindBelowQuick(ml, d) is { } b) Add(b); }
        // 2) the usual folders on every fixed drive, up to three levels deep, within the time limit
        foreach (var drive in FixedDrives())
            foreach (var root in UsualRoots.Select(r => Path.Combine(drive, r)).Prepend(drive))
            {
                if (sw.Elapsed > limit) break;
                if (Directory.Exists(root)) Walk(ml, root, root == drive ? 1 : 3, sw, limit, Add);
            }
        var list = found.Values
            .OrderByDescending(g => LaunchWatcher.SameGameVersion(g.Version, ml.GameVersion))
            .ThenBy(g => g.Dir, StringComparer.OrdinalIgnoreCase).ToList();
        AppLog.Write($"game search {ml.Id}: {list.Count} found in {sw.Elapsed.TotalSeconds:0.0} s" + string.Concat(list.Select(g => $"; {g.Dir} ({g.Version})")));
        lock (Cache) Cache[ml.Id] = list;
        return list;
    }

    private static IEnumerable<string> FixedDrives()
    {
        foreach (var d in DriveInfo.GetDrives())
        {
            bool ok;
            try { ok = d.DriveType == DriveType.Fixed && d.IsReady; } catch { ok = false; }
            if (ok) yield return d.RootDirectory.FullName;
        }
    }

    /// <summary>Depth-limited walk: a folder is a hit when the game exe is where the modlist expects it (e.g. bin\x64).</summary>
    private static void Walk(Modlist ml, string root, int depth, Stopwatch sw, TimeSpan limit, Action<string> add)
    {
        var level = new List<string> { root };
        for (var d = 0; d <= depth && level.Count > 0; d++)
        {
            var next = new List<string>();
            foreach (var dir in level)
            {
                if (sw.Elapsed > limit) return;
                add(dir);
                if (d == depth) continue;
                try
                {
                    foreach (var sub in Directory.EnumerateDirectories(dir))
                    {
                        var name = Path.GetFileName(sub);
                        if (Skip.Contains(name) || name.StartsWith('.')) continue;
                        try { if ((File.GetAttributes(sub) & FileAttributes.ReparsePoint) != 0) continue; } catch { continue; }
                        next.Add(sub);
                    }
                }
                catch { /* no access */ }
            }
            level = next;
        }
    }

    private static string? FindBelowQuick(Modlist ml, string dir) => Directory.Exists(dir) ? GameFolders.FindBelow(ml, dir) : null;

    /// <summary>Install folders other apps record: uninstall entries (this user and all users) and game apps' own records.</summary>
    private static IEnumerable<string> RecordedFolders()
    {
        var dirs = new List<string>();
        void FromKey(RegistryKey root, string path, string value)
        {
            try
            {
                using var k = root.OpenSubKey(path);
                if (k == null) return;
                foreach (var name in k.GetSubKeyNames())
                    try { using var s = k.OpenSubKey(name); if (s?.GetValue(value) is string v && v.Length > 2) dirs.Add(v); } catch { }
            }
            catch { }
        }
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
                try
                {
                    using var root = RegistryKey.OpenBaseKey(hive, view);
                    FromKey(root, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", "InstallLocation");
                    FromKey(root, @"SOFTWARE\GOG.com\Games", "path");
                }
                catch { }
        // a game app's install manifests (JSON files with "InstallLocation")
        try
        {
            var manifests = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests");
            if (Directory.Exists(manifests))
                foreach (var f in Directory.EnumerateFiles(manifests, "*.item"))
                    try
                    {
                        using var doc = JsonDocument.Parse(File.ReadAllText(f));
                        if (doc.RootElement.TryGetProperty("InstallLocation", out var p) && p.GetString() is { Length: > 2 } s) dirs.Add(s);
                    }
                    catch { }
        }
        catch { }
        return dirs.Distinct(StringComparer.OrdinalIgnoreCase);
    }
}
