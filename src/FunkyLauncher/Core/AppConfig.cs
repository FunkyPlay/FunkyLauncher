// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
namespace FunkyLauncher.Core;

/// <summary>Launcher-wide settings. Everything about a specific modlist lives in catalog.json.</summary>
public static class AppConfig
{
    public const string TelegramChannel = "FunkyPlayTV";
    public const string TelegramUrl = "https://t.me/" + TelegramChannel;
    public const string YouTubeUrl = "https://www.youtube.com/@" + TelegramChannel;
    /// <summary>Launcher community in Telegram: download + support.</summary>
    public const string SupportUrl = "https://t.me/FunkyLauncher";
    /// <summary>The support bot (Funky Help sends the RustDesk ID here).</summary>
    public const string SupportBotName = "FunkyLauncherBot";
    public const string SupportBotUrl = "https://t.me/" + SupportBotName;
    public const string GitHubUrl = "https://github.com/FunkyPlay/FunkyLauncher";
    public const string PrivacyUrl = GitHubUrl + "/blob/main/PRIVACY.md";
    /// <summary>Start page of the built-in browser.</summary>
    public const string BrowserHome = "https://www.nexusmods.com/games/cyberpunk2077";

    public static string Version =>
        System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0";

    /// <summary>
    /// Online catalog of modlists (raw catalog.json on GitHub). If it can't be loaded,
    /// the cached or built-in copy is used.
    /// </summary>
    public const string CatalogUrl = "https://raw.githubusercontent.com/FunkyPlay/FunkyLauncher/main/catalog.json";

    /// <summary>Official Wabbajack releases (we download it, never repackage it).</summary>
    public const string WabbajackReleasesApi = "https://api.github.com/repos/wabbajack-tools/wabbajack/releases/latest";

    /// <summary>Settings and caches. FUNKYLAUNCHER_DATA (developer tests only) points it at a throwaway folder.</summary>
    public static string DataDir =>
        Environment.GetEnvironmentVariable("FUNKYLAUNCHER_DATA") is { Length: > 0 } test
            ? test
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FunkyLauncher");
}
