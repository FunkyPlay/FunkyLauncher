// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
namespace FunkyLauncher.Core;

/// <summary>Launcher-wide settings. Everything about a specific modlist lives in catalog.json.</summary>
public static class AppConfig
{
    public const string TelegramChannel = "FunkyPlayTV";
    public const string TelegramUrl = "https://t.me/" + TelegramChannel;
    public const string YouTubeUrl = "https://www.youtube.com/@" + TelegramChannel;

    /// <summary>
    /// Online catalog of modlists (raw catalog.json on GitHub). Empty until the GitHub repository exists —
    /// then the built-in copy is used.
    /// </summary>
    public const string CatalogUrl = "";

    /// <summary>Official Wabbajack releases (we download it, never repackage it).</summary>
    public const string WabbajackReleasesApi = "https://api.github.com/repos/wabbajack-tools/wabbajack/releases/latest";

    public static string DataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FunkyLauncher");
}
