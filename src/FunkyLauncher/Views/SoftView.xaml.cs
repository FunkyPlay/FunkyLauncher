// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FunkyLauncher.Core;
using FunkyLauncher.Services;

namespace FunkyLauncher.Views;

/// <summary>Useful software from the catalog. Each button opens the program's official website.</summary>
public partial class SoftView : UserControl
{
    public SoftView()
    {
        InitializeComponent();
        Loc.Changed += Refresh;
        Refresh();
    }

    public void Refresh()
    {
        PageTitle.Text = Loc.T("soft.title");
        Subtitle.Text = Loc.T("soft.subtitle");
        Items.Children.Clear();
        foreach (var s in Catalog.Software) Items.Children.Add(Card(s));
    }

    private Border Card(SoftwareItem s)
    {
        Brush color;
        try { color = (Brush)new BrushConverter().ConvertFrom(s.Color)!; } catch { color = (Brush)FindResource("Cyan"); }

        var tile = new Border
        {
            Width = 48, Height = 48, CornerRadius = new CornerRadius(12), Background = color,
            Child = new TextBlock
            {
                Text = s.Name.TrimStart('.').Substring(0, 1).ToUpperInvariant(),
                FontFamily = (FontFamily)FindResource("TitleFont"), FontSize = 22, Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        };
        var head = new DockPanel();
        DockPanel.SetDock(tile, Dock.Left);
        head.Children.Add(tile);
        var names = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(new TextBlock { Text = s.Name, FontSize = 16, FontWeight = FontWeights.Bold, TextTrimming = TextTrimming.CharacterEllipsis });
        names.Children.Add(new TextBlock { Text = s.Category.ToString(), FontSize = 12, Foreground = (Brush)FindResource("Muted") });
        head.Children.Add(names);

        var button = new Button { Content = Loc.T("soft.open"), Style = (Style)FindResource("SecondaryButton"), Height = 38, Margin = new Thickness(0, 12, 0, 0) };
        button.Click += (_, _) => PlayService.OpenUrl(s.Url);

        var body = new DockPanel();
        DockPanel.SetDock(head, Dock.Top);
        DockPanel.SetDock(button, Dock.Bottom);
        body.Children.Add(head);
        body.Children.Add(button);
        body.Children.Add(new TextBlock { Text = s.Description.ToString(), FontSize = 13, LineHeight = 19, Foreground = new SolidColorBrush(Color.FromRgb(0xC9, 0xC9, 0xD6)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) });

        return new Border { Style = (Style)FindResource("CardBorder"), Padding = new Thickness(18, 16, 18, 16), Margin = new Thickness(0, 0, 16, 16), Height = 210, Child = body };
    }
}
