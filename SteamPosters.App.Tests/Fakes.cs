using System.Net;
using System.Text;
using SteamPosters.Artwork;
using SteamPosters.Artwork.SteamGridDb;
using SteamPosters.Core.IO;
using SteamPosters.Core.Steam;
using steamposters.Services;

namespace SteamPosters.App.Tests;

internal sealed class FakeKeyStore(string? key = null) : IApiKeyStore
{
    public string? Key { get; private set; } = key;

    public string? Load() => Key;

    public void Save(string apiKey) => Key = apiKey;

    public void Delete() => Key = null;
}

/// <summary>In-memory SteamGridDB: search returns games sharing a word with the term; every game has two images per kind.</summary>
internal sealed class FakeProvider(params ProviderGame[] games) : IArtworkProvider
{
    public bool RejectKey { get; init; }

    public List<string> Searches { get; } = new();

    public string Name => "Fake";

    public Task<IReadOnlyList<ProviderGame>> SearchGamesAsync(string term, CancellationToken cancellationToken = default)
    {
        if (RejectKey) throw new SteamGridDbAuthException(HttpStatusCode.Unauthorized);
        Searches.Add(term);
        var words = Words(term);
        IReadOnlyList<ProviderGame> hits = games.Where(g => Words(g.Name).Intersect(words).Any()).ToList();
        return Task.FromResult(hits);
    }

    public Task<IReadOnlyList<ArtworkImage>> GetArtworkAsync(string gameId, ArtworkKind kind, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ArtworkImage> images = Enumerable.Range(1, 2).Select(n => new ArtworkImage(
            $"{gameId}-{kind}-{n}", kind,
            new Uri($"https://cdn.example.invalid/{gameId}/{kind}/{n}.png"),
            new Uri($"https://cdn.example.invalid/thumb/{gameId}/{kind}/{n}.png"),
            600, 900, "image/png", "alternate", 10 - n, 0, 0, false, false, false, "artist")).ToList();
        return Task.FromResult(images);
    }

    private static IEnumerable<string> Words(string s) =>
        s.ToLowerInvariant().Split([' ', ':', '-'], StringSplitOptions.RemoveEmptyEntries);
}

/// <summary>Serves real, decodable images: PNG by default, or whatever <see cref="Respond"/> picks per URL.</summary>
internal sealed class PngHandler : HttpMessageHandler
{
    /// <summary>A minimal PNG header, enough for code that only sniffs the format.</summary>
    public static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

    public Func<Uri, byte[]> Respond { get; set; } = _ => TestImages.Png;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Respond(request.RequestUri!)) });
}

/// <summary>Real images made with SkiaSharp.</summary>
internal static class TestImages
{
    public static readonly byte[] Png = Encode(SkiaSharp.SKEncodedImageFormat.Png, SkiaSharp.SKColors.Red);
    public static readonly byte[] Jpeg = Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, SkiaSharp.SKColors.Green);
    public static readonly byte[] WebP = Encode(SkiaSharp.SKEncodedImageFormat.Webp, SkiaSharp.SKColors.Blue);

    private static byte[] Encode(SkiaSharp.SKEncodedImageFormat format, SkiaSharp.SKColor color)
    {
        using var bitmap = new SkiaSharp.SKBitmap(8, 12);
        bitmap.Erase(color);
        using var data = bitmap.Encode(format, 90);
        return data.ToArray();
    }
}

/// <summary>A pretend Steam client: tests switch it on and off and see what the app asked of it.</summary>
internal sealed class FakeSteamProcess : ISteamProcess
{
    private TaskCompletionSource? _userCloses;

    public bool Running { get; set; }

    /// <summary>Whether a shutdown request works (false: Steam ignores it).</summary>
    public bool ShutdownWorks { get; set; } = true;

    public int ShutdownRequests { get; private set; }

    public int Starts { get; private set; }

    /// <summary>Whether something is waiting for the user to close Steam.</summary>
    public bool IsWaitingForUser => _userCloses is not null;

    public bool IsRunning() => Running;

    public Task<bool> ShutdownAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ShutdownRequests++;
        if (ShutdownWorks) Running = false;
        return Task.FromResult(!Running);
    }

    public Task WaitForExitAsync(CancellationToken cancellationToken = default)
    {
        if (!Running) return Task.CompletedTask;
        _userCloses = new TaskCompletionSource();
        cancellationToken.Register(() => _userCloses?.TrySetCanceled());
        return _userCloses.Task;
    }

    /// <summary>The user quits Steam.</summary>
    public void UserClosesSteam()
    {
        Running = false;
        _userCloses?.TrySetResult();
        _userCloses = null;
    }

    public void Start()
    {
        Starts++;
        Running = true;
    }
}

/// <summary>A made-up Steam install in a temp folder with one account and a few non-Steam games.</summary>
internal sealed class FakeSteam : IDisposable
{
    public const string AccountId = "39734274";

    public FakeSteam(params (string Name, string Exe, string LaunchOptions, uint AppId)[] games)
    {
        Root = Path.Combine(Path.GetTempPath(), "SteamPostersAppTests", Guid.NewGuid().ToString("N"));
        var config = Path.Combine(Root, "userdata", AccountId, "config");
        Directory.CreateDirectory(Path.Combine(config, "grid"));
        Directory.CreateDirectory(Path.Combine(Root, "config"));
        File.WriteAllText(Path.Combine(Root, "config", "loginusers.vdf"),
            "\"users\" { \"76561198000000002\" { \"PersonaName\" \"player_one\" \"MostRecent\" \"1\" } }");
        File.WriteAllBytes(Path.Combine(config, "shortcuts.vdf"), Shortcuts(games));
        CacheFolder = Path.Combine(Root, "cache");
        BackupsFolder = Path.Combine(Root, "backups");
    }

    public string BackupsFolder { get; }

    public FakeSteamProcess Process { get; } = new();

    public PngHandler Downloads { get; } = new();

    public string ShortcutsPath => Path.Combine(Root, "userdata", AccountId, "config", "shortcuts.vdf");

    public string Root { get; }

    public string CacheFolder { get; }

    public string GridFolder => Path.Combine(Root, "userdata", AccountId, "config", "grid");

    public AppServices Services(IArtworkProvider provider, IApiKeyStore keys) => new(
        new SteamLocator(() => Root),
        keys,
        _ => provider,
        new ImageCache(new HttpClient(Downloads), CacheFolder),
        new BackupService(BackupsFolder),
        _ => Process);

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch (IOException) { }
    }

    private static byte[] Shortcuts((string Name, string Exe, string LaunchOptions, uint AppId)[] games)
    {
        using var ms = new MemoryStream();
        void Str(string s) { ms.Write(Encoding.UTF8.GetBytes(s)); ms.WriteByte(0); }
        ms.WriteByte(0); Str("shortcuts");
        for (var i = 0; i < games.Length; i++)
        {
            var g = games[i];
            ms.WriteByte(0); Str(i.ToString());
            if (g.AppId != 0) { ms.WriteByte(2); Str("appid"); ms.Write(BitConverter.GetBytes(g.AppId)); }
            ms.WriteByte(1); Str("AppName"); Str(g.Name);
            ms.WriteByte(1); Str("Exe"); Str(g.Exe);
            ms.WriteByte(1); Str("LaunchOptions"); Str(g.LaunchOptions);
            ms.WriteByte(1); Str("icon"); Str("");
            ms.WriteByte(2); Str("LastPlayTime"); ms.Write(BitConverter.GetBytes(1700000000u));
            ms.WriteByte(0); Str("tags"); ms.WriteByte(1); Str("0"); Str("Favorites"); ms.WriteByte(8);
            ms.WriteByte(8);
        }
        ms.WriteByte(8); ms.WriteByte(8);
        return ms.ToArray();
    }
}
