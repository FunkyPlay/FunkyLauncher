// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.IO.Compression;
using System.Text;

namespace FunkyPatcher;

/// <summary>
/// Reads a Starfield plugin at the level of bytes (no library): its master list, where each record lies and what its
/// sub-records are. Used to prove that a record copied into our patch is byte-for-byte the winning record, apart from
/// the name and the FormIDs renumbered for the patch's own master list.
/// Starfield's FormIDs in a file: the top byte counts FULL masters only; a medium master's record is FD + index + 16-bit id,
/// a light master's record is FE + 12-bit index + 12-bit id; the plugin's own records use the number of its full masters.
/// </summary>
public sealed class RawPlugin
{
    public enum Style { Full, Medium, Light }
    public sealed record Sub(string Type, byte[] Data);
    public sealed record Rec(string Type, uint Flags, List<Sub> Subs);

    private readonly string _path;
    private readonly string _name;
    private readonly List<string> _full = new(), _medium = new(), _light = new();
    private readonly Dictionary<(string Mod, uint Id), (long Offset, int Size, uint Flags, string Type, uint RawId, int Parent)> _index = new();
    /// <summary>How this plugin itself is flagged (decides how many bits its own record numbers have).</summary>
    public Style Own { get; }
    private uint OwnMask => Own switch { Style.Light => 0xFFFu, Style.Medium => 0xFFFFu, _ => 0xFFFFFFu };

    /// <summary>Every record in the file: (owner plugin, id there, record type, FormID exactly as stored).</summary>
    public IEnumerable<(string Mod, uint Id, string Type, uint RawId)> Records => _index.Select(kv => (kv.Key.Mod, kv.Key.Id, kv.Value.Type, kv.Value.RawId));
    public bool Has(string ownerPlugin, uint id) => _index.ContainsKey((ownerPlugin.ToLowerInvariant(), id));
    /// <summary>True when the record is the plugin's own one but written in the "light" form (FE...) although the plugin is not light:
    /// the library cuts such numbers to 12 bits, the game reads them by the plugin's type (16 bits for medium).</summary>
    public bool IsForeignForm(string ownerPlugin, uint id) =>
        _index.TryGetValue((ownerPlugin.ToLowerInvariant(), id), out var at) && ownerPlugin.Equals(_name, StringComparison.OrdinalIgnoreCase) && (at.RawId >> 24) == 0xFE && Own != Style.Light;
    public uint RawFormId(string ownerPlugin, uint id) => _index[(ownerPlugin.ToLowerInvariant(), id)].RawId;
    public string Name => _name;
    /// <summary>Type of the group the record sits in (0 top level, 3 interior cell sub-block, 5 exterior cell sub-block ...).</summary>
    public int ParentGroupType(string ownerPlugin, uint id) => _index[(ownerPlugin.ToLowerInvariant(), id)].Parent;
    /// <summary>Flags of the plugin's own header (0x80 = texts live in string tables, 0x1 = master...).</summary>
    public uint HeaderFlags { get; private set; }
    /// <summary>Master names in the order of the file (all kinds together).</summary>
    public List<string> MastersInFileOrder { get; } = new();
    private bool? _foreign;
    /// <summary>The plugin is not light yet writes its own records in the light form (FE...): its raw bytes cannot be moved to another plugin as they are.</summary>
    public bool HasForeignOwnRecords => _foreign ??= _separated && Own != Style.Light
        && _index.Any(kv => kv.Key.Mod == _name.ToLowerInvariant() && (kv.Value.RawId >> 24) == 0xFE);
    /// <summary>The 24 bytes of the record header exactly as stored.</summary>
    public byte[] RecordHeader(string ownerPlugin, uint id)
    {
        var at = _index[(ownerPlugin.ToLowerInvariant(), id)];
        using var f = File.OpenRead(_path);
        f.Position = at.Offset - 24;
        var h = new byte[24];
        f.ReadExactly(h, 0, 24);
        return h;
    }
    public IReadOnlyList<string> Masters => _full.Concat(_medium).Concat(_light).ToList();

    public static Style StyleOf(string path)
    {
        var h = new byte[12];
        using var f = File.OpenRead(path);
        f.ReadExactly(h, 0, 12);
        var flags = BitConverter.ToUInt32(h, 8);
        return (flags & 0x100) != 0 ? Style.Light : (flags & 0x400) != 0 ? Style.Medium : Style.Full;
    }

    /// <param name="styleOfMaster">master file name -> its style (from the same files the library reads)</param>
    public int RecordCount => _index.Count;
    public int MasterCount => _full.Count + _medium.Count + _light.Count;
    private readonly bool _separated;

    /// <param name="separated">Starfield: three master lists. Fallout 4 and older games: one list, the top byte is simply the master's number.</param>
    public RawPlugin(string path, string name, Func<string, Style> styleOfMaster, bool separated = true)
    {
        _separated = separated;
        _path = path; _name = name;
        Own = separated ? StyleOf(path) : Style.Full;
        using var f = File.OpenRead(path);
        var h = new byte[24];
        f.ReadExactly(h, 0, 24);
        HeaderFlags = BitConverter.ToUInt32(h, 8);
        var headerSize = BitConverter.ToInt32(h, 4);
        var header = new byte[headerSize];
        f.ReadExactly(header, 0, headerSize);
        foreach (var s in Subrecords(header))
        {
            if (s.Type != "MAST") continue;
            var m = Encoding.UTF8.GetString(s.Data).TrimEnd('\0');
            MastersInFileOrder.Add(m);
            (!separated ? _full : styleOfMaster(m) switch { Style.Light => _light, Style.Medium => _medium, _ => _full }).Add(m);
        }
        Walk(f, 24 + headerSize, f.Length, 0);
    }

    private void Walk(FileStream f, long pos, long end, int parentGroupType)
    {
        var h = new byte[24];
        while (pos + 24 <= end)
        {
            f.Position = pos;
            f.ReadExactly(h, 0, 24);
            var type = Encoding.ASCII.GetString(h, 0, 4);
            var size = BitConverter.ToUInt32(h, 4);
            if (type == "GRUP")
            {
                Walk(f, pos + 24, pos + size, BitConverter.ToInt32(h, 12));
                pos += size;
                continue;
            }
            var key = Decode(BitConverter.ToUInt32(h, 12));
            if (key != null) _index[key.Value] = (pos + 24, (int)size, BitConverter.ToUInt32(h, 8), type, BitConverter.ToUInt32(h, 12), parentGroupType);
            pos += 24 + size;
        }
    }

    /// <summary>A FormID as stored in this file -> (plugin that owns the record, its id there). Null = cannot be resolved.</summary>
    public (string Mod, uint Id)? Decode(uint raw)
    {
        var top = raw >> 24;
        if (_separated && top == 0xFE)
        {
            var i = (int)((raw >> 12) & 0xFFF);
            // one past the last light master = the plugin's own record (files saved by newer tools; older ones use the full index)
            // the plugin's own number has as many bits as the plugin's type allows (light 12, medium 16, full 24), not always 12
            return i < _light.Count ? (_light[i].ToLowerInvariant(), raw & 0xFFF) : i == _light.Count ? (_name.ToLowerInvariant(), raw & OwnMask) : null;
        }
        if (_separated && top == 0xFD)
        {
            var i = (int)((raw >> 16) & 0xFF);
            return i < _medium.Count ? (_medium[i].ToLowerInvariant(), raw & 0xFFFF) : i == _medium.Count ? (_name.ToLowerInvariant(), raw & 0xFFFF) : null;
        }
        if (top < _full.Count) return (_full[(int)top].ToLowerInvariant(), raw & 0xFFFFFF);
        // the engine reads every index at or above the number of full masters as the plugin itself (some mods write 02xxxxxx with one master)
        return top >= _full.Count ? (_name.ToLowerInvariant(), raw & 0xFFFFFF) : null;
    }

    public Rec? Read(string ownerPlugin, uint id)
    {
        if (!_index.TryGetValue((ownerPlugin.ToLowerInvariant(), id), out var at)) return null;
        using var f = File.OpenRead(_path);
        f.Position = at.Offset;
        var data = new byte[at.Size];
        f.ReadExactly(data, 0, at.Size);
        if ((at.Flags & 0x00040000) != 0) // compressed: 4 bytes of unpacked size, then zlib
        {
            var unpacked = new byte[BitConverter.ToInt32(data, 0)];
            using var z = new ZLibStream(new MemoryStream(data, 4, data.Length - 4), CompressionMode.Decompress);
            z.ReadExactly(unpacked, 0, unpacked.Length);
            data = unpacked;
        }
        return new Rec(at.Type, at.Flags & ~0x00040000u, Subrecords(data));
    }

    private static List<Sub> Subrecords(byte[] d)
    {
        var res = new List<Sub>();
        var pos = 0;
        long big = -1;
        while (pos + 6 <= d.Length)
        {
            var type = Encoding.ASCII.GetString(d, pos, 4);
            int size = BitConverter.ToUInt16(d, pos + 4);
            pos += 6;
            if (type == "XXXX") { big = BitConverter.ToUInt32(d, pos); pos += size; continue; } // real size of the next sub-record
            if (big >= 0) { size = (int)big; big = -1; }
            res.Add(new Sub(type, d.AsSpan(pos, size).ToArray()));
            pos += size;
        }
        return res;
    }

    /// <summary>
    /// Null when the patch record equals the source record apart from the name (FULL) and renumbered FormIDs;
    /// otherwise a short reason. <paramref name="notes"/> counts the differences we accept on purpose.
    /// </summary>
    public static string? Difference(RawPlugin src, Rec a, RawPlugin dst, Rec b, bool allowAddedStop, Dictionary<string, int> notes)
    {
        if (a.Type != b.Type) return $"type {a.Type} -> {b.Type}";
        if (a.Flags != b.Flags) return $"record flags {a.Flags:X} -> {b.Flags:X}";
        int i = 0, j = 0;
        while (i < a.Subs.Count || j < b.Subs.Count)
        {
            if (i >= a.Subs.Count || j >= b.Subs.Count || a.Subs[i].Type != b.Subs[j].Type)
            {
                if (allowAddedStop && j < b.Subs.Count && b.Subs[j].Type == "STOP" && b.Subs[j].Data.Length == 0)
                { j++; notes["added empty STOP"] = notes.GetValueOrDefault("added empty STOP") + 1; continue; }
                return i >= a.Subs.Count ? $"extra {b.Subs[j].Type}" : j >= b.Subs.Count ? $"lost {a.Subs[i].Type}" : $"{a.Subs[i].Type} became {b.Subs[j].Type}";
            }
            var (x, y) = (a.Subs[i].Data, b.Subs[j].Data);
            var t = a.Subs[i].Type;
            i++; j++;
            if (t == "FULL" || x.AsSpan().SequenceEqual(y)) continue;
            if (x.Length != y.Length)
            {
                // No re-encoded text is accepted: a text that already was UTF-8 (Russian translations of mods), read by the
                // library as an old Windows encoding and written again, looks "converted" but is ruined. Other fields must
                // stay exactly as the author's bytes — otherwise the record is left out of the patch.
                return $"{t} size {x.Length} -> {y.Length}";
            }
            for (var k = 0; k < x.Length; k++)
            {
                if (x[k] == y[k]) continue;
                var ok = false;
                for (var o = k; o >= Math.Max(0, k - 3) && !ok; o--)
                {
                    if (o + 4 > x.Length) continue;
                    var p = src.Decode(BitConverter.ToUInt32(x, o));
                    var q = dst.Decode(BitConverter.ToUInt32(y, o));
                    if (p != null && p == q) { ok = true; k = o + 3; }
                }
                if (!ok) return $"{t} data differs at byte {k}";
            }
        }
        return null;
    }

    private static readonly Encoding Windows1252 = MakeCp1252();
    private static Encoding MakeCp1252()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252);
    }
    private static string Cp1252(byte[] b) => Windows1252.GetString(b);
}
