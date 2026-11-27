// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using FunkyLauncher.Core;

namespace FunkyLauncher.Services.Build;

/// <summary>
/// №14 free disk space (free since 27.11): shows what can go and how much it frees, deletes only what the player ticked.
/// Safe by default: the previous version (without the archives folder), duplicate and older archive copies, Mod Organizer's
/// temporary files, old cached modlist files. "All mod archives" is never ticked by itself (updates would download them again).
/// </summary>
public static class Space
{
    public static void Start()
    {
        BuildTools.GameStarting += CheckDisk;
        BuildTools.Add("space", "⌫", () => Ui.T("Чистка места", "Free up space"),
            () => Ui.T("Покажет, сколько места занимают лишние файлы, и удалит отмеченное.", "Shows how much space leftovers take and deletes what you tick."),
            () => { var r = new StackPanel(); Fill(r); return r; });
        BuildTools.Add("disk", "▤", () => Ui.T("Место перед игрой", "Space before play"),
            () => Ui.T("Предупредит при ИГРАТЬ, если на диске почти нет места.", "Warns at PLAY when a drive is nearly full."),
            () => { var r = new StackPanel(); FillDisk(r); return r; });
    }

    private sealed class Item
    {
        public string Title = "";
        public string Note = "";
        public bool Safe = true;
        public List<string> Files = new();
        public List<string> Dirs = new();
        public long Bytes;
        public Action? After;
    }

    private static long DirSize(string d)
    {
        try { return new DirectoryInfo(d).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length); } catch { return 0; }
    }

    private static bool Inside(string path, string dir) =>
        Path.GetFullPath(path).TrimEnd('\\').StartsWith(Path.GetFullPath(dir).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)
        || Path.GetFullPath(path).TrimEnd('\\').Equals(Path.GetFullPath(dir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    // "Name-12345-1-2-1700000000.zip" / "Name 12345 1.2 2026-10-29T12-59Z hash.zip"
    private static readonly Regex OldName = new(@"-(\d+)-[\d-]+-\d{10}(?: \(\d+\))?\.\w+$", RegexOptions.Compiled);
    private static readonly Regex NewName = new(@" (\d+) \S+ \d{4}-\d\d-\d\dT\d\d-\d\dZ", RegexOptions.Compiled);
    private static readonly Regex Copy = new(@" \(\d+\)(\.\w+)$", RegexOptions.Compiled);

    /// <summary>Archive names the installed mods were made from (meta.ini installationFile), current and previous install.</summary>
    private static HashSet<string> Referenced(params string?[] installDirs)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in installDirs.Where(d => d != null && Directory.Exists(Path.Combine(d!, "mods"))))
            foreach (var meta in Directory.EnumerateFiles(Path.Combine(dir!, "mods"), "meta.ini", SearchOption.AllDirectories))
                try
                {
                    foreach (var l in File.ReadLines(meta))
                        if (l.StartsWith("installationFile=", StringComparison.OrdinalIgnoreCase))
                            set.Add(Path.GetFileName(l[17..].Trim().Replace('/', '\\')));
                }
                catch { }
        return set;
    }

    private static List<Item> Scan(ModlistInfo m)
    {
        var s = BuildTools.Settings;
        var items = new List<Item>();
        var downloads = s.DownloadDirs.TryGetValue(m.Id, out var dd) && dd.Length > 0 ? dd : Path.Combine(m.InstallDir, "downloads");
        s.Previous.TryGetValue(m.Id, out var prev);
        // only a real old modlist folder (Mod Organizer in it) that doesn't hold the current one
        var prevDir = prev != null && Directory.Exists(prev.Dir) && File.Exists(Path.Combine(prev.Dir, "ModOrganizer.exe"))
                      && !Inside(m.InstallDir, prev.Dir) && Path.GetPathRoot(prev.Dir)?.TrimEnd('\\') != prev.Dir.TrimEnd('\\') ? prev.Dir : null;

        // 1) previous version (the archives folder inside it stays)
        if (prevDir != null)
        {
            var it = new Item
            {
                Title = Ui.T($"Прошлая версия сборки {prev!.Version}", $"Previous modlist version {prev!.Version}"),
                Note = prevDir + Ui.T(" · откатиться на неё будет нельзя", " · no rollback to it afterwards"),
            };
            foreach (var e in Directory.EnumerateFileSystemEntries(prevDir))
            {
                if (Inside(downloads, e)) continue; // the archives folder (or the folder holding it) is not touched here
                if (Directory.Exists(e)) { it.Dirs.Add(e); it.Bytes += DirSize(e); } else { it.Files.Add(e); it.Bytes += new FileInfo(e).Length; }
            }
            it.After = () => { s.Previous.Remove(m.Id); s.Save(); };
            items.Add(it);
        }

        // 2) archives: duplicate copies and older versions nobody uses
        if (Directory.Exists(downloads))
        {
            var refd = Referenced(m.InstallDir, prevDir);
            var arch = new DirectoryInfo(downloads).GetFiles().Where(f => !f.Name.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) && !f.Name.EndsWith(".part", StringComparison.OrdinalIgnoreCase)).ToList();
            var dup = new Item { Title = Ui.T("Повторные копии архивов «(1)»", "Duplicate archive copies \"(1)\""), Note = Ui.T("тот же файл, скачанный второй раз", "the same file downloaded twice") };
            foreach (var f in arch.Where(f => Copy.IsMatch(f.Name) && !refd.Contains(f.Name)))
            {
                var orig = new FileInfo(Path.Combine(downloads, Copy.Replace(f.Name, "$1")));
                if (orig.Exists && orig.Length == f.Length) { dup.Files.Add(f.FullName); dup.Bytes += f.Length; }
            }
            var old = new Item { Title = Ui.T("Старые версии архивов", "Older archive versions"), Note = Ui.T("у мода есть архив новее, а этот сборкой не используется", "the mod has a newer archive and the modlist doesn't use this one") };
            foreach (var g in arch.Where(f => !dup.Files.Contains(f.FullName)).GroupBy(f => (OldName.Match(f.Name) is { Success: true } a ? a : NewName.Match(f.Name)) is { Success: true } x ? x.Groups[1].Value : "").Where(g => g.Key.Length > 0 && g.Count() > 1))
            {
                var newest = g.MaxBy(f => f.LastWriteTimeUtc)!;
                foreach (var f in g.Where(f => f != newest && !refd.Contains(f.Name))) { old.Files.Add(f.FullName); old.Bytes += f.Length; }
            }
            foreach (var it in new[] { dup, old }.Where(i => i.Files.Count > 0))
            {
                foreach (var f in it.Files.ToList()) if (File.Exists(f + ".meta")) it.Files.Add(f + ".meta");
                items.Add(it);
            }

            var all = new Item
            {
                Title = Ui.T("Все архивы модов", "All mod archives"), Safe = false,
                Note = Ui.T("нужны только для обновления и починки сборки — без них лаунчер скачает моды заново (долго)", "only needed to update or repair the modlist — without them the launcher downloads the mods again (slow)"),
            };
            foreach (var f in arch) { all.Files.Add(f.FullName); all.Bytes += f.Length; if (File.Exists(f.FullName + ".meta")) all.Files.Add(f.FullName + ".meta"); }
            if (all.Files.Count > 0) items.Add(all);
        }

        // 3) Mod Organizer temporary files
        var tmp = new Item { Title = Ui.T("Временные файлы Mod Organizer", "Mod Organizer temporary files"), Note = Ui.T("отчёты о вылетах, кэш браузера, журналы старше недели", "crash dumps, browser cache, logs older than a week") };
        foreach (var d in new[] { "crashDumps", "webcache", "__temp__" }.Select(x => Path.Combine(m.InstallDir, x)).Where(Directory.Exists))
            foreach (var e in Directory.EnumerateFileSystemEntries(d))
            {
                if (Directory.Exists(e)) { tmp.Dirs.Add(e); tmp.Bytes += DirSize(e); } else { tmp.Files.Add(e); tmp.Bytes += new FileInfo(e).Length; }
            }
        var logs = Path.Combine(m.InstallDir, "logs");
        if (Directory.Exists(logs))
            foreach (var f in new DirectoryInfo(logs).GetFiles().Where(f => f.LastWriteTime < DateTime.Now.AddDays(-7))) { tmp.Files.Add(f.FullName); tmp.Bytes += f.Length; }
        if (tmp.Bytes > 0) items.Add(tmp);

        // 4) launcher cache: modlist files of older versions
        var cache = Path.Combine(BuildTools.DataDir, "modlists", m.Id);
        if (Directory.Exists(cache))
        {
            var c = new Item { Title = Ui.T("Кэш лаунчера: файлы старых версий сборки", "Launcher cache: files of older modlist versions") };
            var current = m.Version ?? "";
            var files = new DirectoryInfo(cache).GetFiles();
            var newest = files.MaxBy(f => f.LastWriteTimeUtc); // the next update's file stays too
            foreach (var f in files.Where(f => f != newest && (current.Length == 0 || !f.Name.StartsWith(current + ".", StringComparison.OrdinalIgnoreCase))))
            { c.Files.Add(f.FullName); c.Bytes += f.Length; }
            if (c.Bytes > 0) items.Add(c);
        }
        return items;
    }

    private static void Fill(StackPanel root)
    {
        root.Children.Clear();
        var m = BuildTools.Selected;
        if (m == null) { root.Children.Add(Ui.Text(Ui.T("Выбери установленную сборку в Библиотеке.", "Pick an installed modlist in Library."))); return; }
        root.Children.Add(Ui.Title(Ui.T("Что можно убрать — ", "What can go — ") + m.Title));
        var status = Ui.Text(Ui.T("Считаю…", "Counting…"), 12.5, Ui.Cyan, top: 6);
        var list = new StackPanel();
        root.Children.Add(list);
        root.Children.Add(status);
        Task.Run(() => Scan(m)).ContinueWith(t =>
        {
            if (t.Exception != null) { status.Text = t.Exception.InnerException?.Message ?? ""; return; }
            var items = t.Result;
            if (items.Count == 0) { status.Text = Ui.T("Чисто — убирать нечего.", "All clean — nothing to remove."); return; }
            var checks = new List<(CheckBox Cb, Item It)>();
            var total = Ui.Text("", 14, Ui.TextBrush, top: 8);
            void Sum() => total.Text = Ui.T("Освободится: ", "Will free: ") + Ui.Size(checks.Where(c => c.Cb.IsChecked == true).Sum(c => c.It.Bytes));
            foreach (var it in items)
            {
                var cb = new CheckBox { IsChecked = it.Safe, Margin = new Thickness(0, 6, 0, 0) };
                var txt = new StackPanel();
                txt.Children.Add(new TextBlock { Text = $"{it.Title} — {Ui.Size(it.Bytes)}", FontSize = 13.5, FontWeight = FontWeights.SemiBold, Foreground = it.Safe ? Ui.TextBrush : Ui.Pink, TextWrapping = TextWrapping.Wrap });
                if (it.Note.Length > 0) txt.Children.Add(Ui.Text(it.Note, 12));
                cb.Content = txt;
                cb.Click += (_, _) => Sum();
                checks.Add((cb, it));
                list.Children.Add(cb);
            }
            Sum();
            list.Children.Add(total);
            list.Children.Add(Ui.Row(Ui.Button(Ui.T("Удалить отмеченное", "Delete ticked"), async () =>
            {
                var pick = checks.Where(c => c.Cb.IsChecked == true).Select(c => c.It).ToList();
                if (pick.Count == 0) return;
                if (Ui.GameRunning(m)) { status.Text = Ui.T("Сначала закрой игру.", "Close the game first."); return; }
                if (!Ui.Ask(Ui.T($"Удалить навсегда (без корзины):\n\n{string.Join("\n", pick.Select(p => "• " + p.Title + " — " + Ui.Size(p.Bytes)))}\n\nВернуть будет нельзя.",
                                 $"Delete for good (not to the Recycle Bin):\n\n{string.Join("\n", pick.Select(p => "• " + p.Title + " — " + Ui.Size(p.Bytes)))}\n\nThis can't be undone."), "Funky Launcher")) return;
                status.Text = Ui.T("Удаляю…", "Deleting…");
                var (freed, errors) = await Task.Run(() => Delete(pick));
                foreach (var p in pick) try { p.After?.Invoke(); } catch { }
                BuildTools.Log($"space {m.Id}: freed {freed} bytes, {errors} errors");
                BuildTools.RefreshUi();
                Fill(root);
            }, primary: true)));
            status.Text = "";
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private static (long Freed, int Errors) Delete(List<Item> items)
    {
        long freed = 0; var errors = 0;
        foreach (var it in items)
        {
            foreach (var f in it.Files)
                try { if (File.Exists(f)) { var n = new FileInfo(f).Length; File.SetAttributes(f, FileAttributes.Normal); File.Delete(f); freed += n; } } catch { errors++; }
            foreach (var d in it.Dirs)
                try
                {
                    if (!Directory.Exists(d)) continue;
                    var n = DirSize(d);
                    foreach (var f in Directory.EnumerateFiles(d, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
                    Directory.Delete(d, true);
                    freed += n;
                }
                catch { errors++; }
        }
        return (freed, errors);
    }

    // ---------- №46 space before play (free since 27.11) ----------

    public sealed record DriveState(string Root, string What, long Free, bool Low);

    public static List<DriveState> Drives(ModlistInfo? m)
    {
        var want = new List<(string Path, string What)>();
        if (m != null)
        {
            want.Add((m.InstallDir, Ui.T("сборка", "modlist")));
            if (File.Exists(m.GameExe)) want.Add((m.GameExe, Ui.T("игра", "game")));
            if (m.SaveFolder != null) want.Add((m.SaveFolder, Ui.T("сохранения", "saves")));
        }
        want.Add((Path.GetTempPath(), Ui.T("Windows и временные файлы", "Windows and temp files")));
        var min = BuildData.Current.DiskMinGb * (1L << 30);
        var list = new List<DriveState>();
        foreach (var g in want.Where(w => w.Path.Length > 2).GroupBy(w => Path.GetPathRoot(w.Path)!.ToUpperInvariant()))
        {
            try
            {
                var di = new DriveInfo(g.Key);
                if (!di.IsReady) continue;
                list.Add(new DriveState(g.Key.TrimEnd('\\'), string.Join(", ", g.Select(x => x.What).Distinct()), di.AvailableFreeSpace, di.AvailableFreeSpace < min));
            }
            catch { }
        }
        return list;
    }

    private static void CheckDisk(ModlistInfo m)
    {
        if (!BuildData.Current.DiskCheck) return;
        var low = Drives(m).Where(x => x.Low).ToList();
        if (low.Count == 0) return;
        BuildTools.Notify(Ui.T("Мало места на диске", "Low disk space"),
            string.Join("; ", low.Select(x => $"{x.Root} ({x.What}): {Ui.Size(x.Free)}")) + ". " +
            Ui.T("С модами игра может вылететь или не сохраниться. Освободи место: Инструменты → «Сборка» → «Чистка места».", "With mods the game may crash or fail to save. Free some space: Tools → \"Modlist\" → \"Free up space\"."));
        BuildTools.Log("disk low: " + string.Join(", ", low.Select(x => $"{x.Root} {x.Free}")));
    }

    private static void FillDisk(StackPanel root)
    {
        root.Children.Clear();
        var d = BuildData.Current;
        root.Children.Add(Ui.Text(Ui.T("Игра с модами пишет кэш шейдеров, журналы и сохранения. Когда на диске почти нет места, она вылетает или сохранение получается битым. Лаунчер проверит место при нажатии ИГРАТЬ и покажет уведомление — игра всё равно запустится.",
            "A modded game writes shader cache, logs and saves. When a drive is nearly full, it crashes or a save comes out broken. The launcher checks space at PLAY and shows a pop-up — the game still starts."), bottom: 8));
        var on = new CheckBox { Content = Ui.T("Проверять место перед игрой", "Check space before play"), IsChecked = d.DiskCheck };
        on.Click += (_, _) => { d.DiskCheck = on.IsChecked == true; BuildData.Save(); };
        root.Children.Add(on);
        var label = Ui.Text(Ui.T("Предупреждать, если свободно меньше:", "Warn when free space is under:"), 13);
        label.VerticalAlignment = VerticalAlignment.Center;
        label.Margin = new Thickness(0, 0, 10, 0);
        var row = Ui.Row(label);
        foreach (var gb in new[] { 5, 10, 20, 40 })
        {
            var sel = d.DiskMinGb == gb;
            row.Children.Add(Ui.Button((sel ? "✓ " : "") + Ui.T($"{gb} ГБ", $"{gb} GB"), () => { d.DiskMinGb = gb; BuildData.Save(); FillDisk(root); }, primary: sel, height: 30));
        }
        root.Children.Add(row);

        root.Children.Add(new Border { Height = 12 });
        var m = BuildTools.Selected;
        root.Children.Add(Ui.Title(Ui.T("Сейчас", "Now") + (m == null ? "" : " — " + m.Title)));
        foreach (var x in Drives(m))
            root.Children.Add(Ui.Text($"{(x.Low ? "⚠" : "✓")} {x.Root} ({x.What}): " + Ui.T("свободно ", "free ") + Ui.Size(x.Free), 13.5, x.Low ? Ui.Pink : Ui.Cyan, top: 2));
    }
}
