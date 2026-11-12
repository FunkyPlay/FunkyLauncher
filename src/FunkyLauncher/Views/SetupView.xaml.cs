// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FunkyLauncher.Core;
using FunkyLauncher.Services;
using Microsoft.Win32;

namespace FunkyLauncher.Views;

public partial class SetupView : UserControl
{
    private readonly MainWindow _main;
    private List<CheckItem> _checks = new();
    private CancellationTokenSource? _cts;
    private bool _checking;
    private bool _busy;

    private Modlist Ml => _main.Selected;
    private bool Installed => _main.Settings.IsInstalled(Ml);
    /// <summary>A modlist folder the launcher did not install — offer "check and use" or "install again", never PLAY.</summary>
    private bool Found => _main.Settings.FoundUnknown(Ml);

    public SetupView(MainWindow main)
    {
        InitializeComponent();
        _main = main;
        Loc.Changed += () => { ApplyTexts(); _ = RunChecksAsync(); };
        _main.Activated += OnMainActivated;
        ResetForModlist();
    }

    /// <summary>Called when another modlist is selected in the library.</summary>
    public void ResetForModlist()
    {
        FolderBox.Text = _main.Settings.InstallDir(Ml);
        ProgressPanel.Visibility = Visibility.Collapsed;
        _checks.Clear();
        ApplyTexts();
    }

    private void Back_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => _main.ShowLibrary();

    public void OnShown()
    {
        ApplyTexts();
        if (!_busy) _ = RunChecksAsync();
    }

    /// <summary>"Repair" from the launch-problem window: verify the files right away.</summary>
    public void StartRepair()
    {
        ApplyTexts();
        if (!_busy) _ = VerifyAsync();
    }

    private bool Updating => Installed && _main.Settings.NeedsUpdate(Ml);

    private void ApplyTexts()
    {
        BackLink.Text = Loc.T("setup.back");
        PageTitle.Text = Loc.F("setup.title", Ml.Title);
        Subtitle.Text = Loc.T("setup.subtitle");
        CheckTitle.Text = Loc.T("setup.check");
        RecheckButton.Content = Loc.T("setup.recheck");
        FolderLabel.Text = Loc.T("setup.folder");
        BrowseButton.Content = Loc.T("setup.browse");
        DownloadsText.Text = Loc.F("setup.downloads", _main.Settings.DownloadsDir(Ml));
        NexusButton.Content = Loc.T("setup.nexus.login");
        CancelButton.Content = Loc.T("setup.cancel");
        ContinueHint.Text = Loc.T("inst.continue");
        InstalledText.Text = Loc.T("setup.installed");
        ReinstallButton.Content = Loc.T("setup.reinstall");
        ApplyMode();
    }

    /// <summary>Not installed: big INSTALL. Installed: big VERIFY FILES + small Reinstall.</summary>
    private void ApplyMode()
    {
        var installed = Installed;
        var updating = Updating;
        var found = Found;
        InstallButton.Content = updating ? Loc.F("setup.update", Ml.Version) : installed ? Loc.T("setup.verify")
            : found ? Loc.T("setup.found.use") : Loc.T("setup.install");
        InstalledNote.Visibility = installed || found ? Visibility.Visible : Visibility.Collapsed;
        InstalledText.Text = updating
            ? Loc.F("setup.update.note", Ml.Version, _main.Settings.UpdateDir(Ml), _main.Settings.InstalledVersion(Ml) ?? "")
            : found ? Loc.F("setup.found", _main.Settings.InstallDir(Ml))
            : Loc.T("setup.installed");
        InstalledText.TextWrapping = TextWrapping.Wrap;
        ReinstallButton.Content = updating ? Loc.T("setup.verify.small") : found ? Loc.T("setup.found.reinstall") : Loc.T("setup.reinstall");
        ReinstallButton.Visibility = (installed || found) && !_busy ? Visibility.Visible : Visibility.Collapsed;
        // installed (and not updating): PLAY is the main action here too; verify becomes a secondary button
        var canPlay = installed && !updating;
        PlayButton.Content = Loc.T("home.play");
        PlayButton.Visibility = canPlay && !_busy ? Visibility.Visible : Visibility.Collapsed;
        InstallButton.Style = (Style)FindResource(canPlay ? "SecondaryButton" : "PrimaryButton");
        var noErrors = _checks.Count > 0 && _checks.All(c => c.State != CheckState.Error);
        InstallButton.IsEnabled = !_busy && Ml.IsReleased && noErrors;
        ReinstallButton.IsEnabled = InstallButton.IsEnabled;
        // already logged in to Nexus (the check list says so): no "Log in" button
        NexusButton.Visibility = WabbajackService.IsNexusLoggedIn ? Visibility.Collapsed : Visibility.Visible;
        RenderComposition();
    }

    // ---------- Modlist composition: language and 18+ add-on ----------

    /// <summary>Language picked on this page but not applied yet (installed modlist only).</summary>
    private string? _pendingLang;

    /// <summary>Language the installed modlist has right now: the Russian pack on = "ru".</summary>
    private string AppliedLang(AddonFile ru) => AddonService.Prefs(_main.Settings, ru).On ? "ru" : "en";

    private void RenderComposition()
    {
        var s = _main.Settings;
        var ru = ForThisVersion(Addons.Russian(Ml.Id));
        var adult = ForThisVersion(Addons.Adult(Ml.Id));
        CompositionPanel.Children.Clear();
        var installed = Installed && !Updating;
        if (ru == null && adult == null) { CompositionBox.Visibility = ApplyButton.Visibility = Visibility.Collapsed; return; }
        CompositionBox.Visibility = Visibility.Visible;

        CompositionPanel.Children.Add(new TextBlock
        {
            Text = Loc.T("comp.title"), FontSize = 11, FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("Muted"),
            Margin = new Thickness(0, 0, 0, 10),
        });

        if (ru != null)
        {
            var lang = _pendingLang ?? (installed ? AppliedLang(ru) : s.LangOf(Ml.Id));
            var row = Row(Loc.T("comp.lang"), Loc.T(installed ? "comp.lang.hint.installed" : "comp.lang.hint"), out var right);
            var seg = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            seg.Children.Add(Segment("Русский", lang == "ru", () => PickLang("ru")));
            seg.Children.Add(Segment("English", lang == "en", () => PickLang("en")));
            right.Child = new Border { Background = (Brush)FindResource("CardActive"), CornerRadius = new CornerRadius(8), Padding = new Thickness(3), Child = seg };
            CompositionPanel.Children.Add(row);
        }

        if (adult != null)
        {
            if (ru != null) CompositionPanel.Children.Add(Separator());
            var on = AddonService.Prefs(s, adult).On;
            var wanted = installed ? on : s.AdultWanted.Contains(Ml.Id);
            var row = Row(Loc.T("comp.adult"), Loc.T(installed && on ? "comp.adult.hint.on" : "comp.adult.hint"), out var right);
            var box = new CheckBox { IsChecked = wanted, VerticalAlignment = VerticalAlignment.Center, IsEnabled = !_busy };
            box.Click += (_, _) => AdultClicked(adult, box.IsChecked == true);
            right.Child = box;
            CompositionPanel.Children.Add(row);
        }

        var pending = installed && ru != null && _pendingLang != null && _pendingLang != AppliedLang(ru);
        ApplyButton.Content = Loc.T("comp.apply");
        ApplyButton.Visibility = pending ? Visibility.Visible : Visibility.Collapsed;
        ApplyButton.IsEnabled = !_busy;
    }

    /// <summary>
    /// A pack is made for one modlist version (its mod order and anchors): it is offered only when the catalog's
    /// version — the one installed or about to be installed — matches. Older installs keep what they had.
    /// </summary>
    private AddonFile? ForThisVersion(AddonFile? a)
    {
        if (a == null) return null;
        var v = Installed ? _main.Settings.InstalledVersion(Ml) ?? Ml.Version : Ml.Version;
        return a.ForMainVersion.Length == 0 || a.ForMainVersion == v ? a : null;
    }

    private Grid Row(string title, string hint, out Border right)
    {
        var g = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
        left.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.Bold });
        left.Children.Add(new TextBlock { Text = hint, FontSize = 12, Foreground = (Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
        g.Children.Add(left);
        right = new Border { VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(right, 1);
        g.Children.Add(right);
        return g;
    }

    private Border Separator() => new() { Height = 1, Background = (Brush)FindResource("CardActive"), Margin = new Thickness(0, 10, 0, 10) };

    /// <summary>One half of the Русский / English switch — a real button (mouse-up on a plain Border was lost after the install).</summary>
    private Button Segment(string text, bool selected, Action click)
    {
        var b = new Button
        {
            Content = text, Style = (Style)FindResource(selected ? "PrimaryButton" : "SecondaryButton"),
            Height = 32, MinWidth = 86, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(2, 0, 2, 0),
            FontSize = 13, IsEnabled = !_busy,
        };
        b.Click += (_, _) => click();
        return b;
    }

    private void PickLang(string lang)
    {
        var ru = ForThisVersion(Addons.Russian(Ml.Id));
        if (Installed && !Updating && ru != null) _pendingLang = lang == AppliedLang(ru) ? null : lang;
        else { _main.Settings.ModlistLang[Ml.Id] = lang; _main.Settings.Save(); }
        RenderComposition();
    }

    private void AdultClicked(AddonFile adult, bool want)
    {
        var s = _main.Settings;
        if (!(Installed && !Updating))
        {
            // before the install: remember the wish; age confirmation and levels come right after the install
            if (want) s.AdultWanted.Add(Ml.Id); else s.AdultWanted.Remove(Ml.Id);
            s.Save();
            RenderComposition();
            return;
        }
        // installed: the add-on window asks for the age, levels and installs / switches it off
        new AddonWindow(s, Ml, adult) { Owner = _main }.ShowDialog();
        RenderComposition();
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        var ru = ForThisVersion(Addons.Russian(Ml.Id));
        if (ru == null || _pendingLang == null) return;
        var lang = _pendingLang;
        BeginBusy();
        try
        {
            ProgressText.Text = await ApplyLanguageAsync(ru, lang);
            _main.Settings.ModlistLang[Ml.Id] = lang;
            _main.Settings.Save();
            _pendingLang = null;
        }
        catch (Exception ex) { ProgressText.Text = ex.Message; }
        finally { await EndBusyAsync(); }
    }

    /// <summary>"ru": installs (or switches back on) the Russian pack; "en": switches it off. Returns the text to show.</summary>
    private async Task<string> ApplyLanguageAsync(AddonFile ru, string lang)
    {
        var s = _main.Settings;
        if (lang == "en")
        {
            AddonService.Disable(s, Ml, ru);
            return Loc.T("comp.lang.done.en");
        }
        var progress = new Progress<string>(t => { ProgressText.Text = Loc.T("comp.lang.working"); LogLine.Text = t; });
        var r = await AddonService.InstallAsync(s, Ml, ru, level2: false, progress, CancellationToken.None);
        if (r.NeedManual.Count > 0)
        {
            var win = new ManualDownloadWindow(r.NeedManual, s.DownloadsDir(Ml)) { Owner = _main };
            win.ShowDialog();
            if (r.NeedManual.Any(i => i.Done)) r = await AddonService.InstallAsync(s, Ml, ru, level2: false, progress, CancellationToken.None);
        }
        var lines = new List<string> { Loc.F("comp.lang.done.ru", r.Installed) };
        if (r.NeedManual.Count > 0) lines.Add(Loc.F("comp.lang.notdownloaded", r.NeedManual.Count));
        if (r.Errors.Count > 0) lines.Add(Loc.F("addon.errors", string.Join("\n", r.Errors)));
        return string.Join("\n", lines);
    }

    /// <summary>
    /// Right after a successful install or update (Wabbajack removes everything that is not in the modlist):
    /// puts the Russian pack on if Russian is chosen, and opens the 18+ window if the player asked for it.
    /// </summary>
    private async Task AfterInstallAsync()
    {
        var s = _main.Settings;
        var ru = ForThisVersion(Addons.Russian(Ml.Id));
        if (ru != null)
        {
            var lang = s.LangOf(Ml.Id);
            AddonService.Prefs(s, ru).On = false; // the install just removed the pack's mods
            if (lang == "ru") ProgressText.Text = await ApplyLanguageAsync(ru, "ru");
        }
        var adult = ForThisVersion(Addons.Adult(Ml.Id));
        if (adult != null && (s.AdultWanted.Contains(Ml.Id) || AddonService.Prefs(s, adult).On))
        {
            s.AdultWanted.Remove(Ml.Id);
            s.Save();
            new AddonWindow(s, Ml, adult) { Owner = _main }.ShowDialog();
        }
    }

    private async Task RunChecksAsync()
    {
        if (_checking || !Ml.IsReleased) return;
        _checking = true;
        CheckList.Children.Clear();
        CheckList.Children.Add(new TextBlock { Text = Loc.T("check.running"), Foreground = (Brush)FindResource("Muted") });
        InstallButton.IsEnabled = ReinstallButton.IsEnabled = false;
        try
        {
            _checks = await SystemCheck.RunAllAsync(_main.Settings, Ml, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _checks = new List<CheckItem> { new(CheckState.Error, ex.Message) };
        }
        finally
        {
            _checking = false;
        }
        CheckList.Children.Clear();
        foreach (var c in _checks) CheckList.Children.Add(CheckRow(c));
        ApplyMode();
        _main.RefreshNexus();
    }

    private Border CheckRow(CheckItem c)
    {
        var error = c.State == CheckState.Error;
        var icon = new Border
        {
            Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Margin = new Thickness(0, 0, 14, 0),
            Background = error ? (Brush)FindResource("Pink") : new SolidColorBrush(Color.FromArgb(0x29, 0x7A, 0xE8, 0xFF)),
            Child = new TextBlock
            {
                Text = c.State switch { CheckState.Ok => "✓", CheckState.Warning => "i", _ => "!" },
                FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Foreground = error ? (Brush)FindResource("OnPink") : (Brush)FindResource("Cyan"),
            },
        };
        var texts = new StackPanel();
        texts.Children.Add(new TextBlock { Text = c.Title, FontWeight = FontWeights.SemiBold, FontSize = 14 });
        if (!string.IsNullOrEmpty(c.Hint))
            texts.Children.Add(new TextBlock { Text = c.Hint, FontSize = 12, Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(0, 2, 0, 0) });
        var row = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        row.Children.Add(icon);
        row.Children.Add(texts);
        return new Border
        {
            Background = (Brush)FindResource(error ? "PinkSoft" : "Card"), CornerRadius = new CornerRadius(12), Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 0, 0, 8),
            BorderThickness = new Thickness(error ? 1 : 0), BorderBrush = (Brush)FindResource("Pink"),
            Child = row,
        };
    }

    private void Recheck_Click(object sender, RoutedEventArgs e) => _ = RunChecksAsync();

    private void ApplyFolder(string dir)
    {
        _main.Settings.SetInstallDir(Ml, dir);
        FolderBox.Text = _main.Settings.InstallDir(Ml);
        DownloadsText.Text = Loc.F("setup.downloads", _main.Settings.DownloadsDir(Ml));
        _ = RunChecksAsync();
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = Loc.T("setup.folder") };
        if (dlg.ShowDialog() == true) ApplyFolder(dlg.FolderName);
    }

    private void FolderBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (FolderBox.Text.Trim() != _main.Settings.InstallDir(Ml)) ApplyFolder(FolderBox.Text);
    }

    private void Nexus_Click(object sender, RoutedEventArgs e) => StartNexusLogin();

    /// <summary>
    /// Nexus login happens in the Wabbajack app. A new player does not have it yet — download it first
    /// (otherwise the "login required" check would block INSTALL forever).
    /// </summary>
    public async void StartNexusLogin()
    {
        if (_busy) return;
        if (WabbajackService.FindGui() == null)
        {
            NexusButton.IsEnabled = InstallButton.IsEnabled = false;
            ProgressPanel.Visibility = Visibility.Visible;
            ProgressPercent.Text = "";
            ContinueHint.Text = ""; // "the install continues next time" is about installing, not this download
            try
            {
                var dl = new Progress<int>(p =>
                {
                    ProgressText.Text = Loc.F("inst.wj.download", p);
                    ProgressFill.Width = ((FrameworkElement)ProgressFill.Parent).ActualWidth * p / 100.0;
                });
                await WabbajackService.DownloadAsync(dl, CancellationToken.None);
            }
            catch (Exception ex)
            {
                ProgressText.Text = Loc.F("inst.failed", ex.Message);
                NexusButton.IsEnabled = true;
                return;
            }
            NexusButton.IsEnabled = true;
        }
        ProgressText.Text = Loc.T("nexus.login.opening");
        PlayService.OpenWabbajackGui();
        _ = RunChecksAsync();
        WaitForNexusLogin();
    }

    private System.Windows.Threading.DispatcherTimer? _nexusTimer;

    /// <summary>
    /// After "Log in to Nexus" the player logs in inside Wabbajack. Watch for the login file and re-run the
    /// checks the moment it appears — so INSTALL lights up by itself, no need to close Wabbajack or click anything.
    /// </summary>
    private void WaitForNexusLogin()
    {
        if (WabbajackService.IsNexusLoggedIn) return;
        _nexusTimer?.Stop();
        var started = DateTime.Now;
        _nexusTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _nexusTimer.Tick += (_, _) =>
        {
            if (WabbajackService.IsNexusLoggedIn)
            {
                _nexusTimer!.Stop();
                ProgressText.Text = Loc.T("nexus.login.done");
                _main.RefreshNexus();
                if (!_busy) _ = RunChecksAsync();
            }
            else if (DateTime.Now - started > TimeSpan.FromMinutes(20)) _nexusTimer!.Stop();
        };
        _nexusTimer.Start();
    }

    /// <summary>The window got focus again (e.g. after Wabbajack): if the Nexus login appeared meanwhile, re-check.</summary>
    private void OnMainActivated(object? sender, EventArgs e)
    {
        if (!IsVisible || _busy || _checking) return;
        var shownMissing = _checks.Any(c => c.State == CheckState.Error && c.Title == Loc.T("check.nexus.no"));
        if (shownMissing && WabbajackService.IsNexusLoggedIn)
        {
            _nexusTimer?.Stop();
            ProgressText.Text = Loc.T("nexus.login.done");
            _main.RefreshNexus();
            _ = RunChecksAsync();
        }
    }

    private void Install_Click(object sender, RoutedEventArgs e)
    {
        if (Updating) _ = InstallAsync(_main.Settings.UpdateDir(Ml));
        else if (Installed || Found) _ = VerifyAsync();
        else _ = InstallAsync();
    }

    private void Play_Click(object sender, RoutedEventArgs e) => _main.PlaySelected();

    private void Reinstall_Click(object sender, RoutedEventArgs e)
    {
        if (Updating) _ = VerifyAsync();
        else _ = InstallAsync();
    }

    private void BeginBusy()
    {
        _busy = true;
        _cts = new CancellationTokenSource();
        RecheckButton.IsEnabled = BrowseButton.IsEnabled = FolderBox.IsEnabled = false;
        CancelButton.Visibility = Visibility.Visible;
        ProgressPanel.Visibility = Visibility.Visible;
        ProgressPercent.Text = "";
        ProgressFill.Width = 0;
        ApplyMode();
    }

    private async Task EndBusyAsync()
    {
        _busy = false;
        CancelButton.Visibility = Visibility.Collapsed;
        RecheckButton.IsEnabled = BrowseButton.IsEnabled = FolderBox.IsEnabled = true;
        await RunChecksAsync();
    }

    private Progress<InstallProgress> MakeProgress() => new(p =>
    {
        ProgressText.Text = p.Total > 0 ? $"{p.Status} · {Loc.F("inst.downloaded", p.Downloaded, p.Total)}" : p.Status;
        var pct = p.Total > 0 ? Math.Min(100, p.Downloaded * 100 / p.Total) : 0;
        ProgressPercent.Text = p.Total > 0 ? pct + "%" : "";
        ProgressFill.Width = ((FrameworkElement)ProgressFill.Parent).ActualWidth * pct / 100.0;
        if (!string.IsNullOrWhiteSpace(p.LogLine)) LogLine.Text = p.LogLine;
    });

    private void FillDone()
    {
        ProgressPercent.Text = "100%";
        ProgressFill.Width = ((FrameworkElement)ProgressFill.Parent).ActualWidth;
    }

    /// <param name="updateDir">Update: install the new version into this new folder and keep the current one for rollback.</param>
    private async Task InstallAsync(string? updateDir = null)
    {
        BeginBusy();
        try
        {
            while (true)
            {
                var (outcome, code, manual) = await InstallService.RunAsync(_main.Settings, Ml, MakeProgress(), _cts!.Token, updateDir);
                if (outcome == InstallOutcome.NeedsManualDownloads && OpenManualDownloads())
                    continue; // everything was downloaded by hand — run the install again
                var s = _main.Settings;
                if (outcome == InstallOutcome.Success)
                {
                    if (updateDir != null)
                    {
                        // keep the old working version; the new one becomes current
                        s.Previous[Ml.Id] = new PreviousInstall { Dir = s.InstallDir(Ml), Version = s.InstalledVersion(Ml) ?? "" };
                        s.InstallDirs[Ml.Id] = updateDir;
                        FolderBox.Text = updateDir;
                    }
                    s.MarkInstalled(Ml, s.InstallDir(Ml), Ml.Version);
                }
                ProgressText.Text = outcome switch
                {
                    InstallOutcome.Success when updateDir != null => Loc.F("inst.update.done", Ml.Version),
                    InstallOutcome.Success => Loc.T("inst.done"),
                    InstallOutcome.NeedsManualDownloads => Loc.F("inst.manual", manual),
                    InstallOutcome.Cancelled => Loc.T("inst.stopped"),
                    _ => Loc.F("inst.failed", code),
                };
                if (outcome == InstallOutcome.Success)
                {
                    FillDone();
                    try { await AfterInstallAsync(); }
                    catch (Exception ex) { ProgressText.Text += "\n" + ex.Message; }
                }
                break;
            }
        }
        catch (GameCheckException ex)
        {
            ProgressText.Text = ex.Message;
        }
        catch (Exception ex)
        {
            ProgressText.Text = Loc.F("inst.failed", ex.Message);
        }
        finally
        {
            await EndBusyAsync();
        }
    }

    /// <summary>Shows the manual download window. True = every missing file is now downloaded.</summary>
    private bool OpenManualDownloads()
    {
        var file = _main.Settings.ResolveWabbajack(Ml);
        if (file == null) return false;
        var items = ManualQueue.Build(file, _main.Settings.DownloadsDir(Ml));
        if (items.Count == 0) return false;
        var win = new ManualDownloadWindow(items, _main.Settings.DownloadsDir(Ml)) { Owner = _main };
        win.ShowDialog();
        return win.AllDone;
    }

    private async Task VerifyAsync()
    {
        var adopting = Found; // a folder the launcher did not install: it becomes "installed" only if the check passes
        BeginBusy();
        try
        {
            var (ok, problems, ignored) = await InstallService.VerifyAsync(_main.Settings, Ml, MakeProgress(), _cts!.Token);
            if (ok && adopting) _main.Settings.MarkInstalled(Ml, _main.Settings.InstallDir(Ml), Ml.Version);
            ProgressText.Text = !ok ? Loc.F(adopting ? "inst.found.fail" : "inst.verify.fail", problems)
                : adopting ? Loc.T("inst.found.ok")
                : ignored > 0 ? Loc.F("inst.verify.ok.ignored", ignored)
                : Loc.T("inst.verify.ok");
            if (ok) FillDone();
        }
        catch (OperationCanceledException)
        {
            ProgressText.Text = Loc.T("inst.stopped");
        }
        catch (Exception ex)
        {
            ProgressText.Text = Loc.F("inst.failed", ex.Message);
        }
        finally
        {
            await EndBusyAsync();
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();
}
