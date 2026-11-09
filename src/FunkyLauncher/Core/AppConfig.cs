// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
namespace FunkyLauncher.Core;

/// <summary>Fixed facts about the modlist and the services the launcher talks to.</summary>
public static class AppConfig
{
    public const string ModlistTitle = "NEON NIGHTS";
    public const string ModlistVersion = "0.1.0";
    public const string ModlistFileName = "Neon Nights.wabbajack";

    /// <summary>MO2 profile created by the modlist.</summary>
    public const string Mo2Profile = "Wabbajack";

    /// <summary>MO2 executable title that starts the game.</summary>
    public const string Mo2GameExecutable = "Cyberpunk 2077";

    public const string TelegramChannel = "FunkyPlayTV";
    public const string TelegramUrl = "https://t.me/" + TelegramChannel;
    public const string YouTubeUrl = "https://www.youtube.com/@" + TelegramChannel;

    /// <summary>Official Wabbajack releases (we download it, never repackage it).</summary>
    public const string WabbajackReleasesApi = "https://api.github.com/repos/wabbajack-tools/wabbajack/releases/latest";

    /// <summary>Required game version reported by wabbajack-cli list-games.</summary>
    public const string RequiredGameVersion = "2.5";

    /// <summary>Space needed: modlist (~19 GB) + archives (~18 GB) + reserve.</summary>
    public const long RequiredFreeBytes = 45L * 1024 * 1024 * 1024;

    public static string DataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FunkyLauncher");

    public const string DefaultInstallDir = @"C:\Games\NeonNights";
}
