// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FunkyLauncher.Core;
using FunkyLauncher.Services;

namespace FunkyLauncher.Views;

/// <summary>Small Funky Screen windows: "saved" popup (bottom right) and the recording badge (top). Neither gets into a shot or a video.</summary>
public static class ScreenToast
{
    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x11;

    private static void HideFromCapture(Window w) =>
        w.SourceInitialized += (_, _) => { try { SetWindowDisplayAffinity(new WindowInteropHelper(w).Handle, WDA_EXCLUDEFROMCAPTURE); } catch { } };

    private static Window? _toast;

    private static Window Frame(UIElement content)
    {
        var w = new Window
        {
            WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, ResizeMode = ResizeMode.NoResize,
            Topmost = true, ShowInTaskbar = false, ShowActivated = false, SizeToContent = SizeToContent.WidthAndHeight,
            FontFamily = (FontFamily)Application.Current.FindResource("UiFont"),
            Content = new Border
            {
                Background = (Brush)Application.Current.FindResource("Panel"), BorderBrush = (Brush)Application.Current.FindResource("Pink"),
                BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(14), Padding = new Thickness(16, 14, 16, 14), Child = content,
            },
        };
        HideFromCapture(w);
        return w;
    }

    private static Button Btn(string text, bool primary, Action click)
    {
        var b = new Button
        {
            Content = text, Style = (Style)Application.Current.FindResource(primary ? "PrimaryButton" : "SecondaryButton"),
            Height = 32, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(0, 0, 8, 0), FontSize = 13,
        };
        b.Click += (_, _) => click();
        return b;
    }

    /// <summary>"Saved and copied": picture preview (or video name) and buttons Open / Folder (+ Player / GIF for a video).</summary>
    public static void Saved(string file, bool video, Action<string>? openInPlayer, Action<string>? reportFlaw = null)
    {
        _toast?.Close();
        var body = new StackPanel { Width = 340 };
        body.Children.Add(new TextBlock
        {
            Text = Loc.T(video ? "screen.saved.video" : "screen.saved.shot"), FontSize = 15, FontWeight = FontWeights.Bold,
            Foreground = (Brush)Application.Current.FindResource("Text"),
        });
        if (!video)
            try
            {
                var bi = new BitmapImage();
                bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.DecodePixelWidth = 340; bi.UriSource = new Uri(file); bi.EndInit();
                body.Children.Add(new Image { Source = bi, MaxHeight = 190, Margin = new Thickness(0, 10, 0, 0), Stretch = Stretch.Uniform });
            }
            catch { /* preview is optional */ }
        body.Children.Add(new TextBlock
        {
            Text = Path.GetFileName(file), FontSize = 12, Margin = new Thickness(0, 8, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = (Brush)Application.Current.FindResource("Muted"),
        });
        var status = new TextBlock { FontSize = 12, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.FindResource("Cyan") };
        var row = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        Window? w = null;
        row.Children.Add(Btn(Loc.T("screen.open"), reportFlaw == null || video, () => { FunkyScreen.OpenFile(file); w?.Close(); }));
        row.Children.Add(Btn(Loc.T("screen.folder"), false, () => { FunkyScreen.ShowInFolder(file); w?.Close(); }));
        if (!video && reportFlaw != null) // 0.1.15: a small flaw in a modlist goes to its own queue, not to support
            row.Children.Add(Btn(Loc.T("flaw.toast"), true, () => { reportFlaw(file); w?.Close(); }));
        if (video && openInPlayer != null)
            row.Children.Add(Btn(Loc.T("screen.toplayer"), false, () => { openInPlayer(file); w?.Close(); }));
        if (video)
            row.Children.Add(Btn("GIF", false, async () =>
            {
                status.Text = Loc.T("screen.gif.making");
                try
                {
                    var gif = await FunkyScreen.MakeGifAsync(file);
                    try { Clipboard.SetFileDropList(new System.Collections.Specialized.StringCollection { gif }); } catch { }
                    status.Text = Loc.F("screen.gif.done", Path.GetFileName(gif));
                }
                catch (Exception e) { status.Text = Loc.F("screen.err", e.Message); }
            }));
        body.Children.Add(row);
        body.Children.Add(status);

        w = Frame(body);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(video ? 15 : 7) };
        timer.Tick += (_, _) => { if (!w.IsMouseOver && status.Text != Loc.T("screen.gif.making")) { timer.Stop(); w.Close(); } };
        w.Loaded += (_, _) =>
        {
            var area = SystemParameters.WorkArea;
            w.Left = area.Right - w.ActualWidth - 16;
            w.Top = area.Bottom - w.ActualHeight - 16;
            timer.Start();
        };
        w.Closed += (_, _) => { timer.Stop(); if (_toast == w) _toast = null; };
        _toast = w;
        w.Show();
    }

    /// <summary>A short message near the clock (reminders, backup done…), closes by itself.</summary>
    public static void Message(string title, string text)
    {
        var body = new StackPanel { Width = 340 };
        body.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.FindResource("Text") });
        if (text.Length > 0)
            body.Children.Add(new TextBlock { Text = text, FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), Foreground = (Brush)Application.Current.FindResource("Muted") });
        var w = Frame(body);
        w.MouseLeftButtonUp += (_, _) => w.Close();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        timer.Tick += (_, _) => { if (!w.IsMouseOver) { timer.Stop(); w.Close(); } };
        w.Loaded += (_, _) =>
        {
            var area = SystemParameters.WorkArea;
            w.Left = area.Right - w.ActualWidth - 16;
            w.Top = area.Bottom - w.ActualHeight - 16;
            timer.Start();
        };
        w.Closed += (_, _) => timer.Stop();
        w.Show();
    }

    private static Window? _badge;

    /// <summary>The "● REC 00:12" badge at the top of the main screen with a Stop button. Null hides it.</summary>
    public static void RecordingBadge(Action? stop)
    {
        _badge?.Close(); _badge = null;
        if (stop == null) return;
        var time = new TextBlock { FontSize = 14, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)Application.Current.FindResource("Text") };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock { Text = "●", FontSize = 16, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x3B, 0x5C)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        row.Children.Add(time);
        row.Children.Add(new TextBlock { Text = Loc.T("screen.rec.hint"), FontSize = 12, Foreground = (Brush)Application.Current.FindResource("Muted"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0) });
        row.Children.Add(Btn(Loc.T("screen.rec.stop"), true, stop));
        var w = Frame(row);
        var tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        void Upd() { var s = DateTime.Now - FunkyScreen.RecStarted; time.Text = $"REC {(int)s.TotalMinutes:00}:{s.Seconds:00}"; }
        tick.Tick += (_, _) => Upd();
        Upd();
        w.Loaded += (_, _) =>
        {
            var area = SystemParameters.WorkArea;
            w.Left = area.Left + (area.Width - w.ActualWidth) / 2;
            w.Top = area.Top + 12;
            tick.Start();
        };
        w.Closed += (_, _) => tick.Stop();
        _badge = w;
        w.Show();
    }
}
