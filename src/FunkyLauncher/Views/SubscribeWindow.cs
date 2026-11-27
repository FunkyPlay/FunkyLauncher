// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FunkyLauncher.Core;
using FunkyLauncher.Services;

namespace FunkyLauncher.Views;

/// <summary>
/// "Modlist installed ✓ — follow us": shown once, after the first successful modlist install. Russian: one button to
/// the Telegram channel; English: YouTube and Instagram. Any button (also "Not now") closes it for good.
/// It sends nothing anywhere — the buttons only open a link in the browser.
/// </summary>
public sealed class SubscribeWindow : Window
{
    public SubscribeWindow(Window owner)
    {
        Owner = owner;
        Title = "Funky Launcher";
        Width = 600;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = (Brush)Application.Current.FindResource("Bg");
        FontFamily = (FontFamily)Application.Current.FindResource("UiFont");
        Foreground = Brushes.White;

        var en = Loc.Lang == "en";
        var root = new StackPanel { Margin = new Thickness(28, 24, 28, 22) };
        root.Children.Add(new TextBlock { Text = Loc.T("subscribe.title"), FontSize = 22, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap });
        root.Children.Add(new TextBlock { Text = Loc.T("subscribe.text"), FontSize = 14.5, LineHeight = 22, Foreground = new SolidColorBrush(Color.FromRgb(0xD6, 0xD6, 0xE2)), Margin = new Thickness(0, 12, 0, 0), TextWrapping = TextWrapping.Wrap });

        var buttons = new WrapPanel { Margin = new Thickness(0, 22, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
        Button Make(string label, bool primary, string? url)
        {
            var b = new Button
            {
                Content = label, Height = 42, Padding = new Thickness(primary ? 24 : 20, 0, primary ? 24 : 20, 0), Margin = new Thickness(10, 0, 0, 0),
                Style = (Style)Application.Current.FindResource(primary ? "PrimaryButton" : "SecondaryButton"),
            };
            b.Click += (_, _) => { if (url != null) PlayService.OpenUrl(url); Close(); };
            buttons.Children.Add(b);
            return b;
        }
        Make(Loc.T("subscribe.later"), false, null);
        if (en)
        {
            Make("YouTube", true, Catalog.YouTube);
            if (Catalog.Social.Instagram.Length > 0) Make("Instagram", true, Catalog.Social.Instagram);
        }
        else Make(Loc.T("subscribe.telegram"), true, AppConfig.TelegramUrl).IsDefault = true;
        root.Children.Add(buttons);
        root.Children.Add(new TextBlock { Text = Loc.T("subscribe.once"), FontSize = 12, Foreground = (Brush)Application.Current.FindResource("Muted"), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) });
        Content = root;
        KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) Close(); };
    }

    /// <summary>After the first successful install: show once, then never again (whatever the player pressed).</summary>
    public static void ShowOnce(MainWindow main)
    {
        var s = main.Settings;
        if (AppConfig.NexusEdition || s.SubscribeShown || main.ShotsMode || !main.IsLoaded || !main.IsVisible) return;
        s.SubscribeShown = true;
        s.Save();
        new SubscribeWindow(main).ShowDialog();
    }
}
