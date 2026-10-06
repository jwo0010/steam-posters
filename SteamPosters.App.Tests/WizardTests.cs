using SteamPosters.Artwork;
using SteamPosters.Core.Steam;
using SteamPosters.Matching;
using steamposters.Services;
using steamposters.ViewModels;

namespace SteamPosters.App.Tests;

public class WizardTests
{
    private static readonly ProviderGame Avowed = new("1", "Avowed", 2025, true);
    private static readonly ProviderGame Totk = new("2", "The Legend of Zelda: Tears of the Kingdom", 2023, true);
    private static readonly ProviderGame Zelda1986 = new("3", "The Legend of Zelda", 1986, true);
    private static readonly ProviderGame Mystery = new("4", "Mystery Island Deluxe", 2019, true);

    private static FakeSteam Library() => new(
        ("Avowed", @"""E:\games\Avowed\Avowed.exe""", "", 1001u),
        ("Launcher", @"""D:\Games\The Legend of Zelda - TotK\Launcher.exe""", "", 1002u),
        ("yuzu", @"""C:\Emu\yuzu\yuzu.exe""", "", 1003u),
        ("MIDX", @"""E:\games\midx\midx.exe""", "", 1004u));

    private static FakeProvider Provider() => new(Avowed, Totk, Zelda1986, Mystery);

    /// <summary>Runs Setup and Match, ending on the Review page.</summary>
    private static async Task<MainWindowViewModel> ToReview(FakeSteam steam, FakeProvider provider)
    {
        var main = new MainWindowViewModel(steam.Services(provider, new FakeKeyStore("saved-key")));
        await main.NextCommand.ExecuteAsync(null);
        await main.NextCommand.ExecuteAsync(null);
        Assert.IsType<ReviewViewModel>(main.CurrentStep);
        return main;
    }

    [Fact]
    public void Setup_FindsSteamAndDefaultAccount_ButNeedsAKey()
    {
        using var steam = Library();
        var keys = new FakeKeyStore();
        var main = new MainWindowViewModel(steam.Services(Provider(), keys));
        var setup = Assert.IsType<SetupViewModel>(main.CurrentStep);

        Assert.True(setup.SteamFound);
        Assert.Equal(FakeSteam.AccountId, setup.SelectedAccount!.AccountId);
        Assert.Equal("player_one", setup.SelectedAccount.PersonaName);
        Assert.False(setup.HasValidKey);
        Assert.False(main.NextCommand.CanExecute(null));
        Assert.False(main.BackCommand.CanExecute(null));
        Assert.Equal(new[] { true, false, false, false }, main.Steps.Select(s => s.IsCurrent));
    }

    [Fact]
    public async Task Setup_ValidKey_IsCheckedThenSaved()
    {
        using var steam = Library();
        var keys = new FakeKeyStore();
        var main = new MainWindowViewModel(steam.Services(Provider(), keys));
        var setup = (SetupViewModel)main.CurrentStep;

        setup.ApiKeyInput = "  good-key  ";
        await setup.SaveKeyCommand.ExecuteAsync(null);

        Assert.Equal("good-key", keys.Key);
        Assert.True(setup.HasValidKey);
        Assert.Equal("", setup.ApiKeyInput);
        Assert.True(main.NextCommand.CanExecute(null));
    }

    [Fact]
    public async Task Setup_RejectedKey_IsNotSaved()
    {
        using var steam = Library();
        var keys = new FakeKeyStore();
        var setup = new SetupViewModel(steam.Services(new FakeProvider { RejectKey = true }, keys), new WizardSession());

        setup.ApiKeyInput = "bad-key";
        await setup.SaveKeyCommand.ExecuteAsync(null);

        Assert.Null(keys.Key);
        Assert.False(setup.HasValidKey);
        Assert.Contains("rejected", setup.KeyStatus);
    }

    [Fact]
    public async Task Match_FillsGames_WithConfidence_DefaultTicks_AndPosters()
    {
        using var steam = Library();
        var main = await ToReview(steam, Provider());
        var games = ((ReviewViewModel)main.CurrentStep).Games;

        Assert.Equal(new[] { "Avowed", "Launcher", "yuzu", "MIDX" }, games.Select(g => g.CurrentName));

        var avowed = games[0];
        Assert.Equal(MatchConfidence.Matched, avowed.Confidence);
        Assert.True(avowed.Include);
        Assert.Equal("1-Poster-1", avowed.Poster.Selected!.Image.Id);
        // All five pieces are auto-picked, but only the chosen thumbnails are downloaded up front.
        Assert.All(avowed.Slots, s => Assert.Equal($"1-{s.Kind}-1", s.Selected!.Image.Id));
        Assert.All(avowed.Slots, s => Assert.NotNull(s.Selected!.ThumbnailPath));
        Assert.All(avowed.Slots, s => Assert.Null(s.Options[1].ThumbnailPath));
        Assert.NotNull(avowed.PosterPreviewPath);

        var zelda = games[1];
        Assert.Equal(Totk, zelda.Match);
        Assert.Equal("The Legend of Zelda: Tears of the Kingdom", zelda.NewName);

        var yuzu = games[2];
        Assert.Equal(MatchConfidence.NotFound, yuzu.Confidence);
        Assert.True(yuzu.IsEmulatorOrTool);
        Assert.Equal("Emulator", yuzu.ConfidenceText);
        Assert.False(yuzu.Include);

        Assert.Equal(MatchConfidence.NotFound, games[3].Confidence);
        Assert.False(games[3].Include);
    }

    [Fact]
    public async Task Review_ManualFix_PicksGame_TicksIt_AndLoadsArt()
    {
        using var steam = Library();
        var main = await ToReview(steam, Provider());
        var review = (ReviewViewModel)main.CurrentStep;
        var midx = review.Games[3];

        review.SelectedGame = midx;
        review.SearchText = "Mystery Island";
        await review.SearchCommand.ExecuteAsync(null);
        Assert.Equal(new[] { Mystery }, review.SearchResults);

        await review.PickCommand.ExecuteAsync(Mystery);

        Assert.Equal(Mystery, midx.Match);
        Assert.True(midx.IsManual);
        Assert.Equal("Picked by you", midx.ConfidenceText);
        Assert.True(midx.Include);
        Assert.Equal("Mystery Island Deluxe", midx.NewName);
        Assert.All(midx.Slots, s => Assert.Equal($"4-{s.Kind}-1", s.Selected!.Image.Id));
    }

    [Fact]
    public async Task Review_ChangingAMatch_KeepsOldOneAsAlternative_AndResetsArt()
    {
        using var steam = Library();
        var main = await ToReview(steam, Provider());
        var review = (ReviewViewModel)main.CurrentStep;
        var zelda = review.Games[1];

        review.SelectedGame = zelda;
        await review.PickCommand.ExecuteAsync(Zelda1986);

        Assert.Equal(Zelda1986, zelda.Match);
        Assert.Contains(Totk, zelda.Alternatives);
        Assert.DoesNotContain(Zelda1986, zelda.Alternatives);
        Assert.All(zelda.Slots, s => Assert.Equal($"3-{s.Kind}-1", s.Selected!.Image.Id));
    }

    [Fact]
    public async Task Review_SelectingArt_AndKeepingCurrent()
    {
        using var steam = Library();
        var main = await ToReview(steam, Provider());
        var review = (ReviewViewModel)main.CurrentStep;
        review.SelectedGame = review.Games[0];

        Assert.True(review.SelectedGame.Poster.IsActive);
        review.SelectSlotCommand.Execute(review.SelectedGame.Slot(ArtworkKind.Hero));
        Assert.True(review.SelectedSlot!.IsActive);
        Assert.False(review.SelectedGame.Poster.IsActive);
        await Task.Delay(50); // slot loads in the background when selected
        Assert.All(review.SelectedSlot.Options, o => Assert.NotNull(o.ThumbnailPath));   // opened: all thumbnails
        Assert.NotNull(review.SelectedSlot.Selected!.FullImagePath);                      // and the full image
        Assert.Equal(review.SelectedSlot.Selected.FullImagePath, review.SelectedSlot.LargePreviewPath);

        var second = review.SelectedSlot.Options[1];
        review.SelectOptionCommand.Execute(second);
        await Task.Delay(50);
        Assert.Same(second, review.SelectedSlot.Selected);
        Assert.True(second.IsSelected);
        Assert.NotNull(second.FullImagePath);
        Assert.Equal(second.FullImagePath, review.SelectedSlot.LargePreviewPath);

        review.KeepCurrentCommand.Execute(null);
        Assert.Null(review.SelectedSlot.Selected);
        Assert.False(second.IsSelected);
    }

    [Fact]
    public async Task Apply_SummarisesOnlyTickedGames()
    {
        using var steam = Library();
        var main = await ToReview(steam, Provider());
        var review = (ReviewViewModel)main.CurrentStep;
        review.Games[1].Include = false;                 // leave Zelda alone
        review.Games[0].NewName = "Avowed";              // unchanged name, art only

        await main.NextCommand.ExecuteAsync(null);
        var apply = Assert.IsType<ApplyViewModel>(main.CurrentStep);

        var change = Assert.Single(apply.Plan!.Changes);
        Assert.Equal(("0", 1001u, null as string), (change.ShortcutIndex, change.AppId, change.NewName));
        Assert.Equal(Enum.GetValues<ArtworkKind>(), change.Artwork.Keys.Order());
        Assert.Equal(new[] { "Avowed: poster, wide, hero, logo, icon" }, apply.Lines);
        Assert.False(main.NextCommand.CanExecute(null));
    }

    [Fact]
    public void ApplyPlan_IncludesRenameOnlyChanges()
    {
        using var steam = Library();
        var file = ShortcutsFile.Load(Path.Combine(steam.Root, "userdata", FakeSteam.AccountId, "config", "shortcuts.vdf"));
        var game = new GameItemViewModel(file.Shortcuts[3], new GridArtwork(steam.GridFolder)) { Include = true, NewName = "  Mystery Island  " };
        var account = new SteamLocator(() => steam.Root).GetAccounts(steam.Root)[0];

        var plan = ApplyPlan.From(account, [game]);

        var change = Assert.Single(plan.Changes);
        Assert.Equal("Mystery Island", change.NewName);
        Assert.Empty(change.Artwork);
    }

    [Fact]
    public async Task ExistingSteamArt_IsShownUntilReplaced()
    {
        using var steam = Library();
        var existing = Path.Combine(steam.GridFolder, "1004p.png");
        File.WriteAllBytes(existing, PngHandler.Png);
        var main = await ToReview(steam, Provider());

        var midx = ((ReviewViewModel)main.CurrentStep).Games[3];
        Assert.Equal(existing, midx.Poster.ExistingPath);
        Assert.Equal(existing, midx.PosterPreviewPath);
    }
}
