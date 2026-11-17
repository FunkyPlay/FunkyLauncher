// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Security.Cryptography;
using FunkyLauncher.Core;

namespace FunkyLauncher.Services;

/// <summary>
/// Installs a modlist from plain Nexus lists (Starfield) instead of a .wabbajack file: Wabbajack checks the game's own
/// files against a known release and would refuse a game version it does not know, while this only checks the game
/// exe's version. Steps: the game → the official Mod Organizer 2 as a portable instance next to nothing else →
/// every mod of the base list (and HD planets if chosen) downloaded under the player's own Nexus login (wabbajack-cli),
/// checked by SHA-256 and unpacked → the MO2 profile written from the order file. Russian and 18+ come right after,
/// through the same add-on code as for Neon Nights.
/// </summary>
public static class NexusInstallService
{
    /// <summary>Files the last run could not download (free Nexus account): shown in the manual download window.</summary>
    public static List<ManualItem> LastManual { get; private set; } = new();
    /// <summary>Problems of the last run (hash mismatches, unpack errors) for the screen and a report.</summary>
    public static List<string> LastErrors { get; private set; } = new();

    public static async Task<(InstallOutcome Outcome, int ExitCode, int Manual)> RunAsync(
        Settings s, Modlist ml, IProgress<InstallProgress> progress, CancellationToken token)
    {
        LastManual = new();
        LastErrors = new();
        try
        {
            var main = Addons.Base(ml.Id) ?? throw new InvalidOperationException(Loc.T("check.list.no"));
            var order = Addons.Order(ml.Id) ?? throw new InvalidOperationException(Loc.T("check.list.no"));

            progress.Report(new InstallProgress(Loc.T("inst.preparing"), 0, 0, null));
            if (WabbajackService.FindCli() == null)
            {
                var dl = new Progress<int>(p => progress.Report(new InstallProgress(Loc.F("inst.wj.download", p), 0, 0, null)));
                await WabbajackService.DownloadAsync(dl, token);
            }

            progress.Report(new InstallProgress(Loc.T("inst.checkgame"), 0, 0, null));
            var game = await SystemCheck.EnsureGameReadyAndGetAsync(ml, token);

            var installDir = s.InstallDir(ml);
            var downloads = s.DownloadsDir(ml);
            Directory.CreateDirectory(installDir);
            Directory.CreateDirectory(downloads);
            if (!s.DownloadDirs.ContainsKey(ml.Id)) { s.DownloadDirs[ml.Id] = downloads; s.Save(); }

            await EnsureMo2Async(installDir, downloads, progress, token);
            if (!File.Exists(Path.Combine(installDir, "ModOrganizer.ini")) || Mo2Profile.GamePath(installDir) is not { } gp
                || !gp.Equals(game.Path, StringComparison.OrdinalIgnoreCase))
                Mo2Profile.WriteInstance(installDir, ml, game.Path);

            // base first, then HD planets if the player ticked them (both: every mod of the list)
            var packs = new List<AddonFile> { main };
            // optional packs that are ticked; the Russian voice-over only for the Russian version (it can be 10 GB)
            packs.AddRange(Addons.Extras(ml.Id).Where(x => AddonService.Prefs(s, x.Pack).On && (x.Kind != "voice" || s.LangOf(ml.Id) == "ru"))
                .Select(x => x.Pack));
            var total = packs.Sum(p => p.Mods.Count);
            var before = 0;
            foreach (var pack in packs)
            {
                var start = before;
                var text = new Progress<string>(t => progress.Report(new InstallProgress(Loc.T("inst.running"), -1, total, t)));
                var r = await AddonService.InstallAsync(s, ml, pack, level2: true, text, token,
                    (done, _) => progress.Report(new InstallProgress(Loc.T("inst.running"), start + done, total, null)));
                if (pack == main) { AddonService.Prefs(s, main).On = true; s.Save(); }
                LastManual.AddRange(r.NeedManual);
                LastErrors.AddRange(r.Errors);
                before += pack.Mods.Count;
            }
            Mo2Profile.Write(s, ml, order);

            if (LastManual.Count > 0) return (InstallOutcome.NeedsManualDownloads, 3, LastManual.Count);
            if (LastErrors.Count > 0) return (InstallOutcome.Failed, LastErrors.Count, 0);
            return (InstallOutcome.Success, 0, 0);
        }
        catch (OperationCanceledException)
        {
            return (InstallOutcome.Cancelled, -1, 0);
        }
    }

    /// <summary>Official MO2 release (catalog "mo2": url + sha256), unpacked into the install folder once.</summary>
    private static async Task EnsureMo2Async(string installDir, string downloads, IProgress<InstallProgress> progress, CancellationToken token)
    {
        if (File.Exists(Path.Combine(installDir, "ModOrganizer.exe"))) return;
        var src = Catalog.Mo2 ?? throw new InvalidOperationException("Mod Organizer 2: no download source in the catalog");
        var archive = Path.Combine(downloads, Path.GetFileName(new Uri(src.Url).LocalPath));
        if (!File.Exists(archive) || !await HashOk(archive, src.Sha256, token))
        {
            var dl = new Progress<int>(p => progress.Report(new InstallProgress(Loc.F("inst.mo2.download", p), 0, 0, null)));
            await WabbajackService.DownloadFileAsync(src.Url, archive, dl, token);
            if (!await HashOk(archive, src.Sha256, token)) throw new InvalidOperationException(Loc.F("addon.err.hash", "Mod Organizer 2"));
        }
        progress.Report(new InstallProgress(Loc.T("inst.mo2.unpack"), 0, 0, null));
        var tmp = Path.Combine(installDir, ".mo2-unpack");
        if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
        var code = 0;
        try { await Archives.ExtractAsync(archive, tmp, token); }
        catch (OperationCanceledException) { throw; }
        catch { code = 1; }
        var root = tmp;
        // some releases have one top folder
        while (!File.Exists(Path.Combine(root, "ModOrganizer.exe")) && Directory.GetDirectories(root) is { Length: 1 } one && Directory.GetFiles(root).Length == 0)
            root = one[0];
        if (code != 0 || !File.Exists(Path.Combine(root, "ModOrganizer.exe")))
        {
            Directory.Delete(tmp, true);
            throw new InvalidOperationException("Mod Organizer 2: unpack failed (" + code + ")");
        }
        foreach (var e in Directory.EnumerateFileSystemEntries(root))
        {
            var dst = Path.Combine(installDir, Path.GetFileName(e));
            if (Directory.Exists(e)) { if (!Directory.Exists(dst)) Directory.Move(e, dst); }
            else File.Move(e, dst, overwrite: true);
        }
        Directory.Delete(tmp, true);
    }

    private static async Task<bool> HashOk(string file, string sha256, CancellationToken token)
    {
        await using var f = File.OpenRead(file);
        return Convert.ToHexString(await SHA256.HashDataAsync(f, token)).Equals(sha256, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// "Verify files" for a Nexus-list modlist: MO2 is there, every wanted mod of the base and the packs that are on
    /// has its folder with content. Returns the missing ones (they come back with INSTALL — downloaded archives are reused).
    /// </summary>
    public static (bool Ok, List<string> Missing) Verify(Settings s, Modlist ml)
    {
        var missing = new List<string>();
        var dir = s.InstallDir(ml);
        if (!File.Exists(Path.Combine(dir, "ModOrganizer.exe"))) missing.Add("ModOrganizer.exe");
        var mods = Path.Combine(dir, "mods");
        var mainId = Addons.Base(ml.Id)?.AddonId;
        foreach (var a in Addons.All(ml.Id))
        {
            var p = AddonService.Prefs(s, a);
            if (a.AddonId != mainId && !p.On) continue;
            foreach (var x in AddonService.Wanted(a, a.AddonId == mainId || p.Level2))
            {
                var d = Path.Combine(mods, x.ModFolder);
                if (!Directory.Exists(d) || !Directory.EnumerateFileSystemEntries(d).Any(e => !e.EndsWith("meta.ini", StringComparison.OrdinalIgnoreCase)))
                    missing.Add(x.DisplayName.ToString());
            }
        }
        return (missing.Count == 0, missing);
    }
}
