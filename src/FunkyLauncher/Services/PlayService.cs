// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Diagnostics;
using FunkyLauncher.Core;

namespace FunkyLauncher.Services;

/// <summary>Starts the game through Mod Organizer 2 with the modlist profile.</summary>
public static class PlayService
{
    public static void Play(Settings settings, Modlist ml)
    {
        var dir = settings.InstallDir(ml);
        // MO2: -p <profile> picks the profile, moshortcut://:<title> runs that executable
        var psi = new ProcessStartInfo(Path.Combine(dir, "ModOrganizer.exe"), $"-p \"{ml.Mo2Profile}\" \"moshortcut://:{ml.Mo2Executable}\"")
        {
            WorkingDirectory = dir,
            UseShellExecute = false,
        };
        Process.Start(psi);
    }

    public static void OpenMo2(Settings settings, Modlist ml)
    {
        var dir = settings.InstallDir(ml);
        Process.Start(new ProcessStartInfo(Path.Combine(dir, "ModOrganizer.exe"), $"-p \"{ml.Mo2Profile}\"") { WorkingDirectory = dir, UseShellExecute = false });
    }

    public static void OpenFolder(string dir)
    {
        if (Directory.Exists(dir)) Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
    }

    public static void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    /// <summary>
    /// Opens the support bot with <paramref name="text"/> already typed in the message box (Telegram app first,
    /// the t.me page if the app is not installed). The text is also put on the clipboard as a fallback.
    /// </summary>
    public static void OpenSupportChat(string text, Settings s, Modlist m)
    {
        // a fixed last line so support (and the bot) sees the game, modlist, its version and language at once
        var game = m.Game.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToLowerInvariant() ?? "game";
        var installed = s.IsInstalled(m) ? s.InstalledVersion(m) ?? m.Version : "—";
        var lang = Addons.Russian(m.Id) != null ? (AddonService.Prefs(s, Addons.Russian(m.Id)!).On ? " RU" : " EN") : "";
        text += $"\n\n#{game} · {m.Title} {installed}{lang} · Funky Launcher {AppConfig.Version}";
        OpenSupportChat(text);
    }

    public static void OpenSupportChat(string text)
    {
        try { System.Windows.Clipboard.SetText(text); } catch { }
        var enc = Uri.EscapeDataString(text);
        try { OpenUrl($"tg://resolve?domain={AppConfig.SupportBotName}&text={enc}"); }
        catch { OpenUrl($"{AppConfig.SupportBotUrl}?text={enc}"); }
    }

    public static void OpenWabbajackGui()
    {
        var gui = WabbajackService.FindGui();
        if (gui != null) Process.Start(new ProcessStartInfo(gui) { WorkingDirectory = Path.GetDirectoryName(gui)!, UseShellExecute = true });
    }
}
