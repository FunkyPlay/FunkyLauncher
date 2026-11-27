// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Security.Cryptography;
using System.Text;
using FunkyLauncher.Core;

namespace FunkyLauncher.Services;

/// <summary>
/// Russian names of items from the mods. After the Russian version is installed (or on the "Rebuild" button) the helper
/// (src/FunkyPatcher, built into the launcher) reads the load order of the installed modlist and, from a dictionary,
/// makes a few small patch plugins in our own mod folder. The authors' files are never changed; the patch is made on
/// this PC and is not distributed. A failure never breaks the install: the modlist works, the names stay in English,
/// and the reason goes to the log (funkypatcher-log.txt in the install folder, which the error report includes).
/// </summary>
public static class TranslationService
{
    public sealed record Result(bool Built, bool Skipped, int Made, int Patches, string? Error);

    public static string LogFile(Settings s, Modlist m) => Path.Combine(s.InstallDir(m), "funkypatcher-log.txt");
    public static string ReportFile(Settings s, Modlist m) => Path.Combine(s.InstallDir(m), "funkypatcher-report.json");

    /// <summary>The modlist has Russian names (a dictionary is listed in its order file).</summary>
    public static TranslationSpec? Spec(Modlist m) =>
        Mo2Profile.IsNexusList(m) && Addons.Order(m.Id)?.Translation is { ModFolder.Length: > 0, Dicts.Count: > 0 } t ? t : null;

    /// <summary>Russian version is on for this modlist.</summary>
    public static bool RussianOn(Settings s, Modlist m) => Addons.Russian(m.Id) is { } ru && AddonService.Prefs(s, ru).On;

    private static void Log(Settings s, Modlist m, string text)
    {
        try { File.AppendAllText(LogFile(s, m), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {text}\r\n"); } catch { }
    }

    /// <summary>
    /// Builds the patch plugins and switches them on in the profile. Safe to call again: the old generated plugins are replaced.
    /// Never throws (except for cancellation): the problem comes back in <see cref="Result.Error"/>.
    /// </summary>
    public static async Task<Result> BuildAsync(Settings s, Modlist m, IProgress<string>? progress, CancellationToken token)
    {
        var spec = Spec(m);
        if (spec == null || !RussianOn(s, m)) return new Result(false, true, 0, 0, null);
        var installDir = s.InstallDir(m);
        var mods = Path.Combine(installDir, "mods");
        var target = Path.Combine(mods, spec.ModFolder);
        var order = Addons.Order(m.Id)!;
        try
        {
            if (AddonService.Mo2Running(s, m)) throw new InvalidOperationException(Loc.T("trans.err.mo2"));
            var game = Mo2Profile.GamePath(installDir) ?? throw new InvalidOperationException("game folder is not set in ModOrganizer.ini");
            if (!File.Exists(Path.Combine(Mo2Profile.ProfileDir(s, m), "plugins.txt"))) throw new InvalidOperationException("the MO2 profile is not written yet");
            progress?.Report(Loc.T("trans.building"));
            Log(s, m, $"start {m.Id} {m.Version} launcher {AppConfig.Version}");

            // the dictionaries: the refreshed online copy if its hash is right, else the one built into the launcher
            var dictFiles = new List<string>();
            var tmpDicts = Path.Combine(AppConfig.DataDir, "addons", "tmp-dicts");
            Directory.CreateDirectory(tmpDicts);
            foreach (var d in spec.Dicts)
                dictFiles.Add(await DictFileAsync(d, tmpDicts, token));

            // built next to the mod folder and moved in only when it is whole
            var building = target + ".building";
            if (Directory.Exists(building)) Directory.Delete(building, true);
            Directory.CreateDirectory(building);
            token.ThrowIfCancellationRequested();
            // a separate process (GPL Mutagen stays out of the launcher's process, Legal 25.11)
            var r = await PatcherTool.BuildAsync(installDir, m.Mo2Profile, game, dictFiles, building, spec.GameType, spec.Prefix,
                ReportFile(s, m), t => Log(s, m, "  " + t), token);
            if (!r.Ok) throw new InvalidOperationException(r.Error ?? "the helper failed");

            Directory.CreateDirectory(target);
            var before = Generated(target, spec.Prefix);
            RemoveGenerated(target, spec.Prefix);
            foreach (var f in Directory.EnumerateFiles(building).Where(f => Path.GetFileName(f).StartsWith(spec.Prefix + "_", StringComparison.OrdinalIgnoreCase)))
                File.Move(f, Path.Combine(target, Path.GetFileName(f)), overwrite: true);
            Directory.Delete(building, true);
            var padded = PadMissing(target, spec.Prefix, before);
            if (padded > 0) Log(s, m, $"kept {padded} empty patch plugins of the earlier build (old saves list them)");
            var meta = Path.Combine(target, "meta.ini");
            if (!File.Exists(meta)) File.WriteAllText(meta, "[General]\r\nrepository=FunkyPlay\r\ncomments=Generated by Funky Launcher from the item-name dictionary\r\n");
            Mo2Profile.RememberOurs(s, m, spec.ModFolder);
            Mo2Profile.Write(s, m, order);
            Log(s, m, $"done: {r.Made} records in {r.Patches} plugins, {r.Skipped} left in English");
            return new Result(true, false, r.Made, r.Patches, null);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // the modlist works without the names: no half-made plugins stay in the load order
            Log(s, m, "FAILED " + ex);
            try { if (Directory.Exists(target)) RemoveGenerated(target, spec.Prefix); Mo2Profile.Write(s, m, order); } catch { }
            try { if (Directory.Exists(target + ".building")) Directory.Delete(target + ".building", true); } catch { }
            return new Result(false, false, 0, 0, ex.Message);
        }
    }

    private static List<string> Generated(string folder, string prefix) =>
        Directory.EnumerateFiles(folder).Select(Path.GetFileName).OfType<string>()
            .Where(n => n.StartsWith(prefix + "_", StringComparison.OrdinalIgnoreCase) && Path.GetExtension(n).ToLowerInvariant() is ".esm" or ".esp" or ".esl")
            .ToList();

    /// <summary>
    /// 0.1.24 (Fallout 4 test 26.11): a new dictionary can make fewer patch plugins than the last build did. A save made
    /// with the old ones then says "missing content: FunkyPlay_RU_Names_Raw021.esp…" on load. Harmless, but it scares the
    /// player — so each plugin of the earlier build that is not made again is written back empty (no records, the same
    /// header and flags as a freshly made one, light when that one is light). They stay in the folder, so the next build
    /// keeps them too: the set of names only grows, and old saves never miss a plugin. Returns how many were written.
    /// </summary>
    private static int PadMissing(string folder, string prefix, IEnumerable<string> before)
    {
        var now = Generated(folder, prefix);
        var missing = before.Where(n => !now.Contains(n, StringComparer.OrdinalIgnoreCase)).ToList();
        if (missing.Count == 0 || now.Count == 0) return 0;
        var template = EmptyFrom(File.ReadAllBytes(Path.Combine(folder, now.OrderBy(n => n, StringComparer.Ordinal).First())));
        if (template == null) return 0;
        foreach (var n in missing) File.WriteAllBytes(Path.Combine(folder, n), template);
        return missing.Count;
    }

    /// <summary>
    /// The TES4 header of a plugin alone: its record count set to 0 and its masters dropped (no records — nothing to
    /// depend on, so a mod switched off later never makes the empty plugin fail to load). Same flags, so light stays light.
    /// </summary>
    private static byte[]? EmptyFrom(byte[] plugin)
    {
        if (plugin.Length < 24 || Encoding.ASCII.GetString(plugin, 0, 4) != "TES4") return null;
        var size = (int)BitConverter.ToUInt32(plugin, 4);
        if (24 + size > plugin.Length) return null;
        var subs = new MemoryStream();
        var hedr = false;
        for (var o = 24; o + 6 <= 24 + size;)
        {
            var type = Encoding.ASCII.GetString(plugin, o, 4);
            var len = BitConverter.ToUInt16(plugin, o + 4);
            if (o + 6 + len > 24 + size) return null;
            var sub = plugin.AsSpan(o, 6 + len).ToArray();
            if (type == "HEDR" && len >= 8) { BitConverter.GetBytes(0u).CopyTo(sub, 6 + 4); hedr = true; }
            if (type is not ("MAST" or "DATA")) subs.Write(sub); // DATA in TES4 only follows a MAST
            o += 6 + len;
        }
        if (!hedr) return null;
        var head = plugin.AsSpan(0, 24).ToArray();
        BitConverter.GetBytes((uint)subs.Length).CopyTo(head, 4);
        return head.Concat(subs.ToArray()).ToArray();
    }

    /// <summary>Removes the plugins this service generated (our own files) from the mod folder.</summary>
    private static void RemoveGenerated(string folder, string prefix)
    {
        foreach (var f in Directory.EnumerateFiles(folder).ToList())
        {
            var name = Path.GetFileName(f);
            var ext = Path.GetExtension(f).ToLowerInvariant();
            if (name.StartsWith(prefix + "_", StringComparison.OrdinalIgnoreCase) && ext is ".esm" or ".esp" or ".esl")
                File.Delete(f);
        }
    }

    private static string CacheFile(TranslationDict d) => Path.Combine(AppConfig.DataDir, "addons", d.Path.Replace('/', Path.DirectorySeparatorChar));

    private static bool HashIs(byte[] data, string sha) =>
        string.IsNullOrEmpty(sha) || Convert.ToHexString(SHA256.HashData(data)).Equals(sha, StringComparison.OrdinalIgnoreCase);

    /// <summary>A file on disk with the dictionary: the online copy refreshed earlier if it passes the hash, else the built-in one.</summary>
    private static async Task<string> DictFileAsync(TranslationDict d, string folder, CancellationToken token)
    {
        var dst = Path.Combine(folder, Path.GetFileName(d.Path));
        var cached = CacheFile(d);
        if (File.Exists(cached))
        {
            var bytes = await File.ReadAllBytesAsync(cached, token);
            if (HashIs(bytes, d.Sha256)) { await File.WriteAllBytesAsync(dst, bytes, token); return dst; }
        }
        var res = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/addons/" + d.Path.Replace('\\', '/')))
            ?? throw new FileNotFoundException("dictionary " + d.Path);
        using (var src = res.Stream)
        using (var o = File.Create(dst)) await src.CopyToAsync(o, token);
        return dst;
    }

    /// <summary>Downloads the current dictionaries from GitHub into the data folder (kept only if the hash is right).</summary>
    public static async Task RefreshDictsAsync(System.Net.Http.HttpClient http, string onlineBase, TranslationSpec spec, CancellationToken token)
    {
        foreach (var d in spec.Dicts)
        {
            try
            {
                var cached = CacheFile(d);
                if (File.Exists(cached) && HashIs(await File.ReadAllBytesAsync(cached, token), d.Sha256)) continue;
                var bytes = await http.GetByteArrayAsync(onlineBase + d.Path, token);
                if (string.IsNullOrEmpty(d.Sha256) || !HashIs(bytes, d.Sha256)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(cached)!);
                await File.WriteAllBytesAsync(cached, bytes, token);
            }
            catch { /* offline: the built-in dictionary is used */ }
        }
    }
}
