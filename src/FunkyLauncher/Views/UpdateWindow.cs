// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FunkyLauncher.Core;
using FunkyLauncher.Services;

namespace FunkyLauncher.Views;

/// <summary>"A new Funky Launcher is out": what changed, Update / Later. Update downloads, swaps and restarts.</summary>
public sealed class UpdateWindow : Window
{
    private readonly MainWindow _main;
    private readonly LauncherUpdate.Release _r;
    private readonly TextBlock _status;
    private readonly ProgressBar _bar;
    private readonly Button _update;
    private readonly Button _later;
    private readonly Button _manual;

    public UpdateWindow(MainWindow main, LauncherUpdate.Release r)
    {
        _main = main;
        _r = r;
        Owner = main;
        Title = "Funky Launcher";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = (Brush)Application.Current.FindResource("Bg");
        FontFamily = (FontFamily)Application.Current.FindResource("UiFont");
        Foreground = Brushes.White;

        var root = new StackPanel { Margin = new Thickness(26, 22, 26, 22) };
        root.Children.Add(new TextBlock { Text = Loc.F("update.title", r.Version), FontSize = 20, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap });
        root.Children.Add(new TextBlock { Text = Loc.F("update.current", AppConfig.Version), FontSize = 13, Foreground = (Brush)Application.Current.FindResource("Muted"), Margin = new Thickness(0, 6, 0, 14), TextWrapping = TextWrapping.Wrap });
        if (r.Notes.Length > 0)
        {
            root.Children.Add(new TextBlock { Text = Loc.T("update.notes"), FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
            root.Children.Add(new Border
            {
                Style = (Style)Application.Current.FindResource("CardBorder"),
                Padding = new Thickness(14, 10, 14, 10),
                Child = new ScrollViewer
                {
                    MaxHeight = 260,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = new TextBlock { Text = r.Notes, FontSize = 13, LineHeight = 19, Foreground = new SolidColorBrush(Color.FromRgb(0xC9, 0xC9, 0xD6)), TextWrapping = TextWrapping.Wrap },
                },
            });
        }
        _bar = new ProgressBar { Height = 6, Margin = new Thickness(0, 16, 0, 0), Minimum = 0, Maximum = 1, Visibility = Visibility.Collapsed };
        root.Children.Add(_bar);
        _status = new TextBlock { FontSize = 13, Foreground = (Brush)Application.Current.FindResource("Cyan"), Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        root.Children.Add(_status);

        var buttons = new WrapPanel { Margin = new Thickness(0, 16, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
        _manual = new Button { Content = Loc.T("update.manual"), Style = (Style)Application.Current.FindResource("SecondaryButton"), Height = 42, Padding = new Thickness(16, 0, 16, 0), Margin = new Thickness(0, 0, 10, 0), Visibility = Visibility.Collapsed };
        _manual.Click += (_, _) => { PlayService.OpenUrl(LauncherUpdate.DownloadUrl); Close(); };
        _later = new Button { Content = Loc.T("update.later"), Style = (Style)Application.Current.FindResource("SecondaryButton"), Height = 42, Padding = new Thickness(22, 0, 22, 0), Margin = new Thickness(0, 0, 10, 0) };
        _later.Click += (_, _) => Close();
        _update = new Button { Content = Loc.T("update.now"), Style = (Style)Application.Current.FindResource("PrimaryButton"), Height = 42, Padding = new Thickness(26, 0, 26, 0) };
        _update.Click += async (_, _) => await UpdateAsync();
        buttons.Children.Add(_manual);
        buttons.Children.Add(_later);
        buttons.Children.Add(_update);
        root.Children.Add(buttons);
        Content = root;

        if (Environment.GetEnvironmentVariable("FUNKYLAUNCHER_UPDATE_AUTO") == "1") // test hook: press "Update" by itself
            ContentRendered += async (_, _) => await UpdateAsync();
    }

    private void Say(string text)
    {
        _status.Text = text;
        _status.Visibility = Visibility.Visible;
    }

    private async Task UpdateAsync()
    {
        if (Catalog.Items.Any(m => m.IsReleased && BackupService.GameRunning(m))) { Say(Loc.T("update.gamerunning")); return; }
        var exe = LauncherUpdate.SelfUpdatableExe();
        if (exe == null || _r.ExeUrl == null || _r.ShaUrl == null)
        {
            PlayService.OpenUrl(LauncherUpdate.DownloadUrl);
            Close();
            return;
        }
        _update.IsEnabled = _later.IsEnabled = false;
        _bar.Visibility = Visibility.Visible;
        Say(Loc.F("update.downloading", 0));
        try
        {
            var progress = new Progress<double>(p => { _bar.Value = p; Say(Loc.F("update.downloading", (int)(p * 100))); });
            await LauncherUpdate.InstallAsync(_r, exe, progress, CancellationToken.None);
            Say(Loc.T("update.restart"));
            _main.ExitForUpdate();
        }
        catch (Exception ex)
        {
            _bar.Visibility = Visibility.Collapsed;
            Say(Loc.F("update.error", ex.Message));
            _later.IsEnabled = true;
            _manual.Visibility = Visibility.Visible;
        }
    }
}
