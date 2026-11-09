// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.IO.Compression;
using System.Text.Json;

namespace FunkyLauncher.Services;

/// <summary>One Nexus file the player has to download by hand (free Nexus account).</summary>
public sealed class ManualItem
{
    public string ArchiveName { get; init; } = "";
    public long Size { get; init; }
    public string ModName { get; init; } = "";
    public string GameDomain { get; init; } = "";
    public long ModId { get; init; }
    public long FileId { get; init; }
    public bool Done { get; set; }

    public string PageUrl => $"https://www.nexusmods.com/{GameDomain}/mods/{ModId}?tab=files&file_id={FileId}";
}

/// <summary>Builds the list of Nexus archives that are not yet in the downloads folder.</summary>
public static class ManualQueue
{
    // Wabbajack game names → Nexus site domains (extend as new games are added)
    private static readonly Dictionary<string, string> Domains = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Cyberpunk2077"] = "cyberpunk2077",
        ["Starfield"] = "starfield",
        ["SkyrimSpecialEdition"] = "skyrimspecialedition",
        ["Fallout4"] = "fallout4",
        ["Witcher3"] = "witcher3",
    };

    public static List<ManualItem> Build(string wabbajackFile, string downloadsDir)
    {
        var items = new List<ManualItem>();
        using var zip = ZipFile.OpenRead(wabbajackFile);
        using var s = zip.GetEntry("modlist")!.Open();
        using var doc = JsonDocument.Parse(s);
        foreach (var a in doc.RootElement.GetProperty("Archives").EnumerateArray())
        {
            var state = a.GetProperty("State");
            var type = state.GetProperty("$type").GetString() ?? "";
            if (!type.StartsWith("NexusDownloader", StringComparison.Ordinal)) continue;

            var name = a.GetProperty("Name").GetString() ?? "";
            var size = a.GetProperty("Size").GetInt64();
            if (IsPresent(downloadsDir, name, size)) continue;

            var game = state.GetProperty("GameName").GetString() ?? "";
            items.Add(new ManualItem
            {
                ArchiveName = name,
                Size = size,
                ModName = state.TryGetProperty("Name", out var n) ? n.GetString() ?? name : name,
                GameDomain = Domains.TryGetValue(game, out var d) ? d : game.ToLowerInvariant(),
                ModId = state.GetProperty("ModID").GetInt64(),
                FileId = state.GetProperty("FileID").GetInt64(),
            });
        }
        return items;
    }

    public static bool IsPresent(string downloadsDir, string name, long size)
    {
        var path = Path.Combine(downloadsDir, name);
        return File.Exists(path) && new FileInfo(path).Length == size;
    }
}
