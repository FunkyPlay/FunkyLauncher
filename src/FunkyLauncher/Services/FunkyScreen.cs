// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using FunkyLauncher.Core;

namespace FunkyLauncher.Services;

/// <summary>
/// Funky Screen: a screenshot of the screen under the mouse or of a picked area, and a video of the screen
/// (FFmpeg gdigrab, the same FFmpeg the player downloads). Only ordinary Windows calls — reading the screen
/// (BitBlt) — nothing is pressed or typed for the player. Files go to Pictures\Funky Скрины and Videos\Funky Записи.
/// </summary>
public static class FunkyScreen
{
    public static string ShotsDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), Loc.T("screen.folder.shots"));
    public static string VideosDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), Loc.T("screen.folder.videos"));

    // ---------- monitors in real pixels ----------

    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr ctx);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT p, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr mon, ref MONITORINFO mi);
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFO { public int Size; public RECT Monitor, Work; public uint Flags; }

    private static readonly IntPtr PerMonitorV2 = new(-4);

    /// <summary>Runs code with real (unscaled) pixel coordinates on this thread, then restores the old mode.</summary>
    public static T InRealPixels<T>(Func<T> f)
    {
        var old = SetThreadDpiAwarenessContext(PerMonitorV2);
        try { return f(); }
        finally { if (old != IntPtr.Zero) SetThreadDpiAwarenessContext(old); }
    }

    /// <summary>The monitor under the mouse, in real pixels. Call inside <see cref="InRealPixels"/>.</summary>
    public static Rectangle MonitorUnderMouse()
    {
        GetCursorPos(out var p);
        var mi = new MONITORINFO { Size = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(MonitorFromPoint(p, 2 /* NEAREST */), ref mi);
        return Rectangle.FromLTRB(mi.Monitor.L, mi.Monitor.T, mi.Monitor.R, mi.Monitor.B);
    }

    public static Bitmap Grab(Rectangle r)
    {
        var bmp = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(r.Location, Point.Empty, r.Size, CopyPixelOperation.SourceCopy);
        return bmp;
    }

    /// <summary>Saves a picture as PNG with a time stamp name; returns the path.</summary>
    public static string SavePng(Bitmap bmp)
    {
        Directory.CreateDirectory(ShotsDir);
        var file = Unique(Path.Combine(ShotsDir, $"Funky {DateTime.Now:yyyy-MM-dd HH-mm-ss}.png"));
        bmp.Save(file, ImageFormat.Png);
        try { Saved?.Invoke(file, false); } catch { /* a listener never breaks a screenshot */ }
        return file;
    }

    /// <summary>A screenshot (video=false, raised before it goes to the clipboard) or a recording was saved.</summary>
    public static event Action<string, bool>? Saved;

    private static string Unique(string file)
    {
        if (!File.Exists(file)) return file;
        var dir = Path.GetDirectoryName(file)!; var name = Path.GetFileNameWithoutExtension(file); var ext = Path.GetExtension(file);
        for (var i = 2; ; i++) { var f = Path.Combine(dir, $"{name} ({i}){ext}"); if (!File.Exists(f)) return f; }
    }

    // ---------- video ----------

    private static Process? _rec;
    private static string? _recFile;
    public static DateTime RecStarted { get; private set; }
    public static bool Recording => _rec is { HasExited: false };

    /// <summary>Starts recording the given screen area (real pixels) to an MP4. FFmpeg must be ready (<see cref="PlayerEngine.Ready"/>).</summary>
    public static void StartRecording(Rectangle r)
    {
        if (Recording) return;
        Directory.CreateDirectory(VideosDir);
        _recFile = Unique(Path.Combine(VideosDir, $"Funky {DateTime.Now:yyyy-MM-dd HH-mm-ss}.mp4"));
        // H.264 needs even sizes.
        var w = r.Width & ~1; var h = r.Height & ~1;
        var args = $"-hide_banner -loglevel error -f gdigrab -framerate 30 -draw_mouse 1 -offset_x {r.X} -offset_y {r.Y} -video_size {w}x{h} -i desktop " +
                   $"-c:v libx264 -preset ultrafast -crf 20 -pix_fmt yuv420p -movflags +faststart \"{_recFile}\"";
        var psi = new ProcessStartInfo(PlayerEngine.Ffmpeg, args)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardError = true,
        };
        _rec = Process.Start(psi) ?? throw new InvalidOperationException("ffmpeg");
        _rec.ErrorDataReceived += (_, _) => { };
        _rec.BeginErrorReadLine();
        RecStarted = DateTime.Now;
    }

    /// <summary>Stops the recording ("q" to FFmpeg, so the MP4 is finished properly); returns the file or null.</summary>
    public static async Task<string?> StopRecordingAsync()
    {
        var p = _rec; var file = _recFile;
        _rec = null; _recFile = null;
        if (p == null) return null;
        try
        {
            if (!p.HasExited) { await p.StandardInput.WriteAsync('q'); await p.StandardInput.FlushAsync(); }
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await p.WaitForExitAsync(cts.Token); } catch (OperationCanceledException) { try { p.Kill(); } catch { } }
        }
        finally { p.Dispose(); }
        var ok = file != null && File.Exists(file) && new FileInfo(file).Length > 0;
        if (ok) try { Saved?.Invoke(file!, true); } catch { }
        return ok ? file : null;
    }

    /// <summary>Makes a GIF next to the video (15 fps, 640 px wide, own palette); returns its path.</summary>
    public static async Task<string> MakeGifAsync(string video)
    {
        var gif = Unique(Path.ChangeExtension(video, ".gif"));
        var filter = "fps=15,scale=640:-1:flags=lanczos,split[a][b];[a]palettegen=stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=4";
        var psi = new ProcessStartInfo(PlayerEngine.Ffmpeg, $"-hide_banner -loglevel error -y -i \"{video}\" -vf \"{filter}\" \"{gif}\"")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true,
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("ffmpeg");
        var err = await p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        if (p.ExitCode != 0 || !File.Exists(gif)) throw new InvalidOperationException(err.Trim().Split('\n').LastOrDefault() ?? "ffmpeg");
        return gif;
    }

    public static void OpenFile(string file) { try { Process.Start(new ProcessStartInfo(file) { UseShellExecute = true }); } catch { } }

    public static void ShowInFolder(string file)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{file}\"") { UseShellExecute = true }); } catch { }
    }

    public static void OpenFolder(string dir)
    {
        try { Directory.CreateDirectory(dir); Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true }); } catch { }
    }
}
