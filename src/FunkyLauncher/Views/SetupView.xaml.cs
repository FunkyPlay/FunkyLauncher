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

    public SetupView(MainWindow main)
    {
        InitializeComponent();
        _main = main;
        Loc.Changed += () => { ApplyTexts(); _ = RunChecksAsync(); };
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
        if (!_busy) _ = RunChecksAsync();
    }

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
        InstallButton.Content = installed ? Loc.T("setup.verify") : Loc.T("setup.install");
        InstalledNote.Visibility = installed ? Visibility.Visible : Visibility.Collapsed;
        ReinstallButton.Visibility = installed && !_busy ? Visibility.Visible : Visibility.Collapsed;
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

    private void Nexus_Click(object sender, RoutedEventArgs e) => PlayService.OpenWabbajackGui();

    private void Install_Click(object sender, RoutedEventArgs e)
    {
        if (Installed) _ = VerifyAsync();
        else _ = InstallAsync();
    }

    private void Reinstall_Click(object sender, RoutedEventArgs e) => _ = InstallAsync();

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

    private async Task InstallAsync()
    {
        BeginBusy();
        try
        {
            while (true)
            {
                var (outcome, code, manual) = await InstallService.RunAsync(_main.Settings, Ml, MakeProgress(), _cts!.Token);
                if (outcome == InstallOutcome.NeedsManualDownloads && OpenManualDownloads())
                    continue; // everything was downloaded by hand — run the install again
                ProgressText.Text = outcome switch
                {
                    InstallOutcome.Success => Loc.T("inst.done"),
                    InstallOutcome.NeedsManualDownloads => Loc.F("inst.manual", manual),
                    InstallOutcome.Cancelled => Loc.T("inst.stopped"),
                    _ => Loc.F("inst.failed", code),
                };
                if (outcome == InstallOutcome.Success) FillDone();
                break;
            }
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
        BeginBusy();
        try
        {
            var (ok, problems, ignored) = await InstallService.VerifyAsync(_main.Settings, Ml, MakeProgress(), _cts!.Token);
            ProgressText.Text = !ok ? Loc.F("inst.verify.fail", problems)
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
