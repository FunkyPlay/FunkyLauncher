// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using FunkyLauncher.Core;

namespace FunkyLauncher.Services;

public sealed class ChatMessage
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("from")] public string From { get; set; } = "";
    [JsonPropertyName("text")] public string Text { get; set; } = "";
    [JsonPropertyName("time")] public string Time { get; set; } = "";
    [JsonPropertyName("file")] public string? File { get; set; }
}

/// <summary>
/// Support chat inside the launcher: messages go to the same queue as @FunkyLauncherBot on our server and the support
/// team's answers come back here. No account: this PC has a random id and secret (support.json in the data folder);
/// the server ties the secret to the id on the first message. Nothing is sent until the player presses Send.
/// </summary>
public static class SupportChat
{
    private sealed class Identity
    {
        public string Install { get; set; } = "";
        public string Key { get; set; } = "";
        public long LastSeen { get; set; }
    }

    private static string FilePath => Path.Combine(AppConfig.DataDir, "support.json");
    private static string HistoryPath => Path.Combine(AppConfig.DataDir, "support-history.json");

    /// <summary>Server address: the catalog's "supportApi" (can move without a new launcher), else the built-in one.</summary>
    public static string Api => Catalog.SupportApi is { Length: > 0 } a ? a.TrimEnd('/') : AppConfig.SupportApi;

    private static Identity Me()
    {
        try
        {
            if (File.Exists(FilePath) && JsonSerializer.Deserialize<Identity>(File.ReadAllText(FilePath)) is { Install.Length: > 0 } id) return id;
        }
        catch { }
        var fresh = new Identity
        {
            Install = Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant(),
            Key = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant(),
        };
        Save(fresh);
        return fresh;
    }

    private static void Save(Identity id)
    {
        Directory.CreateDirectory(AppConfig.DataDir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(id));
    }

    private static HttpClient Http()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("FunkyLauncher/" + AppConfig.Version);
        return h;
    }

    /// <summary>Sends one message (optionally with a file: a screenshot or an error report). Throws with a readable text on failure.</summary>
    public static async Task SendAsync(string text, string label, string? attachPath, CancellationToken token)
    {
        var me = Me();
        object? file = null;
        if (attachPath != null && File.Exists(attachPath))
        {
            var bytes = await File.ReadAllBytesAsync(attachPath, token);
            if (bytes.Length > 10 * 1024 * 1024) throw new InvalidOperationException(Loc.T("chat.err.big"));
            file = new { name = Path.GetFileName(attachPath), data = Convert.ToBase64String(bytes) };
        }
        using var http = Http();
        var resp = await http.PostAsJsonAsync(Api + "/send", new
        {
            install = me.Install, key = me.Key, text, label, lang = Loc.Lang, file,
        }, token);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException(Loc.F("chat.err.send", (int)resp.StatusCode));
    }

    /// <summary>All messages of this PC's conversation (kept locally too, so the history shows offline).</summary>
    public static async Task<List<ChatMessage>> LoadAsync(CancellationToken token)
    {
        var me = Me();
        var history = LocalHistory();
        var after = history.Count > 0 ? history.Max(m => m.Id) : 0;
        try
        {
            using var http = Http();
            var url = $"{Api}/messages?install={me.Install}&key={me.Key}&after={after}";
            var fresh = await http.GetFromJsonAsync<List<ChatMessage>>(url, token) ?? new();
            if (fresh.Count > 0)
            {
                history.AddRange(fresh.Where(f => history.All(h => h.Id != f.Id)));
                history = history.OrderBy(m => m.Id).ToList();
                File.WriteAllText(HistoryPath, JsonSerializer.Serialize(history));
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { /* offline: show what we have */ }
        return history;
    }

    private static List<ChatMessage> LocalHistory()
    {
        try { if (File.Exists(HistoryPath)) return JsonSerializer.Deserialize<List<ChatMessage>>(File.ReadAllText(HistoryPath)) ?? new(); }
        catch { }
        return new();
    }

    /// <summary>This PC's anonymous id, shown in the chat window (random, not taken from hardware or the Windows name).</summary>
    public static string InstallId => Me().Install;

    /// <summary>"Delete my data": asks the server to erase this PC's conversation and files, then erases the local copy.</summary>
    public static async Task DeleteAsync(CancellationToken token)
    {
        var me = Me();
        using var http = Http();
        var resp = await http.PostAsJsonAsync(Api + "/delete", new { install = me.Install, key = me.Key }, token);
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException(Loc.F("chat.err.send", (int)resp.StatusCode));
        try { File.Delete(HistoryPath); } catch { }
        try { File.Delete(FilePath); } catch { } // a new random id next time
    }

    /// <summary>An answer from support the player has not seen yet (for the dot on the button).</summary>
    public static async Task<bool> HasUnreadAsync(CancellationToken token)
    {
        if (!File.Exists(FilePath)) return false; // never wrote: nothing to fetch
        var all = await LoadAsync(token);
        var me = Me();
        return all.Any(m => m.From == "support" && m.Id > me.LastSeen);
    }

    public static void MarkSeen(IEnumerable<ChatMessage> all)
    {
        var me = Me();
        var max = all.Where(m => m.From == "support").Select(m => m.Id).DefaultIfEmpty(0).Max();
        if (max > me.LastSeen) { me.LastSeen = max; Save(me); }
    }

    /// <summary>The fixed label support sees with every message: game, modlist, version, language, launcher.</summary>
    public static string Label(Settings s, Modlist m)
    {
        var game = m.Game.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToLowerInvariant() ?? "game";
        var installed = s.IsInstalled(m) ? s.InstalledVersion(m) ?? m.Version : "—";
        var lang = Addons.Russian(m.Id) != null ? (AddonService.Prefs(s, Addons.Russian(m.Id)!).On ? " RU" : " EN") : "";
        return $"#{game} · {m.Title} {installed}{lang} · Funky Launcher {AppConfig.Version}";
    }
}
