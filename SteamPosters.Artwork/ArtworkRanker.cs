using SteamPosters.Core.Steam;

namespace SteamPosters.Artwork;

/// <summary>
/// Orders candidate images best first: drops NSFW, humor and epilepsy-warning images, then
/// prefers Steam's recommended size, the preferred style, community score, and upvotes.
/// </summary>
public static class ArtworkRanker
{
    public static IReadOnlyList<ArtworkImage> Rank(IEnumerable<ArtworkImage> images, ArtworkKind kind) =>
        images.Where(i => !i.Nsfw && !i.Humor && !i.Epilepsy)
              .OrderByDescending(i => SizeFit(i, kind))
              .ThenByDescending(i => i.Style == PreferredStyle(kind))
              .ThenByDescending(i => i.Score)
              .ThenByDescending(i => i.Upvotes)
              .ToList();

    /// <summary>"official" for logos and icons, "alternate" for posters and banners, none for heroes.</summary>
    public static string? PreferredStyle(ArtworkKind kind) => kind switch
    {
        ArtworkKind.Logo or ArtworkKind.Icon => "official",
        ArtworkKind.Poster or ArtworkKind.Wide => "alternate",
        _ => null,
    };

    /// <summary>2 = Steam's recommended size, 1 = another accepted size or big enough, 0 = usable.</summary>
    internal static int SizeFit(ArtworkImage image, ArtworkKind kind) => kind switch
    {
        ArtworkKind.Poster => Fit(image, (600, 900), (342, 482), (660, 930)),
        ArtworkKind.Wide => Fit(image, (920, 430), (460, 215)),
        ArtworkKind.Hero => image.Width >= 3840 ? 2 : image.Width >= 1920 ? 1 : 0,
        ArtworkKind.Icon => image.Width == image.Height ? (image.Width >= 256 ? 2 : 1) : 0,
        _ => 0,
    };

    private static int Fit(ArtworkImage image, (int W, int H) recommended, params (int W, int H)[] accepted)
    {
        var size = (image.Width, image.Height);
        if (size == recommended) return 2;
        return accepted.Contains(size) ? 1 : 0;
    }
}
