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
        // "--test-install --own-folder <modlistId> <folder> <mod folder>": lay down one of our own mod folders from the launcher's files
        if (args.Length >= 5 && args[1] == "--own-folder")
        {
            var errs = new List<string>();
            var ok = Addons.Order(args[2]) is { } o && NexusInstallService.ExtractOwnFolder(args[3], o, args[4], errs);
            if (errs.Count > 0) File.WriteAllText(args[3].TrimEnd('\\') + ".error.txt", string.Join("\n", errs));
            return ok ? 0 : 1;
        }
        // "--test-install --profile <modlistId> <install folder>": write the MO2 profile (and our fixes) like after an install
        // "--test-install --ensure <modlistId> <install folder>": what PLAY does for a modlist installed by an older launcher
        if (args.Length >= 4 && args[1] == "--ensure")
        {
            try
            {
                Catalog.LoadBuiltIn();
                var es = Settings.Load();
                Modlist.InstallDirOf = m => es.InstallDir(m);
                var eml = Catalog.Find(args[2]) ?? throw new InvalidOperationException("no modlist " + args[2]);
                es.SetInstallDir(eml, Path.GetFullPath(args[3]));
                Mo2Profile.EnsureFixes(es, eml);
                return 0;
            }
            catch (Exception ex) { File.WriteAllText(Path.GetFullPath(args[3]).TrimEnd('\\') + ".ensure.error.txt", ex.ToString()); return 1; }
        }
        // "--test-install --translate <modlistId> <install folder> [en]": build the Russian item names for an installed modlist
        // (the Russian pack is counted as on unless "en"); result and reason go to <folder>.translate.log
        if (args.Length >= 4 && args[1] == "--translate")
        {
            var tlog = Path.GetFullPath(args[3]).TrimEnd('\\') + ".translate.log";
            try
            {
                Loc.Set("ru");
                Catalog.LoadBuiltIn();
                var ts = Settings.Load();
                Modlist.InstallDirOf = m => ts.InstallDir(m);
                var tml = Catalog.Find(args[2]) ?? throw new InvalidOperationException("no modlist " + args[2]);
                ts.SetInstallDir(tml, Path.GetFullPath(args[3]));
                if (Addons.Russian(tml.Id) is { } tru) AddonService.Prefs(ts, tru).On = !(args.Length > 4 && args[4] == "en");
                var lines = new List<string>();
                var tr = await TranslationService.BuildAsync(ts, tml, new Progress<string>(lines.Add), CancellationToken.None);
                File.WriteAllLines(tlog, lines.Append($"built={tr.Built} skipped={tr.Skipped} made={tr.Made} patches={tr.Patches} error={tr.Error}"));
                return tr.Error == null ? 0 : 1;
            }
            catch (Exception ex) { File.WriteAllText(tlog, ex.ToString()); return 1; }
        }
        if (args.Length >= 4 && args[1] == "--profile")
        {
            var plog = Path.GetFullPath(args[3]).TrimEnd('\\') + ".profile.log";
            void PLog(string t) => File.AppendAllText(plog, t + Environment.NewLine);
            try
            {
                Catalog.LoadBuiltIn();
                var ps = Settings.Load();
                Modlist.InstallDirOf = m => ps.InstallDir(m);
                var pml = Catalog.Find(args[2]) ?? throw new InvalidOperationException("no modlist " + args[2]);
                ps.SetInstallDir(pml, Path.GetFullPath(args[3]));
                var order = Addons.Order(pml.Id) ?? throw new InvalidOperationException("no order file");
                foreach (var f in NifFixer.Apply(Path.Combine(ps.InstallDir(pml), "mods"), order.NifFixes, PLog))
                    PLog("fixed folder: " + f);
                Mo2Profile.Write(ps, pml, order);
                PLog("profile written");
                return 0;
            }
            catch (Exception ex) { PLog(ex.ToString()); return 1; }
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
