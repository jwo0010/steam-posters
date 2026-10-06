using System.Collections.Generic;
using System.Linq;
using SteamPosters.Artwork;
using SteamPosters.Core.Steam;
using steamposters.ViewModels;

namespace steamposters.Services;

/// <summary>One game's approved changes. Step 5 (apply) writes these into Steam.</summary>
public sealed record GameChange(
    string ShortcutIndex,
    uint AppId,
    string OldName,
    string? NewName,
    IReadOnlyDictionary<ArtworkKind, ArtworkImage> Artwork);

/// <summary>Everything the user approved in the wizard, for one Steam account.</summary>
public sealed record ApplyPlan(SteamAccount Account, IReadOnlyList<GameChange> Changes)
{
    public bool IsEmpty => Changes.Count == 0;

    public static ApplyPlan From(SteamAccount account, IEnumerable<GameItemViewModel> games)
    {
        var changes = new List<GameChange>();
        foreach (var game in games.Where(g => g.Include))
        {
            var newName = string.IsNullOrWhiteSpace(game.NewName) || game.NewName.Trim() == game.CurrentName
                ? null
                : game.NewName.Trim();
            var art = game.Slots
                .Where(s => s.Selected is not null)
                .ToDictionary(s => s.Kind, s => s.Selected!.Image);
            if (newName is not null || art.Count > 0)
                changes.Add(new GameChange(game.ShortcutIndex, game.AppId, game.CurrentName, newName, art));
        }
        return new ApplyPlan(account, changes);
    }
}
