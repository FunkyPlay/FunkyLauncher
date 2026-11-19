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
    // Nexus sometimes answers "Something went wrong… the download link could not be retrieved" instead of the file:
    // its own (temporary) refusal. The player then sees an English error and thinks the modlist is broken — we say what it is.
    private readonly System.Windows.Threading.DispatcherTimer _nexusWatch = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly HashSet<ManualItem> _refused = new();
    private bool _refusalShown;

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
        RetryButton.Content = Loc.Lang == "en" ? "Try again" : "Повторить";
        _nexusWatch.Tick += async (_, _) => await CheckNexusRefusalAsync();
        Closed += (_, _) => _nexusWatch.Stop();
        Loaded += async (_, _) => await StartAsync();
    }

    private async Task StartAsync()
    {
        try
        {
            // the launcher's own browser profile, so the Nexus login is remembered between runs
            await WebProfile.InitAsync(Web);
        }
        catch (Exception ex)
        {
            StatusText.Text = Loc.F("manual.nowebview", ex.Message);
            return;
        }
        Web.CoreWebView2.DownloadStarting += OnDownloadStarting;
        Web.CoreWebView2.NewWindowRequested += (_, e) => { e.Handled = true; Web.CoreWebView2.Navigate(e.Uri); };
        Web.CoreWebView2.SourceChanged += (_, _) => AddressText.Text = Web.CoreWebView2.Source;
        // after logging in Nexus lands on its home page — go back to the current mod's file page
        Web.CoreWebView2.NavigationCompleted += (_, _) =>
        {
            if (_current == null || !Uri.TryCreate(Web.CoreWebView2.Source, UriKind.Absolute, out var u)) return;
            if (u.Host == "www.nexusmods.com" && u.AbsolutePath.Trim('/') is "" or "cyberpunk2077")
                Web.CoreWebView2.Navigate(_current.PageUrl);
        };
        _nexusWatch.Start();
        Next();
    }

    /// <summary>Looks at the page text for Nexus's "could not give the download link" error and explains it in the status line.</summary>
    private async Task CheckNexusRefusalAsync()
    {
        if (_current == null || Web.CoreWebView2 == null || _refusalShown) return;
        string found;
        try
        {
            found = await Web.CoreWebView2.ExecuteScriptAsync(
                "(function(){var t=((document.body&&document.body.innerText)||'').toLowerCase();" +
                "return /something went wrong[\\s\\S]{0,200}download link/.test(t);})()"); // the two phrases together: a mod description alone must not trigger it
        }
        catch { return; }
        if (found != "true") return;
        _refusalShown = true;
        _refused.Add(_current);
        var en = Loc.Lang == "en";
        // the same answer for several mods in a row: Nexus is not giving links right now, a pause helps more than retries
        StatusText.Text = _refused.Count >= 3
            ? (en ? "Nexus is not giving download links right now. The modlist is fine. Press \"Continue later\" and come back in 30–60 minutes — we continue from this place."
                  : "Nexus сейчас не выдаёт ссылки на файлы. Со сборкой всё в порядке. Нажмите «Продолжить позже» и вернитесь через 30–60 минут — продолжим с этого места.")
            : (en ? "Nexus did not give the download link for this file — this is on its side, the modlist is fine. Press \"Try again\" or \"Skip\"; what is downloaded stays."
                  : "Nexus не выдал ссылку на этот файл — это на его стороне, со сборкой всё в порядке. Нажмите «Повторить» или «Пропустить»: скачанное сохранено.");
        RetryButton.Visibility = Visibility.Visible;
    }

    private void ClearRefusal()
    {
        _refusalShown = false;
        RetryButton.Visibility = Visibility.Collapsed;
    }

    private void Retry_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null) return;
        ClearRefusal();
        StatusText.Text = "";
        Web.CoreWebView2.Navigate(_current.PageUrl);
    }

    private void Next()
    {
        ClearRefusal();
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
        // a file on its author's site (not Nexus): a different hint — "press the download button on the page"
        HintText.Text = Loc.T(string.IsNullOrEmpty(_current.Url) ? "manual.hint" : "manual.hint.site");
        Web.CoreWebView2.Navigate(_current.PageUrl);
        UpdateProgress();
        RenderQueue();
    }

    private void OnDownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e)
    {
        var item = _current;
        if (item == null) return;
        // a site with several files for one mod (parts of a RAR, a DLC archive): the file the player clicked decides
        // which item it is — by its name, else the first part whose name is not known yet
        var suggested = Path.GetFileName(e.ResultFilePath);
        if (!string.IsNullOrEmpty(item.Url))
        {
            var pending = _items.Where(i => !i.Done && i.Url == item.Url).ToList();
            item = pending.FirstOrDefault(i => i.ArchiveName.Equals(suggested, StringComparison.OrdinalIgnoreCase))
                   ?? pending.FirstOrDefault(i => i.ArchiveName.Length == 0) ?? item;
            if (item.ArchiveName.Length == 0)
            {
                item.ArchiveName = suggested;
                if (item.Key != null) NameMap.Set(_downloadsDir, item.Key, suggested);
            }
        }
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
                _refused.Clear(); // a file came through: Nexus gives links again
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
        ClearRefusal();
        StatusText.Text = "";
        Web.CoreWebView2.Navigate(_current.PageUrl);
        RenderQueue();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
