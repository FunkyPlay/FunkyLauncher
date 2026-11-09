// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FunkyLauncher.Core;
using FunkyLauncher.Services;

namespace FunkyLauncher.Views;

/// <summary>Library: all FunkyPlay modlists on top, the selected one below with PLAY.</summary>
public partial class LibraryView : UserControl
{
    private readonly MainWindow _main;

    public LibraryView(MainWindow main)
    {
        InitializeComponent();
        _main = main;
        Loc.Changed += Refresh;
        Refresh();
    }

    public void Refresh()
    {
        PageTitle.Text = Loc.T("lib.title");
        Subtitle.Text = Loc.T("lib.subtitle");
        Cards.Children.Clear();
        foreach (var m in Catalog.Items) Cards.Children.Add(Card(m));
        RefreshSelected();
    }

    private void RefreshSelected()
    {
        var s = _main.Settings;
        var m = _main.Selected;
        var installed = m.IsReleased && s.IsInstalled(m);
        var img = Images.Load(m.Banner);
        Banner.Background = img != null
            ? new ImageBrush(img) { Stretch = Stretch.UniformToFill, AlignmentY = AlignmentY.Center }
            : new LinearGradientBrush(Color.FromRgb(0x2A, 0x14, 0x26), Color.FromRgb(0x0F, 0x2A, 0x33), 45);
        TitleText.Text = m.Title;
        TagText.Text = m.Tag.ToString();
        Description.Text = m.Description.ToString();
        Sub.Text = !m.IsReleased ? Loc.F("home.sub.soon", m.Author)
            : installed ? Loc.F("home.sub.installed", m.Author, m.Version, s.InstallDir(m))
            : Loc.F("home.sub.notinstalled", m.Author, m.Version);
        MainButton.Content = installed ? Loc.T("home.play") : !m.IsReleased ? Loc.T("lib.soon") : Loc.T("home.install");
        MainButton.IsEnabled = m.IsReleased;
        FolderButton.Content = Loc.T("home.folder");
        Mo2Button.Content = Loc.T("home.mo2");
        FolderButton.IsEnabled = Mo2Button.IsEnabled = installed;
        FolderButton.Visibility = Mo2Button.Visibility = installed ? Visibility.Visible : Visibility.Collapsed;
        StatusTitle.Text = installed ? Loc.T("home.ready") : Loc.T("home.notready");
        SetupButton.Content = Loc.T("lib.setup");
        SetupButton.IsEnabled = m.IsReleased;

        StatusList.Children.Clear();
        if (m.IsReleased)
        {
            AddStatus(installed, installed ? s.InstallDir(m) : Loc.T("home.notready"));
            AddStatus(WabbajackService.IsNexusLoggedIn, WabbajackService.IsNexusLoggedIn ? Loc.T("check.nexus.ok") : Loc.T("check.nexus.no"));
        }
        PlayMessage.Text = "";
    }

    private void AddStatus(bool ok, string text) =>
        StatusList.Children.Add(new TextBlock
        {
            Text = (ok ? "✓  " : "•  ") + text,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6),
            Foreground = ok ? new SolidColorBrush(Color.FromRgb(0xC9, 0xC9, 0xD6)) : (Brush)FindResource("Pink"),
        });

    private Border Card(Modlist m)
    {
        var installed = m.IsReleased && _main.Settings.IsInstalled(m);
        var selected = m.Id == _main.Selected.Id;
        var img = Images.Load(m.Banner);

        var (chipText, chipFg, chipBg) = !m.IsReleased
            ? (Loc.T("lib.soon"), "#C9C9D6", "#CC33333F")
            : installed ? (Loc.T("lib.installed"), "#9AF2FF", "#CC0F2A33") : (Loc.F("lib.available", m.SizeGb), "#FF7AD9", "#CC2A1426");
        var bc = new BrushConverter();

        var grid = new Grid();
        grid.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(13),
            Background = new LinearGradientBrush(Color.FromArgb(0x10, 0x0F, 0x0F, 0x14), Color.FromArgb(0xF0, 0x0F, 0x0F, 0x14), 90),
        });
        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(14, 0, 14, 12) };
        info.Children.Add(new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 2, 8, 2),
            Background = (Brush)bc.ConvertFrom(chipBg)!,
            Child = new TextBlock { Text = chipText, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = (Brush)bc.ConvertFrom(chipFg)! },
        });
        info.Children.Add(new TextBlock { Text = m.Title, FontFamily = (FontFamily)FindResource("TitleFont"), FontSize = 19, Margin = new Thickness(0, 6, 0, 0) });
        info.Children.Add(new TextBlock { Text = m.Tag.ToString(), FontSize = 11.5, Foreground = (Brush)FindResource("Muted"), TextTrimming = TextTrimming.CharacterEllipsis });
        grid.Children.Add(info);

        var card = new Border
        {
            Width = 270, Height = 130, Margin = new Thickness(0, 0, 16, 0), CornerRadius = new CornerRadius(14),
            Background = img != null
                ? new ImageBrush(img) { Stretch = Stretch.UniformToFill }
                : new LinearGradientBrush(Color.FromRgb(0x2A, 0x14, 0x26), Color.FromRgb(0x0F, 0x2A, 0x33), 45),
            BorderThickness = new Thickness(2),
            BorderBrush = selected ? (Brush)FindResource("Pink") : (Brush)bc.ConvertFrom("#2A2A36")!,
            Opacity = m.IsReleased ? 1 : 0.75,
            Cursor = Cursors.Hand,
            Child = grid,
        };
        card.MouseLeftButtonUp += (_, _) => _main.Select(m);
        return card;
    }

    private void MainButton_Click(object sender, RoutedEventArgs e)
    {
        var m = _main.Selected;
        if (!_main.Settings.IsInstalled(m)) { _main.ShowSetup(); return; }
        try
        {
            PlayService.Play(_main.Settings, m);
            PlayMessage.Text = Loc.T("home.play.started");
        }
        catch (Exception ex)
        {
            PlayMessage.Text = Loc.F("home.play.error", ex.Message);
        }
    }

    private void Setup_Click(object sender, RoutedEventArgs e) => _main.ShowSetup();
    private void FolderButton_Click(object sender, RoutedEventArgs e) => PlayService.OpenFolder(_main.Settings.InstallDir(_main.Selected));
    private void Mo2Button_Click(object sender, RoutedEventArgs e)
    {
        try { PlayService.OpenMo2(_main.Settings, _main.Selected); } catch (Exception ex) { PlayMessage.Text = Loc.F("home.play.error", ex.Message); }
    }
}
