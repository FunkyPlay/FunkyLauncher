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
    }

    // ---------- Funky Pro key ----------

    private void RefreshPro()
    {
        ProLabel.Text = "Funky Pro";
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

    private void ProRemove_Click(object sender, RoutedEventArgs e)
    {
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
