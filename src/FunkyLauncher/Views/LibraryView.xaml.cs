// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FunkyLauncher.Core;

namespace FunkyLauncher.Views;

/// <summary>Grid of all FunkyPlay modlists from the catalog.</summary>
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
    }

    private Border Card(Modlist m)
    {
        var installed = m.IsReleased && _main.Settings.IsInstalled(m);
        var selected = m.Id == _main.Selected.Id;

        // picture
        var img = Images.Load(m.Banner);
        var picture = new Border
        {
            Height = 170,
            CornerRadius = new CornerRadius(14, 14, 0, 0),
            Background = img != null
                ? new ImageBrush(img) { Stretch = Stretch.UniformToFill }
                : new LinearGradientBrush(Color.FromRgb(0x2A, 0x14, 0x26), Color.FromRgb(0x0F, 0x2A, 0x33), 45),
        };
        var pictureGrid = new Grid();
        pictureGrid.Children.Add(picture);
        if (img == null)
            pictureGrid.Children.Add(new TextBlock
            {
                Text = m.Title, FontFamily = (FontFamily)FindResource("TitleFont"), FontSize = 26, Opacity = 0.55,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            });

        // status chip
        var (chipText, chipFg, chipBg) = !m.IsReleased
            ? (Loc.T("lib.soon"), "#C9C9D6", "#33333F")
            : installed ? (Loc.T("lib.installed"), "#9AF2FF", "#243CE6FF") : (Loc.F("lib.available", m.SizeGb), "#FF7AD9", "#2A1426");
        var chip = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 3, 10, 3),
            Background = (Brush)new BrushConverter().ConvertFrom(chipBg)!, Margin = new Thickness(0, 10, 0, 0),
            Child = new TextBlock { Text = chipText, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = (Brush)new BrushConverter().ConvertFrom(chipFg)! },
        };

        var info = new StackPanel { Margin = new Thickness(16, 12, 16, 16) };
        info.Children.Add(new TextBlock { Text = m.Title, FontFamily = (FontFamily)FindResource("TitleFont"), FontSize = 20 });
        info.Children.Add(new TextBlock { Text = m.Tag.ToString(), FontSize = 13, Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(0, 4, 0, 0) });
        if (m.ModCount > 0)
            info.Children.Add(new TextBlock { Text = $"by {m.Author} · {Loc.F("lib.mods", m.ModCount)}", FontSize = 12, Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(0, 2, 0, 0) });
        info.Children.Add(chip);

        var stack = new StackPanel();
        stack.Children.Add(pictureGrid);
        stack.Children.Add(info);

        var card = new Border
        {
            Width = 300, Margin = new Thickness(0, 0, 20, 20), CornerRadius = new CornerRadius(14),
            Background = (Brush)FindResource("Card"), Child = stack,
            BorderThickness = new Thickness(selected ? 2 : 1),
            BorderBrush = selected ? (Brush)FindResource("Pink") : (Brush)new BrushConverter().ConvertFrom("#2A2A36")!,
            Opacity = m.IsReleased ? 1 : 0.7,
        };
        if (m.IsReleased)
        {
            card.Cursor = Cursors.Hand;
            card.MouseLeftButtonUp += (_, _) => _main.Select(m);
        }
        return card;
    }
}
