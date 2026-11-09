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
        PageTitle.Text = Loc.T("settings.title");
        ModlistLabel.Text = Loc.T("settings.modlist");
        ModlistBrowse.Content = Loc.T("setup.browse");
        ModlistBox.Text = _main.Settings.ResolveModlist() ?? "—";
        AboutLabel.Text = Loc.T("settings.about");
        var ver = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0";
        AboutText.Text = Loc.F("settings.about.text", ver);
    }

    private void ModlistBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Wabbajack (*.wabbajack)|*.wabbajack" };
        if (dlg.ShowDialog() != true) return;
        _main.Settings.ModlistPath = dlg.FileName;
        _main.Settings.Save();
        Refresh();
    }
}
