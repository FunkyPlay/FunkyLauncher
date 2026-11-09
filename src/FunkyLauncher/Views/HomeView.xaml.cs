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
        RenderNews();
    }

    /// <summary>Three FunkyPlay directions: modlists, software, videos.</summary>
    private void RenderTiles()
    {
        Tiles.Children.Clear();
        Tiles.Children.Add(Tile("🎮", "tile.mods", "#FF3CC8", () => _main.ShowLibrary()));
        Tiles.Children.Add(Tile("🧰", "tile.soft", "#3CE6FF", () => _main.ShowSoft()));
        Tiles.Children.Add(Tile("▶", "tile.video", "#A07CFF", () => PlayService.OpenUrl(AppConfig.YouTubeUrl)));
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

    private async Task LoadNewsAsync()
    {
        try { _news = await NewsService.LoadAsync(3, CancellationToken.None); }
        catch { _newsFailed = true; }
        RenderNews();
    }

    private void RenderNews()
    {
        NewsGrid.Children.Clear();
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
        }
        catch (Exception ex)
        {
            ReportText.Text = Loc.F("start.report.error", ex.Message);
        }
        ReportButton.IsEnabled = true;
    }

    private void Library_Click(object sender, RoutedEventArgs e) => _main.ShowLibrary();
    private void Support_Click(object sender, RoutedEventArgs e) => PlayService.OpenUrl(AppConfig.SupportUrl);
    private void Telegram_Click(object sender, RoutedEventArgs e) => PlayService.OpenUrl(AppConfig.TelegramUrl);
    private void YouTube_Click(object sender, RoutedEventArgs e) => PlayService.OpenUrl(AppConfig.YouTubeUrl);
    private void GitHub_Click(object sender, RoutedEventArgs e) => PlayService.OpenUrl(AppConfig.GitHubUrl);
    private void AllNews_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => PlayService.OpenUrl(AppConfig.TelegramUrl);
}
