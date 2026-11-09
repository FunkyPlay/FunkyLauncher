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
            Background = error ? (Brush)FindResource("Pink") : new SolidColorBrush(Color.FromArgb(0x29, 0x3C, 0xE6, 0xFF)),
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
                if (outcome == InstallOutcome.Success) FillDone();
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
