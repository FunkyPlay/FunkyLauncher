// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Text;

namespace FunkyPatcher;

/// <summary>How a patch plugin of this game looks (what the game's own tools write in the header).</summary>
internal sealed record PluginShape(uint Flags, ushort FormVersion, float HedrVersion, bool MasterData, string Extension);

/// <summary>One record to translate: the record, and which of its text fields get which text.</summary>
internal sealed record ByteJob(string Owner, uint Id, string Record, List<(string Field, string Text)> Fields, string What);

/// <summary>
/// "Byte copy": for records the library would damage. The winning record is taken as raw bytes, only the text
/// sub-records named in the dictionary are replaced, everything else stays exactly as the author wrote it.
/// One small light plugin per winning plugin: its masters are the masters of the winner plus the winner itself (last),
/// so every FormID inside the copied bytes keeps its meaning and nothing is renumbered.
/// </summary>
internal static class ByteCopy
{
    /// <summary>Index of the sub-record a dictionary field points to (-1 = there is none).
    /// FULL: the record's own name (before any object template); OBTE-FULL#n: the n-th FULL after the first OBTE;
    /// WNAM#n: the n-th WNAM; ATTX: the first ATTX.</summary>
    public static int FindSub(List<RawPlugin.Sub> subs, string field)
    {
        var firstObte = subs.FindIndex(s => s.Type == "OBTE");
        if (field.Equals("FULL", StringComparison.OrdinalIgnoreCase))
        {
            var i = subs.FindIndex(s => s.Type == "FULL");
            return i >= 0 && (firstObte < 0 || i < firstObte) ? i : -1;
        }
        if (field.Equals("ATTX", StringComparison.OrdinalIgnoreCase)) return subs.FindIndex(s => s.Type == "ATTX");
        var hash = field.IndexOf('#');
        if (hash < 0 || !int.TryParse(field[(hash + 1)..], out var n) || n < 0) return -1;
        var head = field[..hash];
        if (head.Equals("WNAM", StringComparison.OrdinalIgnoreCase))
        {
            for (var i = 0; i < subs.Count; i++) if (subs[i].Type == "WNAM" && n-- == 0) return i;
            return -1;
        }
        if (head.Equals("OBTE-FULL", StringComparison.OrdinalIgnoreCase) && firstObte >= 0)
        {
            for (var i = firstObte + 1; i < subs.Count; i++) if (subs[i].Type == "FULL" && n-- == 0) return i;
        }
        return -1;
    }

    public static bool IsKnownField(string field)
    {
        var f = field.Trim();
        if (f.Equals("FULL", StringComparison.OrdinalIgnoreCase) || f.Equals("ATTX", StringComparison.OrdinalIgnoreCase)) return true;
        var hash = f.IndexOf('#');
        return hash > 0 && int.TryParse(f[(hash + 1)..], out var n) && n >= 0
            && (f[..hash].Equals("WNAM", StringComparison.OrdinalIgnoreCase) || f[..hash].Equals("OBTE-FULL", StringComparison.OrdinalIgnoreCase));
    }

    private static byte[] TextBytes(string text)
    {
        var b = Encoding.UTF8.GetBytes(text);
        var r = new byte[b.Length + 1];
        b.CopyTo(r, 0);
        return r;
    }

    private static byte[] BuildRecord(byte[] header, uint flags, List<RawPlugin.Sub> subs)
    {
        var body = new MemoryStream();
        void Sub(string type, ushort size, byte[] data)
        {
            body.Write(Encoding.ASCII.GetBytes(type));
            body.Write(BitConverter.GetBytes(size));
            body.Write(data);
        }
        foreach (var s in subs)
        {
            if (s.Data.Length > 0xFFFF)
            {
                body.Write(Encoding.ASCII.GetBytes("XXXX"));
                body.Write(BitConverter.GetBytes((ushort)4));
                body.Write(BitConverter.GetBytes((uint)s.Data.Length));
                Sub(s.Type, 0, s.Data);
            }
            else Sub(s.Type, (ushort)s.Data.Length, s.Data);
        }
        var h = (byte[])header.Clone();
        BitConverter.GetBytes((uint)body.Length).CopyTo(h, 4);
        BitConverter.GetBytes(flags).CopyTo(h, 8); // the copy is stored uncompressed: no "compressed" flag
        var res = new byte[24 + body.Length];
        h.CopyTo(res, 0);
        body.ToArray().CopyTo(res, 24);
        return res;
    }

    private static void WriteSub(Stream s, string type, byte[] data)
    {
        s.Write(Encoding.ASCII.GetBytes(type));
        s.Write(BitConverter.GetBytes((ushort)data.Length));
        s.Write(data);
    }

    private static byte[] Grup(byte[] label, int type, byte[] content)
    {
        var o = new MemoryStream();
        o.Write(Encoding.ASCII.GetBytes("GRUP"));
        o.Write(BitConverter.GetBytes((uint)(24 + content.Length)));
        o.Write(label);
        o.Write(BitConverter.GetBytes(type));
        o.Write(new byte[8]); // stamp, version info
        o.Write(content);
        return o.ToArray();
    }

    /// <summary>The records of one type as top-level group(s). Interior cells sit in block / sub-block groups named by the last two
    /// decimal digits of the cell's number (checked on the games' own plugins: both use the number inside its plugin).</summary>
    private static byte[] TopGroup(string type, List<Item> items, ref int groups)
    {
        groups++;
        var label = Encoding.ASCII.GetBytes(type);
        if (type != "CELL") return Grup(label, 0, items.SelectMany(i => i.Raw).ToArray());
        var body = new MemoryStream();
        foreach (var block in items.GroupBy(i => i.Job.Id % 10).OrderBy(g => g.Key))
        {
            var blockBody = new MemoryStream();
            foreach (var sub in block.GroupBy(i => i.Job.Id / 10 % 10).OrderBy(g => g.Key))
            {
                blockBody.Write(Grup(BitConverter.GetBytes((uint)sub.Key), 3, sub.SelectMany(i => i.Raw).ToArray()));
                groups++;
            }
            body.Write(Grup(BitConverter.GetBytes((uint)block.Key), 2, blockBody.ToArray()));
            groups++;
        }
        return Grup(label, 0, body.ToArray());
    }

    private static byte[] BuildPlugin(PluginShape shape, List<string> masters, SortedDictionary<string, List<Item>> byType)
    {
        var groups = 0;
        var tops = byType.Select(kv => TopGroup(kv.Key, kv.Value, ref groups)).ToList();
        var records = byType.Values.Sum(l => l.Count) + groups;
        var hdr = new MemoryStream();
        var hedr = new byte[12];
        BitConverter.GetBytes(shape.HedrVersion).CopyTo(hedr, 0);
        BitConverter.GetBytes((uint)records).CopyTo(hedr, 4);
        BitConverter.GetBytes(0x800u).CopyTo(hedr, 8);
        WriteSub(hdr, "HEDR", hedr);
        foreach (var m in masters)
        {
            WriteSub(hdr, "MAST", Encoding.UTF8.GetBytes(m + "\0"));
            if (shape.MasterData) WriteSub(hdr, "DATA", new byte[8]);
        }
        var o = new MemoryStream();
        o.Write(Encoding.ASCII.GetBytes("TES4"));
        o.Write(BitConverter.GetBytes((uint)hdr.Length));
        o.Write(BitConverter.GetBytes(shape.Flags));
        o.Write(new byte[8]); // FormID, version-control info
        o.Write(BitConverter.GetBytes(shape.FormVersion));
        o.Write(new byte[2]);
        o.Write(hdr.ToArray());
        foreach (var t in tops) o.Write(t);
        return o.ToArray();
    }

    private sealed class Item
    {
        public ByteJob Job = null!;
        public byte[] Header = null!;
        public uint Flags;
        public List<RawPlugin.Sub> Subs = null!;
        public Dictionary<int, byte[]> Changed = new();
        public byte[] Raw = null!;
    }

    /// <param name="modsLastFirst">plugin file names, the last loaded first (the first one that has the record wins)</param>
    public static void Run(Program.Report report, PluginShape shape, bool separated, string outDir, string prefix,
        IReadOnlyList<string> modsLastFirst, Func<string, RawPlugin?> raw, Func<string, RawPlugin.Style> styleOf, List<ByteJob> jobs)
    {
        var byWinner = new SortedDictionary<string, List<Item>>(StringComparer.OrdinalIgnoreCase);
        foreach (var job in jobs)
        {
            string? winner = null;
            foreach (var m in modsLastFirst) if (raw(m)!.Has(job.Owner, job.Id)) { winner = m; break; }
            if (winner == null) { report.Skip("record not found in the load order", job.What); continue; }
            var w = raw(winner)!;
            if ((w.HeaderFlags & 0x80) != 0) { report.Skip("winner plugin keeps its texts in string tables (localized)", job.What + " in " + winner); continue; }
            if (w.HasForeignOwnRecords) { report.Skip("winner plugin writes its own records in a form that cannot be moved (not light, FE numbers)", job.What + " in " + winner); continue; }
            if (raw(job.Owner) is { } own && own.HasForeignOwnRecords) { report.Skip("owner plugin writes its own records in a form that cannot be moved (not light, FE numbers)", job.What); continue; }
            if (w.Masters.Count >= 250) { report.Skip("winner plugin has too many masters for a patch", job.What + " in " + winner); continue; }
            var rec = w.Read(job.Owner, job.Id)!;
            if (!rec.Type.Equals(job.Record, StringComparison.OrdinalIgnoreCase)) { report.Skip("record type differs from the dictionary (" + rec.Type + ")", job.What + " in " + winner); continue; }
            if (rec.Type == "CELL" && w.ParentGroupType(job.Owner, job.Id) != 3) { report.Skip("exterior cell left alone (its worldspace record would have to be copied too)", job.What + " in " + winner); continue; }
            var item = new Item { Job = job, Header = w.RecordHeader(job.Owner, job.Id), Flags = rec.Flags, Subs = rec.Subs.ToList() };
            foreach (var (field, text) in job.Fields)
            {
                var i = FindSub(item.Subs, field);
                if (i < 0) { report.Skip("sub-record not found in the winning record (" + field.Split('#')[0] + ")", job.What + " " + field + " in " + winner); continue; }
                var data = TextBytes(text);
                if (data.AsSpan().SequenceEqual(item.Subs[i].Data)) { report.Skip("already has this name", job.What + " " + field); continue; }
                item.Changed[i] = data;
            }
            if (item.Changed.Count == 0) continue;
            foreach (var (i, data) in item.Changed) item.Subs[i] = new RawPlugin.Sub(item.Subs[i].Type, data);
            item.Raw = BuildRecord(item.Header, item.Flags, item.Subs);
            if (!byWinner.TryGetValue(winner, out var list)) byWinner[winner] = list = new();
            list.Add(item);
        }

        Directory.CreateDirectory(outDir);
        var number = 0;
        foreach (var (winner, items) in byWinner)
        {
            var w = raw(winner)!;
            var masters = w.MastersInFileOrder.Where(m => !m.Equals(winner, StringComparison.OrdinalIgnoreCase)).Append(winner).ToList();
            var name = $"{prefix}_Raw{++number:000}";
            var file = Path.Combine(outDir, name + shape.Extension);
            for (var pass = 1; ; pass++)
            {
                var byType = new SortedDictionary<string, List<Item>>(StringComparer.Ordinal);
                foreach (var it in items)
                {
                    var t = it.Job.Record.ToUpperInvariant();
                    if (!byType.TryGetValue(t, out var l)) byType[t] = l = new();
                    l.Add(it);
                }
                File.WriteAllBytes(file, BuildPlugin(shape, masters, byType));
                // proof: read the written plugin back with our own reader; every record = the winner's bytes, apart from the named texts
                var written = new RawPlugin(file, name + shape.Extension, styleOf, separated);
                var bad = new List<Item>();
                var expected = new HashSet<(string, uint)>(items.Select(it => (it.Job.Owner.ToLowerInvariant(), it.Job.Id)));
                foreach (var it in items)
                {
                    var why = Check(w, written, it);
                    if (why == null) continue;
                    bad.Add(it);
                    report.Skip(why.StartsWith("record missing") ? "byte copy: winner (light) numbers its own records in the full form, they would change meaning in the patch" : "byte copy did not pass the check", it.Job.What + " in " + winner + ": " + why);
                }
                if (bad.Count == 0)
                {
                    foreach (var r in written.Records)
                        if (!expected.Contains((r.Mod, r.Id))) throw new InvalidDataException($"{name}: unexpected record {r.Type} {r.Mod}:{r.Id:X}");
                    break;
                }
                if (pass >= 2) throw new InvalidDataException($"{name}: byte copy still differs after {pass} passes");
                foreach (var b in bad) items.Remove(b);
                if (items.Count == 0) break;
            }
            if (items.Count == 0) { if (File.Exists(file)) File.Delete(file); number--; continue; }
            report.Made += items.Count;
            report.MadeByBytes += items.Count;
            report.Patches.Add(new Program.PatchInfo { File = name + shape.Extension, Records = items.Count, Masters = masters.Count, Winner = winner });
        }
    }

    /// <summary>Null when the record in the written plugin is the winner's record apart from the named texts.</summary>
    private static string? Check(RawPlugin src, RawPlugin dst, Item it)
    {
        var a = src.Read(it.Job.Owner, it.Job.Id);
        var b = dst.Read(it.Job.Owner, it.Job.Id);
        if (a == null) return "source record not found";
        if (b == null) return "record missing in the patch (its FormID does not resolve to the same record)";
        if (a.Type != b.Type) return $"type {a.Type} -> {b.Type}";
        if (a.Flags != b.Flags) return $"record flags {a.Flags:X} -> {b.Flags:X}";
        if (a.Subs.Count != b.Subs.Count) return $"sub-record count {a.Subs.Count} -> {b.Subs.Count}";
        var h1 = src.RecordHeader(it.Job.Owner, it.Job.Id);
        var h2 = dst.RecordHeader(it.Job.Owner, it.Job.Id);
        if (!h1.AsSpan(12, 12).SequenceEqual(h2.AsSpan(12, 12))) return "record header (FormID, version) differs";
        for (var i = 0; i < a.Subs.Count; i++)
        {
            if (a.Subs[i].Type != b.Subs[i].Type) return $"{a.Subs[i].Type} became {b.Subs[i].Type}";
            var changed = it.Changed.TryGetValue(i, out var want);
            if (changed ? !b.Subs[i].Data.AsSpan().SequenceEqual(want) : !a.Subs[i].Data.AsSpan().SequenceEqual(b.Subs[i].Data))
                return $"{a.Subs[i].Type} #{i} differs";
        }
        return null;
    }
}
