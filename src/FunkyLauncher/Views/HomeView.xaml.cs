// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FunkyLauncher.Core;
using FunkyLauncher.Services;

namespace FunkyLauncher.Views;

/// <summary>Home: about Funky Launcher, FunkyPlay news, what's new, support.</summary>
public partial class HomeView : UserControl
{
    private readonly MainWindow _main;
    private List<NewsPost>? _news;
    private bool _newsFailed;

    public HomeView(MainWindow main)
    {
        InitializeComponent();
        _main = main;
        Loc.Changed += Refresh;
        Refresh();
        _ = LoadNewsAsync();
    }

    public void Refresh()
    {
        BrandTag.Text = Loc.T("start.brand");
        StartTitle.Text = Loc.T("start.title");
        RenderTiles();
        StartText.Text = Loc.T("start.text");
        PremiumText.Text = Loc.T("start.premium");
        FreeText.Text = Loc.T("start.free");
        LibraryButton.Content = Loc.T("start.library");
        NewsTitle.Text = Loc.T("home.news");
        AllNews.Text = Loc.T("home.news.all");
        WhatsNewTitle.Text = Loc.F("start.whatsnew", AppConfig.Version);
        WhatsNewText.Text = Loc.T("start.whatsnew.items");
        SupportTitle.Text = Loc.T("start.support");
        SupportText.Text = Loc.T("start.support.text");
        SupportButton.Content = Loc.T("start.support.write");
        ReportButton.Content = Loc.T("start.support.report");
        HelpButton.Content = Loc.T(_helpOn ? "help.stop" : "help.button");
        HelpButton.Visibility = RemoteHelp.Available ? Visibility.Visible : Visibility.Collapsed;
        HelpSendButton.Content = Loc.T("help.send");
        HelpSendButton.Visibility = _helpOn ? Visibility.Visible : Visibility.Collapsed;
        RenderNews();
    }

    private bool _helpOn;

    /// <summary>
    /// "Send to support": copies "Funky Help — ID …" to the clipboard and opens @FunkyLauncherBot, so the player only
    /// pastes it and adds the one-time password from the RustDesk window (RustDesk never gives that password out).
    /// </summary>
    private async void HelpSend_Click(object sender, RoutedEventArgs e)
    {
        HelpSendButton.IsEnabled = false;
        try
        {
            var id = await RemoteHelp.GetIdAsync();
            var text = id != null ? Loc.F("help.send.text", RemoteHelp.FormatId(id)) : Loc.T("help.send.noid");
            PlayService.OpenSupportChat(text, _main.Settings, _main.Selected);
            ReportText.Text = id != null ? Loc.T("help.send.done") : Loc.T("help.send.done.noid");
        }
        catch (Exception ex) { ReportText.Text = Loc.F("help.error", ex.Message); }
        HelpSendButton.IsEnabled = true;
    }

    /// <summary>Funky Help: lets support connect to this PC with RustDesk — only while its window is open.</summary>
    private async void Help_Click(object sender, RoutedEventArgs e)
    {
        if (_helpOn)
        {
            RemoteHelp.Stop();
            _helpOn = false;
            HelpButton.Content = Loc.T("help.button");
            HelpSendButton.Visibility = Visibility.Collapsed;
            ReportText.Text = Loc.T("help.stopped");
            return;
        }
        if (MessageBox.Show(Window.GetWindow(this), Loc.T("help.confirm"), Loc.T("help.title"),
                MessageBoxButton.YesNo, MessageBoxImage.Information, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        HelpButton.IsEnabled = false;
        try
        {
            await RemoteHelp.StartAsync(new Progress<int>(p => ReportText.Text = Loc.F("help.downloading", p)), CancellationToken.None);
            _helpOn = true;
            HelpButton.Content = Loc.T("help.stop");
            HelpSendButton.Visibility = Visibility.Visible;
            ReportText.Text = Loc.T("help.started");
        }
        catch (Exception ex) { ReportText.Text = Loc.F("help.error", ex.Message); }
        HelpButton.IsEnabled = true;
    }

    /// <summary>Three FunkyPlay directions: modlists, software, videos.</summary>
    private void RenderTiles()
    {
        Tiles.Children.Clear();
        Tiles.Children.Add(Tile("🎮", "tile.mods", "#FF3FD2", () => _main.ShowLibrary()));
        Tiles.Children.Add(Tile("🧰", "tile.soft", "#7AE8FF", () => _main.ShowSoft()));
        Tiles.Children.Add(Tile("▶", "tile.video", "#B9A2F2", () => PlayService.OpenUrl(AppConfig.YouTubeUrl)));
    }

    private Border Tile(string icon, string key, string color, Action open)
    {
        var accent = (Brush)new BrushConverter().ConvertFrom(color)!;
        var iconBox = new Border
        {
            Width = 46, Height = 46, CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 0, 14, 0),
            Background = new SolidColorBrush(((SolidColorBrush)accent).Color) { Opacity = 0.18 },
            BorderBrush = accent, BorderThickness = new Thickness(1),
            Child = new TextBlock { Text = icon, FontSize = 20, Foreground = accent, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock { Text = Loc.T(key), FontSize = 16, FontWeight = FontWeights.Bold });
        texts.Children.Add(new TextBlock { Text = Loc.T(key + ".text"), FontSize = 12.5, Foreground = (Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
        var row = new DockPanel();
        DockPanel.SetDock(iconBox, Dock.Left);
        row.Children.Add(iconBox);
        row.Children.Add(texts);
        var tile = new Border
        {
            Style = (Style)FindResource("CardBorder"), Padding = new Thickness(18, 14, 18, 14), Margin = new Thickness(0, 0, 14, 0),
            Cursor = System.Windows.Input.Cursors.Hand, Child = row,
            BorderBrush = (Brush)new BrushConverter().ConvertFrom("#2A2A36")!, BorderThickness = new Thickness(1),
        };
        tile.MouseEnter += (_, _) => tile.BorderBrush = accent;
        tile.MouseLeave += (_, _) => tile.BorderBrush = (Brush)new BrushConverter().ConvertFrom("#2A2A36")!;
        tile.MouseLeftButtonUp += (_, _) => open();
        return tile;
    }

    /// <summary>Called when "Load news" is switched in Settings.</summary>
    public void ReloadNews()
    {
        _news = null;
        _newsFailed = false;
        _ = LoadNewsAsync();
    }

    private async Task LoadNewsAsync()
    {
        if (!_main.Settings.LoadNews) { RenderNews(); return; } // privacy: news turned off
        try { _news = await NewsService.LoadAsync(3, CancellationToken.None); }
        catch { _newsFailed = true; }
        RenderNews();
    }

    private void RenderNews()
    {
        NewsGrid.Children.Clear();
        if (!_main.Settings.LoadNews) { NewsGrid.Children.Add(NewsCard(null, Loc.T("home.news.off"), null)); return; }
        if (_news == null && !_newsFailed) { NewsGrid.Children.Add(NewsCard(null, Loc.T("home.news.loading"), null)); return; }
        if (_news == null || _news.Count == 0) { NewsGrid.Children.Add(NewsCard(null, Loc.T("home.news.empty"), null)); return; }
        foreach (var p in _news) NewsGrid.Children.Add(NewsCard(p.Date, p.Text, p.Url));
    }

    private Border NewsCard(DateTimeOffset? date, string text, string? url)
    {
        var panel = new StackPanel();
        if (date != null)
            panel.Children.Add(new TextBlock { Text = date.Value.ToLocalTime().ToString("dd.MM.yyyy"), FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x7A, 0xD9)), Margin = new Thickness(0, 0, 0, 6) });
        panel.Children.Add(new TextBlock { Text = text, FontSize = 13, LineHeight = 19, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.WordEllipsis, Foreground = new SolidColorBrush(Color.FromRgb(0xE2, 0xE2, 0xEA)) });
        var card = new Border { Style = (Style)FindResource("CardBorder"), Margin = new Thickness(0, 0, 14, 0), Child = panel };
        if (url != null)
        {
            card.Cursor = System.Windows.Input.Cursors.Hand;
            card.MouseLeftButtonUp += (_, _) => PlayService.OpenUrl(url);
        }
        return card;
    }

    private async void Report_Click(object sender, RoutedEventArgs e)
    {
        ReportButton.IsEnabled = false;
        ReportText.Text = Loc.T("start.report.working");
        try
        {
            var file = await ReportService.CreateAsync(_main.Settings, _main.Selected, CancellationToken.None);
            ReportText.Text = Loc.F("start.report.done", Path.GetFileName(file));
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{file}\"") { UseShellExecute = true });
            PlayService.OpenSupportChat(Loc.F("report.send.text", Path.GetFileName(file)), _main.Settings, _main.Selected);
        }
        catch (Exception ex)
        {
            ReportText.Text = Loc.F("start.report.error", ex.Message);
        }
        ReportButton.IsEnabled = true;
    }

    private void Library_Click(object sender, RoutedEventArgs e) => _main.ShowLibrary();
    // "Contact support": the support bot with a ready message (was the news channel)
    private void Support_Click(object sender, RoutedEventArgs e) =>
        PlayService.OpenSupportChat(Loc.T("support.write.text"), _main.Settings, _main.Selected);
    private void Telegram_Click(object sender, RoutedEventArgs e) => PlayService.OpenUrl(AppConfig.TelegramUrl);
    private void YouTube_Click(object sender, RoutedEventArgs e) => PlayService.OpenUrl(AppConfig.YouTubeUrl);
    private void GitHub_Click(object sender, RoutedEventArgs e) => PlayService.OpenUrl(AppConfig.GitHubUrl);
    private void AllNews_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => PlayService.OpenUrl(AppConfig.TelegramUrl);
}
