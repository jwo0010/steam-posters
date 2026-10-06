using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using SteamPosters.Core.IO;

namespace SteamPosters.Artwork;

public enum ImageFormat { Png, Jpeg, WebP, Ico }

/// <summary>A downloaded image on disk.</summary>
public sealed record CachedImage(string Path, ImageFormat Format)
{
    /// <summary>
    /// Steam's grid folder reads only .png and .jpg, so WebP must be converted before it is applied.
    /// (ICO is fine for the shortcut icon field.)
    /// </summary>
    public bool NeedsConversion => Format == ImageFormat.WebP;
}

/// <summary>
/// On-disk cache of downloaded artwork, keyed by a SHA-256 of the URL. Downloads never carry the
/// provider's API key, are size-capped, and must really be PNG, JPEG, WebP or ICO.
/// </summary>
public sealed class ImageCache
{
    public const long DefaultMaxImageBytes = 20 * 1024 * 1024;

    private static readonly (ImageFormat Format, string Extension)[] Formats =
        [(ImageFormat.Png, ".png"), (ImageFormat.Jpeg, ".jpg"), (ImageFormat.WebP, ".webp"), (ImageFormat.Ico, ".ico")];

    private readonly HttpClient _http;
    private readonly ConcurrentDictionary<string, Lazy<Task<CachedImage>>> _inFlight = new();

    public ImageCache(HttpClient http, string? folder = null, long maxImageBytes = DefaultMaxImageBytes)
    {
        _http = http;
        Folder = folder ?? DefaultFolder;
        MaxImageBytes = maxImageBytes;
    }

    /// <summary>%LOCALAPPDATA%\SteamPosters\cache (local, so cached images don't roam with the profile).</summary>
    public static string DefaultFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SteamPosters", "cache");

    public string Folder { get; }

    public long MaxImageBytes { get; }

    /// <summary>The cached image for this URL, or null if it hasn't been downloaded.</summary>
    public CachedImage? TryGet(Uri url)
    {
        var key = Key(url);
        foreach (var (format, extension) in Formats)
        {
            var path = Path.Combine(Folder, key + extension);
            if (File.Exists(path)) return new CachedImage(path, format);
        }
        return null;
    }

    /// <summary>Returns the cached image, downloading it first if needed. Concurrent calls share one download.</summary>
    public async Task<CachedImage> GetOrDownloadAsync(Uri url, CancellationToken cancellationToken = default)
    {
        if (TryGet(url) is { } cached) return cached;

        var key = Key(url);
        var download = _inFlight.GetOrAdd(key, _ => new Lazy<Task<CachedImage>>(() => DownloadAsync(url, key, cancellationToken)));
        try
        {
            return await download.Value;
        }
        finally
        {
            _inFlight.TryRemove(new KeyValuePair<string, Lazy<Task<CachedImage>>>(key, download));
        }
    }

    public static ImageFormat? DetectFormat(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])) return ImageFormat.Png;
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF])) return ImageFormat.Jpeg;
        if (bytes.Length >= 12 && bytes.StartsWith("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8)) return ImageFormat.WebP;
        if (bytes.Length >= 6 && bytes.StartsWith((ReadOnlySpan<byte>)[0x00, 0x00, 0x01, 0x00]) && (bytes[4] | bytes[5]) != 0) return ImageFormat.Ico;
        return null;
    }

    private async Task<CachedImage> DownloadAsync(Uri url, string key, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaxImageBytes) throw TooLarge(url);

        var bytes = await ReadCappedAsync(response.Content, url, cancellationToken);
        var format = DetectFormat(bytes)
            ?? throw new InvalidDataException($"Downloaded file from {url} is not a PNG, JPEG, WebP or ICO image.");

        Directory.CreateDirectory(Folder);
        var path = Path.Combine(Folder, key + Formats.Single(f => f.Format == format).Extension);
        SafeFileWriter.Write(path, bytes);
        return new CachedImage(path, format);
    }

    private async Task<byte[]> ReadCappedAsync(HttpContent content, Uri url, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxImageBytes) throw TooLarge(url);
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    private InvalidDataException TooLarge(Uri url) =>
        new($"Image at {url} is larger than {MaxImageBytes / (1024 * 1024)} MB.");

    private static string Key(Uri url) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(url.AbsoluteUri)));
}
