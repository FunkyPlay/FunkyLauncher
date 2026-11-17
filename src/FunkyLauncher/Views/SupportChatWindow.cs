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
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(20) };

    /// <summary>Opens (or brings forward) the chat; <paramref name="draft"/> and <paramref name="attach"/> prefill a new message.</summary>
    public static void Open(Window? owner, Settings s, Modlist m, string draft = "", string? attach = null)
    {
        if (_open == null) { _open = new SupportChatWindow(SupportChat.Label(s, m)) { Owner = owner }; _open.Show(); }
        else { _open.Activate(); if (_open.WindowState == WindowState.Minimized) _open.WindowState = WindowState.Normal; }
        if (draft.Length > 0 && _open._box.Text.Length == 0) _open._box.Text = draft;
        if (attach != null) _open.SetAttach(attach);
    }

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
            var dlg = new OpenFileDialog { Filter = Loc.T("chat.attach.filter") };
            if (dlg.ShowDialog(this) == true) SetAttach(dlg.FileName);
        };
        DockPanel.SetDock(attach, Dock.Left);
        row.Children.Add(attach);
        _attachText = new TextBlock { FontSize = 12, Foreground = R("Muted"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        row.Children.Add(_attachText);
        bottom.Children.Add(row);
        _status = new TextBlock { FontSize = 12, Foreground = R("Muted"), Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap };
        bottom.Children.Add(_status);
        var tg = new TextBlock { FontSize = 12, Margin = new Thickness(0, 6, 0, 0) };
        var link = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run(Loc.T("chat.telegram"))) { Foreground = R("Cyan") };
        link.Click += (_, _) => PlayService.OpenUrl(AppConfig.SupportBotUrl);
        tg.Inlines.Add(link);
        bottom.Children.Add(tg);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);

        _scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _list, Background = R("Panel") };
        root.Children.Add(new Border { CornerRadius = new CornerRadius(12), Background = R("Panel"), Padding = new Thickness(10), Child = _scroll });
        Content = root;

        Loaded += async (_, _) => { App.DarkTitleBar(this); await RefreshAsync(); _timer.Start(); };
        _timer.Tick += async (_, _) => await RefreshAsync();
        Closed += (_, _) => { _timer.Stop(); _open = null; };
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

    private async Task SendAsync()
    {
        var text = _box.Text.Trim();
        if (text.Length == 0 && _attach == null) return;
        _send.IsEnabled = false;
        _status.Text = Loc.T("chat.sending");
        try
        {
            await SupportChat.SendAsync(text, _label, _attach, CancellationToken.None);
            _box.Text = ""; _attach = null; _attachText.Text = "";
            _status.Text = Loc.T("chat.sent");
            await RefreshAsync();
        }
        catch (Exception ex) { _status.Text = ex is InvalidOperationException ? ex.Message : Loc.F("chat.err.send", ex.Message); }
        finally { _send.IsEnabled = true; }
    }
}
