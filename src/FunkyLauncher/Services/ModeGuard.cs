// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Diagnostics;

namespace FunkyLauncher.Services;

/// <summary>
/// Recording mode hides the desktop icons and the taskbar. If the launcher closes or crashes while it is on,
/// the player is left without a desktop. A tiny hidden copy of the launcher ("--mode-guard &lt;pid&gt;") waits for
/// the launcher to exit and then puts everything back. On a normal close the launcher puts it back itself.
/// </summary>
public static class ModeGuard
{
    private static bool _started;

    /// <summary>Start the guard for this launcher (once per run).</summary>
    public static void Start()
    {
        if (_started) return;
        try
        {
            var exe = Environment.ProcessPath;
            if (exe == null) return;
            Process.Start(new ProcessStartInfo(exe, $"--mode-guard {Environment.ProcessId}")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(exe)!,
            });
            _started = true;
        }
        catch { /* no guard: the reminder at the next start still offers to put everything back */ }
    }

    /// <summary>
    /// "--mode-guard &lt;pid&gt;": wait for that launcher to exit, then turn recording mode off if it is still on.
    /// Returns true when this process was the guard (the caller exits right away).
    /// </summary>
    public static bool RunIfGuard(string[] args)
    {
        var i = Array.IndexOf(args, "--mode-guard");
        if (i < 0) return false;
        if (i + 1 < args.Length && int.TryParse(args[i + 1], out var pid))
        {
            try { Process.GetProcessById(pid).WaitForExit(); } catch { /* already gone */ }
            try { if (RecordingMode.IsActive) RecordingMode.Disable(); } catch { }
        }
        return true;
    }
}
