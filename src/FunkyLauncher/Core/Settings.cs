// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Text.Json;

namespace FunkyLauncher.Core;

/// <summary>User settings stored in %LOCALAPPDATA%\FunkyLauncher\settings.json.</summary>
public sealed class Settings
{
    public string Language { get; set; } = "";
    public string InstallDir { get; set; } = AppConfig.DefaultInstallDir;

    /// <summary>Path to the .wabbajack file. Empty = look next to the launcher.</summary>
    public string ModlistPath { get; set; } = "";

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

    public string DownloadsDir => Path.Combine(InstallDir, "downloads");

    public bool IsInstalled => File.Exists(Path.Combine(InstallDir, "ModOrganizer.exe"));

    /// <summary>Finds the .wabbajack file: settings, next to the exe, then the developer output folder.</summary>
    public string? ResolveModlist()
    {
        var candidates = new[]
        {
            ModlistPath,
            Path.Combine(AppContext.BaseDirectory, AppConfig.ModlistFileName),
            Path.Combine(AppConfig.DataDir, AppConfig.ModlistFileName),
            Path.Combine(@"C:\Games\Wabbajack_out", AppConfig.ModlistFileName),
        };
        return candidates.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p));
    }
}
