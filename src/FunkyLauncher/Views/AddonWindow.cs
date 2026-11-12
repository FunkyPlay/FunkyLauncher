// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FunkyLauncher.Core;
using FunkyLauncher.Services;

namespace FunkyLauncher.Views;

/// <summary>
/// The 18+ add-on: first the age confirmation (every time it is turned on; "Cancel" is the default), then the
/// choice of levels and the install. Mod names are shown only after the confirmation. No pictures anywhere.
/// </summary>
public sealed class AddonWindow : Window
{
    private const string NexusSettingsUrl = "https://www.nexusmods.com/users/myaccount?tab=content+blocking";

    private readonly Settings _s;
    private readonly Modlist _m;
    private readonly AddonFile _a;
    private readonly StackPanel _body = new();
    private bool _busy;

    public AddonWindow(Settings s, Modlist m, AddonFile a)
    {
        _s = s; _m = m; _a = a;
        Title = Loc.T("addon.title");
        Width = 620; SizeToContent = SizeToContent.Height; MaxHeight = 860;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = (Brush)Application.Current.FindResource("Bg");
        Foreground = (Brush)Application.Current.FindResource("Text");
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = new Border { Padding = new Thickness(28, 24, 28, 24), Child = _body } };
        Closing += (_, e) => { if (_busy) e.Cancel = true; };
        if (AddonService.Prefs(s, a).On) ShowOptions(); else ShowConfirm();
    }

    private TextBlock Text(string t, double size = 13.5, bool bold = false, Brush? color = null) => new()
    {
        Text = t, FontSize = size, FontWeight = bold ? FontWeights.Bold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap,
        Foreground = color ?? (Brush)FindResource("Text"), Margin = new Thickness(0, 0, 0, 12), LineHeight = size * 1.45,
    };

    private Button Btn(string t, bool primary, RoutedEventHandler click)
    {
        var b = new Button
        {
            Content = t, Style = (Style)FindResource(primary ? "PrimaryButton" : "SecondaryButton"),
            Height = primary ? 48 : 42, Padding = new Thickness(18, 0, 18, 0), Margin = new Thickness(0, 0, 10, 0),
        };
        if (primary) b.FontSize = 14;
        b.Click += click;
        return b;
    }

    /// <summary>Step 1: who it is for and what the player confirms (texts from the legal chat).</summary>
    private void ShowConfirm()
    {
        _body.Children.Clear();
        _body.Children.Add(Text(Loc.T("addon.confirm.title"), 22, bold: true));
        _body.Children.Add(Text(Loc.T("addon.confirm.text")));
        _body.Children.Add(new Border
        {
            Background = (Brush)FindResource("PinkSoft"), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 12, 14, 4), Margin = new Thickness(0, 4, 0, 16),
            Child = new StackPanel { Children = { Text(Loc.T("addon.nexus.step"), 12.5), NexusLink() } },
        });
        var cancel = Btn(Loc.T("addon.cancel"), false, (_, _) => Close());
        cancel.IsDefault = true; cancel.IsCancel = true; // Enter and Esc both mean "Cancel"
        var yes = Btn(Loc.T("addon.confirm.yes"), false, (_, _) =>
        {
            AddonService.Prefs(_s, _a).ConfirmedAt = DateTime.Now;
            _s.Save();
            ShowOptions();
        });
        _body.Children.Add(new WrapPanel { Children = { yes, cancel } });
        Loaded += (_, _) => cancel.Focus();
    }

    private TextBlock NexusLink()
    {
        var link = Text(Loc.T("addon.nexus.open") + " →", 13, bold: true, (Brush)FindResource("Cyan"));
        link.Cursor = System.Windows.Input.Cursors.Hand;
        link.MouseLeftButtonUp += (_, _) => PlayService.OpenUrl(NexusSettingsUrl);
        return link;
    }

    /// <summary>Step 2: levels, what will be added, install / turn off.</summary>
    private void ShowOptions()
    {
        _body.Children.Clear();
        var prefs = AddonService.Prefs(_s, _a);
        var states = AddonService.State(_s, _m, _a);
        _body.Children.Add(Text(Loc.T("addon.title"), 22, bold: true));

        var l1 = _a.Mods.Count(x => x.Level == 1);
        var level1 = new CheckBox { Content = Loc.F("addon.level1", l1), IsChecked = true, IsEnabled = false, Margin = new Thickness(0, 0, 0, 8), FontSize = 13.5 };
        var level2 = new CheckBox { Content = Loc.T("addon.level2"), IsChecked = prefs.Level2, Margin = new Thickness(0, 0, 0, 14), FontSize = 13.5 };
        _body.Children.Add(level1);
        if (_a.Mods.Any(x => x.Level == 2)) _body.Children.Add(level2);

        _body.Children.Add(Text(Loc.T("addon.mods"), 13, bold: true));
        var list = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        foreach (var st in states)
        {
            var status = st.Installed && st.Enabled ? Loc.T("addon.mod.ok") : st.Installed ? Loc.T("addon.mod.off") : Loc.T("addon.mod.new");
            list.Children.Add(new TextBlock
            {
                Text = $"{(st.Mod.Level == 2 ? "②" : "①")}  {st.Mod.DisplayName} — {status}",
                FontSize = 12.5, Margin = new Thickness(0, 0, 0, 4), TextWrapping = TextWrapping.Wrap,
                Foreground = st.Installed && st.Enabled ? (Brush)FindResource("Cyan") : (Brush)FindResource("Muted"),
            });
        }
        _body.Children.Add(list);
        _body.Children.Add(Text(Loc.T("addon.hint.premium"), 12.5, color: (Brush)FindResource("Muted")));

        var message = Text("", 13, color: (Brush)FindResource("Muted"));
        var install = Btn(Loc.T("addon.install"), true, async (_, _) => await InstallAsync(level2.IsChecked == true, message));
        var off = Btn(Loc.T("addon.disable"), false, (_, _) =>
        {
            try { AddonService.Disable(_s, _m, _a); ShowOptions(); Status(Loc.T("addon.disabled")); }
            catch (Exception ex) { message.Text = ex.Message; }
        });
        off.Visibility = prefs.On ? Visibility.Visible : Visibility.Collapsed;
        var close = Btn(Loc.T("addon.close"), false, (_, _) => Close());
        close.IsCancel = true;
        _body.Children.Add(new WrapPanel { Margin = new Thickness(0, 4, 0, 12), Children = { install, off, close } });
        _body.Children.Add(message);
        _message = message;
        _buttons = new[] { install, off, close };
        _level2 = level2;
    }

    private TextBlock? _message;
    private Button[] _buttons = Array.Empty<Button>();
    private CheckBox? _level2;

    private void Status(string t) { if (_message != null) _message.Text = t; }

    private async Task InstallAsync(bool level2, TextBlock message)
    {
        if (!_s.IsInstalled(_m)) { message.Text = Loc.T("addon.err.notinstalled"); return; }
        SetBusy(true);
        try
        {
            var progress = new Progress<string>(t => message.Text = t);
            var r = await AddonService.InstallAsync(_s, _m, _a, level2, progress, CancellationToken.None);
            var manualLeft = 0;
            if (r.NeedManual.Count > 0)
            {
                // free Nexus account (or adult content off): the player clicks "Slow download" in the built-in browser
                var win = new ManualDownloadWindow(r.NeedManual, _s.DownloadsDir(_m)) { Owner = this };
                win.ShowDialog();
                if (r.NeedManual.Any(i => i.Done)) r = await AddonService.InstallAsync(_s, _m, _a, level2, progress, CancellationToken.None);
                manualLeft = r.NeedManual.Count;
            }
            ShowOptions();
            var lines = new List<string>();
            if (manualLeft > 0) lines.Add(Loc.F("addon.notdownloaded", manualLeft));
            if (r.Errors.Count > 0) lines.Add(Loc.F("addon.errors", string.Join("\n", r.Errors)));
            Status(lines.Count == 0 ? Loc.F("addon.done", r.Installed) : string.Join("\n\n", lines));
            if (manualLeft > 0) _body.Children.Add(NexusLink());
        }
        catch (Exception ex)
        {
            ShowOptions();
            Status(ex.Message);
        }
        finally { SetBusy(false); }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        foreach (var b in _buttons) b.IsEnabled = !busy;
        if (_level2 != null) _level2.IsEnabled = !busy;
    }
}
