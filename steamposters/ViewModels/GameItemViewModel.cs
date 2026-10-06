using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using SteamPosters.Artwork;
using SteamPosters.Core.Steam;
using SteamPosters.Matching;

namespace steamposters.ViewModels;

/// <summary>One non-Steam game as shown in the wizard: its current state, its match and the chosen art.</summary>
public sealed partial class GameItemViewModel : ObservableObject
{
    public GameItemViewModel(Shortcut shortcut, GridArtwork grid)
    {
        ShortcutIndex = shortcut.Index;
        AppId = shortcut.AppId;
        CurrentName = shortcut.AppName;
        Exe = shortcut.Exe.Trim('"');
        LaunchOptions = shortcut.LaunchOptions;
        _newName = shortcut.AppName;
        Slots = Enum.GetValues<ArtworkKind>()
            .Select(kind => new ArtSlotViewModel(kind, grid.FindExisting(AppId, kind).FirstOrDefault()))
            .ToList();
        Poster.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ArtSlotViewModel.PreviewPath)) OnPropertyChanged(nameof(PosterPreviewPath));
        };
    }

    public string ShortcutIndex { get; }

    public uint AppId { get; }

    public string CurrentName { get; }

    public string Exe { get; }

    public string LaunchOptions { get; }

    public IReadOnlyList<ArtSlotViewModel> Slots { get; }

    public ArtSlotViewModel Poster => Slot(ArtworkKind.Poster);

    /// <summary>The chosen poster's thumbnail, or the poster Steam already has.</summary>
    public string? PosterPreviewPath => Poster.PreviewPath;

    [ObservableProperty]
    private string _newName;

    [ObservableProperty]
    private bool _include;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConfidenceText), nameof(NeedsAttention))]
    private MatchConfidence _confidence = MatchConfidence.NotFound;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConfidenceText))]
    private bool _isManual;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MatchText))]
    private ProviderGame? _match;

    [ObservableProperty]
    private bool _isMatching;

    [ObservableProperty]
    private string? _reason;

    public bool IsEmulatorOrTool { get; private set; }

    public ObservableCollection<ProviderGame> Alternatives { get; } = new();

    public string ConfidenceText => IsManual ? "Picked by you" : Confidence switch
    {
        MatchConfidence.NotFound when IsEmulatorOrTool => "Emulator",
        MatchConfidence.Matched => "Matched",
        MatchConfidence.CheckThis => "Check this",
        _ => "Not found",
    };

    public bool NeedsAttention => !IsManual && Confidence != MatchConfidence.Matched;

    public string MatchText => Match is { } g
        ? g.ReleaseYear is { } y ? $"{g.Name} ({y})" : g.Name
        : Reason is { } r ? $"No match: {r}" : "No match yet";

    public ArtSlotViewModel Slot(ArtworkKind kind) => Slots.First(s => s.Kind == kind);

    /// <summary>Takes the automatic match result. Not-found entries (and emulators) start unticked.</summary>
    public void ApplyResult(MatchResult result)
    {
        IsEmulatorOrTool = result.IsEmulatorOrTool;
        OnPropertyChanged(nameof(ConfidenceText));
        Reason = result.Reason;
        Alternatives.Clear();
        foreach (var alternative in result.Alternatives) Alternatives.Add(alternative.Game);
        SetMatch(result.Game, result.Confidence, manual: false);
        if (result.SuggestedName is { } name) NewName = name;
        Include = result.Confidence != MatchConfidence.NotFound;
    }

    /// <summary>The user picked a game by hand (manual fix): it counts as confirmed.</summary>
    public void PickManually(ProviderGame game)
    {
        if (Match is { } previous && previous != game && !Alternatives.Contains(previous)) Alternatives.Insert(0, previous);
        Alternatives.Remove(game);
        SetMatch(game, MatchConfidence.Matched, manual: true);
        NewName = game.Name;
        Include = true;
    }

    private void SetMatch(ProviderGame? game, MatchConfidence confidence, bool manual)
    {
        Match = game;
        Confidence = confidence;
        IsManual = manual;
        foreach (var slot in Slots) slot.Reset();
        OnPropertyChanged(nameof(MatchText));
    }
}

/// <summary>One art piece (poster, banner, hero, logo, icon) of a game and its candidate images.</summary>
public sealed partial class ArtSlotViewModel(ArtworkKind kind, string? existingPath) : ObservableObject
{
    public ArtworkKind Kind { get; } = kind;

    public string Title => Kind switch
    {
        ArtworkKind.Poster => "Poster",
        ArtworkKind.Wide => "Wide banner",
        ArtworkKind.Hero => "Hero",
        ArtworkKind.Logo => "Logo",
        ArtworkKind.Icon => "Icon",
        _ => Kind.ToString(),
    };

    /// <summary>The file Steam already uses for this piece, if any.</summary>
    public string? ExistingPath { get; } = existingPath;

    public ObservableCollection<ArtOptionViewModel> Options { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewPath), nameof(LargePreviewPath))]
    private ArtOptionViewModel? _selected;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isLoaded;

    /// <summary>Whether this piece is the one open in the Review picker.</summary>
    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private string? _error;

    public string? PreviewPath => Selected?.ThumbnailPath ?? ExistingPath;

    /// <summary>Best available image for the big preview: full resolution, then thumbnail, then Steam's current file.</summary>
    public string? LargePreviewPath => Selected?.FullImagePath ?? PreviewPath;

    partial void OnSelectedChanged(ArtOptionViewModel? oldValue, ArtOptionViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.IsSelected = false;
            oldValue.PropertyChanged -= OnSelectedThumbnailChanged;
        }
        if (newValue is not null)
        {
            newValue.IsSelected = true;
            newValue.PropertyChanged += OnSelectedThumbnailChanged;
        }
    }

    private void OnSelectedThumbnailChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ArtOptionViewModel.ThumbnailPath) or nameof(ArtOptionViewModel.FullImagePath))
        {
            OnPropertyChanged(nameof(PreviewPath));
            OnPropertyChanged(nameof(LargePreviewPath));
        }
    }

    public void Reset()
    {
        Selected = null;
        Options.Clear();
        IsLoaded = false;
        Error = null;
    }
}

/// <summary>One candidate image.</summary>
public sealed partial class ArtOptionViewModel(ArtworkImage image) : ObservableObject
{
    public ArtworkImage Image { get; } = image;

    public string Description => $"{Image.Width}x{Image.Height} · {Image.Style} · score {Image.Score}" +
                                 (Image.Author is { } a ? $" · by {a}" : "");

    [ObservableProperty]
    private string? _thumbnailPath;

    /// <summary>The full-resolution download, once someone has looked at it.</summary>
    [ObservableProperty]
    private string? _fullImagePath;

    [ObservableProperty]
    private bool _isLoadingFullImage;

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Tile size in the picker, shaped like the art piece (about the thumbnail's own size).</summary>
    public double TileWidth => Image.Kind switch
    {
        ArtworkKind.Poster => 180,
        ArtworkKind.Wide => 300,
        ArtworkKind.Hero => 390,
        ArtworkKind.Logo => 280,
        _ => 128,
    };

    public double TileHeight => Image.Kind switch
    {
        ArtworkKind.Poster => 270,
        ArtworkKind.Wide => 140,
        ArtworkKind.Hero => 126,
        ArtworkKind.Logo => 110,
        _ => 128,
    };
}
