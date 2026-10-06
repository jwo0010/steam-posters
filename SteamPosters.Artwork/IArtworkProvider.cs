using SteamPosters.Core.Steam;

namespace SteamPosters.Artwork;

/// <summary>A game as known to an artwork provider (e.g. a SteamGridDB game id).</summary>
public sealed record ProviderGame(string Id, string Name, int? ReleaseYear, bool Verified);

/// <summary>One downloadable image offered by a provider.</summary>
public sealed record ArtworkImage(
    string Id,
    ArtworkKind Kind,
    Uri Url,
    Uri ThumbnailUrl,
    int Width,
    int Height,
    string Mime,
    string Style,
    int Score,
    int Upvotes,
    int Downvotes,
    bool Nsfw,
    bool Humor,
    bool Epilepsy,
    string? Author);

/// <summary>A source of game artwork. SteamGridDB first; others can slot in behind this.</summary>
public interface IArtworkProvider
{
    string Name { get; }

    Task<IReadOnlyList<ProviderGame>> SearchGamesAsync(string term, CancellationToken cancellationToken = default);

    /// <summary>Candidate images for one art piece, best first. Empty when the provider has none.</summary>
    Task<IReadOnlyList<ArtworkImage>> GetArtworkAsync(string gameId, ArtworkKind kind, CancellationToken cancellationToken = default);
}
