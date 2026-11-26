// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FunkyLauncher.Services;

namespace FunkyLauncher;

/// <summary>
/// Developer tool for the design team: FunkyLauncher.exe --shots [folder]
/// Saves a PNG of the main pages (RU and EN), then closes. The window is drawn off-screen, nothing is clicked,
/// window position is not saved. Use a separate data folder (FUNKYLAUNCHER_DATA) to show a prepared state.
/// </summary>
public partial class MainWindow
{
    private bool _shotsMode;
    // FUNKYLAUNCHER_SHOTS_PX=2560x1440: exact picture size for video. The layout stays 1280 wide (as on a usual screen),
    // the picture is drawn larger; the base pages get the given height, the "full" pages stay taller.
    private double _shotScale, _shotBaseHeight = 800;
    public bool ShotsMode => _shotsMode;

    /// <summary>The new version is in place and started: close this one for good (not to the tray).</summary>
    public void ExitForUpdate()
    {
        _exiting = true;
        Application.Current.Shutdown();
    }

    private void SetupShots(string[] args)
    {
        var k = Array.IndexOf(args, "--shots");
        if (k < 0) return;
        _shotsMode = true;
        var dir = k + 1 < args.Length && !args[k + 1].StartsWith("--")
            ? args[k + 1]
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Funky Launcher shots", DateTime.Now.ToString("yyyy-MM-dd_HH-mm"));
        Directory.CreateDirectory(dir);
        WindowState = WindowState.Normal;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Width = 1280; Height = 800;
        Left = -30000; Top = -30000;
        ShowInTaskbar = false;
        ShowActivated = false;
        var px = (Environment.GetEnvironmentVariable("FUNKYLAUNCHER_SHOTS_PX") ?? "").Split('x');
        if (px.Length == 2 && int.TryParse(px[0], out var pw) && int.TryParse(px[1], out var ph) && pw >= 1280 && ph >= 700)
        {
            _shotScale = pw / 1280.0;
            _shotBaseHeight = Math.Max(700, ph / _shotScale);
        }
        ContentRendered += async (_, _) => await TakeShotsAsync(dir);
    }

    private async Task TakeShotsAsync(string dir)
    {
        var startLang = Core.Loc.Lang;
        // the window frame takes a few pixels: with an exact picture size the content itself must be 1280 wide
        double dw = 0, dh = 0;
        if (_shotScale > 0 && Content is FrameworkElement root0)
        {
            root0.UpdateLayout();
            dw = Width - root0.ActualWidth; dh = Height - root0.ActualHeight;
            Width = 1280 + dw;
        }
        if (Environment.GetEnvironmentVariable("FUNKYLAUNCHER_SHOTS_GUIDE") == "1")
        {
            try { await GuideShotsAsync(dir, dh); }
            finally
            {
                Core.Loc.Set(startLang);
                _exiting = true;
                Application.Current.Shutdown();
            }
            return;
        }
        try
        {
            foreach (var lang in new[] { "ru", "en" })
            {
                Core.Loc.Set(lang);
                (lang == "en" ? LangEn : LangRu).IsChecked = true; // the RU/EN switch in the side column
                var pages = new (string Name, Action Show, double Height)[]
                {
                    ("01-home", () => NavHome.IsChecked = true, 800),
                    ("02-home-full", () => NavHome.IsChecked = true, 1240),
                    ("03-library", () => { NavHome.IsChecked = true; NavLibrary.IsChecked = true; }, 800),
                    ("03b-library-more", () => { NavHome.IsChecked = true; NavLibrary.IsChecked = true; _library.ShowDetails(); }, 800),
                    ("04-setup", () => ShowSetup(), 800),
                    ("05-setup-full", () => ShowSetup(), 1240),
                    ("06-soft", () => NavSoft.IsChecked = true, 800),
                    ("07-tools", () => { NavTools.IsChecked = true; _tools.Refresh(); }, 800),
                    ("08-settings", () => NavSettings.IsChecked = true, 800),
                    ("09-tools-backup", () => { NavTools.IsChecked = true; _tools.ShowBackup(); }, 1000),
                    ("10-tools-shoot", () => { NavTools.IsChecked = true; _tools.ShowShoot(); }, 1000),
                    ("11-tools-game", () => { NavTools.IsChecked = true; _tools.ShowGame(); }, 1000),
                    ("12-tools-pc", () => { NavTools.IsChecked = true; _tools.ShowPc(); _tools.RunPcCheck(); }, 1100),
                }.ToList();
                if (ProModules.Active) // Funky Pro pages: only with a Pro key (developer data folder)
                {
                    pages.Add(("15-pro", () => ShowProPage(), 800));
                    pages.Add(("15b-pro-full", () => ShowProPage(), 2600));
                    pages.Add(("15c-pro-bottom", () => { ShowProPage(); _proView.ScrollToBottom(); }, 1300));
                    pages.Add(("16-pro-main", () => ShowProPage("main"), 1000));
                    foreach (var p in ProModules.Panels(Core.ProPlace.Tools))
                        pages.Add(("16-pro-" + p.Id, () => ShowProPage(p.Id), 1000));
                    pages.Add(("17-pro-settings", () => NavSettings.IsChecked = true, 1600));
                }
                foreach (var (name, show, height) in pages)
                {
                    try
                    {
                        Height = (height == 800 ? _shotBaseHeight : height) + dh;
                        show();
                        await Task.Delay(name.EndsWith("-pc") ? 15000 : 3000); // news, checks and images load asynchronously
                        Save(Path.Combine(dir, $"{lang}-{name}.png"));
                    }
                    catch (Exception e) { File.AppendAllText(Path.Combine(dir, "errors.txt"), $"{lang}-{name}: {e}\n"); }
                }
                Height = _shotBaseHeight + dh;
                // 0.1.22: the home page in the Funky Pro themes (accent colours as Looks.cs sets them), then back
                if (lang == "ru")
                {
                    var res = Application.Current.Resources;
                    var keep = new[] { "Pink", "PinkSoft", "Cyan" }.ToDictionary(k => k, k => res[k]);
                    foreach (var (tname, pink, soft, cyan) in new[] { ("sunset", "#FF8A3C", "#40210E", "#FFD166"), ("wasteland", "#E8C547", "#3A300F", "#7CE38B"), ("ocean", "#3C9DFF", "#10294A", "#5FF0D0"), ("ruby", "#FF3B5C", "#3F0F1A", "#FFC857"), ("cosmos", "#8B7BFF", "#221C4A", "#7AE8FF") })
                        try
                        {
                            res["Pink"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(pink));
                            res["PinkSoft"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(soft));
                            res["Cyan"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(cyan));
                            NavHome.IsChecked = true;
                            _home.Refresh();
                            await Task.Delay(1500);
                            Save(Path.Combine(dir, $"theme-{tname}-home.png"));
                        }
                        catch (Exception e) { File.AppendAllText(Path.Combine(dir, "errors.txt"), $"theme {tname}: {e}\n"); }
                    foreach (var (k, v) in keep) res[k] = v;
                    _home.Refresh();
                }
                // the "new version" window, with a sample release (what players will see at the next update)
                try
                {
                    var sample = new Services.LauncherUpdate.Release("0.1.9",
                        lang == "ru" ? "Что нового\n• Starfield: сборка в один клик\n• Funky Скрин: снимки и видео экрана\n• Установка программ в один клик"
                                     : "What's new\n• Starfield: one-click modlist\n• Funky Screen: screenshots and screen video\n• One-click program installs",
                        null, null);
                    // drawn without showing the window: its content is laid out at the window's width and saved
                    var uw = new Views.UpdateWindow(this, sample);
                    var body = (FrameworkElement)uw.Content;
                    uw.Content = null;
                    var holder = new System.Windows.Controls.Border { Background = uw.Background, Child = body, Width = uw.Width - 16 };
                    holder.Measure(new Size(holder.Width, double.PositiveInfinity));
                    holder.Arrange(new Rect(holder.DesiredSize));
                    holder.UpdateLayout();
                    await Task.Delay(300);
                    SaveElement(holder, Path.Combine(dir, $"{lang}-13-update-window.png"));
                }
                catch (Exception e) { File.AppendAllText(Path.Combine(dir, "errors.txt"), $"{lang}-update: {e}\n"); }
                // "Report a flaw" (0.1.15): the form and the thank-you screen
                try
                {
                    if (Views.FlawWindow.Target(Settings, Selected) is { } fm)
                    {
                        var (form, thanks, bg, fw) = Views.FlawWindow.ForShots(Settings, fm);
                        var (mform, mthanks, _, _) = Views.FlawWindow.ForShots(Settings, fm, "mod");
                        foreach (var (el, nm) in new[] { (form, "18-flaw-window"), (thanks, "18b-flaw-thanks"), (mform, "18c-mod-window"), (mthanks, "18d-mod-thanks") })
                        {
                            var holder = new System.Windows.Controls.Border { Background = bg, Child = el, Width = fw - 16 };
                            holder.Measure(new Size(holder.Width, double.PositiveInfinity));
                            holder.Arrange(new Rect(holder.DesiredSize));
                            holder.UpdateLayout();
                            await Task.Delay(300);
                            SaveElement(holder, Path.Combine(dir, $"{lang}-{nm}.png"));
                        }
                    }
                }
                catch (Exception e) { File.AppendAllText(Path.Combine(dir, "errors.txt"), $"{lang}-flaw: {e}\n"); }
                // 0.1.16: "How to install REDmod"
                try
                {
                    if (Core.Catalog.Find("neon-nights") is { } nn)
                    {
                        var tool = nn.RequiredTools?.FirstOrDefault() ?? new Core.RequiredTool { Name = new Core.LocText { Ru = "REDmod", En = "REDmod" } };
                        var (hb, hbg, hw) = Views.ToolHelpWindow.ForShots(tool, nn);
                        var holder = new System.Windows.Controls.Border { Background = hbg, Child = hb, Width = hw - 16 };
                        holder.Measure(new Size(holder.Width, double.PositiveInfinity));
                        holder.Arrange(new Rect(holder.DesiredSize));
                        holder.UpdateLayout();
                        await Task.Delay(300);
                        SaveElement(holder, Path.Combine(dir, $"{lang}-19-tool-help.png"));
                        // 0.1.22: "Mods of the modlist" (Neon Nights, the real base list)
                        var (mb, mbg, mw, mh) = Views.ModsWindow.ForShots(Settings, nn);
                        var mholder = new System.Windows.Controls.Border { Background = mbg, Child = mb, Width = mw - 16, Height = mh - 40 };
                        mholder.Measure(new Size(mholder.Width, mholder.Height));
                        mholder.Arrange(new Rect(mholder.DesiredSize));
                        mholder.UpdateLayout();
                        await Task.Delay(300);
                        SaveElement(mholder, Path.Combine(dir, $"{lang}-30-mods-window.png"));
                        foreach (var other in new[] { "starfield", "fallout4" })
                            if (Core.Catalog.Find(other) is { } om && Services.ModChoices.Items(Settings, om).Count > 0)
                            {
                                var (ob, obg, ow, oh) = Views.ModsWindow.ForShots(Settings, om);
                                var oholder = new System.Windows.Controls.Border { Background = obg, Child = ob, Width = ow - 16, Height = oh - 40 };
                                oholder.Measure(new Size(oholder.Width, oholder.Height));
                                oholder.Arrange(new Rect(oholder.DesiredSize));
                                oholder.UpdateLayout();
                                await Task.Delay(300);
                                SaveElement(oholder, Path.Combine(dir, $"{lang}-30-mods-window-{other}.png"));
                            }
                        // 0.1.22: the disk space estimate of every modlist (install / update), for checking the numbers
                        if (lang == "ru")
                        {
                            var lines = new List<string>();
                            foreach (var dm in Core.Catalog.Items)
                                foreach (var upd in new[] { false, true })
                                    lines.Add($"{dm.Id} update={upd}: " + string.Join(", ", Services.DiskSpace.Needs(Settings, dm, update: upd)
                                        .Select(x => $"{x.Key} {x.Value / (1L << 30)} GB")) + $" | short: {Services.DiskSpace.Describe(Services.DiskSpace.Check(Settings, dm, update: upd))}");
                            File.WriteAllLines(Path.Combine(dir, "disk-space.txt"), lines);
                        }
                        // the red check row as the player without REDmod sees it (test data: this PC may have REDmod)
                        var redRow = new System.Windows.Controls.Border { Background = (Brush)FindResource("Bg"), Padding = new Thickness(20), Width = 820, Child = _setup.ShotToolRow(tool) };
                        redRow.Measure(new Size(redRow.Width, double.PositiveInfinity));
                        redRow.Arrange(new Rect(redRow.DesiredSize));
                        redRow.UpdateLayout();
                        await Task.Delay(300);
                        SaveElement(redRow, Path.Combine(dir, $"{lang}-19b-tool-missing.png"));
                        // 0.1.18: "Run as administrator" on the game exe (error 740) — the check row and the question before PLAY.
                        // Test data: a neutral game folder, no store name (rule 20.11)
                        var exe = Path.Combine(@"D:\Games\Cyberpunk 2077", nn.GameExePath);
                        var adminRow = new System.Windows.Controls.Border { Background = (Brush)FindResource("Bg"), Padding = new Thickness(20), Width = 820, Child = _setup.ShotAdminRow(exe, nn.GameExe) };
                        adminRow.Measure(new Size(adminRow.Width, double.PositiveInfinity));
                        adminRow.Arrange(new Rect(adminRow.DesiredSize));
                        adminRow.UpdateLayout();
                        await Task.Delay(300);
                        SaveElement(adminRow, Path.Combine(dir, $"{lang}-22-admin-row.png"));
                        var ask = Views.Dialog.ForShots(Core.Loc.F("admin.flag.text", nn.GameExe), Core.Loc.F("admin.flag.title", nn.GameExe),
                            MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.Yes);
                        ask.Measure(new Size(ask.Width, double.PositiveInfinity));
                        ask.Arrange(new Rect(ask.DesiredSize));
                        ask.UpdateLayout();
                        await Task.Delay(300);
                        SaveElement(ask, Path.Combine(dir, $"{lang}-22b-admin-ask.png"));
                    }
                    // 0.1.19: the red "game not found" row with "Show the game folder" (a Nexus-list modlist: Starfield)
                    if (Core.Catalog.Find("starfield") is { } sf)
                    {
                        var gameRow = new System.Windows.Controls.Border { Background = (Brush)FindResource("Bg"), Padding = new Thickness(20), Width = 820, Child = _setup.ShotGameMissingRow(sf) };
                        gameRow.Measure(new Size(gameRow.Width, double.PositiveInfinity));
                        gameRow.Arrange(new Rect(gameRow.DesiredSize));
                        gameRow.UpdateLayout();
                        await Task.Delay(300);
                        SaveElement(gameRow, Path.Combine(dir, $"{lang}-23-game-missing.png"));
                        var copies = new System.Windows.Controls.Border { Background = (Brush)FindResource("Bg"), Padding = new Thickness(20), Width = 820, Child = _setup.ShotCopiesRow(sf) };
                        copies.Measure(new Size(copies.Width, double.PositiveInfinity));
                        copies.Arrange(new Rect(copies.DesiredSize));
                        copies.UpdateLayout();
                        await Task.Delay(300);
                        SaveElement(copies, Path.Combine(dir, $"{lang}-23b-game-copies.png"));
                        var warn = Views.Dialog.ForShots(Core.Loc.F("files.warn.text", sf.GameVersion, "ShatteredSpace.esm", sf.Game), Core.Loc.T("files.warn.title"),
                            MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No, Core.Loc.T("files.warn.go"), Core.Loc.T("files.warn.cancel"));
                        warn.Measure(new Size(warn.Width, double.PositiveInfinity));
                        warn.Arrange(new Rect(warn.DesiredSize));
                        warn.UpdateLayout();
                        await Task.Delay(300);
                        SaveElement(warn, Path.Combine(dir, $"{lang}-26-game-files-ask.png"));
                    }
                    // 0.1.19: the three-step Nexus login window (before Wabbajack opens)
                    {
                        var nx = Views.NexusLoginWindow.ForShots();
                        nx.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                        nx.Arrange(new Rect(nx.DesiredSize));
                        nx.UpdateLayout();
                        await Task.Delay(300);
                        SaveElement(nx, Path.Combine(dir, $"{lang}-27-nexus-login-steps.png"));
                    }
                    // 0.1.19: the side panel notices a Nexus login by itself (a throwaway login file, the real one is not touched)
                    {
                        var fake = Path.Combine(dir, "nexus-login-test");
                        File.Delete(fake);
                        WabbajackService.TestLoginFile = fake;
                        try
                        {
                            await Task.Delay(3000);
                            var before = NexusStatus.Text;
                            SaveInPlace(NexusCard, Path.Combine(dir, $"{lang}-28-nexus-side-before.png"));
                            File.WriteAllText(fake, "test");
                            await Task.Delay(3000);
                            SaveInPlace(NexusCard, Path.Combine(dir, $"{lang}-28b-nexus-side-after.png"));
                            File.AppendAllText(Path.Combine(dir, "nexus-watch.txt"), $"{lang}: before '{before}' -> after '{NexusStatus.Text}' (no click, no window activation)\n");
                        }
                        finally { File.Delete(fake); WabbajackService.TestLoginFile = null; await Task.Delay(2500); }
                    }
                    // 0.1.19: "back to the previous version" of a modlist updated in place (test versions)
                    {
                        var rb = Views.Dialog.ForShots(Core.Loc.F("lib.rollback.inplace", "0.1.4", "0.1.5"), Core.Loc.T("lib.rollback.title"),
                            MessageBoxButton.YesNo, MessageBoxImage.Question);
                        rb.Measure(new Size(rb.Width, double.PositiveInfinity));
                        rb.Arrange(new Rect(rb.DesiredSize));
                        rb.UpdateLayout();
                        await Task.Delay(300);
                        SaveElement(rb, Path.Combine(dir, $"{lang}-24-rollback-ask.png"));
                    }
                    // 26.11 (Design, shorts): the safety checks players meet — low disk space, foreign mods, a path with
                    // Russian letters, the error report. Sample folders only, nothing on this PC is read or changed.
                    {
                        var ru = Core.Loc.Lang == "ru";
                        var shotsDlg = new[]
                        {
                            ("31-disk-short", Views.Dialog.ForShots(Core.Loc.F("disk.short.text", Core.Loc.F("disk.short.line", "C:", 38, 112)), Core.Loc.T("disk.short.title"),
                                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No, Core.Loc.T("disk.short.go"), Core.Loc.T("disk.short.cancel"))),
                            ("32-foreign-mods", Views.Dialog.ForShots(Core.Loc.F("foreign.text", @"D:\Games\Cyberpunk 2077", "• archive\\pc\\mod\\SuperCarPack.archive\n• r6\\scripts\\OldHudTweak.reds"), "NEON NIGHTS",
                                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.Yes)),
                            ("33-plain-path", Views.Dialog.ForShots(Core.Loc.F("plainpath.text", ru ? @"C:\Игры\Моя сборка" : @"C:\Games\My Modlist", @"C:\Games\FunkyPlay-NeonNights"), Core.Loc.T("check.folder.chars"),
                                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes, Core.Loc.T("plainpath.yes"), Core.Loc.T("plainpath.no"))),
                            ("34-error-report", Views.Dialog.ForShots(Core.Loc.F("start.report.done", "FunkyLauncher-report-2026-11-26_15-40.zip"), Core.Loc.T("start.support.report"),
                                MessageBoxButton.OK, MessageBoxImage.Information)),
                        };
                        foreach (var (nm, dlg) in shotsDlg)
                        {
                            dlg.Measure(new Size(dlg.Width, double.PositiveInfinity));
                            dlg.Arrange(new Rect(dlg.DesiredSize));
                            dlg.UpdateLayout();
                            await Task.Delay(300);
                            SaveElement(dlg, Path.Combine(dir, $"{lang}-{nm}.png"));
                        }
                    }
                    // 0.1.19: "Full graphics / Lightweight" — the three states of the Design mock-up (a test pack of ~6 GB)
                    foreach (var (vram, full, nm) in new[] { (6.0, false, "25-graphics"), (6.0, true, "25b-graphics-full"), (12.0, true, "25c-graphics-12gb") })
                    {
                        var heavy = new Core.AddonFile { ModlistId = "neon-nights", AddonId = "shots-test-heavy",
                            Mods = new() { new Core.AddonMod { ModFolder = "test", Archive = new Core.AddonArchive { Size = 3_000_000_000 } } } };
                        var panel = new System.Windows.Controls.Border { Background = (Brush)FindResource("Card"), CornerRadius = new CornerRadius(12),
                            Padding = new Thickness(18), Child = _setup.ShotGraphicsRow(heavy, vram, full) };
                        var gr = new System.Windows.Controls.Border { Background = (Brush)FindResource("Bg"), Padding = new Thickness(20), Width = 820, Child = panel };
                        gr.Measure(new Size(gr.Width, double.PositiveInfinity));
                        gr.Arrange(new Rect(gr.DesiredSize));
                        gr.UpdateLayout();
                        await Task.Delay(300);
                        SaveElement(gr, Path.Combine(dir, $"{lang}-{nm}.png"));
                    }
                    // 0.1.21: the same switch for Atomic Nights (fallout4) and Nova Nights (starfield) with their real heavy packs —
                    // the setup page as is, then the row with an 8 GB card (the light version recommended)
                    var was = Settings.SelectedId;
                    try
                    {
                        foreach (var id in new[] { "fallout4", "starfield" })
                        {
                            if (Core.Addons.Extras(id).FirstOrDefault(x => x.Kind == "heavy").Pack is not { } heavy) continue;
                            Settings.SelectedId = id;
                            _setup.ResetForModlist(); // the folder box and the checks of this modlist
                            Height = 1240 + dh;
                            ShowSetup();
                            await Task.Delay(3000);
                            Save(Path.Combine(dir, $"{lang}-29-setup-{id}.png"));
                            var panel = new System.Windows.Controls.Border { Background = (Brush)FindResource("Card"), CornerRadius = new CornerRadius(12),
                                Padding = new Thickness(18), Child = _setup.ShotGraphicsRow(heavy, 8.0, true) };
                            var gr = new System.Windows.Controls.Border { Background = (Brush)FindResource("Bg"), Padding = new Thickness(20), Width = 820, Child = panel };
                            gr.Measure(new Size(gr.Width, double.PositiveInfinity));
                            gr.Arrange(new Rect(gr.DesiredSize));
                            gr.UpdateLayout();
                            await Task.Delay(300);
                            SaveElement(gr, Path.Combine(dir, $"{lang}-29b-graphics-{id}-8gb.png"));
                        }
                    }
                    finally { Settings.SelectedId = was; _setup.ResetForModlist(); Height = _shotBaseHeight + dh; }
                }
                catch (Exception e) { File.AppendAllText(Path.Combine(dir, "errors.txt"), $"{lang}-toolhelp: {e}\n"); }
                // 0.1.17: the support chat with the "paste a screenshot / drag a file" hint
                try
                {
                    Views.SupportChatWindow.Open(this, Settings, Selected);
                    await Task.Delay(4000);
                    if (Views.SupportChatWindow.Current is { } cw)
                    {
                        var body = (FrameworkElement)cw.Content;
                        double w = ((FrameworkElement)cw.Content).ActualWidth, h = ((FrameworkElement)cw.Content).ActualHeight;
                        cw.Content = null;
                        var holder = new System.Windows.Controls.Border { Background = cw.Background, Child = body, Width = w, Height = h };
                        holder.Measure(new Size(w, h));
                        holder.Arrange(new Rect(0, 0, w, h));
                        holder.UpdateLayout();
                        await Task.Delay(300);
                        SaveElement(holder, Path.Combine(dir, $"{lang}-21-support-chat.png"));
                        holder.Child = null;
                        cw.Close();
                    }
                }
                catch (Exception e) { File.AppendAllText(Path.Combine(dir, "errors.txt"), $"{lang}-chat: {e}\n"); }
                // 0.1.17: the built-in browser can't work — the message in the middle of the manual-download window
                try
                {
                    foreach (var (kind, nm, detail) in new[] { (Views.WebGuard.Fail.NoRuntime, "20-web-noruntime", "WebView2RuntimeNotFoundException"),
                        (Views.WebGuard.Fail.Start, "20b-web-start", "COMException 0x80070005"), (Views.WebGuard.Fail.Page, "20c-web-page", "ConnectionReset"),
                        (Views.WebGuard.Fail.Crash, "20d-web-crash", "BrowserProcessExited") })
                    {
                        var panel = Views.WebGuard.Panel(kind, detail, Core.Loc.T("manual.outside.note"), () => { }, () => { }, () => { });
                        var holder = new System.Windows.Controls.Border { Width = 920, Height = 600, Background = (System.Windows.Media.Brush)FindResource("Bg"), Padding = new Thickness(18), Child = panel };
                        holder.Measure(new Size(920, 600));
                        holder.Arrange(new Rect(0, 0, 920, 600));
                        holder.UpdateLayout();
                        await Task.Delay(200);
                        SaveElement(holder, Path.Combine(dir, $"{lang}-{nm}.png"));
                    }
                }
                catch (Exception e) { File.AppendAllText(Path.Combine(dir, "errors.txt"), $"{lang}-webfail: {e}\n"); }
                // 0.1.17 live test of the built-in browser (FUNKYLAUNCHER_WEBTEST=1): starts, or shows the message; manual files taken from Downloads
                if (lang == "ru" && Environment.GetEnvironmentVariable("FUNKYLAUNCHER_WEBTEST") == "1")
                {
                    var rep = new System.Text.StringBuilder();
                    try
                    {
                        var bw = new Views.BrowserWindow("https://www.nexusmods.com/") { Owner = this, WindowStartupLocation = WindowStartupLocation.Manual, Left = -30000, Top = -30000, ShowActivated = false };
                        bw.Show();
                        await Task.Delay(12000);
                        rep.AppendLine($"browser: failed={bw.WebFailed} source={bw.WebSource}");
                        bw.Close();

                        var fake = Path.Combine(dir, "webtest-downloads");
                        var target = Path.Combine(dir, "webtest-modlist-downloads");
                        Directory.CreateDirectory(fake);
                        var bytes = new byte[123457];
                        new Random(1).NextBytes(bytes);
                        var item = new Services.ManualItem { ArchiveName = "webtest-mod.7z", Size = bytes.Length, ModName = "Webtest mod", GameDomain = "cyberpunk2077", ModId = 107, FileId = 1 };
                        Views.ManualDownloadWindow.DownloadsForTests = fake;
                        var mw = new Views.ManualDownloadWindow(new List<Services.ManualItem> { item }, target) { Owner = this, WindowStartupLocation = WindowStartupLocation.Manual, Left = -30000, Top = -30000, ShowActivated = false };
                        mw.Show();
                        await Task.Delay(8000);
                        rep.AppendLine($"manual: failed={mw.WebFailed}");
                        mw.OutsideForTests();
                        File.WriteAllBytes(Path.Combine(fake, item.ArchiveName), bytes);
                        await Task.Delay(5000);
                        rep.AppendLine($"manual outside: done={item.Done} copied={File.Exists(Path.Combine(target, item.ArchiveName)) && new FileInfo(Path.Combine(target, item.ArchiveName)).Length == bytes.Length}");
                        try { mw.Close(); } catch { }
                    }
                    catch (Exception e) { rep.AppendLine("error: " + e); }
                    // the support chat with a big screenshot (a 4K PNG over 10 MB is made a JPEG and sent)
                    try
                    {
                        var big = Path.Combine(dir, "webtest-big-screenshot.png");
                        var px = new byte[3840 * 2160 * 3];
                        var rnd = new Random(2); // like a game screenshot: smooth colours with fine detail
                        for (var k2 = 0; k2 < px.Length; k2++) { var p2 = k2 / 3; px[k2] = (byte)((p2 % 3840) / 15 + (p2 / 3840) / 9 + k2 % 3 * 40 + rnd.Next(8)); }
                        var bmp = System.Windows.Media.Imaging.BitmapSource.Create(3840, 2160, 96, 96, System.Windows.Media.PixelFormats.Rgb24, null, px, 3840 * 3);
                        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
                        using (var fs = File.Create(big)) enc.Save(fs);
                        var t0 = DateTime.Now;
                        if (Environment.GetEnvironmentVariable("FUNKYLAUNCHER_WEBTEST_CHAT") != "1") { rep.AppendLine("chat: skipped (the live support queue; FUNKYLAUNCHER_WEBTEST_CHAT=1 to send)"); throw new OperationCanceledException(); }
                        await Services.SupportChat.SendAsync("ТЕСТ чата Pro: большой снимок 4K (проверка сжатия, ответа не нужно)", "test", big, CancellationToken.None);
                        rep.AppendLine($"chat big png {new FileInfo(big).Length / 1048576.0:0.0} MB: sent in {(DateTime.Now - t0).TotalSeconds:0.0} s");
                    }
                    catch (Exception e) { rep.AppendLine("chat error: " + e.Message); }
                    File.WriteAllText(Path.Combine(dir, "web-live.txt"), rep.ToString());
                }
                // the one-time "follow us" window after the first modlist install
                try
                {
                    var sw = new Views.SubscribeWindow(this);
                    var body = (FrameworkElement)sw.Content;
                    sw.Content = null;
                    var holder = new System.Windows.Controls.Border { Background = sw.Background, Child = body, Width = sw.Width - 16 };
                    holder.Measure(new Size(holder.Width, double.PositiveInfinity));
                    holder.Arrange(new Rect(holder.DesiredSize));
                    holder.UpdateLayout();
                    await Task.Delay(300);
                    SaveElement(holder, Path.Combine(dir, $"{lang}-14-subscribe-window.png"));
                }
                catch (Exception e) { File.AppendAllText(Path.Combine(dir, "errors.txt"), $"{lang}-subscribe: {e}\n"); }
            }
        }
        finally
        {
            Core.Loc.Set(startLang);
            (startLang == "en" ? LangEn : LangRu).IsChecked = true;
            _exiting = true;
            Application.Current.Shutdown();
        }
    }

    /// <summary>
    /// FUNKYLAUNCHER_SHOTS_GUIDE=1 (with an empty FUNKYLAUNCHER_DATA): only the pictures of the install guide video
    /// (Design 25.11) — what a new player sees: the privacy notice, Neon Nights not installed, the setup page with
    /// Standard / High / Ultra and the PC hint, the download window, the window after a failed start.
    /// </summary>
    private async Task GuideShotsAsync(string dir, double dh)
    {
        void Err(string what, Exception e) => File.AppendAllText(Path.Combine(dir, "errors.txt"), $"{what}: {e}\n");
        var nn = Core.Catalog.Find("neon-nights");
        if (nn != null && Settings.IsInstalled(nn)) Settings.InstallDirs[nn.Id] = @"C:\Games\NeonNights-guide-shots"; // a folder that does not exist
        Settings.SelectedId = "neon-nights";
        // a PC a bit weaker than the test laptop: 8 GB of video memory → "we recommend High"
        Views.SetupView.ShotHardware(new HardwareInfo { Gpu = "NVIDIA GeForce RTX 4060", VramGb = 8, RamGb = 16, Threads = 16, Cores = 8, Ssd = true });
        foreach (var lang in new[] { "ru", "en" })
        {
            Core.Loc.Set(lang);
            (lang == "en" ? LangEn : LangRu).IsChecked = true;
            try
            {
                var pv = Views.Dialog.ForShots(Core.Loc.T("privacy.notice"), Core.Loc.T("settings.privacy"), MessageBoxButton.OK, MessageBoxImage.Information);
                pv.Measure(new Size(pv.Width, double.PositiveInfinity)); pv.Arrange(new Rect(pv.DesiredSize)); pv.UpdateLayout();
                await Task.Delay(300);
                SaveElement(pv, Path.Combine(dir, $"{lang}-g1-privacy.png"));
            }
            catch (Exception e) { Err($"{lang}-privacy", e); }
            foreach (var (name, show, height) in new (string, Action, double)[]
            {
                ("g2-home", () => NavHome.IsChecked = true, 800),
                ("g2b-library", () => { NavHome.IsChecked = true; NavLibrary.IsChecked = true; }, 800),
                ("g3-setup", () => { _setup.ResetForModlist(); ShowSetup(); }, 800),
                ("g3b-setup-full", () => ShowSetup(), 1240),
            })
                try
                {
                    Height = (height == 800 ? _shotBaseHeight : height) + dh;
                    show();
                    await Task.Delay(3500);
                    Save(Path.Combine(dir, $"{lang}-{name}.png"));
                }
                catch (Exception e) { Err($"{lang}-{name}", e); }
            Height = _shotBaseHeight + dh;
            // the download window (free Nexus account): the queue and the step text; the Nexus page itself is the player's own
            try
            {
                var items = new List<ManualItem>
                {
                    // the real first files of the Neon Nights list
                    new() { ModName = "Cyber Engine Tweaks", GameDomain = "cyberpunk2077", ModId = 107, FileId = 168615, Size = 34_868_963, ArchiveName = "CET-107.zip" },
                    new() { ModName = "ArchiveXL", GameDomain = "cyberpunk2077", ModId = 4198, FileId = 177875, Size = 1_077_330, ArchiveName = "ArchiveXL-4198-177875.zip" },
                    new() { ModName = "TweakXL", GameDomain = "cyberpunk2077", ModId = 4197, FileId = 177876, Size = 1_069_269, ArchiveName = "TweakXL-4197-177876.zip" },
                    new() { ModName = "Codeware", GameDomain = "cyberpunk2077", ModId = 7780, FileId = 177877, Size = 2_877_016, ArchiveName = "Codeware-7780-177877.zip" },
                };
                var dl = Path.Combine(dir, "_downloads");
                Directory.CreateDirectory(dl);
                var w = new Views.ManualDownloadWindow(items, dl)
                {
                    WindowStartupLocation = WindowStartupLocation.Manual, Left = -30000, Top = -30000, ShowInTaskbar = false, ShowActivated = false,
                };
                w.Show();
                await Task.Delay(6000);
                await SaveWindowBody(w, Path.Combine(dir, $"{lang}-g4-download.png"));
                w.Close();
            }
            catch (Exception e) { Err($"{lang}-download", e); }
            // after a failed start: REPAIR and "Back to version …"
            try
            {
                var p = new Views.LaunchProblemWindow(new LaunchReport(LaunchResult.Crashed, TimeSpan.FromMinutes(3), null), new Core.PreviousInstall { Version = "0.1.7" })
                {
                    WindowStartupLocation = WindowStartupLocation.Manual, Left = -30000, Top = -30000, ShowInTaskbar = false, ShowActivated = false,
                };
                p.Show();
                await Task.Delay(1500);
                await SaveWindowBody(p, Path.Combine(dir, $"{lang}-g5-problem.png"));
                p.Close();
                var q = new Views.LaunchProblemWindow(new LaunchReport(LaunchResult.NotStarted, TimeSpan.Zero, null), new Core.PreviousInstall { Version = "0.1.7" })
                {
                    WindowStartupLocation = WindowStartupLocation.Manual, Left = -30000, Top = -30000, ShowInTaskbar = false, ShowActivated = false,
                };
                q.Show();
                await Task.Delay(1500);
                await SaveWindowBody(q, Path.Combine(dir, $"{lang}-g5b-problem-notstarted.png"));
                q.Close();
            }
            catch (Exception e) { Err($"{lang}-problem", e); }
            // 0.1.23: the GTA V page (FUNKYLAUNCHER_SHOTS_GTA = a game folder, e.g. a test copy)
            if (Environment.GetEnvironmentVariable("FUNKYLAUNCHER_SHOTS_GTA") is { Length: > 0 } gtaDir && Core.Catalog.Find("gta5") is { } gm)
                try
                {
                    var (gb, gbg, gw, gh) = Views.GtaWindow.ForShots(Settings, gm, gtaDir);
                    var gholder = new System.Windows.Controls.Border { Background = gbg, Child = gb, Width = gw - 16, Height = gh - 40 };
                    gholder.Measure(new Size(gholder.Width, gholder.Height));
                    gholder.Arrange(new Rect(gholder.DesiredSize));
                    gholder.UpdateLayout();
                    await Task.Delay(300);
                    SaveElement(gholder, Path.Combine(dir, $"{lang}-g7-gta-window.png"));
                }
                catch (Exception e) { Err($"{lang}-gta", e); }
            // 0.1.23: the "Licenses" window
            try
            {
                var (lb, lbg, lw, lh) = Views.LicensesWindow.ForShots();
                var lholder = new System.Windows.Controls.Border { Background = lbg, Child = lb, Width = lw - 16, Height = lh - 40 };
                lholder.Measure(new Size(lholder.Width, lholder.Height));
                lholder.Arrange(new Rect(lholder.DesiredSize));
                lholder.UpdateLayout();
                await Task.Delay(300);
                SaveElement(lholder, Path.Combine(dir, $"{lang}-g8-licenses.png"));
            }
            catch (Exception e) { Err($"{lang}-licenses", e); }
            // "Mods of the modlist" of Nova Nights and Atomic Nights (the news picture: not all about Cyberpunk)
            foreach (var other in new[] { "starfield", "fallout4" })
                try
                {
                    if (Core.Catalog.Find(other) is not { } om || Services.ModChoices.Items(Settings, om).Count == 0) continue;
                    var (ob, obg, ow, oh) = Views.ModsWindow.ForShots(Settings, om);
                    var oholder = new System.Windows.Controls.Border { Background = obg, Child = ob, Width = ow - 16, Height = oh - 40 };
                    oholder.Measure(new Size(oholder.Width, oholder.Height));
                    oholder.Arrange(new Rect(oholder.DesiredSize));
                    oholder.UpdateLayout();
                    await Task.Delay(300);
                    SaveElement(oholder, Path.Combine(dir, $"{lang}-g6-mods-window-{other}.png"));
                }
                catch (Exception e) { Err($"{lang}-mods-{other}", e); }
        }
    }

    /// <summary>A shown off-screen window: its content moved into a free element of the window's size, then drawn
    /// (drawing the window itself off-screen cut its right side).</summary>
    private async Task SaveWindowBody(Window w, string file)
    {
        if (w.Content is not FrameworkElement body) return;
        double width = double.IsNaN(w.Width) ? Math.Max(body.ActualWidth, 600) : w.Width;
        w.Content = null;
        var holder = new System.Windows.Controls.Border { Background = w.Background ?? (Brush)FindResource("Bg"), Child = body, Width = width };
        if (!double.IsNaN(w.Height) && w.SizeToContent == SizeToContent.Manual) holder.Height = w.Height;
        holder.Measure(new Size(holder.Width, double.IsNaN(holder.Height) ? double.PositiveInfinity : holder.Height));
        holder.Arrange(new Rect(holder.DesiredSize));
        holder.UpdateLayout();
        await Task.Delay(300);
        SaveElement(holder, file);
    }

    private void Save(string file) => SaveVisual(this, file, _shotScale);

    /// <summary>An element that sits inside the window (not a free one): drawn through a VisualBrush on the page background,
    /// RenderTargetBitmap of the element itself would draw it at its window offset — an empty picture.</summary>
    private void SaveInPlace(FrameworkElement el, string file)
    {
        const double pad = 12;
        var dpi = VisualTreeHelper.GetDpi(this);
        var w = el.ActualWidth + pad * 2; var h = el.ActualHeight + pad * 2;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(Background ?? Brushes.Black, null, new Rect(0, 0, w, h));
            dc.DrawRectangle(new VisualBrush(el) { Stretch = Stretch.None }, null, new Rect(pad, pad, el.ActualWidth, el.ActualHeight));
        }
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(w * dpi.DpiScaleX), (int)Math.Ceiling(h * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bmp.Render(dv);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(file);
        enc.Save(fs);
    }

    private void SaveElement(FrameworkElement el, string file)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(el.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(el.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bmp.Render(el);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(file);
        enc.Save(fs);
    }

    private static void SaveVisual(Window win, string file, double scale = 0)
    {
        if (win.Content is not FrameworkElement root) return;
        root.UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(win);
        var sx = scale > 0 ? scale : dpi.DpiScaleX;
        var sy = scale > 0 ? scale : dpi.DpiScaleY;
        var w = (int)Math.Round(root.ActualWidth * sx);
        var h = (int)Math.Round(root.ActualHeight * sy);
        var bmp = new RenderTargetBitmap(w, h, 96 * sx, 96 * sy, PixelFormats.Pbgra32);
        var bg = new DrawingVisual();
        using (var dc = bg.RenderOpen())
            dc.DrawRectangle(win.Background ?? Brushes.Black, null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
        bmp.Render(bg);
        bmp.Render(root);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(file);
        enc.Save(fs);
    }
}
