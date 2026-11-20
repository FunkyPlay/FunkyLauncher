// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using FunkyLauncher.Core;

namespace FunkyLauncher.Services;

/// <summary>The launcher's own journal (DataDir\launcher.log): the reasons of failures, so support can see them in the error report.</summary>
public static class AppLog
{
    public static string File => Path.Combine(AppConfig.DataDir, "launcher.log");

    public static void Write(string msg)
    {
        try
        {
            Directory.CreateDirectory(AppConfig.DataDir);
            System.IO.File.AppendAllText(File, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {msg}\n");
        }
        catch { }
    }
}
