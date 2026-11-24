// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Windows;
using System.Windows.Media;
using FunkyLauncher.Core;
using FunkyLauncher.Services;
using FunkyLauncher.Views;

namespace FunkyLauncher;

public partial class MainWindow : Window, IProUi
{
    public Settings Settings { get; } = Settings.Load();

    /// <summary>The modlist shown on Home / Install / Settings.</summary>
    public Modlist Selected => Catalog.Find(Settings.SelectedId) ?? Catalog.Items.First();

    private readonly HomeView _home;
    private readonly LibraryView _library;
    private readonly SetupView _setup;
    private readonly SoftView _soft;
    private readonly ToolsView _tools;
    private readonly ProView _proView;
    private readonly SettingsView _settingsView;

    public MainWindow()
    {
        InitializeComponent();
        if (string.IsNullOrEmpty(Settings.Language)) Settings.Language = Loc.DefaultLang();
        Loc.Set(Settings.Language);
        Catalog.LoadBuiltIn();
        // 0.1.19: a player back on the previous version keeps using that version's own lists
        Addons.InstalledListsDir = id => Catalog.Find(id) is { } ml ? Services.ModlistRollback.InstalledLists(Settings, ml) : null;
        Modlist.InstallDirOf = m => Settings.InstallDir(m);

        _home = new HomeView(this);
        _library = new LibraryView(this);
        _setup = new SetupView(this);
        _soft = new SoftView();
        _tools = new ToolsView(this);
        _proView = new ProView(this);
        _settingsView = new SettingsView(this);

        (Loc.Lang == "en" ? LangEn : LangRu).IsChecked = true;
        Loc.Changed += ApplyTexts;
        ApplyTexts();
        Page.Content = _home;
        _ = RefreshCatalogAsync();
        Activated += (_, _) => RefreshNexus(); // e.g. after logging in through Wabbajack
        // 0.1.19: the Nexus login (Wabbajack writes its file) is noticed by itself, whatever button started it and
        // even while the launcher window stays in the background — a File.Exists every 2 s costs nothing
        _nexusWatch = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _nexusWatch.Tick += (_, _) => WatchNexus();
        _nexusWatch.Start();
        RestoreWindow();
        Closing += (_, e) =>
        {
            if (!_shotsMode) SaveWindow();
            // the launcher closes for good (not to the tray): recording mode must not outlive it
            if ((!Settings.DesktopsOn || _exiting) && RecordingMode.IsActive)
            {
                try
                {
                    var t = new Thread(() => RecordingMode.Disable());
                    t.SetApartmentState(ApartmentState.STA);
                    t.Start();
                    t.Join(5000);
                }
                catch { }
            }
            // Funky Desktops keeps working in the tray when the window is closed
            if (Settings.DesktopsOn && !_exiting) { e.Cancel = true; Hide(); ShowTrayHintOnce(); }
        };
        ContentRendered += (_, _) =>
        {
            if (_shotsMode) return;
            // started by a modlist's small installer: open that modlist's install page right away
            var startArgs = Environment.GetCommandLineArgs();
            if (startArgs.Contains("--from-setup")) EnsureDesktopShortcut();
            if (OpenId(startArgs) is { } open) OpenModlist(open);
            ShowPrivacyNoticeOnce(); RemindRecordingMode(); RemindGameMode();        };
        SourceInitialized += (_, _) => RegisterShootHotkey();

        // developer test: FunkyLauncher.exe --test-manual N  → manual download of the N smallest Nexus files into %TEMP%
        var args = Environment.GetCommandLineArgs();
        // developer tool: FunkyLauncher.exe --shots [folder] → PNG of the main pages (RU + EN) for the design team, then closes
        SetupShots(args);
        // Funky Desktops is a test only for Funky: turned on once with --enable-desktops
        if (args.Contains("--enable-desktops") && !Settings.DesktopsBeta) { Settings.DesktopsBeta = true; Settings.Save(); }
        StartDesktops();
        ProModules.Ui = this;
        ProModules.Refresh(Settings); // Funky Pro: loads the module only with a valid key
        ProModules.ShowUi = ProModules.SalesOpen || args.Contains("--show-pro") || ProLicense.Active(Settings);
        var i = Array.IndexOf(args, "--test-manual");
        // "--test-manual N big" → N-1 smallest files + one file of about 500 MB (slow download test)
        // developer test: FunkyLauncher.exe --test-problem → shows the "game crashed" window
        // developer test: FunkyLauncher.exe --test-pccheck → opens Tools → "Can my PC run it?" and runs the check
        // developer test: FunkyLauncher.exe --test-gamemode → opens Tools → Game mode (nothing is closed)
        if (args.Contains("--test-gamemode"))
            ContentRendered += (_, _) => { NavTools.IsChecked = true; _tools.ShowGame(); };
        // developer test: FunkyLauncher.exe --test-player [file] → opens Funky Player (with the file)
        var pi = Array.IndexOf(args, "--test-player");
        if (pi >= 0)
            ContentRendered += (_, _) => { NavTools.IsChecked = true; _tools.OpenPlayer(pi + 1 < args.Length ? args[pi + 1] : null); };
        // developer test: FunkyLauncher.exe --test-desktops → opens Tools → Funky Desktops (changes nothing)
        if (args.Contains("--test-desktops"))
            ContentRendered += (_, _) => { NavTools.IsChecked = true; _tools.ShowDesktops(); };
        // developer test: FunkyLauncher.exe --test-pro → opens Tools → Funky Pro
        if (args.Contains("--test-pro"))
            ContentRendered += (_, _) => ShowProPage();
        // developer test: FunkyLauncher.exe --test-addon → opens the 18+ add-on window for the selected modlist (changes nothing by itself)
        if (args.Contains("--test-addon"))
            ContentRendered += (_, _) =>
            {
                NavLibrary.IsChecked = true;
                if (Addons.Adult(Selected.Id) is { } a) new AddonWindow(Settings, Selected, a) { Owner = this }.Show();
            };
        if (args.Contains("--test-pccheck"))
            ContentRendered += (_, _) => { NavTools.IsChecked = true; _tools.ShowPc(); _tools.RunPcCheck(); };
        // developer test: FunkyLauncher.exe --test-web → the built-in browser opens Nexus for 15 s, then says whether it worked
        // (0.1.17: run "as administrator" to check WebView2 with administrator rights)
        if (args.Contains("--test-web"))
            ContentRendered += async (_, _) =>
            {
                var bw = new BrowserWindow("https://www.nexusmods.com/") { Owner = this };
                bw.Show();
                await Task.Delay(15000);
                var result = $"admin token: {LowRights.IsAdminToken()}, WebView2 {WebProfile.RuntimeVersion() ?? "none"}, failed: {bw.WebFailed}, page: {bw.WebSource ?? "-"}";
                AppLog.Write("test-web: " + result);
                Dialog.Show(this, result, "--test-web", MessageBoxButton.OK, MessageBoxImage.Information);
            };
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

    /// <summary>Settings → privacy switches changed.</summary>
    public void OnPrivacyChanged()
    {
        _home.ReloadNews();
        if (Settings.UpdateCatalog) _ = RefreshCatalogAsync();
    }

    /// <summary>First start: tell what the launcher connects to and where to turn it off.</summary>
    private void ShowPrivacyNoticeOnce()
    {
        if (Settings.PrivacyNoticeShown) return;
        Views.Dialog.Show(this, Loc.T("privacy.notice"), Loc.T("settings.privacy"), MessageBoxButton.OK, MessageBoxImage.Information);
        Settings.PrivacyNoticeShown = true;
        Settings.Save();
    }

    private async Task RefreshCatalogAsync()
    {
        if (!Settings.UpdateCatalog) return; // privacy: use the cached / built-in catalog only
        if (await Catalog.RefreshAsync(CancellationToken.None))
        {
            _home.Refresh();
            _library.Refresh();
            _soft.Refresh();
            _tools.Refresh();
        }
        // language packs and 18+ lists: fixes arrive without a new launcher
        await Addons.RefreshOnlineAsync(Catalog.Items.Select(m => m.Id), CancellationToken.None);
    }

    public void ApplyTexts()
    {
        NavHome.Content = Loc.T("nav.home");
        NavLibrary.Content = Loc.T("nav.library");
        RefreshProNav();
        NavSoft.Content = Loc.T("nav.soft");
        RefreshToolsDot();
        NavSettings.Content = Loc.T("nav.settings");
        RefreshNexus();
    }

    private void SiteSide_Click(object sender, RoutedEventArgs e)
    {
        try { Services.PlayService.OpenUrl(SiteLinks.Home); } catch { }
    }

    private void SupportSide_Click(object sender, RoutedEventArgs e) =>
        PlayService.OpenSupportChat("", Settings, Selected);

    private readonly System.Windows.Threading.DispatcherTimer _nexusWatch;
    private bool? _nexusSeen;
    private bool _nexusAsked; // the three-step window was shown: a toast when the login arrives

    internal void NexusAsked() => _nexusAsked = true;

    /// <summary>The login file appeared or disappeared: the side panel, the library and the install checks follow.</summary>
    private void WatchNexus()
    {
        var ok = WabbajackService.IsNexusLoggedIn;
        if (_nexusSeen == ok) return;
        var first = _nexusSeen == null;
        _nexusSeen = ok;
        if (first) return; // the state at start is drawn by ApplyTexts
        RefreshNexus();
        if (Page.Content == _library) _library.Refresh();
        _setup.NexusChanged(ok);
        // the player is probably still in Wabbajack or the browser: tell them there, on top of everything
        if (ok && _nexusAsked) { _nexusAsked = false; Views.ScreenToast.Message(Loc.T("nexus.toast.done"), Loc.T("nexus.toast.done.text")); }
    }

    public void RefreshNexus()
    {
        SupportSideButton.Content = "💬 " + Loc.T("start.support.write");
        SiteSideButton.Content = "🌐 funkylauncher.com";
        SiteSideButton.ToolTip = Loc.T("site.tip");
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
            if (Views.Dialog.Show(this, Loc.T("nexus.logout.confirm"), Loc.T("nexus.logout.title"),
                    MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            try { WebProfile.LogOutNexus(); }
            catch (Exception ex) { Views.Dialog.Show(this, Loc.F("nexus.logout.error", ex.Message), "Funky Launcher"); }
        }
        else
        {
            if (!Views.NexusLoginWindow.Ask(this)) return;
            _nexusAsked = true;
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

    // ---- "--open <modlistId>": a modlist's small installer (PlayGround file) starts the launcher on that modlist
    private static string OpenRequestFile => Path.Combine(AppConfig.DataDir, "open-request.txt");

    private static string? OpenId(string[] args)
    {
        var i = Array.IndexOf(args, "--open");
        if (i < 0) i = Array.IndexOf(args, "--play"); // Funky Pro shortcut "straight into the game"
        if (i < 0 || i + 1 >= args.Length) return null;
        var id = args[i + 1].Trim();
        if (!args.Contains("--play")) return id;
        var p = Array.IndexOf(args, "--profile");
        return "play:" + id + (p >= 0 && p + 1 < args.Length ? "|" + args[p + 1].Trim() : "");
    }

    /// <summary>A second start while the launcher runs: leave the modlist id for the running window.</summary>
    public static void WriteOpenRequest(string[] args)
    {
        try { if (OpenId(args) is { Length: > 0 } id) { Directory.CreateDirectory(AppConfig.DataDir); File.WriteAllText(OpenRequestFile, id); } }
        catch { }
    }

    /// <summary>The running window was brought to the front: open the modlist a second start asked for.</summary>
    public void TakeOpenRequest()
    {
        try
        {
            if (!File.Exists(OpenRequestFile)) return;
            var id = File.ReadAllText(OpenRequestFile).Trim();
            File.Delete(OpenRequestFile);
            OpenModlist(id);
        }
        catch { }
    }

    /// <summary>Started by a modlist's small installer: the launcher lives in a data folder, so the player gets a desktop shortcut once.</summary>
    private static void EnsureDesktopShortcut()
    {
        try
        {
            var lnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Funky Launcher.lnk");
            if (File.Exists(lnk)) return;
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return;
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return;
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic sc = shell.CreateShortcut(lnk);
            sc.TargetPath = exe;
            sc.WorkingDirectory = Path.GetDirectoryName(exe);
            sc.IconLocation = exe + ",0";
            sc.Description = "Funky Launcher";
            sc.Save();
        }
        catch { }
    }

    private void OpenModlist(string id)
    {
        // "--play <id> [--profile <MO2 profile>]": a Funky Pro desktop shortcut; without an active Pro key it just opens the modlist
        if (id.StartsWith("play:"))
        {
            var parts = id[5..].Split('|', 2);
            if (Catalog.Find(parts[0]) is not { } pm) return;
            if (ProModules.Active && Settings.IsInstalled(pm)) { ((IProUi)this).Play(pm, parts.Length > 1 ? parts[1] : null); return; }
            id = parts[0];
        }
        if (Catalog.Find(id) is not { } m) return;
        Select(m);
        if (Settings.IsInstalled(m)) return; // installed: the library with PLAY is the right page
        ShowSetup();
    }

    /// <summary>Selects a modlist in the library.</summary>
    public void Select(Modlist m)
    {
        Settings.SelectedId = m.Id;
        Settings.Save();
        ProModules.RaiseSelectionChanged(Settings, m);
        _setup.ResetForModlist();
        ShowLibrary();
    }

    public void ShowSoft()
    {
        if (NavSoft.IsChecked == true) { _soft.Refresh(); Page.Content = _soft; }
        else NavSoft.IsChecked = true;
    }

    // ---------- Funky Desktops: per virtual desktop icons + tray ----------

    // Recording mode hotkey: Ctrl+Alt+F12 turns it on/off from anywhere (the launcher may be minimized or hidden).
    private const int ShootHotkeyId = 0xF00D;
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr h, int id);

    // Funky Screen: Ctrl+Shift+S area, Ctrl+Shift+F screen, Ctrl+Shift+R record (on/off in Tools → Funky Screen).
    private const int ScreenAreaId = 0xF00E, ScreenFullId = 0xF00F, ScreenRecId = 0xF010;
    private IntPtr _hwnd;

    /// <summary>Registers or removes the Funky Screen keys by the setting; returns false if another program already owns one.</summary>
    public bool ApplyScreenHotkeys()
    {
        if (_hwnd == IntPtr.Zero) return true;
        foreach (var id in new[] { ScreenAreaId, ScreenFullId, ScreenRecId }) UnregisterHotKey(_hwnd, id);
        if (!Settings.ScreenHotkeys) return true;
        const uint mods = 0x2 | 0x4 | 0x4000; // CONTROL | SHIFT | NOREPEAT
        var ok = RegisterHotKey(_hwnd, ScreenAreaId, mods, 0x53);
        ok &= RegisterHotKey(_hwnd, ScreenFullId, mods, 0x46);
        ok &= RegisterHotKey(_hwnd, ScreenRecId, mods, 0x52);
        return ok;
    }

    private void RegisterShootHotkey()
    {
        var src = System.Windows.Interop.HwndSource.FromHwnd(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        if (src == null) return;
        // MOD_ALT | MOD_CONTROL | MOD_NOREPEAT, VK_F12. If another program owns it, the button still works.
        RegisterHotKey(src.Handle, ShootHotkeyId, 0x1 | 0x2 | 0x4000, 0x7B);
        _hwnd = src.Handle;
        ApplyScreenHotkeys();
        foreach (var (id, k) in _proHotkeys.ToList())
            if (!RegisterHotKey(_hwnd, id, k.Mods, k.Vk)) _proHotkeys.Remove(id); // taken by another program
        src.AddHook((IntPtr hwnd, int msg, IntPtr wp, IntPtr lp, ref bool handled) =>
        {
            if (msg == 0x0312 && wp.ToInt32() is >= ScreenAreaId and <= ScreenRecId) // Funky Screen
            {
                switch (wp.ToInt32())
                {
                    case ScreenAreaId: _tools.ScreenArea(); break;
                    case ScreenFullId: _tools.ScreenFull(); break;
                    default: _tools.ScreenRecord(); break;
                }
                handled = true;
            }
            else if (msg == 0x0312 && ProModules.HotkeyPressed(wp.ToInt32())) handled = true; // Funky Pro hotkey
            else if (msg == 0x0312 && wp.ToInt32() == ShootHotkeyId) // WM_HOTKEY
            {
                var wasOn = RecordingMode.IsActive;
                _tools.ToggleShoot();
                if (wasOn)
                {
                    Show();
                    if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
                    Activate();
                }
                handled = true;
            }
            return IntPtr.Zero;
        });
    }

    private System.Windows.Forms.NotifyIcon? _tray;
    private System.Windows.Threading.DispatcherTimer? _deskTimer;
    private Guid? _lastDesk;
    private bool _exiting;

    /// <summary>Turns Funky Desktops on/off. Off always puts every icon back.</summary>
    public void SetDesktops(bool on)
    {
        Settings.DesktopsOn = on;
        Settings.Save();
        if (on) StartDesktops();
        else
        {
            _deskTimer?.Stop();
            _lastDesk = null;
            try { Services.Desktops.ShowAll(); } catch { }
            RebuildTray();
        }
        RefreshToolsDot();
    }

    /// <summary>Applies the icon set of the current desktop right away (after the player changed a checkbox).</summary>
    public void ApplyDesktopNow() { _lastDesk = null; DeskTick(); }

    /// <summary>The Pro key was added or removed.</summary>
    public void OnProChanged()
    {
        ProModules.Refresh(Settings);
        if (!ProModules.Active) { ((IProUi)this).SetBadge(null); ((IProUi)this).SetHomeBackground(null); } // Pro look goes with the key
        RefreshProNav();
        if (!ProModules.Active && NavPro.IsChecked == true) NavTools.IsChecked = true;
        _tools.Refresh();
    }

    private void StartDesktops()
    {
        if (!Settings.DesktopsOn)
        {
            // icons left hidden after a crash: put them back silently
            try { if (Services.Desktops.HasHidden) Services.Desktops.ShowAll(); } catch { }
            return;
        }
        RebuildTray();
        _deskTimer ??= new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _deskTimer.Tick -= DeskTimer_Tick;
        _deskTimer.Tick += DeskTimer_Tick;
        _deskTimer.Start();
        DeskTick();
    }

    private void DeskTimer_Tick(object? sender, EventArgs e) => DeskTick();

    private void DeskTick()
    {
        try
        {
            var cur = Services.Desktops.Current();
            if (cur == null || cur == _lastDesk) return;
            _lastDesk = cur;
            Settings.DesktopHide.TryGetValue(cur.Value.ToString(), out var hide);
            Services.Desktops.Apply(hide ?? new List<string>());
        }
        catch { /* never break the launcher because of the desktop */ }
    }

    /// <summary>The tray icon: shown while Funky Desktops is on or Funky Pro has tray items; the menu is built again each time.</summary>
    private bool _trayExitHooked;
    private void RebuildTray()
    {
        var pro = ProModules.TrayItems;
        if (!Settings.DesktopsOn && pro.Count == 0) { _tray?.Dispose(); _tray = null; return; }
        if (_tray == null)
        {
            System.Drawing.Icon? icon = null;
            try { using var s = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/launcher.ico"))!.Stream; icon = new System.Drawing.Icon(s); } catch { }
            _tray = new System.Windows.Forms.NotifyIcon { Icon = icon ?? System.Drawing.SystemIcons.Application, Visible = true };
            _tray.DoubleClick += (_, _) => ShowFromTray();
            if (!_trayExitHooked) { _trayExitHooked = true; Application.Current.Exit += (_, _) => { _tray?.Dispose(); _tray = null; }; } // no ghost icon
        }
        _tray.Text = Settings.DesktopsOn ? "Funky Launcher · Desktops" : "Funky Launcher";
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add(Loc.T("tray.open"), null, (_, _) => ShowFromTray());
        foreach (var (_, text, click) in pro)
            menu.Items.Add(text, null, (_, _) => Dispatcher.Invoke(() => { try { click(); } catch { /* Pro item */ } }));
        if (Settings.DesktopsOn)
            menu.Items.Add(Loc.T("tray.showall"), null, (_, _) => Dispatcher.Invoke(() => { SetDesktops(false); _tools.Refresh(); }));
        menu.Items.Add(Loc.T("tray.exit"), null, (_, _) => Dispatcher.Invoke(ExitApp));
        var old = _tray.ContextMenuStrip;
        _tray.ContextMenuStrip = menu;
        old?.Dispose();
    }

    // ---------- Funky Pro doors (IProUi) ----------

    void IProUi.Play(Modlist m, string? mo2Profile)
    {
        Select(m);
        if (Settings.IsInstalled(m)) _library.Play(mo2Profile);
    }

    /// <summary>"Funky Pro" in the menu (under Library) — only with a valid key and the module loaded.</summary>
    private void RefreshProNav()
    {
        NavPro.Visibility = ProModules.Use != null ? Visibility.Visible : Visibility.Collapsed;
        if (NavPro.Content == null) NavPro.Content = ProView.Title(15, true);
    }

    /// <summary>Opens the Funky Pro page (toolId = one tool, null = all).</summary>
    public void ShowProPage(string? toolId = null)
    {
        if (ProModules.Use == null) { NavTools.IsChecked = true; return; }
        if (NavPro.IsChecked != true) NavPro.IsChecked = true;
        _proView.Show(toolId);
        Page.Content = _proView;
    }

    void IProUi.RefreshPages()
    {
        if (Page.Content == _proView) _proView.Refresh();
        _library.Refresh();
        _tools.Refresh();
        _settingsView.Refresh();
    }

    void IProUi.TrayItemsChanged() => RebuildTray();

    void IProUi.SetHomeBackground(string? file) { try { _home.SetHeroBackground(file); } catch { /* a broken file never breaks the page */ } }

    void IProUi.SetBadge(string? text)
    {
        ProBadge.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        ProBadgeText.Text = text ?? "";
    }

    // Pro hotkeys asked for before the window handle exists are registered when it appears
    private readonly Dictionary<int, (uint Mods, uint Vk)> _proHotkeys = new();

    bool IProUi.RegisterHotkey(int id, uint mods, uint vk)
    {
        if (_hwnd != IntPtr.Zero && !RegisterHotKey(_hwnd, id, mods, vk)) return false;
        _proHotkeys[id] = (mods, vk);
        return true;
    }

    void IProUi.UnregisterHotkey(int id)
    {
        _proHotkeys.Remove(id);
        if (_hwnd != IntPtr.Zero) UnregisterHotKey(_hwnd, id);
    }

    private void ShowFromTray() => Dispatcher.Invoke(() => { Show(); if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal; Activate(); });

    private bool _trayHintShown;
    private void ShowTrayHintOnce()
    {
        if (_trayHintShown || _tray == null) return;
        _trayHintShown = true;
        _tray.ShowBalloonTip(4000, "Funky Desktops", Loc.T("tray.hint"), System.Windows.Forms.ToolTipIcon.Info);
    }

    /// <summary>Real exit (from the tray): icons back, tray gone.</summary>
    private void ExitApp()
    {
        _exiting = true;
        try { Services.Desktops.ShowAll(); } catch { }
        _tray?.Dispose(); _tray = null;
        Application.Current.Shutdown();
    }

    /// <summary>"Tools ●" while recording mode is on — something on the PC is changed.</summary>
    public void RefreshToolsDot() =>
        NavTools.Content = Loc.T("nav.tools") + (RecordingMode.IsActive || GameMode.IsActive || Settings.DesktopsOn ? "  ●" : "");

    /// <summary>Game mode was left on (launcher closed or crashed during the game): offer to start the programs again.</summary>
    private void RemindGameMode()
    {
        var st = GameMode.Load();
        if (st?.Active != true) return;
        if (Views.Dialog.Show(this, Loc.F("game.reminder", ToolsView.When(st.Started)), Loc.T("tools.game"),
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        NavTools.IsChecked = true;
        _tools.ShowGame();
        _tools.TurnOffGame();
    }

    /// <summary>Recording mode was left on (launcher closed, PC restarted): offer to put everything back.</summary>
    private void RemindRecordingMode()
    {
        var st = RecordingMode.Load();
        if (st?.Active != true) return;
        if (Views.Dialog.Show(this, Loc.F("shoot.reminder", ToolsView.When(st.Started)), Loc.T("tools.shoot"),
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) { ModeGuard.Start(); return; } // kept on: still put back if the launcher closes
        NavTools.IsChecked = true;
        _tools.ShowShoot();
        _tools.TurnOffShoot();
    }

    /// <summary>Tools page (save backup) — e.g. from the backup line in the library.</summary>
    public void ShowTools()
    {
        if (NavTools.IsChecked == true) { _tools.Refresh(); Page.Content = _tools; }
        else NavTools.IsChecked = true;
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
        var target = sender == NavHome ? (object)_home : sender == NavLibrary ? _library : sender == NavSoft ? _soft : sender == NavTools ? _tools : sender == NavPro ? _proView : _settingsView;
        if (sender == NavPro && ReferenceEquals(Page.Content, _proView)) { _proView.Show(null); return; } // the menu item again → all Pro tools
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
        else if (sender == NavTools) { _tools.Refresh(); Page.Content = _tools; }
        else if (sender == NavPro) { _proView.Refresh(); Page.Content = _proView; }
        else if (sender == NavSettings) { _settingsView.Refresh(); Page.Content = _settingsView; }
    }

    private void Lang_Checked(object sender, RoutedEventArgs e)
    {
        var lang = sender == LangEn ? "en" : "ru";
        if (_shotsMode) return; // pictures for the design team: the language is switched by the shots code, nothing is saved
        if (Loc.Lang == lang && Settings.Language == lang) return;
        Settings.Language = lang;
        Settings.Save();
        Loc.Set(lang);
    }
}
