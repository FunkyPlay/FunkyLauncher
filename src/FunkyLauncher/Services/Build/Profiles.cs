// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using FunkyLauncher.Core;

namespace FunkyLauncher.Services.Build;

/// <summary>
/// №9 (free since 27.11) own modlist profiles without opening Mod Organizer: "Full", "Light for a laptop", "Without 18+" or any.
/// A profile is kept as "the modlist's profile minus these mods" and its MO2 folder ("Funky - name") is built again from
/// the modlist's own profile every time — so it survives modlist updates and keeps our fixes. PLAY uses the picked one.
/// </summary>
public static class Profiles
{
    public const string Prefix = "Funky - ";

    public static void Start()
    {
        // the launcher puts its fixes into the modlist's profile right before PLAY: the picked profile gets them too
        BuildTools.GameStarting += m =>
        {
            if (BuildData.Current.ActiveProfile.TryGetValue(m.Id, out var a) && Own(m.Id).FirstOrDefault(p => p.Name == a) is { } p)
                try { Build(m, p); } catch (Exception ex) { BuildTools.Log("profile before play: " + ex.Message); }
        };
        Settings.Installed += (s, ml, _, _) =>
        {
            if (ml.Installer == "gta") return;
            var m = ModlistInfo.Of(s, ml);
            foreach (var p in Own(m.Id)) try { Build(m, p); } catch (Exception ex) { BuildTools.Log("profile rebuild: " + ex.Message); }
        };
        BuildTools.Add("profiles", "☰", () => Ui.T("Мои профили сборки", "My modlist profiles"),
            () => Ui.T("Сохраняй свои наборы включённых модов и переключайся между ними.", "Save your own sets of enabled mods and switch between them."),
            () => { var r = new StackPanel(); Fill(r); return r; });
    }

    /// <summary>The MO2 profile PLAY starts (null = the modlist's own), only when its folder is there.</summary>
    public static string? PlayProfile(Settings s, Modlist ml)
    {
        if (!BuildData.Current.ActiveProfile.TryGetValue(ml.Id, out var p) || p.Length == 0) return null;
        return Directory.Exists(Path.Combine(s.InstallDir(ml), "profiles", Prefix + p)) ? Prefix + p : null;
    }

    public static List<OwnProfile> Own(string id) =>
        BuildData.Current.Profiles.TryGetValue(id, out var l) ? l : BuildData.Current.Profiles[id] = new();

    // ---------- the modlist's mods, grouped by separators ----------

    public sealed record Group(string Name, List<string> Mods);

    private static string BaseDir(ModlistInfo m) => Path.Combine(m.InstallDir, "profiles", m.Mo2Profile);

    /// <summary>modlist.txt is highest priority first; in MO2 a separator stands above its mods, so in the file the
    /// mods of a separator are the lines ABOVE it (up to the previous separator).</summary>
    public static List<Group> Groups(ModlistInfo m)
    {
        var file = Path.Combine(BaseDir(m), "modlist.txt");
        var groups = new List<Group>();
        var pending = new List<string>();
        foreach (var raw in File.ReadAllLines(file))
        {
            if (raw.Length < 2 || raw[0] == '#') continue;
            if (raw[0] != '+') continue; // only mods the modlist itself has on
            var name = raw[1..];
            if (name.EndsWith("_separator")) { groups.Add(new Group(name[..^10], pending)); pending = new(); }
            else pending.Add(name);
        }
        if (pending.Count > 0) groups.Add(new Group(Ui.T("Без раздела", "No section"), pending));
        groups.Reverse(); // as MO2 shows them: top section first
        foreach (var g in groups) g.Mods.Reverse();
        return groups.Where(g => g.Mods.Count > 0).ToList();
    }

    /// <summary>Builds "profiles\Funky - name" from the modlist's own profile with the switched-off mods.</summary>
    public static void Build(ModlistInfo m, OwnProfile p)
    {
        var src = BaseDir(m);
        var dst = Path.Combine(m.InstallDir, "profiles", Prefix + p.Name);
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.GetFiles(src))
            if (!Path.GetFileName(f).Equals("modlist.txt", StringComparison.OrdinalIgnoreCase))
                File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), overwrite: true);
        var off = new HashSet<string>(p.Off, StringComparer.OrdinalIgnoreCase);
        var lines = File.ReadAllLines(Path.Combine(src, "modlist.txt"))
            .Select(l => l.Length > 1 && l[0] == '+' && off.Contains(l[1..]) ? "-" + l[1..] : l);
        File.WriteAllLines(Path.Combine(dst, "modlist.txt"), lines, new UTF8Encoding(false));
        BuildTools.Log($"profile {m.Id}/{p.Name}: {off.Count} mods off");
    }

    // ---------- ready-made sets ----------

    private static readonly Regex Heavy = new(@"свет|текстур|графи|погод|light|textur|graphic|weather|enb|reshade|\b4k\b|\bhd\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Light: sections about lighting, textures, graphics, weather are off.</summary>
    public static List<string> LightSet(ModlistInfo m) => Groups(m).Where(g => Heavy.IsMatch(g.Name)).SelectMany(g => g.Mods).ToList();

    /// <summary>The modlist's pack of a kind ("heavy", "hd", "adult") from the launcher; null on a launcher without it.</summary>
    private static AddonFile? Pack(ModlistInfo m, string kind)
    {
        try { return Addons.OfKind(m.Id, kind); } catch { return null; }
    }

    /// <summary>24.11 "Weak PC": the mods of the modlist's heavy pack (the same as Lightweight graphics, chosen by the game
    /// chats); a modlist without one — the old guess by section names.</summary>
    public static List<string> WeakPcSet(ModlistInfo m) =>
        Pack(m, "heavy") is { Mods.Count: > 0 } h ? h.Mods.Select(x => x.ModFolder).Where(x => x.Length > 0).ToList() : LightSet(m);

    /// <summary>24.11 "For streaming": no 18+ mods (the game chats will add mods with copyrighted music later).</summary>
    public static List<string> StreamSet(ModlistInfo m) => AdultSet(m);

    /// <summary>"Beauty" needs the heavy and HD packs installed and on: which of them are off now (for a hint).</summary>
    private static bool BeautyMissing(ModlistInfo m)
    {
        try
        {
            var s = BuildTools.Settings;
            return new[] { "heavy", "hd" }.Select(k => Pack(m, k)).Any(p => p != null && !AddonService.Prefs(s, p).On);
        }
        catch { return false; }
    }

    /// <summary>Without 18+: the mods of the modlist's 18+ add-on.</summary>
    public static List<string> AdultSet(ModlistInfo m) =>
        Pack(m, "adult") is { } a ? a.Mods.Select(x => x.ModFolder).Where(x => x.Length > 0).ToList() : new();

    // ---------- UI ----------

    /// <summary>The profile choice under PLAY in Library (only when the player has own profiles).</summary>
    public static FrameworkElement LibraryRow()
    {
        var m = BuildTools.Selected;
        if (m == null || Own(m.Id).Count == 0) return new Border();
        var row = Ui.Row(new TextBlock { Text = Ui.T("Профиль:", "Profile:"), VerticalAlignment = VerticalAlignment.Center, Foreground = Ui.Muted, Margin = new Thickness(0, 0, 8, 0) });
        var active = BuildData.Current.ActiveProfile.TryGetValue(m.Id, out var a) ? a : "";
        foreach (var name in new[] { "" }.Concat(Own(m.Id).Select(p => p.Name)))
            row.Children.Add(Ui.Button((active == name ? "✓ " : "") + (name.Length == 0 ? Ui.T("Сборка целиком", "Whole modlist") : name), () =>
            {
                BuildData.Current.ActiveProfile[m.Id] = name; BuildData.Save(); BuildTools.RefreshUi();
            }, primary: active == name, height: 30));
        return row;
    }

    private static void Fill(StackPanel root)
    {
        root.Children.Clear();
        var m = BuildTools.Selected;
        if (m == null) { root.Children.Add(Ui.Text(Ui.T("Выбери установленную сборку в Библиотеке.", "Pick an installed modlist in Library."))); return; }
        if (!File.Exists(Path.Combine(BaseDir(m), "modlist.txt"))) { root.Children.Add(Ui.Text(Ui.T("Профиль сборки не найден.", "The modlist profile isn't found."))); return; }
        var own = Own(m.Id);
        var active = BuildData.Current.ActiveProfile.TryGetValue(m.Id, out var a) ? a : "";
        var status = Ui.Text("", 12.5, Ui.Cyan, top: 4);

        root.Children.Add(Ui.Title(Ui.T("Профили — ", "Profiles — ") + m.Title));
        root.Children.Add(Ui.Text(Ui.T("Кнопка ИГРАТЬ запускает отмеченный профиль. Обновление сборки профили не ломает — они сами пересобираются. Выключая моды, проверь игру: некоторые моды нужны другим.",
            "PLAY starts the ticked profile. Modlist updates don't break profiles — they rebuild themselves. When switching mods off, check the game: some mods are needed by others."), bottom: 6));
        foreach (var name in new[] { "" }.Concat(own.Select(p => p.Name)))
        {
            var p = own.FirstOrDefault(x => x.Name == name);
            var box = new StackPanel();
            box.Children.Add(new TextBlock { Text = (active == name ? "● " : "○ ") + (name.Length == 0 ? Ui.T("Сборка целиком (как у Funky)", "Whole modlist (as Funky made it)") : name), FontSize = 14, FontWeight = FontWeights.SemiBold });
            if (p != null) box.Children.Add(Ui.Text(Ui.T($"выключено модов: {p.Off.Count}", $"mods off: {p.Off.Count}"), 12.5, top: 2));
            var row = Ui.Row(Ui.Button(active == name ? Ui.T("✓ Играть этим", "✓ Playing this") : Ui.T("Играть этим", "Play this"), () =>
            {
                BuildData.Current.ActiveProfile[m.Id] = name; BuildData.Save(); BuildTools.RefreshUi(); Fill(root);
            }, primary: active == name, height: 30));
            if (p != null)
            {
                var editor = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 0) };
                row.Children.Add(Ui.Button(Ui.T("Моды…", "Mods…"), () =>
                {
                    if (editor.Visibility == Visibility.Visible) { editor.Visibility = Visibility.Collapsed; return; }
                    Editor(editor, m, p); editor.Visibility = Visibility.Visible;
                }, height: 30));
                row.Children.Add(Ui.Button(Ui.T("Убрать профиль", "Remove profile"), () =>
                {
                    if (!Ui.Ask(Ui.T($"Убрать профиль «{p.Name}»? Сохранения и моды не трогаются.", $"Remove the profile \"{p.Name}\"? Saves and mods are not touched."), "Funky Launcher")) return;
                    own.Remove(p);
                    if (active == p.Name) BuildData.Current.ActiveProfile[m.Id] = "";
                    BuildData.Save();
                    var dir = Path.Combine(m.InstallDir, "profiles", Prefix + p.Name); // only our own "Funky - " folder
                    try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
                    BuildTools.RefreshUi(); Fill(root);
                }, height: 30));
                box.Children.Add(row);
                box.Children.Add(editor);
            }
            else box.Children.Add(row);
            root.Children.Add(Ui.Card(box));
        }

        root.Children.Add(new Border { Height = 10 });
        root.Children.Add(Ui.Title(Ui.T("Новый профиль", "New profile")));
        var nameBox = new TextBox { Width = 260, FontSize = 13, Margin = new Thickness(0, 0, 8, 0), VerticalContentAlignment = VerticalAlignment.Center, Height = 30 };
        void Create(string name, List<string> off)
        {
            name = Regex.Replace(name.Trim(), @"[\\/:*?""<>|]", "");
            if (name.Length == 0) { status.Text = Ui.T("Напиши название.", "Type a name."); return; }
            if (own.Any(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) { status.Text = Ui.T("Такой профиль уже есть.", "That profile exists."); return; }
            var p = new OwnProfile { Name = name, Off = off.Distinct().ToList() };
            try { Build(m, p); } catch (Exception ex) { status.Text = Ui.T("Не получилось: ", "Failed: ") + ex.Message; return; }
            own.Add(p); BuildData.Save(); BuildTools.RefreshUi(); Fill(root);
        }
        root.Children.Add(Ui.Row(nameBox, Ui.Button(Ui.T("Создать (все моды включены)", "Create (all mods on)"), () => Create(nameBox.Text, new()), height: 30)));
        // 24.11 ready-made sets: "Weak PC", "For streaming", "Beauty"
        var weak = WeakPcSet(m);
        var stream = StreamSet(m);
        var ready = Ui.Row();
        if (weak.Count > 0) ready.Children.Add(Ui.Button(Ui.T($"Слабый ПК (−{weak.Count})", $"Weak PC (−{weak.Count})"), () => Create(Ui.T("Слабый ПК", "Weak PC"), weak), height: 30));
        ready.Children.Add(Ui.Button(Ui.T("Для стрима", "For streaming"), () => Create(Ui.T("Для стрима", "For streaming"), stream), height: 30));
        ready.Children.Add(Ui.Button(Ui.T("Красота", "Beauty"), () =>
        {
            Create(Ui.T("Красота", "Beauty"), new());
            if (BeautyMissing(m)) status.Text = Ui.T("Для «Красоты» выбери «Полная графика» (и HD, если есть) на странице сборки — сейчас часть красивых модов не стоит.",
                "For \"Beauty\" pick \"Full graphics\" (and HD, if there is one) on the modlist page — some of the beautiful mods aren't installed now.");
        }, height: 30));
        root.Children.Add(Ui.Text(Ui.T("Или готовый: «Слабый ПК» — без тяжёлых модов, «Для стрима» — без 18+, «Красота» — всё включено.",
            "Or a ready one: \"Weak PC\" — no heavy mods, \"For streaming\" — no 18+, \"Beauty\" — everything on."), 12.5, top: 8));
        root.Children.Add(ready);
        root.Children.Add(status);
    }

    private static void Editor(StackPanel ed, ModlistInfo m, OwnProfile p)
    {
        ed.Children.Clear();
        var off = new HashSet<string>(p.Off, StringComparer.OrdinalIgnoreCase);
        var search = new TextBox { Width = 300, Height = 28, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 6), ToolTip = Ui.T("Поиск мода", "Find a mod") };
        var list = new StackPanel();
        var save = Ui.Button(Ui.T("Сохранить профиль", "Save profile"), () =>
        {
            p.Off = off.ToList(); BuildData.Save();
            try { Build(m, p); BuildTools.Notify("Funky Launcher", Ui.T($"Профиль «{p.Name}» сохранён", $"Profile \"{p.Name}\" saved")); } catch (Exception ex) { BuildTools.Notify("Funky Launcher", ex.Message); }
            BuildTools.RefreshUi();
        }, primary: true, height: 30);
        ed.Children.Add(Ui.Row(search, save));
        ed.Children.Add(list);
        var groups = Groups(m);
        void Draw()
        {
            list.Children.Clear();
            var q = search.Text.Trim();
            foreach (var g in groups)
            {
                var mods = q.Length == 0 ? g.Mods : g.Mods.Where(x => x.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
                if (mods.Count == 0) continue;
                var all = new CheckBox { Content = new TextBlock { Text = $"{g.Name}  ({g.Mods.Count(x => !off.Contains(x))}/{g.Mods.Count})", FontWeight = FontWeights.SemiBold, FontSize = 13.5 }, Margin = new Thickness(0, 10, 0, 2), IsChecked = g.Mods.All(x => !off.Contains(x)) ? true : g.Mods.All(off.Contains) ? false : null };
                all.Click += (_, _) => { var on = all.IsChecked == true; foreach (var x in g.Mods) { if (on) off.Remove(x); else off.Add(x); } Draw(); };
                list.Children.Add(all);
                var inner = new StackPanel { Margin = new Thickness(24, 0, 0, 0) };
                foreach (var mod in mods.Take(q.Length == 0 ? 400 : 100))
                {
                    var cb = new CheckBox { Content = mod, IsChecked = !off.Contains(mod), FontSize = 12.5, Margin = new Thickness(0, 1, 0, 1) };
                    cb.Click += (_, _) => { if (cb.IsChecked == true) off.Remove(mod); else off.Add(mod); };
                    inner.Children.Add(cb);
                }
                list.Children.Add(inner);
            }
        }
        search.TextChanged += (_, _) => Draw();
        Draw();
    }
}

public sealed class OwnProfile
{
    public string Name { get; set; } = "";
    /// <summary>Mod folder names switched off in this profile.</summary>
    public List<string> Off { get; set; } = new();
}
