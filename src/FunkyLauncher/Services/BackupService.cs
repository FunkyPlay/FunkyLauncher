// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using FunkyLauncher.Core;

namespace FunkyLauncher.Services;

public enum BackupKind { Auto, Manual, BeforeRestore }

/// <summary>One save backup: a folder with a full copy of the game's save folder + backup.json.</summary>
public sealed class BackupInfo
{
    public string Dir { get; set; } = "";
    public DateTime Date { get; set; }
    public BackupKind Kind { get; set; }
    /// <summary>Number of saves (sub-folders) in the backup.</summary>
    public int Saves { get; set; }
    public int Files { get; set; }
    /// <summary>Size of the save folder at backup time.</summary>
    public long TotalBytes { get; set; }
    /// <summary>Bytes really written for this backup (unchanged files are hard links to the previous backup).</summary>
    public long NewBytes { get; set; }
    /// <summary>Screenshot of the newest save, relative to Dir (the game puts one into every save).</summary>
    public string? Preview { get; set; }

    public string? PreviewPath => Preview == null ? null : Path.Combine(Dir, "files", Preview);
}

/// <summary>
/// Save backups. Every backup is a complete copy of the save folder, but files that did not change since the
/// previous backup are NTFS hard links to it — 10 backups of 1 GB of saves take about 1 GB, not 10.
/// Restore always copies (never links) files back, so the game can never change a backup.
/// Nothing in the game's save folder is removed until a "before restore" backup of it is made and verified.
/// </summary>
public static class BackupService
{
    public const int KeepAuto = 10;
    private const string InfoName = "backup.json";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static readonly SemaphoreSlim Lock = new(1, 1);

    public static string Root(Settings s) =>
        !string.IsNullOrWhiteSpace(s.BackupDir) ? s.BackupDir : Path.Combine(AppConfig.DataDir, "Backups");

    public static string ModlistDir(Settings s, Modlist m) => Path.Combine(Root(s), m.Id);

    public static bool Supported(Modlist m) => m.ResolveSaveFolder() != null;

    public static bool GameRunning(Modlist m)
    {
        var name = Path.GetFileNameWithoutExtension(m.GameExe);
        return name.Length > 0 && Process.GetProcessesByName(name).Length > 0;
    }

    /// <summary>All backups of a modlist, newest first.</summary>
    public static List<BackupInfo> List(Settings s, Modlist m)
    {
        var dir = ModlistDir(s, m);
        var list = new List<BackupInfo>();
        if (!Directory.Exists(dir)) return list;
        foreach (var d in Directory.EnumerateDirectories(dir))
        {
            try
            {
                var file = Path.Combine(d, InfoName);
                if (!File.Exists(file)) continue; // unfinished or foreign folder
                var info = JsonSerializer.Deserialize<BackupInfo>(File.ReadAllText(file));
                if (info == null) continue;
                info.Dir = d;
                list.Add(info);
            }
            catch { /* a broken backup.json: skip it */ }
        }
        return list.OrderByDescending(x => x.Date).ToList();
    }

    public static BackupInfo? Latest(Settings s, Modlist m) => List(s, m).FirstOrDefault();

    /// <summary>Space really used by all backups of a modlist (hard-linked files counted once).</summary>
    public static long UsedBytes(Settings s, Modlist m) => List(s, m).Sum(x => x.NewBytes);

    /// <summary>
    /// Makes a backup. Returns null when there is nothing to back up (no save folder) or, for an automatic
    /// backup, when nothing changed since the newest backup.
    /// </summary>
    public static async Task<BackupInfo?> CreateAsync(Settings s, Modlist m, BackupKind kind, CancellationToken ct)
    {
        await Lock.WaitAsync(ct);
        try { return await Task.Run(() => Create(s, m, kind, ct), ct); }
        finally { Lock.Release(); }
    }

    private static BackupInfo? Create(Settings s, Modlist m, BackupKind kind, CancellationToken ct)
    {
        var source = m.ResolveSaveFolder() ?? throw new InvalidOperationException(Loc.T("backup.err.nofolder"));
        if (!Directory.Exists(source)) return null;
        var files = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).ToList();
        if (files.Count == 0) return null;

        var prev = Latest(s, m);
        var prevFiles = prev == null ? null : Path.Combine(prev.Dir, "files");
        if (kind == BackupKind.Auto && prev != null && SameContent(source, files, prevFiles!)) return null;

        var root = ModlistDir(s, m);
        Directory.CreateDirectory(root);
        var stamp = DateTime.Now;
        var name = stamp.ToString("yyyy-MM-dd_HH-mm-ss") + "_" + kind.ToString().ToLowerInvariant();
        var dir = Path.Combine(root, name);
        for (var i = 2; Directory.Exists(dir); i++) dir = Path.Combine(root, name + "_" + i);
        var tmp = dir + ".part";
        if (Directory.Exists(tmp)) Directory.Delete(tmp, true); // leftover of an interrupted backup — ours
        var target = Path.Combine(tmp, "files");

        long total = 0, written = 0;
        foreach (var f in files)
        {
            ct.ThrowIfCancellationRequested();
            var rel = Path.GetRelativePath(source, f);
            var dst = Path.Combine(target, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            var fi = new FileInfo(f);
            total += fi.Length;
            var old = prevFiles == null ? null : Path.Combine(prevFiles, rel);
            if (old != null && Unchanged(fi, old) && CreateHardLink(dst, old, IntPtr.Zero)) continue;
            File.Copy(f, dst, overwrite: true);
            File.SetLastWriteTimeUtc(dst, fi.LastWriteTimeUtc);
            written += fi.Length;
        }

        var info = new BackupInfo
        {
            Date = stamp,
            Kind = kind,
            Files = files.Count,
            Saves = Directory.EnumerateDirectories(source).Count(),
            TotalBytes = total,
            NewBytes = written,
            Preview = NewestScreenshot(source),
        };
        File.WriteAllText(Path.Combine(tmp, InfoName), JsonSerializer.Serialize(info, Json));
        Directory.Move(tmp, dir); // the backup appears only when it is complete
        info.Dir = dir;

        if (kind == BackupKind.Auto) PruneAuto(s, m);
        return info;
    }

    /// <summary>
    /// Puts the save folder back to how it was in the backup: first a "before restore" backup of the current
    /// saves (checked: same number of files and bytes), then the folder is emptied and the backup copied in.
    /// </summary>
    public static async Task RestoreAsync(Settings s, Modlist m, BackupInfo backup, CancellationToken ct)
    {
        if (GameRunning(m)) throw new InvalidOperationException(Loc.T("backup.err.running"));
        var source = m.ResolveSaveFolder() ?? throw new InvalidOperationException(Loc.T("backup.err.nofolder"));
        var from = Path.Combine(backup.Dir, "files");
        if (!Directory.Exists(from)) throw new InvalidOperationException(Loc.T("backup.err.broken"));

        await Lock.WaitAsync(ct);
        try
        {
            await Task.Run(() =>
            {
                if (Directory.Exists(source) && Directory.EnumerateFileSystemEntries(source).Any())
                {
                    var safety = Create(s, m, BackupKind.BeforeRestore, ct)
                                 ?? throw new InvalidOperationException(Loc.T("backup.err.safety"));
                    var current = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).Select(f => new FileInfo(f)).ToList();
                    if (safety.Files != current.Count || safety.TotalBytes != current.Sum(f => f.Length))
                        throw new InvalidOperationException(Loc.T("backup.err.safety"));
                    EmptyFolder(source);
                }
                Directory.CreateDirectory(source);
                foreach (var f in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
                {
                    ct.ThrowIfCancellationRequested();
                    var dst = Path.Combine(source, Path.GetRelativePath(from, f));
                    Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                    File.Copy(f, dst, overwrite: true); // a copy, never a link: the game must not touch the backup
                    File.SetAttributes(dst, FileAttributes.Normal);
                    File.SetLastWriteTimeUtc(dst, File.GetLastWriteTimeUtc(f));
                }
            }, ct);
        }
        finally { Lock.Release(); }
    }

    /// <summary>Deletes one backup folder (only a folder of ours: inside the backup root and with backup.json).</summary>
    public static void Delete(Settings s, Modlist m, BackupInfo backup)
    {
        var root = Path.GetFullPath(ModlistDir(s, m)).TrimEnd('\\') + "\\";
        var dir = Path.GetFullPath(backup.Dir);
        if (!dir.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(Path.Combine(dir, InfoName))) return;
        foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(dir, true); // removes only these links; other backups keep their copies of shared files
    }

    /// <summary>Keeps the newest automatic backups only. Manual and "before restore" backups are never pruned.</summary>
    private static void PruneAuto(Settings s, Modlist m)
    {
        foreach (var old in List(s, m).Where(x => x.Kind == BackupKind.Auto).Skip(KeepAuto))
        {
            try { Delete(s, m, old); } catch { /* try again next time */ }
        }
    }

    private static bool Unchanged(FileInfo f, string old)
    {
        var o = new FileInfo(old);
        return o.Exists && o.Length == f.Length && o.LastWriteTimeUtc == f.LastWriteTimeUtc;
    }

    private static bool SameContent(string source, List<string> files, string prevFiles)
    {
        if (!Directory.Exists(prevFiles)) return false;
        if (Directory.EnumerateFiles(prevFiles, "*", SearchOption.AllDirectories).Count() != files.Count) return false;
        return files.All(f => Unchanged(new FileInfo(f), Path.Combine(prevFiles, Path.GetRelativePath(source, f))));
    }

    private static string? NewestScreenshot(string source)
    {
        var shot = new DirectoryInfo(source).EnumerateFiles("*.png", SearchOption.AllDirectories)
            .Where(f => f.Name.Equals("screenshot.png", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault();
        return shot == null ? null : Path.GetRelativePath(source, shot.FullName);
    }

    private static void EmptyFolder(string dir)
    {
        foreach (var f in Directory.EnumerateFiles(dir)) { File.SetAttributes(f, FileAttributes.Normal); File.Delete(f); }
        foreach (var d in Directory.EnumerateDirectories(dir))
        {
            foreach (var f in Directory.EnumerateFiles(d, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(d, true);
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);
}
