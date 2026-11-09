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
            AddStatus(installed, installed ? s.InstallDir(m) : Loc.T("home.notready"));
            AddStatus(WabbajackService.IsNexusLoggedIn, WabbajackService.IsNexusLoggedIn ? Loc.T("check.nexus.ok") : Loc.T("check.nexus.no"));
            if (update) AddStatus(false, Loc.F("lib.update.available", m.Version));
            if (prev != null) AddStatus(true, Loc.F("lib.previous", prev.Version));
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
        info.Children.Add(new TextBlock { Text = m.Title, FontFamily = (FontFamily)FindResource("TitleFont"), FontSize = 19, Margin = new Thickness(0, 6, 0, 0) });
        info.Children.Add(new TextBlock { Text = m.Tag.ToString(), FontSize = 11.5, Foreground = (Brush)FindResource("Muted"), TextTrimming = TextTrimming.CharacterEllipsis });
        grid.Children.Add(info);

        var card = new Border
        {
            Width = 270, Height = 130, Margin = new Thickness(0, 0, 16, 0), CornerRadius = new CornerRadius(14),
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

            // 2) start and watch
            PlayService.Play(s, m);
            PlayMessage.Text = Loc.T("play.watch");
            _watching = true;
            var report = await LaunchWatcher.WatchAsync(s, m, CancellationToken.None);
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

    private void Setup_Click(object sender, RoutedEventArgs e) => _main.ShowSetup();
    private void FolderButton_Click(object sender, RoutedEventArgs e) => PlayService.OpenFolder(_main.Settings.InstallDir(_main.Selected));
    private void Mo2Button_Click(object sender, RoutedEventArgs e)
    {
        try { PlayService.OpenMo2(_main.Settings, _main.Selected); } catch (Exception ex) { PlayMessage.Text = Loc.F("home.play.error", ex.Message); }
    }
}
