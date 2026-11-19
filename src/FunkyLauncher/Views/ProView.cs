// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using FunkyLauncher.Core;
using FunkyLauncher.Services;

namespace FunkyLauncher.Views;

/// <summary>
/// The Funky Pro page (menu item under Library, only with a valid key): the Pro module's tools grouped
/// (Saves / Modlist / Gaming and PC / Recording / Look) as big cards; a card opens that tool's settings.
/// Layout by the design team (approved by Funky 19.11.2026). The tools themselves live in the closed Pro module.
/// </summary>
public sealed class ProView : UserControl
{
    private readonly MainWindow _main;
    private readonly StackPanel _root = new() { Margin = new Thickness(28, 22, 28, 22) };
    private readonly ScrollViewer _scroll;
    private readonly Dictionary<string, FrameworkElement> _built = new();
    private FrameworkElement? _mainPanel;
    private string? _open;

    public static readonly (string Id, string Color)[] Groups =
    {
        ("saves", "#FF3FD2"), ("build", "#B98CFF"), ("play", "#7AE8FF"), ("record", "#FF9ADF"), ("look", "#9AC5F8"), ("other", "#C9C9D6"),
    };

    public ProView(MainWindow main)
    {
        _main = main;
        _scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _root };
        Content = _scroll;
        Loc.Changed += Refresh;
    }

    private sealed record Tool(string Id, string Icon, string Title, string Text, string Group, int Order, Func<bool?>? IsOn, Func<FrameworkElement?> Create);

    private List<Tool> Tools()
    {
        var list = new List<Tool>();
        if (ProModules.Use is { } m)
        {
            var (g, o, on) = ProModules.ToolInfo("main");
            list.Add(new Tool("main", "☁", Loc.T("tools.pro.main"), Loc.T("tools.pro.main.text"), g == "other" ? "saves" : g, g == "other" ? 0 : o, on,
                () => { try { return _mainPanel ??= m.CreatePanel(); } catch (Exception ex) { return new TextBlock { Text = ex.Message }; } }));
        }
        foreach (var p in ProModules.Panels(ProPlace.Tools))
        {
            var (g, o, on) = ProModules.ToolInfo(p.Id);
            list.Add(new Tool(p.Id, p.Icon, p.Title, p.Text, g, o, on, () => _built.TryGetValue(p.Id, out var e) ? e : _built[p.Id] = ProPanels.Build(p)));
        }
        return list;
    }

    public int Count => Tools().Count;

    /// <summary>Opens one tool (null = the grouped list).</summary>
    public void Show(string? toolId) { _open = toolId; Refresh(); _scroll.ScrollToTop(); }

    /// <summary>"Funky Pro" with the brand sign in a frame in place of the letter F. menu = the sidebar item (pink frame with glow, "Pro" cyan).</summary>
    public static FrameworkElement Title(double size, bool menu)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var sign = new Image { Source = new BitmapImage(new Uri("pack://application:,,,/Assets/icon.png")), Width = size * (menu ? 1.05 : 0.95), Height = size * (menu ? 1.05 : 0.95) };
        RenderOptions.SetBitmapScalingMode(sign, BitmapScalingMode.HighQuality);
        var frame = new Border
        {
            Child = sign, CornerRadius = new CornerRadius(size * 0.24), VerticalAlignment = VerticalAlignment.Center, ClipToBounds = true,
            BorderThickness = new Thickness(menu ? 1.5 : 0), Margin = new Thickness(0, 0, 1, 0),
        };
        if (menu)
        {
            frame.SetResourceReference(Border.BorderBrushProperty, "Pink");
            frame.Effect = new DropShadowEffect { Color = Color.FromRgb(0xFF, 0x3F, 0xD2), BlurRadius = 10, ShadowDepth = 0, Opacity = 0.8 };
        }
        row.Children.Add(frame);
        var text = new TextBlock { FontSize = size, VerticalAlignment = VerticalAlignment.Center };
        if (!menu) text.FontFamily = (FontFamily)Application.Current.FindResource("TitleFont");
        text.Inlines.Add(new Run("unky "));
        var pro = new Run("Pro");
        if (menu) pro.SetResourceReference(TextElement.ForegroundProperty, "Cyan");
        text.Inlines.Add(pro);
        row.Children.Add(text);
        return row;
    }

    public void Refresh()
    {
        _root.Children.Clear();
        if (ProModules.Use == null) return;
        var tools = Tools();

        // header: title + line on the left, status on the right
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 18) };
        var (_, info) = ProLicense.Check(_main.Settings.ProKey, DateTime.UtcNow);
        var status = new TextBlock { FontSize = 13.5, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(12, 0, 0, 2) };
        var active = new Run(Loc.F("pro.page.status", info?.Until.ToString("dd.MM.yyyy") ?? ""));
        active.SetResourceReference(TextElement.ForegroundProperty, "Cyan");
        status.Inlines.Add(active);
        status.Inlines.Add(new Run("  ·  " + Loc.F("pro.page.count", tools.Count)) { Foreground = (Brush)FindResource("Muted") });
        DockPanel.SetDock(status, Dock.Right);
        head.Children.Add(status);
        var titles = new StackPanel();
        titles.Children.Add(Title(34, false));
        titles.Children.Add(new TextBlock { Text = Loc.T("pro.page.sub"), FontSize = 14, Foreground = new SolidColorBrush(Color.FromRgb(0xC9, 0xC9, 0xD6)), Margin = new Thickness(0, 6, 0, 0) });
        head.Children.Add(titles);
        _root.Children.Add(head);

        // one tool open
        if (_open != null && tools.FirstOrDefault(t => t.Id == _open) is { } open)
        {
            var back = new Button { Content = Loc.T("pro.page.back"), Style = (Style)FindResource("SecondaryButton"), Height = 36, Padding = new Thickness(14, 0, 14, 0), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 14), FontSize = 13 };
            back.Click += (_, _) => Show(null);
            _root.Children.Add(back);
            var box = new StackPanel();
            var color = Groups.FirstOrDefault(g => g.Id == open.Group).Color ?? "#C9C9D6";
            box.Children.Add(Header(open, color, 52));
            box.Children.Add(new TextBlock { Text = open.Text, FontSize = 13.5, Foreground = (Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 14) });
            var el = open.Create();
            if (el != null)
            {
                if (el.Parent is Panel old) old.Children.Remove(el); // the tool keeps its state between visits
                else if (el.Parent is ContentControl cc) cc.Content = null;
                box.Children.Add(el);
            }
            _root.Children.Add(new Border { Style = (Style)FindResource("CardBorder"), Padding = new Thickness(26, 22, 26, 22), Child = box });
            return;
        }
        _open = null;

        foreach (var (gid, color) in Groups)
        {
            var items = tools.Where(t => t.Group == gid).OrderBy(t => t.Order).ToList();
            if (items.Count == 0) continue;
            var label = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 12) };
            label.Children.Add(new Border { Width = 6, Height = 20, CornerRadius = new CornerRadius(3), Background = Brush(color), Margin = new Thickness(0, 0, 10, 0) });
            label.Children.Add(new TextBlock { Text = Loc.T("pro.group." + gid), FontSize = 17, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
            _root.Children.Add(label);
            var grid = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 14) };
            foreach (var t in items) grid.Children.Add(Card(t, color));
            _root.Children.Add(grid);
        }
    }

    private static SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));

    private DockPanel Header(Tool t, string color, double icon)
    {
        var head = new DockPanel();
        var tile = new Border
        {
            Width = icon, Height = icon, CornerRadius = new CornerRadius(icon * 0.24), Background = Brush(color),
            Child = new TextBlock { Text = t.Icon, FontSize = icon * 0.42, Foreground = Brush("#07070D"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        DockPanel.SetDock(tile, Dock.Left);
        head.Children.Add(tile);
        var names = new StackPanel { Margin = new Thickness(18, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(new TextBlock { Text = t.Title, FontSize = icon > 60 ? 17 : 19, FontWeight = FontWeights.Bold, TextTrimming = TextTrimming.CharacterEllipsis });
        bool? on = null;
        try { on = t.IsOn?.Invoke(); } catch { }
        if (on != null)
        {
            var st = new TextBlock { Text = Loc.T(on == true ? "pro.page.on" : "pro.page.off"), FontSize = 12.5, Margin = new Thickness(0, 3, 0, 0) };
            if (on == true) st.SetResourceReference(TextBlock.ForegroundProperty, "Pink"); else st.Foreground = (Brush)FindResource("Muted");
            names.Children.Add(st);
        }
        head.Children.Add(names);
        return head;
    }

    private Border Card(Tool t, string color)
    {
        var body = new StackPanel();
        body.Children.Add(Header(t, color, 66));
        body.Children.Add(new TextBlock
        {
            Text = t.Text, FontSize = 13, LineHeight = 19, Foreground = new SolidColorBrush(Color.FromRgb(0xC9, 0xC9, 0xD6)),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 0), MaxHeight = 39, TextTrimming = TextTrimming.CharacterEllipsis,
        });
        var card = new Border
        {
            Style = (Style)FindResource("CardBorder"), Padding = new Thickness(26, 22, 26, 22), Margin = new Thickness(0, 0, 16, 16),
            BorderThickness = new Thickness(2), BorderBrush = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand, Child = body,
        };
        card.MouseEnter += (_, _) => card.SetResourceReference(Border.BorderBrushProperty, "Pink");
        card.MouseLeave += (_, _) => card.BorderBrush = Brushes.Transparent;
        card.MouseLeftButtonUp += (_, _) => Show(t.Id);
        return card;
    }
}
