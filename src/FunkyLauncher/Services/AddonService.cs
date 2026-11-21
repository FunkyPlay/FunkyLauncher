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
        s.Addons.TryGetValue(a.AddonId, out var p) ? p : s.Addons[a.AddonId] = new AddonPrefs
        {
            // an optional pack marked "default" starts ticked (one-click install); 18+ and language never do
            On = a.Default == true && Addons.ExtraKinds.Any(k => a.AddonId.EndsWith("-" + k, StringComparison.OrdinalIgnoreCase)),
        };

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

    /// <summary>The MO2 of this modlist is open (another game's MO2 does not matter). Unknown path → counted as open.</summary>
    public static bool Mo2Running(Settings s, Modlist m)
    {
        var dir = Path.GetFullPath(s.InstallDir(m)).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var p in Process.GetProcessesByName("ModOrganizer"))
        {
            try { if (p.MainModule?.FileName is { } f && !f.StartsWith(dir, StringComparison.OrdinalIgnoreCase)) continue; }
            catch { }
            return true;
        }
        return false;
    }

    /// <summary>The Nexus file id written into an installed mod's meta.ini (0 = unknown).</summary>
    private static long InstalledFileId(string modDir)
    {
        try
        {
            var meta = Path.Combine(modDir, "meta.ini");
            if (!File.Exists(meta)) return 0;
            foreach (var l in File.ReadLines(meta))
                if (l.StartsWith("1\\fileid=") && long.TryParse(l[9..], out var id)) return id;
        }
        catch { }
        return 0;
    }

    /// <summary>Installed and of the same Nexus file (a new modlist version may switch a mod to a newer file).</summary>
    private static bool IsCurrent(string mods, AddonMod x)
    {
        var dir = Path.Combine(mods, x.ModFolder);
        if (!Directory.Exists(dir)) return false;
        var id = InstalledFileId(dir);
        if (!(id == 0 || x.Nexus.FileId == 0 || id == x.Nexus.FileId)) return false;
        // same file, but the list now lays it out differently (a file added or taken out): install it again
        var meta = Path.Combine(dir, "meta.ini");
        try
        {
            var line = File.Exists(meta) ? File.ReadLines(meta).FirstOrDefault(l => l.StartsWith("funkyLayout=")) : null;
            return line == null || line[12..] == LayoutHash(x);
        }
        catch { return true; }
    }

    /// <summary>Short hash of how the mod is laid out (type, folder, map) — written into meta.ini.</summary>
    private static string LayoutHash(AddonMod x) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("2|" + System.Text.Json.JsonSerializer.Serialize(x.Install))))[..16].ToLowerInvariant();

    /// <param name="counter">Optional: (mods done, mods wanted) for a progress bar.</param>
    public static async Task<AddonResult> InstallAsync(Settings s, Modlist m, AddonFile a, bool level2, IProgress<string> progress, CancellationToken token,
        Action<int, int>? counter = null)
    {
        if (Mo2Running(s, m)) throw new InvalidOperationException(Loc.T("addon.err.mo2"));
        var mods = ModsDir(s, m);
        var wanted = Wanted(a, level2);
        // everything is already unpacked (e.g. switching Russian back on): only switch the mods on — seconds, not minutes
        if (wanted.All(x => IsCurrent(mods, x)))
        {
            progress.Report(Loc.T("addon.enabling"));
            await Task.Run(() => SetEnabled(s, m, a, level2, on: true), token);
            await AfterRussianAsync(s, m, a, progress, token);
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
            counter?.Invoke(i, wanted.Count);
            var x = wanted[i];
            var name = x.DisplayName.ToString();
            var target = Path.Combine(mods, x.ModFolder);
            if (x.Install.Type == "bundled")
            {
                // nothing to download: the folder is laid down with the modlist's own files; the profile switches it on
                done++;
                if (Directory.Exists(target)) Mo2Profile.RememberOurs(s, m, x.ModFolder);
                continue;
            }
            if (IsCurrent(mods, x)) { done++; Mo2Profile.RememberOurs(s, m, x.ModFolder); continue; }
            var oldDir = target + ".old-version";

            // a mod made of several files (a multi-part RAR plus a DLC archive, from its author's site)
            if (x.Archives.Count > 0)
            {
                var parts = x.Archives;
                var names = parts.Select((p, k) => string.IsNullOrEmpty(p.LocalFileName) ? NameMap.Get(downloads, x.ModFolder, k) : p.LocalFileName).ToList();
                var absent = Enumerable.Range(0, parts.Count).Where(k => names[k] == null || !ManualQueue.IsPresent(downloads, names[k]!, parts[k].Size)).ToList();
                // parts that are Nexus files of their own (Neon Nights: one mod folder made from two Nexus files): try them
                // through the player's login first, like single-archive mods
                if (absent.Count > 0 && string.IsNullOrEmpty(x.Url))
                {
                    progress.Report(Loc.F("addon.downloading", i + 1, wanted.Count, name));
                    foreach (var k in absent.ToList())
                    {
                        var part = parts[k];
                        if (names[k] == null || part.Nexus is not { ModId: > 0, FileId: > 0 } pn) continue;
                        var one = new AddonMod
                        {
                            Nexus = new AddonNexus { Game = x.Nexus.Game, ModId = pn.ModId, FileId = pn.FileId },
                            Archive = new AddonArchive { LocalFileName = names[k]!, Size = part.Size },
                        };
                        if (await DownloadAsync(one, Path.Combine(downloads, names[k]!), token)) absent.Remove(k);
                    }
                }
                if (absent.Count > 0)
                {
                    foreach (var k in absent)
                        manual.Add(new ManualItem
                        {
                            ArchiveName = names[k] ?? "", Size = parts[k].Size, Url = x.Url, Key = NameMap.Key(x.ModFolder, k),
                            ModName = $"{name} ({k + 1}/{parts.Count})",
                            GameDomain = x.Nexus.Game,
                            ModId = parts[k].Nexus?.ModId > 0 ? parts[k].Nexus!.ModId : x.Nexus.ModId,
                            FileId = parts[k].Nexus?.FileId > 0 ? parts[k].Nexus!.FileId : x.Nexus.FileId,
                        });
                    continue;
                }
                progress.Report(Loc.F("addon.checking", i + 1, wanted.Count, name));
                var bad = false;
                for (var k = 0; k < parts.Count; k++)
                {
                    var file = Path.Combine(downloads, names[k]!);
                    if (string.IsNullOrEmpty(parts[k].Sha256))
                    {
                        if (RecordFile != null) Record(a, x, file, await Sha256Async(file, token));
                        continue; // a site file whose hash nobody could know yet: the layout check below still has to pass
                    }
                    if (!string.Equals(await Sha256Async(file, token), parts[k].Sha256, StringComparison.OrdinalIgnoreCase)) bad = true;
                }
                if (bad) { errors.Add(Loc.F("addon.err.hash", name)); continue; }
                progress.Report(Loc.F("addon.unpacking", i + 1, wanted.Count, name));
                // RAR volumes: only the first part is opened, it reads the rest itself
                var firsts = names.Select(n => Path.Combine(downloads, n!))
                    .Where(f => !System.Text.RegularExpressions.Regex.IsMatch(f, @"\.part(0*[2-9]|0*\d{2,})\.rar$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)).ToList();
                var hadMulti = Directory.Exists(target);
                if (hadMulti)
                {
                    if (Directory.Exists(oldDir)) Directory.Delete(oldDir, true);
                    Directory.Move(target, oldDir);
                }
                try
                {
                    await UnpackAsync(x, firsts, target, token);
                    done++;
                    Mo2Profile.RememberOurs(s, m, x.ModFolder);
                    // 0.1.19: during a modlist update the old folder is kept for "back to the previous version"
                    if (hadMulti) ModlistRollback.KeepMod(mods, x.ModFolder, oldDir); else ModlistRollback.AddedMod(mods, x.ModFolder);
                }
                catch (OperationCanceledException) { RestoreOld(target, oldDir); throw; }
                catch (Exception ex) { RestoreOld(target, oldDir); errors.Add(Loc.F("addon.err.unpack", name, ex.Message)); }
                continue;
            }

            var archive = Path.Combine(downloads, x.Archive.LocalFileName);
            // our own patch inside the launcher: no download, just written out from the exe
            if (!string.IsNullOrEmpty(x.Embedded) && !ManualQueue.IsPresent(downloads, x.Archive.LocalFileName, x.Archive.Size))
            {
                var res = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/addons/" + x.Embedded.Replace('\\', '/')));
                if (res == null) { errors.Add(Loc.F("addon.err.unpack", name, "embedded file missing")); continue; }
                using (var src = res.Stream)
                using (var dst = File.Create(archive)) await src.CopyToAsync(dst, token);
            }
            if (!ManualQueue.IsPresent(downloads, x.Archive.LocalFileName, x.Archive.Size))
            {
                progress.Report(Loc.F("addon.downloading", i + 1, wanted.Count, name));
                // a file outside Nexus (e.g. a Russian voice-over on its author's site): the player presses the site's own
                // download button in the launcher's browser — the launcher catches the file and installs it
                var ok = string.IsNullOrEmpty(x.Url) && await DownloadAsync(x, archive, token);
                if (!ok)
                {
                    manual.Add(new ManualItem
                    {
                        ArchiveName = x.Archive.LocalFileName, Size = x.Archive.Size, ModName = name,
                        GameDomain = x.Nexus.Game, ModId = x.Nexus.ModId, FileId = x.Nexus.FileId, Url = x.Url,
                    });
                    continue;
                }
            }

            progress.Report(Loc.F("addon.checking", i + 1, wanted.Count, name));
            var hash = await Sha256Async(archive, token);
            if (string.IsNullOrEmpty(x.Archive.Sha256) && RecordFile != null)
                Record(a, x, archive, hash); // developer run that fills in the hashes of a new list
            else if (!string.Equals(hash, x.Archive.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(Loc.F("addon.err.hash", name));
                continue;
            }

            progress.Report(Loc.F("addon.unpacking", i + 1, wanted.Count, name));
            // an older file of this mod (modlist update): set it aside, it is removed once the new one is in place
            var had = Directory.Exists(target);
            if (had)
            {
                if (Directory.Exists(oldDir)) Directory.Delete(oldDir, true);
                Directory.Move(target, oldDir);
            }
            try
            {
                try { await UnpackAsync(x, archive, target, token); }
                catch (Exception) when (!token.IsCancellationRequested)
                {
                    // a rare one-off unpack failure (seen with two installs at once): one more try before giving up
                    if (Directory.Exists(target + ".addon-new")) Directory.Delete(target + ".addon-new", true);
                    await UnpackAsync(x, archive, target, token);
                }
                done++;
                Mo2Profile.RememberOurs(s, m, x.ModFolder);
                if (had) ModlistRollback.KeepMod(mods, x.ModFolder, oldDir); else ModlistRollback.AddedMod(mods, x.ModFolder);
            }
            catch (OperationCanceledException) { RestoreOld(target, oldDir); throw; }
            catch (Exception ex) { RestoreOld(target, oldDir); errors.Add(Loc.F("addon.err.unpack", name, ex.Message)); }
        }
        counter?.Invoke(wanted.Count, wanted.Count);

        progress.Report(Loc.T("addon.enabling"));
        SetEnabled(s, m, a, level2, on: true);
        await AfterRussianAsync(s, m, a, progress, token);
        return new AddonResult(done, manual, errors);
    }

    /// <summary>The Russian pack is on: the Russian names of items from the mods are built on this PC (a failure only leaves them in English).</summary>
    private static async Task AfterRussianAsync(Settings s, Modlist m, AddonFile a, IProgress<string> progress, CancellationToken token)
    {
        if (Addons.Russian(m.Id) is { } ru && ru.AddonId == a.AddonId && TranslationService.Spec(m) != null)
            await TranslationService.BuildAsync(s, m, progress, token);
    }

    /// <summary>Switches the add-on's mods off in modlist.txt. The files stay in the mods folder.</summary>
    public static void Disable(Settings s, Modlist m, AddonFile a)
    {
        if (Mo2Running(s, m)) throw new InvalidOperationException(Loc.T("addon.err.mo2"));
        SetEnabled(s, m, a, level2: false, on: false);
    }

    /// <summary>
    /// Writes modlist.txt (a backup is kept next to it). Wanted mods that are on disk get "+name" right above their
    /// anchor (top of the file = highest priority); every other add-on mod becomes "-name".
    /// </summary>
    private static void SetEnabled(Settings s, Modlist m, AddonFile a, bool level2, bool on)
    {
        // Nexus-list modlists (Starfield): the whole profile, plugins included, is written from the order file
        if (Mo2Profile.IsNexusList(m) && Addons.Order(m.Id) is { } order)
        {
            var pr = Prefs(s, a);
            pr.On = on;
            pr.Level2 = on && level2;
            s.Save();
            Mo2Profile.Write(s, m, order);
            return;
        }
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
            // some lists keep Nexus' own path as the archive name ("7c/85/a7/…"): its folders may not exist yet
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(archive))!);
            // a list without "filePage" (Starfield Russian pack): the page is built from game / mod id / file id
            var page = !string.IsNullOrEmpty(x.Nexus.FilePage) ? x.Nexus.FilePage
                : $"https://www.nexusmods.com/{x.Nexus.Game}/mods/{x.Nexus.ModId}?tab=files&file_id={x.Nexus.FileId}";
            var code = await WabbajackService.RunAsync($"download-url -u \"{page}\" -o \"{tmp}\"", _ => { }, token);
            if (code != 0 || !File.Exists(tmp) || new FileInfo(tmp).Length == 0
                || (x.Archive.Size > 0 && new FileInfo(tmp).Length != x.Archive.Size)) { TryDelete(tmp); return false; }
            File.Move(tmp, archive, overwrite: true);
            return true;
        }
        catch (OperationCanceledException) { TryDelete(tmp); throw; }
        catch { TryDelete(tmp); return false; }
    }

    private static async Task UnpackAsync(AddonMod x, string archive, string target, CancellationToken token) =>
        await UnpackAsync(x, new List<string> { archive }, target, token);

    /// <summary>Several archives of one mod (e.g. a voice-over and its DLC part) are unpacked into one folder first.</summary>
    private static async Task UnpackAsync(AddonMod x, List<string> archives, string target, CancellationToken token)
    {
        var tmp = target + ".addon-tmp";
        if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
        Directory.CreateDirectory(tmp);
        try
        {
            // a single plugin or other plain file (our own patch): it goes into the mod folder as it is
            if (x.Install.Type == "copyFile")
            {
                File.Copy(archives[0], Path.Combine(tmp, x.Archive.LocalFileName), overwrite: true);
                var plain = target + ".addon-new";
                if (Directory.Exists(plain)) Directory.Delete(plain, true);
                Directory.Move(tmp, plain);
                File.WriteAllText(Path.Combine(plain, "meta.ini"), $"[General]\r\nrepository=FunkyPlay\r\nfunkyLayout={LayoutHash(x)}\r\n");
                Directory.Move(plain, target);
                return;
            }
            if (archives.Count == 1) await Archives.ExtractAsync(archives[0], tmp, token);
            else if (x.Install.PerArchiveFolders)
                for (var k = 0; k < archives.Count; k++)
                    await Archives.ExtractAsync(archives[k], Path.Combine(tmp, k.ToString()), token);
            else
                foreach (var a in archives)
                {
                    // each archive into its own folder first (a failed attempt clears only that one), then merged into
                    // one tree by moving — paths in the map stay as they are inside the archives
                    var sub = tmp + ".part";
                    if (Directory.Exists(sub)) Directory.Delete(sub, true);
                    await Archives.ExtractAsync(a, sub, token);
                    MergeMove(sub, tmp);
                    Directory.Delete(sub, true);
                }
            var built = Layout(x, tmp, target);
            // MO2 shows the mod as coming from Nexus (and can check it for updates)
            File.WriteAllText(Path.Combine(built, "meta.ini"),
                $"[General]\r\ngameName={x.Nexus.Game}\r\nmodid={x.Nexus.ModId}\r\ninstallationFile={x.Archive.LocalFileName}\r\n" +
                $"repository=Nexus\r\nfunkyLayout={LayoutHash(x)}\r\n\r\n[installedFiles]\r\n1\\modid={x.Nexus.ModId}\r\n1\\fileid={x.Nexus.FileId}\r\nsize=1\r\n");
            Directory.Move(built, target);
        }
        finally
        {
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
        }
    }

    /// <summary>
    /// Turns the unpacked archive into the mod folder's content and returns that folder (next to the target).
    /// copyAll: everything, or the folder "from"; fomod / files / gameRoot: exactly the files of the "map"
    /// (a FOMOD installer's choices were turned into this map when the list was prepared — no FOMOD runs here).
    /// </summary>
    private static string Layout(AddonMod x, string unpacked, string target)
    {
        var type = x.Install.Type;
        if (type is "copyAll" or "copyFrom")
        {
            var root = string.IsNullOrEmpty(x.Install.From) ? BethesdaDataRoot(x, unpacked) : Path.Combine(unpacked, Norm(x.Install.From));
            if (!Directory.Exists(root)) throw new InvalidOperationException("no " + x.Install.From);
            var missing = x.Install.ExpectedTopFolders.Where(f => !Directory.Exists(Path.Combine(root, f)) && !File.Exists(Path.Combine(root, f))).ToList();
            if (missing.Count > 0) throw new InvalidOperationException("no " + string.Join(", ", missing));
            var built = target + ".addon-new";
            if (Directory.Exists(built)) Directory.Delete(built, true);
            Directory.Move(root, built); // a move, not a copy: archives here can be 10 GB
            return built;
        }
        if (type is "fomod" or "files" or "gameRoot" && x.Install.Map.Count > 0)
        {
            var built = target + ".addon-new";
            if (Directory.Exists(built)) Directory.Delete(built, true);
            Directory.CreateDirectory(built);
            foreach (var pair in x.Install.Map)
            {
                if (pair.Count < 2) continue;
                // "*/name": the file is somewhere in the archive (a path the list author could not see)
                var src = pair[0].StartsWith("*/")
                    ? Directory.EnumerateFiles(unpacked, pair[0][2..], SearchOption.AllDirectories).FirstOrDefault() ?? Path.Combine(unpacked, Norm(pair[0][2..]))
                    : Path.Combine(unpacked, Norm(pair[0]));
                var dst = Path.GetFullPath(Path.Combine(built, Norm(pair[1])));
                if (!dst.StartsWith(Path.GetFullPath(built), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("bad path " + pair[1]);
                if (Directory.Exists(src)) CopyDir(src, dst);
                else if (File.Exists(src))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                    File.Copy(src, dst, overwrite: true);
                }
                else throw new InvalidOperationException("no " + pair[0]);
            }
            return built;
        }
        throw new InvalidOperationException(Loc.T("addon.err.layout"));
    }

    private static readonly HashSet<string> BethesdaGames = new(StringComparer.OrdinalIgnoreCase)
        { "starfield", "fallout4", "skyrimspecialedition", "fallout76" };

    private static readonly HashSet<string> DataFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "textures", "meshes", "materials", "scripts", "strings", "sound", "interface", "geometries", "terrain", "video",
        "particles", "sfse", "f4se", "skse", "mcm", "music", "vis", "lodsettings", "shadersfx", "planetdata", "distantlod",
        "tools", "fomod", "source", "fonts", "programs", "seq", "misc", "facegen",
    };

    /// <summary>
    /// What MO2 itself does on install for Bethesda games: if the archive wraps the game files in "Data" or in one
    /// extra folder ("ModName\Data\…"), take the content from there — plugins must sit at the top of the mod folder.
    /// Stops as soon as plugins, .ba2 archives or known data folders are on this level. Other games: archive as is.
    /// </summary>
    private static string BethesdaDataRoot(AddonMod x, string unpacked)
    {
        if (!BethesdaGames.Contains(x.Nexus.Game)) return unpacked;
        var root = unpacked;
        for (var depth = 0; depth < 4; depth++)
        {
            var files = Directory.GetFiles(root);
            var dirs = Directory.GetDirectories(root);
            if (files.Any(f => Path.GetExtension(f).ToLowerInvariant() is ".esm" or ".esp" or ".esl" or ".ba2" or ".bsa")) break;
            if (dirs.Any(d => DataFolders.Contains(Path.GetFileName(d)))) break;
            var data = dirs.FirstOrDefault(d => Path.GetFileName(d).Equals("Data", StringComparison.OrdinalIgnoreCase));
            if (data != null) { root = data; continue; }
            if (dirs.Length == 1 && files.Length == 0) { root = dirs[0]; continue; }
            break;
        }
        return root;
    }

    private static string Norm(string p) => p.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar).Trim(Path.DirectorySeparatorChar);

    /// <summary>Moves every file of <paramref name="from"/> into the same place under <paramref name="to"/> (no copying of big files).</summary>
    private static void MergeMove(string from, string to)
    {
        foreach (var f in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories).ToList())
        {
            var dst = Path.Combine(to, Path.GetRelativePath(from, f));
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.Move(f, dst, overwrite: true);
        }
    }

    private static void CopyDir(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var f in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var dst = Path.Combine(to, Path.GetRelativePath(from, f));
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.Copy(f, dst, overwrite: true);
        }
        foreach (var d in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, d)));
    }

    /// <summary>FUNKYLAUNCHER_RECORD=&lt;file&gt; (developer runs only): a list without hashes is accepted and its hashes are written there.</summary>
    private static string? RecordFile => Environment.GetEnvironmentVariable("FUNKYLAUNCHER_RECORD") is { Length: > 0 } f ? f : null;

    private static readonly object RecordLock = new();

    private static void Record(AddonFile a, AddonMod x, string archive, string hash)
    {
        lock (RecordLock)
            File.AppendAllText(RecordFile!, $"{a.AddonId}\t{x.Id}\t{x.Nexus.ModId}\t{x.Nexus.FileId}\t{new FileInfo(archive).Length}\t{hash}\t{archive}\r\n");
    }

    private static async Task<string> Sha256Async(string file, CancellationToken token)
    {
        await using var f = File.OpenRead(file);
        return Convert.ToHexString(await SHA256.HashDataAsync(f, token)).ToLowerInvariant();
    }

    private static void TryDelete(string f) { try { if (File.Exists(f)) File.Delete(f); } catch { } }

    /// <summary>The new file did not install: the previous version of the mod goes back.</summary>
    private static void RestoreOld(string target, string oldDir)
    {
        try { if (Directory.Exists(oldDir) && !Directory.Exists(target)) Directory.Move(oldDir, target); } catch { }
    }
}
