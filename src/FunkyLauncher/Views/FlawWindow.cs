// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FunkyLauncher.Core;
using FunkyLauncher.Services;
using Microsoft.Win32;
using static FunkyLauncher.Services.FlawReport.Texts;

namespace FunkyLauncher.Views;

/// <summary>
/// "Report a flaw" window (free): a few words + one screenshot (a file / from the clipboard / the last Funky Screen
/// shot), what the launcher adds is shown under it, Send → "Thank you" with a way to support. Working frame in the
/// support window's style; the final look and texts come from the design and copy team.
/// </summary>
public sealed class FlawWindow : Window
{
    private static FlawWindow? _open;
    private readonly Settings _s;
    private readonly Modlist _m;
    private readonly TextBox _box;
    private readonly Image _preview = new() { MaxHeight = 220, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock _picName;
    private readonly TextBlock _status;
    private readonly Button _send;
    private readonly Button _drop;
    private string? _picture;
    private UIElement? _form;

    /// <summary>Opens (or brings forward) the window for the modlist; <paramref name="picture"/> prefills the screenshot.</summary>
    public static void Open(Window? owner, Settings s, Modlist m, string? picture = null)
    {
        if (_open != null && _open._m.Id != m.Id) { _open.Close(); _open = null; }
        if (_open == null) { _open = new FlawWindow(s, m); if (owner is { IsVisible: true }) _open.Owner = owner; _open.Show(); }
        else { _open.Activate(); if (_open.WindowState == WindowState.Minimized) _open.WindowState = WindowState.Normal; }
        if (picture != null) _open.SetPicture(picture);
    }

    /// <summary>For --shots: the form (with the last Funky Screen shot) and the thank-you screen, not shown on screen.</summary>
    internal static (FrameworkElement Form, FrameworkElement Thanks, Brush Bg, double Width) ForShots(Settings s, Modlist m)
    {
        var w = new FlawWindow(s, m);
        w._box.Text = Loc.Lang == "en" ? "The jacket in the shop is called in English" : "Куртка в магазине называется по-английски";
        w.SetPicture(FlawReport.LastShot());
        var form = (FrameworkElement)w.Content;
        w.Content = null;
        return (form, (FrameworkElement)w.Thanks(), w.Background, w.Width);
    }

    /// <summary>The installed modlist a report is about: the selected one, else the first installed (null = none).</summary>
    public static Modlist? Target(Settings s, Modlist? selected) =>
        selected != null && s.IsInstalled(selected) ? selected : Catalog.Items.FirstOrDefault(x => x.IsReleased && s.IsInstalled(x));

    private static Brush R(string key) => (Brush)Application.Current.FindResource(key);

    private static Button Btn(string text, bool primary, RoutedEventHandler click, double height = 34)
    {
        var b = new Button { Content = text, Height = height, Padding = new Thickness(14, 0, 14, 0), Margin = new Thickness(0, 0, 8, 0), FontSize = 13, Style = (Style)Application.Current.FindResource(primary ? "PrimaryButton" : "SecondaryButton") };
        b.Click += click;
        return b;
    }

    private FlawWindow(Settings s, Modlist m)
    {
        _s = s; _m = m;
        Title = T("Сообщить о недочёте", "Report a flaw");
        Width = 560; SizeToContent = SizeToContent.Height; MinWidth = 440;
        ResizeMode = ResizeMode.CanMinimize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = R("Bg"); Foreground = R("Text");
        if (Application.Current.TryFindResource("UiFont") is FontFamily f) FontFamily = f;

        _box = new TextBox
        {
            AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 80, MaxHeight = 160, MaxLength = FlawReport.MaxText,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(10, 8, 10, 8), FontSize = 14,
            Background = R("Card"), Foreground = R("Text"), BorderBrush = R("CardActive"), CaretBrush = R("Text"),
        };
        _picName = new TextBlock { FontSize = 12, Foreground = R("Muted"), Margin = new Thickness(0, 6, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        _status = new TextBlock { FontSize = 12.5, Foreground = R("Pink"), Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
        _send = Btn(T("Отправить", "Send"), true, async (_, _) => await SendAsync(), 40);
        _send.MinWidth = 140;
        _drop = Btn(T("Убрать снимок", "Remove the screenshot"), false, (_, _) => SetPicture(null), 30);
        Content = _form = Form();
        Loaded += (_, _) => { App.DarkTitleBar(this); _box.Focus(); };
        Closed += (_, _) => { if (_open == this) _open = null; };
        // Ctrl+V anywhere in the window takes a picture from the clipboard
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.V && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control && ClipboardPicture() is { } p)
            { SetPicture(p); e.Handled = true; }
        };
    }

    private UIElement Form()
    {
        var root = new StackPanel { Margin = new Thickness(22, 18, 22, 20) };
        root.Children.Add(new TextBlock { Text = T("Сообщить о недочёте", "Report a flaw") + " — " + _m.Title, FontSize = 18, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap });
        root.Children.Add(new TextBlock
        {
            Text = T("Заметил мелочь — сломанную текстуру, английское название, странный предмет? Пришли пару слов и снимок. Мы разберём и поправим сборку в обновлении.",
                     "Spotted something small — a broken texture, an untranslated name, an odd item? Send a few words and a screenshot. We'll look into it and fix the modlist in an update."),
            FontSize = 13, Foreground = R("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 12),
        });
        root.Children.Add(new TextBlock { Text = T("Что не так", "What's wrong"), FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        root.Children.Add(_box);

        root.Children.Add(new TextBlock { Text = T("Снимок экрана", "Screenshot"), FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 6) });
        var pick = new WrapPanel();
        pick.Children.Add(Btn(T("Выбрать файл…", "Choose a file…"), false, (_, _) =>
        {
            var dlg = new OpenFileDialog { Filter = T("Картинки", "Pictures") + "|*.png;*.jpg;*.jpeg;*.bmp" };
            if (Directory.Exists(FunkyScreen.ShotsDir)) dlg.InitialDirectory = FunkyScreen.ShotsDir;
            if (dlg.ShowDialog(this) == true) SetPicture(dlg.FileName);
        }, 30));
        pick.Children.Add(Btn(T("Из буфера (Ctrl+V)", "From the clipboard (Ctrl+V)"), false, (_, _) =>
        {
            if (ClipboardPicture() is { } p) SetPicture(p); else _status.Text = T("В буфере нет картинки.", "There is no picture in the clipboard.");
        }, 30));
        var last = FlawReport.LastShot();
        if (last != null)
            pick.Children.Add(Btn(T("Последний снимок Funky Скрин", "The last Funky Screen shot"), false, (_, _) => SetPicture(FlawReport.LastShot()), 30));
        root.Children.Add(pick);
        root.Children.Add(new Border { Child = _preview, Margin = new Thickness(0, 8, 0, 0) });
        var picRow = new DockPanel();
        _drop.Visibility = Visibility.Collapsed;
        DockPanel.SetDock(_drop, Dock.Right);
        picRow.Children.Add(_drop);
        picRow.Children.Add(_picName);
        root.Children.Add(picRow);

        root.Children.Add(new TextBlock
        {
            Text = T("Вместе с сообщением уйдёт: ", "Sent with the message: ") + FlawReport.Label(_s, _m) + T(". Больше ничего о ПК и о тебе.", ". Nothing else about your PC or you."),
            FontSize = 12, Foreground = R("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0),
        });
        // consent: the final text comes from Legal
        root.Children.Add(new TextBlock
        {
            Text = T("Нажимая «Отправить», ты передаёшь текст и снимок команде FunkyPlay, чтобы улучшить сборку. Не присылай личные данные. Удалить свои сообщения можно по просьбе в поддержку.",
                     "By pressing Send you give the text and screenshot to the FunkyPlay team to improve the modlist. Don't send personal data. Ask support to delete your messages any time."),
            FontSize = 12, Foreground = R("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0),
        });
        root.Children.Add(_status);
        var row = new DockPanel { Margin = new Thickness(0, 12, 0, 0), LastChildFill = false };
        DockPanel.SetDock(_send, Dock.Right);
        _send.Margin = new Thickness(0);
        row.Children.Add(_send);
        var support = new TextBlock { FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 330 };
        support.Inlines.Add(new System.Windows.Documents.Run(T("Игра не запускается или вылетает? ", "The game won't start or crashes? ")) { Foreground = R("Muted") });
        var link = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run(T("Это в поддержку", "That's for support"))) { Foreground = R("Cyan") };
        link.Click += (_, _) => { PlayService.OpenSupportChat("", _s, _m); Close(); };
        support.Inlines.Add(link);
        DockPanel.SetDock(support, Dock.Left);
        row.Children.Add(support);
        root.Children.Add(row);
        return root;
    }

    private UIElement Thanks()
    {
        var root = new StackPanel { Margin = new Thickness(22, 22, 22, 22) };
        root.Children.Add(new TextBlock { Text = T("Спасибо!", "Thank you!"), FontSize = 22, FontWeight = FontWeights.Bold });
        root.Children.Add(new TextBlock
        {
            Text = T("Мы разбираем такие сообщения и правим сборку в обновлениях. Отвечать на них не будем.\n\nЕсли игра не запускается или вылетает — это в поддержку.",
                     "We go through these messages and fix the modlist in updates. We won't reply to them.\n\nIf the game won't start or crashes — that's for support."),
            FontSize = 14, Foreground = R("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 16),
        });
        var row = new WrapPanel();
        row.Children.Add(Btn(T("Написать в поддержку", "Write to support"), false, (_, _) => { PlayService.OpenSupportChat("", _s, _m); Close(); }, 38));
        row.Children.Add(Btn(T("Ещё один недочёт", "One more flaw"), false, (_, _) => { _box.Text = ""; SetPicture(null); _status.Text = ""; Content = _form; }, 38));
        row.Children.Add(Btn(T("Закрыть", "Close"), true, (_, _) => Close(), 38));
        root.Children.Add(row);
        return root;
    }

    /// <summary>A picture from the clipboard (an image or a copied picture file), saved to a temp PNG.</summary>
    private static string? ClipboardPicture()
    {
        try
        {
            if (Clipboard.ContainsFileDropList())
                foreach (var f in Clipboard.GetFileDropList())
                    if (f != null && new[] { ".png", ".jpg", ".jpeg", ".bmp" }.Contains(Path.GetExtension(f).ToLowerInvariant())) return f;
            if (!Clipboard.ContainsImage() || Clipboard.GetImage() is not { } img) return null;
            var file = Path.Combine(Path.GetTempPath(), $"funky-flaw-{DateTime.Now:yyyyMMdd-HHmmss}.png");
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(img));
            using (var fs = File.Create(file)) enc.Save(fs);
            return file;
        }
        catch { return null; }
    }

    private void SetPicture(string? file)
    {
        _picture = file != null && File.Exists(file) ? file : null;
        _status.Text = "";
        if (_picture == null) { _preview.Source = null; _picName.Text = ""; _drop.Visibility = Visibility.Collapsed; return; }
        try
        {
            var bi = new BitmapImage();
            bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.DecodePixelWidth = 520; bi.UriSource = new Uri(_picture); bi.EndInit();
            _preview.Source = bi;
        }
        catch { _preview.Source = null; }
        _picName.Text = "🖼 " + Path.GetFileName(_picture);
        _drop.Visibility = Visibility.Visible;
    }

    private async Task SendAsync()
    {
        if (_box.Text.Trim().Length == 0 && _picture == null) { _status.Text = T("Напиши пару слов или добавь снимок.", "Write a few words or add a screenshot."); return; }
        _send.IsEnabled = false;
        _status.Foreground = R("Cyan");
        _status.Text = T("Отправляю…", "Sending…");
        try
        {
            await FlawReport.SendAsync(_s, _m, _box.Text, _picture, CancellationToken.None);
            Content = Thanks();
        }
        catch (Exception ex)
        {
            _status.Foreground = R("Pink");
            _status.Text = ex is InvalidOperationException ? ex.Message : T("Не отправилось: ", "Not sent: ") + ex.Message;
        }
        finally { _send.IsEnabled = true; }
    }
}
