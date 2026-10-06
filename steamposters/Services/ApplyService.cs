using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SteamPosters.Artwork;
using SteamPosters.Core.IO;
using SteamPosters.Core.Steam;

namespace steamposters.Services;

/// <summary>An image ready to write: already PNG or JPEG.</summary>
public sealed record PreparedImage(uint AppId, ArtworkKind Kind, byte[] Bytes);

public sealed record ApplyOutcome(BackupSet Backup, int Renamed, int ImagesWritten, IReadOnlyList<string> Warnings);

/// <summary>The apply failed and every file was put back from the backup taken just before.</summary>
public sealed class ApplyFailedException(string message, Exception inner) : Exception(message, inner);

/// <summary>
/// Writes an <see cref="ApplyPlan"/> into Steam: back up first, rename games in shortcuts.vdf
/// (keeping each game's appid), write the art files, and roll everything back on any error.
/// Steam must be closed while <see cref="ApplyAsync"/> runs.
/// </summary>
public sealed class ApplyService(ImageCache images, BackupService backups)
{
    /// <summary>
    /// Downloads every chosen image at full resolution and converts it for Steam. Done before
    /// Steam is closed, so Steam is down only for the quick file writes.
    /// </summary>
    public async Task<IReadOnlyList<PreparedImage>> PrepareAsync(ApplyPlan plan, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var prepared = new List<PreparedImage>();
        var total = plan.Changes.Sum(c => c.Artwork.Count);
        foreach (var change in plan.Changes)
        {
            foreach (var (kind, image) in change.Artwork)
            {
                progress?.Report($"Downloading {prepared.Count + 1} of {total}: {change.NewName ?? change.OldName} {kind.ToString().ToLowerInvariant()}");
                var cached = await images.GetOrDownloadAsync(image.Url, cancellationToken);
                var bytes = await File.ReadAllBytesAsync(cached.Path, cancellationToken);
                // Icons are referenced from shortcuts.vdf; PNG is the safest format there.
                bytes = kind == ArtworkKind.Icon ? SteamImageConverter.ToPng(bytes) : SteamImageConverter.ToPngOrJpeg(bytes);
                prepared.Add(new PreparedImage(change.AppId, kind, bytes));
            }
        }
        return prepared;
    }

    /// <summary>Backs up, then writes names and art. On any failure, restores the backup and throws <see cref="ApplyFailedException"/>.</summary>
    public ApplyOutcome Apply(ApplyPlan plan, IReadOnlyList<PreparedImage> prepared, IProgress<string>? progress = null)
    {
        var account = plan.Account;
        var grid = new GridArtwork(account.GridPath);
        var renames = plan.Changes.Where(c => c.NewName is not null).ToList();
        var icons = prepared.Where(p => p.Kind == ArtworkKind.Icon).ToList();
        var touchesShortcuts = renames.Count > 0 || icons.Count > 0;

        var affected = new List<string>();
        if (touchesShortcuts) affected.Add(account.ShortcutsPath);
        foreach (var image in prepared) affected.AddRange(grid.PathsAffectedByWrite(image.AppId, image.Kind, image.Bytes));

        progress?.Report("Backing up...");
        var backup = backups.Create(affected, $"Steam Posters: {plan.Changes.Count} games");
        var warnings = new List<string>();
        try
        {
            var written = new Dictionary<(uint, ArtworkKind), string>();
            foreach (var image in prepared)
            {
                progress?.Report($"Writing {image.Kind.ToString().ToLowerInvariant()} for {image.AppId}...");
                written[(image.AppId, image.Kind)] = grid.Write(image.AppId, image.Kind, image.Bytes);
            }

            var renamed = 0;
            if (touchesShortcuts)
            {
                progress?.Report("Updating Steam's game list...");
                // Read the file only now: Steam rewrites it when it exits.
                var file = ShortcutsFile.Load(account.ShortcutsPath);
                foreach (var change in plan.Changes)
                {
                    var shortcut = Find(file, change);
                    if (shortcut is null)
                    {
                        warnings.Add($"{change.OldName} is no longer in Steam's list; its name was left alone.");
                        continue;
                    }
                    // Pin the id before renaming, so an old entry without a stored appid keeps its art.
                    if (shortcut.StoredAppId is null) shortcut.Fields.SetUInt32("appid", shortcut.ComputedAppId);
                    if (change.NewName is { } newName && shortcut.AppName != newName)
                    {
                        shortcut.AppName = newName;
                        renamed++;
                    }
                    if (written.TryGetValue((change.AppId, ArtworkKind.Icon), out var iconPath)) shortcut.Icon = iconPath;
                }
                file.Save(account.ShortcutsPath);
            }
            return new ApplyOutcome(backup, renamed, prepared.Count, warnings);
        }
        catch (Exception ex)
        {
            backups.Restore(backup);
            throw new ApplyFailedException($"Applying failed, so everything was put back as it was: {ex.Message}", ex);
        }
    }

    /// <summary>The shortcut for a change: same index and id if Steam kept the order, otherwise by id.</summary>
    private static Shortcut? Find(ShortcutsFile file, GameChange change) =>
        file.Shortcuts.FirstOrDefault(s => s.Index == change.ShortcutIndex && s.AppId == change.AppId)
        ?? file.Shortcuts.FirstOrDefault(s => s.AppId == change.AppId);
}
