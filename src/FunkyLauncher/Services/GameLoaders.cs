// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Security.Cryptography;
using System.Text.Json;
using FunkyLauncher.Core;

namespace FunkyLauncher.Services;

/// <summary>
/// A few loader files (CET, RED4ext, redscript for Cyberpunk) are read by Windows / the game from the game folder
/// before Mod Organizer 2 starts its virtual file system, so MO2 cannot provide them. Before PLAY they are copied
/// from the modlist folder into the game folder; the launcher remembers what it put there, and
/// "Restore clean game" takes exactly that back out (and returns the player's own files it had replaced).
/// The list comes from the catalog ("gameLoaders", "gameCleanup").
/// </summary>
public static class GameLoaders
{
    private sealed class Manifest
    {
        public string GamePath { get; set; } = "";
        public List<Entry> Files { get; set; } = new();
        /// <summary>Mods the player already had in the game folder (some of their files were there before us).</summary>
        public List<string> PlayerMods { get; set; } = new();
    }

    private sealed class Entry
    {
        public string Path { get; set; } = "";
        public string Mod { get; set; } = "";
        public string Sha256 { get; set; } = "";
        /// <summary>The player's own file was there before; a copy is kept in the backup folder.</summary>
        public bool Replaced { get; set; }
    }

    public sealed record Conflict(string Path, string Mod);

    private static string Dir => System.IO.Path.Combine(AppConfig.DataDir, "gamefiles");
    private static string ManifestPath(Modlist ml) => System.IO.Path.Combine(Dir, ml.Id + ".json");
    private static string BackupDir(Modlist ml) => System.IO.Path.Combine(Dir, "backup", ml.Id);

    public static bool Has(Modlist ml) => ml.GameLoaders.Count > 0;

    private static Manifest Load(Modlist ml)
    {
        try
        {
            if (File.Exists(ManifestPath(ml)))
                return JsonSerializer.Deserialize<Manifest>(File.ReadAllText(ManifestPath(ml))) ?? new();
        }
        catch { }
        return new();
    }

    private static void Save(Modlist ml, Manifest m)
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(ManifestPath(ml), JsonSerializer.Serialize(m, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string Hash(string file)
    {
        using var s = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(s));
    }

    private static string Norm(string rel) => rel.Replace('/', System.IO.Path.DirectorySeparatorChar);

    /// <summary>Enabled mods of the MO2 profile ("+Name" lines of modlist.txt).</summary>
    private static HashSet<string> EnabledMods(Settings s, Modlist ml)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var f = System.IO.Path.Combine(s.InstallDir(ml), "profiles", ml.Mo2Profile, "modlist.txt");
            foreach (var line in File.ReadLines(f))
                if (line.StartsWith('+')) set.Add(line[1..].Trim());
        }
        catch { }
        return set;
    }

    /// <summary>The file inside the modlist: mods\&lt;folder starting with "mod"&gt;\path, an enabled folder first.</summary>
    private static string? Source(Settings s, Modlist ml, GameLoader l, HashSet<string> enabled)
    {
        var mods = System.IO.Path.Combine(s.InstallDir(ml), "mods");
        if (!Directory.Exists(mods)) return null;
        return Directory.GetDirectories(mods)
            .Where(d => System.IO.Path.GetFileName(d).StartsWith(l.Mod, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(d => enabled.Contains(System.IO.Path.GetFileName(d)))
            .Select(d => System.IO.Path.Combine(d, Norm(l.Path)))
            .FirstOrDefault(File.Exists);
    }

    /// <summary>Player's own (not ours) loader files that differ from the modlist's ones.</summary>
    public static List<Conflict> Conflicts(Settings s, Modlist ml, string gamePath)
    {
        var man = Load(ml);
        var enabled = EnabledMods(s, ml);
        var list = new List<Conflict>();
        foreach (var l in ml.GameLoaders)
        {
            var src = Source(s, ml, l, enabled);
            var dst = System.IO.Path.Combine(gamePath, Norm(l.Path));
            if (src == null || !File.Exists(dst)) continue;
            if (man.Files.Any(e => e.Path.Equals(l.Path, StringComparison.OrdinalIgnoreCase))) continue;
            if (!Hash(src).Equals(Hash(dst), StringComparison.OrdinalIgnoreCase)) list.Add(new Conflict(l.Path, l.Mod));
        }
        return list;
    }

    /// <summary>
    /// Puts the loaders into the game folder. <paramref name="replaceOwn"/>: the player agreed to replace
    /// their own different files (they are backed up). Returns the loaders that could not be found in the modlist.
    /// </summary>
    public static List<string> Ensure(Settings s, Modlist ml, string gamePath, bool replaceOwn)
    {
        var man = Load(ml);
        if (!string.Equals(man.GamePath, gamePath, StringComparison.OrdinalIgnoreCase) && man.Files.Count == 0) man.GamePath = gamePath;
        var enabled = EnabledMods(s, ml);
        var missing = new List<string>();
        foreach (var l in ml.GameLoaders)
        {
            var src = Source(s, ml, l, enabled);
            if (src == null) { missing.Add(l.Path); continue; }
            var dst = System.IO.Path.Combine(gamePath, Norm(l.Path));
            var srcHash = Hash(src);
            var ours = man.Files.FirstOrDefault(e => e.Path.Equals(l.Path, StringComparison.OrdinalIgnoreCase));
            if (File.Exists(dst))
            {
                var dstHash = Hash(dst);
                if (dstHash.Equals(srcHash, StringComparison.OrdinalIgnoreCase))
                {
                    if (ours != null) ours.Sha256 = srcHash;
                    else if (!man.PlayerMods.Contains(l.Mod)) man.PlayerMods.Add(l.Mod);
                    continue; // already the right file (ours, or the player's identical one: then it stays theirs)
                }
                if (ours == null)
                {
                    if (!man.PlayerMods.Contains(l.Mod)) man.PlayerMods.Add(l.Mod);
                if (!replaceOwn) continue;
                    var bak = System.IO.Path.Combine(BackupDir(ml), Norm(l.Path));
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(bak)!);
                    File.Copy(dst, bak, true);
                    ours = new Entry { Path = l.Path, Mod = l.Mod, Replaced = true };
                    man.Files.Add(ours);
                }
            }
            else if (ours == null)
            {
                ours = new Entry { Path = l.Path, Mod = l.Mod };
                man.Files.Add(ours);
            }
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dst)!);
            File.Copy(src, dst, true);
            ours.Sha256 = srcHash;
            man.GamePath = gamePath;
            Save(ml, man);
        }
        Save(ml, man);
        return missing;
    }

    /// <summary>Something of ours is in the game folder.</summary>
    public static bool Placed(Modlist ml) => Load(ml).Files.Count > 0;

    /// <summary>
    /// Takes our loaders out of the game folder (only files that are still exactly ours), puts the player's
    /// replaced files back, and removes what those mods created next to them (logs, settings).
    /// Returns files that were left because the player changed them.
    /// </summary>
    public static List<string> Clean(Modlist ml)
    {
        var man = Load(ml);
        var kept = new List<string>();
        if (man.Files.Count == 0 || string.IsNullOrEmpty(man.GamePath)) return kept;
        var game = man.GamePath;
        var ourMods = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in man.Files)
        {
            var dst = System.IO.Path.Combine(game, Norm(e.Path));
            try
            {
                if (File.Exists(dst) && !Hash(dst).Equals(e.Sha256, StringComparison.OrdinalIgnoreCase)) { kept.Add(e.Path); continue; }
                var bak = System.IO.Path.Combine(BackupDir(ml), Norm(e.Path));
                if (e.Replaced && File.Exists(bak)) File.Copy(bak, dst, true);
                else if (File.Exists(dst)) File.Delete(dst);
                if (!man.PlayerMods.Contains(e.Mod)) ourMods.Add(e.Mod);
            }
            catch { kept.Add(e.Path); }
        }
        // what the mods created themselves — only for mods the player did not have before
        foreach (var c in ml.GameCleanup.Where(c => ourMods.Contains(c.Mod)))
        {
            var p = System.IO.Path.Combine(game, Norm(c.Path));
            try
            {
                if (Directory.Exists(p)) Directory.Delete(p, true);
                else if (File.Exists(p)) File.Delete(p);
            }
            catch { kept.Add(c.Path); }
        }
        // empty folders left behind (e.g. red4ext, bin\x64\plugins)
        foreach (var e in man.Files.Select(e => e.Path).Concat(ml.GameCleanup.Select(c => c.Path)))
        {
            var d = System.IO.Path.GetDirectoryName(System.IO.Path.Combine(game, Norm(e)));
            while (d != null && d.Length > game.Length && Directory.Exists(d) && !Directory.EnumerateFileSystemEntries(d).Any())
            {
                try { Directory.Delete(d); } catch { break; }
                d = System.IO.Path.GetDirectoryName(d);
            }
        }
        if (kept.Count == 0)
        {
            try { File.Delete(ManifestPath(ml)); } catch { }
            try { if (Directory.Exists(BackupDir(ml))) Directory.Delete(BackupDir(ml), true); } catch { }
        }
        else
        {
            man.Files = man.Files.Where(e => kept.Contains(e.Path)).ToList();
            Save(ml, man);
        }
        return kept;
    }
}
