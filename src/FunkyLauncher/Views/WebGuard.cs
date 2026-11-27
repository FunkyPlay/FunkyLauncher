// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FunkyLauncher.Core;
using FunkyLauncher.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace FunkyLauncher.Views;

/// <summary>
/// Keeps the built-in browser (WebView2) of a window alive and honest (0.1.17, support): when it can't start
/// (no WebView2 runtime, Windows refused), a page did not load, or the browser crashed, the player sees a clear message
/// in the middle of the window with "Try again" and "Open in the normal browser" instead of an empty window.
/// The reason goes into launcher.log (it is in the error report).
/// "Try again" makes a new browser control, so it also helps after the browser process died.
/// </summary>
internal sealed class WebGuard
{
    public enum Fail { NoRuntime, Start, Page, Crash }

    private readonly Panel _host;
    private readonly Func<WebView2, Task> _setup;
    private readonly Func<string?> _url;
    private readonly string _outsideNote;
    private Border? _panel;

    public WebView2 Web { get; private set; }
    public Action? OpenedOutside { get; set; }
    public bool Failed => _panel != null;

    /// <param name="host">An empty Grid where the browser lives.</param>
    /// <param name="setup">Hooks the window's events on a freshly started browser and opens its page.</param>
    /// <param name="url">The page that "Open in the normal browser" opens.</param>
    /// <param name="outsideNote">One more sentence after "open it in the normal browser" (e.g. "the launcher takes the file from Downloads").</param>
    public WebGuard(Panel host, Func<WebView2, Task> setup, Func<string?> url, string outsideNote = "")
    {
        _host = host; _setup = setup; _url = url; _outsideNote = outsideNote;
        Web = NewWeb();
    }

    private WebView2 NewWeb()
    {
        var w = new WebView2 { DefaultBackgroundColor = System.Drawing.Color.FromArgb(0x10, 0x10, 0x16) };
        _host.Children.Insert(0, w);
        return w;
    }

    public async Task<bool> StartAsync()
    {
        try { await WebProfile.InitAsync(Web); }
        catch (Exception ex)
        {
            Show(WebProfile.IsNoRuntime(ex) ? Fail.NoRuntime : Fail.Start, ex.Message);
            return false;
        }
        var core = Web.CoreWebView2;
        core.ProcessFailed += (_, e) =>
        {
            AppLog.Write($"webview2 process failed: {e.ProcessFailedKind} reason {e.Reason} exit {e.ExitCode} {e.ProcessDescription}");
            if (e.ProcessFailedKind is CoreWebView2ProcessFailedKind.BrowserProcessExited)
                WebProfile.Reset();
            if (e.ProcessFailedKind is CoreWebView2ProcessFailedKind.BrowserProcessExited
                or CoreWebView2ProcessFailedKind.RenderProcessExited or CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)
                Show(Fail.Crash, e.ProcessFailedKind.ToString());
        };
        core.NavigationCompleted += (_, e) =>
        {
            if (e.IsSuccess) return;
            AppLog.Write($"webview2 page failed: {e.WebErrorStatus} http {e.HttpStatusCode} {core.Source}");
            // the site answered (e.g. a 403 check page): its own page is shown, that is not ours to cover.
            // OperationCanceled = we opened another page; ConnectionAborted = the page turned into a file download.
            if (e.HttpStatusCode > 0 || e.WebErrorStatus is CoreWebView2WebErrorStatus.OperationCanceled
                    or CoreWebView2WebErrorStatus.ConnectionAborted) return;
            Show(Fail.Page, e.WebErrorStatus.ToString());
        };
        await _setup(Web);
        return true;
    }

    /// <summary>Shows the message instead of the browser (WebView2 is a separate window: anything drawn over it would stay hidden).</summary>
    public void Show(Fail kind, string detail)
    {
        Hide();
        Web.Visibility = Visibility.Collapsed;
        _panel = Panel(kind, detail, _outsideNote,
            () => Open(WebProfile.RuntimeUrl),
            async () => await RetryAsync(),
            () => { if (_url() is { Length: > 0 } u) Open(u); OpenedOutside?.Invoke(); });
        _host.Children.Add(_panel);
    }

    /// <summary>The message itself (also drawn by --shots).</summary>
    internal static Border Panel(Fail kind, string detail, string outsideNote, Action install, Action retry, Action outside)
    {
        var id = kind switch { Fail.NoRuntime => "runtime", Fail.Start => "start", Fail.Page => "page", _ => "crash" };
        var text = Loc.T($"web.fail.{id}.text");
        if (outsideNote.Length > 0) text += " " + outsideNote;
        var box = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        box.Children.Add(new TextBlock { Text = "⚠", FontSize = 34, Foreground = R("Pink"), HorizontalAlignment = HorizontalAlignment.Center });
        box.Children.Add(new TextBlock
        {
            Text = Loc.T($"web.fail.{id}.title"), FontSize = 22, FontWeight = FontWeights.Bold, Foreground = R("Text"), TextWrapping = TextWrapping.Wrap, MaxWidth = 600,
            TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 8, 0, 10),
        });
        box.Children.Add(new TextBlock { Text = text, MaxWidth = 600, FontSize = 14, Foreground = R("Muted"), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, LineHeight = 20 });
        box.Children.Add(new TextBlock
        {
            Text = Loc.F("web.fail.reason", detail), FontSize = 11.5, Foreground = R("Muted"), Opacity = 0.7, TextWrapping = TextWrapping.Wrap, MaxWidth = 600,
            TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 10, 0, 0),
        });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 20, 0, 0) };
        if (kind == Fail.NoRuntime) buttons.Children.Add(Btn(Loc.T("web.fail.install"), "PrimaryButton", install));
        buttons.Children.Add(Btn(Loc.T("web.fail.retry"), kind == Fail.NoRuntime ? "SecondaryButton" : "PrimaryButton", retry));
        buttons.Children.Add(Btn(Loc.T("web.fail.outside"), "SecondaryButton", outside));
        box.Children.Add(buttons);
        return new Border { Background = R("Card"), Padding = new Thickness(28), Child = box };
    }

    private void Hide()
    {
        if (_panel == null) return;
        _host.Children.Remove(_panel);
        _panel = null;
    }

    /// <summary>A new browser control, started again; the window's events are hooked anew by its setup.</summary>
    public async Task RetryAsync()
    {
        Hide();
        AppLog.Write("webview2 try again");
        _host.Children.Remove(Web);
        try { Web.Dispose(); } catch { }
        Web = NewWeb();
        await StartAsync();
    }

    private static void Open(string url)
    {
        try { PlayService.OpenUrl(url); } catch (Exception ex) { AppLog.Write("open url failed: " + ex.Message); }
    }

    private static Button Btn(string text, string style, Action click)
    {
        var b = new Button { Content = text, Height = 40, Padding = new Thickness(18, 0, 18, 0), Margin = new Thickness(5), Style = (Style)Application.Current.FindResource(style) };
        b.Click += (_, _) => click();
        return b;
    }

    private static Brush R(string key) => (Brush)Application.Current.FindResource(key);
}
