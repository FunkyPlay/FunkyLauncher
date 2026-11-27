// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FunkyLauncher.Core;
using FunkyLauncher.Services;

namespace FunkyLauncher.Views;

/// <summary>
/// 0.1.23 (Legal 25.11): "Licenses" — the full texts from the repository's licenses\ folder (built into the exe):
/// GPL-3.0, the third-party notices, FunkyPatcher's components. "Open folder"
/// writes them to DataDir\licenses; "Source code" opens the tag of this version on GitHub.
/// </summary>
public sealed class LicensesWindow : Window
{
    /// <summary>Files in the order shown (the first is opened).</summary>
    public static readonly string[] Files =
    {
        "README.txt", "THIRD-PARTY-NOTICES.txt", "GPL-3.0.txt", "FunkyPatcher-NOTICES.txt",
        "WebView2-LICENSE.txt", "WebView2-NOTICE.txt", "dotnet-THIRD-PARTY-NOTICES.txt",
    };

    public static string Text(string file)
    {
        var res = Application.GetResourceStream(new Uri("pack://application:,,,/licenses/" + file));
        if (res == null) return "";
        using var r = new StreamReader(res.Stream);
        return r.ReadToEnd();
    }

    public static string SourceUrl => AppConfig.NexusEdition ? $"{AppConfig.GitHubUrl}/tree/{AppConfig.SourceBranch}" : $"{AppConfig.GitHubUrl}/tree/v{AppConfig.Version.Split('+')[0]}";

    /// <summary>Writes the texts to DataDir\licenses and returns the folder.</summary>
    public static string Extract()
    {
        var dir = Path.Combine(AppConfig.DataDir, "licenses");
        Directory.CreateDirectory(dir);
        foreach (var f in Files) File.WriteAllText(Path.Combine(dir, f), Text(f));
        return dir;
    }

    public static void Open(Window owner) => new LicensesWindow { Owner = owner }.ShowDialog();

    /// <summary>For --shots: the content without showing the window.</summary>
    internal static (FrameworkElement Body, Brush Bg, double Width, double Height) ForShots()
    {
        var w = new LicensesWindow();
        var body = (FrameworkElement)w.Content;
        w.Content = null;
        return (body, w.Background, w.Width, w.Height);
    }

    private LicensesWindow()
    {
        Title = Loc.T("lic.title");
        Width = 900; Height = 640; MinWidth = 640; MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        Background = (Brush)Application.Current.FindResource("Bg");
        FontFamily = (FontFamily)Application.Current.FindResource("UiFont");
        Foreground = Brushes.White;

        var root = new DockPanel { Margin = new Thickness(22, 18, 22, 18) };
        var head = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        head.Children.Add(new TextBlock { Text = Loc.T("lic.title"), FontSize = 20, FontWeight = FontWeights.Bold });
        head.Children.Add(new TextBlock { Text = Loc.T("lic.text"), FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), Foreground = (Brush)Application.Current.FindResource("Muted") });
        DockPanel.SetDock(head, Dock.Top);
        root.Children.Add(head);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        Button B(string key, string style) => new() { Content = Loc.T(key), Style = (Style)Application.Current.FindResource(style), Height = 40, Padding = new Thickness(18, 0, 18, 0), Margin = new Thickness(10, 0, 0, 0) };
        var source = B("lic.source", "SecondaryButton");
        source.Click += (_, _) => { try { PlayService.OpenUrl(SourceUrl); } catch { } };
        var folder = B("lic.folder", "SecondaryButton");
        folder.Click += (_, _) => { try { Process.Start(new ProcessStartInfo("explorer.exe", "\"" + Extract() + "\"") { UseShellExecute = true }); } catch { } };
        var close = B("lic.close", "PrimaryButton");
        close.Click += (_, _) => Close();
        buttons.Children.Add(source); buttons.Children.Add(folder); buttons.Children.Add(close);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        var list = new ListBox { Width = 240, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = Brushes.White, FontSize = 13 };
        foreach (var f in Files) list.Items.Add(f);
        DockPanel.SetDock(list, Dock.Left);
        root.Children.Add(list);
        var text = new TextBox
        {
            IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalAlignment = VerticalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Top, Height = double.NaN,
            AcceptsReturn = true, FontFamily = new FontFamily("Consolas"), FontSize = 12, Margin = new Thickness(12, 0, 0, 0), Padding = new Thickness(10),
            Background = new SolidColorBrush(Color.FromRgb(0x14, 0x15, 0x1E)), Foreground = new SolidColorBrush(Color.FromRgb(0xD9, 0xDA, 0xE6)), BorderThickness = new Thickness(0),
        };
        root.Children.Add(text);
        list.SelectionChanged += (_, _) => { if (list.SelectedItem is string f) { text.Text = Text(f); text.ScrollToHome(); } };
        list.SelectedIndex = 0;

        Content = root;
        Loaded += (_, _) => App.DarkTitleBar(this);
    }
}
