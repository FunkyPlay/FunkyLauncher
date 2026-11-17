// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using SharpCompress.Archives;
using SharpCompress.Common;

namespace FunkyLauncher.Services;

/// <summary>
/// Unpacks mod archives (zip, 7z, rar) inside the launcher with SharpCompress. wabbajack-cli "extract" silently
/// dropped a whole folder of one Fallout 4 archive (Vtaw Wardrobe 9: VaultSuit\111), so mods are not unpacked with it.
/// Paths that would leave the target folder are refused.
/// </summary>
public static class Archives
{
    /// <summary>
    /// SharpCompress first; an archive it refuses (e.g. LZMA2 with a dictionary over 64 MB) falls back to
    /// wabbajack-cli "extract" — it handles those, and the missing-folder problem was not seen with them.
    /// </summary>
    public static async Task ExtractAsync(string archive, string outDir, CancellationToken token)
    {
        try { await ExtractManagedAsync(archive, outDir, token); }
        catch (OperationCanceledException) { throw; }
        catch (Exception first)
        {
            if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
            Directory.CreateDirectory(outDir);
            var code = await WabbajackService.RunAsync($"extract -i \"{archive}\" -o \"{outDir}\"", _ => { }, token);
            if (code != 0 || !Directory.EnumerateFileSystemEntries(outDir).Any())
                throw new InvalidOperationException(first.Message + " / wabbajack-cli extract " + code);
        }
    }

    private static Task ExtractManagedAsync(string archive, string outDir, CancellationToken token) => Task.Run(() =>
    {
        Directory.CreateDirectory(outDir);
        var root = Path.GetFullPath(outDir).TrimEnd('\\') + "\\";
        using var a = ArchiveFactory.OpenArchive(archive);
        string Target(string key)
        {
            var dst = Path.GetFullPath(Path.Combine(root, key.Replace('/', '\\').TrimStart('\\')));
            if (!dst.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("bad path in archive: " + key);
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            return dst;
        }
        if (a.IsSolid || a.Type == ArchiveType.SevenZip)
        {
            using var reader = a.ExtractAllEntries(); // sequential: the only fast way for solid 7z / rar
            while (reader.MoveToNextEntry())
            {
                token.ThrowIfCancellationRequested();
                var e = reader.Entry;
                if (e.IsDirectory || string.IsNullOrEmpty(e.Key)) continue;
                using var fs = File.Create(Target(e.Key));
                reader.WriteEntryTo(fs);
            }
        }
        else
        {
            foreach (var e in a.Entries) // zip, non-solid rar
            {
                token.ThrowIfCancellationRequested();
                if (e.IsDirectory || string.IsNullOrEmpty(e.Key)) continue;
                using var fs = File.Create(Target(e.Key));
                using var src = e.OpenEntryStream();
                src.CopyTo(fs);
            }
        }
        if (!Directory.EnumerateFileSystemEntries(outDir).Any()) throw new InvalidOperationException("empty archive");
    }, token);
}
