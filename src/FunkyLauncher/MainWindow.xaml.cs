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

    /// <summary>The modlist shown on Home / Install / Settings.</summary>
    public Modlist Selected => Catalog.Find(Settings.SelectedId) ?? Catalog.Items.First();

    private readonly HomeView _home;
    private readonly LibraryView _library;
    private readonly SetupView _setup;
    private readonly SoftView _soft;
    private readonly SettingsView _settingsView;

    public MainWindow()
    {
        InitializeComponent();
        if (string.IsNullOrEmpty(Settings.Language)) Settings.Language = Loc.DefaultLang();
        Loc.Set(Settings.Language);
        Catalog.LoadBuiltIn();

        _home = new HomeView(this);
        _library = new LibraryView(this);
        _setup = new SetupView(this);
        _soft = new SoftView();
        _settingsView = new SettingsView(this);

        (Loc.Lang == "en" ? LangEn : LangRu).IsChecked = true;
        Loc.Changed += ApplyTexts;
        ApplyTexts();
        Page.Content = _home;
        _ = RefreshCatalogAsync();
        Activated += (_, _) => RefreshNexus(); // e.g. after logging in through Wabbajack
        RestoreWindow();
        Closing += (_, _) => SaveWindow();

        // developer test: FunkyLauncher.exe --test-manual N  → manual download of the N smallest Nexus files into %TEMP%
        var args = Environment.GetCommandLineArgs();
        var i = Array.IndexOf(args, "--test-manual");
        // "--test-manual N big" → N-1 smallest files + one file of about 500 MB (slow download test)
        // developer test: FunkyLauncher.exe --test-problem → shows the "game crashed" window
        if (args.Contains("--test-problem"))
            Loaded += (_, _) => new LaunchProblemWindow(new LaunchReport(LaunchResult.Crashed, TimeSpan.FromMinutes(7), @"C:\Games\NeonNights\overwrite\red4ext\...\crash-test.txt"), Settings.PreviousOf(Selected)) { Owner = this }.ShowDialog();
        if (i >= 0) Loaded += (_, _) => TestManual(i + 1 < args.Length && int.TryParse(args[i + 1], out var n) ? n : 2,
                                                   args.Contains("big"));
    }

    /// <summary>Opens where it was closed last time, if that place is still on a screen.</summary>
    private void RestoreWindow()
    {
        var s = Settings;
        if (s.WindowWidth is double w && s.WindowHeight is double h && s.WindowLeft is double l && s.WindowTop is double t)
        {
            var visible = l + 100 < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth
                          && t + 50 < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight
                          && l + w - 100 > SystemParameters.VirtualScreenLeft && t >= SystemParameters.VirtualScreenTop - 10;
            if (visible)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = l; Top = t;
                Width = Math.Max(MinWidth, w); Height = Math.Max(MinHeight, h);
            }
        }
        if (s.WindowMaximized) WindowState = WindowState.Maximized;
    }

    private void SaveWindow()
    {
        var r = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        Settings.WindowLeft = r.Left; Settings.WindowTop = r.Top;
        Settings.WindowWidth = r.Width; Settings.WindowHeight = r.Height;
        Settings.WindowMaximized = WindowState == WindowState.Maximized;
        Settings.Save();
    }

    private void TestManual(int count, bool big)
    {
        var file = Settings.ResolveWabbajack(Selected);
        if (file == null) return;
        var dir = Path.Combine(Path.GetTempPath(), "FunkyManualTest");
        var all = ManualQueue.Build(file, dir).OrderBy(x => x.Size).ToList();
        var items = all.Take(big ? count - 1 : count).ToList();
        if (big && all.Count > 0) items.Add(all.OrderBy(x => Math.Abs(x.Size - 500L * 1024 * 1024)).First());
        new ManualDownloadWindow(items, dir) { Owner = this }.ShowDialog();
    }

    private async Task RefreshCatalogAsync()
    {
        if (await Catalog.RefreshAsync(CancellationToken.None))
        {
            _home.Refresh();
            _library.Refresh();
            _soft.Refresh();
        }
    }

    public void ApplyTexts()
    {
        NavHome.Content = Loc.T("nav.home");
        NavLibrary.Content = Loc.T("nav.library");
        NavSoft.Content = Loc.T("nav.soft");
        NavSettings.Content = Loc.T("nav.settings");
        RefreshNexus();
    }

    public void RefreshNexus()
    {
        var ok = WabbajackService.IsNexusLoggedIn;
        NexusStatus.Text = "● " + Loc.T(ok ? "check.nexus.ok" : "check.nexus.no");
        NexusStatus.Foreground = (Brush)FindResource(ok ? "Cyan" : "Pink");
        NexusAction.Text = Loc.T(ok ? "nexus.logout" : "nexus.login");
    }

    private BrowserWindow? _browser;

    private void Browser_Click(object sender, RoutedEventArgs e)
    {
        if (_browser is { IsLoaded: true }) { _browser.Activate(); return; }
        _browser = new BrowserWindow(AppConfig.BrowserHome) { Owner = this };
        _browser.Show();
    }

    private void Nexus_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (WabbajackService.IsNexusLoggedIn)
        {
            if (MessageBox.Show(this, Loc.T("nexus.logout.confirm"), Loc.T("nexus.logout.title"),
                    MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            try { WebProfile.LogOutNexus(); }
            catch (Exception ex) { MessageBox.Show(this, Loc.F("nexus.logout.error", ex.Message), "Funky Launcher"); }
        }
        else
        {
            if (MessageBox.Show(this, Loc.T("nexus.login.info"), Loc.T("nexus.login"),
                    MessageBoxButton.OKCancel, MessageBoxImage.Information) != MessageBoxResult.OK) return;
            if (WabbajackService.FindGui() == null)
            {
                // a new player: Wabbajack is downloaded first, with progress on the install page
                ShowSetup();
                _setup.StartNexusLogin();
                return;
            }
            PlayService.OpenWabbajackGui();
        }
        RefreshNexus();
        _library.Refresh();
    }

    /// <summary>Selects a modlist in the library.</summary>
    public void Select(Modlist m)
    {
        Settings.SelectedId = m.Id;
        Settings.Save();
        _setup.ResetForModlist();
        ShowLibrary();
    }

    public void ShowSoft()
    {
        if (NavSoft.IsChecked == true) { _soft.Refresh(); Page.Content = _soft; }
        else NavSoft.IsChecked = true;
    }

    public void ShowLibrary()
    {
        if (NavLibrary.IsChecked == true) { _library.Refresh(); Page.Content = _library; }
        else NavLibrary.IsChecked = true;
    }

    /// <summary>Install / verify page of the selected modlist (opened from the library).</summary>
    public void ShowSetup(bool startVerify = false)
    {
        if (NavLibrary.IsChecked != true) NavLibrary.IsChecked = true;
        Page.Content = _setup;
        if (startVerify) _setup.StartRepair(); else _setup.OnShown();
    }

    /// <summary>
    /// A click on the already selected menu item (e.g. "Library" while the install page is open inside it)
    /// does not raise Checked — show that page anyway.
    /// </summary>
    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        var target = sender == NavHome ? (object)_home : sender == NavLibrary ? _library : sender == NavSoft ? _soft : _settingsView;
        if (!ReferenceEquals(Page.Content, target)) Nav_Checked(sender, e);
    }

    /// <summary>PLAY from the install page: go to the library and start the game the usual way (with all checks).</summary>
    public void PlaySelected()
    {
        ShowLibrary();
        _library.Play();
    }

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (Page == null) return;
        if (sender == NavHome) { _home.Refresh(); Page.Content = _home; }
        else if (sender == NavLibrary) { _library.Refresh(); Page.Content = _library; }
        else if (sender == NavSoft) { _soft.Refresh(); Page.Content = _soft; }
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
