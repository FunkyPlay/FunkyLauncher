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
    private bool _installing;

    public SetupView(MainWindow main)
    {
        InitializeComponent();
        _main = main;
        FolderBox.Text = main.Settings.InstallDir;
        Loc.Changed += () => { ApplyTexts(); _ = RunChecksAsync(); };
        ApplyTexts();
    }

    public void OnShown()
    {
        if (!_installing) _ = RunChecksAsync();
    }

    private void ApplyTexts()
    {
        PageTitle.Text = Loc.T("setup.title");
        Subtitle.Text = Loc.T("setup.subtitle");
        CheckTitle.Text = Loc.T("setup.check");
        RecheckButton.Content = Loc.T("setup.recheck");
        FolderLabel.Text = Loc.T("setup.folder");
        BrowseButton.Content = Loc.T("setup.browse");
        DownloadsText.Text = Loc.F("setup.downloads", _main.Settings.DownloadsDir);
        NexusButton.Content = Loc.T("setup.nexus.login");
        InstallButton.Content = Loc.T("setup.install");
        CancelButton.Content = Loc.T("setup.cancel");
        ContinueHint.Text = Loc.T("inst.continue");
    }

    private async Task RunChecksAsync()
    {
        if (_checking) return;
        _checking = true;
        CheckList.Children.Clear();
        CheckList.Children.Add(new TextBlock { Text = Loc.T("check.running"), Foreground = (Brush)FindResource("Muted") });
        InstallButton.IsEnabled = false;
        try
        {
            _checks = await SystemCheck.RunAllAsync(_main.Settings, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _checks = new List<CheckItem> { new(CheckState.Error, ex.Message) };
        }
        finally
        {
            _checking = false;
        }
        RenderChecks();
        _main.RefreshNexus();
    }

    private void RenderChecks()
    {
        CheckList.Children.Clear();
        foreach (var c in _checks) CheckList.Children.Add(CheckRow(c));
        // a missing Wabbajack is only a warning: the install downloads it
        InstallButton.IsEnabled = !_installing && _checks.All(c => c.State != CheckState.Error);
    }

    private Border CheckRow(CheckItem c)
    {
        var (mark, color, bg) = c.State switch
        {
            CheckState.Ok => ("✓", "Cyan", "Card"),
            CheckState.Warning => ("i", "Cyan", "Card"),
            _ => ("!", "Pink", "PinkSoft"),
        };
        var icon = new Border
        {
            Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Margin = new Thickness(0, 0, 14, 0),
            Background = c.State == CheckState.Error ? (Brush)FindResource("Pink") : new SolidColorBrush(Color.FromArgb(0x29, 0x3C, 0xE6, 0xFF)),
            Child = new TextBlock
            {
                Text = mark, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Foreground = c.State == CheckState.Error ? (Brush)FindResource("OnPink") : (Brush)FindResource(color),
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
            Background = (Brush)FindResource(bg), CornerRadius = new CornerRadius(12), Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 0, 0, 8),
            BorderThickness = new Thickness(c.State == CheckState.Error ? 1 : 0), BorderBrush = (Brush)FindResource("Pink"),
            Child = row,
        };
    }

    private void Recheck_Click(object sender, RoutedEventArgs e) => _ = RunChecksAsync();

    private void ApplyFolder(string dir)
    {
        _main.Settings.InstallDir = dir.Trim();
        _main.Settings.Save();
        FolderBox.Text = _main.Settings.InstallDir;
        DownloadsText.Text = Loc.F("setup.downloads", _main.Settings.DownloadsDir);
        _ = RunChecksAsync();
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = Loc.T("setup.folder") };
        if (dlg.ShowDialog() == true) ApplyFolder(dlg.FolderName);
    }

    private void FolderBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (FolderBox.Text.Trim() != _main.Settings.InstallDir) ApplyFolder(FolderBox.Text);
    }

    private void Nexus_Click(object sender, RoutedEventArgs e)
    {
        PlayService.OpenWabbajackGui();
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        _installing = true;
        _cts = new CancellationTokenSource();
        InstallButton.IsEnabled = false;
        RecheckButton.IsEnabled = BrowseButton.IsEnabled = FolderBox.IsEnabled = false;
        CancelButton.Visibility = Visibility.Visible;
        ProgressPanel.Visibility = Visibility.Visible;

        var progress = new Progress<InstallProgress>(p =>
        {
            ProgressText.Text = p.Total > 0 ? $"{p.Status} · {Loc.F("inst.downloaded", p.Downloaded, p.Total)}" : p.Status;
            var pct = p.Total > 0 ? Math.Min(100, p.Downloaded * 100 / p.Total) : 0;
            ProgressPercent.Text = p.Total > 0 ? pct + "%" : "";
            ProgressFill.Width = ((FrameworkElement)ProgressFill.Parent).ActualWidth * pct / 100.0;
            if (!string.IsNullOrWhiteSpace(p.LogLine)) LogLine.Text = p.LogLine;
        });

        try
        {
            var (outcome, code, manual) = await InstallService.RunAsync(_main.Settings, progress, _cts.Token);
            ProgressText.Text = outcome switch
            {
                InstallOutcome.Success => Loc.T("inst.done"),
                InstallOutcome.NeedsManualDownloads => Loc.F("inst.manual", manual),
                InstallOutcome.Cancelled => Loc.T("inst.stopped"),
                _ => Loc.F("inst.failed", code),
            };
            if (outcome == InstallOutcome.Success)
            {
                ProgressPercent.Text = "100%";
                ProgressFill.Width = ((FrameworkElement)ProgressFill.Parent).ActualWidth;
            }
        }
        catch (Exception ex)
        {
            ProgressText.Text = Loc.F("inst.failed", ex.Message);
        }
        finally
        {
            _installing = false;
            CancelButton.Visibility = Visibility.Collapsed;
            RecheckButton.IsEnabled = BrowseButton.IsEnabled = FolderBox.IsEnabled = true;
            await RunChecksAsync();
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();
}
