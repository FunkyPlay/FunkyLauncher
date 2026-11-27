// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.IO;
using FunkyLauncher.Core;

namespace FunkyLauncher.Services;

/// <summary>
/// 0.1.22: free space check right before an install, an update or an add-on pack — per drive (the modlist folder and
/// the mod archives folder can be on different drives). A full disk mid-install leaves a broken modlist, so the
/// player is told before anything is downloaded. Estimates only: the player can still go on.
/// </summary>
public static class DiskSpace
{
    private const long Gb = 1L << 30;
    /// <summary>Unpacked mods take more than the archives (most are compressed).</summary>
    private const double Unpack = 1.5;
    private const long Reserve = 3 * Gb;

    public sealed record Short(string Drive, long FreeBytes, long NeedBytes);

    /// <summary>Drives that have less free space than the job needs (empty = enough everywhere).</summary>
    public static List<Short> Check(Settings s, Modlist m, IEnumerable<AddonFile>? packs = null, bool update = false)
    {
        var res = new List<Short>();
        foreach (var (drive, bytes) in Needs(s, m, packs, update))
        {
            if (bytes <= 0) continue;
            var (free, name) = SystemCheck.FreeSpace(drive);
            var total = bytes + Reserve;
            if (free < total) res.Add(new Short(name, free, total));
        }
        return res;
    }

    /// <summary>Bytes the job writes, per drive root (without the reserve).</summary>
    public static Dictionary<string, long> Needs(Settings s, Modlist m, IEnumerable<AddonFile>? packs = null, bool update = false)
    {
        var need = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var installDir = s.InstallDir(m);
        var downloads = s.DownloadsDir(m);
        void Add(string dir, long bytes) { var d = Root(dir); need[d] = need.GetValueOrDefault(d) + bytes; }

        var list = packs?.ToList();
        if (list == null)
        {
            // the whole modlist: the base (Nexus list) and the ticked optional packs (the voice-over only in Russian)
            list = new List<AddonFile>();
            if (Mo2Profile.IsNexusList(m) && Addons.Base(m.Id) is { } main) list.Add(main);
            if (s.LangOf(m.Id) == "ru" && Addons.Russian(m.Id) is { } ru) list.Add(ru);
            list.AddRange(Addons.Extras(m.Id).Where(x => AddonService.Prefs(s, x.Pack).On && (x.Kind != "voice" || s.LangOf(m.Id) == "ru")).Select(x => x.Pack));
            if (!Mo2Profile.IsNexusList(m))
            {
                // .wabbajack modlist: about half the size is the archives, half the installed modlist (Neon: 17.8 + 18.6 GB).
                // An update installs a whole new copy next to the current one (kept for rollback) and downloads the changed archives.
                var size = Math.Max(1, m.SizeGb) * Gb;
                Add(installDir, size / 2);
                Add(downloads, update ? size / 6 : size / 2);
            }
        }

        {
            // Nexus list / add-on pack: only what is not on the disk yet
            var modsDir = Path.Combine(installDir, "mods");
            foreach (var x in list.SelectMany(p => p.Mods))
            {
                var parts = x.Archives.Count > 0 ? x.Archives : new List<AddonArchive> { x.Archive };
                var size = parts.Sum(a => Math.Max(0, a.Size));
                if (parts.Any(a => a.LocalFileName.Length == 0 || !ManualQueue.IsPresent(downloads, a.LocalFileName, a.Size)))
                    Add(downloads, size);
                if (x.ModFolder.Length > 0 && !Directory.Exists(Path.Combine(modsDir, x.ModFolder)))
                    Add(installDir, (long)(size * Unpack));
            }
        }

        return need;
    }

    /// <summary>
    /// Before the job: true = enough space, or the player chose "Continue anyway" (logged). "Cancel" is the default.
    /// </summary>
    public static bool Confirm(System.Windows.Window? owner, Settings s, Modlist m, IEnumerable<AddonFile>? packs = null, bool update = false)
    {
        List<Short> shorts;
        try { shorts = Check(s, m, packs, update); } catch (Exception ex) { AppLog.Write("disk check failed: " + ex.Message); return true; }
        if (shorts.Count == 0) return true;
        AppLog.Write("disk: low space " + string.Join("; ", shorts.Select(x => $"{x.Drive} free {x.FreeBytes / Gb} GB need {x.NeedBytes / Gb} GB")));
        var go = Views.Dialog.Show(owner, Loc.F("disk.short.text", Describe(shorts)), Loc.T("disk.short.title"),
            System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning, System.Windows.MessageBoxResult.No,
            Loc.T("disk.short.go"), Loc.T("disk.short.cancel")) == System.Windows.MessageBoxResult.Yes;
        if (go) AppLog.Write("disk: continue anyway");
        return go;
    }

    /// <summary>One line per drive: "D: — free 12 GB, about 30 GB needed".</summary>
    public static string Describe(IEnumerable<Short> shorts) =>
        string.Join("\n", shorts.Select(x => Loc.F("disk.short.line", x.Drive, x.FreeBytes / Gb, (x.NeedBytes + Gb - 1) / Gb)));

    private static string Root(string dir)
    {
        try { return Path.GetPathRoot(Path.GetFullPath(dir)) ?? "C:\\"; } catch { return "C:\\"; }
    }
}
