// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FunkyLauncher;

/// <summary>
/// Developer tool for the design team: FunkyLauncher.exe --shots [folder]
/// Saves a PNG of the main pages (RU and EN), then closes. The window is drawn off-screen, nothing is clicked,
/// window position is not saved. Use a separate data folder (FUNKYLAUNCHER_DATA) to show a prepared state.
/// </summary>
public partial class MainWindow
{
    private bool _shotsMode;
    public bool ShotsMode => _shotsMode;

    /// <summary>The new version is in place and started: close this one for good (not to the tray).</summary>
    public void ExitForUpdate()
    {
        _exiting = true;
        Application.Current.Shutdown();
    }

    private void SetupShots(string[] args)
    {
        var k = Array.IndexOf(args, "--shots");
        if (k < 0) return;
        _shotsMode = true;
        var dir = k + 1 < args.Length && !args[k + 1].StartsWith("--")
            ? args[k + 1]
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Funky Launcher shots", DateTime.Now.ToString("yyyy-MM-dd_HH-mm"));
        Directory.CreateDirectory(dir);
        WindowState = WindowState.Normal;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Width = 1280; Height = 800;
        Left = -30000; Top = -30000;
        ShowInTaskbar = false;
        ShowActivated = false;
        ContentRendered += async (_, _) => await TakeShotsAsync(dir);
    }

    private async Task TakeShotsAsync(string dir)
    {
        var startLang = Core.Loc.Lang;
        try
        {
            foreach (var lang in new[] { "ru", "en" })
            {
                Core.Loc.Set(lang);
                var pages = new (string Name, Action Show, double Height)[]
                {
                    ("01-home", () => NavHome.IsChecked = true, 800),
                    ("02-home-full", () => NavHome.IsChecked = true, 1240),
                    ("03-library", () => { NavHome.IsChecked = true; NavLibrary.IsChecked = true; }, 800),
                    ("04-setup", () => ShowSetup(), 800),
                    ("05-setup-full", () => ShowSetup(), 1240),
                    ("06-soft", () => NavSoft.IsChecked = true, 800),
                    ("07-tools", () => { NavTools.IsChecked = true; _tools.Refresh(); }, 800),
                    ("08-settings", () => NavSettings.IsChecked = true, 800),
                };
                foreach (var (name, show, height) in pages)
                {
                    try
                    {
                        Height = height;
                        show();
                        await Task.Delay(3000); // news, checks and images load asynchronously
                        Save(Path.Combine(dir, $"{lang}-{name}.png"));
                    }
                    catch (Exception e) { File.AppendAllText(Path.Combine(dir, "errors.txt"), $"{lang}-{name}: {e}\n"); }
                }
                Height = 800;
            }
        }
        finally
        {
            Core.Loc.Set(startLang);
            _exiting = true;
            Application.Current.Shutdown();
        }
    }

    private void Save(string file)
    {
        if (Content is not FrameworkElement root) return;
        root.UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(this);
        var w = (int)Math.Ceiling(root.ActualWidth * dpi.DpiScaleX);
        var h = (int)Math.Ceiling(root.ActualHeight * dpi.DpiScaleY);
        var bmp = new RenderTargetBitmap(w, h, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        var bg = new DrawingVisual();
        using (var dc = bg.RenderOpen())
            dc.DrawRectangle(Background ?? Brushes.Black, null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
        bmp.Render(bg);
        bmp.Render(root);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(file);
        enc.Save(fs);
    }
}
