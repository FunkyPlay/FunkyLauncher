// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows.Media.Imaging;
using FunkyLauncher.Core;

namespace FunkyLauncher.Services;

/// <summary>
/// "Report a flaw" (free, 0.1.15): a small thing in a modlist (a broken texture, an English name, an odd item) with a
/// few words and a screenshot goes to its own queue on our server — not to support, nobody answers it, the game's team
/// sorts it out for the next modlist update. The launcher adds only: game, modlist, its version, variant (RU/EN, 18+),
/// launcher version. Same anonymous install id as the support chat; nothing else about the PC or the player.
/// Server side: "Боты" chat, POST {supportApi}/flaw (200 {ok,id}; 400 empty/bad fields; 403 another key; 413 big file;
/// 429 the daily limit, 10 per install — "error" is a ready text in the player's language). "Delete my data" in the
/// support chat erases these reports too.
/// </summary>
public static class FlawReport
{
    public const int MaxText = 1000;
    private const long MaxBytes = 5L * 1024 * 1024;

    public static string Game(Modlist m) =>
        m.Game.Contains("Cyberpunk", StringComparison.OrdinalIgnoreCase) ? "cyberpunk"
        : m.Game.Contains("Starfield", StringComparison.OrdinalIgnoreCase) ? "starfield"
        : m.Game.Contains("Fallout 4", StringComparison.OrdinalIgnoreCase) ? "fallout4"
        : m.Game.Contains("GTA", StringComparison.OrdinalIgnoreCase) || m.Game.Contains("Grand Theft", StringComparison.OrdinalIgnoreCase) ? "gta5"
        : m.Game.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToLowerInvariant() ?? "game";

    /// <summary>"RU", "EN", "RU 18+"… ("" when the modlist has no language choice).</summary>
    public static string Variant(Settings s, Modlist m)
    {
        var v = "";
        if (Addons.Russian(m.Id) is { } ru) v = AddonService.Prefs(s, ru).On ? "RU" : "EN";
        if (Addons.Adult(m.Id) is { } ad && AddonService.Prefs(s, ad).On) v = (v + " 18+").Trim();
        return v;
    }

    /// <summary>What the player sees under the text box: exactly what goes with the message.</summary>
    public static string Label(Settings s, Modlist m)
    {
        var version = s.InstalledVersion(m) ?? m.Version;
        var variant = Variant(s, m);
        return $"{m.Title} {version}{(variant.Length > 0 ? " · " + variant : "")} · Funky Launcher {AppConfig.Version}";
    }

    /// <summary>The picture as it is sent: JPEG, the long side up to 1920 px, quality 85 (less traffic, a quick send).</summary>
    public static (string Name, byte[] Data) Picture(string file)
    {
        var bi = new BitmapImage();
        bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.StreamSource = new MemoryStream(File.ReadAllBytes(file)); bi.EndInit();
        BitmapSource src = bi;
        var side = Math.Max(bi.PixelWidth, bi.PixelHeight);
        if (side > 1920) src = new TransformedBitmap(bi, new System.Windows.Media.ScaleTransform(1920.0 / side, 1920.0 / side));
        foreach (var q in new[] { 85, 70, 55 })
        {
            var enc = new JpegBitmapEncoder { QualityLevel = q };
            enc.Frames.Add(BitmapFrame.Create(src));
            using var ms = new MemoryStream();
            enc.Save(ms);
            if (ms.Length <= MaxBytes) return (Path.GetFileNameWithoutExtension(file) + ".jpg", ms.ToArray());
        }
        throw new InvalidOperationException(Loc.T("flaw.err.big"));
    }

    /// <summary>Sends one report. Throws with a text for the player.</summary>
    /// <summary>A mod page on Nexus Mods (the only link "suggest a mod" takes).</summary>
    public static bool IsNexusUrl(string url) =>
        Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps
        && (u.Host.Equals("nexusmods.com", StringComparison.OrdinalIgnoreCase) || u.Host.EndsWith(".nexusmods.com", StringComparison.OrdinalIgnoreCase));

    public static async Task SendAsync(Settings s, Modlist m, string text, string? picture, CancellationToken token,
        string kind = "flaw", string modName = "", string modUrl = "")
    {
        text = text.Trim();
        if (text.Length > MaxText) text = text[..MaxText];
        object? file = null;
        if (picture != null)
        {
            var (name, data) = await Task.Run(() => Picture(picture), token);
            file = new { name, data = Convert.ToBase64String(data) };
        }
        var (install, key) = SupportChat.Ids();
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("FunkyLauncher/" + AppConfig.Version);
        using var resp = await http.PostAsJsonAsync(SupportChat.Api + "/flaw", new
        {
            install, key, game = Game(m), modlist = m.Id, title = m.Title, version = s.InstalledVersion(m) ?? m.Version,
            variant = Variant(s, m), launcher = AppConfig.Version, lang = Loc.Lang, text, file,
            kind, mod_name = kind == "mod" ? modName.Trim() : null, mod_url = kind == "mod" && modUrl.Trim().Length > 0 ? modUrl.Trim() : null,
        }, token);
        if (resp.IsSuccessStatusCode) return;
        string? error = null;
        try { error = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(token)).RootElement.GetProperty("error").GetString(); } catch { }
        throw new InvalidOperationException((int)resp.StatusCode switch
        {
            429 => error ?? Loc.T("flaw.err.429"),
            413 => Loc.T("flaw.err.413"),
            _ => error ?? Loc.F("flaw.err.code", (int)resp.StatusCode),
        });
    }

    /// <summary>The newest Funky Screen screenshot (null = none).</summary>
    public static string? LastShot()
    {
        try
        {
            var dir = FunkyScreen.ShotsDir;
            return Directory.Exists(dir) ? new DirectoryInfo(dir).EnumerateFiles("*.png").OrderByDescending(f => f.LastWriteTime).FirstOrDefault()?.FullName : null;
        }
        catch { return null; }
    }
}
