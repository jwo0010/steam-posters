using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SteamPosters.Artwork.SteamGridDb;
using SteamPosters.Core.Steam;
using SteamPosters.Matching;
using steamposters.Services;

namespace steamposters.ViewModels;

/// <summary>Step 2: read the non-Steam games and match each one, showing progress.</summary>
public sealed partial class MatchViewModel(AppServices services, WizardSession session) : StepViewModel
{
    private readonly ArtLoader _artLoader = new(services.Images);
    private CancellationTokenSource? _cts;

    public override string Title => "Find games";

    public override bool CanGoNext => !IsBusy && session.Games.Count > 0;

    public System.Collections.ObjectModel.ObservableCollection<GameItemViewModel> Games => session.Games;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private int _progress;

    [ObservableProperty]
    private int _total;

    [ObservableProperty]
    private string? _status;

    partial void OnIsBusyChanged(bool value)
    {
        RaiseCanGoNextChanged();
        CancelCommand.NotifyCanExecuteChanged();
        RematchCommand.NotifyCanExecuteChanged();
    }

    public override async Task OnEnterAsync()
    {
        if (session.Account is not { } account || session.LoadedAccountId == account.AccountId) return;
        if (!LoadGames(account)) return;
        session.LoadedAccountId = account.AccountId;
        await RunMatchingAsync();
    }

    private bool LoadGames(SteamAccount account)
    {
        session.Games.Clear();
        RaiseCanGoNextChanged();
        if (!File.Exists(account.ShortcutsPath))
        {
            Status = "This account has no non-Steam games yet. Add some in Steam (Games > Add a Non-Steam Game) and come back.";
            return false;
        }
        try
        {
            var file = ShortcutsFile.Load(account.ShortcutsPath);
            var grid = new GridArtwork(account.GridPath);
            foreach (var shortcut in file.Shortcuts) session.Games.Add(new GameItemViewModel(shortcut, grid));
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Status = $"Couldn't read Steam's list of non-Steam games: {ex.Message}";
            return false;
        }
    }

    private bool CanRematch() => !IsBusy && session.Games.Count > 0;

    [RelayCommand(CanExecute = nameof(CanRematch))]
    private Task RematchAsync() => RunMatchingAsync();

    private bool CanCancel() => IsBusy;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => _cts?.Cancel();

    private async Task RunMatchingAsync()
    {
        if (session.Provider is not { } provider) return;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        var matcher = new MatchService(provider);
        IsBusy = true;
        Total = session.Games.Count;
        Progress = 0;
        try
        {
            foreach (var game in session.Games.ToList())
            {
                token.ThrowIfCancellationRequested();
                Status = $"Matching {game.CurrentName} ({Progress + 1} of {Total})...";
                game.IsMatching = true;
                try
                {
                    game.ApplyResult(await matcher.MatchAsync(game.CurrentName, game.Exe, game.LaunchOptions, token));
                    if (game.Match is not null) await _artLoader.LoadSlotAsync(provider, game, game.Poster, token);
                }
                finally
                {
                    game.IsMatching = false;
                }
                Progress++;
            }
            var matched = session.Games.Count(g => g.Confidence == MatchConfidence.Matched);
            var check = session.Games.Count(g => g.Confidence == MatchConfidence.CheckThis);
            Status = $"Done: {matched} matched, {check} to check, {Total - matched - check} not found.";
        }
        catch (OperationCanceledException)
        {
            Status = $"Stopped after {Progress} of {Total}. Unmatched games can be searched by hand on the next page.";
        }
        catch (SteamGridDbAuthException ex)
        {
            Status = ex.Message + " Go back and enter it again.";
        }
        catch (SteamGridDbException ex)
        {
            Status = $"SteamGridDB problem: {ex.Message} You can still continue and search by hand.";
        }
        finally
        {
            IsBusy = false;
            _cts.Dispose();
            _cts = null;
        }
    }
}
