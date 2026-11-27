// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FunkyLauncher.Core;

public static class Images
{
    /// <summary>"res:/Assets/x.jpg" = image built into the launcher; https://… = image from the internet.</summary>
    public static ImageSource? Load(string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return null;
        try
        {
            if (source.StartsWith("res:", StringComparison.OrdinalIgnoreCase))
                return new BitmapImage(new Uri("pack://application:,,," + source[4..]));
            if (source.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                var img = new BitmapImage();
                img.BeginInit();
                img.UriSource = new Uri(source);
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.EndInit();
                return img;
            }
        }
        catch
        {
            // a broken image just shows the plain background
        }
        return null;
    }
}
