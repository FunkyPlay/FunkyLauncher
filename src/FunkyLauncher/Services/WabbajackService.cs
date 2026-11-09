// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using FunkyLauncher.Core;

namespace FunkyLauncher.Services;

/// <summary>Finds or downloads the official Wabbajack and runs its command-line tool.</summary>
public static class WabbajackService
{
    private static string ManagedRoot => Path.Combine(AppConfig.DataDir, "Wabbajack");

    public static string NexusLoginFile =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Wabbajack", "encrypted", "nexus-oauth-info");

    public static bool IsNexusLoggedIn => File.Exists(NexusLoginFile);

    /// <summary>Path to wabbajack-cli.exe if one is present (managed copy first, then C:\Tools\Wabbajack).</summary>
    public static string? FindCli()
    {
        foreach (var root in new[] { ManagedRoot, @"C:\Tools\Wabbajack" })
        {
            if (!Directory.Exists(root)) continue;
            var found = Directory.EnumerateFiles(root, "wabbajack-cli.exe", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (found != null) return found;
        }
        return null;
    }

    /// <summary>The Wabbajack GUI that sits next to the cli folder (used for Nexus login).</summary>
    public static string? FindGui()
    {
        var cli = FindCli();
        if (cli == null) return null;
        var versionDir = Directory.GetParent(Path.GetDirectoryName(cli)!)!.FullName;
        var gui = Path.Combine(versionDir, "Wabbajack.exe");
        return File.Exists(gui) ? gui : null;
    }

    public static string? CliVersion()
    {
        var cli = FindCli();
        if (cli == null) return null;
        return Directory.GetParent(Path.GetDirectoryName(cli)!)!.Name;
    }

    /// <summary>Downloads the latest official Wabbajack release zip from GitHub and unpacks it.</summary>
    public static async Task<string> DownloadAsync(IProgress<int> progress, CancellationToken token)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("FunkyLauncher/" + AppConfig.ModlistVersion);
        var json = await http.GetStringAsync(AppConfig.WabbajackReleasesApi, token);
        using var doc = JsonDocument.Parse(json);
        var tag = doc.RootElement.GetProperty("tag_name").GetString()!;
        var asset = doc.RootElement.GetProperty("assets").EnumerateArray()
            .First(a => a.GetProperty("name").GetString() == tag + ".zip");
        var url = asset.GetProperty("browser_download_url").GetString()!;
        var size = asset.GetProperty("size").GetInt64();

        Directory.CreateDirectory(ManagedRoot);
        var zipPath = Path.Combine(ManagedRoot, tag + ".zip");
        using (var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token))
        {
            resp.EnsureSuccessStatusCode();
            await using var src = await resp.Content.ReadAsStreamAsync(token);
            await using var dst = File.Create(zipPath);
            var buffer = new byte[1 << 16];
            long done = 0;
            int read;
            while ((read = await src.ReadAsync(buffer, token)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, read), token);
                done += read;
                progress.Report((int)(done * 100 / Math.Max(1, size)));
            }
        }

        var target = Path.Combine(ManagedRoot, tag);
        if (Directory.Exists(target)) Directory.Delete(target, true);
        ZipFile.ExtractToDirectory(zipPath, target);
        File.Delete(zipPath);
        return FindCli() ?? throw new InvalidOperationException("wabbajack-cli.exe not found after unpacking");
    }

    /// <summary>Runs wabbajack-cli and streams every output line.</summary>
    public static async Task<int> RunAsync(string args, Action<string> onLine, CancellationToken token)
    {
        var cli = FindCli() ?? throw new InvalidOperationException("wabbajack-cli.exe not found");
        var psi = new ProcessStartInfo(cli, args)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(cli)!,
        };
        using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        p.OutputDataReceived += (_, e) => { if (e.Data != null) onLine(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) onLine(e.Data); };
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        using var reg = token.Register(() => { try { if (!p.HasExited) p.Kill(true); } catch { } });
        await p.WaitForExitAsync(CancellationToken.None);
        token.ThrowIfCancellationRequested();
        return p.ExitCode;
    }

    /// <summary>Number of archives listed in a .wabbajack file (it is a zip with a "modlist" JSON entry).</summary>
    public static int CountArchives(string wabbajackFile)
    {
        try
        {
            using var zip = ZipFile.OpenRead(wabbajackFile);
            var entry = zip.GetEntry("modlist");
            if (entry == null) return 0;
            using var s = entry.Open();
            using var doc = JsonDocument.Parse(s);
            return doc.RootElement.TryGetProperty("Archives", out var a) ? a.GetArrayLength() : 0;
        }
        catch
        {
            return 0;
        }
    }
}
