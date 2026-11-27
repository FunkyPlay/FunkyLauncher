// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using FunkyLauncher.Core;
using Microsoft.Win32;

namespace FunkyLauncher.Services.Build;

/// <summary>
/// №13 (free since 27.11) move an installed modlist to another folder or disk in one click: copy → check (same files, same bytes) →
/// fix the paths inside ModOrganizer.ini → switch the launcher to the new folder → only then remove the old copy (if asked).
/// </summary>
public static class Mover
{
    private static bool _busy;

    public static void Start()
    {
        BuildTools.Add("move", "⇄", () => Ui.T("Перенос сборки", "Move a modlist"),
            () => Ui.T("Перенеси сборку на другой диск без переустановки.", "Move your modlist to another drive without reinstalling."),
            () => { var r = new StackPanel(); Fill(r); return r; });
    }

    private static bool Mo2Running(string dir) =>
        Process.GetProcessesByName("ModOrganizer").Any(p =>
        {
            try { return p.MainModule?.FileName?.StartsWith(dir, StringComparison.OrdinalIgnoreCase) == true; } catch { return false; }
        });

    private static (int Files, long Bytes) Count(string dir)
    {
        var fs = new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories).ToList();
        return (fs.Count, fs.Sum(f => f.Length));
    }

    /// <summary>The move; progress gets "copied of total" text.</summary>
    public static async Task MoveAsync(ModlistInfo m, string newDir, bool removeOld, Action<string> progress)
    {
        var oldDir = Path.GetFullPath(m.InstallDir).TrimEnd('\\');
        newDir = Path.GetFullPath(newDir).TrimEnd('\\');
        if (newDir.StartsWith(oldDir + "\\", StringComparison.OrdinalIgnoreCase) || newDir.Equals(oldDir, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(Ui.T("Новая папка не может быть внутри старой.", "The new folder can't be inside the old one."));
        if (Directory.Exists(newDir) && Directory.EnumerateFileSystemEntries(newDir).Any())
            throw new InvalidOperationException(Ui.T("Папка уже есть и не пустая: ", "The folder exists and isn't empty: ") + newDir);
        if (Ui.GameRunning(m) || Mo2Running(oldDir))
            throw new InvalidOperationException(Ui.T("Сначала закрой игру и Mod Organizer.", "Close the game and Mod Organizer first."));

        var (files, bytes) = await Task.Run(() => Count(oldDir));
        var sameDisk = string.Equals(Path.GetPathRoot(oldDir), Path.GetPathRoot(newDir), StringComparison.OrdinalIgnoreCase);
        if (!sameDisk)
        {
            var free = new DriveInfo(Path.GetPathRoot(newDir)!).AvailableFreeSpace;
            if (free < bytes + (1L << 30))
                throw new InvalidOperationException(Ui.T($"На диске мало места: нужно {Ui.Size(bytes)}, свободно {Ui.Size(free)}.", $"Not enough space: need {Ui.Size(bytes)}, free {Ui.Size(free)}."));
        }

        if (sameDisk)
        {
            progress(Ui.T("Переношу (тот же диск — мгновенно)…", "Moving (same disk — instant)…"));
            Directory.CreateDirectory(Path.GetDirectoryName(newDir)!);
            if (Directory.Exists(newDir)) Directory.Delete(newDir); // empty, checked above
            await Task.Run(() => Directory.Move(oldDir, newDir));
        }
        else
        {
            long done = 0; var n = 0;
            await Task.Run(() =>
            {
                foreach (var f in Directory.EnumerateFiles(oldDir, "*", SearchOption.AllDirectories))
                {
                    var to = Path.Combine(newDir, Path.GetRelativePath(oldDir, f));
                    Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                    File.Copy(f, to, overwrite: false);
                    done += new FileInfo(f).Length;
                    if (++n % 200 == 0) progress(Ui.T($"Копирую: {Ui.Size(done)} из {Ui.Size(bytes)}", $"Copying: {Ui.Size(done)} of {Ui.Size(bytes)}"));
                }
                foreach (var d in Directory.EnumerateDirectories(oldDir, "*", SearchOption.AllDirectories)) // empty folders too
                    Directory.CreateDirectory(Path.Combine(newDir, Path.GetRelativePath(oldDir, d)));
            });
            progress(Ui.T("Проверяю копию…", "Checking the copy…"));
            var (f2, b2) = await Task.Run(() => Count(newDir));
            if (f2 != files || b2 != bytes)
                throw new InvalidOperationException(Ui.T($"Копия не совпала ({f2} из {files} файлов) — старая папка не тронута, новую удали вручную: ", $"The copy doesn't match ({f2} of {files} files) — the old folder is untouched, delete the new one by hand: ") + newDir);
        }

        FixIni(Path.Combine(newDir, "ModOrganizer.ini"), oldDir, newDir);
        var s = BuildTools.Settings;
        s.InstallDirs[m.Id] = newDir;
        if (s.DownloadDirs.TryGetValue(m.Id, out var dl) && dl.StartsWith(oldDir + "\\", StringComparison.OrdinalIgnoreCase))
            s.DownloadDirs[m.Id] = newDir + dl[oldDir.Length..];
        s.Save();
        BuildTools.Log($"moved {m.Id}: {oldDir} → {newDir}");

        if (!sameDisk && removeOld)
        {
            progress(Ui.T("Убираю старую копию…", "Removing the old copy…"));
            await Task.Run(() =>
            {
                foreach (var f in Directory.EnumerateFiles(oldDir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
                Directory.Delete(oldDir, true);
            });
        }
        BuildTools.RefreshUi();
    }

    /// <summary>Replaces the old folder in every spelling Mod Organizer uses: C:/a/b, C:\a\b and C:\\a\\b (inside @ByteArray).</summary>
    public static void FixIni(string ini, string oldDir, string newDir)
    {
        if (!File.Exists(ini)) return;
        var bytes = File.ReadAllBytes(ini);
        var bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        var text = Encoding.UTF8.GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0));
        foreach (var (o, n) in new[]
        {
            (oldDir.Replace('\\', '/'), newDir.Replace('\\', '/')),
            (oldDir.Replace("\\", "\\\\"), newDir.Replace("\\", "\\\\")),
            (oldDir, newDir),
        })
            // only the whole folder name: "C:/Games/X" must not change "C:/Games/X-2"
            text = System.Text.RegularExpressions.Regex.Replace(text, System.Text.RegularExpressions.Regex.Escape(o) + @"(?=[\\/""\r\n)]|$)",
                n.Replace("$", "$$"), System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Multiline);
        File.Copy(ini, ini + ".before-move", overwrite: true);
        File.WriteAllText(ini, text, new UTF8Encoding(bom));
    }

    private static void Fill(StackPanel root)
    {
        root.Children.Clear();
        var list = BuildTools.Modlists();
        if (list.Count == 0) { root.Children.Add(Ui.Text(Ui.T("Нет установленных сборок.", "No installed modlists."))); return; }
        root.Children.Add(Ui.Text(Ui.T("Сохранения и игра остаются на месте — переезжает только папка сборки (Mod Organizer и моды). На тот же диск — мгновенно, на другой — копия с проверкой.",
            "Saves and the game stay where they are — only the modlist folder moves (Mod Organizer and mods). Same disk — instant, another disk — copy and check."), bottom: 8));
        foreach (var m in list)
        {
            var box = new StackPanel();
            box.Children.Add(new TextBlock { Text = m.Title, FontSize = 14, FontWeight = FontWeights.SemiBold });
            box.Children.Add(Ui.Text(Ui.T("Сейчас: ", "Now: ") + m.InstallDir, 12.5, top: 2));
            var size = Ui.Text("", 12.5);
            box.Children.Add(size);
            Task.Run(() => Count(m.InstallDir).Bytes).ContinueWith(t => size.Text = Ui.T("Размер: ", "Size: ") + Ui.Size(t.Result), TaskScheduler.FromCurrentSynchronizationContext());
            var remove = new CheckBox { Content = Ui.T("После проверки убрать старую копию (на другом диске)", "Remove the old copy after the check (another disk)"), IsChecked = true, Margin = new Thickness(0, 6, 0, 0) };
            box.Children.Add(remove);
            var status = Ui.Text("", 12.5, Ui.Cyan, top: 6);
            box.Children.Add(Ui.Row(Ui.Button(Ui.T("Перенести в другую папку…", "Move to another folder…"), async () =>
            {
                if (_busy) return;
                var dlg = new OpenFolderDialog { Title = Ui.T("Куда перенести сборку", "Where to move the modlist") };
                if (dlg.ShowDialog() != true) return;
                var target = Path.Combine(dlg.FolderName, Path.GetFileName(m.InstallDir.TrimEnd('\\')));
                if (!Ui.Ask(Ui.T($"Перенести «{m.Title}»\n\nиз {m.InstallDir}\nв {target}?", $"Move \"{m.Title}\"\n\nfrom {m.InstallDir}\nto {target}?"), "Funky Launcher")) return;
                _busy = true;
                try
                {
                    await MoveAsync(m, target, remove.IsChecked == true, t => Application.Current.Dispatcher.BeginInvoke(() => status.Text = t));
                    status.Text = Ui.T("Готово: ", "Done: ") + target;
                    BuildTools.Notify(Ui.T("Сборка перенесена", "Modlist moved"), target);
                }
                catch (Exception ex) { status.Text = Ui.T("Не получилось: ", "Failed: ") + ex.Message; }
                finally { _busy = false; }
            }, primary: true)));
            box.Children.Add(status);
            root.Children.Add(Ui.Card(box));
        }
    }
}
