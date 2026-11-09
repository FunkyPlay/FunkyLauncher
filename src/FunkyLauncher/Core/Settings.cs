// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Text.Json;

namespace FunkyLauncher.Core;

/// <summary>User settings stored in %LOCALAPPDATA%\FunkyLauncher\settings.json.</summary>
public sealed class Settings
{
    public string Language { get; set; } = "";
    public string SelectedId { get; set; } = "neon-nights";

    /// <summary>Install folder per modlist id.</summary>
    public Dictionary<string, string> InstallDirs { get; set; } = new();

    /// <summary>A .wabbajack file the user picked by hand, per modlist id.</summary>
    public Dictionary<string, string> ModlistFiles { get; set; } = new();

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

    public string DownloadsDir(Modlist m) => Path.Combine(InstallDir(m), "downloads");

    public bool IsInstalled(Modlist m) => File.Exists(Path.Combine(InstallDir(m), "ModOrganizer.exe"));

    /// <summary>Where a downloaded .wabbajack file for this modlist version is kept.</summary>
    public static string CachedWabbajack(Modlist m) =>
        Path.Combine(AppConfig.DataDir, "modlists", m.Id, $"{m.Version}.wabbajack");

    /// <summary>Finds the .wabbajack file: picked by hand, downloaded, next to the exe, developer folder.</summary>
    public string? ResolveWabbajack(Modlist m)
    {
        ModlistFiles.TryGetValue(m.Id, out var picked);
        var candidates = new[]
        {
            picked,
            CachedWabbajack(m),
            string.IsNullOrEmpty(m.WabbajackFile) ? null : Path.Combine(AppContext.BaseDirectory, m.WabbajackFile),
            string.IsNullOrEmpty(m.WabbajackFile) ? null : Path.Combine(@"C:\Games\Wabbajack_out", m.WabbajackFile),
        };
        return candidates.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p));
    }
}
