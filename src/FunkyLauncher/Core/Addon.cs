// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FunkyLauncher.Core;

/// <summary>
/// An optional add-on for a modlist (today: the 18+ add-on for Neon Nights). It is a plain list of Nexus files:
/// the launcher downloads them under the player's own Nexus login, unpacks them into the modlist's MO2 "mods"
/// folder and switches them on in modlist.txt. Built into the launcher as addons/&lt;modlistId&gt;-adult.json.
/// </summary>
public sealed class AddonFile
{
    [JsonPropertyName("schema")] public int Schema { get; set; }
    [JsonPropertyName("modlistId")] public string ModlistId { get; set; } = "";
    [JsonPropertyName("addonId")] public string AddonId { get; set; } = "";
    /// <summary>The Russian pack names its id "packId"; it is used as the add-on id.</summary>
    [JsonPropertyName("packId")] public string PackId { set { if (AddonId.Length == 0) AddonId = value; } }
    [JsonPropertyName("listVersion")] public string ListVersion { get; set; } = "";
    [JsonPropertyName("forMainVersion")] public string ForMainVersion { get; set; } = "";
    /// <summary>Optional packs (HD, upscaling): ticked by default for a new install. Never used for 18+.</summary>
    [JsonPropertyName("default")] public bool? Default { get; set; }
    /// <summary>0.1.22 (Neon 0.1.8): part of the pack is translated by AI (with the authors' permission) — the launcher says so.</summary>
    [JsonPropertyName("aiTranslated")] public bool AiTranslated { get; set; }
    /// <summary>INI values set in this pack's mod files while the pack is on (e.g. frame generation off by default).</summary>
    [JsonPropertyName("filePatches")] public List<FilePatch> FilePatches { get; set; } = new();
    [JsonPropertyName("mods")] public List<AddonMod> Mods { get; set; } = new();
}

public sealed class AddonMod
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("level")] public int Level { get; set; } = 1;
    /// <summary>A mod we decided not to ship (kept in the file for the record).</summary>
    [JsonPropertyName("blocked")] public bool Blocked { get; set; }
    /// <summary>Neutral name for the screen — shown only after the age confirmation.</summary>
    [JsonPropertyName("displayName")] public LocText DisplayName { get; set; } = new();
    [JsonPropertyName("nexus")] public AddonNexus Nexus { get; set; } = new();
    [JsonPropertyName("archive")] public AddonArchive Archive { get; set; } = new();
    [JsonPropertyName("modFolder")] public string ModFolder { get; set; } = "";
    [JsonPropertyName("install")] public AddonInstall Install { get; set; } = new();
    [JsonPropertyName("modlist")] public AddonAnchor Modlist { get; set; } = new();
    [JsonPropertyName("dependsOn")] public List<string> DependsOn { get; set; } = new();
    /// <summary>Plugins (.esm/.esp) of the mod — Bethesda games; their order comes from the modlist's order file.</summary>
    [JsonPropertyName("plugins")] public List<string> Plugins { get; set; } = new();
    /// <summary>Mod folders this mod stands in for: switched off while it is on (e.g. the 18+ variant of an outfit mod).</summary>
    [JsonPropertyName("replaces")] public List<string> Replaces { get; set; } = new();
    /// <summary>Only together with that pack (e.g. "adult": a translation of an 18+ mod); installed with its own pack, switched on only then.</summary>
    [JsonPropertyName("onlyWith")] public string? OnlyWith { get; set; }
    /// <summary>0.1.23: only while that pack is OFF (e.g. "ru": a mod with no Russian translation, English version only).</summary>
    [JsonPropertyName("onlyWithout")] public string? OnlyWithout { get; set; }
    /// <summary>A file not on Nexus: the page where the player presses the site's download button (the launcher installs the file).</summary>
    [JsonPropertyName("url")] public string? Url { get; set; }
    /// <summary>Our own small file (a FunkyPlay compatibility patch) built into the launcher: "files/starfield/X.esm".</summary>
    [JsonPropertyName("embedded")] public string? Embedded { get; set; }
    /// <summary>Several files make one mod (e.g. a multi-part RAR + a DLC archive); empty = the single <see cref="Archive"/>.</summary>
    [JsonPropertyName("archives")] public List<AddonArchive> Archives { get; set; } = new();
    /// <summary>"" / "ready" = offered; anything else (e.g. "waitingAuthorPermission") keeps the mod out of the launcher.</summary>
    [JsonPropertyName("status")] public string? Status { get; set; }
}

/// <summary>One INI value the launcher sets in a mod's file after installing it (e.g. a setting that breaks another mod).</summary>
public sealed class FilePatch
{
    [JsonPropertyName("modFolder")] public string ModFolder { get; set; } = "";
    [JsonPropertyName("file")] public string File { get; set; } = "";
    [JsonPropertyName("section")] public string Section { get; set; } = "";
    [JsonPropertyName("key")] public string Key { get; set; } = "";
    [JsonPropertyName("value")] public string Value { get; set; } = "";
    /// <summary>Only while that pack is on (e.g. "ru": the Russian voice-over's archive list).</summary>
    [JsonPropertyName("onlyWith")] public string? OnlyWith { get; set; }
}

public sealed class AddonNexus
{
    [JsonPropertyName("game")] public string Game { get; set; } = "";
    [JsonPropertyName("modId")] public long ModId { get; set; }
    [JsonPropertyName("fileId")] public long FileId { get; set; }
    [JsonPropertyName("filePage")] public string FilePage { get; set; } = "";
}

public sealed class AddonArchive
{
    [JsonPropertyName("localFileName")] public string LocalFileName { get; set; } = "";
    [JsonPropertyName("size")] public long Size { get; set; }
    [JsonPropertyName("sha256")] public string? Sha256 { get; set; } = "";
    /// <summary>A part of a several-archive mod that is a Nexus file of its own (another mod or file id).</summary>
    [JsonPropertyName("nexus")] public AddonNexus? Nexus { get; set; }
}

public sealed class AddonInstall
{
    /// <summary>
    /// "copyAll": the whole archive (or its folder <see cref="From"/>) goes into mods/&lt;modFolder&gt;;
    /// "fomod" / "files": exactly the files in <see cref="Files"/>; "gameRoot": like "files" (the launcher copies them
    /// into the game folder before PLAY, see gameLoaders); "skip": not installed.
    /// </summary>
    [JsonPropertyName("type")] public string Type { get; set; } = "copyAll";
    [JsonPropertyName("expectedTopFolders")] public List<string> ExpectedTopFolders { get; set; } = new();
    /// <summary>Folder inside the archive whose content becomes the mod ("" = the archive root).</summary>
    [JsonPropertyName("from")] public string? From { get; set; }
    /// <summary>Explicit layout: [path in archive, path in the mod folder] — a folder is copied with everything in it.</summary>
    [JsonPropertyName("map")] public List<List<string>> Map { get; set; } = new();
    /// <summary>
    /// Several archives: each one is unpacked into its own folder "0", "1", … and the map's paths start with that number
    /// (two archives may hold the same path in different versions — the map says which one).
    /// </summary>
    [JsonPropertyName("perArchiveFolders")] public bool PerArchiveFolders { get; set; }
}

/// <summary>
/// Load order of a Bethesda modlist (Starfield): every mod folder of the base and all its packs, top = highest
/// priority (MO2 modlist.txt), and every plugin in load order. The launcher writes the profile from it, keeping only
/// what is installed and switched on — so language, 18+ and HD planets need no separate order files.
/// </summary>
public sealed class ModOrder
{
    [JsonPropertyName("modlistId")] public string ModlistId { get; set; } = "";
    [JsonPropertyName("modlist")] public List<string> Modlist { get; set; } = new();
    [JsonPropertyName("plugins")] public List<string> Plugins { get; set; } = new();
    /// <summary>Plugins that stay listed but switched off (e.g. a duplicate .esp that breaks NPC outfits).</summary>
    [JsonPropertyName("pluginsOff")] public List<string> PluginsOff { get; set; } = new();
    /// <summary>Lines of the profile's game INI (StarfieldCustom.ini); "{lang}" becomes ru / en.</summary>
    [JsonPropertyName("ini")] public List<string> Ini { get; set; } = new();
    /// <summary>INI values set in mod files after the install (applied every time the profile is written).</summary>
    [JsonPropertyName("filePatches")] public List<FilePatch> FilePatches { get; set; } = new();
    /// <summary>Broken meshes fixed on the player's PC (Fallout 4): see Services/NifFixer.</summary>
    [JsonPropertyName("nifFixes")] public List<NifFix> NifFixes { get; set; } = new();
    /// <summary>Mod folders that stay switched off in the profile (as in the modlist author's profile).</summary>
    [JsonPropertyName("modlistOff")] public List<string> ModlistOff { get; set; } = new();
    /// <summary>0.1.23: mod folders an earlier version of the modlist had and this one dropped — switched off where they are
    /// still on after an update (Neon 0.1.7: Immersive Third Person next to Cyber Sight). Their files stay.</summary>
    [JsonPropertyName("dropped")] public List<string> Dropped { get; set; } = new();
    /// <summary>0.1.22: mod folders the rest of the modlist stands on — the player cannot switch them off in the launcher.</summary>
    [JsonPropertyName("core")] public List<string> Core { get; set; } = new();
    /// <summary>Our own files (a zip built into the launcher, paths relative to the install folder): profile files, own mods.</summary>
    [JsonPropertyName("ownFiles")] public string OwnFiles { get; set; } = "";
    /// <summary>Mod folders made only of our own files (switched on like the base mods).</summary>
    [JsonPropertyName("ownFolders")] public List<string> OwnFolders { get; set; } = new();
    /// <summary>MO2 separators: empty folders "…_separator" the launcher creates.</summary>
    [JsonPropertyName("separators")] public List<string> Separators { get; set; } = new();
    /// <summary>Binary deltas applied to a mod's file on the player's PC (Wabbajack's OCTODELTA), only to the expected original.</summary>
    [JsonPropertyName("patches")] public List<FilePatchDelta> Patches { get; set; } = new();
    /// <summary>Russian names of items from the mods, made on the player's PC (Services/TranslationService); older launchers ignore this field.</summary>
    [JsonPropertyName("translation")] public TranslationSpec? Translation { get; set; }
}

/// <summary>
/// The "Russian names" mod: a small set of patch plugins the launcher builds on the player's PC from a dictionary
/// (owner plugin + FormID + field -> Russian text). Our own generated files: replaced at every rebuild.
/// </summary>
public sealed class TranslationSpec
{
    /// <summary>Our own mod folder in MO2's "mods" (switched on only for the Russian version).</summary>
    [JsonPropertyName("modFolder")] public string ModFolder { get; set; } = "";
    /// <summary>Names of the generated plugins start with this (they are put at the end of the load order).</summary>
    [JsonPropertyName("prefix")] public string Prefix { get; set; } = "FunkyPlay_RU_Names";
    /// <summary>"starfield" or "fallout4".</summary>
    [JsonPropertyName("gameType")] public string GameType { get; set; } = "";
    /// <summary>Dictionaries: built into the launcher ("files/…") and refreshed online; the hash tells a good file from a broken one.</summary>
    [JsonPropertyName("dicts")] public List<TranslationDict> Dicts { get; set; } = new();
    /// <summary>Authors of the mods whose names we translated and who asked to be named (name + link to the mod page).</summary>
    [JsonPropertyName("credits")] public List<TranslationCredit> Credits { get; set; } = new();
}

public sealed class TranslationDict
{
    [JsonPropertyName("path")] public string Path { get; set; } = "";
    [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
}

public sealed class TranslationCredit
{
    [JsonPropertyName("mod")] public string Mod { get; set; } = "";
    [JsonPropertyName("author")] public string Author { get; set; } = "";
    [JsonPropertyName("url")] public string Url { get; set; } = "";
}

public sealed class FilePatchDelta
{
    [JsonPropertyName("modFolder")] public string ModFolder { get; set; } = "";
    [JsonPropertyName("file")] public string File { get; set; } = "";
    /// <summary>The delta built into the launcher: "files/neon-nights/patches/&lt;id&gt;".</summary>
    [JsonPropertyName("delta")] public string Delta { get; set; } = "";
    /// <summary>Size of the patched file.</summary>
    [JsonPropertyName("size")] public long Size { get; set; }
    /// <summary>SHA-256 of the file the delta is for (as unpacked from the mod's archive) and of the result.</summary>
    [JsonPropertyName("sourceSha256")] public string SourceSha256 { get; set; } = "";
    [JsonPropertyName("resultSha256")] public string ResultSha256 { get; set; } = "";
}

/// <summary>A mesh taken out of a mod's BA2, fixed and saved into our own mod folder <see cref="Into"/>.</summary>
public sealed class NifFix
{
    [JsonPropertyName("into")] public string Into { get; set; } = "";
    [JsonPropertyName("modFolder")] public string ModFolder { get; set; } = "";
    [JsonPropertyName("archive")] public string Archive { get; set; } = "";
    [JsonPropertyName("file")] public string File { get; set; } = "";
}

/// <summary>Where the mod goes in modlist.txt: right above <see cref="InsertAbove"/>, else right below the fallback.</summary>
public sealed class AddonAnchor
{
    [JsonPropertyName("insertAbove")] public string? InsertAbove { get; set; }
    [JsonPropertyName("fallbackInsertBelow")] public string? FallbackInsertBelow { get; set; }
}

/// <summary>What the player chose for an add-on (kept only in settings.json on this PC).</summary>
public sealed class AddonPrefs
{
    public bool On { get; set; }
    public bool Level2 { get; set; }
    /// <summary>When the player last confirmed "I am 18 or older" (local time, nothing is sent anywhere).</summary>
    public DateTime? ConfirmedAt { get; set; }
}

public static class Addons
{
    /// <summary>The 18+ add-on built into this launcher for a modlist, or null.</summary>
    public static AddonFile? Adult(string modlistId) => Load(modlistId, "adult");

    /// <summary>The Russian pack (mod translations on top of the English base), or null if the modlist has none.</summary>
    public static AddonFile? Russian(string modlistId) => Load(modlistId, "ru");

    /// <summary>The base of a modlist installed from a plain Nexus list (Starfield) instead of a .wabbajack file.</summary>
    public static AddonFile? Base(string modlistId) => Load(modlistId, "main");

    /// <summary>"HD planets": big optional textures, off by default.</summary>
    public static AddonFile? Hd(string modlistId) => Load(modlistId, "hd");

    /// <summary>Optional packs shown as a checkbox each (off by default): Starfield "hd" planets, Fallout 4 "upscaling" (DLSS / FSR).</summary>
    /// 0.1.19 "heavy": the heavy mods of a modlist (HD textures…) — on by default ("default": true); off = the light version.
    // "high" (0.1.21): mods of the High and Ultra versions, off in Standard; "heavy" = mods of Ultra only
    public static readonly string[] ExtraKinds = { "hd", "upscaling", "voice", "heavy", "high" };

    public static IEnumerable<(string Kind, AddonFile Pack)> Extras(string modlistId) =>
        ExtraKinds.Select(k => (k, Load(modlistId, k))).Where(x => x.Item2 != null).Select(x => (x.k, x.Item2!));

    /// <summary>The pack of a kind ("ru", "adult", "hd", …) — for mods that come "onlyWith" another pack.</summary>
    public static AddonFile? OfKind(string modlistId, string kind) => Load(modlistId, kind);

    /// <summary>The load order of a Nexus-list modlist (mods and plugins of the base and all packs), or null.</summary>
    public static ModOrder? Order(string modlistId)
    {
        try
        {
            ModOrder? o = null;
            if (Pinned(modlistId, "order") is (true, var pinned))
                return pinned == null ? null : JsonSerializer.Deserialize<ModOrder>(pinned) is { } po && po.ModlistId == modlistId && po.Modlist.Count > 0 ? po : null;
            var cached = CacheFile(modlistId, "order");
            if (File.Exists(cached))
                try { o = JsonSerializer.Deserialize<ModOrder>(File.ReadAllText(cached)); } catch { o = null; }
            if (o == null || o.ModlistId != modlistId || o.Modlist.Count == 0)
            {
                var res = System.Windows.Application.GetResourceStream(new Uri($"pack://application:,,,/addons/{modlistId}-order.json"));
                if (res == null) return null;
                using var s = res.Stream;
                o = JsonSerializer.Deserialize<ModOrder>(s);
            }
            return o != null && o.ModlistId == modlistId && o.Modlist.Count > 0 ? o : null;
        }
        catch { return null; }
    }

    /// <summary>All packs of a modlist that the launcher installs from Nexus: base, HD, Russian, 18+.</summary>
    public static IEnumerable<AddonFile> All(string modlistId) =>
        new[] { Base(modlistId) }.Concat(Extras(modlistId).Select(x => x.Pack)).Concat(new[] { Russian(modlistId), Adult(modlistId) }).Where(a => a != null)!;

    /// <summary>
    /// 0.1.19: the folder with the lists of the version actually installed when it is not the catalog's one (the player
    /// went back to the previous version). Set by the app; null = the current lists.
    /// </summary>
    public static Func<string, string?>? InstalledListsDir;

    /// <summary>The list as the launcher has it now (downloaded copy, else built in) — kept next to an install.</summary>
    public static string? RawJson(string modlistId, string kind)
    {
        try
        {
            var cached = CacheFile(modlistId, kind);
            if (File.Exists(cached))
            {
                var t = File.ReadAllText(cached);
                using var d = JsonDocument.Parse(t);
                if (d.RootElement.TryGetProperty("modlistId", out var id) && id.GetString() == modlistId) return t;
            }
            var res = System.Windows.Application.GetResourceStream(new Uri($"pack://application:,,,/addons/{modlistId}-{kind}.json"));
            if (res == null) return null;
            using var r = new StreamReader(res.Stream);
            return r.ReadToEnd();
        }
        catch { return null; }
    }

    /// <summary>The installed version's own copy of a list: (true, text or null if that version had no such pack) or (false, _) = not pinned.</summary>
    private static (bool Pinned, string? Json) Pinned(string modlistId, string kind)
    {
        if (InstalledListsDir?.Invoke(modlistId) is not { } dir) return (false, null);
        var f = Path.Combine(dir, $"{modlistId}-{kind}.json");
        try { return (true, File.Exists(f) ? File.ReadAllText(f) : null); } catch { return (false, null); }
    }

    /// <summary>FUNKYLAUNCHER_ADDONS_BRANCH (developer tests only): read the packs from a test branch instead of main.</summary>
    private static string OnlineBase =>
        "https://raw.githubusercontent.com/FunkyPlay/FunkyLauncher/"
        + (Environment.GetEnvironmentVariable("FUNKYLAUNCHER_ADDONS_BRANCH") is { Length: > 0 } b && b.All(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.') ? b : "main")
        + "/addons/";
    private static string CacheFile(string modlistId, string kind) =>
        Path.Combine(AppConfig.DataDir, "addons", $"{modlistId}-{kind}.json");

    /// <summary>
    /// Downloads the current packs from GitHub (same files as built in) into the data folder, so a fixed list reaches
    /// players without a new launcher. Only files that parse and belong to the modlist are kept. Off with UpdateCatalog.
    /// </summary>
    public static async Task RefreshOnlineAsync(IEnumerable<string> modlistIds, CancellationToken token)
    {
        using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        foreach (var id in modlistIds)
            foreach (var kind in new[] { "ru", "adult", "main", "order" }.Concat(ExtraKinds))
            {
                try
                {
                    var json = await http.GetStringAsync(OnlineBase + Uri.EscapeDataString($"{id}-{kind}.json"), token);
                    if (kind == "order")
                    {
                        var o = JsonSerializer.Deserialize<ModOrder>(json);
                        if (o == null || o.ModlistId != id || o.Modlist.Count == 0) continue;
                    }
                    else
                    {
                        var a = JsonSerializer.Deserialize<AddonFile>(json);
                        if (a == null || a.ModlistId != id || a.Mods.Count == 0) continue;
                    }
                    var file = CacheFile(id, kind);
                    Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                    await File.WriteAllTextAsync(file, json, token);
                }
                catch { /* offline, 404 (no such pack) — keep what we have */ }
            }
        // 0.1.22: the descriptions of the mods ("Mods of the modlist" window)
        foreach (var id in modlistIds)
            try
            {
                var json = await http.GetStringAsync(OnlineBase + Uri.EscapeDataString($"{id}-about.json"), token);
                if (!IsAbout(json, id)) continue;
                var file = CacheFile(id, "about");
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                await File.WriteAllTextAsync(file, json, token);
            }
            catch { }
        // the dictionaries of the Russian item names (a changed one reaches players without a new launcher)
        foreach (var id in modlistIds)
            try { if (Order(id)?.Translation is { } t) await Services.TranslationService.RefreshDictsAsync(http, OnlineBase, t, token); }
            catch { }
    }

    /// <summary>
    /// 0.1.22 (Funky 25.11): our own one-line description of each mod of the base, RU and EN, by mod folder.
    /// File addons/{id}-about.json — downloaded with the packs, so a new description needs no new launcher.
    /// </summary>
    /// <summary>"hidden": true in the about file — the mod is not listed in the window (it still stays in the modlist).</summary>
    public const string HiddenMark = "\u0000hidden";

    public static IReadOnlyDictionary<string, string> About(string modlistId) => About(modlistId, out _, out _);

    public static IReadOnlyDictionary<string, string> About(string modlistId, out Dictionary<string, long> nexusIds) =>
        About(modlistId, out nexusIds, out _);

    /// <summary>"required": true in the about file — the player cannot switch the mod off (an author's condition:
    /// sabbath7991 allowed our translation only while his mods are mandatory, Legal 25.11).</summary>
    public static HashSet<string> Required(string modlistId)
    {
        // asked once per mod while the window or the profile is built: read the file once per its version
        DateTime stamp;
        try { var f = CacheFile(modlistId, "about"); stamp = File.Exists(f) ? File.GetLastWriteTimeUtc(f) : DateTime.MinValue; } catch { stamp = DateTime.MinValue; }
        lock (_required)
        {
            if (_required.TryGetValue(modlistId, out var c) && c.Stamp == stamp) return c.Set;
            About(modlistId, out _, out var r);
            _required[modlistId] = (stamp, r);
            return r;
        }
    }

    private static readonly Dictionary<string, (DateTime Stamp, HashSet<string> Set)> _required = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="nexusIds">"nexusModId" in the about file: the mod's own Nexus page when the list's top-level id
    /// is wrong (mods made of several archives — the list took another archive's id, chat Cyberpunk 25.11).</param>
    public static IReadOnlyDictionary<string, string> About(string modlistId, out Dictionary<string, long> nexusIds, out HashSet<string> required)
    {
        var res = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        nexusIds = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        required = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            string? json = null;
            var cached = CacheFile(modlistId, "about");
            if (File.Exists(cached)) try { json = File.ReadAllText(cached); } catch { }
            if (json == null || !IsAbout(json, modlistId))
            {
                var r = System.Windows.Application.GetResourceStream(new Uri($"pack://application:,,,/addons/{modlistId}-about.json"));
                if (r == null) return res;
                using var sr = new StreamReader(r.Stream);
                json = sr.ReadToEnd();
            }
            using var d = JsonDocument.Parse(json);
            if (!d.RootElement.TryGetProperty("mods", out var mods)) return res;
            var lang = Loc.Lang == "ru" ? "ru" : "en";
            foreach (var p in mods.EnumerateObject())
            {
                if (p.Value.TryGetProperty("nexusModId", out var nid) && nid.TryGetInt64(out var id) && id > 0) nexusIds[p.Name] = id;
                if (p.Value.TryGetProperty("required", out var rq) && rq.ValueKind == JsonValueKind.True) required.Add(p.Name);
            }
            foreach (var p in mods.EnumerateObject())
                if (p.Value.TryGetProperty("hidden", out var h) && h.ValueKind == JsonValueKind.True) res[p.Name] = HiddenMark;
                else if (p.Value.TryGetProperty(lang, out var t) && t.GetString() is { Length: > 0 } s) res[p.Name] = s;
        }
        catch { }
        return res;
    }

    private static bool IsAbout(string json, string modlistId)
    {
        try
        {
            using var d = JsonDocument.Parse(json);
            return d.RootElement.TryGetProperty("modlistId", out var id) && id.GetString() == modlistId
                && d.RootElement.TryGetProperty("mods", out var m) && m.ValueKind == JsonValueKind.Object;
        }
        catch { return false; }
    }

    private static AddonFile? Load(string modlistId, string kind)
    {
        try
        {
            AddonFile? a = null;
            var cached = CacheFile(modlistId, kind);
            if (Pinned(modlistId, kind) is (true, var pinned))
            {
                if (pinned == null) return null; // the installed version had no such pack
                a = JsonSerializer.Deserialize<AddonFile>(pinned);
            }
            else if (File.Exists(cached))
                try { a = JsonSerializer.Deserialize<AddonFile>(File.ReadAllText(cached)); } catch { a = null; }
            if (a == null || a.ModlistId != modlistId)
            {
                var uri = new Uri($"pack://application:,,,/addons/{modlistId}-{kind}.json");
                var res = System.Windows.Application.GetResourceStream(uri);
                if (res == null) return null;
                using var s = res.Stream;
                a = JsonSerializer.Deserialize<AddonFile>(s);
            }
            if (a == null || a.ModlistId != modlistId) return null;
            foreach (var m in a.Mods) { m.Archive ??= new(); m.Nexus ??= new(); } // "archive": null in a list must not break the whole pack
            // "bundled": our own mod whose folder comes with the base install — the pack downloads nothing, it only switches it on
            a.Mods = a.Mods.Where(m => !m.Blocked && m.Install.Type != "skip" && m.ModFolder.Length > 0
                && (string.IsNullOrEmpty(m.Status) || m.Status == "ready")
                && (!string.IsNullOrEmpty(m.Archive.LocalFileName) || m.Archives.Count > 0 || m.Install.Type == "bundled")).ToList();
            foreach (var m in a.Mods)
                if (m.DisplayName.Ru.Length == 0) { m.DisplayName.Ru = m.Id; m.DisplayName.En = m.Id; }
            return a.Mods.Count > 0 ? a : null;
        }
        catch { return null; }
    }
}
