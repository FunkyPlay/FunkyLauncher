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
        ToolCards.Children.Add(ToolCard("⟲", Loc.T("tools.backup"), Loc.T("tools.backup.text"), "#FF3CC8", active: true));
        ToolCards.Children.Add(ToolCard("●", Loc.T("tools.shoot"), Loc.T("tools.shoot.text"), "#3CE6FF", active: false));

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

    private Border ToolCard(string icon, string title, string text, string color, bool active)
    {
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
        if (!active) names.Children.Add(new TextBlock { Text = Loc.T("tools.soon"), FontSize = 12, Foreground = (Brush)FindResource("Muted") });
        head.Children.Add(names);
        var body = new StackPanel();
        body.Children.Add(head);
        body.Children.Add(new TextBlock { Text = text, FontSize = 13, Foreground = new SolidColorBrush(Color.FromRgb(0xC9, 0xC9, 0xD6)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) });
        return new Border
        {
            Style = (Style)FindResource("CardBorder"), Padding = new Thickness(18, 16, 18, 16), Margin = new Thickness(0, 0, 16, 8),
            BorderThickness = new Thickness(active ? 2 : 0), BorderBrush = (Brush)FindResource("Pink"), Opacity = active ? 1 : 0.6, Child = body,
        };
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
