using System.Net;
using SteamPosters.Artwork.SteamGridDb;
using SteamPosters.Core.Steam;

namespace SteamPosters.Artwork.Tests;

public class SteamGridDbProviderTests
{
    private const string Key = "test-key-123";

    private static (SteamGridDbProvider Provider, FakeHandler Handler, List<TimeSpan> Delays) Create(
        Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new FakeHandler(respond);
        var delays = new List<TimeSpan>();
        var options = new SteamGridDbOptions
        {
            Delay = (d, _) => { delays.Add(d); return Task.CompletedTask; },
            Now = () => new DateTimeOffset(2026, 10, 6, 0, 0, 0, TimeSpan.Zero),
        };
        return (new SteamGridDbProvider(new HttpClient(handler), Key, options), handler, delays);
    }

    private static string Image(int id, int width, int height, string style = "alternate", int score = 0,
        int upvotes = 0, bool nsfw = false, bool humor = false, bool epilepsy = false) => $$"""
        {"id":{{id}},"score":{{score}},"style":"{{style}}","width":{{width}},"height":{{height}},
         "nsfw":{{(nsfw ? "true" : "false")}},"humor":{{(humor ? "true" : "false")}},"epilepsy":{{(epilepsy ? "true" : "false")}},
         "notes":null,"mime":"image/png","language":"en","url":"https://cdn2.steamgriddb.com/grid/{{id}}.png",
         "thumb":"https://cdn2.steamgriddb.com/thumb/{{id}}.jpg","lock":false,"upvotes":{{upvotes}},"downvotes":0,
         "author":{"name":"artist{{id}}","steam64":"1","avatar":"https://example.invalid/a.png"} }
        """;

    private static string Data(params string[] items) => $$"""{"success":true,"data":[{{string.Join(",", items)}}]}""";

    [Fact]
    public async Task Search_ParsesResults_SendsBearerKey_AndEncodesTerm()
    {
        var (provider, handler, _) = Create(_ => FakeHandler.Json("""
            {"success":true,"data":[
              {"id":5254316,"name":"Avowed","release_date":1739836800,"types":["steam"],"verified":true},
              {"id":42,"name":"Avowed Fan Thing","types":[],"verified":false}]}
            """));

        var games = await provider.SearchGamesAsync("Assassin's Creed: Black Flag");

        var request = Assert.Single(handler.Requests);
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal(Key, request.Headers.Authorization.Parameter);
        Assert.Equal("https://www.steamgriddb.com/api/v2/search/autocomplete/Assassin%27s%20Creed%3A%20Black%20Flag",
            request.RequestUri!.AbsoluteUri);
        Assert.Equal(new ProviderGame("5254316", "Avowed", 2025, true), games[0]);
        Assert.Equal(new ProviderGame("42", "Avowed Fan Thing", null, false), games[1]);
    }

    [Fact]
    public async Task Search_BlankTerm_MakesNoRequest()
    {
        var (provider, handler, _) = Create(_ => throw new InvalidOperationException());

        Assert.Empty(await provider.SearchGamesAsync("  "));
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData(ArtworkKind.Poster, "/api/v2/grids/game/77", "dimensions=600x900,342x482,660x930&")]
    [InlineData(ArtworkKind.Wide, "/api/v2/grids/game/77", "dimensions=920x430,460x215&")]
    [InlineData(ArtworkKind.Hero, "/api/v2/heroes/game/77", "")]
    [InlineData(ArtworkKind.Logo, "/api/v2/logos/game/77", "")]
    [InlineData(ArtworkKind.Icon, "/api/v2/icons/game/77", "")]
    public async Task GetArtwork_UsesRightEndpointAndFilters(ArtworkKind kind, string path, string dimensions)
    {
        var (provider, handler, _) = Create(_ => FakeHandler.Json(Data()));

        await provider.GetArtworkAsync("77", kind);

        var uri = Assert.Single(handler.Requests).RequestUri!;
        Assert.Equal(path, uri.AbsolutePath);
        Assert.Equal($"?{dimensions}types=static&nsfw=false&humor=false&epilepsy=false", uri.Query);
        Assert.Equal(Key, handler.Requests[0].Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task GetArtwork_ParsesFields_AndRanksResults()
    {
        var (provider, _, _) = Create(_ => FakeHandler.Json(Data(
            Image(1, 342, 482, score: 50),               // right shape, smaller
            Image(2, 600, 900, style: "blurred", score: 90),
            Image(3, 600, 900, style: "alternate", score: 10),
            Image(4, 600, 900, style: "alternate", score: 10, upvotes: 7),
            Image(5, 600, 900, score: 999, nsfw: true),
            Image(6, 600, 900, score: 999, humor: true),
            Image(7, 600, 900, score: 999, epilepsy: true),
            Image(8, 500, 500, score: 500))));             // wrong shape

        var images = await provider.GetArtworkAsync("77", ArtworkKind.Poster);

        Assert.Equal(new[] { "4", "3", "2", "1", "8" }, images.Select(i => i.Id));
        var top = images[0];
        Assert.Equal(ArtworkKind.Poster, top.Kind);
        Assert.Equal(new Uri("https://cdn2.steamgriddb.com/grid/4.png"), top.Url);
        Assert.Equal(new Uri("https://cdn2.steamgriddb.com/thumb/4.jpg"), top.ThumbnailUrl);
        Assert.Equal((600, 900, "image/png", "alternate", 7, "artist4"), (top.Width, top.Height, top.Mime, top.Style, top.Upvotes, top.Author));
    }

    [Fact]
    public async Task GetArtwork_LogosPreferOfficialStyle()
    {
        var (provider, _, _) = Create(_ => FakeHandler.Json(Data(
            Image(1, 1280, 400, style: "custom", score: 100),
            Image(2, 1280, 400, style: "official", score: 1))));

        var images = await provider.GetArtworkAsync("77", ArtworkKind.Logo);

        Assert.Equal("2", images[0].Id);
    }

    [Fact]
    public async Task Unauthorized_ThrowsClearAuthError()
    {
        var (provider, _, _) = Create(_ => FakeHandler.Json("""{"success":false,"errors":["Unauthorized"]}""", HttpStatusCode.Unauthorized));

        var ex = await Assert.ThrowsAsync<SteamGridDbAuthException>(() => provider.SearchGamesAsync("x"));
        Assert.Contains("API key is invalid or missing", ex.Message);
        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
    }

    [Fact]
    public async Task NotFound_ReturnsEmptyList()
    {
        var (provider, _, _) = Create(_ => FakeHandler.Json("""{"success":false,"errors":["Game not found"]}""", HttpStatusCode.NotFound));

        Assert.Empty(await provider.GetArtworkAsync("999999", ArtworkKind.Hero));
    }

    [Fact]
    public async Task TooManyRequests_HonoursRetryAfter_ThenSucceeds()
    {
        var calls = 0;
        var (provider, handler, delays) = Create(_ =>
        {
            if (++calls > 2) return FakeHandler.Json(Data(Image(1, 600, 900)));
            var limited = FakeHandler.Json("""{"success":false}""", HttpStatusCode.TooManyRequests);
            limited.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(calls == 1 ? 3 : 600));
            return limited;
        });

        var images = await provider.GetArtworkAsync("77", ArtworkKind.Poster);

        Assert.Single(images);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal(new[] { TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(30) }, delays); // second wait capped
    }

    [Fact]
    public async Task TooManyRequests_GivesUpAfterBoundedRetries()
    {
        var (provider, handler, _) = Create(_ => FakeHandler.Json("{}", HttpStatusCode.TooManyRequests));

        var ex = await Assert.ThrowsAsync<SteamGridDbException>(() => provider.SearchGamesAsync("x"));

        Assert.Equal(HttpStatusCode.TooManyRequests, ex.StatusCode);
        Assert.Equal(4, handler.Requests.Count); // 1 try + 3 retries
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, "<html>oops</html>", "HTTP 500")]
    [InlineData(HttpStatusCode.BadRequest, """{"success":false,"errors":["bad dimensions"]}""", "bad dimensions")]
    [InlineData(HttpStatusCode.OK, """{"success":false,"errors":["nope"]}""", "nope")]
    public async Task OtherFailures_ThrowTypedExceptionWithStatus(HttpStatusCode status, string body, string expected)
    {
        var (provider, _, _) = Create(_ => FakeHandler.Json(body, status));

        var ex = await Assert.ThrowsAsync<SteamGridDbException>(() => provider.SearchGamesAsync("x"));

        Assert.Equal(status, ex.StatusCode);
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public async Task NetworkError_ThrowsTypedException()
    {
        var (provider, _, _) = Create(_ => throw new HttpRequestException("no route"));

        var ex = await Assert.ThrowsAsync<SteamGridDbException>(() => provider.SearchGamesAsync("x"));
        Assert.IsType<HttpRequestException>(ex.InnerException);
    }

    [Fact]
    public void Constructor_RequiresKey()
    {
        Assert.Throws<ArgumentException>(() => new SteamGridDbProvider(new HttpClient(), " "));
    }

    [LiveSteamGridDbFact]
    public async Task Live_SearchAndFetchPosters()
    {
        var key = Environment.GetEnvironmentVariable(LiveSteamGridDbFactAttribute.EnvironmentVariable)!;
        using var http = new HttpClient();
        var provider = new SteamGridDbProvider(http, key);

        var games = await provider.SearchGamesAsync("Avowed");
        Assert.NotEmpty(games);
        var posters = await provider.GetArtworkAsync(games[0].Id, ArtworkKind.Poster);
        Assert.NotEmpty(posters);
    }
}
