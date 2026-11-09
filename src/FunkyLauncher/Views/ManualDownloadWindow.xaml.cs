// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FunkyLauncher.Core;
using FunkyLauncher.Services;
using Microsoft.Web.WebView2.Core;

namespace FunkyLauncher.Views;

/// <summary>
/// Free Nexus accounts: opens each mod's file page in a built-in browser. The player clicks
/// "Slow download"; the launcher catches the file and saves it into the modlist downloads folder.
/// </summary>
public partial class ManualDownloadWindow : Window
{
    private readonly List<ManualItem> _items;
    private readonly string _downloadsDir;
    private ManualItem? _current;
    private readonly HashSet<ManualItem> _skipped = new();

    public bool AllDone => _items.All(i => i.Done);

    public ManualDownloadWindow(List<ManualItem> items, string downloadsDir)
    {
        InitializeComponent();
        _items = items;
        _downloadsDir = downloadsDir;
        TitleText.Text = Loc.T("manual.title");
        HintText.Text = Loc.T("manual.hint");
        QueueTitle.Text = Loc.T("manual.queue");
        TiredText.Text = Loc.T("manual.tired");
        SkipButton.Content = Loc.T("manual.skip");
        CloseButton.Content = Loc.T("manual.close");
        Loaded += async (_, _) => await StartAsync();
    }

    private async Task StartAsync()
    {
        try
        {
            // a separate browser profile so the Nexus login is remembered between runs
            var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(AppConfig.DataDir, "WebView2"));
            await Web.EnsureCoreWebView2Async(env);
        }
        catch (Exception ex)
        {
            StatusText.Text = Loc.F("manual.nowebview", ex.Message);
            return;
        }
        Web.CoreWebView2.DownloadStarting += OnDownloadStarting;
        Web.CoreWebView2.NewWindowRequested += (_, e) => { e.Handled = true; Web.CoreWebView2.Navigate(e.Uri); };
        Web.CoreWebView2.SourceChanged += (_, _) => AddressText.Text = Web.CoreWebView2.Source;
        Next();
    }

    private void Next()
    {
        _current = _items.FirstOrDefault(i => !i.Done && !_skipped.Contains(i))
                   ?? _items.FirstOrDefault(i => !i.Done);
        if (_current == null)
        {
            StatusText.Text = Loc.T("manual.done");
            UpdateProgress();
            DialogResult = true;
            return;
        }
        _skipped.Remove(_current);
        StatusText.Text = "";
        Web.CoreWebView2.Navigate(_current.PageUrl);
        UpdateProgress();
        RenderQueue();
    }

    private void OnDownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e)
    {
        var item = _current;
        if (item == null) return;
        var target = Path.Combine(_downloadsDir, item.ArchiveName);
        Directory.CreateDirectory(_downloadsDir);
        e.ResultFilePath = target;
        e.Handled = true; // hide the browser's own download bubble
        var op = e.DownloadOperation;
        op.BytesReceivedChanged += (_, _) =>
        {
            var pct = item.Size > 0 ? (int)(op.BytesReceived * 100 / item.Size) : 0;
            StatusText.Text = Loc.F("manual.saving", item.ArchiveName, Math.Min(100, pct));
        };
        op.StateChanged += (_, _) =>
        {
            if (op.State != CoreWebView2DownloadState.Completed) return;
            if (ManualQueue.IsPresent(_downloadsDir, item.ArchiveName, item.Size))
            {
                item.Done = true;
                Next();
            }
            else
            {
                StatusText.Text = Loc.F("manual.wrongsize", Path.GetFileName(op.ResultFilePath));
                try { File.Delete(op.ResultFilePath); } catch { }
            }
        };
    }

    private void UpdateProgress()
    {
        var done = _items.Count(i => i.Done);
        ProgressText.Text = Loc.F("manual.progress", done, _items.Count);
        var pct = _items.Count == 0 ? 100 : done * 100 / _items.Count;
        Percent.Text = pct + "%";
        Fill.Width = ((FrameworkElement)Fill.Parent).ActualWidth * pct / 100.0;
    }

    private void RenderQueue()
    {
        QueueList.Children.Clear();
        var start = Math.Max(0, _items.IndexOf(_current!) - 2);
        foreach (var i in _items.Skip(start).Take(40))
        {
            var isCurrent = i == _current;
            QueueList.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 0, 0, 6),
                Background = isCurrent ? (Brush)FindResource("PinkSoft") : Brushes.Transparent,
                BorderBrush = (Brush)FindResource("Pink"), BorderThickness = new Thickness(isCurrent ? 1 : 0),
                Child = new TextBlock
                {
                    Text = (i.Done ? "✓  " : isCurrent ? "●  " : "○  ") + i.ModName,
                    FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap,
                    Foreground = i.Done ? (Brush)FindResource("Cyan") : isCurrent ? (Brush)FindResource("Text") : (Brush)FindResource("Muted"),
                },
            });
        }
    }

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null) return;
        _skipped.Add(_current);
        var next = _items.FirstOrDefault(i => !i.Done && !_skipped.Contains(i));
        if (next == null) { _skipped.Clear(); next = _items.FirstOrDefault(i => !i.Done); }
        if (next == null) return;
        _current = next;
        Web.CoreWebView2.Navigate(_current.PageUrl);
        RenderQueue();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
