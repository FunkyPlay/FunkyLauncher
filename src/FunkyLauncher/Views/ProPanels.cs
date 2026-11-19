// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Windows;
using System.Windows.Controls;
using FunkyLauncher.Core;
using FunkyLauncher.Services;

namespace FunkyLauncher.Views;

/// <summary>Puts the panels of the Funky Pro module into a page (nothing without a valid key and the module).</summary>
public static class ProPanels
{
    /// <summary>Builds the panels of <paramref name="place"/> into <paramref name="host"/> (cleared first).</summary>
    public static void Fill(Panel host, ProPlace place)
    {
        host.Children.Clear();
        foreach (var p in ProModules.Panels(place))
        {
            var content = Build(p);
            if (place == ProPlace.Library) { host.Children.Add(content); continue; }
            var box = new StackPanel();
            box.Children.Add(new TextBlock { Text = p.Icon + "  " + p.Title, FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            if (p.Text.Length > 0)
                box.Children.Add(new TextBlock
                {
                    Text = p.Text, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 12),
                    Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("Muted"),
                });
            box.Children.Add(content);
            var card = new Border { Child = box, Margin = new Thickness(0, 12, 0, 0) };
            if (Application.Current.TryFindResource("CardBorder") is Style st) card.Style = st;
            host.Children.Add(card);
        }
    }

    /// <summary>The panel's own content; a broken panel shows its error instead of breaking the page.</summary>
    public static FrameworkElement Build(ProPanelInfo p)
    {
        try { return p.Create(); }
        catch (Exception ex)
        {
            return new TextBlock { Text = "Funky Pro: " + ex.Message, TextWrapping = TextWrapping.Wrap, FontSize = 12.5,
                Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("Pink") };
        }
    }
}
