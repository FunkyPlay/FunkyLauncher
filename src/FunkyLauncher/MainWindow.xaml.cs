// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Windows;
using System.Windows.Media;
using FunkyLauncher.Core;
using FunkyLauncher.Services;
using FunkyLauncher.Views;

namespace FunkyLauncher;

public partial class MainWindow : Window
{
    public Settings Settings { get; } = Settings.Load();

    private readonly HomeView _home;
    private readonly SetupView _setup;
    private readonly SettingsView _settingsView;

    public MainWindow()
    {
        InitializeComponent();
        if (string.IsNullOrEmpty(Settings.Language)) Settings.Language = Loc.DefaultLang();
        Loc.Set(Settings.Language);

        _home = new HomeView(this);
        _setup = new SetupView(this);
        _settingsView = new SettingsView(this);

        (Loc.Lang == "en" ? LangEn : LangRu).IsChecked = true;
        Loc.Changed += ApplyTexts;
        ApplyTexts();
        Page.Content = _home;
    }

    public void ApplyTexts()
    {
        NavHome.Content = Loc.T("nav.home");
        NavSetup.Content = Loc.T("nav.setup");
        NavSettings.Content = Loc.T("nav.settings");
        RefreshNexus();
    }

    public void RefreshNexus()
    {
        var ok = WabbajackService.IsNexusLoggedIn;
        NexusStatus.Text = ok ? "● " + Loc.T("check.nexus.ok") : "● " + Loc.T("check.nexus.no");
        NexusStatus.Foreground = (Brush)FindResource(ok ? "Cyan" : "Pink");
    }

    public void ShowHome() { NavHome.IsChecked = true; }
    public void ShowSetup() { NavSetup.IsChecked = true; }

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (Page == null) return;
        if (sender == NavHome) { _home.Refresh(); Page.Content = _home; }
        else if (sender == NavSetup) { Page.Content = _setup; _setup.OnShown(); }
        else if (sender == NavSettings) { _settingsView.Refresh(); Page.Content = _settingsView; }
    }

    private void Lang_Checked(object sender, RoutedEventArgs e)
    {
        var lang = sender == LangEn ? "en" : "ru";
        if (Loc.Lang == lang && Settings.Language == lang) return;
        Settings.Language = lang;
        Settings.Save();
        Loc.Set(lang);
    }
}
