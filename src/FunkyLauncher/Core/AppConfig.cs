// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
namespace FunkyLauncher.Core;

/// <summary>Launcher-wide settings. Everything about a specific modlist lives in catalog.json.</summary>
public static class AppConfig
{
    public const string TelegramChannel = "FunkyPlayTV";
    public const string TelegramUrl = "https://t.me/" + TelegramChannel;
    public const string YouTubeUrl = "https://www.youtube.com/@" + TelegramChannel;
    /// <summary>The English YouTube channel (Funky 20.11): the fallback when no catalog has "youtubeEn".</summary>
    public const string YouTubeEnUrl = "https://www.youtube.com/@FunkyPlayTV_EN";
    /// <summary>The English Telegram channel (since 2026-11-20 @FunkyLauncher is English, @FunkyPlayTV Russian).</summary>
    public const string TelegramEnUrl = "https://t.me/FunkyLauncher";
    /// <summary>The Telegram channel for the launcher's language: English players → @FunkyLauncher, Russian → @FunkyPlayTV.</summary>
    public static string Telegram => Loc.Lang == "en" ? TelegramEnUrl : TelegramUrl;
    /// <summary>Launcher news: the Telegram channel of the player's language.</summary>
    public static string LauncherNewsUrl => Telegram;
    /// <summary>The support bot (Funky Help sends the RustDesk ID here).</summary>
    public const string SupportBotName = "FunkyLauncherBot";
    /// <summary>Support chat inside the launcher (same queue as the bot). The catalog's "supportApi" overrides it.</summary>
    public const string SupportApi = "https://api.funkylauncher.com/support";
    public const string SupportBotUrl = "https://t.me/" + SupportBotName;
    public const string GitHubUrl = "https://github.com/FunkyPlay/FunkyLauncher";
    public const string PrivacyUrl = GitHubUrl + "/blob/main/PRIVACY.md";
    /// <summary>Start page of the built-in browser.</summary>
    public const string BrowserHome = "https://www.nexusmods.com/games/cyberpunk2077";

    public static string Version =>
        System.Reflection.Assembly.GetExecutingAssembly().GetName().Version is { } av ? av.ToString(av.Revision > 0 ? 4 : 3) : "0.1.0";

    /// <summary>
    /// Online catalog of modlists (raw catalog.json on GitHub). If it can't be loaded,
    /// the cached or built-in copy is used.
    /// </summary>
    /// <summary>The catalog from main; FUNKYLAUNCHER_ADDONS_BRANCH (developer tests only) reads it from the test branch too,
    /// so a modlist update (e.g. Neon 0.1.7) can be tested before main changes.</summary>
    public static string CatalogUrl =>
        "https://raw.githubusercontent.com/FunkyPlay/FunkyLauncher/"
        + (Environment.GetEnvironmentVariable("FUNKYLAUNCHER_ADDONS_BRANCH") is { Length: > 0 } b && b.All(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.') ? b : "main")
        + "/catalog2.json";

    /// <summary>Official Wabbajack releases (we download it, never repackage it).</summary>
    public const string WabbajackReleasesApi = "https://api.github.com/repos/wabbajack-tools/wabbajack/releases/latest";

    /// <summary>Settings and caches. FUNKYLAUNCHER_DATA (developer tests only) points it at a throwaway folder.</summary>
    public static string DataDir =>
        Environment.GetEnvironmentVariable("FUNKYLAUNCHER_DATA") is { Length: > 0 } test
            ? test
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FunkyLauncher");
}
