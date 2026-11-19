// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Windows;

namespace FunkyLauncher.Core;

/// <summary>
/// The door for the separate Funky Pro module (FunkyLauncher.Pro.dll). The open launcher only knows this interface;
/// the module is loaded when a valid Pro key is saved. Without the module or the key, every free feature works as usual.
/// This interface stays as it is (an older module must keep loading); new doors are added to <see cref="IProHost"/>.
/// </summary>
public interface IProModule
{
    string Version { get; }

    /// <summary>Called once after loading.</summary>
    void Start(IProHost host);

    /// <summary>How many automatic save backups to keep (free: 10).</summary>
    int AutoBackupsToKeep(int free);

    /// <summary>A save backup was just made (backupDir holds "files" and backup.json).</summary>
    void OnBackupCreated(string modlistId, string modlistTitle, string backupDir);

    /// <summary>The main Pro panel shown in Tools → Funky Pro (null = none).</summary>
    FrameworkElement? CreatePanel();
}

/// <summary>An installed modlist as the Pro module sees it.</summary>
public sealed record ProModlist(
    string Id, string Title, string Game, string InstallDir, string? SaveFolder,
    string Mo2Profile, string GameExe, string? Version);

/// <summary>A save backup as the Pro module sees it. Kind: "auto", "manual", "beforerestore".</summary>
public sealed record ProBackup(string Dir, DateTime Date, string Kind, int Saves, int Files, long TotalBytes, string? PreviewPath);

/// <summary>Where a Pro panel is shown.</summary>
public enum ProPlace
{
    /// <summary>Its own card in Tools.</summary>
    Tools,
    /// <summary>A block in Settings, under the Pro key.</summary>
    Settings,
    /// <summary>Under the PLAY button of the selected modlist in Library (built again when the selection changes).</summary>
    Library,
}

/// <summary>How a game session ended.</summary>
public sealed record ProGameExit(TimeSpan Played, bool Crashed, bool NotStarted);

/// <summary>
/// What the launcher gives the Pro module.
/// API 1 (launcher 0.1.13): Settings, DataDir, Language, Log.
/// API 2 (launcher 0.1.14): everything below. A module must check <see cref="ApiVersion"/> (missing on API 1 → MissingMethodException).
/// </summary>
public interface IProHost
{
    Settings Settings { get; }
    string DataDir { get; }
    string Language { get; }
    void Log(string message);

    // ---------- API 2 ----------

    int ApiVersion { get; }
    string LauncherVersion { get; }

    /// <summary>Installed modlists (catalog order).</summary>
    IReadOnlyList<ProModlist> Modlists();
    /// <summary>The modlist selected in the launcher (null if it is not installed).</summary>
    ProModlist? Selected { get; }

    /// <summary>PLAY exactly like the button (checks, backup, game mode, watch). mo2Profile = another MO2 profile
    /// of the same modlist (null = its own). Selects the modlist and opens Library.</summary>
    void Play(string modlistId, string? mo2Profile = null);

    /// <summary>Right before the game starts (after the launcher's checks and backup). UI thread; keep it quick.</summary>
    event Action<ProModlist>? GameStarting;
    /// <summary>The game process appeared (process id). UI thread.</summary>
    event Action<ProModlist, int>? GameStarted;
    /// <summary>The game is gone (or never started). UI thread.</summary>
    event Action<ProModlist, ProGameExit>? GameExited;
    /// <summary>A modlist was installed or updated: (modlist, old version or null, new version).</summary>
    event Action<ProModlist, string?, string>? ModlistInstalled;
    /// <summary>The player picked another modlist.</summary>
    event Action<ProModlist?>? SelectionChanged;
    /// <summary>The launcher is closing.</summary>
    event Action? Closing;

    /// <summary>Asked before rotation removes an automatic backup: (modlist id, backup folder) → true = keep it (pinned).</summary>
    Func<string, string, bool>? KeepBackup { get; set; }

    IReadOnlyList<ProBackup> Backups(string modlistId);
    /// <summary>A backup now; null = no saves, or (auto) nothing changed since the last one.</summary>
    Task<ProBackup?> BackupNow(string modlistId, bool manual);
    /// <summary>Puts the whole backup back (the launcher first backs up the current saves).</summary>
    Task RestoreBackup(string modlistId, string backupDir);

    /// <summary>A panel, built on the UI thread when shown. Same id again = replaced.</summary>
    void AddPanel(ProPlace place, string id, string icon, string title, string text, Func<FrameworkElement> create);
    void RemovePanel(string id);
    /// <summary>Rebuilds the launcher pages (after a Pro setting changed what they show).</summary>
    void RefreshUi();

    /// <summary>A short pop-up near the clock.</summary>
    void Notify(string title, string text);
    /// <summary>An item in the tray icon menu (the icon is shown while there are items).</summary>
    void AddTrayItem(string id, string text, Action click);
    void RemoveTrayItem(string id);
    /// <summary>A global hotkey (mods: 1 Alt, 2 Ctrl, 4 Shift, 8 Win; vk: Windows virtual-key code). false = another program owns it.</summary>
    bool SetHotkey(string id, uint mods, uint vk, Action pressed);
    void RemoveHotkey(string id);

    /// <summary>ffmpeg.exe of the launcher's player (null = not downloaded yet).</summary>
    string? FfmpegPath { get; }
}
