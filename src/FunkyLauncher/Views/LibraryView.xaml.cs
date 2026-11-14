// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FunkyLauncher.Core;
using FunkyLauncher.Services;

namespace FunkyLauncher.Views;

/// <summary>Library: all FunkyPlay modlists on top, the selected one below with PLAY.</summary>
public partial class LibraryView : UserControl
{
    private readonly MainWindow _main;

    public LibraryView(MainWindow main)
    {
        InitializeComponent();
        _main = main;
        Loc.Changed += Refresh;
        Refresh();
    }

    public void Refresh()
    {
        PageTitle.Text = Loc.T("lib.title");
        Subtitle.Text = Loc.T("lib.subtitle");
        Cards.Children.Clear();
        foreach (var m in Catalog.Items) Cards.Children.Add(Card(m));
        RefreshSelected();
    }

    private void RefreshSelected()
    {
        var s = _main.Settings;
        var m = _main.Selected;
        var installed = m.IsReleased && s.IsInstalled(m);
        var img = Images.Load(m.Banner);
        Banner.Background = img != null
            ? new ImageBrush(img) { Stretch = Stretch.UniformToFill, AlignmentY = AlignmentY.Center }
            : new LinearGradientBrush(Color.FromRgb(0x2A, 0x14, 0x26), Color.FromRgb(0x0F, 0x2A, 0x33), 45);
        TitleText.Text = m.Title;
        TagText.Text = m.Tag.ToString();
        Description.Text = m.Description.ToString();
        var details = m.Details?.ToString() ?? "";
        MoreLink.Text = Loc.T("lib.more");
        MoreLink.Visibility = details.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        LessLink.Text = Loc.T("lib.less");
        DetailsTitle.Text = m.Title;
        DetailsText.Text = details;
        DetailsPanel.Visibility = Visibility.Collapsed;
        var req = m.Requirements?.ToString() ?? "";
        ReqPanel.Visibility = req.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        ReqTitle.Text = Loc.T("lib.requirements");
        ReqText.Text = req;
        Sub.Text = !m.IsReleased ? Loc.F("home.sub.soon", m.Author)
            : installed ? Loc.F("home.sub.installed", m.Author, m.Version, s.InstallDir(m))
            : Loc.F("home.sub.notinstalled", m.Author, m.Version);
        MainButton.Content = installed ? Loc.T("home.play") : !m.IsReleased ? Loc.T("lib.soon") : Loc.T("home.install");
        MainButton.IsEnabled = m.IsReleased;
        FolderButton.Content = Loc.T("home.folder");
        Mo2Button.Content = Loc.T("home.mo2");
        FolderButton.IsEnabled = Mo2Button.IsEnabled = installed;
        FolderButton.Visibility = Mo2Button.Visibility = installed ? Visibility.Visible : Visibility.Collapsed;
        StatusTitle.Text = installed ? Loc.T("home.ready") : Loc.T("home.notready");
        var update = s.NeedsUpdate(m);
        SetupButton.Content = update ? Loc.F("lib.update", m.Version) : Loc.T("lib.setup");
        SetupButton.IsEnabled = m.IsReleased;
        if (installed) Sub.Text = Loc.F("home.sub.installed", m.Author, s.InstalledVersion(m) ?? m.Version, s.InstallDir(m));

        CleanGameButton.Content = Loc.T("loaders.clean");
        CleanGameButton.Visibility = GameLoaders.Placed(m) ? Visibility.Visible : Visibility.Collapsed;

        var prev = s.PreviousOf(m);
        RollbackButton.Visibility = DeleteOldButton.Visibility = prev != null ? Visibility.Visible : Visibility.Collapsed;
        if (prev != null)
        {
            RollbackButton.Content = Loc.F("lib.rollback", prev.Version);
            DeleteOldButton.Content = Loc.T("lib.deleteold");
        }

        StatusList.Children.Clear();
        if (m.IsReleased)
        {
            AddStatus(installed, installed ? s.InstallDir(m) : s.FoundUnknown(m) ? Loc.F("lib.found", s.InstallDir(m)) : Loc.T("home.notready"));
            AddStatus(WabbajackService.IsNexusLoggedIn, WabbajackService.IsNexusLoggedIn ? Loc.T("check.nexus.ok") : Loc.T("check.nexus.no"));
            if (update) AddStatus(false, Loc.F("lib.update.available", m.Version));
            if (prev != null) AddStatus(true, Loc.F("lib.previous", prev.Version));
            if (installed && BackupService.Supported(m)) AddBackupStatus(m);
            if (installed) AddAddonStatus(m);
        }
        if (!_watching) PlayMessage.Text = "";
    }

    private bool _watching;

    private void AddStatus(bool ok, string text) =>
        StatusList.Children.Add(new TextBlock
        {
            Text = (ok ? "✓  " : "•  ") + text,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6),
            Foreground = ok ? new SolidColorBrush(Color.FromRgb(0xC9, 0xC9, 0xD6)) : (Brush)FindResource("Pink"),
        });

    /// <summary>"Save backup: today 21:40 · open" — a click opens Tools.</summary>
    private void AddBackupStatus(Modlist m)
    {
        BackupInfo? last = null;
        try { last = BackupService.Latest(_main.Settings, m); } catch { /* unreadable backup folder */ }
        var line = new TextBlock
        {
            Text = (last != null ? "✓  " : "•  ") + (last != null ? Loc.F("lib.backup.last", ToolsView.When(last.Date)) : Loc.T("lib.backup.none")),
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6),
            Foreground = (Brush)FindResource("Cyan"),
            Cursor = Cursors.Hand,
        };
        line.MouseLeftButtonUp += (_, _) => _main.ShowTools();
        StatusList.Children.Add(line);
    }

    /// <summary>"18+ add-on · off" — one neutral line; a click opens the add-on window (age confirmation first).</summary>
    private void AddAddonStatus(Modlist m)
    {
        var a = Addons.Adult(m.Id);
        if (a == null) return;
        var s = _main.Settings;
        var on = AddonService.Prefs(s, a).On;
        var reapply = AddonService.NeedsReapply(s, m, a);
        var box = new StackPanel { Margin = new Thickness(0, 6, 0, 6), Cursor = Cursors.Hand };
        box.Children.Add(new TextBlock
        {
            Text = (on && !reapply ? "✓  " : "•  ") + Loc.T(reapply ? "addon.line.reapply" : on ? "addon.line.on" : "addon.line.off"),
            FontSize = 13, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap,
            Foreground = reapply ? (Brush)FindResource("Pink") : (Brush)FindResource("Cyan"),
        });
        if (!on)
            box.Children.Add(new TextBlock { Text = Loc.F("addon.about", m.Title), FontSize = 12, Foreground = (Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        box.MouseLeftButtonUp += (_, _) =>
        {
            new AddonWindow(s, m, a) { Owner = _main }.ShowDialog();
            RefreshSelected();
        };
        StatusList.Children.Add(box);
    }

    private Border Card(Modlist m)
    {
        var installed = m.IsReleased && _main.Settings.IsInstalled(m);
        var selected = m.Id == _main.Selected.Id;
        var img = Images.Load(m.Banner);

        var (chipText, chipFg, chipBg) = !m.IsReleased
            ? (Loc.T("lib.soon"), "#C9C9D6", "#CC33333F")
            : installed ? (Loc.T("lib.installed"), "#9AF2FF", "#CC0F2A33") : (Loc.F("lib.available", m.SizeGb), "#FF7AD9", "#CC2A1426");
        var bc = new BrushConverter();

        var grid = new Grid();
        grid.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(13),
            Background = new LinearGradientBrush(Color.FromArgb(0x10, 0x0F, 0x0F, 0x14), Color.FromArgb(0xF0, 0x0F, 0x0F, 0x14), 90),
        });
        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(14, 0, 14, 12) };
        info.Children.Add(new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 2, 8, 2),
            Background = (Brush)bc.ConvertFrom(chipBg)!,
            Child = new TextBlock { Text = chipText, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = (Brush)bc.ConvertFrom(chipFg)! },
        });
        info.Children.Add(new TextBlock { Text = m.Title, FontFamily = (FontFamily)FindResource("TitleFont"), FontSize = m.Title.Length > 14 ? 14 : 18, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis });
        info.Children.Add(new TextBlock { Text = m.Tag.ToString(), FontSize = 11.5, Foreground = (Brush)FindResource("Muted"), TextTrimming = TextTrimming.CharacterEllipsis });
        grid.Children.Add(info);

        var card = new Border
        {
            Width = 226, Height = 124, Margin = new Thickness(0, 0, 16, 0), CornerRadius = new CornerRadius(14),
            Background = img != null
                ? new ImageBrush(img) { Stretch = Stretch.UniformToFill }
                : new LinearGradientBrush(Color.FromRgb(0x2A, 0x14, 0x26), Color.FromRgb(0x0F, 0x2A, 0x33), 45),
            BorderThickness = new Thickness(2),
            BorderBrush = selected ? (Brush)FindResource("Pink") : (Brush)bc.ConvertFrom("#2A2A36")!,
            Opacity = m.IsReleased ? 1 : 0.75,
            Cursor = Cursors.Hand,
            Child = grid,
        };
        card.MouseLeftButtonUp += (_, _) => _main.Select(m);
        return card;
    }

    public void Play() => MainButton_Click(this, new RoutedEventArgs());

    private async void MainButton_Click(object sender, RoutedEventArgs e)
    {
        var m = _main.Selected;
        var s = _main.Settings;
        if (!s.IsInstalled(m)) { _main.ShowSetup(); return; }
        if (_watching) return;
        MainButton.IsEnabled = false;
        try
        {
            // 1) the game was updated by Steam / Epic / GOG → mods may break
            PlayMessage.Text = Loc.T("play.checking");
            var game = await SystemCheck.FindGameAsync(m.Game, CancellationToken.None);
            if (game != null && !LaunchWatcher.SameGameVersion(game.Version, m.GameVersion))
            {
                var answer = MessageBox.Show(_main, Loc.F("play.version.text", m.Game, game.Version, m.Title, m.GameVersion),
                    Loc.T("play.version.title"), MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
                if (answer != MessageBoxResult.Yes) { PlayMessage.Text = ""; return; }
            }

            // 1b) loaders (CET, RED4ext, redscript…) must be in the game folder itself: MO2 can't provide them
            if (GameLoaders.Has(m))
            {
                if (game == null) { PlayMessage.Text = Loc.T("loaders.nogame"); return; }
                PlayMessage.Text = Loc.T("loaders.working");
                var conflicts = await Task.Run(() => GameLoaders.Conflicts(s, m, game.Path));
                var replace = conflicts.Count > 0 && MessageBox.Show(_main,
                    Loc.F("loaders.conflict", game.Path, string.Join("\n", conflicts.Select(c => "• " + c.Path))),
                    m.Title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes) == MessageBoxResult.Yes;
                try
                {
                    var missing = await Task.Run(() => GameLoaders.Ensure(s, m, game.Path, replace));
                    if (missing.Count > 0)
                        MessageBox.Show(_main, Loc.F("loaders.missing", string.Join("\n", missing.Select(x => "• " + x))),
                            m.Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                catch (UnauthorizedAccessException) { PlayMessage.Text = Loc.F("loaders.denied", game.Path); return; }
                catch (IOException ex) { PlayMessage.Text = Loc.F("loaders.error", ex.Message); return; }
                CleanGameButton.Visibility = GameLoaders.Placed(m) ? Visibility.Visible : Visibility.Collapsed;
            }

            // 2) save backup (never blocks the game: a failed backup is only reported)
            string? backupNote = null;
            if (s.AutoBackup && BackupService.Supported(m) && !BackupService.GameRunning(m))
            {
                PlayMessage.Text = Loc.T("backup.working");
                try { await BackupService.CreateAsync(s, m, BackupKind.Auto, CancellationToken.None); }
                catch (Exception ex) { backupNote = Loc.F("backup.play.skip", ex.Message); }
            }

            // 3) game mode (if the player turned it on for PLAY): close the picked programs before the game
            var gameModeByUs = false;
            if (s.GameOnPlay && !GameMode.IsActive)
            {
                PlayMessage.Text = Loc.T("game.play.on");
                try { await Task.Run(() => GameMode.Enable(ToolsView.GameOptions(s, byPlay: true))); gameModeByUs = true; }
                catch { /* never blocks the game */ }
                _main.RefreshToolsDot();
            }

            // 4) start and watch
            PlayService.Play(s, m);
            PlayMessage.Text = backupNote == null ? Loc.T("play.watch") : backupNote + "\n" + Loc.T("play.watch");
            _watching = true;
            LaunchReport report;
            try { report = await LaunchWatcher.WatchAsync(s, m, CancellationToken.None); }
            finally
            {
                // the game has exited (or the watch failed): give the programs back
                if (gameModeByUs) { try { await Task.Run(GameMode.Disable); } catch { } _main.RefreshToolsDot(); }
            }
            _watching = false;
            PlayMessage.Text = "";
            if (report.Result != LaunchResult.Ok) await HandleProblemAsync(m, report);
        }
        catch (Exception ex)
        {
            _watching = false;
            PlayMessage.Text = Loc.F("home.play.error", ex.Message);
        }
        finally
        {
            MainButton.IsEnabled = true;
        }
    }

    private async Task HandleProblemAsync(Modlist m, LaunchReport report)
    {
        var win = new LaunchProblemWindow(report, _main.Settings.PreviousOf(m)) { Owner = _main };
        win.ShowDialog();
        switch (win.Choice)
        {
            case ProblemChoice.Repair:
                _main.ShowSetup(startVerify: true);
                break;
            case ProblemChoice.Rollback:
                Rollback(m, ask: false);
                break;
            case ProblemChoice.Report:
                try
                {
                    var file = await ReportService.CreateAsync(_main.Settings, m, CancellationToken.None);
                    PlayMessage.Text = Loc.F("start.report.done", Path.GetFileName(file));
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{file}\"") { UseShellExecute = true });
                    PlayService.OpenSupportChat(Loc.F("report.send.text", Path.GetFileName(file)), _main.Settings, m);
                }
                catch (Exception ex) { PlayMessage.Text = Loc.F("start.report.error", ex.Message); }
                break;
        }
    }

    private void Rollback(Modlist m, bool ask)
    {
        var s = _main.Settings;
        var prev = s.PreviousOf(m);
        if (prev == null) return;
        if (ask && MessageBox.Show(_main, Loc.F("lib.rollback.confirm", prev.Version, prev.Dir, s.InstalledVersion(m) ?? ""),
                Loc.T("lib.rollback.title"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        s.SwapWithPrevious(m);
        Refresh();
        PlayMessage.Text = Loc.F("lib.rollback.done", s.InstalledVersion(m) ?? "");
    }

    private void Rollback_Click(object sender, RoutedEventArgs e) => Rollback(_main.Selected, ask: true);

    /// <summary>Takes the loaders the launcher put into the game folder back out.</summary>
    private void CleanGame_Click(object sender, RoutedEventArgs e)
    {
        var m = _main.Selected;
        if (_watching) { PlayMessage.Text = Loc.T("loaders.clean.running"); return; }
        if (MessageBox.Show(_main, Loc.T("loaders.clean.confirm"), Loc.T("loaders.clean"),
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        try
        {
            var kept = GameLoaders.Clean(m);
            RefreshSelected();
            PlayMessage.Text = kept.Count == 0 ? Loc.T("loaders.clean.done")
                : Loc.F("loaders.clean.kept", string.Join(", ", kept));
        }
        catch (Exception ex) { RefreshSelected(); PlayMessage.Text = Loc.F("loaders.error", ex.Message); }
    }

    private async void DeleteOld_Click(object sender, RoutedEventArgs e)
    {
        var m = _main.Selected;
        var s = _main.Settings;
        var prev = s.PreviousOf(m);
        if (prev == null) return;
        // never delete the current install or something like "C:\"
        var target = Path.GetFullPath(prev.Dir).TrimEnd('\\');
        if (string.Equals(target, Path.GetFullPath(s.InstallDir(m)).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
            || Path.GetPathRoot(target)?.TrimEnd('\\') == target) return;
        if (MessageBox.Show(_main, Loc.F("lib.deleteold.confirm", prev.Version, prev.Dir), Loc.T("lib.deleteold"),
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        DeleteOldButton.IsEnabled = RollbackButton.IsEnabled = false;
        PlayMessage.Text = Loc.T("lib.deleting");
        var keep = Path.GetFullPath(s.DownloadsDir(m)).TrimEnd('\\');
        try
        {
            await Task.Run(() => DeleteExcept(prev.Dir, keep));
            s.Previous.Remove(m.Id);
            s.Save();
            PlayMessage.Text = Loc.T("lib.deleted");
        }
        catch (Exception ex)
        {
            PlayMessage.Text = Loc.F("lib.delete.error", ex.Message);
        }
        DeleteOldButton.IsEnabled = RollbackButton.IsEnabled = true;
        var msg = PlayMessage.Text;
        Refresh();
        PlayMessage.Text = msg;
    }

    /// <summary>Deletes an old install folder but never the mod archives folder (it may live inside it).</summary>
    private static void DeleteExcept(string dir, string keep)
    {
        foreach (var f in Directory.EnumerateFiles(dir)) { File.SetAttributes(f, FileAttributes.Normal); File.Delete(f); }
        foreach (var d in Directory.EnumerateDirectories(dir))
        {
            var full = Path.GetFullPath(d).TrimEnd('\\');
            if (string.Equals(full, keep, StringComparison.OrdinalIgnoreCase)) continue;
            if (keep.StartsWith(full + "\\", StringComparison.OrdinalIgnoreCase)) { DeleteExcept(d, keep); continue; }
            foreach (var f in Directory.EnumerateFiles(d, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(d, true);
        }
        if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
    }

    private void Details_Toggle(object sender, MouseButtonEventArgs e) =>
        DetailsPanel.Visibility = DetailsPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;

    private void Setup_Click(object sender, RoutedEventArgs e) => _main.ShowSetup();
    private void FolderButton_Click(object sender, RoutedEventArgs e) => PlayService.OpenFolder(_main.Settings.InstallDir(_main.Selected));
    private void Mo2Button_Click(object sender, RoutedEventArgs e)
    {
        try { PlayService.OpenMo2(_main.Settings, _main.Selected); } catch (Exception ex) { PlayMessage.Text = Loc.F("home.play.error", ex.Message); }
    }
}
