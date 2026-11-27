// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Windows;
using System.Windows.Input;
using FunkyLauncher.Core;
using FunkyLauncher.Services;

namespace FunkyLauncher.Views;

/// <summary>Built-in browser (WebView2) with the launcher's profile — the same Nexus login as manual downloads.</summary>
public partial class BrowserWindow : Window
{
    private readonly string _home;
    private readonly WebGuard _guard;
    private Microsoft.Web.WebView2.Wpf.WebView2 Web => _guard.Web;
    internal bool WebFailed => _guard.Failed;
    internal string? WebSource => Web.CoreWebView2?.Source;

    public BrowserWindow(string home)
    {
        InitializeComponent();
        _home = home;
        Title = "Funky Launcher · " + Loc.T("browser.title");
        // a clear message instead of an empty window when the built-in browser can't work (0.1.17)
        _guard = new WebGuard(WebHost, web =>
        {
            var core = web.CoreWebView2;
            core.NewWindowRequested += (_, e) => { e.Handled = true; core.Navigate(e.Uri); };
            core.SourceChanged += (_, _) => Address.Text = core.Source;
            core.HistoryChanged += (_, _) =>
            {
                BackButton.IsEnabled = core.CanGoBack;
                ForwardButton.IsEnabled = core.CanGoForward;
            };
            core.Navigate(Address.Text.StartsWith("http") ? Address.Text : _home);
            return Task.CompletedTask;
        }, () => Address.Text.StartsWith("http") ? Address.Text : _home);
        Loaded += async (_, _) => await _guard.StartAsync();
    }

    private void Back_Click(object sender, RoutedEventArgs e) { if (Web.CoreWebView2?.CanGoBack == true) Web.CoreWebView2.GoBack(); }
    private void Forward_Click(object sender, RoutedEventArgs e) { if (Web.CoreWebView2?.CanGoForward == true) Web.CoreWebView2.GoForward(); }
    private async void Reload_Click(object sender, RoutedEventArgs e)
    {
        if (_guard.Failed || Web.CoreWebView2 == null) await _guard.RetryAsync(); else Web.CoreWebView2.Reload();
    }
    private void Home_Click(object sender, RoutedEventArgs e) => Web.CoreWebView2?.Navigate(_home);

    private void Address_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Web.CoreWebView2 == null) return;
        var text = Address.Text.Trim();
        if (text.Length == 0) return;
        // a web address or a search
        var url = (text.Contains(' ') || !text.Contains('.')) && !AppConfig.NexusEdition
            ? "https://www.google.com/search?q=" + Uri.EscapeDataString(text)
            : text.StartsWith("http://") || text.StartsWith("https://") ? text : "https://" + text;
        Web.CoreWebView2.Navigate(url);
    }
}
