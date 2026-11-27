// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FunkyLauncher.Core;
using FunkyLauncher.Services;

namespace FunkyLauncher.Views;

/// <summary>
/// 0.1.22 (Funky 24.11): "Mods of the modlist" — every mod of the base with a switch, a search, "As in the modlist".
/// Frameworks the others stand on are locked. Saving rewrites the MO2 profile (the files stay on disk).
/// </summary>
public sealed class ModsWindow : Window
{
    private readonly Modlist _m;
    private readonly Settings _s;
    private readonly List<ModChoices.Item> _items;
    private readonly Dictionary<string, bool> _on;
    private readonly StackPanel _list = new();
    private readonly TextBlock _count = new() { FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBox _search = new() { Height = 36, FontSize = 14, Padding = new Thickness(10, 6, 10, 6) };
    private bool _saved;

    public static bool Show(Window owner, Settings s, Modlist m)
    {
        var w = new ModsWindow(s, m) { Owner = owner };
        w.ShowDialog();
        return w._saved;
    }

    /// <summary>For --shots: the content without showing the window.</summary>
    internal static (FrameworkElement Body, Brush Bg, double Width, double Height) ForShots(Settings s, Modlist m)
    {
        var w = new ModsWindow(s, m);
        var body = (FrameworkElement)w.Content;
        w.Content = null;
        return (body, w.Background, w.Width, w.Height);
    }

    private static Brush R(string key) => (Brush)Application.Current.FindResource(key);

    private ModsWindow(Settings s, Modlist m)
    {
        _s = s; _m = m;
        _items = ModChoices.Items(s, m);
        _on = _items.ToDictionary(i => i.Folder, i => i.On, StringComparer.OrdinalIgnoreCase);
        Title = Loc.F("mods.title", NiceTitle(m.Title));
        Width = 720; Height = 760; ResizeMode = ResizeMode.CanResize; MinWidth = 560; MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        Background = R("Bg");
        FontFamily = (FontFamily)Application.Current.FindResource("UiFont");
        Foreground = Brushes.White;

        var root = new DockPanel { Margin = new Thickness(26, 22, 26, 22) };
        var head = new StackPanel();
        head.Children.Add(new TextBlock { Text = Loc.F("mods.title", NiceTitle(m.Title)), FontSize = 20, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap });
        head.Children.Add(new TextBlock { Text = Loc.T(Bethesda ? "mods.hint.saves" : "mods.hint"), FontSize = 13, Foreground = R("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 12) });
        var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(_count, Dock.Right);
        _count.Margin = new Thickness(12, 0, 0, 0);
        bar.Children.Add(_count);
        var searchHost = new Grid();
        var placeholder = new TextBlock { Text = Loc.T("mods.search"), Foreground = R("Muted"), FontSize = 14, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        _search.TextChanged += (_, _) => { placeholder.Visibility = _search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; Render(); };
        searchHost.Children.Add(_search);
        searchHost.Children.Add(placeholder);
        bar.Children.Add(searchHost);
        head.Children.Add(bar);
        DockPanel.SetDock(head, Dock.Top);
        root.Children.Add(head);

        var buttons = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
        var reset = new Button { Content = Loc.T("mods.reset"), Style = (Style)Application.Current.FindResource("SecondaryButton"), Height = 42, Padding = new Thickness(18, 0, 18, 0) };
        reset.Click += (_, _) => { foreach (var i in _items) _on[i.Folder] = i.DefaultOn || i.Core; Render(); };
        DockPanel.SetDock(reset, Dock.Left);
        buttons.Children.Add(reset);
        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = Loc.T("mods.cancel"), Style = (Style)Application.Current.FindResource("SecondaryButton"), Height = 42, Padding = new Thickness(18, 0, 18, 0), Margin = new Thickness(0, 0, 10, 0) };
        cancel.Click += (_, _) => Close();
        var save = new Button { Content = Loc.T("mods.save"), Style = (Style)Application.Current.FindResource("PrimaryButton"), Height = 42, FontSize = 15, Padding = new Thickness(24, 0, 24, 0) };
        save.Click += (_, _) => SaveClicked();
        right.Children.Add(cancel);
        right.Children.Add(save);
        buttons.Children.Add(right);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        root.Children.Add(new Border
        {
            Background = R("Card"), CornerRadius = new CornerRadius(12), Padding = new Thickness(6),
            Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _list },
        });
        Content = root;
        Render();
    }

    private bool Bethesda => !_m.Game.Contains("cyberpunk", StringComparison.OrdinalIgnoreCase);

    private static string NiceTitle(string t) =>
        t.Length > 0 && t == t.ToUpperInvariant() ? string.Join(' ', t.ToLowerInvariant().Split(' ').Select(w => w.Length > 0 ? char.ToUpperInvariant(w[0]) + w[1..] : w)) : t;

    private void Render()
    {
        _list.Children.Clear();
        var q = _search.Text.Trim();
        foreach (var i in _items)
        {
            if (q.Length > 0 && !i.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase) && !i.Folder.Contains(q, StringComparison.OrdinalIgnoreCase)
                && !i.About.Contains(q, StringComparison.CurrentCultureIgnoreCase) && !i.Author.Contains(q, StringComparison.CurrentCultureIgnoreCase)) continue;
            var on = _on[i.Folder];
            var changed = !i.Core && on != i.DefaultOn;
            var row = new DockPanel { Margin = new Thickness(8, 4, 8, 4) };
            var box = new CheckBox { IsChecked = on, IsEnabled = !i.Core, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            var folder = i.Folder;
            box.Click += (_, _) => { _on[folder] = box.IsChecked == true; Render(); };
            DockPanel.SetDock(box, Dock.Left);
            row.Children.Add(box);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = i.Name, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = changed ? R("Cyan") : on ? Brushes.White : R("Muted") });
            if (i.About.Length > 0)
                text.Children.Add(new TextBlock { Text = i.About, FontSize = 12.5, Foreground = R("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 0) });
            var note = i.Core ? "🔒 " + Loc.T("mods.core") : !i.DefaultOn ? Loc.T("mods.offbydefault") : changed ? Loc.T("mods.changed") : "";
            var line = new WrapPanel { Margin = new Thickness(0, 1, 0, 0) };
            // 0.1.24 (Legal 26.11): every mod belongs to its author — the name from Nexus next to the page link
            if (i.Author.Length > 0) note = Loc.F("mods.author", i.Author) + (note.Length > 0 ? "  ·  " + note : "");
            if (note.Length > 0) line.Children.Add(new TextBlock { Text = note + (i.Url.Length > 0 ? "  ·  " : ""), FontSize = 11.5, Foreground = R("Muted") });
            if (i.Url.Length > 0)
            {
                var url = i.Url;
                var link = new TextBlock { Text = Loc.T("mods.page"), FontSize = 11.5, Foreground = R("Cyan"), Cursor = System.Windows.Input.Cursors.Hand, TextDecorations = TextDecorations.Underline, ToolTip = url };
                link.MouseLeftButtonUp += (_, _) => PlayService.OpenUrl(url);
                line.Children.Add(link);
            }
            if (line.Children.Count > 0) text.Children.Add(line);
            row.Children.Add(text);
            _list.Children.Add(row);
        }
        var total = _items.Count;
        var onCount = _on.Count(x => x.Value);
        var diff = _items.Count(i => !i.Core && _on[i.Folder] != i.DefaultOn);
        _count.Text = Loc.F("mods.count", onCount, total) + (diff > 0 ? " · " + Loc.F("mods.diff", diff) : "");
        _count.Foreground = diff > 0 ? R("Cyan") : R("Muted");
    }

    private void SaveClicked()
    {
        var turnedOff = _items.Where(i => i.HasPlugin && i.On && !_on[i.Folder]).Select(i => i.Name).ToList();
        if (Bethesda && turnedOff.Count > 0
            && Dialog.Show(this, Loc.F("mods.saves.warn", string.Join("\n", turnedOff.Take(8).Select(n => "• " + n))), Title,
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        try
        {
            ModChoices.Save(_s, _m, _on.Select(x => (x.Key, x.Value)));
            _saved = true;
            Close();
        }
        catch (Exception ex)
        {
            Dialog.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
