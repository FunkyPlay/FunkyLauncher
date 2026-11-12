// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Diagnostics;
using System.Security.Cryptography;
using FunkyLauncher.Core;

namespace FunkyLauncher.Services;

/// <summary>
/// "Funky Help": starts the official, unmodified RustDesk client (downloaded on first use, checked by SHA-256)
/// pointed at the FunkyPlay RustDesk server. RustDesk reads the server from its own file name
/// ("rustdesk-host=...,key=....exe" — the documented way to preconfigure the client). It runs only while its window
/// is open: no install, no service, no autostart, no unattended access. The player reads the ID and the one-time
/// password from the RustDesk window and sends them to support.
/// </summary>
public static class RemoteHelp
{
    private static string Dir => Path.Combine(AppConfig.DataDir, "help");

    private static string ExeFor(RemoteHelpSource s) => Path.Combine(Dir, $"rustdesk-host={s.Host},key={s.Key},.exe");

    public static bool Available => Catalog.RemoteHelp != null;

    public static async Task StartAsync(IProgress<int> progress, CancellationToken ct)
    {
        var s = Catalog.RemoteHelp ?? throw new InvalidOperationException(Loc.T("help.err.nosource"));
        Directory.CreateDirectory(Dir);
        var exe = ExeFor(s);
        if (!File.Exists(exe) || Hash(exe) != s.Sha256.ToLowerInvariant())
        {
            // an old client with another server/key name stays unused; download the current one
            var tmp = Path.Combine(Dir, "rustdesk-download.exe");
            await WabbajackService.DownloadFileAsync(s.Url, tmp, progress, ct);
            if (Hash(tmp) != s.Sha256.ToLowerInvariant())
            {
                File.Delete(tmp);
                throw new InvalidOperationException(Loc.T("help.err.hash"));
            }
            File.Move(tmp, exe, overwrite: true);
        }
        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Dir });
    }

    /// <summary>The portable RustDesk unpacks itself to %LOCALAPPDATA%\rustdesk and runs from there.</summary>
    private static string PortableDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "rustdesk");

    /// <summary>Closes the RustDesk started by "Funky Help" (our folder or its portable folder) — access ends at once.
    /// A RustDesk the player installed himself (Program Files) is not touched.</summary>
    public static void Stop()
    {
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                var f = p.MainModule?.FileName;
                if (f != null && (f.StartsWith(Dir, StringComparison.OrdinalIgnoreCase) || f.StartsWith(PortableDir, StringComparison.OrdinalIgnoreCase)))
                    p.Kill(entireProcessTree: true);
            }
            catch { /* processes we can't inspect are not ours */ }
        }
    }

    /// <summary>This PC's RustDesk ID ("rustdesk --get-id" of the running portable copy), or null if it can't be read.</summary>
    public static async Task<string?> GetIdAsync()
    {
        var exe = Path.Combine(PortableDir, "rustdesk.exe");
        if (!File.Exists(exe)) return null;
        try
        {
            using var p = Process.Start(new ProcessStartInfo(exe, "--get-id")
            {
                UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true, WorkingDirectory = PortableDir,
            })!;
            var read = p.StandardOutput.ReadToEndAsync();
            var done = await Task.WhenAny(read, Task.Delay(15000));
            if (done != read) { try { p.Kill(true); } catch { } return null; }
            var id = (await read).Trim();
            return System.Text.RegularExpressions.Regex.IsMatch(id, @"^\d{6,12}$") ? id : null;
        }
        catch { return null; }
    }

    /// <summary>"123456789" → "123 456 789" (as RustDesk shows it).</summary>
    public static string FormatId(string id) =>
        System.Text.RegularExpressions.Regex.Replace(id, @"(\d)(?=(\d{3})+$)", "$1 ");

    private static string Hash(string file)
    {
        using var f = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(f)).ToLowerInvariant();
    }
}
