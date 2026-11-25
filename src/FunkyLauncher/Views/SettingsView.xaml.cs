// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using FunkyLauncher.Core;
using Microsoft.Win32;

namespace FunkyLauncher.Views;

public partial class SettingsView : UserControl
{
    private readonly MainWindow _main;

    public SettingsView(MainWindow main)
    {
        InitializeComponent();
        _main = main;
        Loc.Changed += Refresh;
        Refresh();
    }

    public void Refresh()
    {
        var m = _main.Selected;
        PageTitle.Text = Loc.T("settings.title");
        ModlistLabel.Text = Loc.F("settings.modlist.for", m.Title);
        ModlistBrowse.Content = Loc.T("setup.browse");
        ModlistBox.Text = _main.Settings.ResolveWabbajack(m) ?? "—";
        PrivacyLabel.Text = Loc.T("settings.privacy");
        NewsCheck.Content = Loc.T("settings.news");
        CatalogCheck.Content = Loc.T("settings.catalog");
        NewsCheck.IsChecked = _main.Settings.LoadNews;
        CatalogCheck.IsChecked = _main.Settings.UpdateCatalog;
        PrivacyText.Text = Loc.T("settings.privacy.text");
        PrivacyLink.Text = Loc.T("settings.privacy.link");
        RefreshPro();
        AboutLabel.Text = Loc.T("settings.about");
        var ver = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0";
        AboutText.Text = Loc.F("settings.about.text", ver, Catalog.Source);
        AboutNotice.Text = Loc.T("settings.about.notice");
        AboutThirdParty.Text = Loc.T("settings.about.thirdparty");
        AboutLinks.Inlines.Clear();
        foreach (var (text, url) in new[] { ("🌐 " + Loc.T("site.name"), SiteLinks.Home), ("✈ Telegram", SiteLinks.Telegram), ("▶ YouTube", SiteLinks.YouTube) })
        {
            if (AboutLinks.Inlines.Count > 0) AboutLinks.Inlines.Add(new System.Windows.Documents.Run("     "));
            var link = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run(text)) { Foreground = (System.Windows.Media.Brush)FindResource("Cyan"), TextDecorations = null };
            link.Click += (_, _) => { try { Services.PlayService.OpenUrl(url); } catch { } };
            AboutLinks.Inlines.Add(link);
        }
    }

    // ---------- Funky Pro key ----------

    private void RefreshPro()
    {
        ProLabel.Text = "Funky Pro";
        ProLabel.Visibility = ProCard.Visibility = Services.ProModules.ShowUi ? Visibility.Visible : Visibility.Collapsed;
        ProActivate.Content = Loc.T("pro.activate");
        ProRemove.Content = Loc.T("pro.remove");
        var (status, info) = ProLicense.Check(_main.Settings.ProKey, DateTime.UtcNow);
        ProStatus.Text = status switch
        {
            ProKeyStatus.Valid => Loc.F("pro.status.valid", info!.Until.ToString("dd.MM.yyyy")),
            ProKeyStatus.Expired => Loc.F("pro.status.expired", info!.Until.ToString("dd.MM.yyyy")),
            ProKeyStatus.Invalid => Loc.T("pro.status.invalid"),
            _ => Loc.T("pro.status.none"),
        };
        ProStatus.Foreground = status == ProKeyStatus.Valid ? (System.Windows.Media.Brush)FindResource("Cyan")
            : status == ProKeyStatus.None ? (System.Windows.Media.Brush)FindResource("Text") : (System.Windows.Media.Brush)FindResource("Pink");
        ProText.Text = Loc.T("pro.text");
        ProRemove.Visibility = string.IsNullOrEmpty(_main.Settings.ProKey) ? Visibility.Collapsed : Visibility.Visible;
        if (status == ProKeyStatus.Valid) ProKeyBox.Text = "";
        // a valid key: the "Funky Pro is active" block (design team, 19.11); the key box only for "Change key"
        var activeView = status == ProKeyStatus.Valid && !_changingKey;
        ProActive.Content = activeView ? ActiveBlock(info!) : null;
        ProStatus.Visibility = ProText.Visibility = ProKeyRow.Visibility = activeView ? Visibility.Collapsed : Visibility.Visible;
        ProPanels.Fill(ProSettingsHost, ProPlace.Settings);
    }

    private void ProActivate_Click(object sender, RoutedEventArgs e)
    {
        var key = ProKeyBox.Text.Trim();
        var (status, info) = ProLicense.Check(key, DateTime.UtcNow);
        switch (status)
        {
            case ProKeyStatus.Valid:
                _main.Settings.ProKey = key;
                _main.Settings.Save();
                _changingKey = false;
                ProMessage.Text = Loc.F("pro.done", info!.Until.ToString("dd.MM.yyyy"));
                _main.OnProChanged();
                break;
            case ProKeyStatus.Expired:
                ProMessage.Text = Loc.F("pro.status.expired", info!.Until.ToString("dd.MM.yyyy"));
                break;
            default:
                ProMessage.Text = Loc.T("pro.bad");
                break;
        }
        RefreshPro();
    }

    private bool _changingKey;

    private FrameworkElement ActiveBlock(ProKeyInfo info)
    {
        var key = _main.Settings.ProKey;
        var masked = key.Length > 8 ? "FPRO-••••-••••-" + key[^4..].ToUpperInvariant() : "FPRO-••••";
        var grid = new DockPanel();
        var tile = new Border
        {
            Width = 64, Height = 64, CornerRadius = new CornerRadius(16), VerticalAlignment = VerticalAlignment.Top,
            Background = new System.Windows.Media.LinearGradientBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF3FD2"),
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#7AE8FF"), new Point(0, 0), new Point(1, 1)),
            Child = new TextBlock { Text = "★", FontSize = 26, Foreground = System.Windows.Media.Brushes.Black, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        DockPanel.SetDock(tile, Dock.Left);
        grid.Children.Add(tile);
        Button Btn(string text, bool primary, RoutedEventHandler click)
        {
            var b = new Button { Content = text, Style = (Style)FindResource(primary ? "PrimaryButton" : "SecondaryButton"), Height = primary ? 54 : 46, FontSize = primary ? 16 : 15, Padding = new Thickness(22, 0, 22, 0), Margin = new Thickness(12, 0, 0, 10) };
            b.Click += click;
            return b;
        }
        var buttons = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(Btn(Loc.T("pro.set.open"), true, (_, _) => _main.ShowProPage()));
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        row.Children.Add(Btn(Loc.T("pro.set.change"), false, (_, _) => { _changingKey = true; RefreshPro(); ProKeyBox.Focus(); }));
        row.Children.Add(Btn(Loc.T("pro.remove"), false, ProRemove_Click));
        buttons.Children.Add(row);
        DockPanel.SetDock(buttons, Dock.Right);
        grid.Children.Add(buttons);
        var texts = new StackPanel { Margin = new Thickness(18, 4, 0, 0) };
        var title = new TextBlock { FontSize = 19, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap };
        title.Inlines.Add(new System.Windows.Documents.Run(Loc.T("pro.set.active")));
        var until = new System.Windows.Documents.Run(Loc.F("pro.set.until", info.Until.ToString("dd.MM.yyyy")));
        until.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Cyan");
        title.Inlines.Add(until);
        texts.Children.Add(title);
        texts.Children.Add(new TextBlock { Text = Loc.F("pro.set.key", masked), FontSize = 13, Foreground = (System.Windows.Media.Brush)FindResource("Muted"), Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap });
        texts.Children.Add(new TextBlock { Text = Loc.T("pro.set.free"), FontSize = 13, Foreground = (System.Windows.Media.Brush)FindResource("Muted"), Margin = new Thickness(0, 22, 0, 0), TextWrapping = TextWrapping.Wrap });
        grid.Children.Add(texts);
        return grid;
    }

    private void ProRemove_Click(object sender, RoutedEventArgs e)
    {
        _changingKey = false;
        _main.Settings.ProKey = "";
        _main.Settings.Save();
        ProMessage.Text = Loc.T("pro.removed");
        _main.OnProChanged();
        RefreshPro();
    }

    private void Privacy_Changed(object sender, RoutedEventArgs e)
    {
        _main.Settings.LoadNews = NewsCheck.IsChecked == true;
        _main.Settings.UpdateCatalog = CatalogCheck.IsChecked == true;
        _main.Settings.Save();
        _main.OnPrivacyChanged();
    }

    private void PrivacyLink_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
        Services.PlayService.OpenUrl(AppConfig.PrivacyUrl);

    private void ModlistBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Wabbajack (*.wabbajack)|*.wabbajack" };
        if (dlg.ShowDialog() != true) return;
        _main.Settings.ModlistFiles[_main.Selected.Id] = dlg.FileName;
        _main.Settings.Save();
        Refresh();
    }
}
