// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FunkyLauncher.Core;

namespace FunkyLauncher.Services;

/// <summary>
/// Nexus ticket 279278 (26.11): the launcher's own use of the Nexus Mods API, for the registration test build.
/// Login: OAuth 2.0 with PKCE (public app, loopback 127.0.0.1 callback) once Nexus gives us a client id, or the player's
/// personal API key for testing. The token stays on the player's PC, encrypted for this Windows user (DPAPI), never in
/// logs or error reports. Every request carries Application-Name / Application-Version; calls happen only on the player's action.
/// </summary>
public static class NexusApi
{
    public const string AppName = "Funky Launcher";
    private const string Authorize = "https://users.nexusmods.com/oauth/authorize";
    private const string TokenUrl = "https://users.nexusmods.com/oauth/token";
    private const string V1 = "https://api.nexusmods.com/v1/";

    /// <summary>The client id Nexus registers for us; until then FUNKYLAUNCHER_NEXUS_CLIENT_ID (developer tests only).</summary>
    public static string ClientId => Environment.GetEnvironmentVariable("FUNKYLAUNCHER_NEXUS_CLIENT_ID") is { Length: > 0 } c ? c : "";

    private static string StoreFile => Path.Combine(AppConfig.DataDir, "nexus-login.bin");

    public sealed class Login
    {
        public string Kind { get; set; } = "";          // "oauth" or "apikey"
        public string AccessToken { get; set; } = "";
        public string RefreshToken { get; set; } = "";
        public DateTime ExpiresUtc { get; set; }
        public string ApiKey { get; set; } = "";
        public string UserName { get; set; } = "";
        public bool Premium { get; set; }
    }

    public static HttpClient Http()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        h.DefaultRequestHeaders.Add("Application-Name", AppName);
        h.DefaultRequestHeaders.Add("Application-Version", AppConfig.Version.Split('+')[0]);
        h.DefaultRequestHeaders.UserAgent.ParseAdd($"FunkyLauncher/{AppConfig.Version.Split('+')[0]}");
        return h;
    }

    // ------------------------------------------------------------------ storage (this PC, this Windows user)

    public static Login? Load()
    {
        try
        {
            if (!File.Exists(StoreFile)) return null;
            var plain = ProtectedData.Unprotect(File.ReadAllBytes(StoreFile), Entropy, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<Login>(plain);
        }
        catch { return null; }
    }

    private static void Save(Login l)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StoreFile)!);
        File.WriteAllBytes(StoreFile, ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(l), Entropy, DataProtectionScope.CurrentUser));
    }

    /// <summary>"Log out": forgets the token on this PC (the access itself is revoked on the Nexus site).</summary>
    public static void LogOut() { try { File.Delete(StoreFile); } catch { } }

    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("FunkyLauncher.NexusLogin.v1");

    // ------------------------------------------------------------------ login

    /// <summary>Testing only (Nexus API policy: personal keys for testing): checks a personal API key and keeps it.</summary>
    public static async Task<Login> LoginWithKeyAsync(string key, CancellationToken token)
    {
        var l = new Login { Kind = "apikey", ApiKey = key.Trim() };
        await ValidateAsync(l, token);
        Save(l);
        return l;
    }

    /// <summary>OAuth 2.0 + PKCE: opens the Nexus page in the browser, waits for the code on 127.0.0.1, exchanges it.</summary>
    public static async Task<Login> LoginWithBrowserAsync(Action<string> openUrl, CancellationToken token)
    {
        if (ClientId.Length == 0) throw new InvalidOperationException("Nexus client id is not set yet (registration pending)");
        var verifier = B64Url(RandomNumberGenerator.GetBytes(48));
        var challenge = B64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = B64Url(RandomNumberGenerator.GetBytes(16));
        var port = CallbackPort; // registered with Nexus as the callback URI, so it is fixed
        var redirect = $"http://127.0.0.1:{port}/callback";
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        openUrl($"{Authorize}?client_id={Uri.EscapeDataString(ClientId)}&response_type=code&scope=&redirect_uri={Uri.EscapeDataString(redirect)}"
            + $"&state={state}&code_challenge_method=S256&code_challenge={challenge}");

        string? code = null;
        using (token.Register(() => { try { listener.Stop(); } catch { } }))
        {
            while (code == null)
            {
                var ctx = await listener.GetContextAsync();
                if (ctx.Request.Url?.AbsolutePath != "/callback") { ctx.Response.StatusCode = 404; ctx.Response.Close(); continue; }
                var q = System.Web.HttpUtility.ParseQueryString(ctx.Request.Url.Query);
                var ok = q["state"] == state && q["code"] is { Length: > 0 };
                var page = Encoding.UTF8.GetBytes(ok
                    ? "<html><body style='font-family:sans-serif;background:#111;color:#eee'><h2>Funky Launcher</h2><p>Вход в Nexus выполнен — вернитесь в лаунчер. / Logged in, go back to the launcher.</p></body></html>"
                    : "<html><body style='font-family:sans-serif;background:#111;color:#eee'><h2>Funky Launcher</h2><p>Вход не выполнен. / Login failed.</p></body></html>");
                ctx.Response.ContentType = "text/html; charset=utf-8";
                await ctx.Response.OutputStream.WriteAsync(page, token);
                ctx.Response.Close();
                if (!ok) throw new InvalidOperationException("Nexus login was not completed" + (q["error"] is { } e ? $" ({e})" : ""));
                code = q["code"];
            }
        }

        using var http = Http();
        var res = await http.PostAsync(TokenUrl, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code", ["redirect_uri"] = redirect, ["scope"] = "",
            ["client_id"] = ClientId, ["code"] = code!, ["code_verifier"] = verifier,
        }), token);
        var l = TokensFrom(await res.EnsureSuccessStatusCode().Content.ReadAsStringAsync(token));
        await ValidateAsync(l, token);
        Save(l);
        return l;
    }

    private static Login TokensFrom(string json, Login? into = null)
    {
        using var d = JsonDocument.Parse(json);
        var r = d.RootElement;
        var l = into ?? new Login { Kind = "oauth" };
        l.AccessToken = r.GetProperty("access_token").GetString() ?? "";
        if (r.TryGetProperty("refresh_token", out var rt) && rt.GetString() is { Length: > 0 } rts) l.RefreshToken = rts;
        l.ExpiresUtc = DateTime.UtcNow.AddSeconds(r.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 3600) - TimeSpan.FromMinutes(1);
        return l;
    }

    /// <summary>A fresh access token (refreshes it when it expired). 4xx on refresh = the player revoked the access → logged out.</summary>
    private static async Task<Login> FreshAsync(Login l, CancellationToken token)
    {
        if (l.Kind != "oauth" || DateTime.UtcNow < l.ExpiresUtc) return l;
        using var http = Http();
        var res = await http.PostAsync(TokenUrl, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token", ["refresh_token"] = l.RefreshToken, ["client_id"] = ClientId,
        }), token);
        if ((int)res.StatusCode is >= 400 and < 500) { LogOut(); throw new InvalidOperationException("Nexus access was revoked — log in again"); }
        TokensFrom(await res.EnsureSuccessStatusCode().Content.ReadAsStringAsync(token), l);
        Save(l);
        return l;
    }

    private static void Auth(HttpRequestMessage m, Login l)
    {
        if (l.Kind == "apikey") m.Headers.Add("apikey", l.ApiKey);
        else m.Headers.Authorization = new AuthenticationHeaderValue("Bearer", l.AccessToken);
    }

    // ------------------------------------------------------------------ API calls (only on the player's action)

    /// <summary>v1 users/validate: the account name and whether it has Premium (Premium = direct download links).</summary>
    public static async Task ValidateAsync(Login l, CancellationToken token)
    {
        l = await FreshAsync(l, token);
        using var http = Http();
        using var m = new HttpRequestMessage(HttpMethod.Get, V1 + "users/validate.json");
        Auth(m, l);
        using var res = await http.SendAsync(m, token);
        if (res.StatusCode == HttpStatusCode.Unauthorized) throw new InvalidOperationException("Nexus did not accept the login");
        using var d = JsonDocument.Parse(await res.EnsureSuccessStatusCode().Content.ReadAsStringAsync(token));
        l.UserName = d.RootElement.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
        l.Premium = d.RootElement.TryGetProperty("is_premium", out var p) && p.ValueKind == JsonValueKind.True;
    }

    /// <summary>
    /// v1 download_link for one file. Premium: works directly. Without Premium Nexus wants the key/expires of an nxm link
    /// the player got by pressing "Slow download" on the file page — the launcher then keeps the manual path (the page).
    /// </summary>
    public static async Task<string?> DownloadLinkAsync(string game, long modId, long fileId, string? nxmKey, long? nxmExpires, CancellationToken token)
    {
        if (Load() is not { } l) throw new InvalidOperationException("not logged in to Nexus");
        l = await FreshAsync(l, token);
        var url = $"{V1}games/{Uri.EscapeDataString(game)}/mods/{modId}/files/{fileId}/download_link.json"
            + (nxmKey is { Length: > 0 } && nxmExpires is { } ex ? $"?key={Uri.EscapeDataString(nxmKey)}&expires={ex}" : "");
        using var http = Http();
        using var m = new HttpRequestMessage(HttpMethod.Get, url);
        Auth(m, l);
        using var res = await http.SendAsync(m, token);
        if (res.StatusCode == HttpStatusCode.Forbidden) return null; // no Premium and no nxm key → the manual path
        using var d = JsonDocument.Parse(await res.EnsureSuccessStatusCode().Content.ReadAsStringAsync(token));
        return d.RootElement.ValueKind == JsonValueKind.Array && d.RootElement.GetArrayLength() > 0
            && d.RootElement[0].TryGetProperty("URI", out var u) ? u.GetString() : null;
    }

    // ------------------------------------------------------------------ helpers

    private static string B64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>The callback port we register with Nexus (http://127.0.0.1:52731/callback).</summary>
    public const int CallbackPort = 52731;

}
