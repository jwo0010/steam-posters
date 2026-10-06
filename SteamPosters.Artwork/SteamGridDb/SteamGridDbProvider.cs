using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using SteamPosters.Core.Steam;

namespace SteamPosters.Artwork.SteamGridDb;

/// <summary>A SteamGridDB request failed. <see cref="StatusCode"/> is null for network errors and timeouts.</summary>
public class SteamGridDbException(string message, HttpStatusCode? statusCode = null, Exception? inner = null)
    : Exception(message, inner)
{
    public HttpStatusCode? StatusCode { get; } = statusCode;
}

/// <summary>The API key is missing, wrong or revoked (HTTP 401/403).</summary>
public sealed class SteamGridDbAuthException(HttpStatusCode statusCode)
    : SteamGridDbException("The SteamGridDB API key is invalid or missing. Get one at steamgriddb.com/profile/preferences/api.", statusCode);

public sealed class SteamGridDbOptions
{
    public Uri BaseAddress { get; init; } = new("https://www.steamgriddb.com/api/v2/");

    /// <summary>At most this many API calls in flight at once.</summary>
    public int MaxConcurrency { get; init; } = 4;

    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Retries after HTTP 429.</summary>
    public int MaxRetries { get; init; } = 3;

    /// <summary>Upper bound for one Retry-After wait.</summary>
    public TimeSpan MaxRetryAfter { get; init; } = TimeSpan.FromSeconds(30);

    public Func<TimeSpan, CancellationToken, Task> Delay { get; init; } = Task.Delay;

    public Func<DateTimeOffset> Now { get; init; } = () => DateTimeOffset.UtcNow;
}

/// <summary>Client for the SteamGridDB v2 API. Each user supplies their own free API key.</summary>
public sealed class SteamGridDbProvider : IArtworkProvider
{
    private const string Filters = "types=static&nsfw=false&humor=false&epilepsy=false";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly SteamGridDbOptions _options;
    private readonly SemaphoreSlim _concurrency;

    public SteamGridDbProvider(HttpClient http, string apiKey, SteamGridDbOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) throw new ArgumentException("A SteamGridDB API key is required.", nameof(apiKey));
        _http = http;
        _apiKey = apiKey.Trim();
        _options = options ?? new SteamGridDbOptions();
        _concurrency = new SemaphoreSlim(_options.MaxConcurrency, _options.MaxConcurrency);
    }

    public string Name => "SteamGridDB";

    public async Task<IReadOnlyList<ProviderGame>> SearchGamesAsync(string term, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(term)) return [];
        var items = await GetDataAsync<List<SearchItem>>(
            "search/autocomplete/" + Uri.EscapeDataString(term.Trim()), cancellationToken);
        return (items ?? [])
            .Select(i => new ProviderGame(i.Id.ToString(), i.Name, ReleaseYear(i.ReleaseDate), i.Verified))
            .ToList();
    }

    public async Task<IReadOnlyList<ArtworkImage>> GetArtworkAsync(string gameId, ArtworkKind kind, CancellationToken cancellationToken = default)
    {
        var items = await GetDataAsync<List<ImageItem>>(ArtworkPath(gameId, kind), cancellationToken);
        var images = (items ?? [])
            .Where(i => Uri.IsWellFormedUriString(i.Url, UriKind.Absolute))
            .Select(i => new ArtworkImage(
                i.Id.ToString(), kind, new Uri(i.Url),
                Uri.IsWellFormedUriString(i.Thumb, UriKind.Absolute) ? new Uri(i.Thumb) : new Uri(i.Url),
                i.Width, i.Height, i.Mime ?? "", i.Style ?? "", i.Score, i.Upvotes, i.Downvotes,
                i.Nsfw, i.Humor, i.Epilepsy ?? false, i.Author?.Name));
        return ArtworkRanker.Rank(images, kind);
    }

    /// <summary>The API path (relative to the base address) for one art piece of a game.</summary>
    public static string ArtworkPath(string gameId, ArtworkKind kind)
    {
        var id = Uri.EscapeDataString(gameId);
        return kind switch
        {
            ArtworkKind.Poster => $"grids/game/{id}?dimensions=600x900,342x482,660x930&{Filters}",
            ArtworkKind.Wide => $"grids/game/{id}?dimensions=920x430,460x215&{Filters}",
            ArtworkKind.Hero => $"heroes/game/{id}?{Filters}",
            ArtworkKind.Logo => $"logos/game/{id}?{Filters}",
            ArtworkKind.Icon => $"icons/game/{id}?{Filters}",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    /// <summary>Sends a GET and unwraps {"success":true,"data":...}. HTTP 404 returns default ("nothing found").</summary>
    private async Task<T?> GetDataAsync<T>(string relativePath, CancellationToken cancellationToken)
    {
        var uri = new Uri(_options.BaseAddress, relativePath);
        for (var attempt = 0; ; attempt++)
        {
            using var response = await SendAsync(uri, cancellationToken);
            var status = response.StatusCode;

            if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new SteamGridDbAuthException(status);
            if (status == HttpStatusCode.NotFound)
                return default;
            if (status == HttpStatusCode.TooManyRequests)
            {
                if (attempt >= _options.MaxRetries)
                    throw new SteamGridDbException("SteamGridDB is rate limiting requests; try again in a minute.", status);
                await _options.Delay(RetryDelay(response, attempt), cancellationToken);
                continue;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            Envelope<T>? envelope = null;
            try
            {
                envelope = JsonSerializer.Deserialize<Envelope<T>>(body, JsonOptions);
            }
            catch (JsonException ex) when (response.IsSuccessStatusCode)
            {
                throw new SteamGridDbException("SteamGridDB returned an unreadable response.", status, ex);
            }
            catch (JsonException)
            {
                // Error pages are often HTML; the status code below says enough.
            }

            if (!response.IsSuccessStatusCode || envelope is null || !envelope.Success)
            {
                var detail = envelope?.Errors is { Count: > 0 } errors ? string.Join("; ", errors) : $"HTTP {(int)status}";
                throw new SteamGridDbException($"SteamGridDB request failed: {detail}", status);
            }
            return envelope.Data;
        }
    }

    private async Task<HttpResponseMessage> SendAsync(Uri uri, CancellationToken cancellationToken)
    {
        await _concurrency.WaitAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.RequestTimeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token);
            return response;
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SteamGridDbException("SteamGridDB did not respond in time.", null, ex);
        }
        catch (HttpRequestException ex)
        {
            throw new SteamGridDbException("Could not reach SteamGridDB.", ex.StatusCode, ex);
        }
        finally
        {
            _concurrency.Release();
        }
    }

    private TimeSpan RetryDelay(HttpResponseMessage response, int attempt)
    {
        var retryAfter = response.Headers.RetryAfter;
        var requested = retryAfter?.Delta ?? (retryAfter?.Date is { } date ? date - _options.Now() : (TimeSpan?)null);
        var delay = requested is { } r && r > TimeSpan.Zero ? r : TimeSpan.FromSeconds(Math.Pow(2, attempt));
        return delay < _options.MaxRetryAfter ? delay : _options.MaxRetryAfter;
    }

    private static int? ReleaseYear(long? unixSeconds) =>
        unixSeconds is > 0 ? DateTimeOffset.FromUnixTimeSeconds(unixSeconds.Value).Year : null;

    private sealed record Envelope<T>(bool Success, T? Data, List<string>? Errors);

    private sealed record SearchItem(int Id, string Name, long? ReleaseDate, bool Verified);

    private sealed record ImageItem(
        int Id, int Score, string? Style, int Width, int Height, bool Nsfw, bool Humor, bool? Epilepsy,
        string? Mime, string Url, string? Thumb, int Upvotes, int Downvotes, AuthorItem? Author);

    private sealed record AuthorItem(string? Name);
}
