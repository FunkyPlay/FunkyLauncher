// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using FunkyLauncher.Core;

namespace FunkyLauncher.Services;

public sealed record AddonModState(AddonMod Mod, bool Installed, bool Enabled);

public sealed record AddonResult(int Installed, List<ManualItem> NeedManual, List<string> Errors);

/// <summary>
/// Installs an add-on into an installed modlist: downloads each Nexus file with the official wabbajack-cli
/// ("download-url", the player's own Nexus login — Premium downloads by itself), checks SHA-256, unpacks it with
/// wabbajack-cli into mods\&lt;folder&gt; and switches the mod on in MO2's modlist.txt. Files Wabbajack can't fetch
/// (free account, or adult content turned off on Nexus) are returned for the manual download window.
/// Nothing outside the modlist folder and its downloads folder is touched.
/// </summary>
public static class AddonService
{
    public static string ModsDir(Settings s, Modlist m) => Path.Combine(s.InstallDir(m), "mods");
    public static string ModlistTxt(Settings s, Modlist m) => Path.Combine(s.InstallDir(m), "profiles", m.Mo2Profile, "modlist.txt");

    public static AddonPrefs Prefs(Settings s, AddonFile a) =>
        s.Addons.TryGetValue(a.AddonId, out var p) ? p : s.Addons[a.AddonId] = new AddonPrefs();

    /// <summary>Mods of the levels the player chose.</summary>
    public static List<AddonMod> Wanted(AddonFile a, bool level2) => a.Mods.Where(x => x.Level == 1 || (level2 && x.Level == 2)).ToList();

    public static List<AddonModState> State(Settings s, Modlist m, AddonFile a)
    {
        var lines = File.Exists(ModlistTxt(s, m)) ? File.ReadAllLines(ModlistTxt(s, m)) : Array.Empty<string>();
        return a.Mods.Select(x => new AddonModState(x,
            Directory.Exists(Path.Combine(ModsDir(s, m), x.ModFolder)),
            lines.Contains("+" + x.ModFolder))).ToList();
    }

    /// <summary>The add-on is on, but some of its mods are gone or switched off (e.g. after the modlist was updated).</summary>
    public static bool NeedsReapply(Settings s, Modlist m, AddonFile a)
    {
        var p = Prefs(s, a);
        if (!p.On || !s.IsInstalled(m)) return false;
        var wanted = Wanted(a, p.Level2).Select(x => x.Id).ToHashSet();
        return State(s, m, a).Any(x => wanted.Contains(x.Mod.Id) && !(x.Installed && x.Enabled));
    }

    /// <summary>MO2 rewrites modlist.txt when it closes, so it must not be running while we change it.</summary>
    public static bool Mo2Running() => Process.GetProcessesByName("ModOrganizer").Length > 0;

    public static async Task<AddonResult> InstallAsync(Settings s, Modlist m, AddonFile a, bool level2, IProgress<string> progress, CancellationToken token)
    {
        if (Mo2Running()) throw new InvalidOperationException(Loc.T("addon.err.mo2"));
        var mods = ModsDir(s, m);
        var wanted = Wanted(a, level2);
        // everything is already unpacked (e.g. switching Russian back on): only switch the mods on — seconds, not minutes
        if (wanted.All(x => Directory.Exists(Path.Combine(mods, x.ModFolder))))
        {
            progress.Report(Loc.T("addon.enabling"));
            await Task.Run(() => SetEnabled(s, m, a, level2, on: true), token);
            return new AddonResult(wanted.Count, new List<ManualItem>(), new List<string>());
        }
        var downloads = s.DownloadsDir(m);
        Directory.CreateDirectory(downloads);
        var manual = new List<ManualItem>();
        var errors = new List<string>();
        var done = 0;

        for (var i = 0; i < wanted.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            var x = wanted[i];
            var name = x.DisplayName.ToString();
            var target = Path.Combine(mods, x.ModFolder);
            if (Directory.Exists(target)) { done++; continue; }

            var archive = Path.Combine(downloads, x.Archive.LocalFileName);
            if (!ManualQueue.IsPresent(downloads, x.Archive.LocalFileName, x.Archive.Size))
            {
                progress.Report(Loc.F("addon.downloading", i + 1, wanted.Count, name));
                var ok = await DownloadAsync(x, archive, token);
                if (!ok)
                {
                    manual.Add(new ManualItem
                    {
                        ArchiveName = x.Archive.LocalFileName, Size = x.Archive.Size, ModName = name,
                        GameDomain = x.Nexus.Game, ModId = x.Nexus.ModId, FileId = x.Nexus.FileId,
                    });
                    continue;
                }
            }

            progress.Report(Loc.F("addon.checking", i + 1, wanted.Count, name));
            if (!string.Equals(await Sha256Async(archive, token), x.Archive.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(Loc.F("addon.err.hash", name));
                continue;
            }

            progress.Report(Loc.F("addon.unpacking", i + 1, wanted.Count, name));
            try { await UnpackAsync(x, archive, target, token); done++; }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { errors.Add(Loc.F("addon.err.unpack", name, ex.Message)); }
        }

        progress.Report(Loc.T("addon.enabling"));
        SetEnabled(s, m, a, level2, on: true);
        return new AddonResult(done, manual, errors);
    }

    /// <summary>Switches the add-on's mods off in modlist.txt. The files stay in the mods folder.</summary>
    public static void Disable(Settings s, Modlist m, AddonFile a)
    {
        if (Mo2Running()) throw new InvalidOperationException(Loc.T("addon.err.mo2"));
        SetEnabled(s, m, a, level2: false, on: false);
    }

    /// <summary>
    /// Writes modlist.txt (a backup is kept next to it). Wanted mods that are on disk get "+name" right above their
    /// anchor (top of the file = highest priority); every other add-on mod becomes "-name".
    /// </summary>
    private static void SetEnabled(Settings s, Modlist m, AddonFile a, bool level2, bool on)
    {
        var path = ModlistTxt(s, m);
        if (!File.Exists(path)) throw new InvalidOperationException(Loc.F("addon.err.nomodlist", path));
        var original = File.ReadAllLines(path).ToList();
        var lines = new List<string>(original);
        var wanted = on ? Wanted(a, level2).Select(x => x.Id).ToHashSet() : new HashSet<string>();

        foreach (var x in a.Mods) lines.RemoveAll(l => l.Length > 1 && (l[0] == '+' || l[0] == '-') && l[1..] == x.ModFolder);
        foreach (var x in a.Mods)
        {
            var onDisk = Directory.Exists(Path.Combine(ModsDir(s, m), x.ModFolder));
            if (!onDisk) continue;
            var line = (wanted.Contains(x.Id) ? "+" : "-") + x.ModFolder;
            var above = Find(lines, x.Modlist.InsertAbove);
            if (above >= 0) { lines.Insert(above, line); continue; }
            var below = Find(lines, x.Modlist.FallbackInsertBelow);
            if (below >= 0) { lines.Insert(below + 1, line); continue; }
            lines.Insert(FirstModLine(lines), line);
        }

        if (!lines.SequenceEqual(original))
        {
            File.Copy(path, path + ".before-addon", overwrite: true);
            File.WriteAllLines(path, lines, new UTF8Encoding(false));
        }

        var p = Prefs(s, a);
        p.On = on;
        p.Level2 = on && level2;
        s.Save();
    }

    private static int Find(List<string> lines, string? name) =>
        string.IsNullOrEmpty(name) ? -1 : lines.FindIndex(l => l.Length > 1 && "+-*".Contains(l[0]) && l[1..] == name);

    /// <summary>After MO2's leading "# This file was automatically generated…" comment lines.</summary>
    private static int FirstModLine(List<string> lines)
    {
        var i = 0;
        while (i < lines.Count && lines[i].StartsWith('#')) i++;
        return i;
    }

    /// <summary>wabbajack-cli download-url: the Nexus file through the player's Wabbajack login (works for Premium).</summary>
    private static async Task<bool> DownloadAsync(AddonMod x, string archive, CancellationToken token)
    {
        var tmp = archive + ".addon-part";
        try
        {
            if (File.Exists(tmp)) File.Delete(tmp);
            var code = await WabbajackService.RunAsync($"download-url -u \"{x.Nexus.FilePage}\" -o \"{tmp}\"", _ => { }, token);
            if (code != 0 || !File.Exists(tmp) || new FileInfo(tmp).Length != x.Archive.Size) { TryDelete(tmp); return false; }
            File.Move(tmp, archive, overwrite: true);
            return true;
        }
        catch (OperationCanceledException) { TryDelete(tmp); throw; }
        catch { TryDelete(tmp); return false; }
    }

    private static async Task UnpackAsync(AddonMod x, string archive, string target, CancellationToken token)
    {
        var tmp = target + ".addon-tmp";
        if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
        Directory.CreateDirectory(tmp);
        var log = new StringBuilder();
        var code = await WabbajackService.RunAsync($"extract -i \"{archive}\" -o \"{tmp}\"", l => log.AppendLine(l), token);
        var missing = x.Install.ExpectedTopFolders.Where(f => !Directory.Exists(Path.Combine(tmp, f)) && !File.Exists(Path.Combine(tmp, f))).ToList();
        if (code != 0 || !Directory.EnumerateFileSystemEntries(tmp).Any() || missing.Count > 0)
        {
            Directory.Delete(tmp, true);
            throw new InvalidOperationException(missing.Count > 0 ? "no " + string.Join(", ", missing) : "wabbajack-cli extract " + code);
        }
        // MO2 shows the mod as coming from Nexus (and can check it for updates)
        File.WriteAllText(Path.Combine(tmp, "meta.ini"),
            $"[General]\r\ngameName={x.Nexus.Game}\r\nmodid={x.Nexus.ModId}\r\ninstallationFile={x.Archive.LocalFileName}\r\n" +
            $"repository=Nexus\r\n\r\n[installedFiles]\r\n1\\modid={x.Nexus.ModId}\r\n1\\fileid={x.Nexus.FileId}\r\nsize=1\r\n");
        Directory.Move(tmp, target);
    }

    private static async Task<string> Sha256Async(string file, CancellationToken token)
    {
        await using var f = File.OpenRead(file);
        return Convert.ToHexString(await SHA256.HashDataAsync(f, token)).ToLowerInvariant();
    }

    private static void TryDelete(string f) { try { if (File.Exists(f)) File.Delete(f); } catch { } }
}
