namespace SteamPosters.Artwork.Tests;

public class ImageCacheTests
{
    private static readonly Uri PosterUrl = new("https://cdn2.steamgriddb.com/grid/4.png");

    [Fact]
    public async Task Download_ThenServesFromDisk()
    {
        using var temp = new TempFolder();
        var handler = new FakeHandler(_ => FakeHandler.Bytes(TestImages.Png));
        var cache = new ImageCache(new HttpClient(handler), temp.Path);

        Assert.Null(cache.TryGet(PosterUrl));
        var first = await cache.GetOrDownloadAsync(PosterUrl);
        var second = await cache.GetOrDownloadAsync(PosterUrl);

        Assert.Single(handler.Requests);
        Assert.Equal(first, second);
        Assert.Equal(ImageFormat.Png, first.Format);
        Assert.False(first.NeedsConversion);
        Assert.Equal(TestImages.Png, File.ReadAllBytes(first.Path));
        Assert.Matches("^[0-9a-f]{64}\\.png$", Path.GetFileName(first.Path));
        Assert.Equal(first, cache.TryGet(PosterUrl));
        Assert.Null(handler.Requests[0].Headers.Authorization);
    }

    [Fact]
    public async Task ConcurrentRequestsForSameUrl_ShareOneDownload()
    {
        using var temp = new TempFolder();
        var gate = new TaskCompletionSource();
        var calls = 0;
        var handler = new BlockingHandler(gate.Task, () => Interlocked.Increment(ref calls));
        var cache = new ImageCache(new HttpClient(handler), temp.Path);

        var a = cache.GetOrDownloadAsync(PosterUrl);
        var b = cache.GetOrDownloadAsync(PosterUrl);
        gate.SetResult();

        Assert.Equal((await a).Path, (await b).Path);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData("jpeg", ImageFormat.Jpeg, ".jpg", false)]
    [InlineData("webp", ImageFormat.WebP, ".webp", true)]
    [InlineData("ico", ImageFormat.Ico, ".ico", false)]
    public async Task DetectsFormatFromBytes(string which, ImageFormat format, string extension, bool needsConversion)
    {
        using var temp = new TempFolder();
        var bytes = which switch { "jpeg" => TestImages.Jpeg, "webp" => TestImages.WebP, _ => TestImages.Ico };
        var cache = new ImageCache(new HttpClient(new FakeHandler(_ => FakeHandler.Bytes(bytes))), temp.Path);

        // The URL says .png; the bytes decide.
        var image = await cache.GetOrDownloadAsync(PosterUrl);

        Assert.Equal(format, image.Format);
        Assert.EndsWith(extension, image.Path);
        Assert.Equal(needsConversion, image.NeedsConversion);
    }

    [Fact]
    public async Task NonImageBytes_AreRejected_AndNotCached()
    {
        using var temp = new TempFolder();
        var cache = new ImageCache(new HttpClient(new FakeHandler(_ => FakeHandler.Json("<html>not an image</html>"))), temp.Path);

        await Assert.ThrowsAsync<InvalidDataException>(() => cache.GetOrDownloadAsync(PosterUrl));

        Assert.Empty(Directory.GetFiles(temp.Path));
        Assert.Null(cache.TryGet(PosterUrl));
    }

    [Fact]
    public async Task OversizedImages_AreRejected()
    {
        using var temp = new TempFolder();
        var big = TestImages.Png.Concat(new byte[200]).ToArray();
        var cache = new ImageCache(new HttpClient(new FakeHandler(_ => FakeHandler.Bytes(big))), temp.Path, maxImageBytes: 100);

        await Assert.ThrowsAsync<InvalidDataException>(() => cache.GetOrDownloadAsync(PosterUrl));

        Assert.Empty(Directory.GetFiles(temp.Path));
    }

    [Fact]
    public async Task OversizedStream_WithoutContentLength_IsRejected()
    {
        using var temp = new TempFolder();
        var big = TestImages.Png.Concat(new byte[200]).ToArray();
        var handler = new FakeHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StreamContent(new UnknownLengthStream(big)),
        });
        var cache = new ImageCache(new HttpClient(handler), temp.Path, maxImageBytes: 100);

        await Assert.ThrowsAsync<InvalidDataException>(() => cache.GetOrDownloadAsync(PosterUrl));
    }

    [Fact]
    public async Task HttpErrors_Propagate()
    {
        using var temp = new TempFolder();
        var handler = new FakeHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        var cache = new ImageCache(new HttpClient(handler), temp.Path);

        await Assert.ThrowsAsync<HttpRequestException>(() => cache.GetOrDownloadAsync(PosterUrl));
    }

    private sealed class BlockingHandler(Task gate, Action onCall) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            onCall();
            await gate;
            return FakeHandler.Bytes(TestImages.Png);
        }
    }

    /// <summary>A stream that hides its length, like a chunked HTTP response.</summary>
    private sealed class UnknownLengthStream(byte[] data) : MemoryStream(data)
    {
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
    }
}
