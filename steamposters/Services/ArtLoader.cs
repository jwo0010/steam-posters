using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SteamPosters.Artwork;
using steamposters.ViewModels;

namespace steamposters.Services;

/// <summary>Fills a game's art slots from the provider and downloads their thumbnails.</summary>
public sealed class ArtLoader(ImageCache images)
{
    /// <summary>How many options to show per art piece.</summary>
    public const int MaxOptions = 12;

    public async Task LoadSlotAsync(IArtworkProvider provider, GameItemViewModel game, ArtSlotViewModel slot, CancellationToken cancellationToken = default)
    {
        if (game.Match is not { } match || slot.IsLoaded || slot.IsLoading) return;
        slot.IsLoading = true;
        try
        {
            var images = await provider.GetArtworkAsync(match.Id, slot.Kind, cancellationToken);
            // The match may have changed while we waited; don't fill the slot with the old game's art.
            if (!ReferenceEquals(game.Match, match)) return;

            slot.Options.Clear();
            foreach (var image in images.Take(MaxOptions)) slot.Options.Add(new ArtOptionViewModel(image));
            slot.Selected ??= slot.Options.FirstOrDefault();
            slot.IsLoaded = true;

            foreach (var option in slot.Options.ToList())
                await LoadThumbnailAsync(option, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            slot.Error = ex.Message;
        }
        finally
        {
            slot.IsLoading = false;
        }
    }

    private async Task LoadThumbnailAsync(ArtOptionViewModel option, CancellationToken cancellationToken)
    {
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
