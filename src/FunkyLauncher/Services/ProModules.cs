// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Reflection;
using System.Runtime.Loader;
using System.Windows;
using FunkyLauncher.Core;

namespace FunkyLauncher.Services;

/// <summary>What the main window does for the Pro doors (set by MainWindow).</summary>
public interface IProUi
{
    void SetHomeBackground(string? file);
    void SetBadge(string? text);
    void Play(Modlist m, string? mo2Profile);
    void RefreshPages();
    void TrayItemsChanged();
    bool RegisterHotkey(int id, uint mods, uint vk);
    void UnregisterHotkey(int id);
}

/// <summary>A panel the Pro module added.</summary>
public sealed record ProPanelInfo(ProPlace Place, string Id, string Icon, string Title, string Text, Func<FrameworkElement> Create);

/// <summary>Loads the Funky Pro module (%LOCALAPPDATA%\FunkyLauncher\pro\FunkyLauncher.Pro.dll) when a valid key is saved.</summary>
public static class ProModules
{
    public const int ApiVersion = 5;

    public static string Dir => Path.Combine(AppConfig.DataDir, "pro");
    public static string DllPath => Path.Combine(Dir, "FunkyLauncher.Pro.dll");

    public static IProModule? Current { get; private set; }
    public static string? LoadError { get; private set; }
    public static bool Installed => File.Exists(DllPath);

    /// <summary>Funky Pro is not on sale yet (Funky, 2026-11-13): the Pro section stays hidden for players.
    /// Turn to true when sales open.</summary>
    public const bool SalesOpen = false;

    /// <summary>Show Pro UI: when sales are open, for someone who already has a valid key, or with --show-pro (developer).</summary>
    public static bool ShowUi { get; set; }

    /// <summary>Set by MainWindow before <see cref="Refresh"/>.</summary>
    public static IProUi? Ui { get; set; }

    private static bool _hooked;
    private static Host? _host;

    /// <summary>(Re)checks the key and loads the module once. A module can't be unloaded: removing the key just stops using it.</summary>
    public static void Refresh(Settings s)
    {
        LoadError = null;
        if (!ProLicense.Active(s)) { Active = false; Ui?.TrayItemsChanged(); return; }
        Active = true;
        if (Current != null || !Installed) { Ui?.TrayItemsChanged(); return; }
        try
        {
            var asm = AssemblyLoadContext.Default.LoadFromAssemblyPath(DllPath);
            var type = asm.GetTypes().FirstOrDefault(t => typeof(IProModule).IsAssignableFrom(t) && !t.IsAbstract)
                       ?? throw new InvalidOperationException("no IProModule in FunkyLauncher.Pro.dll");
            var m = (IProModule)Activator.CreateInstance(type)!;
            _host = new Host(s);
            Hook();
            m.Start(_host);
            Current = m;
        }
        catch (Exception ex) { LoadError = ex.Message; Log("load: " + ex); }
        Ui?.TrayItemsChanged();
    }

    /// <summary>
    /// Funky Pro theme, applied before any window is built (a launcher's XAML takes its colours once):
    /// pro	heme.json = { "Pink": "#RRGGBB", … } written by the Pro module; only with a valid key; only known colour keys.
    /// </summary>
    public static void ApplyEarlyTheme(Settings s)
    {
        try
        {
            var file = Path.Combine(Dir, "theme.json");
            if (!File.Exists(file) || !ProLicense.Active(s) || Application.Current == null) return;
            var map = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file)) ?? new();
            foreach (var (key, hex) in map)
            {
                if (key is not ("Pink" or "PinkSoft" or "Cyan" or "Bg" or "Panel" or "Card" or "CardActive")) continue;
                if (System.Windows.Media.ColorConverter.ConvertFromString(hex) is System.Windows.Media.Color c)
                    Application.Current.Resources[key] = new System.Windows.Media.SolidColorBrush(c);
            }
        }
        catch (Exception ex) { Log("theme: " + ex.Message); }
    }

    /// <summary>Key valid right now (the module, if loaded, is only used while this is true).</summary>
    public static bool Active { get; private set; }

    public static IProModule? Use => Active ? Current : null;

    private static Host? Live => Active && Current != null ? _host : null;

    // ---------- what the launcher shows for Pro (empty without a valid key) ----------

    public static IEnumerable<ProPanelInfo> Panels(ProPlace place) =>
        Live == null ? Enumerable.Empty<ProPanelInfo>() : _host!.PanelList.Where(p => p.Place == place).ToList();

    public static IReadOnlyList<(string Id, string Text, Action Click)> TrayItems =>
        Live == null ? Array.Empty<(string, string, Action)>() : _host!.Tray.ToList();

    /// <summary>A WM_HOTKEY the main window did not know: true if it was a Pro hotkey.</summary>
    public static bool HotkeyPressed(int id)
    {
        if (_host == null || !_host.HotkeyActions.TryGetValue(id, out var a)) return false;
        if (Live != null) Safe("hotkey", a);
        return true;
    }

    /// <summary>MO2 profile Funky Pro picked for PLAY (null = the modlist's own).</summary>
    public static string? PlayProfile(Modlist m)
    {
        var f = Live?.PlayProfile;
        if (f == null) return null;
        try { return f(m.Id) is { Length: > 0 } p && Directory.Exists(Path.Combine(AppSettingsInstallDir(m), "profiles", p)) ? p : null; }
        catch (Exception ex) { Log("profile: " + ex.Message); return null; }
    }

    private static string AppSettingsInstallDir(Modlist m) => _host!.Settings.InstallDir(m);

    /// <summary>Asked by backup rotation.</summary>
    public static bool KeepBackup(Modlist m, string dir)
    {
        var f = Live?.KeepBackup;
        if (f == null) return false;
        try { return f(m.Id, dir); } catch (Exception ex) { Log("keep: " + ex.Message); return false; }
    }

    // ---------- events from the launcher ----------

    public static void RaiseGameStarting(Settings s, Modlist m) => Live?.Fire(h => h.Starting?.Invoke(ToPro(s, m)));
    public static void RaiseGameStarted(Settings s, Modlist m, int pid) => Live?.Fire(h => h.Started?.Invoke(ToPro(s, m), pid));
    public static void RaiseGameExited(Settings s, Modlist m, LaunchReport r) => Live?.Fire(h =>
        h.Exited?.Invoke(ToPro(s, m), new ProGameExit(r.RunTime, r.Result == LaunchResult.Crashed, r.Result == LaunchResult.NotStarted)));
    public static void RaiseSelectionChanged(Settings s, Modlist m) => Live?.Fire(h => h.Selection?.Invoke(s.IsInstalled(m) ? ToPro(s, m) : null));

    private static void Hook()
    {
        if (_hooked) return;
        _hooked = true;
        BackupService.Created += (id, title, dir) =>
        {
            if (!Active) return;
            try { Current?.OnBackupCreated(id, title, dir); } catch (Exception ex) { Log("backup hook: " + ex.Message); }
        };
        FunkyScreen.Saved += (file, video) => Live?.Fire(h => h.Capture?.Invoke(file, video));
        Settings.Installed += (s, m, old, now) => Live?.Fire(h => h.InstalledEv?.Invoke(ToPro(s, m), old, now));
        if (Application.Current != null) Application.Current.Exit += (_, _) => Live?.Fire(h => h.ClosingEv?.Invoke());
    }

    public static ProModlist ToPro(Settings s, Modlist m) =>
        new(m.Id, m.Title, m.Game, s.InstallDir(m), SafeSaveFolder(m), m.Mo2Profile, m.GameExePath, s.InstalledVersion(m));

    private static string? SafeSaveFolder(Modlist m) { try { return m.ResolveSaveFolder(); } catch { return null; } }

    private static ProBackup ToPro(BackupInfo b) =>
        new(b.Dir, b.Date, b.Kind.ToString().ToLowerInvariant(), b.Saves, b.Files, b.TotalBytes, b.PreviewPath);

    private static void Safe(string what, Action a)
    {
        try { a(); } catch (Exception ex) { Log(what + ": " + ex); }
    }

    private static void Log(string msg)
    {
        try { Directory.CreateDirectory(Dir); File.AppendAllText(Path.Combine(Dir, "pro.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {msg}\n"); } catch { }
    }

    private sealed class Host(Settings s) : IProHost
    {
        public Settings Settings => s;
        public string DataDir => Dir;
        public string Language => Loc.Lang;
        public void Log(string message) => ProModules.Log(message);

        public int ApiVersion => ProModules.ApiVersion;
        public string LauncherVersion => AppConfig.Version;

        public IReadOnlyList<ProModlist> Modlists() =>
            Catalog.Items.Where(m => m.IsReleased && s.IsInstalled(m)).Select(m => ToPro(s, m)).ToList();

        public ProModlist? Selected
        {
            get
            {
                var m = Catalog.Find(s.SelectedId);
                return m != null && s.IsInstalled(m) ? ToPro(s, m) : null;
            }
        }

        public void Play(string modlistId, string? mo2Profile = null)
        {
            if (Catalog.Find(modlistId) is { } m) OnUi(() => Ui?.Play(m, mo2Profile));
        }

        // backing delegates: ProModules raises them (a C# event can only be invoked inside its class)
        internal Action<ProModlist>? Starting;
        internal Action<ProModlist, int>? Started;
        internal Action<ProModlist, ProGameExit>? Exited;
        internal Action<ProModlist, string?, string>? InstalledEv;
        internal Action<ProModlist?>? Selection;
        internal Action? ClosingEv;
        internal Action<string, bool>? Capture;
        public event Action<string, bool>? CaptureSaved { add => Capture += value; remove => Capture -= value; }

        public event Action<ProModlist>? GameStarting { add => Starting += value; remove => Starting -= value; }
        public event Action<ProModlist, int>? GameStarted { add => Started += value; remove => Started -= value; }
        public event Action<ProModlist, ProGameExit>? GameExited { add => Exited += value; remove => Exited -= value; }
        public event Action<ProModlist, string?, string>? ModlistInstalled { add => InstalledEv += value; remove => InstalledEv -= value; }
        public event Action<ProModlist?>? SelectionChanged { add => Selection += value; remove => Selection -= value; }
        public event Action? Closing { add => ClosingEv += value; remove => ClosingEv -= value; }

        /// <summary>Raises an event; a broken Pro handler never breaks the launcher.</summary>
        public void Fire(Action<Host> raise) => Safe("event", () => raise(this));

        public Func<string, string, bool>? KeepBackup { get; set; }
        public Func<string, string?>? PlayProfile { get; set; }

        public IReadOnlyList<ProBackup> Backups(string modlistId) =>
            Catalog.Find(modlistId) is { } m ? BackupService.List(s, m).Select(ToPro).ToList() : Array.Empty<ProBackup>();

        public async Task<ProBackup?> BackupNow(string modlistId, bool manual)
        {
            var m = Catalog.Find(modlistId) ?? throw new ArgumentException("unknown modlist " + modlistId);
            var b = await BackupService.CreateAsync(s, m, manual ? BackupKind.Manual : BackupKind.Auto, CancellationToken.None);
            return b == null ? null : ToPro(b);
        }

        public async Task RestoreBackup(string modlistId, string backupDir)
        {
            var m = Catalog.Find(modlistId) ?? throw new ArgumentException("unknown modlist " + modlistId);
            var b = BackupService.List(s, m).FirstOrDefault(x => string.Equals(x.Dir, backupDir, StringComparison.OrdinalIgnoreCase))
                    ?? throw new ArgumentException("unknown backup " + backupDir);
            await BackupService.RestoreAsync(s, m, b, CancellationToken.None);
        }

        internal readonly List<ProPanelInfo> PanelList = new();

        public void AddPanel(ProPlace place, string id, string icon, string title, string text, Func<FrameworkElement> create)
        {
            PanelList.RemoveAll(p => p.Id == id);
            PanelList.Add(new ProPanelInfo(place, id, icon, title, text, create));
        }

        public void RemovePanel(string id) => PanelList.RemoveAll(p => p.Id == id);

        public void RefreshUi() => OnUi(() => Ui?.RefreshPages());
        public void SetHomeBackground(string? file) => OnUi(() => Ui?.SetHomeBackground(file));
        public void SetBadge(string? text) => OnUi(() => Ui?.SetBadge(text));

        public void Notify(string title, string text) => OnUi(() => Views.ScreenToast.Message(title, text));

        internal readonly List<(string Id, string Text, Action Click)> Tray = new();

        public void AddTrayItem(string id, string text, Action click)
        {
            Tray.RemoveAll(t => t.Id == id);
            Tray.Add((id, text, click));
            OnUi(() => Ui?.TrayItemsChanged());
        }

        public void RemoveTrayItem(string id)
        {
            if (Tray.RemoveAll(t => t.Id == id) > 0) OnUi(() => Ui?.TrayItemsChanged());
        }

        // Pro hotkeys use ids 0xF100.. (the launcher's own are 0xF00D..0xF010)
        private readonly Dictionary<string, int> _hotkeyIds = new();
        private int _nextHotkey = 0xF100;
        internal readonly Dictionary<int, Action> HotkeyActions = new();

        public bool SetHotkey(string id, uint mods, uint vk, Action pressed)
        {
            RemoveHotkey(id);
            var num = _nextHotkey++;
            var ok = false;
            OnUi(() => ok = Ui?.RegisterHotkey(num, mods | 0x4000 /* NOREPEAT */, vk) == true);
            if (!ok) return false;
            _hotkeyIds[id] = num;
            HotkeyActions[num] = pressed;
            return true;
        }

        public void RemoveHotkey(string id)
        {
            if (!_hotkeyIds.Remove(id, out var num)) return;
            HotkeyActions.Remove(num);
            OnUi(() => Ui?.UnregisterHotkey(num));
        }

        public string? FfmpegPath => File.Exists(PlayerEngine.Ffmpeg) ? PlayerEngine.Ffmpeg : null;

        private static void OnUi(Action a)
        {
            var d = Application.Current?.Dispatcher;
            if (d == null || d.CheckAccess()) Safe("ui", a);
            else d.Invoke(() => Safe("ui", a));
        }
    }
}
