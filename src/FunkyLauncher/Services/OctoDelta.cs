// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace FunkyLauncher.Services;

/// <summary>
/// Applies an Octodiff delta ("OCTODELTA", the format Wabbajack uses for the few files a modlist changes): the new file
/// is built from pieces of the original plus the delta's own bytes. The delta names the SHA-1 of the original it was
/// made from — it is applied only to exactly that file, so an already patched or a different file is left alone.
/// </summary>
public static class OctoDelta
{
    /// <summary>True if the file was patched now; false if it is not the expected original (e.g. patched before).</summary>
    public static bool Apply(string file, Stream delta, long expectedSize)
    {
        using var r = new BinaryReader(delta, Encoding.UTF8, leaveOpen: true);
        if (Encoding.ASCII.GetString(r.ReadBytes(9)) != "OCTODELTA") throw new InvalidDataException("not an Octodiff delta");
        r.ReadByte(); // version
        var algo = r.ReadString();
        var hashLen = r.ReadInt32();
        var wantHash = r.ReadBytes(hashLen);
        if (Encoding.ASCII.GetString(r.ReadBytes(3)) != ">>>") throw new InvalidDataException("bad delta header");
        if (!algo.Equals("SHA1", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("unknown hash " + algo);

        byte[] haveHash;
        using (var f = File.OpenRead(file)) haveHash = SHA1.HashData(f);
        if (!haveHash.AsSpan().SequenceEqual(wantHash)) return false;

        var tmp = file + ".funkypatch";
        using (var src = File.OpenRead(file))
        using (var dst = File.Create(tmp))
        {
            var buf = new byte[1 << 16];
            while (delta.Position < delta.Length)
            {
                var cmd = r.ReadByte();
                if (cmd == 0x60) // copy a piece of the original
                {
                    var start = r.ReadInt64();
                    var length = r.ReadInt64();
                    src.Seek(start, SeekOrigin.Begin);
                    while (length > 0)
                    {
                        var n = src.Read(buf, 0, (int)Math.Min(buf.Length, length));
                        if (n <= 0) throw new InvalidDataException("delta reads past the original");
                        dst.Write(buf, 0, n);
                        length -= n;
                    }
                }
                else if (cmd == 0x80) // new bytes from the delta
                {
                    var length = r.ReadInt64();
                    while (length > 0)
                    {
                        var n = r.Read(buf, 0, (int)Math.Min(buf.Length, length));
                        if (n <= 0) throw new InvalidDataException("delta is cut short");
                        dst.Write(buf, 0, n);
                        length -= n;
                    }
                }
                else throw new InvalidDataException($"unknown delta command 0x{cmd:x2}");
            }
        }
        if (expectedSize > 0 && new FileInfo(tmp).Length != expectedSize)
        {
            File.Delete(tmp);
            throw new InvalidDataException("patched file has the wrong size");
        }
        File.Move(tmp, file, overwrite: true);
        return true;
    }
}
