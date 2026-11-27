// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Diagnostics;
using System.Text;

namespace FunkyLauncher.Services;

/// <summary>
/// One-click installs through winget, the package manager built into Windows 10/11. winget downloads the
/// program from its author's official source and checks the file hash; the launcher never stores installers.
/// </summary>
public static class WingetService
{
    private static bool? _available;

    /// <summary>winget is present (Windows 10 1809+ with App Installer, every Windows 11).</summary>
    public static async Task<bool> AvailableAsync()
    {
        if (_available is { } a) return a;
        try { var (code, _) = await RunAsync("--version", TimeSpan.FromSeconds(20)); _available = code == 0; }
        catch { _available = false; }
        return _available.Value;
    }

    /// <summary>Is the package already installed on this PC?</summary>
    public static async Task<bool> IsInstalledAsync(string id, string source)
    {
        try
        {
            var (code, output) = await RunAsync($"list --id \"{id}\" --exact --source {source} --accept-source-agreements --disable-interactivity", TimeSpan.FromSeconds(60));
            return code == 0 && output.Contains(id, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>Installs the package silently; Windows may ask for admin rights (UAC). Returns null on success or an error text.</summary>
    public static async Task<string?> InstallAsync(string id, string source)
    {
        var (code, output) = await RunAsync(
            $"install --id \"{id}\" --exact --source {source} --silent --accept-package-agreements --accept-source-agreements --disable-interactivity",
            TimeSpan.FromMinutes(20));
        if (code == 0) return null;
        if (code == unchecked((int)0x8A15002B)) return null; // APPINSTALLER_CLI_ERROR_UPDATE_NOT_APPLICABLE: already installed, nothing newer
        var last = output.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length > 0) ?? "";
        return $"winget 0x{code:X8} {last}".Trim();
    }

    private static async Task<(int Code, string Output)> RunAsync(string args, TimeSpan timeout)
    {
        var psi = new ProcessStartInfo("winget", args)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("winget");
        var outTask = p.StandardOutput.ReadToEndAsync();
        var errTask = p.StandardError.ReadToEndAsync();
        using var cts = new CancellationTokenSource(timeout);
        try { await p.WaitForExitAsync(cts.Token); }
        catch (OperationCanceledException) { try { p.Kill(true); } catch { } throw new TimeoutException("winget"); }
        return (p.ExitCode, await outTask + await errTask);
    }
}
