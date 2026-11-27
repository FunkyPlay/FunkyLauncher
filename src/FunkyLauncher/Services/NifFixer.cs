// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.IO;
using System.IO.Compression;
using System.Text;
using FunkyLauncher.Core;

namespace FunkyLauncher.Services;

/// <summary>
/// Fixes known broken meshes on the player's PC (Fallout 4). Some mods ship a NIF whose block name points outside the
/// file's string table — the game crashes when it loads it. We never ship the authors' files: the launcher takes the
/// mesh out of the mod's own BA2, sets the bad name to "no name" (-1) and saves the copy as a separate mod folder,
/// which MO2 puts above the original (a loose file wins over the BA2). The authors' archives are not touched.
/// </summary>
public static class NifFixer
{
    private static readonly HashSet<string> NamedBlocks = new(StringComparer.Ordinal)
        { "BSLightingShaderProperty", "BSEffectShaderProperty", "BSTriShape", "BSSubIndexTriShape", "NiNode", "BSFadeNode" };

    private const string StampFile = "funkylauncher-fixes.txt";

    /// <summary>Writes every fix whose source mod is installed; returns the target mod folders that now hold fixed files.</summary>
    public static HashSet<string> Apply(string modsDir, IEnumerable<NifFix> fixes, Action<string>? log = null)
    {
        var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // which source archive each fixed file was made from (one small file in our mod folder; redo when the mod changes)
        var stamps = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> Stamps(string into)
        {
            if (stamps.TryGetValue(into, out var map)) return map;
            map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var f = Path.Combine(modsDir, into, StampFile);
            if (File.Exists(f))
                foreach (var l in File.ReadAllLines(f))
                    if (l.Split('\t') is [var k, var v]) map[k] = v;
            return stamps[into] = map;
        }

        foreach (var fx in fixes)
        {
            try
            {
                var ba2 = Path.Combine(modsDir, fx.ModFolder, fx.Archive);
                if (!File.Exists(ba2)) continue;
                var target = Path.Combine(modsDir, fx.Into, fx.File.Replace('/', '\\'));
                var source = $"{new FileInfo(ba2).Length}|{File.GetLastWriteTimeUtc(ba2).Ticks}";
                var map = Stamps(fx.Into);
                if (File.Exists(target) && map.TryGetValue(fx.File, out var was) && was == source) { done.Add(fx.Into); continue; }

                var nif = ReadFromBa2(ba2, fx.File);
                if (nif == null) { log?.Invoke($"nif fix: {fx.File} not found in {fx.Archive}"); continue; }
                var fixedCount = FixNames(nif);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllBytes(target, nif);
                map[fx.File] = source;
                File.WriteAllLines(Path.Combine(modsDir, fx.Into, StampFile), map.Select(p => $"{p.Key}\t{p.Value}"));
                log?.Invoke($"nif fix: {fx.File} — {fixedCount} name(s) fixed");
                done.Add(fx.Into);
            }
            catch (Exception e) { log?.Invoke($"nif fix: {fx.File}: {e.Message}"); }
        }
        return done;
    }

    /// <summary>One file from a Fallout 4 general BA2 (BTDX / GNRL), unpacked; null if the archive has no such file.</summary>
    public static byte[]? ReadFromBa2(string ba2, string path)
    {
        static string Norm(string p) => p.Replace('/', '\\').ToLowerInvariant();
        var want = Norm(path);
        using var fs = File.OpenRead(ba2);
        using var r = new BinaryReader(fs);
        var magic = Encoding.ASCII.GetString(r.ReadBytes(4));
        r.ReadUInt32();
        var type = Encoding.ASCII.GetString(r.ReadBytes(4));
        if (magic != "BTDX" || type != "GNRL") throw new InvalidDataException("not a general BA2 archive");
        var count = r.ReadUInt32();
        var namesAt = r.ReadInt64();
        var recs = new (long Off, uint Packed, uint Size)[count];
        for (var i = 0; i < count; i++)
        {
            r.ReadBytes(16); // name hash, extension, dir hash, flags
            var off = r.ReadInt64();
            var packed = r.ReadUInt32();
            var size = r.ReadUInt32();
            r.ReadUInt32(); // align
            recs[i] = (off, packed, size);
        }
        fs.Seek(namesAt, SeekOrigin.Begin);
        for (var i = 0; i < count; i++)
        {
            var len = r.ReadUInt16();
            var name = Encoding.UTF8.GetString(r.ReadBytes(len));
            if (Norm(name) != want) continue;
            var (off, packed, size) = recs[i];
            fs.Seek(off, SeekOrigin.Begin);
            if (packed == 0) return r.ReadBytes((int)size);
            using var z = new ZLibStream(new MemoryStream(r.ReadBytes((int)packed)), CompressionMode.Decompress);
            var outBuf = new byte[size];
            z.ReadExactly(outBuf);
            return outBuf;
        }
        return null;
    }

    /// <summary>Sets every block name that points outside the string table to -1 (no name). Returns how many were fixed.</summary>
    public static int FixNames(byte[] d)
    {
        var i = Array.IndexOf(d, (byte)'\n') + 1;
        var ver = BitConverter.ToUInt32(d, i);
        if (ver != 0x14020007) throw new InvalidDataException("not a Fallout 4 NIF");
        var blocks = BitConverter.ToInt32(d, i + 5 + 4);
        var bsver = BitConverter.ToUInt32(d, i + 13);
        i += 17;
        void ShortString() => i += 1 + d[i];
        ShortString(); ShortString(); ShortString();
        if (bsver >= 130) ShortString();
        int ntypes = BitConverter.ToUInt16(d, i); i += 2;
        var types = new string[ntypes];
        for (var t = 0; t < ntypes; t++)
        {
            var l = BitConverter.ToInt32(d, i);
            types[t] = Encoding.ASCII.GetString(d, i + 4, l);
            i += 4 + l;
        }
        var typeOf = new int[blocks];
        for (var b = 0; b < blocks; b++) { typeOf[b] = BitConverter.ToUInt16(d, i); i += 2; }
        var sizes = new int[blocks];
        for (var b = 0; b < blocks; b++) { sizes[b] = BitConverter.ToInt32(d, i); i += 4; }
        var strings = BitConverter.ToInt32(d, i); i += 8;
        for (var s = 0; s < strings; s++) i += 4 + BitConverter.ToInt32(d, i);
        var groups = BitConverter.ToInt32(d, i); i += 4 + 4 * groups;

        var fixedCount = 0;
        var off = i;
        for (var b = 0; b < blocks; b++)
        {
            var type = types[typeOf[b]];
            if (NamedBlocks.Contains(type))
            {
                var at = off + (type == "BSLightingShaderProperty" ? 4 : 0);
                var name = BitConverter.ToInt32(d, at);
                if (name != -1 && (name < 0 || name >= strings))
                {
                    BitConverter.GetBytes(-1).CopyTo(d, at);
                    fixedCount++;
                }
            }
            off += sizes[b];
        }
        return fixedCount;
    }
}
