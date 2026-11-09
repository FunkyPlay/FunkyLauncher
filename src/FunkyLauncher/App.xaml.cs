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

    protected override void OnStartup(StartupEventArgs e)
    {
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
