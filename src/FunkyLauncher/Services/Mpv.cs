// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Globalization;
using System.Runtime.InteropServices;

namespace FunkyLauncher.Services;

/// <summary>
/// A thin wrapper over libmpv (the downloaded player engine). mpv draws straight into a window handle we give it.
/// Only what the Funky player needs: open, pause, frame step, seek, position, screenshot.
/// </summary>
public sealed class Mpv : IDisposable
{
    private IntPtr _h;
    private Thread? _events;
    private static bool _loaded;

    public Mpv(IntPtr window)
    {
        if (!_loaded)
        {
            NativeLibrary.SetDllImportResolver(typeof(Mpv).Assembly, (name, _, _) =>
                name == "libmpv-2.dll" ? NativeLibrary.Load(PlayerEngine.Mpv) : IntPtr.Zero);
            _loaded = true;
        }
        _h = mpv_create();
        if (_h == IntPtr.Zero) throw new InvalidOperationException("mpv_create failed");
        Set("wid", ((long)window).ToString(CultureInfo.InvariantCulture));
        Set("vo", "gpu");
        Set("hwdec", "auto-safe");
        Set("keep-open", "yes");          // stay on the last frame instead of closing
        Set("idle", "yes");
        Set("input-default-bindings", "no");
        Set("input-vo-keyboard", "no");   // keys go to the launcher window
        Set("osc", "no");
        Set("osd-level", "1");
        Set("screenshot-format", "png");
        Set("terminal", "no");
        Check(mpv_initialize(_h), "mpv_initialize");
        _events = new Thread(EventLoop) { IsBackground = true, Name = "mpv events" };
        _events.Start();
    }

    // ---------- commands ----------

    public void Load(string file) => Command("loadfile", file, "replace");
    public void TogglePause() => Command("cycle", "pause");
    public void SetPause(bool on) => SetProperty("pause", on ? "yes" : "no");
    public void FrameStep() => Command("frame-step");
    public void FrameBack() => Command("frame-back-step");
    public void SeekRelative(double s) => Command("seek", s.ToString(CultureInfo.InvariantCulture), "relative+exact");
    public void SeekAbsolute(double s) => Command("seek", s.ToString(CultureInfo.InvariantCulture), "absolute+exact");
    public void CycleAudio() => Command("cycle", "aid");
    public void ShowText(string text) => Command("show-text", text, "1500");
    /// <summary>Saves the current video frame at full resolution (no subtitles / OSD).</summary>
    public void Screenshot(string file) => Command("screenshot-to-file", file, "video");

    public double Position => GetDouble("time-pos");
    public double Duration => GetDouble("duration");
    public bool Paused => GetString("pause") == "yes";
    public double Fps => GetDouble("container-fps");
    public string? Path => GetString("path");
    public int Volume { set => SetProperty("volume", value.ToString(CultureInfo.InvariantCulture)); }

    // ---------- plumbing ----------

    public void Command(params string[] args)
    {
        if (_h == IntPtr.Zero) return;
        var ptrs = new IntPtr[args.Length + 1];
        try
        {
            for (var i = 0; i < args.Length; i++) ptrs[i] = Marshal.StringToCoTaskMemUTF8(args[i]);
            mpv_command(_h, ptrs);
        }
        finally { foreach (var p in ptrs) if (p != IntPtr.Zero) Marshal.FreeCoTaskMem(p); }
    }

    private void Set(string name, string value) => mpv_set_option_string(_h, name, value);

    public void SetProperty(string name, string value) { if (_h != IntPtr.Zero) mpv_set_property_string(_h, name, value); }

    public string? GetString(string name)
    {
        if (_h == IntPtr.Zero) return null;
        var p = mpv_get_property_string(_h, name);
        if (p == IntPtr.Zero) return null;
        try { return Marshal.PtrToStringUTF8(p); } finally { mpv_free(p); }
    }

    private double GetDouble(string name) =>
        double.TryParse(GetString(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private void EventLoop()
    {
        while (true)
        {
            var h = _h;
            if (h == IntPtr.Zero) return;
            var ev = mpv_wait_event(h, 1.0);
            if (ev == IntPtr.Zero) continue;
            if (Marshal.ReadInt32(ev) == 1) return; // MPV_EVENT_SHUTDOWN
        }
    }

    private static void Check(int rc, string what) { if (rc < 0) throw new InvalidOperationException($"{what}: {rc}"); }

    public void Dispose()
    {
        var h = _h;
        if (h == IntPtr.Zero) return;
        _h = IntPtr.Zero;
        mpv_terminate_destroy(h);
    }

    [DllImport("libmpv-2.dll")] private static extern IntPtr mpv_create();
    [DllImport("libmpv-2.dll")] private static extern int mpv_initialize(IntPtr ctx);
    [DllImport("libmpv-2.dll")] private static extern void mpv_terminate_destroy(IntPtr ctx);
    [DllImport("libmpv-2.dll")] private static extern int mpv_command(IntPtr ctx, IntPtr[] args);
    [DllImport("libmpv-2.dll")] private static extern int mpv_set_option_string(IntPtr ctx, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string data);
    [DllImport("libmpv-2.dll")] private static extern int mpv_set_property_string(IntPtr ctx, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string data);
    [DllImport("libmpv-2.dll")] private static extern IntPtr mpv_get_property_string(IntPtr ctx, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport("libmpv-2.dll")] private static extern void mpv_free(IntPtr data);
    [DllImport("libmpv-2.dll")] private static extern IntPtr mpv_wait_event(IntPtr ctx, double timeout);
}
