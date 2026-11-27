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
        NewsCheck.Visibility = CatalogCheck.Visibility = AppConfig.NexusEdition ? Visibility.Collapsed : Visibility.Visible; // Nexus version: no Telegram news, no online catalog
        CatalogCheck.IsChecked = _main.Settings.UpdateCatalog;
        PrivacyText.Text = Loc.T(AppConfig.NexusEdition ? "settings.privacy.text.nexus" : "settings.privacy.text");
        PrivacyLink.Text = Loc.T("settings.privacy.link");
        // 27.11 (Funky, Nexus rules): launcher betas are free — the launcher only, never modlists
        BetaCheck.Content = Loc.Lang == "en" ? "Get launcher beta versions" : "Получать бета-версии лаунчера";
        BetaCheck.IsChecked = _main.Settings.LauncherBeta;
        BetaCheck.Visibility = BetaText.Visibility = AppConfig.NexusEdition ? Visibility.Collapsed : Visibility.Visible; // Nexus version: no self-update
        BetaText.Text = Loc.Lang == "en"
            ? "A beta comes earlier through the same \"New version\" window on Home and may have bugs — found one, write to support. Only the launcher itself: modlists and fixes come to everyone at once. Turn it off — the next regular version comes as usual."
            : "Бета приходит раньше в том же окне «Новая версия» на Главной и может быть с ошибками — нашёл, напиши в поддержку. Только сам лаунчер: сборки и исправления приходят всем сразу. Выключишь — следующая обычная версия придёт как всегда.";
        AboutLabel.Text = Loc.T("settings.about");
        var ver = Assembly.GetExecutingAssembly().GetName().Version is { } av ? av.ToString(av.Revision > 0 ? 4 : 3) : "0.1.0";
        AboutText.Text = Loc.F("settings.about.text", ver, Catalog.Source);
        AboutNotice.Text = Loc.T("settings.about.notice");
        AboutThirdParty.Text = Loc.T("settings.about.thirdparty");
        AboutLinks.Inlines.Clear();
        foreach (var (text, url) in AppConfig.NexusEdition ? Array.Empty<(string, string)>() : new[] { ("🌐 " + Loc.T("site.name"), SiteLinks.Home), ("✈ Telegram", SiteLinks.Telegram), ("▶ YouTube", SiteLinks.YouTube) })
        {
            if (AboutLinks.Inlines.Count > 0) AboutLinks.Inlines.Add(new System.Windows.Documents.Run("     "));
            var link = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run(text)) { Foreground = (System.Windows.Media.Brush)FindResource("Cyan"), TextDecorations = null };
            link.Click += (_, _) => { try { Services.PlayService.OpenUrl(url); } catch { } };
            AboutLinks.Inlines.Add(link);
        }
        // 0.1.23: full license texts and the source of this version (Legal 25.11)
        AboutLinks.Inlines.Add(new System.Windows.Documents.Run("     "));
        var lic = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run("📄 " + Loc.T("lic.title"))) { Foreground = (System.Windows.Media.Brush)FindResource("Cyan"), TextDecorations = null };
        lic.Click += (_, _) => LicensesWindow.Open(Window.GetWindow(this));
        AboutLinks.Inlines.Add(lic);
    }

    private void Beta_Changed(object sender, RoutedEventArgs e)
    {
        _main.Settings.LauncherBeta = BetaCheck.IsChecked == true;
        _main.Settings.Save();
        Services.AppLog.Write("launcher beta: " + _main.Settings.LauncherBeta);
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
