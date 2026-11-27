// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FunkyLauncher.Core;

namespace FunkyLauncher.Services.Build;

/// <summary>Small WPF helpers for the modlist tools, in the launcher's look. Texts are RU/EN pairs.</summary>
internal static class Ui
{
    public static string T(string ru, string en) => Loc.Lang == "en" ? en : ru;

    public static Brush Brush(string key, byte r, byte g, byte b) =>
        Application.Current?.TryFindResource(key) as Brush ?? new SolidColorBrush(Color.FromRgb(r, g, b));

    public static Brush Muted => Brush("Muted", 0xA3, 0xA3, 0xB5);
    public static Brush Cyan => Brush("Cyan", 0x3C, 0xE6, 0xFF);
    public static Brush Pink => Brush("Pink", 0xFF, 0x3F, 0xD2);
    public static Brush TextBrush => Brush("Text", 0xF2, 0xF2, 0xF7);

    public static TextBlock Title(string text) =>
        new() { Text = text, FontSize = 17, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 4, 0, 4), TextWrapping = TextWrapping.Wrap };

    public static TextBlock Text(string text, double size = 13, Brush? color = null, double top = 0, double bottom = 0) =>
        new() { Text = text, FontSize = size, Foreground = color ?? Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, top, 0, bottom) };

    public static Button Button(string text, Action click, bool primary = false, double height = 32)
    {
        var b = new Button { Content = text, Height = height, Padding = new Thickness(14, 0, 14, 0), Margin = new Thickness(0, 0, 8, 0), FontSize = 13 };
        if (Application.Current?.TryFindResource(primary ? "PrimaryButton" : "SecondaryButton") is Style st) b.Style = st;
        b.Click += (_, _) => click();
        return b;
    }

    public static StackPanel Row(params UIElement[] items)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        foreach (var i in items) p.Children.Add(i);
        return p;
    }

    public static Border Card(UIElement child) => new()
    {
        Child = child, CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 0, 8),
        Background = new SolidColorBrush(Color.FromArgb(0x40, 0x2A, 0x2A, 0x36)),
        BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x36)), BorderThickness = new Thickness(1),
    };

    public static string Size(long bytes) =>
        bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.0} " + T("ГБ", "GB")
        : bytes >= 1L << 20 ? $"{bytes / (double)(1L << 20):0.0} " + T("МБ", "MB")
        : $"{Math.Max(1, bytes / 1024)} " + T("КБ", "KB");

    public static bool Ask(string text, string title = "Funky Launcher") =>
        Views.Dialog.Show(Application.Current?.MainWindow, text, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public static void Info(string text, string title = "Funky Launcher") =>
        Views.Dialog.Show(Application.Current?.MainWindow, text, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public static bool GameRunning(ModlistInfo m)
    {
        var name = System.IO.Path.GetFileNameWithoutExtension(m.GameExe);
        return name.Length > 0 && System.Diagnostics.Process.GetProcessesByName(name).Length > 0;
    }
}
