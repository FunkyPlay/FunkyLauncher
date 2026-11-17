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

    /// <summary>
    /// "wabbajack" (default): installed from a .wabbajack file. "nexus": installed by the launcher itself from the Nexus
    /// lists in addons/ (&lt;id&gt;-main.json + packs + &lt;id&gt;-order.json) into a fresh portable MO2 — for games whose
    /// files Wabbajack would reject (a game version it does not know).
    /// </summary>
    [JsonPropertyName("installer")] public string Installer { get; set; } = "wabbajack";
    /// <summary>MO2's game name for ModOrganizer.ini (e.g. "Starfield").</summary>
    [JsonPropertyName("mo2GameName")] public string Mo2GameName { get; set; } = "";
    /// <summary>The profile's game INI the launcher writes (e.g. "StarfieldCustom.ini").</summary>
    [JsonPropertyName("mo2IniName")] public string Mo2IniName { get; set; } = "";
    /// <summary>
    /// Every game INI MO2 keeps per profile (e.g. "StarfieldPrefs.ini"). Missing ones are copied from
    /// Documents\My Games\&lt;myGamesFolder&gt; (or created empty) so MO2 shows no "missing INI files" warning.
    /// </summary>
    [JsonPropertyName("mo2ProfileInis")] public List<string> Mo2ProfileInis { get; set; } = new();
    [JsonPropertyName("myGamesFolder")] public string MyGamesFolder { get; set; } = "";
    /// <summary>A Russian voice-over we may only link to (its authors did not allow installing it): shown as a button for the Russian version.</summary>
    [JsonPropertyName("voiceLink")] public string VoiceLink { get; set; } = "";
    /// <summary>MO2 executables for a fresh instance; binary is relative to the game folder. The first one is PLAY.</summary>
    [JsonPropertyName("mo2Executables")] public List<Mo2Exe> Mo2Executables { get; set; } = new();
    /// <summary>Folders the game may be in when no store reports it (relative to drive roots, e.g. "Games\\Starfield").</summary>
    [JsonPropertyName("gameFolderHints")] public List<string> GameFolderHints { get; set; } = new();
    /// <summary>Steam app id — finds the game in any Steam library.</summary>
    [JsonPropertyName("steamAppId")] public string SteamAppId { get; set; } = "";
    /// <summary>The game's shader cache files/folders for "Reset shader cache" ({LocalAppData}, {Documents}, {Game} tokens).</summary>
    [JsonPropertyName("shaderCache")] public List<string> ShaderCache { get; set; } = new();
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

    /// <summary>Loader files the launcher copies from the modlist into the game folder before PLAY (MO2 can't provide them).</summary>
    [JsonPropertyName("gameLoaders")] public List<GameLoader> GameLoaders { get; set; } = new();
    /// <summary>What those mods create in the game folder by themselves — removed by "Restore clean game".</summary>
    [JsonPropertyName("gameCleanup")] public List<GameLoader> GameCleanup { get; set; } = new();

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
            // MO2 with profile-local saves (Starfield): the saves live inside the modlist folder
            if (p.Contains("{InstallDir}", StringComparison.OrdinalIgnoreCase) && InstallDirOf?.Invoke(this) is { Length: > 0 } dir)
                p = p.Replace("{InstallDir}", dir, StringComparison.OrdinalIgnoreCase);
            return p.Contains('{') || !Path.IsPathFullyQualified(p) ? null : Path.GetFullPath(p);
        }
        catch { return null; }
    }

    /// <summary>The install folder of a modlist on this PC (set once by the main window from the settings).</summary>
    public static Func<Modlist, string?>? InstallDirOf { get; set; }

    public bool IsReleased => Status == "released";
    public long RequiredFreeBytes => (SizeGb + 5L) * 1024 * 1024 * 1024;
}

public sealed class Mo2Exe
{
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("binary")] public string Binary { get; set; } = "";
}

/// <summary>Official Mod Organizer 2 release for modlists the launcher installs itself (never rehosted).</summary>
public sealed class Mo2Source
{
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("url")] public string Url { get; set; } = "";
    [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
}

/// <summary>A file in the game folder: path relative to the game folder (and to the mod folder), and the mod folder name prefix.</summary>
public sealed class GameLoader
{
    [JsonPropertyName("path")] public string Path { get; set; } = "";
    [JsonPropertyName("mod")] public string Mod { get; set; } = "";
}

/// <summary>A useful program on the Software page. The button opens the official site — we never rehost installers.</summary>
public sealed class SoftwareItem
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("category")] public LocText Category { get; set; } = new();
    [JsonPropertyName("description")] public LocText Description { get; set; } = new();
    [JsonPropertyName("url")] public string Url { get; set; } = "";
    /// <summary>Tile color, e.g. "#FF3FD2".</summary>
    [JsonPropertyName("color")] public string Color { get; set; } = "#7AE8FF";
    /// <summary>winget package id for a one-click install (empty = only the website button).</summary>
    [JsonPropertyName("winget")] public string Winget { get; set; } = "";
    /// <summary>winget source: "winget" (community repo) or "msstore" (Microsoft Store).</summary>
    [JsonPropertyName("wingetSource")] public string WingetSource { get; set; } = "winget";
    /// <summary>Shown in the install confirmation (risks, personal-use-only license).</summary>
    [JsonPropertyName("warning")] public LocText Warning { get; set; } = new();
    /// <summary>Launcher languages the card is shown in, e.g. ["ru"] (empty = all).</summary>
    [JsonPropertyName("langs")] public List<string> Langs { get; set; } = new();
}

public sealed class CatalogFile
{
    [JsonPropertyName("version")] public int Version { get; set; } = 1;
    [JsonPropertyName("modlists")] public List<Modlist> Modlists { get; set; } = new();
    [JsonPropertyName("software")] public List<SoftwareItem> Software { get; set; } = new();
    /// <summary>Where the player engine is downloaded from (can be updated on GitHub without a new launcher).</summary>
    [JsonPropertyName("playerEngine")] public PlayerEngineSources? PlayerEngine { get; set; }
    /// <summary>"Funky Help": the official RustDesk client and our own RustDesk server (address + public key).</summary>
    [JsonPropertyName("remoteHelp")] public RemoteHelpSource? RemoteHelp { get; set; }
    /// <summary>Social links for the English audience (Instagram, TikTok) — can change without a new launcher (e.g. a new nickname).</summary>
    [JsonPropertyName("social")] public SocialLinks? Social { get; set; }
    [JsonPropertyName("mo2")] public Mo2Source? Mo2 { get; set; }
}

public sealed class SocialLinks
{
    [JsonPropertyName("instagram")] public string Instagram { get; set; } = "";
    [JsonPropertyName("tiktok")] public string TikTok { get; set; } = "";
}

public sealed class RemoteHelpSource
{
    [JsonPropertyName("url")] public string Url { get; set; } = "";
    [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("host")] public string Host { get; set; } = "";
    /// <summary>The server's PUBLIC key (safe to publish): clients refuse servers without it.</summary>
    [JsonPropertyName("key")] public string Key { get; set; } = "";
}

/// <summary>One downloadable part of the player engine: URL, SHA-256 and what to take out of the archive.</summary>
public sealed class EngineFile
{
    [JsonPropertyName("url")] public string Url { get; set; } = "";
    [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
    /// <summary>Path inside the .7z archive (wildcards allowed), extracted flat into the engine folder.</summary>
    [JsonPropertyName("extract")] public string Extract { get; set; } = "";
}

public sealed class PlayerEngineSources
{
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("sevenZip")] public EngineFile SevenZip { get; set; } = new();
    [JsonPropertyName("mpv")] public EngineFile Mpv { get; set; } = new();
    [JsonPropertyName("ffmpeg")] public EngineFile Ffmpeg { get; set; } = new();
}

/// <summary>
/// The list of all FunkyPlay modlists. Loaded from GitHub (so new projects appear without updating
/// the launcher); falls back to the last downloaded copy, then to the copy built into the launcher.
/// </summary>
public static class Catalog
{
    public static List<Modlist> Items { get; private set; } = new();
    public static List<SoftwareItem> Software { get; private set; } = new();
    /// <summary>Player engine sources: online/cached catalog first, else the ones built into this launcher.</summary>
    public static PlayerEngineSources? PlayerEngine { get; private set; }
    private static PlayerEngineSources? BuiltInPlayerEngine;
    public static RemoteHelpSource? RemoteHelp { get; private set; }
    private static RemoteHelpSource? BuiltInRemoteHelp;
    /// <summary>Instagram / TikTok: online/cached catalog first, each link falls back to the built-in one.</summary>
    public static SocialLinks Social { get; private set; } = new();
    private static SocialLinks BuiltInSocial = new();
    public static string Source { get; private set; } = "";

    private static string CachePath => Path.Combine(AppConfig.DataDir, "catalog.json");

    public static void LoadBuiltIn()
    {
        var uri = new Uri("pack://application:,,,/catalog.json");
        using var s = System.Windows.Application.GetResourceStream(uri)!.Stream;
        var builtIn = Parse(s);
        BuiltInSaveFolders = builtIn.Modlists.Where(m => !string.IsNullOrWhiteSpace(m.SaveFolder)).ToDictionary(m => m.Id, m => m.SaveFolder!);
        BuiltInPcChecks = builtIn.Modlists.Where(m => m.PcCheck != null).ToDictionary(m => m.Id, m => m.PcCheck!);
        BuiltInLoaders = builtIn.Modlists.Where(m => m.GameLoaders.Count > 0).ToDictionary(m => m.Id, m => (m.GameLoaders, m.GameCleanup));
        BuiltInPlayerEngine = builtIn.PlayerEngine;
        BuiltInRemoteHelp = builtIn.RemoteHelp;
        BuiltInMo2 = builtIn.Mo2;
        BuiltInSocial = builtIn.Social ?? new();
        BuiltInSoftware = builtIn.Software.Where(x => x.Winget.Length > 0 || x.Langs.Count > 0).GroupBy(SoftKey).ToDictionary(g => g.Key, g => g.First());
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
    /// <summary>One-click install data known to this launcher — used when an online/cached catalog (older) has none.</summary>
    private static Dictionary<string, SoftwareItem> BuiltInSoftware = new();
    private static string SoftKey(SoftwareItem x) => x.Id.Length > 0 ? x.Id : x.Name;
    private static Dictionary<string, (List<GameLoader> Loaders, List<GameLoader> Cleanup)> BuiltInLoaders = new();

    private static void Apply(CatalogFile c)
    {
        foreach (var m in c.Modlists)
        {
            if (string.IsNullOrWhiteSpace(m.SaveFolder) && BuiltInSaveFolders.TryGetValue(m.Id, out var f)) m.SaveFolder = f;
            if (m.PcCheck == null && BuiltInPcChecks.TryGetValue(m.Id, out var pc)) m.PcCheck = pc;
            if (m.GameLoaders.Count == 0 && BuiltInLoaders.TryGetValue(m.Id, out var gl)) { m.GameLoaders = gl.Loaders; m.GameCleanup = gl.Cleanup; }
        }
        Items = c.Modlists;
        Software = c.Software.Where(x => x.Name.Length > 0 && x.Url.StartsWith("https://")).ToList();
        foreach (var s in Software)
        {
            if (!BuiltInSoftware.TryGetValue(SoftKey(s), out var b)) continue;
            if (s.Langs.Count == 0) s.Langs = b.Langs;
            if (s.Winget.Length > 0) continue;
            s.Winget = b.Winget;
            s.WingetSource = b.WingetSource;
            if (s.Warning.ToString().Length == 0) s.Warning = b.Warning;
        }
        PlayerEngine = c.PlayerEngine is { Mpv.Url.Length: > 0 } ? c.PlayerEngine : BuiltInPlayerEngine;
        Social = new SocialLinks
        {
            Instagram = c.Social?.Instagram is { } ig && ig.StartsWith("https://") ? ig : BuiltInSocial.Instagram,
            TikTok = c.Social?.TikTok is { } tt && tt.StartsWith("https://") ? tt : BuiltInSocial.TikTok,
        };
        RemoteHelp = c.RemoteHelp is { Url.Length: > 0, Host.Length: > 0 } ? c.RemoteHelp : BuiltInRemoteHelp;
        Mo2 = c.Mo2 is { Url.Length: > 0, Sha256.Length: > 0 } ? c.Mo2 : BuiltInMo2;
    }

    public static Mo2Source? Mo2 { get; private set; }
    private static Mo2Source? BuiltInMo2;

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
