// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FunkyLauncher.Views;

/// <summary>
/// 0.1.22 (Design 24.11): colours that follow the accent theme instead of the built-in pink. The logo and the F mark
/// stay pink always. With the launcher's own look (Neon) everything looks exactly as before.
/// </summary>
public static class ThemeColors
{
    public static readonly Color DefaultPink = Color.FromRgb(0xFF, 0x3F, 0xD2);
    public static readonly Color DarkText = Color.FromRgb(0x07, 0x07, 0x0D);

    public static Color Pink => Get("Pink", DefaultPink);
    public static Color Cyan => Get("Cyan", Color.FromRgb(0x7A, 0xE8, 0xFF));
    public static bool IsDefault => Pink == DefaultPink;

    public static SolidColorBrush Brush(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    public static Color Mix(Color a, Color b, double t) => Color.FromArgb(
        (byte)(a.A + (b.A - a.A) * t), (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    public static Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

    /// <summary>Text on an accent-coloured button: dark on light accents (Sunset orange, Wasteland yellow), white otherwise.</summary>
    public static Color OnAccent(Color bg)
    {
        static double Lin(byte v) { var c = v / 255.0; return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4); }
        var l = 0.2126 * Lin(bg.R) + 0.7152 * Lin(bg.G) + 0.0722 * Lin(bg.B);
        return l >= 0.36 ? DarkText : Colors.White;
    }

    private static Color Get(string key, Color fallback) =>
        Application.Current?.TryFindResource(key) is SolidColorBrush b ? b.Color : fallback;

    private static (Color Pink, BitmapSource Image)? _tinted;

    /// <summary>The home header picture with its pink stripes turned to the theme colour (cached per colour).</summary>
    public static BitmapSource Tint(BitmapSource src, Color to)
    {
        if (_tinted is { } t && t.Pink == to) return t.Image;
        var conv = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
        int w = conv.PixelWidth, h = conv.PixelHeight, stride = w * 4;
        var px = new byte[stride * h];
        conv.CopyPixels(px, stride, 0);
        var (toH, _, _) = Hsv(to);
        var fromH = Hsv(DefaultPink).H;
        for (var i = 0; i < px.Length; i += 4)
        {
            var (hh, s, v) = Hsv(Color.FromRgb(px[i + 2], px[i + 1], px[i]));
            if (s < 0.25 || v < 0.12) continue;
            var d = Math.Abs(hh - fromH); d = Math.Min(d, 360 - d);
            if (d > 50) continue;
            var c = FromHsv((toH + (hh - fromH) + 360) % 360, s, v);
            px[i] = c.B; px[i + 1] = c.G; px[i + 2] = c.R;
        }
        var res = BitmapSource.Create(w, h, conv.DpiX, conv.DpiY, PixelFormats.Bgra32, null, px, stride);
        res.Freeze();
        _tinted = (to, res);
        return res;
    }

    private static (double H, double S, double V) Hsv(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        double hh = d == 0 ? 0 : max == r ? 60 * (((g - b) / d) % 6) : max == g ? 60 * ((b - r) / d + 2) : 60 * ((r - g) / d + 4);
        if (hh < 0) hh += 360;
        return (hh, max == 0 ? 0 : d / max, max);
    }

    private static Color FromHsv(double hh, double s, double v)
    {
        double c = v * s, x = c * (1 - Math.Abs(hh / 60 % 2 - 1)), m = v - c;
        var (r, g, b) = hh < 60 ? (c, x, 0d) : hh < 120 ? (x, c, 0d) : hh < 180 ? (0d, c, x) : hh < 240 ? (0d, x, c) : hh < 300 ? (x, 0d, c) : (c, 0d, x);
        return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }
}

/// <summary>Button background → text colour (dark on light accents). Used by the PrimaryButton template.</summary>
public sealed class OnAccentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        ThemeColors.Brush(value is SolidColorBrush b ? ThemeColors.OnAccent(b.Color) : Colors.White);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
