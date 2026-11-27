// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using FunkyLauncher.Core;
using FunkyLauncher.Services;
using Microsoft.Win32;

namespace FunkyLauncher.Views;

/// <summary>
/// "Chat with support" inside the launcher: the conversation, a text box, "Attach" (screenshot or error report),
/// "Send". New answers are fetched every 20 s while the window is open. One window at a time.
/// </summary>
public sealed class SupportChatWindow : Window
{
    private static SupportChatWindow? _open;
    private readonly StackPanel _list = new();
    private readonly ScrollViewer _scroll;
    private readonly TextBox _box;
    private readonly TextBlock _status;
    private readonly TextBlock _attachText;
    private readonly Button _send;
    private readonly string _label;
    private string? _attach;
    // 0.1.18: every 90 s, not 20 (Bots, 21.11): the server counts these checks in the same per-IP limit as sending
    // (60 an hour) — with the chat open every 20 s (180 an hour) any message got 429. Support answers within 2 hours.
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(90) };

    /// <summary>Opens (or brings forward) the chat; <paramref name="draft"/> and <paramref name="attach"/> prefill a new message.</summary>
    public static void Open(Window? owner, Settings s, Modlist m, string draft = "", string? attach = null)
    {
        if (_open == null) { _open = new SupportChatWindow(SupportChat.Label(s, m)) { Owner = owner }; _open.Show(); }
        else { _open.Activate(); if (_open.WindowState == WindowState.Minimized) _open.WindowState = WindowState.Normal; }
        if (draft.Length > 0 && _open._box.Text.Length == 0) _open._box.Text = draft;
        if (attach != null) _open.SetAttach(attach);
    }

    /// <summary>--shots: the open chat window.</summary>
    internal static Window? Current => _open;

    private Brush R(string key) => (Brush)Application.Current.FindResource(key);

    private SupportChatWindow(string label)
    {
        _label = label;
        Title = Loc.T("chat.title");
        Width = 560; Height = 680; MinWidth = 420; MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = R("Bg"); Foreground = R("Text");
        if (Application.Current.TryFindResource("UiFont") is FontFamily f) FontFamily = f;

        var root = new DockPanel { Margin = new Thickness(18) };
        var head = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        head.Children.Add(new TextBlock { Text = Loc.T("chat.title"), FontSize = 18, FontWeight = FontWeights.Bold });
        head.Children.Add(new TextBlock { Text = Loc.T("chat.notice"), FontSize = 12, Foreground = R("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        var idRow = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
        var del = new Button { Content = Loc.T("chat.delete"), Height = 28, Padding = new Thickness(10, 0, 10, 0), FontSize = 12, Style = (Style)Application.Current.FindResource("SecondaryButton") };
        del.Click += async (_, _) =>
        {
            if (Dialog.Show(this, Loc.T("chat.delete.confirm"), Loc.T("chat.delete"), MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            try { await SupportChat.DeleteAsync(CancellationToken.None); _list.Children.Clear(); _status.Text = Loc.T("chat.delete.done"); }
            catch (Exception ex) { _status.Text = ex.Message; }
        };
        DockPanel.SetDock(del, Dock.Right);
        idRow.Children.Add(del);
        idRow.Children.Add(new TextBlock { Text = Loc.F("chat.id", SupportChat.InstallId), FontSize = 12, Foreground = R("Muted"), VerticalAlignment = VerticalAlignment.Center });
        head.Children.Add(idRow);
        DockPanel.SetDock(head, Dock.Top);
        root.Children.Add(head);

        var bottom = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        _box = new TextBox
        {
            AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 70, MaxHeight = 160, MaxLength = 4000,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(10, 8, 10, 8), FontSize = 14,
            Background = R("Card"), Foreground = R("Text"), BorderBrush = R("CardActive"), CaretBrush = R("Text"),
        };
        bottom.Children.Add(_box);
        var row = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        _send = new Button { Content = Loc.T("chat.send"), MinWidth = 120, Height = 38, Style = (Style)Application.Current.FindResource("PrimaryButton") };
        _send.Click += async (_, _) => await SendAsync();
        DockPanel.SetDock(_send, Dock.Right);
        row.Children.Add(_send);
        var attach = new Button { Content = Loc.T("chat.attach"), Height = 38, Padding = new Thickness(14, 0, 14, 0), Margin = new Thickness(0, 0, 10, 0), Style = (Style)Application.Current.FindResource("SecondaryButton") };
        attach.Click += (_, _) =>
        {
            try
            {
                var dlg = new OpenFileDialog { Filter = Loc.T("chat.attach.filter") };
                if (dlg.ShowDialog(this) == true) SetAttach(dlg.FileName);
            }
            catch (Exception ex) { AppLog.Write("support chat: file dialog failed: " + ex.Message); _status.Text = Loc.F("chat.err.file", ex.Message); }
        };
        DockPanel.SetDock(attach, Dock.Left);
        row.Children.Add(attach);
        _attachText = new TextBlock { FontSize = 12, Foreground = R("Muted"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        row.Children.Add(_attachText);
        bottom.Children.Add(row);
        // 0.1.17: a screenshot without a file (Win+Shift+S) is pasted, a file can be dropped
        bottom.Children.Add(new TextBlock { Text = Loc.T("chat.paste") + ". " + Loc.T("chat.drop"), FontSize = 12, Foreground = R("Muted"), Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap });
        _status = new TextBlock { FontSize = 12, Foreground = R("Muted"), Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap };
        bottom.Children.Add(_status);
        var tg = new TextBlock { FontSize = 12, Margin = new Thickness(0, 6, 0, 0) };
        var link = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run(Loc.T("chat.telegram"))) { Foreground = R("Cyan") };
        link.Click += (_, _) => PlayService.OpenUrl(AppConfig.SupportBotUrl);
        tg.Inlines.Add(link);
        if (!AppConfig.NexusEdition) bottom.Children.Add(tg); // Nexus version: the chat only, no Telegram link
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);

        _scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _list, Background = R("Panel") };
        root.Children.Add(new Border { CornerRadius = new CornerRadius(12), Background = R("Panel"), Padding = new Thickness(10), Child = _scroll });
        Content = root;

        PreviewKeyDown += OnPreviewKeyDown;
        AllowDrop = true;
        Drop += OnDrop;
        Loaded += async (_, _) => { App.DarkTitleBar(this); await RefreshAsync(); _timer.Start(); };
        _timer.Tick += async (_, _) => await RefreshAsync();
        Closed += (_, _) => { _timer.Stop(); _open = null; };
    }

    /// <summary>
    /// Ctrl+V with a picture in the clipboard (Win+Shift+S, Print Screen: there is no file then): the picture is saved as a PNG
    /// and attached (0.1.17, support: "can't send screenshots"). Also works when the launcher runs as administrator.
    /// </summary>
    private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.V || (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == 0) return;
        try
        {
            if (Clipboard.ContainsFileDropList() && Clipboard.GetFileDropList() is { Count: > 0 } files && File.Exists(files[0]))
            {
                SetAttach(files[0]!);
                e.Handled = true;
                return;
            }
            if (!Clipboard.ContainsImage() || Clipboard.GetImage() is not { } img) return;
            var dir = Path.Combine(AppConfig.DataDir, "chat-pictures");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, $"screenshot-{DateTime.Now:yyyyMMdd-HHmmss}.png");
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(img));
            using (var fs = File.Create(file)) enc.Save(fs);
            SetAttach(file);
            e.Handled = true;
        }
        catch (Exception ex) { AppLog.Write("support chat: paste failed: " + ex.Message); _status.Text = Loc.F("chat.err.file", ex.Message); }
    }

    /// <summary>A file dropped onto the window.</summary>
    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } f && File.Exists(f[0])) SetAttach(f[0]);
    }

    private void SetAttach(string path)
    {
        _attach = path;
        _attachText.Text = "📎 " + Path.GetFileName(path);
    }

    private async Task RefreshAsync()
    {
        var all = await SupportChat.LoadAsync(CancellationToken.None);
        _list.Children.Clear();
        if (all.Count == 0)
            _list.Children.Add(new TextBlock { Text = Loc.T("chat.empty"), Foreground = R("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6) });
        foreach (var m in all) _list.Children.Add(Bubble(m));
        _scroll.ScrollToEnd();
        SupportChat.MarkSeen(all);
    }

    private Border Bubble(ChatMessage m)
    {
        var mine = m.From != "support";
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = mine ? Loc.T("chat.you") : Loc.T("chat.support"), FontSize = 11, FontWeight = FontWeights.Bold, Foreground = mine ? R("Muted") : R("Cyan") });
        body.Children.Add(new TextBlock { Text = m.Text, FontSize = 14, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        if (!string.IsNullOrEmpty(m.File)) body.Children.Add(new TextBlock { Text = "📎 " + m.File, FontSize = 12, Foreground = R("Muted") });
        if (m.Time.Length > 0) body.Children.Add(new TextBlock { Text = m.Time, FontSize = 10.5, Foreground = R("Muted"), Margin = new Thickness(0, 3, 0, 0) });
        return new Border
        {
            Background = mine ? R("CardActive") : R("Card"), CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 8, 12, 8),
            Margin = mine ? new Thickness(60, 4, 4, 4) : new Thickness(4, 4, 60, 4), Child = body,
            HorizontalAlignment = mine ? HorizontalAlignment.Right : HorizontalAlignment.Left,
        };
    }

    /// <summary>
    /// False when the player says nothing about the problem: the prefilled template ends with an empty
    /// "Что случилось:" / "What happened:", or an error report (.zip) is attached with no text at all.
    /// A screenshot alone is fine: it usually shows the problem itself.
    /// </summary>
    internal static bool SaysWhatHappened(string text, string? attach)
    {
        foreach (var lang in new[] { "Что случилось:", "What happened:" })
        {
            var i = text.LastIndexOf(lang, StringComparison.OrdinalIgnoreCase);
            if (i >= 0) return text[(i + lang.Length)..].Trim().Length > 0;
        }
        var isReport = attach != null && Path.GetExtension(attach).Equals(".zip", StringComparison.OrdinalIgnoreCase);
        return !isReport || text.Length > 0;
    }

    private async Task SendAsync()
    {
        var text = _box.Text.Trim();
        if (text.Length == 0 && _attach == null) return;
        // 0.1.18: a report or a "Что случилось:" template with nothing written about the problem is not sent
        if (!SaysWhatHappened(text, _attach))
        {
            _status.Text = Loc.T("chat.err.what");
            _box.Focus();
            _box.CaretIndex = _box.Text.Length;
            return;
        }
        _send.IsEnabled = false;
        _timer.Stop(); // no checks while sending (and while waiting out a 429)
        _status.Text = Loc.T("chat.sending");
        try
        {
            await SupportChat.SendAsync(text, _label, _attach, CancellationToken.None, new Progress<string>(t => _status.Text = t));
            _box.Text = ""; _attach = null; _attachText.Text = "";
            _status.Text = Loc.T("chat.sent");
            await RefreshAsync();
        }
        catch (Exception ex) { _status.Text = ex is InvalidOperationException ? ex.Message : Loc.F("chat.err.send", ex.Message); }
        finally { _send.IsEnabled = true; if (IsVisible) _timer.Start(); } // closed while sending: no more checks
    }
}
