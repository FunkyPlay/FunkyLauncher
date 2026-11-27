// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using FunkyLauncher.Core;

namespace FunkyLauncher.Services.Build;

/// <summary>
/// №67 (free since 27.11) a copy of the game's own settings (ini/json, key binds) apart from the saves.
/// Made at PLAY when they changed and by a button; kept as zip archives in game-settings\&lt;modlist&gt;, the newest 20.
/// Only the game's own folders (and files the player added) — never Mod Organizer's profile or the modlist (Legal 27.11). Putting a copy back first
/// copies the current files. Only small text files are taken (settings, not caches or saves).
/// </summary>
public static class GameSettings
{
    private const int Keep = 20;
    private static readonly string[] Exts = { ".ini", ".json", ".xml", ".txt", ".cfg" };
    private static readonly string[] SkipNames = { "crashinfo.json", "modlist.txt", "plugins.txt", "loadorder.txt", "archives.txt", "lockedorder.txt" }; // the modlist itself — never

    public static void Start()
    {
        BuildTools.GameStarting += m => { if (BuildData.Current.SettingsAuto) _ = Task.Run(() => { try { Copy(m, onlyChanged: true); } catch (Exception ex) { BuildTools.Log("settings copy: " + ex.Message); } }); };
        BuildTools.Add("gamesettings", "⚙", () => Ui.T("Настройки игр", "Game settings"),
            () => Ui.T("Копия настроек игры и клавиш отдельно от сохранений — вернуть одной кнопкой.", "A copy of the game's settings and key binds apart from saves — put back in one click."),
            () => { var r = new StackPanel(); Fill(r); return r; });
    }

    private static string Dir(ModlistInfo m) => Path.Combine(BuildTools.DataDir, "game-settings", m.Id);

    /// <summary>Folders whose small text files are settings: the game's "My Games" folder or its local app data (not the MO2 profile).</summary>
    public static List<string> Sources(ModlistInfo m)
    {
        var list = new List<string>();
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (m.Game.Contains("Cyberpunk", StringComparison.OrdinalIgnoreCase))
            list.Add(Path.Combine(local, "CD Projekt Red", "Cyberpunk 2077"));
        else if (m.SaveFolder is { } sf && Path.GetFileName(sf.TrimEnd('\\')).Equals("Saves", StringComparison.OrdinalIgnoreCase))
            list.Add(Path.GetDirectoryName(sf.TrimEnd('\\'))!); // Documents\My Games\<game>
        if (BuildData.Current.SettingsExtra.TryGetValue(m.Id, out var extra)) list.AddRange(extra);
        return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>The files a copy takes now.</summary>
    public static List<string> Files(ModlistInfo m)
    {
        var files = new List<string>();
        foreach (var s in Sources(m))
        {
            try
            {
                if (File.Exists(s)) { files.Add(s); continue; }
                if (!Directory.Exists(s)) continue;
                files.AddRange(Directory.EnumerateFiles(s, "*", SearchOption.TopDirectoryOnly).Where(f =>
                    Exts.Contains(Path.GetExtension(f).ToLowerInvariant()) && !SkipNames.Contains(Path.GetFileName(f).ToLowerInvariant())
                    && new FileInfo(f).Length <= 2 << 20));
            }
            catch { }
        }
        return files.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string Stamp(List<string> files) =>
        Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(
            string.Join("|", files.Select(f => { var i = new FileInfo(f); return $"{f}:{i.Length}:{i.LastWriteTimeUtc.Ticks}"; })))));

    /// <summary>Makes a copy; null = nothing to copy or (onlyChanged) nothing changed.</summary>
    public static string? Copy(ModlistInfo m, bool onlyChanged)
    {
        var files = Files(m);
        if (files.Count == 0) return null;
        var stamp = Stamp(files);
        lock (BuildData.Current)
            if (onlyChanged && BuildData.Current.SettingsStamp.TryGetValue(m.Id, out var old) && old == stamp) return null;
        var dir = Dir(m);
        Directory.CreateDirectory(dir);
        var zip = Path.Combine(dir, $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.zip");
        for (var i = 2; File.Exists(zip); i++) zip = Path.Combine(dir, $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}-{i}.zip");
        var map = new Dictionary<string, string>();
        using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            var i = 0;
            foreach (var f in files)
            {
                var entry = $"{i++:D3}_{Path.GetFileName(f)}";
                using (var src = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var dst = z.CreateEntry(entry).Open()) src.CopyTo(dst);
                map[entry] = f;
            }
            using var w = new StreamWriter(z.CreateEntry("funky-settings.json").Open());
            w.Write(JsonSerializer.Serialize(new { modlist = m.Id, title = m.Title, date = DateTime.Now, files = map }));
        }
        lock (BuildData.Current) { BuildData.Current.SettingsStamp[m.Id] = stamp; BuildData.Save(); }
        foreach (var old in Directory.GetFiles(dir, "*.zip").OrderByDescending(x => x).Skip(Keep)) try { File.Delete(old); } catch { } // our own old copies
        BuildTools.Log($"settings copy {m.Id}: {files.Count} files");
        return zip;
    }

    /// <summary>Puts a copy back: the current files are copied first.</summary>
    public static int Restore(ModlistInfo m, string zip)
    {
        if (Ui.GameRunning(m)) throw new InvalidOperationException(Ui.T("Сначала закрой игру.", "Close the game first."));
        Copy(m, onlyChanged: false);
        var n = 0;
        using var z = ZipFile.OpenRead(zip);
        var man = z.GetEntry("funky-settings.json") ?? throw new InvalidOperationException("funky-settings.json");
        using var doc = JsonDocument.Parse(man.Open());
        var allowed = Sources(m).Select(x => Path.GetFullPath(x).TrimEnd('\\')).ToList();
        bool Allowed(string f) => allowed.Any(a => f.Equals(a, StringComparison.OrdinalIgnoreCase) || f.StartsWith(a + "\\", StringComparison.OrdinalIgnoreCase));
        foreach (var p in doc.RootElement.GetProperty("files").EnumerateObject())
        {
            var to = p.Value.GetString();
            if (string.IsNullOrEmpty(to) || z.GetEntry(p.Name) is not { } e) continue;
            to = Path.GetFullPath(to);
            if (!Allowed(to)) continue; // only the game's settings folders and the player's own files — e.g. not an old copy's MO2 profile
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            if (File.Exists(to)) File.SetAttributes(to, FileAttributes.Normal);
            e.ExtractToFile(to, overwrite: true);
            n++;
        }
        BuildTools.Log($"settings restore {m.Id}: {Path.GetFileName(zip)} ({n})");
        return n;
    }

    private static void Fill(StackPanel root)
    {
        root.Children.Clear();
        var d = BuildData.Current;
        root.Children.Add(Ui.Text(Ui.T("Графика, звук, клавиши — всё, что ты настраивал в самой игре. Если обновление игры или мод сбросили настройки, верни копию одной кнопкой. Сохранения, моды и Mod Organizer не трогаются.",
            "Graphics, sound, key binds — all you set in the game itself. If a game update or a mod reset them, put a copy back in one click. Saves, mods and Mod Organizer are not touched."), bottom: 6));
        var auto = new CheckBox { Content = Ui.T("Делать копию при нажатии ИГРАТЬ, если настройки изменились (хранятся последние 20)", "Make a copy at PLAY when the settings changed (the newest 20 are kept)"), IsChecked = d.SettingsAuto };
        auto.Click += (_, _) => { d.SettingsAuto = auto.IsChecked == true; BuildData.Save(); };
        root.Children.Add(auto);

        var m = BuildTools.Selected;
        root.Children.Add(new Border { Height = 12 });
        root.Children.Add(Ui.Title(Ui.T("Сборка", "Modlist") + (m == null ? "" : " — " + m.Title)));
        if (m == null) { root.Children.Add(Ui.Text(Ui.T("Выбери установленную сборку в Библиотеке.", "Pick an installed modlist in Library."))); return; }
        var status = Ui.Text("", 12.5, Ui.Cyan, top: 6);
        var files = Files(m);
        root.Children.Add(Ui.Text(Ui.T($"Файлов настроек: {files.Count}", $"Settings files: {files.Count}") + (files.Count > 0 ? " — " + string.Join(", ", files.Select(Path.GetFileName).Take(8)) + (files.Count > 8 ? "…" : "") : ""), 12.5));
        root.Children.Add(Ui.Row(
            Ui.Button(Ui.T("Сделать копию сейчас", "Copy now"), async () =>
            {
                try { var z = await Task.Run(() => Copy(m, onlyChanged: false)); status.Text = z == null ? Ui.T("Нечего копировать.", "Nothing to copy.") : Ui.T("Готово.", "Done."); Fill(root); }
                catch (Exception ex) { status.Text = ex.Message; }
            }, primary: true, height: 30),
            Ui.Button(Ui.T("Добавить свой файл…", "Add a file…"), () =>
            {
                var dlg = new Microsoft.Win32.OpenFileDialog { Filter = Ui.T("Настройки", "Settings") + "|*.ini;*.json;*.xml;*.txt;*.cfg" };
                if (dlg.ShowDialog() != true) return;
                if (!d.SettingsExtra.TryGetValue(m.Id, out var l)) d.SettingsExtra[m.Id] = l = new();
                if (!l.Contains(dlg.FileName, StringComparer.OrdinalIgnoreCase)) l.Add(dlg.FileName);
                BuildData.Save(); Fill(root);
            }, height: 30),
            Ui.Button(Ui.T("Открыть папку копий", "Open the copies folder"), () =>
            {
                Directory.CreateDirectory(Dir(m));
                System.Diagnostics.Process.Start("explorer.exe", $"\"{Dir(m)}\"");
            }, height: 30)));
        root.Children.Add(status);

        var zips = Directory.Exists(Dir(m)) ? Directory.GetFiles(Dir(m), "*.zip").OrderByDescending(x => x).ToList() : new();
        root.Children.Add(new Border { Height = 10 });
        if (zips.Count == 0) root.Children.Add(Ui.Text(Ui.T("Копий пока нет.", "No copies yet.")));
        foreach (var z in zips.Take(10))
        {
            var when = File.GetLastWriteTime(z);
            var line = Ui.Row(Ui.Text($"{when:dd.MM.yyyy HH:mm}", 13.5, Ui.TextBrush));
            ((TextBlock)line.Children[0]).VerticalAlignment = VerticalAlignment.Center;
            ((TextBlock)line.Children[0]).Margin = new Thickness(0, 0, 12, 0);
            line.Children.Add(Ui.Button(Ui.T("Вернуть", "Put back"), () =>
            {
                if (!Ui.Ask(Ui.T($"Вернуть настройки от {when:dd.MM HH:mm}? Текущие сначала сохранятся в копию.", $"Put back the settings from {when:dd.MM HH:mm}? The current ones are copied first."), "Funky Launcher")) return;
                try { var n = Restore(m, z); status.Text = Ui.T($"Возвращено файлов: {n}.", $"Files put back: {n}."); }
                catch (Exception ex) { status.Text = ex.Message; }
            }, height: 28));
            line.Margin = new Thickness(0, 0, 0, 4);
            root.Children.Add(line);
        }
    }
}
