// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using FunkyLauncher.Core;
using FunkyLauncher.Services;

namespace FunkyLauncher.Views;

/// <summary>Free tools: save backup now, more later (recording mode…).</summary>
public partial class ToolsView : UserControl
{
    private readonly MainWindow _main;
    private string? _modlistId;
    private bool _busy;

    public ToolsView(MainWindow main)
    {
        InitializeComponent();
        _main = main;
        Loc.Changed += Refresh;
        Refresh();
    }

    private Settings S => _main.Settings;

    private static List<Modlist> Supported => Catalog.Items.Where(m => m.IsReleased && BackupService.Supported(m)).ToList();

    private Modlist? Current
    {
        get
        {
            var all = Supported;
            return all.FirstOrDefault(m => m.Id == _modlistId) ?? all.FirstOrDefault(m => m.Id == _main.Selected.Id) ?? all.FirstOrDefault();
        }
    }

    public void Refresh()
    {
        PageTitle.Text = Loc.T("tools.title");
        Subtitle.Text = Loc.T("tools.subtitle");
        ToolCards.Children.Clear();
        ToolCards.Children.Add(ToolCard("⟲", Loc.T("tools.backup"), Loc.T("tools.backup.text"), "#FF3CC8", "backup", null));
        ToolCards.Children.Add(ToolCard("●", Loc.T("tools.shoot"), Loc.T("tools.shoot.text"), "#3CE6FF", "shoot",
            RecordingMode.IsActive ? Loc.T("tools.on") : null));
        ToolCards.Children.Add(ToolCard("▶", Loc.T("tools.game"), Loc.T("tools.game.text"), "#46D46E", "game",
            GameMode.IsActive ? Loc.T("tools.on") : null));
        ToolCards.Children.Add(ToolCard("✓", Loc.T("tools.pc"), Loc.T("tools.pc.text"), "#A07CFF", "pc", null));
        ToolCards.Children.Add(ToolCard("▷", Loc.T("tools.player"), Loc.T("tools.player.text"), "#FF8A3C", "player", null));
        GamePanel.Visibility = _tool == "game" ? Visibility.Visible : Visibility.Collapsed;
        RefreshGame();
        BackupPanel.Visibility = _tool == "backup" ? Visibility.Visible : Visibility.Collapsed;
        ShootPanel.Visibility = _tool == "shoot" ? Visibility.Visible : Visibility.Collapsed;
        PcPanel.Visibility = _tool == "pc" ? Visibility.Visible : Visibility.Collapsed;
        RefreshShoot();
        RefreshPc();

        BackupTitle.Text = Loc.T("tools.backup");
        AutoCheck.Content = Loc.F("backup.auto", BackupService.KeepAuto);
        AutoCheck.IsChecked = S.AutoBackup;
        NowButton.Content = Loc.T("backup.now");
        SavesButton.Content = Loc.T("backup.opensaves");
        DirButton.Content = Loc.T("backup.changedir");
        ListTitle.Text = Loc.T("backup.list");
        RefreshBackup();
    }

    private void RefreshBackup()
    {
        var m = Current;
        ModlistRow.Children.Clear();
        ListPanel.Children.Clear();
        var has = m != null;
        NowButton.IsEnabled = SavesButton.IsEnabled = DirButton.IsEnabled = AutoCheck.IsEnabled = has && !_busy;
        if (m == null) { SourceText.Text = Loc.T("backup.none"); StoreText.Text = ""; return; }

        ModlistRow.Children.Add(new TextBlock { Text = Loc.T("backup.for"), FontSize = 13, Foreground = (Brush)FindResource("Muted"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
        foreach (var x in Supported)
        {
            var on = x.Id == m.Id;
            var chip = new Button
            {
                Content = x.Title, Style = (Style)FindResource("SecondaryButton"), Height = 32, Padding = new Thickness(14, 0, 14, 0),
                Margin = new Thickness(0, 0, 8, 0), BorderBrush = on ? (Brush)FindResource("Pink") : (Brush)FindResource("Line"),
            };
            chip.Click += (_, _) => { _modlistId = x.Id; Message.Text = ""; RefreshBackup(); };
            ModlistRow.Children.Add(chip);
        }

        var source = m.ResolveSaveFolder()!;
        SourceText.Text = Directory.Exists(source) ? Loc.F("backup.source", source) : Loc.F("backup.source.missing", source);
        var list = BackupService.List(S, m);
        StoreText.Text = Loc.F("backup.store", BackupService.Root(S), Size(list.Sum(x => x.NewBytes)));
        if (BackupService.GameRunning(m)) Message.Text = Loc.T("backup.running");

        if (list.Count == 0)
        {
            ListPanel.Children.Add(new TextBlock { Text = Loc.T("backup.empty"), FontSize = 13, Foreground = (Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap });
            return;
        }
        foreach (var b in list) ListPanel.Children.Add(Row(m, b));
    }

    private Border ToolCard(string icon, string title, string text, string color, string id, string? badge)
    {
        var active = _tool == id;
        var bc = new BrushConverter();
        var head = new DockPanel();
        var tile = new Border
        {
            Width = 44, Height = 44, CornerRadius = new CornerRadius(12), Background = (Brush)bc.ConvertFrom(color)!,
            Child = new TextBlock { Text = icon, FontSize = 20, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        DockPanel.SetDock(tile, Dock.Left);
        head.Children.Add(tile);
        var names = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.Bold });
        if (badge != null) names.Children.Add(new TextBlock { Text = "● " + badge, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("Pink") });
        head.Children.Add(names);
        var body = new StackPanel();
        body.Children.Add(head);
        body.Children.Add(new TextBlock { Text = text, FontSize = 13, Foreground = new SolidColorBrush(Color.FromRgb(0xC9, 0xC9, 0xD6)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) });
        var card = new Border
        {
            Style = (Style)FindResource("CardBorder"), Padding = new Thickness(18, 16, 18, 16), Margin = new Thickness(0, 0, 16, 8),
            BorderThickness = new Thickness(2), BorderBrush = active ? (Brush)FindResource("Pink") : Brushes.Transparent,
            Cursor = System.Windows.Input.Cursors.Hand, Child = body,
        };
        card.MouseLeftButtonUp += (_, _) =>
        {
            if (id == "player") { OpenPlayer(); return; } // the player is its own window
            _tool = id; Refresh();
        };
        return card;
    }

    // ---------- player ----------

    private PlayerWindow? _player;

    public void OpenPlayer(string? file = null)
    {
        if (_player is { IsLoaded: true }) { _player.Activate(); return; }
        _player = new PlayerWindow(file) { Owner = _main };
        _player.Show();
    }

    // ---------- game mode ----------

    private List<RunningApp>? _apps;

    public void ShowGame() { _tool = "game"; Refresh(); }

    private void RefreshGame()
    {
        var st = GameMode.Load();
        var on = st?.Active == true;
        GameTitle.Text = Loc.T("tools.game");
        GameStatus.Text = on ? Loc.F("shoot.status.on", When(st!.Started)) : Loc.T("shoot.status.off");
        GameStatus.Foreground = on ? (Brush)FindResource("Pink") : (Brush)FindResource("Muted");
        GameIntro.Text = Loc.T("game.intro");
        GameAppsTitle.Text = Loc.T("game.apps");
        GameRefresh.Content = Loc.T("game.refresh");
        GameTorrents.Content = Loc.T("game.torrents"); GameTorrents.IsChecked = S.GameTorrents;
        var power = false;
        try { power = GameMode.PowerModeAvailable(); } catch { }
        GamePower.Content = Loc.T("game.power"); GamePower.IsChecked = S.GamePower;
        GamePower.Visibility = power ? Visibility.Visible : Visibility.Collapsed;
        GamePowerNa.Text = Loc.T("game.power.na");
        GamePowerNa.Visibility = power ? Visibility.Collapsed : Visibility.Visible;
        GameOneDrive.Content = Loc.T("game.onedrive"); GameOneDrive.IsChecked = S.GameOneDrive;
        GameRelaunch.Content = Loc.T("game.relaunch"); GameRelaunch.IsChecked = S.GameRelaunch;
        GameOnPlay.Content = Loc.T("game.onplay"); GameOnPlay.IsChecked = S.GameOnPlay;
        var wgm = true;
        try { wgm = GameMode.WindowsGameModeOn(); } catch { }
        GameWinRow.Visibility = wgm ? Visibility.Collapsed : Visibility.Visible;
        GameWinText.Text = Loc.T("game.wingm.off");
        GameWinButton.Content = Loc.T("game.wingm.open");
        GameButton.Content = on ? Loc.T("game.off") : Loc.T("game.on");
        foreach (var c in new Control[] { GameTorrents, GamePower, GameOneDrive, GameRelaunch, GameRefresh }) c.IsEnabled = !on;
        if (_tool == "game" && _apps == null && !on) LoadApps();
        ShowApps(on);
    }

    private void LoadApps()
    {
        try { _apps = GameMode.RunningApps(); } catch { _apps = new(); }
    }

    private void ShowApps(bool on)
    {
        GameApps.Children.Clear();
        if (on)
        {
            var closed = GameMode.Load()?.Closed ?? new();
            foreach (var a in closed) GameApps.Children.Add(new TextBlock { Text = "✓ " + a.Name, FontSize = 13, Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(0, 0, 18, 6) });
            return;
        }
        var list = (_apps ?? new()).Where(a => !a.Torrent).ToList();
        if (list.Count == 0) { GameApps.Children.Add(new TextBlock { Text = Loc.T("game.apps.empty"), FontSize = 13, Foreground = (Brush)FindResource("Muted") }); return; }
        foreach (var a in list)
        {
            var cb = new CheckBox
            {
                Content = Loc.F("game.app", a.Title, Size(a.Bytes)), Margin = new Thickness(0, 0, 18, 8),
                IsChecked = S.GameApps.Contains(a.Name, StringComparer.OrdinalIgnoreCase), Tag = a.Name,
            };
            cb.Click += (_, _) =>
            {
                S.GameApps.RemoveAll(x => string.Equals(x, a.Name, StringComparison.OrdinalIgnoreCase));
                if (cb.IsChecked == true) S.GameApps.Add(a.Name);
                S.Save();
            };
            GameApps.Children.Add(cb);
        }
    }

    private void GameRefresh_Click(object sender, RoutedEventArgs e) { LoadApps(); RefreshGame(); }

    private void GameOption_Click(object sender, RoutedEventArgs e)
    {
        S.GameTorrents = GameTorrents.IsChecked == true;
        S.GamePower = GamePower.IsChecked == true;
        S.GameOneDrive = GameOneDrive.IsChecked == true;
        S.GameRelaunch = GameRelaunch.IsChecked == true;
        S.GameOnPlay = GameOnPlay.IsChecked == true;
        S.Save();
    }

    private void GameWin_Click(object sender, RoutedEventArgs e) => PlayService.OpenUrl("ms-settings:gaming-gamemode");

    public void TurnOffGame() { if (GameMode.IsActive) Game_Click(this, new RoutedEventArgs()); }

    private async void Game_Click(object sender, RoutedEventArgs e)
    {
        GameButton.IsEnabled = false;
        var turnOn = !GameMode.IsActive;
        string msg;
        try
        {
            if (turnOn)
            {
                GameMessage.Text = Loc.T("game.working");
                var (freed, errors) = await Task.Run(() => GameMode.Enable(GameOptions(S, byPlay: false)));
                msg = Loc.F("game.done.on", Size(freed)) + (errors.Count > 0 ? "\n" + Loc.F("game.notclosed", string.Join(", ", errors)) : "");
            }
            else
            {
                var errors = await Task.Run(GameMode.Disable);
                msg = errors.Count > 0 ? Loc.F("game.error", string.Join("; ", errors)) : Loc.T("game.done.off");
                _apps = null;
            }
        }
        catch (Exception ex) { msg = ex.Message; }
        GameButton.IsEnabled = true;
        Refresh();
        _main.RefreshToolsDot();
        GameMessage.Text = msg;
    }

    public static GameMode.Options GameOptions(Settings s, bool byPlay) =>
        new(s.GameApps.ToList(), s.GameTorrents, s.GamePower, s.GameOneDrive, s.GameRelaunch, byPlay);

    // ---------- can my PC run it ----------

    private string? _pcModlistId;
    private PcResult? _pcResult;
    private string? _pcResultFor;

    private static List<Modlist> PcModlists => Catalog.Items.Where(m => m.IsReleased && m.PcCheck != null).ToList();

    private Modlist? PcCurrent => PcModlists.FirstOrDefault(m => m.Id == _pcModlistId) ?? PcModlists.FirstOrDefault(m => m.Id == _main.Selected.Id) ?? PcModlists.FirstOrDefault();

    /// <summary>Opens the PC check panel (e.g. from the developer test argument).</summary>
    public void ShowPc() { _tool = "pc"; Refresh(); }

    private void RefreshPc()
    {
        var m = PcCurrent;
        PcTitle.Text = Loc.T("tools.pc");
        PcButton.Content = Loc.T("pc.run");
        PcModlistRow.Children.Clear();
        PcButton.IsEnabled = m != null;
        if (m == null) { PcIntro.Text = Loc.T("pc.none"); return; }
        PcIntro.Text = Loc.T("pc.intro");
        PcModlistRow.Children.Add(new TextBlock { Text = Loc.T("backup.for"), FontSize = 13, Foreground = (Brush)FindResource("Muted"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
        foreach (var x in PcModlists)
        {
            var chip = new Button
            {
                Content = x.Title, Style = (Style)FindResource("SecondaryButton"), Height = 32, Padding = new Thickness(14, 0, 14, 0),
                Margin = new Thickness(0, 0, 8, 0), BorderBrush = x.Id == m.Id ? (Brush)FindResource("Pink") : (Brush)FindResource("Line"),
            };
            chip.Click += (_, _) => { _pcModlistId = x.Id; RefreshPc(); };
            PcModlistRow.Children.Add(chip);
        }
        ShowPcResult(_pcResultFor == m.Id ? _pcResult : null);
    }

    public void RunPcCheck() => Pc_Click(this, new RoutedEventArgs());

    private async void Pc_Click(object sender, RoutedEventArgs e)
    {
        var m = PcCurrent;
        if (m?.PcCheck == null) return;
        PcButton.IsEnabled = false;
        PcVerdict.Text = "";
        PcVerdictText.Text = Loc.T("pc.running");
        PcRows.Children.Clear();
        var installed = S.IsInstalled(m);
        var dir = S.InstallDir(m);
        try
        {
            var hw = await Task.Run(() => PcCheck.Read(dir));
            _pcResult = PcCheck.Evaluate(hw, m.PcCheck, m, installed, DateTime.Now);
            _pcResultFor = m.Id;
            ShowPcResult(_pcResult);
        }
        catch (Exception ex) { PcVerdictText.Text = ex.Message; }
        PcButton.IsEnabled = true;
    }

    private void ShowPcResult(PcResult? r)
    {
        PcRows.Children.Clear();
        if (r == null) { PcVerdict.Text = ""; PcVerdictText.Text = ""; return; }
        PcVerdict.Text = Loc.T(r.Verdict switch { CheckState.Ok => "pc.verdict.ok", CheckState.Warning => "pc.verdict.warn", _ => "pc.verdict.err" });
        PcVerdict.Foreground = r.Verdict switch
        {
            CheckState.Ok => (Brush)FindResource("Cyan"),
            CheckState.Warning => new SolidColorBrush(Color.FromRgb(0xFF, 0xC8, 0x3C)),
            _ => (Brush)FindResource("Pink"),
        };
        PcVerdictText.Text = Loc.T(r.Verdict switch { CheckState.Ok => "pc.verdict.ok.text", CheckState.Warning => "pc.verdict.warn.text", _ => "pc.verdict.err.text" });
        foreach (var row in r.Rows) PcRows.Children.Add(PcRowView(row));
    }

    private Border PcRowView(PcRow row)
    {
        var (mark, color) = row.State switch
        {
            CheckState.Ok => ("✓", (Brush)FindResource("Cyan")),
            CheckState.Warning => ("!", (Brush)new SolidColorBrush(Color.FromRgb(0xFF, 0xC8, 0x3C))),
            _ => ("✖", (Brush)FindResource("Pink")),
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new TextBlock { Text = mark, FontSize = 18, FontWeight = FontWeights.Bold, Foreground = color, VerticalAlignment = VerticalAlignment.Top });
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = row.Title, FontSize = 14.5, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        text.Children.Add(new TextBlock { Text = row.Detail, FontSize = 12.5, Foreground = (Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        if (!string.IsNullOrEmpty(row.AdviceText) && row.SoftId == null)
            text.Children.Add(new TextBlock { Text = row.AdviceText, FontSize = 12.5, Foreground = (Brush)FindResource("Cyan"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        var soft = row.SoftId == null ? null : Catalog.Software.FirstOrDefault(s => s.Id == row.SoftId);
        if (soft != null)
        {
            var b = new Button { Content = Loc.F("pc.open", soft.Name), ToolTip = row.AdviceText, Style = (Style)FindResource("SecondaryButton"), Height = 34, Padding = new Thickness(14, 0, 14, 0), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            b.Click += (_, _) => PlayService.OpenUrl(soft.Url);
            Grid.SetColumn(b, 2);
            grid.Children.Add(b);
        }
        return new Border { Background = (Brush)FindResource("CardActive"), CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 0, 8), Child = grid };
    }

    // ---------- recording mode ----------

    private string _tool = "backup";

    /// <summary>Opens the recording mode panel (e.g. from the start-up reminder).</summary>
    public void ShowShoot() { _tool = "shoot"; Refresh(); }

    private void RefreshShoot()
    {
        var state = RecordingMode.Load();
        var on = state?.Active == true;
        ShootTitle.Text = Loc.T("tools.shoot");
        ShootStatus.Text = on ? Loc.F("shoot.status.on", When(state!.Started)) : Loc.T("shoot.status.off");
        ShootStatus.Foreground = on ? (Brush)FindResource("Pink") : (Brush)FindResource("Muted");
        ShootIntro.Text = Loc.T("shoot.intro");
        ShootIcons.Content = Loc.T("shoot.icons"); ShootIcons.IsChecked = S.ShootIcons;
        ShootWallpaper.Content = Loc.T("shoot.wallpaper"); ShootWallpaper.IsChecked = S.ShootWallpaper;
        ShootWindows.Content = Loc.T("shoot.windows"); ShootWindows.IsChecked = S.ShootWindows;
        ShootTaskbar.Content = Loc.T("shoot.taskbar"); ShootTaskbar.IsChecked = S.ShootTaskbar;
        WallpaperText.Text = S.ShootWallpaperFile.Length > 0 ? Loc.F("shoot.wallpaper.own", Path.GetFileName(S.ShootWallpaperFile)) : Loc.T("shoot.wallpaper.brand");
        WallpaperPick.Content = Loc.T("shoot.wallpaper.pick");
        WallpaperBrand.Content = Loc.T("shoot.wallpaper.usebrand");
        WallpaperBrand.Visibility = S.ShootWallpaperFile.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var c in new Control[] { ShootIcons, ShootWallpaper, ShootWindows, ShootTaskbar, WallpaperPick, WallpaperBrand }) c.IsEnabled = !on;
        string warn = "";
        try { warn = !on && S.ShootWallpaper && RecordingMode.SpotlightWallpaper ? Loc.T("shoot.warn.spotlight") : ""; } catch { }
        ShootWarn.Text = warn;
        ShootWarn.Visibility = warn.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        ShootButton.Content = on ? Loc.T("shoot.off") : Loc.T("shoot.on");
        DndButton.Content = Loc.T("shoot.dnd");
        DndHint.Text = Loc.T("shoot.dnd.hint");
    }

    private void ShootOption_Click(object sender, RoutedEventArgs e)
    {
        S.ShootIcons = ShootIcons.IsChecked == true;
        S.ShootWallpaper = ShootWallpaper.IsChecked == true;
        S.ShootWindows = ShootWindows.IsChecked == true;
        S.ShootTaskbar = ShootTaskbar.IsChecked == true;
        S.Save();
        RefreshShoot();
    }

    private void WallpaperPick_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp" };
        if (dlg.ShowDialog(_main) != true) return;
        S.ShootWallpaperFile = dlg.FileName;
        S.Save();
        RefreshShoot();
    }

    private void WallpaperBrand_Click(object sender, RoutedEventArgs e)
    {
        S.ShootWallpaperFile = "";
        S.Save();
        RefreshShoot();
    }

    private void Dnd_Click(object sender, RoutedEventArgs e) => PlayService.OpenUrl("ms-settings:notifications");

    public void TurnOffShoot() { if (RecordingMode.IsActive) Shoot_Click(this, new RoutedEventArgs()); }

    private async void Shoot_Click(object sender, RoutedEventArgs e)
    {
        ShootButton.IsEnabled = false;
        var turnOn = !RecordingMode.IsActive;
        List<string> errors;
        try
        {
            if (turnOn)
            {
                var file = S.ShootWallpaperFile.Length > 0 && File.Exists(S.ShootWallpaperFile) ? S.ShootWallpaperFile : RecordingMode.BrandWallpaper();
                var o = new RecordingMode.Options(S.ShootIcons, S.ShootWallpaper, file, S.ShootWindows, S.ShootTaskbar);
                errors = await RunSta(() => RecordingMode.Enable(o));
            }
            else errors = await RunSta(RecordingMode.Disable);
        }
        catch (Exception ex) { errors = new() { ex.Message }; }
        ShootButton.IsEnabled = true;
        Refresh();
        _main.RefreshToolsDot();
        ShootMessage.Text = errors.Count > 0 ? Loc.F("shoot.error", string.Join("; ", errors))
            : Loc.T(turnOn ? "shoot.done.on" : "shoot.done.off");
    }

    /// <summary>Shell COM objects want a single-threaded apartment; run off the UI thread so the window stays responsive.</summary>
    private static Task<T> RunSta<T>(Func<T> f)
    {
        var tcs = new TaskCompletionSource<T>();
        var t = new Thread(() => { try { tcs.SetResult(f()); } catch (Exception ex) { tcs.SetException(ex); } });
        t.SetApartmentState(ApartmentState.STA);
        t.IsBackground = true;
        t.Start();
        return tcs.Task;
    }

    private Border Row(Modlist m, BackupInfo b)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var pic = new Border { Width = 128, Height = 72, CornerRadius = new CornerRadius(8), Background = (Brush)FindResource("Panel"), Margin = new Thickness(0, 0, 14, 0) };
        var img = Preview(b.PreviewPath);
        if (img != null) pic.Background = new ImageBrush(img) { Stretch = Stretch.UniformToFill };
        grid.Children.Add(pic);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = When(b.Date), FontSize = 15, FontWeight = FontWeights.Bold });
        text.Children.Add(new TextBlock { Text = Kind(b.Kind), FontSize = 12, Foreground = b.Kind == BackupKind.Auto ? (Brush)FindResource("Muted") : (Brush)FindResource("Cyan"), Margin = new Thickness(0, 2, 0, 0) });
        text.Children.Add(new TextBlock { Text = Loc.F("backup.line", b.Saves, Size(b.TotalBytes), Size(b.NewBytes)), FontSize = 12, Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(0, 2, 0, 0) });
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        buttons.Children.Add(Small(Loc.T("backup.restore"), () => _ = Restore(m, b), pink: true));
        buttons.Children.Add(Small(Loc.T("backup.open"), () => PlayService.OpenFolder(Path.Combine(b.Dir, "files"))));
        // automatic backups are rotated by the launcher; manual and "before restore" ones only by the player
        if (b.Kind != BackupKind.Auto) buttons.Children.Add(Small(Loc.T("backup.delete"), () => Delete(m, b)));
        Grid.SetColumn(buttons, 2);
        grid.Children.Add(buttons);

        return new Border { Background = (Brush)FindResource("CardActive"), CornerRadius = new CornerRadius(10), Padding = new Thickness(10), Margin = new Thickness(0, 0, 0, 8), Child = grid };
    }

    private Button Small(string text, Action click, bool pink = false)
    {
        var b = new Button { Content = text, Style = (Style)FindResource("SecondaryButton"), Height = 34, Padding = new Thickness(14, 0, 14, 0), Margin = new Thickness(8, 0, 0, 0), IsEnabled = !_busy };
        if (pink) b.BorderBrush = (Brush)FindResource("Pink");
        b.Click += (_, _) => click();
        return b;
    }

    private static BitmapImage? Preview(string? path)
    {
        if (path == null || !File.Exists(path)) return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad; // don't keep the file open (it may be deleted later)
            bmp.DecodePixelWidth = 256;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    private void SetBusy(bool busy, string message)
    {
        _busy = busy;
        Message.Text = message;
        RefreshBackupKeepMessage();
    }

    private void RefreshBackupKeepMessage()
    {
        var msg = Message.Text;
        RefreshBackup();
        Message.Text = msg;
    }

    private async void Now_Click(object sender, RoutedEventArgs e)
    {
        var m = Current;
        if (m == null || _busy) return;
        if (BackupService.GameRunning(m)) { Message.Text = Loc.T("backup.running"); return; }
        SetBusy(true, Loc.T("backup.working"));
        string msg;
        try
        {
            var b = await BackupService.CreateAsync(S, m, BackupKind.Manual, CancellationToken.None);
            msg = b == null ? Loc.T("backup.nosaves") : Loc.F("backup.done", b.Saves);
        }
        catch (Exception ex) { msg = Loc.F("backup.error", ex.Message); }
        SetBusy(false, msg);
    }

    private async Task Restore(Modlist m, BackupInfo b)
    {
        if (_busy) return;
        if (BackupService.GameRunning(m)) { Message.Text = Loc.T("backup.running"); return; }
        if (MessageBox.Show(_main, Loc.F("backup.restore.confirm", When(b.Date)), Loc.T("backup.restore"),
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        SetBusy(true, Loc.T("backup.restoring"));
        string msg;
        try
        {
            await BackupService.RestoreAsync(S, m, b, CancellationToken.None);
            msg = Loc.F("backup.restored", When(b.Date));
        }
        catch (Exception ex) { msg = Loc.F("backup.restore.error", ex.Message); }
        SetBusy(false, msg);
    }

    private void Delete(Modlist m, BackupInfo b)
    {
        if (_busy) return;
        if (MessageBox.Show(_main, Loc.F("backup.delete.confirm", When(b.Date)), Loc.T("backup.delete"),
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        try { BackupService.Delete(S, m, b); Message.Text = ""; }
        catch (Exception ex) { Message.Text = ex.Message; }
        RefreshBackupKeepMessage();
    }

    private void Auto_Click(object sender, RoutedEventArgs e)
    {
        S.AutoBackup = AutoCheck.IsChecked == true;
        S.Save();
    }

    private void Saves_Click(object sender, RoutedEventArgs e)
    {
        var dir = Current?.ResolveSaveFolder();
        if (dir != null) PlayService.OpenFolder(dir);
    }

    private void Dir_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = Loc.T("backup.changedir") };
        if (dlg.ShowDialog(_main) != true) return;
        S.BackupDir = dlg.FolderName;
        S.Save();
        Message.Text = "";
        RefreshBackup();
    }

    private static string Kind(BackupKind k) => k switch
    {
        BackupKind.Manual => Loc.T("backup.kind.manual"),
        BackupKind.BeforeRestore => Loc.T("backup.kind.beforerestore"),
        _ => Loc.T("backup.kind.auto"),
    };

    public static string When(DateTime d)
    {
        var time = d.ToString("HH:mm");
        if (d.Date == DateTime.Today) return Loc.F("date.today", time);
        if (d.Date == DateTime.Today.AddDays(-1)) return Loc.F("date.yesterday", time);
        return d.ToString("dd.MM.yyyy HH:mm");
    }

    public static string Size(long bytes) =>
        bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.0} {(Loc.Lang == "en" ? "GB" : "ГБ")}"
        : $"{Math.Max(0, bytes) / (double)(1L << 20):0.#} {(Loc.Lang == "en" ? "MB" : "МБ")}";
}
