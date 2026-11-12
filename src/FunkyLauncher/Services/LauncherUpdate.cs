// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Net.Http;
using System.Text.Json;
using FunkyLauncher.Core;

namespace FunkyLauncher.Services;

/// <summary>
/// Is a newer Funky Launcher out? Reads the latest GitHub release of FunkyPlay/FunkyLauncher (one small request at start,
/// nothing is sent about the player). The launcher never updates itself: it only shows a bar with a download button.
/// </summary>
public static class LauncherUpdate
{
    public const string LatestApi = "https://api.github.com/repos/FunkyPlay/FunkyLauncher/releases/latest";
    public const string DownloadUrl = "https://funkylauncher.com/download";

    /// <summary>The newer version ("0.1.9"), or null if this one is the latest or GitHub can't be reached.</summary>
    public static async Task<string?> NewerVersionAsync(CancellationToken token)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("FunkyLauncher/" + AppConfig.Version);
            using var doc = JsonDocument.Parse(await http.GetStringAsync(LatestApi, token));
            var tag = doc.RootElement.GetProperty("tag_name").GetString()?.TrimStart('v', 'V') ?? "";
            return Version.TryParse(tag, out var latest) && Version.TryParse(AppConfig.Version, out var mine) && latest > mine
                ? tag : null;
        }
        catch { return null; }
    }
}
