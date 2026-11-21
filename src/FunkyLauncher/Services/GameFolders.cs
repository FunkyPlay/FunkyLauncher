// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Text.Json;
using System.Text.RegularExpressions;
using FunkyLauncher.Core;
using Microsoft.Win32;

namespace FunkyLauncher.Services;

/// <summary>
/// Where a game may be when Wabbajack's store detection does not see it: the folder the player picked
/// (kept in gamefolders.json on this PC), every Steam library, and the catalog's folder hints on each drive.
/// </summary>
public static partial class GameFolders
{
    private static string FilePath => Path.Combine(AppConfig.DataDir, "gamefolders.json");

    private static Dictionary<string, string> Load()
    {
        try { if (File.Exists(FilePath)) return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(FilePath)) ?? new(); }
        catch { }
        return new();
    }

    public static string? Picked(Modlist ml) => Load().TryGetValue(ml.Id, out var d) && Directory.Exists(d) ? d : null;

    /// <summary>Remembers the game folder the player picked. False if the game exe is not in it.</summary>
    public static bool Pick(Modlist ml, string dir)
    {
        if (string.IsNullOrEmpty(ml.GameExe) || !File.Exists(Path.Combine(dir, ml.GameExePath))) return false;
        var all = Load();
        all[ml.Id] = Path.GetFullPath(dir).TrimEnd('\\');
        Directory.CreateDirectory(AppConfig.DataDir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true }));
        return true;
    }

    /// <summary>The folder with the game exe one or two levels below <paramref name="dir"/> (the player picked "D:\Games"). Null if none or several.</summary>
    public static string? FindBelow(Modlist ml, string dir)
    {
        if (string.IsNullOrEmpty(ml.GameExe)) return null;
        var found = new List<string>();
        try
        {
            foreach (var a in Directory.EnumerateDirectories(dir))
            {
                if (File.Exists(Path.Combine(a, ml.GameExePath))) found.Add(a);
                else
                    try { foreach (var b in Directory.EnumerateDirectories(a)) if (File.Exists(Path.Combine(b, ml.GameExePath))) found.Add(b); }
                    catch { /* no access — skip */ }
                if (found.Count > 1) return null;
            }
        }
        catch { return null; }
        return found.Count == 1 ? found[0] : null;
    }

    /// <summary>Two new lines and "In this folder: a, b, c…" — what the player actually picked (up to 8 names); "" if unreadable.</summary>
    public static string Contents(string dir)
    {
        try
        {
            var names = Directory.EnumerateFileSystemEntries(dir).Select(Path.GetFileName).Take(9).ToList();
            if (names.Count == 0) return Environment.NewLine + Environment.NewLine + Loc.T("setup.gamefolder.empty");
            return Environment.NewLine + Environment.NewLine + Loc.F("setup.gamefolder.has", string.Join(", ", names.Take(8)) + (names.Count > 8 ? "…" : ""));
        }
        catch { return ""; }
    }

    public static IEnumerable<string> Candidates(Modlist ml)
    {
        if (Picked(ml) is { } p) yield return p;
        if (ml.SteamAppId.Length > 0)
            foreach (var lib in SteamLibraries())
            {
                var manifest = Path.Combine(lib, "steamapps", $"appmanifest_{ml.SteamAppId}.acf");
                if (!File.Exists(manifest)) continue;
                string? dir = null;
                try { dir = InstallDirLine().Match(File.ReadAllText(manifest)).Groups["d"].Value; } catch { }
                if (!string.IsNullOrEmpty(dir)) yield return Path.Combine(lib, "steamapps", "common", dir);
            }
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType != DriveType.Fixed) continue;
            foreach (var hint in ml.GameFolderHints)
                yield return Path.Combine(drive.RootDirectory.FullName, hint);
        }
    }

    [GeneratedRegex("\"installdir\"\\s+\"(?<d>[^\"]+)\"")]
    private static partial Regex InstallDirLine();

    [GeneratedRegex("\"path\"\\s+\"(?<p>[^\"]+)\"")]
    private static partial Regex LibraryPathLine();

    private static IEnumerable<string> SteamLibraries()
    {
        string? steam = null;
        try { steam = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string; } catch { }
        if (string.IsNullOrEmpty(steam) || !Directory.Exists(steam)) yield break;
        steam = steam.Replace('/', '\\');
        yield return steam;
        var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf)) yield break;
        string text;
        try { text = File.ReadAllText(vdf); } catch { yield break; }
        foreach (Match m in LibraryPathLine().Matches(text))
        {
            var p = m.Groups["p"].Value.Replace(@"\\", @"\");
            if (!p.Equals(steam, StringComparison.OrdinalIgnoreCase) && Directory.Exists(p)) yield return p;
        }
    }
}
