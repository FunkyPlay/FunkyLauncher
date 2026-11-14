// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FunkyLauncher;

public partial class App : Application
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_CAPTION_COLOR = 35; // Windows 11

    private static Mutex? _single;

    protected override void OnStartup(StartupEventArgs e)
    {
        // started by the previous version after an update: let it close first (it holds the lock below)
        Services.LauncherUpdate.WaitForPrevious(e.Args);
        Services.LauncherUpdate.CleanupOld();
        // one launcher per data folder: a second start only brings the running window to the front
        // (two copies wrote the same settings and modlist.txt at once)
        var key = "FunkyLauncher." + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(Core.AppConfig.DataDir.ToLowerInvariant())))[..16];
        _single = new Mutex(true, key + ".single", out var first);
        var show = new EventWaitHandle(false, EventResetMode.AutoReset, key + ".show");
        if (!first)
        {
            show.Set();
            Environment.Exit(0);
        }
        new Thread(() =>
        {
            while (show.WaitOne())
                Dispatcher.Invoke(() =>
                {
                    if (MainWindow is not { } w) return;
                    w.Show();
                    if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
                    w.Activate();
                });
        }) { IsBackground = true }.Start();


        // every launcher window gets a dark title bar, whatever the Windows theme is
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((s, _) => DarkTitleBar((Window)s)));
        base.OnStartup(e);
    }

    public static void DarkTitleBar(Window w)
    {
        try
        {
            var hwnd = new WindowInteropHelper(w).Handle;
            if (hwnd == IntPtr.Zero) return;
            var on = 1;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int));
            var color = 0x00140F0F; // COLORREF 0x00BBGGRR → #0F0F14, the launcher background
            DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref color, sizeof(int));
        }
        catch { /* older Windows: keep the default title bar */ }
    }
}
