// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using FunkyLauncher.Core;

namespace FunkyLauncher.Views;

/// <summary>
/// 0.1.19: before Wabbajack opens for the Nexus login — three numbered steps, each with a small drawing of what to
/// press in Wabbajack (Settings → Nexus Mods → Log in), so the player does not have to look for it.
/// The launcher notices the login by itself (MainWindow watches the login file).
/// </summary>
public static class NexusLoginWindow
{
    /// <summary>true = the player pressed "Open Wabbajack".</summary>
    public static bool Ask(Window? owner)
    {
        var app = Application.Current;
        var ok = false;
        var win = new Window
        {
            Title = Loc.T("nexus.steps.title"), Owner = owner is { IsLoaded: true } ? owner : null,
            WindowStartupLocation = owner is { IsLoaded: true } ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = owner == null,
            Background = app?.TryFindResource("Bg") as Brush ?? Brushes.Black,
            Foreground = app?.TryFindResource("Text") as Brush ?? Brushes.White,
        };
        if (app?.TryFindResource("UiFont") is FontFamily font) win.FontFamily = font;
        win.Content = Body(r => { ok = r; win.Close(); });
        win.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) win.Close(); };
        win.ShowDialog();
        return ok;
    }

    /// <summary>For --shots: the window's content, not shown on screen.</summary>
    internal static Border ForShots()
    {
        var app = Application.Current;
        var b = new Border { Background = app?.TryFindResource("Bg") as Brush ?? Brushes.Black, Child = Body(_ => { }) };
        if (app?.TryFindResource("UiFont") is FontFamily font) TextElement.SetFontFamily(b, font);
        TextElement.SetForeground(b, app?.TryFindResource("Text") as Brush ?? Brushes.White);
        return b;
    }

    private static Brush Res(string key, Brush fallback) => Application.Current?.TryFindResource(key) as Brush ?? fallback;

    private static StackPanel Body(Action<bool> done)
    {
        var root = new StackPanel { Margin = new Thickness(28, 24, 28, 24), Width = 640 };
        root.Children.Add(new TextBlock { Text = Loc.T("nexus.steps.title"), FontSize = 19, FontWeight = FontWeights.Bold });
        root.Children.Add(new TextBlock
        {
            Text = Loc.T("nexus.steps.intro"), FontSize = 14, LineHeight = 21, TextWrapping = TextWrapping.Wrap,
            Foreground = Res("Muted", Brushes.Gray), Margin = new Thickness(0, 8, 0, 18),
        });
        root.Children.Add(Step(1, Loc.T("nexus.steps.1"), Settings()));
        root.Children.Add(Step(2, Loc.T("nexus.steps.2"), NexusCard()));
        root.Children.Add(Step(3, Loc.T("nexus.steps.3"), Browser()));
        root.Children.Add(new TextBlock
        {
            Text = Loc.T("nexus.steps.auto"), FontSize = 13, LineHeight = 19, TextWrapping = TextWrapping.Wrap,
            Foreground = Res("Cyan", Brushes.Cyan), Margin = new Thickness(0, 6, 0, 0),
        });

        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        void Add(string label, bool result, bool primary)
        {
            var b = new Button { Content = label, MinWidth = 120, Height = 40, Margin = new Thickness(10, 0, 0, 0), Padding = new Thickness(18, 0, 18, 0) };
            if (Application.Current?.TryFindResource(primary ? "PrimaryButton" : "SecondaryButton") is Style st) b.Style = st;
            b.Click += (_, _) => done(result);
            if (primary) b.IsDefault = true;
            row.Children.Add(b);
        }
        Add(Loc.T("nexus.steps.open"), true, true);
        Add(Loc.T("nexus.steps.cancel"), false, false);
        root.Children.Add(row);
        return root;
    }

    /// <summary>A step: the number in a circle, the text, and on the right the drawing of what to press.</summary>
    private static Border Step(int n, string text, UIElement picture)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
        var num = new Border
        {
            Width = 32, Height = 32, CornerRadius = new CornerRadius(16), Background = Res("Pink", Brushes.HotPink),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0),
            Child = new TextBlock { Text = n.ToString(), FontSize = 15, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        var label = new TextBlock { Text = text, FontSize = 14.5, LineHeight = 21, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
        Grid.SetColumn(label, 1);
        Grid.SetColumn(picture, 2);
        grid.Children.Add(num); grid.Children.Add(label); grid.Children.Add(picture);
        return new Border
        {
            Background = Res("Card", Brushes.DimGray), CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 12, 12, 12),
            Margin = new Thickness(0, 0, 0, 10), Child = grid,
        };
    }

    // ---- drawings: a sketch of the Wabbajack window part, the thing to press framed in cyan (Wabbajack is in English)

    private static Border Frame(UIElement inner) => new()
    {
        Background = new SolidColorBrush(Color.FromRgb(0x1B, 0x1B, 0x22)), CornerRadius = new CornerRadius(7),
        BorderBrush = Res("Line", Brushes.Gray), BorderThickness = new Thickness(1), Height = 78, Child = inner,
    };

    private static Border Target(string text, double size = 12.5) => new()
    {
        BorderBrush = Res("Cyan", Brushes.Cyan), BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(5),
        Background = new SolidColorBrush(Color.FromArgb(0x33, 0x00, 0xE5, 0xFF)), Padding = new Thickness(8, 3, 8, 3),
        HorizontalAlignment = HorizontalAlignment.Left,
        Child = new TextBlock { Text = text, FontSize = size, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White },
    };

    /// <summary>Step 1: "Settings" in Wabbajack, framed.</summary>
    private static UIElement Settings()
    {
        var t = Target("⚙  Settings", 13.5);
        t.Margin = new Thickness(14, 0, 0, 0); t.VerticalAlignment = VerticalAlignment.Center;
        return Frame(t);
    }

    /// <summary>Step 2: the "Nexus Mods" login line, "Log in" framed.</summary>
    private static UIElement NexusCard()
    {
        var g = new DockPanel { Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center, LastChildFill = false };
        var name = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        name.Children.Add(new TextBlock { Text = "Nexus Mods", FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
        DockPanel.SetDock(name, Dock.Left);
        var t = Target("Log in"); t.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(t, Dock.Right);
        g.Children.Add(name); g.Children.Add(t);
        return Frame(g);
    }

    /// <summary>Step 3: the browser with the Nexus page, the confirm button framed.</summary>
    private static UIElement Browser()
    {
        var s = new StackPanel();
        s.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x33)), CornerRadius = new CornerRadius(6, 6, 0, 0), Padding = new Thickness(8, 3, 8, 3),
            Child = new TextBlock { Text = "users.nexusmods.com", FontSize = 10.5, Foreground = Res("Muted", Brushes.Gray) },
        });
        var body = new DockPanel { Margin = new Thickness(10, 8, 10, 0), LastChildFill = false };
        var who = new TextBlock { Text = "Wabbajack", FontSize = 12, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(who, Dock.Left);
        var t = Target(Loc.T("nexus.steps.3.button"), 12);
        DockPanel.SetDock(t, Dock.Right);
        body.Children.Add(who); body.Children.Add(t);
        s.Children.Add(body);
        return Frame(s);
    }
}
