// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Diagnostics;
using FunkyLauncher.Core;

namespace FunkyLauncher.Services;

/// <summary>Starts the game through Mod Organizer 2 with the modlist profile.</summary>
public static class PlayService
{
    public static void Play(Settings settings)
    {
        var mo2 = Path.Combine(settings.InstallDir, "ModOrganizer.exe");
        // MO2: -p <profile> picks the profile, moshortcut://:<title> runs that executable
        var psi = new ProcessStartInfo(mo2, $"-p \"{AppConfig.Mo2Profile}\" \"moshortcut://:{AppConfig.Mo2GameExecutable}\"")
        {
            WorkingDirectory = settings.InstallDir,
            UseShellExecute = false,
        };
        Process.Start(psi);
    }

    public static void OpenMo2(Settings settings)
    {
        var mo2 = Path.Combine(settings.InstallDir, "ModOrganizer.exe");
        Process.Start(new ProcessStartInfo(mo2, $"-p \"{AppConfig.Mo2Profile}\"") { WorkingDirectory = settings.InstallDir, UseShellExecute = false });
    }

    public static void OpenFolder(string dir)
    {
        if (Directory.Exists(dir)) Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
    }

    public static void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    public static void OpenWabbajackGui()
    {
        var gui = WabbajackService.FindGui();
        if (gui != null) Process.Start(new ProcessStartInfo(gui) { WorkingDirectory = Path.GetDirectoryName(gui)!, UseShellExecute = true });
    }
}
