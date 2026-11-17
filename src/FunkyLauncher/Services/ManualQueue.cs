// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.IO.Compression;
using System.Text.Json;

namespace FunkyLauncher.Services;

/// <summary>One Nexus file the player has to download by hand (free Nexus account).</summary>
/// <summary>
/// Names of site files the list could not know in advance (the player's browser decides them): remembered per
/// mod folder and part in &lt;downloads&gt;\_funky-names.json, so the next install finds the file again.
/// </summary>
public static class NameMap
{
    private static string File(string downloads) => Path.Combine(downloads, "_funky-names.json");
    public static string Key(string modFolder, int part) => modFolder + "|" + part;

    private static Dictionary<string, string> Load(string downloads)
    {
        try { if (System.IO.File.Exists(File(downloads))) return JsonSerializer.Deserialize<Dictionary<string, string>>(System.IO.File.ReadAllText(File(downloads))) ?? new(); }
        catch { }
        return new();
    }

    public static string? Get(string downloads, string modFolder, int part) => Load(downloads).TryGetValue(Key(modFolder, part), out var n) ? n : null;

    public static void Set(string downloads, string key, string name)
    {
        var all = Load(downloads);
        all[key] = name;
        Directory.CreateDirectory(downloads);
        System.IO.File.WriteAllText(File(downloads), JsonSerializer.Serialize(all));
    }
}

public sealed class ManualItem
{
    /// <summary>"" = not known yet (a site file): the name the browser gives it is taken and remembered.</summary>
    public string ArchiveName { get; set; } = "";
    /// <summary>For a part of a multi-file mod: where its real name is remembered (<see cref="NameMap"/>).</summary>
    public string? Key { get; init; }
    public long Size { get; init; }
    public string ModName { get; init; } = "";
    public string GameDomain { get; init; } = "";
    public long ModId { get; init; }
    public long FileId { get; init; }
    public bool Done { get; set; }

    /// <summary>A page outside Nexus to download the file from (its author's site); null = the Nexus file page.</summary>
    public string? Url { get; init; }

    public string PageUrl => !string.IsNullOrEmpty(Url) ? Url : $"https://www.nexusmods.com/{GameDomain}/mods/{ModId}?tab=files&file_id={FileId}";
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
        // size 0 = not known yet (a list still being prepared): the file being there is enough, its hash is checked later
        return File.Exists(path) && (size <= 0 || new FileInfo(path).Length == size);
    }
}
