// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FunkyLauncher.Core;

/// <summary>
/// An optional add-on for a modlist (today: the 18+ add-on for Neon Nights). It is a plain list of Nexus files:
/// the launcher downloads them under the player's own Nexus login, unpacks them into the modlist's MO2 "mods"
/// folder and switches them on in modlist.txt. Built into the launcher as addons/&lt;modlistId&gt;-adult.json.
/// </summary>
public sealed class AddonFile
{
    [JsonPropertyName("schema")] public int Schema { get; set; }
    [JsonPropertyName("modlistId")] public string ModlistId { get; set; } = "";
    [JsonPropertyName("addonId")] public string AddonId { get; set; } = "";
    /// <summary>The Russian pack names its id "packId"; it is used as the add-on id.</summary>
    [JsonPropertyName("packId")] public string PackId { set { if (AddonId.Length == 0) AddonId = value; } }
    [JsonPropertyName("listVersion")] public string ListVersion { get; set; } = "";
    [JsonPropertyName("forMainVersion")] public string ForMainVersion { get; set; } = "";
    [JsonPropertyName("mods")] public List<AddonMod> Mods { get; set; } = new();
}

public sealed class AddonMod
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("level")] public int Level { get; set; } = 1;
    /// <summary>A mod we decided not to ship (kept in the file for the record).</summary>
    [JsonPropertyName("blocked")] public bool Blocked { get; set; }
    /// <summary>Neutral name for the screen — shown only after the age confirmation.</summary>
    [JsonPropertyName("displayName")] public LocText DisplayName { get; set; } = new();
    [JsonPropertyName("nexus")] public AddonNexus Nexus { get; set; } = new();
    [JsonPropertyName("archive")] public AddonArchive Archive { get; set; } = new();
    [JsonPropertyName("modFolder")] public string ModFolder { get; set; } = "";
    [JsonPropertyName("install")] public AddonInstall Install { get; set; } = new();
    [JsonPropertyName("modlist")] public AddonAnchor Modlist { get; set; } = new();
    [JsonPropertyName("dependsOn")] public List<string> DependsOn { get; set; } = new();
}

public sealed class AddonNexus
{
    [JsonPropertyName("game")] public string Game { get; set; } = "";
    [JsonPropertyName("modId")] public long ModId { get; set; }
    [JsonPropertyName("fileId")] public long FileId { get; set; }
    [JsonPropertyName("filePage")] public string FilePage { get; set; } = "";
}

public sealed class AddonArchive
{
    [JsonPropertyName("localFileName")] public string LocalFileName { get; set; } = "";
    [JsonPropertyName("size")] public long Size { get; set; }
    [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
}

public sealed class AddonInstall
{
    /// <summary>"copyAll": the whole archive goes into mods/&lt;modFolder&gt;.</summary>
    [JsonPropertyName("type")] public string Type { get; set; } = "copyAll";
    [JsonPropertyName("expectedTopFolders")] public List<string> ExpectedTopFolders { get; set; } = new();
}

/// <summary>Where the mod goes in modlist.txt: right above <see cref="InsertAbove"/>, else right below the fallback.</summary>
public sealed class AddonAnchor
{
    [JsonPropertyName("insertAbove")] public string? InsertAbove { get; set; }
    [JsonPropertyName("fallbackInsertBelow")] public string? FallbackInsertBelow { get; set; }
}

/// <summary>What the player chose for an add-on (kept only in settings.json on this PC).</summary>
public sealed class AddonPrefs
{
    public bool On { get; set; }
    public bool Level2 { get; set; }
    /// <summary>When the player last confirmed "I am 18 or older" (local time, nothing is sent anywhere).</summary>
    public DateTime? ConfirmedAt { get; set; }
}

public static class Addons
{
    /// <summary>The 18+ add-on built into this launcher for a modlist, or null.</summary>
    public static AddonFile? Adult(string modlistId) => Load(modlistId, "adult");

    /// <summary>The Russian pack (mod translations on top of the English base), or null if the modlist has none.</summary>
    public static AddonFile? Russian(string modlistId) => Load(modlistId, "ru");

    private static AddonFile? Load(string modlistId, string kind)
    {
        try
        {
            var uri = new Uri($"pack://application:,,,/addons/{modlistId}-{kind}.json");
            var res = System.Windows.Application.GetResourceStream(uri);
            if (res == null) return null;
            using var s = res.Stream;
            var a = JsonSerializer.Deserialize<AddonFile>(s);
            if (a == null || a.ModlistId != modlistId) return null;
            a.Mods = a.Mods.Where(m => !m.Blocked && m.ModFolder.Length > 0 && m.Archive.LocalFileName.Length > 0).ToList();
            foreach (var m in a.Mods)
                if (m.DisplayName.Ru.Length == 0) { m.DisplayName.Ru = m.Id; m.DisplayName.En = m.Id; }
            return a.Mods.Count > 0 ? a : null;
        }
        catch { return null; }
    }
}
