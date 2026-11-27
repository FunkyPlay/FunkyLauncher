// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using FunkyLauncher.Core;
using Microsoft.Win32;

namespace FunkyLauncher.Services.Build;

/// <summary>
/// №8 (free since 27.11) moving to a new PC: one file with the saves of every installed modlist, the
/// launcher settings, the player's own profiles and presets, plus the list of modlists to install there. Paths of this
/// PC (install folders, window place) never travel: they mean nothing on another PC. Settings sync through a cloud
/// folder is not part of the launcher.
/// </summary>
public static class MoveFile
{
    /// <summary>Launcher settings that make sense on another PC.</summary>
    private static readonly string[] Portable =
    {
        "Language", "AutoBackup", "ShootIcons", "ShootWallpaper", "ShootWindows", "ShootTaskbar", "ScreenHotkeys",
        "GameApps", "GameTorrents", "GamePower", "GameOneDrive", "GameRelaunch", "GameOnPlay", "LoadNews", "UpdateCatalog",
        "ModlistLang", "Addons", "AdultWanted",
    };

    public static void Start()
    {
        BuildTools.Add("move-pc", "⇪", () => Ui.T("Переезд на новый ПК", "Moving to a new PC"),
            () => Ui.T("Сохранения, настройки и список сборок на новый ПК одним файлом.", "Saves, settings and the modlist list on a new PC from a single file."),
            () => { var r = new StackPanel(); Fill(r); return r; });
    }

    private static JsonObject PortableSettings()
    {
        var o = new JsonObject();
        foreach (var name in Portable)
            if (typeof(Settings).GetProperty(name) is { } p)
                o[name] = JsonSerializer.SerializeToNode(p.GetValue(BuildTools.Settings), p.PropertyType);
        return o;
    }

    private static void ApplySettings(JsonObject o)
    {
        foreach (var name in Portable)
            if (typeof(Settings).GetProperty(name) is { CanWrite: true } p && o[name] is { } v)
                try { p.SetValue(BuildTools.Settings, v.Deserialize(p.PropertyType)); } catch { }
        BuildTools.Settings.Save();
    }

    /// <summary>The modlist tools' choices that mean the same on another PC (own profiles, presets, space check).</summary>
    private static JsonObject PortableTools()
    {
        var d = BuildData.Current;
        return new JsonObject
        {
            ["Profiles"] = JsonSerializer.SerializeToNode(d.Profiles), ["ActiveProfile"] = JsonSerializer.SerializeToNode(d.ActiveProfile),
            ["Preset"] = JsonSerializer.SerializeToNode(d.Preset), ["DiskCheck"] = d.DiskCheck, ["DiskMinGb"] = d.DiskMinGb,
            ["GameWatch"] = d.GameWatch, ["GameWatchBackup"] = d.GameWatchBackup,
        };
    }

    private static void ApplyTools(JsonObject o)
    {
        var d = BuildData.Current;
        // profiles: the other PC's ones are added, a profile with the same name here stays as it is
        if (o["Profiles"]?.Deserialize<Dictionary<string, List<OwnProfile>>>() is { } pr)
            foreach (var (id, list) in pr)
            {
                var mine = Profiles.Own(id);
                foreach (var p in list.Where(p => !mine.Any(x => x.Name.Equals(p.Name, StringComparison.OrdinalIgnoreCase)))) mine.Add(p);
            }
        if (o["ActiveProfile"]?.Deserialize<Dictionary<string, string>>() is { } ap)
            foreach (var (id, p) in ap) d.ActiveProfile.TryAdd(id, p);
        if (o["Preset"]?.Deserialize<Dictionary<string, string>>() is { } ps)
            foreach (var (id, p) in ps) d.Preset.TryAdd(id, p);
        if (o["DiskCheck"]?.GetValue<bool>() is { } dc) d.DiskCheck = dc;
        if (o["DiskMinGb"]?.GetValue<int>() is { } dm) d.DiskMinGb = dm;
        if (o["GameWatch"]?.GetValue<bool>() is { } gw) d.GameWatch = gw;
        if (o["GameWatchBackup"]?.GetValue<bool>() is { } gb) d.GameWatchBackup = gb;
        BuildData.Save();
    }

    public static async Task<string> ExportAsync(string folder, Action<string> progress)
    {
        var file = Path.Combine(folder, Ui.T("Funky Launcher — переезд ", "Funky Launcher — move ") + $"{DateTime.Now:yyyy-MM-dd}.zip");
        var mods = BuildTools.Modlists();
        await Task.Run(() =>
        {
            using var fs = new FileStream(file + ".part", FileMode.Create);
            using var z = new ZipArchive(fs, ZipArchiveMode.Create);
            var manifest = new JsonObject
            {
                ["kind"] = "funky-move", ["date"] = DateTime.Now.ToString("s"), ["pc"] = Environment.MachineName, ["launcher"] = AppConfig.Version,
                ["modlists"] = new JsonArray(mods.Select(m => (JsonNode)new JsonObject { ["id"] = m.Id, ["title"] = m.Title, ["game"] = m.Game, ["version"] = m.Version }).ToArray()),
                ["settings"] = PortableSettings(),
                ["tools"] = PortableTools(),
            };
            using (var w = new StreamWriter(z.CreateEntry("manifest.json").Open())) w.Write(manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            foreach (var m in mods.Where(m => m.SaveFolder != null && Directory.Exists(m.SaveFolder)))
            {
                progress(Ui.T("Сохранения: ", "Saves: ") + m.Title);
                foreach (var f in Directory.EnumerateFiles(m.SaveFolder!, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        using var src = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        var e = z.CreateEntry($"saves/{m.Id}/" + Path.GetRelativePath(m.SaveFolder!, f).Replace('\\', '/'), CompressionLevel.Fastest);
                        e.LastWriteTime = File.GetLastWriteTime(f);
                        using var dst = e.Open();
                        src.CopyTo(dst);
                    }
                    catch (IOException) { }
                }
            }
        });
        File.Move(file + ".part", file, overwrite: true);
        BuildTools.Log("move export " + file);
        return file;
    }

    /// <summary>Reads the file: what will be applied.</summary>
    public static (JsonObject Manifest, Dictionary<string, int> Saves) Peek(string file)
    {
        using var z = ZipFile.OpenRead(file);
        var man = z.GetEntry("manifest.json") ?? throw new InvalidOperationException(Ui.T("Это не файл переезда Funky Launcher.", "Not a Funky Launcher move file."));
        using var r = new StreamReader(man.Open());
        var o = JsonNode.Parse(r.ReadToEnd())!.AsObject();
        if (o["kind"]?.GetValue<string>() != "funky-move") throw new InvalidOperationException(Ui.T("Это не файл переезда Funky Launcher.", "Not a Funky Launcher move file."));
        var saves = z.Entries.Where(e => e.FullName.StartsWith("saves/") && e.Length > 0)
            .GroupBy(e => e.FullName.Split('/')[1]).ToDictionary(g => g.Key, g => g.Count());
        return (o, saves);
    }

    /// <summary>Applies settings and puts saves into this PC's save folders; never overwrites a save that is already here.
    /// An older move file works too (parts it does not know are skipped).</summary>
    public static async Task<string> ImportAsync(string file)
    {
        var (man, _) = Peek(file);
        if (man["settings"] is JsonObject so) ApplySettings(so);
        if (man["tools"] is JsonObject to) ApplyTools(to);
        int added = 0, kept = 0;
        await Task.Run(() =>
        {
            using var z = ZipFile.OpenRead(file);
            foreach (var g in z.Entries.Where(e => e.FullName.StartsWith("saves/") && e.Length > 0).GroupBy(e => e.FullName.Split('/')[1]))
            {
                var folder = Catalog.Find(g.Key)?.ResolveSaveFolder();
                if (folder == null) continue;
                var root = Path.GetFullPath(folder).TrimEnd('\\') + "\\";
                foreach (var e in g)
                {
                    var to = Path.GetFullPath(Path.Combine(folder, string.Join('\\', e.FullName.Split('/').Skip(2))));
                    if (!to.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue; // "..\" in a file name never leaves the save folder
                    if (File.Exists(to)) { kept++; continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                    e.ExtractToFile(to);
                    File.SetLastWriteTime(to, e.LastWriteTime.LocalDateTime);
                    added++;
                }
            }
        });
        BuildTools.Log($"move import: {added} added, {kept} kept");
        BuildTools.RefreshUi();
        return Ui.T($"Готово: файлов сохранений добавлено {added}, уже были на месте {kept}. Настройки перенесены.", $"Done: {added} save files added, {kept} were already here. Settings applied.");
    }

    private static void Fill(StackPanel root)
    {
        root.Children.Clear();
        root.Children.Add(Ui.Title(Ui.T("Переезд на новый ПК", "Moving to a new PC")));
        root.Children.Add(Ui.Text(Ui.T("1) Здесь: «Собрать файл переезда» — сохранения всех установленных сборок, настройки лаунчера, твои профили и пресеты, список сборок. 2) На новом ПК: поставь лаунчер, «Загрузить файл переезда», потом установи сборки из списка.",
            "1) Here: \"Make a move file\" — saves of all installed modlists, launcher settings, your profiles and presets, the modlist list. 2) On the new PC: install the launcher, \"Load a move file\", then install the modlists from the list."), bottom: 6));
        var status = Ui.Text("", 12.5, Ui.Cyan, top: 6);
        root.Children.Add(Ui.Row(
            Ui.Button(Ui.T("Собрать файл переезда…", "Make a move file…"), async () =>
            {
                var dlg = new OpenFolderDialog { Title = Ui.T("Куда положить файл (флешка, облако)", "Where to put the file (USB stick, cloud)") };
                if (dlg.ShowDialog() != true) return;
                try { var f = await ExportAsync(dlg.FolderName, t => Application.Current.Dispatcher.BeginInvoke(() => status.Text = t)); status.Text = Ui.T("Готово: ", "Done: ") + f; }
                catch (Exception ex) { status.Text = Ui.T("Не получилось: ", "Failed: ") + ex.Message; }
            }, primary: true),
            Ui.Button(Ui.T("Загрузить файл переезда…", "Load a move file…"), async () =>
            {
                var dlg = new OpenFileDialog { Filter = "Funky Launcher (*.zip)|*.zip" };
                if (dlg.ShowDialog() != true) return;
                try
                {
                    var (man, saves) = Peek(dlg.FileName);
                    var mods = (man["modlists"] as JsonArray)?.Select(x => $"• {x?["title"]} {x?["version"]} ({x?["game"]})" + (saves.TryGetValue(x?["id"]?.GetValue<string>() ?? "", out var n) ? Ui.T($" — файлов сохранений: {n}", $" — save files: {n}") : "")) ?? Array.Empty<string>();
                    if (!Ui.Ask(Ui.T($"Файл с ПК «{man["pc"]}» от {man["date"]}.\n\nСборки:\n{string.Join("\n", mods)}\n\nПеренести настройки и сохранения? Сохранения, которые уже есть на этом ПК, не заменяются.",
                                     $"File from PC \"{man["pc"]}\", {man["date"]}.\n\nModlists:\n{string.Join("\n", mods)}\n\nApply settings and saves? Saves already on this PC are not replaced."))) return;
                    status.Text = Ui.T("Переношу…", "Applying…");
                    status.Text = await ImportAsync(dlg.FileName) + Ui.T(" Установи сборки из списка в Библиотеке.", " Install the modlists from the list in Library.");
                }
                catch (Exception ex) { status.Text = Ui.T("Не получилось: ", "Failed: ") + ex.Message; }
            })));
        root.Children.Add(status);
    }
}
