namespace FunkyLauncher.Core;

/// <summary>Our site and channels, in the launcher's language (Funky 24.11: the site must be visible in the launcher).</summary>
public static class SiteLinks
{
    private static bool En => Loc.Lang == "en";

    public static string Home => En ? "https://funkylauncher.com/en/" : "https://funkylauncher.com/";

    public static string Telegram => En ? "https://t.me/FunkyLauncher" : "https://t.me/FunkyPlayTV";

    public static string YouTube => En
        ? "https://www.youtube.com/channel/UCAYFhRSFcRDeRWVbHe5XPEw"
        : "https://www.youtube.com/@FunkyPlayTV";

    /// <summary>The modlist's page on the site, or null when there is none yet.</summary>
    public static string? Modlist(string title)
    {
        var slug = title.Trim().ToLowerInvariant().Replace(' ', '-');
        return slug is "neon-nights" or "atomic-nights" or "nova-nights" ? Home + slug + "/" : null;
    }
}
