// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FunkyLauncher.Core;

namespace FunkyLauncher.Views;

/// <summary>"How to install REDmod" (0.1.16, design): a small window like the update window — 3 steps, "Got it" / "Check again".
/// No store names and no links out (Legal). Returns true when the player pressed "Check again".</summary>
public sealed class ToolHelpWindow : Window
{
    private bool _recheck;

    public static bool Show(Window owner, RequiredTool tool, Modlist m)
    {
        var w = new ToolHelpWindow(tool, m) { Owner = owner };
        w.ShowDialog();
        return w._recheck;
    }

    /// <summary>For --shots: the content without showing the window.</summary>
    internal static (FrameworkElement Body, Brush Bg, double Width) ForShots(RequiredTool tool, Modlist m)
    {
        var w = new ToolHelpWindow(tool, m);
        var body = (FrameworkElement)w.Content;
        w.Content = null;
        return (body, w.Background, w.Width);
    }

    private ToolHelpWindow(RequiredTool tool, Modlist m)
    {
        Title = Loc.F("tool.help.title", tool.Name);
        Width = 520; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        Background = (Brush)Application.Current.FindResource("Bg");
        FontFamily = (FontFamily)Application.Current.FindResource("UiFont");
        Foreground = Brushes.White;
        var root = new StackPanel { Margin = new Thickness(26, 22, 26, 22) };
        root.Children.Add(new TextBlock { Text = Loc.F("tool.help.title", tool.Name), FontSize = 20, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap });
        var game = m.Game;
        for (var i = 1; i <= 3; i++)
        {
            var line = new DockPanel { Margin = new Thickness(0, i == 1 ? 16 : 10, 0, 0) };
            var n = new Border
            {
                Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Top,
                Background = new SolidColorBrush(Color.FromArgb(0x29, 0x7A, 0xE8, 0xFF)),
                Child = new TextBlock { Text = i.ToString(), FontWeight = FontWeights.Bold, Foreground = (Brush)Application.Current.FindResource("Cyan"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            DockPanel.SetDock(n, Dock.Left);
            line.Children.Add(n);
            line.Children.Add(new TextBlock { Text = Loc.F("tool.help.step" + i, tool.Name, game), FontSize = 14, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Foreground = new SolidColorBrush(Color.FromRgb(0xD9, 0xDA, 0xE6)) });
            root.Children.Add(line);
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        var ok = new Button { Content = Loc.T("tool.help.ok"), Style = (Style)Application.Current.FindResource("SecondaryButton"), Height = 42, Padding = new Thickness(22, 0, 22, 0), Margin = new Thickness(0, 0, 10, 0) };
        ok.Click += (_, _) => Close();
        var again = new Button { Content = Loc.T("tool.recheck"), Style = (Style)Application.Current.FindResource("PrimaryButton"), Height = 42, Padding = new Thickness(22, 0, 22, 0) };
        again.Click += (_, _) => { _recheck = true; Close(); };
        buttons.Children.Add(ok);
        buttons.Children.Add(again);
        root.Children.Add(buttons);
        Content = root;
        Loaded += (_, _) => App.DarkTitleBar(this);
    }
}
