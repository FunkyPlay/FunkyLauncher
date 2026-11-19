// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
// FunkyPatcher: builds, on the player's own PC, small override plugins that give Russian names to records of the
// installed mods. Input: a dictionary "owner plugin + FormID + field -> Russian text" (no text of the mods themselves).
// The winning record of the player's load order is copied into the patch and only its name is changed.
// The authors' files are never modified; the patch is made locally and is not distributed.
using System.Text;
using System.Text.Json;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Environments;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Aspects;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Noggog;

namespace FunkyPatcher;

public static class Program
{
    private const int MaxMastersPerPatch = 180;
    private static readonly HashSet<string> SkippedTypes = new(StringComparer.OrdinalIgnoreCase) { "REFR", "CELL", "WRLD", "DIAL" };

    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        string? Arg(string name) { var i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        var mo2 = Arg("--mo2"); var profile = Arg("--profile"); var dict = Arg("--dict"); var outDir = Arg("--out");
        var game = Arg("--game"); var prefix = Arg("--name") ?? "FunkyPlay_RU_Names";
        if (mo2 == null || profile == null || dict == null || outDir == null || game == null)
        {
            Console.Error.WriteLine("FunkyPatcher --mo2 <MO2 folder> --profile <MO2 profile> --game <game folder> --dict <dictionary.tsv> --out <output mod folder> [--name <plugin name prefix>]");
            return 2;
        }
        var report = new Report();
        try
        {
            Run(mo2, profile, game, dict, outDir, prefix, report);
            report.Ok = true;
        }
        catch (Exception ex)
        {
            report.Error = ex.ToString();
        }
        Directory.CreateDirectory(outDir);
        File.WriteAllText(Path.Combine(outDir, "funkypatcher-report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        Console.WriteLine($"ok={report.Ok} made={report.Made} skipped={report.Skipped.Values.Sum()} patches={report.Patches.Count}");
        if (report.Error != null) Console.Error.WriteLine(report.Error);
        return report.Ok ? 0 : 1;
    }

    private sealed class Report
    {
        public bool Ok { get; set; }
        public string? Error { get; set; }
        public int DictionaryLines { get; set; }
        public int Made { get; set; }
        public SortedDictionary<string, int> Skipped { get; set; } = new();
        public List<string> SkippedSamples { get; set; } = new();
        public List<PatchInfo> Patches { get; set; } = new();
        public int PluginsInLoadOrder { get; set; }
        public List<string> FixedFlagsInCopy { get; set; } = new();
        public SortedDictionary<string, int> NotFoundByOwner { get; set; } = new();
        public void Skip(string why, string what)
        {
            Skipped[why] = Skipped.GetValueOrDefault(why) + 1;
            if (why.StartsWith("record not found") || why.StartsWith("record the library")) { var k = why[..10] + " " + what.Split(':')[0] + " " + what.Split(' ')[1]; NotFoundByOwner[k] = NotFoundByOwner.GetValueOrDefault(k) + 1; }
            if (SkippedSamples.Count < 60 && !why.StartsWith("record type left alone")) SkippedSamples.Add(why + ": " + what);
        }
    }

    private sealed class PatchInfo
    {
        public string File { get; set; } = "";
        public int Records { get; set; }
        public int Masters { get; set; }
    }

    private sealed record Entry(string Owner, uint Id, string Record, string Field, string Ru);

    private static void Run(string mo2, string profile, string game, string dictPath, string outDir, string prefix, Report report)
    {
        var profileDir = Path.Combine(mo2, "profiles", profile);
        var modsDir = Path.Combine(mo2, "mods");
        var gameData = Path.Combine(game, "Data");

        // ---- MO2: enabled mods, top of modlist.txt = highest priority
        var enabledMods = File.ReadAllLines(Path.Combine(profileDir, "modlist.txt"))
            .Where(l => l.StartsWith('+')).Select(l => l[1..]).Where(n => !n.EndsWith("_separator")).ToList();

        // ---- virtual Data folder: which real file each plugin / strings file comes from
        string? Resolve(string relative)
        {
            foreach (var mod in enabledMods)
            {
                var p = Path.Combine(modsDir, mod, relative);
                if (File.Exists(p)) return p;
            }
            var g = Path.Combine(gameData, relative);
            return File.Exists(g) ? g : null;
        }

        // ---- load order: loadorder.txt gives the order, plugins.txt says which are on ("*name")
        // (for Starfield plugins.txt itself is the order; loadorder.txt may lack some lines, so it only helps as a fallback)
        var pluginLines = File.ReadAllLines(Path.Combine(profileDir, "plugins.txt")).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')).ToList();
        var on = new HashSet<string>(pluginLines.Where(l => l.StartsWith('*')).Select(l => l[1..].Trim()), StringComparer.OrdinalIgnoreCase);
        var order = pluginLines.Select(l => l.TrimStart('*').Trim()).ToList();
        var loadOrderFile = Path.Combine(profileDir, "loadorder.txt");
        if (File.Exists(loadOrderFile))
            foreach (var l in File.ReadAllLines(loadOrderFile).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')))
                if (!order.Contains(l, StringComparer.OrdinalIgnoreCase)) order.Add(l);
        // the game's own masters are always loaded, whatever plugins.txt says
        var official = new[] { "Starfield.esm", "Constellation.esm", "OldMars.esm", "SFBGS003.esm", "SFBGS004.esm", "SFBGS006.esm", "SFBGS007.esm", "SFBGS008.esm", "ShatteredSpace.esm", "BlueprintShips-Starfield.esm" };
        // Bethesda's own plugins (the game and its official add-ons, also the ones named in Starfield.ccc) load without a star
        var ccc = Path.Combine(game, "Starfield.ccc");
        var forced = new HashSet<string>(official, StringComparer.OrdinalIgnoreCase);
        if (File.Exists(ccc)) foreach (var l in File.ReadAllLines(ccc)) if (l.Trim().Length > 0) forced.Add(l.Trim());
        bool Forced(string n) => forced.Contains(n)
            || (System.Text.RegularExpressions.Regex.IsMatch(n, @"^(BlueprintShips-)?SFBGS[0-9A-Fa-f]{3}\.esm$") && File.Exists(Path.Combine(gameData, n)));
        var names = new List<string>();
        foreach (var o in official) if (File.Exists(Path.Combine(gameData, o)) && !names.Contains(o, StringComparer.OrdinalIgnoreCase)) names.Add(o);
        foreach (var o in order)
            if (!names.Contains(o, StringComparer.OrdinalIgnoreCase) && (on.Contains(o) || Forced(o))) names.Add(o);

        // ---- a flat folder of links to the real plugin files (Mutagen reads one Data folder)
        var flat = Path.Combine(Path.GetTempPath(), "FunkyPatcher-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(flat);
        try
        {
            var listings = new List<ModKey>();
            foreach (var n in names)
            {
                if (n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue; // an older patch of ours is rebuilt, never read
                var src = Resolve(n);
                if (src == null) { report.Skip("plugin file not found", n); continue; }
                var dst = Path.Combine(flat, n);
                if (BothLightAndMedium(src))
                {
                    // a few mods carry both the "light" and the "medium" flag; the game then treats the plugin as light.
                    // Our temporary copy gets only the light flag so the library can read it; the author's file is untouched.
                    var bytes = File.ReadAllBytes(src);
                    bytes[9] &= 0xFB; // header flags are at offset 8, little-endian: 0x400 (medium) is bit 2 of the second byte
                    File.WriteAllBytes(dst, bytes);
                    report.FixedFlagsInCopy.Add(n);
                }
                else if (!TryHardLink(dst, src)) File.Copy(src, dst);
                listings.Add(ModKey.FromFileName(n));
            }
            report.PluginsInLoadOrder = listings.Count;

            using var env = GameEnvironment.Typical.Builder<IStarfieldMod, IStarfieldModGetter>(GameRelease.Starfield)
                .WithTargetDataFolder(flat)
                .WithLoadOrder(listings.ToArray())
                .Build();
            // Not the library's link cache: it reads every record of every mod and one record it cannot read stops everything.
            // We look only at the record lists (FormID -> place in file) and read just the records we translate.
            var mods = env.LoadOrder.ListedOrder.Where(l => l.Mod != null).Select(l => l.Mod!).Reverse().ToList(); // last loaded first
            var typeBySignature = RecordTypesBySignature();
            var groups = new Dictionary<(ModKey, Type), IGroupGetter?>();
            IGroupGetter? Group(IStarfieldModGetter mod, Type getter)
            {
                if (groups.TryGetValue((mod.ModKey, getter), out var g)) return g;
                try { g = mod.GetTopLevelGroup(getter); } catch { g = null; }
                return groups[(mod.ModKey, getter)] = g;
            }

            // ---- dictionary
            var entries = new List<Entry>();
            foreach (var line in File.ReadLines(dictPath).Skip(1))
            {
                if (line.Length == 0) continue;
                var c = line.Split('\t');
                if (c.Length < 5) { report.Skip("bad dictionary line", line); continue; }
                entries.Add(new Entry(c[0].Trim(), Convert.ToUInt32(c[1].Trim(), 16), c[2].Trim(), c[3].Trim(), c[4]));
            }
            report.DictionaryLines = entries.Count;
            var strings = new Dictionary<string, Dictionary<uint, string>?>(StringComparer.OrdinalIgnoreCase);
            string? Text(Entry e)
            {
                if (!e.Ru.StartsWith("@segnet:")) return e.Ru;
                var parts = e.Ru.Split(':');
                if (parts.Length != 3 || !uint.TryParse(parts[2], out var id)) return null;
                if (!strings.TryGetValue(parts[1], out var table))
                {
                    var file = Resolve(Path.Combine("Strings", parts[1] + "_ru.STRINGS"));
                    strings[parts[1]] = table = file == null ? null : ReadStrings(file);
                }
                return table != null && table.TryGetValue(id, out var s) && s.Length > 0 ? s : null;
            }

            // ---- patches
            Directory.CreateDirectory(outDir);
            foreach (var old in Directory.EnumerateFiles(outDir, prefix + "_*.esm")) File.Delete(old);
            var patches = new List<(StarfieldMod Mod, HashSet<ModKey> Masters, int Count)>();
            void NewPatch()
            {
                var key = new ModKey($"{prefix}_{patches.Count + 1:00}", ModType.Master);
                var mod = new StarfieldMod(key, StarfieldRelease.Starfield);
                patches.Add((mod, new HashSet<ModKey>(), 0));
            }
            NewPatch();

            foreach (var e in entries)
            {
                var what = $"{e.Owner}:{e.Id:X6} {e.Record}";
                if (!e.Field.Equals("FULL", StringComparison.OrdinalIgnoreCase)) { report.Skip("field not supported", what); continue; }
                if (SkippedTypes.Contains(e.Record)) { report.Skip("record type left alone (" + e.Record.ToUpperInvariant() + ")", what); continue; }
                var text = Text(e);
                if (text == null) { report.Skip("no text (official translation not found)", what + " " + e.Ru); continue; }
                var formKey = new FormKey(ModKey.FromFileName(e.Owner), e.Id);
                if (!typeBySignature.TryGetValue(e.Record, out var getterType)) { report.Skip("record type unknown to the library (" + e.Record + ")", what); continue; }
                IStarfieldModGetter? winner = null; IGroupGetter? winnerGroup = null;
                foreach (var mod in mods)
                {
                    var g = Group(mod, getterType);
                    if (g != null && g.ContainsKey(formKey)) { winner = mod; winnerGroup = g; break; }
                }
                if (winner == null) { report.Skip("record not found in the load order", what); continue; }
                try
                {
                    var rec = winnerGroup![formKey];
                    // the name is the record's own field, or (generic base forms and the like) a "full name" component
                    var nameNow = rec is ITranslatedNamedGetter named ? named.Name?.String : NameComponent(rec)?.Name?.String;
                    if (rec is not ITranslatedNamedGetter && NameComponent(rec) == null) { report.Skip("record has no name field", what + " " + rec.GetType().Name); continue; }
                    if (string.Equals(nameNow, text, StringComparison.Ordinal)) { report.Skip("already has this name", what); continue; }

                    var need = new HashSet<ModKey> { formKey.ModKey, winner.ModKey };
                    foreach (var link in rec.EnumerateFormLinks()) if (!link.IsNull) need.Add(link.FormKey.ModKey);
                    var cur = patches[^1];
                    if (cur.Count > 0 && cur.Masters.Union(need).Count() > MaxMastersPerPatch) { NewPatch(); cur = patches[^1]; }

                    var copy = (IStarfieldMajorRecord)((IStarfieldMajorRecordGetter)rec).DeepCopy();
                    if (copy is ITranslatedNamed target) target.Name = text;
                    else if (NameComponent(copy) is FullNameComponent full) full.Name = text;
                    else { report.Skip("copy has no name field", what); continue; }
                    DropChildRecords(copy); // e.g. a quest's dialogue and scenes: the patch overrides the quest itself only
                    cur.Mod.GetTopLevelGroup(copy.GetType()).AddUntyped(copy);
                    cur.Masters.UnionWith(need);
                    patches[^1] = (cur.Mod, cur.Masters, cur.Count + 1);
                    report.Made++;
                }
                catch (Exception ex)
                {
                    report.Skip("record the library cannot read", what + " in " + winner.ModKey.FileName + ": " + ex.GetBaseException().Message);
                }
            }

            foreach (var (mod, _, count) in patches)
            {
                if (count == 0) continue;
                var file = Path.Combine(outDir, mod.ModKey.FileName);
                mod.ModHeader.Flags |= StarfieldModHeader.HeaderFlag.Master | StarfieldModHeader.HeaderFlag.Light; // overrides only: a light plugin does not take a full plugin slot
                // Starfield keeps texts inside plugins as UTF-8 (so do the Russian translations of mods)
                var utf8 = new Mutagen.Bethesda.Plugins.Binary.Streams.EncodingBundle(Mutagen.Bethesda.Strings.DI.MutagenEncoding._utf8, Mutagen.Bethesda.Strings.DI.MutagenEncoding._utf8);
                mod.BeginWrite.ToPath(file).WithLoadOrder(env.LoadOrder).WithEmbeddedEncodings(utf8).Write();
                // read the written file back: the real master list and a check that every record opens
                using var back = StarfieldMod.Create(StarfieldRelease.Starfield).FromPath(file).WithLoadOrder(env.LoadOrder.ListedOrder.Select(l => l.ModKey)).WithDataFolder(flat).Construct();
                var readable = back.EnumerateMajorRecords().Count();
                if (readable != count) throw new InvalidDataException($"{mod.ModKey.FileName}: wrote {count} records, read back {readable}");
                report.Patches.Add(new PatchInfo { File = mod.ModKey.FileName, Records = count, Masters = back.ModHeader.MasterReferences.Count });
            }
        }
        finally
        {
            try { Directory.Delete(flat, true); } catch { }
        }
    }

    /// <summary>.STRINGS: count, data size, then (id, offset) pairs, then zero-terminated UTF-8 texts.</summary>
    private static Dictionary<uint, string> ReadStrings(string path)
    {
        var b = File.ReadAllBytes(path);
        var count = BitConverter.ToUInt32(b, 0);
        var dataStart = 8 + (int)count * 8;
        var res = new Dictionary<uint, string>((int)count);
        for (var i = 0; i < count; i++)
        {
            var id = BitConverter.ToUInt32(b, 8 + i * 8);
            var off = dataStart + (int)BitConverter.ToUInt32(b, 12 + i * 8);
            var end = off;
            while (end < b.Length && b[end] != 0) end++;
            res[id] = Encoding.UTF8.GetString(b, off, end - off);
        }
        return res;
    }

    /// <summary>Records kept inside this record (lists of other major records) are not part of our override.</summary>
    private static void DropChildRecords(object copy)
    {
        foreach (var p in copy.GetType().GetProperties())
        {
            if (!p.PropertyType.IsGenericType || p.GetIndexParameters().Length > 0) continue;
            var arg = p.PropertyType.GetGenericArguments()[0];
            if (!typeof(IMajorRecordGetter).IsAssignableFrom(arg)) continue;
            if (p.GetValue(copy) is System.Collections.IList list && !list.IsReadOnly) list.Clear();
        }
    }

    /// <summary>The "full name" component of a record that keeps its name in components (null if it has none).</summary>
    private static IFullNameComponentGetter? NameComponent(object rec)
    {
        if (rec.GetType().GetProperty("Components")?.GetValue(rec) is not System.Collections.IEnumerable list) return null;
        foreach (var c in list) if (c is IFullNameComponentGetter f) return f;
        return null;
    }

    /// <summary>"WEAP" -> IWeaponGetter and so on, from the library's own record registrations (top-level record types).</summary>
    private static Dictionary<string, Type> RecordTypesBySignature()
    {
        var res = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in typeof(StarfieldMod).Assembly.GetTypes())
        {
            if (!t.Name.EndsWith("_Registration")) continue;
            var sig = t.GetField("TriggeringRecordType", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)?.GetValue(null);
            var getter = t.GetField("GetterType", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)?.GetValue(null) as Type;
            if (sig is RecordType rt && getter != null && typeof(IStarfieldMajorRecordGetter).IsAssignableFrom(getter))
                res.TryAdd(rt.Type, getter);
        }
        return res;
    }

    private static bool BothLightAndMedium(string path)
    {
        var h = new byte[12];
        using var f = File.OpenRead(path);
        if (f.Read(h, 0, 12) < 12 || h[0] != 'T' || h[1] != 'E' || h[2] != 'S' || h[3] != '4') return false;
        var flags = BitConverter.ToUInt32(h, 8);
        return (flags & 0x100) != 0 && (flags & 0x400) != 0;
    }

    private static bool TryHardLink(string link, string target)
    {
        try { return CreateHardLink(link, target, IntPtr.Zero); } catch { return false; }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);
}
