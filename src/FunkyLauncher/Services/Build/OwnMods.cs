// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using FunkyLauncher.Core;
using Microsoft.Win32;

namespace FunkyLauncher.Services.Build;

/// <summary>
/// №10 (free since 27.11) "my mods on top of the modlist": the player's OWN files (an archive or a folder they already have) are kept in
/// the launcher's folder (ownmods) and put into the modlist as Mod Organizer mods "[Мой] name" at the top of the order (highest priority),
/// in the modlist's profile and in the player's own profiles. A modlist update makes a new install folder — the mods
/// are put back by themselves (after the install, and before every PLAY if something removed them).
/// Nothing is downloaded: only files the player picked. Simple mods only (no installer scripts).
/// </summary>
public static class OwnMods
{
    public const string Prefix = "[Мой] ";

    public sealed class Entry
    {
        public string Name { get; set; } = "";
        public bool On { get; set; } = true;
        public DateTime Added { get; set; }
        public string From { get; set; } = "";
    }

    public static void Start()
    {
        Settings.Installed += (s, ml, _, _) => { if (ml.Installer != "gta") SafeApply(ModlistInfo.Of(s, ml)); };
        BuildTools.GameStarting += SafeApply; // after the profiles are rebuilt (Profiles starts first)
        BuildTools.Add("ownmods", "✚", () => Ui.T("Мои моды поверх сборки", "My mods on top of the modlist"),
            () => Ui.T("Добавь свой мод со своего ПК: он ставится выше модов сборки и возвращается после её обновления.", "Add your own mod from your PC: it sits on top of the modlist and returns after its updates."),
            () => { var r = new StackPanel(); Fill(r); return r; });
    }

    private static string Store(string modlistId) => Path.Combine(BuildTools.DataDir, "ownmods", modlistId);

    public static List<Entry> List(string modlistId) =>
        BuildData.Current.OwnMods.TryGetValue(modlistId, out var l) ? l : BuildData.Current.OwnMods[modlistId] = new();

    private static void SafeApply(ModlistInfo m)
    {
        try { Apply(m); } catch (Exception ex) { BuildTools.Log("own mods: " + ex.Message); }
    }

    // ---------- adding ----------

    /// <summary>Unpacks an archive (or copies a folder) into the store; a single wrapper folder is dropped.</summary>
    public static async Task<Entry> AddAsync(ModlistInfo m, string source)
    {
        var baseName = Path.GetFileNameWithoutExtension(source.TrimEnd('\\'));
        foreach (var c in Path.GetInvalidFileNameChars()) baseName = baseName.Replace(c, '_');
        var name = baseName;
        for (var i = 2; List(m.Id).Any(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase)); i++) name = $"{baseName} ({i})";
        var dst = Path.Combine(Store(m.Id), name);
        var tmp = dst + ".part";
        if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
        Directory.CreateDirectory(tmp);
        if (Directory.Exists(source)) await Task.Run(() => CopyDir(source, tmp));
        else await Archives.ExtractAsync(source, tmp, CancellationToken.None);

        // "ModName\..." inside the archive: the mod is that folder
        var root = tmp;
        while (Directory.GetFiles(root).Length == 0 && Directory.GetDirectories(root) is { Length: 1 } one && !LooksLikeData(Path.GetFileName(one[0]))) root = one[0];
        if (File.Exists(Path.Combine(root, "fomod", "ModuleConfig.xml")))
        {
            Directory.Delete(tmp, true);
            throw new InvalidOperationException(Ui.T("В этом моде есть установщик с выбором вариантов (FOMOD) — его ставь через Mod Organizer, лаунчер такие не разбирает.",
                                                     "This mod has an installer with options (FOMOD) — install it with Mod Organizer, the launcher doesn't handle those."));
        }
        if (Directory.Exists(dst)) Directory.Delete(dst, true);
        if (root == tmp) Directory.Move(tmp, dst);
        else { Directory.Move(root, dst); Directory.Delete(tmp, true); }

        var e = new Entry { Name = name, Added = DateTime.Now, From = Path.GetFileName(source) };
        List(m.Id).Add(e);
        BuildData.Save();
        Apply(m);
        BuildTools.Log($"own mod {m.Id}: {name}");
        return e;
    }

    /// <summary>Folder names a mod keeps inside it (then it is not a wrapper).</summary>
    private static bool LooksLikeData(string n) =>
        n.Equals("archive", StringComparison.OrdinalIgnoreCase) || n.Equals("bin", StringComparison.OrdinalIgnoreCase) || n.Equals("r6", StringComparison.OrdinalIgnoreCase)
        || n.Equals("red4ext", StringComparison.OrdinalIgnoreCase) || n.Equals("Textures", StringComparison.OrdinalIgnoreCase) || n.Equals("Meshes", StringComparison.OrdinalIgnoreCase)
        || n.Equals("Interface", StringComparison.OrdinalIgnoreCase) || n.Equals("F4SE", StringComparison.OrdinalIgnoreCase) || n.Equals("SFSE", StringComparison.OrdinalIgnoreCase)
        || n.Equals("Data", StringComparison.OrdinalIgnoreCase) || n.Equals("Sound", StringComparison.OrdinalIgnoreCase) || n.Equals("Scripts", StringComparison.OrdinalIgnoreCase);

    private static void CopyDir(string from, string to)
    {
        foreach (var f in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var t = Path.Combine(to, Path.GetRelativePath(from, f));
            Directory.CreateDirectory(Path.GetDirectoryName(t)!);
            File.Copy(f, t, true);
        }
    }

    // ---------- putting into the modlist ----------

    private static IEnumerable<string> ProfileDirs(ModlistInfo m)
    {
        var profiles = Path.Combine(m.InstallDir, "profiles");
        yield return Path.Combine(profiles, m.Mo2Profile);
        foreach (var p in Profiles.Own(m.Id)) yield return Path.Combine(profiles, Profiles.Prefix + p.Name);
    }

    private static bool IsPlugin(string f) => Path.GetExtension(f).ToLowerInvariant() is ".esp" or ".esm" or ".esl";

    /// <summary>Copies missing mods into the install and puts "+[Мой] name" at the top of every profile's modlist.txt (switched-off ones as "-").</summary>
    public static void Apply(ModlistInfo m)
    {
        var list = List(m.Id);
        var mods = Path.Combine(m.InstallDir, "mods");
        if (!Directory.Exists(mods)) return;
        var wanted = new List<string>();
        foreach (var e in list)
        {
            var src = Path.Combine(Store(m.Id), e.Name);
            if (!Directory.Exists(src)) continue;
            var dst = Path.Combine(mods, Prefix + e.Name);
            if (!Directory.Exists(dst)) CopyDir(src, dst);
            wanted.Add(e.Name);
        }
        foreach (var prof in ProfileDirs(m).Where(Directory.Exists))
        {
            var file = Path.Combine(prof, "modlist.txt");
            if (!File.Exists(file)) continue;
            var lines = File.ReadAllLines(file).ToList();
            var old = string.Join("\n", lines);
            lines.RemoveAll(l => l.Length > 1 && l[1..].StartsWith(Prefix)); // ours, put back in order below
            var at = lines.Count > 0 && lines[0].StartsWith('#') ? 1 : 0;   // the top = highest priority
            foreach (var e in list.Where(x => wanted.Contains(x.Name)).Reverse())
                lines.Insert(at, (e.On ? "+" : "-") + Prefix + e.Name);
            if (string.Join("\n", lines) != old) File.WriteAllLines(file, lines, new UTF8Encoding(false));

            // plugins of our mods (games with .esp/.esm/.esl): switched on at the end of plugins.txt / loadorder.txt
            var plugins = list.Where(x => wanted.Contains(x.Name))
                .SelectMany(x => Directory.EnumerateFiles(Path.Combine(mods, Prefix + x.Name)).Where(IsPlugin).Select(f => (Name: Path.GetFileName(f), x.On)))
                .ToList();
            if (plugins.Count == 0) continue;
            var pf = Path.Combine(prof, "plugins.txt");
            if (File.Exists(pf))
            {
                var pl = File.ReadAllLines(pf).ToList();
                var before = string.Join("\n", pl);
                foreach (var (name, on) in plugins)
                {
                    pl.RemoveAll(l => l.TrimStart('*').Equals(name, StringComparison.OrdinalIgnoreCase));
                    pl.Add(on ? "*" + name : name);
                }
                if (string.Join("\n", pl) != before) File.WriteAllLines(pf, pl, new UTF8Encoding(false));
            }
            var lo = Path.Combine(prof, "loadorder.txt");
            if (File.Exists(lo))
            {
                var ll = File.ReadAllLines(lo).ToList();
                var added = false;
                foreach (var (name, _) in plugins)
                    if (!ll.Any(l => l.Equals(name, StringComparison.OrdinalIgnoreCase))) { ll.Add(name); added = true; }
                if (added) File.WriteAllLines(lo, ll, new UTF8Encoding(false));
            }
        }
    }

    public static void Remove(ModlistInfo m, Entry e)
    {
        List(m.Id).Remove(e);
        BuildData.Save();
        // only our own folders: "[Мой] name" in the install and the copy in the store
        foreach (var dir in new[] { Path.Combine(m.InstallDir, "mods", Prefix + e.Name), Path.Combine(Store(m.Id), e.Name) })
            if (Directory.Exists(dir) && Path.GetFileName(dir).Length > 0)
            {
                foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
                Directory.Delete(dir, true);
            }
        Apply(m); // takes the line out of every profile
        BuildTools.Log($"own mod removed {m.Id}: {e.Name}");
    }

    // ---------- panel ----------

    private static void Fill(StackPanel root)
    {
        root.Children.Clear();
        var m = BuildTools.Selected;
        if (m == null) { root.Children.Add(Ui.Text(Ui.T("Выбери установленную сборку в Библиотеке.", "Pick an installed modlist in Library."))); return; }
        var status = Ui.Text("", 12.5, Ui.Cyan, top: 6);
        root.Children.Add(Ui.Title(Ui.T("Мои моды — ", "My mods — ") + m.Title));
        root.Children.Add(Ui.Text(Ui.T("Свои файлы модов (архив или папка, которые уже есть у тебя) ставятся поверх сборки — выше всех модов, и возвращаются сами после её обновления. Лаунчер ничего не скачивает. Мод может конфликтовать со сборкой — если игра ведёт себя странно, выключи его здесь.",
            "Your own mod files (an archive or a folder you already have) go on top of the modlist — above all mods, and come back by themselves after an update. The launcher downloads nothing. A mod may conflict with the modlist — if the game acts up, switch it off here."), bottom: 2));
        // Legal 19.11
        root.Children.Add(Ui.Text(Ui.T("Ты ставишь свои файлы на свою ответственность. Сборка с твоими модами не поддерживается — при проблемах сначала убери их. Соблюдай условия авторов модов. Не используй свои моды в сетевых режимах игры.",
            "You install your own files at your own risk. A modlist with your mods is not supported — if something breaks, remove them first. Follow the mod authors' terms. Don't use your own mods in online game modes."), 12, Ui.Pink, bottom: 8));
        root.Children.Add(Ui.Row(
            Ui.Button(Ui.T("Добавить архив…", "Add an archive…"), async () =>
            {
                var dlg = new OpenFileDialog { Filter = Ui.T("Архивы модов", "Mod archives") + "|*.zip;*.7z;*.rar" };
                if (dlg.ShowDialog() != true) return;
                await Add(m, dlg.FileName, status, root);
            }, primary: true, height: 32),
            Ui.Button(Ui.T("Добавить папку…", "Add a folder…"), async () =>
            {
                var dlg = new OpenFolderDialog();
                if (dlg.ShowDialog() != true) return;
                await Add(m, dlg.FolderName, status, root);
            }, height: 32)));
        root.Children.Add(status);
        var list = List(m.Id);
        if (list.Count == 0) { root.Children.Add(Ui.Text(Ui.T("Своих модов пока нет.", "No own mods yet."), top: 8)); return; }
        root.Children.Add(new Border { Height = 10 });
        foreach (var e in list)
        {
            var box = new DockPanel();
            var del = Ui.Button(Ui.T("Убрать", "Remove"), () =>
            {
                if (Ui.GameRunning(m)) { status.Text = Ui.T("Сначала закрой игру.", "Close the game first."); return; }
                if (!Ui.Ask(Ui.T($"Убрать мод «{e.Name}» из сборки? Его копия в лаунчере тоже удалится.", $"Remove the mod \"{e.Name}\" from the modlist? Its copy in the launcher is deleted too."), "Funky Launcher")) return;
                try { Remove(m, e); Fill(root); } catch (Exception ex) { status.Text = ex.Message; }
            }, height: 28);
            DockPanel.SetDock(del, Dock.Right);
            box.Children.Add(del);
            var on = new CheckBox { IsChecked = e.On, VerticalAlignment = VerticalAlignment.Center, Content = new TextBlock { Text = $"{e.Name}   ·   {e.From}   ·   {e.Added:dd.MM.yyyy}", FontSize = 13.5 } };
            on.Click += (_, _) => { e.On = on.IsChecked == true; BuildData.Save(); try { Apply(m); } catch (Exception ex) { status.Text = ex.Message; } };
            box.Children.Add(on);
            root.Children.Add(Ui.Card(box));
        }
    }

    private static async Task Add(ModlistInfo m, string source, TextBlock status, StackPanel root)
    {
        if (Ui.GameRunning(m)) { status.Text = Ui.T("Сначала закрой игру.", "Close the game first."); return; }
        status.Text = Ui.T("Добавляю…", "Adding…");
        try { var e = await AddAsync(m, source); Fill(root); BuildTools.Notify("Funky Launcher", Ui.T("Мод добавлен: ", "Mod added: ") + e.Name); }
        catch (Exception ex) { status.Text = Ui.T("Не получилось: ", "Failed: ") + ex.Message; }
    }
}
