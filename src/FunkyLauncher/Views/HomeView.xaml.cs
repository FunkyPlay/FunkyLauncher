// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FunkyLauncher.Core;
using FunkyLauncher.Services;

namespace FunkyLauncher.Views;

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
        var s = _main.Settings;
        var m = _main.Selected;
        var installed = m.IsReleased && s.IsInstalled(m);
        var img = Images.Load(m.Banner);
        Banner.Background = img != null
            ? new ImageBrush(img) { Stretch = Stretch.UniformToFill, AlignmentY = AlignmentY.Center }
            : new SolidColorBrush(Color.FromRgb(0x1D, 0x1D, 0x26));
        TitleText.Text = m.Title;
        TagText.Text = m.Tag.ToString();
        Sub.Text = !m.IsReleased ? Loc.F("home.sub.soon", m.Author)
            : installed ? Loc.F("home.sub.installed", m.Author, m.Version, s.InstallDir(m))
            : Loc.F("home.sub.notinstalled", m.Author, m.Version);
        MainButton.Content = installed ? Loc.T("home.play") : !m.IsReleased ? Loc.T("lib.soon") : Loc.T("home.install");
        MainButton.IsEnabled = m.IsReleased;
        FolderButton.Content = Loc.T("home.folder");
        Mo2Button.Content = Loc.T("home.mo2");
        FolderButton.IsEnabled = Mo2Button.IsEnabled = installed;
        NewsTitle.Text = Loc.T("home.news");
        AllNews.Text = Loc.T("home.news.all");
        StatusTitle.Text = installed ? Loc.T("home.ready") : Loc.T("home.notready");

        StatusList.Children.Clear();
        AddStatus(installed, installed ? s.InstallDir(m) : Loc.T("home.notready"));
        AddStatus(WabbajackService.IsNexusLoggedIn, WabbajackService.IsNexusLoggedIn ? Loc.T("check.nexus.ok") : Loc.T("check.nexus.no"));
        RenderNews();
    }

    private void AddStatus(bool ok, string text)
    {
        StatusList.Children.Add(new TextBlock
        {
            Text = (ok ? "✓  " : "•  ") + text,
            FontSize = 13,
            Margin = new Thickness(0, 0, 0, 6),
            Foreground = ok ? new SolidColorBrush(Color.FromRgb(0xC9, 0xC9, 0xD6)) : (Brush)FindResource("Pink"),
        });
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
        panel.Children.Add(new TextBlock { Text = text, FontSize = 13, LineHeight = 19, Foreground = new SolidColorBrush(Color.FromRgb(0xE2, 0xE2, 0xEA)) });
        var card = new Border { Style = (Style)FindResource("CardBorder"), Margin = new Thickness(0, 0, 14, 0), Child = panel };
        if (url != null)
        {
            card.Cursor = System.Windows.Input.Cursors.Hand;
            card.MouseLeftButtonUp += (_, _) => PlayService.OpenUrl(url);
        }
        return card;
    }

    private void MainButton_Click(object sender, RoutedEventArgs e)
    {
        var m = _main.Selected;
        if (!_main.Settings.IsInstalled(m)) { _main.ShowSetup(); return; }
        try
        {
            PlayService.Play(_main.Settings, m);
            PlayMessage.Text = Loc.T("home.play.started");
        }
        catch (Exception ex)
        {
            PlayMessage.Text = Loc.F("home.play.error", ex.Message);
        }
    }

    private void FolderButton_Click(object sender, RoutedEventArgs e) => PlayService.OpenFolder(_main.Settings.InstallDir(_main.Selected));
    private void Mo2Button_Click(object sender, RoutedEventArgs e)
    {
        try { PlayService.OpenMo2(_main.Settings, _main.Selected); } catch (Exception ex) { PlayMessage.Text = Loc.F("home.play.error", ex.Message); }
    }
    private void Telegram_Click(object sender, RoutedEventArgs e) => PlayService.OpenUrl(AppConfig.TelegramUrl);
    private void YouTube_Click(object sender, RoutedEventArgs e) => PlayService.OpenUrl(AppConfig.YouTubeUrl);
    private void AllNews_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => PlayService.OpenUrl(AppConfig.TelegramUrl);
}
