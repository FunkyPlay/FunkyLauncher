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
/// Server side: "Боты" chat, POST {supportApi}/flaw.
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

    /// <summary>A picture made small enough to send: up to 2560 px wide, JPEG when the PNG is too big.</summary>
    public static (string Name, byte[] Data) Picture(string file)
    {
        var bytes = File.ReadAllBytes(file);
        var name = Path.GetFileName(file);
        if (bytes.Length <= MaxBytes && !TooWide(bytes)) return (name, bytes);
        var bi = new BitmapImage();
        bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.StreamSource = new MemoryStream(bytes); bi.EndInit();
        BitmapSource src = bi;
        if (bi.PixelWidth > 2560) src = new TransformedBitmap(bi, new System.Windows.Media.ScaleTransform(2560.0 / bi.PixelWidth, 2560.0 / bi.PixelWidth));
        foreach (var q in new[] { 88, 75, 60 })
        {
            var enc = new JpegBitmapEncoder { QualityLevel = q };
            enc.Frames.Add(BitmapFrame.Create(src));
            using var ms = new MemoryStream();
            enc.Save(ms);
            if (ms.Length <= MaxBytes) return (Path.GetFileNameWithoutExtension(file) + ".jpg", ms.ToArray());
        }
        throw new InvalidOperationException(Texts.T("Снимок слишком большой — выбери другой.", "The screenshot is too big — pick another one."));
    }

    private static bool TooWide(byte[] bytes)
    {
        try
        {
            var d = BitmapDecoder.Create(new MemoryStream(bytes), BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            return d.Frames[0].PixelWidth > 3840;
        }
        catch { return false; }
    }

    /// <summary>Sends one report. Throws with a text for the player.</summary>
    public static async Task SendAsync(Settings s, Modlist m, string text, string? picture, CancellationToken token)
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
        }, token);
        if (resp.IsSuccessStatusCode) return;
        string? error = null;
        try { error = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(token)).RootElement.GetProperty("error").GetString(); } catch { }
        throw new InvalidOperationException((int)resp.StatusCode switch
        {
            404 => Texts.T("Приём недочётов включится на сервере в ближайшем обновлении.", "Flaw reports are switched on at the server in an upcoming update."),
            429 => error ?? Texts.T("На сегодня хватит — спасибо! Пришли остальное завтра.", "That's enough for today — thank you! Send the rest tomorrow."),
            413 => Texts.T("Снимок слишком большой.", "The screenshot is too big."),
            _ => error ?? Texts.T($"Не отправилось (ошибка {(int)resp.StatusCode}). Попробуй позже.", $"Not sent (error {(int)resp.StatusCode}). Try again later."),
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

    /// <summary>RU/EN texts of this feature live here (not in Loc) until the design and copy team give the final ones.</summary>
    public static class Texts
    {
        public static string T(string ru, string en) => Loc.Lang == "en" ? en : ru;
    }
}
