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
        if (string.IsNullOrEmpty(ml.GameExe) || !File.Exists(Path.Combine(dir, ml.GameExe))) return false;
        var all = Load();
        all[ml.Id] = Path.GetFullPath(dir).TrimEnd('\\');
        Directory.CreateDirectory(AppConfig.DataDir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true }));
        return true;
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
