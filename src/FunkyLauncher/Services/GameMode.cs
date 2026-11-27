// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using FunkyLauncher.Core;

namespace FunkyLauncher.Services;

/// <summary>A running program the player can close for the game.</summary>
public sealed record RunningApp(string Name, string Title, long Bytes, bool Torrent);

/// <summary>A program game mode closed — how to start it again.</summary>
public sealed class ClosedApp
{
    public string Name { get; set; } = "";
    public string? Path { get; set; }
    /// <summary>Microsoft Store apps can't be started by path — they are started by their app id.</summary>
    public string? AppId { get; set; }
}

public sealed class GameModeState
{
    public bool Active { get; set; }
    public DateTime Started { get; set; }
    public List<ClosedApp> Closed { get; set; } = new();
    public bool Relaunch { get; set; } = true;
    public bool PowerChanged { get; set; }
    public Guid OldOverlay { get; set; }
    public string? OneDrivePath { get; set; }
    public long FreedBytes { get; set; }
    /// <summary>Turned on by PLAY: turned off by the launcher when the game exits.</summary>
    public bool ByPlay { get; set; }
}

/// <summary>
/// Game mode: closes the programs the player picked (politely, like the X button; apps that only hide in the
/// tray are ended), optionally torrent clients and OneDrive, sets the Windows 11 "Best performance" power mode
/// on the standard Balanced plan. Everything is put back after the game. State is saved before every change.
/// </summary>
public static class GameMode
{
    private static string Dir => Path.Combine(AppConfig.DataDir, "gamemode");
    private static string StatePath => Path.Combine(Dir, "state.json");
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static readonly Guid BalancedPlan = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    public static readonly Guid BestPerformance = new("ded574b5-45a0-4f42-8737-46345c09c238");

    /// <summary>Never offered for closing: Windows itself, the launcher, game stores (DRM), mod tools, voice and recording apps.</summary>
    private static readonly HashSet<string> Never = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "FunkyLauncher", "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost", "TextInputHost",
        "SystemSettings", "ApplicationFrameHost", "LockApp", "Widgets", "dwm", "csrss", "ctfmon", "SecurityHealthSystray",
        "steam", "steamwebhelper", "EpicGamesLauncher", "EpicWebHelper", "GalaxyClient", "GalaxyClient Helper", "Battle.net",
        "EADesktop", "UbisoftConnect", "upc", "ModOrganizer", "Wabbajack", "Cyberpunk2077", "REDprelauncher", "Starfield",
        "Discord", "obs64", "obs32", "obs", "Streamlabs OBS", "NVIDIA Overlay", "NVIDIA Share", "nvcontainer", "RadeonSoftware",
        "claude", "msedgewebview2", "Taskmgr", "devenv", "WindowsTerminal", "powershell", "pwsh", "cmd", "conhost",
    };

    private static readonly HashSet<string> Torrents = new(StringComparer.OrdinalIgnoreCase)
    {
        "uTorrent", "utweb", "qbittorrent", "BitTorrent", "transmission-qt", "transmission-gtk", "deluge", "tixati", "Vuze", "biglybt", "MediaGet",
    };

    // ---------- state ----------

    public static GameModeState? Load()
    {
        try { return File.Exists(StatePath) ? JsonSerializer.Deserialize<GameModeState>(File.ReadAllText(StatePath)) : null; }
        catch { return null; }
    }

    public static bool IsActive => Load()?.Active == true;

    private static void Save(GameModeState s)
    {
        Directory.CreateDirectory(Dir);
        var tmp = StatePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(s, Json));
        File.Move(tmp, StatePath, overwrite: true);
    }

    // ---------- what is running ----------

    /// <summary>Programs with a window (or torrent clients in the tray), with their memory summed over all processes.</summary>
    public static List<RunningApp> RunningApps()
    {
        var own = Environment.ProcessId;
        var list = new List<RunningApp>();
        foreach (var g in Process.GetProcesses().GroupBy(p => p.ProcessName, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var name = g.Key;
                if (Never.Contains(name) || g.Any(p => p.Id == own) || g.Any(p => p.SessionId == 0)) continue;
                var torrent = Torrents.Contains(name);
                var withWindow = g.FirstOrDefault(p => p.MainWindowHandle != IntPtr.Zero && p.MainWindowTitle.Length > 0);
                if (withWindow == null && !torrent && !TrayApps.Contains(name)) continue;
                long bytes = 0;
                foreach (var p in g) { try { bytes += p.WorkingSet64; } catch { } }
                var title = name;
                try { title = g.First().MainModule?.FileVersionInfo.FileDescription is { Length: > 0 } d ? d : name; } catch { }
                list.Add(new RunningApp(name, title, bytes, torrent));
            }
            catch { /* a process that exits while we look at it */ }
        }
        return list.OrderByDescending(a => a.Bytes).ToList();
    }

    /// <summary>Apps that keep running in the tray after their window is closed — they have to be ended.</summary>
    private static readonly HashSet<string> TrayApps = new(StringComparer.OrdinalIgnoreCase)
    {
        "Telegram", "Spotify", "uTorrent", "utweb", "qbittorrent", "BitTorrent", "WhatsApp", "Viber", "Skype", "Zoom", "Slack",
        "OneDrive", "Dropbox", "YandexDisk2", "Yandex", "Teams", "ms-teams",
    };

    // ---------- on / off ----------

    public sealed record Options(List<string> Apps, bool Torrents, bool Power, bool OneDrive, bool Relaunch, bool ByPlay);

    public static (long Freed, List<string> Errors) Enable(Options o)
    {
        var errors = new List<string>();
        if (IsActive) return (0, errors);
        var s = new GameModeState { Active = true, Started = DateTime.Now, Relaunch = o.Relaunch, ByPlay = o.ByPlay };
        Save(s);

        var running = RunningApps();
        var close = running.Where(a => o.Apps.Contains(a.Name, StringComparer.OrdinalIgnoreCase) || (o.Torrents && a.Torrent)).ToList();
        foreach (var app in close)
        {
            try
            {
                var info = Describe(app.Name);
                s.Closed.Add(info);
                Save(s); // remember it BEFORE closing, so it can always be started again
                if (CloseApp(app.Name)) s.FreedBytes += app.Bytes;
                else errors.Add(app.Title);
                Save(s);
            }
            catch (Exception ex) { errors.Add(app.Title + ": " + ex.Message); }
        }

        if (o.OneDrive)
        {
            try
            {
                var od = Process.GetProcessesByName("OneDrive").FirstOrDefault();
                var path = od?.MainModule?.FileName;
                if (path != null)
                {
                    s.OneDrivePath = path; Save(s);
                    Process.Start(new ProcessStartInfo(path, "/shutdown") { UseShellExecute = false })?.WaitForExit(15000);
                }
            }
            catch (Exception ex) { errors.Add("OneDrive: " + ex.Message); }
        }

        if (o.Power && PowerModeAvailable())
        {
            try
            {
                if (PowerGetActualOverlayScheme(out var old) == 0 && old != BestPerformance)
                {
                    s.OldOverlay = old; s.PowerChanged = true; Save(s);
                    if (PowerSetActiveOverlayScheme(BestPerformance) != 0) { s.PowerChanged = false; Save(s); errors.Add("power"); }
                }
            }
            catch (Exception ex) { errors.Add("power: " + ex.Message); }
        }
        return (s.FreedBytes, errors);
    }

    public static List<string> Disable()
    {
        var s = Load();
        var errors = new List<string>();
        if (s == null || !s.Active) return errors;

        if (s.PowerChanged)
        {
            try { if (PowerSetActiveOverlayScheme(s.OldOverlay) != 0) errors.Add("power"); }
            catch (Exception ex) { errors.Add("power: " + ex.Message); }
        }
        if (s.OneDrivePath != null && Process.GetProcessesByName("OneDrive").Length == 0)
        {
            try { Process.Start(new ProcessStartInfo(s.OneDrivePath, "/background") { UseShellExecute = false }); }
            catch (Exception ex) { errors.Add("OneDrive: " + ex.Message); }
        }
        if (s.Relaunch)
        {
            foreach (var a in s.Closed)
            {
                if (Process.GetProcessesByName(a.Name).Length > 0) continue; // already started by the player
                try { Start(a); }
                catch (Exception ex) { errors.Add(a.Name + ": " + ex.Message); }
            }
        }
        s.Active = false;
        Save(s);
        return errors;
    }

    /// <summary>Closes like the X button; if the app only hid in the tray (or has no window) it is ended.</summary>
    private static bool CloseApp(string name)
    {
        // WM_CLOSE to every visible window of the app (a browser may have several windows)
        var pids = Process.GetProcessesByName(name).Select(p => (uint)p.Id).ToHashSet();
        EnumWindows((h, _) =>
        {
            if (IsWindowVisible(h) && GetWindow(h, 4) == IntPtr.Zero) // top-level, not owned
            {
                GetWindowThreadProcessId(h, out var pid);
                if (pids.Contains(pid)) PostMessage(h, 0x0010, IntPtr.Zero, IntPtr.Zero);
            }
            return true;
        }, IntPtr.Zero);
        if (WaitGone(name, 6000)) return true;
        // still running: a tray app keeps living after its window is closed
        if (!TrayApps.Contains(name) && !Torrents.Contains(name))
        {
            // a window is still open → it is asking "save changes?" — leave it to the player
            if (HasVisibleWindow(name)) return false;
            // no windows left: the app is finishing (e.g. Notepad saves its tabs) — give it time
            WaitGone(name, 15000);
            return true;
        }
        foreach (var p in Process.GetProcessesByName(name)) { try { p.Kill(entireProcessTree: true); } catch { } }
        return WaitGone(name, 5000);
    }

    private static bool HasVisibleWindow(string name)
    {
        var pids = Process.GetProcessesByName(name).Select(p => (uint)p.Id).ToHashSet();
        var found = false;
        EnumWindows((h, _) =>
        {
            if (IsWindowVisible(h) && GetWindow(h, 4) == IntPtr.Zero)
            {
                GetWindowThreadProcessId(h, out var pid);
                if (pids.Contains(pid)) { found = true; return false; }
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    private static bool WaitGone(string name, int ms)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms)
        {
            if (Process.GetProcessesByName(name).Length == 0) return true;
            Thread.Sleep(200);
        }
        return Process.GetProcessesByName(name).Length == 0;
    }

    private static ClosedApp Describe(string name)
    {
        var info = new ClosedApp { Name = name };
        foreach (var p in Process.GetProcessesByName(name))
        {
            try
            {
                info.Path ??= p.MainModule?.FileName;
                if (info.AppId == null)
                {
                    uint len = 512;
                    var sb = new StringBuilder((int)len);
                    if (GetApplicationUserModelId(p.Handle, ref len, sb) == 0 && sb.Length > 0) info.AppId = sb.ToString();
                }
            }
            catch { }
        }
        return info;
    }

    private static void Start(ClosedApp a)
    {
        if (a.AppId != null)
            Process.Start(new ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{a.AppId}") { UseShellExecute = true });
        else if (a.Path != null && File.Exists(a.Path))
            Process.Start(new ProcessStartInfo(a.Path) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(a.Path)!, WindowStyle = ProcessWindowStyle.Minimized });
    }

    // ---------- power mode ----------

    /// <summary>The Windows 11 power mode only works on the standard Balanced plan (not on a PC maker's own plan).</summary>
    public static bool PowerModeAvailable()
    {
        try
        {
            if (PowerGetActiveScheme(IntPtr.Zero, out var p) != 0) return false;
            var plan = Marshal.PtrToStructure<Guid>(p);
            LocalFree(p);
            return plan == BalancedPlan && PowerGetActualOverlayScheme(out _) == 0;
        }
        catch { return false; }
    }

    /// <summary>Windows "Game Mode" (Settings → Gaming). On unless the player turned it off.</summary>
    public static bool WindowsGameModeOn()
    {
        using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\GameBar");
        return k?.GetValue("AutoGameModeEnabled") is not int v || v != 0;
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("powrprof.dll")] private static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr guid);
    [DllImport("powrprof.dll")] private static extern uint PowerGetActualOverlayScheme(out Guid overlay);
    [DllImport("powrprof.dll")] private static extern uint PowerSetActiveOverlayScheme(Guid overlay);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr p);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern int GetApplicationUserModelId(IntPtr process, ref uint length, StringBuilder id);
}
