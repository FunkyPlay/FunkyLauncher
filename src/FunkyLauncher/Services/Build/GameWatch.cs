// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FunkyLauncher.Core;

namespace FunkyLauncher.Services.Build;

/// <summary>
/// №71 (free since 27.11; Legal 19.11): the warning "the game version changed" at PLAY was always free.
/// This checks ahead — at start and every 6 hours — makes a save backup when it notices a change, and explains
/// how the player can turn off automatic updates in the game client's own settings. It never blocks updates, never edits
/// client files, never downloads or hands out older game versions and promises nothing about the modlist working.
/// </summary>
public static class GameWatch
{
    public static void Start()
    {
        BuildTools.Add("gamewatch", "⟳", () => Ui.T("Версия игры", "Game version"),
            () => Ui.T("Лаунчер замечает смену версии игры, делает бэкап сохранений и подскажет, как отключить автообновление.", "The launcher spots a game version change, backs up your saves and shows how to turn off auto-update."),
            () => { var r = new StackPanel(); Fill(r); return r; });
        var t = new DispatcherTimer { Interval = TimeSpan.FromHours(6) };
        t.Tick += async (_, _) => await CheckAllAsync(notify: true);
        t.Start();
        if (Environment.GetCommandLineArgs().Any(a => a.Equals("--shots", StringComparison.OrdinalIgnoreCase))) return; // pictures only
        _ = Task.Delay(TimeSpan.FromSeconds(90)).ContinueWith(_ => Application.Current?.Dispatcher.BeginInvoke(async () => await CheckAllAsync(notify: true)));
    }

    public sealed record Result(ModlistInfo Modlist, string Need, string? Have);

    public static async Task<List<Result>> CheckAllAsync(bool notify)
    {
        var list = new List<Result>();
        var d = BuildData.Current;
        if (!d.GameWatch && notify) return list;
        foreach (var m in BuildTools.Modlists())
        {
            if (Catalog.Find(m.Id) is not { } ml || ml.GameVersion.Length == 0) continue;
            GameInfo? game = null;
            try { game = await SystemCheck.FindGameAsync(ml, CancellationToken.None); } catch { }
            var have = game?.Version;
            list.Add(new Result(m, ml.GameVersion, have));
            if (!notify || have == null || LaunchWatcher.SameGameVersion(have, ml.GameVersion)) continue;
            if (d.GameSeen.TryGetValue(m.Id, out var seen) && seen == have) continue; // told about this version already
            d.GameSeen[m.Id] = have; BuildData.Save();
            if (d.GameWatchBackup)
                try { if (BackupService.Supported(ml)) await BackupService.CreateAsync(BuildTools.Settings, ml, BackupKind.Manual, CancellationToken.None); }
                catch (Exception ex) { BuildTools.Log("game watch backup: " + ex.Message); }
            BuildTools.Notify(Ui.T("Версия игры изменилась", "The game version changed"),
                Ui.T($"{m.Title} рассчитана на версию {ml.GameVersion}, сейчас стоит {have}. Подскажем, что сделать: Инструменты → «Сборка» → «Версия игры».",
                     $"{m.Title} is made for version {ml.GameVersion}, now {have} is installed. Here's what to do: Tools → \"Modlist\" → \"Game version\"."));
            BuildTools.Log($"game version {m.Id}: {have} (needs {ml.GameVersion})");
        }
        return list;
    }

    private static async void Fill(StackPanel root)
    {
        root.Children.Clear();
        var d = BuildData.Current;
        var watch = new CheckBox { Content = Ui.T("Проверять версию игры заранее (при запуске лаунчера и каждые 6 часов)", "Check the game version ahead (at launcher start and every 6 hours)"), IsChecked = d.GameWatch };
        watch.Click += (_, _) => { d.GameWatch = watch.IsChecked == true; BuildData.Save(); };
        root.Children.Add(watch);
        var backup = new CheckBox { Content = Ui.T("Когда версия изменилась — сразу сделать бэкап сохранений", "When the version changed — back up the saves at once"), IsChecked = d.GameWatchBackup, Margin = new Thickness(0, 4, 0, 0) };
        backup.Click += (_, _) => { d.GameWatchBackup = backup.IsChecked == true; BuildData.Save(); };
        root.Children.Add(backup);

        root.Children.Add(new Border { Height = 12 });
        root.Children.Add(Ui.Title(Ui.T("Сейчас", "Now")));
        var now = new StackPanel();
        root.Children.Add(now);
        now.Children.Add(Ui.Text(Ui.T("Проверяю…", "Checking…"), 12.5, Ui.Cyan));

        root.Children.Add(new Border { Height = 12 });
        root.Children.Add(Ui.Title(Ui.T("Как не получить обновление игры случайно", "How not to get a game update by accident")));
        root.Children.Add(Ui.Text(Ui.T(
            "Сборка рассчитана на конкретную версию игры. Новая версия может сломать моды, пока их авторы не обновят их. Чтобы обновление не пришло само, в настройках клиента игры, откуда ты её запускаешь: открой свойства игры → «Обновления» → выбери «обновлять только при запуске» или «не обновлять автоматически» (название пункта зависит от клиента). Лаунчер сам ничего в клиенте не меняет и обновления не блокирует.\n\nЕсли игра уже обновилась: не загружай сохранения из сборки, дождись новой версии сборки в Библиотеке — или верни прежнюю версию игры средствами самого клиента, если он это позволяет. Бэкап сохранений уже сделан.",
            "The modlist is made for a specific game version. A new version may break mods until their authors update them. To keep the update from coming by itself, in the settings of the game client you start it from: open the game's properties → \"Updates\" → pick \"only update when I launch\" or \"don't update automatically\" (the item's name depends on the client). The launcher changes nothing in the client and blocks no updates.\n\nIf the game has already updated: don't load your modlist saves, wait for the new modlist version in Library — or return the previous game version with the client's own tools if it allows that. Your saves are already backed up."), 13));
        root.Children.Add(Ui.Text(Ui.T("Funky Launcher — неофициальная программа; сборка рассчитана на конкретную версию игры, и работа с другими версиями не обещается.",
            "Funky Launcher is unofficial software; a modlist is made for a specific game version, and other versions are not promised to work."), 12, top: 8));

        try
        {
            var res = await CheckAllAsync(notify: false);
            now.Children.Clear();
            if (res.Count == 0) now.Children.Add(Ui.Text(Ui.T("Нет установленных сборок.", "No installed modlists.")));
            foreach (var r in res)
            {
                var ok = r.Have != null && LaunchWatcher.SameGameVersion(r.Have, r.Need);
                now.Children.Add(Ui.Text($"{(ok ? "✓" : r.Have == null ? "?" : "⚠")} {r.Modlist.Title}: " +
                    (r.Have == null ? Ui.T("игра не найдена", "game not found") : Ui.T($"стоит {r.Have}, сборка рассчитана на {r.Need}", $"installed {r.Have}, the modlist is made for {r.Need}")),
                    13, ok ? Ui.Cyan : Ui.Pink, top: 2));
            }
        }
        catch (Exception ex) { now.Children.Clear(); now.Children.Add(Ui.Text(ex.Message)); }
    }
}
