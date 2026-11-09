// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FunkyLauncher.Core;

/// <summary>Text in two languages.</summary>
public sealed class LocText
{
    [JsonPropertyName("ru")] public string Ru { get; set; } = "";
    [JsonPropertyName("en")] public string En { get; set; } = "";
    public override string ToString() => Loc.Lang == "en" && En.Length > 0 ? En : Ru;
}

/// <summary>One modlist (project) in the catalog.</summary>
public sealed class Modlist
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("author")] public string Author { get; set; } = "Funky";
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; } = "released"; // released | soon

    /// <summary>Game name exactly as wabbajack-cli list-games prints it.</summary>
    [JsonPropertyName("game")] public string Game { get; set; } = "";
    [JsonPropertyName("gameVersion")] public string GameVersion { get; set; } = "";
    [JsonPropertyName("dlcName")] public LocText? DlcName { get; set; }
    /// <summary>Folder inside the game that proves the DLC is installed (relative path).</summary>
    [JsonPropertyName("dlcCheckPath")] public string? DlcCheckPath { get; set; }

    [JsonPropertyName("tag")] public LocText Tag { get; set; } = new();
    [JsonPropertyName("description")] public LocText Description { get; set; } = new();

    /// <summary>Banner: "res:/Assets/x.jpg" for a bundled image or an https URL.</summary>
    [JsonPropertyName("banner")] public string Banner { get; set; } = "";

    [JsonPropertyName("wabbajackFile")] public string WabbajackFile { get; set; } = "";
    [JsonPropertyName("wabbajackUrl")] public string? WabbajackUrl { get; set; }
    [JsonPropertyName("sizeGb")] public int SizeGb { get; set; } = 40;
    [JsonPropertyName("modCount")] public int ModCount { get; set; }

    [JsonPropertyName("mo2Profile")] public string Mo2Profile { get; set; } = "Wabbajack";
    [JsonPropertyName("mo2Executable")] public string Mo2Executable { get; set; } = "";
    [JsonPropertyName("defaultInstallDir")] public string DefaultInstallDir { get; set; } = "";

    public bool IsReleased => Status == "released";
    public long RequiredFreeBytes => (SizeGb + 5L) * 1024 * 1024 * 1024;
}

/// <summary>A useful program on the Software page. The button opens the official site — we never rehost installers.</summary>
public sealed class SoftwareItem
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("category")] public LocText Category { get; set; } = new();
    [JsonPropertyName("description")] public LocText Description { get; set; } = new();
    [JsonPropertyName("url")] public string Url { get; set; } = "";
    /// <summary>Tile color, e.g. "#FF3CC8".</summary>
    [JsonPropertyName("color")] public string Color { get; set; } = "#3CE6FF";
}

public sealed class CatalogFile
{
    [JsonPropertyName("version")] public int Version { get; set; } = 1;
    [JsonPropertyName("modlists")] public List<Modlist> Modlists { get; set; } = new();
    [JsonPropertyName("software")] public List<SoftwareItem> Software { get; set; } = new();
}

/// <summary>
/// The list of all FunkyPlay modlists. Loaded from GitHub (so new projects appear without updating
/// the launcher); falls back to the last downloaded copy, then to the copy built into the launcher.
/// </summary>
public static class Catalog
{
    public static List<Modlist> Items { get; private set; } = new();
    public static List<SoftwareItem> Software { get; private set; } = new();
    public static string Source { get; private set; } = "";

    private static string CachePath => Path.Combine(AppConfig.DataDir, "catalog.json");

    public static void LoadBuiltIn()
    {
        var uri = new Uri("pack://application:,,,/catalog.json");
        using var s = System.Windows.Application.GetResourceStream(uri)!.Stream;
        Apply(Parse(s));
        Source = "built-in";
        if (File.Exists(CachePath))
        {
            try
            {
                using var f = File.OpenRead(CachePath);
                var cached = Parse(f);
                if (cached.Modlists.Count > 0) { Apply(cached); Source = "cache"; }
            }
            catch { }
        }
    }

    private static void Apply(CatalogFile c)
    {
        Items = c.Modlists;
        Software = c.Software.Where(x => x.Name.Length > 0 && x.Url.StartsWith("https://")).ToList();
    }

    /// <summary>Tries to refresh from the internet. Returns true if the list changed source.</summary>
    public static async Task<bool> RefreshAsync(CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(AppConfig.CatalogUrl)) return false;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("FunkyLauncher");
            var json = await http.GetStringAsync(AppConfig.CatalogUrl, token);
            using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
            var items = Parse(ms);
            if (items.Modlists.Count == 0) return false;
            Apply(items);
            Source = "online";
            Directory.CreateDirectory(AppConfig.DataDir);
            File.WriteAllText(CachePath, json);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static Modlist? Find(string? id) => Items.FirstOrDefault(m => m.Id == id);

    private static CatalogFile Parse(Stream s)
    {
        var c = JsonSerializer.Deserialize<CatalogFile>(s) ?? new CatalogFile();
        c.Modlists = c.Modlists.Where(m => m.Id.Length > 0).ToList();
        return c;
    }
}
