using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SteamPosters.Artwork;
using steamposters.ViewModels;

namespace steamposters.Services;

/// <summary>Fills a game's art slots from the provider and downloads thumbnails and full images.</summary>
public sealed class ArtLoader(ImageCache images)
{
    /// <summary>How many options to show per art piece.</summary>
    public const int MaxOptions = 12;

    /// <summary>
    /// Fetches the ranked options for one art piece and preselects the best. Only the selected
    /// option's thumbnail is downloaded unless <paramref name="allThumbnails"/> is set (the
    /// Review page loads the rest when the piece is opened).
    /// </summary>
    public async Task LoadSlotAsync(IArtworkProvider provider, GameItemViewModel game, ArtSlotViewModel slot,
        bool allThumbnails, CancellationToken cancellationToken = default)
    {
        if (game.Match is not { } match) return;
        if (!slot.IsLoaded && !slot.IsLoading)
        {
            slot.IsLoading = true;
            try
            {
                var found = await provider.GetArtworkAsync(match.Id, slot.Kind, cancellationToken);
                // The match may have changed while we waited; don't fill the slot with the old game's art.
                if (!ReferenceEquals(game.Match, match)) return;

                slot.Options.Clear();
                foreach (var image in found.Take(MaxOptions)) slot.Options.Add(new ArtOptionViewModel(image));
                slot.Selected ??= slot.Options.FirstOrDefault();
                slot.IsLoaded = true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                slot.Error = ex.Message;
                return;
            }
            finally
            {
                slot.IsLoading = false;
            }
        }

        if (slot.Selected is { } selected) await LoadThumbnailAsync(selected, cancellationToken);
        if (allThumbnails)
        {
            foreach (var option in slot.Options.ToList()) await LoadThumbnailAsync(option, cancellationToken);
        }
    }

    /// <summary>Downloads the full-resolution image for a large preview (and for applying later).</summary>
    public async Task LoadFullImageAsync(ArtOptionViewModel option, CancellationToken cancellationToken = default)
    {
        if (option.FullImagePath is not null || option.IsLoadingFullImage) return;
        option.IsLoadingFullImage = true;
        try
        {
            option.FullImagePath = (await images.GetOrDownloadAsync(option.Image.Url, cancellationToken)).Path;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The thumbnail stays as the preview.
        }
        finally
        {
            option.IsLoadingFullImage = false;
        }
    }

    private async Task LoadThumbnailAsync(ArtOptionViewModel option, CancellationToken cancellationToken)
    {
        if (option.ThumbnailPath is not null) return;
        try
        {
            option.ThumbnailPath = (await images.GetOrDownloadAsync(option.Image.ThumbnailUrl, cancellationToken)).Path;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A missing thumbnail only costs the preview; the option stays selectable.
        }
    }
}
