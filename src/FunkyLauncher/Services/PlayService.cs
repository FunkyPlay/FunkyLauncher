// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Diagnostics;
using FunkyLauncher.Core;

namespace FunkyLauncher.Services;

/// <summary>Starts the game through Mod Organizer 2 with the modlist profile.</summary>
public static class PlayService
{
    public static void Play(Settings settings, Modlist ml, string? mo2Profile = null)
    {
        var dir = settings.InstallDir(ml);
        var profile = string.IsNullOrWhiteSpace(mo2Profile) ? ml.Mo2Profile : mo2Profile;
        // MO2: -p <profile> picks the profile, moshortcut://:<title> runs that executable
        var psi = new ProcessStartInfo(Path.Combine(dir, "ModOrganizer.exe"), $"-p \"{profile}\" \"moshortcut://:{ml.Mo2Executable}\"")
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
    public static void OpenSupportChat(string text, Settings s, Modlist m, string? attach = null)
    {
        // 0.1.9: support chat inside the launcher (same queue as the bot; the game / modlist / version label goes
        // with every message). Telegram stays one click away inside the chat window.
        Views.SupportChatWindow.Open(System.Windows.Application.Current?.MainWindow, s, m, text, attach);
    }

    public static void OpenSupportChat(string text)
    {
        try { System.Windows.Clipboard.SetText(text); } catch { }
        var enc = Uri.EscapeDataString(text);
        try { OpenUrl($"tg://resolve?domain={AppConfig.SupportBotName}&text={enc}"); }
        catch { OpenUrl($"{AppConfig.SupportBotUrl}?text={enc}"); }
    }

    /// <summary>
    /// Opens Wabbajack's window (the Nexus login). Wabbajack refuses to run as administrator ("Don't run Wabbajack as Admin!"),
    /// so with administrator rights (also: UAC off, or the built-in "Administrator" account) it is started as a normal user
    /// (0.1.17, support #55/#57). False = it could not be done: the player got a clear message instead of Wabbajack's error.
    /// </summary>
    public static bool OpenWabbajackGui()
    {
        var gui = WabbajackService.FindGui();
        if (gui == null) return false;
        var dir = Path.GetDirectoryName(gui)!;
        if (!LowRights.IsAdminToken())
        {
            Process.Start(new ProcessStartInfo(gui) { WorkingDirectory = dir, UseShellExecute = true });
            return true;
        }
        if (LowRights.TryStart(gui, dir, out var error)) { Log("wabbajack window as a normal user"); return true; }
        Log("wabbajack window as a normal user failed: " + error);
        // the old way still helps when UAC is on: Explorer then runs with the player's normal rights
        if (UacOn())
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{gui}\"") { UseShellExecute = true });
            return true;
        }
        Views.Dialog.Show(System.Windows.Application.Current?.MainWindow, Loc.T("wj.admin.text"), Loc.T("wj.admin.title"),
            System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        return false;
    }

    /// <summary>User Account Control is on (EnableLUA = 1, the Windows default).</summary>
    private static bool UacOn()
    {
        try
        {
            using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System");
            return k?.GetValue("EnableLUA") is not int v || v != 0;
        }
        catch { return true; }
    }

    private static void Log(string msg)
    {
        try { File.AppendAllText(Path.Combine(AppConfig.DataDir, "launcher.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {msg}\n"); } catch { }
    }

    /// <summary>The launcher runs with administrator rights ("Run as administrator").</summary>
    public static bool IsElevated()
    {
        try
        {
            using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(id).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }
}
