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
        AboutLabel.Text = Loc.T("settings.about");
        var ver = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0";
        AboutText.Text = Loc.F("settings.about.text", ver, Catalog.Source);
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
