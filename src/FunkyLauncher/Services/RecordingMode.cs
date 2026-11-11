// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using FunkyLauncher.Core;

namespace FunkyLauncher.Services;

/// <summary>What recording mode changed — saved to a file BEFORE anything is changed, so it can always be undone.</summary>
public sealed class RecordingState
{
    public bool Active { get; set; }
    public DateTime Started { get; set; }
    public bool IconsHidden { get; set; }
    public WallpaperState? Wallpaper { get; set; }
    public List<WindowRef> Minimized { get; set; } = new();
    public bool TaskbarChanged { get; set; }
    public int TaskbarOldState { get; set; }
}

public sealed class WallpaperState
{
    public bool Slideshow { get; set; }
    public List<string> SlideshowItems { get; set; } = new();
    public int SlideshowOptions { get; set; }
    public uint SlideshowTick { get; set; }
    public int Position { get; set; }
    public uint BackgroundColor { get; set; }
    public bool Enabled { get; set; } = true;
    /// <summary>Per monitor: original wallpaper path and our copy of it (Windows may overwrite the original).</summary>
    public List<MonitorWallpaper> Monitors { get; set; } = new();
}

public sealed class MonitorWallpaper
{
    public string Monitor { get; set; } = "";
    public string Path { get; set; } = "";
    public string? Copy { get; set; }
}

public sealed class WindowRef
{
    public long Handle { get; set; }
    public int Pid { get; set; }
    public string Class { get; set; } = "";
}

/// <summary>
/// Recording mode: hides desktop icons, sets a FunkyPlay wallpaper, minimizes windows (not recording apps),
/// optionally auto-hides the taskbar. Everything is put back by Disable(), also after a restart or a crash.
/// Only documented Windows APIs are used (IDesktopWallpaper, SHAppBarMessage, ShowWindow).
/// </summary>
public static class RecordingMode
{
    private static string Dir => Path.Combine(AppConfig.DataDir, "recording");
    private static string StatePath => Path.Combine(Dir, "state.json");
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>Programs that stay on screen: the launcher itself and recording / streaming / voice apps.</summary>
    private static readonly HashSet<string> Keep = new(StringComparer.OrdinalIgnoreCase)
    {
        "FunkyLauncher", "obs64", "obs32", "obs", "Streamlabs OBS", "Streamlabs Desktop", "NVIDIA Overlay", "NVIDIA Share",
        "nvsphelper64", "Discord", "XSplit.Core", "RadeonSoftware", "ShareX", "OpenScreen",
    };

    public static RecordingState? Load()
    {
        try { return File.Exists(StatePath) ? JsonSerializer.Deserialize<RecordingState>(File.ReadAllText(StatePath)) : null; }
        catch { return null; }
    }

    public static bool IsActive => Load()?.Active == true;

    private static void Save(RecordingState s)
    {
        Directory.CreateDirectory(Dir);
        var tmp = StatePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(s, Json));
        File.Move(tmp, StatePath, overwrite: true);
    }

    /// <summary>Wallpaper type from Windows: 0 picture, 1 solid colour, 2 slideshow, 3 Windows Spotlight.</summary>
    public static int BackgroundType()
    {
        using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Wallpapers");
        return k?.GetValue("BackgroundType") is int v ? v : 0;
    }

    /// <summary>Windows Spotlight can't be turned back on by a program — the player is warned first.</summary>
    public static bool SpotlightWallpaper => BackgroundType() == 3;

    /// <summary>The FunkyPlay wallpaper shipped with the launcher (copied out of the exe once).</summary>
    public static string BrandWallpaper()
    {
        Directory.CreateDirectory(Dir);
        var path = Path.Combine(Dir, "funkyplay-wallpaper-v4.jpg");
        if (!File.Exists(path))
        {
            using var src = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/wallpaper.jpg"))!.Stream;
            using var dst = File.Create(path);
            src.CopyTo(dst);
        }
        return path;
    }

    public sealed record Options(bool Icons, bool Wallpaper, string WallpaperFile, bool Windows, bool Taskbar);

    /// <summary>Turns recording mode on. Each step is saved to the state file before the next one starts.</summary>
    public static List<string> Enable(Options o)
    {
        if (IsActive) return new();
        var errors = new List<string>();
        var s = new RecordingState { Active = true, Started = DateTime.Now };
        Save(s);

        if (o.Wallpaper)
        {
            try
            {
                s.Wallpaper = CaptureWallpaper();
                Save(s);
                var dw = Desktop();
                dw.SetWallpaper(null, o.WallpaperFile);
                dw.SetPosition(4); // fill
            }
            catch (Exception ex) { errors.Add("wallpaper: " + ex.Message); }
        }
        if (o.Icons)
        {
            try
            {
                if (!IconsHiddenNow() && ToggleIcons()) { s.IconsHidden = true; Save(s); }
            }
            catch (Exception ex) { errors.Add("icons: " + ex.Message); }
        }
        if (o.Taskbar)
        {
            try
            {
                var old = TaskbarState();
                if ((old & 1) == 0)
                {
                    s.TaskbarOldState = old; s.TaskbarChanged = true; Save(s);
                    SetTaskbarState(old | 1);
                }
            }
            catch (Exception ex) { errors.Add("taskbar: " + ex.Message); }
        }
        if (o.Windows)
        {
            try
            {
                foreach (var w in WindowsToMinimize())
                {
                    s.Minimized.Add(w);
                    ShowWindowAsync((IntPtr)w.Handle, 6); // SW_MINIMIZE
                }
                Save(s);
            }
            catch (Exception ex) { errors.Add("windows: " + ex.Message); }
        }
        return errors;
    }

    /// <summary>Puts back everything recording mode changed. Steps that fail are reported; the rest still run.</summary>
    public static List<string> Disable()
    {
        var s = Load();
        var errors = new List<string>();
        if (s == null || !s.Active) return errors;

        if (s.IconsHidden)
        {
            try { if (IconsHiddenNow()) ToggleIcons(); }
            catch (Exception ex) { errors.Add("icons: " + ex.Message); }
        }
        if (s.Wallpaper != null)
        {
            try { RestoreWallpaper(s.Wallpaper); }
            catch (Exception ex) { errors.Add("wallpaper: " + ex.Message); }
        }
        if (s.TaskbarChanged)
        {
            try { SetTaskbarState(s.TaskbarOldState); }
            catch (Exception ex) { errors.Add("taskbar: " + ex.Message); }
        }
        foreach (var w in Enumerable.Reverse(s.Minimized))
        {
            try
            {
                var h = (IntPtr)w.Handle;
                // after a restart a handle may belong to another window: check process and class too
                if (IsWindow(h) && IsIconic(h) && PidOf(h) == w.Pid && ClassOf(h) == w.Class) ShowWindowAsync(h, 9); // SW_RESTORE
            }
            catch { /* a closed window — nothing to restore */ }
        }

        if (errors.Count == 0)
        {
            s.Active = false;
            Save(s);
        }
        return errors;
    }

    // ---------- desktop icons ----------

    public static bool IconsHiddenNow()
    {
        using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
        return k?.GetValue("HideIcons") is int v && v != 0;
    }

    /// <summary>Same as Desktop → View → Show desktop icons. Returns true if the state really changed.</summary>
    private static bool ToggleIcons()
    {
        var before = IconsHiddenNow();
        var view = DesktopView();
        if (view == IntPtr.Zero) throw new InvalidOperationException("desktop window not found");
        SendMessage(view, 0x0111, (IntPtr)0x7402, IntPtr.Zero); // WM_COMMAND, "toggle desktop icons"
        for (var i = 0; i < 20 && IconsHiddenNow() == before; i++) Thread.Sleep(50);
        return IconsHiddenNow() != before;
    }

    private static IntPtr DesktopView()
    {
        var progman = FindWindow("Progman", null);
        var view = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        if (view != IntPtr.Zero) return view;
        var worker = IntPtr.Zero;
        while ((worker = FindWindowEx(IntPtr.Zero, worker, "WorkerW", null)) != IntPtr.Zero)
        {
            view = FindWindowEx(worker, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (view != IntPtr.Zero) return view;
        }
        return IntPtr.Zero;
    }

    // ---------- wallpaper ----------

    private static IDesktopWallpaper Desktop() => (IDesktopWallpaper)new DesktopWallpaperClass();

    private static WallpaperState CaptureWallpaper()
    {
        var dw = Desktop();
        var w = new WallpaperState
        {
            Position = dw.GetPosition(),
            BackgroundColor = dw.GetBackgroundColor(),
        };
        var status = dw.GetStatus();
        w.Enabled = (status & 1) != 0;
        if ((status & 2) != 0 || BackgroundType() == 2)
        {
            try
            {
                var items = dw.GetSlideshow();
                for (uint i = 0; i < items.GetCount(); i++) w.SlideshowItems.Add(items.GetItemAt(i).GetDisplayName(0x80058000)); // SIGDN_FILESYSPATH
                dw.GetSlideshowOptions(out var opt, out var tick);
                w.SlideshowOptions = opt; w.SlideshowTick = tick;
                w.Slideshow = w.SlideshowItems.Count > 0;
            }
            catch { w.Slideshow = false; }
        }
        Directory.CreateDirectory(Dir);
        var n = dw.GetMonitorDevicePathCount();
        for (uint i = 0; i < n; i++)
        {
            var id = dw.GetMonitorDevicePathAt(i);
            var path = "";
            try { path = dw.GetWallpaper(id) ?? ""; } catch { }
            var m = new MonitorWallpaper { Monitor = id, Path = path };
            if (path.Length > 0 && File.Exists(path))
            {
                var copy = Path.Combine(Dir, $"old-wallpaper-{i}{ImageExt(path)}");
                File.Copy(path, copy, overwrite: true);
                m.Copy = copy;
            }
            w.Monitors.Add(m);
        }
        return w;
    }

    private static void RestoreWallpaper(WallpaperState w)
    {
        var dw = Desktop();
        if (w.Slideshow)
        {
            var pidls = new List<IntPtr>();
            try
            {
                foreach (var p in w.SlideshowItems)
                    if (SHParseDisplayName(p, IntPtr.Zero, out var pidl, 0, out _) == 0) pidls.Add(pidl);
                if (pidls.Count == 0) throw new InvalidOperationException("slideshow folder not found");
                Marshal.ThrowExceptionForHR(SHCreateShellItemArrayFromIDLists((uint)pidls.Count, pidls.ToArray(), out var array));
                dw.SetSlideshowOptions(w.SlideshowOptions, w.SlideshowTick);
                dw.SetSlideshow(array);
            }
            finally { foreach (var p in pidls) Marshal.FreeCoTaskMem(p); }
        }
        else
        {
            foreach (var m in w.Monitors)
            {
                // the original path may be Windows' own "TranscodedWallpaper" that our wallpaper overwrote — then use our copy
                var original = m.Path.Length > 0 && File.Exists(m.Path) && !m.Path.Contains(@"\Microsoft\Windows\Themes\", StringComparison.OrdinalIgnoreCase);
                var file = original ? m.Path : m.Copy;
                if (!string.IsNullOrEmpty(file) && File.Exists(file)) dw.SetWallpaper(m.Monitor, file);
            }
        }
        dw.SetBackgroundColor(w.BackgroundColor);
        dw.SetPosition(w.Position);
        if (!w.Enabled) dw.Enable(false);
    }

    private static string ImageExt(string path)
    {
        var head = new byte[4];
        using (var f = File.OpenRead(path)) f.ReadExactly(head, 0, Math.Min(4, (int)f.Length));
        if (head[0] == 0xFF && head[1] == 0xD8) return ".jpg";
        if (head[0] == 0x89 && head[1] == 0x50) return ".png";
        if (head[0] == 0x42 && head[1] == 0x4D) return ".bmp";
        var ext = Path.GetExtension(path);
        return ext.Length > 0 ? ext : ".jpg";
    }

    // ---------- taskbar ----------

    private static int TaskbarState()
    {
        var d = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>() };
        return (int)SHAppBarMessage(4, ref d); // ABM_GETSTATE
    }

    private static void SetTaskbarState(int state)
    {
        var d = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = FindWindow("Shell_TrayWnd", null), lParam = (IntPtr)state };
        SHAppBarMessage(10, ref d); // ABM_SETSTATE
    }

    // ---------- windows ----------

    /// <summary>Normal visible app windows, except the launcher and recording apps.</summary>
    public static List<WindowRef> WindowsToMinimize()
    {
        var own = Environment.ProcessId;
        var list = new List<WindowRef>();
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h) || IsIconic(h) || GetWindow(h, 4) != IntPtr.Zero) return true; // GW_OWNER
            if ((GetWindowLong(h, -20) & 0x80) != 0) return true; // WS_EX_TOOLWINDOW
            if (GetWindowTextLength(h) == 0) return true;
            if (DwmGetWindowAttribute(h, 14, out int cloaked, 4) == 0 && cloaked != 0) return true; // DWMWA_CLOAKED
            var cls = ClassOf(h);
            if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return true;
            var pid = PidOf(h);
            if (pid == own) return true;
            string name;
            try { name = Process.GetProcessById(pid).ProcessName; } catch { return true; }
            if (Keep.Contains(name)) return true;
            list.Add(new WindowRef { Handle = (long)h, Pid = pid, Class = cls });
            return true;
        }, IntPtr.Zero);
        return list;
    }

    private static int PidOf(IntPtr h) { GetWindowThreadProcessId(h, out var pid); return (int)pid; }

    private static string ClassOf(IntPtr h)
    {
        var sb = new StringBuilder(256);
        GetClassName(h, sb, sb.Capacity);
        return sb.ToString();
    }

    // ---------- Win32 ----------

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int index);
    [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr h, int cmd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string cls, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string? title);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int value, int size);
    [DllImport("shell32.dll")] private static extern uint SHAppBarMessage(uint msg, ref APPBARDATA data);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHParseDisplayName(string name, IntPtr bc, out IntPtr pidl, uint sfgaoIn, out uint sfgaoOut);
    [DllImport("shell32.dll")] private static extern int SHCreateShellItemArrayFromIDLists(uint count, IntPtr[] pidls, out IShellItemArray array);

    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA
    {
        public int cbSize; public IntPtr hWnd; public uint uCallbackMessage; public uint uEdge;
        public int left, top, right, bottom; public IntPtr lParam;
    }

    [ComImport, Guid("C2CF3110-460E-4fc1-B9D0-8A1C0C9CC4BD")]
    private class DesktopWallpaperClass { }

    [ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDesktopWallpaper
    {
        void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorID, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
        [return: MarshalAs(UnmanagedType.LPWStr)] string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorID);
        [return: MarshalAs(UnmanagedType.LPWStr)] string GetMonitorDevicePathAt(uint monitorIndex);
        uint GetMonitorDevicePathCount();
        Rect GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorID);
        void SetBackgroundColor(uint color);
        uint GetBackgroundColor();
        void SetPosition(int position);
        int GetPosition();
        void SetSlideshow(IShellItemArray items);
        IShellItemArray GetSlideshow();
        void SetSlideshowOptions(int options, uint slideshowTick);
        void GetSlideshowOptions(out int options, out uint slideshowTick);
        void AdvanceSlideshow([MarshalAs(UnmanagedType.LPWStr)] string? monitorID, int direction);
        int GetStatus();
        void Enable([MarshalAs(UnmanagedType.Bool)] bool enable);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [ComImport, Guid("b63ea76d-1f85-456f-a19c-48159efa858b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemArray
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        void GetPropertyStore(int flags, ref Guid riid, out IntPtr ppv);
        void GetPropertyDescriptionList(IntPtr keyType, ref Guid riid, out IntPtr ppv);
        void GetAttributes(int attribFlags, uint sfgaoMask, out uint attribs);
        uint GetCount();
        IShellItem GetItemAt(uint index);
        void EnumItems(out IntPtr ppenum);
    }

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        IShellItem GetParent();
        [return: MarshalAs(UnmanagedType.LPWStr)] string GetDisplayName(uint sigdn);
        uint GetAttributes(uint mask);
        int Compare(IShellItem other, uint hint);
    }

    /// <summary>Read-only check for testing: what would be saved and minimized, without changing anything.</summary>
    public static string DryRun()
    {
        var sb = new StringBuilder();
        var w = CaptureWallpaperInfoOnly();
        sb.AppendLine($"background type {BackgroundType()}, slideshow {w.Slideshow}: {string.Join("; ", w.SlideshowItems)}, options {w.SlideshowOptions}, tick {w.SlideshowTick}, position {w.Position}, enabled {w.Enabled}");
        foreach (var m in w.Monitors) sb.AppendLine($"monitor {m.Monitor[..Math.Min(40, m.Monitor.Length)]}… wallpaper {m.Path}");
        sb.AppendLine($"icons hidden {IconsHiddenNow()}, desktop view found {DesktopView() != IntPtr.Zero}, taskbar state {TaskbarState()}");
        foreach (var x in WindowsToMinimize())
        {
            string name; try { name = Process.GetProcessById(x.Pid).ProcessName; } catch { name = "?"; }
            sb.AppendLine($"would minimize: {name} [{x.Class}]");
        }
        return sb.ToString();
    }

    private static WallpaperState CaptureWallpaperInfoOnly()
    {
        var dw = Desktop();
        var w = new WallpaperState { Position = dw.GetPosition(), BackgroundColor = dw.GetBackgroundColor() };
        var status = dw.GetStatus();
        w.Enabled = (status & 1) != 0;
        try
        {
            var items = dw.GetSlideshow();
            for (uint i = 0; i < items.GetCount(); i++) w.SlideshowItems.Add(items.GetItemAt(i).GetDisplayName(0x80058000));
            dw.GetSlideshowOptions(out var opt, out var tick);
            w.SlideshowOptions = opt; w.SlideshowTick = tick;
            w.Slideshow = (status & 2) != 0 || (BackgroundType() == 2 && w.SlideshowItems.Count > 0);
        }
        catch { }
        for (uint i = 0; i < dw.GetMonitorDevicePathCount(); i++)
        {
            var id = dw.GetMonitorDevicePathAt(i);
            string p; try { p = dw.GetWallpaper(id); } catch (Exception ex) { p = "error " + ex.Message; }
            w.Monitors.Add(new MonitorWallpaper { Monitor = id, Path = p });
        }
        return w;
    }
}
