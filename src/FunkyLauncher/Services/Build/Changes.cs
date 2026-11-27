// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FunkyLauncher.Core;

namespace FunkyLauncher.Services.Build;

/// <summary>
/// №15 (free since 27.11) "what changed in my modlist" after an update: added, removed and updated mods (version from each mod's meta.ini),
/// counted by comparing the previous install folder (kept for rollback) with the new one.
/// </summary>
public static class Changes
{
    public static void Start()
    {
        Settings.Installed += (s, ml, old, now) =>
        {
            if (ml.Installer == "gta") return;
            try { if (Compare(ModlistInfo.Of(s, ml), old, now) is { } e) { Journal(ml.Id).Add(e); BuildData.Save(); } }
            catch (Exception ex) { BuildTools.Log("changes: " + ex.Message); }
        };
        BuildTools.Add("changes", "≡", () => Ui.T("Что изменилось в сборке", "What changed in the modlist"),
            () => Ui.T("После обновления — подробное сравнение: что добавлено, убрано, обновлено.", "After an update: a detailed comparison of what was added, removed or updated."),
            () => { var r = new StackPanel(); Fill(r); return r; });
    }

    public static List<ChangeEntry> Journal(string id) =>
        BuildData.Current.Changes.TryGetValue(id, out var l) ? l : BuildData.Current.Changes[id] = new();

    /// <summary>Mods switched on in the profile → their version (meta.ini), "" when unknown.</summary>
    public static Dictionary<string, string> Mods(string installDir, string profile) =>
        Read(Path.Combine(installDir, "profiles", profile, "modlist.txt"), Path.Combine(installDir, "mods"));

    /// <summary>The mods switched on in a modlist.txt; each version from the first mods folder that has the mod.</summary>
    private static Dictionary<string, string> Read(string list, params string[] modsDirs)
    {
        var res = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(list)) return res;
        foreach (var l in File.ReadLines(list))
        {
            if (l.Length < 2 || l[0] != '+' || l.EndsWith("_separator")) continue;
            var name = l[1..];
            var ver = "";
            var meta = modsDirs.Select(d => Path.Combine(d, name, "meta.ini")).FirstOrDefault(File.Exists);
            if (meta != null)
                try { ver = File.ReadLines(meta).FirstOrDefault(x => x.StartsWith("version=", StringComparison.OrdinalIgnoreCase))?[8..].Trim() ?? ""; } catch { }
            res[name] = ver;
        }
        return res;
    }

    /// <summary>
    /// The previous version vs the current one; null when there is nothing to compare. A separate old folder (older
    /// installs, Wabbajack) or — since 0.1.19 — the in-place update's store &lt;install&gt;\_rollback: the old profile files
    /// and the replaced mod folders are kept there (unchanged mods are the same folders as now).
    /// </summary>
    public static ChangeEntry? Compare(ModlistInfo m, string? oldVersion, string? newVersion)
    {
        if (Catalog.Find(m.Id) is not { } ml || BuildTools.Settings.PreviousOf(ml) is not { } prev || !Directory.Exists(prev.Dir)) return null;
        var a = prev.InPlace
            ? Read(Path.Combine(prev.Dir, "items", "profiles", m.Mo2Profile, "modlist.txt"), Path.Combine(prev.Dir, "items", "mods"), Path.Combine(m.InstallDir, "mods"))
            : Mods(prev.Dir, m.Mo2Profile);
        var b = Mods(m.InstallDir, m.Mo2Profile);
        if (a.Count == 0 || b.Count == 0) return null;
        static string Clean(string v) => v.Trim('d', 'D'); // MO2 writes "d1.2" for unknown-source versions
        return new ChangeEntry
        {
            Date = DateTime.Now,
            From = oldVersion ?? prev.Version,
            To = newVersion ?? m.Version ?? "",
            Added = b.Keys.Except(a.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList(),
            Removed = a.Keys.Except(b.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList(),
            Updated = b.Where(x => a.TryGetValue(x.Key, out var v) && v.Length > 0 && x.Value.Length > 0 && Clean(v) != Clean(x.Value))
                       .Select(x => $"{x.Key}: {Clean(a[x.Key])} → {Clean(x.Value)}").OrderBy(x => x).ToList(),
        };
    }

    private static ChangeEntry? Latest(ModlistInfo m)
    {
        var j = Journal(m.Id);
        if (j.Count == 0 && Compare(m, null, null) is { } e) { j.Add(e); BuildData.Save(); } // installed before this journal: count it now
        return j.LastOrDefault();
    }

    private static string Short(ChangeEntry e) =>
        $"{e.From} → {e.To}: " + Ui.T($"+{e.Added.Count} модов, −{e.Removed.Count}, обновлено {e.Updated.Count}", $"+{e.Added.Count} mods, −{e.Removed.Count}, updated {e.Updated.Count}");

    /// <summary>One line under PLAY in Library about the last update.</summary>
    public static FrameworkElement LibraryLine()
    {
        var m = BuildTools.Selected;
        if (m == null) return new Border();
        ChangeEntry? e;
        try { e = Latest(m); } catch { e = null; }
        if (e == null) return new Border();
        return new TextBlock { Text = "≡ " + Ui.T("Обновление ", "Update ") + Short(e) + Ui.T(" · список: Инструменты → «Сборка»", " · list: Tools → \"Modlist\""), FontSize = 12.5, Foreground = Ui.Muted, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap };
    }

    private static void Fill(StackPanel root)
    {
        root.Children.Clear();
        var m = BuildTools.Selected;
        if (m == null) { root.Children.Add(Ui.Text(Ui.T("Выбери установленную сборку в Библиотеке.", "Pick an installed modlist in Library."))); return; }
        root.Children.Add(Ui.Title(Ui.T("Журнал обновлений — ", "Update journal — ") + m.Title));
        Latest(m);
        var j = Journal(m.Id);
        if (j.Count == 0) { root.Children.Add(Ui.Text(Ui.T("Пока не с чем сравнить: журнал появится после первого обновления сборки.", "Nothing to compare yet: the journal appears after the first modlist update."))); return; }
        foreach (var e in Enumerable.Reverse(j))
        {
            var box = new StackPanel();
            box.Children.Add(new TextBlock { Text = Short(e), FontSize = 14, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            box.Children.Add(Ui.Text(e.Date.ToString("dd.MM.yyyy HH:mm"), 12, top: 2));
            void Part(string title, List<string> items, Brush color)
            {
                if (items.Count == 0) return;
                var more = new StackPanel { Visibility = Visibility.Collapsed };
                foreach (var x in items) more.Children.Add(Ui.Text("• " + x, 12.5, color));
                var head = Ui.Button($"{title} ({items.Count})", () => more.Visibility = more.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible, height: 28);
                box.Children.Add(Ui.Row(head));
                box.Children.Add(more);
            }
            Part(Ui.T("Добавлены", "Added"), e.Added, Ui.Cyan);
            Part(Ui.T("Обновлены", "Updated"), e.Updated, Ui.TextBrush);
            Part(Ui.T("Убраны", "Removed"), e.Removed, Ui.Pink);
            root.Children.Add(Ui.Card(box));
        }
    }
}

public sealed class ChangeEntry
{
    public DateTime Date { get; set; }
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public List<string> Added { get; set; } = new();
    public List<string> Removed { get; set; } = new();
    public List<string> Updated { get; set; } = new();
}
