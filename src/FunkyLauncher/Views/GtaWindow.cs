// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FunkyLauncher.Core;
using FunkyLauncher.Services;

namespace FunkyLauncher.Views;

/// <summary>
/// 0.1.23: the GTA V Enhanced page — no MO2 here, the mods go into the game folder (see <see cref="GtaInstallService"/>).
/// The game folder, Standard / High / Ultra (High and Ultra need NaturalVision Enhanced), the steps the player does
/// himself (Script Hook V, NVE, Dispatch V), INSTALL, PLAY and "Restore clean game".
/// </summary>
public sealed class GtaWindow : Window
{
    private readonly Settings _s;
    private readonly Modlist _m;
    private readonly StackPanel _body = new();
    private readonly TextBlock _status = new() { FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    private readonly Button _install, _play, _restore;
    private string? _game;
    private bool _busy;

    public static void Show(Window owner, Settings s, Modlist m) => new GtaWindow(s, m) { Owner = owner }.ShowDialog();

    /// <summary>For --shots: the page for a given game folder, not shown on screen.</summary>
    internal static (FrameworkElement Body, Brush Bg, double Width, double Height) ForShots(Settings s, Modlist m, string game)
    {
        var w = new GtaWindow(s, m) { _game = game };
        w.Render();
        var body = (FrameworkElement)w.Content;
        w.Content = null;
        return (body, w.Background, w.Width, w.Height);
    }

    private static Brush R(string key) => (Brush)Application.Current.FindResource(key);
    private static Style St(string key) => (Style)Application.Current.FindResource(key);

    private GtaWindow(Settings s, Modlist m)
    {
        _s = s; _m = m;
        Title = "Grand Theft Auto V Enhanced";
        Width = 760; Height = 820; MinWidth = 600; MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        Background = R("Bg");
        FontFamily = (FontFamily)Application.Current.FindResource("UiFont");
        Foreground = Brushes.White;

        var root = new DockPanel { Margin = new Thickness(26, 22, 26, 22) };
        var head = new StackPanel();
        head.Children.Add(new TextBlock { Text = "GRAND THEFT AUTO V ENHANCED", FontSize = 22, FontWeight = FontWeights.Bold });
        head.Children.Add(new TextBlock { Text = Loc.T("gta.intro"), FontSize = 13, Foreground = R("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 12) });
        DockPanel.SetDock(head, Dock.Top);
        root.Children.Add(head);

        var buttons = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
        _restore = new Button { Content = Loc.T("gta.restore"), Style = St("SecondaryButton"), Height = 42, Padding = new Thickness(18, 0, 18, 0) };
        _restore.Click += async (_, _) => await RestoreAsync();
        DockPanel.SetDock(_restore, Dock.Left);
        buttons.Children.Add(_restore);
        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        _play = new Button { Content = Loc.T("gta.play"), Style = St("SecondaryButton"), Height = 42, Padding = new Thickness(22, 0, 22, 0), Margin = new Thickness(0, 0, 10, 0) };
        _play.Click += (_, _) => Play();
        _install = new Button { Content = Loc.T("gta.install"), Style = St("PrimaryButton"), Height = 42, FontSize = 15, Padding = new Thickness(26, 0, 26, 0) };
        _install.Click += async (_, _) => await InstallAsync();
        right.Children.Add(_play);
        right.Children.Add(_install);
        buttons.Children.Add(right);
        var bottom = new StackPanel();
        bottom.Children.Add(_status);
        bottom.Children.Add(buttons);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);

        root.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _body });
        Content = root;
        Loaded += async (_, _) => await FindGameAsync();
    }

    // ---------------------------------------------------------------- the version

    private const string HighId = "gta5-high", HeavyId = "gta5-heavy";

    private int Level
    {
        get => _s.Addons.TryGetValue(HeavyId, out var h) && h.On ? 2 : _s.Addons.TryGetValue(HighId, out var hi) && hi.On ? 1 : 0;
        set
        {
            _s.Addons[HighId] = new AddonPrefs { On = value >= 1 };
            _s.Addons[HeavyId] = new AddonPrefs { On = value >= 2 };
            _s.Save();
        }
    }

    private async Task FindGameAsync()
    {
        var saved = _s.InstallDirs.TryGetValue(GtaInstallService.GameKey, out var d) && File.Exists(Path.Combine(d, GtaInstallService.GameExe)) ? d : null;
        if (saved == null)
            try { saved = (await SystemCheck.FindGameAsync(_m, CancellationToken.None))?.Path; } catch { }
        _game = saved;
        // without NVE only Standard is possible; a first visit with NVE starts at Ultra (like the other modlists)
        if (_game != null && !GtaInstallService.HasNve(_game) && Level > 0) Level = 0;
        else if (_game != null && GtaInstallService.HasNve(_game) && !_s.Addons.ContainsKey(HighId)) Level = 2;
        Render();
    }

    // ---------------------------------------------------------------- the page

    private void Render()
    {
        _body.Children.Clear();
        var downloads = _s.DownloadsDir(_m);

        // the game folder
        var folder = Card(Loc.T("gta.folder.title"));
        var row = new DockPanel();
        var pick = new Button { Content = Loc.T("gta.folder.pick"), Style = St("SecondaryButton"), Height = 36, Padding = new Thickness(14, 0, 14, 0), Margin = new Thickness(12, 0, 0, 0) };
        pick.Click += (_, _) => PickFolder();
        DockPanel.SetDock(pick, Dock.Right);
        row.Children.Add(pick);
        row.Children.Add(new TextBlock
        {
            Text = _game ?? Loc.T("gta.folder.none"), FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap,
            Foreground = _game == null ? R("Pink") : Brushes.White,
        });
        folder.Children.Add(row);
        _body.Children.Add(Wrap(folder));

        if (_game == null) { Buttons(false, false); return; }
        var nve = GtaInstallService.HasNve(_game);
        var installed = GtaInstallService.IsInstalled(_game);

        // Standard / High / Ultra
        var ver = Card(Loc.T("gta.version.title"));
        ver.Children.Add(Muted(Loc.T(new[] { "gta.version.std.hint", "gta.version.high.hint", "gta.version.ultra.hint" }[Level])));
        var seg = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        string[] keys = { "gfx3.standard", "gfx3.high", "gfx3.ultra" };
        for (var i = 0; i < 3; i++)
        {
            var lvl = i;
            var b = new Button
            {
                Content = Loc.T(keys[i]), Height = 36, Padding = new Thickness(16, 0, 16, 0), Margin = new Thickness(0, 0, 8, 0),
                Style = St(Level == i ? "PrimaryButton" : "SecondaryButton"), IsEnabled = !_busy && (i == 0 || nve),
            };
            b.Click += (_, _) => { Level = lvl; Render(); };
            seg.Children.Add(b);
        }
        ver.Children.Add(seg);
        if (!nve) ver.Children.Add(Muted(Loc.T("gta.version.nonve"), R("Cyan")));
        else ver.Children.Add(Muted(Loc.T("gta.version.advice")));
        _body.Children.Add(Wrap(ver));

        // the steps the player does himself
        var plan = GtaInstallService.MakePlan(_game, downloads, Level);
        var steps = Card(Loc.T("gta.steps.title"));
        if (plan != null)
            foreach (var st in plan.Manual)
                steps.Children.Add(Step(st.Mod.DisplayName.Length > 0 ? st.Mod.DisplayName : st.Mod.Id, st.Mod.Install.Note.ToString(),
                    st.Done, st.Required, st.Mod.Install.Page, null));
        // Dispatch V: the file the player downloads himself and points the launcher to
        if (plan?.Install.FirstOrDefault(x => x.UserArchive != null) is { } dispatch)
        {
            var have = DispatchFile(plan, downloads) != null;
            steps.Children.Add(Step(dispatch.DisplayName.Length > 0 ? dispatch.DisplayName : dispatch.Id, dispatch.Install.Note.ToString(),
                have, false, dispatch.Install.Page, () => PickDispatch(downloads)));
        }
        _body.Children.Add(Wrap(steps));

        var blocked = plan == null || plan.Blocking.Any();
        _status.Text = installed ? Loc.T("gta.status.installed") : blocked ? Loc.T("gta.status.blocked") : Loc.T("gta.status.ready");
        _status.Foreground = blocked && !installed ? R("Pink") : R("Muted");
        Buttons(!blocked, installed);
    }

    private void Buttons(bool canInstall, bool installed)
    {
        _install.IsEnabled = canInstall && !_busy;
        _install.Content = Loc.T(installed ? "gta.update" : "gta.install");
        _play.IsEnabled = installed && !_busy;
        _restore.IsEnabled = installed && !_busy;
    }

    private static StackPanel Card(string title)
    {
        var p = new StackPanel();
        p.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8) });
        return p;
    }

    private static Border Wrap(StackPanel p) => new()
    {
        Background = R("Card"), CornerRadius = new CornerRadius(12), Padding = new Thickness(16), Margin = new Thickness(0, 0, 0, 12), Child = p,
    };

    private static TextBlock Muted(string text, Brush? color = null) => new()
    {
        Text = text, FontSize = 12.5, Foreground = color ?? R("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0),
    };

    private FrameworkElement Step(string name, string note, bool done, bool required, string page, Action? pickFile)
    {
        var box = new DockPanel { Margin = new Thickness(0, 6, 0, 6) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        if (page.Length > 0)
        {
            var open = new Button { Content = Loc.T("gta.step.page"), Style = St("SecondaryButton"), Height = 32, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(8, 0, 0, 0) };
            open.Click += (_, _) => { try { PlayService.OpenUrl(page); } catch { } };
            actions.Children.Add(open);
        }
        if (pickFile != null)
        {
            var pick = new Button { Content = Loc.T("gta.step.pick"), Style = St("SecondaryButton"), Height = 32, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(8, 0, 0, 0) };
            pick.Click += (_, _) => pickFile();
            actions.Children.Add(pick);
        }
        DockPanel.SetDock(actions, Dock.Right);
        box.Children.Add(actions);
        var left = new StackPanel();
        var mark = done ? "✓  " : required ? "•  " : "○  ";
        left.Children.Add(new TextBlock
        {
            Text = mark + name + (required ? "" : "  " + Loc.T("gta.step.optional")), FontSize = 13.5, FontWeight = FontWeights.SemiBold,
            Foreground = done ? R("Cyan") : required ? R("Pink") : Brushes.White, TextWrapping = TextWrapping.Wrap,
        });
        if (!done && note.Length > 0) left.Children.Add(Muted(note));
        box.Children.Add(left);
        return box;
    }

    // ---------------------------------------------------------------- actions

    private void PickFolder()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = Loc.T("gta.folder.pick") };
        if (dlg.ShowDialog(this) != true) return;
        if (!File.Exists(Path.Combine(dlg.FolderName, GtaInstallService.GameExe)))
        {
            Dialog.Show(this, Loc.F("gta.folder.wrong", GtaInstallService.GameExe), Title);
            return;
        }
        _game = dlg.FolderName;
        _s.InstallDirs[GtaInstallService.GameKey] = _game;
        _s.Save();
        Render();
    }

    private static string? DispatchFile(GtaInstallService.Plan plan, string downloads)
    {
        var x = plan.Install.First(y => y.UserArchive != null);
        if (x.Install.Check.FirstOrDefault() is not { } c) return null;
        var pattern = c.Replace("{downloads}", downloads).Replace('/', '\\');
        var dir = Path.GetDirectoryName(pattern)!;
        return Directory.Exists(dir) ? Directory.EnumerateFiles(dir, Path.GetFileName(pattern)).FirstOrDefault() : null;
    }

    /// <summary>The player points to his Dispatch V archive: a copy goes to the downloads folder under a name the list expects.</summary>
    private void PickDispatch(string downloads)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Dispatch V (*.rar;*.zip;*.7z)|*.rar;*.zip;*.7z", Title = Loc.T("gta.step.pick") };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            Directory.CreateDirectory(downloads);
            var name = Path.GetFileName(dlg.FileName);
            if (!name.Contains("Dispatch V FEAR Edition", StringComparison.OrdinalIgnoreCase)) name = "Dispatch V FEAR Edition" + Path.GetExtension(name);
            File.Copy(dlg.FileName, Path.Combine(downloads, name), true);
        }
        catch (Exception ex) { Dialog.Show(this, ex.Message, Title); }
        Render();
    }

    private async Task InstallAsync()
    {
        if (_busy || _game == null) return;
        if (Process.GetProcessesByName(Path.GetFileNameWithoutExtension(GtaInstallService.GameExe)).Length > 0)
        { Dialog.Show(this, Loc.T("gta.err.running"), Title); return; }
        var downloads = _s.DownloadsDir(_m);
        _busy = true;
        Buttons(false, false);
        try
        {
            var progress = new Progress<string>(t => _status.Text = t);
            for (var round = 0; round < 3; round++)
            {
                var plan = GtaInstallService.MakePlan(_game, downloads, Level);
                if (plan == null || plan.Blocking.Any()) break;
                var r = await GtaInstallService.InstallAsync(_game, downloads, plan, progress, CancellationToken.None);
                if (r.NeedManual.Count > 0)
                {
                    // free Nexus account: "Slow download" for each file, then the install goes on
                    var win = new ManualDownloadWindow(r.NeedManual, downloads) { Owner = this };
                    win.ShowDialog();
                    if (r.NeedManual.Any(i => i.Done)) continue;
                }
                _s.InstallDirs[GtaInstallService.GameKey] = _game;
                _s.Save();
                var text = r.Errors.Count == 0 && r.NeedManual.Count == 0 ? Loc.T("gta.done")
                    : Loc.F("gta.done.partial", r.Installed, plan.Install.Count, string.Join("\n", r.Errors.Take(6)));
                _busy = false;
                Render();
                _status.Text = text;
                return;
            }
        }
        catch (Exception ex) { _status.Text = Loc.F("inst.failed", ex.Message); AppLog.Write("gta install failed: " + ex); }
        _busy = false;
        Render();
    }

    private async Task RestoreAsync()
    {
        if (_busy || _game == null) return;
        if (Dialog.Show(this, Loc.T("gta.restore.ask"), Loc.T("gta.restore"), MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No)
            != MessageBoxResult.Yes) return;
        _busy = true;
        Buttons(false, false);
        var game = _game;
        var (removed, restored, errors) = await Task.Run(() => GtaInstallService.Restore(game));
        _busy = false;
        Render();
        _status.Text = errors.Count == 0 ? Loc.F("gta.restore.done", removed, restored) : Loc.F("gta.restore.errors", string.Join("\n", errors.Take(6)));
    }

    private void Play()
    {
        if (_game == null) return;
        try
        {
            // the game's own launcher (Rockstar / Steam) starts it; args.txt carries -nobattleye
            var exe = Path.Combine(_game, GtaInstallService.GameExe);
            Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = _game, UseShellExecute = true });
            AppLog.Write("gta play: " + exe);
            Close();
        }
        catch (Exception ex) { Dialog.Show(this, Loc.F("inst.failed", ex.Message), Title); }
    }
}
