// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using FunkyLauncher.Core;

namespace FunkyLauncher.Services;

/// <summary>
/// Developer test without the window: "--test-install &lt;modlistId&gt; &lt;folder&gt; [ru|en] [hd] [adult]" installs a
/// Nexus-list modlist (Starfield) exactly like the INSTALL button and writes everything into &lt;folder&gt;.log.
/// Use with FUNKYLAUNCHER_DATA=&lt;throwaway folder&gt; so the player's settings stay untouched, and FUNKYLAUNCHER_RECORD
/// to collect the archive hashes of a list still being prepared. Exit code 0 = installed.
/// </summary>
public static class TestInstall
{
    public static bool Is(string[] args) => Array.IndexOf(args, "--test-install") >= 0;

    public static async Task<int> RunAsync(string[] args)
    {
        // "--test-install --extract <archive> <folder>": unpack one archive exactly like an install does
        if (args.Length >= 4 && args[1] == "--extract")
        {
            try { await Archives.ExtractAsync(args[2], args[3], CancellationToken.None); return 0; }
            catch (Exception ex) { File.WriteAllText(args[3].TrimEnd('\\') + ".error.txt", ex.ToString()); return 1; }
        }
        var i = Array.IndexOf(args, "--test-install");
        if (args.Length < i + 3) return 2;
        var id = args[i + 1];
        var dir = Path.GetFullPath(args[i + 2]);
        var opts = args.Skip(i + 3).Select(a => a.ToLowerInvariant()).ToHashSet();
        var log = dir.TrimEnd('\\') + ".log";
        void Log(string t) { try { File.AppendAllText(log, $"{DateTime.Now:HH:mm:ss} {t}\r\n"); } catch { } }

        try
        {
            Loc.Set(opts.Contains("en") ? "en" : "ru");
            Catalog.LoadBuiltIn();
            var s = Settings.Load();
            Modlist.InstallDirOf = m => s.InstallDir(m);
            var ml = Catalog.Find(id) ?? throw new InvalidOperationException("no modlist " + id);
            s.SetInstallDir(ml, dir);
            // "dl=<folder>": reuse archives already downloaded there (e.g. the modlist author's MO2 downloads)
            if (args.Skip(i + 3).FirstOrDefault(a => a.StartsWith("dl=", StringComparison.OrdinalIgnoreCase)) is { } dl)
                s.DownloadDirs[ml.Id] = dl[3..];
            foreach (var (kind, pack) in Addons.Extras(id)) AddonService.Prefs(s, pack).On = opts.Contains(kind);
            s.Save();
            Log($"start {id} → {dir} [{string.Join(' ', opts)}]");

            var last = "";
            var progress = new Progress<InstallProgress>(p =>
            {
                var line = p.Total > 0 && p.Downloaded >= 0 ? $"{p.Status} {p.Downloaded}/{p.Total}" : p.Status;
                if (line != last) { Log(line); last = line; }
                if (!string.IsNullOrWhiteSpace(p.LogLine)) Log("  " + p.LogLine);
            });
            var (outcome, code, manual) = await NexusInstallService.RunAsync(s, ml, progress, CancellationToken.None);
            Log($"base: {outcome} code {code} manual {manual}");
            foreach (var e in NexusInstallService.LastErrors) Log("  ERROR " + e);
            foreach (var m in NexusInstallService.LastManual) Log($"  MANUAL {m.ModName} {m.PageUrl}");
            if (outcome != InstallOutcome.Success) return 1;
            s.MarkInstalled(ml, dir, ml.Version);

            var text = new Progress<string>(t => Log("  " + t));
            foreach (var (pack, on) in new[] { (Addons.Russian(id), opts.Contains("ru")), (Addons.Adult(id), opts.Contains("adult")) })
            {
                if (pack == null) continue;
                if (!on) { AddonService.Disable(s, ml, pack); continue; }
                var r = await AddonService.InstallAsync(s, ml, pack, level2: true, text, CancellationToken.None);
                Log($"{pack.AddonId}: installed {r.Installed}, manual {r.NeedManual.Count}, errors {r.Errors.Count}");
                foreach (var e in r.Errors) Log("  ERROR " + e);
                if (r.Errors.Count > 0 || r.NeedManual.Count > 0) return 1;
            }
            Log("done");
            return 0;
        }
        catch (Exception ex)
        {
            Log("FAILED " + ex);
            return 1;
        }
    }
}
