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
using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Archives;
using Noggog;

namespace FunkyPatcher;

public static class Program
{
    private const int MaxMastersPerPatch = 180;
    private static readonly HashSet<string> SkippedTypes = new(StringComparer.OrdinalIgnoreCase) { "REFR", "WRLD", "DIAL" };

    /// <summary>What the launcher (or the command line) gives the helper.</summary>
    /// <param name="Mo2Dir">the portable MO2 folder of the modlist</param>
    /// <param name="Profile">MO2 profile name (its plugins.txt / modlist.txt give the load order)</param>
    /// <param name="GameDir">the game folder</param>
    /// <param name="DictPaths">dictionaries: "owner plugin, FormID, record, field, Russian text" (tab separated, one header line each)</param>
    /// <param name="OutDir">folder of our own mod: the patch plugins are written here, older ones of this name are replaced</param>
    /// <param name="GameType">starfield or fallout4</param>
    /// <param name="ReportPath">where to write the JSON report (null = in OutDir)</param>
    public sealed record PatcherOptions(string Mo2Dir, string Profile, string GameDir, IReadOnlyList<string> DictPaths, string OutDir,
        string GameType = "starfield", string Prefix = "FunkyPlay_RU_Names", string? ReportPath = null);

    public sealed record PatcherResult(bool Ok, string? Error, int Made, int Skipped, int Patches, string ReportJson);

    /// <summary>The whole job: reads the load order, builds the patch plugins into OutDir. Never throws: errors come back in the result.</summary>
    public static PatcherResult Build(PatcherOptions o, Action<string>? progress = null)
    {
        var report = new Report { Progress = progress };
        try
        {
            if (o.GameType.Equals("fallout4", StringComparison.OrdinalIgnoreCase))
                Run<IFallout4Mod, IFallout4ModGetter>(GameInfo.Fallout4, o.Mo2Dir, o.Profile, o.GameDir, o.DictPaths, o.OutDir, o.Prefix, false, report);
            else
                Run<IStarfieldMod, IStarfieldModGetter>(GameInfo.Starfield, o.Mo2Dir, o.Profile, o.GameDir, o.DictPaths, o.OutDir, o.Prefix, false, report);
            report.Ok = true;
        }
        catch (Exception ex)
        {
            report.Error = ex.ToString();
        }
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        try
        {
            var path = o.ReportPath ?? Path.Combine(o.OutDir, "funkypatcher-report.json");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path, json);
        }
        catch { /* the report is for us; the patch itself is already written */ }
        return new PatcherResult(report.Ok, report.Error, report.Made, report.Skipped.Values.Sum(), report.Patches.Count, json);
    }

    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        string? Arg(string name) { var i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        var dicts = new List<string>();
        for (var i = 0; i < args.Length - 1; i++) if (args[i] == "--dict") dicts.Add(args[i + 1]);
        var mo2 = Arg("--mo2"); var profile = Arg("--profile"); var outDir = Arg("--out");
        var game = Arg("--game"); var prefix = Arg("--name") ?? "FunkyPlay_RU_Names";
        var gameType = (Arg("--game-type") ?? "starfield").ToLowerInvariant();
        if (mo2 == null || profile == null || dicts.Count == 0 || outDir == null || game == null)
        {
            Console.Error.WriteLine("FunkyPatcher --mo2 <MO2 folder> --profile <MO2 profile> --game <game folder> --dict <dictionary.tsv> [--dict <another.tsv>] --out <output mod folder> [--name <plugin name prefix>] [--game-type starfield|fallout4]");
            return 2;
        }
        var r = Build(new PatcherOptions(mo2, profile, game, dicts, outDir, gameType, prefix), Console.WriteLine);
        Console.WriteLine($"ok={r.Ok} made={r.Made} skipped={r.Skipped} patches={r.Patches}");
        if (r.Error != null) Console.Error.WriteLine(r.Error);
        return r.Ok ? 0 : 1;
    }

    internal sealed class Report
    {
        [System.Text.Json.Serialization.JsonIgnore] public Action<string>? Progress { get; set; }
        public bool Ok { get; set; }
        public string? Error { get; set; }
        public int DictionaryLines { get; set; }
        public int Made { get; set; }
        public int MadeByBytes { get; set; }
        public SortedDictionary<string, int> SentToByteCopy { get; set; } = new();
        public SortedDictionary<string, int> Skipped { get; set; } = new();
        public List<string> SkippedSamples { get; set; } = new();
        public List<PatchInfo> Patches { get; set; } = new();
        public int PluginsInLoadOrder { get; set; }
        public List<string> FixedFlagsInCopy { get; set; } = new();
        public Dictionary<string, int> AcceptedDifferences { get; set; } = new();
        public SortedDictionary<string, int> ChangedBeyondName { get; set; } = new();
        public SortedDictionary<string, int> NotFoundByOwner { get; set; } = new();
        public void Skip(string why, string what)
        {
            Skipped[why] = Skipped.GetValueOrDefault(why) + 1;
            if (why.StartsWith("record not found") || why.StartsWith("record the library")) { var k = why[..10] + " " + what.Split(':')[0] + " " + what.Split(' ')[1]; NotFoundByOwner[k] = NotFoundByOwner.GetValueOrDefault(k) + 1; }
            if ((SkippedSamples.Count < 60 || ((why.StartsWith("copy would") || why.StartsWith("byte copy")) && SkippedSamples.Count < 2400)) && !why.StartsWith("record type left alone")) SkippedSamples.Add(why + ": " + what);
        }
    }

    internal sealed class PatchInfo
    {
        public string File { get; set; } = "";
        public int Records { get; set; }
        public int Masters { get; set; }
        public string? Winner { get; set; }
    }

    private sealed record Entry(string Owner, uint Id, string Record, string Field, string Ru);

    /// <summary>What differs between the games we patch.</summary>
    private sealed record GameInfo(GameRelease Release, string[] Official, string Ccc, string ForcedPattern, bool SeparatedMasters,
        string OfficialTextPrefix, ModType PatchType, int PatchFlags, PluginShape Shape)
    {
        public static readonly GameInfo Starfield = new(GameRelease.Starfield,
            new[] { "Starfield.esm", "Constellation.esm", "OldMars.esm", "SFBGS003.esm", "SFBGS004.esm", "SFBGS006.esm", "SFBGS007.esm", "SFBGS008.esm", "ShatteredSpace.esm", "BlueprintShips-Starfield.esm" },
            "Starfield.ccc", @"^(BlueprintShips-)?SFBGS[0-9A-Fa-f]{3}\.esm$", true, "@segnet:", ModType.Master, 0x1 | 0x100, new PluginShape(0x101, 0x240, 0.96f, false, ".esm")); // master + light (small)
        public static readonly GameInfo Fallout4 = new(GameRelease.Fallout4,
            new[] { "Fallout4.esm", "DLCRobot.esm", "DLCworkshop01.esm", "DLCCoast.esm", "DLCworkshop02.esm", "DLCworkshop03.esm", "DLCNukaWorld.esm" },
            "Fallout4.ccc", @"^cc[A-Za-z0-9]+-.*\.es[lm]$", false, "@game:", ModType.Plugin, 0x200, new PluginShape(0x200, 0x83, 1.0f, true, ".esp")); // a light (ESL-flagged) .esp
    }

    private static void Run<TMod, TModGetter>(GameInfo info, string mo2, string profile, string game, IReadOnlyList<string> dictPaths, string outDir, string prefix, bool allowStop, Report report)
        where TMod : class, IContextMod<TMod, TModGetter>, TModGetter
        where TModGetter : class, IContextGetterMod<TMod, TModGetter>
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
        var official = info.Official;
        // Bethesda's own plugins (the game and its official add-ons, also the ones named in Starfield.ccc) load without a star
        var ccc = Path.Combine(game, info.Ccc);
        var forced = new HashSet<string>(official, StringComparer.OrdinalIgnoreCase);
        if (File.Exists(ccc)) foreach (var l in File.ReadAllLines(ccc)) if (l.Trim().Length > 0) forced.Add(l.Trim());
        bool Forced(string n) => forced.Contains(n)
            || (System.Text.RegularExpressions.Regex.IsMatch(n, info.ForcedPattern) && File.Exists(Path.Combine(gameData, n)));
        var names = new List<string>();
        foreach (var o in official) if (File.Exists(Path.Combine(gameData, o)) && !names.Contains(o, StringComparer.OrdinalIgnoreCase)) names.Add(o);
        // the game loads its own plugins first (right after Starfield.esm and the DLC), wherever plugins.txt lists them;
        // the starred mods follow in the order of plugins.txt
        // the Creation Club plugins named in the game's .ccc file are loaded by the game even when plugins.txt does not list them
        if (File.Exists(ccc))
            foreach (var l in File.ReadAllLines(ccc).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')))
                if (!names.Contains(l, StringComparer.OrdinalIgnoreCase) && File.Exists(Path.Combine(gameData, l))) names.Add(l);
        foreach (var o in order)
            if (!names.Contains(o, StringComparer.OrdinalIgnoreCase) && Forced(o)) names.Add(o);
        // the game (and MO2 when it starts the game) puts every master-flagged plugin (.esm, .esl, or the master flag in the header)
        // before the ordinary plugins, whatever plugins.txt says; inside each group the order of plugins.txt stays
        bool IsMasterFile(string n)
        {
            if (n.EndsWith(".esm", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".esl", StringComparison.OrdinalIgnoreCase)) return true;
            var p = Resolve(n);
            if (p == null) return false;
            try { var h = new byte[12]; using var f = File.OpenRead(p); return f.Read(h, 0, 12) == 12 && (BitConverter.ToUInt32(h, 8) & 1) != 0; }
            catch { return false; }
        }
        foreach (var wantMaster in new[] { true, false })
            foreach (var o in order)
                if (!names.Contains(o, StringComparer.OrdinalIgnoreCase) && on.Contains(o) && IsMasterFile(o) == wantMaster) names.Add(o);

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
                if (info.SeparatedMasters && BothLightAndMedium(src))
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

            using var env = GameEnvironment.Typical.Builder<TMod, TModGetter>(info.Release)
                .WithTargetDataFolder(flat)
                .WithLoadOrder(listings.ToArray())
                .WithUtf8Encoding() // texts inside plugins: both games read them as UTF-8 with Russian; read them the same way so they are written back unchanged
                .Build();
            // Not the library's link cache: it reads every record of every mod and one record it cannot read stops everything.
            // We look only at the record lists (FormID -> place in file) and read just the records we translate.
            var mods = env.LoadOrder.ListedOrder.Where(l => l.Mod != null).Select(l => l.Mod!).Reverse().ToList(); // last loaded first
            var typeBySignature = RecordTypesBySignature(typeof(TMod).Assembly);
            var groups = new Dictionary<(ModKey, Type), IGroupGetter?>();
            IGroupGetter? Group(TModGetter mod, Type getter)
            {
                if (groups.TryGetValue((mod.ModKey, getter), out var g)) return g;
                try { g = mod.GetTopLevelGroup(getter); } catch { g = null; }
                return groups[(mod.ModKey, getter)] = g;
            }

            // ---- dictionary
            var entries = new List<Entry>();
            report.Progress?.Invoke("dictionary");
            foreach (var line in dictPaths.SelectMany(d => File.ReadLines(d).Skip(1)))
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
                if (!e.Ru.StartsWith(info.OfficialTextPrefix)) return e.Ru;
                var parts = e.Ru.Split(':');
                if (parts.Length != 3 || !uint.TryParse(parts[2], out var id)) return null;
                if (!strings.TryGetValue(parts[1], out var table))
                {
                    var rel = Path.Combine("Strings", parts[1] + "_ru.STRINGS");
                    var file = Resolve(rel);
                    // the game's own Russian texts may sit inside its archives (Fallout 4: "… - Interface.ba2")
                    var bytes = file != null ? File.ReadAllBytes(file) : FromArchives(info.Release, gameData, rel);
                    strings[parts[1]] = table = bytes == null ? null : ReadStrings(bytes);
                }
                return table != null && table.TryGetValue(id, out var s) && s.Length > 0 ? s : null;
            }

            // our own byte reader of the plugins (numbers of records by the game's rules, see RawPlugin)
            var styles = new Dictionary<string, RawPlugin.Style>(StringComparer.OrdinalIgnoreCase);
            RawPlugin.Style StyleOf(string name)
            {
                if (styles.TryGetValue(name, out var st)) return st;
                var f = Path.Combine(flat, name);
                return styles[name] = File.Exists(f) ? RawPlugin.StyleOf(f) : RawPlugin.Style.Full;
            }
            var raws = new Dictionary<ModKey, RawPlugin>();
            RawPlugin Raw(ModKey k) => raws.TryGetValue(k, out var r) ? r : raws[k] = new RawPlugin(Path.Combine(flat, k.FileName), k.FileName, StyleOf, info.SeparatedMasters);

            // ---- patches
            Directory.CreateDirectory(outDir);
            foreach (var pat in new[] { prefix + "_*.esm", prefix + "_*.esp" }) foreach (var old in Directory.EnumerateFiles(outDir, pat)) File.Delete(old);
            var patches = new List<(TMod Mod, HashSet<ModKey> Masters, int Count)>();
            void NewPatch()
            {
                var key = new ModKey($"{prefix}_{patches.Count + 1:00}", info.PatchType);
                var mod = ModInstantiator<TMod>.Activator(key, info.Release);
                patches.Add((mod, new HashSet<ModKey>(), 0));
            }
            NewPatch();
            var made = new Dictionary<TMod, List<(FormKey Key, ModKey Winner, IMajorRecord Copy, string What)>>();

            // records that have a field the library cannot address (OBTE-FULL#n, WNAM#n, ATTX) go to the byte copy as a whole
            var byteOnly = new HashSet<(string, uint)>(entries.Where(x => !x.Field.Equals("FULL", StringComparison.OrdinalIgnoreCase) || x.Record.Equals("CELL", StringComparison.OrdinalIgnoreCase)).Select(x => (x.Owner.ToLowerInvariant(), x.Id)));
            var fallback = new HashSet<(string, uint)>();
            foreach (var e in entries)
            {
                var what = $"{e.Owner}:{e.Id:X6} {e.Record}";
                var recKey = (e.Owner.ToLowerInvariant(), e.Id);
                if (!mods.Any(m => string.Equals(m.ModKey.FileName.String, e.Owner, StringComparison.OrdinalIgnoreCase))) { report.Skip("owner plugin is not in the load order", what); continue; }
                if (!ByteCopy.IsKnownField(e.Field)) { report.Skip("field not supported", what + " " + e.Field); continue; }
                if (!e.Field.Equals("FULL", StringComparison.OrdinalIgnoreCase) || byteOnly.Contains(recKey)) continue; // done by the byte copy below
                void ToByteCopy(string why) { fallback.Add(recKey); report.SentToByteCopy[why] = report.SentToByteCopy.GetValueOrDefault(why) + 1; }
                if (SkippedTypes.Contains(e.Record)) { report.Skip("record type left alone (" + e.Record.ToUpperInvariant() + ")", what); continue; }
                var text = Text(e);
                if (text == null) { report.Skip("no text (official translation not found)", what + " " + e.Ru); continue; }
                var formKey = new FormKey(ModKey.FromFileName(e.Owner), e.Id);
                if (!typeBySignature.TryGetValue(e.Record, out var getterType)) { ToByteCopy("record type unknown to the library"); continue; }
                TModGetter? winner = null; IGroupGetter? winnerGroup = null;
                foreach (var mod in mods)
                {
                    var g = Group(mod, getterType);
                    if (g != null && g.ContainsKey(formKey)) { winner = mod; winnerGroup = g; break; }
                }
                if (winner == null)
                {
                    // the library may not see a record whose number is written in a form it misreads; say so instead of "not found"
                    var ownerRaw = mods.FirstOrDefault(m => m.ModKey == formKey.ModKey);
                    if (ownerRaw != null && Raw(formKey.ModKey).Own != RawPlugin.Style.Light && Raw(formKey.ModKey).Has(e.Owner, e.Id) && Raw(formKey.ModKey).IsForeignForm(e.Owner, e.Id))
                        report.Skip("owner keeps its own records in the light form although it is not light (the library cannot address them)", what);
                    else report.Skip("record not found in the load order", what);
                    continue;
                }
                // protection: by the game's rules (numbers read by the plugin's type) this record must exist in the winning plugin
                if (!Raw(winner.ModKey).Has(e.Owner, e.Id)) { report.Skip("record does not exist for the game (would be a new record, not an override)", what + " in " + winner.ModKey.FileName); continue; }
                if (Raw(formKey.ModKey).IsForeignForm(e.Owner, e.Id)) { report.Skip("owner keeps its own records in the light form although it is not light (the library cannot address them)", what); continue; }
                try
                {
                    var rec = winnerGroup![formKey];
                    // the name is the record's own field, or (generic base forms and the like) a "full name" component
                    var nameNow = rec is ITranslatedNamedGetter named ? named.Name?.String
                        : rec is INamedGetter plain ? plain.Name
                        : (NameComponent(rec) as ITranslatedNamedGetter)?.Name?.String;
                    if (rec is not ITranslatedNamedGetter && rec is not INamedGetter && NameComponent(rec) == null) { ToByteCopy("record has no name field in the library"); continue; }
                    if (string.Equals(nameNow, text, StringComparison.Ordinal)) { report.Skip("already has this name", what); continue; }

                    var need = new HashSet<ModKey> { formKey.ModKey, winner.ModKey };
                    foreach (var link in rec.EnumerateFormLinks()) if (!link.IsNull) need.Add(link.FormKey.ModKey);
                    var cur = patches[^1];
                    if (cur.Count > 0 && cur.Masters.Union(need).Count() > MaxMastersPerPatch) { NewPatch(); cur = patches[^1]; }

                    var copy = (IMajorRecord)((IMajorRecordGetter)rec).Duplicate(formKey);
                    if (copy is ITranslatedNamed target) target.Name = text;
                    else if (copy is INamed simple) simple.Name = text; // a few record types keep the name as plain text
                    else if (NameComponent(copy) is ITranslatedNamed full) full.Name = text;
                    else { ToByteCopy("copy has no name field in the library"); continue; }
                    DropChildRecords(copy); // e.g. a quest's dialogue and scenes: the patch overrides the quest itself only
                    cur.Mod.GetTopLevelGroup(copy.GetType()).AddUntyped(copy);
                    cur.Masters.UnionWith(need);
                    patches[^1] = (cur.Mod, cur.Masters, cur.Count + 1);
                    if (!made.TryGetValue(cur.Mod, out var list)) made[cur.Mod] = list = new();
                    list.Add((formKey, winner.ModKey, copy, what));
                }
                catch (Exception ex)
                {
                    ToByteCopy("record the library cannot read");
                }
            }

            // ---- write, then prove every record: byte-for-byte the winning record, apart from the name and renumbered FormIDs
            var utf8 = new Mutagen.Bethesda.Plugins.Binary.Streams.EncodingBundle(Mutagen.Bethesda.Strings.DI.MutagenEncoding._utf8, Mutagen.Bethesda.Strings.DI.MutagenEncoding._utf8);
            foreach (var (mod, _, _) in patches)
            {
                if (!made.TryGetValue(mod, out var list) || list.Count == 0) continue;
                var file = Path.Combine(outDir, mod.ModKey.FileName);
                // overrides only: a light plugin does not take a full plugin slot
                dynamic header = ((dynamic)mod).ModHeader;
                header.Flags = (dynamic)Enum.ToObject(((object)header.Flags).GetType(), Convert.ToInt32((object)header.Flags) | info.PatchFlags);
                for (var pass = 1; ; pass++)
                {
                    // Starfield keeps texts inside plugins as UTF-8 (so do the Russian translations of mods)
                    ((dynamic)mod).BeginWrite.ToPath(file).WithLoadOrder(env.LoadOrder).WithEmbeddedEncodings(utf8).Write();
                    var written = new RawPlugin(file, mod.ModKey.FileName, StyleOf, info.SeparatedMasters);
                    var bad = new List<int>();
                    // protection: the patch holds exactly the records we made, each one an override of a record that exists (game's rules)
                    var madeKeys = new HashSet<(string, uint)>(list.Select(m => (m.Key.ModKey.FileName.String.ToLowerInvariant(), m.Key.ID)));
                    foreach (var r in written.Records)
                        if (!madeKeys.Contains((r.Mod, r.Id))) throw new InvalidDataException($"{mod.ModKey.FileName}: unexpected record {r.Type} {r.Mod}:{r.Id:X}");
                    for (var n = 0; n < list.Count; n++)
                    {
                        var m = list[n];
                        string? diff;
                        try
                        {
                            var a = Raw(m.Winner).Read(m.Key.ModKey.FileName, m.Key.ID);
                            var b = written.Read(m.Key.ModKey.FileName, m.Key.ID);
                            diff = a == null ? "source record not found by the byte reader" : b == null ? "record missing in the patch"
                                : RawPlugin.Difference(Raw(m.Winner), a, written, b, false, pass == 1 ? report.AcceptedDifferences : new());
                        }
                        catch (Exception ex) { diff = "check failed: " + ex.GetBaseException().Message; }
                        if (diff == null) continue;
                        bad.Add(n);
                        report.SentToByteCopy["copy would change more than the name"] = report.SentToByteCopy.GetValueOrDefault("copy would change more than the name") + 1;
                        fallback.Add((m.Key.ModKey.FileName.String.ToLowerInvariant(), m.Key.ID));
                        var kind = m.What.Split(' ')[^1] + " " + System.Text.RegularExpressions.Regex.Replace(diff, @"\d+", "N");
                        report.ChangedBeyondName[kind] = report.ChangedBeyondName.GetValueOrDefault(kind) + 1;
                    }
                    if (bad.Count == 0) break;
                    if (pass >= 3) throw new InvalidDataException($"{mod.ModKey.FileName}: records still differ after {pass} passes");
                    for (var q = bad.Count - 1; q >= 0; q--)
                    {
                        var at = bad[q];
                        mod.Remove(list[at].Key);
                        list.RemoveAt(at);
                    }
                    if (list.Count == 0) { File.Delete(file); break; }
                }
                if (list.Count == 0) continue;
                // the written file once more, by our own reader: every record is there, and the real master list
                var final = new RawPlugin(file, mod.ModKey.FileName, StyleOf, info.SeparatedMasters);
                if (final.RecordCount != list.Count) throw new InvalidDataException($"{mod.ModKey.FileName}: wrote {list.Count} records, the file has {final.RecordCount}");
                report.Made += list.Count;
                report.Patches.Add(new PatchInfo { File = mod.ModKey.FileName, Records = list.Count, Masters = final.MasterCount });
            }
            report.Progress?.Invoke("byte copy");
            // ---- byte copy: the records the library cannot (or may not) copy, and the fields it cannot address
            var jobs = new List<ByteJob>();
            foreach (var g in entries.Where(x => !SkippedTypes.Contains(x.Record) && ByteCopy.IsKnownField(x.Field))
                         .GroupBy(x => (x.Owner.ToLowerInvariant(), x.Id)))
            {
                if (!byteOnly.Contains(g.Key) && !fallback.Contains(g.Key)) continue;
                var fields = new List<(string, string)>();
                foreach (var x in g)
                {
                    var t = Text(x);
                    if (t == null) { report.Skip("no text (official translation not found)", $"{x.Owner}:{x.Id:X6} {x.Record} {x.Field} {x.Ru}"); continue; }
                    fields.Add((x.Field, t));
                }
                var first = g.First();
                if (fields.Count > 0) jobs.Add(new ByteJob(first.Owner, first.Id, first.Record, fields, $"{first.Owner}:{first.Id:X6} {first.Record}"));
            }
            ByteCopy.Run(report, info.Shape, info.SeparatedMasters, outDir, prefix,
                mods.Select(m => m.ModKey.FileName.String).ToList(),
                n => { var k = mods.FirstOrDefault(m => string.Equals(m.ModKey.FileName.String, n, StringComparison.OrdinalIgnoreCase)); return k == null ? null : Raw(k.ModKey); },
                StyleOf, jobs);
        }
        finally
        {
            try { Directory.Delete(flat, true); } catch { }
        }
    }

    /// <summary>.STRINGS: count, data size, then (id, offset) pairs, then zero-terminated UTF-8 texts.</summary>
    /// <summary>A file of the game's archives (BA2), by its path inside; null when no archive has it.</summary>
    private static byte[]? FromArchives(GameRelease release, string gameData, string relative)
    {
        var want = relative.Replace('/', '\\');
        foreach (var path in Directory.EnumerateFiles(gameData, "*.ba2"))
        {
            var name = Path.GetFileName(path);
            if (name.Contains("Textures", StringComparison.OrdinalIgnoreCase) || name.Contains("Voices", StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                var reader = Archive.CreateReader(release, path);
                foreach (var f in reader.Files)
                    if (f.Path.Replace('/', '\\').Equals(want, StringComparison.OrdinalIgnoreCase)) return f.GetBytes();
            }
            catch { }
        }
        return null;
    }

    private static Dictionary<uint, string> ReadStrings(byte[] b)
    {
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
    private static object? NameComponent(object rec)
    {
        if (rec.GetType().GetProperty("Components")?.GetValue(rec) is not System.Collections.IEnumerable list) return null;
        foreach (var c in list) if (c != null && c.GetType().Name.Contains("FullNameComponent")) return c;
        return null;
    }

    /// <summary>"WEAP" -> IWeaponGetter and so on, from the library's own record registrations (top-level record types).</summary>
    private static Dictionary<string, Type> RecordTypesBySignature(System.Reflection.Assembly assembly)
    {
        var res = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in assembly.GetTypes())
        {
            if (!t.Name.EndsWith("_Registration")) continue;
            var sig = t.GetField("TriggeringRecordType", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)?.GetValue(null);
            var getter = t.GetField("GetterType", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)?.GetValue(null) as Type;
            if (sig is RecordType rt && getter != null && typeof(IMajorRecordGetter).IsAssignableFrom(getter))
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
