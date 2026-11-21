// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Security.Cryptography;
using FunkyLauncher.Core;

namespace FunkyLauncher.Services;

/// <summary>
/// 0.1.19 check of the game's key files before installing (Funky 21.11). Everything stays on this PC: nothing about
/// the files is sent anywhere. A missing file is the only hard case (a row and a question before installing, "Install
/// anyway" always works); a size or checksum that differs from the one known copy is only written to the launcher log —
/// the same game version exists in several byte-different builds, a warning there would be a false alarm. The exe's
/// version is checked by the existing version row.
/// </summary>
public static class GameFiles
{
    /// <summary>Files bigger than this are compared by size only (hashing them would take long).</summary>
    private const long HashLimit = 256L * 1024 * 1024;

    public sealed record Result(List<string> Missing, List<string> Unverified, int Verified)
    {
        public bool AllVerified => Missing.Count == 0 && Unverified.Count == 0 && Verified > 0;
    }

    /// <summary>Files the catalog lists, minus those another row already checks (the required tools, e.g. REDmod).</summary>
    private static IEnumerable<GameFileRef> Listed(Modlist ml)
    {
        var tools = (ml.RequiredTools ?? new()).SelectMany(t => t.Check.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(Norm).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return (ml.GameFiles ?? new()).Where(f => f.Path.Length > 0 && !tools.Contains(Norm(f.Path)));
    }

    private static string Norm(string p) => p.Replace('/', '\\').Trim('\\');

    public static bool Has(Modlist ml) => Listed(ml).Any();

    public static Result Check(Modlist ml, string gamePath)
    {
        var missing = new List<string>();
        var unverified = new List<string>();
        var verified = 0;
        foreach (var f in Listed(ml))
        {
            var file = Path.Combine(gamePath, Norm(f.Path));
            var name = Path.GetFileName(Norm(f.Path));
            if (!File.Exists(file)) { missing.Add(name); continue; }
            try
            {
                var size = new FileInfo(file).Length;
                if (f.Size is { } want && want != size) { unverified.Add(name); AppLog.Write($"game files: {f.Path} size {size}, known {want}"); continue; }
                if (f.Sha256 is { Count: > 0 } sums && size <= HashLimit)
                {
                    using var s = File.OpenRead(file);
                    var hash = Convert.ToHexString(SHA256.HashData(s));
                    if (!sums.Any(x => x.Equals(hash, StringComparison.OrdinalIgnoreCase)))
                    {
                        unverified.Add(name);
                        AppLog.Write($"game files: {f.Path} sha256 {hash[..12]}…, not one of the {sums.Count} known");
                        continue;
                    }
                }
                verified++;
            }
            catch (Exception ex) { unverified.Add(name); AppLog.Write($"game files: {f.Path}: {ex.Message}"); }
        }
        return new Result(missing, unverified, verified);
    }

    /// <summary>"a, b, c, d" or "a, b, c … +N" (the window has a length limit).</summary>
    public static string Names(List<string> names) =>
        names.Count <= 4 ? string.Join(", ", names) : string.Join(", ", names.Take(3)) + $" … +{names.Count - 3}";
}
