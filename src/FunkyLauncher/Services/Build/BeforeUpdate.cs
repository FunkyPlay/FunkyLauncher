// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FunkyLauncher.Core;

namespace FunkyLauncher.Services.Build;

/// <summary>
/// №39 (free since 27.11): as soon as a new modlist version appears, a save backup "before the update
/// to X" is made (so it is there before the player presses Update); after the update the saves go back to it in one
/// click. It is a manual backup, so rotation never removes it. The modlist files themselves are not rolled back.
/// </summary>
public static class BeforeUpdate
{
    private static bool _busy;

    public static void Start()
    {
        Settings.Installed += (s, ml, old, now) =>
        {
            if (old == null || old == now || Pinned(ml) is null) return;
            BuildTools.Notify(Ui.T("Сборка обновлена", "Modlist updated"),
                Ui.T($"{ml.Title} {now}. Бэкап сохранений перед обновлением сохранён — если что-то не так, верни его: Инструменты → «Сборка» → «Бэкап перед обновлением».",
                     $"{ml.Title} {now}. A save backup from before the update is kept — if anything is off, put it back: Tools → \"Modlist\" → \"Backup before update\"."));
        };
        var t = new DispatcherTimer { Interval = TimeSpan.FromMinutes(30) };
        t.Tick += async (_, _) => await CheckAsync();
        t.Start();
        _ = Task.Delay(TimeSpan.FromSeconds(20)).ContinueWith(_ => Application.Current?.Dispatcher.BeginInvoke(async () => await CheckAsync()));
        BuildTools.Add("beforeupdate", "⤒", () => Ui.T("Бэкап перед обновлением", "Backup before update"),
            () => Ui.T("Вышла новая версия сборки — сохранения сами копируются в бэкап. После обновления — вернуть одной кнопкой.", "A new modlist version is out — your saves are backed up by themselves. After the update — put them back in one click."),
            () => { var r = new StackPanel(); Fill(r); return r; });
    }

    /// <summary>A new version in the catalog and no backup for it yet → make one (manual: never pruned).</summary>
    public static async Task CheckAsync()
    {
        if (_busy) return;
        // --shots (pictures for the design team) only draws the pages: no backups of the player's saves (21.11)
        if (Environment.GetCommandLineArgs().Any(a => a.Equals("--shots", StringComparison.OrdinalIgnoreCase))) return;
        _busy = true;
        try
        {
            var s = BuildTools.Settings;
            var d = BuildData.Current;
            foreach (var ml in Catalog.Items.Where(x => x.Installer != "gta" && s.IsInstalled(x) && BackupService.Supported(x)))
            {
                var installed = s.InstalledVersion(ml);
                if (ml.Version.Length == 0 || installed == null || installed == ml.Version) continue;
                if (d.BeforeUpdate.TryGetValue(ml.Id, out var done) && done.Version == ml.Version) continue;
                if (BackupService.GameRunning(ml)) continue; // after the game
                var b = await BackupService.CreateAsync(s, ml, BackupKind.Manual, CancellationToken.None);
                d.BeforeUpdate[ml.Id] = new BeforeUpdateMark { Version = ml.Version, Backup = b == null ? "" : Path.GetFileName(b.Dir) };
                BuildData.Save();
                BuildTools.Log($"before update {ml.Id}: {installed} → {ml.Version}, backup {(b == null ? "none (no saves)" : Path.GetFileName(b.Dir))}");
            }
        }
        catch (Exception ex) { BuildTools.Log("before update: " + ex.Message); }
        finally { _busy = false; }
    }

    /// <summary>The "before the update" backup of the modlist (null = none, or it was deleted by hand).</summary>
    public static BackupInfo? Pinned(Modlist ml)
    {
        if (!BuildData.Current.BeforeUpdate.TryGetValue(ml.Id, out var mark) || mark.Backup.Length == 0) return null;
        try { return BackupService.List(BuildTools.Settings, ml).FirstOrDefault(b => Path.GetFileName(b.Dir).Equals(mark.Backup, StringComparison.OrdinalIgnoreCase)); }
        catch { return null; }
    }

    private static void Fill(StackPanel root)
    {
        root.Children.Clear();
        root.Children.Add(Ui.Text(Ui.T("Лаунчер каждые полчаса смотрит, не вышла ли новая версия сборки. Вышла — сразу копирует сохранения в бэкап «вручную» (такие сами не удаляются). Если после обновления сохранения не грузятся или что-то не так — верни их здесь. Файлы самой сборки не откатываются.",
            "Every half an hour the launcher looks for a new modlist version. When there is one, it copies your saves into a manual backup (those are never removed by themselves). If the saves don't load after the update — put them back here. The modlist files themselves are not rolled back."), bottom: 8));
        var status = Ui.Text("", 12.5, Ui.Cyan, top: 6);
        var s = BuildTools.Settings;
        var list = Catalog.Items.Where(x => x.Installer != "gta" && s.IsInstalled(x) && BackupService.Supported(x)).ToList();
        if (list.Count == 0) root.Children.Add(Ui.Text(Ui.T("Нет установленных сборок.", "No installed modlists.")));
        foreach (var ml in list)
        {
            var installed = s.InstalledVersion(ml);
            var b = Pinned(ml);
            var mark = BuildData.Current.BeforeUpdate.TryGetValue(ml.Id, out var mk) ? mk : null;
            var line = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            line.Children.Add(new TextBlock { Text = ml.Title + (installed != null ? $" {installed}" : ""), FontSize = 14, FontWeight = FontWeights.SemiBold });
            var newer = ml.Version.Length > 0 && installed != null && ml.Version != installed;
            line.Children.Add(Ui.Text(newer ? Ui.T($"Есть новая версия {ml.Version}.", $"Version {ml.Version} is out.") : Ui.T("Стоит последняя версия.", "The latest version is installed."), 12.5, newer ? Ui.Pink : Ui.Muted));
            line.Children.Add(Ui.Text(b == null ? Ui.T("Бэкапа перед обновлением пока нет.", "No backup before an update yet.")
                : Ui.T($"📌 Бэкап перед обновлением до {mark!.Version}: {b.Date:dd.MM.yyyy HH:mm}, сохранений: {b.Saves}", $"📌 Backup before the update to {mark!.Version}: {b.Date:dd.MM.yyyy HH:mm}, saves: {b.Saves}"), 13, Ui.Cyan, top: 2));
            if (b != null)
                line.Children.Add(Ui.Row(Ui.Button(Ui.T("Вернуть сохранения как до обновления", "Put the saves back as before the update"), async () =>
                {
                    if (BackupService.GameRunning(ml)) { status.Text = Ui.T("Сначала закрой игру.", "Close the game first."); return; }
                    if (!Ui.Ask(Ui.T($"Вернуть сохранения {ml.Title} от {b.Date:dd.MM HH:mm}? Текущие сначала скопируются в бэкап.", $"Put back the {ml.Title} saves from {b.Date:dd.MM HH:mm}? The current ones are backed up first."))) return;
                    try { await BackupService.RestoreAsync(s, ml, b, CancellationToken.None); status.Text = Ui.T("Сохранения возвращены.", "Saves put back."); }
                    catch (Exception ex) { status.Text = ex.Message; }
                }, height: 30)));
            root.Children.Add(line);
        }
        root.Children.Add(status);
    }
}

public sealed class BeforeUpdateMark
{
    public string Version { get; set; } = "";
    /// <summary>The backup folder name ("" = there were no saves).</summary>
    public string Backup { get; set; } = "";
}
