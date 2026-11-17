// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FunkyLauncher.Views;

/// <summary>
/// Questions and notices in the launcher's own dark style (instead of the white Windows MessageBox, which looked
/// out of place in videos and screenshots). Same arguments and result as MessageBox.Show.
/// </summary>
public static class Dialog
{
    public static MessageBoxResult Show(Window? owner, string text, string title,
        MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.None,
        MessageBoxResult defaultResult = MessageBoxResult.None)
    {
        var app = Application.Current;
        Brush Res(string key, Brush fallback) => app?.TryFindResource(key) as Brush ?? fallback;
        var result = buttons is MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel ? MessageBoxResult.No
                   : buttons == MessageBoxButton.OKCancel ? MessageBoxResult.Cancel : MessageBoxResult.OK;

        var win = new Window
        {
            Title = title, Owner = owner is { IsLoaded: true } ? owner : null,
            WindowStartupLocation = owner is { IsLoaded: true } ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = owner == null,
            MinWidth = 420, MaxWidth = 620,
            Background = Res("Bg", new SolidColorBrush(Color.FromRgb(0x0F, 0x0F, 0x14))),
            Foreground = Res("Text", Brushes.White),
        };
        if (app?.TryFindResource("UiFont") is FontFamily font) win.FontFamily = font;

        var mark = icon switch
        {
            MessageBoxImage.Warning => "!",
            MessageBoxImage.Error => "×",
            MessageBoxImage.Question => "?",
            MessageBoxImage.Information => "i",
            _ => "",
        };
        var root = new StackPanel { Margin = new Thickness(26, 22, 26, 22) };
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        if (mark.Length > 0)
        {
            var badge = new Border
            {
                Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Margin = new Thickness(0, 0, 12, 0),
                Background = icon is MessageBoxImage.Warning or MessageBoxImage.Error ? Res("Pink", Brushes.HotPink) : Res("CardActive", Brushes.DimGray),
                Child = new TextBlock { Text = mark, FontWeight = FontWeights.Bold, FontSize = 15, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            DockPanel.SetDock(badge, Dock.Left);
            head.Children.Add(badge);
        }
        head.Children.Add(new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
        root.Children.Add(head);
        root.Children.Add(new TextBlock { Text = text, FontSize = 14, LineHeight = 21, TextWrapping = TextWrapping.Wrap, Foreground = Res("Text", Brushes.White) });

        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        void Add(string label, MessageBoxResult r, bool primary)
        {
            var b = new Button { Content = label, MinWidth = 110, Height = 38, Margin = new Thickness(10, 0, 0, 0), Padding = new Thickness(16, 0, 16, 0) };
            if (app?.TryFindResource(primary ? "PrimaryButton" : "SecondaryButton") is Style st) b.Style = st;
            b.Click += (_, _) => { result = r; win.Close(); };
            if (primary) b.IsDefault = true;
            row.Children.Add(b);
        }
        var ru = Core.Loc.Lang != "en";
        switch (buttons)
        {
            case MessageBoxButton.YesNo:
            case MessageBoxButton.YesNoCancel:
                var yesFirst = defaultResult != MessageBoxResult.No;
                Add(ru ? "Да" : "Yes", MessageBoxResult.Yes, yesFirst);
                Add(ru ? "Нет" : "No", MessageBoxResult.No, !yesFirst);
                if (buttons == MessageBoxButton.YesNoCancel) Add(ru ? "Отмена" : "Cancel", MessageBoxResult.Cancel, false);
                break;
            case MessageBoxButton.OKCancel:
                Add("OK", MessageBoxResult.OK, defaultResult != MessageBoxResult.Cancel);
                Add(ru ? "Отмена" : "Cancel", MessageBoxResult.Cancel, defaultResult == MessageBoxResult.Cancel);
                break;
            default:
                Add("OK", MessageBoxResult.OK, true);
                break;
        }
        root.Children.Add(row);
        win.Content = root;
        win.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) win.Close(); };
        win.ShowDialog();
        return result;
    }
}
