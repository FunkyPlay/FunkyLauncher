// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FunkyLauncher.Core;
using FunkyLauncher.Services;
using Microsoft.Win32;

namespace FunkyLauncher.Views;

public partial class SetupView : UserControl
{
    private readonly MainWindow _main;
    private List<CheckItem> _checks = new();
    private CancellationTokenSource? _cts;
    private bool _checking;
    private bool _busy;
    /// <summary>The modlist being installed / checked right now: the page stays on it until the work ends.</summary>
    private Modlist? _busyMl;

    private Modlist Ml => _busy && _busyMl != null ? _busyMl : _main.Selected;

    /// <summary>Something runs on this page (install, update, verify) — for the library to bring the player back here.</summary>
    public bool IsBusy => _busy;
    private bool Installed => _main.Settings.IsInstalled(Ml);
    /// <summary>A modlist folder the launcher did not install — offer "check and use" or "install again", never PLAY.</summary>
    private bool Found => _main.Settings.FoundUnknown(Ml);

    public SetupView(MainWindow main)
    {
        InitializeComponent();
        _main = main;
        Loc.Changed += () => { ApplyTexts(); _ = RunChecksAsync(); };
        ResetForModlist();
    }

    /// <summary>Called when another modlist is selected in the library.</summary>
    public void ResetForModlist()
    {
        // an install is running: keep its page, its progress bar and its log — the player only looked elsewhere
        if (_busy) { ApplyTexts(); return; }
        FolderBox.Text = _main.Settings.InstallDir(Ml);
        ProgressPanel.Visibility = Visibility.Collapsed;
        _checks.Clear();
        ApplyTexts();
    }

    private void Back_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => _main.ShowLibrary();

    public void OnShown()
    {
        ApplyTexts();
        if (!_busy) _ = RunChecksAsync();
    }

    /// <summary>"Repair" from the launch-problem window: verify the files right away.</summary>
    public void StartRepair()
    {
        ApplyTexts();
        if (!_busy) _ = VerifyAsync();
    }

    private bool Updating => Installed && _main.Settings.NeedsUpdate(Ml);

    private void ApplyTexts()
    {
        BackLink.Text = Loc.T("setup.back");
        PageTitle.Text = Loc.F("setup.title", Ml.Title);
        Subtitle.Text = Loc.T(Ml.GameLoaders.Count == 2 ? "setup.subtitle.2" : "setup.subtitle");
        CheckTitle.Text = Loc.T("setup.check");
        RecheckButton.Content = Loc.T("setup.recheck");
        GameFolderButton.Content = Loc.T("setup.gamefolder");
        FolderLabel.Text = Loc.T("setup.folder");
        BrowseButton.Content = Loc.T("setup.browse");
        DownloadsText.Text = Loc.F("setup.downloads", _main.Settings.DownloadsDir(Ml));
        NexusButton.Content = Loc.T("setup.nexus.login");
        CancelButton.Content = Loc.T("setup.cancel");
        ContinueHint.Text = Loc.T("inst.continue");
        InstalledText.Text = Loc.T("setup.installed");
        ReinstallButton.Content = Loc.T("setup.reinstall");
        ApplyMode();
    }

    /// <summary>Not installed: big INSTALL. Installed: big VERIFY FILES + small Reinstall.</summary>
    private void ApplyMode()
    {
        var installed = Installed;
        var updating = Updating;
        var found = Found;
        InstallButton.Content = updating ? Loc.F("setup.update", Ml.Version) : installed ? Loc.T("setup.verify")
            : found ? Loc.T("setup.found.use") : Loc.T("setup.install");
        InstalledNote.Visibility = installed || found ? Visibility.Visible : Visibility.Collapsed;
        InstalledText.Text = updating
            ? Loc.F("setup.update.note", Ml.Version, _main.Settings.UpdateDir(Ml), _main.Settings.InstalledVersion(Ml) ?? "")
            : found ? Loc.F("setup.found", _main.Settings.InstallDir(Ml))
            : Loc.T("setup.installed");
        InstalledText.TextWrapping = TextWrapping.Wrap;
        ReinstallButton.Content = updating ? Loc.T("setup.verify.small") : found ? Loc.T("setup.found.reinstall") : Loc.T("setup.reinstall");
        ReinstallButton.Visibility = (installed || found) && !_busy ? Visibility.Visible : Visibility.Collapsed;
        // installed (and not updating): PLAY is the main action here too; verify becomes a secondary button
        var canPlay = installed && !updating;
        PlayButton.Content = Loc.T("home.play");
        PlayButton.Visibility = canPlay && !_busy ? Visibility.Visible : Visibility.Collapsed;
        InstallButton.Style = (Style)FindResource(canPlay ? "SecondaryButton" : "PrimaryButton");
        var noErrors = _checks.Count > 0 && _checks.All(c => c.State != CheckState.Error);
        InstallButton.IsEnabled = !_busy && Ml.IsReleased && noErrors;
        ReinstallButton.IsEnabled = InstallButton.IsEnabled;
        // already logged in to Nexus (the check list says so): no "Log in" button
        NexusButton.Visibility = WabbajackService.IsNexusLoggedIn ? Visibility.Collapsed : Visibility.Visible;
        // games the launcher finds itself (Starfield outside Steam): the player can show the game folder
        GameFolderButton.Visibility = Mo2Profile.IsNexusList(Ml) && !_busy ? Visibility.Visible : Visibility.Collapsed;
        RenderComposition();
    }

    // ---------- Modlist composition: language and 18+ add-on ----------

    /// <summary>Language picked on this page but not applied yet (installed modlist only).</summary>
    private string? _pendingLang;

    /// <summary>Language the installed modlist has right now: the Russian pack on = "ru".</summary>
    private string AppliedLang(AddonFile ru) => AddonService.Prefs(_main.Settings, ru).On ? "ru" : "en";

    private void RenderComposition()
    {
        var s = _main.Settings;
        var ru = ForThisVersion(Addons.Russian(Ml.Id));
        var adult = ForThisVersion(Addons.Adult(Ml.Id));
        var extras = Addons.Extras(Ml.Id).Select(x => (x.Kind, Pack: ForThisVersion(x.Pack))).Where(x => x.Pack != null).ToList();
        CompositionPanel.Children.Clear();
        var installed = Installed && !Updating;
        if (ru == null && adult == null && extras.Count == 0) { CompositionBox.Visibility = ApplyButton.Visibility = Visibility.Collapsed; return; }
        CompositionBox.Visibility = Visibility.Visible;

        CompositionPanel.Children.Add(new TextBlock
        {
            Text = Loc.T("comp.title"), FontSize = 11, FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("Muted"),
            Margin = new Thickness(0, 0, 0, 10),
        });

        if (ru != null)
        {
            var lang = _pendingLang ?? (installed ? AppliedLang(ru) : s.LangOf(Ml.Id));
            var row = Row(Loc.T("comp.lang"), Loc.T(installed ? "comp.lang.hint.installed" : "comp.lang.hint") + (ru.AiTranslated ? " " + Loc.T("comp.lang.ai") : ""), out var right);
            var seg = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            seg.Children.Add(Segment(Loc.T("comp.lang.ru"), lang == "ru", () => PickLang("ru")));
            seg.Children.Add(Segment(Loc.T("comp.lang.en"), lang == "en", () => PickLang("en")));
            right.Child = new Border { Background = (Brush)FindResource("CardActive"), CornerRadius = new CornerRadius(8), Padding = new Thickness(3), Child = seg };
            CompositionPanel.Children.Add(row);
        }

        if (adult != null)
        {
            if (ru != null) CompositionPanel.Children.Add(Separator());
            var on = AddonService.Prefs(s, adult).On;
            var wanted = installed ? on : s.AdultWanted.Contains(Ml.Id);
            var row = Row(Loc.T("comp.adult"), Loc.T(installed && on ? "comp.adult.hint.on" : "comp.adult.hint"), out var right);
            var box = new CheckBox { IsChecked = wanted, VerticalAlignment = VerticalAlignment.Center, IsEnabled = !_busy };
            box.Click += (_, _) => AdultClicked(adult, box.IsChecked == true);
            right.Child = box;
            CompositionPanel.Children.Add(row);
        }

        // a Russian voice-over we may only link to: a visible button for the Russian version (not hidden in the description)
        var showLang = ru == null ? "" : (_pendingLang ?? (installed ? AppliedLang(ru) : s.LangOf(Ml.Id)));
        if (Ml.VoiceLink.StartsWith("https://") && showLang == "ru")
        {
            CompositionPanel.Children.Add(Separator());
            var row = Row(Loc.T("comp.voice"), Loc.T("comp.voice.link.hint"), out var right);
            var b = new Button
            {
                Content = Loc.T("comp.voice.link"), Style = (Style)FindResource("SecondaryButton"),
                Height = 32, Padding = new Thickness(12, 0, 12, 0), FontSize = 13,
            };
            var url = Ml.VoiceLink;
            b.Click += (_, _) => PlayService.OpenUrl(url);
            right.Child = b;
            CompositionPanel.Children.Add(row);
        }

        // Russian names of items from the mods (made on this PC): what it is, who the authors are, and a rebuild button
        if (showLang == "ru" && TranslationService.Spec(Ml) is { } names)
        {
            CompositionPanel.Children.Add(Separator());
            var row = Row(Loc.T("trans.title"), Loc.T("trans.note"), out var right);
            var rebuild = new Button
            {
                Content = Loc.T("trans.rebuild"), Style = (Style)FindResource("SecondaryButton"),
                Height = 32, Padding = new Thickness(12, 0, 12, 0), FontSize = 13,
                IsEnabled = !_busy && installed && _pendingLang == null,
            };
            rebuild.Click += (_, _) => _ = RebuildNamesAsync();
            right.Child = rebuild;
            CompositionPanel.Children.Add(row);
            if (names.Credits.Count > 0)
            {
                foreach (var c in names.Credits)
                {
                    var line = new TextBlock { FontSize = 12, Foreground = (Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
                    line.Inlines.Add(new System.Windows.Documents.Run(Loc.F("trans.credit", c.Mod, c.Author)));
                    if (c.Url.StartsWith("https://"))
                    {
                        var link = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run(c.Url)) { Foreground = (Brush)FindResource("Cyan") };
                        var url = c.Url;
                        link.Click += (_, _) => PlayService.OpenUrl(url);
                        line.Inlines.Add(link);
                    }
                    line.Inlines.Add(new System.Windows.Documents.Run("." + Loc.T("trans.credit.tail")));
                    CompositionPanel.Children.Add(line);
                }
                CompositionPanel.Children.Add(new TextBlock { Text = Loc.T("trans.credits.general"), FontSize = 12, Foreground = (Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
            }
        }

        var highPack = extras.FirstOrDefault(x => x.Kind == "high").Pack;
        var heavyPack = extras.FirstOrDefault(x => x.Kind == "heavy").Pack;
        foreach (var (kind, pack) in extras)
        {
            if (kind == "high" && heavyPack != null) continue; // drawn together with heavy as Standard / High / Ultra
            CompositionPanel.Children.Add(Separator());
            if (kind == "heavy") { CompositionPanel.Children.Add(highPack != null ? GraphicsRow3(pack!, highPack) : GraphicsRow(pack!)); continue; }
            var on = AddonService.Prefs(s, pack!).On;
            // space on disk: the archive plus what it unpacks to (about the same again), in GB as the cards write it
            var gb = Math.Max(1, (int)Math.Round(pack!.Mods.Sum(x => x.Archive.Size + x.Archives.Sum(p => p.Size)) * 2 / 1e9));
            var row = Row(Loc.T("comp." + kind), Loc.F("comp." + kind + ".hint", gb), out var right);
            var box = new CheckBox { IsChecked = on, VerticalAlignment = VerticalAlignment.Center, IsEnabled = !_busy };
            var p = pack;
            box.Click += (_, _) => _ = ExtraClickedAsync(p, box.IsChecked == true);
            right.Child = box;
            CompositionPanel.Children.Add(row);
        }

        // 0.1.22 (Funky 24.11): single mods on/off without MO2 — once the modlist is on disk
        if (installed && Mo2Profile.IsNexusList(Ml) && Addons.Order(Ml.Id) != null)
        {
            CompositionPanel.Children.Add(Separator());
            var changed = ModChoices.Of(s, Ml).Count;
            var row = Row(Loc.T("mods.row"), changed > 0 ? Loc.F("mods.row.hint.changed", changed) : Loc.T("mods.row.hint"), out var right);
            var open = new Button
            {
                Content = Loc.T("mods.open"), Style = (Style)FindResource("SecondaryButton"),
                Height = 34, Padding = new Thickness(14, 0, 14, 0), FontSize = 13, IsEnabled = !_busy,
            };
            open.Click += (_, _) => { if (ModsWindow.Show(_main, s, Ml)) RenderComposition(); };
            right.Child = open;
            CompositionPanel.Children.Add(row);
        }

        var pending = installed && ru != null && _pendingLang != null && _pendingLang != AppliedLang(ru);
        ApplyButton.Content = Loc.T("comp.apply");
        ApplyButton.Visibility = pending ? Visibility.Visible : Visibility.Collapsed;
        ApplyButton.IsEnabled = !_busy;
    }

    /// <summary>
    /// A pack is made for one modlist version (its mod order and anchors): it is offered only when the catalog's
    /// version — the one installed or about to be installed — matches. Older installs keep what they had.
    /// </summary>
    private AddonFile? ForThisVersion(AddonFile? a)
    {
        if (a == null) return null;
        var v = Installed ? _main.Settings.InstalledVersion(Ml) ?? Ml.Version : Ml.Version;
        return a.ForMainVersion.Length == 0 || a.ForMainVersion == v ? a : null;
    }

    private Grid Row(string title, string hint, out Border right) => Row(title, hint, out right, false);

    /// <summary>stacked: the buttons go under the text on the full width (Funky 24.11: three buttons squeezed the text into a column).</summary>
    private Grid Row(string title, string hint, out Border right, bool stacked)
    {
        if (stacked)
        {
            var sg = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            sg.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            sg.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var top = new StackPanel();
            top.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap });
            top.Children.Add(new TextBlock { Text = hint, FontSize = 12, Foreground = (Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
            sg.Children.Add(top);
            right = new Border { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };
            Grid.SetRow(right, 1);
            sg.Children.Add(right);
            return sg;
        }
        var g = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
        left.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.Bold });
        left.Children.Add(new TextBlock { Text = hint, FontSize = 12, Foreground = (Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
        g.Children.Add(left);
        right = new Border { VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(right, 1);
        g.Children.Add(right);
        return g;
    }

    private Border Separator() => new() { Height = 1, Background = (Brush)FindResource("CardActive"), Margin = new Thickness(0, 10, 0, 10) };

    /// <summary>One half of the Русский / English switch — a real button (mouse-up on a plain Border was lost after the install).</summary>
    private Button Segment(string text, bool selected, Action click)
    {
        var b = new Button
        {
            Content = text, Style = (Style)FindResource(selected ? "PrimaryButton" : "SecondaryButton"),
            Height = 32, MinWidth = 86, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(2, 0, 2, 0),
            FontSize = 13, FontWeight = FontWeights.SemiBold, IsEnabled = !_busy, // Design 21.11: the selected (pink) one as bold as the other
        };
        b.Click += (_, _) => click();
        return b;
    }

    private void PickLang(string lang)
    {
        var ru = ForThisVersion(Addons.Russian(Ml.Id));
        if (Installed && !Updating && ru != null) _pendingLang = lang == AppliedLang(ru) ? null : lang;
        else { _main.Settings.ModlistLang[Ml.Id] = lang; _main.Settings.Save(); }
        RenderComposition();
    }

    private void AdultClicked(AddonFile adult, bool want)
    {
        var s = _main.Settings;
        if (!(Installed && !Updating))
        {
            // before the install: remember the wish; age confirmation and levels come right after the install
            if (want) s.AdultWanted.Add(Ml.Id); else s.AdultWanted.Remove(Ml.Id);
            s.Save();
            RenderComposition();
            return;
        }
        // 0.1.21: switching 18+ off on a started game — saves made with it may lose the character's head (Fallout 4 chat, 24.11)
        if (!want && AddonService.Prefs(s, adult).On
            && Views.Dialog.Show(_main, Loc.T("adult.off.saves"), Ml.Title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            RenderComposition();
            return;
        }
        // installed: the add-on window asks for the age, levels and installs / switches it off
        new AddonWindow(s, Ml, adult) { Owner = _main }.ShowDialog();
        RenderComposition();
    }

    /// <summary>Video memory, read once in the background (WMI can take a second); null = not known yet.</summary>
    private static double? _vramGb;
    private static bool _vramAsked;

    /// <summary>
    /// 0.1.19 "Full graphics / Light" (Funky 21.11: "a light version — a switch with a recommendation"). Light = the
    /// modlist's heavy pack is off. With 8 GB of video memory or less the light version is recommended — a line and the
    /// option marked; the choice stays the player's and nothing is switched by itself.
    /// </summary>
    private Grid GraphicsRow(AddonFile heavy)
    {
        var s = _main.Settings;
        var full = AddonService.Prefs(s, heavy).On;
        var gb = Math.Max(1, (int)Math.Round(heavy.Mods.Sum(x => x.Archive.Size + x.Archives.Sum(p => p.Size)) * 2 / 1e9));
        if (!_vramAsked)
        {
            _vramAsked = true;
            _ = Task.Run(PcCheck.VramGb).ContinueWith(t => Dispatcher.BeginInvoke(() => { _vramGb = t.Result; RenderComposition(); }),
                TaskContinuationOptions.OnlyOnRanToCompletion);
        }
        var recommendLight = _vramGb is > 0 and <= 8.5; // 8 GB cards report a little over 8
        // Design mock-up state 1: before the install, with ≤ 8 GB and no choice of the player yet, Lightweight is picked in
        // advance (an installed modlist never changes by itself)
        if (recommendLight && full && !(Installed && !Updating) && !s.GraphicsChosen.Contains(Ml.Id))
        {
            AddonService.Prefs(s, heavy).On = full = false;
            s.Save();
        }
        // Design mock-up 21.11: a row under the language; the selected option's hint; with ≤ 8 GB a cyan line and the
        // "RECOMMENDED" badge on the light option (plus a cyan frame while it is not selected)
        var row = Row(Loc.T("gfx.title"), Loc.T(full ? "gfx.full.hint" : "gfx.light.hint"), out var right);
        if (recommendLight && row.Children[0] is StackPanel left)
            left.Children.Add(new TextBlock { Text = Loc.F("gfx.recommend", Math.Round(_vramGb!.Value)), FontSize = 12, Foreground = (Brush)FindResource("Cyan"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        var seg = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        seg.Children.Add(Segment(Loc.T("gfx.full"), full, () => { if (!full) _ = GraphicsPickedAsync(heavy, true, gb); }));
        var lite = Segment(Loc.T("gfx.light"), !full, () => { if (full) _ = GraphicsPickedAsync(heavy, false, gb); });
        if (recommendLight && full) { lite.BorderBrush = (Brush)FindResource("Cyan"); lite.BorderThickness = new Thickness(1.5); }
        if (recommendLight)
        {
            var holder = new Grid();
            holder.Children.Add(lite);
            holder.Children.Add(new Border
            {
                Background = (Brush)FindResource("Cyan"), CornerRadius = new CornerRadius(4), Padding = new Thickness(5, 1, 5, 1),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -10, 0, 0),
                IsHitTestVisible = false,
                Child = new TextBlock { Text = Loc.T("gfx.badge"), FontSize = 9, FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("Bg") },
            });
            seg.Children.Add(holder);
        }
        else seg.Children.Add(lite);
        right.Child = new Border { Background = (Brush)FindResource("CardActive"), CornerRadius = new CornerRadius(8), Padding = new Thickness(3, 10, 3, 3), Child = seg };
        return row;
    }

    /// <summary>Installed: asks first (light: the heavy mods go off, files stay; full: they come back, downloaded if missing).</summary>
    private async Task GraphicsPickedAsync(AddonFile heavy, bool full, int gb)
    {
        if (Installed && !Updating && Views.Dialog.Show(_main, full ? Loc.F("gfx.switch.full", gb) : Loc.T("gfx.switch.light"),
                Loc.T("gfx.title"), MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes) != MessageBoxResult.Yes) return;
        _main.Settings.GraphicsChosen.Add(Ml.Id); // the player's own choice: no more picking in advance
        await ExtraClickedAsync(heavy, full);
        if (ProgressText.Text == Loc.T("comp.extra.on") || ProgressText.Text == Loc.T("comp.extra.off"))
            ProgressText.Text = Loc.T(full ? "gfx.done.full" : "gfx.done.light");
    }

    /// <summary>
    /// 0.1.21 (Funky 24.11 via the Cyberpunk chat): three versions — Standard / High / Ultra, Ultra by default.
    /// Ultra = heavy and high on; High = heavy off, high on; Standard = both off. With little video memory a version is
    /// recommended (≤ 6.5 GB Standard, ≤ 8.5 GB High; thresholds to be confirmed by the game chats' measurements).
    /// </summary>
    private Grid GraphicsRow3(AddonFile heavy, AddonFile high)
    {
        var s = _main.Settings;
        var level = AddonService.Prefs(s, heavy).On ? 2 : AddonService.Prefs(s, high).On ? 1 : 0;
        if (!_hwAsked)
        {
            _hwAsked = true;
            var dir = s.InstallDir(Ml);
            _ = Task.Run(() => PcCheck.Read(dir)).ContinueWith(t => Dispatcher.BeginInvoke(() => { _hw = t.Result; RenderComposition(); }),
                TaskContinuationOptions.OnlyOnRanToCompletion);
        }
        // Funky 24.11: not by the video card alone — the weakest part of the PC decides (video memory, RAM, processor, HDD)
        var (recommend, reason) = Recommend(_hw);
        if (recommend is { } r && level > r && !(Installed && !Updating) && !s.GraphicsChosen.Contains(Ml.Id))
        {
            AddonService.Prefs(s, heavy).On = r >= 2;
            AddonService.Prefs(s, high).On = r >= 1;
            s.Save();
            level = r;
        }
        string[] keys = { "gfx3.standard", "gfx3.high", "gfx3.ultra" };
        var row = Row(Loc.T("gfx3.title"), Loc.T("gfx3.load") + " " + Loc.T(keys[level] + ".hint"), out var right, stacked: true);
        if (recommend != null && row.Children[0] is StackPanel left)
            left.Children.Add(new TextBlock { Text = Loc.F("gfx3.recommend", reason, Loc.T(keys[recommend.Value])), FontSize = 12, Foreground = (Brush)FindResource("Cyan"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        var seg = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        for (var i = 0; i < 3; i++)
        {
            var target = i;
            var b = Segment(Loc.T(keys[i]), level == i, () => { if (level != target) _ = GraphicsPicked3Async(heavy, high, target, keys[target]); });
            if (recommend == i && level != i) { b.BorderBrush = (Brush)FindResource("Cyan"); b.BorderThickness = new Thickness(1.5); }
            seg.Children.Add(b);
        }
        right.Child = new Border { Background = (Brush)FindResource("CardActive"), CornerRadius = new CornerRadius(8), Padding = new Thickness(3, 10, 3, 3), Child = seg };
        return row;
    }

    private static HardwareInfo? _hw;
    private static bool _hwAsked;

    /// <summary>The suggested version (0 Standard, 1 High; null = Ultra is fine) and the weakest part that decided it.
    /// Thresholds are a first guess, to be tuned by the game chats' measurements.</summary>
    private static (int? Level, string Reason) Recommend(HardwareInfo? h)
    {
        if (h == null) return (null, "");
        var parts = new List<(int Level, string Why)>();
        if (h.VramGb > 0) parts.Add((h.VramGb <= 6.5 ? 0 : h.VramGb <= 8.5 ? 1 : 2, Loc.F("gfx3.why.vram", Math.Round(h.VramGb))));
        if (h.RamGb > 0) parts.Add((h.RamGb < 12 ? 0 : h.RamGb < 24 ? 1 : 2, Loc.F("gfx3.why.ram", Math.Round(h.RamGb))));
        if (h.Threads > 0) parts.Add((h.Threads < 8 ? 0 : h.Threads < 12 ? 1 : 2, Loc.F("gfx3.why.cpu", h.Threads)));
        if (h.Ssd == false) parts.Add((1, Loc.T("gfx3.why.hdd")));
        if (parts.Count == 0) return (null, "");
        var worst = parts.OrderBy(p => p.Level).First();
        return worst.Level >= 2 ? (null, "") : (worst.Level, worst.Why);
    }

    private async Task GraphicsPicked3Async(AddonFile heavy, AddonFile high, int level, string key)
    {
        if (Installed && !Updating && Views.Dialog.Show(_main, Loc.F("gfx3.switch", Loc.T(key)),
                Loc.T("gfx.title"), MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes) != MessageBoxResult.Yes) return;
        _main.Settings.GraphicsChosen.Add(Ml.Id);
        // turn off first (nothing to download), then on
        if (level < 2 && AddonService.Prefs(_main.Settings, heavy).On) await ExtraClickedAsync(heavy, false);
        if (level < 1 && AddonService.Prefs(_main.Settings, high).On) await ExtraClickedAsync(high, false);
        if (level >= 1 && !AddonService.Prefs(_main.Settings, high).On) await ExtraClickedAsync(high, true);
        if (level >= 2 && !AddonService.Prefs(_main.Settings, heavy).On) await ExtraClickedAsync(heavy, true);
        ProgressText.Text = Loc.F("gfx3.done", Loc.T(key));
    }

    /// <summary>For --shots (the install guide video): a sample PC for the Standard / High / Ultra hint.</summary>
    internal static void ShotHardware(HardwareInfo h) { _hw = h; _hwAsked = true; }

    /// <summary>For --shots: the graphics row with a test pack and a 6 GB card (the light version recommended).</summary>
    internal Grid ShotGraphicsRow(AddonFile heavy, double vramGb, bool full)
    {
        _vramAsked = true;
        _vramGb = vramGb;
        var s = _main.Settings;
        AddonService.Prefs(s, heavy).On = full;
        var chosen = s.GraphicsChosen.Add(Ml.Id); // the state as drawn, without picking in advance
        try { return GraphicsRow(heavy); }
        finally { if (chosen) s.GraphicsChosen.Remove(Ml.Id); }
    }

    /// <summary>HD planets: before the install only the wish is kept; installed — download / switch on, or switch off.</summary>
    private async Task ExtraClickedAsync(AddonFile hd, bool want)
    {
        var s = _main.Settings;
        if (!(Installed && !Updating))
        {
            AddonService.Prefs(s, hd).On = want;
            s.Save();
            RenderComposition();
            return;
        }
        if (want && !DiskSpace.Confirm(_main, s, Ml, new[] { hd })) { RenderComposition(); return; }
        BeginBusy();
        try
        {
            if (!want) { AddonService.Disable(s, Ml, hd); ProgressText.Text = Loc.T("comp.extra.off"); }
            else
            {
                var progress = new Progress<string>(t => { ProgressText.Text = Loc.T("comp.extra.working"); LogLine.Text = t; });
                var r = await AddonService.InstallAsync(s, Ml, hd, level2: false, progress, _cts!.Token);
                if (r.NeedManual.Count > 0)
                {
                    new ManualDownloadWindow(r.NeedManual, s.DownloadsDir(Ml)) { Owner = _main }.ShowDialog();
                    if (r.NeedManual.Any(i => i.Done)) r = await AddonService.InstallAsync(s, Ml, hd, level2: false, progress, _cts!.Token);
                }
                ProgressText.Text = r.Errors.Count > 0 ? Loc.F("addon.errors", string.Join(Environment.NewLine, r.Errors))
                    : r.NeedManual.Any(i => !i.Done) ? Loc.F("comp.lang.notdownloaded", r.NeedManual.Count(i => !i.Done))
                    : Loc.T("comp.extra.on");
            }
        }
        catch (OperationCanceledException) { ProgressText.Text = Loc.T("inst.stopped"); }
        catch (Exception ex) { ProgressText.Text = ex.Message; }
        finally { await EndBusyAsync(); }
    }

    /// <summary>The "Rebuild the Russian names" button: the helper runs again (new mods, a new dictionary, a failed first try).</summary>
    private async Task RebuildNamesAsync()
    {
        var s = _main.Settings;
        BeginBusy();
        try
        {
            ProgressText.Text = Loc.T("trans.building");
            var progress = new Progress<string>(t => LogLine.Text = t);
            var r = await TranslationService.BuildAsync(s, Ml, progress, _cts!.Token);
            ProgressText.Text = r.Skipped ? Loc.T("trans.skipped")
                : r.Error != null ? Loc.F("trans.failed", r.Error)
                : Loc.F("trans.done", r.Made);
        }
        catch (OperationCanceledException) { ProgressText.Text = Loc.T("inst.stopped"); }
        catch (Exception ex) { ProgressText.Text = ex.Message; }
        finally { await EndBusyAsync(); }
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        var ru = ForThisVersion(Addons.Russian(Ml.Id));
        if (ru == null || _pendingLang == null) return;
        var lang = _pendingLang;
        if (lang == "ru" && !DiskSpace.Confirm(_main, _main.Settings, Ml, new[] { ru })) return;
        BeginBusy();
        try
        {
            ProgressText.Text = await ApplyLanguageAsync(ru, lang);
            _main.Settings.ModlistLang[Ml.Id] = lang;
            _main.Settings.Save();
            _pendingLang = null;
        }
        catch (Exception ex) { ProgressText.Text = ex.Message; }
        finally { await EndBusyAsync(); }
    }

    /// <summary>"ru": installs (or switches back on) the Russian pack; "en": switches it off. Returns the text to show.</summary>
    private async Task<string> ApplyLanguageAsync(AddonFile ru, string lang)
    {
        var s = _main.Settings;
        if (lang == "en")
        {
            AddonService.Disable(s, Ml, ru);
            return Loc.T("comp.lang.done.en");
        }
        var progress = new Progress<string>(t => { ProgressText.Text = Loc.T("comp.lang.working"); LogLine.Text = t; });
        var r = await AddonService.InstallAsync(s, Ml, ru, level2: false, progress, CancellationToken.None);
        if (r.NeedManual.Count > 0)
        {
            var win = new ManualDownloadWindow(r.NeedManual, s.DownloadsDir(Ml)) { Owner = _main };
            win.ShowDialog();
            if (r.NeedManual.Any(i => i.Done)) r = await AddonService.InstallAsync(s, Ml, ru, level2: false, progress, CancellationToken.None);
        }
        var lines = new List<string> { Loc.F("comp.lang.done.ru", r.Installed) };
        if (r.NeedManual.Count > 0) lines.Add(Loc.F("comp.lang.notdownloaded", r.NeedManual.Count));
        if (r.Errors.Count > 0) lines.Add(Loc.F("addon.errors", string.Join("\n", r.Errors)));
        return string.Join("\n", lines);
    }

    /// <summary>
    /// Right after a successful install or update (Wabbajack removes everything that is not in the modlist):
    /// puts the Russian pack on if Russian is chosen, and opens the 18+ window if the player asked for it.
    /// </summary>
    private async Task AfterInstallAsync()
    {
        var s = _main.Settings;
        var ru = ForThisVersion(Addons.Russian(Ml.Id));
        if (ru != null)
        {
            var lang = s.LangOf(Ml.Id);
            if (Mo2Profile.IsNexusList(Ml))
                ProgressText.Text = await ApplyLanguageAsync(ru, lang); // mods of the other language stay on disk, switched off
            else
            {
                AddonService.Prefs(s, ru).On = false; // the install just removed the pack's mods
                if (lang == "ru") ProgressText.Text = await ApplyLanguageAsync(ru, "ru");
            }
        }
        // Neon Nights (Wabbajack install wipes extra mods): ticked optional packs go on top again — the Russian
        // voice-over only with the Russian version. Nexus-list modlists got theirs during the install itself.
        if (!Mo2Profile.IsNexusList(Ml))
            foreach (var (kind, pack) in Addons.Extras(Ml.Id))
            {
                var p = ForThisVersion(pack);
                if (p == null || !AddonService.Prefs(s, p).On || (kind == "voice" && s.LangOf(Ml.Id) != "ru")) continue;
                var progress = new Progress<string>(t => { ProgressText.Text = Loc.T("comp.extra.working"); LogLine.Text = t; });
                var r = await AddonService.InstallAsync(s, Ml, p, level2: false, progress, CancellationToken.None);
                if (r.NeedManual.Count > 0)
                {
                    new ManualDownloadWindow(r.NeedManual, s.DownloadsDir(Ml)) { Owner = _main }.ShowDialog();
                    if (r.NeedManual.Any(i => i.Done)) await AddonService.InstallAsync(s, Ml, p, level2: false, progress, CancellationToken.None);
                }
            }
        var adult = ForThisVersion(Addons.Adult(Ml.Id));
        if (adult != null && (s.AdultWanted.Contains(Ml.Id) || AddonService.Prefs(s, adult).On))
        {
            s.AdultWanted.Remove(Ml.Id);
            s.Save();
            new AddonWindow(s, Ml, adult) { Owner = _main }.ShowDialog();
        }
    }

    private async Task RunChecksAsync()
    {
        if (_checking || !Ml.IsReleased) return;
        _checking = true;
        CheckList.Children.Clear();
        CheckList.Children.Add(new TextBlock { Text = Loc.T("check.running"), Foreground = (Brush)FindResource("Muted") });
        InstallButton.IsEnabled = ReinstallButton.IsEnabled = false;
        try
        {
            _checks = await SystemCheck.RunAllAsync(_main.Settings, Ml, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _checks = new List<CheckItem> { new(CheckState.Error, ex.Message) };
        }
        finally
        {
            _checking = false;
        }
        CheckList.Children.Clear();
        foreach (var c in _checks) CheckList.Children.Add(CheckRow(c));
        ApplyMode();
        _main.RefreshNexus();
    }

    private Border CheckRow(CheckItem c)
    {
        if (c.Tool != null || c.Admin != null || c.PickGame || c.Copies != null) return ToolRow(c);
        var error = c.State == CheckState.Error;
        var icon = new Border
        {
            Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Margin = new Thickness(0, 0, 14, 0),
            Background = error ? (Brush)FindResource("Pink") : new SolidColorBrush(Color.FromArgb(0x29, 0x7A, 0xE8, 0xFF)),
            Child = new TextBlock
            {
                Text = c.State switch { CheckState.Ok => "✓", CheckState.Warning => "i", _ => "!" },
                FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Foreground = error ? (Brush)FindResource("OnPink") : (Brush)FindResource("Cyan"),
            },
        };
        var texts = new StackPanel();
        texts.Children.Add(new TextBlock { Text = c.Title, FontWeight = FontWeights.SemiBold, FontSize = 14 });
        if (!string.IsNullOrEmpty(c.Hint))
            texts.Children.Add(new TextBlock { Text = c.Hint, FontSize = 12, Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(0, 2, 0, 0) });
        var row = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        row.Children.Add(icon);
        row.Children.Add(texts);
        return new Border
        {
            Background = (Brush)FindResource(error ? "PinkSoft" : "Card"), CornerRadius = new CornerRadius(12), Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 0, 0, 8),
            BorderThickness = new Thickness(error ? 1 : 0), BorderBrush = (Brush)FindResource("Pink"),
            Child = row,
        };
    }

    /// <summary>0.1.16 (design): a missing required tool — red frame, red "!", the hint, "How to install" and "Check again".</summary>
    private Border ToolRow(CheckItem c)
    {
        // "several copies found" is a notice, not a problem: the launcher's cyan instead of red
        var notice = c.Copies != null;
        var red = notice ? (Brush)FindResource("Cyan") : new SolidColorBrush(Color.FromRgb(0xFF, 0x5A, 0x6A));
        var icon = new Border
        {
            Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center,
            Background = notice ? new SolidColorBrush(Color.FromArgb(0x29, 0x7A, 0xE8, 0xFF)) : new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0x5A, 0x6A)),
            Child = new TextBlock { Text = notice ? "i" : "!", FontWeight = FontWeights.Bold, Foreground = red, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock { Text = c.Title, FontWeight = FontWeights.SemiBold, FontSize = 14, Foreground = red });
        if (!string.IsNullOrEmpty(c.Hint))
            texts.Children.Add(new TextBlock { Text = c.Hint, FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(0xD9, 0xDA, 0xE6)), Margin = new Thickness(0, 3, 0, 0) });
        Button Small(string text, RoutedEventHandler click)
        {
            var b = new Button { Content = text, Style = (Style)FindResource("SecondaryButton"), Height = 32, Padding = new Thickness(12, 0, 12, 0), FontSize = 12.5, Margin = new Thickness(0, 0, 0, 6) };
            b.Click += click;
            return b;
        }
        var buttons = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        if (c.PickGame)
            buttons.Children.Add(Small(Loc.T("check.game.pickbtn"), (_, _) => GameFolder_Click(this, new RoutedEventArgs())));
        else if (c.Copies is { } copies)
            buttons.Children.Add(Small(Loc.T("check.game.copies.btn"), (_, _) => { if (ChooseCopy(copies)) _ = RunChecksAsync(); }));
        else if (c.Admin is { } flag)
            buttons.Children.Add(Small(Loc.T("admin.flag.fix"), (_, _) =>
            {
                var err = AdminFlag.Remove(flag);
                Views.Dialog.Show(_main, err ?? Loc.T("admin.flag.done"), "Funky Launcher", MessageBoxButton.OK,
                    err == null ? MessageBoxImage.Information : MessageBoxImage.Warning);
                _ = RunChecksAsync();
            }));
        else
            buttons.Children.Add(Small(Loc.T("tool.howto"), (_, _) => { if (ToolHelpWindow.Show(_main, c.Tool!, Ml)) _ = RunChecksAsync(); }));
        buttons.Children.Add(Small(Loc.T("tool.recheck"), (_, _) => _ = RunChecksAsync()));
        var row = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        DockPanel.SetDock(buttons, Dock.Right);
        row.Children.Add(icon);
        row.Children.Add(buttons);
        row.Children.Add(texts);
        return new Border
        {
            Background = (Brush)FindResource("Card"), CornerRadius = new CornerRadius(12), Padding = new Thickness(16, 12, 14, 6), Margin = new Thickness(0, 0, 0, 8),
            BorderThickness = new Thickness(1.5), BorderBrush = red, Child = row,
        };
    }

    /// <summary>For --shots: the "REDmod is not installed" row with test data (the PC itself may have REDmod).</summary>
    internal Border ShotToolRow(RequiredTool tool) =>
        ToolRow(new CheckItem(CheckState.Error, Loc.F("check.tool.no", tool.Name), SystemCheck.ToolHint(tool), tool));

    /// <summary>For --shots: the "Run as administrator" row with test data (the button does nothing here).</summary>
    internal Border ShotAdminRow(string exe, string gameExe) =>
        ToolRow(new CheckItem(CheckState.Warning, Loc.F("admin.flag.title", gameExe),
            Loc.T("admin.flag.hint") + "\n" + AdminFlag.Steps(new AdminFlag.Found(exe, true, false)), Admin: new AdminFlag.Found("", false, false)));

    private void Recheck_Click(object sender, RoutedEventArgs e) => _ = RunChecksAsync();

    private void GameFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = Loc.F("setup.gamefolder.title", Ml.Game, Ml.GameExe) };
        if (dlg.ShowDialog() != true) return;
        if (!GameFolders.Pick(Ml, dlg.FolderName))
        {
            // 0.1.19: the exe one or two folders deeper (a library folder, "Games") — take that folder by itself
            if (GameFolders.FindBelow(Ml, dlg.FolderName) is { } below && GameFolders.Pick(Ml, below))
                Views.Dialog.Show(_main, Loc.F("setup.gamefolder.below", Ml.GameExe, below), "Funky Launcher", MessageBoxButton.OK, MessageBoxImage.Information);
            else
                Views.Dialog.Show(_main, Loc.F("setup.gamefolder.bad", Ml.GameExe) + GameFolders.Contents(dlg.FolderName), Loc.F("check.game.no", Ml.Game),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        _ = RunChecksAsync();
    }

    /// <summary>Several copies of the game: the player picks one (a button each: folder and version).</summary>
    private bool ChooseCopy(List<FoundGame> copies)
    {
        var win = new Window
        {
            Title = Loc.F("check.game.copies", copies.Count, Ml.Game), Owner = _main, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, MaxWidth = 720,
            Background = (Brush)FindResource("Bg"), Foreground = (Brush)FindResource("Text"),
        };
        var picked = false;
        var root = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
        root.Children.Add(new TextBlock { Text = Loc.F("check.game.copies.pick", Ml.Game), FontSize = 15, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        foreach (var g in copies)
        {
            var right = LaunchWatcher.SameGameVersion(g.Version, Ml.GameVersion);
            var b = new Button
            {
                Style = (Style)FindResource(right ? "PrimaryButton" : "SecondaryButton"), Margin = new Thickness(0, 0, 0, 8),
                Padding = new Thickness(14, 8, 14, 8), HorizontalContentAlignment = HorizontalAlignment.Left, Height = double.NaN,
                Content = new TextBlock { Text = $"{g.Dir}\n{Loc.F("check.game.copies.version", g.Version)}", TextWrapping = TextWrapping.Wrap },
            };
            var dir = g.Dir;
            b.Click += (_, _) => { picked = GameFolders.Pick(Ml, dir); win.Close(); };
            root.Children.Add(b);
        }
        win.Content = root;
        win.ShowDialog();
        return picked;
    }

    /// <summary>For --shots: the red "game not found" row (the PC itself may have the game).</summary>
    internal Border ShotGameMissingRow(Modlist ml) =>
        ToolRow(new CheckItem(CheckState.Error, Loc.F("check.game.no", ml.Game), Loc.F("check.game.pick", ml.GameExe), PickGame: true));

    /// <summary>For --shots: "Found 2 copies" with test folders.</summary>
    internal Border ShotCopiesRow(Modlist ml) =>
        ToolRow(new CheckItem(CheckState.Warning, Loc.F("check.game.copies", 2, ml.Game), Loc.F("check.game.copies.hint", @"D:\Games\Starfield", ml.GameVersion),
            Copies: new() { new FoundGame(@"D:\Games\Starfield", ml.GameVersion), new FoundGame(@"E:\Games\Starfield", "1.15.222") }));

    private void ApplyFolder(string dir)
    {
        _main.Settings.SetInstallDir(Ml, dir);
        FolderBox.Text = _main.Settings.InstallDir(Ml);
        DownloadsText.Text = Loc.F("setup.downloads", _main.Settings.DownloadsDir(Ml));
        _ = RunChecksAsync();
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = Loc.T("setup.folder") };
        if (dlg.ShowDialog() == true) ApplyFolder(dlg.FolderName);
    }

    private void FolderBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (FolderBox.Text.Trim() != _main.Settings.InstallDir(Ml)) ApplyFolder(FolderBox.Text);
    }

    private void Nexus_Click(object sender, RoutedEventArgs e)
    {
        if (!NexusLoginWindow.Ask(_main)) return;
        _main.NexusAsked();
        StartNexusLogin();
    }

    /// <summary>
    /// Nexus login happens in the Wabbajack app. A new player does not have it yet — download it first
    /// (otherwise the "login required" check would block INSTALL forever).
    /// </summary>
    public async void StartNexusLogin()
    {
        if (_busy) return;
        if (WabbajackService.FindGui() == null)
        {
            NexusButton.IsEnabled = InstallButton.IsEnabled = false;
            ProgressPanel.Visibility = Visibility.Visible;
            ProgressPercent.Text = "";
            ContinueHint.Text = ""; // "the install continues next time" is about installing, not this download
            try
            {
                var dl = new Progress<int>(p =>
                {
                    ProgressText.Text = Loc.F("inst.wj.download", p);
                    ProgressFill.Width = ((FrameworkElement)ProgressFill.Parent).ActualWidth * p / 100.0;
                });
                await WabbajackService.DownloadAsync(dl, CancellationToken.None);
            }
            catch (Exception ex)
            {
                ProgressText.Text = Loc.F("inst.failed", ex.Message);
                NexusButton.IsEnabled = true;
                return;
            }
            NexusButton.IsEnabled = true;
        }
        ProgressText.Text = Loc.T("nexus.login.opening");
        PlayService.OpenWabbajackGui();
        _ = RunChecksAsync();
        WaitForNexusLogin();
    }

    private bool _waitingLogin;

    /// <summary>
    /// After "Log in to Nexus" the player logs in inside Wabbajack. The main window watches the login file and calls
    /// <see cref="NexusChanged"/> the moment it appears — INSTALL lights up by itself, no need to close Wabbajack or click anything.
    /// </summary>
    private void WaitForNexusLogin() => _waitingLogin = !WabbajackService.IsNexusLoggedIn;

    /// <summary>The Nexus login appeared or was removed (from any page): re-run the checks.</summary>
    public void NexusChanged(bool loggedIn)
    {
        if (loggedIn && _waitingLogin)
        {
            _waitingLogin = false;
            if (!_busy) ProgressText.Text = Loc.T("nexus.login.done");
        }
        if (!_busy) _ = RunChecksAsync();
    }

    private void Install_Click(object sender, RoutedEventArgs e)
    {
        if (Updating) _ = InstallAsync(_main.Settings.UpdateDir(Ml));
        else if (Installed || Found) _ = VerifyAsync();
        else _ = InstallAsync();
    }

    private void Play_Click(object sender, RoutedEventArgs e) => _main.PlaySelected();

    private void Reinstall_Click(object sender, RoutedEventArgs e)
    {
        if (Updating) _ = VerifyAsync();
        else _ = InstallAsync();
    }

    private void BeginBusy()
    {
        _busyMl = _main.Selected;
        _busy = true;
        ContinueHint.Text = Loc.T("inst.continue");
        DoneTelegram.Visibility = Visibility.Collapsed;
        _cts = new CancellationTokenSource();
        RecheckButton.IsEnabled = BrowseButton.IsEnabled = FolderBox.IsEnabled = false;
        CancelButton.Visibility = Visibility.Visible;
        ProgressPanel.Visibility = Visibility.Visible;
        ProgressPercent.Text = "";
        ProgressFill.Width = 0;
        ProgressFill.Background = new LinearGradientBrush(ThemeColors.Pink, ThemeColors.Cyan, 0); // 0.1.22: the theme colours
        ApplyMode();
    }

    private async Task EndBusyAsync()
    {
        _busy = false;
        var finished = _busyMl;
        _busyMl = null;
        // the player chose another modlist meanwhile: the page now follows it (the finished one keeps its result)
        CancelButton.Visibility = Visibility.Collapsed;
        RecheckButton.IsEnabled = BrowseButton.IsEnabled = FolderBox.IsEnabled = true;
        if (finished != null && finished.Id != _main.Selected.Id) ResetForModlist();
        await RunChecksAsync();
    }

    private Progress<InstallProgress> MakeProgress() => new(p =>
    {
        if (p.Downloaded < 0) { if (!string.IsNullOrWhiteSpace(p.LogLine)) LogLine.Text = p.LogLine; return; } // text only, keep the bar
        ProgressText.Text = p.Total > 0 ? $"{p.Status} · {Loc.F("inst.downloaded", p.Downloaded, p.Total)}" : p.Status;
        var pct = p.Total > 0 ? Math.Min(100, p.Downloaded * 100 / p.Total) : 0;
        ProgressPercent.Text = p.Total > 0 ? pct + "%" : "";
        ProgressFill.Width = ((FrameworkElement)ProgressFill.Parent).ActualWidth * pct / 100.0;
        if (!string.IsNullOrWhiteSpace(p.LogLine)) LogLine.Text = p.LogLine;
    });

    private void DoneTelegram_Click(object sender, RoutedEventArgs e) => PlayService.OpenUrl(AppConfig.LauncherNewsUrl);

    private void FillDone()
    {
        ProgressPercent.Text = "100%";
        ProgressFill.Width = ((FrameworkElement)ProgressFill.Parent).ActualWidth;
        // done: no stale "unpacking…" line and no "you can close, it continues later" under a finished install
        LogLine.Text = "";
        ContinueHint.Text = "";
    }

    /// <param name="updateDir">Update: install the new version into this new folder and keep the current one for rollback.</param>
    /// <summary>
    /// 0.1.19: key game files missing — "Install anyway" / "Cancel" before installing (always allowed; remembered for
    /// this game folder and version). Everything is checked on this PC only.
    /// </summary>
    private async Task<bool> GameFilesOkAsync()
    {
        if (!GameFiles.Has(Ml)) return true;
        GameInfo? game = null;
        try { game = await SystemCheck.FindGameAsync(Ml, CancellationToken.None); } catch { }
        if (game == null) return true; // the install itself says the game is not found
        var r = await Task.Run(() => GameFiles.Check(Ml, game.Path));
        var key = $"{Ml.Id}|{game.Path}|{game.Version}";
        if (r.Missing.Count == 0 || _main.Settings.GameFilesAccepted.Contains(key)) return true;
        if (Views.Dialog.Show(_main, Loc.F("files.warn.text", Ml.GameVersion, GameFiles.Names(r.Missing), Ml.Game), Loc.T("files.warn.title"),
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No, Loc.T("files.warn.go"), Loc.T("files.warn.cancel")) != MessageBoxResult.Yes)
            return false;
        _main.Settings.GameFilesAccepted.Add(key);
        _main.Settings.Save();
        AppLog.Write($"game files: install anyway ({GameFiles.Names(r.Missing)} missing)");
        return true;
    }

    private async Task InstallAsync(string? updateDir = null)
    {
        if (!await GameFilesOkAsync()) return;
        // 0.1.23 (Funky 26.11): a simple path for everyone — non-Latin letters or spaces in the modlist folder break MO2
        // on some PCs, so a first install offers the same drive with a plain folder name (one click)
        if (!Installed && !SystemCheck.IsPlainPath(_main.Settings.InstallDir(Ml)))
        {
            var plain = SystemCheck.PlainSuggestion(_main.Settings.InstallDir(Ml), Ml);
            if (Dialog.Show(_main, Loc.F("plainpath.text", _main.Settings.InstallDir(Ml), plain), Loc.T("check.folder.chars"),
                    MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes, Loc.T("plainpath.yes"), Loc.T("plainpath.no")) == MessageBoxResult.Yes)
            {
                _main.Settings.SetInstallDir(Ml, plain);
                FolderBox.Text = plain;
            }
        }
        if (!DiskSpace.Confirm(_main, _main.Settings, Ml, update: Installed)) return;
        BeginBusy();
        var firstInstall = !Installed; // an update of an installed modlist is not "the first install"
        try
        {
            while (true)
            {
                var nexus = Mo2Profile.IsNexusList(Ml);
                if (nexus) updateDir = null; // a Nexus-list modlist updates in place: only changed mods are downloaded again
                var (outcome, code, manual) = nexus
                    ? await NexusInstallService.RunAsync(_main.Settings, Ml, MakeProgress(), _cts!.Token)
                    : await InstallService.RunAsync(_main.Settings, Ml, MakeProgress(), _cts!.Token, updateDir);
                if (outcome == InstallOutcome.NeedsManualDownloads && OpenManualDownloads())
                    continue; // everything was downloaded by hand — run the install again
                var s = _main.Settings;
                if (outcome == InstallOutcome.Success)
                {
                    if (updateDir != null)
                    {
                        // keep the old working version; the new one becomes current
                        s.Previous[Ml.Id] = new PreviousInstall { Dir = s.InstallDir(Ml), Version = s.InstalledVersion(Ml) ?? "" };
                        s.InstallDirs[Ml.Id] = updateDir;
                        FolderBox.Text = updateDir;
                    }
                    s.MarkInstalled(Ml, s.InstallDir(Ml), Ml.Version);
                }
                ProgressText.Text = outcome switch
                {
                    InstallOutcome.Success when updateDir != null => Loc.F("inst.update.done", Ml.Version),
                    InstallOutcome.Success => Loc.T("inst.done"),
                    InstallOutcome.NeedsManualDownloads => Loc.F("inst.manual", manual),
                    InstallOutcome.Cancelled => Loc.T("inst.stopped"),
                    _ when nexus && NexusInstallService.LastErrors.Count > 0 =>
                        Loc.F("addon.errors", string.Join(Environment.NewLine, NexusInstallService.LastErrors.Take(8))),
                    _ => Loc.F("inst.failed", code),
                };
                if (outcome == InstallOutcome.Success)
                {
                    FillDone();
                    DoneTelegramText.Text = Loc.T("inst.done.telegram");
                    DoneTelegram.Visibility = Loc.Lang == "en" ? Visibility.Collapsed : Visibility.Visible; // Russian channel: RU only
                    try { await AfterInstallAsync(); }
                    catch (Exception ex) { ProgressText.Text += "\n" + ex.Message; }
                    if (firstInstall) SubscribeWindow.ShowOnce(_main); // once ever, after the first modlist install (not after an update)
                }
                break;
            }
        }
        catch (GameCheckException ex)
        {
            ProgressText.Text = ex.Message;
        }
        catch (Exception ex)
        {
            ProgressText.Text = Loc.F("inst.failed", ex.Message);
        }
        finally
        {
            await EndBusyAsync();
        }
    }

    /// <summary>Shows the manual download window. True = every missing file is now downloaded.</summary>
    private bool OpenManualDownloads()
    {
        if (Mo2Profile.IsNexusList(Ml))
        {
            var list = NexusInstallService.LastManual;
            if (list.Count == 0) return false;
            var w = new ManualDownloadWindow(list, _main.Settings.DownloadsDir(Ml)) { Owner = _main };
            w.ShowDialog();
            return w.AllDone;
        }
        var file = _main.Settings.ResolveWabbajack(Ml);
        if (file == null) return false;
        var items = ManualQueue.Build(file, _main.Settings.DownloadsDir(Ml));
        if (items.Count == 0) return false;
        var win = new ManualDownloadWindow(items, _main.Settings.DownloadsDir(Ml)) { Owner = _main };
        win.ShowDialog();
        return win.AllDone;
    }

    private async Task VerifyAsync()
    {
        var adopting = Found; // a folder the launcher did not install: it becomes "installed" only if the check passes
        BeginBusy();
        try
        {
            bool ok; int problems, ignored = 0;
            if (Mo2Profile.IsNexusList(Ml))
            {
                var (good, missing) = await Task.Run(() => NexusInstallService.Verify(_main.Settings, Ml));
                ok = good; problems = missing.Count;
                if (!good) LogLine.Text = string.Join(", ", missing.Take(10));
            }
            else (ok, problems, ignored) = await InstallService.VerifyAsync(_main.Settings, Ml, MakeProgress(), _cts!.Token);
            if (ok && adopting) _main.Settings.MarkInstalled(Ml, _main.Settings.InstallDir(Ml), Ml.Version);
            ProgressText.Text = !ok ? Loc.F(adopting ? "inst.found.fail" : "inst.verify.fail", problems)
                : adopting ? Loc.T("inst.found.ok")
                : ignored > 0 ? Loc.F("inst.verify.ok.ignored", ignored)
                : Loc.T("inst.verify.ok");
            if (ok) FillDone();
        }
        catch (OperationCanceledException)
        {
            ProgressText.Text = Loc.T("inst.stopped");
        }
        catch (Exception ex)
        {
            ProgressText.Text = Loc.F("inst.failed", ex.Message);
        }
        finally
        {
            await EndBusyAsync();
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();
}
