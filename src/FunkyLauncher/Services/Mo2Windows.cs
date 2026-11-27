// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FunkyLauncher.Services;

/// <summary>
/// Hides Mod Organizer's windows (splash and "waiting on an application to close") while the game
/// runs from PLAY. MO2 keeps working in the background and exits with the game.
/// </summary>
public sealed class Mo2Windows
{
    private delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int cmd);
    private const int SW_HIDE = 0, SW_SHOW = 5;

    private readonly string _installDir;
    private readonly Dictionary<IntPtr, uint> _hidden = new(); // window → MO2 process id

    public Mo2Windows(string installDir) => _installDir = Path.GetFullPath(installDir).TrimEnd('\\') + "\\";

    /// <summary>Hides every visible window of this modlist's ModOrganizer.exe.</summary>
    public void HideAll()
    {
        var pids = new HashSet<uint>();
        foreach (var p in Process.GetProcessesByName("ModOrganizer"))
        {
            try
            {
                if (p.MainModule?.FileName.StartsWith(_installDir, StringComparison.OrdinalIgnoreCase) == true) pids.Add((uint)p.Id);
            }
            catch { /* no access — not ours */ }
        }
        if (pids.Count == 0) return;
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pids.Contains(pid) && IsWindowVisible(hwnd))
            {
                ShowWindow(hwnd, SW_HIDE);
                _hidden[hwnd] = pid;
            }
            return true;
        }, IntPtr.Zero);
    }

    /// <summary>Shows the hidden windows again (something went wrong — the player must see MO2's message).</summary>
    public void ShowAll()
    {
        foreach (var (h, pid) in _hidden)
        {
            // only if the window still exists and still belongs to MO2 (handles can be reused)
            GetWindowThreadProcessId(h, out var now);
            if (now == pid) ShowWindow(h, SW_SHOW);
        }
        _hidden.Clear();
    }
}
