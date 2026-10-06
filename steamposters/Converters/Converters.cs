using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SteamPosters.Matching;

namespace steamposters.Converters;

/// <summary>
/// Loads an image file into a Bitmap (cached per path); null for missing or unreadable files.
/// A numeric ConverterParameter decodes at most that many pixels wide, so 4K art doesn't
/// sit in memory at full size just to be previewed.
/// </summary>
public sealed class PathToBitmapConverter : IValueConverter
{
    private readonly Dictionary<string, Bitmap?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || path.Length == 0) return null;
        var maxWidth = int.TryParse(parameter?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var w) ? w : 0;
        var key = maxWidth > 0 ? $"{path}|{maxWidth}" : path;
        if (_cache.TryGetValue(key, out var cached)) return cached;
        Bitmap? bitmap = null;
        try
        {
            if (File.Exists(path))
            {
                bitmap = new Bitmap(path);
                if (maxWidth > 0 && bitmap.PixelSize.Width > maxWidth)
                {
                    bitmap.Dispose();
                    using var stream = File.OpenRead(path);
                    bitmap = Bitmap.DecodeToWidth(stream, maxWidth, BitmapInterpolationMode.HighQuality);
                }
            }
        }
        catch (Exception)
        {
            // Unreadable or unsupported image: show nothing.
        }
        _cache[key] = bitmap;
        return bitmap;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Badge colour for a match confidence (manual picks count as matched).</summary>
public sealed class ConfidenceToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        MatchConfidence.Matched => new SolidColorBrush(Color.Parse("#2E7D32")),
        MatchConfidence.CheckThis => new SolidColorBrush(Color.Parse("#B26A00")),
        _ => new SolidColorBrush(Color.Parse("#8A8A8A")),
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
