// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Net.Http;
using System.Text.Json;
using FunkyLauncher.Core;

namespace FunkyLauncher.Services;

/// <summary>
/// 0.1.19 "back to the previous version" for modlists the launcher installs itself (Neon / Atomic / Nova Nights). They
/// update in place, so before an update the old state of everything the update changes is kept in
/// &lt;install&gt;\_rollback: replaced mod folders (moved, not copied — same disk, no extra space), the profile files
/// (modlist / plugins / load order), our own files and the lists the old version was installed from. "Back to version X"
/// swaps the two states — nothing is downloaded; the same button then goes forward again. One previous version is kept:
/// the next update starts a new one. While the player is on the older version, the launcher keeps using that version's
/// lists (toggling Russian / 18+ never writes the newer order into the profile); updating again is only by the button.
/// </summary>
public static class ModlistRollback
{
    public const string StoreName = "_rollback";
    /// <summary>The lists (main, packs, order) the current install was made from: written after every successful install.</summary>
    public const string ListsName = "_funky-lists";

    private static readonly string[] ListKinds = { "main", "order", "ru", "adult", "hd", "upscaling", "voice" };
    private static readonly string[] ProfileFiles = { "modlist.txt", "plugins.txt", "loadorder.txt", "lockedorder.txt", "settings.ini" };

    /// <summary>The modlist being updated right now (its old state goes into the store); null when none.</summary>
    private static string? _updating;
    private static string _root = "";

    public sealed class Info
    {
        public string Version { get; set; } = "";
        public DateTime Date { get; set; }
        public bool Lists { get; set; }
    }

    private static string Store(string installDir) => Path.Combine(installDir, StoreName);
    private static string Items(string installDir) => Path.Combine(Store(installDir), "items");
    private static string Index(string installDir) => Path.Combine(Store(installDir), "items.txt");
    private static string Absent(string installDir) => Path.Combine(Store(installDir), "absent.txt");
    private static string InfoFile(string installDir) => Path.Combine(Store(installDir), "info.json");

    /// <summary>The version kept for "back to version X" (null when there is none).</summary>
    public static Info? Kept(string installDir)
    {
        try { return File.Exists(InfoFile(installDir)) ? JsonSerializer.Deserialize<Info>(File.ReadAllText(InfoFile(installDir))) : null; }
        catch { return null; }
    }

    // ---------- the lists of the installed version ----------

    /// <summary>
    /// For <see cref="Addons"/>: the folder with the lists of the version actually installed, when it is not the
    /// catalog's version (the player went back, or has not updated yet). Null = use the current lists.
    /// </summary>
    public static string? InstalledLists(Settings s, Modlist m)
    {
        if (_updating == m.Id || !Mo2Profile.IsNexusList(m) || !s.IsInstalled(m)) return null;
        if (s.InstalledVersion(m) is not { Length: > 0 } v || v == m.Version) return null;
        var dir = Path.Combine(s.InstallDir(m), ListsName);
        return Directory.Exists(dir) ? dir : null;
    }

    /// <summary>After a successful install: the lists it was made from are kept next to it.</summary>
    public static void SnapshotLists(Settings s, Modlist m)
    {
        try
        {
            var dir = Path.Combine(s.InstallDir(m), ListsName);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            foreach (var kind in ListKinds)
                if (Addons.RawJson(m.Id, kind) is { } json) File.WriteAllText(Path.Combine(dir, $"{m.Id}-{kind}.json"), json);
        }
        catch (Exception ex) { AppLog.Write("rollback: lists not kept: " + ex.Message); }
    }

    // ---------- during an update ----------

    /// <summary>Starts an update from <paramref name="fromVersion"/>: a fresh store with the profile files and the old lists.</summary>
    public static async Task BeginAsync(Settings s, Modlist m, string fromVersion, CancellationToken token)
    {
        var dir = s.InstallDir(m);
        _root = Path.GetFullPath(dir).TrimEnd('\\');
        try
        {
            if (Directory.Exists(Store(dir))) Directory.Delete(Store(dir), true); // one previous version is kept
            Directory.CreateDirectory(Items(dir));
            foreach (var p in Directory.Exists(Path.Combine(dir, "profiles")) ? Directory.GetDirectories(Path.Combine(dir, "profiles")) : Array.Empty<string>())
                foreach (var f in ProfileFiles)
                    SaveFile(Path.Combine("profiles", Path.GetFileName(p), f));
            SaveFile("funkylauncher-mods.txt");
            // the lists of the old version: kept since 0.1.19; an older install — taken from the repository's history
            var lists = Path.Combine(dir, ListsName);
            if (!Directory.Exists(lists)) await FetchOldListsAsync(m, fromVersion, lists, token);
            var hasLists = Directory.Exists(lists);
            if (hasLists) MoveIn(ListsName, lists); else MarkAbsent(ListsName);
            File.WriteAllText(InfoFile(dir), JsonSerializer.Serialize(new Info { Version = fromVersion, Date = DateTime.Now, Lists = hasLists }));
            _updating = m.Id;
            AppLog.Write($"rollback: keeping {m.Id} {fromVersion} in {Store(dir)} (lists: {hasLists})");
        }
        catch (Exception ex)
        {
            AppLog.Write("rollback: not started: " + ex.Message);
            _updating = null;
            try { if (Directory.Exists(Store(dir))) Directory.Delete(Store(dir), true); } catch { }
        }
    }

    /// <summary>The update finished (any outcome): the store stays; a successful one gets its own lists kept.</summary>
    public static void End(Settings s, Modlist m, bool success)
    {
        if (_updating != m.Id) { if (success) SnapshotLists(s, m); return; }
        _updating = null;
        if (success) SnapshotLists(s, m);
    }

    private static bool Active(string installDir) =>
        _updating != null && string.Equals(Path.GetFullPath(installDir).TrimEnd('\\'), _root, StringComparison.OrdinalIgnoreCase);

    /// <summary>A mod folder the update replaced: the old one goes into the store instead of being deleted.</summary>
    public static void KeepMod(string modsDir, string modFolder, string oldDir)
    {
        var installDir = Path.GetDirectoryName(Path.GetFullPath(modsDir).TrimEnd('\\'))!;
        if (Active(installDir) && Directory.Exists(oldDir))
            try { MoveIn(Path.Combine("mods", modFolder), oldDir); return; }
            catch (Exception ex) { AppLog.Write($"rollback: {modFolder} not kept: {ex.Message}"); }
        if (Directory.Exists(oldDir)) Directory.Delete(oldDir, true);
    }

    /// <summary>A mod folder the update added (the old version did not have it).</summary>
    public static void AddedMod(string modsDir, string modFolder)
    {
        var installDir = Path.GetDirectoryName(Path.GetFullPath(modsDir).TrimEnd('\\'))!;
        if (Active(installDir)) MarkAbsent(Path.Combine("mods", modFolder));
    }

    /// <summary>A file of ours the update is about to write or patch (own files, deltas): its old content is kept.</summary>
    public static void BeforeWrite(string installDir, string file)
    {
        if (!Active(installDir)) return;
        var rel = Path.GetRelativePath(_root, Path.GetFullPath(file));
        if (rel.StartsWith("..")) return;
        SaveFile(rel);
    }

    // ---------- the store: first write wins (it is the old version's state) ----------

    private static HashSet<string> ReadSet(string file) =>
        File.Exists(file) ? File.ReadAllLines(file).Where(l => l.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase) : new(StringComparer.OrdinalIgnoreCase);

    private static bool Known(string rel) => ReadSet(Index(_root)).Contains(rel) || ReadSet(Absent(_root)).Contains(rel);

    private static void SaveFile(string rel)
    {
        if (Known(rel)) return;
        var src = Path.Combine(_root, rel);
        if (!File.Exists(src)) { MarkAbsent(rel); return; }
        var dst = Path.Combine(Items(_root), rel);
        Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
        File.Copy(src, dst, true);
        File.AppendAllLines(Index(_root), new[] { rel });
    }

    private static void MoveIn(string rel, string from)
    {
        if (Known(rel)) { if (Directory.Exists(from)) Directory.Delete(from, true); return; }
        var dst = Path.Combine(Items(_root), rel);
        Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
        Directory.Move(from, dst);
        File.AppendAllLines(Index(_root), new[] { rel });
    }

    private static void MarkAbsent(string rel)
    {
        if (Known(rel)) return;
        File.AppendAllLines(Absent(_root), new[] { rel });
    }

    // ---------- back (and forward again) ----------

    /// <summary>
    /// Swaps the installed version with the kept one. Throws with a readable text if the game or MO2 is running.
    /// Returns the version now installed.
    /// </summary>
    public static string Swap(Settings s, Modlist m)
    {
        if (AddonService.Mo2Running(s, m) || BackupService.GameRunning(m)) throw new InvalidOperationException(Loc.T("addon.err.mo2"));
        var dir = Path.GetFullPath(s.InstallDir(m)).TrimEnd('\\');
        var info = Kept(dir) ?? throw new InvalidOperationException("no kept version");
        var current = s.InstalledVersion(m) ?? "";
        var items = Items(dir);
        var tmp = Path.Combine(Store(dir), "items-swap");
        if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
        Directory.CreateDirectory(tmp);
        var index = ReadSet(Index(dir));
        var absent = ReadSet(Absent(dir));
        var newIndex = new List<string>();
        var newAbsent = new List<string>();
        void Move(string from, string to)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            if (Directory.Exists(from)) Directory.Move(from, to); else File.Move(from, to);
        }
        bool Exists(string p) => Directory.Exists(p) || File.Exists(p);
        // what only the current version has goes into the store
        foreach (var rel in absent)
            if (Exists(Path.Combine(dir, rel))) { Move(Path.Combine(dir, rel), Path.Combine(tmp, rel)); newIndex.Add(rel); }
        // the kept version's state comes back; its counterpart goes into the store
        foreach (var rel in index)
        {
            var cur = Path.Combine(dir, rel);
            if (Exists(cur)) { Move(cur, Path.Combine(tmp, rel)); newIndex.Add(rel); }
            else newAbsent.Add(rel);
            Move(Path.Combine(items, rel), cur);
        }
        Directory.Delete(items, true);
        Directory.Move(tmp, items);
        File.WriteAllLines(Index(dir), newIndex);
        File.WriteAllLines(Absent(dir), newAbsent);
        var hasLists = Directory.Exists(Path.Combine(items, ListsName));
        File.WriteAllText(InfoFile(dir), JsonSerializer.Serialize(new Info { Version = current, Date = DateTime.Now, Lists = hasLists }));
        s.MarkInstalled(m, dir, info.Version);
        AppLog.Write($"rollback: {m.Id} {current} → {info.Version} ({index.Count} back, {absent.Count} put aside)");
        return info.Version;
    }

    /// <summary>"Delete the previous version": the store goes away (the current version is not touched).</summary>
    public static void Forget(Settings s, Modlist m)
    {
        var store = Store(s.InstallDir(m));
        if (Directory.Exists(store)) Directory.Delete(store, true);
    }

    // ---------- lists of an install made before 0.1.19 ----------

    /// <summary>Test hook: the repository the lists' history is read from.</summary>
    internal static string Repo = "FunkyPlay/FunkyLauncher";

    /// <summary>
    /// The newest commit of the online catalog that still says <paramref name="version"/> for this modlist; the lists of
    /// that commit are the ones the old version was installed from. Nothing found / offline = no lists (the rollback still
    /// swaps the mod folders and the profile).
    /// </summary>
    private static async Task FetchOldListsAsync(Modlist m, string version, string into, CancellationToken token)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("FunkyLauncher/" + AppConfig.Version);
            var commits = JsonDocument.Parse(await http.GetStringAsync($"https://api.github.com/repos/{Repo}/commits?path=catalog2.json&per_page=60", token)).RootElement;
            foreach (var c in commits.EnumerateArray().Take(60))
            {
                var sha = c.GetProperty("sha").GetString()!;
                var cat = JsonDocument.Parse(await http.GetStringAsync($"https://raw.githubusercontent.com/{Repo}/{sha}/catalog2.json", token)).RootElement;
                var lists = cat.ValueKind == JsonValueKind.Array ? cat : cat.GetProperty("modlists");
                var ml = lists.EnumerateArray().FirstOrDefault(x => x.TryGetProperty("id", out var id) && id.GetString() == m.Id);
                if (ml.ValueKind != JsonValueKind.Object || !ml.TryGetProperty("version", out var v) || v.GetString() != version) continue;
                Directory.CreateDirectory(into);
                foreach (var kind in ListKinds)
                    try
                    {
                        var json = await http.GetStringAsync($"https://raw.githubusercontent.com/{Repo}/{sha}/addons/{Uri.EscapeDataString($"{m.Id}-{kind}.json")}", token);
                        File.WriteAllText(Path.Combine(into, $"{m.Id}-{kind}.json"), json);
                    }
                    catch (HttpRequestException) { /* no such pack in that version */ }
                if (!File.Exists(Path.Combine(into, $"{m.Id}-main.json"))) { Directory.Delete(into, true); return; }
                AppLog.Write($"rollback: lists of {m.Id} {version} taken from {sha[..7]}");
                return;
            }
            AppLog.Write($"rollback: no lists of {m.Id} {version} in the history");
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { AppLog.Write("rollback: old lists not found: " + ex.Message); }
    }
}
