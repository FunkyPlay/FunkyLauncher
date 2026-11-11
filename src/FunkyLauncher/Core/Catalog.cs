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
    /// <summary>Full description shown under "More ▾" in the library.</summary>
    [JsonPropertyName("details")] public LocText? Details { get; set; }
    /// <summary>Requirements block (game version, recommended PC, disk space).</summary>
    [JsonPropertyName("requirements")] public LocText? Requirements { get; set; }

    /// <summary>Banner: "res:/Assets/x.jpg" for a bundled image or an https URL.</summary>
    [JsonPropertyName("banner")] public string Banner { get; set; } = "";

    [JsonPropertyName("wabbajackFile")] public string WabbajackFile { get; set; } = "";
    [JsonPropertyName("wabbajackUrl")] public string? WabbajackUrl { get; set; }
    [JsonPropertyName("sizeGb")] public int SizeGb { get; set; } = 40;
    [JsonPropertyName("modCount")] public int ModCount { get; set; }

    [JsonPropertyName("mo2Profile")] public string Mo2Profile { get; set; } = "Wabbajack";
    [JsonPropertyName("mo2Executable")] public string Mo2Executable { get; set; } = "";
    /// <summary>Game process file name, e.g. "Cyberpunk2077.exe" — the launcher watches it after PLAY.</summary>
    [JsonPropertyName("gameExe")] public string GameExe { get; set; } = "";
    [JsonPropertyName("defaultInstallDir")] public string DefaultInstallDir { get; set; } = "";

    /// <summary>
    /// Where the game keeps its saves, for save backups. Tokens: {SavedGames}, {Documents}, {LocalAppData}, {AppData}.
    /// Example: "{SavedGames}\\CD Projekt Red\\Cyberpunk 2077".
    /// </summary>
    [JsonPropertyName("saveFolder")] public string? SaveFolder { get; set; }

    /// <summary>"Can my PC run it?" requirements for this modlist (null = no check).</summary>
    [JsonPropertyName("pcCheck")] public PcRequirements? PcCheck { get; set; }

    /// <summary>The real save folder on this PC (Documents may live in OneDrive), or null if unknown.</summary>
    public string? ResolveSaveFolder()
    {
        if (string.IsNullOrWhiteSpace(SaveFolder)) return null;
        try
        {
            var p = SaveFolder
                .Replace("{SavedGames}", KnownFolders.SavedGames, StringComparison.OrdinalIgnoreCase)
                .Replace("{Documents}", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), StringComparison.OrdinalIgnoreCase)
                .Replace("{LocalAppData}", Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), StringComparison.OrdinalIgnoreCase)
                .Replace("{AppData}", Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), StringComparison.OrdinalIgnoreCase);
            return p.Contains('{') || !Path.IsPathFullyQualified(p) ? null : Path.GetFullPath(p);
        }
        catch { return null; }
    }

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
        var builtIn = Parse(s);
        BuiltInSaveFolders = builtIn.Modlists.Where(m => !string.IsNullOrWhiteSpace(m.SaveFolder)).ToDictionary(m => m.Id, m => m.SaveFolder!);
        BuiltInPcChecks = builtIn.Modlists.Where(m => m.PcCheck != null).ToDictionary(m => m.Id, m => m.PcCheck!);
        Apply(builtIn);
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

    /// <summary>Save folders known to this launcher version — used when an online/cached catalog has none.</summary>
    private static Dictionary<string, string> BuiltInSaveFolders = new();
    private static Dictionary<string, PcRequirements> BuiltInPcChecks = new();

    private static void Apply(CatalogFile c)
    {
        foreach (var m in c.Modlists)
        {
            if (string.IsNullOrWhiteSpace(m.SaveFolder) && BuiltInSaveFolders.TryGetValue(m.Id, out var f)) m.SaveFolder = f;
            if (m.PcCheck == null && BuiltInPcChecks.TryGetValue(m.Id, out var pc)) m.PcCheck = pc;
        }
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

/// <summary>Windows folders that have no Environment.SpecialFolder value.</summary>
internal static class KnownFolders
{
    private static readonly Guid SavedGamesId = new("4C5C32FF-BB9D-43b0-B5B4-2D72E54EAAA4");

    public static string SavedGames
    {
        get
        {
            if (SHGetKnownFolderPath(SavedGamesId, 0, IntPtr.Zero, out var ptr) == 0)
            {
                try { return System.Runtime.InteropServices.Marshal.PtrToStringUni(ptr) ?? Fallback; }
                finally { System.Runtime.InteropServices.Marshal.FreeCoTaskMem(ptr); }
            }
            return Fallback;
        }
    }

    private static string Fallback => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Saved Games");

    [System.Runtime.InteropServices.DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPStruct)] Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);
}
