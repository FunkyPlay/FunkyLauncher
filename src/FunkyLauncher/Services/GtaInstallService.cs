// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using FunkyLauncher.Core;

namespace FunkyLauncher.Services;

/// <summary>
/// 0.1.23: GTA V Enhanced — no MO2 here: the mods go straight into the game folder. Three versions from three packs
/// (addons/gta5-main.json = Standard, + gta5-high.json = High, + gta5-heavy.json = Ultra; High and Ultra need
/// NaturalVision Enhanced, which the player installs himself). Every file we write is listed in
/// &lt;game&gt;\_FunkyPlay\installed.txt and every file we overwrite is kept in &lt;game&gt;\_FunkyPlay\backup, so
/// "Restore clean game" puts the folder back exactly. NVE's own files are never edited or replaced (Legal 25.11):
/// add-on packs go through Simple Mods Loader (mods\update\x64\dlcpacks), our overrides into its update.rpf layer.
/// </summary>
public static class GtaInstallService
{
    public const string ModlistId = "gta5";
    public const string GameExe = "GTA5_Enhanced.exe";

    // ---------------------------------------------------------------- data

    public sealed class Pack
    {
        [JsonPropertyName("addonId")] public string AddonId { get; set; } = "";
        [JsonPropertyName("listVersion")] public string ListVersion { get; set; } = "";
        [JsonPropertyName("gameVersion")] public string GameVersion { get; set; } = "";
        [JsonPropertyName("tokens")] public Dictionary<string, Dictionary<string, string>> Tokens { get; set; } = new();
        [JsonPropertyName("merge")] public List<Merge> Merge { get; set; } = new();
        [JsonPropertyName("mods")] public List<Mod> Mods { get; set; } = new();
    }

    public sealed class Merge
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("when")] public string? When { get; set; }
        [JsonPropertyName("enabled")] public bool? Enabled { get; set; }
        [JsonPropertyName("copy")] public string? Copy { get; set; }
    }

    public sealed class Mod
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("displayName")] public string DisplayName { get; set; } = "";
        [JsonPropertyName("modFolder")] public string ModFolder { get; set; } = "";
        [JsonPropertyName("nexus")] public AddonNexus Nexus { get; set; } = new();
        [JsonPropertyName("download")] public Download? Download { get; set; }
        [JsonPropertyName("archive")] public AddonArchive Archive { get; set; } = new();
        [JsonPropertyName("install")] public Install Install { get; set; } = new();
        [JsonPropertyName("nested")] public Dictionary<string, List<List<string>>> Nested { get; set; } = new();
        [JsonPropertyName("iniSet")] public Dictionary<string, Dictionary<string, Dictionary<string, string>>> IniSet { get; set; } = new();
        [JsonPropertyName("jsonSet")] public Dictionary<string, Dictionary<string, JsonElement>> JsonSet { get; set; } = new();
        [JsonPropertyName("userArchive")] public UserArchive? UserArchive { get; set; }
        [JsonPropertyName("mapAfterPick")] public List<List<string>> MapAfterPick { get; set; } = new();
        [JsonPropertyName("when")] public string? When { get; set; }
        [JsonPropertyName("hold")] public JsonElement? Hold { get; set; }
        [JsonPropertyName("required")] public bool Required { get; set; }
        [JsonPropertyName("default")] public bool? Default { get; set; }
        [JsonPropertyName("noOverwrite")] public bool NoOverwrite { get; set; }
        [JsonPropertyName("provides")] public string? Provides { get; set; }

        public bool OnHold => Hold is { ValueKind: JsonValueKind.Object };
        public bool IsManual => Install.Type == "manual";
    }

    public sealed class Download
    {
        [JsonPropertyName("url")] public string Url { get; set; } = "";
        [JsonPropertyName("urls")] public List<string> Urls { get; set; } = new();
    }

    public sealed class Install
    {
        [JsonPropertyName("type")] public string Type { get; set; } = "files";
        [JsonPropertyName("map")] public List<List<string>> Map { get; set; } = new();
        [JsonPropertyName("page")] public string Page { get; set; } = "";
        [JsonPropertyName("check")] public List<string> Check { get; set; } = new();
        [JsonPropertyName("note")] public LocText Note { get; set; } = new();
    }

    public sealed class UserArchive
    {
        [JsonPropertyName("expect")] public string Expect { get; set; } = "";
        [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
    }

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip };

    /// <summary>Standard / High / Ultra packs: the cached online copy first, the one built into the launcher otherwise.</summary>
    public static Pack? LoadPack(string kind)
    {
        try
        {
            string? json = null;
            var cached = Path.Combine(AppConfig.DataDir, "addons", $"{ModlistId}-{kind}.json");
            if (File.Exists(cached)) json = File.ReadAllText(cached);
            if (json == null)
            {
                var r = System.Windows.Application.GetResourceStream(new Uri($"pack://application:,,,/addons/{ModlistId}-{kind}.json"));
                if (r == null) return null;
                using var sr = new StreamReader(r.Stream);
                json = sr.ReadToEnd();
            }
            return JsonSerializer.Deserialize<Pack>(json, Json);
        }
        catch (Exception ex) { AppLog.Write($"gta pack {kind}: {ex.Message}"); return null; }
    }

    // ---------------------------------------------------------------- the game folder

    /// <summary>NaturalVision Enhanced is installed (its loader and its update pack are in the game folder).</summary>
    public static bool HasNve(string game) =>
        File.Exists(Path.Combine(game, "NVE.asi")) && File.Exists(Path.Combine(game, "onigiri.asi"))
        && Directory.Exists(Path.Combine(game, "onigiri", "dlcpacks", "NVE_update"));

    private static string OursDir(string game) => Path.Combine(game, "_FunkyPlay");
    private static string InstalledList(string game) => Path.Combine(OursDir(game), "installed.txt");
    private static string BackupDir(string game) => Path.Combine(OursDir(game), "backup");

    public static bool IsInstalled(string game) => File.Exists(InstalledList(game));

    /// <summary>{gameRoot}, {downloads}, {nested} and the NVE / standard table of the main pack.</summary>
    private static string Expand(string path, Pack main, string game, string downloads, bool nve)
    {
        var p = path.Replace("{gameRoot}", game).Replace("{downloads}", downloads);
        if (main.Tokens.TryGetValue(nve ? "nve" : "std", out var t))
            foreach (var (k, v) in t) p = p.Replace(k, v);
        p = p.Replace('/', '\\');
        return Path.IsPathRooted(p) ? Path.GetFullPath(p) : Path.GetFullPath(Path.Combine(game, p));
    }

    // ---------------------------------------------------------------- plan

    public sealed record ManualStep(Mod Mod, bool Done, bool Required);

    public sealed class Plan
    {
        public List<Mod> Install { get; } = new();
        public List<ManualStep> Manual { get; } = new();
        public List<Merge> Merges { get; } = new();
        public bool Nve { get; init; }
        public Pack Main { get; init; } = new();
        /// <summary>Required manual steps not done yet (Script Hook V; NVE for High/Ultra): the install cannot start.</summary>
        public IEnumerable<ManualStep> Blocking => Manual.Where(x => x.Required && !x.Done);
    }

    /// <summary>What a version (0 Standard, 1 High, 2 Ultra) installs into this game folder.</summary>
    public static Plan? MakePlan(string game, string downloads, int level)
    {
        if (LoadPack("main") is not { } main) return null;
        var packs = new List<Pack> { main };
        if (level >= 1 && LoadPack("high") is { } high) packs.Add(high);
        if (level >= 2 && LoadPack("heavy") is { } heavy) packs.Add(heavy);
        var nve = HasNve(game);
        var plan = new Plan { Nve = nve, Main = main };
        foreach (var x in packs.SelectMany(p => p.Mods))
        {
            if (x.OnHold) continue;                                    // waiting for the author's answer (Drive V, Deformation)
            if (x.When == "nve" && !nve || x.When == "std" && nve) continue;
            if (x.IsManual && x.UserArchive == null)
            {
                var done = x.Install.Check.Count > 0 && x.Install.Check.All(c => CheckOne(c, main, game, downloads, nve));
                plan.Manual.Add(new ManualStep(x, done, x.Required));
                continue;
            }
            if (x.Default == false) continue;                          // optional, shown as a link only
            plan.Install.Add(x);
        }
        plan.Merges.AddRange(main.Merge.Where(mg => !string.IsNullOrEmpty(mg.Copy) && (mg.When == null || (mg.When == "nve") == nve)));
        return plan;
    }

    private static bool CheckOne(string check, Pack main, string game, string downloads, bool nve)
    {
        var p = Expand(check, main, game, downloads, nve);
        if (p.Contains('*'))
        {
            var dir = Path.GetDirectoryName(p)!;
            return Directory.Exists(dir) && Directory.EnumerateFiles(dir, Path.GetFileName(p)).Any();
        }
        return File.Exists(p) || Directory.Exists(p);
    }

    // ---------------------------------------------------------------- install

    public sealed record Result(int Installed, List<ManualItem> NeedManual, List<string> Errors, List<string> Skipped);

    /// <summary>
    /// Downloads what is missing (Nexus through the player's login, GitHub directly), checks every archive, lays the
    /// files into the game folder and writes the settings. Files from Nexus that need "Slow download" come back in
    /// <see cref="Result.NeedManual"/> — the caller shows the download window and runs this again.
    /// </summary>
    public static async Task<Result> InstallAsync(string game, string downloads, Plan plan, IProgress<string> progress, CancellationToken token)
    {
        Directory.CreateDirectory(downloads);
        Directory.CreateDirectory(OursDir(game));
        var log = new InstallLog(game);
        var manual = new List<ManualItem>();
        var errors = new List<string>();
        var skipped = new List<string>();
        var done = 0;
        var work = Path.Combine(Path.GetTempPath(), "fl-gta-unpack");
        string? dispatchDir = null;

        for (var i = 0; i < plan.Install.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            var x = plan.Install[i];
            var name = string.IsNullOrEmpty(x.DisplayName) ? x.Id : x.DisplayName;
            try
            {
                string archive = "";
                string? looseDir = null;
                if (x.Download is { Urls.Count: > 0 } loose && string.IsNullOrEmpty(x.Archive.LocalFileName))
                {
                    // single files from GitHub (ReShade effects pinned to a commit): no archive, kept in downloads\<mod>
                    looseDir = Path.Combine(downloads, Safe(x.ModFolder));
                    Directory.CreateDirectory(looseDir);
                    progress.Report(Loc.F("addon.downloading", i + 1, plan.Install.Count, name));
                    var failed = false;
                    foreach (var u in loose.Urls)
                    {
                        var f = Path.Combine(looseDir, Uri.UnescapeDataString(u[(u.LastIndexOf('/') + 1)..]));
                        if (!File.Exists(f) && !await DownloadUrlAsync(u, f, 0, token)) { failed = true; break; }
                    }
                    if (failed) { errors.Add(Loc.F("addon.err.unpack", name, "download failed")); continue; }
                }
                else if (x.UserArchive != null)
                {
                    // a file the player downloads himself (Dispatch V: its mirror page must not be scraped, Legal 24.11)
                    var pattern = x.Install.Check.FirstOrDefault() is { } c ? Expand(c, plan.Main, game, downloads, plan.Nve) : null;
                    var found = pattern == null ? null : Directory.Exists(Path.GetDirectoryName(pattern)!)
                        ? Directory.EnumerateFiles(Path.GetDirectoryName(pattern)!, Path.GetFileName(pattern)).FirstOrDefault() : null;
                    if (found == null) { skipped.Add(name); continue; }
                    progress.Report(Loc.F("addon.checking", i + 1, plan.Install.Count, name));
                    if (!string.Equals(await AddonService.Sha256Async(found, token), x.UserArchive.Sha256, StringComparison.OrdinalIgnoreCase))
                    { errors.Add(Loc.F("addon.err.hash", name)); continue; }
                    archive = found;
                }
                else
                {
                    archive = Path.Combine(downloads, x.Archive.LocalFileName);
                    if (!ManualQueue.IsPresent(downloads, x.Archive.LocalFileName, x.Archive.Size))
                    {
                        progress.Report(Loc.F("addon.downloading", i + 1, plan.Install.Count, name));
                        var ok = x.Download is { Url.Length: > 0 } d
                            ? await DownloadUrlAsync(d.Url, archive, x.Archive.Size, token)
                            : x.Nexus.ModId > 0 && await AddonService.DownloadAsync(new AddonMod { Nexus = x.Nexus, Archive = x.Archive }, archive, token);
                        if (!ok)
                        {
                            if (x.Nexus.ModId > 0)
                                manual.Add(new ManualItem { ArchiveName = x.Archive.LocalFileName, Size = x.Archive.Size, ModName = name,
                                    GameDomain = x.Nexus.Game, ModId = x.Nexus.ModId, FileId = x.Nexus.FileId });
                            else errors.Add(Loc.F("addon.err.unpack", name, "download failed"));
                            continue;
                        }
                    }
                    progress.Report(Loc.F("addon.checking", i + 1, plan.Install.Count, name));
                    if (!string.IsNullOrEmpty(x.Archive.Sha256)
                        && !string.Equals(await AddonService.Sha256Async(archive, token), x.Archive.Sha256, StringComparison.OrdinalIgnoreCase))
                    { errors.Add(Loc.F("addon.err.hash", name)); continue; }
                }

                progress.Report(Loc.F("addon.unpacking", i + 1, plan.Install.Count, name));
                var tmp = looseDir ?? Path.Combine(work, Safe(x.ModFolder));
                if (looseDir == null)
                {
                    if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
                    Directory.CreateDirectory(tmp);
                    await Archives.ExtractAsync(archive, tmp, token);
                }
                var map = x.UserArchive != null ? x.MapAfterPick : x.Install.Map;
                foreach (var pair in map)
                {
                    if (pair.Count < 2) continue;
                    var src = Resolve(tmp, pair[0]);
                    if (pair[1].StartsWith("{nested}/", StringComparison.Ordinal))
                    {
                        var key = pair[1]["{nested}/".Length..];
                        if (File.Exists(src) && x.Nested.TryGetValue(key, out var inner)) LayNested(src, inner, plan, game, downloads, log, x.NoOverwrite);
                        else errors.Add(Loc.F("addon.err.unpack", name, "nested " + key));
                        continue;
                    }
                    if (pair[1].StartsWith("{merge}/", StringComparison.Ordinal)) continue; // inputs of a paused merge
                    var dst = Expand(pair[1], plan.Main, game, downloads, plan.Nve);
                    if (File.Exists(src)) log.Put(src, dst, x.NoOverwrite);
                    else if (Directory.Exists(src))
                        foreach (var f in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
                            log.Put(f, Path.Combine(dst, Path.GetRelativePath(src, f)), x.NoOverwrite);
                    else errors.Add(Loc.F("addon.err.unpack", name, "missing " + pair[0]));
                }
                ApplySettings(x, plan, game);
                if (x.UserArchive != null) dispatchDir = tmp; else if (looseDir == null) TryDeleteDir(tmp);
                done++;
            }
            catch (OperationCanceledException) { log.Save(); throw; }
            catch (Exception ex) { errors.Add(Loc.F("addon.err.unpack", name, ex.Message)); }
        }

        // simple merges: a file of the player's Dispatch V copy laid down as is ("gameconfig-std")
        foreach (var mg in plan.Merges)
        {
            var parts = mg.Copy!.Split("->", 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || dispatchDir == null) continue;
            var src = Path.Combine(dispatchDir, parts[0].Replace("Dispatch ", "", StringComparison.Ordinal).Replace('/', '\\'));
            if (File.Exists(src)) log.Put(src, Expand(parts[1], plan.Main, game, downloads, plan.Nve), false);
            else errors.Add($"merge {mg.Id}: missing {parts[0]}");
        }
        if (dispatchDir != null) TryDeleteDir(dispatchDir);
        TryDeleteDir(work);

        // BattlEye blocks script mods in story mode: start without it (args.txt is read by the game itself)
        log.AppendLine(Path.Combine(game, "args.txt"), "-nobattleye");
        log.Save();
        // only a complete install may clean up what the previous version had (a half one would lose files still to come)
        var pruned = errors.Count == 0 && manual.Count == 0 && skipped.Count == 0 ? log.Prune() : 0;
        if (pruned > 0) RemoveEmptyDirs(game);
        AppLog.Write($"gta install: {done}/{plan.Install.Count}, manual {manual.Count}, errors {errors.Count}, skipped {skipped.Count}, old files removed {pruned}");
        return new Result(done, manual, errors, skipped);
    }

    private static void LayNested(string oiv, List<List<string>> map, Plan plan, string game, string downloads, InstallLog log, bool noOverwrite)
    {
        using var zip = ZipFile.OpenRead(oiv);
        foreach (var pair in map)
        {
            if (pair.Count < 2) continue;
            var e = zip.Entries.FirstOrDefault(z => string.Equals(z.FullName.Replace('\\', '/'), pair[0], StringComparison.OrdinalIgnoreCase));
            if (e == null) throw new FileNotFoundException(pair[0]);
            var tmp = Path.Combine(Path.GetTempPath(), "fl-gta-" + Guid.NewGuid().ToString("N"));
            e.ExtractToFile(tmp);
            try { log.Put(tmp, Expand(pair[1], plan.Main, game, downloads, plan.Nve), noOverwrite); }
            finally { try { File.Delete(tmp); } catch { } }
        }
    }

    private static async Task<bool> DownloadUrlAsync(string url, string file, long size, CancellationToken token)
    {
        var part = file + ".part";
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("FunkyLauncher");
            using (var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token))
            {
                if (!resp.IsSuccessStatusCode) return false;
                await using var src = await resp.Content.ReadAsStreamAsync(token);
                await using var dst = File.Create(part);
                await src.CopyToAsync(dst, token);
            }
            if (size > 0 && new FileInfo(part).Length != size) { File.Delete(part); return false; }
            File.Move(part, file, overwrite: true);
            return true;
        }
        catch (OperationCanceledException) { try { File.Delete(part); } catch { } throw; }
        catch (Exception ex) { AppLog.Write($"gta download {url}: {ex.Message}"); try { File.Delete(part); } catch { } return false; }
    }

    // ---------------------------------------------------------------- settings in mod files

    private static readonly Regex NveCond = new(@"\{nve\?([^:}]*):([^}]*)\}", RegexOptions.CultureInvariant);

    private static void ApplySettings(Mod x, Plan plan, string game)
    {
        foreach (var (rel, sections) in x.IniSet)
        {
            var file = FindModFile(x, plan, game, rel);
            if (file == null) { AppLog.Write($"gta ini: {rel} not found"); continue; }
            var lines = File.ReadAllLines(file).ToList();
            foreach (var (section, keys) in sections)
                foreach (var (key, raw) in keys)
                    SetIni(lines, section, key, NveCond.Replace(raw, m => plan.Nve ? m.Groups[1].Value : m.Groups[2].Value));
            File.WriteAllLines(file, lines);
        }
        foreach (var (rel, values) in x.JsonSet)
        {
            var file = FindModFile(x, plan, game, rel);
            if (file == null) { AppLog.Write($"gta json: {rel} not found"); continue; }
            var root = JsonNode.Parse(File.ReadAllText(file));
            if (root == null) continue;
            foreach (var (path, value) in values)
            {
                // "Hotkeys.Teleport Key": the last dot separates the object from the key (keys may have spaces)
                var dot = path.LastIndexOf('.');
                var obj = dot < 0 ? root : root[path[..dot]];
                if (obj is JsonObject o) o[dot < 0 ? path : path[(dot + 1)..]] = JsonNode.Parse(value.GetRawText());
            }
            File.WriteAllText(file, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    /// <summary>The installed copy of a mod's file named by its path under the game folder or by its file name in the map.</summary>
    private static string? FindModFile(Mod x, Plan plan, string game, string rel)
    {
        var direct = Path.Combine(game, rel.Replace('/', '\\'));
        if (File.Exists(direct)) return direct;
        foreach (var pair in x.Install.Map)
            if (pair.Count >= 2 && pair[1].EndsWith("/" + rel, StringComparison.OrdinalIgnoreCase))
            {
                var p = Expand(pair[1], plan.Main, game, "", plan.Nve);
                if (File.Exists(p)) return p;
            }
        return null;
    }

    /// <summary>Only the value after "=" changes; a comment after ";" stays. Section "" = keys before any [section].</summary>
    internal static void SetIni(List<string> lines, string section, string key, string value)
    {
        var inSection = section.Length == 0;
        var lastInSection = section.Length == 0 ? -1 : -2;
        for (var i = 0; i < lines.Count; i++)
        {
            var t = lines[i].Trim();
            if (t.StartsWith('[') && t.EndsWith(']'))
            {
                if (section.Length == 0) break;
                inSection = string.Equals(t[1..^1].Trim(), section, StringComparison.OrdinalIgnoreCase);
                if (inSection) lastInSection = i;
                continue;
            }
            if (!inSection) continue;
            lastInSection = i;
            var eq = lines[i].IndexOf('=');
            if (eq < 0 || !string.Equals(lines[i][..eq].Trim(), key, StringComparison.OrdinalIgnoreCase)) continue;
            var rest = lines[i][(eq + 1)..];
            var semi = rest.IndexOf(';');
            var comment = semi >= 0 ? " " + rest[semi..].TrimStart() : "";
            lines[i] = lines[i][..(eq + 1)] + (lines[i][(eq + 1)..].StartsWith(' ') ? " " : "") + value + comment;
            return;
        }
        // not there yet: add it at the end of the section (or a new section at the end)
        if (lastInSection >= -1) lines.Insert(lastInSection + 1, $"{key}={value}");
        else { lines.Add($"[{section}]"); lines.Add($"{key}={value}"); }
    }

    // ---------------------------------------------------------------- restore

    /// <summary>"Restore clean game": removes every file we laid down and puts back what we replaced.</summary>
    public static (int Removed, int Restored, List<string> Errors) Restore(string game)
    {
        var errors = new List<string>();
        int removed = 0, restored = 0;
        var list = InstalledList(game);
        if (File.Exists(list))
            foreach (var line in File.ReadAllLines(list).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var rel = line.StartsWith("+ ") ? line[2..] : line.StartsWith("a ") ? null : line;
                if (rel == null || rel.Length == 0) continue;
                var f = Path.Combine(game, rel);
                try { if (File.Exists(f)) { File.Delete(f); removed++; } } catch (Exception ex) { errors.Add(rel + ": " + ex.Message); }
            }
        var backup = BackupDir(game);
        if (Directory.Exists(backup))
            foreach (var f in Directory.EnumerateFiles(backup, "*", SearchOption.AllDirectories))
            {
                var dst = Path.Combine(game, Path.GetRelativePath(backup, f));
                try { Directory.CreateDirectory(Path.GetDirectoryName(dst)!); File.Copy(f, dst, true); restored++; }
                catch (Exception ex) { errors.Add(dst + ": " + ex.Message); }
            }
        // lines we appended to the game's own text files (args.txt)
        if (File.Exists(list))
            foreach (var line in File.ReadAllLines(list).Where(l => l.StartsWith("a ")))
            {
                var parts = line[2..].Split('\t', 2);
                if (parts.Length != 2) continue;
                var f = Path.Combine(game, parts[0]);
                try
                {
                    if (!File.Exists(f)) continue;
                    var keep = File.ReadAllLines(f).Where(l => l.Trim() != parts[1]).ToList();
                    if (keep.Count == 0) File.Delete(f); else File.WriteAllLines(f, keep);
                }
                catch (Exception ex) { errors.Add(parts[0] + ": " + ex.Message); }
            }
        if (errors.Count == 0)
        {
            RemoveEmptyDirs(game);
            TryDeleteDir(OursDir(game));
        }
        AppLog.Write($"gta restore: removed {removed}, restored {restored}, errors {errors.Count}");
        return (removed, restored, errors);
    }

    private static void RemoveEmptyDirs(string game)
    {
        // only the folders mods create (never the game's own: those always hold files)
        foreach (var top in new[] { "mods", "scripts", "RampageFiles", "reshade-shaders" })
        {
            var d = Path.Combine(game, top);
            if (!Directory.Exists(d)) continue;
            foreach (var sub in Directory.EnumerateDirectories(d, "*", SearchOption.AllDirectories).OrderByDescending(s => s.Length).Append(d))
                try { if (!Directory.EnumerateFileSystemEntries(sub).Any()) Directory.Delete(sub); } catch { }
        }
    }

    // ---------------------------------------------------------------- the file journal

    /// <summary>installed.txt: "+ rel" = a file we created or replaced (the replaced original is in backup\rel); "a rel\tline" = a line we appended.</summary>
    private sealed class InstallLog
    {
        private readonly string _game;
        private readonly List<string> _lines = new();
        private readonly HashSet<string> _known;

        public InstallLog(string game)
        {
            _game = game;
            _known = File.Exists(InstalledList(game))
                ? new HashSet<string>(File.ReadAllLines(InstalledList(game)), StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        public void Put(string src, string dst, bool noOverwrite)
        {
            var root = Path.GetFullPath(_game).TrimEnd('\\') + "\\";
            if (!dst.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("outside the game folder: " + dst);
            var rel = dst[root.Length..];
            var line = "+ " + rel;
            var ours = _known.Contains(line);
            if (File.Exists(dst))
            {
                if (noOverwrite && !ours) return;                     // the player's / NVE's own file stays
                // NaturalVision's own files are never replaced (Legal 25.11): a file of ours that meets one there stops
                if (!ours && rel.StartsWith("onigiri\\", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(Loc.F("gta.err.nvefile", rel));
                if (!ours)
                {
                    var bak = Path.Combine(BackupDir(_game), rel);
                    if (!File.Exists(bak)) { Directory.CreateDirectory(Path.GetDirectoryName(bak)!); File.Copy(dst, bak); }
                }
            }
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.Copy(src, dst, true);
            _written.Add(line);
            if (_known.Add(line)) _lines.Add(line);
        }

        private readonly HashSet<string> _written = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// After a complete install: files of ours from an earlier install that this version no longer has (another
        /// version picked, NVE added or removed — SML is DSOUND.dll without NVE and SimpleModsLoader.asi with it, GTA
        /// chat 25.11) are removed, and a game file they had replaced comes back from the backup.
        /// </summary>
        public int Prune()
        {
            Save();
            var list = InstalledList(_game);
            if (!File.Exists(list)) return 0;
            var keep = new List<string>();
            var removed = 0;
            foreach (var line in File.ReadAllLines(list).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!line.StartsWith("+ ") || _written.Contains(line)) { keep.Add(line); continue; }
                var rel = line[2..];
                var f = Path.Combine(_game, rel);
                var bak = Path.Combine(BackupDir(_game), rel);
                try
                {
                    if (File.Exists(bak)) { File.Copy(bak, f, true); File.Delete(bak); }
                    else if (File.Exists(f)) File.Delete(f);
                    removed++;
                }
                catch (Exception ex) { AppLog.Write($"gta prune {rel}: {ex.Message}"); keep.Add(line); }
            }
            File.WriteAllLines(list, keep);
            _known.Clear();
            foreach (var l in keep) _known.Add(l);
            return removed;
        }

        public void AppendLine(string file, string text)
        {
            var root = Path.GetFullPath(_game).TrimEnd('\\') + "\\";
            var rel = Path.GetFullPath(file)[root.Length..];
            if (File.Exists(file) && File.ReadAllLines(file).Any(l => l.Trim() == text)) return;
            var old = File.Exists(file) ? File.ReadAllText(file) : "";
            File.AppendAllText(file, (old.Length > 0 && !old.EndsWith('\n') ? Environment.NewLine : "") + text + Environment.NewLine);
            var line = $"a {rel}\t{text}";
            if (_known.Add(line)) _lines.Add(line);
        }

        public void Save()
        {
            if (_lines.Count == 0) return;
            Directory.CreateDirectory(OursDir(_game));
            File.AppendAllLines(InstalledList(_game), _lines);
            _lines.Clear();
        }
    }

    /// <summary>
    /// A path inside the unpacked archive. Archive names may carry accents the list lost ("Mapa Satélite" vs
    /// "Mapa Satelite"): when the exact path is missing, each folder is matched ignoring case and accents.
    /// </summary>
    private static string Resolve(string root, string rel)
    {
        var exact = Path.Combine(root, rel.Replace('/', '\\'));
        if (File.Exists(exact) || Directory.Exists(exact)) return exact;
        var cur = root;
        foreach (var part in rel.Split('/', '\\', StringSplitOptions.RemoveEmptyEntries))
        {
            var direct = Path.Combine(cur, part);
            if (File.Exists(direct) || Directory.Exists(direct)) { cur = direct; continue; }
            if (!Directory.Exists(cur)) return exact;
            var key = Plain(part);
            var hit = Directory.EnumerateFileSystemEntries(cur).FirstOrDefault(e => Plain(Path.GetFileName(e)) == key);
            if (hit == null) return exact;
            cur = hit;
        }
        return cur;
    }

    private static string Plain(string s) =>
        string.Concat(s.Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark))
            .ToLowerInvariant();

    private static string Safe(string s) => string.Concat(s.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    private static void TryDeleteDir(string d) { try { if (Directory.Exists(d)) Directory.Delete(d, true); } catch { } }
}
