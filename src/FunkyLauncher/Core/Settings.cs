// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Text.Json;

namespace FunkyLauncher.Core;

public sealed class PreviousInstall
{
    public string Dir { get; set; } = "";
    public string Version { get; set; } = "";
}

/// <summary>User settings stored in %LOCALAPPDATA%\FunkyLauncher\settings.json.</summary>
public sealed class Settings
{
    public string Language { get; set; } = "";
    public string SelectedId { get; set; } = "neon-nights";

    /// <summary>Save backups: one before every PLAY; where backups live ("" = %LOCALAPPDATA%\FunkyLauncher\Backups).</summary>
    public bool AutoBackup { get; set; } = true;
    public string BackupDir { get; set; } = "";

    /// <summary>Recording mode options; ShootWallpaperFile "" = the FunkyPlay wallpaper.</summary>
    public bool ShootIcons { get; set; } = true;
    public bool ShootWallpaper { get; set; } = true;
    public bool ShootWindows { get; set; } = true;
    public bool ShootTaskbar { get; set; }
    public string ShootWallpaperFile { get; set; } = "";

    /// <summary>Game mode: programs to close (process names), torrents, power mode, OneDrive, start them again, on PLAY.</summary>
    public List<string> GameApps { get; set; } = new();
    public bool GameTorrents { get; set; } = true;
    public bool GamePower { get; set; } = true;
    public bool GameOneDrive { get; set; }
    public bool GameRelaunch { get; set; } = true;
    public bool GameOnPlay { get; set; }

    /// <summary>Funky Desktops (test, hidden unless enabled with --enable-desktops): on/off and, per virtual desktop id, the icons hidden there.</summary>
    /// <summary>Funky Pro key ("FPRO-…"), checked offline by ProLicense.</summary>
    public string ProKey { get; set; } = "";

    public bool DesktopsBeta { get; set; }
    public bool DesktopsOn { get; set; }
    public Dictionary<string, List<string>> DesktopHide { get; set; } = new();

    /// <summary>Privacy: load Telegram news on start / refresh the modlist catalog from GitHub on start.</summary>
    public bool LoadNews { get; set; } = true;
    public bool UpdateCatalog { get; set; } = true;
    /// <summary>The privacy notice was shown on the first start.</summary>
    public bool PrivacyNoticeShown { get; set; }

    /// <summary>Last window position and size (null = centered default).</summary>
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }

    /// <summary>Install folder per modlist id.</summary>
    public Dictionary<string, string> InstallDirs { get; set; } = new();

    /// <summary>A .wabbajack file the user picked by hand, per modlist id.</summary>
    public Dictionary<string, string> ModlistFiles { get; set; } = new();

    /// <summary>Add-ons (e.g. the 18+ add-on) per add-on id: on/off, level 2, when the age was confirmed. Stays on this PC.</summary>
    public Dictionary<string, AddonPrefs> Addons { get; set; } = new();

    /// <summary>Language of each modlist ("ru" / "en") the player chose on the install page. Default: the launcher language.</summary>
    public Dictionary<string, string> ModlistLang { get; set; } = new();

    public string LangOf(string modlistId) =>
        ModlistLang.TryGetValue(modlistId, out var l) && l is "ru" or "en" ? l : (Loc.Lang == "en" ? "en" : "ru");

    /// <summary>The player ticked "18+ add-on" before installing: offer it right after the install.</summary>
    public HashSet<string> AdultWanted { get; set; } = new();

    private static string FilePath => Path.Combine(AppConfig.DataDir, "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch
        {
            // a broken file falls back to defaults
        }
        return new Settings();
    }

    public void Save()
    {
        Directory.CreateDirectory(AppConfig.DataDir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    public string InstallDir(Modlist m) =>
        InstallDirs.TryGetValue(m.Id, out var d) && !string.IsNullOrWhiteSpace(d) ? d : m.DefaultInstallDir;

    public void SetInstallDir(Modlist m, string dir) { InstallDirs[m.Id] = dir.Trim(); Save(); }

    /// <summary>Modlist version installed in the current folder, per modlist id.</summary>
    public Dictionary<string, string> InstalledVersions { get; set; } = new();

    /// <summary>Mod archives folder, fixed at the first install so updates reuse the same archives.</summary>
    public Dictionary<string, string> DownloadDirs { get; set; } = new();

    /// <summary>The previous working version kept after an update (for rollback), per modlist id.</summary>
    public Dictionary<string, PreviousInstall> Previous { get; set; } = new();

    public string DownloadsDir(Modlist m) =>
        DownloadDirs.TryGetValue(m.Id, out var d) && !string.IsNullOrWhiteSpace(d) ? d : Path.Combine(InstallDir(m), "downloads");

    /// <summary>File the launcher writes into a folder after it installed (or checked) the modlist there.</summary>
    public const string MarkerName = "funkylauncher.json";

    private static bool HasMo2(string dir) =>
        !string.IsNullOrWhiteSpace(dir) && File.Exists(Path.Combine(dir, "ModOrganizer.exe"));

    /// <summary>Installed = Mod Organizer is there AND the launcher put it there (marker file).</summary>
    public static bool IsInstalledIn(string dir) => HasMo2(dir) && File.Exists(Path.Combine(dir, MarkerName));

    public bool IsInstalled(Modlist m) => IsInstalledIn(InstallDir(m));

    /// <summary>
    /// A modlist folder the launcher did not install (left from an old launcher version or copied by hand):
    /// no PLAY button for it — the player either verifies it ("check and use") or installs again.
    /// </summary>
    public bool FoundUnknown(Modlist m) => HasMo2(InstallDir(m)) && !IsInstalled(m);

    /// <summary>Remembers that this folder holds a working modlist of this version.</summary>
    public void MarkInstalled(Modlist m, string dir, string version)
    {
        File.WriteAllText(Path.Combine(dir, MarkerName), JsonSerializer.Serialize(
            new { modlist = m.Id, version, date = DateTime.Now.ToString("yyyy-MM-dd HH:mm") },
            new JsonSerializerOptions { WriteIndented = true }));
        InstalledVersions[m.Id] = version;
        Save();
    }

    /// <summary>Installed version; installs made before version tracking count as the catalog version.</summary>
    public string? InstalledVersion(Modlist m) =>
        !IsInstalled(m) ? null : InstalledVersions.TryGetValue(m.Id, out var v) && v.Length > 0 ? v : m.Version;

    public bool NeedsUpdate(Modlist m) => m.IsReleased && IsInstalled(m) && m.Version.Length > 0 && InstalledVersion(m) != m.Version;

    /// <summary>Folder for a new version: "C:\Games\NeonNights" → "C:\Games\NeonNights-0.2.0".</summary>
    public string UpdateDir(Modlist m)
    {
        var dir = InstallDir(m).TrimEnd('\\', '/');
        var old = InstalledVersion(m);
        if (!string.IsNullOrEmpty(old) && dir.EndsWith("-" + old)) dir = dir[..^(old.Length + 1)];
        return dir + "-" + m.Version;
    }

    public PreviousInstall? PreviousOf(Modlist m) =>
        Previous.TryGetValue(m.Id, out var p) && IsInstalledIn(p.Dir) ? p : null;

    /// <summary>Swaps the current and the previous version (rollback, or back to the newer one).</summary>
    public void SwapWithPrevious(Modlist m)
    {
        var prev = PreviousOf(m) ?? throw new InvalidOperationException("no previous version");
        Previous[m.Id] = new PreviousInstall { Dir = InstallDir(m), Version = InstalledVersion(m) ?? "" };
        InstallDirs[m.Id] = prev.Dir;
        InstalledVersions[m.Id] = prev.Version;
        Save();
    }

    /// <summary>Where a downloaded .wabbajack file for this modlist version is kept.</summary>
    public static string CachedWabbajack(Modlist m) =>
        Path.Combine(AppConfig.DataDir, "modlists", m.Id, $"{m.Version}.wabbajack");

    /// <summary>Finds the .wabbajack file: picked by hand, downloaded for this version, next to the exe.</summary>
    public string? ResolveWabbajack(Modlist m)
    {
        ModlistFiles.TryGetValue(m.Id, out var picked);
        var candidates = new[]
        {
            picked,
            CachedWabbajack(m),
            string.IsNullOrEmpty(m.WabbajackFile) ? null : Path.Combine(AppContext.BaseDirectory, m.WabbajackFile),
        };
        return candidates.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p));
    }
}
