using System.IO;
using SkiaSharp;
using SteamPosters.Core.Steam;

namespace steamposters.Services;

/// <summary>
/// Steam's grid folder only reads PNG and JPEG. Anything else (WebP from SteamGridDB, ICO icons)
/// is decoded with SkiaSharp, which the app already ships through Avalonia, and saved as PNG.
/// </summary>
public static class SteamImageConverter
{
    public static byte[] ToPngOrJpeg(byte[] image)
    {
        try
        {
            GridArtwork.DetectExtension(image);
            return image;                                   // already PNG or JPEG
        }
        catch (InvalidDataException)
        {
            return ToPng(image);
        }
    }

    /// <summary>Re-encodes any image SkiaSharp can read (PNG, JPEG, WebP, ICO, ...) as PNG.</summary>
    public static byte[] ToPng(byte[] image)
    {
        using var data = SKData.CreateCopy(image);
        using var codec = SKCodec.Create(data)
            ?? throw new InvalidDataException("The image could not be read (unsupported or damaged file).");
        using var bitmap = SKBitmap.Decode(codec)
            ?? throw new InvalidDataException("The image could not be read (unsupported or damaged file).");
        using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidDataException("The image could not be converted to PNG.");
        return encoded.ToArray();
    }
}
