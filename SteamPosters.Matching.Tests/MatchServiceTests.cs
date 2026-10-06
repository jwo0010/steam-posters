using SteamPosters.Artwork;
using SteamPosters.Core.Steam;

namespace SteamPosters.Matching.Tests;

public class MatchServiceTests
{
    /// <summary>In-memory provider: a search returns every game sharing a word with the term.</summary>
    private sealed class FakeProvider(params ProviderGame[] games) : IArtworkProvider
    {
        public List<string> Searches { get; } = new();

        public string Name => "Fake";

        public Task<IReadOnlyList<ProviderGame>> SearchGamesAsync(string term, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Searches.Add(term);
            var words = NameSimilarity.Tokens(term);
            IReadOnlyList<ProviderGame> hits = games.Where(g => NameSimilarity.Tokens(g.Name).Intersect(words).Any()).ToList();
            return Task.FromResult(hits);
        }

        public Task<IReadOnlyList<ArtworkImage>> GetArtworkAsync(string gameId, ArtworkKind kind, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private static readonly ProviderGame Avowed = new("1", "Avowed", 2025, true);
    private static readonly ProviderGame Starfield = new("2", "Starfield", 2023, true);
    private static readonly ProviderGame StarfieldFan = new("3", "Starfield Galaxy Fan Edition", null, false);
    private static readonly ProviderGame Totk = new("4", "The Legend of Zelda: Tears of the Kingdom", 2023, true);
    private static readonly ProviderGame Botw = new("5", "The Legend of Zelda: Breath of the Wild", 2017, true);
    private static readonly ProviderGame AcBlackFlag = new("6", "Assassin's Creed IV Black Flag", 2013, true);
    private static readonly ProviderGame AcUnity = new("7", "Assassin's Creed Unity", 2014, true);
    private static readonly ProviderGame MarioOdyssey = new("8", "Super Mario Odyssey", 2017, true);

    private static FakeProvider Library() => new(Avowed, Starfield, StarfieldFan, Totk, Botw, AcBlackFlag, AcUnity, MarioOdyssey);

    [Fact]
    public async Task ExactName_IsMatched_WithOneSearch()
    {
        var provider = Library();
        var result = await new MatchService(provider).MatchAsync("Avowed", @"""E:\games\Avowed\Avowed\Avowed.exe""");

        Assert.Equal(MatchConfidence.Matched, result.Confidence);
        Assert.Equal(Avowed, result.Game);
        Assert.Equal("Avowed", result.SuggestedName);
        Assert.Equal(1.0, result.Score);
        Assert.Single(provider.Searches);
    }

    [Fact]
    public async Task LauncherInGameFolder_MatchesViaFolder()
    {
        var provider = Library();
        var result = await new MatchService(provider).MatchAsync("Launcher", @"""D:\Games\The Legend of Zelda - TotK\Launcher.exe""");

        Assert.Equal(new[] { "The Legend of Zelda TotK" }, provider.Searches);
        Assert.Equal(MatchConfidence.Matched, result.Confidence);
        Assert.Equal(Totk, result.Game);
        Assert.Equal(TermSource.Folder, result.MatchedTerm!.Source);
        Assert.Contains(result.Alternatives, a => a.Game == Botw);
    }

    /// <summary>Real SteamGridDB behaviour: unknown words are ignored, so only the spelled-out name finds TotK.</summary>
    private sealed class AbbreviationBlindProvider : IArtworkProvider
    {
        private static readonly ProviderGame Zelda1986 = new("38050", "The Legend of Zelda", 1986, true);

        public List<string> Searches { get; } = new();

        public string Name => "Fake";

        public Task<IReadOnlyList<ProviderGame>> SearchGamesAsync(string term, CancellationToken cancellationToken = default)
        {
            Searches.Add(term);
            IReadOnlyList<ProviderGame> hits = term.Contains("Tears") ? [Totk, Zelda1986] : [Zelda1986, Botw];
            return Task.FromResult(hits);
        }

        public Task<IReadOnlyList<ArtworkImage>> GetArtworkAsync(string gameId, ArtworkKind kind, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    [Fact]
    public async Task KnownAbbreviation_IsFoundViaSpelledOutSearch_NotMatchedToPartialName()
    {
        var provider = new AbbreviationBlindProvider();
        var result = await new MatchService(provider).MatchAsync("Launcher", @"""D:\Games\The Legend of Zelda - TotK\Launcher.exe""");

        Assert.Equal(new[] { "The Legend of Zelda TotK", "The Legend of Zelda Tears of the Kingdom" }, provider.Searches);
        Assert.Equal(MatchConfidence.Matched, result.Confidence);
        Assert.Equal(Totk, result.Game);
        Assert.Contains(result.MatchedTerm!.Source, new[] { TermSource.Folder, TermSource.Expanded }); // both score 1.0
        Assert.True(result.Alternatives.Single(a => a.Game.Id == "38050").Score < 0.85);
    }

    [Fact]
    public void PartialName_IsNotAStrongMatch()
    {
        Assert.True(NameSimilarity.Score("The Legend of Zelda TotK", "The Legend of Zelda") < 0.85);
    }

    [Fact]
    public async Task StoreFolders_StillMatchStarfield()
    {
        var result = await new MatchService(Library()).MatchAsync("Starfield", @"""E:\games\Starfield\Starfield-SomeStore\Starfield\Starfield.exe""");

        Assert.Equal(MatchConfidence.Matched, result.Confidence);
        Assert.Equal(Starfield, result.Game);
    }

    [Fact]
    public async Task AbbreviatedExe_MatchesBlackFlag()
    {
        var result = await new MatchService(Library()).MatchAsync("ACBlackFlag",
            @"E:\games\Assassins-Creed-Black-Flag-Resynced\Assassins-Creed-Black-Flag-Resynced\Assassins Creed Black Flag Resynced\ACBlackFlag.exe");

        Assert.NotEqual(MatchConfidence.NotFound, result.Confidence);
        Assert.Equal(AcBlackFlag, result.Game);
        Assert.Equal("Assassin's Creed IV Black Flag", result.SuggestedName);
    }

    [Theory]
    [InlineData("Ryujinx.exe - Shortcut", @"""C:\Users\me\Desktop\ryujinx-canary-1.3.287-win_x64\publish\Ryujinx.exe""")]
    [InlineData("yuzu", @"""C:\Users\me\AppData\Local\yuzu\yuzu-windows-msvc\yuzu.exe""")]
    public async Task EmulatorsWithoutGame_AreNotFound_WithoutSearching(string appName, string exe)
    {
        var provider = Library();
        var result = await new MatchService(provider).MatchAsync(appName, exe, "");

        Assert.Equal(MatchConfidence.NotFound, result.Confidence);
        Assert.True(result.IsEmulatorOrTool);
        Assert.Equal(MatchService.EmulatorReason, result.Reason);
        Assert.Null(result.SuggestedName);
        Assert.Empty(provider.Searches);
    }

    [Fact]
    public async Task EmulatorWithGameFile_SearchesForThatGame()
    {
        var provider = Library();
        var result = await new MatchService(provider).MatchAsync("yuzu", @"""C:\Emu\yuzu\yuzu.exe""",
            @"-f -g ""D:\Roms\Super Mario Odyssey [0100000000010000][v0].nsp""");

        Assert.Equal(new[] { "Super Mario Odyssey" }, provider.Searches);
        Assert.Equal(MatchConfidence.Matched, result.Confidence);
        Assert.Equal(MarioOdyssey, result.Game);
        Assert.True(result.IsEmulatorOrTool);
    }

    [Fact]
    public async Task SeveralCloseResults_AreCheckThis()
    {
        var provider = new FakeProvider(new("10", "Space Game Deluxe", null, false), new("11", "Space Game Remastered", null, false));
        var result = await new MatchService(provider).MatchAsync("Space Deluxe", @"C:\x\SpaceDeluxe.exe");

        Assert.Equal(MatchConfidence.CheckThis, result.Confidence);
        Assert.NotNull(result.Game);
    }

    [Fact]
    public async Task NoResults_IsNotFound()
    {
        var result = await new MatchService(Library()).MatchAsync("Obscure Indie", @"C:\x\obscure.exe");

        Assert.Equal(MatchConfidence.NotFound, result.Confidence);
        Assert.Null(result.Game);
        Assert.Equal("no search results", result.Reason);
    }

    [Fact]
    public async Task SearchesAreCappedPerGame()
    {
        var provider = Library();
        var options = new MatchOptions { MaxSearches = 2 };
        await new MatchService(provider, options).MatchAsync("Alpha", @"C:\Beta Gamma\Delta Eps\Zeta Eta\Theta.exe");

        Assert.Equal(2, provider.Searches.Count);
    }

    [Fact]
    public async Task MatchAll_YieldsPerShortcut_AndCarriesIds()
    {
        var file = ShortcutsFile.Parse(BuildShortcuts(("0", "Avowed", @"""E:\Avowed\Avowed.exe""", 111u), ("1", "Launcher", @"""D:\Games\Starfield\Launcher.exe""", 222u)));
        var results = new List<MatchResult>();

        await foreach (var r in new MatchService(Library()).MatchAllAsync(file.Shortcuts)) results.Add(r);

        Assert.Equal(new[] { ("0", 111u, "Avowed"), ("1", 222u, "Starfield") },
            results.Select(r => (r.ShortcutIndex, r.AppId, r.SuggestedName!)));
    }

    [Fact]
    public async Task Cancellation_StopsMatching()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new MatchService(Library()).MatchAsync("Avowed", "Avowed.exe", cancellationToken: cts.Token));
    }

    private static byte[] BuildShortcuts(params (string Index, string Name, string Exe, uint AppId)[] entries)
    {
        using var ms = new MemoryStream();
        void Str(string s) { ms.Write(System.Text.Encoding.UTF8.GetBytes(s)); ms.WriteByte(0); }
        ms.WriteByte(0); Str("shortcuts");
        foreach (var e in entries)
        {
            ms.WriteByte(0); Str(e.Index);
            ms.WriteByte(2); Str("appid"); ms.Write(BitConverter.GetBytes(e.AppId));
            ms.WriteByte(1); Str("AppName"); Str(e.Name);
            ms.WriteByte(1); Str("Exe"); Str(e.Exe);
            ms.WriteByte(8);
        }
        ms.WriteByte(8); ms.WriteByte(8);
        return ms.ToArray();
    }
}
