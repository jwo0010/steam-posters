using SteamPosters.Core.IO;

namespace SteamPosters.Core.Steam;

public enum ArtworkKind
{
    /// <summary>Portrait capsule, 600x900: {id}p</summary>
    Poster,
    /// <summary>Horizontal capsule, 920x430: {id}</summary>
    Wide,
    /// <summary>Background banner, 3840x1240: {id}_hero</summary>
    Hero,
    /// <summary>Transparent logo: {id}_logo</summary>
    Logo,
    /// <summary>Icon: {id}_icon</summary>
    Icon,
}

/// <summary>Artwork files in a Steam account's config\grid folder.</summary>
public sealed class GridArtwork
{
    public static readonly IReadOnlyList<string> Extensions = [".png", ".jpg", ".jpeg"];

    public GridArtwork(string gridFolder) => GridFolder = gridFolder;

    public string GridFolder { get; }

    public static string BaseName(uint appId, ArtworkKind kind) => kind switch
    {
        ArtworkKind.Poster => $"{appId}p",
        ArtworkKind.Wide => $"{appId}",
        ArtworkKind.Hero => $"{appId}_hero",
        ArtworkKind.Logo => $"{appId}_logo",
        ArtworkKind.Icon => $"{appId}_icon",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public string PathFor(uint appId, ArtworkKind kind, string extension) =>
        Path.Combine(GridFolder, BaseName(appId, kind) + NormalizeExtension(extension));

    /// <summary>Existing files for this art piece, in .png/.jpg/.jpeg order.</summary>
    public IReadOnlyList<string> FindExisting(uint appId, ArtworkKind kind) =>
        Extensions.Select(ext => PathFor(appId, kind, ext)).Where(File.Exists).ToList();

    /// <summary>Paths a write of this art piece would create or remove (for backups).</summary>
    public IReadOnlyList<string> PathsAffectedByWrite(uint appId, ArtworkKind kind, ReadOnlySpan<byte> image) =>
        FindExisting(appId, kind).Append(PathFor(appId, kind, DetectExtension(image))).Distinct().ToList();

    /// <summary>
    /// Writes an image (PNG or JPEG, detected from its bytes) and removes the other-extension
    /// variants of the same piece so Steam can't pick up a stale one. Returns the written path.
    /// </summary>
    public string Write(uint appId, ArtworkKind kind, byte[] image)
    {
        var path = PathFor(appId, kind, DetectExtension(image));
        Directory.CreateDirectory(GridFolder);
        SafeFileWriter.Write(path, image);
        foreach (var other in FindExisting(appId, kind))
        {
            if (!string.Equals(other, path, StringComparison.OrdinalIgnoreCase)) File.Delete(other);
        }
        return path;
    }

    public static string DetectExtension(ReadOnlySpan<byte> image)
    {
        if (image.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])) return ".png";
        if (image.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF])) return ".jpg";
        throw new InvalidDataException("Image is neither PNG nor JPEG.");
    }

    private static string NormalizeExtension(string extension)
    {
        var ext = extension.StartsWith('.') ? extension.ToLowerInvariant() : "." + extension.ToLowerInvariant();
        if (!Extensions.Contains(ext)) throw new ArgumentException($"Unsupported artwork extension '{extension}'.");
        return ext;
    }
}
