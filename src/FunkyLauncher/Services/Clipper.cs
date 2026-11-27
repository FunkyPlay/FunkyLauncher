// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Diagnostics;
using System.Globalization;

namespace FunkyLauncher.Services;

/// <summary>Cuts a piece out of a video with the downloaded FFmpeg. The source file is only read, never changed.</summary>
public static class Clipper
{
    public static string ClipsDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Funky Отрывки");
    public static string FramesDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Funky Кадры");

    /// <summary>Folders where games and Windows put recordings (NVIDIA ShadowPlay, Xbox Game Bar), plus our clips.</summary>
    public static IEnumerable<string> RecordingFolders()
    {
        var videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        foreach (var d in new[] { Path.Combine(videos, "NVIDIA"), Path.Combine(videos, "Captures"), Path.Combine(videos, "Radeon ReLive"), ClipsDir })
            if (Directory.Exists(d)) yield return d;
    }

    public static readonly string[] VideoExt = { ".mp4", ".mkv", ".mov", ".avi", ".webm", ".m4v", ".wmv", ".ts", ".flv" };

    public static List<FileInfo> Recordings(int max = 300)
    {
        var list = new List<FileInfo>();
        foreach (var d in RecordingFolders())
        {
            try
            {
                list.AddRange(new DirectoryInfo(d).EnumerateFiles("*", SearchOption.AllDirectories)
                    .Where(f => VideoExt.Contains(f.Extension.ToLowerInvariant()) && f.Length > 0));
            }
            catch { /* a folder we can't read */ }
        }
        return list.OrderByDescending(f => f.LastWriteTime).Take(max).ToList();
    }

    public static string Stamp(double s) => TimeSpan.FromSeconds(Math.Max(0, s)).ToString(s >= 3600 ? @"h\:mm\:ss\.ff" : @"m\:ss\.ff");

    /// <summary>
    /// fast = stream copy (seconds, 1:1 quality, edges snap to the nearest key frame);
    /// otherwise re-encode with H.264/AAC (frame-accurate).
    /// </summary>
    public static async Task<string> CutAsync(string input, double start, double end, bool fast, bool mute, CancellationToken ct)
    {
        Directory.CreateDirectory(ClipsDir);
        var ext = fast ? Path.GetExtension(input).ToLowerInvariant() : ".mp4";
        var name = $"{Path.GetFileNameWithoutExtension(input)} {Stamp(start).Replace(':', '.')}-{Stamp(end).Replace(':', '.')}{(fast ? "" : " точно")}";
        var output = Path.Combine(ClipsDir, name + ext);
        for (var i = 2; File.Exists(output); i++) output = Path.Combine(ClipsDir, $"{name} ({i}){ext}");

        string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-y", "-ss", F(start), "-i", input, "-t", F(end - start), "-map", "0:v:0", "-map", "0:a?" };
        if (fast) args.AddRange(new[] { "-c", "copy", "-avoid_negative_ts", "make_zero" });
        else args.AddRange(new[] { "-c:v", "libx264", "-preset", "veryfast", "-crf", "18", "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "192k", "-movflags", "+faststart" });
        if (mute) args.Add("-an");
        args.Add(output);

        var psi = new ProcessStartInfo(PlayerEngine.Ffmpeg) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        _ = p.StandardOutput.ReadToEndAsync(ct);
        var err = await p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        if (p.ExitCode != 0 || !File.Exists(output) || new FileInfo(output).Length == 0)
        {
            try { if (File.Exists(output)) File.Delete(output); } catch { }
            throw new InvalidOperationException(err.Trim().Split('\n').LastOrDefault() ?? "ffmpeg");
        }
        return output;
    }
}
