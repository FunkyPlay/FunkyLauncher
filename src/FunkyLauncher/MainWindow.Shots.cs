// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FunkyLauncher.Core;

namespace FunkyLauncher;

/// <summary>
/// Developer tool for the design team: FunkyLauncher.exe --shots [folder]
/// Saves a PNG of every page (RU and EN) plus the texts of the install dialogs, then closes.
/// The window is drawn off-screen, nothing is clicked, settings and window position are not saved.
/// </summary>
public partial class MainWindow
{
    private bool _shotsMode;

    private void SetupShots(string[] args)
    {
        var k = Array.IndexOf(args, "--shots");
        if (k < 0) return;
        _shotsMode = true;
        var dir = k + 1 < args.Length && !args[k + 1].StartsWith("--")
            ? args[k + 1]
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Funky Launcher shots", DateTime.Now.ToString("yyyy-MM-dd_HH-mm"));
        Directory.CreateDirectory(dir);

        // fixed size, off every screen (works the same on any monitor, nothing pops up on Funky's 4K screen)
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
        var startLang = Loc.Lang;
        try
        {
            foreach (var lang in new[] { "ru", "en" })
            {
                Loc.Set(lang);
                var pages = new (string Name, Action Show)[]
                {
                    ("01-home", () => NavHome.IsChecked = true),
                    ("02-library", () => { NavHome.IsChecked = true; NavLibrary.IsChecked = true; }),
                    ("03-soft", () => NavSoft.IsChecked = true),
                    ("04-tools", () => { NavTools.IsChecked = true; _tools.Refresh(); }),
                    ("05-tools-pc", () => { NavTools.IsChecked = true; _tools.ShowPc(); }),
                    ("06-tools-game", () => { NavTools.IsChecked = true; _tools.ShowGame(); }),
                    ("07-tools-screen", () => { NavTools.IsChecked = true; _tools.ShowScreen(); }),
                    ("08-tools-shoot", () => { NavTools.IsChecked = true; _tools.ShowShoot(); }),
                    ("09-settings", () => NavSettings.IsChecked = true),
                };
                foreach (var (name, show) in pages)
                {
                    try
                    {
                        show();
                        await Task.Delay(2500); // news, winget checks and images load asynchronously
                        Save(Path.Combine(dir, $"{lang}-{name}.png"));
                    }
                    catch (Exception e) { File.AppendAllText(Path.Combine(dir, "errors.txt"), $"{lang}-{name}: {e}\n"); }
                }
                File.WriteAllText(Path.Combine(dir, $"{lang}-dialogs.txt"), DialogTexts(), Encoding.UTF8);
            }
        }
        finally
        {
            Loc.Set(startLang);
            _exiting = true;
            Application.Current.Shutdown();
        }
    }

    /// <summary>Texts of the message boxes on the Software page (they can't be drawn into a picture).</summary>
    private static string DialogTexts()
    {
        var sb = new StringBuilder();
        var offered = Catalog.Software.Where(x => x.Winget.Length > 0).ToList();
        sb.AppendLine("=== " + Loc.T("soft.title") + " — consent ===");
        sb.AppendLine(Loc.F("soft.consent", string.Join(", ", offered.Select(x => x.Name))));
        foreach (var s in offered)
        {
            var warning = s.Warning.ToString();
            if (warning.Length > 0) warning = "\n\n⚠ " + warning;
            sb.AppendLine();
            sb.AppendLine("=== " + s.Name + " ===");
            sb.AppendLine(Loc.F("soft.install.confirm", s.Name, warning));
        }
        foreach (var s in Catalog.Software.Where(x => x.Winget.Length == 0))
            sb.AppendLine().AppendLine("=== " + s.Name + " — website only: " + s.Url);
        return sb.ToString();
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
