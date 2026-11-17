// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Diagnostics;
using FunkyLauncher.Core;

namespace FunkyLauncher.Services;

/// <summary>
/// "Reset shader cache": deletes the game's compiled shader cache (Starfield: %LOCALAPPDATA%\Starfield\Pipeline.cache)
/// and the graphics drivers' shader caches listed in the catalog, so the game builds its shaders again on the next start
/// — helps against stutter and black / broken textures after mod or driver changes. Only cache files are touched;
/// files in use are skipped. The idea follows the Starfield Shader Cache Reset tool (Nexus 15632), our own code.
/// </summary>
public static class ShaderCache
{
    public static bool Has(Modlist m) => m.ShaderCache.Count > 0;

    private static string? Resolve(string p, string? gamePath)
    {
        var r = p.Replace("{LocalAppData}", Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), StringComparison.OrdinalIgnoreCase)
                 .Replace("{Documents}", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), StringComparison.OrdinalIgnoreCase);
        if (r.Contains("{Game}", StringComparison.OrdinalIgnoreCase))
        {
            if (gamePath == null) return null;
            r = r.Replace("{Game}", gamePath, StringComparison.OrdinalIgnoreCase);
        }
        return r.Contains('{') || !Path.IsPathFullyQualified(r) ? null : Path.GetFullPath(r);
    }

    public static bool GameRunning(Modlist m) =>
        m.GameExe.Length > 0 && Process.GetProcessesByName(Path.GetFileNameWithoutExtension(m.GameExe)).Length > 0;

    /// <summary>Deletes the cache files. Returns (files deleted, bytes freed, files skipped because in use).</summary>
    public static (int Files, long Bytes, int Skipped) Reset(Modlist m, string? gamePath)
    {
        int files = 0, skipped = 0;
        long bytes = 0;
        foreach (var raw in m.ShaderCache)
        {
            var p = Resolve(raw, gamePath);
            if (p == null) continue;
            IEnumerable<string> targets = File.Exists(p) ? new[] { p }
                : Directory.Exists(p) ? Directory.EnumerateFiles(p, "*", SearchOption.AllDirectories)
                : Array.Empty<string>();
            foreach (var f in targets.ToList())
            {
                try
                {
                    var len = new FileInfo(f).Length;
                    File.Delete(f);
                    files++;
                    bytes += len;
                }
                catch { skipped++; } // a driver or another game holds it: fine, it is rebuilt anyway
            }
        }
        return (files, bytes, skipped);
    }
}
