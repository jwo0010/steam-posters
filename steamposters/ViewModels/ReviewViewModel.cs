using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SteamPosters.Artwork;
using steamposters.Services;

namespace steamposters.ViewModels;

/// <summary>
/// Step 3: review every game. Untick ones to leave alone, fix names, swap art, and fix a wrong
/// or missing match by searching SteamGridDB by hand.
/// </summary>
public sealed partial class ReviewViewModel : StepViewModel
{
    private readonly WizardSession _session;
    private readonly ArtLoader _artLoader;

    public ReviewViewModel(AppServices services, WizardSession session)
    {
        _session = session;
        _artLoader = new ArtLoader(services.Images);
        session.Games.CollectionChanged += (_, e) =>
        {
            foreach (GameItemViewModel g in e.NewItems ?? Array.Empty<GameItemViewModel>()) g.PropertyChanged += OnGamePropertyChanged;
            RefreshSummary();
        };
    }

    public override string Title => "Review";

    public override bool CanGoNext => _session.Games.Any(g => g.Include);

    public ObservableCollection<GameItemViewModel> Games => _session.Games;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private GameItemViewModel? _selectedGame;

    [ObservableProperty]
    private ArtSlotViewModel? _selectedSlot;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    private string _searchText = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    private bool _isSearching;

    [ObservableProperty]
    private string? _searchStatus;

    [ObservableProperty]
    private string? _summary;

    public bool HasSelection => SelectedGame is not null;

    /// <summary>Search results, or the automatic match's alternatives until the user searches.</summary>
    public ObservableCollection<ProviderGame> SearchResults { get; } = new();

    public override Task OnEnterAsync()
    {
        RefreshSummary();
        SelectedGame ??= Games.FirstOrDefault(g => g.NeedsAttention && !g.IsEmulatorOrTool) ?? Games.FirstOrDefault();
        return Task.CompletedTask;
    }

    partial void OnSelectedGameChanged(GameItemViewModel? value)
    {
        SearchText = value?.Match?.Name ?? value?.CurrentName ?? "";
        SearchStatus = null;
        ShowAlternatives();
        SelectedSlot = value?.Poster;
    }

    partial void OnSelectedSlotChanged(ArtSlotViewModel? value) => _ = LoadSelectedSlotAsync();

    private async Task LoadSelectedSlotAsync()
    {
        if (SelectedGame is { } game && SelectedSlot is { } slot && _session.Provider is { } provider)
            await _artLoader.LoadSlotAsync(provider, game, slot);
    }

    private bool CanSearch() => !IsSearching && !string.IsNullOrWhiteSpace(SearchText) && _session.Provider is not null;

    /// <summary>Manual fix: search SteamGridDB for whatever the user typed.</summary>
    [RelayCommand(CanExecute = nameof(CanSearch))]
    private async Task SearchAsync()
    {
        IsSearching = true;
        SearchStatus = "Searching...";
        try
        {
            var results = await _session.Provider!.SearchGamesAsync(SearchText.Trim());
            SearchResults.Clear();
            foreach (var game in results) SearchResults.Add(game);
            SearchStatus = results.Count == 0 ? "Nothing found. Try the game's full name or fewer words." : null;
        }
        catch (Exception ex)
        {
            SearchStatus = $"Search failed: {ex.Message}";
        }
        finally
        {
            IsSearching = false;
        }
    }

    /// <summary>Use a search result (or alternative) as this game's match.</summary>
    [RelayCommand]
    private async Task PickAsync(ProviderGame? game)
    {
        if (SelectedGame is not { } selected || game is null) return;
        selected.PickManually(game);
        ShowAlternatives();
        SelectedSlot = selected.Poster;
        await LoadSelectedSlotAsync();
    }

    [RelayCommand]
    private void SelectOption(ArtOptionViewModel? option)
    {
        if (SelectedSlot is { } slot && option is not null) slot.Selected = option;
    }

    /// <summary>Keep whatever Steam has for this piece (don't replace it).</summary>
    [RelayCommand]
    private void KeepCurrent()
    {
        if (SelectedSlot is { } slot) slot.Selected = null;
    }

    private void ShowAlternatives()
    {
        SearchResults.Clear();
        foreach (var game in SelectedGame?.Alternatives ?? Enumerable.Empty<ProviderGame>()) SearchResults.Add(game);
    }

    private void OnGamePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GameItemViewModel.Include) or nameof(GameItemViewModel.Confidence) or nameof(GameItemViewModel.IsManual))
            RefreshSummary();
    }

    private void RefreshSummary()
    {
        var included = Games.Count(g => g.Include);
        var toCheck = Games.Count(g => g.Include && g.NeedsAttention);
        Summary = $"{included} of {Games.Count} games selected" + (toCheck > 0 ? $", {toCheck} to check" : "");
        RaiseCanGoNextChanged();
    }
}
