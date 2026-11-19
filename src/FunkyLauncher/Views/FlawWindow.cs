// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FunkyLauncher.Core;
using FunkyLauncher.Services;
using Microsoft.Win32;

namespace FunkyLauncher.Views;

/// <summary>
/// "Report a flaw" window (free, 0.1.15): "What's wrong?" + one screenshot (a file / the clipboard / the last Funky Screen
/// shot), what is sent, a support plate, the consent line, Cancel / Send → "Thanks". Look: the design team's approved
/// mock-up (Бренд\Стиль v4\make_launcher_report.py, 20.11); texts: Copywriter + Legal (Сообщить о недочёте — тексты).
/// </summary>
public sealed class FlawWindow : Window
{
    private static FlawWindow? _open;
    private readonly Settings _s;
    private readonly Modlist _m;
    private readonly TextBox _box;
    private readonly Border _shot;
    private readonly TextBlock _shotHint;
    private readonly TextBlock _status;
    private readonly Button _send;
    private readonly Border _frame;
    private string? _picture;

    private static readonly Brush Line = new SolidColorBrush(Color.FromRgb(0x34, 0x35, 0x4A));
    private static readonly Brush Field = new SolidColorBrush(Color.FromRgb(0x15, 0x15, 0x1C));
    private static readonly Brush Plate = new SolidColorBrush(Color.FromRgb(0x1D, 0x1D, 0x26));
    private static readonly Brush Soft = new SolidColorBrush(Color.FromRgb(0x9D, 0x9E, 0xB0));
    private static readonly Brush Small = new SolidColorBrush(Color.FromRgb(0x8D, 0x8E, 0xA0));
    private static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(0xB4, 0xB5, 0xC4));

    /// <summary>Opens (or brings forward) the window for the modlist; <paramref name="picture"/> prefills the screenshot.</summary>
    public static void Open(Window? owner, Settings s, Modlist m, string? picture = null)
    {
        if (_open != null && _open._m.Id != m.Id) { _open.Close(); _open = null; }
        if (_open == null)
        {
            _open = new FlawWindow(s, m);
            if (owner is { IsVisible: true }) _open.Owner = owner; else _open.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            _open.Show();
        }
        else { _open.Activate(); if (_open.WindowState == WindowState.Minimized) _open.WindowState = WindowState.Normal; }
        if (picture != null) _open.SetPicture(picture);
    }

    /// <summary>The installed modlist a report is about: the selected one, else the first installed (null = none).</summary>
    public static Modlist? Target(Settings s, Modlist? selected) =>
        selected != null && s.IsInstalled(selected) ? selected : Catalog.Items.FirstOrDefault(x => x.IsReleased && s.IsInstalled(x));

    /// <summary>For --shots: the form (with the last Funky Screen shot) and the thank-you screen, not shown on screen.</summary>
    internal static (FrameworkElement Form, FrameworkElement Thanks, Brush Bg, double Width) ForShots(Settings s, Modlist m)
    {
        var w = new FlawWindow(s, m);
        w.SetPicture(FlawReport.LastShot());
        w.Content = null;
        var t = new FlawWindow(s, m);
        t._frame.Child = t.Thanks();
        t._frame.Width = 560;
        t.Content = null;
        return (w._frame, t._frame, R("Bg"), 640 + 16);
    }

    private static Brush R(string key) => (Brush)Application.Current.FindResource(key);

    private static Button Btn(string text, bool primary, RoutedEventHandler click, bool small = false)
    {
        var b = new Button
        {
            Content = text, Height = small ? 34 : 42, Padding = new Thickness(small ? 14 : 22, 0, small ? 14 : 22, 0), FontSize = small ? 13 : 15,
            FontWeight = FontWeights.SemiBold, Style = (Style)Application.Current.FindResource(primary ? "PrimaryButton" : "SecondaryButton"),
        };
        b.Click += click;
        return b;
    }

    private FlawWindow(Settings s, Modlist m)
    {
        _s = s; _m = m;
        Title = Loc.T("flaw.title");
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize; SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Foreground = Brushes.White;
        if (Application.Current.TryFindResource("UiFont") is FontFamily f) FontFamily = f;

        _box = new TextBox
        {
            AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 80, MaxLength = FlawReport.MaxText,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(10, 8, 10, 8), FontSize = 13.5,
            Background = Brushes.Transparent, Foreground = Brushes.White, BorderThickness = new Thickness(0), CaretBrush = Brushes.White,
        };
        _shot = new Border { Width = 156, Height = 88, CornerRadius = new CornerRadius(8), BorderBrush = Line, BorderThickness = new Thickness(1), Background = Field, ClipToBounds = true };
        _shotHint = new TextBlock { FontSize = 11, Foreground = Small, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 6, 0, 0), MaxWidth = 156 };
        _status = new TextBlock { FontSize = 12.5, Foreground = R("Pink"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0), Visibility = Visibility.Collapsed };
        _send = Btn(Loc.T("flaw.send"), true, async (_, _) => await SendAsync());
        _frame = new Border
        {
            Width = 610, Padding = new Thickness(30, 26, 30, 24), Background = new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x16)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x2C, 0x2C, 0x3A)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(18),
            Margin = new Thickness(8),
        };
        _frame.Child = Form();
        Content = _frame;
        MouseLeftButtonDown += (_, e) => { if (e.OriginalSource is Border or StackPanel or TextBlock && e.ButtonState == MouseButtonState.Pressed) try { DragMove(); } catch { } };
        Loaded += (_, _) => _box.Focus();
        Closed += (_, _) => { if (_open == this) _open = null; };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
            else if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control && !_box.IsKeyboardFocused && ClipboardPicture() is { } p) { SetPicture(p); e.Handled = true; }
        };
    }

    private static TextBlock Label(string text, double top) => new() { Text = text, FontSize = 13.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, top, 0, 0) };

    private UIElement Form()
    {
        var root = new StackPanel();
        var head = new TextBlock { FontSize = 23, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap };
        head.Inlines.Add(new System.Windows.Documents.Run(Loc.T("flaw.title")));
        head.Inlines.Add(new System.Windows.Documents.Run(" · " + NiceTitle(_m.Title)) { Foreground = Muted, FontWeight = FontWeights.Normal });
        root.Children.Add(head);

        root.Children.Add(Label(Loc.T("flaw.what"), 18));
        var placeholder = new TextBlock { Text = Loc.T("flaw.example"), FontSize = 13.5, Foreground = new SolidColorBrush(Color.FromRgb(0x77, 0x78, 0x8A)), Margin = new Thickness(12, 9, 12, 0), IsHitTestVisible = false };
        _box.TextChanged += (_, _) => placeholder.Visibility = _box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var field = new Grid();
        field.Children.Add(placeholder);
        field.Children.Add(_box);
        root.Children.Add(new Border { Child = field, Margin = new Thickness(0, 7, 0, 0), CornerRadius = new CornerRadius(10), BorderBrush = Line, BorderThickness = new Thickness(1), Background = Field });

        root.Children.Add(Label(Loc.T("flaw.shot"), 16));
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 7, 0, 0) };
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(_shot);
        left.Children.Add(_shotHint);
        row.Children.Add(left);
        var buttons = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        void Add(Button b) { b.HorizontalAlignment = HorizontalAlignment.Left; b.Margin = new Thickness(0, 0, 0, 7); buttons.Children.Add(b); }
        Add(Btn(Loc.T("flaw.file"), false, (_, _) =>
        {
            var dlg = new OpenFileDialog { Filter = Loc.T("flaw.pictures") + "|*.png;*.jpg;*.jpeg;*.webp;*.bmp" };
            if (Directory.Exists(FunkyScreen.ShotsDir)) dlg.InitialDirectory = FunkyScreen.ShotsDir;
            if (dlg.ShowDialog(this) == true) SetPicture(dlg.FileName);
        }, small: true));
        Add(Btn(Loc.T("flaw.paste"), false, (_, _) =>
        {
            if (ClipboardPicture() is { } p) SetPicture(p); else Status(Loc.T("flaw.paste.none"));
        }, small: true));
        Add(Btn(Loc.T("flaw.last"), false, (_, _) =>
        {
            if (FlawReport.LastShot() is { } p) SetPicture(p); else Status(Loc.T("flaw.last.none"));
        }, small: true));
        row.Children.Add(buttons);
        root.Children.Add(row);
        SetPicture(_picture);

        root.Children.Add(new TextBlock
        {
            Text = Loc.T("flaw.sent"),
            FontSize = 11.5, LineHeight = 17, Foreground = Soft, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0),
        });

        var plate = new DockPanel();
        var sup = Btn(Loc.T("flaw.support"), false, (_, _) => { PlayService.OpenSupportChat("", _s, _m); Close(); }, small: true);
        DockPanel.SetDock(sup, Dock.Right);
        plate.Children.Add(sup);
        plate.Children.Add(new TextBlock { Text = Loc.T("flaw.support.plate"), FontSize = 12.5, Foreground = new SolidColorBrush(Color.FromRgb(0xD9, 0xDA, 0xE6)), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) });
        root.Children.Add(new Border { Child = plate, Background = Plate, CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 9, 9, 9), Margin = new Thickness(0, 12, 0, 0) });

        // Legal 20.11: the consent line next to Send, no tick box
        var agree = new TextBlock { FontSize = 10.5, LineHeight = 15.5, Foreground = Small, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 13, 0, 0) };
        agree.Inlines.Add(new System.Windows.Documents.Run(Loc.T("flaw.agree")));
        var legal = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run("funkylauncher.com/legal")) { Foreground = R("Cyan") };
        legal.Click += (_, _) => PlayService.OpenUrl("https://funkylauncher.com/legal");
        agree.Inlines.Add(legal);
        root.Children.Add(agree);
        root.Children.Add(_status);
        var end = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 15, 0, 0) };
        var cancel = Btn(Loc.T("flaw.cancel"), false, (_, _) => Close());
        cancel.Margin = new Thickness(0, 0, 12, 0);
        end.Children.Add(cancel);
        end.Children.Add(_send);
        root.Children.Add(end);
        return root;
    }

    /// <summary>"NEON NIGHTS" → "Neon Nights" next to the window title (as on the mock-up).</summary>
    private static string NiceTitle(string t) =>
        t.Length > 0 && t == t.ToUpperInvariant() ? string.Join(' ', t.ToLowerInvariant().Split(' ').Select(w => w.Length > 0 ? char.ToUpperInvariant(w[0]) + w[1..] : w)) : t;

    private UIElement Thanks()
    {
        var root = new StackPanel();
        var head = new TextBlock { FontSize = 23, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap };
        head.Inlines.Add(new System.Windows.Documents.Run(Loc.T("flaw.thanks.title")));
        head.Inlines.Add(new System.Windows.Documents.Run("✓") { Foreground = R("Cyan") });
        root.Children.Add(head);
        root.Children.Add(new TextBlock
        {
            Text = Loc.T("flaw.thanks.text"),
            FontSize = 15, LineHeight = 22, Foreground = new SolidColorBrush(Color.FromRgb(0xB9, 0xB9, 0xC8)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 11, 0, 0),
        });
        var end = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 22, 0, 0) };
        var sup = Btn(Loc.T("flaw.support"), false, (_, _) => { PlayService.OpenSupportChat("", _s, _m); Close(); });
        sup.Margin = new Thickness(0, 0, 12, 0);
        end.Children.Add(sup);
        end.Children.Add(Btn(Loc.T("flaw.done"), true, (_, _) => Close()));
        root.Children.Add(end);
        return root;
    }

    /// <summary>A picture from the clipboard (an image or a copied picture file), saved to a temp PNG.</summary>
    private static string? ClipboardPicture()
    {
        try
        {
            if (Clipboard.ContainsFileDropList())
                foreach (var f in Clipboard.GetFileDropList())
                    if (f != null && new[] { ".png", ".jpg", ".jpeg", ".webp", ".bmp" }.Contains(Path.GetExtension(f).ToLowerInvariant())) return f;
            if (!Clipboard.ContainsImage() || Clipboard.GetImage() is not { } img) return null;
            var file = Path.Combine(Path.GetTempPath(), $"funky-flaw-{DateTime.Now:yyyyMMdd-HHmmss}.png");
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(img));
            using (var fs = File.Create(file)) enc.Save(fs);
            return file;
        }
        catch { return null; }
    }

    private void Status(string text)
    {
        _status.Foreground = R("Pink");
        _status.Text = text;
        _status.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Shows the picture before it is sent (Legal: the player sees it and can take it back).</summary>
    private void SetPicture(string? file)
    {
        _picture = file != null && File.Exists(file) ? file : null;
        Status("");
        _shotHint.Inlines.Clear();
        if (_picture == null)
        {
            _shot.Child = new TextBlock { Text = Loc.T("flaw.noshot"), FontSize = 11.5, Foreground = Small, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            return;
        }
        try
        {
            var bi = new BitmapImage();
            bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.DecodePixelWidth = 320; bi.UriSource = new Uri(_picture); bi.EndInit();
            _shot.Child = new Image { Source = bi, Stretch = Stretch.Uniform };
        }
        catch { _shot.Child = null; }
        var drop = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run("✕ " + Loc.T("flaw.remove"))) { Foreground = R("Cyan") };
        drop.Click += (_, _) => SetPicture(null);
        _shotHint.Inlines.Add(drop);
    }

    private async Task SendAsync()
    {
        if (_box.Text.Trim().Length == 0 && _picture == null) { Status(Loc.T("flaw.empty")); return; }
        _send.IsEnabled = false;
        _status.Foreground = R("Cyan"); _status.Text = Loc.T("flaw.sending"); _status.Visibility = Visibility.Visible;
        try
        {
            await FlawReport.SendAsync(_s, _m, _box.Text, _picture, CancellationToken.None);
            _frame.Child = Thanks();
            _frame.Width = 560;
        }
        catch (Exception ex) { Status(ex is InvalidOperationException ? ex.Message : Loc.T("flaw.err.notsent") + ex.Message); }
        finally { _send.IsEnabled = true; }
    }
}
