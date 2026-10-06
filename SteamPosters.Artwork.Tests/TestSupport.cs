using System.Net;
using System.Text;

namespace SteamPosters.Artwork.Tests;

/// <summary>An HttpMessageHandler that answers from a callback and records every request. No network.</summary>
internal sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = new();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Requests.Add(request);
        return Task.FromResult(respond(request));
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Bytes(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
}

/// <summary>A fresh folder under %TEMP% that is deleted when disposed.</summary>
internal sealed class TempFolder : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SteamPostersTests", Guid.NewGuid().ToString("N"));

    public TempFolder() => Directory.CreateDirectory(Path);

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch (IOException) { }
    }
}

/// <summary>A test that only runs when STEAMGRIDDB_API_KEY is set (it calls the real API).</summary>
public sealed class LiveSteamGridDbFactAttribute : FactAttribute
{
    public const string EnvironmentVariable = "STEAMGRIDDB_API_KEY";

    public LiveSteamGridDbFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnvironmentVariable)))
            Skip = $"Set {EnvironmentVariable} to run live SteamGridDB tests.";
    }
}

internal static class TestImages
{
    public static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];
    public static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 9, 9];
    public static readonly byte[] WebP = [.. "RIFF"u8, 4, 0, 0, 0, .. "WEBP"u8, .. "VP8 "u8];
    public static readonly byte[] Ico = [0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 16, 16];
}
