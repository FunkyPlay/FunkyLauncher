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

    private static HttpClient Http(int seconds = 30)
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(seconds) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("FunkyLauncher/" + AppConfig.Version);
        return h;
    }

    /// <summary>Sends one message (optionally with a file: a screenshot or an error report). Throws with a readable text on failure.</summary>
    public static async Task SendAsync(string text, string label, string? attachPath, CancellationToken token, IProgress<string>? waiting = null)
    {
        var me = Me();
        object? file = null;
        if (attachPath != null && File.Exists(attachPath))
        {
            byte[] bytes;
            var name = Path.GetFileName(attachPath);
            try
            {
                bytes = await File.ReadAllBytesAsync(attachPath, token);
                // a big screenshot (a 4K PNG is 10-25 MB) becomes a JPEG of the same size in pixels: readable, a few MB
                if (bytes.Length > 3 * 1024 * 1024 && IsPicture(name))
                    (name, bytes) = await Task.Run(() => SmallerPicture(name, bytes), token);
                // a big error report: the server stops reading a body after 60 s, on a slow upload (~1 Mbit/s) that is
                // ~7 MB of JSON. Each log is cut to its last 256 KB (the end shows the failure), usually under 1 MB.
                else if (bytes.Length > SmallReport && Path.GetExtension(name).Equals(".zip", StringComparison.OrdinalIgnoreCase))
                    bytes = await Task.Run(() => SmallerReport(bytes), token);
            }
            catch (IOException ex) { throw new InvalidOperationException(Loc.F("chat.err.file", ex.Message)); }
            catch (UnauthorizedAccessException ex) { throw new InvalidOperationException(Loc.F("chat.err.file", ex.Message)); }
            if (bytes.Length > 10 * 1024 * 1024) throw new InvalidOperationException(Loc.T("chat.err.big"));
            file = new { name, data = Convert.ToBase64String(bytes) };
        }
        // 0.1.18: the body is built in memory and sent with its length. PostAsJsonAsync streamed it without a length
        // (chunked). Player 21.11: six report uploads, each cut by the server after 60 s of reading the body (Caddy
        // "i/o timeout", 504) and seen here as "Error while copying content to a stream". Now: a big report is made
        // smaller (above), the body goes with its length, and a broken upload is retried twice before giving up.
        var body = JsonSerializer.SerializeToUtf8Bytes(new { install = me.Install, key = me.Key, text, label, lang = Loc.Lang, file });
        // a file on a slow connection needs more than 30 seconds (0.1.17: 8 MB at 2 Mbit/s is ~45 s)
        using var http = Http(file != null ? 300 : 30);
        HttpResponseMessage? resp = null;
        var busy = 0;
        for (var attempt = 1; resp == null; attempt++)
        {
            try
            {
                using var content = new ByteArrayContent(body);
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
                resp = await http.PostAsync(Api + "/send", content, token);
                // 0.1.18: 429 = the server's limit of messages per minute (support #142: "send answered 429").
                // Wait as long as the server says (Retry-After, else 30 s), show the countdown, send again by itself — twice at most.
                // Only the per-IP limit ("too many requests") can free up in a minute; the hourly message limit and the
                // daily file limit don't — those are answered at once, without waiting (Bots, 21.11).
                if ((int)resp.StatusCode == 429 && busy < 2 && Limit(await ErrorOf(resp, token)) == null)
                {
                    busy++;
                    var wait = WaitOf(resp);
                    AppLog.Write($"support chat: send answered 429 ({await ErrorOf(resp, token)}), waiting {wait} s (try {busy})");
                    resp.Dispose();
                    resp = null;
                    for (var left = wait; left > 0; left--)
                    {
                        waiting?.Report(Loc.F("chat.wait", left));
                        await Task.Delay(1000, token);
                    }
                    waiting?.Report(Loc.T("chat.sending"));
                    attempt--; // a wait is not a broken upload
                }
            }
            catch (TaskCanceledException) when (!token.IsCancellationRequested)
            {
                AppLog.Write($"support chat: sending timed out ({body.Length / 1024} KB" + (file != null ? ", with a file)" : ")"));
                throw new InvalidOperationException(Loc.T("chat.err.slow"));
            }
            catch (HttpRequestException ex)
            {
                AppLog.Write($"support chat: send attempt {attempt} broke ({body.Length / 1024} KB): {Reason(ex)}");
                if (attempt >= 3) throw new InvalidOperationException(Loc.F("chat.err.net", Reason(ex)));
                await Task.Delay(attempt * 3000, token);
            }
        }
        if (!resp.IsSuccessStatusCode)
        {
            var code = (int)resp.StatusCode;
            var error = await ErrorOf(resp, token);
            AppLog.Write($"support chat: send answered {code} ({body.Length / 1024} KB){(error != null ? ": " + error : "")}");
            if (code == 429 && Limit(error) is { } limit) throw new InvalidOperationException(limit);
            // 504/408: the server gave up waiting for a slow upload (Caddy reads a body for 60 s at most)
            throw new InvalidOperationException(code == 413 ? Loc.T("chat.err.big") : code is 504 or 408 ? Loc.T("chat.err.slow")
                : code == 429 ? Loc.F("chat.err.busy", WaitOf(resp)) : Loc.F("chat.err.send", code));
        }
    }

    /// <summary>The server's "error" text ({"ok":false,"error":"..."}); null if there is none. Read once, kept.</summary>
    private static async Task<string?> ErrorOf(HttpResponseMessage resp, CancellationToken token)
    {
        try
        {
            await resp.Content.LoadIntoBufferAsync();
            var s = await resp.Content.ReadAsStringAsync(token);
            return JsonDocument.Parse(s).RootElement.TryGetProperty("error", out var e) ? e.GetString() : null;
        }
        catch { return null; }
    }

    /// <summary>A limit that waiting a minute won't lift → the player's text; null for the per-IP limit or anything else.</summary>
    internal static string? Limit(string? error) =>
        error == null ? null
        : error.Contains("daily file limit", StringComparison.OrdinalIgnoreCase) ? Loc.T("chat.err.files")
        : error.Contains("message limit", StringComparison.OrdinalIgnoreCase) ? Loc.T("chat.err.hour")
        : null;

    /// <summary>Seconds the server asks to wait after 429: Retry-After (seconds or a date), else 30; 5..120.</summary>
    internal static int WaitOf(HttpResponseMessage resp)
    {
        var ra = resp.Headers.RetryAfter;
        double s = ra?.Delta?.TotalSeconds ?? (ra?.Date is { } d ? (d - DateTimeOffset.UtcNow).TotalSeconds : 30);
        return (int)Math.Clamp(Math.Ceiling(s), 5, 120);
    }

    private const int SmallReport = 2 * 1024 * 1024;
    private const int ReportTail = 256 * 1024;

    /// <summary>
    /// A copy of a report zip with every file over 256 KB cut to its last 256 KB (marked in the file).
    /// Not a zip, or nothing to cut = the original bytes.
    /// </summary>
    internal static byte[] SmallerReport(byte[] bytes)
    {
        try
        {
            using var src = new System.IO.Compression.ZipArchive(new MemoryStream(bytes), System.IO.Compression.ZipArchiveMode.Read);
            if (src.Entries.All(e => e.Length <= ReportTail)) return bytes;
            var ms = new MemoryStream();
            using (var dst = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var e in src.Entries)
                {
                    if (e.FullName.EndsWith('/')) continue;
                    using var input = e.Open();
                    using var output = dst.CreateEntry(e.FullName, System.IO.Compression.CompressionLevel.SmallestSize).Open();
                    if (e.Length <= ReportTail) { input.CopyTo(output); continue; }
                    var buf = new MemoryStream();
                    input.CopyTo(buf);
                    var mark = System.Text.Encoding.UTF8.GetBytes($"[Funky Launcher: only the last {ReportTail / 1024} KB of {e.Length / 1024} KB]\n");
                    output.Write(mark);
                    output.Write(buf.GetBuffer(), (int)(buf.Length - ReportTail), ReportTail);
                }
            }
            AppLog.Write($"support chat: report {bytes.Length / 1024} KB cut to {ms.Length / 1024} KB");
            return ms.Length < bytes.Length ? ms.ToArray() : bytes;
        }
        catch (Exception ex) { AppLog.Write("support chat: report not cut: " + ex.Message); return bytes; }
    }

    /// <summary>The innermost reason of a network failure ("connection was reset…"), not the generic outer text.</summary>
    private static string Reason(Exception ex)
    {
        while (ex.InnerException != null) ex = ex.InnerException;
        return ex.Message.TrimEnd('.');
    }

    private static bool IsPicture(string name) =>
        Path.GetExtension(name).ToLowerInvariant() is ".png" or ".bmp" or ".jpg" or ".jpeg" or ".tif" or ".tiff";

    /// <summary>The picture as a JPEG (quality 90, long side at most 3840 px). Unreadable as a picture = sent as it is.</summary>
    private static (string, byte[]) SmallerPicture(string name, byte[] bytes)
    {
        try
        {
            var bi = new System.Windows.Media.Imaging.BitmapImage();
            bi.BeginInit(); bi.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; bi.StreamSource = new MemoryStream(bytes); bi.EndInit();
            System.Windows.Media.Imaging.BitmapSource src = bi;
            var side = Math.Max(bi.PixelWidth, bi.PixelHeight);
            if (side > 3840) src = new System.Windows.Media.Imaging.TransformedBitmap(bi, new System.Windows.Media.ScaleTransform(3840.0 / side, 3840.0 / side));
            var enc = new System.Windows.Media.Imaging.JpegBitmapEncoder { QualityLevel = 90 };
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(src));
            using var ms = new MemoryStream();
            enc.Save(ms);
            return ms.Length < bytes.Length ? (Path.GetFileNameWithoutExtension(name) + ".jpg", ms.ToArray()) : (name, bytes);
        }
        catch { return (name, bytes); }
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

    /// <summary>The same anonymous id and secret for the other queues of our server (flaw reports).</summary>
    public static (string Install, string Key) Ids() { var me = Me(); return (me.Install, me.Key); }

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
