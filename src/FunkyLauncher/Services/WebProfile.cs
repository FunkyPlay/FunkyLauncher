// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using FunkyLauncher.Core;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace FunkyLauncher.Services;

/// <summary>
/// The launcher's own browser profile (WebView2), shared by the manual-download window and the
/// built-in browser, so the Nexus login is remembered. Also handles "log out of Nexus".
/// </summary>
public static class WebProfile
{
    private static string Folder => Path.Combine(AppConfig.DataDir, "WebView2");
    private static string LogoutFlag => Path.Combine(AppConfig.DataDir, "nexus-logout.flag");
    private static Task<CoreWebView2Environment>? _env;

    /// <summary>Microsoft's installer of the WebView2 runtime (the "Evergreen" bootstrapper).</summary>
    public const string RuntimeUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

    public static async Task InitAsync(WebView2 web)
    {
        try
        {
            _env ??= CoreWebView2Environment.CreateAsync(null, Folder);
            await web.EnsureCoreWebView2Async(await _env);
        }
        catch (Exception ex)
        {
            // a failed start must not stick: the next window (or "Try again") creates the browser anew
            _env = null;
            AppLog.Write($"webview2 start failed (admin {LowRights.IsAdminToken()}, runtime {RuntimeVersion() ?? "none"}): {ex.GetType().Name}: {ex.Message}");
            throw;
        }
        if (File.Exists(LogoutFlag))
        {
            // the player logged out of Nexus: forget the browser login too
            await web.CoreWebView2.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.Cookies);
            try { File.Delete(LogoutFlag); } catch { }
        }
    }

    /// <summary>The browser process died: the next start makes a new environment.</summary>
    public static void Reset() => _env = null;

    /// <summary>The installed WebView2 runtime version, or null when it is not installed.</summary>
    public static string? RuntimeVersion()
    {
        try { return CoreWebView2Environment.GetAvailableBrowserVersionString(); }
        catch { return null; }
    }

    /// <summary>The exception means "the WebView2 runtime is not installed".</summary>
    public static bool IsNoRuntime(Exception ex) => ex is WebView2RuntimeNotFoundException || RuntimeVersion() == null;

    /// <summary>Logs out of Nexus: removes Wabbajack's saved login and clears the browser login next time it opens.</summary>
    public static void LogOutNexus()
    {
        if (File.Exists(WabbajackService.NexusLoginFile)) File.Delete(WabbajackService.NexusLoginFile);
        Directory.CreateDirectory(AppConfig.DataDir);
        File.WriteAllText(LogoutFlag, DateTime.Now.ToString("O"));
    }
}
