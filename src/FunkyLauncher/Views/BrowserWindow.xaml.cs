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

    public BrowserWindow(string home)
    {
        InitializeComponent();
        _home = home;
        Title = "Funky Launcher · " + Loc.T("browser.title");
        Loaded += async (_, _) =>
        {
            try { await WebProfile.InitAsync(Web); }
            catch (Exception ex) { Address.Text = Loc.F("manual.nowebview", ex.Message); return; }
            Web.CoreWebView2.NewWindowRequested += (_, e) => { e.Handled = true; Web.CoreWebView2.Navigate(e.Uri); };
            Web.CoreWebView2.SourceChanged += (_, _) => Address.Text = Web.CoreWebView2.Source;
            Web.CoreWebView2.HistoryChanged += (_, _) =>
            {
                BackButton.IsEnabled = Web.CoreWebView2.CanGoBack;
                ForwardButton.IsEnabled = Web.CoreWebView2.CanGoForward;
            };
            Web.CoreWebView2.Navigate(_home);
        };
    }

    private void Back_Click(object sender, RoutedEventArgs e) { if (Web.CoreWebView2?.CanGoBack == true) Web.CoreWebView2.GoBack(); }
    private void Forward_Click(object sender, RoutedEventArgs e) { if (Web.CoreWebView2?.CanGoForward == true) Web.CoreWebView2.GoForward(); }
    private void Reload_Click(object sender, RoutedEventArgs e) => Web.CoreWebView2?.Reload();
    private void Home_Click(object sender, RoutedEventArgs e) => Web.CoreWebView2?.Navigate(_home);

    private void Address_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Web.CoreWebView2 == null) return;
        var text = Address.Text.Trim();
        if (text.Length == 0) return;
        // a web address or a search
        var url = text.Contains(' ') || !text.Contains('.')
            ? "https://www.google.com/search?q=" + Uri.EscapeDataString(text)
            : text.StartsWith("http://") || text.StartsWith("https://") ? text : "https://" + text;
        Web.CoreWebView2.Navigate(url);
    }
}
